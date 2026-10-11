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
    public const string ShopMap = "shopMap";
    public const string Sites = "sites";
    public const string Blog = "blog";

    /// <summary>DefaultOn: trạng thái khi admin chưa từng bật/tắt (chưa có dòng trong DB) — tính năng đã chạy
    /// từ trước khi có cờ thì để true, không thì deploy xong tự ẩn mất.</summary>
    public record Definition(string Key, string NameVi, string NameEn, string DescriptionVi, string DescriptionEn,
                             bool DefaultOn = false);

    /// <summary>Mọi cờ hợp lệ — thêm tính năng mới thì thêm ở đây.</summary>
    public static readonly IReadOnlyList<Definition> All =
    [
        new(CheckHistory, "Lịch sử dò vé", "Check history",
            "Tab Lịch sử dò vé ở trang Tài khoản (vé đã dò vẫn được ghi lại khi tắt).",
            "The Check history tab on the Account page (checks are still recorded while off)."),
        new(ScratchTickets, "Vé cào 2 số", "2-digit scratch tickets",
            "Mua vé cào, mục Vé của tôi và thẻ số dư ở trang Tài khoản. Vé đã mua vẫn được chốt và trả thưởng khi tắt.",
            "Buying scratch tickets, My tickets and the balance card. Bought tickets still settle and pay out while off."),
        new(ShopMap, "Bản đồ điểm bán", "Shop map",
            "Bản đồ điểm bán vé số do người dùng ghim (/ban-do): thêm điểm, xác nhận, đánh giá, báo vé trúng, báo cáo.",
            "The user-pinned lottery shop map (/ban-do): add shops, confirm, review, report wins and abuse."),
        new(Sites, "Website con", "Mini-sites",
            "Người dùng / đại lý tự tạo website riêng (/s/{tên}): giao diện, sản phẩm, bài viết, nhận giữ vé trả tại quầy.",
            "Users / agents build their own site (/s/{name}): theme, products, posts, in-store ticket reservations."),
        new(Blog, "Blog", "Blog",
            "Tab Blog (/blog): đọc, viết bài, đăng ảnh, like, bình luận. Tắt thì ẩn tab và chặn API; bài cũ vẫn giữ.",
            "The Blog tab (/blog): read, post, upload images, vote, comment. Off hides the tab and blocks the API; posts are kept.",
            DefaultOn: true),
    ];

    public record FlagDto(string Key, string Name, string Description, bool Enabled, DateTime? UpdatedAt, string? UpdatedBy);

    private const string CacheKey = "feature-flags";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    public static bool IsKnown(string key) => All.Any(d => d.Key == key);

    private static bool On(Dictionary<string, FeatureFlag> flags, string key) =>
        flags.GetValueOrDefault(key)?.Enabled ?? All.FirstOrDefault(d => d.Key == key)?.DefaultOn == true;

    private Task<Dictionary<string, FeatureFlag>> LoadAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync(CacheKey, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheFor;
            return await db.FeatureFlags.AsNoTracking().ToDictionaryAsync(x => x.Key, ct);
        })!;

    public async Task<bool> IsEnabledAsync(string key, CancellationToken ct) =>
        On(await LoadAsync(ct), key);

    /// <summary>Cờ nào user này dùng được: đã bật, hoặc user là admin (xem trước).</summary>
    public async Task<Dictionary<string, bool>> AvailableAsync(bool isAdmin, CancellationToken ct)
    {
        var flags = await LoadAsync(ct);
        return All.ToDictionary(d => d.Key, d => isAdmin || On(flags, d.Key));
    }

    /// <summary>Danh sách cho trang Quản trị (tên/mô tả theo ngôn ngữ).</summary>
    public async Task<FlagDto[]> ListAsync(bool en, CancellationToken ct)
    {
        var flags = await LoadAsync(ct);
        return All.Select(d =>
        {
            var f = flags.GetValueOrDefault(d.Key);
            return new FlagDto(d.Key, en ? d.NameEn : d.NameVi, en ? d.DescriptionEn : d.DescriptionVi, On(flags, d.Key),
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
