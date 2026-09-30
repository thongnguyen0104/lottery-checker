namespace LotteryChecker.Api.Models;

/// <summary>
/// Một vé đã dò ra kết quả (Status = Checked) — nguồn cho thống kê toàn hệ thống. Mỗi (ngày, đài,
/// số vé) chỉ tính 1 lần: dò lại cùng một vé (hay nhiều người dò chung tấm vé) không đếm trùng.
/// </summary>
public class CheckedTicket
{
    public int Id { get; set; }
    public DateOnly DrawDate { get; set; }
    public string Province { get; set; } = "";
    public string TicketNumber { get; set; } = "";
    public bool IsWinner { get; set; }
    /// <summary>Tổng tiền thưởng (VNĐ). long chứ không decimal: SQLite không SUM được cột decimal.</summary>
    public long Prize { get; set; }
    public DateTime CheckedAt { get; set; }
}
