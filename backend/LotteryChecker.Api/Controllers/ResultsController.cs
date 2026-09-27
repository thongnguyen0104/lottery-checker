using LotteryChecker.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Controllers;

[ApiController]
public class ResultsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ResultsController(AppDbContext db) => _db = db;

    /// <summary>Liệt kê các (ngày → đài) đang có kết quả trong DB, ngày mới nhất trước.</summary>
    [HttpGet("/api/results/available")]
    public async Task<IActionResult> Available(CancellationToken ct)
    {
        var pairs = await _db.LotteryResults
            .Select(r => new { r.DrawDate, r.Province })
            .Distinct()
            .ToListAsync(ct);

        var grouped = pairs
            .GroupBy(p => p.DrawDate)
            .OrderByDescending(g => g.Key)
            .Select(g => new
            {
                drawDate = g.Key.ToString("yyyy-MM-dd"),
                provinces = g.Select(x => x.Province).OrderBy(x => x).ToArray()
            });

        return Ok(grouped);
    }

    /// <summary>
    /// Bảng kết quả đầy đủ của 1 đài trong 1 ngày. Giải xếp ĐB, 1..8; số trong cùng giải giữ
    /// đúng thứ tự trên trang nguồn (thứ tự insert = Id tăng dần, xem ResultScraper).
    /// </summary>
    [HttpGet("/api/results/{date}/{province}")]
    public async Task<ActionResult<ProvinceResultDto>> Detail(DateOnly date, string province, CancellationToken ct)
    {
        var rows = await _db.LotteryResults
            .Where(r => r.DrawDate == date && r.Province == province)
            .OrderBy(r => r.Id)
            .Select(r => new { r.Region, r.PrizeTier, r.Number })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return NotFound(new { error = $"Chưa có kết quả đài {province} ngày {date:dd/MM/yyyy}." });

        var prizes = rows
            .GroupBy(r => r.PrizeTier)
            .OrderBy(g => TierRank(g.Key))
            .Select(g => new PrizeRowDto(g.Key, g.Select(r => r.Number).ToArray()))
            .ToArray();

        return new ProvinceResultDto(date.ToString("yyyy-MM-dd"), province, rows[0].Region, prizes);
    }

    // "DB" đứng đầu, rồi "1".."8"; mã lạ (không nên có) dồn xuống cuối.
    private static int TierRank(string tier) =>
        tier == "DB" ? 0 : int.TryParse(tier, out var n) ? n : int.MaxValue;
}

public record PrizeRowDto(string Tier, string[] Numbers);

public record ProvinceResultDto(string DrawDate, string Province, string Region, PrizeRowDto[] Prizes);
