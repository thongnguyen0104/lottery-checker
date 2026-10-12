using System.Text.Json.Serialization;

namespace LotteryChecker.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameSessionStatus
{
    Started,
    Finished,
    Expired,    // bỏ dở quá hạn / mở lượt mới đè lên → không thưởng
}

/// <summary>
/// Một lượt chơi minigame đua xe. Seed do server sinh; điểm do server tự chạy lại từ Inputs
/// (<see cref="Services.MiniGameSim.Replay"/>) — không bao giờ tin điểm client gửi.
/// </summary>
public class GameSession
{
    public int Id { get; set; }
    /// <summary>Client giữ id này — không lộ Id tăng dần.</summary>
    public Guid PublicId { get; set; }
    public int UserId { get; set; }
    public uint Seed { get; set; }
    /// <summary>Lượt có thưởng — quota trong ngày bị trừ ngay lúc bắt đầu (bỏ dở cũng mất lượt).</summary>
    public bool Rewarded { get; set; }
    public GameSessionStatus Status { get; set; }
    public int Score { get; set; }
    public int Ticks { get; set; }
    public long Reward { get; set; }
    /// <summary>Thao tác client gửi (JSON [[tick, dir], ...]) — giữ lại để soi khi nghi gian lận.</summary>
    public string? Inputs { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
