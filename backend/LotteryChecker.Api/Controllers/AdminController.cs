using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LotteryChecker.Api.Controllers;

[ApiController]
public class AdminController : ControllerBase
{
    private readonly ResultScraper _scraper;
    private readonly IWebHostEnvironment _env;

    public AdminController(ResultScraper scraper, IWebHostEnvironment env)
    {
        _scraper = scraper; _env = env;
    }

    /// <summary>Cào tay toàn bộ bảng MN (mọi đài, các ngày gần nhất) để test scraper. Chỉ Development.</summary>
    [HttpPost("/api/admin/fetch")]
    public async Task<IActionResult> Fetch(CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        var saved = await _scraper.FetchLatestMienNam(ct);
        return Ok(new
        {
            saved,
            note = saved == 0
                ? "Không lưu (parse 0 đài hợp lệ hoặc lỗi DOM) — xem log server."
                : $"OK — lưu {saved} dòng (~{saved / 18} đài-ngày)."
        });
    }
}
