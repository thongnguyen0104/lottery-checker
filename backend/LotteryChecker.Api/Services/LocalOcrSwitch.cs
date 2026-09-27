using LotteryChecker.Api.Models;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Cờ bật/tắt OCR cục bộ (<c>Ocr:LocalEnabled</c>, mặc định bật). Tắt thì /api/scan gửi ảnh thẳng
/// cho cloud (Gemini, rồi OCR.space) thay vì chỉ gọi cloud khi local đọc không chắc. Cờ của cloud
/// nằm ở từng service (<c>Gemini:Enabled</c>, <c>CloudOcr:Enabled</c>) vì còn tuỳ có ApiKey hay không.
/// Đọc một lần lúc khởi động — đổi cờ phải restart app.
/// </summary>
public sealed record LocalOcrSwitch(bool Enabled)
{
    /// <summary>
    /// Tắt local mà không còn nguồn cloud nào dùng được (tắt hết, hoặc thiếu ApiKey) thì VẪN bật
    /// local: thà đọc chậm hơn còn hơn mọi lượt quét đều trả form trống. <paramref name="forcedOn"/>
    /// = true khi phải bật ngược cấu hình như vậy, để Program.cs cảnh báo.
    /// </summary>
    public static LocalOcrSwitch From(IConfiguration config, out bool forcedOn)
    {
        var wanted = config.GetValue("Ocr:LocalEnabled", true);
        forcedOn = !wanted && !CloudUsable(config, "Gemini") && !CloudUsable(config, "CloudOcr");
        return new LocalOcrSwitch(wanted || forcedOn);
    }

    /// <summary>Cùng điều kiện với IsEnabled của GeminiTicketReader / CloudOcrService.</summary>
    public static bool CloudUsable(IConfiguration config, string section) =>
        config.GetValue<bool>($"{section}:Enabled") && !string.IsNullOrWhiteSpace(config[$"{section}:ApiKey"]);
}

/// <summary>
/// Chỗ giữ cho <see cref="ITicketOcrEngine"/> khi OCR cục bộ tắt: ScanController vẫn cần một engine
/// để khởi tạo, nhưng không được nạp model ONNX/Tesseract (tốn RAM + ~0,4s) cho thứ không ai dùng.
/// ScanController đã kiểm <see cref="LocalOcrSwitch"/> trước khi gọi — gọi tới đây là lỗi lập trình.
/// </summary>
public sealed class DisabledOcrEngine : ITicketOcrEngine
{
    public string Name => "local-off";

    public TicketInfo Extract(byte[] imageBytes) =>
        throw new InvalidOperationException("OCR cục bộ đang tắt (Ocr:LocalEnabled=false).");
}
