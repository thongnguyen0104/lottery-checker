# Hướng dẫn Setup Chi Tiết — Dò Vé Số (C# + React)

> ⚠️ **DEPRECATED**: Nội dung file này đã được merge vào [`lottery-checker-plan.md`](./lottery-checker-plan.md). File giữ tham khảo lịch sử, **KHÔNG còn cập nhật**. Hãy theo dõi `lottery-checker-plan.md` để có thông tin mới nhất (kèm cơ cấu giải XSKT Miền Nam, code `LotteryMatcher` chuẩn, seed data 1.152 dòng, deploy Oracle Cloud, ...).

> **Cách dùng tài liệu này**: Đọc và làm tuần tự từ đầu xuống. Mỗi bước có **lệnh copy-paste** và **cách kiểm tra đã thành công chưa** trước khi sang bước tiếp.
>
> **Thời gian dự kiến**: 2-3 giờ cho lần setup đầu tiên (chủ yếu đợi tải/cài).
>
> Hướng dẫn chính cho **Windows 10/11**. Lệnh thay thế cho **macOS** ghi trong khung 🍎.
>
> **Phiên bản nền tảng**:
> - **.NET 10** (LTS, hỗ trợ đến 11/2028) — SDK `10.0.300` trở lên, ngôn ngữ C# 14
> - **Node.js 20+** (LTS)
> - **React 18** + Vite 5
> - **Tesseract 5.x** + dữ liệu tiếng Việt `vie.traineddata`

---

## Mục lục

