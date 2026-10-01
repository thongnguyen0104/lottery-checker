using System.Text.Json.Serialization;

namespace LotteryChecker.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationKind
{
    PostComment,   // có người bình luận bài của mình
    CommentReply,  // có người trả lời bình luận của mình
    ShopReview,    // có người đánh giá điểm bán mình ghim
    ShopWin,       // có người báo điểm bán mình ghim từng bán vé trúng
    SiteReservation, // có khách gửi yêu cầu giữ vé trên website của mình
}

/// <summary>
/// Thông báo cho 1 tài khoản. Chỉ lưu id — tiêu đề bài / nội dung / tên người viết lấy lúc đọc, nên
/// nguồn (bình luận, bài, điểm bán, đánh giá) bị xoá là thông báo tự biến mất theo (cascade + join).
/// Blog: PostId + CommentId. Điểm bán: ShopId + (ShopReviewId | ShopWinReportId).
/// </summary>
public class Notification
{
    public int Id { get; set; }
    /// <summary>Người nhận.</summary>
    public int UserId { get; set; }
    public NotificationKind Kind { get; set; }
    public int? PostId { get; set; }
    public int? CommentId { get; set; }
    public int? ShopId { get; set; }
    public int? ShopReviewId { get; set; }
    public int? ShopWinReportId { get; set; }
    public int? SiteReservationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}
