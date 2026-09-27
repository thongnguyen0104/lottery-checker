using LotteryChecker.Api.Middleware;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LotteryChecker.Api.Controllers;

[ApiController]
public class ScanController : ControllerBase
{
    private readonly ImagePreprocessor _preprocessor;
    private readonly ITicketOcrEngine _ocr;
    private readonly TicketTextParser _parser;
    private readonly CloudOcrService _cloudOcr;
    private readonly TicketResultValidator _validator;
    private readonly LocalRetryReader _retry;
    private readonly LotteryMatcher _matcher;
    private readonly ILogger<ScanController> _log;

    public ScanController(ImagePreprocessor p, ITicketOcrEngine o, TicketTextParser parser,
                          CloudOcrService cloud, TicketResultValidator validator, LocalRetryReader retry,
                          LotteryMatcher m, ILogger<ScanController> log)
    {
        _preprocessor = p; _ocr = o; _parser = parser; _cloudOcr = cloud; _validator = validator;
        _retry = retry; _matcher = m; _log = log;
    }

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

        // Luồng: OCR cục bộ là đường chính; OCR.space (~5s) chỉ là FALLBACK khi kết quả cục bộ
        // không qua validate nghiệp vụ. Vé chụp rõ không bao giờ chạm tới cloud.
        TicketInfo info;
        TicketValidation localCheck;
        // "local" | "local-expired" | "local-review" | "cloud" | "cloud-failed" | "cloud-disabled"
        string ocrPath;
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

            // Giải mã 1 lần, giữ ảnh trong RAM: ảnh cloud chỉ dựng từ đây khi thật sự cần.
            using var prepared = _preprocessor.Load(new MemoryStream(original));
            resized = prepared.Resized;
            var localImage = prepared.GetLocal(_preprocessor.LocalMode);
            timer.Mark("preprocess");

            info = _ocr.Extract(localImage);
            localCheck = _validator.Validate(info);
            timer.Mark("localOcr");     // OCR + parse + validate (2 bước sau chỉ vài ms)
            // Chi tiết bên trong localOcr (dò vùng chữ / nhận dạng dòng / số dòng) — để tối ưu đúng
            // chỗ trên máy thật, nơi endpoint dò cấu hình (chỉ Development) không chạy được.
            foreach (var (name, value) in info.EngineTimings ?? [])
                timer.Record(name, value);

            // Thiếu/sai ngày hoặc đài → đọc lại trên ảnh lọc khác để lấp đúng trường đó, trước khi
            // tính tới cloud (2–30s). Chỉ lấp, không đè — xem LocalRetryReader.
            if (_preprocessor.RetryMode is { } retryMode && LocalRetryReader.Needed(localCheck))
            {
                retry = _retry.Run(info, localCheck, prepared, retryMode, img => _ocr.Extract(img));
                if (retry.Filled.Count > 0) localCheck = _validator.Validate(info);
                timer.Mark("localRetry");
            }
            else timer.Skip("localRetry");

            if (localCheck.Passed)
            {
                ocrPath = "local";
                SkipCloudStages(timer);
            }
            else if (_validator.IsConfidentlyExpired(info, localCheck))
            {
                // Số vé + đài đã chắc, chỉ có ngày là quá cũ (và đọc khớp ở ≥2 chỗ) → vé hết hạn.
                ocrPath = "local-expired";
                SkipCloudStages(timer);
            }
            else if (_cloudOcr.OnlyForTicketNumber && !TicketResultValidator.CloudCanHelp(localCheck))
            {
                // Số vé đã chắc, chỉ đài/ngày chưa chắc → trả ngay, form đánh dấu trường cần kiểm
                // tra (needsReview) thay vì bắt user chờ cloud vài giây cho thứ tự chọn được.
                ocrPath = "local-review";
                SkipCloudStages(timer);
            }
            else if (!_cloudOcr.IsEnabled)
            {
                ocrPath = "cloud-disabled";
                SkipCloudStages(timer);
            }
            else
            {
                var cloudImage = prepared.EncodeForCloud();
                cloudUploadKb = cloudImage.Length / 1024;
                timer.Mark("cloudPrepare");

                // Chờ OCR.space trả lời hẳn (ưu tiên kết quả chính xác hơn tốc độ) — trần duy nhất là
                // HttpClient.Timeout của CloudOcrService ở Program.cs.
                var cloudText = await _cloudOcr.ReadTextAsync(cloudImage, ct);
                timer.Mark("cloudOcr");

                if (cloudText != null)
                {
                    ocrPath = "cloud";
                    info = _parser.MergeFromCloudText(info, cloudText,
                        preferCloudNumber: !localCheck.NumberOk || !localCheck.ConfidenceOk,
                        replaceDateIf: localCheck.DateOk ? null : _validator.IsPlausibleDrawDate);
                    timer.Mark("merge");
                }
                else
                {
                    ocrPath = "cloud-failed";
                    timer.Skip("merge");
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

        var lowConfidence = info.OcrConfidence < 0.55;

        // Log đủ để thống kê sau này: tỷ lệ vé phải gọi cloud (ocrPath) và VÌ SAO (reasons) —
        // đó là số liệu để chỉnh ngưỡng validate / tiền xử lý.
        _log.LogInformation(
            "Quét ảnh {SizeKb}KB (resize={Resized}, gửi cloud {CloudKb}KB) bằng {Engine}/{Preprocess}: " +
            "path={Path} reasons=[{Reasons}] retry={Retry} conf={Confidence:0.00} | {Stages}",
            image.Length / 1024, resized, cloudUploadKb, _ocr.Name, _preprocessor.LocalMode,
            ocrPath, string.Join(",", localCheck.Reasons),
            retry == null ? "-" : $"{_retry.Strategy}/{_preprocessor.RetryMode}(lines={retry.CroppedLines},full={retry.UsedFull})[{string.Join(",", retry.Filled)}]",
            info.OcrConfidence, timer);

        return Ok(new
        {
            ticketNumber = info.TicketNumber,
            drawDate = info.DrawDate?.ToString("yyyy-MM-dd"),
            province = info.Province,
            confidence = info.OcrConfidence,
            lowConfidence,
            ticketNumberFromCloud = info.TicketNumberFromCloud,
            allProvinces = info.Province == null ? ProvinceMatcher.AllCodes : null,
            warning = BuildWarning(info),
            // Kết quả đi đường nào + vì sao local không qua — để benchmark biết tỷ lệ fallback.
            ocrPath,
            localValidation = new { passed = localCheck.Passed, reasons = localCheck.Reasons },
            // Lượt đọc lại lấp được trường nào (null = không cần đọc lại). localValidation ở trên
            // là kết quả SAU khi lấp.
            localRetry = retry == null ? null : new
            {
                mode = _preprocessor.RetryMode.ToString(), strategy = _retry.Strategy.ToString(),
                croppedLines = retry.CroppedLines, usedFull = retry.UsedFull, filled = retry.Filled,
            },
            // Trường nào của kết quả CUỐI user nên kiểm tra lại trên form (đánh dấu vàng).
            needsReview = _validator.FieldsToReview(info),
            // Thời gian từng chặng (ms), chặng không chạy = null. Là số liệu để biết nên tối ưu
            // chỗ nào khi chạy trên máy thật (VM prod chậm hơn máy dev nhiều).
            timings = timer.ToTimings()
        });
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
