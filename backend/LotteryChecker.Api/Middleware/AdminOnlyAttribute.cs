using LotteryChecker.Api.Data;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Middleware;

/// <summary>
/// Chỉ admin: tra DB mỗi request chứ không tin claim trong cookie — thu quyền admin là mất quyền ngay,
/// không đợi cookie (30 ngày) hết hạn. Admin còn mật khẩu mặc định / vừa bị đặt lại thì phải đổi trước.
/// </summary>
public class AdminOnlyAttribute() : TypeFilterAttribute(typeof(AdminOnlyFilter));

public class AdminOnlyFilter(AppDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var req = ctx.HttpContext.Request;
        if (ctx.HttpContext.User.UserId() is not { } uid) { ctx.Result = new UnauthorizedResult(); return; }

        var user = await db.Users.AsNoTracking().Where(x => x.Id == uid)
            .Select(x => new { x.IsAdmin, x.MustChangePassword }).FirstOrDefaultAsync();
        if (user is not { IsAdmin: true })
        {
            ctx.Result = new ObjectResult(new { error = Lang.T(req, "Bạn không có quyền quản trị.", "You don't have admin access.") })
                { StatusCode = 403 };
            return;
        }
        if (user.MustChangePassword)
        {
            ctx.Result = new ObjectResult(new
            {
                error = Lang.T(req, "Bạn cần đổi mật khẩu trước khi dùng trang quản trị.", "Change your password before using the admin page."),
                code = "must_change_password",
            }) { StatusCode = 403 };
            return;
        }
        await next();
    }
}
