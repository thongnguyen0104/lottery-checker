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

        TicketInfo info;
        try
        {
            using var stream = image.OpenReadStream();
            var processed = _preprocessor.Preprocess(stream);
            info = _ocr.Extract(processed);
        }
        catch (Exception ex)
        {
            // Trả lỗi rõ ràng (kèm CORS header) thay vì để exception thành 500 —
            // tránh trình duyệt báo "Network Error" do mất CORS header ở trang lỗi dev.
            return UnprocessableEntity(new { error = $"Không xử lý được ảnh: {ex.Message}" });
        }

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
