using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LotteryChecker.Api.Controllers;

/// <summary>Chuông thông báo (cần đăng nhập). Thông báo mới còn được đẩy realtime qua NotificationHub.</summary>
[ApiController]
[Route("api/notifications")]
public class NotificationsController(NotificationService notifications) : ControllerBase
{
    /// <summary>Ids null / bỏ trống = đánh dấu đọc hết.</summary>
    public record ReadRequest(int[]? Ids);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        User.UserId() is { } uid ? Ok(await notifications.ListAsync(uid, ct)) : Unauthorized();

    [HttpPost("read")]
    public async Task<IActionResult> Read(ReadRequest body, CancellationToken ct) =>
        User.UserId() is { } uid ? Ok(new { unread = await notifications.MarkReadAsync(uid, body.Ids, ct) }) : Unauthorized();
}
