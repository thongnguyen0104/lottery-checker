using System.Text.Json;
using System.Text.RegularExpressions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Services;

/// <summary>Cấu hình mục "Sites" trong appsettings.</summary>
public class SiteOptions
{
    /// <summary>Đủ chừng này người KHÁC NHAU báo cáo (chưa xử lý) thì site tự ẩn chờ admin.</summary>
    public int AutoHideReports { get; set; } = 3;
    /// <summary>1 người (IP / tài khoản) giữ tối đa chừng này yêu cầu đang chờ trên 1 site.</summary>
    public int MaxPendingReservations { get; set; } = 3;
}

/// <summary>Lỗi nghiệp vụ của website con — controller trả về <see cref="Status"/> + câu theo ngôn ngữ.</summary>
public class SiteException(int status, string vi, string en) : Exception(en)
{
    public int Status { get; } = status;
    public string Vi { get; } = vi;
    public string En { get; } = en;

    public static SiteException NotFound() => new(404, "Không tìm thấy website.", "Site not found.");
    public static SiteException NoSite() => new(404, "Bạn chưa tạo website.", "You haven't created a site yet.");
    public static SiteException Invalid(string vi, string en) => new(400, vi, en);
}

/// <summary>
/// Website con của người dùng / đại lý: cấu hình nháp → publish, sản phẩm, bài viết, yêu cầu giữ vé, báo cáo.
/// Chủ site chỉ thao tác trên site của chính mình (tìm theo OwnerUserId) nên không có kiểm tra quyền rời rạc.
/// </summary>
public partial class SiteService(AppDbContext db, TimeProvider clock, SiteOptions opt, SiteHtmlSanitizer sanitizer)
{
    public const int SlugMin = 3, SlugMax = 40;
    public const int NameMax = 60, TaglineMax = 120, AddressMax = 200, HoursMax = 80, AboutMax = 20_000;
    public const int ProductNameMax = 80, ProductDescMax = 500, MaxProducts = 50;
    public const int PostTitleMax = 120, PostContentMax = 50_000, MaxPosts = 200, PostsOnHome = 6;
    public const int CustomerNameMax = 40, ReservationNoteMax = 300, MaxQuantity = 100, ReportNoteMax = 300;
    public const int MaxPendingImagesPerUser = 20;
    public static readonly TimeSpan PendingImageTtl = TimeSpan.FromHours(24);
    public const string ImageUrlPrefix = "/api/sites/images/";

    public static readonly IReadOnlyList<string> Themes = ["classic", "modern", "lucky", "minimal"];
    public static readonly IReadOnlyList<string> BlockTypes = ["hero", "about", "products", "reserve", "posts", "results", "map", "contact"];

    /// <summary>Slug trùng tên trang của app / dễ gây nhầm là trang chính thức.</summary>
    private static readonly HashSet<string> ReservedSlugs =
    [
        "admin", "api", "app", "blog", "www", "help", "support", "login", "register", "official", "chinh-thuc",
        "quan-tri", "ban-do", "ket-qua", "du-doan", "tai-khoan", "so-may-man", "vietlott", "xo-so", "xoso",
    ];

    // ───────────────────────── Cấu hình ─────────────────────────

    public record BlockConfig(string Type, bool Enabled);

    /// <summary>Lưu thành JSON ở Site.DraftJson / PublishedJson. Thứ tự Blocks = thứ tự hiện trên trang.</summary>
    public record SiteConfig(string Name, string? Tagline, string Theme, string PrimaryColor, string? LogoUrl, string? CoverUrl,
                             string? Phone, string? Zalo, string? Facebook, string? Address, string? OpeningHours,
                             string? AboutHtml, BlockConfig[] Blocks);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static SiteConfig DefaultConfig(string name) => new(
        name, null, Themes[0], "#d32f2f", null, null, null, null, null, null, null, null,
        BlockTypes.Select(t => new BlockConfig(t, t is not ("results" or "map"))).ToArray());

