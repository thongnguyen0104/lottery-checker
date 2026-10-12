using LotteryChecker.Api.Middleware;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace LotteryChecker.Api.Controllers;

/// <summary>Màn Vietlott: kết quả Mega 6/45 / Power 6/55 + thống kê, gợi ý số kỳ tới.</summary>
[ApiController]
[RequireFeature(FeatureFlags.Vietlott)]
public class VietlottController(VietlottStatsService stats, IMemoryCache cache, TimeProvider clock) : ControllerBase
{
    public const int MaxPageSize = 50;
    /// <summary>Khoảng thống kê cho phép (0 = toàn bộ lịch sử) — giới hạn để cache không phình theo tham số.</summary>
    public static readonly int[] Windows = [0, 50, 100, 200, 500];

    /// <summary>Danh sách kỳ quay mới → cũ, phân trang.</summary>
    [HttpGet("/api/vietlott/{game}")]
    public async Task<ActionResult<VietlottStatsService.ResultsDto>> Results(string game, [FromQuery] int page = 0,
                                                                             [FromQuery] int size = 20, CancellationToken ct = default)
    {
        if (!VietlottScraper.IsGame(game)) return NotFound();
        page = Math.Max(0, page);
        size = Math.Clamp(size, 1, MaxPageSize);
        return await stats.ResultsAsync(game, page, size, NextDraw(game), ct);
    }

    /// <summary>Thống kê + gợi ý cho kỳ tới. last: chỉ tính N kỳ gần nhất (0 = toàn bộ).</summary>
    [HttpGet("/api/vietlott/{game}/stats")]
    public async Task<ActionResult<VietlottStatsService.StatsDto>> Stats(string game, [FromQuery] int last = 0,
                                                                         CancellationToken ct = default)
    {
        if (!VietlottScraper.IsGame(game)) return NotFound();
        if (!Windows.Contains(last))
            return BadRequest(new { error = Lang.T(Request, "Khoảng thống kê không hợp lệ.", "Invalid statistics window.") });

        var next = NextDraw(game);
        // Dữ liệu chỉ đổi khi có kỳ mới (3 lần/tuần) → cache ngắn là đủ, đỡ tính lại backtest mỗi lượt xem.
        return await cache.GetOrCreateAsync($"vietlott:{game}:{last}:{next:yyyyMMdd}", e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return stats.StatsAsync(game, last, next, ct);
        }) ?? throw new InvalidOperationException();
    }

    private DateOnly NextDraw(string game) => VietlottScraper.NextDrawDate(game, DrawSchedule.NowVn(clock));
}
