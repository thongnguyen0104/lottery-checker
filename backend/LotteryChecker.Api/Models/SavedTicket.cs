using System.Text.Json.Serialization;

namespace LotteryChecker.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SavedTicketStatus
{
    Waiting,   // chưa có kết quả (chưa xổ / chưa cào được) → worker tự dò khi có
    Won,
    Lost,
    Expired,   // quá hạn lĩnh mà vẫn chưa có kết quả (đài nghỉ xổ / cào thất bại kéo dài)
}

/// <summary>
/// Vé người dùng lưu để hệ thống tự dò sau giờ xổ và nhắc lĩnh thưởng. Mỗi (user, ngày, đài, số) chỉ 1 dòng.
/// Chốt bởi <see cref="Services.SavedTicketService.SettleAsync"/>.
/// </summary>
public class SavedTicket
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string TicketNumber { get; set; } = "";
    public DateOnly DrawDate { get; set; }
    public string Province { get; set; } = "";
    public SavedTicketStatus Status { get; set; }
    /// <summary>long, không decimal: SQLite không SUM được decimal.</summary>
    public long Prize { get; set; }
    /// <summary>Tên các giải trúng, ngăn bằng "|" (vd "Giải Bảy|Giải Tám").</summary>
    public string? PrizeTiers { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SettledAt { get; set; }
    /// <summary>Người dùng bấm "Đã lĩnh" — không nhắc nữa.</summary>
    public DateTime? ClaimedAt { get; set; }
    /// <summary>0 = chưa nhắc, 1 = đã nhắc còn 5 ngày, 2 = đã nhắc còn 1 ngày.</summary>
    public byte ReminderStage { get; set; }
}
