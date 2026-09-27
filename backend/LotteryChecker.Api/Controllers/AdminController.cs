using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LotteryChecker.Api.Controllers;

[ApiController]
public class AdminController : ControllerBase
{
    private readonly ResultScraper _scraper;
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ImagePreprocessor _preprocessor;
    private readonly OcrService _ocr;
    private readonly CloudOcrService _cloudOcr;
    private readonly GeminiTicketReader _gemini;
    private readonly RapidOcrService _onnx;
    private readonly TicketResultValidator _validator;
    private readonly LocalRetryReader _retry;

    public AdminController(ResultScraper scraper, AppDbContext db, IWebHostEnvironment env,
                           ImagePreprocessor preprocessor, OcrService ocr, CloudOcrService cloudOcr,
                           GeminiTicketReader gemini, RapidOcrService onnx, TicketResultValidator validator,
                           LocalRetryReader retry)
    {
        _scraper = scraper; _db = db; _env = env; _preprocessor = preprocessor; _ocr = ocr;
        _cloudOcr = cloudOcr; _gemini = gemini; _onnx = onnx; _validator = validator; _retry = retry;
    }

    // Tên file mang đáp án để tính accuracy: "288921_2026-06-05_BinhDuong.jpg".
    private static readonly System.Text.RegularExpressions.Regex ExpectedFromName =
        new(@"^(?<num>\d{6})_(?<date>\d{4}-\d{2}-\d{2})_(?<prov>[A-Za-z]+)");

