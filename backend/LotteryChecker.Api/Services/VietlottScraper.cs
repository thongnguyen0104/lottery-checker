using System.Globalization;
using System.Text.RegularExpressions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Services;

// Cào kết quả Vietlott Mega 6/45 và Power 6/55 từ minhngoc.com.vn (trang đã cào XSKT MN, không chặn bot).
// ĐỔI NGUỒN 2026-10-12: vietlott.vn trả Cloudflare "Just a moment..." (403) cho mọi request từ VM —
// chặn theo IP/ASN, không liên quan tốc độ cào — nên bỏ API AJAX cũ, chuyển sang trang tĩnh minhngoc.
//
// minhngoc không có bảng nhiều kỳ/trang cho Vietlott — mỗi URL chỉ 1 ngày:
//   /ket-qua-xo-so/dien-toan-vietlott/{mega-6x45|power-6x55}/dd-MM-yyyy.html
// Ngày không có kỳ quay (hoặc chưa tới giờ công bố) thì trang tự hiện kỳ GẦN NHẤT TRƯỚC đó — nên bước kế tiếp
// luôn nhảy tới (ngày trả về trong trang − 1 ngày) thay vì lùi từng ngày, bỏ qua được mọi ngày trống không cần dò.
// Ngày trước cả kỳ đầu tiên thì trang lại hiện kỳ MỚI NHẤT hiện có (không phải "không có dữ liệu") — tín hiệu
// này trùng với tín hiệu "đã bắt kịp lịch sử đang có trong DB" (known.Add trả false), nên 1 vòng lặp dùng được
// cho cả cào hằng ngày (từ hôm nay lùi) lẫn cào lại lịch sử (từ kỳ cũ nhất đang có lùi tiếp — VietlottBackfillWorker).
// Kỳ đầu tiên của mỗi loại vé không có "Kỳ vé" trên trang (lỗi hiển thị của minhngoc) → gán DrawId = 1.
public class VietlottScraper(AppDbContext db, HttpClient http, ILogger<VietlottScraper> logger)
{
    public const string Mega = "645";
    public const string Power = "655";
    public static readonly string[] Games = [Mega, Power];

    /// <summary>Số lớn nhất được chọn của từng loại vé.</summary>
    public static int MaxNumber(string game) => game == Power ? 55 : 45;

    public static bool IsGame(string game) => game is Mega or Power;

    /// <summary>Nghỉ giữa 2 ngày khi cào nhiều ngày liên tiếp trong 1 lượt — lịch sự với server.</summary>
    public static TimeSpan PageDelay = TimeSpan.FromMilliseconds(400);

    private const string Base = "https://www.minhngoc.com.vn/ket-qua-xo-so/dien-toan-vietlott";

    private static string Slug(string game) => game == Power ? "power-6x55" : "mega-6x45";

    public record ParsedDraw(int DrawId, DateOnly DrawDate, int[] Numbers, int? Bonus);

