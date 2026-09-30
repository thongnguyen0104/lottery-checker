using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Cờ tính năng: tắt = user thường không thấy, API trả 404. Admin luôn dùng được (xem trước trước khi bật).
/// Đọc qua cache ngắn — mọi request có [RequireFeature] đều hỏi, không cần chạm DB mỗi lần.
/// </summary>
public class FeatureFlags(AppDbContext db, IMemoryCache cache, TimeProvider clock)
{
    public const string CheckHistory = "checkHistory";
    public const string ScratchTickets = "scratchTickets";

    public record Definition(string Key, string NameVi, string NameEn, string DescriptionVi, string DescriptionEn);

    /// <summary>Mọi cờ hợp lệ — thêm tính năng mới thì thêm ở đây.</summary>
    public static readonly IReadOnlyList<Definition> All =
    [
        new(CheckHistory, "Lịch sử dò vé", "Check history",
            "Tab Lịch sử dò vé ở trang Tài khoản (vé đã dò vẫn được ghi lại khi tắt).",
            "The Check history tab on the Account page (checks are still recorded while off)."),
        new(ScratchTickets, "Vé cào 2 số", "2-digit scratch tickets",
            "Mua vé cào, mục Vé của tôi và thẻ số dư ở trang Tài khoản. Vé đã mua vẫn được chốt và trả thưởng khi tắt.",
            "Buying scratch tickets, My tickets and the balance card. Bought tickets still settle and pay out while off."),
    ];

    public record FlagDto(string Key, string Name, string Description, bool Enabled, DateTime? UpdatedAt, string? UpdatedBy);

    private const string CacheKey = "feature-flags";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    public static bool IsKnown(string key) => All.Any(d => d.Key == key);

    private Task<Dictionary<string, FeatureFlag>> LoadAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync(CacheKey, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheFor;
            return await db.FeatureFlags.AsNoTracking().ToDictionaryAsync(x => x.Key, ct);
        })!;

    public async Task<bool> IsEnabledAsync(string key, CancellationToken ct) =>
        (await LoadAsync(ct)).GetValueOrDefault(key)?.Enabled == true;

    /// <summary>Cờ nào user này dùng được: đã bật, hoặc user là admin (xem trước).</summary>
    public async Task<Dictionary<string, bool>> AvailableAsync(bool isAdmin, CancellationToken ct)
    {
        var flags = await LoadAsync(ct);
        return All.ToDictionary(d => d.Key, d => isAdmin || flags.GetValueOrDefault(d.Key)?.Enabled == true);
    }

    /// <summary>Danh sách cho trang Quản trị (tên/mô tả theo ngôn ngữ).</summary>
    public async Task<FlagDto[]> ListAsync(bool en, CancellationToken ct)
    {
        var flags = await LoadAsync(ct);
        return All.Select(d =>
        {
            var f = flags.GetValueOrDefault(d.Key);
            return new FlagDto(d.Key, en ? d.NameEn : d.NameVi, en ? d.DescriptionEn : d.DescriptionVi, f?.Enabled == true,
                               f == null ? null : DateTime.SpecifyKind(f.UpdatedAt, DateTimeKind.Utc), f?.UpdatedBy);
        }).ToArray();
    }

    /// <summary>Bật/tắt cờ; false = key không hợp lệ. Xoá cache để đổi có hiệu lực ngay.</summary>
    public async Task<bool> SetAsync(string key, bool enabled, string? by, CancellationToken ct)
    {
        if (!IsKnown(key)) return false;
        var f = await db.FeatureFlags.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (f == null) db.FeatureFlags.Add(f = new FeatureFlag { Key = key });
        f.Enabled = enabled;
        f.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        f.UpdatedBy = by;
        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey);
        return true;
    }
}
