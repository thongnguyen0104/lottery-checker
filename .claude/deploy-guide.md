# Deploy Prod — Phương án tiết kiệm nhất

> Thay thế §9 của `lottery-checker-plan.md` (§9 viết trước khi có Vite proxy, `DrawSchedule`,
> cloud OCR và startup catch-up — vài bước ở đó giờ đã sai).
>
> **Tổng chi phí phương án khuyến nghị: $0/tháng, $0–3/năm** (chỉ tốn tiền domain, và
> có cách $0 hoàn toàn).

---

## 0. Chọn phương án

| # | Cách | Chi phí | Ưu | Nhược |
|---|---|---|---|---|
| 0 | Máy Windows của bạn + Cloudflare Tunnel | **$0** | Tesseract đã chạy sẵn, không cài Linux, 15 phút xong | Máy phải bật 24/7, mất điện = mất web |
| **1** | **Oracle Always Free ARM + Caddy (1 VM, 1 domain)** | **$0/tháng** | Thật sự 24/7, 4 vCPU/24GB miễn phí vĩnh viễn, OCR nhanh | Phải fix native lib Tesseract, Oracle hay hết slot ARM |
| 2 | Oracle Always Free AMD micro (1/8 OCPU, 1GB) | $0/tháng | Luôn có slot | 1/8 OCPU → Tesseract 3 lượt PSM rất chậm (10–30s/ảnh) |
| 3 | Fly.io / Railway | $0–5/tháng | Deploy bằng Docker, không quản server | Free tier hay đổi chính sách; Railway hết credit là dừng |

**Khuyến nghị: #1.** Hướng dẫn dưới đây đi theo #1; phần khác biệt cho #0 và #2 ghi ở §9.

Vì sao **không** tách frontend lên Cloudflare Pages như §9 cũ: cho Caddy serve luôn `dist/`
cùng domain với `/api` thì **hết CORS**, không cần `VITE_API_URL`, không cần domain thứ 2 —
mà vẫn $0. Code frontend hiện tại đã gọi `/api/...` tương đối nên chạy thẳng, không sửa gì.

---

## 1. Ba việc cần trong code — ĐÃ LÀM XONG

Ghi lại để biết prod hoạt động thế nào, không phải việc còn tồn.

### 1.1 Migrate DB ở Production — ĐÃ SỬA

Trước đây `Program.cs` chỉ `db.Database.Migrate()` trong `if (IsDevelopment())`, lên prod sẽ
không có bảng nào → **mọi request lỗi `no such table: LotteryResults`**. Nay migrate chạy ở mọi
môi trường; seed data giả và OpenAPI/Scalar vẫn chỉ ở dev.

Đã verify: chạy `ASPNETCORE_ENVIRONMENT=Production` với DB trắng → file `.db` được tạo có bảng,
`/api/check` trả `NoData` bình thường, `/scalar/v1` trả 404.

### 1.2 API key OCR.space — ĐÃ BỎ KHỎI REPO

`appsettings.json` từng để `"ApiKey": "helloworld"` (key demo công khai của OCR.space, dùng chung
toàn thế giới nên rate-limit liên tục). Nay để rỗng và **bạn phải tự set key**, nếu không cloud OCR
tắt (app vẫn chạy, chỉ đọc số vé cách điệu kém hơn). Khởi động thiếu key sẽ có log:
`warn: CloudOcr đang bật nhưng thiếu ApiKey — ...`

Lấy key free tại https://ocr.space/ocrapi (25.000 request/tháng, $0), rồi:

```powershell
# dev (máy Windows) — lưu ngoài repo, không commit
cd D:\Projects\lottery-checker\backend\LotteryChecker.Api
dotnet user-secrets set "CloudOcr:ApiKey" "<KEY_CUA_BAN>"
```

Prod: đặt biến môi trường `CloudOcr__ApiKey` (xem §5). .NET tự map `__` → `:`.

