using System.Security.Cryptography;
using System.Text;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LotteryChecker.Api.Controllers;

/// <summary>
/// Admin cộng tiền (ảo) vào số dư user. Chạy cả ở production nên bắt buộc khoá: header X-Admin-Key
/// phải khớp Admin:ApiKey. Chưa cấu hình khoá = endpoint tắt (404).
///
/// curl -X POST https://…/api/admin/wallet/topup -H "X-Admin-Key: …" -H "Content-Type: application/json" \
///      -d '{"username":"abc1234567","amount":100000,"note":"tặng"}'
/// </summary>
[ApiController]
public class AdminWalletController(Wallet wallet, IConfiguration config) : ControllerBase
{
    public record TopUpRequest(string? Username, long Amount, string? Note);

    [HttpPost("/api/admin/wallet/topup")]
    [EnableRateLimiting(AuthController.RateLimitPolicy)] // chặn dò khoá admin
    public async Task<IActionResult> TopUp(TopUpRequest body, [FromHeader(Name = "X-Admin-Key")] string? key,
                                           CancellationToken ct)
    {
        var expected = config["Admin:ApiKey"];
        if (string.IsNullOrWhiteSpace(expected)) return NotFound();
        if (key == null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(expected)))
            return Unauthorized();

        if (body.Amount is <= 0 or > Wallet.MaxTopUp)
            return BadRequest(new { error = $"amount phải từ 1 đến {Wallet.MaxTopUp:N0}." });
        var note = string.IsNullOrWhiteSpace(body.Note) ? "Admin cộng tiền" : body.Note.Trim()[..Math.Min(body.Note.Trim().Length, 200)];

        var balance = await wallet.TopUpAsync(body.Username ?? "", body.Amount, note, ct);
        return balance == null
            ? NotFound(new { error = "Không có tài khoản này." })
            : Ok(new { username = AccountRules.Normalize(body.Username), balance });
    }
}
