using System.Text.RegularExpressions;
using HtmlAgilityPack;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;

namespace LotteryChecker.Api.Services;

// Cào kết quả XSKT Miền Nam từ xosodaiphat.com (HTML TĨNH — không AJAX).
// Mỗi ngày 1 URL riêng: /xsmn-DD-MM-YYYY.html, chứa bảng <table class="...table-xsmn...">:
//   - <thead>: ô đầu "Giải", các ô sau là tên đài (cột).
//   - mỗi <tr>: <td>G.n + 1 <td class=tn_prize> mỗi đài, số nằm trong <span>.
//   - ngày lấy từ link <a href="/xsmn-DD-MM-YYYY.html"> trong trang.
// Cào 30 ngày gần nhất (vé hết hạn lĩnh thưởng sau 30 ngày). Dedupe theo (ngày, đài),
// sanity-check 18 số/đài để KHÔNG ghi data rác. LƯU Ý best-effort: selector phụ thuộc DOM xosodaiphat.
public class ResultScraper
{
    private readonly AppDbContext _db;
    private readonly ILogger<ResultScraper> _logger;
    private readonly HttpClient _http;
    private readonly ProvinceMatcher _provinces;

    private const int PerProvince = 18; // ĐB1+G1..G8 = 1+1+1+2+7+1+3+1+1
    private const int DaysBack = 30;    // vé hết hạn sau 30 ngày

    public ResultScraper(AppDbContext db, ILogger<ResultScraper> logger, HttpClient http,
                         ProvinceMatcher provinces)
    {
        _db = db; _logger = logger; _http = http; _provinces = provinces;
    }

    /// <summary>Cào kết quả 30 ngày gần nhất (mọi đài MN). Trả tổng số dòng đã lưu.</summary>
    public async Task<int> FetchLast30Days(CancellationToken ct)
    {
        // Dedupe theo (ngày, đài) — các trang ngày có thể trùng lặp board.
        var acc = new Dictionary<(DateOnly Date, string Code), List<LotteryResult>>();

        var today = DateOnly.FromDateTime(DateTime.Now);
        for (int i = 0; i < DaysBack; i++)
        {
            var date = today.AddDays(-i);
            var url = $"https://xosodaiphat.com/xsmn-{date:dd-MM-yyyy}.html";
            try
            {
                var html = await _http.GetStringAsync(url, ct);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                ParseBoardsInto(doc, acc);
            }
            catch (Exception ex)
            {
                // 404 (ngày chưa có kết quả) hoặc lỗi mạng — bỏ qua ngày đó, tiếp tục.
                _logger.LogWarning("Bỏ qua {Url}: {Msg}", url, ex.Message);
            }
            await Task.Delay(250, ct); // lịch sự với server, tránh rate-limit
        }

        return await PersistAsync(acc, ct);
    }

    // Parse mọi bảng table-xsmn trong 1 trang, gom vào acc (chỉ giữ đài đủ 18 số; overwrite trùng key).
    private void ParseBoardsInto(HtmlDocument doc,
        Dictionary<(DateOnly Date, string Code), List<LotteryResult>> acc)
    {
        var tables = doc.DocumentNode.SelectNodes("//table[contains(@class,'table-xsmn')]");
        if (tables == null) return;

        foreach (var table in tables)
        {
            var date = FindDate(table);
            if (date is null) continue;

            var headers = table.SelectNodes(".//thead//th") ?? table.SelectNodes(".//tr[1]/th");
            if (headers == null || headers.Count < 2) continue;
            var codes = new List<string?>();
            for (int i = 1; i < headers.Count; i++)
                codes.Add(_provinces.FindBestMatch(HtmlEntity.DeEntitize(headers[i].InnerText)));

            var byProvince = new Dictionary<string, List<LotteryResult>>();
            var rows = table.SelectNodes(".//tbody/tr") ?? table.SelectNodes(".//tr");
            if (rows == null) continue;

            foreach (var row in rows)
            {
                var cells = row.SelectNodes("./td");
                if (cells == null || cells.Count < 2) continue;

                var tier = MapTier(HtmlEntity.DeEntitize(cells[0].InnerText));
                if (tier is null) continue;

                for (int c = 1; c < cells.Count; c++)
                {
                    var colIdx = c - 1;
                    if (colIdx >= codes.Count) break;
                    var code = codes[colIdx];
                    if (code is null) continue;

                    var spans = cells[c].SelectNodes(".//span");
                    if (spans == null) continue;
                    foreach (var span in spans)
                    {
                        var num = span.InnerText.Trim();
                        if (num.Length is < 2 or > 6 || !num.All(char.IsDigit)) continue;

                        if (!byProvince.TryGetValue(code, out var list))
                            byProvince[code] = list = new();
                        list.Add(new LotteryResult
                        {
                            DrawDate = date.Value, Region = "MN",
                            Province = code, PrizeTier = tier, Number = num
                        });
                    }
                }
            }

            foreach (var (code, list) in byProvince)
                if (list.Count == PerProvince)
                    acc[(date.Value, code)] = list; // dedupe: 1 board/đài/ngày
        }
    }