### 1.3 Timezone — ĐÃ SỬA TRONG CODE

Vòng lặp 19h của `DailyResultFetchWorker` từng dùng `DateTime.Now` → trên server UTC sẽ cào lúc
02:00 giờ VN. Nay nó tính mốc theo `DrawSchedule.NowVn()`, nên **chạy đúng 19:00 giờ VN dù server
ở múi giờ nào**. Logic "chưa đến giờ xổ" vốn đã dùng giờ VN từ trước.

`TZ=Asia/Ho_Chi_Minh` trong systemd giờ chỉ còn tác dụng cho **timestamp trong log** — nên giữ cho
dễ đọc log, nhưng không còn là điều kiện để chạy đúng.

---

## 2. Tạo VM Oracle (một lần)

1. https://cloud.oracle.com → Sign up Always Free (cần thẻ để verify, **không bị trừ tiền**).
2. Region: chọn **Singapore** hoặc **Osaka** (gần VN nhất, ping ~30–60ms).
3. Compute → Create Instance:
   - Image: **Ubuntu 24.04**
   - Shape: **VM.Standard.A1.Flex** — 4 OCPU / 24GB RAM (toàn bộ hạn mức ARM free)
   - Lưu private key khi nó cho tải.
4. Networking → Security List của subnet → thêm **Ingress rule**: `0.0.0.0/0` TCP port **80** và **443**.

```bash
ssh -i key.key ubuntu@<PUBLIC_IP>
```

> **Bẫy Oracle kinh điển**: mở Security List xong vẫn không vào được port 80. Image Oracle có
> iptables chặn sẵn, phải mở thêm ở trong VM:
> ```bash
> sudo iptables -I INPUT 5 -p tcp --dport 80 -j ACCEPT
> sudo iptables -I INPUT 6 -p tcp --dport 443 -j ACCEPT
> sudo apt install -y iptables-persistent   # chọn Yes để lưu
> sudo netfilter-persistent save
> ```

> Nếu Oracle báo **"Out of host capacity"** cho ARM (rất thường xuyên): thử lại vào 2–5h sáng,
> đổi Availability Domain, hoặc dùng phương án #2 (AMD micro) — xem §9.

---

## 3. Cài server (chạy 1 lần, ~5 phút)

```bash
# .NET 10 ASP.NET Core runtime (không cần SDK trên server)
wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb
sudo apt update
sudo apt install -y aspnetcore-runtime-10.0

# Native lib cho Tesseract NuGet (app tự mang tessdata theo, apt chỉ để có .so)
sudo apt install -y tesseract-ocr libtesseract-dev libleptonica-dev

# BẮT BUỘC: Tesseract NuGet 5.2.0 chỉ đóng gói native cho Windows
# (publish ra x64/tesseract50.dll + x64/leptonica-1.82.0.dll), nên trên Linux nó
# DllImport theo đúng 2 tên đó -> phải symlink sang .so thật của Ubuntu.
ARCH="$(uname -m)-linux-gnu"
cd /usr/lib/$ARCH
sudo ln -sf "$(ls libtesseract.so.* | head -1)" libtesseract50.so
sudo ln -sf "$(ls liblept.so.*      | head -1)" libleptonica-1.82.0.so
sudo ldconfig
cd ~

# Caddy — reverse proxy + tự xin SSL Let's Encrypt
sudo apt install -y debian-keyring debian-archive-keyring apt-transport-https curl
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' \
  | sudo gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' \
  | sudo tee /etc/apt/sources.list.d/caddy-stable.list
sudo apt update && sudo apt install -y caddy

# Thư mục
sudo mkdir -p /opt/lottery-api /var/www/lottery /var/lib/lottery
sudo chown -R ubuntu:ubuntu /opt/lottery-api /var/www/lottery /var/lib/lottery
```

**Verify**: `dotnet --list-runtimes` (có `Microsoft.AspNetCore.App 10.0.x`),
`tesseract --version`, `systemctl status caddy`.