    private static SiteConfig Parse(string json) => JsonSerializer.Deserialize<SiteConfig>(json, Json)!;
    private static string Serialize(SiteConfig c) => JsonSerializer.Serialize(c, Json);

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex(@"^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorPattern();

    [GeneratedRegex(@"sites/\d{4}/\d{2}/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.webp")]
    public static partial Regex ImageKeyPattern();

    public static string ImageUrl(string key) => ImageUrlPrefix + key;

    public static string? ValidateSlug(string? slug, bool en)
    {
        slug ??= "";
        if (slug.Length < SlugMin || slug.Length > SlugMax || !SlugPattern().IsMatch(slug))
            return en ? $"Address must be {SlugMin}–{SlugMax} lowercase letters, digits or dashes."
                      : $"Địa chỉ gồm {SlugMin}–{SlugMax} chữ thường không dấu, số hoặc gạch ngang.";
        if (ReservedSlugs.Contains(slug))
            return en ? "This address is reserved — pick another." : "Địa chỉ này đã được giữ — chọn tên khác nhé.";
        return null;
    }

    /// <summary>Chuẩn hoá + kiểm tra cấu hình nháp; ném <see cref="SiteException"/> khi sai.</summary>
    public SiteConfig Normalize(SiteConfig c)
    {
        string? Opt(string? s, int max, string vi, string en)
        {
            s = s?.Trim();
            if (string.IsNullOrEmpty(s)) return null;
            if (s.Length > max) throw SiteException.Invalid($"{vi} tối đa {max} ký tự.", $"{en} is at most {max} characters.");
            return s;
        }
        string? Img(string? url)
        {
            url = url?.Trim();
            if (string.IsNullOrEmpty(url)) return null;
            if (!url.StartsWith(ImageUrlPrefix, StringComparison.Ordinal) || !ImageKeyPattern().IsMatch(url[ImageUrlPrefix.Length..]))
                throw SiteException.Invalid("Ảnh không hợp lệ — tải ảnh lên lại nhé.", "Invalid image — please upload it again.");
            return url;
        }

        var name = c.Name?.Trim() ?? "";
        if (name.Length is < 2 or > NameMax)
            throw SiteException.Invalid($"Tên website 2–{NameMax} ký tự.", $"Site name must be 2–{NameMax} characters.");
        var theme = Themes.Contains(c.Theme) ? c.Theme : Themes[0];
        if (!ColorPattern().IsMatch(c.PrimaryColor ?? ""))
            throw SiteException.Invalid("Màu chủ đạo không hợp lệ.", "Invalid primary color.");

        var phone = Opt(c.Phone, 20, "Số điện thoại", "Phone");
        if (phone != null) phone = ShopService.NormalizePhone(phone)
            ?? throw SiteException.Invalid("Số điện thoại không hợp lệ.", "Invalid phone number.");
        var zalo = Opt(c.Zalo, 20, "Zalo", "Zalo");
        if (zalo != null) zalo = ShopService.NormalizePhone(zalo)
            ?? throw SiteException.Invalid("Số Zalo không hợp lệ.", "Invalid Zalo number.");
        var fb = Opt(c.Facebook, 200, "Link Facebook", "Facebook link");
        if (fb != null && !(Uri.TryCreate(fb, UriKind.Absolute, out var u) && u.Scheme == "https"
                            && (u.Host.EndsWith("facebook.com") || u.Host == "fb.com" || u.Host == "m.me")))
            throw SiteException.Invalid("Link Facebook phải là https://facebook.com/…", "Facebook link must be https://facebook.com/…");

        var about = sanitizer.Sanitize(c.AboutHtml);
        if (about.Length > AboutMax)
            throw SiteException.Invalid("Phần giới thiệu dài quá.", "The About section is too long.");

        // Giữ thứ tự user gửi, bỏ khối lạ / trùng, thêm khối còn thiếu (tắt) ở cuối.
        var blocks = (c.Blocks ?? []).Where(b => BlockTypes.Contains(b.Type)).DistinctBy(b => b.Type).ToList();
        blocks.AddRange(BlockTypes.Where(t => blocks.All(b => b.Type != t)).Select(t => new BlockConfig(t, false)));

        return new SiteConfig(name, Opt(c.Tagline, TaglineMax, "Khẩu hiệu", "Tagline"), theme, c.PrimaryColor!.ToLowerInvariant(),
            Img(c.LogoUrl), Img(c.CoverUrl), phone, zalo, fb,
            Opt(c.Address, AddressMax, "Địa chỉ", "Address"), Opt(c.OpeningHours, HoursMax, "Giờ mở cửa", "Opening hours"),
            about == "" ? null : about, blocks.ToArray());
    }

    // ───────────────────────── Site của tôi ─────────────────────────

    public record ShopRef(Guid Id, string Name, string Address, double Lat, double Lng, int? OpensAtMin, int? ClosesAtMin);

    public record MineDto(Guid Id, string Slug, SiteStatus Status, SiteConfig Draft, SiteConfig? Published, DateTime? PublishedAt,
                          bool HasUnpublishedChanges, ShopRef? Shop, ShopRef[] MyShops);

    public record SettingsInput(string? Slug, SiteConfig Config, Guid? ShopId);

    public Task<bool> IsSlugTakenAsync(string slug, int? exceptOwner, CancellationToken ct) =>
        db.Sites.AnyAsync(x => x.Slug == slug && x.OwnerUserId != exceptOwner, ct);

    public async Task<MineDto?> GetMineAsync(int userId, CancellationToken ct) =>
        await db.Sites.AsNoTracking().FirstOrDefaultAsync(x => x.OwnerUserId == userId, ct) is { } s ? await ToMineAsync(s, ct) : null;

    public async Task<MineDto> CreateAsync(int userId, string slug, CancellationToken ct)
    {
        if (await db.Sites.AnyAsync(x => x.OwnerUserId == userId, ct))
            throw new SiteException(409, "Bạn đã có website rồi.", "You already have a site.");
        if (await IsSlugTakenAsync(slug, null, ct))
            throw new SiteException(409, "Địa chỉ này đã có người dùng.", "This address is taken.");
        var username = await db.Users.Where(x => x.Id == userId).Select(x => x.Username).FirstAsync(ct);
        var s = new Site
        {
            OwnerUserId = userId, Slug = slug, CreatedAt = Now, UpdatedAt = Now,
            DraftJson = Serialize(DefaultConfig(username)),
        };
        db.Sites.Add(s);
        await db.SaveChangesAsync(ct);
        return await ToMineAsync(s, ct);
    }

    /// <summary>Lưu nháp: cấu hình + đổi địa chỉ + gắn điểm bán. Chưa ai thấy cho tới khi Publish.</summary>
    public async Task<MineDto> SaveDraftAsync(int userId, SettingsInput input, CancellationToken ct)
    {
        var s = await MineAsync(userId, ct);
        var config = Normalize(input.Config);
        if (input.Slug is { } slug && slug != s.Slug)
        {
            if (ValidateSlug(slug, false) is { } err) throw SiteException.Invalid(err, ValidateSlug(slug, true)!);
            if (await IsSlugTakenAsync(slug, userId, ct))
                throw new SiteException(409, "Địa chỉ này đã có người dùng.", "This address is taken.");
            s.Slug = slug;
        }
        if (input.ShopId is { } shopPublicId)
        {
            // Chỉ gắn được điểm bán mình ghim — không mượn uy tín điểm của người khác.
            var shop = await db.Shops.FirstOrDefaultAsync(x => x.PublicId == shopPublicId && x.UserId == userId, ct)
                       ?? throw SiteException.Invalid("Chỉ gắn được điểm bán do bạn ghim trên bản đồ.", "You can only link a shop you pinned.");
            s.ShopId = shop.Id;
        }
        else s.ShopId = null;
        s.DraftJson = Serialize(config);
        s.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        await SyncImagesAsync(s, ct);
        return await ToMineAsync(s, ct);
    }

    public async Task<MineDto> PublishAsync(int userId, CancellationToken ct)
    {
        var s = await MineAsync(userId, ct);
        s.PublishedJson = s.DraftJson;
        s.PublishedAt = Now;
        await db.SaveChangesAsync(ct);
        await SyncImagesAsync(s, ct);
        return await ToMineAsync(s, ct);
    }

    private async Task<Site> MineAsync(int userId, CancellationToken ct) =>
        await db.Sites.FirstOrDefaultAsync(x => x.OwnerUserId == userId, ct) ?? throw SiteException.NoSite();

    private async Task<MineDto> ToMineAsync(Site s, CancellationToken ct)
    {
        var myShops = await db.Shops.AsNoTracking().Where(x => x.UserId == s.OwnerUserId && x.Status == ShopStatus.Visible)
            .OrderBy(x => x.Name).ToListAsync(ct);
        var shop = s.ShopId is { } id ? await db.Shops.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) : null;
        return new MineDto(s.PublicId, s.Slug, s.Status, Parse(s.DraftJson), s.PublishedJson is { } p ? Parse(p) : null,
            Utc(s.PublishedAt), s.PublishedJson != s.DraftJson, shop == null ? null : ToShopRef(shop),
            myShops.Select(ToShopRef).ToArray());
    }

