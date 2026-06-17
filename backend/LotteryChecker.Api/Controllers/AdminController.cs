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

    /// <summary>Cào tay 1 đài để test scraper mà không cần đợi worker chạy 19h. Chỉ Development.</summary>
    [HttpPost("/api/admin/fetch")]
    public async Task<IActionResult> Fetch(
        [FromQuery] DateOnly date, [FromQuery] string slug, [FromQuery] string code,
        CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return NotFound();
        if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(code))
            return BadRequest(new { error = "Cần tham số slug và code" });

        var saved = await _scraper.FetchProvince(date, slug, code, ct);
        return Ok(new
        {
            saved,
            note = saved == 0
                ? "Không lưu (số dòng != 18 hoặc lỗi DOM) — xem log server."
                : "OK"
        });
    }
}
