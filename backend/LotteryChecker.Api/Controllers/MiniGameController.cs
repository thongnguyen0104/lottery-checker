using LotteryChecker.Api.Middleware;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using static LotteryChecker.Api.Services.MiniGameService;

namespace LotteryChecker.Api.Controllers;

/// <summary>Minigame đua xe kiếm xu ảo: xem lượt còn lại, bắt đầu lượt (nhận seed), kết thúc (gửi thao tác). Cần đăng nhập.</summary>
[ApiController]
[Authorize]
[Route("api/minigame")]
[RequireFeature(FeatureFlags.MiniGame)]
public class MiniGameController(MiniGameService game) : ControllerBase
{
    public const string RateLimitPolicy = "minigame";

    [HttpGet]
    public async Task<IActionResult> Status(CancellationToken ct) =>
        await game.GetStatusAsync(User.UserId()!.Value, ct) is { } s ? Ok(s) : Unauthorized();

    [HttpPost("start")]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<IActionResult> Start(CancellationToken ct) =>
        await game.StartAsync(User.UserId()!.Value, ct) is { } s ? Ok(s) : Unauthorized();

    [HttpPost("finish")]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<IActionResult> Finish(FinishRequest body, CancellationToken ct)
    {
        var (result, error) = await game.FinishAsync(User.UserId()!.Value, body, ct);
        return error switch
        {
            FinishError.None => Ok(result),
            FinishError.NotFound => NotFound(new { error = Lang.T(Request, "Không tìm thấy lượt chơi.", "Game not found.") }),
            FinishError.AlreadyFinished => Conflict(new { error = Lang.T(Request,
                "Lượt này đã được tính rồi.", "This game has already been scored."), code = "finished" }),
            FinishError.Expired => Conflict(new { error = Lang.T(Request,
                "Lượt chơi đã hết hạn (quá lâu hoặc bạn đã mở lượt khác).",
                "This game has expired (too long, or you started another one)."), code = "expired" }),
            FinishError.BadInputs => BadRequest(new { error = Lang.T(Request,
                "Dữ liệu lượt chơi không hợp lệ.", "Invalid game data.") }),
            FinishError.TooFast => BadRequest(new { error = Lang.T(Request,
                "Lượt chơi kết thúc nhanh bất thường — không được tính.", "The game ended suspiciously fast and wasn't scored."),
                code = "tooFast" }),
            _ => Conflict(new { error = Lang.T(Request, "Hệ thống đang bận, bạn thử lại nhé.", "Busy — please try again.") }),
        };
    }
}
