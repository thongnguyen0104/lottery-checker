using LotteryChecker.Api.Middleware;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static LotteryChecker.Api.Services.SavedTicketService;

namespace LotteryChecker.Api.Controllers;

/// <summary>Vé đã lưu: lưu để hệ thống tự dò sau giờ xổ và nhắc lĩnh thưởng. Cần đăng nhập.</summary>
[ApiController]
[Authorize]
[Route("api/saved-tickets")]
[RequireFeature(FeatureFlags.SavedTickets)]
public class SavedTicketsController(SavedTicketService tickets) : ControllerBase
{
    public record ClaimedRequest(bool Claimed);

    [HttpPost]
    public async Task<IActionResult> Save(SaveRequest body, CancellationToken ct)
    {
        var (t, err) = await tickets.SaveAsync(User.UserId()!.Value, body, ct);
        return t != null ? Ok(t) : Error(err);
    }

    [HttpPost("bulk")]
    public async Task<IActionResult> SaveMany(BulkRequest body, CancellationToken ct)
    {
        if (body.Items is not { Length: > 0 and <= MaxBulk })
            return BadRequest(new { error = Lang.T(Request, $"Mỗi lần lưu từ 1 đến {MaxBulk} vé.", $"Save 1 to {MaxBulk} tickets at a time.") });
        var (saved, err) = await tickets.SaveManyAsync(User.UserId()!.Value, body.Items, ct);
        return saved.Length == 0 ? Error(err) : Ok(new { items = saved, error = err == SaveError.None ? null : Message(err) });
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        SavedTicketStatus? s = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (!Enum.TryParse<SavedTicketStatus>(status, true, out var parsed)) return BadRequest();
            s = parsed;
        }
        return Ok(await tickets.ListAsync(User.UserId()!.Value, s, page, ct));
    }

    [HttpGet("summary")]
    public Task<SummaryDto> Summary(CancellationToken ct) => tickets.SummaryAsync(User.UserId()!.Value, ct);

    [HttpPatch("{id:int}/claimed")]
    public async Task<IActionResult> Claimed(int id, ClaimedRequest body, CancellationToken ct) =>
        await tickets.MarkClaimedAsync(User.UserId()!.Value, id, body.Claimed, ct) is { } t ? Ok(t) : NotFound(NotFoundBody());

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await tickets.DeleteAsync(User.UserId()!.Value, id, ct) ? NoContent() : NotFound(NotFoundBody());

    private object NotFoundBody() => new { error = Lang.T(Request, "Không tìm thấy vé.", "Ticket not found.") };

    private IActionResult Error(SaveError err) => err == SaveError.LimitReached
        ? StatusCode(429, new { error = Message(err), code = "limit" })
        : BadRequest(new { error = Message(err), code = err.ToString() });

    private string Message(SaveError err) => err switch
    {
        SaveError.BadNumber => Lang.T(Request, "Số vé phải đúng 6 chữ số.", "Ticket number must be exactly 6 digits."),
        SaveError.BadProvince => Lang.T(Request, "Đài không hợp lệ.", "Unknown province."),
        SaveError.NotSouth => Lang.T(Request, "Hiện chỉ hỗ trợ lưu vé Miền Nam.", "Only Southern (MN) tickets can be saved for now."),
        SaveError.BadDate => Lang.T(Request, "Đài này không xổ vào ngày đã chọn.", "This province doesn't draw on that date."),
        SaveError.Expired => Lang.T(Request, "Vé đã quá hạn lĩnh thưởng (30 ngày).", "This ticket is past the 30-day claim period."),
        SaveError.TooFar => Lang.T(Request, $"Chỉ lưu được vé xổ trong {MaxDaysAhead} ngày tới.", $"You can only save tickets drawing in the next {MaxDaysAhead} days."),
        SaveError.LimitReached => Lang.T(Request, $"Bạn đang có {MaxWaiting} vé chờ kết quả — xoá bớt rồi lưu tiếp nhé.",
                                                  $"You already have {MaxWaiting} tickets waiting — remove some first."),
        _ => Lang.T(Request, "Không lưu được vé.", "Couldn't save the ticket."),
    };
}
