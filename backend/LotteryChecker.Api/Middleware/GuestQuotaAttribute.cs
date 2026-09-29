using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Middleware;

public enum GuestAction { Scan, Check }

/// <summary>
/// Khách chưa đăng nhập: mỗi IP được xem kết quả dò 1 lần (Guest:CheckLimit). Quét ảnh chỉ đọc số nên
/// cho dư (Guest:ScanLimit = 3) — ảnh mờ phải chụp lại không bị mất lượt. Quét nhiều vé thì máy chủ dò
/// luôn → tính là lượt dò. Lượt đếm lại sau Guest:ResetHours (24h). Hết lượt → 401, FE mở form đăng nhập.
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

    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        if (ctx.HttpContext.User.Identity?.IsAuthenticated == true) { await next(); return; }

        var action = ctx.ActionDescriptor.EndpointMetadata.OfType<GuestQuotaAttribute>().First().Action;
        var ip = ctx.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var usage = await db.GuestUsages.FindAsync(ip);
        var now = clock.GetUtcNow().UtcDateTime;
        if (usage != null && now - usage.FirstAt > TimeSpan.FromHours(config.GetValue("Guest:ResetHours", 24)))
        {
            usage.Scans = usage.Checks = 0;
            usage.FirstAt = now;
        }
        var (used, limit) = action == GuestAction.Scan
            ? (usage?.Scans ?? 0, config.GetValue("Guest:ScanLimit", 3))
            : (usage?.Checks ?? 0, config.GetValue("Guest:CheckLimit", 1));
        if (used >= limit)
        {
            ctx.Result = new ObjectResult(new { error = LoginRequiredError, code = "login_required" }) { StatusCode = 401 };
            return;
        }

        var done = await next();
        // Lúc này result chưa ghi ra response → xem mã trạng thái trên chính result.
        var ok = done.Exception == null && !done.Canceled
                 && (done.Result is not IStatusCodeActionResult r || r.StatusCode is null or < 300);
        if (!ok) return;

        if (usage == null)
            db.GuestUsages.Add(usage = new GuestUsage { Ip = ip, FirstAt = now });
        if (action == GuestAction.Scan) usage.Scans++; else usage.Checks++;
        await db.SaveChangesAsync();
    }
}
