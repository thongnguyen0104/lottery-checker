using System.Security.Cryptography;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Vé cào 2 số: mua cho các đài Miền Nam xổ trong ngày, số ngẫu nhiên 00–99; trùng giải tám của đài
/// đó thì trúng Price × <see cref="PayoutMultiplier"/>. Tiền là số dư ảo trong app (<see cref="Wallet"/>).
/// </summary>
public class ScratchTicketService(AppDbContext db, TimeProvider clock, ILogger<ScratchTicketService> log)
{
    public const long Price = 10_000;
    public const int PayoutMultiplier = 30;
    public const long Prize = Price * PayoutMultiplier;
    public const int MaxQuantity = 20;
    public const int PageSize = 20;
    /// <summary>Ngừng bán trước giờ xổ chừng này — bán sát giờ dễ lệch đồng hồ, lọt vé mua sau khi xổ.</summary>
    public static readonly TimeSpan SalesCloseBefore = TimeSpan.FromMinutes(15);
    /// <summary>Quá chừng này ngày mà đài vẫn không có kết quả (nghỉ xổ / không cào được) thì hoàn tiền vé.</summary>
    public const int RefundAfterDays = 3;
    private const int MaxAttempts = 3;

    public enum BuyError { None, BadQuantity, Closed, BadProvince, InsufficientBalance, NoAccount, Busy }

    public record BuyRequest(string? Province, DateOnly DrawDate, int Quantity);

    public record TicketDto(int Id, DateOnly DrawDate, string Province, string Number, long Price,
                            ScratchTicketStatus Status, string? WinningNumber, long Prize, bool Scratched,
                            DateTime DrawsAt, DateTime PurchasedAt);

    public record BuyResult(TicketDto[] Tickets, long Balance);

    public record BuyOutcome(BuyResult? Result, BuyError Error);

    /// <summary>ClosesAt, DrawsAt: giờ VN.</summary>
    public record ShopDto(DateOnly DrawDate, DateTime ClosesAt, DateTime DrawsAt, IReadOnlyList<string> Provinces,
                          long Price, long Prize, int PayoutMultiplier, int MaxQuantity, long Balance);

    public record PageDto(TicketDto[] Items, bool HasMore);

    /// <summary>Giờ (VN) ngừng bán vé của ngày này.</summary>
    public static DateTime SalesClosesAt(DateOnly day) => day.ToDateTime(DrawSchedule.MnDrawTime) - SalesCloseBefore;

    /// <summary>Ngày đang bán vé: hôm nay nếu chưa tới giờ ngừng bán, không thì ngày mai.</summary>
    public static DateOnly SalesDay(DateTime nowVn)
    {
        var today = DateOnly.FromDateTime(nowVn);
        return nowVn < SalesClosesAt(today) ? today : today.AddDays(1);
    }

    public async Task<ShopDto?> GetShopAsync(int userId, CancellationToken ct)
    {
        var balance = await db.Users.Where(x => x.Id == userId).Select(x => (long?)x.Balance).FirstOrDefaultAsync(ct);
        if (balance == null) return null;
        var day = SalesDay(DrawSchedule.NowVn(clock));
        return new ShopDto(day, SalesClosesAt(day), day.ToDateTime(DrawSchedule.MnDrawTime), DrawSchedule.MnProvincesOn(day),
                           Price, Prize, PayoutMultiplier, MaxQuantity, balance.Value);
    }

