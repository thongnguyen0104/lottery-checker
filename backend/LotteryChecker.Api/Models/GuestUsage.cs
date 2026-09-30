namespace LotteryChecker.Api.Models;

/// <summary>
/// Số lượt khách (chưa đăng nhập) đã dùng — xem <c>GuestQuotaAttribute</c>. Key = "v:{visitorId}"
/// (theo máy) hoặc "ip:{địa chỉ}" (chặn trên theo IP).
/// </summary>
public class GuestUsage
{
    public string Key { get; set; } = "";
    public int Scans { get; set; }
    public int Checks { get; set; }
    public DateTime FirstAt { get; set; }
}
