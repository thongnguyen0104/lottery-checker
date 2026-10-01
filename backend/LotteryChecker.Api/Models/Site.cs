using System.Text.Json.Serialization;

namespace LotteryChecker.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SiteStatus
{
    Active,
    Hidden,   // admin ẩn / tự ẩn khi bị báo cáo nhiều — /s/{slug} trả 404
}

/// <summary>
/// Website con của 1 tài khoản (mỗi tài khoản tối đa 1), xem ở /s/{slug}. Cấu hình (theme, màu, khối…) là JSON
/// <see cref="Services.SiteService.SiteConfig"/>: chủ site sửa DraftJson, bấm Publish thì chép sang PublishedJson.
/// </summary>
public class Site
{
    public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public int OwnerUserId { get; set; }
    /// <summary>[a-z0-9-], 3–40 ký tự, duy nhất.</summary>
    public string Slug { get; set; } = "";
    public SiteStatus Status { get; set; }
    /// <summary>Điểm bán trên bản đồ gắn với site (tuỳ chọn) — khối Bản đồ + nút "Xem website" trên bản đồ.</summary>
    public int? ShopId { get; set; }
    public string DraftJson { get; set; } = "{}";
    /// <summary>null = chưa publish lần nào (người khác chưa xem được).</summary>
    public string? PublishedJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    /// <summary>Số báo cáo chưa xử lý — lưu sẵn cho trang quản trị.</summary>
    public int ReportCount { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SiteProductKind
{
    Traditional,  // vé số kiến thiết
    Vietlott,
    Scratch,      // vé cào / vé bóc
    Other,
}

/// <summary>Sản phẩm trưng bày trên site. Không bán online: khách chỉ gửi yêu cầu giữ vé, trả tiền tại quầy.</summary>
public class SiteProduct
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>VND, null = "liên hệ".</summary>
    public long? Price { get; set; }
    public SiteProductKind Kind { get; set; }
    public string? ImageKey { get; set; }
    /// <summary>null = không giới hạn; 0 = hết (không nhận giữ thêm).</summary>
    public int? Stock { get; set; }
    public bool IsVisible { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SitePostStatus
{
    Draft,
    Published,
}

/// <summary>Bài viết riêng của site. ContentHtml từ TipTap, đã qua <see cref="Services.SiteHtmlSanitizer"/>.</summary>
public class SitePost
{
    public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public int SiteId { get; set; }
    public string Title { get; set; } = "";
    public string ContentHtml { get; set; } = "";
    public string? CoverKey { get; set; }
    public SitePostStatus Status { get; set; }
    /// <summary>Bài Blog chung đã đăng chéo (tạo 1 lần khi publish nếu chủ site chọn).</summary>
    public int? BlogPostId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReservationStatus
{
    Pending,
    Confirmed,
    Completed,
    Cancelled,
}

/// <summary>Yêu cầu giữ vé của khách — chủ site gọi lại xác nhận, khách tới quầy lấy vé và trả tiền.</summary>
public class SiteReservation
{
    public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public int SiteId { get; set; }
    public int? ProductId { get; set; }
    public string CustomerName { get; set; } = "";
    public string Phone { get; set; } = "";
    public int Quantity { get; set; }
    public string? Note { get; set; }
    public ReservationStatus Status { get; set; }
    /// <summary>Khách đang đăng nhập (nếu có).</summary>
    public int? UserId { get; set; }
    /// <summary>"ip:..." / "u:{id}" — giới hạn số yêu cầu đang chờ của 1 người.</summary>
    public string RequesterKey { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Ảnh của site (logo, bìa, sản phẩm, trong bài viết) — key sites/{yyyy}/{MM}/{guid}.webp trên bucket chung.
/// Used tính lại mỗi lần lưu (ảnh còn được nhắc tới ở đâu đó trong site); không dùng quá 24h thì worker xoá.
/// </summary>
public class SiteImage
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public int UserId { get; set; }
    public bool Used { get; set; }
    public int SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SiteReportReason
{
    Scam,           // lừa đảo / thu tiền online
    Inappropriate,  // nội dung không phù hợp
    Impersonation,  // mạo danh
    Other,
}

public class SiteReport
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public int UserId { get; set; }
    public SiteReportReason Reason { get; set; }
    public string? Note { get; set; }
    public bool Resolved { get; set; }
    public DateTime CreatedAt { get; set; }
}