    /// <summary>
    /// Benchmark tiền xử lý cho PP-OCRv5 (dev): mỗi ảnh chạy đủ 4 kiểu A–D × DoAngle bật/tắt,
    /// đo thời gian tiền xử lý + OCR, so với đáp án trong tên file. Quan trọng nhất là
    /// <c>falsePass</c>: qua validate (tức sẽ KHÔNG gọi cloud) mà đọc sai — phải bằng 0.
    ///
    /// curl -F "images=@288921_2026-06-05_BinhDuong.jpg" -F "images=@..." "http://localhost:5177/api/admin/ocr-benchmark?repeat=3"
    ///
    /// unclip = thử UnClipRatio khác cấu hình (nới khung chữ); grid=false = bỏ lưới 4 kiểu lọc × DoAngle,
    /// chỉ chạy các dòng Main/Pipeline (luồng thật) cho nhanh.
    ///
    /// gemini=true = thêm dòng "Gemini:&lt;model&gt;" (cần Gemini:Enabled + ApiKey); local=false = bỏ hết các
    /// dòng OCR cục bộ, chỉ đo Gemini. Với Gemini, falsePass = đủ 3 trường hợp lệ mà SAI — lỗi user không
    /// được cảnh báo. Gói free giới hạn số request/phút: nhiều ảnh × repeat dễ bị 429 (xem log).
    /// </summary>
    [HttpPost("/api/admin/ocr-benchmark")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> OcrBenchmark(List<IFormFile> images, [FromQuery] int repeat = 1,
                                                  [FromQuery] bool includeText = false,
                                                  [FromQuery] float? unclip = null, [FromQuery] bool grid = true,
                                                  [FromQuery] bool gemini = false, [FromQuery] bool local = true,
                                                  CancellationToken ct = default)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (images == null || images.Count == 0) return BadRequest(new { error = "Chưa có ảnh (field 'images')" });
        if (gemini && !_gemini.IsEnabled)
            return BadRequest(new { error = "Gemini đang tắt hoặc thiếu ApiKey (Gemini:Enabled / Gemini:ApiKey)" });
        if (!local && !gemini) return BadRequest(new { error = "local=false thì phải bật gemini=true" });
        repeat = Math.Clamp(repeat, 1, 10);

        var configs = grid && local
            ? Enum.GetValues<LocalPreprocess>()
                .SelectMany(mode => new[] { true, false }.Select(doAngle => (mode, doAngle)))
                .ToArray()
            : [];
        var options = unclip is > 0 ? _onnx.Options with { UnClipRatio = unclip.Value } : _onnx.Options;
        TicketInfo Ocr(Image<Rgba32> img) => _onnx.Extract(img, options);
        var rows = new List<BenchRow>();

        foreach (var file in images)
        {
            ct.ThrowIfCancellationRequested();
            byte[] bytes;
            using (var ms = new MemoryStream()) { await file.CopyToAsync(ms, ct); bytes = ms.ToArray(); }

            var expected = ParseExpected(file.FileName);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var prepared = _preprocessor.Load(new MemoryStream(bytes));
            var loadMs = sw.Elapsed.TotalMilliseconds;

            // Gemini nhận đúng loại ảnh luồng thật gửi: JPEG ≤1600px (FE đã nén sẵn, hoặc EncodeForCloud).
            // prepMs = nén JPEG; ocrMs = một lượt gọi Gemini (gồm cả mạng), trung bình repeat lần.
            if (gemini)
            {
                sw.Restart();
                var jpeg = prepared.EncodeForCloud();
                var encodeMs = sw.Elapsed.TotalMilliseconds;
                double geminiMs = 0;
                TicketInfo? read = null;
                for (var i = 0; i < repeat; i++)
                {
                    sw.Restart();
                    read = await _gemini.ReadAsync(jpeg, ct);
                    geminiMs += sw.Elapsed.TotalMilliseconds;
                }
                rows.Add(ToRow(file.FileName, $"Gemini:{_gemini.Model}", loadMs, encodeMs, geminiMs / repeat,
                               read ?? new TicketInfo { RawText = "(Gemini lỗi — xem log)" }, expected, includeText));
            }
            if (!local) continue;

            // Chạy mồi 1 lần (không tính): lần OCR đầu trên ảnh kích thước mới chậm hơn hẳn
            // (ONNX cấp phát buffer), nếu tính vào thì kiểu chạy đầu tiên luôn bị thiệt.
            Ocr(prepared.GetLocal(LocalPreprocess.Original));

            foreach (var (mode, doAngle) in configs)
            {
                double prepMs = 0, ocrMs = 0;
                TicketInfo info = null!;
                for (var i = 0; i < repeat; i++)
                {
                    // Original ở luồng thật KHÔNG clone (0ms); ở đây có clone nên prepMs của nó
                    // hơi cao hơn thực tế vài chục ms — so các kiểu khác với nhau thì vẫn công bằng.
                    sw.Restart();
                    using var img = prepared.CreateFiltered(mode);
                    prepMs += sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                    info = _onnx.Extract(img, options with { DoAngle = doAngle });
                    ocrMs += sw.Elapsed.TotalMilliseconds;
                }

                rows.Add(ToRow(file.FileName, $"{mode}{(doAngle ? "" : "-noAngle")}",
                    loadMs, prepMs / repeat, ocrMs / repeat, info, expected, includeText));
            }

            // Đúng luồng /api/scan: lượt chính theo cấu hình, rồi các cách đọc lại khi thiếu/sai ngày
            // hoặc đài — mọi cách chạy trên CÙNG một lượt chính để so công bằng. ocrMs = chính + đọc lại.
            if (_preprocessor.RetryMode is { } retryMode)
            {
                // Strategy:* gọi ĐÚNG hàm /api/scan dùng; LinesOnly:* chỉ để xem riêng cách cắt dòng làm được gì.
                var variants = new (string Name, Func<TicketInfo, TicketValidation, IReadOnlyList<string>> Run)[]
                {
                    ($"Main:{_preprocessor.LocalMode}", (_, _) => []),
                    ("Strategy:Full", (i, c) => _retry.Run(i, c, prepared, retryMode, Ocr, RetryStrategy.Full).Filled),
                    ("Strategy:Lines", (i, c) => _retry.Run(i, c, prepared, retryMode, Ocr, RetryStrategy.Lines).Filled),
                    ("LinesOnly:x1", (i, c) => _retry.RetryLines(i, c, prepared, retryMode, Ocr).Filled),
                    ("LinesOnly:x2", (i, c) => _retry.RetryLines(i, c, prepared, retryMode, Ocr, scale: 2).Filled),
                };
                var ms = new double[variants.Length];
                var infos = new TicketInfo[variants.Length];
                double mainPrepMs = 0;
                for (var i = 0; i < repeat; i++)
                {
                    sw.Restart();
                    using var main = prepared.CreateFiltered(_preprocessor.LocalMode);
                    mainPrepMs += sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                    var mainInfo = Ocr(main);
                    var mainMs = sw.Elapsed.TotalMilliseconds;
                    var check = _validator.Validate(mainInfo);

                    for (var v = 0; v < variants.Length; v++)
                    {
                        infos[v] = mainInfo.Clone();
                        ms[v] += mainMs;
                        if (!LocalRetryReader.Needed(check)) continue;
                        sw.Restart();
                        variants[v].Run(infos[v], check);
                        ms[v] += sw.Elapsed.TotalMilliseconds;
                    }
                }
                for (var v = 0; v < variants.Length; v++)
                    rows.Add(ToRow(file.FileName, variants[v].Name, loadMs, mainPrepMs / repeat, ms[v] / repeat,
                                   infos[v], expected, includeText));
            }
        }

        return Ok(new { repeat, configuredLocalPreprocess = _preprocessor.LocalMode.ToString(),
                        configuredDoAngle = _onnx.DoAngle, summary = Summarize(rows), rows });
    }