    private static ShopRef ToShopRef(ShopLocation x) => new(x.PublicId, x.Name, x.Address, x.Lat, x.Lng, x.OpensAtMin, x.ClosesAtMin);

    // ───────────────────────── Trang công khai ─────────────────────────

    public record ProductDto(int Id, string Name, string? Description, long? Price, SiteProductKind Kind, string? ImageUrl,
                             int? Stock, bool IsVisible, int SortOrder);
    public record PostSummaryDto(Guid Id, string Title, string Excerpt, string? CoverUrl, SitePostStatus Status, DateTime? PublishedAt,
                                 bool CrossPosted);
    public record PostDto(Guid Id, string Title, string ContentHtml, string? CoverUrl, SitePostStatus Status, DateTime? PublishedAt,
                          bool CrossPosted);
    public record PublicDto(string Slug, string Owner, SiteConfig Config, ShopRef? Shop, ProductDto[] Products, PostSummaryDto[] Posts);

    /// <summary>
    /// Trang /s/{slug}. preview = chủ site xem bản nháp (kể cả khi chưa publish / bị ẩn).
    /// null = không có / chưa publish / bị ẩn.
    /// </summary>
    public async Task<PublicDto?> GetPublicAsync(string slug, int? viewerId, bool preview, CancellationToken ct)
    {
        var s = await db.Sites.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug, ct);
        if (s == null) return null;
        var isOwner = viewerId == s.OwnerUserId;
        if (preview && !isOwner) return null;
        var json = preview ? s.DraftJson : s.PublishedJson;
        if (json == null || (!preview && s.Status == SiteStatus.Hidden)) return null;

