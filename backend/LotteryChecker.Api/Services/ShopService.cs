using System.Text.RegularExpressions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Services;

/// <summary>Cấu hình mục "Shops" trong appsettings.</summary>
public class ShopOptions
{
    /// <summary>Mỗi tài khoản tạo tối đa chừng này điểm trong 24h.</summary>
    public int DailyCreateLimit { get; set; } = 10;
    /// <summary>Đủ chừng này người KHÁC NHAU báo cáo (chưa xử lý) thì điểm tự ẩn chờ admin.</summary>
    public int AutoHideReports { get; set; } = 3;
}

/// <summary>Lỗi nghiệp vụ của bản đồ điểm bán — controller trả về <see cref="Status"/> + câu theo ngôn ngữ.</summary>
public class ShopException(int status, string vi, string en) : Exception(en)
{
    public int Status { get; } = status;
    public string Vi { get; } = vi;
    public string En { get; } = en;

    public static ShopException NotFound() => new(404,
        "Không tìm thấy điểm bán (có thể đã bị xoá).", "Shop not found (it may have been deleted).");
    public static ShopException Forbidden() => new(403,
        "Bạn không có quyền với điểm bán này.", "You can't change this shop.");
}

/// <summary>
/// Bản đồ điểm bán vé số do người dùng đóng góp: ghim điểm, xác nhận còn bán, đánh giá, báo vé trúng, báo cáo.
/// Khách chỉ xem; mọi thao tác ghi cần đăng nhập (controller kiểm tra trước khi gọi).
/// </summary>
public partial class ShopService(AppDbContext db, TimeProvider clock, ShopOptions opt)
{
    public const int NameMin = 2, NameMax = 80;
    public const int AddressMax = 200, NoteMax = 500, ReviewMax = 1000, ReportNoteMax = 300;
    public const double DuplicateRadiusM = 30;
    public const double MaxNearbyRadiusM = 20_000;
    public const int MaxMarkersPerQuery = 500;
    public const int NearbyTake = 50, SearchTake = 10, DetailTake = 20;
    public const int MaxPendingImagesPerUser = 6;
    public static readonly TimeSpan PendingImageTtl = TimeSpan.FromHours(24);
    /// <summary>Xác nhận lại sớm hơn chừng này thì không tính thêm.</summary>
    public static readonly TimeSpan ConfirmCooldown = TimeSpan.FromHours(20);
    public const int WinMaxAgeDays = 365;
    public const string Vietlott = "Vietlott";

    // Khung bao Việt Nam (gồm Hoàng Sa, Trường Sa) — chặn toạ độ rác.
    public const double MinLat = 6, MaxLat = 24, MinLng = 102, MaxLng = 118;

    public static readonly IReadOnlyList<string> XsktTiers = ["DB", "G1", "G2", "G3", "G4", "G5", "G6", "G7", "G8", "Other"];
    public static readonly IReadOnlyList<string> VietlottTiers = ["Jackpot", "Jackpot2", "G1", "G2", "G3", "Other"];

    public record ShopInput(string? Name, ShopType Type, double Lat, double Lng, string? Address, string? Phone,
                            bool PhoneConsent, int? OpensAtMin, int? ClosesAtMin, string? Note,
                            int? ImageId = null, bool RemoveImage = false);

    /// <summary>Marker gọn cho bản đồ / danh sách. DistanceM chỉ có ở truy vấn "gần tôi".</summary>
    public record MarkerDto(Guid Id, double Lat, double Lng, ShopType Type, string Name, string Address,
                            double? Rating, int RatingCount, int WinCount, int? OpensAtMin, int? ClosesAtMin,
                            DateTime? LastConfirmedAt, double? DistanceM = null);

    public record ReviewDto(int Id, string Username, int Stars, string? Content, DateTime UpdatedAt, bool Mine);
    public record WinDto(int Id, string Username, DateOnly DrawDate, string ProvinceCode, string PrizeTier,
                         string? ImageUrl, DateTime CreatedAt, bool Mine);

    public record DetailDto(Guid Id, string Name, ShopType Type, double Lat, double Lng, string Address, string? Phone,
                            int? OpensAtMin, int? ClosesAtMin, string? Note, string? ImageUrl, ShopStatus Status,
                            int ConfirmCount, DateTime? LastConfirmedAt, double? Rating, int RatingCount, int WinCount,
                            string CreatedBy, DateTime CreatedAt, bool Mine, bool CanEdit,
                            bool ConfirmedRecently, bool ReportedByMe, ReviewDto? MyReview,
                            ReviewDto[] Reviews, WinDto[] Wins);

