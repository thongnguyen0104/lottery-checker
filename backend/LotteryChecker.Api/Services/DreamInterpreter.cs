using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Luận số giấc mơ, theo thứ tự: so khớp sổ mơ cục bộ (<see cref="DreamBook.MatchLocal"/>) → không ra mục
/// nào thì hỏi Gemini model chính → vẫn không ra (lỗi, hết lượt, hoặc không chọn được mục) thì hỏi lần lượt
/// DreamChat:FallbackModels. AI CHỈ chọn khoá trong <see cref="DreamBook"/>; số chính/phụ do code tra từ sổ mơ.
///
/// Dùng chung Endpoint/Model/ApiKey với <see cref="GeminiTicketReader"/> (mục "Gemini"), nhưng bật/tắt
/// riêng bằng DreamChat:Enabled — không phụ thuộc Gemini:Enabled của phần đọc vé.
/// </summary>
public class DreamInterpreter
{
    public const string Disclaimer =
        "Số tham khảo theo quan niệm dân gian, không có cơ sở khoa học và không đảm bảo trúng thưởng.";
    public const string DisclaimerEn =
        "Numbers based on folk beliefs — no scientific basis and no guarantee of winning.";

    private const int MaxSecondary = 5;
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly DreamBook _book;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DreamInterpreter> _log;
    private readonly bool _enabled;
    private readonly string _endpoint;
    private readonly string[] _models;
    private readonly string? _apiKey;
    private readonly string? _thinkingLevel;
    private readonly int _maxRetries;
    private readonly int _retryDelayMs;
    private readonly GeminiQuota? _quota;

