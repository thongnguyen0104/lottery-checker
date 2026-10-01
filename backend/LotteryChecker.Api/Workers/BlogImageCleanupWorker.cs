using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;

namespace LotteryChecker.Api.Workers;

// Dọn ảnh mồ côi mỗi giờ: upload rồi không đăng bài / không lưu điểm bán (quá hạn chờ 24h), ảnh bị thay,
// hoặc của bài / điểm đã xoá mà lúc xoá bucket lỗi. Không dọn thì bucket free 20GB cứ đầy dần vì ảnh không ai xem.
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

                var shops = scope.ServiceProvider.GetRequiredService<ShopService>();
                var shopOrphans = await shops.OrphanImagesAsync(clock.GetUtcNow().UtcDateTime - ShopService.PendingImageTtl, BatchSize, ct);
                if (shopOrphans.Count > 0)
                    logger.LogInformation("Dọn ảnh điểm bán mồ côi: xoá {Done}/{Count} ảnh.", await PurgeShopImagesAsync(shops, shopOrphans, ct), shopOrphans.Count);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Dọn ảnh mồ côi lỗi — thử lại lượt sau.");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task<int> PurgeShopImagesAsync(ShopService shops, List<ShopImage> orphans, CancellationToken ct)
    {
        var done = new List<ShopImage>();
        foreach (var img in orphans)
        {
            try
            {
                await storage.DeleteAsync(img.Key, ct);
                done.Add(img);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                storage.Evict(img.Key);
                logger.LogWarning(e, "Không xoá được ảnh điểm bán {Key} trên bucket — để lượt sau thử lại.", img.Key);
            }
        }
        if (done.Count > 0) await shops.RemoveImageRowsAsync(done, ct);
        return done.Count;
    }
}