    // Idempotent: xoá data cũ của từng (ngày, đài) rồi insert.
    private async Task<int> PersistAsync(
        Dictionary<(DateOnly Date, string Code), List<LotteryResult>> acc, CancellationToken ct)
    {
        if (acc.Count == 0)
        {
            _logger.LogError("Cào xosodaiphat: không parse được đài hợp lệ nào (DOM có thể đổi).");
            return 0;
        }

        var all = new List<LotteryResult>();
        foreach (var ((date, code), list) in acc)
        {
            var existing = _db.LotteryResults.Where(r => r.DrawDate == date && r.Province == code);
            _db.LotteryResults.RemoveRange(existing);
            all.AddRange(list);
        }
        _db.LotteryResults.AddRange(all);

        // Tự dọn vé đã hết hạn lĩnh thưởng (cũ hơn 30 ngày) — chỉ chạy khi cào thành công.
        var cutoff = DateOnly.FromDateTime(DateTime.Now).AddDays(-DaysBack);
        var stale = _db.LotteryResults.Where(r => r.DrawDate < cutoff).ToList();
        if (stale.Count > 0) _db.LotteryResults.RemoveRange(stale);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Cào xosodaiphat: lưu {Count} dòng ({Boards} đài-ngày); dọn {Stale} dòng cũ (>{Days} ngày).",
            all.Count, acc.Count, stale.Count, DaysBack);
        return all.Count;
    }

    // "G.8"->"8", "G.ĐB"/"ĐB"/"Đặc biệt"->"DB", còn lại null (bỏ qua hàng không phải giải)
    private static string? MapTier(string label)
    {
        var t = label.Replace("G.", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (t.Contains("ĐB", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("DB", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Đặc", StringComparison.OrdinalIgnoreCase))
            return "DB";
        return t is "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" ? t : null;
    }

    // Ngày từ link '/xsmn-DD-MM-YYYY.html' gần nhất phía trước bảng (fallback: text dd/MM/yyyy).
    private static DateOnly? FindDate(HtmlNode table)
    {
        var links = table.SelectNodes("preceding::a[contains(@href,'/xsmn-')]");
        var href = links?
            .Select(a => a.GetAttributeValue("href", ""))
            .LastOrDefault(h => Regex.IsMatch(h, @"xsmn-\d{1,2}-\d{1,2}-\d{4}"));
        if (href != null)
        {
            var m = Regex.Match(href, @"xsmn-(\d{1,2})-(\d{1,2})-(\d{4})");
            if (m.Success) return BuildDate(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
        }

        var txt = table.SelectNodes("preceding::*[contains(text(),'/20')]")?.LastOrDefault()?.InnerText;
        var m2 = txt is null ? Match.Empty : Regex.Match(txt, @"(\d{1,2})/(\d{1,2})/(\d{4})");
        return m2.Success ? BuildDate(m2.Groups[1].Value, m2.Groups[2].Value, m2.Groups[3].Value) : null;
    }

    private static DateOnly? BuildDate(string d, string m, string y)
        => int.TryParse(d, out var dd) && int.TryParse(m, out var mm) && int.TryParse(y, out var yy)
           && dd is >= 1 and <= 31 && mm is >= 1 and <= 12 && yy is >= 2020 and <= 2099
           ? new DateOnly(yy, mm, dd) : null;
}
