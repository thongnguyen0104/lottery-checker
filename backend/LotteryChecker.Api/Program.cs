using LotteryChecker.Api.Data;
using LotteryChecker.Api.Middleware;
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
builder.Services.AddSingleton<TicketTextParser>();  // stateless, dùng chung cho mọi engine OCR
builder.Services.AddSingleton<TicketResultValidator>(); // quyết định có cần fallback cloud OCR
builder.Services.AddSingleton<LocalRetryReader>();   // đọc lại lấp ngày/đài thiếu (engine truyền vào lúc gọi)
builder.Services.AddScoped<ImagePreprocessor>();
builder.Services.AddScoped<LotteryMatcher>();        // phụ thuộc AppDbContext (Scoped)

// Hai engine OCR cục bộ — đăng ký CẢ HAI (endpoint debug /api/admin/ocr-debug luôn cần
// Tesseract để so sánh), còn engine thực sự dùng khi quét thì chọn bằng Ocr:Engine.
builder.Services.AddSingleton<TesseractEnginePool>(); // giữ engine sống giữa các request
builder.Services.AddScoped<OcrService>();
// RapidOcr là Singleton: nạp model tốn ~0,4s, và các field của nó chỉ ghi một lần lúc
// InitModels — phần chạy thật là InferenceSession.Run của ONNX Runtime, vốn an toàn đa luồng.
builder.Services.AddSingleton<RapidOcrService>();

var ocrEngine = builder.Configuration["Ocr:Engine"] ?? "onnx";
var useOnnx = !ocrEngine.Equals("tesseract", StringComparison.OrdinalIgnoreCase);
if (useOnnx)
    builder.Services.AddSingleton<ITicketOcrEngine>(sp => sp.GetRequiredService<RapidOcrService>());
else
    builder.Services.AddScoped<ITicketOcrEngine>(sp => sp.GetRequiredService<OcrService>());

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

// Nạp sẵn model của engine đang dùng, để request ĐẦU TIÊN không phải trả giá khởi tạo.
// Chạy nền: app phải mở cổng ngay, không bắt ai chờ khâu này.
// Chỉ nạp engine ĐANG dùng — nạp cả engine kia là tốn RAM cho thứ không ai gọi.
_ = Task.Run(() =>
{
    try
    {
        if (useOnnx)
        {
            // Lấy service ra là ctor chạy InitModels (~0,4s).
            var engine = app.Services.GetRequiredService<ITicketOcrEngine>();

            // Chạy mồi 1 lượt trên ảnh trắng cỡ ảnh FE gửi (1600×2133): lượt OCR ĐẦU TIÊN của ONNX
            // chậm hơn hẳn các lượt sau (cấp phát bộ nhớ, tối ưu graph). Không mồi thì người quét
            // đầu tiên sau mỗi lần deploy/restart gánh phần chậm đó.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using (var blank = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(
                       1600, 2133, SixLabors.ImageSharp.Color.White))
                engine.Extract(blank);
            app.Logger.LogInformation("Khởi động: OCR dùng engine {Engine}, chạy mồi xong trong {Ms}ms.",
                engine.Name, sw.ElapsedMilliseconds);
        }
        else
        {
            var warmupEngines = builder.Configuration.GetValue<int?>("Tesseract:WarmupEngines") ?? 3;
            app.Services.GetRequiredService<TesseractEnginePool>().Warmup(warmupEngines);
            app.Logger.LogInformation("Khởi động: OCR dùng Tesseract, nạp sẵn {Count} engine.",
                warmupEngines);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Không nạp sẵn được model OCR — request đầu sẽ chậm hơn.");
    }
});

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

// Đo + log thời gian mỗi request /api/* — đặt sớm nhất để bao luôn khâu nhận ảnh upload.
app.UseRequestTiming();

app.UseCors();
app.MapControllers();

// Endpoint test nhanh
app.MapGet("/", () => "Lottery Checker API is running. Try /scalar/v1");
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

app.Run();