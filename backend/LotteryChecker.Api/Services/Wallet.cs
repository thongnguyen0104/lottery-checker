using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Services;

/// <summary>Số dư ví: mọi lần cộng/trừ đi qua <see cref="Apply"/> để luôn có dòng trong sổ giao dịch.</summary>
public class Wallet(AppDbContext db, TimeProvider clock)
{
    public const long MaxTopUp = 100_000_000;
    private const int MaxAttempts = 3;

    public enum AdjustError { None, NoAccount, BadAmount, InsufficientBalance }

    public record AdjustOutcome(long Balance, AdjustError Error);

    /// <summary>
    /// Đổi số dư của user đang được track + thêm dòng sổ giao dịch — CHƯA lưu, bên gọi SaveChanges
    /// cùng lúc với thay đổi khác (vé mua, vé chốt) để tất cả cùng thành công hoặc cùng hỏng.
    /// </summary>
    public static void Apply(AppDbContext db, User user, WalletTransactionKind kind, long amount, string? note, DateTime now)
    {
        if (user.Balance + amount < 0) throw new InvalidOperationException("Số dư không được âm.");
        user.Balance += amount;
        db.WalletTransactions.Add(new WalletTransaction
        {
            UserId = user.Id, Kind = kind, Amount = amount, BalanceAfter = user.Balance, Note = note, CreatedAt = now,
        });
    }

    /// <summary>
    /// Admin cộng (amount &gt; 0, ghi TopUp) hoặc trừ (amount &lt; 0, ghi Adjust) số dư của user.
    /// |amount| tối đa <see cref="MaxTopUp"/>; trừ quá số dư hiện có thì từ chối.
    /// </summary>
    public async Task<AdjustOutcome> AdjustAsync(int userId, long amount, string? note, CancellationToken ct)
    {
        if (amount == 0 || Math.Abs(amount) > MaxTopUp) return new(0, AdjustError.BadAmount);
        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();
            var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
            if (user == null) return new(0, AdjustError.NoAccount);
            if (user.Balance + amount < 0) return new(user.Balance, AdjustError.InsufficientBalance);
            Apply(db, user, amount > 0 ? WalletTransactionKind.TopUp : WalletTransactionKind.Adjust, amount, note,
                  clock.GetUtcNow().UtcDateTime);
            try
            {
                await db.SaveChangesAsync(ct);
                return new(user.Balance, AdjustError.None);
            }
            // User vừa mua vé / trúng giữa chừng → đọc lại số dư mới rồi làm lại.
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts) { }
        }
    }

    /// <summary>Cộng tiền theo username (endpoint X-Admin-Key cho script). null = không có user này.</summary>
    public async Task<long?> TopUpAsync(string username, long amount, string? note, CancellationToken ct)
    {
        if (amount is <= 0 or > MaxTopUp) throw new ArgumentOutOfRangeException(nameof(amount));
        var u = AccountRules.Normalize(username);
        var id = await db.Users.Where(x => x.Username == u).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (id == null) return null;
        var r = await AdjustAsync(id.Value, amount, note, ct);
        return r.Error == AdjustError.None ? r.Balance : null;
    }
}
