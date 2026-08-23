# deploy/ — script deploy prod

Chi tiết từng bước, giải thích lý do chọn từng thứ: [`.claude/deploy-guide.md`](../.claude/deploy-guide.md).
Thư mục này là bản rút gọn để chạy.

| File | Chạy ở đâu | Việc |
|---|---|---|
| `setup-server.sh` | trên VM (1 lần) | iptables, .NET runtime, Tesseract + symlink native, Caddy, thư mục, systemd, Caddyfile |
| `publish.ps1` | máy Windows (mỗi lần deploy) | build backend + frontend, nén, scp, restart service |
| `lottery-api.service` | template | systemd unit (`__APP_USER__` được thay khi setup) |
| `Caddyfile.template` | template | Caddy config (`__DOMAIN__` được thay khi setup) |
| `oci-retry-arm.ps1` | máy Windows | thử tạo VM ARM liên tục cho tới khi Oracle có capacity |
| `tunnel.ps1` | máy Windows | chạy app ra internet qua Cloudflare Tunnel, không cần VM |

## Chưa có VM? Hai script gỡ bí

**Oracle báo `Out of capacity` cho ARM** (rất thường xuyên) — để script tự thử, đừng ngồi bấm tay:

```powershell
winget install --id Oracle.OCI-CLI
oci setup config          # dán tenancy/user OCID + region, rồi thêm API key trong Console
.\deploy\oci-retry-arm.ps1
```
Mặc định thử `1 OCPU / 6 GB` mỗi 90 giây (1 OCPU dễ chen vào capacity phân mảnh hơn 2 OCPU),
tự lùi 5 phút khi bị throttle, tạo được thì in Public IP + các bước tiếp theo. Cứ để cửa sổ đó chạy.

**Muốn có link HTTPS dùng ngay hôm nay, không cần VM:**

```powershell
winget install --id Cloudflare.cloudflared
.\deploy\tunnel.ps1
```
Chạy backend (Production, đọc key OCR từ user-secrets) + `vite preview` (bản build thật, service
worker PWA hoạt động) rồi mở tunnel → in ra link `https://....trycloudflare.com`. Ctrl+C là dừng hết.
Máy tắt thì web tắt — đây là bản tạm trong lúc chờ VM.

## Lần đầu

Giả sử VM đã tạo, IP đã reserve, port 80/443 đã mở trong Security List, domain đã trỏ về IP.

```powershell
# 1. Đẩy thư mục deploy lên VM
scp -i D:\Projects\lottery.key -r .\deploy ubuntu@<IP>:~/

# 2. Cài server (SSH vào VM)
ssh -i D:\Projects\lottery.key ubuntu@<IP>
sudo bash ~/deploy/setup-server.sh dove-so.duckdns.org

# 3. Điền key OCR.space (lấy từ máy dev: dotnet user-secrets list)
sudo nano /etc/lottery-api.env      # CloudOcr__ApiKey=...
exit

# 4. Đẩy code (từ máy Windows)
.\deploy\publish.ps1 -Server ubuntu@<IP> -Key D:\Projects\lottery.key
```

## Các lần deploy sau

```powershell
.\deploy\publish.ps1 -Server ubuntu@<IP> -Key D:\Projects\lottery.key
```
Chỉ sửa frontend thì thêm `-FrontendOnly` (không restart backend).

## Verify

```bash
curl https://<domain>/health                    # {"status":"ok",...}
curl https://<domain>/api/results/available     # 30 ngày × các đài
journalctl -u lottery-api -n 40 --no-pager      # có dòng "Khởi động: ..."
```
Rồi mở `https://<domain>` **trên điện thoại thật** — camera phải xin được quyền.

## Lưu ý

- **Không cần backup DB**: `lottery.db` chỉ là cache 30 ngày, mất thì restart service là
  startup catch-up cào lại trong ~40s.
- **Secret không nằm trong repo**: key OCR ở `/etc/lottery-api.env` (chmod 600) trên server,
  ở user-secrets trên máy dev. `dotnet publish` không mang user-secrets theo.
- **Lỗi Tesseract trên Linux** (`libtesseract50`): `setup-server.sh` đã symlink sẵn; nếu vẫn lỗi
  thì xem `.claude/deploy-guide.md` §11.
