using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Services;

/// <summary>Đếm vé đã dò của cả hệ thống: tổng số vé, số vé trúng, tổng tiền thưởng.</summary>
public class CheckStats(AppDbContext db, TimeProvider clock, ILogger<CheckStats> log)
{
    public record Summary(int Tickets, int Winners, long TotalPrize);

    /// <summary>
    /// Ghi nhận 1 vé vừa dò. Chỉ tính vé đã đối chiếu được (Checked) — vé chưa xổ / chưa có kết quả /
    /// hết hạn chưa biết trúng hay trượt. Vé đã ghi rồi thì bỏ qua. Lỗi chỉ ghi log: thống kê hỏng
    /// không được làm hỏng lượt dò của người dùng.
    /// </summary>
    public async Task RecordAsync(ScanResult r, CancellationToken ct)
    {
        if (r.Status != CheckStatus.Checked || r.DrawDate is not { } date || r.Province is not { } province) return;
        try
        {
            var exists = await db.CheckedTickets.AnyAsync(
                x => x.DrawDate == date && x.Province == province && x.TicketNumber == r.ExtractedNumber, ct);
            if (exists) return;

            var row = new CheckedTicket
            {
                DrawDate = date, Province = province, TicketNumber = r.ExtractedNumber,
                IsWinner = r.IsWinner, Prize = (long)r.TotalPrize,
                CheckedAt = clock.GetUtcNow().UtcDateTime,
            };
            db.CheckedTickets.Add(row);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                // Hai người dò cùng vé cùng lúc — index unique chặn bản thứ hai, vậy là đúng.
                db.Entry(row).State = EntityState.Detached;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Không ghi được thống kê vé {Ticket} {Province} {Date}", r.ExtractedNumber, province, date);
        }
    }

    public async Task<Summary> GetAsync(CancellationToken ct) => new(
        await db.CheckedTickets.CountAsync(ct),
        await db.CheckedTickets.CountAsync(x => x.IsWinner, ct),
        await db.CheckedTickets.SumAsync(x => x.Prize, ct));
}
