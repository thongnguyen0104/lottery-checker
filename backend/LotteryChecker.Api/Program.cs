using LotteryChecker.Api.Data;
using LotteryChecker.Api.Services;
using LotteryChecker.Api.Workers;
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
{
    p.AllowAnyHeader().AllowAnyMethod();
    if (builder.Environment.IsDevelopment())
        // Dev: chấp nhận localhost + mọi IP LAN nội bộ (bất kỳ cổng) — tránh
        // "network error" khi Vite đổi cổng (5173→5174) HOẶC khi test từ điện
        // thoại cùng WiFi (origin là IP LAN của máy, vd http://10.200.1.108:5173).
        p.SetIsOriginAllowed(origin =>
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var u)) return false;
            if (u.Host == "localhost") return true;
            if (!System.Net.IPAddress.TryParse(u.Host, out var ip)) return false;
            if (System.Net.IPAddress.IsLoopback(ip)) return true;
            var b = ip.GetAddressBytes();          // chỉ cho IP mạng riêng (RFC 1918)
            return b.Length == 4 && (
                b[0] == 10 ||                              // 10.0.0.0/8
                (b[0] == 192 && b[1] == 168) ||            // 192.168.0.0/16
                (b[0] == 172 && b[1] >= 16 && b[1] <= 31)); // 172.16.0.0/12
        });
    else
        p.WithOrigins(allowedOrigins);
}));

// Controllers + OpenAPI (built-in của .NET 10, KHÔNG cần Swashbuckle)
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Services — OCR + dò số
builder.Services.AddSingleton(TimeProvider.System);  // để test bơm được giờ giả
builder.Services.AddSingleton<ProvinceMatcher>();   // stateless, chỉ data tĩnh
builder.Services.AddScoped<ImagePreprocessor>();
builder.Services.AddScoped<OcrService>();
builder.Services.AddScoped<LotteryMatcher>();        // phụ thuộc AppDbContext (Scoped)

// Cloud OCR (OCR.space) — đọc số vé cách điệu mà Tesseract cục bộ đọc sai. Best-effort.
builder.Services.AddHttpClient<CloudOcrService>(c => c.Timeout = TimeSpan.FromSeconds(30));

// Scraper kết quả XSKT + worker tự động cào hằng ngày
builder.Services.AddHttpClient<ResultScraper>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
});
builder.Services.AddHostedService<DailyResultFetchWorker>();

var app = builder.Build();

// Apply migrations khi khởi động — CẢ Ở PROD. DB là SQLite tạo mới theo file, không migrate
// thì không có bảng nào và mọi request đều lỗi "no such table: LotteryResults".
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Data giả để test/demo — chỉ ở dev, và chỉ khi DB rỗng.
    if (app.Environment.IsDevelopment())
        await SeedData.SeedIfEmptyAsync(db);
}

// ApiKey KHÔNG nằm trong appsettings (tránh commit secret) → cảnh báo 1 lần lúc khởi động
// nếu thiếu, để không âm thầm mất khả năng đọc số vé cách điệu.
//   dev : dotnet user-secrets set "CloudOcr:ApiKey" "<key>"
//   prod: biến môi trường CloudOcr__ApiKey (xem .claude/deploy-guide.md §5)
if (builder.Configuration.GetValue<bool>("CloudOcr:Enabled")
    && string.IsNullOrWhiteSpace(builder.Configuration["CloudOcr:ApiKey"]))
    app.Logger.LogWarning(
        "CloudOcr đang bật nhưng thiếu ApiKey — chỉ dùng Tesseract cục bộ, số vé cách điệu dễ đọc sai. " +
        "Lấy key free tại https://ocr.space/ocrapi rồi set CloudOcr:ApiKey (user-secrets ở dev / env ở prod).");

if (app.Environment.IsDevelopment())
{
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