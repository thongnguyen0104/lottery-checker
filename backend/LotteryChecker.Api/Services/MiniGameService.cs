using System.Security.Cryptography;
using System.Text.Json;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Minigame đua xe kiếm xu ảo cho vé cào. Chống gian lận: server sinh seed, client chỉ gửi chuỗi thao tác
/// (đổi làn ở tick nào), server tự chạy lại ván bằng <see cref="MiniGameSim"/> để tính điểm; mỗi lượt chốt
/// đúng 1 lần; kết thúc sớm hơn thời gian ván thật thì từ chối (chặn tua nhanh). Bot chơi hoàn hảo không chặn
/// tuyệt đối được (seed phải xuống client để vẽ) — nên thưởng có trần và giới hạn lượt / ngày.
/// </summary>
public class MiniGameService(AppDbContext db, TimeProvider clock)
{
    public const int RewardedPlaysPerDay = 5;
    public const long RewardPerPoint = 1_000;
    public const long MaxRewardPerPlay = 5_000;
    public const int MaxInputs = MiniGameSim.MaxTicks;
    /// <summary>Lượt bắt đầu quá chừng này mà chưa kết thúc thì hết hạn (ván dài nhất 90 giây).</summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(3);
    /// <summary>Dung sai trễ mạng / lệch nhịp khi so thời gian chơi thật với số tick của ván.</summary>
    public static readonly TimeSpan TimingTolerance = TimeSpan.FromSeconds(3);
    private const int MaxAttempts = 3;

    public enum FinishError { None, NotFound, AlreadyFinished, Expired, BadInputs, TooFast, Busy }

    public record StatusDto(int RewardedLeft, int RewardedPerDay, long RewardPerPoint, long MaxRewardPerPlay, long Balance);

    public record StartDto(Guid SessionId, uint Seed, bool Rewarded, int RewardedLeft);

    public record FinishRequest(Guid SessionId, int[][]? Inputs);

    public record FinishDto(int Score, int Coins, int Ticks, bool Crashed, long Reward, long Balance, int RewardedLeft);

    public record FinishOutcome(FinishDto? Result, FinishError Error);

    public static long RewardFor(int score) => Math.Min(score * RewardPerPoint, MaxRewardPerPlay);

