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

/// <summary>
/// Thông báo bình luận / trả lời trên Blog và đánh giá / báo vé trúng trên điểm bán: lưu DB (vào lại vẫn
/// thấy) + đẩy realtime nếu đang mở app.
/// </summary>
public class NotificationService(AppDbContext db, INotificationPusher pusher, TimeProvider clock, ILogger<NotificationService> log)
{
    public const int ListSize = 30;
    public const int SnippetMax = 120;

    /// <summary>
    /// Blog: PostId/CommentId/PostTitle; ActorName null = người viết ký Ẩn danh. Điểm bán: ShopPublicId/ShopName,
    /// ActorName = username. Snippet = đầu nội dung bình luận / đánh giá (vé trúng: "đài|giải|ngày").
    /// </summary>
    public record NotificationDto(int Id, NotificationKind Kind, int? PostId, int? CommentId, string? PostTitle,
                                  string? ActorName, string Snippet, DateTime CreatedAt, bool IsRead,
                                  Guid? ShopPublicId = null, string? ShopName = null, int? Stars = null,
                                  string? SiteSlug = null);

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
        await PushAllAsync(rows, n => ToDto(n, post.Title, comment), ct);
    }

    /// <summary>Sau khi thêm / sửa đánh giá: báo người ghim điểm (trừ khi tự đánh giá). Sửa lại thì không báo lần 2.</summary>
    public async Task OnShopReviewAsync(int reviewId, CancellationToken ct)
    {
        var x = await (from r in db.ShopReviews.AsNoTracking()
                       where r.Id == reviewId
                       join s in db.Shops on r.ShopId equals s.Id
                       join u in db.Users on r.UserId equals u.Id
                       select new { r, s, u.Username }).FirstOrDefaultAsync(ct);
        if (x == null || x.s.UserId == x.r.UserId) return;
        if (await db.Notifications.AnyAsync(n => n.ShopReviewId == reviewId, ct)) return;

        var n = new Notification
        {
            UserId = x.s.UserId, Kind = NotificationKind.ShopReview, ShopId = x.s.Id, ShopReviewId = reviewId,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.Notifications.Add(n);
        await db.SaveChangesAsync(ct);
        await PushAllAsync([n], _ => ShopDto(n, x.s, x.Username, x.r.Content ?? "", x.r.Stars), ct);
    }

    /// <summary>Sau khi có người báo vé trúng: báo người ghim điểm (trừ khi tự báo).</summary>
    public async Task OnShopWinAsync(int winId, CancellationToken ct)
    {
        var x = await (from w in db.ShopWinReports.AsNoTracking()
                       where w.Id == winId
                       join s in db.Shops on w.ShopId equals s.Id
                       join u in db.Users on w.UserId equals u.Id
                       select new { w, s, u.Username }).FirstOrDefaultAsync(ct);
        if (x == null || x.s.UserId == x.w.UserId) return;

        var n = new Notification
        {
            UserId = x.s.UserId, Kind = NotificationKind.ShopWin, ShopId = x.s.Id, ShopWinReportId = winId,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.Notifications.Add(n);
        await db.SaveChangesAsync(ct);
        await PushAllAsync([n], _ => ShopDto(n, x.s, x.Username, WinSnippet(x.w), null), ct);
    }

    /// <summary>Khách gửi yêu cầu giữ vé: báo chủ site.</summary>
    public async Task OnSiteReservationAsync(int reservationId, CancellationToken ct)
    {
        var x = await ReservationQuery(db.SiteReservations.AsNoTracking().Where(r => r.Id == reservationId)).FirstOrDefaultAsync(ct);
        if (x == null) return;
        var n = new Notification
        {
            UserId = x.OwnerUserId, Kind = NotificationKind.SiteReservation, SiteReservationId = reservationId,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.Notifications.Add(n);
        await db.SaveChangesAsync(ct);
        await PushAllAsync([n], _ => SiteDto(n, x.Slug, x.r, x.ProductName), ct);
    }

    private IQueryable<ReservationRow> ReservationQuery(IQueryable<SiteReservation> q) =>
        from r in q
        join s in db.Sites on r.SiteId equals s.Id
        join p in db.SiteProducts on r.ProductId equals (int?)p.Id into pj
        from p in pj.DefaultIfEmpty()
        select new ReservationRow(r, s.Slug, s.OwnerUserId, p == null ? null : p.Name);

    private record ReservationRow(SiteReservation r, string Slug, int OwnerUserId, string? ProductName);

    private async Task PushAllAsync(IEnumerable<Notification> rows, Func<Notification, NotificationDto> dto, CancellationToken ct)
    {
        foreach (var n in rows)
        {
            try
            {
                await pusher.PushAsync(n.UserId, dto(n), ct);
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
        // Blog và điểm bán join sang bảng khác nhau → 2 truy vấn, gộp lại theo id (id tăng theo thời gian).
        var blog = await (from n in db.Notifications.AsNoTracking()
                          where n.UserId == userId
                          join c in db.BlogComments on n.CommentId equals (int?)c.Id
                          join p in db.BlogPosts on n.PostId equals (int?)p.Id
                          orderby n.Id descending
                          select new { n, p.Title, c }).Take(ListSize).ToListAsync(ct);

        var reviews = await (from n in db.Notifications.AsNoTracking()
                             where n.UserId == userId
                             join r in db.ShopReviews on n.ShopReviewId equals (int?)r.Id
                             join s in db.Shops on n.ShopId equals (int?)s.Id
                             join u in db.Users on r.UserId equals u.Id
                             orderby n.Id descending
                             select new { n, s, u.Username, r.Content, r.Stars }).Take(ListSize).ToListAsync(ct);

        var wins = await (from n in db.Notifications.AsNoTracking()
                          where n.UserId == userId
                          join w in db.ShopWinReports on n.ShopWinReportId equals (int?)w.Id
                          join s in db.Shops on n.ShopId equals (int?)s.Id
                          join u in db.Users on w.UserId equals u.Id
                          orderby n.Id descending
                          select new { n, s, u.Username, w }).Take(ListSize).ToListAsync(ct);

        var reservations = await (from n in db.Notifications.AsNoTracking()
                                  where n.UserId == userId
                                  join r in db.SiteReservations on n.SiteReservationId equals (int?)r.Id
                                  join s in db.Sites on r.SiteId equals s.Id
                                  join p in db.SiteProducts on r.ProductId equals (int?)p.Id into pj
                                  from p in pj.DefaultIfEmpty()
                                  orderby n.Id descending
                                  select new { n, r, s.Slug, ProductName = p == null ? null : p.Name }).Take(ListSize).ToListAsync(ct);

        var items = blog.Select(x => ToDto(x.n, x.Title, x.c))
            .Concat(reviews.Select(x => ShopDto(x.n, x.s, x.Username, x.Content ?? "", x.Stars)))
            .Concat(wins.Select(x => ShopDto(x.n, x.s, x.Username, WinSnippet(x.w), null)))
            .Concat(reservations.Select(x => SiteDto(x.n, x.Slug, x.r, x.ProductName)))
            .OrderByDescending(x => x.Id).Take(ListSize).ToArray();
        return new(items, await UnreadAsync(userId, ct));
    }

    // Join để không đếm thông báo của nguồn đã xoá (InMemory khi test không có cascade).
    private async Task<int> UnreadAsync(int userId, CancellationToken ct)
    {
        var unread = db.Notifications.Where(n => n.UserId == userId && !n.IsRead);
        return await (from n in unread join c in db.BlogComments on n.CommentId equals (int?)c.Id select n.Id).CountAsync(ct)
             + await (from n in unread join r in db.ShopReviews on n.ShopReviewId equals (int?)r.Id select n.Id).CountAsync(ct)
             + await (from n in unread join w in db.ShopWinReports on n.ShopWinReportId equals (int?)w.Id select n.Id).CountAsync(ct)
             + await (from n in unread join r in db.SiteReservations on n.SiteReservationId equals (int?)r.Id select n.Id).CountAsync(ct);
    }

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
        n.Id, n.Kind, n.PostId, n.CommentId, postTitle, c.AuthorName, Snip(c.Content),
        DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc), n.IsRead);

    private static NotificationDto ShopDto(Notification n, ShopLocation s, string actor, string snippet, int? stars) => new(
        n.Id, n.Kind, null, null, null, actor, Snip(snippet),
        DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc), n.IsRead, s.PublicId, s.Name, stars);

    /// <summary>Giữ vé: ActorName = tên khách, Snippet = "{số lượng}|{tên sản phẩm}" (FE tự dịch).</summary>
    private static NotificationDto SiteDto(Notification n, string slug, SiteReservation r, string? productName) => new(
        n.Id, n.Kind, null, null, null, r.CustomerName, Snip($"{r.Quantity}|{productName}"),
        DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc), n.IsRead, SiteSlug: slug);

    /// <summary>FE tự dịch: "{đài}|{giải}|{yyyy-MM-dd}".</summary>
    private static string WinSnippet(ShopWinReport w) => $"{w.ProvinceCode}|{w.PrizeTier}|{w.DrawDate:yyyy-MM-dd}";

    private static string Snip(string s) => s.Length > SnippetMax ? s[..SnippetMax].TrimEnd() + "…" : s;
}
