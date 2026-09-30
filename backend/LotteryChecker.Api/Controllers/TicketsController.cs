using LotteryChecker.Api.Middleware;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using static LotteryChecker.Api.Services.ScratchTicketService;

namespace LotteryChecker.Api.Controllers;

/// <summary>Vé cào 2 số: xem quầy bán, mua, danh sách "Vé của tôi", cào xem kết quả. Cần đăng nhập.</summary>
[ApiController]
[Authorize]
[Route("api/tickets")]
[RequireFeature(FeatureFlags.ScratchTickets)]
public class TicketsController(ScratchTicketService tickets) : ControllerBase
{
    public const string BuyRateLimitPolicy = "ticket-buy";

    /// <summary>Ngày đang bán, các đài, giá, giờ ngừng bán, số dư hiện tại.</summary>
    [HttpGet("shop")]
    public async Task<IActionResult> Shop(CancellationToken ct) =>
        await tickets.GetShopAsync(User.UserId()!.Value, ct) is { } shop ? Ok(shop) : Unauthorized();

    [HttpGet]
    public Task<PageDto> List([FromQuery] int page = 1, CancellationToken ct = default) =>
        tickets.ListAsync(User.UserId()!.Value, page, ct);

    [HttpPost]
    [EnableRateLimiting(BuyRateLimitPolicy)]
    public async Task<IActionResult> Buy(BuyRequest body, CancellationToken ct)
    {
        var (result, error) = await tickets.BuyAsync(User.UserId()!.Value, body, ct);
        return error switch
        {
            BuyError.None => Ok(result),
            BuyError.NoAccount => Unauthorized(),
            BuyError.BadQuantity => BadRequest(new { error = Lang.T(Request,
                $"Mỗi lần mua từ 1 đến {MaxQuantity} vé.", $"You can buy 1 to {MaxQuantity} tickets at a time.") }),
            BuyError.BadProvince => BadRequest(new { error = Lang.T(Request,
                "Đài này không xổ vào ngày đang bán vé.", "This province doesn't draw on the current sales day.") }),
            BuyError.Closed => Conflict(new { error = Lang.T(Request,
                "Đã hết giờ bán vé cho kỳ này. Tải lại để mua vé kỳ tiếp theo.",
                "Sales for this draw have closed. Reload to buy for the next draw."), code = "closed" }),
            BuyError.InsufficientBalance => Conflict(new { error = Lang.T(Request,
                "Số dư không đủ để mua số vé này.", "Your balance isn't enough for these tickets."), code = "insufficient" }),
            _ => Conflict(new { error = Lang.T(Request,
                "Số dư vừa thay đổi, bạn bấm mua lại nhé.", "Your balance just changed — please try again.") }),
        };
    }

    /// <summary>Đánh dấu đã cào (tiền thưởng đã cộng lúc chốt, đây chỉ để lần sau không phải cào lại).</summary>
    [HttpPost("{id:int}/scratch")]
    public async Task<IActionResult> Scratch(int id, CancellationToken ct) =>
        await tickets.ScratchAsync(User.UserId()!.Value, id, ct) is { } t
            ? Ok(t)
            : NotFound(new { error = Lang.T(Request, "Không tìm thấy vé.", "Ticket not found.") });
}