    public record ReviewInput(int Stars, string? Content);
    public record WinInput(DateOnly DrawDate, string? ProvinceCode, string? PrizeTier, int? ImageId = null);
    public record ReportInput(ShopReportReason Reason, string? Note);

    public record ImageDto(int Id, string Url);

    public record ReportItemDto(string Username, ShopReportReason Reason, string? Note, DateTime CreatedAt);
    public record ReportedDto(Guid Id, string Name, ShopType Type, string Address, double Lat, double Lng,
                              ShopStatus Status, int ReportCount, string CreatedBy, DateTime CreatedAt, ReportItemDto[] Reports);

    public static string ImageUrl(string key) => $"/api/shops/images/{key}";

    // ───────────────────────── Kiểm tra dữ liệu ─────────────────────────

    /// <summary>null = hợp lệ; còn lại là câu lỗi theo ngôn ngữ.</summary>
    public static string? Validate(ShopInput s, bool en)
    {
        var name = s.Name?.Trim() ?? "";
        if (name.Length is < NameMin or > NameMax)
            return en ? $"Name must be {NameMin}–{NameMax} characters." : $"Tên điểm bán dài {NameMin}–{NameMax} ký tự.";
        if (!Enum.IsDefined(s.Type))
            return en ? "Unknown shop type." : "Loại điểm bán không hợp lệ.";
        if (!InVietnam(s.Lat, s.Lng))
            return en ? "The pin must be inside Vietnam." : "Ghim phải nằm trong lãnh thổ Việt Nam.";
        if ((s.Address?.Trim().Length ?? 0) > AddressMax)
            return en ? $"Address is at most {AddressMax} characters." : $"Địa chỉ tối đa {AddressMax} ký tự.";
        if ((s.Note?.Trim().Length ?? 0) > NoteMax)
            return en ? $"Note is at most {NoteMax} characters." : $"Ghi chú tối đa {NoteMax} ký tự.";
        if (s.OpensAtMin is < 0 or > 1439 || s.ClosesAtMin is < 0 or > 1439 || (s.OpensAtMin == null) != (s.ClosesAtMin == null))
            return en ? "Opening hours are invalid." : "Giờ mở cửa không hợp lệ.";

        if (!string.IsNullOrWhiteSpace(s.Phone))
        {
            // SĐT của người bán dạo là thông tin cá nhân — không nhận, kể cả khi gọi thẳng API.
            if (s.Type == ShopType.Street)
                return en ? "Street sellers can't have a phone number." : "Người bán dạo không được kèm số điện thoại.";
            if (!s.PhoneConsent)
                return en ? "Confirm the phone number may be shown publicly." : "Hãy xác nhận số điện thoại được phép công khai.";
            if (NormalizePhone(s.Phone) is null)
                return en ? "Phone number is invalid." : "Số điện thoại không hợp lệ.";
        }
        return null;
    }

    public static bool InVietnam(double lat, double lng) =>
        double.IsFinite(lat) && double.IsFinite(lng) && lat is >= MinLat and <= MaxLat && lng is >= MinLng and <= MaxLng;

    /// <summary>Bỏ khoảng trắng / chấm / gạch; +84 → 0. null = không phải SĐT VN.</summary>
    public static string? NormalizePhone(string phone)
    {
        var p = PhoneSeparators().Replace(phone.Trim(), "");
        if (p.StartsWith("+84")) p = "0" + p[3..];
        return VnPhone().IsMatch(p) ? p : null;
    }

    [GeneratedRegex(@"[\s.\-()]")]
    private static partial Regex PhoneSeparators();
    [GeneratedRegex(@"^0\d{9,10}$")]
    private static partial Regex VnPhone();