    /// <summary>
    /// Mua vé: trừ tiền + tạo vé trong cùng 1 lần lưu. DrawDate do FE gửi phải đúng ngày đang bán —
    /// để ai mở trang từ trước giờ ngừng bán rồi bấm mua sau đó không bị mua nhầm sang ngày mai.
    /// </summary>
    public async Task<BuyOutcome> BuyAsync(int userId, BuyRequest req, CancellationToken ct)
    {
        if (req.Quantity is < 1 or > MaxQuantity) return new(null, BuyError.BadQuantity);
        var day = SalesDay(DrawSchedule.NowVn(clock));
        if (req.DrawDate != day) return new(null, BuyError.Closed);
        var province = DrawSchedule.MnProvincesOn(day)
            .FirstOrDefault(p => string.Equals(p, req.Province, StringComparison.OrdinalIgnoreCase));
        if (province == null) return new(null, BuyError.BadProvince);

        var cost = Price * req.Quantity;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            db.ChangeTracker.Clear();
            var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
            if (user == null) return new(null, BuyError.NoAccount);
            if (user.Balance < cost) return new(null, BuyError.InsufficientBalance);

            var now = clock.GetUtcNow().UtcDateTime;
            var tickets = RandomNumbers(req.Quantity).Select(n => new ScratchTicket
            {
                UserId = userId, DrawDate = day, Province = province, Number = n, Price = Price,
                Status = ScratchTicketStatus.Pending, PurchasedAt = now,
            }).ToList();
            db.ScratchTickets.AddRange(tickets);
            Wallet.Apply(db, user, WalletTransactionKind.Purchase, -cost,
                         $"Mua {req.Quantity} vé {province} {day:dd/MM/yyyy}", now);
            try
            {
                await db.SaveChangesAsync(ct);
                return new(new BuyResult(tickets.Select(ToDto).ToArray(), user.Balance), BuyError.None);
            }
            // Số dư vừa đổi ở request khác (mua song song, admin cộng tiền) → đọc lại rồi thử lại.
            catch (DbUpdateConcurrencyException) { }
        }
        return new(null, BuyError.Busy);
    }

    /// <summary>Vé của user, mới mua trước. Chốt luôn các vé đã có kết quả để trang hiện đúng.</summary>
    public async Task<PageDto> ListAsync(int userId, int page, CancellationToken ct)
    {
        await SettleAsync(userId, ct);
        page = Math.Max(1, page);
        var rows = await db.ScratchTickets.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.PurchasedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * PageSize).Take(PageSize + 1)
            .ToListAsync(ct);
        return new PageDto(rows.Take(PageSize).Select(ToDto).ToArray(), rows.Count > PageSize);
    }

    /// <summary>User đã cào xem vé (chỉ ghi nhận, tiền đã cộng lúc chốt). null = không phải vé của user.</summary>
    public async Task<TicketDto?> ScratchAsync(int userId, int id, CancellationToken ct)
    {
        var t = await db.ScratchTickets.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (t == null) return null;
        if (t.Status is ScratchTicketStatus.Won or ScratchTicketStatus.Lost && t.ScratchedAt == null)
        {
            t.ScratchedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }
        return ToDto(t);
    }

    /// <summary>
    /// Chốt các vé đang chờ đã có giải tám: trúng thì cộng tiền; đài quá <see cref="RefundAfterDays"/>
    /// ngày không có kết quả thì hoàn tiền. userId null = mọi user (worker gọi sau khi cào kết quả).
    /// Lỗi chỉ ghi log — vé còn Pending, lần sau chốt tiếp.
    /// </summary>
    public async Task<int> SettleAsync(int? userId, CancellationToken ct)
    {
        try
        {
            var today = DateOnly.FromDateTime(DrawSchedule.NowVn(clock));
            var q = db.ScratchTickets.Where(t => t.Status == ScratchTicketStatus.Pending && t.DrawDate <= today);
            if (userId != null) q = q.Where(t => t.UserId == userId);
            var pending = await q.Select(t => new { t.Id, t.DrawDate, t.Province }).ToListAsync(ct);
            if (pending.Count == 0) return 0;

            var dates = pending.Select(p => p.DrawDate).Distinct().ToList();
            var eighth = (await db.LotteryResults
                    .Where(r => r.PrizeTier == "8" && dates.Contains(r.DrawDate))
                    .Select(r => new { r.DrawDate, r.Province, r.Number })
                    .ToListAsync(ct))
                .GroupBy(r => (r.DrawDate, r.Province))
                .ToDictionary(g => g.Key, g => g.First().Number);

            var settled = 0;
            foreach (var p in pending)
            {
                var winning = eighth.GetValueOrDefault((p.DrawDate, p.Province));
                if (winning == null && p.DrawDate >= today.AddDays(-RefundAfterDays)) continue;
                if (await SettleOneAsync(p.Id, winning, ct)) settled++;
            }
            if (settled > 0) log.LogInformation("Chốt {Count} vé cào", settled);
            return settled;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Chốt vé cào lỗi (user {UserId})", userId);
            return 0;
        }
    }

    /// <summary>winning null = hoàn tiền. false = vé đã được lượt khác chốt rồi.</summary>
    private async Task<bool> SettleOneAsync(int id, string? winning, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();
            var t = await db.ScratchTickets.FirstAsync(x => x.Id == id, ct);
            if (t.Status != ScratchTicketStatus.Pending) return false;
            var user = await db.Users.FirstAsync(x => x.Id == t.UserId, ct);
            var now = clock.GetUtcNow().UtcDateTime;

            if (winning == null)
            {
                t.Status = ScratchTicketStatus.Refunded;
                Wallet.Apply(db, user, WalletTransactionKind.Refund, t.Price,
                             $"Hoàn vé #{t.Id}: {t.Province} {t.DrawDate:dd/MM/yyyy} không có kết quả", now);
            }
            else
            {
                t.WinningNumber = winning.Length > 2 ? winning[^2..] : winning.PadLeft(2, '0');
                if (t.Number == t.WinningNumber)
                {
                    t.Status = ScratchTicketStatus.Won;
                    t.Prize = t.Price * PayoutMultiplier;
                    Wallet.Apply(db, user, WalletTransactionKind.Win, t.Prize,
                                 $"Trúng vé #{t.Id}: {t.Province} {t.DrawDate:dd/MM/yyyy} số {t.Number}", now);
                }
                else t.Status = ScratchTicketStatus.Lost;
            }
            t.SettledAt = now;
            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            // Lượt khác vừa chốt vé này (Status đổi) hoặc đổi số dư → đọc lại; vé đã chốt thì vòng sau trả false.
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts) { }
        }
    }

    /// <summary>count số khác nhau trong 00–99 (1 lượt mua không trùng số), xáo bằng RNG mật mã.</summary>
    private static IEnumerable<string> RandomNumbers(int count)
    {
        var all = Enumerable.Range(0, 100).ToArray();
        RandomNumberGenerator.Shuffle(all.AsSpan());
        return all.Take(count).Select(n => n.ToString("D2"));
    }

    private static TicketDto ToDto(ScratchTicket t) => new(
        t.Id, t.DrawDate, t.Province, t.Number, t.Price, t.Status, t.WinningNumber, t.Prize, t.ScratchedAt != null,
        DrawSchedule.DrawMoment(t.DrawDate, t.Province), DateTime.SpecifyKind(t.PurchasedAt, DateTimeKind.Utc));
}
