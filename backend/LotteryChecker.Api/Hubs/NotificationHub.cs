using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace LotteryChecker.Api.Hubs;

/// <summary>
/// Kênh realtime chỉ chiều máy chủ → trình duyệt: FE nghe sự kiện <see cref="NotificationEvent"/>.
/// Đăng nhập bằng cookie như API thường; SignalR tự gom mọi kết nối của 1 tài khoản theo claim NameIdentifier.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    public const string Path = "/api/hubs/notifications";
    public const string NotificationEvent = "notification";
}
