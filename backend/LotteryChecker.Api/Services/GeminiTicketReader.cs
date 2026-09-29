using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LotteryChecker.Api.Models;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Đọc vé bằng Gemini (mặc định <c>gemini-3.1-flash-lite</c>): gửi ẢNH, nhận thẳng JSON
/// {số vé, ngày, đài} — khác OCR.space chỉ trả text thô rồi còn phải parse. Model tự hiểu bố cục
/// tờ vé nên phân biệt được số vé với mệnh giá/seri, tên đài với dòng nhà in — những chỗ parse
/// text OCR phải dựa vào luật (TicketTextParser, ProvinceMatcher).
///
/// Best-effort như <see cref="CloudOcrService"/>: mọi lỗi (thiếu key, mạng, quota, JSON hỏng)
/// → trả null, luồng /api/scan tự lùi về nguồn khác. Không bao giờ làm vỡ scan.
/// </summary>
public class GeminiTicketReader
{
    /// <summary>
    /// Gemini không trả điểm tin cậy. Đặt 1 để <see cref="TicketResultValidator"/> chỉ xét luật
    /// nghiệp vụ (số 6 chữ số, ngày hợp lý, đài có thật) — đúng phần kiểm được ở kết quả Gemini.
    /// </summary>
    public const double AssumedConfidence = 1.0;

    private static readonly Regex SixDigits = new(@"^\d{6}$", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly ILogger<GeminiTicketReader> _log;
    private readonly GeminiOptions _opt;
    private readonly GeminiQuota? _quota;

    public GeminiTicketReader(HttpClient http, IConfiguration config, ILogger<GeminiTicketReader> log,
                              GeminiQuota? quota = null)
    {
        _http = http;
        _log = log;
        _quota = quota;
        _opt = config.GetSection("Gemini").Get<GeminiOptions>() ?? new GeminiOptions();
    }

    /// <summary>Bật khi có cấu hình hợp lệ (Enabled + ApiKey không rỗng).</summary>
    public bool IsEnabled => _opt.Enabled && !string.IsNullOrWhiteSpace(_opt.ApiKey);

    /// <summary>Tên model, đưa vào log/benchmark để biết kết quả do model nào đọc.</summary>
    public string Model => _opt.Model;

    /// <summary>
    /// Kết quả đọc: <see cref="Info"/> khi đọc được, không thì <see cref="Error"/> nói vì sao (của lượt
    /// gọi cuối). <see cref="Retried"/> = mã lỗi các lượt trước đã được gọi lại (rỗng = đúng 1 lượt).
    /// </summary>
    public sealed record Result(TicketInfo? Info, string? Error, IReadOnlyList<string> Retried)
    {
        public Result(TicketInfo? info, string? error) : this(info, error, []) { }
    }

    /// <summary>Gửi ảnh JPEG của vé cho Gemini, trả số vé/ngày/đài đọc được hoặc null nếu lỗi.</summary>
    public async Task<TicketInfo?> ReadAsync(byte[] jpeg, CancellationToken ct = default) =>
        (await TryReadAsync(jpeg, ct)).Info;

    /// <summary>
    /// Như <see cref="ReadAsync"/>, kèm mã lỗi ngắn khi thất bại để /api/scan báo được VÌ SAO phải lùi
    /// về OCR.space: "disabled" | "timeout" | "http_{status}" (429 = hết quota, 503 = model quá tải)
    /// | "empty" (bị chặn / không trả chữ) | "bad_json" | "network" | "error".
    ///
    /// Lỗi tạm thời phía Google (<see cref="IsTransient"/>, hay gặp nhất là 503 "model is overloaded")
    /// thì chờ <c>Gemini:RetryDelayMs</c> rồi gọi lại, tối đa <c>Gemini:MaxRetries</c> lần — lượt sau
    /// thường trả lời được trong 1–3s, nhanh hơn hẳn lùi về OCR.space (có lúc gần 20s).
    /// </summary>
    public async Task<Result> TryReadAsync(byte[] jpeg, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            _log.LogDebug("Gemini tắt hoặc thiếu ApiKey — bỏ qua.");
            return new Result(null, "disabled");
        }

        var retried = new List<string>();
        while (true)
        {
            var result = await SendOnceAsync(jpeg, ct);
            if (result.Info != null || retried.Count >= _opt.MaxRetries || !IsTransient(result.Error))
                return result with { Retried = retried };

            retried.Add(result.Error!);
            _log.LogInformation("Gemini {Error} — gọi lại lần {Try} sau {DelayMs}ms.",
                                result.Error, retried.Count, _opt.RetryDelayMs);
            await Task.Delay(_opt.RetryDelayMs, ct);
        }
    }

