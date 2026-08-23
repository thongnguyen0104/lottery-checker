using System.Text.Json.Serialization;

namespace LotteryChecker.Api.Models;

/// <summary>
/// Vì sao cần Status: "không trúng" và "không kết luận được" là 2 chuyện khác nhau.
/// Trả về IsWinner=false cho cả 2 sẽ khiến app báo "vé không trúng" trong khi thực tế
/// vé chưa xổ, hoặc hệ thống chưa cào được kết quả của đài/ngày đó.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CheckStatus
{
    Checked,        // đã đối chiếu với kết quả thật → IsWinner/Winnings có ý nghĩa
    NotDrawnYet,    // chưa đến giờ xổ của đài đó
    NoData,         // đã xổ nhưng DB chưa có kết quả (chưa cào được / đài không xổ ngày đó)
}

public class ScanResult
{
    public string ExtractedNumber { get; set; } = "";
    public DateOnly? DrawDate { get; set; }
    public string? Province { get; set; }
    public CheckStatus Status { get; set; } = CheckStatus.Checked;
    public DateTime? DrawsAt { get; set; }      // giờ VN sẽ xổ — chỉ set khi NotDrawnYet
    public bool IsWinner { get; set; }
    public List<WinningPrize> Winnings { get; set; } = new();
    public decimal TotalPrize { get; set; }
    public double OcrConfidence { get; set; }
}
