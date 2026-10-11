# Kế hoạch: chuyển SQLite → Oracle Autonomous DB, thêm VM2, cân bằng tải bằng OCI Load Balancer

> Trạng thái: **đề xuất**, 2026-10-12
> Đã chốt: đường vào hiện tại là DuckDNS + Caddy tự cấp SSL; database chọn Autonomous Database (23ai); cân bằng tải bằng OCI Flexible Load Balancer (Always Free).

## Context

Prod hiện tại:

- **Một VM** E2.1.Micro (1/8 OCPU, 1 GB) chạy Ubuntu 24.04 ở Singapore.
- **Đường vào:** `dove-so.duckdns.org` → Caddy (tự lấy chứng chỉ Let's Encrypt, port 80/443) → API `127.0.0.1:5000`. Caddy phục vụ luôn frontend từ `/var/www/lottery`.
- **Dữ liệu:** toàn bộ nằm trong SQLite `/var/lib/lottery/lottery.db`. Khoá đăng nhập (DataProtection) nằm ở `/var/lib/lottery/keys`.

Bạn muốn giảm tải cho một máy: thêm VM thứ hai và cân bằng tải bằng **OCI Load Balancer free**, chuyển dữ liệu sang **Autonomous Database (Always Free, 23ai)**.

Kết quả mong muốn:

- 2 VM chạy cùng một bản app, dùng chung một database.
- Load Balancer chia người dùng cho 2 VM, và tự loại VM nào chết khỏi vòng chia.
- Toàn bộ dữ liệu cũ được chuyển sang database mới, người dùng không bị đăng xuất.

### Vì sao không chỉ "dựng thêm VM là xong"

Kết quả khảo sát code cho thấy những chỗ này sẽ hỏng nếu chạy 2 bản cùng lúc:

1. **Mất đăng nhập:** khoá DataProtection lưu trên đĩa từng máy. Cookie `dvs.auth` do VM1 cấp sẽ không đọc được ở VM2 → người dùng bị đăng xuất khi sang máy kia.
2. **Worker chạy trùng:** `DailyResultFetchWorker` và `BlogImageCleanupWorker` sẽ chạy ở cả 2 máy. `ResultScraper.PersistAsync` xoá rồi insert, và `LotteryResult` không có unique index → có thể sinh **kết quả xổ số trùng**.
3. **SignalR không có backplane:** `Clients.User` chỉ tới được trình duyệt đang nối vào cùng máy với nơi tạo thông báo.
4. **`Migrate()` lúc khởi động:** cả 2 máy cùng chạy migration một lúc.
5. **Giới hạn tính theo từng máy:** hạn mức Gemini và throttle Nominatim 1 req/s nằm trong RAM. Chạy 2 máy thì thực tế gấp đôi.

Những phần đã ổn, không cần sửa:

- Logic tiền (`Wallet`, `ScratchTicketService`) dùng optimistic concurrency với token `Balance` / `Status` và retry, không dựa vào khoá của SQLite → chạy đúng trên Oracle và nhiều máy.
- Ảnh đã lưu trên bucket S3, không ghi ra đĩa máy.

## Kiến trúc đích

```
Người dùng ─HTTPS─> dove-so.duckdns.org (A → IP public của LB)
             └─> OCI Flexible LB (Always Free, 10 Mbps)
                   - listener 443: chứng chỉ Let's Encrypt (DNS-01 qua DuckDNS)
                   - listener 80 : redirect sang https
                   - backend set: round robin + LB cookie (sticky), health check GET /health :80
                 ├─> VM1 (Micro) Caddy :80 → API :5000   [Workers ON, Migrate ON, gia hạn chứng chỉ]
                 └─> VM2 (Micro) Caddy :80 → API :5000   [Workers OFF]
                          └──────────┬──────────┘
                     Autonomous DB "lotterydb" (ATP 23ai, Always Free, TLS, ACL theo IP 2 VM)
```

Ghi chú:

- Hạn mức Always Free cho phép 2 VM Micro, 1 Flexible LB 10 Mbps và 2 Autonomous DB. Ta dùng ADB thứ hai làm DB dev.
- Sticky cookie đặt **theo người dùng**: mỗi trình duyệt luôn vào cùng một VM. Như vậy SignalR negotiate không bị lỗi, và tải được chia theo số người dùng.
- Giới hạn 10 Mbps: ảnh chụp vé đã được frontend nén trước khi upload, nên đủ cho lưu lượng hiện tại. Nếu sau này nghẽn, lưu ý điểm này.

---

## Phần 1 — Sửa code (1 PR, branch mới từ `main`)

### 1.1 Đổi provider sang Oracle

- **Bước 0, chặn mọi bước sau:** xác nhận NuGet `Oracle.EntityFrameworkCore` đã có bản hỗ trợ **EF Core 10**. Nếu chưa có → dừng, báo lại để đổi phương án (MySQL HeatWave).
- [LotteryChecker.Api.csproj](../../backend/LotteryChecker.Api/LotteryChecker.Api.csproj): thêm `Oracle.EntityFrameworkCore`. Giữ `Microsoft.EntityFrameworkCore.Sqlite`, vì công cụ chép dữ liệu (1.6) và test vẫn cần.
- [Program.cs:14-16](../../backend/LotteryChecker.Api/Program.cs#L14-L16): `UseSqlite` → `UseOracle(conn, o => o.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion23))`.
- Connection string (TLS, không cần wallet, vì ACL đã bật):
  ```
  User Id=LOTTERY;Password=...;Max Pool Size=10;Data Source=(description=(retry_count=20)(retry_delay=3)(address=(protocol=tcps)(port=1521)(host=adb.ap-singapore-1.oraclecloud.com))(connect_data=(service_name=<xxx>_lotterydb_tp.adb.oraclecloud.com))(security=(ssl_server_dn_match=yes)))
  ```
- `Max Pool Size=10` mỗi VM: bản Always Free giới hạn số session đồng thời (xem con số chính xác trong console). 2 VM cộng lại phải nằm dưới mức đó.
- **Dev local:** trỏ vào ADB thứ hai `lotterydev`, dùng mTLS wallet để không phải sửa ACL mỗi khi IP nhà đổi. Ghi hướng dẫn vào `deploy/ORACLE-DB.md`.

### 1.2 Model cho hợp Oracle

Sửa ở [AppDbContext.cs](../../backend/LotteryChecker.Api/Data/AppDbContext.cs):

- **Oracle coi chuỗi rỗng `''` là NULL.** Nhiều cột như `string X = ""` sẽ lỗi ORA-01400 khi lưu `""`. Cách xử lý:
  - (a) Trong `OnModelCreating`, khi provider là Oracle: mọi cột string **ở mức DB** cho phép NULL. Model C# giữ nguyên.
  - (b) Thêm `IMaterializationInterceptor`: khi đọc lên, gán `""` cho thuộc tính string không-nullable mà DB trả về NULL. Nhờ vậy code hiện tại vẫn thấy `""` như cũ.
  - (c) Grep các truy vấn LINQ có so sánh với `""` hoặc `string.IsNullOrEmpty`, sửa những chỗ chạy trên DB.
- **String không có MaxLength sẽ thành NCLOB.** NCLOB không index, `GROUP BY` hay `DISTINCT` được.
  - `ConfigureConventions`: mặc định `HaveMaxLength(2000)`.
  - Cột nội dung dài thì khai báo rõ `HasColumnType("NCLOB")`: `BlogPost.Content`, `BlogComment.Content`, `SitePost.ContentHtml`, `Site.DraftJson`, `Site.PublishedJson`, cùng các cột HTML/JSON khác tìm thấy khi rà model.
- **Kiểu dữ liệu:** `DateOnly` → DATE, `Guid` → RAW(16), `bool` → BOOLEAN (23ai), `long` → NUMBER(19), `double` → BINARY_DOUBLE. Để provider tự map. Kiểm tra bằng script migration sinh ra.
- **`LIKE` của Oracle phân biệt hoa thường:** [ShopService.cs:183](../../backend/LotteryChecker.Api/Services/ShopService.cs#L183) đổi sang so sánh `UPPER(...)` cả hai vế.
- **Unique index cho `LotteryResult`** theo khoá tự nhiên của một dòng kết quả. Lớp bảo vệ thứ hai, phòng khi worker vẫn chạy trùng.

### 1.3 Migration mới cho Oracle

- Chuyển 17 migration SQLite hiện có vào `Migrations/_sqlite_archive/` và loại khỏi build. Không xoá hẳn, để còn tra lịch sử.
- `dotnet ef migrations add InitialOracle` trỏ vào ADB dev.
- Chuyển phần dữ liệu đang seed bằng SQL riêng của SQLite sang migration mới:
  - Seed superadmin (đang dùng `strftime` trong `AddAdmin`) → `InsertData`.
  - Backfill GUID của `AddBlogPublicId` không cần nữa, vì dữ liệu thật sẽ được chép sang ở 1.6.
- [AdminTests.cs:21-24](../../backend/LotteryChecker.Tests/AdminTests.cs#L21-L24): đổi `Migrate()` → `EnsureCreated()` trên SQLite in-memory. Các test khác dùng InMemory nên không ảnh hưởng.

### 1.4 Chạy được 2 bản cùng lúc

| Việc | Cách làm | Ở đâu |
|---|---|---|
| Khoá DataProtection dùng chung | `PersistKeysToDbContext<AppDbContext>()` (package `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`), `AppDbContext : IDataProtectionKeyContext`. Bỏ `DataProtection:KeysPath`. | [Program.cs:59-65](../../backend/LotteryChecker.Api/Program.cs#L59-L65) |
| Worker chỉ chạy ở VM1 | Cờ cấu hình `Workers:Enabled` (mặc định `true`). Chỉ `AddHostedService<DailyResultFetchWorker/BlogImageCleanupWorker>` khi bật. VM2 đặt `Workers__Enabled=false`. | Program.cs:90, :275 |
| Migrate chỉ ở VM1 | Cờ `Database:MigrateOnStartup` (mặc định `true`). VM2 đặt `false`. | Program.cs:279-289 |
| Thông báo realtime giữa 2 máy | Bỏ đẩy trực tiếp trong `NotificationService`. Thêm `NotificationRelayWorker` chạy ở **mọi** VM: cứ 3s đọc `Notifications` có `Id > lastId`, dựng DTO bằng hàm đọc sẵn có của `NotificationService`, rồi `Clients.User(...)`. Mỗi kết nối chỉ nằm ở một máy nên không bị gửi trùng. Lúc khởi động, `lastId` = Id lớn nhất hiện có. | `Services/NotificationService.cs`, `Workers/` |
| Hạn mức Gemini | Chỉ chỉnh cấu hình: mỗi VM đặt `Gemini__ScanPermitPerMinute` và các giới hạn liên quan bằng **một nửa** giá trị hiện tại. | env |
| Throttle Nominatim | `MinInterval` đọc từ `Geo:MinIntervalSeconds` (mặc định 1). Mỗi VM đặt `2`. | `Services/GeocodingService.cs:17` |
| Race khi tạo `GuestUsage` | Bắt `DbUpdateException` khi insert trùng khoá → đọc lại rồi tăng đếm. | `Middleware/GuestQuotaAttribute.cs:69-76` |

Chấp nhận để nguyên: rate limiter tính theo từng máy (người dùng dính một máy nhờ sticky), cache cờ tính năng lệch tối đa 30s, cache ảnh blog đã xoá ở máy kia.

### 1.5 Health check

`/health` giữ là kiểm tra sống, không gọi DB, để DB chập chờn không làm Load Balancer gỡ cả 2 VM. Thêm `/health/db` (chạy `SELECT 1`) để chạy tay khi cần chẩn đoán.

### 1.6 Công cụ chép dữ liệu (dùng cả để backup)

Thêm lệnh chạy trong chính API:

```
dotnet LotteryChecker.Api.dll copy-data --from sqlite:"Data Source=/path/lottery.db" --to oracle:"<conn>"
```

- Bắt ở đầu `Program.cs`, chạy xong thì thoát, không khởi động web.
- Nguồn là SQLite thì dùng một context con `SqliteSourceContext : AppDbContext`, để EF không dùng chung model đã cache với context Oracle.
- Chép từng bảng theo thứ tự khoá ngoại: Users → bảng của Users → Blog → Shops → Sites → Notifications.
  - Giữ nguyên `Id`.
  - Lô 500 dòng, `AsNoTracking`.
  - Tắt `AutoDetectChanges`.
- Sau khi chép: reset identity từng bảng bằng `ALTER TABLE "X" MODIFY "Id" GENERATED BY DEFAULT ON NULL AS IDENTITY (START WITH LIMIT VALUE)`.
- Chép thêm khoá DataProtection từ thư mục `/var/lib/lottery/keys/*.xml` vào bảng `DataProtectionKeys`. Nhờ vậy cookie hiện tại vẫn dùng được và **không ai bị đăng xuất**.
- In bảng đối chiếu: số dòng nguồn và đích của từng bảng. Lệch bảng nào thì exit code khác 0.
- Chạy chiều ngược `--from oracle --to sqlite` để làm **backup hằng ngày**: cron trên VM1 lúc 03:00 xuất ra file `.db`, nén rồi đẩy lên bucket Object Storage mới `lottery-db-backup`, giữ 14 bản.

---

## Phần 2 — Hướng dẫn trên OCI Console (ghi vào `deploy/ORACLE-DB.md` và `deploy/LOAD-BALANCER.md`)

### 2.1 Tạo Autonomous Database

1. Vào Oracle Database → Autonomous Database → **Create**.
   - Workload: **Transaction Processing**.
   - Bật **Always Free**, version **23ai**.
   - Tên: `lotterydb`. Đặt mật khẩu ADMIN.
2. Network access: **Secure access from allowed IPs and VCNs only**.
   - ACL: IP public của VM1 và VM2, cùng IP máy bạn (chỉ tạm trong lúc migrate).
   - Bỏ chọn **Require mutual TLS**.
3. Tạo thêm `lotterydev` (Always Free) cho dev, giữ mTLS và tải wallet về.
4. Tạo user cho app bằng SQL Worksheet (Database Actions):
   ```sql
   CREATE USER LOTTERY IDENTIFIED BY "<mật khẩu mạnh>";
   GRANT CREATE SESSION, CREATE TABLE, CREATE SEQUENCE, CREATE VIEW TO LOTTERY;
   ALTER USER LOTTERY QUOTA UNLIMITED ON DATA;
   ```
5. Lấy connection string TLS: Database connection → TLS → `lotterydb_tp`.
6. Lưu ý:
   - ADB Always Free tự **dừng sau 7 ngày không có kết nối**. Prod thì không sao; DB dev có thể bị dừng, bấm Start là chạy lại.
   - Kiểm tra mục Backup trên console. Dù có hay không, vẫn giữ cron backup ở 1.6.

### 2.2 Tạo VM2

1. Compute → Create instance `lottery-2`.
   - Image Ubuntu 24.04, shape **VM.Standard.E2.1.Micro** (cùng loại VM1; kiểm tra bằng `uname -m` trên VM1).
   - Đặt cùng VCN `vcn-lottery` và cùng public subnet. Dùng cùng SSH key.
2. Swap 2 GB và `DOTNET_gcServer=0`, làm giống [ORACLE-VM.md:219-229](../../deploy/ORACLE-VM.md#L219-L229).
3. Chạy `sudo bash setup-server.sh dove-so.duckdns.org --behind-lb` (chế độ mới, xem 2.4).
4. `/etc/lottery-api.env`:
   - Chép từ VM1: các key OCR, Gemini, Storage.
   - Thêm `ConnectionStrings__Default=<ADB>`, `Workers__Enabled=false`, `Database__MigrateOnStartup=false`, `Gemini__...` (một nửa), `Geo__MinIntervalSeconds=2`.
5. Deploy bằng `publish.ps1 -Server ubuntu@<ip VM2>`.

### 2.3 Tạo Load Balancer

1. Networking → Load Balancers → **Create Load Balancer** (loại Load Balancer, không phải Network LB).
   - Shape **Flexible**, min = max = **10 Mbps** (mức Always Free).
   - Public, đặt ở public subnet của `vcn-lottery`.
2. Backend set `lottery-bs`:
   - Policy Round Robin.
   - Session persistence: **Load balancer cookie** (`X-Oracle-BMC-LBS-Route`).
   - Health check: HTTP, port 80, path `/health`, interval 10s, timeout 3s, retries 3.
3. Backends: VM1:80 và VM2:80, theo private IP.
4. Chứng chỉ (DuckDNS không cho Load Balancer tự xin chứng chỉ):
   - Trên VM1 cài `acme.sh`, xin chứng chỉ bằng DNS-01: `acme.sh --issue --dns dns_duckdns -d dove-so.duckdns.org` với `DuckDNS_Token`.
   - Script `deploy/lb-cert-renew.sh` làm hook khi gia hạn:
     - `oci lb certificate create` với tên `le-YYYYMMDD`.
     - `oci lb listener update` để gắn chứng chỉ mới vào listener `https`.
     - Xoá chứng chỉ cũ.
   - Xác thực OCI CLI bằng **instance principal**: tạo Dynamic Group chứa VM1 và policy `Allow dynamic-group lottery-vm1 to manage load-balancers in compartment <c>`.
   - Lần đầu thì upload tay trên console.
5. Listeners:
   - `https`: 443, HTTPS, chứng chỉ ở bước 4, backend `lottery-bs`, idle timeout **300s** (đủ cho OCR chậm và SignalR).
   - `http`: 80 → rule set redirect 301 sang `https://{host}{path}?{query}`.
6. Security List:
   - LB nhận 80/443 từ `0.0.0.0/0`.
   - **VM chỉ nhận port 80 từ CIDR của VCN**. Bỏ 443 trên VM. Port 22 chỉ mở cho IP của bạn.

### 2.4 Đổi script deploy

- **[setup-server.sh](../../deploy/setup-server.sh)**: thêm chế độ `--behind-lb`. Caddyfile render với `SITE=":80"`, tắt auto HTTPS, thêm block global:
  ```
  { servers { trusted_proxies static 10.0.0.0/16 } }   # CIDR của VCN
  ```
  và trong `reverse_proxy`: `header_up X-Forwarded-For {client_ip}`.
  - Như vậy API vẫn chỉ tin loopback ([Program.cs:264-267](../../backend/LotteryChecker.Api/Program.cs#L264-L267) giữ nguyên) mà vẫn nhận đúng IP người dùng cho rate limit và guest quota.
  - iptables chỉ mở port 80.
- **[lottery-api.service](../../deploy/lottery-api.service)**: bỏ `ConnectionStrings__Default=Data Source=...` và `DataProtection__KeysPath`, chuyển connection string vào `/etc/lottery-api.env` (chmod 600).
- **[publish.ps1](../../deploy/publish.ps1)**: tham số `-Servers` nhận nhiều máy.
  - Deploy tuần tự: **VM1 trước** (VM1 chạy migration), chờ `/health` OK rồi mới tới VM2. Load Balancer tự né máy đang khởi động lại nhờ health check.
  - Frontend tar lên cả 2 máy.
  - Quy ước: migration phải tương thích ngược (chỉ thêm cột/bảng), vì có lúc 2 máy chạy 2 phiên bản khác nhau.

---

## Phần 3 — Thứ tự chuyển đổi

| # | Việc | Gián đoạn |
|---|---|---|
| 1 | Merge PR Phần 1 (provider chọn qua cấu hình, chưa deploy prod). Test đầu-cuối ở local với `lotterydev`. | 0 |
| 2 | Tạo `lotterydb` + user, ACL (2.1). Từ VM1 chạy `copy-data` **thử** để đo thời gian và đối chiếu số dòng. Xoá schema rồi chạy lại cho sạch. | 0 |
| 3 | **Bảo trì ~15 phút trên VM1:** `systemctl stop lottery-api` → `cp lottery.db lottery.db.bak` → deploy bản mới → `copy-data` thật → sửa env sang ADB → start → kiểm tra đăng nhập, dò vé, ví, blog, thông báo. | ~15' |
| 4 | Chạy 2–3 ngày **một VM + ADB**. Theo dõi `journalctl`, độ trễ, số session ADB, worker cào lúc 16:45. | 0 |
| 5 | Tạo VM2 (2.2), deploy và kiểm tra bằng `curl http://<private-ip-VM2>/health` từ VM1. | 0 |
| 6 | Tạo Load Balancer + chứng chỉ (2.3). Thử bằng cách sửa file `hosts` trên máy bạn: `dove-so.duckdns.org → IP LB`. | 0 |
| 7 | Đổi VM1 sang `--behind-lb` và **trỏ DuckDNS sang IP LB**. Đóng 80/443 public trên VM. | Vài phút (TTL DNS) |

**Rollback:**

- Bước 3–4: trả connection string về SQLite từ `lottery.db.bak`. Dữ liệu ghi sau thời điểm chuyển sẽ mất, nhưng có thể chạy `copy-data --from oracle --to sqlite` trước rồi mới quay lại.
- Bước 7: trỏ DuckDNS về IP VM1, chạy lại `setup-server.sh` chế độ thường.

## Rủi ro chính

- **Chưa có `Oracle.EntityFrameworkCore` cho EF Core 10** → chặn toàn bộ kế hoạch. Kiểm tra ở bước 0. Nếu chưa có, chuyển sang MySQL HeatWave Always Free (EF giống SQLite hơn).
- **Lỗi `''` → NULL** lọt qua 1.2. Giảm thiểu: chạy toàn bộ test thủ công trên `lotterydev` trước, và `copy-data` đối chiếu số dòng.
- **Độ trễ:** SQLite đọc trên đĩa cục bộ, ADB thì mỗi truy vấn đi qua mạng (cùng region, ~1–3 ms). Những trang có N+1 truy vấn sẽ chậm hơn → xem log thời gian của các trang Blog / Bản đồ / Tài khoản ở bước 4.
- **Băng thông LB 10 Mbps:** upload ảnh OCR lúc cao điểm có thể chậm. Theo dõi metric của LB.

## Kiểm tra (verification)

- **Tự động:** `dotnet test` xanh. Thêm test cho:
  - interceptor `''`/NULL (InMemory),
  - `NotificationRelayWorker` (pusher giả, hai lượt đọc không gửi trùng),
  - `copy-data` với SQLite → SQLite (đúng số dòng, giữ Id).
- **Local với `lotterydev`:** đăng ký và đăng nhập, dò vé (ảnh trong `backend/TestData`), mua và chốt vé cào (số dư đúng), blog (bài, ảnh, bình luận), thông báo realtime, bản đồ (tìm có dấu, chữ hoa/thường), website con.
- **Sau bước 3:** cookie cũ vẫn đăng nhập được; `/api/results/available` đủ 30 ngày; số dư ví của vài user khớp bản `.bak`.
- **Sau bước 7:**
  - `curl -I https://dove-so.duckdns.org` → 200, chứng chỉ hợp lệ.
  - Tắt API trên VM2 → app vẫn chạy, LB báo VM2 unhealthy → bật lại thì VM2 tự quay về.
  - Mở 2 trình duyệt khác nhau (2 cookie LB) → bình luận từ trình duyệt A, chuông ở B nhận được trong ≤ 3s.
  - Log VM2 không có dòng "Worker" cào kết quả.
  - Chạy thử `lb-cert-renew.sh` bằng `acme.sh --renew --force` → listener nhận chứng chỉ mới.

## File chính sẽ sửa / thêm

- `backend/LotteryChecker.Api/Program.cs`, `Data/AppDbContext.cs`, `LotteryChecker.Api.csproj`
- `Data/OracleEmptyStringInterceptor.cs` (mới), `Workers/NotificationRelayWorker.cs` (mới), `Tools/CopyDataCommand.cs` (mới)
- `Services/NotificationService.cs`, `Services/ShopService.cs`, `Services/GeocodingService.cs`, `Middleware/GuestQuotaAttribute.cs`
- `Migrations/` (archive cũ + `InitialOracle`), `LotteryChecker.Tests/AdminTests.cs`
- `deploy/setup-server.sh`, `deploy/Caddyfile.template`, `deploy/lottery-api.service`, `deploy/publish.ps1`
- `deploy/ORACLE-DB.md`, `deploy/LOAD-BALANCER.md`, `deploy/lb-cert-renew.sh`, `deploy/db-backup.sh` (mới)
- Sửa `deploy/README.md` / `ORACLE-VM.md`: bỏ câu "Không cần backup DB", vì DB giờ chứa tài khoản và ví.
