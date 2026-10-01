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
    /// <summary>1 ảnh/bài: bucket free chỉ 20GB + ~50k request/tháng, VM 1GB RAM cache ảnh.</summary>
    public const int MaxImagesPerPost = 1;
    /// <summary>Ảnh upload rồi mà chưa đăng bài quá chừng này thì không gắn được nữa (worker sẽ dọn).</summary>
    public static readonly TimeSpan PendingImageTtl = TimeSpan.FromHours(24);
    /// <summary>Tối đa ảnh "chờ đăng" mỗi người — chặn upload hàng loạt không đăng bài để làm đầy bucket.</summary>
    public const int MaxPendingImagesPerOwner = 12;

    /// <summary>ImageIds: id từ POST /api/blog/images, theo thứ tự hiển thị.</summary>
    public record NewPost(string? Title, string? Content, BlogAuthorMode AuthorMode, string? AuthorName, int[]? ImageIds = null);

    /// <summary>Images: đường dẫn tương đối /api/blog/images/{key} (FE tự ghép API_BASE).</summary>
    public record PostDto(int Id, Guid PublicId, string Title, string Content, BlogAuthorMode AuthorMode, string? AuthorName,
                          DateTime CreatedAt, int Likes, int Dislikes, int MyVote, bool Mine, int CommentCount, string[] Images);

    public record ImageDto(int Id, string Url);

    public static string ImageUrl(string key) => $"/api/blog/images/{key}";

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
        if (p.ImageIds is { } ids && (ids.Length > MaxImagesPerPost || ids.Distinct().Count() != ids.Length))
            return en ? $"Up to {MaxImagesPerPost} images per post." : $"Mỗi bài tối đa {MaxImagesPerPost} ảnh.";
        return SignatureError(p.AuthorMode, p.AuthorName, username, en);
    }

    /// <summary>
    /// Ảnh gắn vào bài phải do chính người đăng upload, chưa gắn bài nào và chưa quá hạn chờ.
    /// null = hợp lệ (hoặc không có ảnh).
    /// </summary>
    public async Task<string?> ImageErrorAsync(int[]? imageIds, string ownerKey, bool en, CancellationToken ct)
    {
        if (imageIds is not { Length: > 0 }) return null;
        var since = clock.GetUtcNow().UtcDateTime - PendingImageTtl;
        var ok = await db.BlogImages.CountAsync(x => imageIds.Contains(x.Id) && x.OwnerKey == ownerKey
                                                     && x.PostId == null && x.CreatedAt > since, ct);
        return ok == imageIds.Length ? null
            : en ? "Some images expired — please add them again." : "Có ảnh đã hết hạn, bạn thêm lại ảnh nhé.";
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

    /// <summary>
    /// Đăng bài đã qua <see cref="Validate"/> (và <see cref="ImageErrorAsync"/> nếu có ảnh).
    /// userId/username: null khi là khách.
    /// </summary>
    public async Task<PostDto> CreateAsync(NewPost p, int? userId, string? username, CancellationToken ct)
    {
        var imageIds = p.ImageIds ?? [];
        var images = imageIds.Length == 0 ? []
            : await db.BlogImages.Where(x => imageIds.Contains(x.Id) && x.PostId == null).ToListAsync(ct);
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

        var ordered = images.OrderBy(x => Array.IndexOf(imageIds, x.Id)).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].PostId = post.Id;
            ordered[i].SortOrder = i;
        }
        if (ordered.Count > 0) await db.SaveChangesAsync(ct);
        return ToDto(post, 0, userId, ordered.Select(x => ImageUrl(x.Key)).ToArray());
    }

    /// <summary>Ghi nhận ảnh vừa upload lên bucket (chưa gắn bài).</summary>
    public async Task<ImageDto> AddImageAsync(string key, string ownerKey, int sizeBytes, CancellationToken ct)
    {
        var image = new BlogImage { Key = key, OwnerKey = ownerKey, SizeBytes = sizeBytes, CreatedAt = clock.GetUtcNow().UtcDateTime };
        db.BlogImages.Add(image);
        await db.SaveChangesAsync(ct);
        return new ImageDto(image.Id, ImageUrl(key));
    }

    /// <summary>Số ảnh người này upload mà chưa đăng bài (còn hạn chờ).</summary>
    public Task<int> PendingImageCountAsync(string ownerKey, CancellationToken ct)
    {
        var since = clock.GetUtcNow().UtcDateTime - PendingImageTtl;
        return db.BlogImages.CountAsync(x => x.OwnerKey == ownerKey && x.PostId == null && x.CreatedAt > since, ct);
    }

    /// <summary>
    /// Chỉ phục vụ ảnh đã gắn vào bài. Ảnh chờ đăng thì form xem trước bằng file trên máy; ảnh của bài
    /// đã xoá thì thôi dù bucket chưa kịp xoá.
    /// </summary>
    public Task<bool> IsPublishedImageAsync(string key, CancellationToken ct) =>
        db.BlogImages.AnyAsync(x => x.Key == key && x.PostId != null, ct);

    /// <summary>Ảnh mồ côi cần xoá: chưa gắn bài và tạo trước <paramref name="before"/>.</summary>
    public Task<List<BlogImage>> OrphanImagesAsync(DateTime before, int take, CancellationToken ct) =>
        db.BlogImages.Where(x => x.PostId == null && x.CreatedAt < before).OrderBy(x => x.Id).Take(take).ToListAsync(ct);

    public async Task RemoveImageRowsAsync(IEnumerable<BlogImage> images, CancellationToken ct)
    {
        db.BlogImages.RemoveRange(images);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Dictionary<int, string[]>> ImagesOfAsync(IReadOnlyCollection<int> postIds, CancellationToken ct) =>
        (await db.BlogImages.AsNoTracking()
            .Where(x => x.PostId != null && postIds.Contains(x.PostId.Value))
            .OrderBy(x => x.SortOrder)
            .Select(x => new { PostId = x.PostId!.Value, x.Key })
            .ToListAsync(ct))
        .GroupBy(x => x.PostId)
        .ToDictionary(g => g.Key, g => g.Select(x => ImageUrl(x.Key)).ToArray());

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

        var images = await ImagesOfAsync(ids, ct);
        return new PageDto(posts.Select(x => ToDto(x, mine.GetValueOrDefault(x.Id), userId, images.GetValueOrDefault(x.Id) ?? [])).ToArray(), hasMore);
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
        var images = await ImagesOfAsync([post.Id], ct);
        return ToDto(post, vote?.Value ?? 0, userId, images.GetValueOrDefault(post.Id) ?? []);
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
        // Ảnh thành mồ côi (không xoá dòng) — BlogImageService xoá trên bucket rồi mới xoá dòng. Tự tách
        // chứ không trông vào SetNull của DB (provider InMemory khi test không có).
        foreach (var img in await db.BlogImages.Where(x => x.PostId == postId).ToListAsync(ct)) img.PostId = null;
        db.BlogPosts.Remove(post);   // vote xoá theo (cascade)
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<List<BlogImage>> ImagesOfPostAsync(int postId, CancellationToken ct) =>
        db.BlogImages.Where(x => x.PostId == postId).ToListAsync(ct);

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

    private static PostDto ToDto(BlogPost x, int myVote, int? userId, string[] images) => new(
        x.Id, x.PublicId, x.Title, x.Content, x.AuthorMode, x.AuthorName,
        DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Utc), x.Likes, x.Dislikes, myVote,
        userId != null && x.UserId == userId, x.CommentCount, images);
}
