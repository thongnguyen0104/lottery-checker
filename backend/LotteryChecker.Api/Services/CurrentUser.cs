using System.Security.Claims;

namespace LotteryChecker.Api.Services;

public static class CurrentUser
{
    /// <summary>Id tài khoản đang đăng nhập (claim do AuthController đặt); null = khách.</summary>
    public static int? UserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
