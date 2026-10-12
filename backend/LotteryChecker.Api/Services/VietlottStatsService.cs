using LotteryChecker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Thống kê và gợi ý số Vietlott (Mega 6/45, Power 6/55) từ lịch sử kỳ quay. Chỉ là thống kê tần suất —
/// mỗi kỳ là ngẫu nhiên độc lập, kỳ trước không ảnh hưởng kỳ sau; FE luôn hiện kèm lời nhắc + xác suất thật.
///
/// Chấm điểm số n (chỉ dùng 6 số chính, không tính số đặc biệt của 6/55):
///   - xác suất  = số kỳ có n / tổng số kỳ trong khoảng xem (mốc ngẫu nhiên đều: 6 / max);
///   - xác suất gần = như trên trong <see cref="RecentDraws"/> kỳ gần nhất;
///   - điểm = 0,6 × xác suất + 0,4 × xác suất gần; "gan" = số kỳ liền chưa về.
/// "Kiểm chứng": chạy lại gợi ý "số nóng" cho <see cref="BacktestDraws"/> kỳ gần nhất (mỗi kỳ chỉ dùng dữ liệu
/// trước nó) và đếm trúng mấy số — so với kỳ vọng chọn bừa 36/max để thấy thống kê không "đoán" giỏi hơn.
/// </summary>
public class VietlottStatsService(AppDbContext db)
{
    public const int RecentDraws = 30;
    public const int TopCount = 6;
    public const int BacktestDraws = 30;
    public const int PairCount = 10;
    public const int LatestCount = 12;   // kỳ gần nhất cho bảng "mỗi số về ở kỳ nào"

    public record Draw(int DrawId, DateOnly Date, int[] Numbers, int? Bonus);

    public record DrawDto(int DrawId, string Date, int[] Numbers, int? Bonus);

    public record NumberStat(int Number, int Hits, double Probability, double RecentProbability, int Gap, double Score);

    public record PairStat(int A, int B, int Hits);

    public record Suggestion(string Kind, int[] Numbers);

    public record BacktestRow(int DrawId, string Date, int[] Suggested, int[] Actual, int Matches);

    public record Backtest(double AverageMatches, double RandomExpectation, int[] MatchCounts, BacktestRow[] Rows);

    public record PrizeOdds(int Match, long OneIn);

    public record StatsDto(
        string Game, int MaxNumber, int Draws, string? From, string? To,
        double Baseline,          // 6 / max — xác suất 1 số bất kỳ về trong 1 kỳ
        long JackpotOneIn,        // C(max, 6)
        PrizeOdds[] Odds,         // xác suất trúng 3/4/5/6 số
        string NextDraw,          // yyyy-MM-dd
        NumberStat[] All,         // 1..max cho bảng nhiệt
        NumberStat[] Hot, NumberStat[] Cold,
        PairStat[] Pairs,
        int[] OddCounts,          // [k] = số kỳ có k số lẻ (k = 0..6)
        int[] SumBand,            // tổng 6 số: [phân vị 10, trung vị, phân vị 90]
        Suggestion[] Suggestions,
        Backtest? Backtest,
        NumberStat[] BonusAll);   // 6/55: tần suất số đặc biệt; 6/45: rỗng

    public record ResultsDto(string Game, int Total, int Page, int Size, string NextDraw, DrawDto[] Items);

    public async Task<ResultsDto> ResultsAsync(string game, int page, int size, DateOnly nextDraw, CancellationToken ct)
    {
        var q = db.VietlottDraws.AsNoTracking().Where(d => d.Game == game);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(d => d.DrawId).Skip(page * size).Take(size)
            .Select(d => new { d.DrawId, d.DrawDate, d.Numbers, d.Bonus }).ToListAsync(ct);
        return new ResultsDto(game, total, page, size, nextDraw.ToString("yyyy-MM-dd"),
            rows.Select(r => ToDto(new Draw(r.DrawId, r.DrawDate, Split(r.Numbers), r.Bonus))).ToArray());
    }

