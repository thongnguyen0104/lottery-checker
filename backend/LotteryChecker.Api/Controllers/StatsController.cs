using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace LotteryChecker.Api.Controllers;

[ApiController]
public class StatsController(CheckStats stats, IMemoryCache cache) : ControllerBase
{
    /// <summary>Thống kê vé đã dò của cả hệ thống — màn Dò vé hiện mỗi lần mở; cache ngắn cho đỡ đếm lại.</summary>
    [HttpGet("/api/stats")]
    public async Task<CheckStats.Summary> Get(CancellationToken ct) =>
        (await cache.GetOrCreateAsync("stats:checks", e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return stats.GetAsync(ct);
        }))!;
}
