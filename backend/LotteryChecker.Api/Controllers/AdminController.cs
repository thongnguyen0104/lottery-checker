using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
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
    private readonly ImagePreprocessor _preprocessor;
    private readonly OcrService _ocr;

    public AdminController(ResultScraper scraper, AppDbContext db, IWebHostEnvironment env,
                           ImagePreprocessor preprocessor, OcrService ocr)
    {
        _scraper = scraper; _db = db; _env = env; _preprocessor = preprocessor; _ocr = ocr;
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

    /// <summary>OCR debug (dev): so sánh OCR ảnh gốc vs ảnh sau tiền xử lý, lưu ảnh đã xử lý để xem.</summary>
    [HttpPost("/api/admin/ocr-debug")]
    public async Task<IActionResult> OcrDebug(IFormFile image, CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (image == null || image.Length == 0) return BadRequest(new { error = "Chưa có ảnh" });

        byte[] original;
        using (var ms = new MemoryStream())
        {
            await image.CopyToAsync(ms, ct);
            original = ms.ToArray();
        }

        var processed = _preprocessor.Preprocess(new MemoryStream(original));
        var path = Path.Combine(AppContext.BaseDirectory, "_preprocessed.png");
        await System.IO.File.WriteAllBytesAsync(path, processed, ct);

        static object Dump(TicketInfo i) => new
        {
            rawText = i.RawText,
            confidence = i.OcrConfidence,
            ticketNumber = i.TicketNumber,
            drawDate = i.DrawDate?.ToString("yyyy-MM-dd"),
            province = i.Province
        };

        // Thử nhiều PageSegMode trên ảnh đã tiền xử lý để tìm mode đọc tốt nhất.
        var modes = new[]
        {
            Tesseract.PageSegMode.Auto,
            Tesseract.PageSegMode.SingleColumn,
            Tesseract.PageSegMode.SingleBlock,
            Tesseract.PageSegMode.SparseText,
        };

        return Ok(new
        {
            preprocessedPath = path,
            byMode = modes.Select(m => new { mode = m.ToString(), result = Dump(_ocr.Extract(processed, m)) }),
            original = Dump(_ocr.Extract(original))
        });
    }
}
