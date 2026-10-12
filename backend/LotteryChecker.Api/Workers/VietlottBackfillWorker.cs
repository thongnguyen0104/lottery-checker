using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Workers;

/// <summary>
/// Cào lại lịch sử Vietlott (lần đầu dùng nguồn minhngoc.com.vn, hoặc server mới): mỗi loại vé tối đa
/// <see cref="BatchSize"/> kỳ mỗi lượt, cách <see cref="Interval"/>, cho tới khi bắt kịp kỳ đầu tiên —
/// rải đều để lịch sự với minhngoc, không cào dồn dập hàng nghìn request cùng lúc.
/// Độc lập với <see cref="VietlottFetchWorker"/> (lo cào kỳ MỚI hằng ngày) — 2 worker không tranh nhau vì
/// backfill luôn bắt đầu từ kỳ CŨ NHẤT đang có lùi tiếp, còn worker kia luôn bắt đầu từ hôm nay lùi lại.
/// </summary>
public class VietlottBackfillWorker(IServiceScopeFactory scopeFactory, ILogger<VietlottBackfillWorker> logger) : BackgroundService
{
    public const int BatchSize = 100;
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    /// <summary>Đã xong cả 2 loại thì kiểm tra lại cho chắc (gần như free: chỉ đọc cờ Completed) theo nhịp này.</summary>
    public static readonly TimeSpan IdleRecheck = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Yield();
        // Khởi động chậm: nhường CPU/RAM cho lượt cào XSKT + nạp model OCR trên VM nhỏ.
        try { await Task.Delay(TimeSpan.FromMinutes(1), ct); }
        catch (TaskCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            bool anyLeft;
            try { anyLeft = await TickAsync(ct); }
            catch (OperationCanceledException) { return; }
            try { await Task.Delay(anyLeft ? Interval : IdleRecheck, ct); }
            catch (TaskCanceledException) { return; }
        }
    }

    /// <summary>
    /// Cào tiếp 1 lượt cho mỗi loại vé chưa xong. Trả true nếu CÒN loại nào chưa bắt kịp kỳ đầu tiên
    /// (chỉ set true cho game đó — game khác xong trong cùng lượt này không được "ghi đè" về false).
    /// </summary>
    private async Task<bool> TickAsync(CancellationToken ct)
    {
        var anyLeft = false;
        foreach (var game in VietlottScraper.Games)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.VietlottBackfillStates.AnyAsync(x => x.Game == game && x.Completed, ct)) continue;

            try
            {
                var scraper = scope.ServiceProvider.GetRequiredService<VietlottScraper>();
                var minDate = await db.VietlottDraws.Where(d => d.Game == game)
                    .Select(d => (DateOnly?)d.DrawDate).MinAsync(ct);
                var from = minDate?.AddDays(-1) ?? DateOnly.FromDateTime(DrawSchedule.NowVn());

                var (saved, done) = await scraper.SyncAsync(game, from, BatchSize, ct);
                if (saved > 0) logger.LogInformation("Backfill Vietlott {Game}: lưu thêm {Count} kỳ (từ {From}).", game, saved, from);
                if (done)
                {
                    var state = await db.VietlottBackfillStates.FindAsync([game], ct)
                                ?? db.VietlottBackfillStates.Add(new VietlottBackfillState { Game = game }).Entity;
                    state.Completed = true;
                    state.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    logger.LogInformation("Backfill Vietlott {Game}: đã bắt kịp kỳ đầu tiên, dừng.", game);
                }
                else anyLeft = true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Backfill Vietlott {Game} lỗi", game);
                anyLeft = true;   // lỗi tạm thời (mạng...) — thử lại lượt sau, đừng coi là đã xong
            }
        }
        return anyLeft;
    }
}
