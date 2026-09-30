using System.Security.Claims;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
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
    private string TakenError => Lang.T(Request, "Tên đăng nhập này đã có người dùng.", "This username is already taken.");
    private static readonly PasswordHasher<User> Hasher = new();

    public record Credentials(string? Username, string? Password);

    /// <summary>Username đã có người dùng chưa — FE gọi khi gõ ở form đăng ký.</summary>
    [HttpGet("check-username")]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<IActionResult> CheckUsername([FromQuery] string? username)
    {
        var u = AccountRules.Normalize(username);
        var error = AccountRules.UsernameError(u, Lang.IsEn(Request));
        if (error != null) return Ok(new { available = false, error });
        var taken = await db.Users.AnyAsync(x => x.Username == u);
        return Ok(new { available = !taken, error = taken ? TakenError : null });
    }

    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<IActionResult> Register(Credentials body)
    {
        var u = AccountRules.Normalize(body.Username);
        var error = AccountRules.UsernameError(u, Lang.IsEn(Request)) ?? AccountRules.PasswordError(body.Password, Lang.IsEn(Request));
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
        return Ok(ToAccount(user));
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
            return Unauthorized(new { error = Lang.T(Request, "Sai tên đăng nhập hoặc mật khẩu.", "Wrong username or password.") });

        await SignInAsync(user);
        return Ok(ToAccount(user));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    /// <summary>
    /// Phiên hiện tại — FE gọi lúc mở app để biết đã đăng nhập chưa. Đọc DB (không chỉ cookie) để
    /// quyền admin / cờ phải đổi mật khẩu luôn mới; tài khoản đã bị xoá thì coi như chưa đăng nhập.
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (User.UserId() is not { } uid) return Unauthorized();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == uid);
        return user == null ? Unauthorized() : Ok(ToAccount(user));
    }

    public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    /// <summary>Đổi mật khẩu của chính mình (cần mật khẩu hiện tại). Xong thì tắt cờ phải đổi mật khẩu.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest body)
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == User.UserId()!.Value);
        if (user == null) return Unauthorized();
        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, body.CurrentPassword ?? "") is PasswordVerificationResult.Failed)
            return BadRequest(new { error = Lang.T(Request, "Mật khẩu hiện tại không đúng.", "Your current password is wrong.") });
        var error = AccountRules.PasswordError(body.NewPassword, Lang.IsEn(Request));
        if (error != null) return BadRequest(new { error });
        if (body.NewPassword == body.CurrentPassword)
            return BadRequest(new { error = Lang.T(Request, "Mật khẩu mới phải khác mật khẩu hiện tại.", "The new password must differ from the current one.") });

        user.PasswordHash = Hasher.HashPassword(user, body.NewPassword!);
        user.MustChangePassword = false;
        await db.SaveChangesAsync();
        return Ok(ToAccount(user));
    }

    private static object ToAccount(User u) => new { username = u.Username, isAdmin = u.IsAdmin, mustChangePassword = u.MustChangePassword };

    /// <summary>Dùng chung với trang quản trị (admin đặt lại mật khẩu cho user).</summary>
    public static string HashPassword(User user, string password) => Hasher.HashPassword(user, password);

    private Task SignInAsync(User user) => HttpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Username)],
            CookieAuthenticationDefaults.AuthenticationScheme)),
        new AuthenticationProperties { IsPersistent = true });
}
