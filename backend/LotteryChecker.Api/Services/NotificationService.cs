using LotteryChecker.Api.Data;
using LotteryChecker.Api.Hubs;
using LotteryChecker.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Services;

/// <summary>Đẩy thông báo tới trình duyệt đang mở — tách ra để test không cần SignalR thật.</summary>
public interface INotificationPusher
{
    Task PushAsync(int userId, NotificationService.NotificationDto n, CancellationToken ct);
}

/// <summary>Gửi qua <see cref="NotificationHub"/> tới mọi tab đang đăng nhập tài khoản đó (Clients.User theo claim NameIdentifier).</summary>
public class SignalRNotificationPusher(IHubContext<NotificationHub> hub) : INotificationPusher
{
    public Task PushAsync(int userId, NotificationService.NotificationDto n, CancellationToken ct) =>
        hub.Clients.User(userId.ToString()).SendAsync(NotificationHub.NotificationEvent, n, ct);
}

/// <summary>Thông báo bình luận / trả lời trên Blog: lưu DB (vào lại vẫn thấy) + đẩy realtime nếu đang mở app.</summary>
public class NotificationService(AppDbContext db, INotificationPusher pusher, TimeProvider clock, ILogger<NotificationService> log)
{
    public const int ListSize = 30;
    public const int SnippetMax = 120;

    /// <summary>ActorName null = người viết ký Ẩn danh. Snippet = đầu nội dung bình luận.</summary>
    public record NotificationDto(int Id, NotificationKind Kind, int PostId, int CommentId, string PostTitle,
                                  string? ActorName, string Snippet, DateTime CreatedAt, bool IsRead);

    public record ListDto(NotificationDto[] Items, int Unread);

    /// <summary>
    /// Sau khi thêm bình luận <paramref name="commentId"/>: báo chủ bài, và báo người được trả lời
    /// (<paramref name="repliedToId"/> = bình luận user bấm "Trả lời", có thể là 1 câu trả lời) cùng người
    /// viết bình luận gốc của luồng. Không báo cho chính người viết; mỗi người tối đa 1 thông báo.
    /// </summary>
    public async Task OnCommentAsync(int commentId, int? repliedToId, CancellationToken ct)
    {
        var comment = await db.BlogComments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == commentId, ct);
        if (comment == null) return;
        var post = await db.BlogPosts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == comment.PostId, ct);
        if (post == null) return;

        // Người nhận → loại thông báo. Trả lời đè lên "bình luận bài" vì cụ thể hơn.
        var recipients = new Dictionary<int, NotificationKind>();
        if (post.UserId is { } owner) recipients[owner] = NotificationKind.PostComment;
        var repliedIds = new[] { repliedToId, comment.ParentId }.OfType<int>().Distinct().ToList();
        if (repliedIds.Count > 0)
        {
            var authors = await db.BlogComments.AsNoTracking()
                .Where(x => repliedIds.Contains(x.Id) && x.UserId != null)
                .Select(x => x.UserId!.Value).ToListAsync(ct);
            foreach (var uid in authors) recipients[uid] = NotificationKind.CommentReply;
        }
        if (comment.UserId is { } self) recipients.Remove(self);
        if (recipients.Count == 0) return;

        var now = clock.GetUtcNow().UtcDateTime;
        var rows = recipients.Select(r => new Notification
        {
            UserId = r.Key, Kind = r.Value, PostId = post.Id, CommentId = comment.Id, CreatedAt = now,
        }).ToList();
        db.Notifications.AddRange(rows);
        await db.SaveChangesAsync(ct);

        foreach (var n in rows)
        {
            try
            {
                await pusher.PushAsync(n.UserId, ToDto(n, post.Title, comment), ct);
            }
            catch (Exception ex)
            {
                // Đẩy hỏng không sao: lần mở chuông sau vẫn tải từ DB.
                log.LogWarning(ex, "Không đẩy được thông báo {Id} tới user {UserId}", n.Id, n.UserId);
            }
        }
    }

    /// <summary>Mới nhất trước, tối đa <see cref="ListSize"/>; Unread đếm trên toàn bộ (không chỉ trang này).</summary>
    public async Task<ListDto> ListAsync(int userId, CancellationToken ct)
    {
        var q = from n in db.Notifications.AsNoTracking()
                where n.UserId == userId
                join c in db.BlogComments on n.CommentId equals c.Id
                join p in db.BlogPosts on n.PostId equals p.Id
                select new { n, p.Title, c };
        var rows = await q.OrderByDescending(x => x.n.Id).Take(ListSize).ToListAsync(ct);
        return new(rows.Select(x => ToDto(x.n, x.Title, x.c)).ToArray(), await UnreadAsync(userId, ct));
    }

    // Join để không đếm thông báo của bình luận đã xoá (InMemory khi test không có cascade).
    private Task<int> UnreadAsync(int userId, CancellationToken ct) =>
        (from n in db.Notifications
         where n.UserId == userId && !n.IsRead
         join c in db.BlogComments on n.CommentId equals c.Id
         select n.Id).CountAsync(ct);

    /// <summary>ids null = đánh dấu đọc hết. Trả số chưa đọc còn lại.</summary>
    public async Task<int> MarkReadAsync(int userId, int[]? ids, CancellationToken ct)
    {
        var q = db.Notifications.Where(x => x.UserId == userId && !x.IsRead);
        if (ids != null) q = q.Where(x => ids.Contains(x.Id));
        foreach (var n in await q.ToListAsync(ct)) n.IsRead = true;
        await db.SaveChangesAsync(ct);
        return await UnreadAsync(userId, ct);
    }

    private static NotificationDto ToDto(Notification n, string postTitle, BlogComment c) => new(
        n.Id, n.Kind, n.PostId, n.CommentId, postTitle, c.AuthorName,
        c.Content.Length > SnippetMax ? c.Content[..SnippetMax].TrimEnd() + "…" : c.Content,
        DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc), n.IsRead);
}
