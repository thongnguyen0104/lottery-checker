using LotteryChecker.Api.Services;

namespace LotteryChecker.Api.Workers;

// Mỗi giờ: vé đã lưu quá hạn mà vẫn chưa có kết quả → Hết hạn; vé trúng chưa lĩnh → nhắc ở mốc còn 5 ngày
// và còn 1 ngày (chỉ 08:00–21:00 giờ VN, xem SavedTicketService.RemindAsync).
public class TicketReminderWorker(IServiceScopeFactory scopeFactory, ILogger<TicketReminderWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var tickets = scope.ServiceProvider.GetRequiredService<SavedTicketService>();
                var expired = await tickets.ExpireAsync(ct);
                var reminded = await tickets.RemindAsync(ct);
                if (expired + reminded > 0)
                    logger.LogInformation("Vé đã lưu: {Expired} vé hết hạn, nhắc lĩnh {Reminded} vé.", expired, reminded);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Nhắc lĩnh thưởng lỗi — thử lại lượt sau.");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
