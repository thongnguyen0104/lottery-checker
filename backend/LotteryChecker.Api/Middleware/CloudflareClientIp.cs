using System.Net;

namespace LotteryChecker.Api.Middleware;

/// <summary>
/// Chạy sau Cloudflare Tunnel (cloudflared → Caddy → Kestrel, tất cả trên loopback): IP kết nối luôn là
/// 127.0.0.1, còn X-Forwarded-For thì Caddy ghi đè bằng IP của cloudflared → mọi người dùng chung
/// 1 hạn mức rate limit theo IP. Cloudflare luôn đặt IP thật vào header CF-Connecting-IP (và ghi đè
/// nếu client tự gửi), nên lấy IP từ đó.
///
/// CHỈ bật (Cloudflare:TrustConnectingIp=true) khi server không mở port 80/443 ra internet — đi được
/// thẳng vào Caddy thì ai cũng giả được header này để né rate limit.
/// </summary>
public static class CloudflareClientIp
{
    public const string Header = "CF-Connecting-IP";

    public static IApplicationBuilder UseCloudflareClientIp(this IApplicationBuilder app) =>
        app.Use((ctx, next) =>
        {
            var remote = ctx.Connection.RemoteIpAddress;
            if (remote != null && IPAddress.IsLoopback(remote)
                && IPAddress.TryParse(ctx.Request.Headers[Header].ToString(), out var real))
                ctx.Connection.RemoteIpAddress = real;
            return next(ctx);
        });
}
