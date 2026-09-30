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

    public record PostDto(int Id, string Title, string Content, BlogAuthorMode AuthorMode, string? AuthorName,
                          DateTime CreatedAt, int Likes, int Dislikes, int MyVote, bool Mine);

    public record PageDto(PostDto[] Items, bool HasMore);

    public record VoteDto(int Likes, int Dislikes, int MyVote);

    /// <summary>Kiểm tra bài mới; null = hợp lệ, không thì câu báo lỗi (song ngữ).</summary>
    public static string? Validate(NewPost p, string? username, bool en)
    {
        var title = p.Title?.Trim() ?? "";
        var content = p.Content?.Trim() ?? "";
        var name = p.AuthorName?.Trim() ?? "";
        if (title.Length is < TitleMin or > TitleMax)
            return en ? $"Title must be {TitleMin}–{TitleMax} characters." : $"Tiêu đề cần {TitleMin}–{TitleMax} ký tự.";
        if (content.Length is < ContentMin or > ContentMax)
            return en ? $"Content must be {ContentMin}–{ContentMax} characters." : $"Nội dung cần {ContentMin}–{ContentMax} ký tự.";
        if (!Enum.IsDefined(p.AuthorMode))
            return en ? "Choose how to sign your post." : "Chọn cách ký tên cho bài viết.";
        if (p.AuthorMode == BlogAuthorMode.Account && username == null)
            return en ? "Log in to post under your account name." : "Đăng nhập để ký tên bằng tài khoản.";
        if (p.AuthorMode == BlogAuthorMode.Custom && name.Length is < NameMin or > NameMax)
            return en ? $"Your name must be {NameMin}–{NameMax} characters." : $"Tên hiển thị cần {NameMin}–{NameMax} ký tự.";
        return null;
    }

    /// <summary>Đăng bài đã qua <see cref="Validate"/>. userId/username: null khi là khách.</summary>
    public async Task<PostDto> CreateAsync(NewPost p, int? userId, string? username, CancellationToken ct)
    {
        var post = new BlogPost
        {
            Title = p.Title!.Trim(),
            Content = p.Content!.Trim(),
            AuthorMode = p.AuthorMode,
            AuthorName = p.AuthorMode switch
            {
                BlogAuthorMode.Account => username,
                BlogAuthorMode.Custom => p.AuthorName!.Trim(),
                _ => null,
            },
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

    private static PostDto ToDto(BlogPost x, int myVote, int? userId) => new(
        x.Id, x.Title, x.Content, x.AuthorMode, x.AuthorName,
        DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Utc), x.Likes, x.Dislikes, myVote,
        userId != null && x.UserId == userId);
}
