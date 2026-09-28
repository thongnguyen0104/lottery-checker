using System.Diagnostics;
using LotteryChecker.Api.Middleware;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LotteryChecker.Api.Controllers;

[ApiController]
public class ScanController : ControllerBase
{
    private readonly ImagePreprocessor _preprocessor;
    private readonly LocalOcrSwitch _localOcr;
    private readonly ITicketOcrEngine _ocr;
    private readonly TicketTextParser _parser;
    private readonly GeminiTicketReader _gemini;
    private readonly CloudOcrService _cloudOcr;
    private readonly TicketResultValidator _validator;
    private readonly TicketImageGuard _ticketGuard;
    private readonly LocalRetryReader _retry;
    private readonly LotteryMatcher _matcher;
    private readonly ILogger<ScanController> _log;

    public ScanController(ImagePreprocessor p, LocalOcrSwitch localOcr, ITicketOcrEngine o, TicketTextParser parser,
                          GeminiTicketReader gemini, CloudOcrService cloud, TicketResultValidator validator,
                          TicketImageGuard ticketGuard,
                          LocalRetryReader retry, LotteryMatcher m, ILogger<ScanController> log)
    {
        _preprocessor = p; _localOcr = localOcr; _ocr = o; _parser = parser; _gemini = gemini; _cloudOcr = cloud;
        _validator = validator; _ticketGuard = ticketGuard; _retry = retry; _matcher = m; _log = log;
    }

    /// <summary>
    /// Frontend hỏi 1 lần lúc mở app: thu ảnh về rộng bao nhiêu, nén chất lượng bao nhiêu trước khi
    /// gửi /api/scan. Đổi Ocr:LocalEnabled là FE tự đổi theo — xem <see cref="ScanUploadOptions"/>.
    /// </summary>
    [HttpGet("/api/scan/options")]
    public ScanUploadOptions Options([FromServices] ScanUploadOptions options) => options;

