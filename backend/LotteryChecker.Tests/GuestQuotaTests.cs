using System.Net;
using FluentAssertions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LotteryChecker.Tests;

// Lượt dò thử của khách: đếm theo máy (cookie), IP chỉ là chặn trên nới.
public class GuestQuotaTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private GuestQuotaFilter Filter(Dictionary<string, string?>? cfg = null) => new(_db,
        new ConfigurationBuilder().AddInMemoryCollection(cfg ?? []).Build(), TimeProvider.System);

    private static string Visitor(int n) => n.ToString("x32");

    /// <summary>Chạy filter cho 1 request; trả mã trạng thái (401 = bị bắt đăng nhập).</summary>
    private static async Task<int> Run(GuestQuotaFilter filter, string? visitor, string ip = "1.2.3.4",
                                       GuestAction action = GuestAction.Check, IActionResult? actionResult = null)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (visitor != null) http.Request.Headers.Cookie = $"{VisitorId.Cookie}={visitor}";
        var descriptor = new ActionDescriptor { EndpointMetadata = [new GuestQuotaAttribute(action)] };
        var actionCtx = new ActionContext(http, new RouteData(), descriptor);
        var ctx = new ActionExecutingContext(actionCtx, [], new Dictionary<string, object?>(), null!);
        var result = actionResult ?? new OkResult();
        await filter.OnActionExecutionAsync(ctx, () =>
            Task.FromResult(new ActionExecutedContext(actionCtx, [], null!) { Result = result }));
        return ctx.Result is ObjectResult { StatusCode: { } code } ? code : 200;
    }

    [Fact(DisplayName = "Nhieu may chung 1 IP (4G/wifi) deu duoc do thu lan dau")]
    public async Task SharedIp_EachVisitorGetsFirstCheck()
    {
        var f = Filter();
        (await Run(f, Visitor(1))).Should().Be(200);
        (await Run(f, Visitor(2))).Should().Be(200);
        (await Run(f, Visitor(3))).Should().Be(200);
    }

    [Fact(DisplayName = "Cung 1 may do lan 2 thi bat dang nhap")]
    public async Task SameVisitor_SecondCheckRequiresLogin()
    {
        var f = Filter();
        (await Run(f, Visitor(1))).Should().Be(200);
        (await Run(f, Visitor(1))).Should().Be(401);
    }

    [Fact(DisplayName = "Quet anh cho 3 luot/may, tach rieng luot do")]
    public async Task Scan_HasOwnLimit()
    {
        var f = Filter();
        for (var i = 0; i < 3; i++) (await Run(f, Visitor(1), action: GuestAction.Scan)).Should().Be(200);
        (await Run(f, Visitor(1), action: GuestAction.Scan)).Should().Be(401);
        (await Run(f, Visitor(1))).Should().Be(200);
    }

    [Fact(DisplayName = "Xoa cookie lien tuc van bi chan tren theo IP")]
    public async Task IpBackstop_LimitsCookieClearing()
    {
        var f = Filter(new() { ["Guest:IpCheckLimit"] = "2" });
        (await Run(f, null)).Should().Be(200);
        (await Run(f, null)).Should().Be(200);
        (await Run(f, null)).Should().Be(401);
        (await Run(f, null, ip: "5.6.7.8")).Should().Be(200);
    }

    [Fact(DisplayName = "Request loi khong tru luot")]
    public async Task FailedRequest_DoesNotConsume()
    {
        var f = Filter();
        (await Run(f, Visitor(1), actionResult: new BadRequestResult())).Should().Be(200);
        (await Run(f, Visitor(1))).Should().Be(200);
        (await Run(f, Visitor(1))).Should().Be(401);
    }
}
