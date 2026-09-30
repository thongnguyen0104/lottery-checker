namespace LotteryChecker.Api.Models;

/// <summary>
/// Cờ bật/tắt một tính năng (admin đổi ở trang Quản trị). Chưa có dòng = đang tắt — tính năng mới
/// luôn ẩn cho tới khi admin bật. Danh sách cờ hợp lệ nằm ở <see cref="Services.FeatureFlags.All"/>.
/// </summary>
public class FeatureFlag
{
    public string Key { get; set; } = "";
    public bool Enabled { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>Username admin đổi lần cuối.</summary>
    public string? UpdatedBy { get; set; }
}
