namespace LotteryChecker.Api.Services;

/// <summary>
/// Ngôn ngữ trả lời theo header Accept-Language FE gửi lên ("vi" | "en"). Mặc định tiếng Việt.
/// Chỉ dùng cho lời nhắn hiển thị cho người dùng — log và mã lỗi giữ nguyên.
/// </summary>
public static class Lang
{
    // req null: controller gọi thẳng trong unit test (không có HttpContext) → tiếng Việt.
    public static bool IsEn(HttpRequest? req) =>
        (req?.Headers.AcceptLanguage.ToString() ?? "").TrimStart().StartsWith("en", StringComparison.OrdinalIgnoreCase);

    public static string T(HttpRequest? req, string vi, string en) => IsEn(req) ? en : vi;

    private static readonly System.Globalization.CultureInfo Vi = new("vi-VN"), En = new("en-US");

    /// <summary>Số tiền kiểu người đọc quen: "150.000" (vi) / "150,000" (en) — N0 mặc định theo culture máy chủ.</summary>
    public static string Num(HttpRequest? req, long n) => n.ToString("N0", IsEn(req) ? En : Vi);
}