    /// <summary>Bước 1: gửi ảnh → trả info đã OCR (số vé, ngày, đài) kèm thời gian từng chặng.</summary>
    [HttpPost("/api/scan")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> Scan(IFormFile image, CancellationToken ct)
    {
        // Mốc = lúc Kestrel nhận request, nên chặng "upload" gồm cả thời gian đẩy ảnh từ
        // điện thoại lên — khâu đó chạy ở model binding, TRƯỚC khi action này bắt đầu.
        var timer = new StageTimer(HttpContext.RequestStartTicks());

        if (image == null || image.Length == 0)
            return BadRequest(new { error = "Chưa có ảnh" });

        // Luồng mặc định: OCR cục bộ là đường chính; cloud (Gemini rồi OCR.space, nguồn nào đang bật)
        // chỉ là FALLBACK khi kết quả cục bộ không qua validate nghiệp vụ — vé chụp rõ không chạm tới
        // cloud. Tắt local (Ocr:LocalEnabled=false) thì mọi vé đều do cloud đọc thẳng.
        TicketInfo info;
        TicketValidation? localCheck = null;   // null = không chạy OCR cục bộ
        // "local" | "local-expired" | "local-review" | "cloud" | "cloud-failed" | "cloud-disabled"
        // | "cloud-only" | "cloud-only-failed" (hai cái cuối: local tắt, cloud là đường chính)
        string ocrPath;
        string? cloudProvider = null;          // nguồn cloud đã cho kết quả: "gemini" | "ocrspace"
        var cloudAttempts = new List<CloudAttempt>();   // từng nguồn cloud đã thử, theo thứ tự
        var cloudUploadKb = 0;
        var resized = false;
        RetryOutcome? retry = null;   // null = không đọc lại
        try
        {
            byte[] original;
            using (var ms = new MemoryStream())
            {
                await image.CopyToAsync(ms, ct);
                original = ms.ToArray();
            }
            timer.Mark("upload");       // upload từ điện thoại + đọc ảnh vào bộ nhớ

            if (!_localOcr.Enabled)
            {
                // Ảnh FE gửi (JPEG ≤1600px, đã xoay đúng) gửi NGUYÊN cho cloud; chỉ ảnh khác mới
                // phải giải mã + xoay + nén lại như đường local.
                byte[] cloudImage;
                if (ImagePreprocessor.CanSendAsIs(original))
                {
                    cloudImage = original;
                    timer.Skip("preprocess");
                }
                else
                {
                    using var prepared = _preprocessor.Load(new MemoryStream(original));
                    resized = prepared.Resized;
                    timer.Mark("preprocess");
                    cloudImage = prepared.EncodeForCloud();
                }
                timer.Skip("localOcr");
                timer.Skip("localRetry");
                cloudUploadKb = cloudImage.Length / 1024;
                timer.Mark("cloudPrepare");

                var cloud = await ReadCloudAsync(cloudImage, cloudAttempts, ct);
                timer.Mark("cloudOcr");
                timer.Skip("merge");

                if (cloud != null)
                {
                    ocrPath = "cloud-only";
                    cloudProvider = cloud.Provider;
                    info = cloud.Info ?? FromCloudText(cloud.Text!);
                }
                else
                {
                    // Không nguồn nào trả lời → form trống, user điền tay (warning bên dưới nói rõ).
                    ocrPath = "cloud-only-failed";
                    info = new TicketInfo();
                }
            }
            else
            {
                // Giải mã 1 lần, giữ ảnh trong RAM: ảnh cloud chỉ dựng từ đây khi thật sự cần.
                using var prepared = _preprocessor.Load(new MemoryStream(original));
                resized = prepared.Resized;
                var localImage = prepared.GetLocal(_preprocessor.LocalMode);
                timer.Mark("preprocess");

                info = _ocr.Extract(localImage);
                var check = _validator.Validate(info);
                timer.Mark("localOcr");     // OCR + parse + validate (2 bước sau chỉ vài ms)
                // Chi tiết bên trong localOcr (dò vùng chữ / nhận dạng dòng / số dòng) — để tối ưu đúng
                // chỗ trên máy thật, nơi endpoint dò cấu hình (chỉ Development) không chạy được.
                foreach (var (name, value) in info.EngineTimings ?? [])
                    timer.Record(name, value);

                // Thiếu/sai ngày hoặc đài → đọc lại trên ảnh lọc khác để lấp đúng trường đó, trước khi
                // tính tới cloud (1–30s). Chỉ lấp, không đè — xem LocalRetryReader.
                if (_preprocessor.RetryMode is { } retryMode && LocalRetryReader.Needed(check))
                {
                    retry = _retry.Run(info, check, prepared, retryMode, img => _ocr.Extract(img));
                    if (retry.Filled.Count > 0) check = _validator.Validate(info);
                    timer.Mark("localRetry");
                }
                else timer.Skip("localRetry");
                localCheck = check;

                if (check.Passed)
                {
                    ocrPath = "local";
                    SkipCloudStages(timer);
                }
                else if (_validator.IsConfidentlyExpired(info, check))
                {
                    // Số vé + đài đã chắc, chỉ có ngày là quá cũ (và đọc khớp ở ≥2 chỗ) → vé hết hạn.
                    ocrPath = "local-expired";
                    SkipCloudStages(timer);
                }
                else if (_cloudOcr.OnlyForTicketNumber && !TicketResultValidator.CloudCanHelp(check))
                {
                    // Số vé đã chắc, chỉ đài/ngày chưa chắc → trả ngay, form đánh dấu trường cần kiểm
                    // tra (needsReview) thay vì bắt user chờ cloud vài giây cho thứ tự chọn được.
                    ocrPath = "local-review";
                    SkipCloudStages(timer);
                }
                else if (!_gemini.IsEnabled && !_cloudOcr.IsEnabled)
                {
                    ocrPath = "cloud-disabled";
                    SkipCloudStages(timer);
                }
                else
                {
                    var cloudImage = prepared.EncodeForCloud();
                    cloudUploadKb = cloudImage.Length / 1024;
                    timer.Mark("cloudPrepare");

                    // Chờ cloud trả lời hẳn (ưu tiên kết quả chính xác hơn tốc độ) — trần duy nhất là
                    // HttpClient.Timeout của từng service ở Program.cs.
                    var cloud = await ReadCloudAsync(cloudImage, cloudAttempts, ct);
                    timer.Mark("cloudOcr");

                    if (cloud != null)
                    {
                        ocrPath = "cloud";
                        cloudProvider = cloud.Provider;
                        var preferCloudNumber = !check.NumberOk || !check.ConfidenceOk;
                        Func<DateOnly, bool>? replaceDateIf = check.DateOk ? null : _validator.IsPlausibleDrawDate;
                        info = cloud.Info != null
                            ? TicketTextParser.MergeFromCloudInfo(info, cloud.Info, preferCloudNumber, replaceDateIf)
                            : _parser.MergeFromCloudText(info, cloud.Text!, preferCloudNumber, replaceDateIf);
                        timer.Mark("merge");
                    }
                    else
                    {
                        ocrPath = "cloud-failed";
                        timer.Skip("merge");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Trả lỗi rõ ràng (kèm CORS header) thay vì để exception thành 500 —
            // tránh trình duyệt báo "Network Error" do mất CORS header ở trang lỗi dev.
            _log.LogWarning(ex, "Quét ảnh lỗi sau {ElapsedMs}ms ({Stages})", timer.TotalMs, timer);
            return UnprocessableEntity(new { error = $"Không xử lý được ảnh: {ex.Message}" });
        }

        // Confidence chỉ có nghĩa với OCR cục bộ; cloud không trả điểm tin cậy → null, FE ẩn dòng đó.
        var localRan = localCheck != null;
        var lowConfidence = localRan && info.OcrConfidence < 0.55;
        var rejectedNonTicket = !_ticketGuard.IsLikelyTicket(info);
        if (rejectedNonTicket)
        {
            _log.LogInformation("Ảnh bị chặn vì không giống vé số: path={Path} conf={Confidence:0.00} reasons=[{Reasons}]",
                ocrPath, info.OcrConfidence, string.Join(",", localCheck?.Reasons ?? []));
        }
        var autoCheck = _validator.CanAutoCheck(info, localCheck);

        // Log đủ để thống kê sau này: tỷ lệ vé phải gọi cloud (ocrPath) và VÌ SAO (reasons), tỷ lệ
        // vé được dò luôn (auto) — đó là số liệu để chỉnh ngưỡng validate / tiền xử lý.
        _log.LogInformation(
            "Quét ảnh {SizeKb}KB (resize={Resized}, gửi cloud {CloudKb}KB) bằng {Engine}/{Preprocess}: " +
            "path={Path} cloud={Cloud} attempts=[{Attempts}] reasons=[{Reasons}] retry={Retry} conf={Confidence:0.00} auto={AutoCheck} | {Stages}",
            image.Length / 1024, resized, cloudUploadKb, _ocr.Name, _preprocessor.LocalMode,
            ocrPath, cloudProvider ?? "-", string.Join(", ", cloudAttempts), string.Join(",", localCheck?.Reasons ?? []),
            retry == null ? "-" : $"{_retry.Strategy}/{_preprocessor.RetryMode}(lines={retry.CroppedLines},full={retry.UsedFull})[{string.Join(",", retry.Filled)}]",
            info.OcrConfidence, autoCheck, timer);

        return Ok(new
        {
            ticketNumber = info.TicketNumber,
            drawDate = info.DrawDate?.ToString("yyyy-MM-dd"),
            province = info.Province,
            confidence = localRan ? info.OcrConfidence : (double?)null,
            lowConfidence,
            ticketNumberFromCloud = info.TicketNumberFromCloud,
            allProvinces = info.Province == null ? ProvinceMatcher.AllCodes : null,
            warning = BuildWarning(info),
            // Kết quả đi đường nào + vì sao local không qua — để benchmark biết tỷ lệ fallback.
            ocrPath,
            cloudProvider,
            // Từng nguồn cloud đã thử (null = không gọi cloud): Gemini lỗi gì, mất bao lâu trước khi
            // lùi về OCR.space — chặng cloudOcr ở timings là TỔNG các lượt này.
            cloudAttempts = cloudAttempts.Count > 0 ? cloudAttempts : null,
            // null = OCR cục bộ không chạy (Ocr:LocalEnabled=false).
            localValidation = localCheck == null ? null : new { passed = localCheck.Passed, reasons = localCheck.Reasons },
            // Lượt đọc lại lấp được trường nào (null = không cần đọc lại). localValidation ở trên
            // là kết quả SAU khi lấp.
            localRetry = retry == null ? null : new
            {
                mode = _preprocessor.RetryMode.ToString(), strategy = _retry.Strategy.ToString(),
                croppedLines = retry.CroppedLines, usedFull = retry.UsedFull, filled = retry.Filled,
            },
            // Trường nào của kết quả CUỐI user nên kiểm tra lại trên form (đánh dấu vàng).
            needsReview = rejectedNonTicket ? ["number", "date", "province"] : _validator.FieldsToReview(info),
            // true = ảnh không giống vé số (ảnh người/phong cảnh/đồ vật...), không cho dò tự động.
            rejectedNonTicket,
            rejectionReason = rejectedNonTicket
                ? "Ảnh tải lên không giống vé số. Vui lòng đặt tờ vé vào khung hình và chụp lại."
                : null,
            // true = đủ chắc cả số vé, đài, ngày → FE dò luôn, không hiện form xác nhận.
            autoCheck = !rejectedNonTicket && autoCheck,
            // Thời gian từng chặng (ms), chặng không chạy = null. Là số liệu để biết nên tối ưu
            // chỗ nào khi chạy trên máy thật (VM prod chậm hơn máy dev nhiều).
            timings = timer.ToTimings()
        });
    }

    /// <summary>Kết quả của một nguồn cloud: Gemini trả sẵn <see cref="Info"/>, OCR.space trả <see cref="Text"/>.</summary>
    private sealed record CloudRead(string Provider, TicketInfo? Info, string? Text);

    /// <summary>
    /// Một lượt gọi một nguồn cloud. <see cref="Error"/> null = trả lời được; Gemini có mã lỗi cụ thể
    /// (xem <see cref="GeminiTicketReader.TryReadAsync"/>), OCR.space chỉ có "failed" (chi tiết ở log).
    /// <see cref="Retried"/> = mã lỗi các lượt Gemini đã tự gọi lại (503...) — <see cref="Ms"/> gồm cả chúng.
    /// </summary>
    private sealed record CloudAttempt(string Provider, double Ms, string? Error, IReadOnlyList<string>? Retried = null)
    {
        public override string ToString() =>
            $"{Provider}:{Error ?? "ok"}/{Ms}ms" + (Retried is { Count: > 0 } r ? $"(retried {string.Join(",", r)})" : "");
    }

    /// <summary>
    /// Thử lần lượt các nguồn cloud đang bật: Gemini trước (nhanh hơn, trả thẳng số/ngày/đài), lỗi
    /// thì OCR.space. Null = không nguồn nào trả lời được (hoặc không nguồn nào bật). Mỗi lượt thử
    /// ghi vào <paramref name="attempts"/> — để biết lượt chậm là do Gemini lỗi/treo hay do OCR.space.
    /// </summary>
    private async Task<CloudRead?> ReadCloudAsync(byte[] jpeg, List<CloudAttempt> attempts, CancellationToken ct)
    {
        if (_gemini.IsEnabled)
        {
            var started = Stopwatch.GetTimestamp();
            var gemini = await _gemini.TryReadAsync(jpeg, ct);
            attempts.Add(new CloudAttempt("gemini", ElapsedMs(started), gemini.Error,
                                          gemini.Retried.Count > 0 ? gemini.Retried : null));
            if (gemini.Info is { } fromGemini) return new CloudRead("gemini", fromGemini, null);
        }
        if (_cloudOcr.IsEnabled)
        {
            var started = Stopwatch.GetTimestamp();
            var text = await _cloudOcr.ReadTextAsync(jpeg, ct);
            attempts.Add(new CloudAttempt("ocrspace", ElapsedMs(started), text == null ? "failed" : null));
            if (text != null) return new CloudRead("ocrspace", null, text);
        }
        return null;
    }

    private static double ElapsedMs(long started) =>
        Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1);

    /// <summary>
    /// Text OCR.space khi nó là đường chính (local tắt, Gemini tắt/lỗi): parse như text OCR cục bộ.
    /// OCR.space không trả điểm tin cậy → đặt như Gemini để validator chỉ xét luật nghiệp vụ.
    /// </summary>
    private TicketInfo FromCloudText(string text)
    {
        var info = _parser.Parse(text, GeminiTicketReader.AssumedConfidence);
        info.CloudText = text;
        info.TicketNumberFromCloud = info.TicketNumber != null;
        return info;
    }

    // Local đã đủ tin (hoặc cloud tắt): vẫn ghi đủ key cloud = null để JSON có cấu trúc cố định.
    private static void SkipCloudStages(StageTimer timer)
    {
        timer.Skip("cloudPrepare");
        timer.Skip("cloudOcr");
        timer.Skip("merge");
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
