# Deploy frontend lên Vercel

## Kết luận: chỉ FRONTEND lên được Vercel, BACKEND phải chạy chỗ khác

| Phần | Lên Vercel? | Lý do (từ code) |
|---|---|---|
| `frontend/` (Vite + React + PWA) | ✅ Được | Build ra file tĩnh (`dist/`), không có router phía client, gọi API qua `VITE_API_URL` |
| `backend/` (ASP.NET Core .NET 10) | ❌ Không | Xem 4 lý do dưới |

Vì sao backend không chạy được trên Vercel:

1. **Không có runtime .NET.** Vercel Functions chạy Node.js / Python / Go / Ruby…, không chạy
   .NET và không chạy Docker image.
2. **Có tiến trình chạy nền 24/7.** `DailyResultFetchWorker` cào kết quả lúc 19:00 giờ VN và cào
   bù lúc khởi động. Serverless chỉ sống trong lúc có request, nên sẽ không bao giờ cào.
3. **Ghi file SQLite** (`lottery.db`). Ổ đĩa của Vercel Functions chỉ tạm thời, dữ liệu mất
   sau mỗi lần chạy.
4. **OCR nặng.** Model PP-OCRv5 (~14MB) + native ONNX Runtime/SkiaSharp + `tessdata` (~31MB),
   nạp model mất ~0,4s. Lên serverless là mỗi lần khởi động lạnh phải nạp lại, và dễ vượt giới
   hạn kích thước function.

→ Kiến trúc khi dùng Vercel:

```
Điện thoại ──HTTPS──▶ https://<ten>.vercel.app          (frontend tĩnh, Vercel)
     │
     └──HTTPS──▶ https://<domain-backend>/api/...       (backend .NET trên VM)
```

## Có cần Dockerfile không?

- **Cho Vercel: KHÔNG.** Vercel tự build frontend bằng `npm run build`, không dùng Docker.
- **Cho backend: chỉ cần nếu** bạn chọn host kiểu container (Railway, Fly.io, Render…).
  Nếu backend chạy trên VM Oracle như đã chuẩn bị trong `deploy/` thì **không cần Docker**:
  `setup-server.sh` + systemd + Caddy đã lo hết.

## Nên cân nhắc trước khi làm

Nếu backend chạy trên VM Oracle bằng `deploy/setup-server.sh`, thì **Caddy trên VM đã phục vụ
luôn frontend** cùng domain với `/api` (không CORS, HTTPS tự động). Khi đó Vercel **không bắt buộc**.

Vercel chỉ đáng thêm vào khi bạn muốn:
- `git push` là frontend tự build + deploy (không phải chạy `publish.ps1 -FrontendOnly`),
- mỗi nhánh/PR có link preview riêng,
- frontend phân phối qua CDN toàn cầu.

---

## Chuẩn bị

| # | Cần gì | Ghi chú |
|---|---|---|
| 1 | Tài khoản GitHub, code đã **commit + push** | Vercel build từ GitHub (`origin` = `thongnguyen0104/lottery-checker`). Hiện đang có nhiều file sửa chưa commit — Vercel chỉ thấy những gì đã push |
| 2 | Tài khoản Vercel | Đăng ký bằng GitHub tại https://vercel.com (gói Hobby miễn phí) |
| 3 | **Backend chạy ở một địa chỉ HTTPS CỐ ĐỊNH** | Bắt buộc. Trang Vercel là HTTPS; gọi API `http://` bị trình duyệt chặn (mixed content). Link `*.trycloudflare.com` đổi mỗi lần chạy `tunnel.ps1` → không dùng lâu dài được |
| 4 | Key OCR.space đặt trên server backend | Như cũ: `/etc/lottery-api.env` |

---

## Bước 1 — Dựng backend ở địa chỉ HTTPS cố định (VM Oracle)

Làm theo `deploy/README.md` (mục "Lần đầu"). Tóm tắt:

