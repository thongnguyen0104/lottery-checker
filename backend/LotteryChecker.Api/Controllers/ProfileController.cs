using LotteryChecker.Api.Data;
using LotteryChecker.Api.Middleware;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Controllers;

/// <summary>Trang Tài khoản: thông tin, số dư, lịch sử dò vé — chỉ cho người đã đăng nhập.</summary>
[ApiController]
[Authorize]
[Route("api/profile")]
public class ProfileController(AppDbContext db, CheckHistory history) : ControllerBase
{
    public record ProfileDto(string Username, DateTime CreatedAt, long Balance, CheckHistory.Summary Checks);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var uid = User.UserId()!.Value;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == uid, ct);
        // Cookie còn hạn nhưng tài khoản đã bị xoá → coi như chưa đăng nhập.
        if (user == null) return Unauthorized();
        return Ok(new ProfileDto(user.Username, DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc), user.Balance,
                                 await history.GetSummaryAsync(uid, ct)));
    }

    [HttpGet("history")]
    [RequireFeature(FeatureFlags.CheckHistory)]
    public Task<CheckHistory.PageDto> History([FromQuery] int page = 1, CancellationToken ct = default) =>
        history.ListAsync(User.UserId()!.Value, page, ct);
}
