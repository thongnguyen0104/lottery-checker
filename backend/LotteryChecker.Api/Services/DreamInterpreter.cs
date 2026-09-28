using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Luận số giấc mơ: Gemini đọc câu tiếng Việt và CHỈ chọn khoá trong <see cref="DreamBook"/>; số chính/phụ
/// do code tra từ sổ mơ. Gemini tắt/lỗi → lùi về so khớp chuỗi cục bộ (<see cref="DreamBook.MatchLocal"/>),
/// nên tính năng vẫn chạy khi không có key.
///
/// Dùng chung Endpoint/Model/ApiKey với <see cref="GeminiTicketReader"/> (mục "Gemini"), nhưng bật/tắt
/// riêng bằng DreamChat:Enabled — không phụ thuộc Gemini:Enabled của phần đọc vé.
/// </summary>
public class DreamInterpreter
{
    public const string Disclaimer =
        "Số tham khảo theo quan niệm dân gian, không có cơ sở khoa học và không đảm bảo trúng thưởng.";

    private const int MaxSecondary = 5;
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly DreamBook _book;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DreamInterpreter> _log;
    private readonly bool _enabled;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly string? _apiKey;
    private readonly string? _thinkingLevel;
    private readonly int _maxRetries;
    private readonly int _retryDelayMs;

    public DreamInterpreter(HttpClient http, DreamBook book, IMemoryCache cache, IConfiguration config,
                            ILogger<DreamInterpreter> log)
    {
        _http = http; _book = book; _cache = cache; _log = log;
        _enabled = config.GetValue("DreamChat:Enabled", true);
        _endpoint = config["Gemini:Endpoint"] ?? "https://generativelanguage.googleapis.com/v1beta";
        _model = config["Gemini:Model"] ?? "gemini-3.1-flash-lite";
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

    public async Task<DreamResult> InterpretAsync(string message, CancellationToken ct = default)
    {
        var normalized = Whitespace.Replace(message.Trim(), " ");
        var cacheKey = "dream:" + normalized.ToLowerInvariant();
        if (_cache.TryGetValue(cacheKey, out DreamResult? cached) && cached != null) return cached;

        string? aiError = null;
        if (AiEnabled)
        {
            var (ai, error) = await AskGeminiAsync(normalized, ct);
            if (ai != null)
            {
                var result = Build(ai.Summary, ai.Keys.Select(_book.Find).OfType<DreamEntry>(), ai.Explanation, "ai", null);
                _cache.Set(cacheKey, result, TimeSpan.FromHours(6));
                _log.LogInformation("Luận số AI: {Len} ký tự → {Keys}.", normalized.Length,
                                    string.Join(",", result.Entries.Select(e => e.Key)));
                return result;
            }
            aiError = error;
        }

        // Lùi về so khớp cục bộ — không cache lâu, để lượt sau có thể được Gemini trả lời.
        var entries = _book.MatchLocal(normalized);
        var local = Build(
            entries.Count == 0 ? "Chưa nhận ra chi tiết nào có trong sổ mơ" : string.Join(", ", entries.Select(e => e.Label)),
            entries,
            entries.Count == 0
                ? "Sổ mơ hiện chưa có mục nào khớp với mô tả này. Thử kể rõ con vật, người hay sự việc bạn thấy."
                : "Các chi tiết được đối chiếu trực tiếp với sổ mơ dân gian.",
            "local", aiError);
        _cache.Set(cacheKey, local, TimeSpan.FromMinutes(5));
        return local;
    }

    /// <summary>Số chính = số đầu của mục đầu tiên; số phụ = các số còn lại, bỏ trùng, tối đa 5.</summary>
    internal static DreamResult Build(string summary, IEnumerable<DreamEntry> matched, string explanation,
                                      string source, string? aiError)
    {
        var entries = matched.DistinctBy(e => e.Key).ToArray();
        var all = entries.SelectMany(e => e.Numbers).Distinct().ToList();
        return new DreamResult(
            summary,
            entries.Select(e => new MatchedEntry(e.Key, e.Label, e.Numbers)).ToArray(),
            all.FirstOrDefault(),
            all.Skip(1).Take(MaxSecondary).ToArray(),
            explanation,
            Disclaimer,
            source,
            aiError);
    }

    internal sealed record AiAnswer(string Summary, string[] Keys, string Explanation);

    private async Task<(AiAnswer? Answer, string? Error)> AskGeminiAsync(string message, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var (answer, error) = await SendOnceAsync(message, ct);
            if (answer != null || attempt >= _maxRetries || !GeminiTicketReader.IsTransient(error))
                return (answer, error);
            await Task.Delay(_retryDelayMs, ct);
        }
    }