    /// <summary>Khoảng cách 2 điểm (mét) theo Haversine.</summary>
    public static double DistanceM(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6_371_000;
        double ToRad(double d) => d * Math.PI / 180;
        var dLat = ToRad(lat2 - lat1);
        var dLng = ToRad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    // ───────────────────────── Đọc ─────────────────────────

    /// <summary>Điểm đang hiện trong khung nhìn. Nhiều quá thì ưu tiên điểm có vé trúng / được xác nhận nhiều.</summary>
    public async Task<MarkerDto[]> QueryBboxAsync(double minLat, double minLng, double maxLat, double maxLng,
                                                  ShopType[]? types, bool hasWin, CancellationToken ct)
    {
        var q = db.Shops.AsNoTracking().Where(x => x.Status == ShopStatus.Visible
            && x.Lat >= minLat && x.Lat <= maxLat && x.Lng >= minLng && x.Lng <= maxLng);
        if (types is { Length: > 0 }) q = q.Where(x => types.Contains(x.Type));
        if (hasWin) q = q.Where(x => x.WinReportCount > 0);
        var rows = await q.OrderByDescending(x => x.WinReportCount).ThenByDescending(x => x.ConfirmCount)
            .Take(MaxMarkersPerQuery).ToListAsync(ct);
        return rows.Select(x => ToMarker(x)).ToArray();
    }

    /// <summary>Điểm trong bán kính, gần nhất trước. Lọc thô bằng khung vuông trên DB rồi tính Haversine.</summary>
    public async Task<MarkerDto[]> NearbyAsync(double lat, double lng, double radiusM, CancellationToken ct, int take = NearbyTake)
    {
        radiusM = Math.Clamp(radiusM, 1, MaxNearbyRadiusM);
        var dLat = radiusM / 111_320;
        var dLng = radiusM / (111_320 * Math.Max(0.1, Math.Cos(lat * Math.PI / 180)));
        var rows = await db.Shops.AsNoTracking().Where(x => x.Status == ShopStatus.Visible
                && x.Lat >= lat - dLat && x.Lat <= lat + dLat && x.Lng >= lng - dLng && x.Lng <= lng + dLng)
            .Take(2000).ToListAsync(ct);
        return rows.Select(x => (x, d: DistanceM(lat, lng, x.Lat, x.Lng)))
            .Where(t => t.d <= radiusM).OrderBy(t => t.d).Take(take)
            .Select(t => ToMarker(t.x, Math.Round(t.d))).ToArray();
    }

    /// <summary>Tìm theo tên / địa chỉ; có vị trí thì gần nhất trước.</summary>
    public async Task<MarkerDto[]> SearchAsync(string q, double? lat, double? lng, CancellationToken ct)
    {
        q = q.Trim();
        if (q.Length < 2) return [];
        var pattern = $"%{q.Replace("%", "").Replace("_", "")}%";
        var rows = await db.Shops.AsNoTracking()
            .Where(x => x.Status == ShopStatus.Visible && (EF.Functions.Like(x.Name, pattern) || EF.Functions.Like(x.Address, pattern)))
            .Take(200).ToListAsync(ct);
        var withDist = rows.Select(x => (x, d: lat is { } la && lng is { } ln ? DistanceM(la, ln, x.Lat, x.Lng) : (double?)null));
        return withDist.OrderBy(t => t.d ?? 0).ThenBy(t => t.x.Name).Take(SearchTake)
            .Select(t => ToMarker(t.x, t.d is { } d ? Math.Round(d) : null)).ToArray();
    }

    /// <summary>Chi tiết; điểm đang ẩn thì chỉ người tạo / admin xem được.</summary>
    public async Task<DetailDto?> GetAsync(Guid publicId, int? userId, bool isAdmin, CancellationToken ct)
    {
        var s = await db.Shops.AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct);
        if (s == null || (s.Status == ShopStatus.Hidden && s.UserId != userId && !isAdmin)) return null;
        return await DetailAsync(s, userId, isAdmin, ct);
    }

    private async Task<DetailDto> DetailAsync(ShopLocation s, int? userId, bool isAdmin, CancellationToken ct)
    {
        var createdBy = await db.Users.Where(u => u.Id == s.UserId).Select(u => u.Username).FirstOrDefaultAsync(ct) ?? "";
        var reviews = await (from r in db.ShopReviews.AsNoTracking()
                             where r.ShopId == s.Id
                             join u in db.Users on r.UserId equals u.Id
                             orderby r.UpdatedAt descending
                             select new { r, u.Username }).Take(DetailTake).ToListAsync(ct);
        var wins = await (from w in db.ShopWinReports.AsNoTracking()
                          where w.ShopId == s.Id
                          join u in db.Users on w.UserId equals u.Id
                          orderby w.DrawDate descending, w.Id descending
                          select new { w, u.Username }).Take(DetailTake).ToListAsync(ct);

        ReviewDto? mine = null;
        var confirmedRecently = false;
        var reported = false;
        if (userId is { } uid)
        {
            mine = reviews.Where(x => x.r.UserId == uid).Select(x => ToReview(x.r, x.Username, uid)).FirstOrDefault();
            if (mine == null)
            {
                var my = await db.ShopReviews.AsNoTracking().FirstOrDefaultAsync(r => r.ShopId == s.Id && r.UserId == uid, ct);
                if (my != null) mine = ToReview(my, await db.Users.Where(u => u.Id == uid).Select(u => u.Username).FirstAsync(ct), uid);
            }
            var since = Now - ConfirmCooldown;
            confirmedRecently = await db.ShopConfirms.AnyAsync(c => c.ShopId == s.Id && c.UserId == uid && c.ConfirmedAt > since, ct);
            reported = await db.ShopReports.AnyAsync(r => r.ShopId == s.Id && r.UserId == uid && !r.Resolved, ct);
        }

        return new DetailDto(s.PublicId, s.Name, s.Type, s.Lat, s.Lng, s.Address, s.Phone, s.OpensAtMin, s.ClosesAtMin,
            s.Note, s.ImageKey is { } k ? ImageUrl(k) : null, s.Status, s.ConfirmCount, Utc(s.LastConfirmedAt),
            Rating(s), s.RatingCount, s.WinReportCount, createdBy, Utc(s.CreatedAt),
            Mine: s.UserId == userId, CanEdit: s.UserId == userId || isAdmin, confirmedRecently, reported, mine,
            reviews.Select(x => ToReview(x.r, x.Username, userId)).ToArray(),
            wins.Select(x => ToWin(x.w, x.Username, userId)).ToArray());
    }