---

## 4. Build & đẩy lên server (từ máy Windows)

```powershell
cd D:\Projects\lottery-checker\backend\LotteryChecker.Api
# KHÔNG dùng -r linux-arm64: publish portable chạy được cả ARM lẫn x64, đỡ nhầm kiến trúc
dotnet publish -c Release -o publish

cd D:\Projects\lottery-checker\frontend
# VITE_API_URL phải RỖNG để FE gọi /api cùng origin (đã là mặc định trong .env)
npm run build

# Đẩy lên (scp có sẵn trong Windows 10+)
scp -i key.key -r D:\Projects\lottery-checker\backend\LotteryChecker.Api\publish\* ubuntu@<IP>:/opt/lottery-api/
scp -i key.key -r D:\Projects\lottery-checker\frontend\dist\*                      ubuntu@<IP>:/var/www/lottery/
```

**Verify trên server**: `ls /opt/lottery-api/tessdata` phải có `vie.traineddata` (~7MB) và
`eng.traineddata`. Nếu thiếu → OCR sẽ chết ngay lần scan đầu.

---

## 5. systemd service

```bash
sudo tee /etc/lottery-api.env > /dev/null << 'EOF'
CloudOcr__ApiKey=<KEY_OCRSPACE_THAT_CUA_BAN>
EOF
sudo chmod 600 /etc/lottery-api.env

sudo tee /etc/systemd/system/lottery-api.service > /dev/null << 'EOF'
[Unit]
Description=Lottery Checker API
After=network.target

[Service]
WorkingDirectory=/opt/lottery-api
ExecStart=/usr/bin/dotnet /opt/lottery-api/LotteryChecker.Api.dll
Restart=always
RestartSec=10
User=ubuntu
Environment=ASPNETCORE_ENVIRONMENT=Production
# Chỉ bind loopback — Caddy là cửa duy nhất ra internet
Environment=ASPNETCORE_URLS=http://127.0.0.1:5000
# Chỉ để log dễ đọc — worker đã tự tính 19:00 theo giờ VN, không phụ thuộc dòng này
Environment=TZ=Asia/Ho_Chi_Minh
# DB đặt ngoài thư mục publish để deploy lại không đè mất
Environment=ConnectionStrings__Default=Data Source=/var/lib/lottery/lottery.db
EnvironmentFile=/etc/lottery-api.env

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable --now lottery-api
```

**Verify**:
```bash
curl http://127.0.0.1:5000/health                 # {"status":"ok",...}
journalctl -u lottery-api -n 40 --no-pager | grep -i "Khởi động"
# Lần đầu: "thiếu 30/30 ngày ... bắt đầu cào bù" → ~40s sau "cào bù xong, lưu 1620 dòng"
```

Nếu thấy `DllNotFoundException` / `Failed to load libtesseract...` → xem §8.

---

## 6. Caddy: 1 domain cho cả FE và API

```bash
sudo tee /etc/caddy/Caddyfile > /dev/null << 'EOF'
dove-so.example.com {
    encode zstd gzip

    # API + health đi về backend
    handle /api/* {
        reverse_proxy 127.0.0.1:5000
    }
    handle /health {
        reverse_proxy 127.0.0.1:5000
    }

    # Còn lại là SPA (fallback index.html cho client-side routing)
    handle {
        root * /var/www/lottery
        try_files {path} /index.html
        file_server
    }
}
EOF

sudo systemctl reload caddy
journalctl -u caddy -n 30 --no-pager | grep -i "certificate obtained"
```

Caddy tự xin và tự gia hạn cert Let's Encrypt — không cần certbot, không cần cron.

---

## 7. Domain + DNS

HTTPS là **bắt buộc**, không phải cho đẹp: `getUserMedia` (camera) chỉ chạy trong secure context,
và PWA "Add to Home Screen" cũng yêu cầu HTTPS. Let's Encrypt không cấp cert cho IP trần → phải có domain.

