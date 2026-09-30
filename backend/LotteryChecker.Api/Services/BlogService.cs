using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Services;

/// <summary>Blog: đăng bài (ký tên tài khoản / ẩn danh / tự đặt), liệt kê, like/dislike, xoá bài của mình.</summary>
public class BlogService(AppDbContext db, TimeProvider clock)
{
    public const int PageSize = 10;
    public const int TitleMin = 3, TitleMax = 120;
    public const int ContentMin = 10, ContentMax = 5000;
    public const int NameMin = 2, NameMax = 30;

    public record NewPost(string? Title, string? Content, BlogAuthorMode AuthorMode, string? AuthorName);

    public record PostDto(int Id, Guid PublicId, string Title, string Content, BlogAuthorMode AuthorMode, string? AuthorName,
                          DateTime CreatedAt, int Likes, int Dislikes, int MyVote, bool Mine, int CommentCount);

    public record PageDto(PostDto[] Items, bool HasMore);

    public record VoteDto(int Likes, int Dislikes, int MyVote);

    /// <summary>Kiểm tra bài mới; null = hợp lệ, không thì câu báo lỗi (song ngữ).</summary>
    public static string? Validate(NewPost p, string? username, bool en)
    {
        var title = p.Title?.Trim() ?? "";
        var content = p.Content?.Trim() ?? "";
        if (title.Length is < TitleMin or > TitleMax)
            return en ? $"Title must be {TitleMin}–{TitleMax} characters." : $"Tiêu đề cần {TitleMin}–{TitleMax} ký tự.";
        if (content.Length is < ContentMin or > ContentMax)
            return en ? $"Content must be {ContentMin}–{ContentMax} characters." : $"Nội dung cần {ContentMin}–{ContentMax} ký tự.";
        return SignatureError(p.AuthorMode, p.AuthorName, username, en);
    }

    /// <summary>Cách ký tên (dùng chung cho bài và bình luận); null = hợp lệ.</summary>
    private static string? SignatureError(BlogAuthorMode mode, string? authorName, string? username, bool en)
    {
        if (!Enum.IsDefined(mode))
            return en ? "Choose how to sign." : "Chọn cách ký tên.";
        if (mode == BlogAuthorMode.Account && username == null)
            return en ? "Log in to sign with your account name." : "Đăng nhập để ký tên bằng tài khoản.";
        if (mode == BlogAuthorMode.Custom && (authorName?.Trim() ?? "").Length is < NameMin or > NameMax)
            return en ? $"Your name must be {NameMin}–{NameMax} characters." : $"Tên hiển thị cần {NameMin}–{NameMax} ký tự.";
        return null;
    }

    /// <summary>Tên hiện dưới bài/bình luận: username (Account), tên tự đặt (Custom), null khi Ẩn danh.</summary>
    private static string? DisplayName(BlogAuthorMode mode, string? username, string? authorName) => mode switch
    {
        BlogAuthorMode.Account => username,
        BlogAuthorMode.Custom => authorName!.Trim(),
        _ => null,
    };

