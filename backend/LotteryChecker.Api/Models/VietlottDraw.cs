namespace LotteryChecker.Api.Models;

/// <summary>Một kỳ quay Vietlott Mega 6/45 hoặc Power 6/55 (cào từ vietlott.vn).</summary>
public class VietlottDraw
{
    public int Id { get; set; }
    public string Game { get; set; } = "";          // "645" | "655"
    public int DrawId { get; set; }                  // số kỳ, vd 1574
    public DateOnly DrawDate { get; set; }
    public string Numbers { get; set; } = "";        // 6 số tăng dần, 2 chữ số, cách nhau dấu phẩy: "03,09,16,29,30,44"
    public int? Bonus { get; set; }                  // số đặc biệt (chỉ Power 6/55) — tính cho giải Jackpot 2
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