| Cách | Giá | Ghi chú |
|---|---|---|
| **DuckDNS** (`ten-ban.duckdns.org`) | **$0** | Caddy xin LE cert bình thường. Rẻ nhất tuyệt đối |
| `.io.vn` ở registrar VN | ~30–60k VNĐ/năm | Rẻ và ổn định lâu dài, tên gọn |
| `.xyz` | ~$1–3 năm đầu | Nhưng gia hạn ~$12/năm — đọc kỹ |
| Cloudflare Registrar | giá gốc, không markup | Rẻ nhất cho `.com` nếu muốn tên xịn |

Trỏ DNS: 1 record **A** → public IP của VM. Nếu dùng Cloudflare, để **DNS only (mây xám)** cho
đơn giản — bật proxy (mây cam) thì phải set SSL mode "Full (strict)", dễ vướng lúc xin cert lần đầu.

Sửa tên domain trong `Caddyfile` rồi `sudo systemctl reload caddy`.

### 7.1 Đường DuckDNS (miễn phí) — chi tiết

1. https://www.duckdns.org → "sign in with GitHub" → ô **sub domain**: gõ tên (vd `dove-so`) → **add domain**.
2. Trên trang đó copy **token** (dạng UUID) và dán **public IP của VM** vào ô `current ip` → update.
3. Verify từ máy Windows: `nslookup dove-so.duckdns.org` → phải ra đúng IP VM.
4. Trong `Caddyfile` (§6) đổi `dove-so.example.com` → `dove-so.duckdns.org`.

Caddy dùng HTTP-01 challenge (qua port 80) nên **không cần** token DuckDNS hay plugin DNS gì cả.

> **Quan trọng — IP Oracle mặc định là ephemeral**: reboot/stop instance là có thể đổi IP, lúc đó
> domain trỏ sai và web chết. Sửa 1 lần cho xong: Oracle Console → Instance → Attached VNICs →
> IPv4 Addresses → Edit → đổi Public IP từ **Ephemeral** sang **Reserved** (reserved IP nằm trong
> Always Free, $0).
>
> Muốn chắc ăn hơn nữa, thêm updater tự động trên VM (chạy mỗi 5 phút, tự sửa IP nếu đổi):
> ```bash
> echo 'url="https://www.duckdns.org/update?domains=dove-so&token=<TOKEN>&ip="; curl -s -k -o /tmp/duck.log -K -' > ~/duck.sh
> chmod 700 ~/duck.sh
> ( crontab -l 2>/dev/null; echo "*/5 * * * * ~/duck.sh >/dev/null 2>&1" ) | crontab -
> ~/duck.sh && cat /tmp/duck.log    # phải in "OK"
> ```

---

## 8. Checklist verify prod

```bash
curl https://<domain>/health                       # {"status":"ok",...}
curl https://<domain>/api/results/available        # danh sách 30 ngày × các đài
curl -X POST https://<domain>/api/check -H "Content-Type: application/json" \
     -d '{"ticketNumber":"123456","drawDate":"2026-08-22","province":"TPHCM"}'
```
- [ ] `/api/admin/fetch` trả **404** (đúng — chỉ mở ở Development)
- [ ] `/scalar/v1` trả 404 (đúng — chỉ mở ở Development)
- [ ] Mở `https://<domain>` trên điện thoại thật → camera xin quyền được → chụp vé → đủ 3 stage
- [ ] `journalctl -u lottery-api --since "19:00"` hôm sau có log worker cào lúc 19h

---

## 9. Khác biệt cho phương án #0 và #2

