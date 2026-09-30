namespace LotteryChecker.Api.Middleware;

/// <summary>
/// Định danh khách (chưa đăng nhập) theo máy: cookie ngẫu nhiên HttpOnly sống 1 năm, chưa có thì cấp.
/// Dùng cho like/dislike blog (mỗi máy 1 lượt) và lượt dò thử (<see cref="GuestQuotaFilter"/>).
/// </summary>
public static class VisitorId
{
    public const string Cookie = "dvs.vid";

    /// <summary>Id 32 ký tự hex của máy đang gọi. Phải gọi trước khi response bắt đầu ghi (để kịp set cookie).</summary>
    public static string Get(HttpContext ctx)
    {
        if (ctx.Request.Cookies.TryGetValue(Cookie, out var vid) && vid.Length == 32 && vid.All(char.IsAsciiHexDigit))
            return vid;
        vid = Guid.NewGuid().ToString("N");
        ctx.Response.Cookies.Append(Cookie, vid, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = ctx.Request.IsHttps,
            MaxAge = TimeSpan.FromDays(365), IsEssential = true,
        });
        return vid;
    }
}
