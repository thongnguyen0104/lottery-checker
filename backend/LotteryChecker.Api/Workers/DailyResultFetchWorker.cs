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
        await Task.Yield();   // nhường lại luồng khởi động cho host trước khi cào

        // Cào bù ngay khi khởi động: máy dev/server tắt vài ngày là DB thiếu kết quả,
        // mà vòng lặp dưới chỉ chạy lúc 19h nên có thể phải chờ tới hôm sau.
        await CatchUpOnStartupAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            // Mốc 19:00 tính theo GIỜ VN, không theo giờ máy: server prod chạy UTC thì
            // DateTime.Now sẽ khiến worker cào lúc 02:00 sáng giờ VN.
            // (Hiệu của 2 mốc cùng múi giờ là khoảng thời gian tuyệt đối → Task.Delay đúng ở mọi TZ.)
            var nowVn = DrawSchedule.NowVn();
            var nextRunVn = nowVn.Date.AddHours(19);
            if (nowVn > nextRunVn) nextRunVn = nextRunVn.AddDays(1);

            try { await Task.Delay(nextRunVn - nowVn, ct); }
            catch (TaskCanceledException) { return; }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scraper = scope.ServiceProvider.GetRequiredService<ResultScraper>();
                var saved = await scraper.FetchLast30Days(ct);
                _logger.LogInformation("Worker: cào xong, lưu {Count} dòng.", saved);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker: lỗi khi cào kết quả");
            }
        }
    }

    /// <summary>
    /// Chỉ cào những ngày DB CHƯA có (đã đủ 30 ngày thì không gọi request nào).
    /// Chạy nền — không chặn việc app bắt đầu nhận request.
    /// </summary>
    private async Task CatchUpOnStartupAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var scraper = scope.ServiceProvider.GetRequiredService<ResultScraper>();

            var missing = await scraper.MissingDatesAsync(ct);
            if (missing.Count == 0)
            {
                _logger.LogInformation("Khởi động: DB đã đủ kết quả {Days} ngày gần nhất — bỏ qua cào.",
                    ResultScraper.DaysBack);
                return;
            }

            _logger.LogInformation("Khởi động: thiếu {Count}/{Days} ngày ({From:dd-MM} → {To:dd-MM}) — bắt đầu cào bù.",
                missing.Count, ResultScraper.DaysBack, missing.Min(), missing.Max());

            var saved = await scraper.FetchDates(missing, ct);
            _logger.LogInformation("Khởi động: cào bù xong, lưu {Count} dòng.", saved);
        }
        catch (OperationCanceledException)
        {
            // app đang tắt — không phải lỗi
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Khởi động: lỗi khi cào bù kết quả");
        }
    }
}
