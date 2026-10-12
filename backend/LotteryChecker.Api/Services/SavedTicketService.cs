using System.Text.RegularExpressions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Vé đã lưu: lưu vé (chưa xổ / đã trúng / nhập tay), tự dò khi worker cào xong, báo kết quả ở chuông thông báo,
/// tự chuyển hết hạn và nhắc lĩnh thưởng. Logic dò dùng lại <see cref="LotteryMatcher"/>.
/// </summary>
public partial class SavedTicketService(AppDbContext db, LotteryMatcher matcher, CheckStats stats,
                                        NotificationService notifications, TimeProvider clock,
                                        ILogger<SavedTicketService> log)
{
    public const int PageSize = 20;
    /// <summary>Mỗi tài khoản tối đa chừng này vé đang chờ.</summary>
    public const int MaxWaiting = 100;
    public const int MaxBulk = 20;
    public const int NoteMax = 100;
    /// <summary>Lưu trước tối đa chừng này ngày (vé bán trước cho kỳ sau).</summary>
    public const int MaxDaysAhead = 14;
    /// <summary>Đài đủ chừng này số mới chốt — scraper đang nhập dở thì đợi lượt sau (XSKT MN = 18 số/đài).</summary>
    public const int FullResultRows = 18;
    /// <summary>Nhắc lĩnh thưởng ở mốc còn ≤ 5 ngày và ≤ 1 ngày.</summary>
    public const int RemindFirstDays = 5, RemindLastDays = 1;
    /// <summary>Khung giờ VN được gửi nhắc lĩnh — không đánh thức người ta lúc nửa đêm.</summary>
    public static readonly TimeSpan RemindFrom = new(8, 0, 0), RemindUntil = new(21, 0, 0);

    public enum SaveError { None, BadNumber, BadProvince, NotSouth, BadDate, Expired, TooFar, LimitReached }

    public record SaveRequest(string TicketNumber, DateOnly DrawDate, string Province, string? Note);
    public record BulkRequest(SaveRequest[] Items);

    public record SavedTicketDto(int Id, string TicketNumber, DateOnly DrawDate, string Province, SavedTicketStatus Status,
                                 long Prize, string[] PrizeTiers, string? Note, DateTime CreatedAt, DateTime? SettledAt,
                                 DateTime? ClaimedAt, DateOnly ClaimDeadline, DateTime DrawsAt);

    public record PageDto(SavedTicketDto[] Items, bool HasMore);
    public record SummaryDto(int Waiting, int UnclaimedWins, long UnclaimedPrize);

    [GeneratedRegex(@"^\d{6}$")]
    private static partial Regex SixDigits();

    private DateTime NowVn => DrawSchedule.NowVn(clock);

    private static SavedTicketDto ToDto(SavedTicket t) => new(
        t.Id, t.TicketNumber, t.DrawDate, t.Province, t.Status, t.Prize,
        string.IsNullOrEmpty(t.PrizeTiers) ? [] : t.PrizeTiers.Split('|'), t.Note,
        DateTime.SpecifyKind(t.CreatedAt, DateTimeKind.Utc),
        t.SettledAt is { } s ? DateTime.SpecifyKind(s, DateTimeKind.Utc) : null,
        t.ClaimedAt is { } c ? DateTime.SpecifyKind(c, DateTimeKind.Utc) : null,
        DrawSchedule.ClaimDeadline(t.DrawDate), DrawSchedule.DrawMoment(t.DrawDate, t.Province));

    private SaveError Validate(SaveRequest r)
    {
        if (r.TicketNumber == null || !SixDigits().IsMatch(r.TicketNumber)) return SaveError.BadNumber;
        var code = ProvinceMatcher.AllCodes.FirstOrDefault(c => string.Equals(c, r.Province, StringComparison.OrdinalIgnoreCase));
        if (code == null) return SaveError.BadProvince;
        if (DrawSchedule.RegionOf(code) != "MN") return SaveError.NotSouth;
        if (!DrawSchedule.MayDrawOn(r.DrawDate, code)) return SaveError.BadDate;
        var today = DateOnly.FromDateTime(NowVn);
        if (DrawSchedule.IsExpired(r.DrawDate, NowVn)) return SaveError.Expired;
        if (r.DrawDate > today.AddDays(MaxDaysAhead)) return SaveError.TooFar;
        return SaveError.None;
    }

    /// <summary>
    /// Lưu 1 vé. Trùng thì trả dòng cũ (idempotent). Đã xổ và đã đủ kết quả thì chốt luôn — người dùng đang
    /// nhìn màn kết quả rồi nên không tạo thông báo.
    /// </summary>
    public async Task<(SavedTicketDto? Ticket, SaveError Error)> SaveAsync(int userId, SaveRequest r, CancellationToken ct)
    {
        var err = Validate(r);
        if (err != SaveError.None) return (null, err);
        var province = ProvinceMatcher.AllCodes.First(c => string.Equals(c, r.Province, StringComparison.OrdinalIgnoreCase));

        var existing = await db.SavedTickets.FirstOrDefaultAsync(x => x.UserId == userId && x.DrawDate == r.DrawDate
            && x.Province == province && x.TicketNumber == r.TicketNumber, ct);
        if (existing != null) return (ToDto(existing), SaveError.None);

        if (await db.SavedTickets.CountAsync(x => x.UserId == userId && x.Status == SavedTicketStatus.Waiting, ct) >= MaxWaiting)
            return (null, SaveError.LimitReached);

        var note = r.Note?.Trim();
        var t = new SavedTicket
        {
            UserId = userId, TicketNumber = r.TicketNumber, DrawDate = r.DrawDate, Province = province,
            Status = SavedTicketStatus.Waiting, Note = string.IsNullOrEmpty(note) ? null : note[..Math.Min(NoteMax, note.Length)],
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        var result = await HasFullResultsAsync(t.DrawDate, t.Province, ct) ? await TrySettleAsync(t, ct) : null;

        db.SavedTickets.Add(t);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Bấm lưu 2 lần cùng lúc — index unique chặn bản thứ hai, trả dòng đã có.
            db.Entry(t).State = EntityState.Detached;
            var dup = await db.SavedTickets.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.DrawDate == r.DrawDate
                && x.Province == province && x.TicketNumber == r.TicketNumber, ct);
            if (dup == null) throw;
            return (ToDto(dup), SaveError.None);
        }
        if (result != null) await RecordStatsAsync([result], ct);
        return (ToDto(t), SaveError.None);
    }

    /// <summary>Lưu nhiều vé (màn nhiều vé). Vé lỗi bỏ qua, trả các vé lưu được + lỗi đầu tiên gặp.</summary>
    public async Task<(SavedTicketDto[] Saved, SaveError FirstError)> SaveManyAsync(int userId, SaveRequest[] items, CancellationToken ct)
    {
        var saved = new List<SavedTicketDto>();
        var first = SaveError.None;
        foreach (var r in items.Take(MaxBulk))
        {
            var (t, err) = await SaveAsync(userId, r, ct);
            if (t != null) saved.Add(t);
            else if (first == SaveError.None) first = err;
        }
        return (saved.ToArray(), first);
    }

    /// <summary>status null = mọi trạng thái. Vé chờ: sắp xổ trước; còn lại: mới chốt trước.</summary>
    public async Task<PageDto> ListAsync(int userId, SavedTicketStatus? status, int page, CancellationToken ct)
    {
        page = Math.Max(1, page);
        var q = db.SavedTickets.AsNoTracking().Where(x => x.UserId == userId);
        if (status != null) q = q.Where(x => x.Status == status);
        q = status == SavedTicketStatus.Waiting
            ? q.OrderBy(x => x.DrawDate).ThenBy(x => x.Id)
            : q.OrderByDescending(x => x.DrawDate).ThenByDescending(x => x.Id);
        var rows = await q.Skip((page - 1) * PageSize).Take(PageSize + 1).ToListAsync(ct); // dư 1 để biết còn trang sau
        return new PageDto(rows.Take(PageSize).Select(ToDto).ToArray(), rows.Count > PageSize);
    }

    /// <summary>Cho badge: số vé chờ, số vé trúng chưa lĩnh (còn hạn) và tổng tiền của chúng.</summary>
    public async Task<SummaryDto> SummaryAsync(int userId, CancellationToken ct)
    {
        var mine = db.SavedTickets.AsNoTracking().Where(x => x.UserId == userId);
        var minDraw = DateOnly.FromDateTime(NowVn).AddDays(-DrawSchedule.ClaimDays);
        var waiting = await mine.CountAsync(x => x.Status == SavedTicketStatus.Waiting, ct);
        var wins = await mine.Where(x => x.Status == SavedTicketStatus.Won && x.ClaimedAt == null && x.DrawDate >= minDraw)
            .Select(x => x.Prize).ToListAsync(ct);
        return new SummaryDto(waiting, wins.Count, wins.Sum());
    }

    public async Task<bool> DeleteAsync(int userId, int id, CancellationToken ct)
    {
        var t = await db.SavedTickets.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (t == null) return false;
        // InMemory (test) không cascade → xoá thông báo bằng tay; SQLite thì FK cascade lo.
        db.Notifications.RemoveRange(await db.Notifications.Where(n => n.SavedTicketId == id).ToListAsync(ct));
        db.SavedTickets.Remove(t);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Bật/tắt "Đã lĩnh". Chỉ vé trúng.</summary>
    public async Task<SavedTicketDto?> MarkClaimedAsync(int userId, int id, bool claimed, CancellationToken ct)
    {
        var t = await db.SavedTickets.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (t is not { Status: SavedTicketStatus.Won }) return null;
        t.ClaimedAt = claimed ? t.ClaimedAt ?? clock.GetUtcNow().UtcDateTime : null;
        await db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    private async Task<bool> HasFullResultsAsync(DateOnly date, string province, CancellationToken ct) =>
        await db.LotteryResults.CountAsync(r => r.DrawDate == date && r.Province == province, ct) >= FullResultRows;

    /// <summary>
    /// Dò 1 vé đang chờ; đổi trạng thái nếu đối chiếu được. Trả kết quả để caller ghi CheckStats SAU khi lưu vé
    /// (CheckStats tự SaveChanges — gọi giữa chừng sẽ lưu luôn vé đang sửa dở). Không ghi CheckHistory.
    /// </summary>
    private async Task<ScanResult?> TrySettleAsync(SavedTicket t, CancellationToken ct)
    {
        var r = await matcher.Match(t.TicketNumber, t.DrawDate, t.Province, ct);
        if (r.Status != CheckStatus.Checked) return null;   // NoData / NotDrawnYet → giữ Waiting, lượt sau dò tiếp
        t.Status = r.IsWinner ? SavedTicketStatus.Won : SavedTicketStatus.Lost;
        t.Prize = (long)r.TotalPrize;
        t.PrizeTiers = r.IsWinner ? string.Join('|', r.Winnings.Select(w => w.TierName)) : null;
        t.SettledAt = clock.GetUtcNow().UtcDateTime;
        return r;
    }

    private async Task RecordStatsAsync(IEnumerable<ScanResult> results, CancellationToken ct)
    {
        foreach (var r in results) await stats.RecordAsync(r, ct);
    }

    /// <summary>
    /// Chốt mọi vé đang chờ của các (ngày, đài) đã đủ kết quả. Idempotent (chỉ đụng dòng Waiting), chạy lại
    /// trong khung 16:45–20:00 không sao. Sau đó báo ở chuông, 1 dòng/vé. Trả số vé đã chốt.
    /// </summary>
    public async Task<int> SettleAsync(CancellationToken ct)
    {
        var nowVn = NowVn;
        var pairs = await db.SavedTickets.AsNoTracking().Where(x => x.Status == SavedTicketStatus.Waiting)
            .Select(x => new { x.DrawDate, x.Province }).Distinct().ToListAsync(ct);

        var settled = new List<SavedTicket>();
        foreach (var p in pairs.Where(p => DrawSchedule.HasDrawn(p.DrawDate, p.Province, nowVn)))
        {
            if (!await HasFullResultsAsync(p.DrawDate, p.Province, ct)) continue;
            var tickets = await db.SavedTickets.Where(x => x.Status == SavedTicketStatus.Waiting
                && x.DrawDate == p.DrawDate && x.Province == p.Province).ToListAsync(ct);
            var batch = new List<SavedTicket>();
            var results = new List<ScanResult>();
            foreach (var t in tickets)
            {
                if (await TrySettleAsync(t, ct) is not { } r) continue;
                batch.Add(t);
                results.Add(r);
            }
            if (batch.Count == 0) continue;
            try
            {
                await db.SaveChangesAsync(ct);
                settled.AddRange(batch);
                await RecordStatsAsync(results, ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Lượt chốt khác vừa chốt cùng vé — bỏ lô này, lượt kia đã báo rồi.
                foreach (var e in db.ChangeTracker.Entries<SavedTicket>().ToList()) e.State = EntityState.Detached;
                log.LogInformation("Chốt vé đã lưu {Province} {Date}: lượt khác đã chốt trước.", p.Province, p.DrawDate);
            }
        }
        if (settled.Count == 0) return 0;

        log.LogInformation("Chốt {Count} vé đã lưu ({Won} trúng).", settled.Count, settled.Count(x => x.Status == SavedTicketStatus.Won));
        await notifications.OnSavedTicketsAsync(settled.Select(t =>
            (t, t.Status == SavedTicketStatus.Won ? NotificationKind.TicketWon : NotificationKind.TicketLost)).ToList(), ct);
        return settled.Count;
    }

    /// <summary>Vé chờ đã quá hạn lĩnh mà vẫn chưa có kết quả → Expired. Trả số vé đổi.</summary>
    public async Task<int> ExpireAsync(CancellationToken ct)
    {
        var minDraw = DateOnly.FromDateTime(NowVn).AddDays(-DrawSchedule.ClaimDays);
        var rows = await db.SavedTickets.Where(x => x.Status == SavedTicketStatus.Waiting && x.DrawDate < minDraw).ToListAsync(ct);
        foreach (var t in rows)
        {
            t.Status = SavedTicketStatus.Expired;
            t.SettledAt = clock.GetUtcNow().UtcDateTime;
        }
        if (rows.Count > 0) await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>
    /// Nhắc vé trúng chưa lĩnh: còn ≤ 5 ngày (lần 1) và ≤ 1 ngày (lần 2), mỗi mốc 1 lần. Chỉ gửi 08:00–21:00 giờ VN.
    /// Trả số vé đã nhắc.
    /// </summary>
    public async Task<int> RemindAsync(CancellationToken ct)
    {
        var nowVn = NowVn;
        if (nowVn.TimeOfDay < RemindFrom || nowVn.TimeOfDay >= RemindUntil) return 0;
        var today = DateOnly.FromDateTime(nowVn);
        var minDraw = today.AddDays(-DrawSchedule.ClaimDays);                       // chưa hết hạn
        var maxDraw = today.AddDays(RemindFirstDays - DrawSchedule.ClaimDays);    // còn ≤ 5 ngày
        var rows = await db.SavedTickets.Where(x => x.Status == SavedTicketStatus.Won && x.ClaimedAt == null
            && x.ReminderStage < 2 && x.DrawDate >= minDraw && x.DrawDate <= maxDraw).ToListAsync(ct);

        var due = new List<SavedTicket>();
        foreach (var t in rows)
        {
            var left = DrawSchedule.ClaimDeadline(t.DrawDate).DayNumber - today.DayNumber;
            byte stage = left <= RemindLastDays ? (byte)2 : (byte)1;
            if (t.ReminderStage >= stage) continue;
            t.ReminderStage = stage;
            due.Add(t);
        }
        if (due.Count == 0) return 0;
        await db.SaveChangesAsync(ct);

        await notifications.OnSavedTicketsAsync(due.Select(t => (t, NotificationKind.TicketExpiring)).ToList(), ct);
        return due.Count;
    }
}
