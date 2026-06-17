using System.Text.RegularExpressions;
using HtmlAgilityPack;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;

namespace LotteryChecker.Api.Services;

// Cào kết quả XSKT Miền Nam từ xosodaiphat.com (HTML TĨNH — không AJAX).
// Trang hiển thị ~7 ngày gần nhất; mỗi ngày 1 bảng <table class="...table-xsmn...">:
//   - <thead>: ô đầu "Giải", các ô sau là tên đài (cột).
//   - mỗi <tr>: <td>G.n + 1 <td class=tn_prize> mỗi đài, số nằm trong <span>.
//   - ngày lấy từ link <a href="/xsmn-DD-MM-YYYY.html"> ngay trước bảng.
// LƯU Ý (best-effort): nếu xosodaiphat đổi DOM, selector cần chỉnh. Sanity-check 18 số/đài
// đảm bảo KHÔNG ghi data rác vào DB.
public class ResultScraper
{
    private readonly AppDbContext _db;
    private readonly ILogger<ResultScraper> _logger;
    private readonly HttpClient _http;
    private readonly ProvinceMatcher _provinces;

    private const string Url = "https://xosodaiphat.com/xsmn-xo-so-mien-nam.html";
    private const int PerProvince = 18; // ĐB1+G1..G8 = 1+1+1+2+7+1+3+1+1

    public ResultScraper(AppDbContext db, ILogger<ResultScraper> logger, HttpClient http,
                         ProvinceMatcher provinces)
    {
        _db = db; _logger = logger; _http = http; _provinces = provinces;
    }

    /// <summary>Cào toàn bộ bảng MN (mọi đài, các ngày gần nhất) trên trang. Trả số dòng đã lưu.</summary>
    public async Task<int> FetchLatestMienNam(CancellationToken ct)
    {
        var html = await _http.GetStringAsync(Url, ct);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var tables = doc.DocumentNode.SelectNodes("//table[contains(@class,'table-xsmn')]");
        if (tables == null || tables.Count == 0)
        {
            _logger.LogError("Không tìm thấy bảng 'table-xsmn' ở {Url} (DOM có thể đã đổi).", Url);
            return 0;
        }

        var toSave = new List<LotteryResult>();
        foreach (var table in tables)
        {
            var date = FindDate(table);
            if (date is null)
            {
                _logger.LogWarning("Không xác định được ngày cho 1 bảng — bỏ qua.");
                continue;
            }

            // Cột đài (bỏ ô đầu "Giải")
            var headers = table.SelectNodes(".//thead//th") ?? table.SelectNodes(".//tr[1]/th");
            if (headers == null || headers.Count < 2) continue;
            var codes = new List<string?>();
            for (int i = 1; i < headers.Count; i++)
                codes.Add(_provinces.FindBestMatch(HtmlEntity.DeEntitize(headers[i].InnerText)));

            // Gom số theo đài
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

            // Sanity-check 18 số/đài; chỉ lưu đài đạt. Idempotent theo (ngày, đài).
            foreach (var (code, list) in byProvince)
            {
                if (list.Count != PerProvince)
                {
                    _logger.LogWarning("Đài {Code} {Date}: {Got}/{Exp} số — bỏ qua.",
                        code, date.Value, list.Count, PerProvince);
                    continue;
                }
                var existing = _db.LotteryResults.Where(r => r.DrawDate == date.Value && r.Province == code);
                _db.LotteryResults.RemoveRange(existing);
                toSave.AddRange(list);
            }
        }

        if (toSave.Count == 0)
        {
            _logger.LogError("Cào xosodaiphat: không lưu được dòng nào (parse 0 đài hợp lệ).");
            return 0;
        }

        _db.LotteryResults.AddRange(toSave);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Cào xosodaiphat: lưu {Count} dòng ({Provinces} đài-ngày).",
            toSave.Count, toSave.Count / PerProvince);
        return toSave.Count;
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
