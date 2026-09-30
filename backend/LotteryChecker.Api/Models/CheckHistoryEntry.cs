namespace LotteryChecker.Api.Models;

/// <summary>
/// Một lượt dò vé của tài khoản — nguồn cho "Lịch sử dò vé" ở trang Tài khoản. Khác
/// <see cref="CheckedTicket"/> (thống kê chung, mỗi vé 1 lần): ở đây ghi MỌI lượt, kể cả dò lại và vé
/// chưa xổ / chưa có kết quả / hết hạn, để user thấy đúng những gì mình đã dò.
/// </summary>
public class CheckHistoryEntry
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string TicketNumber { get; set; } = "";
    public DateOnly? DrawDate { get; set; }
    public string? Province { get; set; }
    public CheckStatus Status { get; set; }
    public bool IsWinner { get; set; }
    /// <summary>Tổng tiền thưởng (VNĐ). long chứ không decimal: SQLite không SUM được cột decimal.</summary>
    public long Prize { get; set; }
    public DateTime CheckedAt { get; set; }
}
