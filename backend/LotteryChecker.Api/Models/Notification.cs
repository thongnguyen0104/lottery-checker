using System.Text.Json.Serialization;

namespace LotteryChecker.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationKind
{
    PostComment,   // có người bình luận bài của mình
    CommentReply,  // có người trả lời bình luận của mình
}

/// <summary>
/// Thông báo cho 1 tài khoản. Chỉ lưu id — tiêu đề bài / nội dung / tên người viết lấy từ bình luận lúc
/// đọc, nên bình luận hay bài bị xoá là thông báo tự biến mất theo (cascade + join).
/// </summary>
public class Notification
{
    public int Id { get; set; }
    /// <summary>Người nhận.</summary>
    public int UserId { get; set; }
    public NotificationKind Kind { get; set; }
    public int PostId { get; set; }
    public int CommentId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}
