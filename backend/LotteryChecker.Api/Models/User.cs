namespace LotteryChecker.Api.Models;

/// <summary>Tài khoản đăng nhập — chỉ username + mật khẩu (lưu hash, không lưu mật khẩu gốc).</summary>
public class User
{
    public int Id { get; set; }
    /// <summary>Lưu chữ thường để tra trùng không phân biệt hoa/thường.</summary>
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    /// <summary>
    /// Số dư ví (VNĐ ảo — không nạp/rút tiền thật): admin cộng tay, mua vé cào trừ, trúng thì cộng.
    /// Là concurrency token: 2 request cùng trừ tiền thì request sau lỗi thay vì âm số dư.
    /// Mọi thay đổi phải qua <see cref="Services.Wallet"/> để có dòng trong sổ giao dịch.
    /// </summary>
    public long Balance { get; set; }
    /// <summary>Vào được trang quản trị (quản lý user, cộng tiền). Kiểm tra lại DB mỗi request — xem AdminOnlyAttribute.</summary>
    public bool IsAdmin { get; set; }
    /// <summary>Phải đổi mật khẩu trước khi dùng quyền admin — bật cho tài khoản tạo sẵn (mật khẩu mặc định) và khi admin đặt lại mật khẩu.</summary>
    public bool MustChangePassword { get; set; }
}