1. Tạo VM Oracle Always Free (ARM). Nếu báo hết capacity thì chạy `.\deploy\oci-retry-arm.ps1`.
2. Reserve Public IP, mở port 80/443 trong Security List.
3. Lấy domain miễn phí ở https://www.duckdns.org, ví dụ `dove-so.duckdns.org`, trỏ về IP của VM.
4. Cài server:
   ```powershell
   scp -i D:\Projects\lottery.key -r .\deploy ubuntu@<IP>:~/
   ssh -i D:\Projects\lottery.key ubuntu@<IP>
   sudo bash ~/deploy/setup-server.sh dove-so.duckdns.org
   ```
5. Điền biến môi trường trên VM:
   ```bash
   sudo nano /etc/lottery-api.env
   ```
   ```ini
   CloudOcr__ApiKey=<key OCR.space>
   # Cho phép trang Vercel gọi API (điền ở Bước 3, sau khi có link Vercel)
   Cors__AllowedOrigins__0=https://<ten-project>.vercel.app
   ```
6. Đẩy **chỉ backend** (frontend sẽ nằm trên Vercel):
   ```powershell
   .\deploy\publish.ps1 -Server ubuntu@<IP> -Key D:\Projects\lottery.key -BackendOnly
   ```
7. Kiểm tra:
   ```powershell
   curl https://dove-so.duckdns.org/health
   curl https://dove-so.duckdns.org/api/results/available
   ```

> **Chưa có VM, muốn thử nhanh?** Chạy backend trên máy Windows rồi
> `cloudflared tunnel --url http://localhost:5177` → dùng link `https://….trycloudflare.com` làm
> `VITE_API_URL`. Nhưng mỗi lần chạy lại tunnel link sẽ đổi → phải sửa biến môi trường trên Vercel
> + redeploy + sửa CORS. Chỉ để thử, không dùng thật.

---

## Bước 2 — Đưa frontend lên Vercel

1. **Commit + push** toàn bộ code lên GitHub (nhánh `main`).
2. Vào https://vercel.com/new → **Import Git Repository** → chọn `lottery-checker`.
3. Màn hình **Configure Project**:

   | Mục | Giá trị |
   |---|---|
   | Framework Preset | **Vite** (Vercel tự nhận) |
   | Root Directory | **`frontend`** ← quan trọng, repo có cả backend |
   | Build Command | `npm run build` (mặc định) |
   | Output Directory | `dist` (mặc định) |
   | Install Command | `npm install` (mặc định; repo có `package-lock.json`) |

4. Mở **Environment Variables**, thêm:

   | Name | Value | Environment |
   |---|---|---|
   | `VITE_API_URL` | `https://dove-so.duckdns.org` | Production (và Preview nếu cần) |

   - **Không** có dấu `/` ở cuối, **không** thêm `/api` — code đã tự gọi `/api/scan`, `/api/check`…
     (`frontend/src/api/client.ts`).
   - Biến `VITE_*` được **gắn cứng lúc build**. Đổi giá trị thì phải **Redeploy** mới có hiệu lực.

5. Bấm **Deploy**. Xong sẽ có link `https://<ten-project>.vercel.app`.
6. (Nên làm) **Settings → General → Node.js Version**: chọn `22.x` trở lên (Vite 8 cần Node ≥ 22.12
   hoặc 20.19).

## Bước 3 — Cho phép domain Vercel gọi backend (CORS)

Ở Production, backend chỉ nhận request từ các origin trong `Cors:AllowedOrigins`
(`backend/LotteryChecker.Api/Program.cs`). Thêm link Vercel vào:

```bash
ssh -i D:\Projects\lottery.key ubuntu@<IP>
sudo nano /etc/lottery-api.env
#   Cors__AllowedOrigins__0=https://<ten-project>.vercel.app
sudo systemctl restart lottery-api
```

