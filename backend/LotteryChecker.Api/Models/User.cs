namespace LotteryChecker.Api.Models;

/// <summary>Tài khoản đăng nhập — chỉ username + mật khẩu (lưu hash, không lưu mật khẩu gốc).</summary>
public class User
{
    public int Id { get; set; }
    /// <summary>Lưu chữ thường để tra trùng không phân biệt hoa/thường.</summary>
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
