namespace LotteryChecker.Api.Models;

public class LotteryResult
{
    public int Id { get; set; }
    public DateOnly DrawDate { get; set; }
    public string Region { get; set; } = "";        // "MB", "MT", "MN"
    public string Province { get; set; } = "";       // ví dụ "TPHCM"
    public string PrizeTier { get; set; } = "";      // "DB", "1", "2"... "8"
    public string Number { get; set; } = "";         // số trúng
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}