using System.Text.Json.Serialization;

namespace LotteryChecker.Api.Models;

/// <summary>Cách người viết muốn ký tên dưới bài.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BlogAuthorMode
{
    Account,    // tên tài khoản đang đăng nhập (FE hiện kèm dấu "tài khoản" — không giả mạo được)
    Anonymous,  // "Ẩn danh"
    Custom,     // tên tự gõ
}

/// <summary>Bài viết trên Blog. Like/Dislike lưu sẵn số đếm để liệt kê không phải đếm lại vote.</summary>
public class BlogPost
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public BlogAuthorMode AuthorMode { get; set; }
    /// <summary>Tên hiển thị: username (Account), tên tự đặt (Custom), null khi Ẩn danh.</summary>
    public string? AuthorName { get; set; }
    /// <summary>Người đăng nếu đang đăng nhập (kể cả khi ký ẩn danh) — để họ xoá được bài của mình.</summary>
    public int? UserId { get; set; }
    public int Likes { get; set; }
    public int Dislikes { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Một lượt thích (+1) / không thích (−1) — mỗi người 1 lượt/bài. VoterKey: "u:{id}" khi đăng nhập,
/// "g:{guid}" với khách (cookie ngẫu nhiên, xem BlogController).
/// </summary>
public class BlogVote
{
    public int PostId { get; set; }
    public string VoterKey { get; set; } = "";
    public int Value { get; set; }
}
