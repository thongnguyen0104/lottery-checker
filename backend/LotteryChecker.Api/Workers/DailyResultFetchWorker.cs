using LotteryChecker.Api.Services;

namespace LotteryChecker.Api.Workers;

// Tự động cào kết quả MN mỗi ngày lúc 19:00 (sau khi quay xong ~16:15-16:30).
// 1 request lấy mọi đài cho các ngày gần nhất (xem ResultScraper).
public class DailyResultFetchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyResultFetchWorker> _logger;

    public DailyResultFetchWorker(IServiceScopeFactory scopeFactory,
                                  ILogger<DailyResultFetchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextRun = now.Date.AddHours(19);
            if (now > nextRun) nextRun = nextRun.AddDays(1);

            try { await Task.Delay(nextRun - now, ct); }
            catch (TaskCanceledException) { return; }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scraper = scope.ServiceProvider.GetRequiredService<ResultScraper>();
                var saved = await scraper.FetchLatestMienNam(ct);
                _logger.LogInformation("Worker: cào xong, lưu {Count} dòng.", saved);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker: lỗi khi cào kết quả");
            }
        }
    }
}
