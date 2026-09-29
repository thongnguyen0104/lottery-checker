using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Hạn mức gọi Gemini TOÀN SERVER (mọi người dùng cộng lại). Gói free mỗi model: 15 request/phút,
/// 500 request/ngày — vượt là Google trả 429. Hai lớp:
/// - Theo phút, chia theo tính năng: soi vé 12/phút + luận giấc mơ 12/phút mỗi model; cộng thêm trần
///   chung theo MODEL 14/phút (chừa 1 lượt so với 15 của Google) — luận số lùi về model dự phòng trùng
///   model soi vé thì hai bên cộng lại vẫn không vượt 14.
/// - Theo ngày, chia theo MODEL (soi vé + luận số cộng chung): mặc định 450/ngày, chừa ~50 cho
///   benchmark/test tay. Ngày reset theo giờ Thái Bình Dương như Google (~14–15h giờ VN).
/// Đếm từng lượt gọi HTTP thật (kể cả lượt gọi lại).
///
/// Hết lượt thì KHÔNG chờ: bên gọi coi như Gemini lỗi "rate_limited" và lùi về đường dự phòng sẵn có
/// (soi vé → OCR.space; luận số → model dự phòng / so khớp sổ mơ cục bộ), nên người dùng vẫn có kết quả.
///
/// Bộ đếm nằm trong RAM: restart server là đếm lại từ 0 — Google vẫn nhớ số đã dùng, nên restart
/// nhiều lần trong ngày có thể vẫn chạm 429 (lúc đó vẫn lùi về dự phòng như trên).
/// </summary>
public sealed class GeminiQuota : IDisposable
{
    public const string Error = "rate_limited";

    private static readonly TimeZoneInfo GoogleQuotaZone = FindPacific();

    private readonly RateLimiter _scan;
    private readonly int _dreamPermit;
    private readonly int _modelPermit;
    private readonly int _dailyPermit;
    private readonly Func<DateTimeOffset> _now;
    // Gói free tính quota RIÊNG cho từng model → mỗi model luận số có hạn mức riêng.
    private readonly ConcurrentDictionary<string, RateLimiter> _dream = new(StringComparer.OrdinalIgnoreCase);
    // Trần chung mỗi model/phút, mọi tính năng cộng lại (Google tính 15/phút theo model, không theo tính năng).
    private readonly ConcurrentDictionary<string, RateLimiter> _model = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateOnly Day, int Count)> _daily = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _dailyLock = new();

    public GeminiQuota(IConfiguration config) : this(config, () => DateTimeOffset.UtcNow) { }

    // `now` để test giả giờ; DI không có Func<DateTimeOffset> nên luôn dùng constructor 1 tham số.
    public GeminiQuota(IConfiguration config, Func<DateTimeOffset> now)
    {
        _scan = Create(config.GetValue("Gemini:ScanPermitPerMinute", 12));
        _dreamPermit = config.GetValue("DreamChat:PermitPerMinute", 12);
        _modelPermit = config.GetValue("Gemini:PermitPerMinutePerModel", 14);
        _dailyPermit = config.GetValue("Gemini:DailyPermitPerModel", 450);
        _now = now;
    }

    /// <summary>Lấy 1 lượt gọi `model` cho soi vé; false = hết lượt phút này hoặc model hết lượt hôm nay.</summary>
    public bool TryAcquireScan(string model) => TryAcquire(model, _scan);

    /// <summary>Lấy 1 lượt gọi `model` cho luận giấc mơ; false = hết lượt phút này hoặc model hết lượt hôm nay.</summary>
    public bool TryAcquireDream(string model) => TryAcquire(model, _dream.GetOrAdd(model, _ => Create(_dreamPermit)));

    private bool TryAcquire(string model, RateLimiter perMinute)
    {
        lock (_dailyLock)
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_now(), GoogleQuotaZone).DateTime);
            var used = _daily.TryGetValue(model, out var d) && d.Day == today ? d.Count : 0;
            // Xét hạn mức ngày TRƯỚC: hết ngày thì khỏi tốn lượt phút.
            // Xét lượt của tính năng trước trần model: hụt ở trần model thì chỉ phí lượt của chính tính
            // năng này (cửa sổ trượt không trả lại lượt được), không phí lượt của tính năng kia.
            if (used >= _dailyPermit || !perMinute.AttemptAcquire().IsAcquired) return false;
            if (!_model.GetOrAdd(model, _ => Create(_modelPermit)).AttemptAcquire().IsAcquired) return false;
            _daily[model] = (today, used + 1);
            return true;
        }
    }

    // Cửa sổ trượt (6 đoạn × 10s) thay vì cố định: cửa sổ cố định cho dồn 12 lượt cuối phút trước + 12 lượt
    // đầu phút sau = 24 lượt trong vài giây, trong khi Google đếm theo 60s liên tục.
    private static SlidingWindowRateLimiter Create(int permitPerMinute) => new(new SlidingWindowRateLimiterOptions
    {
        PermitLimit = Math.Max(permitPerMinute, 1),
        Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = 6,
        QueueLimit = 0,
    });

    private static TimeZoneInfo FindPacific()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("PT", TimeSpan.FromHours(-8), "PT", "PT"); }
    }

    public void Dispose()
    {
        _scan.Dispose();
        foreach (var l in _dream.Values.Concat(_model.Values)) l.Dispose();
    }
}
