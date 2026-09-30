using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Services;

/// <summary>Lịch sử dò vé của từng tài khoản (trang Tài khoản).</summary>
public class CheckHistory(AppDbContext db, TimeProvider clock, ILogger<CheckHistory> log)
{
    public const int PageSize = 20;

    public record EntryDto(int Id, string TicketNumber, DateOnly? DrawDate, string? Province, CheckStatus Status,
                           bool IsWinner, long Prize, DateTime CheckedAt);

    public record PageDto(EntryDto[] Items, bool HasMore);

    public record Summary(int Checks, int Winners, long TotalPrize);

    /// <summary>
    /// Ghi 1 lượt dò của tài khoản. Lỗi chỉ ghi log: lịch sử hỏng không được làm hỏng lượt dò.
    /// </summary>
    public async Task RecordAsync(int userId, ScanResult r, CancellationToken ct)
    {
        var row = new CheckHistoryEntry
        {
            UserId = userId, TicketNumber = r.ExtractedNumber, DrawDate = r.DrawDate, Province = r.Province,
            Status = r.Status, IsWinner = r.IsWinner, Prize = (long)r.TotalPrize,
            CheckedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.CheckHistory.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.Entry(row).State = EntityState.Detached;
            log.LogWarning(ex, "Không ghi được lịch sử dò vé {Ticket} của user {UserId}", r.ExtractedNumber, userId);
        }
    }

    /// <summary>Mới nhất trước; page bắt đầu từ 1.</summary>
    public async Task<PageDto> ListAsync(int userId, int page, CancellationToken ct)
    {
        page = Math.Max(1, page);
        var rows = await db.CheckHistory
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CheckedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * PageSize).Take(PageSize + 1) // lấy dư 1 để biết còn trang sau không
            .Select(x => new EntryDto(x.Id, x.TicketNumber, x.DrawDate, x.Province, x.Status, x.IsWinner, x.Prize,
                                        DateTime.SpecifyKind(x.CheckedAt, DateTimeKind.Utc)))
            .ToListAsync(ct);
        return new PageDto(rows.Take(PageSize).ToArray(), rows.Count > PageSize);
    }

    /// <summary>Tổng lượt dò, lượt trúng, tổng tiền trúng — dò lại cùng một vé thì chỉ tính tiền 1 lần.</summary>
    public async Task<Summary> GetSummaryAsync(int userId, CancellationToken ct)
    {
        var mine = db.CheckHistory.Where(x => x.UserId == userId);
        var checks = await mine.CountAsync(ct);
        // Mỗi vé trúng (ngày, đài, số) lấy 1 dòng — dò đi dò lại vé trúng không nhân tiền lên.
        var wins = await mine.Where(x => x.IsWinner)
            .GroupBy(x => new { x.DrawDate, x.Province, x.TicketNumber })
            .Select(g => g.Max(x => x.Prize))
            .ToListAsync(ct);
        return new Summary(checks, wins.Count, wins.Sum());
    }
}
