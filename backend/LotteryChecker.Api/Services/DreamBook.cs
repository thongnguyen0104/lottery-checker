using System.Text.Json;
using System.Text.RegularExpressions;

namespace LotteryChecker.Api.Services;

/// <summary>Một mục sổ mơ: khoá không dấu (AI trả khoá này), tên hiển thị, từ đồng nghĩa, bộ số 2 chữ số.</summary>
public sealed record DreamEntry(string Key, string Label, string[] Aliases, string[] Numbers);

/// <summary>
/// Dữ liệu luận số (Data/dream-book.json, nhúng vào assembly). Là nguồn DUY NHẤT của con số: AI chỉ
/// chọn khoá, số luôn tra từ đây — nên AI không bịa được số. Dữ liệu tĩnh → Singleton.
/// </summary>
public class DreamBook
{
    private static readonly Regex TwoDigits = new(@"^\d{2}$", RegexOptions.Compiled);

    private readonly Dictionary<string, DreamEntry> _byKey;
    private readonly Dictionary<string, DreamEntry> _byLabel;
    // Alias dài trước: "mèo rừng" phải thắng "mèo" khi so khớp cục bộ.
    private readonly (Regex Pattern, DreamEntry Entry)[] _aliases;

    public DreamBook() : this(LoadEmbedded()) { }

    public DreamBook(IReadOnlyList<DreamEntry> entries)
    {
        foreach (var e in entries)
            if (e.Numbers.Length == 0 || e.Numbers.Any(n => !TwoDigits.IsMatch(n)))
                throw new InvalidOperationException($"Sổ mơ: mục '{e.Key}' có số không hợp lệ.");
        Entries = entries;
        _byKey = entries.ToDictionary(e => e.Key, StringComparer.OrdinalIgnoreCase);
        _byLabel = entries.ToDictionary(e => e.Label, StringComparer.OrdinalIgnoreCase);   // tên trùng → lỗi ngay lúc nạp
        _aliases = entries
            .SelectMany(e => e.Aliases.Append(e.Label).Select(a => (Alias: a.ToLowerInvariant(), Entry: e)))
            .DistinctBy(x => x.Alias)
            .OrderByDescending(x => x.Alias.Length)
            // Ranh giới chữ theo Unicode (\b của .NET không hiểu "ắ" là chữ cái trong mọi trường hợp).
            .Select(x => (new Regex($@"(?<!\p{{L}}){Regex.Escape(x.Alias)}(?!\p{{L}})", RegexOptions.Compiled), x.Entry))
            .ToArray();
    }

    public IReadOnlyList<DreamEntry> Entries { get; }

    public IReadOnlyList<string> Keys => Entries.Select(e => e.Key).ToArray();

    public IReadOnlyList<string> Labels => Entries.Select(e => e.Label).ToArray();

    /// <summary>Tìm theo khoá hoặc tên hiển thị (AI trả tên mục — xem DreamInterpreter.BuildRequest).</summary>
    public DreamEntry? Find(string? keyOrLabel) =>
        keyOrLabel != null && (_byKey.TryGetValue(keyOrLabel.Trim(), out var e) || _byLabel.TryGetValue(keyOrLabel.Trim(), out e))
            ? e : null;

    /// <summary>
    /// So khớp chuỗi đơn giản, dùng khi Gemini tắt/lỗi. Alias dài khớp trước và "ăn" đoạn chữ đó, để
    /// "mèo rừng" không ra thêm "mèo nhà". Thứ tự kết quả = thứ tự xuất hiện trong câu.
    /// </summary>
    public IReadOnlyList<DreamEntry> MatchLocal(string message)
    {
        var text = message.ToLowerInvariant();
        var taken = new bool[text.Length];
        var hits = new List<(int Pos, DreamEntry Entry)>();
        foreach (var (pattern, entry) in _aliases)
            foreach (Match m in pattern.Matches(text))
            {
                if (Enumerable.Range(m.Index, m.Length).Any(i => taken[i])) continue;
                for (var i = m.Index; i < m.Index + m.Length; i++) taken[i] = true;
                hits.Add((m.Index, entry));
            }
        return hits.OrderBy(h => h.Pos).Select(h => h.Entry).Distinct().ToArray();
    }

    private static List<DreamEntry> LoadEmbedded()
    {
        using var stream = typeof(DreamBook).Assembly.GetManifestResourceStream("LotteryChecker.Api.Data.dream-book.json")
                           ?? throw new InvalidOperationException("Thiếu resource Data/dream-book.json.");
        var file = JsonSerializer.Deserialize<DreamBookFile>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        // Bảng 40 mục (canonical) trước, mở rộng sau: alias trùng thì mục canonical thắng. Mục không có số
        // ("context_only", vd "ma") chỉ để AI hiểu ngữ cảnh — bỏ, vì luận số không được bịa số.
        return (file?.Entries ?? []).Concat(file?.ExtendedEntries ?? [])
            .Where(e => e.Numbers.Length > 0)
            .ToList();
    }

    private sealed record DreamBookFile(
        List<DreamEntry>? Entries,
        [property: System.Text.Json.Serialization.JsonPropertyName("extended_entries")] List<DreamEntry>? ExtendedEntries);
}
