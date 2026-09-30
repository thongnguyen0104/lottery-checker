using LotteryChecker.Api.Middleware;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using static LotteryChecker.Api.Services.AdminUsers;

namespace LotteryChecker.Api.Controllers;

/// <summary>Trang quản trị: quản lý user, cộng/trừ số dư, đặt lại mật khẩu, cấp quyền admin. Chỉ admin.</summary>
[ApiController]
[AdminOnly]
[Route("api/admin")]
public class AdminUsersController(AdminUsers admin, Wallet wallet) : ControllerBase
{
    public record BalanceRequest(long Amount, string? Note);
    public record PasswordRequest(string? Password);
    public record RoleRequest(bool IsAdmin);

    [HttpGet("overview")]
    public Task<Overview> Overview(CancellationToken ct) => admin.GetOverviewAsync(ct);

    [HttpGet("users")]
    public Task<UserPage> Users([FromQuery] string? search, [FromQuery] string? sort, [FromQuery] int page = 1,
                                CancellationToken ct = default) =>
        admin.ListAsync(search, sort, page, ct);

    [HttpGet("users/{id:int}")]
    public async Task<IActionResult> User(int id, CancellationToken ct) =>
        await admin.GetAsync(id, ct) is { } u ? Ok(u) : NotFound(new { error = NotFoundError });

    [HttpGet("users/{id:int}/transactions")]
    public Task<TransactionPage> Transactions(int id, [FromQuery] int page = 1, CancellationToken ct = default) =>
        admin.TransactionsAsync(id, page, ct);

    /// <summary>amount &gt; 0 cộng tiền, &lt; 0 trừ tiền. Ghi chú kèm tên admin để sổ giao dịch biết ai làm.</summary>
    [HttpPost("users/{id:int}/balance")]
    public async Task<IActionResult> Balance(int id, BalanceRequest body, CancellationToken ct)
    {
        var note = body.Note?.Trim() is { Length: > 0 } n ? n : Lang.T(Request, "Admin điều chỉnh", "Admin adjustment");
        note = $"[{base.User.Identity?.Name}] {note}";
        var (balance, error) = await wallet.AdjustAsync(id, body.Amount, note[..Math.Min(note.Length, 200)], ct);
        return error switch
        {
            Wallet.AdjustError.None => Ok(new { balance }),
            Wallet.AdjustError.NoAccount => NotFound(new { error = NotFoundError }),
            Wallet.AdjustError.BadAmount => BadRequest(new { error = Lang.T(Request,
                $"Số tiền phải khác 0 và không quá {Lang.Num(Request, Wallet.MaxTopUp)} ₫.", $"Amount must be non-zero and at most {Lang.Num(Request, Wallet.MaxTopUp)} VND.") }),
            _ => Conflict(new { error = Lang.T(Request,
                $"Không trừ được: số dư hiện chỉ còn {Lang.Num(Request, balance)} ₫.", $"Can't deduct: the balance is only {Lang.Num(Request, balance)} VND.") }),
        };
    }

    /// <summary>Đặt mật khẩu tạm — user phải đổi lại khi đăng nhập.</summary>
    [HttpPost("users/{id:int}/password")]
    public async Task<IActionResult> Password(int id, PasswordRequest body, CancellationToken ct)
    {
        var error = AccountRules.PasswordError(body.Password, Lang.IsEn(Request));
        if (error != null) return BadRequest(new { error });
        return await admin.ResetPasswordAsync(id, body.Password!, ct) ? NoContent() : NotFound(new { error = NotFoundError });
    }

    [HttpPost("users/{id:int}/role")]
    public async Task<IActionResult> Role(int id, RoleRequest body, CancellationToken ct)
    {
        // Tự bỏ quyền của mình dễ khoá luôn trang quản trị (nếu là admin duy nhất) — nhờ admin khác làm.
        if (id == base.User.UserId())
            return BadRequest(new { error = Lang.T(Request, "Không thể tự đổi quyền của chính mình.", "You can't change your own role.") });
        return await admin.SetAdminAsync(id, body.IsAdmin, ct) ? NoContent() : NotFound(new { error = NotFoundError });
    }

    private string NotFoundError => Lang.T(Request, "Không tìm thấy tài khoản.", "Account not found.");
}