    /// <summary>0h hôm nay giờ VN, đổi ra UTC — mốc đếm lượt có thưởng trong ngày.</summary>
    private DateTime TodayStartUtc()
    {
        var today = DateOnly.FromDateTime(DrawSchedule.NowVn(clock)).ToDateTime(TimeOnly.MinValue);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(today, DateTimeKind.Unspecified), DrawSchedule.VietnamZone);
    }

    private async Task<int> RewardedLeftAsync(int userId, CancellationToken ct)
    {
        var from = TodayStartUtc();
        var used = await db.GameSessions.CountAsync(x => x.UserId == userId && x.Rewarded && x.StartedAt >= from, ct);
        return Math.Max(0, RewardedPlaysPerDay - used);
    }

    public async Task<StatusDto?> GetStatusAsync(int userId, CancellationToken ct)
    {
        var balance = await db.Users.Where(x => x.Id == userId).Select(x => (long?)x.Balance).FirstOrDefaultAsync(ct);
        if (balance == null) return null;
        return new StatusDto(await RewardedLeftAsync(userId, ct), RewardedPlaysPerDay, RewardPerPoint, MaxRewardPerPlay, balance.Value);
    }

    /// <summary>Mở lượt mới: lượt đang dở (nếu có) hết hạn luôn — mỗi user chỉ chơi 1 lượt một lúc.
    /// Còn quota thì lượt có thưởng và trừ quota ngay, để "mở đi mở lại chọn đường dễ" không được lợi.</summary>
    public async Task<StartDto?> StartAsync(int userId, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == userId, ct)) return null;
        var open = await db.GameSessions.Where(x => x.UserId == userId && x.Status == GameSessionStatus.Started).ToListAsync(ct);
        foreach (var s in open) s.Status = GameSessionStatus.Expired;

        var left = await RewardedLeftAsync(userId, ct);
        var session = new GameSession
        {
            PublicId = Guid.NewGuid(), UserId = userId, Seed = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)),
            Rewarded = left > 0, Status = GameSessionStatus.Started, StartedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.GameSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return new StartDto(session.PublicId, session.Seed, session.Rewarded, Math.Max(0, left - 1));
    }

    /// <summary>Thao tác hợp lệ: [tick, dir], tick tăng dần trong [0, MaxTicks), dir ±1. Sai 1 cái là loại cả ván.</summary>
    public static List<(int Tick, int Dir)>? ParseInputs(int[][]? raw)
    {
        raw ??= [];
        if (raw.Length > MaxInputs) return null;
        var list = new List<(int, int)>(raw.Length);
        var last = -1;
        foreach (var x in raw)
        {
            if (x is not [var tick, var dir] || tick <= last || tick >= MiniGameSim.MaxTicks || dir is not (1 or -1)) return null;
            list.Add((tick, dir));
            last = tick;
        }
        return list;
    }

    /// <summary>Chốt lượt: chạy lại ván từ seed + thao tác, cộng thưởng (nếu lượt có thưởng) trong cùng 1 lần lưu.</summary>
    public async Task<FinishOutcome> FinishAsync(int userId, FinishRequest req, CancellationToken ct)
    {
        if (ParseInputs(req.Inputs) is not { } inputs) return new(null, FinishError.BadInputs);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            db.ChangeTracker.Clear();
            var s = await db.GameSessions.FirstOrDefaultAsync(x => x.PublicId == req.SessionId && x.UserId == userId, ct);
            if (s == null) return new(null, FinishError.NotFound);
            if (s.Status == GameSessionStatus.Finished) return new(null, FinishError.AlreadyFinished);
            if (s.Status == GameSessionStatus.Expired) return new(null, FinishError.Expired);

            var now = clock.GetUtcNow().UtcDateTime;
            if (now - s.StartedAt > SessionLifetime)
            {
                s.Status = GameSessionStatus.Expired;
                try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { }
                return new(null, FinishError.Expired);
            }

            var r = MiniGameSim.Replay(s.Seed, inputs);
            // Ván r.Ticks tick phải mất ít nhất chừng đó thời gian thật (theo giờ server, sửa đồng hồ máy khách vô ích).
            var minElapsed = TimeSpan.FromSeconds((double)r.Ticks / MiniGameSim.TicksPerSecond) - TimingTolerance;
            if (now - s.StartedAt < minElapsed) return new(null, FinishError.TooFast);

            var user = await db.Users.FirstAsync(x => x.Id == userId, ct);
            var reward = s.Rewarded ? RewardFor(r.Score) : 0;
            s.Status = GameSessionStatus.Finished;
            s.Score = r.Score;
            s.Ticks = r.Ticks;
            s.Reward = reward;
            s.Inputs = JsonSerializer.Serialize(inputs.Select(x => new[] { x.Tick, x.Dir }));
            s.FinishedAt = now;
            if (reward > 0)
                Wallet.Apply(db, user, WalletTransactionKind.GameReward, reward, $"Minigame đua xe: {r.Score} điểm", now);
            try
            {
                await db.SaveChangesAsync(ct);
                return new(new FinishDto(r.Score, r.Coins, r.Ticks, r.Crashed, reward, user.Balance,
                                         await RewardedLeftAsync(userId, ct)), FinishError.None);
            }
            // Lượt vừa được chốt ở request khác, hoặc số dư vừa đổi → đọc lại; vòng sau sẽ thấy Finished nếu là chốt trùng.
            catch (DbUpdateConcurrencyException) { }
        }
        return new(null, FinishError.Busy);
    }
}