    private static readonly Regex HeaderDateRx = new(@"Ng&agrave;y quay thưởng (\d{2}/\d{2}/\d{4})", RegexOptions.Compiled);
    private static readonly Regex KyRx = new(@"KY_VE"">#?(\d+)<", RegexOptions.Compiled);
    private static readonly Regex ResultBlockRx = new(@"<ul class=""result-number"">(.*?)</ul>",
        RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex NumRx = new(@"finnish(\d) bool"">\s*(\d{1,2})\s*<", RegexOptions.Compiled);

    /// <summary>
    /// Đọc trang 1 ngày. null nếu trang đổi cấu trúc / thiếu số — không ghi data rác.
    /// Lưu ý: "ngày quay thưởng" trong trang có thể KHÁC ngày xin trong URL (xem ghi chú đầu file).
    /// </summary>
    public static ParsedDraw? ParseDay(string html, string game)
    {
        var dateM = HeaderDateRx.Match(html);
        if (!dateM.Success) return null;
        var date = DateOnly.ParseExact(dateM.Groups[1].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture);

        var block = ResultBlockRx.Match(html);
        if (!block.Success) return null;
        var byPos = NumRx.Matches(block.Groups[1].Value)
            .ToDictionary(m => int.Parse(m.Groups[1].Value), m => int.Parse(m.Groups[2].Value));
        if (!Enumerable.Range(1, 6).All(byPos.ContainsKey)) return null;

        var max = MaxNumber(game);
        var main = Enumerable.Range(1, 6).Select(i => byPos[i]).ToArray();
        if (main.Any(n => n < 1 || n > max) || main.Distinct().Count() != 6) return null;

        int? bonus = null;
        if (game == Power)
        {
            if (!byPos.TryGetValue(7, out var b) || b < 1 || b > max) return null;
            bonus = b;
        }

        // Kỳ đầu tiên của loại vé không in "Kỳ vé" trên trang minhngoc → coi là kỳ #1.
        var drawId = KyRx.Match(html) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 1;
        return new ParsedDraw(drawId, date, main.Order().ToArray(), bonus);
    }

    /// <summary>
    /// Cào lùi theo ngày từ <paramref name="from"/>, tối đa <paramref name="maxPages"/> ngày trong lượt này.
    /// Dừng sớm khi gặp kỳ đã có trong DB — nghĩa là bắt kịp lịch sử đang có (cào hằng ngày) hoặc đã lùi quá
    /// kỳ đầu tiên, trang nhảy về kỳ mới nhất hiện có (cào lại lịch sử, xem <see cref="VietlottBackfillWorker"/>).
    /// Trả (số kỳ mới lưu, có dừng sớm vì gặp kỳ đã biết hay không — tức "đã xong", không phải hết quota trang).
    /// </summary>
    public async Task<(int Saved, bool Done)> SyncAsync(string game, DateOnly from, int maxPages, CancellationToken ct)
    {
        var known = (await db.VietlottDraws.Where(d => d.Game == game).Select(d => d.DrawId).ToListAsync(ct)).ToHashSet();
        var cursor = from;
        var saved = 0;
        for (var page = 0; page < maxPages; page++)
        {
            if (page > 0) await Task.Delay(PageDelay, ct);
            string html;
            try { html = await http.GetStringAsync($"{Base}/{Slug(game)}/{cursor:dd-MM-yyyy}.html", ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Vietlott {Game}: lỗi tải trang ngày {Date}.", game, cursor);
                return (saved, false);
            }

            var draw = ParseDay(html, game);
            if (draw == null)
            {
                logger.LogWarning("Vietlott {Game}: không đọc được trang ngày {Date} (trang đổi cấu trúc?).", game, cursor);
                return (saved, false);
            }
            if (!known.Add(draw.DrawId)) return (saved, true);

            db.VietlottDraws.Add(new VietlottDraw
            {
                Game = game, DrawId = draw.DrawId, DrawDate = draw.DrawDate,
                Numbers = string.Join(",", draw.Numbers.Select(n => n.ToString("00"))), Bonus = draw.Bonus,
            });
            await db.SaveChangesAsync(ct);   // lưu từng ngày: tắt app giữa chừng thì lần sau chỉ cào tiếp
            saved++;
            cursor = draw.DrawDate.AddDays(-1);
        }
        if (saved > 0) logger.LogInformation("Vietlott {Game}: lưu {Count} kỳ mới.", game, saved);
        return (saved, false);
    }

    // ── Lịch quay: 18:00 — Mega 6/45: T4, T6, CN; Power 6/55: T3, T5, T7 ──
    public static readonly TimeOnly DrawTime = new(18, 0);

    public static bool DrawsOn(string game, DayOfWeek d) => game == Mega
        ? d is DayOfWeek.Wednesday or DayOfWeek.Friday or DayOfWeek.Sunday
        : d is DayOfWeek.Tuesday or DayOfWeek.Thursday or DayOfWeek.Saturday;

    /// <summary>Kỳ quay kế tiếp (giờ VN): hôm nay nếu có quay và chưa tới 18:00, không thì ngày quay sau.</summary>
    public static DateOnly NextDrawDate(string game, DateTime nowVn)
    {
        var d = DateOnly.FromDateTime(nowVn);
        if (nowVn.TimeOfDay >= DrawTime.ToTimeSpan()) d = d.AddDays(1);
        while (!DrawsOn(game, d.DayOfWeek)) d = d.AddDays(1);
        return d;
    }
}
