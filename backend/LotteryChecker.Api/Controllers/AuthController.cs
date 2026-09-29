using System.Security.Claims;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Controllers;

/// <summary>Đăng ký / đăng nhập bằng username + mật khẩu. Phiên lưu trong cookie HttpOnly (xem Program.cs).</summary>
[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    public const string RateLimitPolicy = "auth";
    private const string TakenError = "Tên đăng nhập này đã có người dùng.";
    private static readonly PasswordHasher<User> Hasher = new();

    public record Credentials(string? Username, string? Password);

    /// <summary>Username đã có người dùng chưa — FE gọi khi gõ ở form đăng ký.</summary>
    [HttpGet("check-username")]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<IActionResult> CheckUsername([FromQuery] string? username)
    {
        var u = AccountRules.Normalize(username);
        var error = AccountRules.UsernameError(u);
        if (error != null) return Ok(new { available = false, error });
        var taken = await db.Users.AnyAsync(x => x.Username == u);
        return Ok(new { available = !taken, error = taken ? TakenError : null });
    }

    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<IActionResult> Register(Credentials body)
    {
        var u = AccountRules.Normalize(body.Username);
        var error = AccountRules.UsernameError(u) ?? AccountRules.PasswordError(body.Password);
        if (error != null) return BadRequest(new { error });
        if (await db.Users.AnyAsync(x => x.Username == u)) return Conflict(new { error = TakenError });

        var user = new User { Username = u, CreatedAt = clock.GetUtcNow().UtcDateTime };
        user.PasswordHash = Hasher.HashPassword(user, body.Password!);
        db.Users.Add(user);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) // 2 người đăng ký cùng tên cùng lúc — index unique chặn
        {
            return Conflict(new { error = TakenError });
        }

        await SignInAsync(user);
        return Ok(new { username = user.Username });
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<IActionResult> Login(Credentials body)
    {
        var u = AccountRules.Normalize(body.Username);
        var user = await db.Users.FirstOrDefaultAsync(x => x.Username == u);
        // Cùng một câu cho sai tên và sai mật khẩu — không để lộ tên nào đã tồn tại.
        if (user == null || Hasher.VerifyHashedPassword(user, user.PasswordHash, body.Password ?? "")
                is PasswordVerificationResult.Failed)
            return Unauthorized(new { error = "Sai tên đăng nhập hoặc mật khẩu." });

        await SignInAsync(user);
        return Ok(new { username = user.Username });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    /// <summary>Phiên hiện tại — FE gọi lúc mở app để biết đã đăng nhập chưa.</summary>
    [HttpGet("me")]
    public IActionResult Me() => User.Identity?.IsAuthenticated == true
        ? Ok(new { username = User.Identity.Name })
        : Unauthorized();

    private Task SignInAsync(User user) => HttpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Username)],
            CookieAuthenticationDefaults.AuthenticationScheme)),
        new AuthenticationProperties { IsPersistent = true });
}