    /// <summary>
    /// Lỗi đáng gọi lại: Google quá tải/trục trặc tạm thời (500/502/503/504). KHÔNG gọi lại 429 (hết
    /// quota — lượt sau vẫn 429, lại tốn thêm lượt), timeout (đã chờ đủ Gemini:TimeoutSeconds rồi),
    /// hay lỗi do chính request (400/403 key sai...) — gọi lại vẫn y vậy.
    /// </summary>
    internal static bool IsTransient(string? error) => error is "http_500" or "http_502" or "http_503" or "http_504";

    private async Task<Result> SendOnceAsync(byte[] jpeg, CancellationToken ct)
    {
        if (_quota != null && !_quota.TryAcquireScan(_opt.Model))
        {
            _log.LogInformation("Gemini: hết hạn mức soi vé/phút — bỏ qua, lùi về nguồn khác.");
            return new Result(null, GeminiQuota.Error);
        }
        try
        {
            // Key đi bằng header, KHÔNG để ở query ?key= — URL hay lọt vào log/exception message.
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{_opt.Endpoint.TrimEnd('/')}/models/{_opt.Model}:generateContent")
            {
                Content = new StringContent(JsonSerializer.Serialize(BuildRequest(jpeg)), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("x-goog-api-key", _opt.ApiKey);

            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Gemini HTTP {Status}: {Body}", (int)resp.StatusCode, Trunc(body));
                return new Result(null, $"http_{(int)resp.StatusCode}");
            }

            var json = ExtractText(body);
            if (json == null)
            {
                // Bị chặn (promptFeedback.blockReason) hoặc hết lượt sinh mà không ra chữ nào.
                _log.LogWarning("Gemini không trả nội dung: {Body}", Trunc(body));
                return new Result(null, "empty");
            }

            var ticket = JsonSerializer.Deserialize<GeminiTicket>(StripCodeFence(json), JsonOpts);
            return ticket == null ? new Result(null, "bad_json") : new Result(ToTicketInfo(ticket, json), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // user huỷ request — không phải lỗi Gemini
        }
        catch (Exception ex)
        {
            // TaskCanceledException khi ct KHÔNG bị huỷ = hết HttpClient.Timeout (Gemini:TimeoutSeconds).
            var error = ex switch
            {
                TaskCanceledException => "timeout",
                JsonException => "bad_json",
                HttpRequestException => "network",
                _ => "error",
            };
            _log.LogWarning(ex, "Gemini thất bại ({Error}).", error);
            return new Result(null, error);
        }
    }

    /// <summary>
    /// Biến JSON Gemini trả thành TicketInfo, bỏ mọi giá trị sai định dạng (số vé không đủ 6 chữ số,
    /// ngày không có thật, đài ngoài danh sách) thành null — form sẽ đánh dấu cho user điền tay.
    /// </summary>
    internal static TicketInfo ToTicketInfo(GeminiTicket t, string rawJson)
    {
        var number = t.TicketNumber is { } n ? Whitespace.Replace(n, "") : null;
        if (number != null && !SixDigits.IsMatch(number)) number = null;

        DateOnly? date = DateOnly.TryParseExact(t.DrawDate?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                                DateTimeStyles.None, out var d) && d.Year is >= 2020 and <= 2099
            ? d : null;

        var province = ProvinceMatcher.AllCodes
            .FirstOrDefault(c => string.Equals(c, t.Province?.Trim(), StringComparison.OrdinalIgnoreCase));

        return new TicketInfo
        {
            RawText = rawJson,
            CloudText = rawJson,
            TicketNumber = number,
            TicketNumberFromCloud = number != null,
            DrawDate = date,
            DrawDateVotes = date != null ? 1 : 0,
            // Mã đài lấy từ danh sách cố định (enum trong schema) — không có chuyện "khớp gần đúng".
            Province = province,
            ProvinceExact = province != null,
            OcrConfidence = AssumedConfidence,
        };
    }

    private object BuildRequest(byte[] jpeg)
    {
        var generationConfig = new Dictionary<string, object>
        {
            ["responseMimeType"] = "application/json",
            ["responseSchema"] = ResponseSchema,
            // Giữ temperature mặc định (1.0): Google khuyến cáo không hạ ở dòng Gemini 3.
        };
        // "minimal" = gần như không suy nghĩ → trả lời nhanh nhất. Để rỗng = mặc định của model
        // (3.1 Flash-Lite mặc định đã là minimal; Gemini 3 Flash mặc định high → chậm hơn nhiều).
        if (!string.IsNullOrWhiteSpace(_opt.ThinkingLevel))
            generationConfig["thinkingConfig"] = new { thinkingLevel = _opt.ThinkingLevel };
        // Low/Medium/High = số token cho mỗi ảnh (280/560/1120 ở Gemini 3). Rỗng = mặc định (High).
        if (!string.IsNullOrWhiteSpace(_opt.MediaResolution))
            generationConfig["mediaResolution"] = $"MEDIA_RESOLUTION_{_opt.MediaResolution.Trim().ToUpperInvariant()}";

        return new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    // Ảnh trước, câu lệnh sau — thứ tự Google khuyên cho prompt một ảnh.
                    parts = new object[]
                    {
                        new { inlineData = new { mimeType = "image/jpeg", data = Convert.ToBase64String(jpeg) } },
                        new { text = Prompt },
                    },
                },
            },
            generationConfig,
        };
    }

    // Luật đọc vé nằm cả ở prompt lẫn schema: schema chặn được định dạng (đài phải thuộc enum),
    // prompt nói phần schema không diễn tả được (bỏ mệnh giá, ngày in ngày-trước-tháng, dòng nhà in).
    private const string Prompt =
        """
        This is a photo of a Vietnamese lottery ticket ("vé xổ số kiến thiết"). Extract:
        - ticketNumber: the 6-digit ticket number. It is printed large and usually repeated several
          times on the ticket. Ignore prices (e.g. "10.000đ"), series/serial codes, barcodes and dates.
        - drawDate: the draw date ("Mở thưởng ngày ..."). Vietnamese tickets print DAY first
          (dd-MM-yyyy, e.g. 05-06-2026 is 5 June 2026). Return it as yyyy-MM-dd.
        - province: the issuing lottery company, i.e. the province name right after
          "XỔ SỐ KIẾN THIẾT". Pick its code from the enum (TPHCM = TP. Hồ Chí Minh,
          MB = Xổ số Miền Bắc / Thủ Đô). The printing-house line ("In tại ...", "Công ty in ...")
          is NOT the province.
        Use null for any field you cannot read with certainty. Never guess.
        """;

    // Schema kiểu OpenAPI của Gemini (responseSchema). Cả 3 trường luôn có mặt, không đọc được = null.
    private static readonly object ResponseSchema = new
    {
        type = "OBJECT",
        properties = new Dictionary<string, object>
        {
            ["ticketNumber"] = new { type = "STRING", nullable = true, description = "Exactly 6 digits." },
            ["drawDate"] = new { type = "STRING", nullable = true, description = "yyyy-MM-dd" },
            ["province"] = new { type = "STRING", nullable = true, @enum = ProvinceMatcher.AllCodes },
        },
        required = new[] { "ticketNumber", "drawDate", "province" },
        propertyOrdering = new[] { "ticketNumber", "drawDate", "province" },
    };

    /// <summary>Ghép text của ứng viên đầu tiên (bỏ phần "thought" nếu có). Null = không có chữ nào.</summary>
    internal static string? ExtractText(string body)
    {
        var parsed = JsonSerializer.Deserialize<GenerateContentResponse>(body, JsonOpts);
        var parts = parsed?.Candidates?.FirstOrDefault()?.Content?.Parts;
        if (parts == null) return null;
        var text = string.Concat(parts.Where(p => p.Thought != true).Select(p => p.Text));
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    // Đã ép responseMimeType=application/json nên hiếm khi có ```json ... ```, nhưng gỡ cho chắc.
    internal static string StripCodeFence(string s)
    {
        s = s.Trim();
        if (!s.StartsWith("```", StringComparison.Ordinal)) return s;
        var start = s.IndexOf('\n');
        var end = s.LastIndexOf("```", StringComparison.Ordinal);
        return start >= 0 && end > start ? s[(start + 1)..end].Trim() : s;
    }

    private static string Trunc(string s) => s.Length > 500 ? s[..500] : s;

    // ---- Cấu hình + DTO ----

    private sealed class GeminiOptions
    {
        public bool Enabled { get; set; }
        public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
        public string Model { get; set; } = "gemini-3.1-flash-lite";
        public string? ApiKey { get; set; }
        public string? ThinkingLevel { get; set; } = "minimal";
        public string? MediaResolution { get; set; }
        public int MaxRetries { get; set; } = 1;       // xem TryReadAsync / IsTransient
        public int RetryDelayMs { get; set; } = 1000;
    }

    internal sealed record GeminiTicket(string? TicketNumber, string? DrawDate, string? Province);

    private sealed class GenerateContentResponse
    {
        public List<Candidate>? Candidates { get; set; }
    }

    private sealed class Candidate
    {
        public Content? Content { get; set; }
    }

    private sealed class Content
    {
        public List<Part>? Parts { get; set; }
    }

    private sealed class Part
    {
        public string? Text { get; set; }
        public bool? Thought { get; set; }
    }
}