    public DreamInterpreter(HttpClient http, DreamBook book, IMemoryCache cache, IConfiguration config,
                            ILogger<DreamInterpreter> log, GeminiQuota? quota = null)
    {
        _http = http; _book = book; _cache = cache; _log = log; _quota = quota;
        _enabled = config.GetValue("DreamChat:Enabled", true);
        _endpoint = config["Gemini:Endpoint"] ?? "https://generativelanguage.googleapis.com/v1beta";
        // Model chính DreamChat:Model (không có thì dùng chung Gemini:Model với đọc vé) rồi tới các model
        // dự phòng DreamChat:FallbackModels, bỏ trùng. Model riêng = quota Google riêng, không giành lượt với soi vé.
        _models = new[] { config["DreamChat:Model"] ?? config["Gemini:Model"] ?? "gemini-3.1-flash-lite" }
            .Concat(config.GetSection("DreamChat:FallbackModels").Get<string[]>() ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _apiKey = config["Gemini:ApiKey"];
        _thinkingLevel = config["DreamChat:ThinkingLevel"] ?? "minimal";
        _maxRetries = config.GetValue("Gemini:MaxRetries", 1);
        _retryDelayMs = config.GetValue("Gemini:RetryDelayMs", 1000);
    }

    /// <summary>Có gọi Gemini không (false = chỉ so khớp cục bộ).</summary>
    public bool AiEnabled => _enabled && !string.IsNullOrWhiteSpace(_apiKey);

    public sealed record MatchedEntry(string Key, string Label, string[] Numbers);

    /// <summary>
    /// source: "ai" | "local". mainNumber null = không mục nào khớp (không bịa số).
    /// aiError: vì sao không dùng được Gemini (null khi Gemini trả lời được hoặc bị tắt).
    /// </summary>
    public sealed record DreamResult(
        string Summary, MatchedEntry[] Entries, string? MainNumber, string[] SecondaryNumbers,
        string Explanation, string Disclaimer, string Source, string? AiError);

    public async Task<DreamResult> InterpretAsync(string message, CancellationToken ct = default, bool en = false)
    {
        var normalized = Whitespace.Replace(message.Trim(), " ");
        var cacheKey = (en ? "dream:en:" : "dream:") + normalized.ToLowerInvariant();
        if (_cache.TryGetValue(cacheKey, out DreamResult? cached) && cached != null) return cached;

        // 1) Sổ mơ cục bộ trước: khớp được là trả luôn, không tốn lượt Gemini (gói free chỉ 15 req/phút).
        var entries = _book.MatchLocal(normalized);
        if (entries.Count > 0)
        {
            var local = Build(LocalSummary(entries, en), entries, LocalExplanation(entries, normalized, en), "local", null, en);
            _cache.Set(cacheKey, local, TimeSpan.FromHours(6));
            return local;
        }

        // 2) Không khớp → hỏi Gemini (hiểu từ đồng nghĩa, câu kể dài); model chính lỗi/hết lượt hoặc không
        //    tìm ra mục nào thì 3) hỏi lần lượt các model dự phòng (mỗi model một quota riêng).
        string? aiError = null;
        AiAnswer? emptyAnswer = null;   // model trả lời được nhưng không chọn mục nào
        if (AiEnabled)
            foreach (var model in _models)
            {
                var (ai, error) = await AskGeminiAsync(model, normalized, en, ct);
                if (ai == null) { aiError = error; continue; }
                var matched = ai.Keys.Select(_book.Find).OfType<DreamEntry>().ToArray();
                if (matched.Length == 0) { emptyAnswer ??= ai; continue; }

                var result = Build(ai.Summary, matched, ai.Explanation, "ai", null, en);
                _cache.Set(cacheKey, result, TimeSpan.FromHours(6));
                _log.LogInformation("Luận số AI ({Model}): {Len} ký tự → {Keys}.", model, normalized.Length,
                                    string.Join(",", result.Entries.Select(e => e.Key)));
                return result;
            }

        // Không nguồn nào ra mục → không bịa số. Không cache lâu, để lượt sau có thể được AI trả lời.
        var none = emptyAnswer != null
            ? Build(emptyAnswer.Summary, [], emptyAnswer.Explanation, "ai", null, en)
            : en
                ? Build("No known dream symbols found", [],
                        "The dream book has no entry matching this description yet. Try naming the animal, person or event you saw.",
                        "local", aiError, en)
                : Build("Chưa nhận ra chi tiết nào có trong sổ mơ", [],
                        "Sổ mơ hiện chưa có mục nào khớp với mô tả này. Thử kể rõ con vật, người hay sự việc bạn thấy.",
                        "local", aiError);
        _cache.Set(cacheKey, none, TimeSpan.FromMinutes(5));
        return none;
    }

    // Sổ mơ chỉ có tên mục tiếng Việt → bản tiếng Anh vẫn giữ tên mục gốc trong ngoặc kép.
    private static string LocalSummary(IReadOnlyList<DreamEntry> entries, bool en) => en
        ? $"A dream about {JoinEn(entries.Select(e => $"\"{e.Label}\""))}"
        : entries.Count == 1
        ? $"Giấc mơ thấy {Lower(entries[0].Label)}"
        : $"Giấc mơ có {JoinVi(entries.Select(e => Lower(e.Label)))}";

    // Vài mẫu câu cho đỡ lặp; chọn theo hash câu hỏi (không random) để cùng câu → cùng lời trong một lần chạy.
    private static readonly Func<string, string, string>[] LocalTemplates =
    [
        (what, first) => $"Trong giấc mơ của bạn có hình ảnh {what}. Dân gian vẫn tin {first} là điềm báo con số, nên mình tra sổ mơ giúp bạn các cặp số bên dưới nhé.",
        (what, first) => $"Mơ thấy {what} à? Theo quan niệm xưa, {first} ứng với những con số riêng — đây là các số người ta hay chọn khi gặp giấc mơ như vậy.",
        (what, first) => $"Chi tiết đáng chú ý nhất là {first}. Mình đã đối chiếu {what} với sổ mơ dân gian và gợi ý bộ số dưới đây, bạn tham khảo cho vui nha.",
    ];

    private static string LocalExplanation(IReadOnlyList<DreamEntry> entries, string message, bool en)
    {
        if (en)
        {
            var items = JoinEn(entries.Select(e => $"\"{e.Label}\""));
            return $"Your dream features {items}. By Vietnamese folk tradition these symbols map to lucky numbers, so here are the pairs from the dream book — just for fun!";
        }
        var what = JoinVi(entries.Select(e => Lower(e.Label)));
        var pick = (int)((uint)StringComparer.OrdinalIgnoreCase.GetHashCode(message) % LocalTemplates.Length);
        return LocalTemplates[pick](what, Lower(entries[0].Label));
    }

    private static string Lower(string label) => label.Length == 0 ? label : char.ToLower(label[0]) + label[1..];

    private static string JoinEn(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count <= 1 ? string.Concat(list) : $"{string.Join(", ", list[..^1])} and {list[^1]}";
    }

    /// <summary>"a", "a và b", "a, b và c".</summary>
    private static string JoinVi(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count <= 1 ? string.Concat(list) : $"{string.Join(", ", list[..^1])} và {list[^1]}";
    }

    /// <summary>Số chính = số đầu của mục đầu tiên; số phụ = các số còn lại, bỏ trùng, tối đa 5.</summary>
    internal static DreamResult Build(string summary, IEnumerable<DreamEntry> matched, string explanation,
                                      string source, string? aiError, bool en = false)
    {
        var entries = matched.DistinctBy(e => e.Key).ToArray();
        var all = entries.SelectMany(e => e.Numbers).Distinct().ToList();
        return new DreamResult(
            summary,
            entries.Select(e => new MatchedEntry(e.Key, e.Label, e.Numbers)).ToArray(),
            all.FirstOrDefault(),
            all.Skip(1).Take(MaxSecondary).ToArray(),
            explanation,
            en ? DisclaimerEn : Disclaimer,
            source,
            aiError);
    }

    internal sealed record AiAnswer(string Summary, string[] Keys, string Explanation);

    private async Task<(AiAnswer? Answer, string? Error)> AskGeminiAsync(string model, string message, bool en, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var (answer, error) = await SendOnceAsync(model, message, en, ct);
            if (answer != null || attempt >= _maxRetries || !GeminiTicketReader.IsTransient(error))
                return (answer, error);
            await Task.Delay(_retryDelayMs, ct);
        }
    }

    private async Task<(AiAnswer?, string?)> SendOnceAsync(string model, string message, bool en, CancellationToken ct)
    {
        if (_quota != null && !_quota.TryAcquireDream(model))
        {
            _log.LogInformation("Gemini (luận số, {Model}): hết hạn mức/phút — bỏ qua.", model);
            return (null, GeminiQuota.Error);
        }
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint.TrimEnd('/')}/models/{model}:generateContent")
            {
                Content = new StringContent(JsonSerializer.Serialize(BuildRequest(model, message, en)), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("x-goog-api-key", _apiKey);

            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Gemini (luận số, {Model}) HTTP {Status}.", model, (int)resp.StatusCode);
                return (null, $"http_{(int)resp.StatusCode}");
            }

            var json = GeminiTicketReader.ExtractText(body);
            if (json == null) return (null, "empty");
            var raw = JsonSerializer.Deserialize<RawAnswer>(GeminiTicketReader.StripCodeFence(json), JsonOpts);
            if (raw == null) return (null, "bad_json");
            return (new AiAnswer(Clip(raw.Summary, 200), raw.Keys?.Take(6).ToArray() ?? [], Clip(raw.Explanation, 600)), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var error = ex switch
            {
                TaskCanceledException => "timeout",
                JsonException => "bad_json",
                HttpRequestException => "network",
                _ => "error",
            };
            _log.LogWarning(ex, "Gemini (luận số, {Model}) thất bại ({Error}).", model, error);
            return (null, error);
        }
    }

    private static string Clip(string? s, int max)
    {
        var t = (s ?? "").Trim();
        return t.Length > max ? t[..max] : t;
    }

    private object BuildRequest(string model, string message, bool en)
    {
        var generationConfig = new Dictionary<string, object>
        {
            ["responseMimeType"] = "application/json",
            ["responseSchema"] = new
            {
                type = "OBJECT",
                properties = new Dictionary<string, object>
                {
                    ["summary"] = new { type = "STRING", description = en ? "Dream summary in English, max 15 words." : "Tóm tắt giấc mơ, tối đa 15 từ." },
                    // Sổ mơ CHỈ gửi qua enum này, dạng tên mục (không khoá, không alias, không số): model tự hiểu
                    // từ đồng nghĩa, danh sách chỉ xuất hiện 1 lần → ~650 token/lượt thay vì ~3.800.
                    ["keys"] = new { type = "ARRAY", items = new { type = "STRING", @enum = _book.Labels } },
                    ["explanation"] = new { type = "STRING", description = en ? "1–3 sentences in English." : "1–3 câu tiếng Việt." },
                },
                required = new[] { "summary", "keys", "explanation" },
                propertyOrdering = new[] { "summary", "keys", "explanation" },
            },
        };
        // thinkingLevel chỉ có ở dòng Gemini 3 — gửi cho 2.x (dùng thinkingBudget) là bị 400.
        if (!string.IsNullOrWhiteSpace(_thinkingLevel) && model.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase))
            generationConfig["thinkingConfig"] = new { thinkingLevel = _thinkingLevel };

        return new
        {
            systemInstruction = new { parts = new[] { new { text = en ? SystemPrompt + SystemPromptEn : SystemPrompt } } },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = message } } },
            },
            generationConfig,
        };
    }

    private const string SystemPrompt =
        """
        Bạn là trợ lý luận số dân gian cho một website dò vé số.
        - Đọc mô tả giấc mơ/sự việc của người dùng, tìm các con vật, người, sự vật trong đó.
        - Trường keys: chọn tên mục sổ mơ (danh sách cho phép trong schema) khớp chi tiết đó, quan trọng nhất đứng đầu, tối đa 3.
          Hiểu cả từ đồng nghĩa/cách gọi vùng miền (vd "con trăn" → Rắn, "lợn" → Heo). Không có mục phù hợp thì trả keys rỗng.
        - Không nêu con số nào trong summary/explanation (hệ thống tự điền số).
        - explanation: giải thích ngắn bằng tiếng Việt vì sao chọn các mục đó, giọng thân thiện.
        - Không bao giờ khẳng định sẽ trúng. Nếu nội dung không phải mô tả giấc mơ/sự việc, trả keys rỗng
          và nhắc người dùng kể lại giấc mơ.
        """;

    // Người dùng chọn tiếng Anh: vẫn chọn keys theo sổ mơ tiếng Việt, chỉ đổi ngôn ngữ phần chữ trả về.
    private const string SystemPromptEn =
        """

        - Người dùng dùng tiếng Anh: viết summary và explanation bằng tiếng Anh (bỏ qua yêu cầu tiếng Việt ở trên).
          Mô tả có thể bằng tiếng Anh — vẫn chọn keys đúng tên mục sổ mơ tiếng Việt trong schema.
        """;

    private sealed record RawAnswer(string? Summary, string[]? Keys, string? Explanation);
}