    /// <summary>
    /// Dò cấu hình tốc độ cho PP-OCRv5 (dev), giữ nguyên tiền xử lý đang cấu hình. Mỗi ảnh chạy
    /// tổ hợp: độ rộng ảnh (giả lập FE thu nhỏ tới W px) × MaxSideLen (cạnh dài ảnh đưa vào model
    /// dò vùng chữ) × ImgResize (cạnh ảnh thu về trước khi dò) × số dòng nhận dạng song song. Chọn tổ hợp nhanh nhất mà vẫn đúng hết,
    /// <c>falsePass</c> = 0.
    ///
    /// curl -F "images=@..." "http://localhost:5177/api/admin/ocr-tuning?widths=1600,1280&amp;maxSides=2000,1280,960&amp;recPar=1,4&amp;repeat=2"
    /// </summary>
    [HttpPost("/api/admin/ocr-tuning")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> OcrTuning(List<IFormFile> images,
        [FromQuery] string widths = "1600,1280,1024", [FromQuery] string maxSides = "2000,1280,960",
        [FromQuery] string imgResizes = "1024", [FromQuery] string recPar = "1,4", [FromQuery] int repeat = 2,
        CancellationToken ct = default)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (images == null || images.Count == 0) return BadRequest(new { error = "Chưa có ảnh (field 'images')" });
        repeat = Math.Clamp(repeat, 1, 10);

        static int[] Ints(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                        .Select(int.Parse).ToArray();
        var configs = (from w in Ints(widths) from m in Ints(maxSides) from z in Ints(imgResizes) from r in Ints(recPar)
                       select (w, m, z, r)).ToArray();
        var rows = new List<BenchRow>();

        foreach (var file in images)
        {
            ct.ThrowIfCancellationRequested();
            byte[] bytes;
            using (var ms = new MemoryStream()) { await file.CopyToAsync(ms, ct); bytes = ms.ToArray(); }
            var expected = ParseExpected(file.FileName);

            using var prepared = _preprocessor.Load(new MemoryStream(bytes));
            var local = prepared.GetLocal(_preprocessor.LocalMode);
            _onnx.Extract(local);   // chạy mồi, không tính (xem OcrBenchmark)

            foreach (var widthGroup in configs.GroupBy(c => c.w))
            {
                // Giả lập FE gửi ảnh rộng W: thu nhỏ một lần cho cả nhóm cấu hình cùng W.
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using var scaled = local.Width > widthGroup.Key
                    ? local.Clone(x => x.Resize(widthGroup.Key, (int)Math.Round(local.Height * (double)widthGroup.Key / local.Width)))
                    : local.Clone();
                var resizeMs = sw.Elapsed.TotalMilliseconds;
                _onnx.Extract(scaled);   // mồi cho kích thước mới

                foreach (var (w, maxSide, imgResize, rec) in widthGroup)
                {
                    var options = _onnx.Options with { MaxSideLen = maxSide, ImgResize = imgResize, RecMaxDegreeOfParallelism = rec };
                    double ocrMs = 0;
                    TicketInfo info = null!;
                    for (var i = 0; i < repeat; i++)
                    {
                        sw.Restart();
                        info = _onnx.Extract(scaled, options);
                        ocrMs += sw.Elapsed.TotalMilliseconds;
                    }
                    rows.Add(ToRow(file.FileName, $"w{w}-max{maxSide}-img{imgResize}-rec{rec}", 0, resizeMs, ocrMs / repeat, info, expected, false));
                }
            }
        }

        return Ok(new { repeat, baseOptions = _onnx.Options.ToString(), environmentProcessors = Environment.ProcessorCount,
                        summary = Summarize(rows), rows });
    }

    private static (string Number, string Date, string Province)? ParseExpected(string fileName)
    {
        var m = ExpectedFromName.Match(Path.GetFileName(fileName));
        return m.Success ? (m.Groups["num"].Value, m.Groups["date"].Value, m.Groups["prov"].Value) : null;
    }

    private BenchRow ToRow(string file, string config, double loadMs, double prepMs, double ocrMs, TicketInfo info,
                           (string Number, string Date, string Province)? expected, bool includeText)
    {
        var check = _validator.Validate(info);
        return new BenchRow(file, config, Math.Round(loadMs, 1), Math.Round(prepMs, 1), Math.Round(ocrMs, 1),
            info.TicketNumber, info.DrawDate?.ToString("yyyy-MM-dd"), info.Province,
            Math.Round(info.OcrConfidence, 3), check.Passed, check.Reasons,
            expected is { } e1 ? e1.Number == info.TicketNumber : null,
            expected is { } e2 ? e2.Date == info.DrawDate?.ToString("yyyy-MM-dd") : null,
            expected is { } e3 ? string.Equals(e3.Province, info.Province, StringComparison.OrdinalIgnoreCase) : null,
            includeText ? info.RawText : null,
            info.EngineTimings?.GetValueOrDefault("ocrDetect"), info.EngineTimings?.GetValueOrDefault("ocrRecognize"),
            info.EngineTimings?.GetValueOrDefault("ocrLines"));
    }

    // Tổng hợp theo cấu hình. accuracy chỉ tính trên ảnh có đáp án trong tên file.
    private static IEnumerable<object> Summarize(List<BenchRow> rows) => rows.GroupBy(r => r.Config).Select(g =>
    {
        var labeled = g.Where(r => r.NumberCorrect != null).ToList();
        bool AllOk(BenchRow r) => r.NumberCorrect == true && r.DateCorrect == true && r.ProvinceCorrect == true;
        return new
        {
            config = g.Key,
            avgPreprocessMs = Math.Round(g.Average(r => r.PreprocessMs), 1),
            avgOcrMs = Math.Round(g.Average(r => r.OcrMs), 1),
            maxOcrMs = Math.Round(g.Max(r => r.OcrMs), 1),
            avgDetMs = Math.Round(g.Average(r => r.DetMs ?? 0), 1),       // model dò vùng chữ
            avgRecMs = Math.Round(g.Average(r => r.RecMs ?? 0), 1),       // cắt + nhận dạng các dòng
            avgLines = Math.Round(g.Average(r => r.Lines ?? 0), 1),
            passRate = Math.Round(g.Count(r => r.Passed) / (double)g.Count(), 3),     // tỷ lệ KHÔNG phải gọi cloud
            labeledImages = labeled.Count,
            numberAccuracy = labeled.Count == 0 ? (double?)null : Math.Round(labeled.Count(r => r.NumberCorrect == true) / (double)labeled.Count, 3),
            allFieldsAccuracy = labeled.Count == 0 ? (double?)null : Math.Round(labeled.Count(AllOk) / (double)labeled.Count, 3),
            falsePass = labeled.Count(r => r.Passed && !AllOk(r)),                    // qua validate mà SAI — phải = 0
        };
    }).OrderByDescending(s => s.falsePass == 0).ThenByDescending(s => s.allFieldsAccuracy).ThenBy(s => s.avgOcrMs)
      .Cast<object>();

    private sealed record BenchRow(
        string File, string Config, double LoadMs, double PreprocessMs, double OcrMs,
        string? TicketNumber, string? DrawDate, string? Province, double Confidence,
        bool Passed, IReadOnlyList<string> Reasons,
        bool? NumberCorrect, bool? DateCorrect, bool? ProvinceCorrect, string? RawText,
        double? DetMs = null, double? RecMs = null, double? Lines = null);

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

        // Cloud OCR (nếu bật): đọc số vé cách điệu. Lưu cả ảnh gửi lên cloud để xem.
        string? cloudText = null, cloudNumber = null;
        if (_cloudOcr.IsEnabled)
        {
            var cloudImg = _preprocessor.PrepareForCloud(new MemoryStream(original));
            await System.IO.File.WriteAllBytesAsync(
                Path.Combine(AppContext.BaseDirectory, "_cloud.jpg"), cloudImg, ct);
            cloudText = await _cloudOcr.ReadTextAsync(cloudImg, ct);
            cloudNumber = cloudText == null ? null : TicketTextParser.ReadTicketNumber(cloudText);
        }

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
            cloudEnabled = _cloudOcr.IsEnabled,
            cloudTicketNumber = cloudNumber,   // số vé do cloud OCR đọc (đáng tin hơn cho font cách điệu)
            cloudText,                          // toàn bộ text cloud đọc được
            production = Dump(_ocr.Extract(processed)),  // Tesseract đa-PSM (chưa merge cloud)
            byMode = modes.Select(m => new { mode = m.ToString(), result = Dump(_ocr.Extract(processed, m)) }),
            original = Dump(_ocr.Extract(original))
        });
    }
}
