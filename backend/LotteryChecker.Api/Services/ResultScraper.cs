using HtmlAgilityPack;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;

namespace LotteryChecker.Api.Services;

// Cào kết quả 1 đài Miền Nam cho 1 ngày từ minhngoc.net.vn. Idempotent.
// LƯU Ý (best-effort): trang minhngoc hiển thị bảng nhiều ngày + dùng AJAX, nên selector
// có thể cần tinh chỉnh với DOM thật. Sanity-check ==18 đảm bảo KHÔNG ghi data rác vào DB.
public class ResultScraper
{
    private readonly AppDbContext _db;
    private readonly ILogger<ResultScraper> _logger;
    private readonly HttpClient _http;

    // Cơ cấu giải XSKT Miền Nam thật: số lượng số trúng mỗi giải (tổng 18 số/đài/ngày)
    private static readonly Dictionary<string, int> ExpectedCounts = new()
    {
        { "DB", 1 }, { "1", 1 }, { "2", 1 }, { "3", 2 }, { "4", 7 },
        { "5", 1 }, { "6", 3 }, { "7", 1 }, { "8", 1 }
    };
    private const int TotalExpected = 1 + 1 + 1 + 2 + 7 + 1 + 3 + 1 + 1; // = 18

    public ResultScraper(AppDbContext db, ILogger<ResultScraper> logger, HttpClient http)
    {
        _db = db; _logger = logger; _http = http;
    }

    /// <summary>Cào kết quả 1 đài MN cho 1 ngày. Trả số dòng đã lưu (0 nếu không đạt sanity-check).</summary>
    public async Task<int> FetchProvince(DateOnly date, string provinceSlug, string provinceCode,
                                         CancellationToken ct)
    {
        var url = $"https://www.minhngoc.net.vn/ket-qua-xo-so/mien-nam/{provinceSlug}.html";
        var html = await _http.GetStringAsync(url, ct);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Best-effort: scope vào block bảng kết quả MN mới nhất (tránh gom số của nhiều ngày).
        var block = doc.DocumentNode.SelectSingleNode("//*[contains(@class,'bkqmiennam')]")
                    ?? doc.DocumentNode;

        var rows = new List<LotteryResult>();
        foreach (var tier in ExpectedCounts.Keys)
        {
            var cssClass = tier == "DB" ? "giaidb" : $"giai{tier}";
            var nodes = block.SelectNodes($".//*[contains(@class, '{cssClass}')]");
            if (nodes == null)
            {
                _logger.LogWarning("Không thấy node class {Class} ở {Url}", cssClass, url);
                continue;
            }

            var numbers = nodes
                .SelectMany(n => n.InnerText.Split(new[] { ' ', '\t', '\n', '\r', '-' },
                                                   StringSplitOptions.RemoveEmptyEntries))
                .Where(s => s.All(char.IsDigit) && s.Length is >= 2 and <= 6)
                .ToList();

            foreach (var num in numbers)
                rows.Add(new LotteryResult
                {
                    DrawDate = date, Region = "MN", Province = provinceCode,
                    PrizeTier = tier, Number = num
                });
        }

        // Sanity-check: phải đúng 18 số. Nếu lệch → KHÔNG ghi DB (tránh data rác).
        if (rows.Count != TotalExpected)
        {
            _logger.LogError(
                "Cào {Province} {Date}: {Got}/{Expected} số — KHÔNG lưu DB. URL={Url}. " +
                "(DOM minhngoc có thể đổi/dùng AJAX → cần tinh chỉnh selector.)",
                provinceCode, date, rows.Count, TotalExpected, url);
            return 0;
        }

        // Idempotent: xoá data cũ của (ngày, đài) trước khi insert.
        var existing = _db.LotteryResults.Where(r => r.DrawDate == date && r.Province == provinceCode);
        _db.LotteryResults.RemoveRange(existing);
        _db.LotteryResults.AddRange(rows);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Cào {Province} {Date}: lưu {Count} dòng OK", provinceCode, date, rows.Count);
        return rows.Count;
    }
}
