namespace LotteryChecker.Api.Services;

/// <summary>
/// Frontend nên thu ảnh về rộng bao nhiêu + nén JPEG chất lượng bao nhiêu trước khi gửi
/// /api/scan. Tuỳ đường đọc chính, vì hai đường cần ảnh khác nhau (đo trên 8 vé thật, 2026-09-27):
/// <list type="bullet">
/// <item>OCR cục bộ: giữ 1600px — thu xuống 1280/1024px làm xuất hiện vé đọc SAI mà vẫn qua validate.</item>
/// <item>Cloud (Gemini): 1280px q0.7 vẫn đúng 16/16 lượt, mà ảnh chỉ ~280KB thay vì ~570KB —
/// upload từ điện thoại nhanh hơn, Gemini trả lời nhanh hơn ~1s. Giảm chất lượng mà giữ 1600px
/// thì KHÔNG nhanh hơn: thời gian đi theo cỡ ảnh chứ không theo chất lượng.</item>
/// </list>
/// Chỉnh bằng <c>Ocr:Upload:Local|Cloud:MaxWidth|Quality</c> (prod: env <c>Ocr__Upload__Cloud__MaxWidth</c>...).
/// </summary>
/// <param name="Quality">Chất lượng JPEG kiểu canvas.toBlob: 0..1.</param>
public sealed record ScanUploadOptions(int MaxWidth, double Quality)
{
    public static ScanUploadOptions From(IConfiguration config, bool localEnabled)
    {
        var (section, width, quality) = localEnabled ? ("Local", 1600, 0.85) : ("Cloud", 1280, 0.70);
        return new ScanUploadOptions(
            // Trần 1600px: server cũng thu về đúng chừng đó (ImagePreprocessor), gửi to hơn chỉ tốn mạng.
            Math.Clamp(config.GetValue($"Ocr:Upload:{section}:MaxWidth", width), 480, 1600),
            Math.Clamp(config.GetValue($"Ocr:Upload:{section}:Quality", quality), 0.5, 0.95));
    }
}
