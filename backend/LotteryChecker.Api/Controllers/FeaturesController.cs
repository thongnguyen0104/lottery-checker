using LotteryChecker.Api.Data;
using LotteryChecker.Api.Middleware;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Controllers;

/// <summary>Cờ tính năng: FE hỏi để biết hiện/ẩn gì; admin bật/tắt ở trang Quản trị.</summary>
[ApiController]
public class FeaturesController(FeatureFlags flags, AppDbContext db) : ControllerBase
{
    public record SetRequest(bool Enabled);

    /// <summary>
    /// { key: dùng được không } cho người đang xem. enabled = trạng thái thật (admin cần để biết tính
    /// năng nào đang xem trước mà user chưa thấy).
    /// </summary>
    [HttpGet("/api/features")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var isAdmin = User.UserId() is { } uid && await db.Users.AnyAsync(x => x.Id == uid && x.IsAdmin, ct);
        var available = await flags.AvailableAsync(isAdmin, ct);
        var enabled = await flags.AvailableAsync(false, ct);
        return Ok(new { available, enabled });
    }

    [HttpGet("/api/admin/features")]
    [AdminOnly]
    public Task<FeatureFlags.FlagDto[]> List(CancellationToken ct) => flags.ListAsync(Lang.IsEn(Request), ct);

    [HttpPost("/api/admin/features/{key}")]
    [AdminOnly]
    public async Task<IActionResult> Set(string key, SetRequest body, CancellationToken ct) =>
        await flags.SetAsync(key, body.Enabled, User.Identity?.Name, ct)
            ? Ok(await flags.ListAsync(Lang.IsEn(Request), ct))
            : NotFound(new { error = Lang.T(Request, "Không có tính năng này.", "Unknown feature.") });
}
