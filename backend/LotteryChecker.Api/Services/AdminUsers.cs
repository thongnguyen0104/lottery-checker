using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Services;

/// <summary>Dữ liệu cho trang quản trị: tổng quan, danh sách/chi tiết user, sổ giao dịch.</summary>
public class AdminUsers(AppDbContext db)
{
    public const int PageSize = 20;

    public record Overview(int Users, int Admins, long TotalBalance, int TicketsSold, int TicketsWon, long TotalPaidOut,
                           long TotalTopUp);

    public record UserRow(int Id, string Username, DateTime CreatedAt, long Balance, bool IsAdmin, int Tickets);

    public record UserPage(UserRow[] Items, bool HasMore, int Total);

    public record TicketStats(int Bought, int Won, int Pending, long Spent, long Winnings);

    public record UserDetail(int Id, string Username, DateTime CreatedAt, long Balance, bool IsAdmin,
                             bool MustChangePassword, TicketStats Tickets, int Checks);

    public record TransactionRow(int Id, WalletTransactionKind Kind, long Amount, long BalanceAfter, string? Note, DateTime CreatedAt);

    public record TransactionPage(TransactionRow[] Items, bool HasMore);

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);

    public async Task<Overview> GetOverviewAsync(CancellationToken ct) => new(
        await db.Users.CountAsync(ct),
        await db.Users.CountAsync(x => x.IsAdmin, ct),
        await db.Users.SumAsync(x => x.Balance, ct),
        await db.ScratchTickets.CountAsync(ct),
        await db.ScratchTickets.CountAsync(x => x.Status == ScratchTicketStatus.Won, ct),
        await db.ScratchTickets.SumAsync(x => x.Prize, ct),
        await db.WalletTransactions.Where(x => x.Kind == WalletTransactionKind.TopUp).SumAsync(x => x.Amount, ct));

    /// <summary>search: một phần username; sort: "new" (mới đăng ký trước, mặc định) | "balance" (số dư cao trước).</summary>
    public async Task<UserPage> ListAsync(string? search, string? sort, int page, CancellationToken ct)
    {
        page = Math.Max(1, page);
        var q = db.Users.AsNoTracking();
        var s = AccountRules.Normalize(search);
        if (s.Length > 0) q = q.Where(x => x.Username.Contains(s));
        var total = await q.CountAsync(ct);
        q = sort == "balance"
            ? q.OrderByDescending(x => x.Balance).ThenBy(x => x.Id)
            : q.OrderByDescending(x => x.Id);
        var rows = await q.Skip((page - 1) * PageSize).Take(PageSize + 1)
            .Select(x => new UserRow(x.Id, x.Username, x.CreatedAt, x.Balance, x.IsAdmin,
                                     db.ScratchTickets.Count(t => t.UserId == x.Id)))
            .ToListAsync(ct);
        return new UserPage(rows.Take(PageSize).Select(r => r with { CreatedAt = Utc(r.CreatedAt) }).ToArray(),
                            rows.Count > PageSize, total);
    }

    public async Task<UserDetail?> GetAsync(int id, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u == null) return null;
        var tickets = db.ScratchTickets.Where(t => t.UserId == id);
        var stats = new TicketStats(
            await tickets.CountAsync(ct),
            await tickets.CountAsync(t => t.Status == ScratchTicketStatus.Won, ct),
            await tickets.CountAsync(t => t.Status == ScratchTicketStatus.Pending, ct),
            await tickets.Where(t => t.Status != ScratchTicketStatus.Refunded).SumAsync(t => t.Price, ct),
            await tickets.SumAsync(t => t.Prize, ct));
        return new UserDetail(u.Id, u.Username, Utc(u.CreatedAt), u.Balance, u.IsAdmin, u.MustChangePassword, stats,
                              await db.CheckHistory.CountAsync(x => x.UserId == id, ct));
    }

    public async Task<TransactionPage> TransactionsAsync(int userId, int page, CancellationToken ct)
    {
        page = Math.Max(1, page);
        var rows = await db.WalletTransactions.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.Id)
            .Skip((page - 1) * PageSize).Take(PageSize + 1)
            .Select(x => new TransactionRow(x.Id, x.Kind, x.Amount, x.BalanceAfter, x.Note, x.CreatedAt))
            .ToListAsync(ct);
        return new TransactionPage(rows.Take(PageSize).Select(r => r with { CreatedAt = Utc(r.CreatedAt) }).ToArray(),
                                   rows.Count > PageSize);
    }

    /// <summary>Admin đặt mật khẩu tạm cho user; user phải đổi lại ở lần đăng nhập sau. false = không có user.</summary>
    public async Task<bool> ResetPasswordAsync(int id, string password, CancellationToken ct)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u == null) return false;
        u.PasswordHash = Controllers.AuthController.HashPassword(u, password);
        u.MustChangePassword = true;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Bật/tắt quyền admin. false = không có user.</summary>
    public async Task<bool> SetAdminAsync(int id, bool isAdmin, CancellationToken ct)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u == null) return false;
        u.IsAdmin = isAdmin;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