    // ───────────────────────── Tạo / sửa / xoá ─────────────────────────

    /// <summary>Tạo điểm đã qua <see cref="Validate"/>. Ném <see cref="ShopException"/> khi vượt hạn mức / ảnh sai.</summary>
    public async Task<DetailDto> CreateAsync(ShopInput input, int userId, CancellationToken ct)
    {
        var since = Now.AddDays(-1);
        if (await db.Shops.CountAsync(x => x.UserId == userId && x.CreatedAt > since, ct) >= opt.DailyCreateLimit)
            throw new ShopException(429,
                $"Mỗi ngày bạn thêm được tối đa {opt.DailyCreateLimit} điểm — mai thêm tiếp nhé.",
                $"You can add up to {opt.DailyCreateLimit} shops per day — try again tomorrow.");

        var now = Now;
        var shop = new ShopLocation { UserId = userId, CreatedAt = now, UpdatedAt = now };
        Apply(shop, input);
        if (input.ImageId is { } imageId) shop.ImageKey = (await ClaimImageAsync(imageId, userId, ct)).Key;
        db.Shops.Add(shop);
        await db.SaveChangesAsync(ct);
        await AddRevisionAsync(shop, userId, ShopRevisionAction.Created, ct);
        return await DetailAsync(shop, userId, false, ct);
    }

    public async Task<DetailDto> UpdateAsync(Guid publicId, ShopInput input, int userId, bool isAdmin, CancellationToken ct)
    {
        var shop = await EditableAsync(publicId, userId, isAdmin, ct);
        Apply(shop, input);
        if (input.ImageId is { } imageId)
        {
            var img = await ClaimImageAsync(imageId, userId, ct);
            await ReleaseImageAsync(shop.ImageKey, ct);
            shop.ImageKey = img.Key;
        }
        else if (input.RemoveImage)
        {
            await ReleaseImageAsync(shop.ImageKey, ct);
            shop.ImageKey = null;
        }
        shop.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        await AddRevisionAsync(shop, userId, ShopRevisionAction.Updated, ct);
        return await DetailAsync(shop, userId, isAdmin, ct);
    }

