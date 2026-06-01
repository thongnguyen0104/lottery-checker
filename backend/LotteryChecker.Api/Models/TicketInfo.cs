namespace LotteryChecker.Api.Models;

public class TicketInfo
{
    public string? RawText { get; set; }
    public string? TicketNumber { get; set; }
    public DateOnly? DrawDate { get; set; }
    public string? Province { get; set; }
    public double OcrConfidence { get; set; }
}