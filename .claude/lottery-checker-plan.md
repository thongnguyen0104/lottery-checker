# Dò Vé Số (C# + React) — Plan & Hướng Dẫn Triển Khai Đầy Đủ

> **Cách dùng tài liệu này**: Đọc và làm tuần tự từ §0 → §11. Mỗi sub-section có 3 khối: **lệnh/code copy-paste** → **cách kiểm tra** → **nếu sai thì xem đâu**. Khi chạy lệnh xong, không thấy output mong đợi thì DỪNG LẠI, xem §"Lỗi thường gặp" trong cùng phần trước khi đi tiếp.
>
> **Thời gian dự kiến**:
> - §1–§3 (đọc kiến trúc + tech stack): 30 phút
> - §4 (cài tools): 1–2 giờ
> - §5 (scaffold backend + frontend + kết nối): 2 giờ
> - §6 (code backend): 1 tuần
> - §7 (code frontend): 3–4 ngày
> - §8 (DB + seed): nửa ngày
> - §9 (deploy): nửa ngày
> - §10–§11 (workflow + polish): liên tục
>
> **Phiên bản nền tảng** (pin cố định, KHÔNG dùng "latest"):
>
> | Thành phần | Version | Ghi chú |
> |---|---|---|
> | .NET SDK | `10.0.300` | LTS, hỗ trợ đến 14/11/2028 |
> | C# | `14` | đi kèm .NET 10 |
> | dotnet-ef tool | `10.0.*` | phải khớp .NET 10 |
> | Node.js | `20 LTS` | |
> | Tesseract OCR | `5.x` | + `vie.traineddata` |
> | Tesseract NuGet | `5.2.0` | |
> | SixLabors.ImageSharp | `3.1.5` | |
> | EF Core Sqlite + Design | `10.0.8` | |
> | HtmlAgilityPack | `1.11.65` | |
> | Serilog.AspNetCore | `10.0.0` | |
> | Scalar.AspNetCore | `2.1.0` | UI cho OpenAPI |
> | React | `18` | |
> | Vite | `5` | |
> | tailwindcss | `3` | **KHÔNG dùng v4** — đổi cú pháp |
>
> **Quy ước đường dẫn**: Windows `D:\Projects\lottery-checker\...`, macOS `~/Projects/lottery-checker/...`.
>
> **Port cố định**: backend `5000`, frontend `5173`.

---

## Mục lục

