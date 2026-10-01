using LotteryChecker.Api.Services;

namespace LotteryChecker.Api.Workers;

// Dọn ảnh Blog mồ côi mỗi giờ: upload rồi không đăng bài (quá hạn chờ 24h), hoặc của bài đã xoá mà
// lúc xoá bucket lỗi. Không dọn thì bucket free 20GB cứ đầy dần vì ảnh không ai xem.
public class BlogImageCleanupWorker(IServiceScopeFactory scopeFactory, BlogImageStorage storage, TimeProvider clock,
                                    ILogger<BlogImageCleanupWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    /// <summary>Mỗi lượt xoá tối đa chừng này — mỗi ảnh là 1 request API (gói free có hạn mức/tháng).</summary>
    public const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!storage.Enabled) return;
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var blog = scope.ServiceProvider.GetRequiredService<BlogService>();
                var images = scope.ServiceProvider.GetRequiredService<BlogImageService>();
                var orphans = await blog.OrphanImagesAsync(clock.GetUtcNow().UtcDateTime - BlogService.PendingImageTtl, BatchSize, ct);
                if (orphans.Count > 0)
                    logger.LogInformation("Dọn ảnh blog mồ côi: xoá {Done}/{Count} ảnh.", await images.PurgeAsync(orphans, ct), orphans.Count);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Dọn ảnh blog mồ côi lỗi — thử lại lượt sau.");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