    /// <summary>Đăng bài đã qua <see cref="Validate"/>. userId/username: null khi là khách.</summary>
    public async Task<PostDto> CreateAsync(NewPost p, int? userId, string? username, CancellationToken ct)
    {
        var post = new BlogPost
        {
            Title = p.Title!.Trim(),
            Content = p.Content!.Trim(),
            AuthorMode = p.AuthorMode,
            AuthorName = DisplayName(p.AuthorMode, username, p.AuthorName),
            UserId = userId,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.BlogPosts.Add(post);
        await db.SaveChangesAsync(ct);
        return ToDto(post, 0, userId);
    }

    /// <summary>Trang <paramref name="page"/> (từ 1). sort "top" = nhiều (like − dislike) nhất trước, còn lại mới nhất trước.</summary>
    public async Task<PageDto> ListAsync(string? sort, int page, string voterKey, int? userId, CancellationToken ct)
    {
        page = Math.Max(1, page);
        var q = db.BlogPosts.AsNoTracking();
        q = sort == "top"
            ? q.OrderByDescending(x => x.Likes - x.Dislikes).ThenByDescending(x => x.Likes).ThenByDescending(x => x.Id)
            : q.OrderByDescending(x => x.Id);
        // Lấy dư 1 bài để biết còn trang sau không.
        var posts = await q.Skip((page - 1) * PageSize).Take(PageSize + 1).ToListAsync(ct);
        var hasMore = posts.Count > PageSize;
        posts = posts.Take(PageSize).ToList();

        var ids = posts.Select(x => x.Id).ToList();
        var mine = await db.BlogVotes
            .Where(v => v.VoterKey == voterKey && ids.Contains(v.PostId))
            .ToDictionaryAsync(v => v.PostId, v => v.Value, ct);

        return new PageDto(posts.Select(x => ToDto(x, mine.GetValueOrDefault(x.Id), userId)).ToArray(), hasMore);
    }

    public Task<PostDto?> GetAsync(int postId, string voterKey, int? userId, CancellationToken ct) =>
        GetAsync(db.BlogPosts.Where(x => x.Id == postId), voterKey, userId, ct);

    /// <summary>Bài theo id công khai — mở link chia sẻ /blog/{guid}.</summary>
    public Task<PostDto?> GetAsync(Guid publicId, string voterKey, int? userId, CancellationToken ct) =>
        GetAsync(db.BlogPosts.Where(x => x.PublicId == publicId), voterKey, userId, ct);

    private async Task<PostDto?> GetAsync(IQueryable<BlogPost> query, string voterKey, int? userId, CancellationToken ct)
    {
        var post = await query.AsNoTracking().FirstOrDefaultAsync(ct);
        if (post == null) return null;
        var vote = await db.BlogVotes.AsNoTracking().FirstOrDefaultAsync(v => v.PostId == post.Id && v.VoterKey == voterKey, ct);
        return ToDto(post, vote?.Value ?? 0, userId);
    }

    /// <summary>Đặt lượt của người này: 1 = thích, −1 = không thích, 0 = bỏ. null = không có bài.</summary>
    public async Task<VoteDto?> VoteAsync(int postId, string voterKey, int value, CancellationToken ct)
    {
        var post = await db.BlogPosts.FindAsync([postId], ct);
        if (post == null) return null;
        value = Math.Sign(value);

        var vote = await db.BlogVotes.FindAsync([postId, voterKey], ct);
        var old = vote?.Value ?? 0;
        if (old != value)
        {
            if (vote == null) db.BlogVotes.Add(new BlogVote { PostId = postId, VoterKey = voterKey, Value = value });
            else if (value == 0) db.BlogVotes.Remove(vote);
            else vote.Value = value;
            await db.SaveChangesAsync(ct);

            // Đếm lại từ bảng vote chứ không cộng chênh lệch: hai lượt bấm cùng lúc có lệch thì
            // lượt kế tiếp tự sửa lại cho đúng.
            post.Likes = await db.BlogVotes.CountAsync(v => v.PostId == postId && v.Value == 1, ct);
            post.Dislikes = await db.BlogVotes.CountAsync(v => v.PostId == postId && v.Value == -1, ct);
            await db.SaveChangesAsync(ct);
        }
        return new VoteDto(post.Likes, post.Dislikes, value);
    }

    /// <summary>Xoá bài — chỉ người đăng (đã đăng nhập lúc đăng). false = không có bài hoặc không phải của họ.</summary>
    public async Task<bool> DeleteAsync(int postId, int userId, CancellationToken ct)
    {
        var post = await db.BlogPosts.FirstOrDefaultAsync(x => x.Id == postId && x.UserId == userId, ct);
        if (post == null) return false;
        db.BlogPosts.Remove(post);   // vote xoá theo (cascade)
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Bình luận ──
    public const int CommentMin = 2, CommentMax = 1000;
    /// <summary>Tối đa trả về cho 1 bài — blog nhỏ, tải hết 1 lần cho đơn giản.</summary>
    public const int MaxCommentsPerPost = 500;

    public record NewComment(string? Content, BlogAuthorMode AuthorMode, string? AuthorName, int? ParentId);

    /// <summary>CanDelete: người viết (đã đăng nhập lúc viết) hoặc admin.</summary>
    public record CommentDto(int Id, int? ParentId, string Content, BlogAuthorMode AuthorMode, string? AuthorName,
                             DateTime CreatedAt, bool CanDelete);

    public enum CommentError { None, NoPost, NoParent }

    public record CommentOutcome(CommentDto? Comment, int CommentCount, CommentError Error);

    public static string? ValidateComment(NewComment c, string? username, bool en)
    {
        var content = c.Content?.Trim() ?? "";
        if (content.Length is < CommentMin or > CommentMax)
            return en ? $"Comments must be {CommentMin}–{CommentMax} characters." : $"Bình luận cần {CommentMin}–{CommentMax} ký tự.";
        return SignatureError(c.AuthorMode, c.AuthorName, username, en);
    }

    /// <summary>Bình luận của bài, cũ trước (FE tự gom luồng theo ParentId). null = không có bài.</summary>
    public async Task<CommentDto[]?> ListCommentsAsync(int postId, int? userId, bool isAdmin, CancellationToken ct)
    {
        if (!await db.BlogPosts.AnyAsync(x => x.Id == postId, ct)) return null;
        var rows = await db.BlogComments.AsNoTracking()
            .Where(x => x.PostId == postId)
            .OrderBy(x => x.Id)
            .Take(MaxCommentsPerPost)
            .ToListAsync(ct);
        return rows.Select(x => ToDto(x, userId, isAdmin)).ToArray();
    }

    /// <summary>
    /// Thêm bình luận đã qua <see cref="ValidateComment"/>. Trả lời một câu trả lời → gắn vào bình luận
    /// gốc của luồng đó (luồng chỉ 1 cấp).
    /// </summary>
    public async Task<CommentOutcome> CreateCommentAsync(int postId, NewComment c, int? userId, string? username,
                                                         CancellationToken ct)
    {
        var post = await db.BlogPosts.FirstOrDefaultAsync(x => x.Id == postId, ct);
        if (post == null) return new(null, 0, CommentError.NoPost);

        int? parentId = null;
        if (c.ParentId is { } pid)
        {
            var parent = await db.BlogComments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pid && x.PostId == postId, ct);
            if (parent == null) return new(null, post.CommentCount, CommentError.NoParent);
            parentId = parent.ParentId ?? parent.Id;
        }

        var comment = new BlogComment
        {
            PostId = postId, ParentId = parentId, Content = c.Content!.Trim(), AuthorMode = c.AuthorMode,
            AuthorName = DisplayName(c.AuthorMode, username, c.AuthorName), UserId = userId,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.BlogComments.Add(comment);
        await db.SaveChangesAsync(ct);
        await RecountCommentsAsync(post, ct);
        return new(ToDto(comment, userId, false), post.CommentCount, CommentError.None);
    }

    /// <summary>
    /// Xoá bình luận (cả các trả lời nếu là bình luận gốc) — người viết hoặc admin.
    /// null = không có / không được xoá; có = số bình luận còn lại của bài.
    /// </summary>
    public async Task<(int PostId, int CommentCount)?> DeleteCommentAsync(int commentId, int userId, bool isAdmin, CancellationToken ct)
    {
        var comment = await db.BlogComments.FirstOrDefaultAsync(x => x.Id == commentId && (isAdmin || x.UserId == userId), ct);
        if (comment == null) return null;
        // Xoá trả lời trước rồi mới xoá gốc — không trông vào cascade của DB (provider InMemory khi test không có).
        db.BlogComments.RemoveRange(await db.BlogComments.Where(x => x.ParentId == commentId).ToListAsync(ct));
        db.BlogComments.Remove(comment);
        await db.SaveChangesAsync(ct);
        var post = await db.BlogPosts.FirstAsync(x => x.Id == comment.PostId, ct);
        await RecountCommentsAsync(post, ct);
        return (post.Id, post.CommentCount);
    }

    // Đếm lại từ bảng như lượt vote — hai người bình luận cùng lúc có lệch thì lần sau tự đúng.
    private async Task RecountCommentsAsync(BlogPost post, CancellationToken ct)
    {
        post.CommentCount = await db.BlogComments.CountAsync(x => x.PostId == post.Id, ct);
        await db.SaveChangesAsync(ct);
    }

    private static CommentDto ToDto(BlogComment x, int? userId, bool isAdmin) => new(
        x.Id, x.ParentId, x.Content, x.AuthorMode, x.AuthorName, DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Utc),
        isAdmin || (userId != null && x.UserId == userId));

    private static PostDto ToDto(BlogPost x, int myVote, int? userId) => new(
        x.Id, x.PublicId, x.Title, x.Content, x.AuthorMode, x.AuthorName,
        DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Utc), x.Likes, x.Dislikes, myVote,
        userId != null && x.UserId == userId, x.CommentCount);
}