        var owner = await db.Users.Where(x => x.Id == s.OwnerUserId).Select(x => x.Username).FirstAsync(ct);
        var shop = s.ShopId is { } id
            ? await db.Shops.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Status == ShopStatus.Visible, ct) : null;
        var products = await db.SiteProducts.AsNoTracking().Where(x => x.SiteId == s.Id && x.IsVisible)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(ct);
        var posts = await db.SitePosts.AsNoTracking().Where(x => x.SiteId == s.Id && x.Status == SitePostStatus.Published)
            .OrderByDescending(x => x.PublishedAt).Take(PostsOnHome).ToListAsync(ct);
        return new PublicDto(s.Slug, owner, Parse(json), shop == null ? null : ToShopRef(shop),
            products.Select(ToDto).ToArray(), posts.Select(ToSummary).ToArray());
    }

    public async Task<PostSummaryDto[]?> ListPublicPostsAsync(string slug, CancellationToken ct)
    {
        var s = await VisibleAsync(slug, ct);
        if (s == null) return null;
        var posts = await db.SitePosts.AsNoTracking().Where(x => x.SiteId == s.Id && x.Status == SitePostStatus.Published)
            .OrderByDescending(x => x.PublishedAt).ToListAsync(ct);
        return posts.Select(ToSummary).ToArray();
    }

    public async Task<PostDto?> GetPublicPostAsync(string slug, Guid postId, int? viewerId, CancellationToken ct)
    {
        var s = await db.Sites.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug, ct);
        if (s == null) return null;
        var isOwner = viewerId == s.OwnerUserId;
        if (!isOwner && (s.PublishedJson == null || s.Status == SiteStatus.Hidden)) return null;
        var p = await db.SitePosts.AsNoTracking().FirstOrDefaultAsync(x => x.SiteId == s.Id && x.PublicId == postId, ct);
        if (p == null || (!isOwner && p.Status != SitePostStatus.Published)) return null;
        return ToDto(p);
    }

    private Task<Site?> VisibleAsync(string slug, CancellationToken ct) =>
        db.Sites.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug && x.Status == SiteStatus.Active && x.PublishedJson != null, ct);

    // ───────────────────────── Sản phẩm ─────────────────────────

    public record ProductInput(string? Name, string? Description, long? Price, SiteProductKind Kind, string? ImageUrl,
                               int? Stock, bool IsVisible = true, int SortOrder = 0);

    public async Task<ProductDto[]> ListProductsAsync(int userId, CancellationToken ct)
    {
        var s = await MineAsync(userId, ct);
        var rows = await db.SiteProducts.AsNoTracking().Where(x => x.SiteId == s.Id)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(ct);
        return rows.Select(ToDto).ToArray();
    }

    public async Task<ProductDto> SaveProductAsync(int userId, int? productId, ProductInput input, CancellationToken ct)
    {
        var s = await MineAsync(userId, ct);
        var name = input.Name?.Trim() ?? "";
        if (name.Length is < 2 or > ProductNameMax)
            throw SiteException.Invalid($"Tên sản phẩm 2–{ProductNameMax} ký tự.", $"Product name must be 2–{ProductNameMax} characters.");
        var desc = input.Description?.Trim();
        if (desc?.Length > ProductDescMax)
            throw SiteException.Invalid($"Mô tả tối đa {ProductDescMax} ký tự.", $"Description is at most {ProductDescMax} characters.");
        if (input.Price is < 0 or > 100_000_000_000)
            throw SiteException.Invalid("Giá không hợp lệ.", "Invalid price.");
        if (input.Stock is < 0 or > 1_000_000)
            throw SiteException.Invalid("Số lượng không hợp lệ.", "Invalid stock.");
        string? imageKey = null;
        if (!string.IsNullOrWhiteSpace(input.ImageUrl))
        {
            if (!input.ImageUrl.StartsWith(ImageUrlPrefix, StringComparison.Ordinal) || !ImageKeyPattern().IsMatch(input.ImageUrl))
                throw SiteException.Invalid("Ảnh không hợp lệ — tải ảnh lên lại nhé.", "Invalid image — please upload it again.");
            imageKey = input.ImageUrl[ImageUrlPrefix.Length..];
        }

        SiteProduct p;
        if (productId is { } id)
            p = await db.SiteProducts.FirstOrDefaultAsync(x => x.Id == id && x.SiteId == s.Id, ct)
                ?? throw new SiteException(404, "Không tìm thấy sản phẩm.", "Product not found.");
        else
        {
            if (await db.SiteProducts.CountAsync(x => x.SiteId == s.Id, ct) >= MaxProducts)
                throw SiteException.Invalid($"Tối đa {MaxProducts} sản phẩm.", $"At most {MaxProducts} products.");
            db.SiteProducts.Add(p = new SiteProduct { SiteId = s.Id, CreatedAt = Now });
        }
        p.Name = name;
        p.Description = string.IsNullOrEmpty(desc) ? null : desc;
        p.Price = input.Price;
        p.Kind = input.Kind;
        p.ImageKey = imageKey;
        p.Stock = input.Stock;
        p.IsVisible = input.IsVisible;
        p.SortOrder = input.SortOrder;
        await db.SaveChangesAsync(ct);
        await SyncImagesAsync(s, ct);
        return ToDto(p);
    }

    public async Task DeleteProductAsync(int userId, int productId, CancellationToken ct)
    {
        var s = await MineAsync(userId, ct);
        var p = await db.SiteProducts.FirstOrDefaultAsync(x => x.Id == productId && x.SiteId == s.Id, ct)
                ?? throw new SiteException(404, "Không tìm thấy sản phẩm.", "Product not found.");
        // Giữ lại yêu cầu giữ vé cũ, chỉ bỏ liên kết.
        foreach (var r in await db.SiteReservations.Where(x => x.ProductId == p.Id).ToListAsync(ct)) r.ProductId = null;
        db.SiteProducts.Remove(p);
        await db.SaveChangesAsync(ct);
        await SyncImagesAsync(s, ct);
    }

    // ───────────────────────── Bài viết ─────────────────────────

    public record PostInput(string? Title, string? ContentHtml, string? CoverUrl, bool Publish, bool CrossPostToBlog = false);

    public async Task<PostSummaryDto[]> ListMyPostsAsync(int userId, CancellationToken ct)
    {
        var s = await MineAsync(userId, ct);
        var rows = await db.SitePosts.AsNoTracking().Where(x => x.SiteId == s.Id).OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);
        return rows.Select(ToSummary).ToArray();
    }

    public async Task<PostDto> GetMyPostAsync(int userId, Guid postId, CancellationToken ct)
    {
        var s = await MineAsync(userId, ct);
        return ToDto(await MyPostAsync(s, postId, ct));
    }

    public async Task<PostDto> SavePostAsync(int userId, Guid? postId, PostInput input, CancellationToken ct)
    {
        var s = await MineAsync(userId, ct);
        var title = input.Title?.Trim() ?? "";
        if (title.Length is < 3 or > PostTitleMax)
            throw SiteException.Invalid($"Tiêu đề 3–{PostTitleMax} ký tự.", $"Title must be 3–{PostTitleMax} characters.");
        var html = sanitizer.Sanitize(input.ContentHtml);
        if (SiteHtmlSanitizer.ToText(html).Length == 0 && !html.Contains("<img"))
            throw SiteException.Invalid("Bài viết chưa có nội dung.", "The post is empty.");
        if (html.Length > PostContentMax)
            throw SiteException.Invalid("Bài viết dài quá.", "The post is too long.");
        string? coverKey = null;
        if (!string.IsNullOrWhiteSpace(input.CoverUrl))
        {
            if (!input.CoverUrl.StartsWith(ImageUrlPrefix, StringComparison.Ordinal) || !ImageKeyPattern().IsMatch(input.CoverUrl))
                throw SiteException.Invalid("Ảnh bìa không hợp lệ.", "Invalid cover image.");
            coverKey = input.CoverUrl[ImageUrlPrefix.Length..];
        }

        SitePost p;
        if (postId is { } id) p = await MyPostAsync(s, id, ct);
        else
        {
            if (await db.SitePosts.CountAsync(x => x.SiteId == s.Id, ct) >= MaxPosts)
                throw SiteException.Invalid($"Tối đa {MaxPosts} bài viết.", $"At most {MaxPosts} posts.");
            db.SitePosts.Add(p = new SitePost { SiteId = s.Id, CreatedAt = Now });
        }
        p.Title = title;
        p.ContentHtml = html;
        p.CoverKey = coverKey;
        p.UpdatedAt = Now;
        if (input.Publish && p.Status != SitePostStatus.Published)
        {
            p.Status = SitePostStatus.Published;
            p.PublishedAt = Now;
        }
        else if (!input.Publish) p.Status = SitePostStatus.Draft;
        await db.SaveChangesAsync(ct);

        // Đăng chéo 1 lần: bài Blog chung gồm đoạn đầu + link về site (Blog chỉ có chữ thuần).
        if (input.CrossPostToBlog && p.Status == SitePostStatus.Published && p.BlogPostId == null && s.PublishedJson != null)
        {
            var owner = await db.Users.Where(x => x.Id == userId).Select(x => x.Username).FirstAsync(ct);
            var link = $"/s/{s.Slug}/bai-viet/{p.PublicId}";
            var text = SiteHtmlSanitizer.ToText(html);
            const int max = 4500;
            if (text.Length > max) text = text[..max].TrimEnd() + "…";
            var blog = new BlogPost
            {
                Title = title, Content = $"{text}\n\n👉 {link}", AuthorMode = BlogAuthorMode.Account,
                AuthorName = owner, UserId = userId, CreatedAt = Now,
            };
            db.BlogPosts.Add(blog);
            await db.SaveChangesAsync(ct);
            p.BlogPostId = blog.Id;
            await db.SaveChangesAsync(ct);
        }
        await SyncImagesAsync(s, ct);
        return ToDto(p);
    }

    public async Task DeletePostAsync(int userId, Guid postId, CancellationToken ct)
    {
        var s = await MineAsync(userId, ct);
        db.SitePosts.Remove(await MyPostAsync(s, postId, ct));
        await db.SaveChangesAsync(ct);
        await SyncImagesAsync(s, ct);
    }

    private async Task<SitePost> MyPostAsync(Site s, Guid postId, CancellationToken ct) =>
        await db.SitePosts.FirstOrDefaultAsync(x => x.SiteId == s.Id && x.PublicId == postId, ct)
        ?? throw new SiteException(404, "Không tìm thấy bài viết.", "Post not found.");

    // ───────────────────────── Giữ vé ─────────────────────────

    public record ReservationInput(int? ProductId, string? CustomerName, string? Phone, int Quantity, string? Note);

    public record ReservationDto(Guid Id, int? ProductId, string? ProductName, string CustomerName, string Phone, int Quantity,
                                 string? Note, ReservationStatus Status, DateTime CreatedAt);

    /// <summary>Khách gửi yêu cầu giữ vé. Trả (id, chủ site) để controller báo chuông cho chủ site.</summary>
    public async Task<(Guid Id, int ReservationId, int OwnerId)> ReserveAsync(string slug, ReservationInput input, int? userId,
                                                                             string requesterKey, CancellationToken ct)
    {
        var s = await VisibleAsync(slug, ct) ?? throw SiteException.NotFound();
        var name = input.CustomerName?.Trim() ?? "";
        if (name.Length is < 2 or > CustomerNameMax)
            throw SiteException.Invalid($"Tên 2–{CustomerNameMax} ký tự.", $"Name must be 2–{CustomerNameMax} characters.");
        var phone = ShopService.NormalizePhone(input.Phone ?? "")
                    ?? throw SiteException.Invalid("Số điện thoại không hợp lệ.", "Invalid phone number.");
        if (input.Quantity is < 1 or > MaxQuantity)
            throw SiteException.Invalid($"Số lượng 1–{MaxQuantity}.", $"Quantity must be 1–{MaxQuantity}.");
        var note = input.Note?.Trim();
        if (note?.Length > ReservationNoteMax)
            throw SiteException.Invalid($"Ghi chú tối đa {ReservationNoteMax} ký tự.", $"Notes are at most {ReservationNoteMax} characters.");
        if (input.ProductId is { } pid)
        {
            var p = await db.SiteProducts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pid && x.SiteId == s.Id && x.IsVisible, ct)
                    ?? throw new SiteException(404, "Không tìm thấy sản phẩm.", "Product not found.");
            if (p.Stock is { } stock && stock < input.Quantity)
                throw SiteException.Invalid("Sản phẩm không còn đủ số lượng.", "Not enough stock left.");
        }
        if (userId == s.OwnerUserId)
            throw SiteException.Invalid("Không tự giữ vé trên site của mình được.", "You can't reserve on your own site.");
        var pending = await db.SiteReservations.CountAsync(x => x.SiteId == s.Id && x.RequesterKey == requesterKey
                                                               && x.Status == ReservationStatus.Pending, ct);
        if (pending >= opt.MaxPendingReservations)
            throw new SiteException(429, "Bạn còn yêu cầu đang chờ — đợi chủ cửa hàng liên hệ nhé.",
                                         "You already have pending requests — please wait for the shop to contact you.");

        var r = new SiteReservation
        {
            SiteId = s.Id, ProductId = input.ProductId, CustomerName = name, Phone = phone, Quantity = input.Quantity,
            Note = string.IsNullOrEmpty(note) ? null : note, UserId = userId, RequesterKey = requesterKey,
            CreatedAt = Now, UpdatedAt = Now,
        };
        db.SiteReservations.Add(r);
        await db.SaveChangesAsync(ct);
        return (r.PublicId, r.Id, s.OwnerUserId);
    }

    public async Task<ReservationDto[]> ListReservationsAsync(int userId, ReservationStatus? status, CancellationToken ct)
    {
        var s = await MineAsync(userId, ct);
        var q = db.SiteReservations.AsNoTracking().Where(x => x.SiteId == s.Id);
        if (status is { } st) q = q.Where(x => x.Status == st);
        var rows = await (from r in q
                          join p in db.SiteProducts on r.ProductId equals (int?)p.Id into pj
                          from p in pj.DefaultIfEmpty()
                          orderby r.Id descending
                          select new { r, ProductName = p == null ? null : p.Name }).Take(200).ToListAsync(ct);
        return rows.Select(x => ToDto(x.r, x.ProductName)).ToArray();
    }

    /// <summary>
    /// Đổi trạng thái. Xác nhận thì trừ tồn kho (nếu có quản lý tồn); huỷ một yêu cầu đã xác nhận thì cộng lại.
    /// </summary>
    public async Task<ReservationDto> SetReservationStatusAsync(int userId, Guid id, ReservationStatus status, CancellationToken ct)
    {
        var s = await MineAsync(userId, ct);
        var r = await db.SiteReservations.FirstOrDefaultAsync(x => x.SiteId == s.Id && x.PublicId == id, ct)
                ?? throw new SiteException(404, "Không tìm thấy yêu cầu.", "Request not found.");
        var p = r.ProductId is { } pid ? await db.SiteProducts.FirstOrDefaultAsync(x => x.Id == pid, ct) : null;
        var wasHeld = r.Status is ReservationStatus.Confirmed or ReservationStatus.Completed;
        var isHeld = status is ReservationStatus.Confirmed or ReservationStatus.Completed;
        if (p?.Stock is { } stock && wasHeld != isHeld)
        {
            if (isHeld && stock < r.Quantity)
                throw SiteException.Invalid("Sản phẩm không còn đủ số lượng.", "Not enough stock left.");
            p.Stock = isHeld ? stock - r.Quantity : stock + r.Quantity;
        }
        r.Status = status;
        r.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return ToDto(r, p?.Name);
    }

    public Task<int> PendingReservationCountAsync(int userId, CancellationToken ct) =>
        (from r in db.SiteReservations
         join s in db.Sites on r.SiteId equals s.Id
         where s.OwnerUserId == userId && r.Status == ReservationStatus.Pending
         select r.Id).CountAsync(ct);

    // ───────────────────────── Báo cáo / quản trị ─────────────────────────

    public record ReportInput(SiteReportReason Reason, string? Note);
    public record ReportItemDto(string Username, SiteReportReason Reason, string? Note, DateTime CreatedAt);
    public record AdminSiteDto(Guid Id, string Slug, string Name, string Owner, SiteStatus Status, bool Published, int ReportCount,
                               DateTime CreatedAt, DateTime? PublishedAt, ReportItemDto[] Reports);

    /// <summary>Báo cáo (1 lần / người / site, gửi lại thì cập nhật). Trả true khi site vừa bị tự ẩn.</summary>
    public async Task<bool> ReportAsync(string slug, ReportInput input, int userId, CancellationToken ct)
    {
        var s = await db.Sites.FirstOrDefaultAsync(x => x.Slug == slug && x.Status == SiteStatus.Active && x.PublishedJson != null, ct)
                ?? throw SiteException.NotFound();
        if (s.OwnerUserId == userId) throw SiteException.Invalid("Không tự báo cáo site của mình.", "You can't report your own site.");
        var note = input.Note?.Trim();
        if (note?.Length > ReportNoteMax)
            throw SiteException.Invalid($"Ghi chú tối đa {ReportNoteMax} ký tự.", $"Notes are at most {ReportNoteMax} characters.");
        var r = await db.SiteReports.FirstOrDefaultAsync(x => x.SiteId == s.Id && x.UserId == userId, ct);
        if (r == null) db.SiteReports.Add(r = new SiteReport { SiteId = s.Id, UserId = userId });
        r.Reason = input.Reason;
        r.Note = string.IsNullOrEmpty(note) ? null : note;
        r.Resolved = false;
        r.CreatedAt = Now;
        await db.SaveChangesAsync(ct);
        s.ReportCount = await db.SiteReports.CountAsync(x => x.SiteId == s.Id && !x.Resolved, ct);
        var hidden = s.ReportCount >= opt.AutoHideReports;
        if (hidden) s.Status = SiteStatus.Hidden;
        await db.SaveChangesAsync(ct);
        return hidden;
    }

    public async Task<AdminSiteDto[]> AdminListAsync(CancellationToken ct)
    {
        var rows = await (from s in db.Sites.AsNoTracking()
                          join u in db.Users on s.OwnerUserId equals u.Id
                          orderby s.ReportCount descending, s.Id descending
                          select new { s, u.Username }).Take(500).ToListAsync(ct);
        var ids = rows.Where(x => x.s.ReportCount > 0).Select(x => x.s.Id).ToList();
        var reports = (await (from r in db.SiteReports.AsNoTracking()
                              where ids.Contains(r.SiteId) && !r.Resolved
                              join u in db.Users on r.UserId equals u.Id
                              select new { r, u.Username }).ToListAsync(ct))
            .ToLookup(x => x.r.SiteId);
        return rows.Select(x => new AdminSiteDto(x.s.PublicId, x.s.Slug, Parse(x.s.PublishedJson ?? x.s.DraftJson).Name, x.Username,
            x.s.Status, x.s.PublishedJson != null, x.s.ReportCount, Utc(x.s.CreatedAt), Utc(x.s.PublishedAt),
            reports[x.s.Id].Select(r => new ReportItemDto(r.Username, r.r.Reason, r.r.Note, Utc(r.r.CreatedAt))).ToArray())).ToArray();
    }

    /// <summary>Admin ẩn / hiện lại — các báo cáo hiện có coi như đã xử lý.</summary>
    public async Task SetStatusAsync(Guid publicId, SiteStatus status, CancellationToken ct)
    {
        var s = await db.Sites.FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw SiteException.NotFound();
        foreach (var r in await db.SiteReports.Where(x => x.SiteId == s.Id && !x.Resolved).ToListAsync(ct)) r.Resolved = true;
        s.Status = status;
        s.ReportCount = 0;
        await db.SaveChangesAsync(ct);
    }

    // ───────────────────────── Ảnh ─────────────────────────

    public record ImageDto(int Id, string Url);

    public async Task<ImageDto> AddImageAsync(string key, int userId, int sizeBytes, CancellationToken ct)
    {
        var img = new SiteImage { Key = key, UserId = userId, SizeBytes = sizeBytes, CreatedAt = Now };
        db.SiteImages.Add(img);
        await db.SaveChangesAsync(ct);
        return new ImageDto(img.Id, ImageUrl(key));
    }

    public Task<int> PendingImageCountAsync(int userId, CancellationToken ct)
    {
        var since = Now - PendingImageTtl;
        return db.SiteImages.CountAsync(x => x.UserId == userId && !x.Used && x.CreatedAt > since, ct);
    }

    /// <summary>Ảnh đang dùng ai cũng xem được; ảnh chưa dùng chỉ người upload xem (khung xem trước).</summary>
    public Task<bool> CanViewImageAsync(string key, int? userId, CancellationToken ct) =>
        db.SiteImages.AnyAsync(x => x.Key == key && (x.Used || x.UserId == userId), ct);

    public Task<List<SiteImage>> OrphanImagesAsync(DateTime before, int take, CancellationToken ct) =>
        db.SiteImages.Where(x => !x.Used && x.CreatedAt < before).OrderBy(x => x.Id).Take(take).ToListAsync(ct);

    public async Task RemoveImageRowsAsync(IEnumerable<SiteImage> images, CancellationToken ct)
    {
        db.SiteImages.RemoveRange(images);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Tính lại ảnh nào của chủ site còn được nhắc tới (nháp, bản đã publish, sản phẩm, bài viết). Ảnh thôi dùng thì
    /// đánh dấu mồ côi với CreatedAt = lúc này → còn 24h "ân hạn" (ví dụ vừa gỡ khỏi nháp rồi muốn gắn lại).
    /// </summary>
    private async Task SyncImagesAsync(Site s, CancellationToken ct)
    {
        var texts = new List<string> { s.DraftJson, s.PublishedJson ?? "" };
        texts.AddRange(await db.SiteProducts.Where(x => x.SiteId == s.Id && x.ImageKey != null).Select(x => x.ImageKey!).ToListAsync(ct));
        texts.AddRange(await db.SitePosts.Where(x => x.SiteId == s.Id).Select(x => x.ContentHtml + " " + (x.CoverKey ?? "")).ToListAsync(ct));
        var used = texts.SelectMany(t => ImageKeyPattern().Matches(t).Select(m => m.Value)).ToHashSet();

        foreach (var img in await db.SiteImages.Where(x => x.UserId == s.OwnerUserId).ToListAsync(ct))
        {
            var isUsed = used.Contains(img.Key);
            if (isUsed == img.Used) continue;
            img.Used = isUsed;
            if (!isUsed) img.CreatedAt = Now;
        }
        await db.SaveChangesAsync(ct);
    }

    // ───────────────────────── Tiện ích ─────────────────────────

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? d) => d is { } v ? Utc(v) : null;

    private static ProductDto ToDto(SiteProduct p) => new(p.Id, p.Name, p.Description, p.Price, p.Kind,
        p.ImageKey is { } k ? ImageUrl(k) : null, p.Stock, p.IsVisible, p.SortOrder);

    private static PostSummaryDto ToSummary(SitePost p)
    {
        var text = SiteHtmlSanitizer.ToText(p.ContentHtml);
        return new(p.PublicId, p.Title, text.Length > 200 ? text[..200].TrimEnd() + "…" : text,
            p.CoverKey is { } k ? ImageUrl(k) : null, p.Status, Utc(p.PublishedAt), p.BlogPostId != null);
    }

    private static PostDto ToDto(SitePost p) => new(p.PublicId, p.Title, p.ContentHtml, p.CoverKey is { } k ? ImageUrl(k) : null,
        p.Status, Utc(p.PublishedAt), p.BlogPostId != null);

    private static ReservationDto ToDto(SiteReservation r, string? productName) => new(r.PublicId, r.ProductId, productName,
        r.CustomerName, r.Phone, r.Quantity, r.Note, r.Status, Utc(r.CreatedAt));
}
