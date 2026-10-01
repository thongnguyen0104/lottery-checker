using System.Text.Json.Serialization;

namespace LotteryChecker.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShopType
{
    Agency,      // đại lý / cửa hàng vé số cố định
    Street,      // người bán dạo hay ngồi 1 chỗ quen — không lưu SĐT (thông tin cá nhân)
    Vietlott,    // điểm bán Vietlott
    Redemption,  // đại lý đổi thưởng
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShopStatus
{
    Visible,
    Hidden,      // bị báo cáo đủ ngưỡng hoặc admin ẩn — chỉ admin thấy
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShopReportReason
{
    NotExist,
    WrongLocation,
    Duplicate,
    Spam,
    Other,
}

/// <summary>
/// Điểm bán vé số do người dùng ghim lên bản đồ (tên "TicketShop" đã dùng cho cửa hàng vé cào).
/// Các số đếm lưu sẵn để truy vấn marker theo khung nhìn khỏi phải join/đếm.
/// </summary>
public class ShopLocation
{
    public int Id { get; set; }
    /// <summary>Id công khai cho link chia sẻ /ban-do/{guid}.</summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();
    /// <summary>Người tạo — sửa/xoá được.</summary>
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public ShopType Type { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public string Address { get; set; } = "";
    /// <summary>Chỉ với loại khác <see cref="ShopType.Street"/>, người nhập đã tick đồng ý công khai.</summary>
    public string? Phone { get; set; }
    /// <summary>Giờ mở/đóng cửa (phút trong ngày, giờ VN); null = không rõ. Đóng &lt; mở = mở qua nửa đêm.</summary>
    public int? OpensAtMin { get; set; }
    public int? ClosesAtMin { get; set; }
    public string? Note { get; set; }
    /// <summary>Key ảnh trên bucket (shops/...), null = không có ảnh.</summary>
    public string? ImageKey { get; set; }
    public ShopStatus Status { get; set; }
    public int ConfirmCount { get; set; }
    public DateTime? LastConfirmedAt { get; set; }
    public int RatingSum { get; set; }
    public int RatingCount { get; set; }
    public int WinReportCount { get; set; }
    /// <summary>Số người (khác nhau) báo cáo chưa được admin xử lý.</summary>
    public int ReportCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>"Vẫn còn bán ở đây" — mỗi người 1 dòng, xác nhận lại thì cập nhật thời gian.</summary>
public class ShopConfirm
{
    public int ShopId { get; set; }
    public int UserId { get; set; }
    public DateTime ConfirmedAt { get; set; }
}

/// <summary>Đánh giá sao + bình luận — mỗi người 1 đánh giá / điểm, sửa được.</summary>
public class ShopReview
{
    public int Id { get; set; }
    public int ShopId { get; set; }
    public int UserId { get; set; }
    public int Stars { get; set; }
    public string? Content { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Người dùng báo "điểm này từng bán vé trúng" — không kiểm chứng được, FE gắn nhãn rõ.</summary>
public class ShopWinReport
{
    public int Id { get; set; }
    public int ShopId { get; set; }
    public int UserId { get; set; }
    public DateOnly DrawDate { get; set; }
    /// <summary>Code đài (vd. "TPHCM", "MB") hoặc "Vietlott".</summary>
    public string ProvinceCode { get; set; } = "";
    /// <summary>"DB", "G1".."G8", "Jackpot", "Other".</summary>
    public string PrizeTier { get; set; } = "";
    public string? ImageKey { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Báo cáo sai/spam — mỗi người 1 lần / điểm. Đủ ngưỡng người khác nhau thì điểm tự ẩn.</summary>
public class ShopReport
{
    public int ShopId { get; set; }
    public int UserId { get; set; }
    public ShopReportReason Reason { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Admin đã xem (khôi phục / giữ ẩn) — không tính vào ngưỡng nữa.</summary>
    public bool Resolved { get; set; }
}

/// <summary>
/// Ảnh điểm bán / ảnh vé trúng — cùng cơ chế với BlogImage: upload trước (Used = false), gắn khi lưu
/// điểm / báo vé trúng; ảnh không dùng quá hạn hoặc bị thay thì BlogImageCleanupWorker dọn.
/// </summary>
public class ShopImage
{
    public int Id { get; set; }
    /// <summary>Key trên bucket: shops/{yyyy}/{MM}/{guid}.webp</summary>
    public string Key { get; set; } = "";
    public int UserId { get; set; }
    public bool Used { get; set; }
    public int SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShopRevisionAction
{
    Created,
    Updated,
}

/// <summary>
/// Lịch sử tạo / sửa điểm bán — mỗi lần lưu ghi lại toàn bộ thông tin SAU khi lưu (JSON), admin so
/// 2 bản liền nhau để thấy ai đổi gì. Xoá điểm thì lịch sử đi theo.
/// </summary>
public class ShopRevision
{
    public int Id { get; set; }
    public int ShopId { get; set; }
    /// <summary>Người lưu (người tạo hoặc admin).</summary>
    public int UserId { get; set; }
    public ShopRevisionAction Action { get; set; }
    public string SnapshotJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