    private async Task<(AiAnswer?, string?)> SendOnceAsync(string message, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint.TrimEnd('/')}/models/{_model}:generateContent")
            {
                Content = new StringContent(JsonSerializer.Serialize(BuildRequest(message)), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("x-goog-api-key", _apiKey);

            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Gemini (luận số) HTTP {Status}.", (int)resp.StatusCode);
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
            _log.LogWarning(ex, "Gemini (luận số) thất bại ({Error}).", error);
            return (null, error);
        }
    }

    private static string Clip(string? s, int max)
    {
        var t = (s ?? "").Trim();
        return t.Length > max ? t[..max] : t;
    }

    private object BuildRequest(string message)
    {
        // Gửi cả sổ mơ (vài chục mục, chỉ khoá + tên + từ đồng nghĩa — KHÔNG gửi số) để model chọn khoá.
        var book = string.Join("\n", _book.Entries.Select(e => $"- {e.Key}: {e.Label} ({string.Join(", ", e.Aliases)})"));
        var generationConfig = new Dictionary<string, object>
        {
            ["responseMimeType"] = "application/json",
            ["responseSchema"] = new
            {
                type = "OBJECT",
                properties = new Dictionary<string, object>
                {
                    ["summary"] = new { type = "STRING", description = "Tóm tắt giấc mơ, tối đa 15 từ." },
                    ["keys"] = new { type = "ARRAY", items = new { type = "STRING", @enum = _book.Keys } },
                    ["explanation"] = new { type = "STRING", description = "1–3 câu tiếng Việt." },
                },
                required = new[] { "summary", "keys", "explanation" },
                propertyOrdering = new[] { "summary", "keys", "explanation" },
            },
        };
        if (!string.IsNullOrWhiteSpace(_thinkingLevel))
            generationConfig["thinkingConfig"] = new { thinkingLevel = _thinkingLevel };

        return new
        {
            systemInstruction = new { parts = new[] { new { text = SystemPrompt } } },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = $"Sổ mơ:\n{book}\n\nMô tả của người dùng:\n{message}" } } },
            },
            generationConfig,
        };
    }

    private const string SystemPrompt =
        """
        Bạn là trợ lý luận số dân gian cho một website dò vé số.
        - Đọc mô tả giấc mơ/sự việc của người dùng, tìm các con vật, người, sự vật trong đó.
        - Chỉ chọn khoá có trong "Sổ mơ" được cung cấp, chi tiết quan trọng nhất đứng đầu, tối đa 3 khoá.
          Hiểu cả từ đồng nghĩa (vd "con trăn" → ran). Không có mục phù hợp thì trả keys rỗng.
        - Không nêu con số nào trong summary/explanation (hệ thống tự điền số).
        - explanation: giải thích ngắn bằng tiếng Việt vì sao chọn các mục đó, giọng thân thiện.
        - Không bao giờ khẳng định sẽ trúng. Nếu nội dung không phải mô tả giấc mơ/sự việc, trả keys rỗng
          và nhắc người dùng kể lại giấc mơ.
        """;

    private sealed record RawAnswer(string? Summary, string[]? Keys, string? Explanation);
}
