using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace LotteryChecker.Api.Controllers;

/// <summary>Màn Dự đoán: thống kê 1 năm + gợi ý số cho từng đài của một ngày xổ.</summary>
[ApiController]
public class PredictController(PredictionService predictions, IMemoryCache cache, TimeProvider clock) : ControllerBase
{
    /// <summary>Xem lại được các ngày đã xổ trong 30 ngày (đối chiếu gợi ý với kết quả thật), tới trước 2 tuần.</summary>
    public const int PastDays = ResultScraper.DaysBack;
    public const int FutureDays = 14;

    /// <summary>
    /// Gợi ý cho các đài MN xổ ngày <paramref name="date"/> (yyyy-MM-dd). Bỏ trống = kỳ xổ kế tiếp:
    /// hôm nay nếu chưa tới giờ xổ, không thì ngày mai.
    /// </summary>
    [HttpGet("/api/predict")]
    public async Task<ActionResult<PredictionService.PredictionDto>> Predict([FromQuery] DateOnly? date, CancellationToken ct)
    {
        var nowVn = DrawSchedule.NowVn(clock);
        var today = DateOnly.FromDateTime(nowVn);
        var target = date ?? (DrawSchedule.HasDrawn(today, "TPHCM", nowVn) ? today.AddDays(1) : today);
        if (target < today.AddDays(-PastDays) || target > today.AddDays(FutureDays))
            return BadRequest(new { error = Lang.T(Request,
                $"Chỉ xem được từ {PastDays} ngày trước tới {FutureDays} ngày tới.",
                $"Only dates from {PastDays} days ago to {FutureDays} days ahead are available.") });

        // Dữ liệu chỉ đổi khi cào kết quả mới (mỗi ngày 1 lần) → cache ngắn là đủ, đỡ tính lại mỗi lượt xem.
        return await cache.GetOrCreateAsync($"predict:{target:yyyy-MM-dd}", e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return predictions.PredictAsync(target, ct);
        }) ?? throw new InvalidOperationException();
    }
}