    /// <summary>last = 0: toàn bộ lịch sử; &gt; 0: chỉ N kỳ gần nhất.</summary>
    public async Task<StatsDto> StatsAsync(string game, int last, DateOnly nextDraw, CancellationToken ct)
    {
        var rows = await db.VietlottDraws.AsNoTracking().Where(d => d.Game == game)
            .Select(d => new { d.DrawId, d.DrawDate, d.Numbers, d.Bonus }).ToListAsync(ct);
        var draws = rows.Select(r => new Draw(r.DrawId, r.DrawDate, Split(r.Numbers), r.Bonus))
            .OrderByDescending(d => d.DrawId).ToList();
        return Build(game, draws, last, nextDraw);
    }

    /// <summary>Tính thống kê từ danh sách kỳ (mới → cũ). Tách riêng để test.</summary>
    public static StatsDto Build(string game, IReadOnlyList<Draw> allDraws, int last, DateOnly nextDraw)
    {
        var max = VietlottScraper.MaxNumber(game);
        var draws = last > 0 ? allDraws.Take(last).ToList() : allDraws.ToList();
        var all = Score(draws, max);

        var hot = draws.Count == 0 ? [] : HotOf(all);
        var cold = draws.Count == 0 ? [] : all.OrderByDescending(s => s.Gap).ThenBy(s => s.Hits).Take(TopCount).ToArray();

        var pairs = new Dictionary<(int, int), int>();
        foreach (var d in draws)
            for (var i = 0; i < 6; i++)
                for (var j = i + 1; j < 6; j++)
                    pairs[(d.Numbers[i], d.Numbers[j])] = pairs.GetValueOrDefault((d.Numbers[i], d.Numbers[j])) + 1;
        var topPairs = pairs.OrderByDescending(p => p.Value).ThenBy(p => p.Key.Item1).ThenBy(p => p.Key.Item2)
            .Take(PairCount).Select(p => new PairStat(p.Key.Item1, p.Key.Item2, p.Value)).ToArray();

        var oddCounts = new int[7];
        foreach (var d in draws) oddCounts[d.Numbers.Count(n => n % 2 == 1)]++;

        var sums = draws.Select(d => d.Numbers.Sum()).Order().ToArray();
        int[] sumBand = sums.Length == 0 ? [] : [Percentile(sums, 0.1), Percentile(sums, 0.5), Percentile(sums, 0.9)];

        var suggestions = draws.Count == 0 ? [] : new[]
        {
            new Suggestion("hot", hot.Select(s => s.Number).Order().ToArray()),
            new Suggestion("balanced", Balanced(all, max, sumBand)),
            new Suggestion("cold", cold.Select(s => s.Number).Order().ToArray()),
        };

        var bonusAll = game == VietlottScraper.Power
            ? Score(draws.Where(d => d.Bonus.HasValue).Select(d => new Draw(d.DrawId, d.Date, [d.Bonus!.Value], null)).ToList(), max)
            : [];

        var jackpot = Choose(max, 6);
        var odds = new[] { 3, 4, 5, 6 }.Select(k => new PrizeOdds(k,
            (long)Math.Round((double)jackpot / (Choose(6, k) * Choose(max - 6, 6 - k))))).ToArray();

        return new StatsDto(game, max, draws.Count,
            draws.Count > 0 ? draws[^1].Date.ToString("yyyy-MM-dd") : null,
            draws.Count > 0 ? draws[0].Date.ToString("yyyy-MM-dd") : null,
            Math.Round(6.0 / max, 4), jackpot, odds, nextDraw.ToString("yyyy-MM-dd"),
            all, hot, cold, topPairs, oddCounts, sumBand, suggestions,
            BuildBacktest(allDraws, max, last), bonusAll);
    }

    private static NumberStat[] HotOf(NumberStat[] all) =>
        all.OrderByDescending(s => s.Score).ThenByDescending(s => s.Hits).ThenBy(s => s.Gap).ThenBy(s => s.Number)
            .Take(TopCount).ToArray();

