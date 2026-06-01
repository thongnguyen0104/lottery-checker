namespace LotteryChecker.Api.Models;

public class ScanResult
{
    public string ExtractedNumber { get; set; } = "";
    public DateOnly? DrawDate { get; set; }
    public string? Province { get; set; }
    public bool IsWinner { get; set; }
    public string? WinningTier { get; set; }
    public decimal PrizeAmount { get; set; }
    public double OcrConfidence { get; set; }
}