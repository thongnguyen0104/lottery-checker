using System.Text.Json.Serialization;

namespace LotteryChecker.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScratchTicketStatus
{
    Pending,    // chưa có kết quả giải tám của đài/ngày đó
    Won,        // số vé trùng giải tám → đã cộng Prize vào số dư
    Lost,
    Refunded,   // quá hạn chờ mà đài không có kết quả (nghỉ xổ / không cào được) → đã hoàn tiền vé
}

/// <summary>
/// Vé cào 2 số (00–99) mua trong app: trúng khi trùng giải tám của đài đã chọn, ngày DrawDate.
/// Kết quả chốt tự động khi có kết quả xổ (xem <see cref="Services.ScratchTicketService.SettleAsync"/>);
/// ScratchedAt chỉ ghi nhận user đã cào xem — không ảnh hưởng tiền thưởng.
/// </summary>
public class ScratchTicket
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly DrawDate { get; set; }
    public string Province { get; set; } = "";
    /// <summary>2 chữ số, "00".."99".</summary>
    public string Number { get; set; } = "";
    /// <summary>Giá lúc mua (VNĐ) — đổi giá sau này không ảnh hưởng vé cũ.</summary>
    public long Price { get; set; }
    public ScratchTicketStatus Status { get; set; }
    /// <summary>Giải tám của đài/ngày — có khi đã chốt Won/Lost.</summary>
    public string? WinningNumber { get; set; }
    public long Prize { get; set; }
    public DateTime PurchasedAt { get; set; }
    public DateTime? SettledAt { get; set; }
    public DateTime? ScratchedAt { get; set; }
}
