namespace LotteryChecker.Api.Models;

public class TicketInfo
{
    public string? RawText { get; set; }
    public string? TicketNumber { get; set; }
    public DateOnly? DrawDate { get; set; }

    /// <summary>
    /// Số phiếu xác nhận <see cref="DrawDate"/>: số chỗ đọc ra đúng ngày đó + số mảnh "tháng-năm"
    /// khớp (0 khi không có ngày). Xem TicketTextParser.AnalyzeDate.
    /// </summary>
    public int DrawDateVotes { get; set; }
    public string? Province { get; set; }
    public double OcrConfidence { get; set; }

    // ---- Tín hiệu cho TicketResultValidator (quyết định có cần gọi cloud OCR không) ----

    /// <summary>Tên đài có nguyên văn trong text (false = chỉ khớp gần đúng Levenshtein).</summary>
    public bool ProvinceExact { get; set; }

    /// <summary>2+ đài khác nhau khớp ngang điểm (vd tên đài + tên tỉnh nơi in vé) — Province chỉ là đoán.</summary>
    public bool ProvinceAmbiguous { get; set; }

    /// <summary>Có 2+ số 6 chữ số khác nhau cùng số lần xuất hiện cao nhất — không biết số nào thật.</summary>
    public bool TicketNumberAmbiguous { get; set; }

    /// <summary>Số vé chỉ có được sau khi sửa ký tự nhầm (O→0, I/l→1, S→5), không có bản đọc sạch.</summary>
    public bool TicketNumberNormalized { get; set; }

    /// <summary>
    /// Thời gian bên trong engine OCR (ms), vd "ocrDetect" / "ocrRecognize" / "ocrLines" — để biết
    /// chặng localOcr chậm ở dò vùng chữ hay nhận dạng dòng. Null nếu engine không đo được.
    /// </summary>
    public Dictionary<string, double>? EngineTimings { get; set; }

    /// <summary>Text cloud OCR đọc được (null nếu không gọi cloud). Phục vụ debug.</summary>
    public string? CloudText { get; set; }

    /// <summary>True nếu số vé cuối cùng lấy từ cloud OCR (đọc số cách điệu tốt hơn Tesseract).</summary>
    public bool TicketNumberFromCloud { get; set; }

    /// <summary>
    /// Từng dòng chữ engine đọc được kèm khung (toạ độ trên ảnh đưa vào OCR) — để đọc lại ĐÚNG
    /// dòng nghi ngờ thay vì cả ảnh (LocalRetryReader). Null nếu engine không trả khung (Tesseract).
    /// </summary>
    public IReadOnlyList<OcrLine>? Lines { get; set; }

    /// <summary>Bản sao nông — để benchmark thử nhiều cách đọc lại trên cùng một lượt đọc chính.</summary>
    public TicketInfo Clone() => (TicketInfo)MemberwiseClone();
}

/// <summary>Một dòng chữ OCR + khung chữ nhật bao quanh (pixel).</summary>
public sealed record OcrLine(string Text, int X, int Y, int Width, int Height);