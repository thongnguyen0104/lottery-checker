using LotteryChecker.Api.Data;
using LotteryChecker.Api.Services;
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

var app = builder.Build();

// Tự động apply migrations khi khởi động (chỉ ở dev)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Seed fixture nhỏ để test /api/check qua HTTP (dev-only, opt-in qua config)
    if (app.Configuration.GetValue<bool>("Seed:SmokeFixture"))
        await DevSmokeSeed.SeedSmokeFixtureIfEmptyAsync(db);

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