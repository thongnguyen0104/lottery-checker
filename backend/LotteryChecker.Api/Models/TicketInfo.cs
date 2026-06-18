namespace LotteryChecker.Api.Models;

public class TicketInfo
{
    public string? RawText { get; set; }
    public string? TicketNumber { get; set; }
    public DateOnly? DrawDate { get; set; }
    public string? Province { get; set; }
    public double OcrConfidence { get; set; }

    /// <summary>Text cloud OCR đọc được (null nếu không gọi cloud). Phục vụ debug.</summary>
    public string? CloudText { get; set; }

    /// <summary>True nếu số vé cuối cùng lấy từ cloud OCR (đọc số cách điệu tốt hơn Tesseract).</summary>
    public bool TicketNumberFromCloud { get; set; }
}