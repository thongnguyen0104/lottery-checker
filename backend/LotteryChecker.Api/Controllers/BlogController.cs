using LotteryChecker.Api.Data;
using LotteryChecker.Api.Middleware;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Controllers;

/// <summary>
/// Blog cho mọi người: khách cũng đọc, viết (ẩn danh / tự đặt tên) và like được; ký tên bằng tài
/// khoản và xoá bài thì cần đăng nhập. Chống spam bằng rate limit theo IP (Program.cs).
/// </summary>
[ApiController]
[Route("api/blog")]
public class BlogController(BlogService blog, NotificationService notifications, AppDbContext db) : ControllerBase
{
    public const string PostRateLimitPolicy = "blog-post";
    public const string VoteRateLimitPolicy = "blog-vote";
    public const string CommentRateLimitPolicy = "blog-comment";

    public record VoteRequest(int Value);

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
        var error = BlogService.Validate(body, username, Lang.IsEn(Request));
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
        return await blog.DeleteAsync(id, uid, ct) ? NoContent() : NotFound(new { error = NotFoundError });
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
