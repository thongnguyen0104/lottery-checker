using LotteryChecker.Api.Data;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Middleware;

/// <summary>
/// Chặn API của tính năng đang tắt (404 + code "feature_disabled") — ẩn ở FE thôi thì vẫn gọi thẳng API
/// được. Admin luôn qua để xem trước tính năng trước khi bật cho mọi người.
/// </summary>
public class RequireFeatureAttribute(string key) : TypeFilterAttribute(typeof(RequireFeatureFilter))
{
    public string Key { get; } = key;
}

public class RequireFeatureFilter(FeatureFlags flags, AppDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var key = ctx.ActionDescriptor.EndpointMetadata.OfType<RequireFeatureAttribute>().Last().Key;
        var ct = ctx.HttpContext.RequestAborted;
        if (await flags.IsEnabledAsync(key, ct) || await IsAdminAsync(ctx, ct)) { await next(); return; }

        ctx.Result = new NotFoundObjectResult(new
        {
            error = Lang.T(ctx.HttpContext.Request, "Tính năng này hiện chưa mở.", "This feature isn't available yet."),
            code = "feature_disabled",
        });
    }

    private async Task<bool> IsAdminAsync(ActionExecutingContext ctx, CancellationToken ct) =>
        ctx.HttpContext.User.UserId() is { } uid
        && await db.Users.AnyAsync(x => x.Id == uid && x.IsAdmin, ct);
}
