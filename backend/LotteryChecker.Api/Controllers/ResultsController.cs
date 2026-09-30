using LotteryChecker.Api.Data;
using LotteryChecker.Api.Services;
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
        // DB giữ tới 1 năm (cho Dự đoán) nhưng màn Kết quả chỉ liệt kê 30 ngày còn lĩnh thưởng.
        var cutoff = ResultScraper.RecentCutoff();
        var pairs = await _db.LotteryResults
            .Where(r => r.DrawDate >= cutoff)
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
            return NotFound(new { error = Lang.T(Request, $"Chưa có kết quả đài {province} ngày {date:dd/MM/yyyy}.", $"No results yet for {province} on {date:dd/MM/yyyy}.") });

        var prizes = rows
            .GroupBy(r => r.PrizeTier)
            .OrderBy(g => TierRank(g.Key))
            .Select(g => new PrizeRowDto(g.Key, g.Select(r => r.Number).ToArray()))
            .ToArray();

        return new ProvinceResultDto(date.ToString("yyyy-MM-dd"), province, rows[0].Region, prizes);
    }

    /// <summary>
    /// Các giải có 2 số cuối = <paramref name="tail"/> trong dữ liệu đang có (~30 ngày), mới nhất trước
    /// — cho nút "Dò số" của Luận số.
    /// </summary>
    [HttpGet("/api/results/search")]
    public async Task<ActionResult<TailHitDto[]>> SearchTail([FromQuery] string? tail, CancellationToken ct)
    {
        if (tail is not { Length: 2 } || !tail.All(char.IsAsciiDigit))
            return BadRequest(new { error = Lang.T(Request, "Cần đúng 2 chữ số, vd ?tail=32.", "Exactly 2 digits required, e.g. ?tail=32.") });

        var cutoff = ResultScraper.RecentCutoff();

        var rows = await _db.LotteryResults
            .Where(r => r.DrawDate >= cutoff && r.Number.EndsWith(tail))
            .Select(r => new { r.DrawDate, r.Province, r.PrizeTier, r.Number, r.Id })
            .ToListAsync(ct);

        return rows
            .OrderByDescending(r => r.DrawDate).ThenBy(r => r.Province).ThenBy(r => TierRank(r.PrizeTier)).ThenBy(r => r.Id)
            .Select(r => new TailHitDto(r.DrawDate.ToString("yyyy-MM-dd"), r.Province, r.PrizeTier, r.Number))
            .ToArray();
    }

    // "DB" đứng đầu, rồi "1".."8"; mã lạ (không nên có) dồn xuống cuối.
    private static int TierRank(string tier) =>
        tier == "DB" ? 0 : int.TryParse(tier, out var n) ? n : int.MaxValue;
}

public record TailHitDto(string DrawDate, string Province, string Tier, string Number);

public record PrizeRowDto(string Tier, string[] Numbers);

public record ProvinceResultDto(string DrawDate, string Province, string Region, PrizeRowDto[] Prizes);
