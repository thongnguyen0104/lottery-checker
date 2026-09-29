namespace LotteryChecker.Api.Models;

/// <summary>Số lượt khách (chưa đăng nhập) đã dùng, theo IP — xem <c>GuestQuotaAttribute</c>.</summary>
public class GuestUsage
{
    public string Ip { get; set; } = "";
    public int Scans { get; set; }
    public int Checks { get; set; }
    public DateTime FirstAt { get; set; }
}
