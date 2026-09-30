using LotteryChecker.Api.Data;
using LotteryChecker.Api.Middleware;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace LotteryChecker.Api.Controllers;

/// <summary>
/// Blog cho mọi người: khách cũng đọc, viết (ẩn danh / tự đặt tên) và like được; ký tên bằng tài
/// khoản và xoá bài thì cần đăng nhập. Chống spam bằng rate limit theo IP (Program.cs).
/// </summary>
[ApiController]
[Route("api/blog")]
public partial class BlogController(BlogService blog, BlogImageService images, BlogImageStorage storage,
                                    NotificationService notifications, AppDbContext db, ILogger<BlogController> log) : ControllerBase
{
    public const string PostRateLimitPolicy = "blog-post";
    public const string VoteRateLimitPolicy = "blog-vote";
    public const string CommentRateLimitPolicy = "blog-comment";
    public const string ImageRateLimitPolicy = "blog-image";

    /// <summary>Ảnh gốc tối đa 5MB (FE nén trước khi gửi nên thường chỉ vài trăm KB).</summary>
    public const int MaxImageBytes = 5 * 1024 * 1024;
    // Chừa chỗ cho phần khung multipart quanh file.
    private const int MaxImageRequestBytes = MaxImageBytes + 64 * 1024;

    public record VoteRequest(int Value);

    /// <summary>FE hỏi để biết có hiện nút thêm ảnh không (máy chủ chưa cấu hình bucket thì ẩn).</summary>
    [HttpGet("options")]
    public object Options() => new
    {
        imagesEnabled = images.Enabled,
        maxImages = BlogService.MaxImagesPerPost,
        maxImageBytes = MaxImageBytes,
    };

    /// <summary>Upload 1 ảnh (chưa gắn bài) → { id, url }. Gửi id kèm lúc đăng bài.</summary>
    [HttpPost("images")]
    [EnableRateLimiting(ImageRateLimitPolicy)]
    [RequestSizeLimit(MaxImageRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImageRequestBytes)]
    public async Task<IActionResult> UploadImage(IFormFile? image, CancellationToken ct)
    {
        if (!images.Enabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = Lang.T(Request,
                "Máy chủ chưa bật đăng ảnh.", "Image uploads are not enabled on this server.") });
        if (image is not { Length: > 0 })
            return BadRequest(new { error = Lang.T(Request, "Chưa chọn ảnh.", "No image selected.") });
        if (image.Length > MaxImageBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = ImageTooLargeError });

        var owner = VoterKey();
        if (await blog.PendingImageCountAsync(owner, ct) >= BlogService.MaxPendingImagesPerOwner)
            return BadRequest(new { error = Lang.T(Request,
                "Bạn có nhiều ảnh chưa đăng quá — đăng bài hoặc chờ một lát rồi thử lại.",
                "Too many images waiting to be posted — post them or try again later.") });

        using var ms = new MemoryStream((int)image.Length);
        await image.CopyToAsync(ms, ct);
        try
        {
            return Ok(await images.UploadAsync(ms.ToArray(), owner, ct));
        }
        catch (InvalidBlogImageException)
        {
            return UnprocessableEntity(new { error = Lang.T(Request,
                "File này không đọc được như ảnh — thử ảnh JPG/PNG/WebP khác nhé.",
                "This file can't be read as an image — try another JPG/PNG/WebP.") });
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Upload ảnh blog lên bucket lỗi");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = Lang.T(Request,
                "Chưa lưu được ảnh, thử lại sau nhé.", "Couldn't save the image — please try again.") });
        }
    }

    /// <summary>Ảnh của bài — đi qua máy chủ (có cache RAM) thay vì tải thẳng từ bucket, xem BlogImageStorage.</summary>
    [HttpGet("images/{**key}")]
    public async Task<IActionResult> GetImage(string key, CancellationToken ct)
    {
        if (!storage.Enabled || !ImageKeyPattern().IsMatch(key) || !await blog.IsPublishedImageAsync(key, ct))
            return NotFound();
        var bytes = await storage.GetAsync(key, ct);
        if (bytes == null) return NotFound();
        Response.Headers.CacheControl = BlogImageStorage.CacheControl;
        return File(bytes, "image/webp");
    }

    [GeneratedRegex(@"^blog/\d{4}/\d{2}/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.webp$")]
    private static partial Regex ImageKeyPattern();

    private string ImageTooLargeError => Lang.T(Request, "Ảnh tối đa 5MB.", "Images must be 5MB or smaller.");

    [HttpGet("posts")]
    public Task<BlogService.PageDto> List([FromQuery] string? sort, [FromQuery] int page = 1, CancellationToken ct = default) =>
        blog.ListAsync(sort, page, VoterKey(), CurrentUserId(), ct);

    /// <summary>1 bài — để mở thẳng bài từ chuông thông báo (bài có thể không nằm ở trang đầu).</summary>
    [HttpGet("posts/{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) =>
        await blog.GetAsync(id, VoterKey(), CurrentUserId(), ct) is { } post ? Ok(post) : NotFound(new { error = NotFoundError });

    /// <summary>1 bài theo id công khai — mở link chia sẻ /blog/{guid}.</summary>
    [HttpGet("posts/{publicId:guid}")]
    public async Task<IActionResult> GetShared(Guid publicId, CancellationToken ct) =>
        await blog.GetAsync(publicId, VoterKey(), CurrentUserId(), ct) is { } post ? Ok(post) : NotFound(new { error = NotFoundError });

    [HttpPost("posts")]
    [EnableRateLimiting(PostRateLimitPolicy)]
    public async Task<IActionResult> Create(BlogService.NewPost body, CancellationToken ct)
    {
        var username = User.Identity?.IsAuthenticated == true ? User.Identity.Name : null;
        var error = BlogService.Validate(body, username, Lang.IsEn(Request))
                    ?? await blog.ImageErrorAsync(body.ImageIds, VoterKey(), Lang.IsEn(Request), ct);
        if (error != null) return BadRequest(new { error });
        return Ok(await blog.CreateAsync(body, CurrentUserId(), username, ct));
    }

    [HttpPost("posts/{id:int}/vote")]
    [EnableRateLimiting(VoteRateLimitPolicy)]
    public async Task<IActionResult> Vote(int id, VoteRequest body, CancellationToken ct)
    {
        var result = await blog.VoteAsync(id, VoterKey(), body.Value, ct);
        return result == null ? NotFound(new { error = NotFoundError }) : Ok(result);
    }

    [HttpDelete("posts/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (CurrentUserId() is not { } uid) return Unauthorized();
        var postImages = await blog.ImagesOfPostAsync(id, ct);
        if (!await blog.DeleteAsync(id, uid, ct)) return NotFound(new { error = NotFoundError });
        // Xoá luôn trên bucket; lỗi thì ảnh còn mồ côi trong DB, worker dọn sau.
        if (postImages.Count > 0 && images.Enabled) await images.PurgeAsync(postImages, ct);
        return NoContent();
    }

    [HttpGet("posts/{id:int}/comments")]
    public async Task<IActionResult> Comments(int id, CancellationToken ct) =>
        await blog.ListCommentsAsync(id, CurrentUserId(), await IsAdminAsync(ct), ct) is { } list
            ? Ok(list)
            : NotFound(new { error = NotFoundError });

    [HttpPost("posts/{id:int}/comments")]
    [EnableRateLimiting(CommentRateLimitPolicy)]
    public async Task<IActionResult> Comment(int id, BlogService.NewComment body, CancellationToken ct)
    {
        var username = User.Identity?.IsAuthenticated == true ? User.Identity.Name : null;
        var error = BlogService.ValidateComment(body, username, Lang.IsEn(Request));
        if (error != null) return BadRequest(new { error });
        var (comment, count, err) = await blog.CreateCommentAsync(id, body, CurrentUserId(), username, ct);
        if (err == BlogService.CommentError.None)
            await notifications.OnCommentAsync(comment!.Id, body.ParentId, ct);
        return err switch
        {
            BlogService.CommentError.None => Ok(new { comment, commentCount = count }),
            BlogService.CommentError.NoParent => NotFound(new { error = Lang.T(Request,
                "Bình luận bạn trả lời không còn nữa.", "The comment you replied to no longer exists.") }),
            _ => NotFound(new { error = NotFoundError }),
        };
    }

    /// <summary>Người viết (đã đăng nhập lúc viết) hoặc admin.</summary>
    [HttpDelete("comments/{id:int}")]
    public async Task<IActionResult> DeleteComment(int id, CancellationToken ct)
    {
        if (CurrentUserId() is not { } uid) return Unauthorized();
        return await blog.DeleteCommentAsync(id, uid, await IsAdminAsync(ct), ct) is { } r
            ? Ok(new { postId = r.PostId, commentCount = r.CommentCount })
            : NotFound(new { error = Lang.T(Request, "Không tìm thấy bình luận (có thể đã bị xoá).", "Comment not found (it may have been deleted).") });
    }

    private async Task<bool> IsAdminAsync(CancellationToken ct) =>
        CurrentUserId() is { } uid && await db.Users.AnyAsync(x => x.Id == uid && x.IsAdmin, ct);

    private string NotFoundError => Lang.T(Request, "Không tìm thấy bài viết (có thể đã bị xoá).", "Post not found (it may have been deleted).");

    private int? CurrentUserId() => User.UserId();

    // Đăng nhập: theo tài khoản (đổi máy vẫn giữ lượt). Khách: theo cookie của máy (VisitorId).
    private string VoterKey() =>
        CurrentUserId() is { } uid ? $"u:{uid}" : $"g:{VisitorId.Get(HttpContext)}";
}
