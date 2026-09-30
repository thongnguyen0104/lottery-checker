using LotteryChecker.Api.Middleware;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LotteryChecker.Api.Controllers;

/// <summary>
/// Blog cho mọi người: khách cũng đọc, viết (ẩn danh / tự đặt tên) và like được; ký tên bằng tài
/// khoản và xoá bài thì cần đăng nhập. Chống spam bằng rate limit theo IP (Program.cs).
/// </summary>
[ApiController]
[Route("api/blog")]
public class BlogController(BlogService blog) : ControllerBase
{
    public const string PostRateLimitPolicy = "blog-post";
    public const string VoteRateLimitPolicy = "blog-vote";

    public record VoteRequest(int Value);

    [HttpGet("posts")]
    public Task<BlogService.PageDto> List([FromQuery] string? sort, [FromQuery] int page = 1, CancellationToken ct = default) =>
        blog.ListAsync(sort, page, VoterKey(), CurrentUserId(), ct);

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

    private string NotFoundError => Lang.T(Request, "Không tìm thấy bài viết (có thể đã bị xoá).", "Post not found (it may have been deleted).");

    private int? CurrentUserId() => User.UserId();

    // Đăng nhập: theo tài khoản (đổi máy vẫn giữ lượt). Khách: theo cookie của máy (VisitorId).
    private string VoterKey() =>
        CurrentUserId() is { } uid ? $"u:{uid}" : $"g:{VisitorId.Get(HttpContext)}";
}
