using LotteryChecker.Api.Services;

namespace LotteryChecker.Api.Workers;

// Cào kết quả Vietlott MỚI (bắt kịp lịch sử đang có trong DB — vài trang là cùng), rồi mỗi tối có quay (18:00)
// thử từ 18:30 tới 21:00, cứ 15' một lần cho tới khi thấy kỳ của hôm nay. Cào LẠI LỊCH SỬ (lần đầu dùng nguồn
// minhngoc, hoặc server mới) là việc riêng của VietlottBackfillWorker — chạy nền, không chặn worker này.
public class VietlottFetchWorker(IServiceScopeFactory scopeFactory, ILogger<VietlottFetchWorker> logger) : BackgroundService
{
    public static readonly TimeSpan FirstTry = new(18, 30, 0);
    public static readonly TimeSpan LastTry = new(21, 0, 0);
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);
    /// <summary>Đủ cho cào hằng ngày (bình thường 1–3 trang) + đệm an toàn nếu lỡ vài kỳ liền chưa cào.</summary>
    private const int DailyMaxPages = 30;

    /// <summary>Lần cào kế tiếp (giờ VN). todayDone: đã có kỳ của hôm nay (hoặc hôm nay không quay).</summary>
    public static DateTime NextRun(DateTime nowVn, bool todayDone)
    {
        var today = nowVn.Date;
        if (nowVn.TimeOfDay < FirstTry) return today + FirstTry;
        if (!todayDone && nowVn.TimeOfDay + RetryInterval <= LastTry) return nowVn + RetryInterval;
        return today.AddDays(1) + FirstTry;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Yield();
        // Khởi động chậm một chút: nhường CPU/RAM cho lượt cào XSKT + nạp model OCR trên VM nhỏ.
        try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
        catch (TaskCanceledException) { return; }

        var todayDone = await SyncAsync("Khởi động", ct);
        while (!ct.IsCancellationRequested)
        {
            var nowVn = DrawSchedule.NowVn();
            var next = NextRun(nowVn, todayDone);
            try { await Task.Delay(next - nowVn, ct); }
            catch (TaskCanceledException) { return; }
            todayDone = await SyncAsync("Worker", ct);
        }
    }

    /// <summary>Cào cả 2 loại. Trả true nếu loại nào quay hôm nay (sau 18:00) đều đã có kỳ hôm nay.</summary>
    private async Task<bool> SyncAsync(string source, CancellationToken ct)
    {
        var done = true;
        var nowVn = DrawSchedule.NowVn();
        var today = DateOnly.FromDateTime(nowVn);
        foreach (var game in VietlottScraper.Games)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<VietlottScraper>().SyncAsync(game, today, DailyMaxPages, ct);

                if (VietlottScraper.DrawsOn(game, today.DayOfWeek) && nowVn.TimeOfDay >= FirstTry)
                {
                    var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
                    done &= db.VietlottDraws.Any(d => d.Game == game && d.DrawDate == today);
                }
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Source}: cào Vietlott {Game} lỗi", source, game);
                done = false;
            }
        }
        return done;
    }
}
