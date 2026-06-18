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
        // Dev: chấp nhận mọi origin localhost/127.0.0.1 (bất kỳ cổng) — tránh
        // "network error" khi Vite đổi cổng (5173→5174) hoặc dùng 127.0.0.1.
        p.SetIsOriginAllowed(origin =>
            Uri.TryCreate(origin, UriKind.Absolute, out var u)
            && (u.Host == "localhost" || u.Host == "127.0.0.1"));
    else
        p.WithOrigins(allowedOrigins);
}));

// Controllers + OpenAPI (built-in của .NET 10, KHÔNG cần Swashbuckle)
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Services — OCR + dò số
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

// Tự động apply migrations khi khởi động (chỉ ở dev)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Seed 1.152 dòng (1 đài/ngày) để test/demo ở dev — chỉ seed khi DB rỗng
    await SeedData.SeedIfEmptyAsync(db);

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