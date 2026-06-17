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
}
