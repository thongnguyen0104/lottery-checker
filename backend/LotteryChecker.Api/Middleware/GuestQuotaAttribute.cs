using LotteryChecker.Api.Services;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace LotteryChecker.Api.Middleware;

public enum GuestAction { Scan, Check }

/// <summary>
/// Khách chưa đăng nhập: mỗi máy (cookie <see cref="VisitorId"/>) được xem kết quả dò 1 lần
/// (Guest:CheckLimit). Quét ảnh chỉ đọc số nên cho dư (Guest:ScanLimit = 3) — ảnh mờ phải chụp lại
/// không bị mất lượt. Quét nhiều vé thì máy chủ dò luôn → tính là lượt dò. Lượt đếm lại sau
/// Guest:ResetHours (24h). Hết lượt → 401, FE mở form đăng nhập.
///
/// Không đếm theo IP làm hạn mức chính: 4G (CGNAT) và wifi chung khiến rất nhiều người chung 1 IP —
/// người đầu dùng hết lượt thì người sau bị bắt đăng nhập ngay lần đầu. IP chỉ là chặn trên nới
/// (Guest:IpScanLimit/IpCheckLimit) để xoá cookie liên tục cũng không dò chùa mãi được.
///
/// Chỉ trừ lượt khi request thành công (2xx): ảnh lỗi/đọc hỏng không làm mất lượt dò thử.
/// Đã đăng nhập thì bỏ qua hoàn toàn.
/// </summary>
public class GuestQuotaAttribute(GuestAction action) : TypeFilterAttribute(typeof(GuestQuotaFilter))
{
    public GuestAction Action { get; } = action;
}

public class GuestQuotaFilter(AppDbContext db, IConfiguration config, TimeProvider clock) : IAsyncActionFilter
{
    public const string LoginRequiredError = "Bạn đã dùng hết lượt dò thử. Đăng nhập hoặc tạo tài khoản để dò tiếp nhé.";
    public const string LoginRequiredErrorEn = "You've used your free check. Log in or create an account to keep checking.";

    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        if (ctx.HttpContext.User.Identity?.IsAuthenticated == true) { await next(); return; }

        var action = ctx.ActionDescriptor.EndpointMetadata.OfType<GuestQuotaAttribute>().First().Action;
        var scan = action == GuestAction.Scan;
        var ip = ctx.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var now = clock.GetUtcNow().UtcDateTime;
        var buckets = new[]
        {
            (key: $"v:{VisitorId.Get(ctx.HttpContext)}",
             limit: scan ? config.GetValue("Guest:ScanLimit", 3) : config.GetValue("Guest:CheckLimit", 1)),
            (key: $"ip:{ip}",
             limit: scan ? config.GetValue("Guest:IpScanLimit", 60) : config.GetValue("Guest:IpCheckLimit", 20)),
        };

        var usages = new List<GuestUsage?>();
        foreach (var (key, limit) in buckets)
        {
            var usage = await Load(key, now);
            if ((scan ? usage?.Scans : usage?.Checks) >= limit)
            {
                ctx.Result = new ObjectResult(new { error = Lang.T(ctx.HttpContext.Request, LoginRequiredError, LoginRequiredErrorEn), code = "login_required" }) { StatusCode = 401 };
                return;
            }
            usages.Add(usage);
        }

        var done = await next();
        // Lúc này result chưa ghi ra response → xem mã trạng thái trên chính result.
        var ok = done.Exception == null && !done.Canceled
                 && (done.Result is not IStatusCodeActionResult r || r.StatusCode is null or < 300);
        if (!ok) return;

        for (var i = 0; i < buckets.Length; i++)
        {
            var usage = usages[i];
            if (usage == null)
                db.GuestUsages.Add(usage = new GuestUsage { Key = buckets[i].key, FirstAt = now });
            if (scan) usage.Scans++; else usage.Checks++;
        }
        await db.SaveChangesAsync();
    }

    // Quá Guest:ResetHours kể từ lượt đầu → đếm lại từ 0.
    private async Task<GuestUsage?> Load(string key, DateTime now)
    {
        var usage = await db.GuestUsages.FindAsync(key);
        if (usage != null && now - usage.FirstAt > TimeSpan.FromHours(config.GetValue("Guest:ResetHours", 24)))
        {
            usage.Scans = usage.Checks = 0;
            usage.FirstAt = now;
        }
        return usage;
    }
}