**#0 — máy Windows + Cloudflare Tunnel** ($0, không cần VM, không cần mở port):
```powershell
winget install Cloudflare.cloudflared
cloudflared tunnel --url http://localhost:5177     # cho ra link https://xxx.trycloudflare.com
```
Bỏ qua §2–§6 hoàn toàn. Tesseract đã chạy sẵn trên máy bạn nên không có vụ native lib. Link
`trycloudflare.com` đổi mỗi lần chạy; muốn cố định thì `cloudflared tunnel create` + 1 domain
trên Cloudflare. Đây là cách rẻ và nhanh nhất để **cho người khác dùng thử**, nhưng máy tắt là web tắt.

**#2 — Oracle AMD micro (1/8 OCPU, 1GB RAM)**: giống hệt §2–§7, thêm swap và cân nhắc CPU:
```bash
sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile
sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```
`OcrService.Extract` chạy Tesseract **3 lượt PSM** trên mỗi ảnh — với 1/8 OCPU có thể 10–30s/ảnh.
Nếu chậm quá: giảm còn 1 lượt (`PageSegMode.SingleColumn`), hoặc dựa hẳn vào cloud OCR
(OCR.space đọc số vé tốt hơn Tesseract, chỉ mất phần đọc đài/ngày).

---

## 10. Vận hành: update, backup, giám sát

**Deploy phiên bản mới** (từ máy dev, ~30s):
```powershell
dotnet publish -c Release -o publish   # trong LotteryChecker.Api
scp -i key.key -r publish\* ubuntu@<IP>:/opt/lottery-api/
ssh -i key.key ubuntu@<IP> "sudo systemctl restart lottery-api"
```
Frontend: `npm run build` rồi scp `dist\*` sang `/var/www/lottery/` (không cần restart gì).

**Backup: KHÔNG cần** — và đây là chỗ tiết kiệm đáng kể. `lottery.db` chỉ là cache 30 ngày,
tái tạo được 100% từ scraper: mất DB → restart service → startup catch-up cào lại trong ~40s.
Không cần object storage, không cần cron backup, không cần trả tiền lưu trữ.

**Giám sát $0**: UptimeRobot free (50 monitor) ping `https://<domain>/health` mỗi 5 phút, gửi
email khi chết. Log: `journalctl -u lottery-api -f`.

---

## 11. Lỗi thường gặp

**`DllNotFoundException` / `Failed to load library libtesseract50`** — lỗi phổ biến nhất khi
deploy Tesseract NuGet lên Linux. Nguyên nhân đã kiểm chứng: `dotnet publish` chỉ sinh ra
`x64/tesseract50.dll` và `x64/leptonica-1.82.0.dll` (bản Windows), không có `.so` nào — nên trên
Linux nó tìm đúng 2 tên `libtesseract50` / `libleptonica-1.82.0`, mà Ubuntu lại đặt tên
`libtesseract.so.5` / `liblept.so.5`. Nếu đã làm symlink ở §3 thì không gặp lỗi này. Kiểm tra:
```bash
ls -l /usr/lib/$(uname -m)-linux-gnu/libtesseract50.so \
      /usr/lib/$(uname -m)-linux-gnu/libleptonica-1.82.0.so
journalctl -u lottery-api -n 60 --no-pager | grep -i "load\|tesseract\|lept"
```

**`no such table: LotteryResults`** → chưa làm §1.1.

**Startup log không có dòng "Khởi động:"** → service chưa chạy hoặc crash sớm:
`journalctl -u lottery-api -n 100 --no-pager`.

**Camera không mở trên điện thoại** → đang vào bằng `http://` hoặc IP. Phải là `https://<domain>`.

**Cào bù trả `lưu 0 dòng` + log "không parse được đài hợp lệ nào"** → DOM xosodaiphat đổi, hoặc
IP server bị chặn. Test tay: `curl -s -A "Mozilla/5.0" https://xosodaiphat.com/xsmn-22-08-2026.html | grep -c table-xsmn`.

**Caddy không xin được cert** → DNS chưa propagate (`dig <domain>` từ server), hoặc port 80 chưa
mở (nhớ cả iptables ở §2).