    /// <summary>Tần suất từng số 1..max trên các kỳ (mới → cũ).</summary>
    private static NumberStat[] Score(IReadOnlyList<Draw> draws, int max)
    {
        var recent = Math.Min(RecentDraws, draws.Count);
        var hits = new int[max + 1];
        var recentHits = new int[max + 1];
        var gap = Enumerable.Repeat(-1, max + 1).ToArray();
        for (var i = 0; i < draws.Count; i++)
            foreach (var n in draws[i].Numbers)
            {
                if (n < 1 || n > max) continue;
                hits[n]++;
                if (i < recent) recentHits[n]++;
                if (gap[n] < 0) gap[n] = i;
            }
        return Enumerable.Range(1, max).Select(n =>
        {
            var p = draws.Count == 0 ? 0 : (double)hits[n] / draws.Count;
            var pr = recent == 0 ? 0 : (double)recentHits[n] / recent;
            return new NumberStat(n, hits[n], Math.Round(p, 4), Math.Round(pr, 4), gap[n] < 0 ? draws.Count : gap[n],
                                  Math.Round(0.6 * p + 0.4 * pr, 4));
        }).ToArray();
    }

    /// <summary>
    /// Bộ "cân bằng": lấy số điểm cao nhưng giữ dáng thường gặp — 3 lẻ/3 chẵn, 3 nhỏ/3 lớn (chia ở max/2),
    /// tổng nằm trong khoảng phân vị 10–90. Tham lam theo điểm; không đạt tổng thì bỏ ràng buộc tổng.
    /// </summary>
    private static int[] Balanced(NumberStat[] all, int max, int[] sumBand)
    {
        var ranked = all.OrderByDescending(s => s.Score).ThenBy(s => s.Number).Select(s => s.Number).ToList();
        var half = max / 2;

        int[]? Pick(bool checkSum)
        {
            // Thử lần lượt các tổ hợp tham lam: duyệt theo điểm, nhận số nếu chưa vượt hạn mức lẻ/chẵn, nhỏ/lớn.
            for (var skip = 0; skip < ranked.Count; skip++)
            {
                var pick = new List<int>();
                foreach (var n in ranked.Skip(skip).Concat(ranked.Take(skip)))
                {
                    var odd = pick.Count(x => x % 2 == 1);
                    var low = pick.Count(x => x <= half);
                    if (n % 2 == 1 ? odd >= 3 : pick.Count - odd >= 3) continue;
                    if (n <= half ? low >= 3 : pick.Count - low >= 3) continue;
                    pick.Add(n);
                    if (pick.Count == 6) break;
                }
                if (pick.Count < 6) continue;
                var sum = pick.Sum();
                if (!checkSum || sumBand.Length == 0 || (sum >= sumBand[0] && sum <= sumBand[2]))
                    return pick.Order().ToArray();
            }
            return null;
        }

        return Pick(true) ?? Pick(false) ?? ranked.Take(6).Order().ToArray();
    }

    private static Backtest? BuildBacktest(IReadOnlyList<Draw> allDraws, int max, int last)
    {
        var rows = new List<BacktestRow>();
        for (var i = 0; i < BacktestDraws && i + 1 < allDraws.Count; i++)
        {
            var target = allDraws[i];
            var history = allDraws.Skip(i + 1);
            var before = (last > 0 ? history.Take(last) : history).ToList();
            if (before.Count < RecentDraws) break;   // quá ít dữ liệu — gợi ý chưa có nghĩa
            var suggested = HotOf(Score(before, max)).Select(s => s.Number).Order().ToArray();
            rows.Add(new BacktestRow(target.DrawId, target.Date.ToString("yyyy-MM-dd"), suggested, target.Numbers,
                                     suggested.Intersect(target.Numbers).Count()));
        }
        if (rows.Count == 0) return null;

        var counts = new int[7];
        foreach (var r in rows) counts[r.Matches]++;
        return new Backtest(Math.Round(rows.Average(r => r.Matches), 3), Math.Round(36.0 / max, 3), counts, rows.ToArray());
    }

    private static int Percentile(int[] sorted, double p) =>
        sorted[Math.Clamp((int)Math.Round(p * (sorted.Length - 1)), 0, sorted.Length - 1)];

    public static long Choose(int n, int k)
    {
        if (k < 0 || k > n) return 0;
        long r = 1;
        for (var i = 1; i <= k; i++) r = r * (n - k + i) / i;
        return r;
    }

    private static int[] Split(string numbers) => numbers.Split(',').Select(int.Parse).ToArray();

    private static DrawDto ToDto(Draw d) => new(d.DrawId, d.Date.ToString("yyyy-MM-dd"), d.Numbers, d.Bonus);
}
