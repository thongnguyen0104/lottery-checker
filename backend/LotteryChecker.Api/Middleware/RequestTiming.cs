using System.Diagnostics;

namespace LotteryChecker.Api.Middleware;

/// <summary>
/// Ghi mốc bắt đầu của mỗi request và log tổng thời gian khi đã trả lời xong.
///
/// Vì sao phải lấy mốc ở middleware chứ không <c>new Stopwatch()</c> trong controller: với
/// multipart upload, thời gian NHẬN ảnh từ điện thoại lên server nằm ở khâu model binding —
/// chạy TRƯỚC khi action bắt đầu. Đo trong action sẽ bỏ sót đúng phần chậm nhất khi mạng yếu
/// (3G/4G yếu có thể mất vài giây chỉ để đẩy 1 tấm ảnh lên).
/// </summary>
public static class RequestTiming
{
    private const string StartKey = "__RequestStartTicks";

    public static IApplicationBuilder UseRequestTiming(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            var start = Stopwatch.GetTimestamp();
            ctx.Items[StartKey] = start;
            try
            {
                await next();
            }
            finally
            {
                // Chỉ log /api/* — bỏ static file và /health (uptime check gọi liên tục) để log đỡ ngập.
                if (ctx.Request.Path.StartsWithSegments("/api"))
                {
                    var elapsedMs = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 1);
                    ctx.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("RequestTiming")
                        .LogInformation("{Method} {Path} → {StatusCode} trong {ElapsedMs}ms",
                            ctx.Request.Method, ctx.Request.Path.Value, ctx.Response.StatusCode, elapsedMs);
                }
            }
        });

    /// <summary>
    /// Mốc lúc Kestrel nhận request. Null khi middleware chưa chạy (vd test gọi thẳng
    /// controller) — lúc đó <see cref="Services.StageTimer"/> tự lấy mốc hiện tại.
    /// </summary>
    public static long? RequestStartTicks(this HttpContext ctx) =>
        ctx.Items.TryGetValue(StartKey, out var value) && value is long ticks ? ticks : null;
}
