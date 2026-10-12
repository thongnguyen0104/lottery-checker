namespace LotteryChecker.Api.Models;

/// <summary>
/// Tiến độ cào lại lịch sử Vietlott (xem <see cref="Workers.VietlottBackfillWorker"/>) — 1 dòng mỗi loại vé.
/// Completed = đã lùi tới kỳ đầu tiên, worker thôi không cào thêm.
/// </summary>
public class VietlottBackfillState
{
    public string Game { get; set; } = "";   // PK: "645" | "655"
    public bool Completed { get; set; }
    public DateTime UpdatedAt { get; set; }
}
