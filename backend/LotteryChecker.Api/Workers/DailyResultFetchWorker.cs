using LotteryChecker.Api.Services;

namespace LotteryChecker.Api.Workers;

// Tự động cào kết quả mỗi ngày lúc 19:00 (sau khi quay xong ~16:15-16:30).
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

    // Lịch quay XSKT Miền Nam theo thứ. code PHẢI khớp ProvinceMatcher; slug theo minhngoc.
    // (best-effort — slug có thể cần kiểm chứng lại với site thật.)
    private static readonly (DayOfWeek Day, string Code, string Slug)[] Schedule =
    {
        (DayOfWeek.Monday,    "TPHCM",     "tp-ho-chi-minh"),
        (DayOfWeek.Monday,    "DongThap",  "dong-thap"),
        (DayOfWeek.Monday,    "CaMau",     "ca-mau"),
        (DayOfWeek.Tuesday,   "BenTre",    "ben-tre"),
        (DayOfWeek.Tuesday,   "VungTau",   "vung-tau"),
        (DayOfWeek.Tuesday,   "BacLieu",   "bac-lieu"),
        (DayOfWeek.Wednesday, "DongNai",   "dong-nai"),
        (DayOfWeek.Wednesday, "CanTho",    "can-tho"),
        (DayOfWeek.Wednesday, "SocTrang",  "soc-trang"),
        (DayOfWeek.Thursday,  "TayNinh",   "tay-ninh"),
        (DayOfWeek.Thursday,  "AnGiang",   "an-giang"),
        (DayOfWeek.Thursday,  "BinhThuan", "binh-thuan"),
        (DayOfWeek.Friday,    "VinhLong",  "vinh-long"),
        (DayOfWeek.Friday,    "BinhDuong", "binh-duong"),
        (DayOfWeek.Friday,    "TraVinh",   "tra-vinh"),
        (DayOfWeek.Saturday,  "TPHCM",     "tp-ho-chi-minh"),
        (DayOfWeek.Saturday,  "LongAn",    "long-an"),
        (DayOfWeek.Saturday,  "HauGiang",  "hau-giang"),
        (DayOfWeek.Sunday,    "TienGiang", "tien-giang"),
        (DayOfWeek.Sunday,    "KienGiang", "kien-giang"),
        (DayOfWeek.Sunday,    "DaLat",     "da-lat"),
    };

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
                var today = DateOnly.FromDateTime(DateTime.Now);
                var todays = Schedule.Where(s => s.Day == DateTime.Now.DayOfWeek).ToArray();

                using var scope = _scopeFactory.CreateScope();
                var scraper = scope.ServiceProvider.GetRequiredService<ResultScraper>();

                foreach (var (_, code, slug) in todays)
                {
                    await scraper.FetchProvince(today, slug, code, ct);
                    await Task.Delay(TimeSpan.FromSeconds(3), ct); // tránh rate-limit
                }

                _logger.LogInformation("Worker: cào xong {Count} đài cho {Date}", todays.Length, today);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker: lỗi khi cào kết quả");
            }
        }
    }
}