1. [Cài các công cụ nền tảng](#phần-1-cài-các-công-cụ-nền-tảng)
2. [Tạo project structure](#phần-2-tạo-project-structure)
3. [Setup Backend C#](#phần-3-setup-backend-c)
4. [Setup Frontend React](#phần-4-setup-frontend-react)
5. [Kết nối Frontend ↔ Backend](#phần-5-kết-nối-frontend--backend)
6. [Cấu hình VS Code workspace](#phần-6-vs-code-workspace)
7. [Workflow hàng ngày](#phần-7-workflow-hàng-ngày)
8. [Xử lý lỗi thường gặp](#phần-8-xử-lý-lỗi-thường-gặp)

---

## Phần 1: Cài các công cụ nền tảng

### 1.1 Mở Terminal/PowerShell với quyền Admin

**Windows**: Bấm phím Windows → gõ "PowerShell" → chuột phải → "Run as administrator".

🍎 **macOS**: Mở Terminal (Cmd+Space → "Terminal"). Không cần admin nhưng sẽ cần `sudo` cho 1 số lệnh.

### 1.2 Cài Git

```powershell
winget install --id Git.Git -e
```

🍎 macOS:
```bash
# Nếu chưa có Homebrew, cài trước:
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
# Rồi cài git:
brew install git
```

**Kiểm tra**: đóng PowerShell, mở lại (để PATH cập nhật), gõ:
```powershell
git --version
```
Phải ra dòng kiểu `git version 2.45.x` hoặc cao hơn.

**Cấu hình tên + email** (lần đầu):
```powershell
git config --global user.name "Tên Của Bạn"
git config --global user.email "you@example.com"
git config --global init.defaultBranch main
```

### 1.3 Cài .NET 10 SDK

> **.NET 10 là phiên bản LTS** (Long Term Support), Microsoft hỗ trợ đến **14/11/2028**. SDK mới nhất khi viết hướng dẫn này là `10.0.300` (phát hành 12/05/2026). Ngôn ngữ C# 14.

```powershell
winget install --id Microsoft.DotNet.SDK.10 -e
```

🍎 macOS:
```bash
brew install --cask dotnet-sdk
```

**Kiểm tra** (mở terminal mới):
```powershell
dotnet --version
```
Phải ra `10.0.xxx` (ví dụ `10.0.300` hoặc cao hơn).

```powershell
dotnet --list-sdks
```
Phải thấy ít nhất 1 dòng SDK 10.x. Nếu máy bạn từng cài .NET 8/9 trước đó, có thể thấy nhiều dòng — không sao, các bản SDK chạy song song được.

**Cài tool EF Core CLI** (dùng để tạo database migrations) — chú ý phải dùng bản 10 để khớp với .NET 10:
```powershell
dotnet tool install --global dotnet-ef --version 10.0.*
```

Nếu trước đó đã cài bản 8/9, update bằng:
```powershell
dotnet tool update --global dotnet-ef --version 10.0.*
```

Kiểm tra:
```powershell
dotnet ef --version
```
Phải ra phiên bản `10.0.x`. Nếu lệnh không nhận, đóng terminal mở lại — PATH chưa refresh.

### 1.4 Cài Node.js (LTS 20)

```powershell
winget install --id OpenJS.NodeJS.LTS -e
```

🍎 macOS:
```bash
brew install node@20
```

**Kiểm tra**:
```powershell
node --version    # phải ra v20.x.x hoặc cao hơn
npm --version     # phải ra 10.x.x hoặc cao hơn
```

### 1.5 Cài VS Code

```powershell
winget install --id Microsoft.VisualStudioCode -e
```

🍎 macOS:
```bash
brew install --cask visual-studio-code
```

**Kiểm tra**: mở VS Code từ Start menu / Launchpad. Trong VS Code, mở Command Palette (Ctrl+Shift+P / Cmd+Shift+P) → gõ "Shell Command: Install 'code' command in PATH" → bấm để có thể gõ `code .` từ terminal.

Kiểm tra trong PowerShell:
```powershell
code --version
```

**Cài extension cần thiết** (chạy trong PowerShell, sẽ tự cài vào VS Code):
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

Giải thích:
- `csdevkit` + `csharp`: hỗ trợ C#, IntelliSense, debug
- `eslint` + `prettier`: lint + format frontend
- `tailwindcss`: gợi ý class Tailwind
- `es7-react-js-snippets`: snippet React (gõ `rfc` → tạo functional component)
- `rest-client`: test API ngay trong VS Code, thay thế Postman

### 1.6 Cài Tesseract OCR

**Đây là phần dễ sai nhất**, đọc kỹ.

#### Windows

Tải installer chính thức từ UB Mannheim build (có hỗ trợ tiếng Việt sẵn):

1. Vào https://github.com/UB-Mannheim/tesseract/wiki
2. Tải `tesseract-ocr-w64-setup-5.x.x.exe` (latest, 64-bit)
3. Chạy installer. Trong bước **"Choose Components"**: tick mở rộng "Additional language data (download)" → tick **Vietnamese**. Cứ Next đến hết.
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
Phải thấy `vie` trong list. Nếu không có, vào lại installer "Modify" để thêm Vietnamese.

🍎 macOS:
```bash
brew install tesseract tesseract-lang
tesseract --list-langs   # phải thấy 'vie'
```

#### Tải file traineddata thủ công (nếu cần)

Nếu vì lý do gì đó không có `vie.traineddata`, tải tay:
- https://github.com/tesseract-ocr/tessdata/raw/main/vie.traineddata
- Lưu vào `C:\Program Files\Tesseract-OCR\tessdata\` (Windows) hoặc `/opt/homebrew/share/tessdata/` (macOS).

### 1.7 Cài Docker Desktop (TÙY CHỌN - bỏ qua nếu chưa cần)

Chưa cần thiết cho dev local, nhưng sẽ cần khi deploy. Có thể cài sau:

```powershell
winget install --id Docker.DockerDesktop -e
```

### 1.8 Tổng kiểm tra cuối Phần 1

Mở PowerShell mới và chạy hết các lệnh sau, **tất cả phải in ra version, không lệnh nào báo lỗi**:

```powershell
git --version
dotnet --version
dotnet ef --version
node --version
npm --version
tesseract --version
code --version
```

✅ Nếu OK hết → sang Phần 2. Nếu sai → xem [Phần 8: Xử lý lỗi](#phần-8-xử-lý-lỗi-thường-gặp).

---

## Phần 2: Tạo project structure

### 2.1 Tạo thư mục gốc

Chọn ổ đĩa và thư mục bạn muốn lưu code. Ví dụ tôi để trong `D:\Projects\`:

```powershell
cd D:\
mkdir Projects
cd Projects
mkdir lottery-checker
cd lottery-checker
```

🍎 macOS:
```bash
cd ~/
mkdir -p Projects/lottery-checker
cd Projects/lottery-checker
```

### 2.2 Khởi tạo Git

```powershell
git init
```

### 2.3 Tạo file `.gitignore` ở thư mục gốc

```powershell
code .gitignore
```
VS Code sẽ mở file rỗng. Paste nội dung sau và lưu (Ctrl+S):

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

# Env files
.env
.env.local
.env.*.local

# IDE
.vscode/*
!.vscode/launch.json
!.vscode/tasks.json
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

# Tesseract tessdata - sẽ download trong Phần 3
backend/LotteryChecker.Api/tessdata/
```

### 2.4 Tạo 2 thư mục con

```powershell
mkdir backend
mkdir frontend
```

Bây giờ structure phải như sau:
```
lottery-checker/
├── .git/
├── .gitignore
├── backend/    (rỗng)
└── frontend/   (rỗng)
```

### 2.5 Commit đầu tiên

```powershell
git add .
git commit -m "Initial commit: project scaffold"
```

---

## Phần 3: Setup Backend C#

### 3.1 Tạo solution và project

```powershell
cd backend
dotnet new sln -n LotteryChecker
dotnet new webapi -n LotteryChecker.Api --use-controllers
dotnet sln add LotteryChecker.Api/LotteryChecker.Api.csproj
```

Giải thích:
- `dotnet new sln` tạo solution file (`.sln`) — file index của Visual Studio gom nhiều project.
- `dotnet new webapi --use-controllers` tạo project Web API với pattern Controller (không phải Minimal API), dễ tổ chức code cho dự án lớn.
- `dotnet sln add` thêm project vào solution.

### 3.2 Cài NuGet packages

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

Đợi mỗi lệnh chạy xong (mỗi cái khoảng 5-15 giây). Nếu lỗi mạng, retry.

> **Lưu ý quan trọng về OpenAPI**: Trong .NET 10, template `webapi` đã có sẵn package `Microsoft.AspNetCore.OpenApi` để sinh OpenAPI spec — **không cần cài Swashbuckle nữa**. Tôi dùng **Scalar** làm UI thay cho SwaggerUI cũ: cài nhanh hơn, giao diện hiện đại, cú pháp gọn. Nếu bạn quen SwaggerUI hơn vẫn có thể `dotnet add package Swashbuckle.AspNetCore` — vẫn chạy tốt với .NET 10.

**Kiểm tra**: mở file `LotteryChecker.Api.csproj` (bằng `code LotteryChecker.Api.csproj`), phải thấy đủ 7 dòng `<PackageReference Include="...">` cho 7 package trên, và dòng `<TargetFramework>net10.0</TargetFramework>`.

### 3.3 Tạo cấu trúc thư mục backend

```powershell
mkdir Controllers
mkdir Services
mkdir Models
mkdir Data
mkdir Workers
mkdir tessdata
```

(Một số folder đã có sẵn từ template, lệnh `mkdir` sẽ báo "đã tồn tại" — bỏ qua.)

### 3.4 Tải Vietnamese traineddata vào project

```powershell
# Windows PowerShell
Invoke-WebRequest -Uri "https://github.com/tesseract-ocr/tessdata/raw/main/vie.traineddata" -OutFile "tessdata\vie.traineddata"
Invoke-WebRequest -Uri "https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata" -OutFile "tessdata\eng.traineddata"
```

🍎 macOS:
```bash
curl -L -o tessdata/vie.traineddata https://github.com/tesseract-ocr/tessdata/raw/main/vie.traineddata
curl -L -o tessdata/eng.traineddata https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata
```

Kiểm tra: `tessdata/vie.traineddata` phải có dung lượng ~14MB, không phải 0 byte.

**Cấu hình copy tessdata khi build**: mở `LotteryChecker.Api.csproj`, thêm vào trước thẻ đóng `</Project>`:

```xml
<ItemGroup>
  <None Update="tessdata\**\*.*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

Lý do: Khi `dotnet run`, .NET copy file output ra thư mục `bin/Debug/`. Nếu không khai báo, `tessdata/` không được copy theo → runtime sẽ lỗi không tìm thấy file.

### 3.5 Xóa file mẫu không cần

Template tạo sẵn `WeatherForecast.cs` và `Controllers/WeatherForecastController.cs` — xóa đi:

```powershell
Remove-Item WeatherForecast.cs
Remove-Item Controllers\WeatherForecastController.cs
```

🍎 macOS: `rm WeatherForecast.cs Controllers/WeatherForecastController.cs`

### 3.6 Tạo Models

Tạo file `Models/LotteryResult.cs`:
```powershell
code Models\LotteryResult.cs
```

Paste:
```csharp
namespace LotteryChecker.Api.Models;

public class LotteryResult
{
    public int Id { get; set; }
    public DateOnly DrawDate { get; set; }
    public string Region { get; set; } = "";        // "MB", "MT", "MN"
    public string Province { get; set; } = "";       // ví dụ "TPHCM"
    public string PrizeTier { get; set; } = "";      // "DB", "1", "2"... "8"
    public string Number { get; set; } = "";         // số trúng
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

Tạo `Models/TicketInfo.cs`:
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

Tạo `Models/ScanResult.cs`:
```csharp
namespace LotteryChecker.Api.Models;

public class ScanResult
{
    public string ExtractedNumber { get; set; } = "";
    public DateOnly? DrawDate { get; set; }
    public string? Province { get; set; }
    public bool IsWinner { get; set; }
    public string? WinningTier { get; set; }
    public decimal PrizeAmount { get; set; }
    public double OcrConfidence { get; set; }
}
```

### 3.7 Tạo DbContext

Tạo `Data/AppDbContext.cs`:
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
            e.HasIndex(x => new { x.DrawDate, x.Province });
            e.HasIndex(x => x.Number);
            e.Property(x => x.Region).HasMaxLength(8);
            e.Property(x => x.Province).HasMaxLength(32);
            e.Property(x => x.PrizeTier).HasMaxLength(4);
            e.Property(x => x.Number).HasMaxLength(8);
        });
    }
}
```

### 3.8 Sửa `appsettings.json`

Mở file `appsettings.json`, thay toàn bộ bằng:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "Default": "Data Source=lottery.db"
  },
  "Tesseract": {
    "DataPath": "./tessdata"
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:5173",
      "http://localhost:3000"
    ]
  }
}
```

### 3.9 Sửa `Program.cs`

Thay toàn bộ `Program.cs` bằng:

```csharp
using LotteryChecker.Api.Data;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// EF Core + SQLite
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// CORS — cho phép frontend gọi
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

// Controllers + OpenAPI (built-in của .NET 10, KHÔNG cần Swashbuckle)
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Tự động apply migrations khi khởi động (chỉ ở dev)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Spec OpenAPI tại /openapi/v1.json
    app.MapOpenApi();
    // UI đẹp tại /scalar/v1
    app.MapScalarApiReference();
}

app.UseCors();
app.MapControllers();

// Endpoint test nhanh
app.MapGet("/", () => "Lottery Checker API is running. Try /scalar/v1");
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

app.Run();
```

> **Điểm khác so với .NET 8**: 
> - `AddOpenApi()` thay cho `AddSwaggerGen()` — built-in, không cần thêm package.
> - `MapOpenApi()` expose spec JSON tại `/openapi/v1.json`.
> - `MapScalarApiReference()` (từ package Scalar.AspNetCore) tạo UI tại `/scalar/v1` — giao diện hiện đại hơn SwaggerUI rõ rệt, tự dark mode theo OS.

### 3.10 Tạo migration đầu tiên

```powershell
# Đảm bảo đang ở thư mục LotteryChecker.Api/
dotnet ef migrations add InitialCreate
dotnet ef database update
```

Sau lệnh `update`, một file `lottery.db` xuất hiện trong thư mục project — đó là SQLite database.

**Kiểm tra**: 
```powershell
dir Migrations
```
Phải thấy 2 file `xxx_InitialCreate.cs` và `AppDbContextModelSnapshot.cs`.

### 3.11 Build & Run thử

```powershell
dotnet build
```
Phải kết thúc bằng `Build succeeded. 0 Warning(s). 0 Error(s).`

```powershell
dotnet run
```

Sau 5-10 giây phải thấy:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5xxx
      Now listening on: https://localhost:7xxx
```

Mở browser, vào:
- `http://localhost:5xxx` (số port hiện trên log) → phải thấy "Lottery Checker API is running..."
- `http://localhost:5xxx/health` → phải thấy JSON `{"status":"ok",...}`
- `http://localhost:5xxx/openapi/v1.json` → phải thấy spec OpenAPI JSON (đang trống vì chưa có controller)
- `http://localhost:5xxx/scalar/v1` → phải thấy Scalar UI (giao diện 2 cột, sidebar bên trái)

Bấm `Ctrl+C` để dừng server.

### 3.12 Tạo controller test đầu tiên

Tạo `Controllers/PingController.cs`:
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

Chạy lại `dotnet run`, vào `http://localhost:5xxx/api/ping` → phải thấy `{"message":"pong",...}`. Vào lại `/scalar/v1` → giờ phải thấy endpoint `GET /api/ping` xuất hiện trong sidebar.

✅ Nếu OK → Phần 3 hoàn tất. Bấm Ctrl+C để dừng server.

> **Commit**: 
> ```powershell
> cd ..\..   # về thư mục gốc lottery-checker
> git add .
> git commit -m "Backend: scaffold + DbContext + ping endpoint"
> ```

---

## Phần 4: Setup Frontend React

### 4.1 Tạo project Vite

```powershell
cd D:\Projects\lottery-checker\frontend
npm create vite@latest . -- --template react-ts
```

Khi hỏi "Current directory is not empty. Remove existing files and continue?" → chọn **"Ignore files and continue"**.

### 4.2 Cài dependencies

```powershell
npm install
```
Đợi 30-60 giây.

```powershell
npm install axios react-webcam
npm install -D tailwindcss@3 postcss autoprefixer vite-plugin-pwa
```

> **Lưu ý**: Tôi pin `tailwindcss@3` thay vì v4. Tailwind v4 mới ra đổi nhiều cú pháp, các tutorial trên mạng đa số vẫn theo v3 — học v3 đỡ rối.

### 4.3 Setup Tailwind

```powershell
npx tailwindcss init -p
```

Mở `tailwind.config.js` (đã sinh tự động) → thay nội dung:
```js
/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  theme: {
    extend: {
      colors: {
        brand: {
          50:  '#FEF2F2', 500: '#DC2626', 600: '#B91C1C', 700: '#991B1B'
        }
      }
    },
  },
  plugins: [],
}
```

Thay toàn bộ `src/index.css` bằng:
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

### 4.4 Setup PWA + cấu hình Vite

Mở `vite.config.ts`, thay nội dung:
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

### 4.5 Copy icon vào `public/`

Copy toàn bộ file PNG/ICO từ bộ icon đã tạo trước đó vào `frontend/public/icons/`, và `favicon.ico` vào `frontend/public/`:
```
frontend/
└── public/
    ├── favicon.ico
    └── icons/
        ├── icon-192.png
        ├── icon-512.png
        ├── icon-maskable-192.png
        ├── icon-maskable-512.png
        ├── apple-touch-icon.png
        └── ...
```

### 4.6 Sửa `index.html`

Thay nội dung `index.html` (ở thư mục gốc frontend):
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

### 4.7 Tạo file `.env`

Tạo `.env` ở thư mục gốc frontend:
```
VITE_API_URL=http://localhost:5000
```

Tạo cả `.env.example` (commit lên git để team biết cần biến nào):
```
VITE_API_URL=http://localhost:5000
```

### 4.8 Viết App.tsx test

Thay `src/App.tsx`:
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

Xóa nội dung mẫu trong `src/App.css` (để rỗng hoặc xóa file). Đảm bảo `src/main.tsx` import đúng:
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

### 4.9 Chạy thử frontend

```powershell
npm run dev
```

Phải thấy:
```
VITE v5.x  ready in 500 ms
➜  Local:   http://localhost:5173/
➜  Network: http://192.168.1.xxx:5173/
```

Mở browser vào `http://localhost:5173` — phải thấy card "Dò Vé Số" và dòng `❌ Lỗi: Network Error` (vì backend đang không chạy, hoặc CORS chưa thông — sẽ xử ở Phần 5).

✅ Nếu card hiển thị đúng style Tailwind (font lớn, màu đỏ, có shadow) → frontend OK.

Bấm Ctrl+C để dừng.

> **Commit**:
> ```powershell
> cd ..   # về thư mục lottery-checker
> git add .
> git commit -m "Frontend: scaffold + Tailwind + PWA config"
> ```

---

## Phần 5: Kết nối Frontend ↔ Backend

### 5.1 Chạy đồng thời 2 server

Mở **2 cửa sổ terminal** (hoặc 2 tab terminal trong VS Code).

**Terminal 1 — Backend**:
```powershell
cd D:\Projects\lottery-checker\backend\LotteryChecker.Api
dotnet run
```
Ghi nhớ port HTTP từ log (ví dụ `http://localhost:5167`).

**Terminal 2 — Frontend**:

Trước khi chạy, mở `.env` ở frontend, sửa lại port nếu cần:
```
VITE_API_URL=http://localhost:5167
```
(thay 5167 bằng port backend thực tế).

```powershell
cd D:\Projects\lottery-checker\frontend
npm run dev
```

### 5.2 Test kết nối

Mở browser → `http://localhost:5173`.

**Trường hợp thấy `✅ Backend OK: {"message":"pong",...}`** → kết nối thông, sang 5.3.

**Trường hợp thấy `❌ Lỗi: Network Error`** hoặc lỗi CORS trong console (F12):
- Kiểm tra port backend đúng với `VITE_API_URL` trong `.env` không.
- Kiểm tra trong `appsettings.json` của backend, `Cors:AllowedOrigins` có `http://localhost:5173` không.
- Restart cả 2 server (Ctrl+C → chạy lại) sau khi sửa.

### 5.3 Cố định port cho backend (tránh đổi mỗi lần)

Mặc định ASP.NET chọn port random. Để cố định:

Mở `LotteryChecker.Api/Properties/launchSettings.json`, sửa profile `http`:
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

Giờ backend luôn chạy ở `http://localhost:5000`. Sửa `.env` frontend:
```
VITE_API_URL=http://localhost:5000
```

Khởi động lại cả 2 → giờ flow đã stable.

---

## Phần 6: VS Code workspace

### 6.1 Mở project bằng VS Code

Từ thư mục gốc `lottery-checker`:
```powershell
code .
```

VS Code sẽ mở cả backend + frontend trong 1 cửa sổ. Có thể sẽ hiện popup "C# Dev Kit needs to be activated" → bấm Activate.

### 6.2 Tạo file `.vscode/launch.json` để debug

Trong VS Code, tạo file `.vscode/launch.json` ở thư mục gốc (Ctrl+Shift+P → "Debug: Open launch.json" → chọn .NET):

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
      "args": [],
      "cwd": "${workspaceFolder}/backend/LotteryChecker.Api",
      "stopAtEntry": false,
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      },
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

Tạo `.vscode/tasks.json`:
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

Giờ trong VS Code, mở Run & Debug panel (Ctrl+Shift+D) → chọn dropdown "🚀 Full Stack" → bấm F5 → cả 2 server tự chạy + có thể đặt breakpoint trên C# code.

### 6.3 File `.vscode/settings.json`

```json
{
  "editor.formatOnSave": true,
  "editor.defaultFormatter": "esbenp.prettier-vscode",
  "[csharp]": {
    "editor.defaultFormatter": "ms-dotnettools.csharp"
  },
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

### 6.4 File `.vscode/extensions.json` (gợi ý extension cho team mới)

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

### 6.5 File HTTP test (thay Postman)

Tạo `backend/LotteryChecker.Api/api-tests.http`:
```http
### Health check
GET http://localhost:5000/health

### Ping
GET http://localhost:5000/api/ping

### Scan ticket (sẽ test khi có endpoint, hiện chưa có)
# POST http://localhost:5000/api/scan
# Content-Type: multipart/form-data; boundary=---boundary
# 
# -----boundary
# Content-Disposition: form-data; name="image"; filename="ticket.jpg"
# Content-Type: image/jpeg
# 
# < ./test-images/sample-ticket.jpg
# -----boundary--
```

Mở file, bấm "Send Request" ngay trên dòng `GET` (do REST Client extension cung cấp) → response hiện ra panel bên cạnh. Tiện hơn Postman nhiều.

---

## Phần 7: Workflow hàng ngày

### 7.1 Khởi động mỗi lần code

**Cách 1 (đơn giản, 2 terminal)**:
```powershell
# Terminal 1
cd D:\Projects\lottery-checker\backend\LotteryChecker.Api
dotnet watch run     # tự reload khi sửa code C#

# Terminal 2  
cd D:\Projects\lottery-checker\frontend
npm run dev          # tự HMR khi sửa React
```

**Cách 2 (debug đầy đủ)**: mở VS Code → F5 → chọn "🚀 Full Stack".

### 7.2 Lệnh thường dùng

**Backend**:
```powershell
dotnet watch run                          # chạy + hot reload
dotnet ef migrations add TenMigration     # tạo migration mới sau khi sửa model
dotnet ef database update                 # apply migration vào DB
dotnet ef migrations remove               # xóa migration chưa apply
dotnet ef database drop -f                # xóa sạch DB (cẩn thận!)
dotnet add package <TenPackage>           # cài thêm NuGet
dotnet test                               # chạy unit test (khi có)
dotnet publish -c Release -o ./publish    # build cho production
```

**Frontend**:
```powershell
npm run dev                # dev server
npm run build              # build production vào dist/
npm run preview            # serve bản build để test
npm install <package>      # cài thêm
npm install -D <package>   # cài dev dependency
npm outdated               # check package cũ
```

### 7.3 Git workflow

```powershell
# Mỗi lần làm xong 1 chức năng:
git add .
git status                  # review xem add đúng chưa
git commit -m "feat: thêm OCR service"
# (đợi đến cuối ngày hoặc cuối feature)
git push                    # push lên remote (khi đã có)
```

Convention commit message (khuyến nghị, không bắt buộc):
- `feat: ...` — tính năng mới
- `fix: ...` — sửa bug
- `refactor: ...` — refactor không đổi behavior
- `docs: ...` — sửa docs
- `chore: ...` — config, build, package

### 7.4 Kết nối với GitHub (khi muốn push)

1. Vào https://github.com → tạo repo mới `lottery-checker` (Private).
2. **Đừng tick** "Add README" (vì local đã có lịch sử git).
3. Copy 2 dòng GitHub cho repo trống:
   ```powershell
   git remote add origin https://github.com/<user>/lottery-checker.git
   git branch -M main
   git push -u origin main
   ```

---

## Phần 8: Xử lý lỗi thường gặp

### Lỗi 1: `dotnet: command not found` sau khi cài SDK
**Nguyên nhân**: Terminal cũ chưa cập nhật PATH.  
**Cách khắc phục**: Đóng hết terminal/PowerShell, mở lại. Nếu vẫn lỗi, restart máy.

### Lỗi 2: `dotnet ef` không nhận
**Nguyên nhân**: Tool global chưa trong PATH.  
**Cách**:
```powershell
# Thêm vào PATH (Windows)
$env:Path += ";$env:USERPROFILE\.dotnet\tools"
```
Hoặc cài lại: `dotnet tool install --global dotnet-ef`.

### Lỗi 3: Tesseract chạy báo `Could not load file 'leptonica-1.82.0.dll'`
**Nguyên nhân**: Trên Windows, package `Tesseract` NuGet 5.x cần native binary.  
**Cách**:
```powershell
dotnet add package Tesseract.Drawing --version 5.2.0
```
Hoặc cài runtime VS C++ Redistributable từ https://aka.ms/vs/17/release/vc_redist.x64.exe.

### Lỗi 4: `npm install` báo `EACCES` hoặc lỗi permission (macOS/Linux)
**Cách**: KHÔNG `sudo npm install`. Thay vào đó:
```bash
mkdir ~/.npm-global
npm config set prefix '~/.npm-global'
echo 'export PATH=~/.npm-global/bin:$PATH' >> ~/.zshrc
source ~/.zshrc
```

### Lỗi 5: Trang React trắng tinh, console báo `Failed to load module script`
**Nguyên nhân**: `index.html` đường dẫn sai, hoặc Vite build cache lỗi.  
**Cách**:
```powershell
rm -rf node_modules/.vite
npm run dev
```

### Lỗi 6: CORS error trên browser console
**Cách kiểm tra**:
- Mở Network tab (F12) → tìm request lỗi → tab Headers → xem `Access-Control-Allow-Origin` có trả về không.
- Trong backend `appsettings.json`, kiểm tra origin frontend có trong `Cors:AllowedOrigins` không.
- Backend ĐÃ restart sau khi sửa chưa? `appsettings.json` chỉ đọc 1 lần lúc khởi động.

### Lỗi 7: Port 5000 / 5173 đã bị chiếm
**Tìm process đang chiếm**:
```powershell
# Windows
netstat -ano | findstr :5000
# Sẽ ra PID, kill bằng:
taskkill /PID <pid> /F
```

🍎 macOS:
```bash
lsof -i :5000
kill -9 <pid>
```

### Lỗi 8: `dotnet watch` không reload khi sửa file
**Cách**: Đảm bảo file thật sự được save. Trong VS Code, bật `"files.autoSave": "afterDelay"` trong settings.

### Lỗi 9: `vie.traineddata` không tìm thấy runtime
**Kiểm tra**:
```powershell
dir bin\Debug\net10.0\tessdata
```
Phải thấy `vie.traineddata`. Nếu không, kiểm tra lại bước **3.4** (cấu hình `<None Update="tessdata\**\*.*">` trong `.csproj`).

### Lỗi 10: HTTPS warning trên Chrome khi chạy localhost
Bỏ qua cũng được trong dev. Hoặc dùng `http` thay `https`:
- Trong `launchSettings.json`, dùng profile `http` thay `https`.
- `dotnet run --launch-profile http`.

---

## ✅ Checklist hoàn tất Phần Setup

Tick từng mục:

- [ ] `git`, `dotnet`, `node`, `npm`, `tesseract`, `code` đều chạy được trong PowerShell
- [ ] `tesseract --list-langs` có `vie`
- [ ] Thư mục `lottery-checker` với 2 sub-folder `backend/` + `frontend/`
- [ ] `.gitignore` ở root + git đã init
- [ ] Backend chạy `dotnet run` không lỗi, vào `http://localhost:5000/api/ping` thấy "pong"
- [ ] `lottery.db` file đã tạo (do migration)
- [ ] Frontend chạy `npm run dev` không lỗi, vào `http://localhost:5173` thấy card "Dò Vé Số"
- [ ] Card hiển thị "✅ Backend OK: ..." (frontend gọi backend thành công)
- [ ] VS Code F5 chạy được "🚀 Full Stack"
- [ ] `git log` thấy ít nhất 2 commit

**Khi tất cả OK** → setup xong. Bước tiếp là viết code thực sự cho OCR, lottery matcher, scraper... theo plan đã lên trước đó.

---

## Bước kế tiếp

Sau khi setup xong, theo plan cũ (`lottery-checker-plan.md`):

**Tuần 1 còn lại**: cấu trúc xong, có thể bắt đầu viết:
1. Models đầy đủ (đã có TicketInfo, LotteryResult, ScanResult)
2. `ProvinceMatcher` + unit test
3. Seed data 1 ngày kết quả thật để test matcher

**Tuần 2**: OCR + ImagePreprocessor (cần nhiều ảnh vé thật để test).

**Tuần 3**: Scraper minhngoc.

**Tuần 4**: Frontend các component.

Cứ làm xong feature nào commit feature đó, đừng để dồn.