- [§0. Bắt đầu](#cách-dùng-tài-liệu-này) (header trên)
- [§1. Tổng quan kiến trúc](#1-tổng-quan-kiến-trúc)
- [§2. Tech stack + lý do chọn](#2-tech-stack--lý-do-chọn)
- [§3. Cấu trúc thư mục](#3-cấu-trúc-thư-mục)
- [§4. Cài tools nền tảng](#4-cài-tools-nền-tảng)
- [§5. Scaffold backend + frontend](#5-scaffold-backend--frontend)
- [§6. Code backend](#6-code-backend)
- [§7. Code frontend](#7-code-frontend)
- [§8. Database & seed data](#8-database--seed-data)
- [§9. Deployment](#9-deployment)
- [§10. VS Code workspace & workflow hàng ngày](#10-vs-code-workspace--workflow-hàng-ngày)
- [§11. Roadmap, lưu ý quan trọng, cost estimate](#11-roadmap-lưu-ý-quan-trọng-cost-estimate)

---

## 1. Tổng quan kiến trúc

Hệ thống gồm 3 thành phần giao tiếp qua HTTP/REST:

- **Frontend (React PWA)**: giao diện người dùng, truy cập camera qua `getUserMedia API`, chụp/upload ảnh vé, gửi về backend.
- **Backend (ASP.NET Core Web API)**: nhận ảnh → tiền xử lý (resize, threshold, deskew) → OCR trích **đồng thời 3 trường: số vé + ngày + đài** → tra `LotteryResults` → trả JSON kết quả trúng/trượt.
- **Background Worker (Hosted Service)**: tự động cào kết quả mỗi ngày từ minhngoc.net.vn / xoso.com.vn → lưu DB.

### Luồng nghiệp vụ 3 stage (capture → confirm → result)

```
┌──────────┐     ảnh      ┌─────────┐  OCR info  ┌───────────┐  confirm  ┌─────────┐
│ CAPTURE  │─────────────▶│ /scan   │───────────▶│  CONFIRM  │──────────▶│ /check  │
│ camera/  │              │ OCR 3   │            │ user xác  │           │ match   │
│ upload   │              │ trường  │            │ nhận/sửa  │           │ giải    │
└──────────┘              └─────────┘            └───────────┘           └────┬────┘
                                                                              │
                                                                              ▼
                                                                        ┌─────────┐
                                                                        │ RESULT  │
                                                                        │ list    │
                                                                        │ giải +  │
                                                                        │ tổng    │
                                                                        └─────────┘
```

User mở app → quét/chụp/upload ảnh vé → frontend POST `multipart/form-data` đến `/api/scan` → backend OCR trích 3 trường (số vé, ngày, đài) → trả JSON cho frontend hiển thị **form pre-filled** cho user xác nhận → user bấm "Dò" → frontend POST `/api/check` với info đã xác nhận → backend tra DB và **trả về tất cả giải trúng** (vé có thể trúng nhiều giải cùng lúc) → frontend render list giải.

### Tại sao có bước xác nhận?

OCR vé số giấy không bao giờ chuẩn 100% (dấu mộc đè số, vé nhăn, ánh sáng yếu). Để app tự quyết và báo "Trượt" trong khi thực tế OCR đọc nhầm là trải nghiệm tệ. Form pre-filled vẫn nhanh hơn nhập tay, an toàn hơn auto.

---

## 2. Tech stack + lý do chọn

### Backend

| Tech | Lý do |
|---|---|
| ASP.NET Core 10 + C# 14 | LTS đến 11/2028, MVC `ControllerBase` rõ ràng hơn Minimal API cho dự án vừa |
| Tesseract OCR (`Tesseract` NuGet 5.2.0) | Miễn phí, chạy local, có hỗ trợ tiếng Việt qua `vie.traineddata` |
| SixLabors.ImageSharp 3.1.5 | Tiền xử lý ảnh cross-platform, không cần native deps phức tạp như OpenCV |
| EF Core 10 + SQLite (dev) / PostgreSQL (prod) | ORM mature, SQLite zero-config cho dev |
| HtmlAgilityPack | Parse HTML từ trang xổ số, đơn giản và bền |
| Serilog | Logging có cấu trúc, sink ra file dễ debug production |
| OpenAPI built-in .NET 10 + Scalar UI | KHÔNG cần Swashbuckle, Scalar đẹp hơn SwaggerUI |

### Frontend

| Tech | Lý do |
|---|---|
| React 18 + TypeScript + Vite 5 | Build nhanh, bundle nhỏ hơn CRA, HMR tốt |
| `react-webcam` hoặc `navigator.mediaDevices.getUserMedia` | Truy cập camera trực tiếp trong browser |
| Tailwind CSS 3 | UI nhanh không cần viết CSS riêng (v4 đổi cú pháp, đa số tutorial vẫn theo v3) |
| Axios | Gọi API có interceptor + error handling dễ hơn fetch raw |
| `vite-plugin-pwa` | Cài lên home screen như app native, dùng offline được |

### Hosting (gần như miễn phí)

| Mục | Provider |
|---|---|
| VM backend + DB + worker | **Oracle Cloud Always Free** (4 vCPU ARM Ampere + 24GB RAM) |
| Frontend + CDN | **Cloudflare Pages** (free) |
| DNS + SSL | Cloudflare + Let's Encrypt qua Caddy |
| Domain | Namecheap `.xyz` ~$1/năm hoặc `.io.vn` ~$3/năm |

---

## 3. Cấu trúc thư mục

```
lottery-checker/
├── .git/
├── .gitignore
├── .vscode/
│   ├── launch.json
│   ├── tasks.json
│   ├── settings.json
│   └── extensions.json
├── backend/
│   ├── LotteryChecker.sln
│   ├── LotteryChecker.Api/
│   │   ├── Controllers/
│   │   │   ├── PingController.cs
│   │   │   └── ScanController.cs
│   │   ├── Services/
│   │   │   ├── ImagePreprocessor.cs
│   │   │   ├── OcrService.cs
│   │   │   ├── ProvinceMatcher.cs
│   │   │   ├── LotteryMatcher.cs
│   │   │   └── ResultScraper.cs
│   │   ├── Models/
│   │   │   ├── LotteryResult.cs
│   │   │   ├── TicketInfo.cs
│   │   │   ├── ScanResult.cs
│   │   │   └── WinningPrize.cs
│   │   ├── Data/
│   │   │   ├── AppDbContext.cs
│   │   │   └── SeedData.cs
│   │   ├── Workers/
│   │   │   └── DailyResultFetchWorker.cs
│   │   ├── Migrations/                  # generate tự động
│   │   ├── tessdata/                    # vie.traineddata + eng.traineddata (gitignored)
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── appsettings.Production.json
│   │   ├── api-tests.http
│   │   └── LotteryChecker.Api.csproj
│   └── LotteryChecker.Tests/
│       └── LotteryMatcherTests.cs
├── frontend/
│   ├── public/
│   │   ├── favicon.ico
│   │   └── icons/
│   ├── src/
│   │   ├── api/
│   │   │   └── client.ts
│   │   ├── components/
│   │   │   ├── CameraCapture.tsx
│   │   │   ├── ImageUpload.tsx
│   │   │   ├── TicketInfoConfirm.tsx
│   │   │   └── ResultDisplay.tsx
│   │   ├── pages/
│   │   │   └── Home.tsx
│   │   ├── App.tsx
│   │   ├── main.tsx
│   │   └── index.css
│   ├── index.html
│   ├── vite.config.ts
│   ├── tailwind.config.js
│   ├── .env
│   ├── .env.example
│   └── package.json
├── docker-compose.yml                   # cho dev (tùy chọn)
└── README.md
```

---

## 4. Cài tools nền tảng

### 4.1 Mở Terminal/PowerShell với quyền Admin

**Windows**: Bấm phím Windows → gõ "PowerShell" → chuột phải → "Run as administrator".

🍎 **macOS**: Mở Terminal (Cmd+Space → "Terminal"). Không cần admin nhưng cần `sudo` cho một số lệnh.

**Kiểm tra**: PowerShell phải có dòng tiêu đề "Administrator: Windows PowerShell". Trên macOS chỉ cần Terminal mở được.

### 4.2 Cài Git

**Lệnh** (Windows PowerShell):
```powershell
winget install --id Git.Git -e
```

🍎 **macOS**:
```bash
# Nếu chưa có Homebrew:
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
brew install git
```

**Kiểm tra**: đóng terminal, mở lại (để PATH cập nhật), gõ `git --version` — phải ra `git version 2.45.x` hoặc cao hơn.

**Cấu hình lần đầu**:
```powershell
git config --global user.name "Tên Của Bạn"
git config --global user.email "you@example.com"
git config --global init.defaultBranch main
```

**Nếu sai**: xem §4.9 Lỗi 1.

### 4.3 Cài .NET 10 SDK

**Lệnh** (Windows):
```powershell
winget install --id Microsoft.DotNet.SDK.10 -e
```

🍎 **macOS**:
```bash
brew install --cask dotnet-sdk
```

**Kiểm tra** (terminal mới):
```powershell
dotnet --version          # phải ra 10.0.300 hoặc cao hơn
dotnet --list-sdks        # phải thấy ít nhất 1 dòng SDK 10.x
```

**Cài tool EF Core CLI** (phải dùng bản 10):
```powershell
dotnet tool install --global dotnet-ef --version 10.0.*
```

Nếu đã cài bản 8/9 trước đó, update:
```powershell
dotnet tool update --global dotnet-ef --version 10.0.*
```

Kiểm tra: `dotnet ef --version` → phải ra `10.0.x`.

**Nếu sai**: §4.9 Lỗi 2.

### 4.4 Cài Node.js (LTS 20)

**Lệnh** (Windows):
```powershell
winget install --id OpenJS.NodeJS.LTS -e
```

🍎 **macOS**:
```bash
brew install node@20
```

**Kiểm tra**:
```powershell
node --version    # v20.x.x hoặc cao hơn
npm --version     # 10.x.x hoặc cao hơn
```

### 4.5 Cài VS Code

**Lệnh** (Windows):
```powershell
winget install --id Microsoft.VisualStudioCode -e
```

🍎 **macOS**:
```bash
brew install --cask visual-studio-code
```

**Kiểm tra**: mở VS Code → Command Palette (Ctrl+Shift+P / Cmd+Shift+P) → gõ "Shell Command: Install 'code' command in PATH" → bấm để có thể `code .` từ terminal.

Kiểm tra: `code --version`.

**Cài extension cần thiết** (chạy trong PowerShell):
```powershell
code --install-extension ms-dotnettools.csdevkit
code --install-extension ms-dotnettools.csharp
code --install-extension dbaeumer.vscode-eslint
code --install-extension esbenp.prettier-vscode
code --install-extension bradlc.vscode-tailwindcss
code --install-extension dsznajder.es7-react-js-snippets
code --install-extension formulahendry.dotnet-test-explorer
code --install-extension humao.rest-client
```

### 4.6 Cài Tesseract OCR

**Đây là phần dễ sai nhất**, đọc kỹ.

#### Windows

1. Vào https://github.com/UB-Mannheim/tesseract/wiki
2. Tải `tesseract-ocr-w64-setup-5.x.x.exe` (latest, 64-bit).
3. Chạy installer. Trong bước **"Choose Components"** → tick mở rộng "Additional language data (download)" → tick **Vietnamese**. Next đến hết.
4. Mặc định cài vào `C:\Program Files\Tesseract-OCR\`. **Ghi nhớ đường dẫn này.**

Thêm vào PATH:
```powershell
[Environment]::SetEnvironmentVariable("Path", $env:Path + ";C:\Program Files\Tesseract-OCR", "User")
```

Đóng-mở lại PowerShell. Kiểm tra:
```powershell
tesseract --version
tesseract --list-langs
```
Phải thấy `vie` trong list. Nếu không có → vào lại installer "Modify" để thêm Vietnamese.

🍎 **macOS**:
```bash
brew install tesseract tesseract-lang
tesseract --list-langs   # phải thấy 'vie'
```

#### Nếu cần tải `vie.traineddata` thủ công

- https://github.com/tesseract-ocr/tessdata/raw/main/vie.traineddata
- Lưu vào `C:\Program Files\Tesseract-OCR\tessdata\` (Windows) hoặc `/opt/homebrew/share/tessdata/` (macOS).

**Nếu sai**: §4.9 Lỗi 3.

### 4.7 Cài Docker Desktop (tùy chọn — bỏ qua nếu chưa cần)

Chưa cần cho dev local, sẽ cần khi deploy:
```powershell
winget install --id Docker.DockerDesktop -e
```

### 4.8 Tổng kiểm tra cuối Phần 4

Mở PowerShell mới và chạy hết các lệnh sau, **tất cả phải in ra version, không lệnh nào báo lỗi**:

```powershell
git --version
dotnet --version
dotnet ef --version
node --version
npm --version
tesseract --version
tesseract --list-langs    # phải có 'vie'
code --version
```

✅ Nếu OK hết → sang §5. Nếu sai → §4.9.

### 4.9 Lỗi thường gặp ở §4

**Lỗi 1**: `dotnet`/`git`/`node` not found sau khi cài
→ Đóng hết terminal, mở lại để PATH cập nhật. Vẫn lỗi → restart máy.

**Lỗi 2**: `dotnet ef` không nhận sau khi `dotnet tool install`
```powershell
$env:Path += ";$env:USERPROFILE\.dotnet\tools"
```
Hoặc cài lại: `dotnet tool install --global dotnet-ef --version 10.0.*`.

**Lỗi 3**: `tesseract --list-langs` không có `vie`
→ Vào lại installer Tesseract Modify → tick Vietnamese. Hoặc tải `vie.traineddata` thủ công đặt vào `tessdata/`.

---

## 5. Scaffold backend + frontend

### 5.1 Tạo thư mục gốc + git init

**Lệnh** (Windows):
```powershell
cd D:\
mkdir Projects
cd Projects
mkdir lottery-checker
cd lottery-checker
git init
```

🍎 **macOS**:
```bash
mkdir -p ~/Projects/lottery-checker
cd ~/Projects/lottery-checker
git init
```

### 5.2 Tạo `.gitignore`

**File**: `lottery-checker/.gitignore`

```gitignore
# .NET
bin/
obj/
*.user
*.suo
.vs/
publish/
*.pdb

# Database files (dev)
*.db
*.db-journal
*.sqlite

# Node
node_modules/
dist/
build/
.vite/

# Env
.env
.env.local
.env.*.local

# IDE
.vscode/*
!.vscode/launch.json
!.vscode/tasks.json
!.vscode/settings.json
!.vscode/extensions.json
.idea/

# OS
.DS_Store
Thumbs.db

# Logs
*.log
logs/

# Uploads tạm
uploads/
temp-images/

# Tesseract tessdata - download trong §5.6
backend/LotteryChecker.Api/tessdata/
```

### 5.3 Tạo 2 thư mục con + commit đầu

```powershell
mkdir backend
mkdir frontend
git add .
git commit -m "chore: initial commit, project scaffold"
```

**Kiểm tra**: `git log` phải có 1 commit.

### 5.4 Tạo solution + Web API project

**Lệnh**:
```powershell
cd backend
dotnet new sln -n LotteryChecker
dotnet new webapi -n LotteryChecker.Api --use-controllers
dotnet sln add LotteryChecker.Api/LotteryChecker.Api.csproj
```

**Kiểm tra**:
- File `LotteryChecker.sln` xuất hiện.
- Thư mục `LotteryChecker.Api/` có `Program.cs`, `LotteryChecker.Api.csproj`, `appsettings.json`.

### 5.5 Cài NuGet packages

```powershell
cd LotteryChecker.Api
dotnet add package Tesseract --version 5.2.0
dotnet add package SixLabors.ImageSharp --version 3.1.5
dotnet add package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.8
dotnet add package Microsoft.EntityFrameworkCore.Design --version 10.0.8
dotnet add package HtmlAgilityPack --version 1.11.65
dotnet add package Serilog.AspNetCore --version 10.0.0
dotnet add package Scalar.AspNetCore --version 2.1.0
```

> **Windows note**: nếu sau này runtime báo `leptonica-1.82.0.dll`:
> ```powershell
> dotnet add package Tesseract.Drawing --version 5.2.0
> ```

> **Lưu ý OpenAPI**: .NET 10 webapi template đã có sẵn `Microsoft.AspNetCore.OpenApi` — **không cần Swashbuckle**. Scalar dùng làm UI thay SwaggerUI.

**Kiểm tra**: mở `LotteryChecker.Api.csproj`, phải thấy đủ 7 `<PackageReference>` và `<TargetFramework>net10.0</TargetFramework>`.

### 5.6 Tạo folder + tải Tesseract data files

```powershell
mkdir Controllers
mkdir Services
mkdir Models
mkdir Data
mkdir Workers
mkdir tessdata
```

(Một số folder đã có từ template — `mkdir` báo "đã tồn tại" thì bỏ qua.)

**Tải traineddata** (Windows):
```powershell
Invoke-WebRequest -Uri "https://github.com/tesseract-ocr/tessdata/raw/main/vie.traineddata" -OutFile "tessdata\vie.traineddata"
Invoke-WebRequest -Uri "https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata" -OutFile "tessdata\eng.traineddata"
```

🍎 **macOS**:
```bash
curl -L -o tessdata/vie.traineddata https://github.com/tesseract-ocr/tessdata/raw/main/vie.traineddata
curl -L -o tessdata/eng.traineddata https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata
```

**Kiểm tra**: `tessdata/vie.traineddata` phải có dung lượng ~14MB, không phải 0 byte.

**Cấu hình copy tessdata khi build**: mở `LotteryChecker.Api.csproj`, thêm vào trước `</Project>`:

```xml
<ItemGroup>
  <None Update="tessdata\**\*.*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

### 5.7 Xóa file mẫu + cố định port

**Xóa** `WeatherForecast.cs` và `Controllers/WeatherForecastController.cs`:
```powershell
Remove-Item WeatherForecast.cs -ErrorAction SilentlyContinue
Remove-Item Controllers\WeatherForecastController.cs -ErrorAction SilentlyContinue
```

🍎 `rm -f WeatherForecast.cs Controllers/WeatherForecastController.cs`

**Cố định port backend = 5000**: mở `Properties/launchSettings.json`, thay profile `http`:
```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:5000",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

### 5.8 Tạo project Vite (frontend)

```powershell
cd ..\..\frontend
npm create vite@latest . -- --template react-ts
```

Khi hỏi "Current directory is not empty..." → chọn **"Ignore files and continue"**.

```powershell
npm install
npm install axios react-webcam
npm install -D tailwindcss@3 postcss autoprefixer vite-plugin-pwa
```

> Pin `tailwindcss@3` — KHÔNG v4.

### 5.9 Setup Tailwind

```powershell
npx tailwindcss init -p
```

**File**: `frontend/tailwind.config.js`
```js
/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  theme: {
    extend: {
      colors: {
        brand: { 50: '#FEF2F2', 500: '#DC2626', 600: '#B91C1C', 700: '#991B1B' }
      }
    },
  },
  plugins: [],
}
```

**File**: `frontend/src/index.css` (thay toàn bộ)
```css
@tailwind base;
@tailwind components;
@tailwind utilities;

html, body, #root { height: 100%; }
body {
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
  background: #FAFAFA;
}
```

### 5.10 Setup Vite + PWA

**File**: `frontend/vite.config.ts`
```ts
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'autoUpdate',
      includeAssets: ['favicon.ico', 'apple-touch-icon.png'],
      manifest: {
        name: 'Dò Vé Số',
        short_name: 'Dò Vé Số',
        description: 'Quét và dò vé số xổ số kiến thiết tự động',
        theme_color: '#DC2626',
        background_color: '#DC2626',
        display: 'standalone',
        orientation: 'portrait',
        start_url: '/',
        icons: [
          { src: '/icons/icon-192.png', sizes: '192x192', type: 'image/png' },
          { src: '/icons/icon-512.png', sizes: '512x512', type: 'image/png' },
          { src: '/icons/icon-maskable-192.png', sizes: '192x192', type: 'image/png', purpose: 'maskable' },
          { src: '/icons/icon-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' }
        ]
      }
    })
  ],
  server: {
    port: 5173,
    host: true   // để test từ điện thoại cùng wifi
  }
})
```

### 5.11 Tạo `.env` + sửa `index.html` + `App.tsx` test

**File**: `frontend/.env`
```
VITE_API_URL=http://localhost:5000
```

**File**: `frontend/.env.example` (commit lên git)
```
VITE_API_URL=http://localhost:5000
```

**File**: `frontend/index.html`
```html
<!DOCTYPE html>
<html lang="vi">
  <head>
    <meta charset="UTF-8" />
    <link rel="icon" href="/favicon.ico" sizes="any" />
    <link rel="apple-touch-icon" href="/icons/apple-touch-icon.png" />
    <meta name="theme-color" content="#DC2626" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0, viewport-fit=cover" />
    <title>Dò Vé Số</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

**File**: `frontend/src/App.tsx` (chỉ để test connect)
```tsx
import { useEffect, useState } from 'react'
import axios from 'axios'

export default function App() {
  const [ping, setPing] = useState<string>('Đang kết nối backend...')

  useEffect(() => {
    axios.get(`${import.meta.env.VITE_API_URL}/api/ping`)
      .then(r => setPing(`✅ Backend OK: ${JSON.stringify(r.data)}`))
      .catch(e => setPing(`❌ Lỗi: ${e.message}`))
  }, [])

  return (
    <div className="min-h-screen flex items-center justify-center p-4">
      <div className="max-w-md w-full bg-white rounded-2xl shadow-xl p-8 text-center">
        <h1 className="text-3xl font-bold text-brand-500 mb-2">🎫 Dò Vé Số</h1>
        <p className="text-gray-500 mb-6">Setup test page</p>
        <div className="text-sm bg-gray-50 p-4 rounded-lg break-all">{ping}</div>
      </div>
    </div>
  )
}
```

**File**: `frontend/src/main.tsx` (đảm bảo đúng)
```tsx
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
```

### 5.12 Chạy thử 2 server

**Terminal 1 — Backend**:
```powershell
cd D:\Projects\lottery-checker\backend\LotteryChecker.Api
dotnet run
```

**Kiểm tra**: thấy log `Now listening on: http://localhost:5000`. Vào browser:
- `http://localhost:5000` → "Lottery Checker API is running..." (sẽ được setup ở §6 cuối, hiện chưa có)
- Sẽ có endpoint `/api/ping` sau khi làm §6.7

Tạm thời thêm endpoint ping test nhanh — tạo `Controllers/PingController.cs`:
```csharp
using Microsoft.AspNetCore.Mvc;
namespace LotteryChecker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { message = "pong", time = DateTime.UtcNow });
}
```

Chạy lại `dotnet run`, vào `http://localhost:5000/api/ping` → phải thấy `{"message":"pong",...}`.

**Terminal 2 — Frontend**:
```powershell
cd D:\Projects\lottery-checker\frontend
npm run dev
```

**Kiểm tra**: log `Local: http://localhost:5173/`. Mở browser → thấy card "🎫 Dò Vé Số" với dòng `✅ Backend OK: {"message":"pong",...}`.

Bấm Ctrl+C ở cả 2 terminal để dừng.

### 5.13 Commit checkpoint §5

```powershell
cd D:\Projects\lottery-checker
git add .
git commit -m "feat: scaffold backend + frontend + ping connectivity"
```

### 5.14 Lỗi thường gặp ở §5

**Lỗi A**: `Network Error` trên frontend, console báo CORS
→ Đảm bảo `appsettings.json` backend có `http://localhost:5173` trong `Cors:AllowedOrigins` (sẽ làm ở §6.9). Restart backend sau khi sửa.

**Lỗi B**: Port 5000 đã bị chiếm
```powershell
netstat -ano | findstr :5000
taskkill /PID <pid> /F
```
🍎 `lsof -i :5000 && kill -9 <pid>`

**Lỗi C**: `npm run dev` báo "EACCES" trên macOS/Linux
→ KHÔNG `sudo npm install`. Cấu hình `npm config set prefix` ra `~/.npm-global`.

---

## 6. Code backend

> Từ đây trở đi, mọi file backend nằm dưới `D:\Projects\lottery-checker\backend\LotteryChecker.Api\` (Windows) hoặc `~/Projects/lottery-checker/backend/LotteryChecker.Api/` (macOS). Mỗi sub-section: tạo file → paste code → build → verify.

### 6.1 Models

#### 6.1.1 `Models/LotteryResult.cs`

Entity lưu kết quả xổ số đã quay. Mỗi dòng = 1 số trúng của 1 giải của 1 đài 1 ngày. Với 1 (DrawDate, Province) miền Nam đủ giải sẽ có **1.152 dòng** (xem §6.5 cơ cấu giải).

```csharp
namespace LotteryChecker.Api.Models;

public class LotteryResult
{
    public int Id { get; set; }
    public DateOnly DrawDate { get; set; }
    public string Region { get; set; } = "";        // "MB", "MT", "MN"
    public string Province { get; set; } = "";       // ví dụ "TPHCM"
    public string PrizeTier { get; set; } = "";      // "DB", "1", "2"... "8"
    public string Number { get; set; } = "";         // số trúng (độ dài = số chữ số của giải)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

**Verify**: build → 0 lỗi. Chưa có data, để §8 seed sau.

#### 6.1.2 `Models/TicketInfo.cs`

Output của `OcrService.Extract()`. 3 trường quan trọng đều có thể null (OCR đọc nhầm thì null) — frontend show form pre-filled để user sửa.

```csharp
namespace LotteryChecker.Api.Models;

public class TicketInfo
{
    public string? RawText { get; set; }
    public string? TicketNumber { get; set; }
    public DateOnly? DrawDate { get; set; }
    public string? Province { get; set; }
    public double OcrConfidence { get; set; }
}
```

#### 6.1.3 `Models/WinningPrize.cs`

Mỗi giải trúng = 1 `WinningPrize`. Vé có thể trúng nhiều giải (xem §6.5 quy tắc "lĩnh đủ giá trị các giải") → `ScanResult.Winnings` là list.

```csharp
namespace LotteryChecker.Api.Models;

public record WinningPrize(string TierName, decimal Amount);
```

#### 6.1.4 `Models/ScanResult.cs`

Response của `/api/check`. Đặc biệt 2 field `Winnings` (list) + `TotalPrize` (sum) thay cho `WinningTier`/`PrizeAmount` đơn lẻ ở các template lottery checker khác — vì XSKT Miền Nam cho phép 1 vé trúng nhiều giải cùng lúc (ĐB + giải tám cùng lúc, ví dụ).

```csharp
namespace LotteryChecker.Api.Models;

public class ScanResult
{
    public string ExtractedNumber { get; set; } = "";
    public DateOnly? DrawDate { get; set; }
    public string? Province { get; set; }
    public bool IsWinner { get; set; }
    public List<WinningPrize> Winnings { get; set; } = new();
    public decimal TotalPrize { get; set; }
    public double OcrConfidence { get; set; }
}
```

**Verify §6.1**: `dotnet build` → 0 error, 0 warning.

**Nếu sai**: kiểm tra namespace của các file phải là `LotteryChecker.Api.Models` (không tự sinh ra `LotteryChecker.Api.LotteryChecker.Api.Models` do nhầm root namespace).

### 6.2 DbContext

**File**: `Data/AppDbContext.cs`

```csharp
using LotteryChecker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<LotteryResult> LotteryResults => Set<LotteryResult>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<LotteryResult>(e =>
        {
            e.HasIndex(x => new { x.DrawDate, x.Province });    // index để query nhanh
            e.HasIndex(x => x.Number);                           // index phụ cho dò ngược
            e.Property(x => x.Region).HasMaxLength(8);
            e.Property(x => x.Province).HasMaxLength(32);
            e.Property(x => x.PrizeTier).HasMaxLength(4);
            e.Property(x => x.Number).HasMaxLength(8);
        });
    }
}
```

**Verify**: build OK. Migration sẽ tạo ở §8.

### 6.3 ImagePreprocessor

Tiền xử lý ảnh trước khi OCR: resize (giảm file size), grayscale + contrast + binarize → tăng tỉ lệ Tesseract đọc đúng.

**File**: `Services/ImagePreprocessor.cs`
```csharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LotteryChecker.Api.Services;

public class ImagePreprocessor
{
    public byte[] Preprocess(Stream input)
    {
        using var image = Image.Load<Rgba32>(input);

        // Resize nếu quá to (giữ tỉ lệ, max 1600px chiều dài)
        if (image.Width > 1600)
        {
            var ratio = 1600f / image.Width;
            image.Mutate(x => x.Resize((int)(image.Width * ratio),
                                       (int)(image.Height * ratio)));
        }

        image.Mutate(x => x
            .Grayscale()
            .Contrast(1.3f)
            .BinaryThreshold(0.5f));

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }
}
```

**Verify**: build OK. Sẽ test end-to-end ở §6.7 khi gọi `/api/scan`.

**Nếu sai**: nếu báo lỗi `BinaryThreshold` không tồn tại → check `SixLabors.ImageSharp` version đúng `3.1.5`.

### 6.4 ProvinceMatcher + OcrService

#### 6.4.1 `Services/ProvinceMatcher.cs`

Tesseract đọc nhầm dấu tiếng Việt ("ĐỒNG THÁP" → "DONG THAP" hoặc "BÔNG THÁP"). Match mềm dẻo bằng Levenshtein distance trên chuỗi đã bỏ dấu.

```csharp
using System.Globalization;
using System.Text;

namespace LotteryChecker.Api.Services;

public class ProvinceMatcher
{
    private static readonly Dictionary<string, string> Provinces = new()
    {
        // Miền Nam (21 tỉnh, áp dụng cơ cấu giải chung)
        {"tphcm", "TPHCM"}, {"tp hcm", "TPHCM"}, {"ho chi minh", "TPHCM"},
        {"dong thap", "DongThap"}, {"ca mau", "CaMau"}, {"ben tre", "BenTre"},
        {"vung tau", "VungTau"}, {"bac lieu", "BacLieu"}, {"dong nai", "DongNai"},
        {"can tho", "CanTho"}, {"soc trang", "SocTrang"}, {"tay ninh", "TayNinh"},
        {"an giang", "AnGiang"}, {"binh thuan", "BinhThuan"}, {"vinh long", "VinhLong"},
        {"binh duong", "BinhDuong"}, {"tra vinh", "TraVinh"}, {"long an", "LongAn"},
        {"hau giang", "HauGiang"}, {"kien giang", "KienGiang"}, {"tien giang", "TienGiang"},
        {"da lat", "DaLat"}, {"lam dong", "LamDong"},
        // Miền Trung
        {"phu yen", "PhuYen"}, {"hue", "Hue"}, {"thua thien hue", "Hue"},
        {"dak lak", "DakLak"}, {"daklak", "DakLak"}, {"quang nam", "QuangNam"},
        {"khanh hoa", "KhanhHoa"}, {"da nang", "DaNang"}, {"binh dinh", "BinhDinh"},
        {"quang tri", "QuangTri"}, {"quang binh", "QuangBinh"}, {"gia lai", "GiaLai"},
        {"ninh thuan", "NinhThuan"}, {"kon tum", "KonTum"}, {"quang ngai", "QuangNgai"},
        // Miền Bắc (chỉ 1 đài chung) — KHÔNG dùng cơ cấu MN, xem §11
        {"mien bac", "MB"}, {"mb", "MB"}, {"ha noi", "MB"}, {"hanoi", "MB"},
    };

    public static IReadOnlyCollection<string> AllCodes => Provinces.Values.Distinct().ToArray();

    public string? FindBestMatch(string ocrText)
    {
        var normalized = RemoveDiacritics(ocrText).ToLowerInvariant();
        foreach (var (key, code) in Provinces)
        {
            if (normalized.Contains(key)) return code;
        }

        // Fallback: thử fuzzy với từng tỉnh (Levenshtein ≤ 2)
        foreach (var (key, code) in Provinces)
        {
            foreach (var word in normalized.Split(new[] { ' ', '\n', '\t' },
                                                  StringSplitOptions.RemoveEmptyEntries))
            {
                if (Levenshtein(word, key) <= 2 && key.Length >= 5) return code;
            }
        }
        return null;
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Replace('đ', 'd').Replace('Đ', 'D')
                 .Normalize(NormalizationForm.FormC);
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                                   d[i - 1, j - 1] + cost);
            }
        return d[a.Length, b.Length];
    }
}
```

#### 6.4.2 `Services/OcrService.cs`

OCR toàn ảnh với `vie+eng` (KHÔNG whitelist số — cần chữ để tìm tên đài + ngày), regex tìm số 6 chữ số, regex/format tìm ngày, ProvinceMatcher tìm đài.

```csharp
using System.Text.RegularExpressions;
using LotteryChecker.Api.Models;
using Tesseract;

namespace LotteryChecker.Api.Services;

public class OcrService
{
    private readonly string _tessDataPath;
    private readonly ProvinceMatcher _provinces;

    public OcrService(IConfiguration config, ProvinceMatcher provinces)
    {
        _tessDataPath = config["Tesseract:DataPath"] ?? "./tessdata";
        _provinces = provinces;
    }

    public TicketInfo Extract(byte[] imageBytes)
    {
        using var engine = new TesseractEngine(_tessDataPath, "vie+eng", EngineMode.Default);
        using var img = Pix.LoadFromMemory(imageBytes);
        using var page = engine.Process(img);

        var text = page.GetText();
        var confidence = page.GetMeanConfidence();

        return new TicketInfo
        {
            RawText = text,
            TicketNumber = ExtractTicketNumber(text),
            DrawDate = ExtractDate(text),
            Province = _provinces.FindBestMatch(text),
            OcrConfidence = confidence
        };
    }

    // Số vé: 6 chữ số liên tục. Heuristic loại 19xx/20xx (đó là năm).
    private static string? ExtractTicketNumber(string text)
    {
        var candidates = Regex.Matches(text, @"\b\d{6}\b")
                              .Select(m => m.Value)
                              .Distinct()
                              .ToList();
        if (candidates.Count == 0) return null;
        return candidates.FirstOrDefault(c => !c.StartsWith("19") && !c.StartsWith("20"))
               ?? candidates.First();
    }

    // Ngày: 28-05-2026, 28/05/2026, 28.05.2026, "ngày 28 tháng 5 năm 2026"
    private static DateOnly? ExtractDate(string text)
    {
        var m1 = Regex.Match(text, @"(\d{1,2})[-/.](\d{1,2})[-/.](\d{4})");
        if (m1.Success && TryBuildDate(m1.Groups[1].Value, m1.Groups[2].Value,
                                       m1.Groups[3].Value, out var d1))
            return d1;

        var m2 = Regex.Match(text,
            @"ng[àa]y\s*(\d{1,2}).*?th[áa]ng\s*(\d{1,2}).*?n[ăa]m\s*(\d{4})",
            RegexOptions.IgnoreCase);
        if (m2.Success && TryBuildDate(m2.Groups[1].Value, m2.Groups[2].Value,
                                       m2.Groups[3].Value, out var d2))
            return d2;

        return null;
    }

    private static bool TryBuildDate(string d, string m, string y, out DateOnly result)
    {
        result = default;
        if (int.TryParse(d, out var dd) && int.TryParse(m, out var mm)
            && int.TryParse(y, out var yy)
            && dd is >= 1 and <= 31 && mm is >= 1 and <= 12
            && yy is >= 2020 and <= 2099)
        {
            try { result = new DateOnly(yy, mm, dd); return true; }
            catch { return false; }
        }
        return false;
    }
}
```

**Verify**: build OK. Sẽ test end-to-end ở §6.7.

**Lỗi thường gặp**:
- `Tesseract.TesseractException: Failed to initialise tesseract engine` → kiểm tra `tessdata/vie.traineddata` đã copy sang `bin/Debug/net10.0/tessdata/` chưa. Nếu không → check `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>` trong `.csproj`.
- Tesseract đọc ra text rỗng → ảnh đầu vào quá tối/quá nhỏ → tăng contrast trong `ImagePreprocessor`.

### 6.5 LotteryMatcher — Logic dò giải theo cơ cấu XSKT Miền Nam

> **Cơ cấu giải thưởng chính thức** (1.000.000 vé loại 10.000đ, 06 chữ số, áp dụng chung 21 tỉnh Miền Nam từ Bình Thuận → Cà Mau, từ 01-01-2017).

#### 6.5.1 Bảng giải chính (lưu DB)

| Số lượng | Tên giải | `PrizeTier` | Số chữ số | Tiền thưởng |
|---|---|---|---|---|
| 1 | Giải đặc biệt | `DB` | 6 | 2.000.000.000đ |
| 1 | Giải nhất | `1` | 5 | 30.000.000đ |
| 1 | Giải hai (nhì) | `2` | 5 | 15.000.000đ |
| 2 | Giải ba | `3` | 5 | 10.000.000đ |
| 7 | Giải bốn (tư) | `4` | 5 | 3.000.000đ |
| 10 | Giải năm | `5` | 4 | 1.000.000đ |
| 30 | Giải sáu | `6` | 4 | 400.000đ |
| 100 | Giải bảy | `7` | 3 | 200.000đ |
| 1.000 | Giải tám | `8` | 2 | 100.000đ |

**Tổng mỗi (DrawDate, Province)** = 1+1+1+2+7+10+30+100+1000 = **1.152 dòng** trong `LotteryResults`. Scraper §6.8 và Seed §8 phải tạo đủ.

#### 6.5.2 Giải phụ (KHÔNG lưu DB — suy ra từ số ĐB)

| Loại | Số lượng | Điều kiện match với ĐB (6 chữ số) | Tiền thưởng |
|---|---|---|---|
| Giải Phụ đặc biệt | 9 | `ticket[1..5] == DB[1..5]` AND `ticket[0] != DB[0]` (5 số cuối khớp, sai duy nhất chữ số đầu = "hàng trăm ngàn") | 50.000.000đ |
| Giải Khuyến khích | 45 | `ticket[0] == DB[0]` AND đúng **1** vị trí trong `ticket[1..5]` khác `DB[1..5]` (chữ số đầu khớp, sai 1 trong 5 chữ số còn lại) | 6.000.000đ |

> Kiểm tra số lượng: 9 = 10 chữ số có thể của vị trí 0 trừ 1 chữ số đúng. 45 = 5 vị trí × 9 chữ số sai khác. Khớp đúng đề bài.

#### 6.5.3 Quy tắc khớp giải 1–8

Số ghi trong `LotteryResult.Number` có độ dài = "số chữ số" của giải đó (cột 4 bảng trên). Logic match:

```
ticket.Substring(ticket.Length - r.Number.Length) == r.Number
```

Tức: lấy N chữ số cuối của vé (N = `Number.Length`) so với số trúng. Không cần switch theo tier — độ dài của `Number` đã encode quy tắc.

#### 6.5.4 Quy tắc tổng hợp

- **"Vé trúng nhiều giải được lĩnh đủ giá trị các giải"** → matcher trả **list** tất cả giải trúng, KHÔNG `return` sớm.
- Trúng ĐB → bỏ qua Phụ ĐB / Khuyến khích (ĐB là superset). Nhưng vẫn xét giải 1–8 song song (ví dụ ĐB=`123456` và số trúng giải tám=`56` → vé `123456` trúng cả ĐB lẫn giải tám).
- Phụ ĐB và Khuyến khích loại trừ lẫn nhau theo định nghĩa (chữ số đầu khác / khớp).

#### 6.5.5 `Services/LotteryMatcher.cs`

```csharp
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Services;

public class LotteryMatcher
{
    private readonly AppDbContext _db;

    public LotteryMatcher(AppDbContext db) => _db = db;

    public async Task<ScanResult> Match(string ticket, DateOnly date, string province, CancellationToken ct)
    {
        var results = await _db.LotteryResults
            .Where(r => r.DrawDate == date && r.Province == province)
            .ToListAsync(ct);

        if (results.Count == 0)
            return new ScanResult
            {
                ExtractedNumber = ticket,
                DrawDate = date,
                Province = province,
                IsWinner = false
            };

        var winnings = new List<WinningPrize>();

        // 1. Giải ĐB (exact 6-digit) — và 2 giải phụ suy ra từ ĐB
        var db = results.FirstOrDefault(r => r.PrizeTier == "DB");
        if (db is { Number.Length: 6 } && ticket.Length == 6)
        {
            if (db.Number == ticket)
            {
                winnings.Add(new WinningPrize("Giải Đặc Biệt", 2_000_000_000m));
                // Trúng ĐB rồi thì KHÔNG xét Phụ ĐB / Khuyến khích nữa
            }
            else if (ticket[1..] == db.Number[1..] && ticket[0] != db.Number[0])
            {
                winnings.Add(new WinningPrize("Giải Phụ Đặc Biệt", 50_000_000m));
            }
            else if (ticket[0] == db.Number[0])
            {
                int diffCount = 0;
                for (int i = 1; i < 6; i++)
                    if (ticket[i] != db.Number[i]) diffCount++;
                if (diffCount == 1)
                    winnings.Add(new WinningPrize("Giải Khuyến Khích", 6_000_000m));
            }
        }

        // 2. Giải 1–8: so N chữ số cuối với mỗi số trúng. KHÔNG return — collect đủ.
        foreach (var r in results.Where(x => x.PrizeTier != "DB"))
        {
            if (r.Number.Length > ticket.Length) continue;
            var lastN = ticket[^r.Number.Length..];
            if (lastN == r.Number)
            {
                winnings.Add(new WinningPrize(GetTierName(r.PrizeTier),
                                              GetPrizeAmount(r.PrizeTier)));
            }
        }

        return new ScanResult
        {
            ExtractedNumber = ticket,
            DrawDate = date,
            Province = province,
            IsWinner = winnings.Count > 0,
            Winnings = winnings,
            TotalPrize = winnings.Sum(w => w.Amount)
        };
    }

    private static string GetTierName(string tier) => tier switch
    {
        "1" => "Giải Nhất", "2" => "Giải Nhì", "3" => "Giải Ba", "4" => "Giải Tư",
        "5" => "Giải Năm", "6" => "Giải Sáu", "7" => "Giải Bảy", "8" => "Giải Tám",
        _   => $"Giải {tier}"
    };

    private static decimal GetPrizeAmount(string tier) => tier switch
    {
        "1" => 30_000_000m, "2" => 15_000_000m, "3" => 10_000_000m, "4" => 3_000_000m,
        "5" => 1_000_000m,  "6" => 400_000m,    "7" => 200_000m,    "8" => 100_000m,
        _   => 0m
    };
}
```

#### 6.5.6 Unit tests bắt buộc cho LotteryMatcher

Tạo project test (1 lần duy nhất):
```powershell
cd D:\Projects\lottery-checker\backend
dotnet new xunit -n LotteryChecker.Tests
dotnet sln add LotteryChecker.Tests/LotteryChecker.Tests.csproj
cd LotteryChecker.Tests
dotnet add reference ..\LotteryChecker.Api\LotteryChecker.Api.csproj
dotnet add package Microsoft.EntityFrameworkCore.InMemory --version 10.0.8
dotnet add package FluentAssertions --version 6.12.0
```

**File**: `LotteryChecker.Tests/LotteryMatcherTests.cs`

```csharp
using FluentAssertions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LotteryChecker.Tests;

public class LotteryMatcherTests
{
    private static AppDbContext NewDb()
    {
        var opt = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(opt);
    }

    private static async Task SeedAsync(AppDbContext db, DateOnly date, string province,
        string dbNumber, string giai8Number)
    {
        db.LotteryResults.Add(new LotteryResult
            { DrawDate = date, Province = province, PrizeTier = "DB", Number = dbNumber });
        db.LotteryResults.Add(new LotteryResult
            { DrawDate = date, Province = province, PrizeTier = "8", Number = giai8Number });
        await db.SaveChangesAsync();
    }

    [Fact(DisplayName = "1. ĐB exact match → đúng 2 tỷ")]
    public async Task DB_Exact_Returns_2Billion()
    {
        var db = NewDb();
        var date = new DateOnly(2026, 6, 2);
        await SeedAsync(db, date, "TPHCM", "123456", "99");

        var r = await new LotteryMatcher(db).Match("123456", date, "TPHCM", CancellationToken.None);

        r.IsWinner.Should().BeTrue();
        r.Winnings.Should().Contain(w => w.TierName == "Giải Đặc Biệt" && w.Amount == 2_000_000_000m);
    }

    [Fact(DisplayName = "2. Phụ ĐB (5 số cuối khớp, sai chữ số đầu) → 50tr, KHÔNG trúng ĐB")]
    public async Task PhuDB_Returns_50Million()
    {
        var db = NewDb();
        var date = new DateOnly(2026, 6, 2);
        await SeedAsync(db, date, "TPHCM", "123456", "99");

        var r = await new LotteryMatcher(db).Match("923456", date, "TPHCM", CancellationToken.None);

        r.Winnings.Should().ContainSingle(w => w.TierName == "Giải Phụ Đặc Biệt" && w.Amount == 50_000_000m);
        r.Winnings.Should().NotContain(w => w.TierName == "Giải Đặc Biệt");
    }

    [Fact(DisplayName = "3. Khuyến khích (sai 1 vị trí trong [1..5]) → 6tr")]
    public async Task KhuyenKhich_Returns_6Million()
    {
        var db = NewDb();
        var date = new DateOnly(2026, 6, 2);
        await SeedAsync(db, date, "TPHCM", "123456", "99");

        // Sai vị trí thứ 3 (4 thành 9): 123956 vs 123456
        var r = await new LotteryMatcher(db).Match("123956", date, "TPHCM", CancellationToken.None);

        r.Winnings.Should().ContainSingle(w => w.TierName == "Giải Khuyến Khích" && w.Amount == 6_000_000m);
    }

    [Fact(DisplayName = "4. Sai 2 vị trí → KHÔNG trúng Khuyến khích (regression)")]
    public async Task TwoDigitsDiff_NotKhuyenKhich()
    {
        var db = NewDb();
        var date = new DateOnly(2026, 6, 2);
        await SeedAsync(db, date, "TPHCM", "123456", "99");

        // 199956 vs 123456 — chữ số đầu khớp, sai 3 vị trí
        var r = await new LotteryMatcher(db).Match("199956", date, "TPHCM", CancellationToken.None);

        r.Winnings.Should().NotContain(w => w.TierName == "Giải Khuyến Khích");
        r.Winnings.Should().NotContain(w => w.TierName == "Giải Phụ Đặc Biệt");
    }

    [Fact(DisplayName = "5. Trúng ĐB + Giải Tám cùng lúc → tổng = 2 tỷ + 100k")]
    public async Task DB_Plus_Giai8_Stacks()
    {
        var db = NewDb();
        var date = new DateOnly(2026, 6, 2);
        // ĐB=123456, Giải 8=56 → vé 123456 trúng cả 2
        await SeedAsync(db, date, "TPHCM", "123456", "56");

        var r = await new LotteryMatcher(db).Match("123456", date, "TPHCM", CancellationToken.None);

        r.Winnings.Should().HaveCount(2);
        r.TotalPrize.Should().Be(2_000_100_000m);
    }
}
```

**Verify**:
```powershell
cd D:\Projects\lottery-checker\backend
dotnet test
```
Phải thấy `Passed: 5, Failed: 0`.

**Nếu sai**:
- Test #2 fail → check logic Phụ ĐB: phải dùng `ticket[1..]` (slice từ index 1, KHÔNG phải `ticket[0..1]`).
- Test #4 fail → vòng `for (int i = 1; i < 6; i++)` chỉ đếm 5 vị trí từ index 1, KHÔNG bao gồm index 0.

### 6.6 ScanController — 2 endpoint

**File**: `Controllers/ScanController.cs`

Endpoint `POST /api/scan` chỉ nhận **ảnh thô**, trả về thông tin đã OCR (chưa dò). Endpoint `POST /api/check` nhận info đã user xác nhận, thực hiện đối chiếu. Tách 2 endpoint để user có cơ hội sửa nếu OCR đọc nhầm, đồng thời `/api/check` không tốn CPU OCR nếu user chỉ chỉnh số.

```csharp
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LotteryChecker.Api.Controllers;

[ApiController]
public class ScanController : ControllerBase
{
    private readonly ImagePreprocessor _preprocessor;
    private readonly OcrService _ocr;
    private readonly LotteryMatcher _matcher;

    public ScanController(ImagePreprocessor p, OcrService o, LotteryMatcher m)
    {
        _preprocessor = p; _ocr = o; _matcher = m;
    }

    /// <summary>Bước 1: gửi ảnh → trả info đã OCR (số vé, ngày, đài).</summary>
    [HttpPost("/api/scan")]
    [RequestSizeLimit(10_000_000)]
    public IActionResult Scan(IFormFile image)
    {
        if (image == null || image.Length == 0)
            return BadRequest(new { error = "Chưa có ảnh" });

        using var stream = image.OpenReadStream();
        var processed = _preprocessor.Preprocess(stream);
        var info = _ocr.Extract(processed);

        var lowConfidence = info.OcrConfidence < 0.55;

        return Ok(new
        {
            ticketNumber = info.TicketNumber,
            drawDate = info.DrawDate?.ToString("yyyy-MM-dd"),
            province = info.Province,
            confidence = info.OcrConfidence,
            lowConfidence,
            allProvinces = info.Province == null ? ProvinceMatcher.AllCodes : null,
            warning = BuildWarning(info)
        });
    }

    /// <summary>Bước 2: user bấm "Dò" với info đã xác nhận/chỉnh sửa.</summary>
    [HttpPost("/api/check")]
    public async Task<IActionResult> Check([FromBody] CheckRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.TicketNumber)
            || req.TicketNumber.Length != 6
            || !req.TicketNumber.All(char.IsDigit))
            return BadRequest(new { error = "Số vé phải là 6 chữ số" });

        var result = await _matcher.Match(req.TicketNumber, req.DrawDate, req.Province, ct);
        return Ok(result);
    }

    private static string? BuildWarning(TicketInfo i)
    {
        var missing = new List<string>();
        if (i.TicketNumber == null) missing.Add("số vé");
        if (i.DrawDate == null)     missing.Add("ngày mở thưởng");
        if (i.Province == null)     missing.Add("đài");
        return missing.Count > 0
            ? $"Không tự đọc được: {string.Join(", ", missing)}. Vui lòng kiểm tra/điền tay."
            : null;
    }
}

public record CheckRequest(string TicketNumber, DateOnly DrawDate, string Province);
```

**Verify**: build OK. Test thực tế sau khi `Program.cs` register DI (§6.9) và seed data (§8).

### 6.7 ResultScraper — Cào 1.152 số mỗi đài mỗi ngày

> **Quan trọng**: Code minh hoạ scraper trong các template lottery checker hay chỉ cào 9 giải (1 số mỗi giải) — đó là SAI với XSKT VN. Mỗi đài có 1+1+1+2+7+10+30+100+1000 = **1.152 số** trúng cần lưu. Scraper phải đếm và sanity-check để khỏi miss giải 8 (1000 số) nếu DOM minhngoc đổi.

**File**: `Services/ResultScraper.cs`

```csharp
using HtmlAgilityPack;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;

namespace LotteryChecker.Api.Services;

public class ResultScraper
{
    private readonly AppDbContext _db;
    private readonly ILogger<ResultScraper> _logger;
    private readonly HttpClient _http;

    // Số chữ số chuẩn của từng giải MN/MT
    private static readonly Dictionary<string, int> ExpectedCounts = new()
    {
        { "DB", 1 }, { "1", 1 }, { "2", 1 }, { "3", 2 }, { "4", 7 },
        { "5", 10 }, { "6", 30 }, { "7", 100 }, { "8", 1000 }
    };
    private const int TotalExpected = 1 + 1 + 1 + 2 + 7 + 10 + 30 + 100 + 1000; // = 1152

    public ResultScraper(AppDbContext db, ILogger<ResultScraper> logger, HttpClient http)
    {
        _db = db; _logger = logger; _http = http;
    }

    /// <summary>Cào kết quả 1 đài MN cho 1 ngày. Idempotent (xoá data cũ trước khi insert).</summary>
    public async Task<int> FetchProvince(DateOnly date, string provinceSlug, string provinceCode,
                                          CancellationToken ct)
    {
        var url = $"https://www.minhngoc.net.vn/ket-qua-xo-so/mien-nam/{provinceSlug}.html";
        var html = await _http.GetStringAsync(url, ct);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var rows = new List<LotteryResult>();
        foreach (var (tier, expected) in ExpectedCounts)
        {
            // Selector minhngoc: <td class="giaidb">, <td class="giai1">..."giai8"
            var cssClass = tier == "DB" ? "giaidb" : $"giai{tier}";
            var nodes = doc.DocumentNode.SelectNodes($"//td[contains(@class, '{cssClass}')]");
            if (nodes == null)
            {
                _logger.LogWarning("Không tìm thấy node CSS {Class} ở {Url}", cssClass, url);
                continue;
            }

            var numbers = nodes
                .SelectMany(n => n.InnerText.Split(new[] { ' ', '\t', '\n', '\r' },
                                                    StringSplitOptions.RemoveEmptyEntries))
                .Where(s => s.All(char.IsDigit) && s.Length is >= 2 and <= 6)
                .ToList();

            foreach (var num in numbers)
            {
                rows.Add(new LotteryResult
                {
                    DrawDate = date,
                    Region = "MN",
                    Province = provinceCode,
                    PrizeTier = tier,
                    Number = num
                });
            }
        }

        // Sanity check: phải đúng 1152 dòng. Nếu thiếu → log warning + KHÔNG ghi DB.
        if (rows.Count != TotalExpected)
        {
            _logger.LogError(
                "Cào {Province} ngày {Date} chỉ ra {Got}/{Expected} số. KHÔNG lưu DB. URL={Url}",
                provinceCode, date, rows.Count, TotalExpected, url);
            return 0;
        }

        // Xoá data cũ (nếu chạy lại) — idempotent
        var existing = _db.LotteryResults
            .Where(r => r.DrawDate == date && r.Province == provinceCode);
        _db.LotteryResults.RemoveRange(existing);

        _db.LotteryResults.AddRange(rows);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Cào {Province} {Date}: lưu {Count} dòng OK",
                               provinceCode, date, rows.Count);
        return rows.Count;
    }
}
```

**Verify**: build OK. End-to-end test sẽ qua worker §6.8 hoặc gọi tay từ controller.

**Lỗi thường gặp**:
- HTTP 403/404 từ minhngoc → trang đổi đường dẫn slug. Mở browser kiểm tra URL thật.
- Số dòng < 1152 → DOM đổi class CSS. Inspect element trang minhngoc, update tên class.
- TooManyRequests → thêm delay giữa các tỉnh (Task.Delay 2-5s).

### 6.8 Worker tự động cào kết quả

**File**: `Workers/DailyResultFetchWorker.cs`

```csharp
using LotteryChecker.Api.Services;

namespace LotteryChecker.Api.Workers;

public class DailyResultFetchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyResultFetchWorker> _logger;

    public DailyResultFetchWorker(IServiceScopeFactory scopeFactory,
                                  ILogger<DailyResultFetchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // Lịch quay XSKT MN: mỗi ngày có 3-4 đài (xem https://xoso.com.vn/lich-mo-thuong)
    // Hard-code mapping tối giản cho MVP — production lưu trong DB hoặc config.
    private static readonly (DayOfWeek day, string code, string slug)[] Schedule =
    {
        (DayOfWeek.Monday,    "TPHCM",    "tp-ho-chi-minh"),
        (DayOfWeek.Monday,    "DongThap", "dong-thap"),
        (DayOfWeek.Monday,    "CaMau",    "ca-mau"),
        (DayOfWeek.Tuesday,   "BenTre",   "ben-tre"),
        (DayOfWeek.Tuesday,   "VungTau",  "vung-tau"),
        (DayOfWeek.Tuesday,   "BacLieu",  "bac-lieu"),
        // ... thêm đủ lịch tuần (xem §11)
    };

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Chạy mỗi ngày lúc 19:00 (sau khi quay xong cả 3 miền — quay 16:15-16:30)
            var now = DateTime.Now;
            var nextRun = now.Date.AddHours(19);
            if (now > nextRun) nextRun = nextRun.AddDays(1);

            try { await Task.Delay(nextRun - now, ct); }
            catch (TaskCanceledException) { return; }

            try
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                var todays = Schedule.Where(s => s.day == DateTime.Now.DayOfWeek).ToArray();

                using var scope = _scopeFactory.CreateScope();
                var scraper = scope.ServiceProvider.GetRequiredService<ResultScraper>();

                foreach (var (_, code, slug) in todays)
                {
                    await scraper.FetchProvince(today, slug, code, ct);
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);  // tránh rate limit
                }

                _logger.LogInformation("Worker: cào xong {Count} đài cho {Date}",
                                       todays.Length, today);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker: lỗi khi cào kết quả");
            }
        }
    }
}
```

**Verify**: build OK. Khi backend chạy, đợi đến 19h hoặc force gọi `ResultScraper.FetchProvince()` từ endpoint admin để test.

### 6.9 `Program.cs` — DI registration

**File**: `Program.cs` (thay toàn bộ)

```csharp
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Services;
using LotteryChecker.Api.Workers;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// EF Core + SQLite
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// Services
builder.Services.AddScoped<ImagePreprocessor>();
builder.Services.AddScoped<OcrService>();
builder.Services.AddScoped<LotteryMatcher>();
builder.Services.AddScoped<ResultScraper>();
builder.Services.AddSingleton<ProvinceMatcher>();
builder.Services.AddHttpClient<ResultScraper>();
builder.Services.AddHostedService<DailyResultFetchWorker>();

// CORS
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

// Controllers + OpenAPI (built-in .NET 10, không cần Swashbuckle)
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Auto-migrate + seed cho dev
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbCtx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbCtx.Database.Migrate();
    await SeedData.SeedIfEmptyAsync(dbCtx);   // xem §8

    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors();
app.MapControllers();

app.MapGet("/",       () => "Lottery Checker API is running. Try /scalar/v1");
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

app.Run();
```

**File**: `appsettings.json`
```json
{
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": { "Default": "Data Source=lottery.db" },
  "Tesseract": { "DataPath": "./tessdata" },
  "Cors": {
    "AllowedOrigins": [ "http://localhost:5173", "http://localhost:3000" ]
  }
}
```

**File**: `appsettings.Production.json` (commit, không chứa secret)
```json
{
  "Cors": {
    "AllowedOrigins": [ "https://your-frontend.pages.dev" ]
  }
}
```

**Verify**:
```powershell
cd D:\Projects\lottery-checker\backend\LotteryChecker.Api
dotnet build
dotnet run
```
- Log thấy `Now listening on: http://localhost:5000`
- `http://localhost:5000` → "Lottery Checker API is running..."
- `http://localhost:5000/health` → JSON `{"status":"ok",...}`
- `http://localhost:5000/scalar/v1` → Scalar UI có sidebar với `POST /api/scan` và `POST /api/check`

### 6.10 File `api-tests.http` để test 2 endpoint

**File**: `LotteryChecker.Api/api-tests.http`

```http
### Health
GET http://localhost:5000/health

### Ping
GET http://localhost:5000/api/ping

### Scan ảnh vé (cần có file ảnh test sẵn)
# POST http://localhost:5000/api/scan
# Content-Type: multipart/form-data; boundary=---boundary
#
# -----boundary
# Content-Disposition: form-data; name="image"; filename="ticket.jpg"
# Content-Type: image/jpeg
#
# < ./test-images/sample-ticket.jpg
# -----boundary--

### Check với info đã xác nhận
POST http://localhost:5000/api/check
Content-Type: application/json

{
  "ticketNumber": "123456",
  "drawDate": "2026-06-02",
  "province": "TPHCM"
}

### Response mẫu khi trúng cả ĐB + Giải Tám:
# {
#   "extractedNumber": "123456",
#   "drawDate": "2026-06-02",
#   "province": "TPHCM",
#   "isWinner": true,
#   "winnings": [
#     { "tierName": "Giải Đặc Biệt", "amount": 2000000000 },
#     { "tierName": "Giải Tám",      "amount": 100000 }
#   ],
#   "totalPrize": 2000100000,
#   "ocrConfidence": 0
# }
```

Mở file trong VS Code (đã cài extension REST Client ở §4.5), bấm "Send Request" ngay trên dòng `POST`.

### 6.11 Commit checkpoint §6

```powershell
cd D:\Projects\lottery-checker
git add .
git commit -m "feat(backend): OCR + ProvinceMatcher + LotteryMatcher với cơ cấu giải MN"
```

---

## 7. Code frontend

> Mọi file frontend nằm dưới `frontend/src/`. State machine 3 stage (capture → confirm → result) đã mô tả ở §1.

### 7.1 `src/api/client.ts` — Axios client

```ts
import axios from 'axios'

const api = axios.create({ baseURL: import.meta.env.VITE_API_URL })

// Bước 1: upload ảnh → nhận info OCR
export async function scanImage(blob: Blob) {
  const fd = new FormData()
  fd.append('image', blob, 'ticket.jpg')
  const { data } = await api.post('/api/scan', fd,
    { headers: { 'Content-Type': 'multipart/form-data' } })
  return data as {
    ticketNumber: string | null
    drawDate: string | null
    province: string | null
    confidence: number
    lowConfidence: boolean
    allProvinces: string[] | null
    warning: string | null
  }
}

// Bước 2: dò với info đã xác nhận
export async function checkTicket(payload: {
  ticketNumber: string
  drawDate: string
  province: string
}) {
  const { data } = await api.post('/api/check', payload)
  return data as {
    extractedNumber: string
    drawDate: string | null
    province: string | null
    isWinner: boolean
    winnings: { tierName: string; amount: number }[]
    totalPrize: number
    ocrConfidence: number
  }
}
```

**Verify**: TypeScript compile OK (`tsc --noEmit` chạy ngầm khi `npm run dev`).

### 7.2 `src/components/CameraCapture.tsx`

```tsx
import Webcam from 'react-webcam'
import { useRef, useCallback } from 'react'

export default function CameraCapture({ onCapture }: { onCapture: (blob: Blob) => void }) {
  const webcamRef = useRef<Webcam>(null)

  const capture = useCallback(async () => {
    const screenshot = webcamRef.current?.getScreenshot()
    if (!screenshot) return
    const res = await fetch(screenshot)
    const blob = await res.blob()
    onCapture(blob)
  }, [onCapture])

  return (
    <div className="relative">
      <Webcam
        ref={webcamRef}
        screenshotFormat="image/jpeg"
        videoConstraints={{ facingMode: 'environment' }}
        className="w-full rounded-lg"
      />
      {/* Khung hướng dẫn căn vé */}
      <div className="absolute inset-x-8 top-1/2 -translate-y-1/2 h-32
                      border-4 border-yellow-400 rounded pointer-events-none" />
      <button onClick={capture}
              className="mt-4 w-full bg-blue-600 text-white py-3 rounded-lg">
        📷 Chụp vé
      </button>
    </div>
  )
}
```

**Verify**: vào `http://localhost:5173` (sau khi gắn vào Home ở §7.6) → browser xin permission camera → thấy live preview + khung vàng + nút "Chụp vé".

**Lỗi thường gặp**: Browser block camera trên HTTP. Vẫn được trên `localhost`, nhưng nếu test qua IP LAN (điện thoại) phải dùng HTTPS — xem §9 hoặc dùng `ngrok` tạm.

### 7.3 `src/components/ImageUpload.tsx`

```tsx
export default function ImageUpload({ onSelect }: { onSelect: (f: File) => void }) {
  return (
    <label className="block border-2 border-dashed p-8 rounded-lg text-center cursor-pointer">
      <input type="file" accept="image/*" capture="environment"
             onChange={e => e.target.files && onSelect(e.target.files[0])}
             className="hidden" />
      <span>📁 Chọn ảnh hoặc chụp từ máy</span>
    </label>
  )
}
```

> Attribute `capture="environment"` giúp mobile mở camera sau trực tiếp khi tap.

### 7.4 `src/components/TicketInfoConfirm.tsx`

```tsx
import { useState } from 'react'

type Props = {
  scanned: {
    ticketNumber: string | null
    drawDate: string | null
    province: string | null
    confidence: number
    lowConfidence: boolean
    warning?: string | null
  }
  allProvinces: { code: string; name: string }[]
  onConfirm: (data: { ticketNumber: string; drawDate: string; province: string }) => void
  onRescan: () => void
}

export default function TicketInfoConfirm({ scanned, allProvinces, onConfirm, onRescan }: Props) {
  const [ticket, setTicket] = useState(scanned.ticketNumber ?? '')
  const [date, setDate] = useState(scanned.drawDate ?? new Date().toISOString().slice(0, 10))
  const [province, setProvince] = useState(scanned.province ?? '')

  const fieldClass = (missing: boolean) =>
    `w-full p-3 border rounded-lg ${missing ? 'border-red-400 bg-red-50' : 'border-gray-300'}`

  return (
    <div className="space-y-4 p-4">
      {scanned.warning && (
        <div className="bg-yellow-50 border border-yellow-300 p-3 rounded text-sm">
          ⚠️ {scanned.warning}
        </div>
      )}

      <label className="block">
        <span className="text-sm text-gray-600">Số vé (6 chữ số)</span>
        <input value={ticket}
               onChange={e => setTicket(e.target.value.replace(/\D/g, '').slice(0, 6))}
               className={fieldClass(!scanned.ticketNumber)}
               inputMode="numeric" placeholder="VD: 123456" />
      </label>

      <label className="block">
        <span className="text-sm text-gray-600">Ngày mở thưởng</span>
        <input type="date" value={date} onChange={e => setDate(e.target.value)}
               className={fieldClass(!scanned.drawDate)} />
      </label>

      <label className="block">
        <span className="text-sm text-gray-600">Đài</span>
        <select value={province} onChange={e => setProvince(e.target.value)}
                className={fieldClass(!scanned.province)}>
          <option value="">-- Chọn đài --</option>
          {allProvinces.map(p => (
            <option key={p.code} value={p.code}>{p.name}</option>
          ))}
        </select>
      </label>

      <div className="text-xs text-gray-500">
        Độ tin cậy OCR: {Math.round(scanned.confidence * 100)}%
      </div>

      <div className="flex gap-2">
        <button onClick={onRescan}
                className="flex-1 border border-gray-300 py-3 rounded-lg">
          📷 Chụp lại
        </button>
        <button
          onClick={() => onConfirm({ ticketNumber: ticket, drawDate: date, province })}
          disabled={!ticket || ticket.length !== 6 || !province}
          className="flex-1 bg-blue-600 text-white py-3 rounded-lg disabled:bg-gray-300">
          ✅ Dò ngay
        </button>
      </div>
    </div>
  )
}
```

### 7.5 `src/components/ResultDisplay.tsx` — Render LIST giải trúng

> **Khác biệt quan trọng với template lottery checker thông thường**: response `/api/check` trả `winnings` là **list** (có thể 0, 1, hoặc nhiều giải), KHÔNG phải 1 giải đơn lẻ. Vì XSKT MN cho phép 1 vé trúng nhiều giải cùng lúc (ĐB + giải tám chẳng hạn).

```tsx
type Winning = { tierName: string; amount: number }

type Props = {
  result: {
    extractedNumber: string
    drawDate: string | null
    province: string | null
    isWinner: boolean
    winnings: Winning[]
    totalPrize: number
  }
  onRescan: () => void
}

const formatVND = (n: number) =>
  n.toLocaleString('vi-VN', { style: 'currency', currency: 'VND', maximumFractionDigits: 0 })

export default function ResultDisplay({ result, onRescan }: Props) {
  const { isWinner, winnings, totalPrize, extractedNumber, drawDate, province } = result

  return (
    <div className="p-4 space-y-4">
      <div className="bg-white rounded-2xl shadow p-5 text-center">
        <div className="text-sm text-gray-500">Vé số</div>
        <div className="text-3xl font-bold tracking-widest my-1">{extractedNumber}</div>
        <div className="text-sm text-gray-500">
          {province} — {drawDate}
        </div>
      </div>

      {isWinner ? (
        <>
          <div className="bg-green-50 border border-green-300 rounded-2xl p-5 text-center">
            <div className="text-green-700 font-medium mb-1">🎉 Chúc mừng! Vé trúng:</div>
            <div className="text-4xl font-bold text-green-700">{formatVND(totalPrize)}</div>
            {winnings.length > 1 && (
              <div className="text-xs text-green-700/70 mt-2">
                ({winnings.length} giải cộng dồn)
              </div>
            )}
          </div>

          <ul className="bg-white rounded-2xl shadow divide-y">
            {winnings.map((w, i) => (
              <li key={i} className="flex items-center justify-between p-4">
                <span className="font-medium">{w.tierName}</span>
                <span className="text-brand-600 font-semibold">{formatVND(w.amount)}</span>
              </li>
            ))}
          </ul>
        </>
      ) : (
        <div className="bg-gray-50 border border-gray-200 rounded-2xl p-6 text-center">
          <div className="text-2xl mb-2">😔</div>
          <div className="font-medium">Tiếc quá, vé không trúng giải nào</div>
          <div className="text-sm text-gray-500 mt-1">Chúc bạn may mắn lần sau!</div>
        </div>
      )}

      <button onClick={onRescan}
              className="w-full bg-blue-600 text-white py-3 rounded-lg">
        🔄 Dò vé khác
      </button>
    </div>
  )
}
```

**Verify**: sau khi seed data ở §8 và gọi `/api/check` với vé khớp ĐB, frontend phải hiển thị list 1 giải; với vé khớp cả ĐB + giải tám phải hiển thị list 2 giải + tổng cộng dồn.

### 7.6 `src/pages/Home.tsx` — State machine 3 stage

```tsx
import { useState } from 'react'
import CameraCapture from '../components/CameraCapture'
import ImageUpload from '../components/ImageUpload'
import TicketInfoConfirm from '../components/TicketInfoConfirm'
import ResultDisplay from '../components/ResultDisplay'
import { scanImage, checkTicket } from '../api/client'

// Danh sách đài đầy đủ — hard-code (frontend cache, không phải gọi API mỗi lần)
const ALL_PROVINCES = [
  { code: 'TPHCM', name: 'TP.HCM' },
  { code: 'DongThap', name: 'Đồng Tháp' },
  { code: 'CaMau', name: 'Cà Mau' },
  { code: 'BenTre', name: 'Bến Tre' },
  { code: 'VungTau', name: 'Vũng Tàu' },
  { code: 'BacLieu', name: 'Bạc Liêu' },
  { code: 'DongNai', name: 'Đồng Nai' },
  { code: 'CanTho', name: 'Cần Thơ' },
  // ... thêm đủ 21 tỉnh MN + MT
]

type Stage = 'capture' | 'confirm' | 'result'

export default function Home() {
  const [stage, setStage] = useState<Stage>('capture')
  const [scanned, setScanned] = useState<any>(null)
  const [result, setResult] = useState<any>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleCapture = async (blob: Blob) => {
    setLoading(true); setError(null)
    try {
      const data = await scanImage(blob)
      setScanned(data)
      setStage('confirm')
    } catch (e: any) {
      setError(e?.message ?? 'Lỗi không xác định')
    } finally {
      setLoading(false)
    }
  }

  const handleConfirm = async (info: { ticketNumber: string; drawDate: string; province: string }) => {
    setLoading(true); setError(null)
    try {
      const res = await checkTicket(info)
      setResult(res)
      setStage('result')
    } catch (e: any) {
      setError(e?.message ?? 'Lỗi không xác định')
    } finally {
      setLoading(false)
    }
  }

  if (loading) return <div className="p-8 text-center">Đang xử lý...</div>
  if (error)   return (
    <div className="p-8 text-center text-red-600">
      <div className="mb-2">❌ {error}</div>
      <button onClick={() => { setError(null); setStage('capture') }}
              className="bg-blue-600 text-white px-4 py-2 rounded">
        Thử lại
      </button>
    </div>
  )

  return (
    <div className="max-w-md mx-auto">
      {stage === 'capture' && (
        <>
          <CameraCapture onCapture={handleCapture} />
          <div className="my-4 text-center text-gray-500">— hoặc —</div>
          <ImageUpload onSelect={f => handleCapture(f)} />
        </>
      )}
      {stage === 'confirm' && (
        <TicketInfoConfirm
          scanned={scanned}
          allProvinces={ALL_PROVINCES}
          onConfirm={handleConfirm}
          onRescan={() => setStage('capture')}
        />
      )}
      {stage === 'result' && (
        <ResultDisplay result={result} onRescan={() => setStage('capture')} />
      )}
    </div>
  )
}
```

**Sửa `App.tsx`** để dùng `Home`:
```tsx
import Home from './pages/Home'
export default function Home_App() {
  return <Home />
}
```
Hoặc thay nội dung `App.tsx` thành `export { default } from './pages/Home'`.

### 7.7 Commit checkpoint §7

```powershell
cd D:\Projects\lottery-checker
git add .
git commit -m "feat(frontend): 3-stage flow (capture/confirm/result) + list winnings render"
```

---

## 8. Database & seed data

### 8.1 Tạo migration đầu tiên

```powershell
cd D:\Projects\lottery-checker\backend\LotteryChecker.Api
dotnet ef migrations add InitialCreate
dotnet ef database update
```

**Verify**:
- Thư mục `Migrations/` xuất hiện với 2 file: `xxx_InitialCreate.cs` và `AppDbContextModelSnapshot.cs`.
- File `lottery.db` xuất hiện (SQLite).

### 8.2 Verify schema bằng `sqlite3` CLI

Cài SQLite CLI nếu chưa có:
- Windows: `winget install --id SQLite.SQLite -e`
- 🍎 macOS: `brew install sqlite`

```powershell
sqlite3 lottery.db ".tables"
sqlite3 lottery.db ".schema LotteryResults"
```

Phải thấy bảng `LotteryResults` với cột `Id, DrawDate, Region, Province, PrizeTier, Number, CreatedAt` và 2 index.

### 8.3 SeedData — 1.152 dòng cho 1 đài test

**File**: `Data/SeedData.cs`

Vì đợi worker chạy lúc 19h hơi chậm cho dev, ta seed sẵn 1 ngày (2026-06-02) cho TPHCM với 1.152 số ngẫu nhiên có 2 case test sẵn:
- ĐB = `123456` để test trúng exact.
- Giải tám có 1 số = `56` để test trúng cộng dồn ĐB+giải tám.

```csharp
using LotteryChecker.Api.Models;

namespace LotteryChecker.Api.Data;

public static class SeedData
{
    public static async Task SeedIfEmptyAsync(AppDbContext db)
    {
        if (db.LotteryResults.Any()) return;

        var date = new DateOnly(2026, 6, 2);
        const string province = "TPHCM";
        var rng = new Random(42); // seed cố định để test ổn định

        var rows = new List<LotteryResult>();

        // Bộ đếm theo cơ cấu MN: (PrizeTier, số chữ số, số lượng)
        var schema = new (string Tier, int Digits, int Count, string? FixedFirst)[]
        {
            ("DB", 6, 1, "123456"),  // ĐB cố định để test
            ("1",  5, 1, null),
            ("2",  5, 1, null),
            ("3",  5, 2, null),
            ("4",  5, 7, null),
            ("5",  4, 10, null),
            ("6",  4, 30, null),
            ("7",  3, 100, null),
            ("8",  2, 1000, "56"),   // có 1 số giải tám = "56" để test trúng kép với ĐB 123456
        };

        foreach (var (tier, digits, count, fixedFirst) in schema)
        {
            for (int i = 0; i < count; i++)
            {
                var number = i == 0 && fixedFirst != null
                    ? fixedFirst
                    : rng.Next(0, (int)Math.Pow(10, digits)).ToString().PadLeft(digits, '0');

                rows.Add(new LotteryResult
                {
                    DrawDate = date,
                    Region = "MN",
                    Province = province,
                    PrizeTier = tier,
                    Number = number
                });
            }
        }

        db.LotteryResults.AddRange(rows);
        await db.SaveChangesAsync();
    }
}
```

> `Program.cs` (§6.9) đã gọi `await SeedData.SeedIfEmptyAsync(dbCtx);` trong block dev.

**Verify**: chạy `dotnet run`, sau đó:
```powershell
sqlite3 lottery.db "SELECT COUNT(*) FROM LotteryResults;"
# phải ra: 1152
sqlite3 lottery.db "SELECT Number FROM LotteryResults WHERE PrizeTier='DB';"
# phải ra: 123456
sqlite3 lottery.db "SELECT COUNT(*) FROM LotteryResults WHERE PrizeTier='8';"
# phải ra: 1000
```

### 8.4 Test end-to-end /api/check qua REST Client

Mở `api-tests.http`, gửi request:
```http
POST http://localhost:5000/api/check
Content-Type: application/json

{
  "ticketNumber": "123456",
  "drawDate": "2026-06-02",
  "province": "TPHCM"
}
```

Response phải có dạng:
```json
{
  "extractedNumber": "123456",
  "drawDate": "2026-06-02",
  "province": "TPHCM",
  "isWinner": true,
  "winnings": [
    { "tierName": "Giải Đặc Biệt", "amount": 2000000000 },
    { "tierName": "Giải Tám",       "amount": 100000 }
  ],
  "totalPrize": 2000100000,
  "ocrConfidence": 0
}
```

### 8.5 Rollback migration (nếu cần đổi schema mid-dev)

```powershell
# Xoá migration chưa apply
dotnet ef migrations remove

# Reset toàn bộ DB (data sẽ mất, seed sẽ chạy lại)
dotnet ef database drop -f
dotnet ef database update
```

### 8.6 Commit checkpoint §8

```powershell
cd D:\Projects\lottery-checker
git add .
git commit -m "feat(db): migration InitialCreate + SeedData 1152 dòng cho dev"
```

### 8.7 Lỗi thường gặp ở §8

- **`dotnet ef migrations add` báo "No DbContext was found"** → đảm bảo chạy từ thư mục `LotteryChecker.Api/`, hoặc thêm `--project LotteryChecker.Api`.
- **Migration apply nhưng bảng không có index** → check `OnModelCreating` đã có `.HasIndex(...)`.
- **`SELECT COUNT(*)` ra 0 sau khi chạy `dotnet run`** → check log có dòng "info" của SeedData chạy không. Thường do `Program.cs` chỉ chạy seed trong `IsDevelopment()` — kiểm tra `ASPNETCORE_ENVIRONMENT=Development` trong `launchSettings.json`.

---

## 9. Deployment

### 9.1 Đăng ký Oracle Cloud Free Tier

1. Vào https://cloud.oracle.com → đăng ký Always Free.
2. Tạo 1 instance **VM.Standard.A1.Flex** (ARM Ampere): 2 vCPU + 12GB RAM (trong free tier).
3. Chọn **Ubuntu 24.04 LTS** (có .NET 10 trong apt repo Microsoft).
4. Mở port 80, 443, 22 trong Security List.
5. Tải private key (`.key` file) để SSH.

**Verify**: SSH thử:
```powershell
ssh -i path\to\key ubuntu@<public-ip>
```

> Nếu Oracle hết slot ARM (thường xuyên xảy ra): backup plan ở §9.7.

### 9.2 Setup server (Ubuntu 24.04)

SSH vào VM rồi chạy:

```bash
# Microsoft package repo cho .NET 10
wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb
sudo apt update

# .NET 10 ASP.NET Core runtime
sudo apt install -y aspnetcore-runtime-10.0

# Verify
dotnet --list-runtimes   # phải thấy Microsoft.AspNetCore.App 10.0.x

# Tesseract + Vietnamese
sudo apt install -y tesseract-ocr tesseract-ocr-vie libtesseract-dev

# Caddy (reverse proxy + auto SSL từ Let's Encrypt)
sudo apt install -y debian-keyring debian-archive-keyring apt-transport-https
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | \
    sudo gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | \
    sudo tee /etc/apt/sources.list.d/caddy-stable.list
sudo apt update && sudo apt install -y caddy
```

**Verify**:
```bash
dotnet --list-runtimes              # ≥ 1 dòng AspNetCore.App 10
tesseract --list-langs              # có 'vie'
systemctl status caddy              # active (running)
```

### 9.3 Publish backend từ máy dev

```powershell
cd D:\Projects\lottery-checker\backend\LotteryChecker.Api
dotnet publish -c Release -o .\publish -r linux-arm64 --self-contained false
```

SCP lên server:
```powershell
scp -i path\to\key -r .\publish ubuntu@<ip>:/home/ubuntu/lottery-api
```

🍎 macOS: `scp -i ~/.ssh/oracle.key -r ./publish ubuntu@<ip>:/home/ubuntu/lottery-api`

**Verify** (trên server):
```bash
ls /home/ubuntu/lottery-api
# phải thấy LotteryChecker.Api.dll, appsettings.json, tessdata/
ls /home/ubuntu/lottery-api/tessdata
# phải có vie.traineddata
```

### 9.4 systemd service

Tạo `/etc/systemd/system/lottery-api.service`:
```ini
[Unit]
Description=Lottery Checker API
After=network.target

[Service]
WorkingDirectory=/home/ubuntu/lottery-api
ExecStart=/usr/bin/dotnet /home/ubuntu/lottery-api/LotteryChecker.Api.dll
Restart=always
RestartSec=10
User=ubuntu
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5000

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now lottery-api
```

**Verify**:
```bash
systemctl status lottery-api         # active (running)
journalctl -u lottery-api -n 50      # log khởi động, không có exception
curl http://localhost:5000/health    # {"status":"ok",...}
```

### 9.5 Caddy reverse proxy + auto SSL

`/etc/caddy/Caddyfile`:
```
api.yourdomain.com {
    reverse_proxy localhost:5000
}
```

```bash
sudo systemctl reload caddy
```

Caddy tự xin SSL cert từ Let's Encrypt — không cần thao tác gì thêm. Log:
```bash
journalctl -u caddy -n 100 | grep -i "obtained certificate"
```

**Verify từ máy client**:
```bash
curl https://api.yourdomain.com/health
# {"status":"ok",...}
```

### 9.6 Deploy frontend lên Cloudflare Pages

1. Push code lên GitHub.
2. Vào https://dash.cloudflare.com → Workers & Pages → Create → connect GitHub repo.
3. Build command: `cd frontend && npm install && npm run build`.
4. Build output: `frontend/dist`.
5. Env var: `VITE_API_URL=https://api.yourdomain.com`.

Cloudflare tự deploy mỗi khi push lên main. Có SSL + CDN toàn cầu miễn phí.

**Verify**: vào `https://your-frontend.pages.dev` → load card "🎫 Dò Vé Số", camera xin permission OK trên HTTPS.

### 9.7 Domain + DNS

Mua domain ở Namecheap / Cloudflare Registrar.
- `yourdomain.com` → CNAME → Cloudflare Pages domain.
- `api.yourdomain.com` → A → IP Oracle VM.

### 9.8 Backup plan (Oracle ARM hết slot)

| Provider | Free tier | Phù hợp |
|---|---|---|
| Fly.io | 3 máy ảo nhỏ free | Tốt, có Vietnam region (Singapore gần) |
| Railway | $5 credit/tháng | Đủ cho API nhỏ |
| Render | 750h/tháng | Có cold start — KHÔNG phù hợp cho worker cào hàng ngày |

### 9.9 Lỗi thường gặp ở §9

- **systemd báo "exit 139" hoặc "leptonica"** → server thiếu native lib Tesseract. `sudo apt install -y libtesseract-dev`.
- **Caddy không xin được SSL** → DNS chưa propagate. Đợi 5-15 phút, hoặc check `dig api.yourdomain.com` từ server.
- **CORS error trên prod** → `appsettings.Production.json` thiếu origin frontend. Sửa, restart service: `sudo systemctl restart lottery-api`.
- **Worker không chạy lúc 19h** → check timezone server: `timedatectl`. Default Oracle UTC, lệch 7h so với VN. Đổi: `sudo timedatectl set-timezone Asia/Ho_Chi_Minh`.

---

## 10. VS Code workspace & workflow hàng ngày

### 10.1 Mở project bằng VS Code

```powershell
cd D:\Projects\lottery-checker
code .
```

VS Code sẽ mở cả backend + frontend trong 1 cửa sổ. Bấm "Activate" nếu hiện popup C# Dev Kit.

### 10.2 `.vscode/launch.json` — Debug F5

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Backend: .NET API",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/backend/LotteryChecker.Api/bin/Debug/net10.0/LotteryChecker.Api.dll",
      "cwd": "${workspaceFolder}/backend/LotteryChecker.Api",
      "stopAtEntry": false,
      "env": { "ASPNETCORE_ENVIRONMENT": "Development" },
      "serverReadyAction": {
        "action": "openExternally",
        "pattern": "\\bNow listening on:\\s+(https?://\\S+)"
      }
    },
    {
      "name": "Frontend: Vite",
      "type": "node",
      "request": "launch",
      "cwd": "${workspaceFolder}/frontend",
      "runtimeExecutable": "npm",
      "runtimeArgs": ["run", "dev"],
      "console": "integratedTerminal"
    }
  ],
  "compounds": [
    {
      "name": "🚀 Full Stack",
      "configurations": ["Backend: .NET API", "Frontend: Vite"]
    }
  ]
}
```

### 10.3 `.vscode/tasks.json`

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "build",
      "command": "dotnet",
      "type": "process",
      "args": [
        "build",
        "${workspaceFolder}/backend/LotteryChecker.Api/LotteryChecker.Api.csproj"
      ],
      "problemMatcher": "$msCompile"
    }
  ]
}
```

### 10.4 `.vscode/settings.json`

```json
{
  "editor.formatOnSave": true,
  "editor.defaultFormatter": "esbenp.prettier-vscode",
  "[csharp]": { "editor.defaultFormatter": "ms-dotnettools.csharp" },
  "tailwindCSS.includeLanguages": {
    "typescript": "javascript",
    "typescriptreact": "javascript"
  },
  "files.exclude": {
    "**/bin": true,
    "**/obj": true,
    "**/node_modules": true
  }
}
```

### 10.5 `.vscode/extensions.json`

```json
{
  "recommendations": [
    "ms-dotnettools.csdevkit",
    "ms-dotnettools.csharp",
    "dbaeumer.vscode-eslint",
    "esbenp.prettier-vscode",
    "bradlc.vscode-tailwindcss",
    "humao.rest-client"
  ]
}
```

### 10.6 Workflow hàng ngày

**Khởi động (Cách 1 — đơn giản, 2 terminal)**:
```powershell
# Terminal 1
cd D:\Projects\lottery-checker\backend\LotteryChecker.Api
dotnet watch run            # tự reload khi sửa code C#

# Terminal 2
cd D:\Projects\lottery-checker\frontend
npm run dev                 # tự HMR khi sửa React
```

**Khởi động (Cách 2 — debug)**: VS Code → F5 → chọn "🚀 Full Stack".

**Lệnh thường dùng**:

Backend:
```powershell
dotnet watch run
dotnet ef migrations add <Name>
dotnet ef database update
dotnet ef database drop -f       # CẨN THẬN: xoá DB
dotnet test
dotnet publish -c Release -o ./publish
```

Frontend:
```powershell
npm run dev
npm run build
npm run preview
npm install <pkg>
npm outdated
```

### 10.7 Git workflow + convention commit

```powershell
git add .
git status
git commit -m "feat: thêm OCR service"
git push
```

Convention:
- `feat:` — tính năng mới
- `fix:` — sửa bug
- `refactor:` — refactor không đổi behavior
- `docs:` — sửa docs
- `chore:` — config, build, package
- `test:` — thêm/sửa test

### 10.8 Push lên GitHub lần đầu

```powershell
git remote add origin https://github.com/<user>/lottery-checker.git
git branch -M main
git push -u origin main
```

---

## 11. Roadmap, lưu ý quan trọng, cost estimate

### 11.1 Roadmap 6 tuần

| Tuần | Nội dung |
|---|---|
| 1 | Setup §4 + scaffold §5 + Models §6.1 + DbContext §6.2 + Migration §8.1. Cuối tuần: build + run "hello world" OK. |
| 2 | OCR §6.3, §6.4 (`ImagePreprocessor`, `OcrService`, `ProvinceMatcher`). Test với 20-30 ảnh vé thật, đo tỉ lệ trích đúng từng trường, tinh chỉnh threshold ảnh và regex ngày. |
| 3 | `LotteryMatcher` §6.5 + **5 unit test** §6.5.6 phải pass. `ResultScraper` §6.7 (1 đài demo). `Worker` §6.8 chạy thử. Seed data §8.3 đủ 1.152 dòng. |
| 4 | Frontend §7 — `CameraCapture` với khung vàng, `ImageUpload`, `TicketInfoConfirm` (form pre-filled 3 trường + cho sửa tay), `ResultDisplay` (list giải + total). PWA config. |
| 5 | Deploy §9: Oracle Cloud + Cloudflare Pages, mua domain, cấu hình HTTPS. Test end-to-end trên mobile thật. |
| 6 | Polish UI, thêm lịch sử dò vé, share kết quả lên Zalo/Facebook, analytics đơn giản (Cloudflare Web Analytics free). |

### 11.2 Lưu ý quan trọng

- **Cơ cấu giải hiện tại CHỈ áp dụng Miền Nam** (21 tỉnh từ Bình Thuận → Cà Mau, từ 01-01-2017). Miền Trung dùng cơ cấu tương tự nhưng cần xác nhận riêng. **Miền Bắc khác hẳn** (giải 7 = 4 số, không có Phụ ĐB / Khuyến khích kiểu MN) — đánh dấu là **out of scope MVP**. Khi mở rộng, viết riêng `LotteryMatcherMB` class.

- **Vietlott** in mã vạch QR — nên scan QR thay vì OCR sẽ chính xác 100%. Library: `ZXing.Net` cho C# hoặc `html5-qrcode` cho frontend. Có thể detect QR trước, nếu có → đọc QR; nếu không → fallback OCR. **Out of scope MVP**.

- **2 giải phụ (Phụ ĐB, Khuyến khích)** là đặc thù Việt Nam, dễ quên khi clone template lottery checker nước ngoài. **Đã có 5 unit test trong §6.5.6 đảm bảo logic này đúng — phải pass trước mỗi commit.**

- **Bước xác nhận là bắt buộc, không nên bỏ qua**. OCR vé số giấy không bao giờ chuẩn 100% (dấu mộc đè số, vé nhăn, ánh sáng yếu). Tự dò luôn rồi báo "Trượt" trong khi thực tế OCR đọc nhầm là trải nghiệm tệ.

- **Tinh chỉnh độ chính xác OCR** (sau MVP): (a) crop ảnh trước khi OCR — yêu cầu user đưa khung vàng trên CameraCapture trùm khít vé; (b) chạy OCR 2 lần với 2 PSM khác nhau (`PageSegMode.SingleBlock` cho text, `PageSegMode.SingleLine` riêng cho dòng số to nhất); (c) train model nhẹ trên dataset 200–500 ảnh vé thật để boost từ ~70% → ~95%.

- **Heuristic "tỉnh gần nhất theo ngày"** (nice-to-have): nếu OCR đọc được ngày nhưng KHÔNG đọc được đài, tra DB xem ngày đó có những đài nào quay → nếu chỉ 1 đài thì auto-pick, nhiều đài thì gợi ý top 3.

- **Pháp lý**: app chỉ "hỗ trợ kiểm tra", không thay thế đối chiếu chính thức tại đại lý. Ghi rõ trong điều khoản sử dụng.

- **Bảo mật**: không lưu ảnh vé sau khi xử lý xong (xóa ngay sau OCR), tránh rò rỉ thông tin. Không log số vé vào file log production.

### 11.3 Cost estimate

| Thành phần | Provider | Chi phí |
|---|---|---|
| VM backend + DB + worker | Oracle Cloud Always Free | **$0/tháng** |
| Frontend hosting + CDN | Cloudflare Pages | **$0/tháng** |
| OCR engine | Tesseract (self-hosted) | **$0** |
| SSL cert | Let's Encrypt qua Caddy | **$0** |
| DNS | Cloudflare | **$0** |
| Domain `.xyz` hoặc `.io.vn` | Namecheap/iNet | **~$1–3/năm** |
| **Tổng** | | **~$3/năm** |

Nếu user > 1000/ngày và OCR chậm, nâng cấp Tesseract → **Google Vision API** (1000 request/tháng đầu free, sau $1.5/1000), độ chính xác cao hơn nhiều.

### 11.4 Verification cuối cùng trước khi nói "MVP done"

- [ ] `dotnet test` pass 5/5 ở `LotteryChecker.Tests` (xem §6.5.6).
- [ ] `sqlite3 lottery.db "SELECT COUNT(*) FROM LotteryResults WHERE Province='TPHCM' AND DrawDate='2026-06-02'"` ra đúng `1152`.
- [ ] `POST /api/check` với `{ticketNumber: "123456", drawDate: "2026-06-02", province: "TPHCM"}` trả `totalPrize >= 2_000_000_000` (vé seeded trùng ĐB).
- [ ] Mobile (iPhone Safari hoặc Android Chrome) truy cập `https://your-frontend.pages.dev` → camera mở được → chụp vé → flow đi đủ 3 stage.
- [ ] Worker chạy 1 lần tự động lúc 19h, log có "Worker: cào xong X đài cho ...".
- [ ] Trang Scalar `https://api.yourdomain.com/scalar/v1` hiển thị 2 endpoint với schema ScanResult đúng có `winnings` + `totalPrize`.