- Ghi **đúng** `https://` + domain, **không** có `/` cuối.
- Sau này gắn domain riêng cho Vercel thì thêm dòng `Cors__AllowedOrigins__1=https://<domain-rieng>`.
- Link **preview** của Vercel (mỗi nhánh một link ngẫu nhiên) sẽ bị CORS chặn, trừ khi dùng
  cách proxy ở mục "Cách khác" bên dưới.

## Bước 4 — Kiểm tra trên điện thoại thật

1. Mở `https://<ten-project>.vercel.app` trên điện thoại (Safari/Chrome).
2. Chụp hoặc chọn ảnh vé → phải ra "Kết quả đọc tự động" và bảng ⏱.
3. Bấm **Dò ngay** → ra kết quả.
4. Nếu báo *"Không kết nối được tới máy chủ"*: mở trang trên máy tính → DevTools (F12) → Console.
   - Có chữ **CORS** → sai/thiếu `Cors__AllowedOrigins__0` (Bước 3).
   - Có **Mixed Content** → `VITE_API_URL` đang là `http://`, phải là `https://`.
   - **404** ở `/api/...` trên domain vercel.app → `VITE_API_URL` trống: set lại rồi **Redeploy**.
5. Đã cài app ra màn hình chính (PWA) từ domain cũ thì phải **cài lại** từ domain Vercel.

## Các lần deploy sau

| Sửa gì | Làm gì |
|---|---|
| Frontend | `git push` lên `main` → Vercel tự build + deploy |
| Backend | `.\deploy\publish.ps1 -Server ubuntu@<IP> -Key D:\Projects\lottery.key -BackendOnly` |
| Đổi URL backend | Sửa `VITE_API_URL` trên Vercel → **Redeploy** |

---

## Cách khác: cho Vercel làm proxy `/api` (không cần CORS)

Tạo file `frontend/vercel.json`:

```json
{
  "rewrites": [
    { "source": "/api/:path*", "destination": "https://dove-so.duckdns.org/api/:path*" }
  ]
}
```

và **để trống** `VITE_API_URL`. Frontend vẫn gọi `/api/...` cùng origin như lúc dev, Vercel chuyển
tiếp sang backend.

| | Gọi thẳng (`VITE_API_URL` + CORS) — **khuyên dùng** | Proxy qua Vercel (`vercel.json`) |
|---|---|---|
| Tốc độ upload ảnh | Đi thẳng tới backend | Thêm 1 chặng qua Vercel |
| Cấu hình backend | Phải thêm CORS | Không cần |
| Link preview của Vercel | Bị CORS chặn | Chạy được |
| Giới hạn | Không | Request đi qua proxy Vercel bị giới hạn thời gian/kích thước — kiểm tra docs Vercel hiện hành |

---

## Nếu không dùng VM: backend lên container (Railway / Fly.io / Render) — cần Dockerfile

Repo **chưa có** Dockerfile. Host container phải đáp ứng:

- **Luôn chạy, không ngủ**: để worker 19:00 cào kết quả. Gói free của Render ngủ khi không có
  request → worker không chạy, và request đầu tiên sau khi thức dậy phải chờ khởi động + nạp model OCR.
- **Ổ đĩa bền (volume)** cho `lottery.db`, trỏ bằng `ConnectionStrings__Default=Data Source=/data/lottery.db`.
  Không có volume cũng chạy được: DB chỉ là cache 30 ngày, mỗi lần khởi động tự cào bù (~40s).
- Biến môi trường: `ASPNETCORE_ENVIRONMENT=Production`, `CloudOcr__ApiKey`, `Cors__AllowedOrigins__0`,
  `ASPNETCORE_URLS=http://0.0.0.0:<PORT>`.
- Image `mcr.microsoft.com/dotnet/aspnet:10.0`. Native ONNX Runtime / SkiaSharp cho Linux đã có
  sẵn trong output của `dotnet publish`. Tesseract thì cần cài thêm `libtesseract` + `libleptonica`
  (xem `setup-server.sh`), nhưng chỉ khi đặt `Ocr:Engine=tesseract` — mặc định là ONNX.
