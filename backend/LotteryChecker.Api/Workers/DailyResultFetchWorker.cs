using LotteryChecker.Api.Services;

namespace LotteryChecker.Api.Workers;

// Tự động cào kết quả MN mỗi ngày. Lần đầu lúc 16:45 (xổ 16:15 + ~30' để web nhập đủ 18 số/đài);
// hôm nay còn thiếu đài nào thì cứ 10' thử lại tới 20:00 — trước đây chỉ cào 1 lần lúc 19:00,
// nên ai dò ngay sau giờ xổ đều gặp "chưa có kết quả", và lỡ lần đó lỗi là mất trắng tới hôm sau.
// Mỗi lần chỉ cào ngày DB còn thiếu (xem ResultScraper.MissingDatesAsync), không cào lại 30 ngày.
public class DailyResultFetchWorker : BackgroundService
{
    /// <summary>Khoảng giữa 2 lần thử lại khi kết quả hôm nay chưa đủ.</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(10);

    /// <summary>Quá mốc này (giờ VN) vẫn thiếu thì thôi — lần cào 16:45 hôm sau sẽ cào bù.</summary>
    public static readonly TimeSpan RetryUntil = new(20, 0, 0);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyResultFetchWorker> _logger;

    public DailyResultFetchWorker(IServiceScopeFactory scopeFactory,
                                  ILogger<DailyResultFetchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Lần cào kế tiếp (giờ VN). IsRetry = chỉ cào lại hôm nay; lần đầu trong ngày thì cào mọi
    /// ngày còn thiếu (bù cả hôm qua nếu hôm qua quá 20:00 vẫn chưa đủ).
    /// </summary>
    public static (DateTime AtVn, bool IsRetry) NextRun(DateTime nowVn, bool todayComplete)
    {
        // Mốc tính theo GIỜ VN, không theo giờ máy: server prod chạy UTC thì DateTime.Now sẽ
        // khiến worker cào lệch 7 tiếng.
        var firstToday = nowVn.Date + DrawSchedule.MnPublishedAt;
        if (nowVn < firstToday) return (firstToday, false);

        var retryAt = nowVn + RetryInterval;
        if (!todayComplete && retryAt <= nowVn.Date + RetryUntil) return (retryAt, true);

        return (firstToday.AddDays(1), false);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Yield();   // nhường lại luồng khởi động cho host trước khi cào

        // Cào bù ngay khi khởi động: máy dev/server tắt vài ngày là DB thiếu kết quả,
        // mà vòng lặp dưới chỉ chạy từ 16:45 nên có thể phải chờ tới chiều.
        var todayComplete = await FetchMissingAsync("Khởi động", todayOnly: false, ct);
        await BackfillHistoryAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            var nowVn = DrawSchedule.NowVn();
            var (nextRunVn, isRetry) = NextRun(nowVn, todayComplete);

            if (!todayComplete && !isRetry && nowVn.TimeOfDay >= DrawSchedule.MnPublishedAt)
                _logger.LogWarning("Worker: đã quá {Until} mà vẫn thiếu kết quả MN hôm nay — dừng thử lại, " +
                                   "lần cào {Next:dd-MM HH:mm} sẽ cào bù.",
                    RetryUntil.ToString(@"hh\:mm"), nextRunVn);

            // Hiệu của 2 mốc cùng múi giờ là khoảng thời gian tuyệt đối → Task.Delay đúng ở mọi TZ.
            try { await Task.Delay(nextRunVn - nowVn, ct); }
            catch (TaskCanceledException) { return; }

            todayComplete = await FetchMissingAsync(isRetry ? "Worker (thử lại)" : "Worker",
                                                    todayOnly: isRetry, ct);
        }
    }

    /// <summary>
    /// Cào bù kết quả cũ (31 ngày → 1 năm) cho thống kê Dự đoán. Lưu theo lô 30 ngày: tắt app giữa
    /// chừng thì lần sau chỉ cào tiếp phần còn thiếu. Lỗi chỉ ghi log — dò vé không cần phần này.
    /// </summary>
    private async Task BackfillHistoryAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var scraper = scope.ServiceProvider.GetRequiredService<ResultScraper>();
            var missing = await scraper.MissingHistoryDatesAsync(ct);
            if (missing.Count == 0) return;

            _logger.LogInformation("Cào bù lịch sử: thiếu {Count} ngày ({From:dd-MM-yyyy} → {To:dd-MM-yyyy}).",
                missing.Count, missing.Min(), missing.Max());
            var saved = 0;
            foreach (var chunk in missing.Chunk(30))
                saved += await scraper.FetchDates(chunk, ct);
            _logger.LogInformation("Cào bù lịch sử xong: lưu {Count} dòng.", saved);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cào bù lịch sử lỗi");
        }
    }

    /// <summary>
    /// Cào những ngày DB CHƯA có. todayOnly: lần thử lại chỉ cần hôm nay — không lôi theo các
    /// ngày không xổ (Tết) mỗi 10' một lần. Trả về: kết quả hôm nay đã đủ mọi đài chưa
    /// (trước 16:45 hôm nay chưa tính là cần có nên luôn true).
    /// </summary>
    private async Task<bool> FetchMissingAsync(string source, bool todayOnly, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var scraper = scope.ServiceProvider.GetRequiredService<ResultScraper>();
            var today = DateOnly.FromDateTime(DrawSchedule.NowVn());

            var missing = await scraper.MissingDatesAsync(ct);
            var toFetch = todayOnly ? missing.Where(d => d == today).ToList() : missing;
            if (toFetch.Count == 0)
            {
                _logger.LogInformation("{Source}: DB đã đủ kết quả {Days} ngày gần nhất — bỏ qua cào.",
                    source, ResultScraper.DaysBack);
                return true;
            }

            _logger.LogInformation("{Source}: thiếu {Count}/{Days} ngày ({From:dd-MM} → {To:dd-MM}) — bắt đầu cào.",
                source, toFetch.Count, ResultScraper.DaysBack, toFetch.Min(), toFetch.Max());

            var saved = await scraper.FetchDates(toFetch, ct);
            var todayComplete = !(await scraper.MissingDatesAsync(ct)).Contains(today);
            _logger.LogInformation("{Source}: cào xong, lưu {Count} dòng; kết quả hôm nay {State}.",
                source, saved, todayComplete ? "đã đủ" : "còn thiếu");
            return todayComplete;
        }
        catch (OperationCanceledException)
        {
            return false;   // app đang tắt — không phải lỗi
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Source}: lỗi khi cào kết quả", source);
            return false;   // coi như còn thiếu → còn trong khung giờ thì 10' sau thử lại
        }
    }
}
