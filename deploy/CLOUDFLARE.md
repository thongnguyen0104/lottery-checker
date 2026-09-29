# Đưa web ra sau Cloudflare (chống DDoS, giấu IP server)

Mô hình sau khi xong:

```
Người dùng ──HTTPS──> Cloudflare (lọc DDoS/bot, rate limit) ──tunnel──> cloudflared (trên VM)
                                                                   └─> Caddy 127.0.0.1:8080 ─> API 127.0.0.1:5000
```

- VM **không mở port nào** ra internet (đóng 80/443). cloudflared tự kết nối *ra* Cloudflare,
  nên kẻ tấn công không có địa chỉ để đánh thẳng vào server — kể cả khi biết IP.
- HTTPS do Cloudflare cấp, Caddy không cần xin chứng chỉ Let's Encrypt nữa.
- Backend lấy IP thật từ header `CF-Connecting-IP` → giới hạn theo IP vẫn đúng từng người
  (bật tự động bằng `Cloudflare__TrustConnectingIp=true` trong `/etc/lottery-api.env`).

Tất cả dùng **gói Free** của Cloudflare, $0.

---

## Bước 0 — Cần một domain thật (không dùng được DuckDNS)

Cloudflare chỉ bảo vệ được domain mà nó **quản lý DNS** (bạn phải trỏ nameserver của domain về
Cloudflare). `dove-so.duckdns.org` là subdomain của DuckDNS, không đổi nameserver được.

→ Mua một domain rẻ (~250.000đ/năm cho `.com`, hoặc `.xyz`/`.site` vài chục nghìn năm đầu) ở
Cloudflare Registrar (giá gốc, tự động dùng Cloudflare luôn), Namecheap, Porkbun, hoặc
nhà đăng ký Việt Nam (Mắt Bão, PA Việt Nam, iNET) nếu muốn `.vn`.

## Bước 1 — Thêm domain vào Cloudflare (10 phút + chờ DNS)

1. Đăng ký tài khoản ở <https://dash.cloudflare.com>.
2. **Add a domain** → nhập domain → chọn gói **Free**.
3. Cloudflare đưa 2 nameserver (dạng `xxx.ns.cloudflare.com`). Vào trang quản lý domain ở nơi
   bạn mua → đổi nameserver thành 2 cái đó. (Mua ở Cloudflare Registrar thì bỏ qua bước này.)
4. Chờ email "domain is active" — thường vài phút, lâu nhất 24h.
5. Nếu Cloudflare tự import bản ghi DNS cũ trỏ về IP VM (A record) thì **xoá đi** — tunnel sẽ tự
   tạo bản ghi, còn giữ A record cũ là lộ IP server.

## Bước 2 — Tạo tunnel, lấy token (5 phút)

1. Dashboard → **Zero Trust** (lần đầu vào sẽ hỏi tên team, chọn gói Free — có thể phải nhập thẻ
   nhưng không bị trừ tiền).
2. **Networks → Tunnels → Create a tunnel** → chọn **Cloudflared** → đặt tên, vd `dove-so`.
3. Màn hình "Install connector" có lệnh `cloudflared service install eyJh...` → **copy phần token**
   `eyJh...` (chuỗi dài). Không cần chạy lệnh đó, script bên dưới làm giúp.
4. **Next** → tab **Public Hostname** → Add:
   | Ô | Giá trị |
   |---|---|
   | Subdomain | để trống (dùng domain gốc) hoặc `www` |
   | Domain | domain của bạn |
   | Service Type | `HTTP` |
   | URL | `127.0.0.1:8080` |
5. **Save tunnel**.

## Bước 3 — Cài trên VM (2 phút)

Từ máy Windows đẩy thư mục deploy mới lên rồi chạy script ở chế độ `--tunnel`:

```powershell
scp -i D:\Projects\lottery.key -r .\deploy ubuntu@<IP>:~/
ssh -i D:\Projects\lottery.key ubuntu@<IP>
```

```bash
sudo CF_TUNNEL_TOKEN='eyJh...' bash ~/deploy/setup-server.sh <domain-của-bạn> --tunnel
```

Script sẽ: cài `cloudflared` và chạy nó thành service, **đóng port 80/443** trong iptables,
đổi Caddy sang chỉ nghe `127.0.0.1:8080`, ghi `Cloudflare__TrustConnectingIp=true` vào env,
restart backend. Chạy lại nhiều lần được; muốn quay về chế độ cũ thì chạy lại **không** có `--tunnel`.

Kiểm tra:

```bash
systemctl status cloudflared --no-pager     # active (running)
curl https://<domain>/health                # {"status":"ok",...}
```

Trên Zero Trust → Tunnels, tunnel phải hiện **HEALTHY**.

## Bước 4 — Đóng port trên Oracle Console (1 phút)

Oracle Cloud → VCN → Security List → **xoá 2 rule Ingress TCP 80 và 443** (giữ lại port 22 cho SSH).
Hai lớp (iptables + Security List) cùng đóng thì server không nhận kết nối web nào ngoài tunnel.

> Frontend trên Vercel ([VERCEL.md](VERCEL.md))? Đổi `VITE_API_URL` sang `https://<domain>` và thêm
> domain Vercel vào `Cors__AllowedOrigins__0` trong `/etc/lottery-api.env`.

## Bước 5 — Bật các lớp bảo vệ trên Cloudflare (5 phút)

Dashboard → chọn domain:

1. **Security → Settings** (hoặc Bots): bật **Bot Fight Mode**.
2. **SSL/TLS → Edge Certificates**: bật **Always Use HTTPS**.
3. **Security → WAF → Rate limiting rules → Create rule** (gói Free được 1 rule):
   - Name: `api-ai`
   - If incoming requests match → **Edit expression**, dán:
     ```
     (http.request.uri.path eq "/api/scan" or http.request.uri.path eq "/api/ai/dream")
     ```
   - Characteristics: **IP**
   - When rate exceeds: **10** requests / **10 seconds**
   - Then: **Block**, duration **10 seconds**
   - Deploy.

   Rule này chặn ngay ở Cloudflare đợt spam dồn dập, không để lọt tới server. Giới hạn chặt theo
   phút (12 lượt Gemini/phút mỗi loại, tối đa 14 lượt/phút mỗi model cho cả server, 10 lượt soi vé / 5 lượt luận mơ mỗi IP mỗi phút) vẫn
   do backend lo.
4. Đang bị tấn công thật: **Overview → Under Attack Mode** = On (mọi người phải qua một màn kiểm
   tra ~5 giây). Hết thì tắt đi.

## Lưu ý

- **Không chạy `setup-server.sh` thường** (không `--tunnel`) khi đang dùng Cloudflare: nó mở lại
  port 80/443 và tắt `TrustConnectingIp`.
- Token tunnel = quyền chạy tunnel của bạn. Lộ thì vào Zero Trust → tunnel → **Refresh token**, rồi
  chạy lại bước 3 với token mới.
- `tunnel.ps1` (link `trycloudflare.com` chạy từ máy Windows) vẫn dùng được để test nhanh, không
  liên quan tới setup này.
