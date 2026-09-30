using LotteryChecker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Thống kê "lô" 2 số cuối (00–99) của từng đài trên 1 năm kết quả gần nhất, và gợi ý số cho một
/// ngày xổ. Chỉ là thống kê tần suất — xổ số là ngẫu nhiên, kỳ trước không ảnh hưởng kỳ sau; FE
/// luôn hiện kèm lời nhắc này.
///
/// Cách chấm điểm một số n của đài X cho ngày D (chỉ dùng kết quả TRƯỚC ngày D):
///   - xác suất năm  = số kỳ có n / tổng số kỳ trong 1 năm (xuất hiện ≥ 1 lần trong 18 giải);
///   - xác suất gần  = như trên nhưng chỉ trong <see cref="RecentDraws"/> kỳ gần nhất;
///   - điểm = 0,6 × xác suất năm + 0,4 × xác suất gần. Số "nóng" = điểm cao nhất.
///   - "gan" = đã bao nhiêu kỳ liền chưa về.
/// </summary>
public class PredictionService(AppDbContext db)
{
    /// <summary>Số kỳ gần nhất dùng cho xác suất "gần đây".</summary>
    public const int RecentDraws = 10;
    public const int TopCount = 6;
    public const int OverdueCount = 4;

    /// <summary>
    /// Xác suất lý thuyết 1 số 2 chữ số về ít nhất 1 lần trong 18 giải của 1 kỳ, nếu mỗi giải
    /// ngẫu nhiên đều: 1 − 0,99^18 ≈ 16,5%. FE so với mốc này để thấy số nào "hơn trung bình".
    /// </summary>
    public static readonly double Baseline = 1 - Math.Pow(0.99, 18);

    public record NumberStat(string Number, int Hits, int Draws, double Probability, double RecentProbability,
                             int Gap, double Score);

    public record ProvincePrediction(
        string Province,
        int Draws,             // số kỳ có dữ liệu trong 1 năm trước ngày dự đoán
        string? From, string? To,
        NumberStat[] Top,      // số nóng — gợi ý chính
        NumberStat[] Overdue,  // số gan lâu nhất
        string? Special,       // gợi ý 6 số: chữ số hay về nhất ở từng vị trí của giải ĐB
        NumberStat[] All,      // đủ 00–99 cho bảng nhiệt
        string[]? Actual);     // 2 số cuối 18 giải nếu ngày đó đã có kết quả (để đối chiếu)

    public record PredictionDto(string Date, double Baseline, ProvincePrediction[] Provinces);

    public async Task<PredictionDto> PredictAsync(DateOnly date, CancellationToken ct)
    {
        var provinces = DrawSchedule.MnProvincesOn(date);
        var from = date.AddDays(-ResultScraper.HistoryDays);
        var rows = await db.LotteryResults
            .Where(r => r.DrawDate >= from && r.DrawDate <= date && provinces.Contains(r.Province))
            .Select(r => new { r.DrawDate, r.Province, r.PrizeTier, r.Number })
            .ToListAsync(ct);

        var list = provinces.Select(p =>
        {
            var mine = rows.Where(r => r.Province == p).ToList();
            var history = mine.Where(r => r.DrawDate < date)
                .Select(r => new Row(r.DrawDate, r.PrizeTier, r.Number)).ToList();
            var actual = mine.Where(r => r.DrawDate == date).Select(r => Tail(r.Number)).ToArray();
            return Build(p, history, actual.Length > 0 ? actual : null);
        }).ToArray();

        return new PredictionDto(date.ToString("yyyy-MM-dd"), Baseline, list);
    }

    public record Row(DateOnly DrawDate, string Tier, string Number);

    /// <summary>Tính thống kê cho 1 đài từ các kết quả trước ngày dự đoán (tách riêng để test).</summary>
    public static ProvincePrediction Build(string province, IReadOnlyList<Row> history, string[]? actual)
    {
        // Kỳ mới nhất trước: index 0 = kỳ gần nhất.
        var draws = history.Select(r => r.DrawDate).Distinct().OrderByDescending(d => d).ToList();
        var drawIndex = draws.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        var recent = Math.Min(RecentDraws, draws.Count);

        var hits = new int[100];
        var drawSets = Enumerable.Range(0, 100).Select(_ => new HashSet<int>()).ToArray();
        foreach (var r in history)
        {
            if (r.Number.Length < 2 || !int.TryParse(Tail(r.Number), out var n)) continue;
            hits[n]++;
            drawSets[n].Add(drawIndex[r.DrawDate]);
        }

        var all = Enumerable.Range(0, 100).Select(n =>
        {
            var set = drawSets[n];
            var p = draws.Count == 0 ? 0 : (double)set.Count / draws.Count;
            var pr = recent == 0 ? 0 : (double)set.Count(i => i < recent) / recent;
            var gap = set.Count == 0 ? draws.Count : set.Min();
            return new NumberStat(n.ToString("00"), hits[n], set.Count, Math.Round(p, 4), Math.Round(pr, 4), gap,
                                  Math.Round(0.6 * p + 0.4 * pr, 4));
        }).ToArray();

        var top = draws.Count == 0 ? [] : all
            .OrderByDescending(s => s.Score).ThenByDescending(s => s.Hits).ThenBy(s => s.Gap)
            .Take(TopCount).ToArray();
        var overdue = draws.Count == 0 ? [] : all
            .OrderByDescending(s => s.Gap).ThenBy(s => s.Hits)
            .Take(OverdueCount).ToArray();

        return new ProvincePrediction(province, draws.Count,
            draws.Count > 0 ? draws[^1].ToString("yyyy-MM-dd") : null,
            draws.Count > 0 ? draws[0].ToString("yyyy-MM-dd") : null,
            top, overdue, SpecialGuess(history), all, actual);
    }

    // Chữ số về nhiều nhất ở từng vị trí của các giải ĐB; hoà thì lấy chữ số của kỳ gần nhất có nó.
    private static string? SpecialGuess(IReadOnlyList<Row> history)
    {
        var specials = history.Where(r => r.Tier == "DB" && r.Number.Length == 6 && r.Number.All(char.IsAsciiDigit))
            .OrderByDescending(r => r.DrawDate).Select(r => r.Number).ToList();
        if (specials.Count == 0) return null;

        var chars = new char[6];
        for (var pos = 0; pos < 6; pos++)
        {
            var p = pos;
            chars[pos] = specials
                .Select((s, i) => (Digit: s[p], Order: i))
                .GroupBy(x => x.Digit)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Min(x => x.Order))
                .First().Key;
        }
        return new string(chars);
    }

    private static string Tail(string number) => number[^2..];
}