    /// <summary>Người tạo hoặc admin. Ảnh của điểm + ảnh vé trúng thành mồ côi để worker xoá trên bucket.</summary>
    public async Task DeleteAsync(Guid publicId, int userId, bool isAdmin, CancellationToken ct)
    {
        var shop = await EditableAsync(publicId, userId, isAdmin, ct);
        var keys = await db.ShopWinReports.Where(w => w.ShopId == shop.Id && w.ImageKey != null)
            .Select(w => w.ImageKey!).ToListAsync(ct);
        if (shop.ImageKey != null) keys.Add(shop.ImageKey);
        foreach (var k in keys) await ReleaseImageAsync(k, ct);
        // InMemory (test) không cascade — xoá tay các bảng con cho chắc, SQLite thì cascade cũng làm việc này.
        db.ShopConfirms.RemoveRange(db.ShopConfirms.Where(x => x.ShopId == shop.Id));
        db.ShopReviews.RemoveRange(db.ShopReviews.Where(x => x.ShopId == shop.Id));
        db.ShopWinReports.RemoveRange(db.ShopWinReports.Where(x => x.ShopId == shop.Id));
        db.ShopReports.RemoveRange(db.ShopReports.Where(x => x.ShopId == shop.Id));
        db.ShopRevisions.RemoveRange(db.ShopRevisions.Where(x => x.ShopId == shop.Id));
        db.Notifications.RemoveRange(db.Notifications.Where(x => x.ShopId == shop.Id));
        db.Shops.Remove(shop);
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(ShopLocation shop, ShopInput s)
    {
        shop.Name = s.Name!.Trim();
        shop.Type = s.Type;
        shop.Lat = Math.Round(s.Lat, 6);
        shop.Lng = Math.Round(s.Lng, 6);
        shop.Address = s.Address?.Trim() ?? "";
        shop.Phone = s.Type != ShopType.Street && s.PhoneConsent && !string.IsNullOrWhiteSpace(s.Phone)
            ? NormalizePhone(s.Phone) : null;
        shop.OpensAtMin = s.OpensAtMin;
        shop.ClosesAtMin = s.ClosesAtMin;
        shop.Note = string.IsNullOrWhiteSpace(s.Note) ? null : s.Note.Trim();
    }

    private async Task<ShopLocation> EditableAsync(Guid publicId, int userId, bool isAdmin, CancellationToken ct)
    {
        var shop = await db.Shops.FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw ShopException.NotFound();
        if (shop.UserId != userId && !isAdmin) throw ShopException.Forbidden();
        return shop;
    }

    private async Task<ShopLocation> VisibleAsync(Guid publicId, CancellationToken ct) =>
        await db.Shops.FirstOrDefaultAsync(x => x.PublicId == publicId && x.Status == ShopStatus.Visible, ct)
        ?? throw ShopException.NotFound();

    // ───────────────────────── Tương tác ─────────────────────────

    /// <summary>"Vẫn còn bán ở đây". Bấm lại trong <see cref="ConfirmCooldown"/> thì không tính thêm.</summary>
    public async Task<(int ConfirmCount, DateTime? LastConfirmedAt)> ConfirmAsync(Guid publicId, int userId, CancellationToken ct)
    {
        var shop = await VisibleAsync(publicId, ct);
        var now = Now;
        var c = await db.ShopConfirms.FirstOrDefaultAsync(x => x.ShopId == shop.Id && x.UserId == userId, ct);
        if (c == null)
        {
            db.ShopConfirms.Add(new ShopConfirm { ShopId = shop.Id, UserId = userId, ConfirmedAt = now });
            shop.ConfirmCount++;
            shop.LastConfirmedAt = now;
        }
        else if (now - c.ConfirmedAt >= ConfirmCooldown)
        {
            c.ConfirmedAt = now;
            shop.LastConfirmedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return (shop.ConfirmCount, Utc(shop.LastConfirmedAt));
    }

    /// <summary>Thêm hoặc sửa đánh giá của mình. IsNew = lần đầu (để báo người tạo điểm).</summary>
    public async Task<(ReviewDto Review, int Id, bool IsNew, double? Rating, int RatingCount)> UpsertReviewAsync(
        Guid publicId, ReviewInput input, int userId, CancellationToken ct)
    {
        if (input.Stars is < 1 or > 5)
            throw new ShopException(400, "Chọn từ 1 đến 5 sao.", "Pick 1 to 5 stars.");
        var content = string.IsNullOrWhiteSpace(input.Content) ? null : input.Content.Trim();
        if (content?.Length > ReviewMax)
            throw new ShopException(400, $"Bình luận tối đa {ReviewMax} ký tự.", $"Reviews are at most {ReviewMax} characters.");

        var shop = await VisibleAsync(publicId, ct);
        var now = Now;
        var r = await db.ShopReviews.FirstOrDefaultAsync(x => x.ShopId == shop.Id && x.UserId == userId, ct);
        var isNew = r == null;
        if (r == null)
        {
            db.ShopReviews.Add(r = new ShopReview { ShopId = shop.Id, UserId = userId, CreatedAt = now });
            shop.RatingCount++;
        }
        else shop.RatingSum -= r.Stars;
        r.Stars = input.Stars;
        r.Content = content;
        r.UpdatedAt = now;
        shop.RatingSum += input.Stars;
        await db.SaveChangesAsync(ct);

        var username = await db.Users.Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync(ct) ?? "";
        return (ToReview(r, username, userId), r.Id, isNew, Rating(shop), shop.RatingCount);
    }

    /// <summary>Người viết hoặc admin.</summary>
    public async Task<(double? Rating, int RatingCount)> DeleteReviewAsync(int reviewId, int userId, bool isAdmin, CancellationToken ct)
    {
        var r = await db.ShopReviews.FirstOrDefaultAsync(x => x.Id == reviewId, ct)
                ?? throw new ShopException(404, "Không tìm thấy đánh giá.", "Review not found.");
        if (r.UserId != userId && !isAdmin) throw ShopException.Forbidden();
        var shop = await db.Shops.FirstAsync(x => x.Id == r.ShopId, ct);
        shop.RatingSum -= r.Stars;
        shop.RatingCount--;
        db.Notifications.RemoveRange(db.Notifications.Where(n => n.ShopReviewId == r.Id));
        db.ShopReviews.Remove(r);
        await db.SaveChangesAsync(ct);
        return (Rating(shop), shop.RatingCount);
    }

    /// <summary>Báo "điểm này từng bán vé trúng" — ngày phải có kỳ quay của đài đó, trong 1 năm gần đây.</summary>
    public async Task<(WinDto Win, int Id)> AddWinAsync(Guid publicId, WinInput input, int userId, CancellationToken ct)
    {
        var province = input.ProvinceCode?.Trim() ?? "";
        var tier = input.PrizeTier?.Trim() ?? "";
        var isVietlott = province == Vietlott;
        var tiers = isVietlott ? VietlottTiers : XsktTiers;
        if (!tiers.Contains(tier))
            throw new ShopException(400, "Hạng giải không hợp lệ.", "Invalid prize tier.");

        var nowVn = DrawSchedule.NowVn(clock);
        var today = DateOnly.FromDateTime(nowVn);
        if (input.DrawDate > today || input.DrawDate < today.AddDays(-WinMaxAgeDays))
            throw new ShopException(400, "Ngày quay phải trong vòng 1 năm trở lại.", "The draw date must be within the past year.");
        if (!isVietlott && (!DrawSchedule.MayDrawOn(input.DrawDate, province) || !DrawSchedule.HasDrawn(input.DrawDate, province, nowVn)))
            throw new ShopException(400, "Đài này không quay vào ngày đã chọn.", "That station has no draw on this date.");

        var shop = await VisibleAsync(publicId, ct);
        if (await db.ShopWinReports.AnyAsync(w => w.ShopId == shop.Id && w.UserId == userId && w.DrawDate == input.DrawDate
                                                  && w.ProvinceCode == province && w.PrizeTier == tier, ct))
            throw new ShopException(409, "Bạn đã báo vé trúng này rồi.", "You already reported this win.");

        var w = new ShopWinReport
        {
            ShopId = shop.Id, UserId = userId, DrawDate = input.DrawDate, ProvinceCode = province, PrizeTier = tier,
            CreatedAt = Now,
        };
        if (input.ImageId is { } imageId) w.ImageKey = (await ClaimImageAsync(imageId, userId, ct)).Key;
        db.ShopWinReports.Add(w);
        shop.WinReportCount++;
        await db.SaveChangesAsync(ct);
        var username = await db.Users.Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync(ct) ?? "";
        return (ToWin(w, username, userId), w.Id);
    }

    /// <summary>Người báo hoặc admin.</summary>
    public async Task<int> DeleteWinAsync(int winId, int userId, bool isAdmin, CancellationToken ct)
    {
        var w = await db.ShopWinReports.FirstOrDefaultAsync(x => x.Id == winId, ct)
                ?? throw new ShopException(404, "Không tìm thấy báo cáo vé trúng.", "Win report not found.");
        if (w.UserId != userId && !isAdmin) throw ShopException.Forbidden();
        var shop = await db.Shops.FirstAsync(x => x.Id == w.ShopId, ct);
        shop.WinReportCount--;
        await ReleaseImageAsync(w.ImageKey, ct);
        db.Notifications.RemoveRange(db.Notifications.Where(n => n.ShopWinReportId == w.Id));
        db.ShopWinReports.Remove(w);
        await db.SaveChangesAsync(ct);
        return shop.WinReportCount;
    }

    /// <summary>
    /// Báo cáo sai/spam — mỗi người 1 lần (đã được admin xử lý thì báo lại được). Trả true nếu lần này làm
    /// điểm bị ẩn (đủ <see cref="ShopOptions.AutoHideReports"/> người khác nhau).
    /// </summary>
    public async Task<bool> ReportAsync(Guid publicId, ReportInput input, int userId, CancellationToken ct)
    {
        if (!Enum.IsDefined(input.Reason))
            throw new ShopException(400, "Lý do không hợp lệ.", "Invalid reason.");
        var note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        if (note?.Length > ReportNoteMax)
            throw new ShopException(400, $"Ghi chú tối đa {ReportNoteMax} ký tự.", $"Notes are at most {ReportNoteMax} characters.");

        var shop = await VisibleAsync(publicId, ct);
        if (shop.UserId == userId)
            throw new ShopException(400, "Đây là điểm bạn ghim — sửa hoặc xoá nó thay vì báo cáo.",
                                         "You pinned this shop — edit or delete it instead.");
        var r = await db.ShopReports.FirstOrDefaultAsync(x => x.ShopId == shop.Id && x.UserId == userId, ct);
        if (r is { Resolved: false })
            throw new ShopException(409, "Bạn đã báo cáo điểm này rồi.", "You already reported this shop.");
        if (r == null) db.ShopReports.Add(r = new ShopReport { ShopId = shop.Id, UserId = userId });
        r.Reason = input.Reason;
        r.Note = note;
        r.CreatedAt = Now;
        r.Resolved = false;
        await db.SaveChangesAsync(ct);

        shop.ReportCount = await db.ShopReports.CountAsync(x => x.ShopId == shop.Id && !x.Resolved, ct);
        var hidden = shop.ReportCount >= opt.AutoHideReports;
        if (hidden) shop.Status = ShopStatus.Hidden;
        await db.SaveChangesAsync(ct);
        return hidden;
    }

    // ───────────────────────── Admin ─────────────────────────

    /// <summary>Điểm đang bị báo cáo hoặc đang ẩn — nhiều báo cáo nhất trước.</summary>
    public async Task<ReportedDto[]> ListReportedAsync(CancellationToken ct)
    {
        var shops = await (from s in db.Shops.AsNoTracking()
                           where s.ReportCount > 0 || s.Status == ShopStatus.Hidden
                           join u in db.Users on s.UserId equals u.Id
                           orderby s.ReportCount descending, s.UpdatedAt descending
                           select new { s, u.Username }).Take(200).ToListAsync(ct);
        var ids = shops.Select(x => x.s.Id).ToList();
        var reports = (await (from r in db.ShopReports.AsNoTracking()
                              where ids.Contains(r.ShopId) && !r.Resolved
                              join u in db.Users on r.UserId equals u.Id
                              select new { r, u.Username }).ToListAsync(ct))
            .GroupBy(x => x.r.ShopId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.r.CreatedAt)
                .Select(x => new ReportItemDto(x.Username, x.r.Reason, x.r.Note, Utc(x.r.CreatedAt))).ToArray());
        return shops.Select(x => new ReportedDto(x.s.PublicId, x.s.Name, x.s.Type, x.s.Address, x.s.Lat, x.s.Lng,
            x.s.Status, x.s.ReportCount, x.Username, Utc(x.s.CreatedAt), reports.GetValueOrDefault(x.s.Id, []))).ToArray();
    }

    /// <summary>Admin hiện lại / ẩn điểm — các báo cáo hiện có coi như đã xử lý.</summary>
    public async Task SetStatusAsync(Guid publicId, ShopStatus status, CancellationToken ct)
    {
        var shop = await db.Shops.FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw ShopException.NotFound();
        foreach (var r in await db.ShopReports.Where(x => x.ShopId == shop.Id && !x.Resolved).ToListAsync(ct)) r.Resolved = true;
        shop.Status = status;
        shop.ReportCount = 0;
        shop.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Thông tin điểm tại 1 lần lưu — ImageUrl thay cho key để admin mở xem được.</summary>
    public record SnapshotDto(string Name, ShopType Type, double Lat, double Lng, string Address, string? Phone,
                              int? OpensAtMin, int? ClosesAtMin, string? Note, string? ImageUrl);

    public record RevisionDto(int Id, string Username, ShopRevisionAction Action, DateTime CreatedAt, SnapshotDto Snapshot);

    public const int HistoryTake = 50;

    private static readonly System.Text.Json.JsonSerializerOptions SnapshotJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    private async Task AddRevisionAsync(ShopLocation s, int userId, ShopRevisionAction action, CancellationToken ct)
    {
        var snap = new SnapshotDto(s.Name, s.Type, s.Lat, s.Lng, s.Address, s.Phone, s.OpensAtMin, s.ClosesAtMin, s.Note,
                                   s.ImageKey is { } k ? ImageUrl(k) : null);
        db.ShopRevisions.Add(new ShopRevision
        {
            ShopId = s.Id, UserId = userId, Action = action, CreatedAt = Now,
            SnapshotJson = System.Text.Json.JsonSerializer.Serialize(snap, SnapshotJson),
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Lịch sử lưu của 1 điểm (admin), mới nhất trước. null = không có điểm.</summary>
    public async Task<RevisionDto[]?> HistoryAsync(Guid publicId, CancellationToken ct)
    {
        var shopId = await db.Shops.Where(x => x.PublicId == publicId).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (shopId == null) return null;
        var rows = await (from r in db.ShopRevisions.AsNoTracking()
                          where r.ShopId == shopId
                          join u in db.Users on r.UserId equals u.Id
                          orderby r.Id descending
                          select new { r, u.Username }).Take(HistoryTake).ToListAsync(ct);
        return rows.Select(x => new RevisionDto(x.r.Id, x.Username, x.r.Action, Utc(x.r.CreatedAt),
            System.Text.Json.JsonSerializer.Deserialize<SnapshotDto>(x.r.SnapshotJson, SnapshotJson)!)).ToArray();
    }

    // ───────────────────────── Ảnh ─────────────────────────

    public async Task<ImageDto> AddImageAsync(string key, int userId, int sizeBytes, CancellationToken ct)
    {
        var img = new ShopImage { Key = key, UserId = userId, SizeBytes = sizeBytes, CreatedAt = Now };
        db.ShopImages.Add(img);
        await db.SaveChangesAsync(ct);
        return new ImageDto(img.Id, ImageUrl(key));
    }

    public Task<int> PendingImageCountAsync(int userId, CancellationToken ct)
    {
        var since = Now - PendingImageTtl;
        return db.ShopImages.CountAsync(x => x.UserId == userId && !x.Used && x.CreatedAt > since, ct);
    }

    /// <summary>Chỉ ảnh đang được dùng mới xem được — ảnh chờ / mồ côi trả 404.</summary>
    public Task<bool> IsUsedImageAsync(string key, CancellationToken ct) =>
        db.ShopImages.AnyAsync(x => x.Key == key && x.Used, ct);

    public Task<List<ShopImage>> OrphanImagesAsync(DateTime before, int take, CancellationToken ct) =>
        db.ShopImages.Where(x => !x.Used && x.CreatedAt < before).OrderBy(x => x.Id).Take(take).ToListAsync(ct);

    public async Task RemoveImageRowsAsync(IEnumerable<ShopImage> images, CancellationToken ct)
    {
        db.ShopImages.RemoveRange(images);
        await db.SaveChangesAsync(ct);
    }

    private async Task<ShopImage> ClaimImageAsync(int imageId, int userId, CancellationToken ct)
    {
        var since = Now - PendingImageTtl;
        var img = await db.ShopImages.FirstOrDefaultAsync(x => x.Id == imageId && x.UserId == userId && !x.Used && x.CreatedAt > since, ct)
                  ?? throw new ShopException(400, "Ảnh không còn hợp lệ — chọn lại ảnh nhé.", "The image expired — please pick it again.");
        img.Used = true;
        return img;
    }

    /// <summary>Thôi dùng ảnh: thành mồ côi (CreatedAt cũ) → worker xoá trên bucket ở lượt kế tiếp.</summary>
    private async Task ReleaseImageAsync(string? key, CancellationToken ct)
    {
        if (key == null) return;
        var img = await db.ShopImages.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (img == null) return;
        img.Used = false;
        img.CreatedAt = DateTime.MinValue;
    }

    // ───────────────────────── Tiện ích ─────────────────────────

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static double? Rating(ShopLocation s) =>
        s.RatingCount > 0 ? Math.Round((double)s.RatingSum / s.RatingCount, 1) : null;

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? d) => d is { } v ? Utc(v) : null;

    private static MarkerDto ToMarker(ShopLocation x, double? distance = null) => new(
        x.PublicId, x.Lat, x.Lng, x.Type, x.Name, x.Address, Rating(x), x.RatingCount, x.WinReportCount,
        x.OpensAtMin, x.ClosesAtMin, Utc(x.LastConfirmedAt), distance);

    private static ReviewDto ToReview(ShopReview r, string username, int? userId) =>
        new(r.Id, username, r.Stars, r.Content, Utc(r.UpdatedAt), r.UserId == userId);

    private static WinDto ToWin(ShopWinReport w, string username, int? userId) =>
        new(w.Id, username, w.DrawDate, w.ProvinceCode, w.PrizeTier, w.ImageKey is { } k ? ImageUrl(k) : null,
            Utc(w.CreatedAt), w.UserId == userId);
}
