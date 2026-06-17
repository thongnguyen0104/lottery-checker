using LotteryChecker.Api.Data;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Controllers;

[ApiController]
public class AdminController : ControllerBase
{
    private readonly ResultScraper _scraper;
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public AdminController(ResultScraper scraper, AppDbContext db, IWebHostEnvironment env)
    {
        _scraper = scraper; _db = db; _env = env;
    }

    /// <summary>Cào tay 30 ngày gần nhất (mọi đài MN) để test scraper. Chỉ Development.</summary>
    [HttpPost("/api/admin/fetch")]
    public async Task<IActionResult> Fetch(CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        var saved = await _scraper.FetchLast30Days(ct);
        return Ok(new
        {
            saved,
            note = saved == 0
                ? "Không lưu (parse 0 đài hợp lệ hoặc lỗi DOM) — xem log server."
                : $"OK — lưu {saved} dòng (~{saved / 18} đài-ngày)."
        });
    }

    /// <summary>Liệt kê các (ngày, đài) đang có trong DB + số dòng mỗi cặp. Chỉ Development.</summary>
    [HttpGet("/api/admin/data")]
    public async Task<IActionResult> Data(CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        var items = await _db.LotteryResults
            .GroupBy(r => new { r.DrawDate, r.Province })
            .Select(g => new
            {
                drawDate = g.Key.DrawDate,
                province = g.Key.Province,
                count = g.Count()
            })
            .OrderByDescending(x => x.drawDate)
            .ThenBy(x => x.province)
            .ToListAsync(ct);

        return Ok(new
        {
            boards = items.Count,
            totalRows = items.Sum(x => x.count),
            items
        });
    }
}
