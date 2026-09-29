using System.Text.RegularExpressions;

namespace LotteryChecker.Api.Services;

/// <summary>Luật username/mật khẩu — FE kiểm tra y hệt (frontend/src/utils/accountRules.ts) để báo lỗi ngay khi gõ.</summary>
public static partial class AccountRules
{
    public const int UsernameMin = 10, UsernameMax = 20, PasswordMin = 8, PasswordMax = 64;

    [GeneratedRegex("^[a-z0-9._]+$")]
    private static partial Regex UsernameChars();

    public static string Normalize(string? username) => (username ?? "").Trim().ToLowerInvariant();

    /// <summary>Lỗi của username (đã Normalize), null = hợp lệ.</summary>
    public static string? UsernameError(string u, bool en = false)
    {
        if (u.Length < UsernameMin || u.Length > UsernameMax)
            return en ? $"Username must be {UsernameMin}–{UsernameMax} characters." : $"Tên đăng nhập phải từ {UsernameMin} đến {UsernameMax} ký tự.";
        if (!UsernameChars().IsMatch(u))
            return en ? "Username may only contain unaccented letters, digits, dots or underscores." : "Tên đăng nhập chỉ gồm chữ không dấu, số, dấu chấm hoặc gạch dưới.";
        return null;
    }

    /// <summary>Mật khẩu chặt: 8–64 ký tự, có chữ hoa, chữ thường, số, ký tự đặc biệt, không khoảng trắng.</summary>
    public static string? PasswordError(string? p, bool en = false)
    {
        p ??= "";
        if (p.Length < PasswordMin || p.Length > PasswordMax)
            return en ? $"Password must be {PasswordMin}–{PasswordMax} characters." : $"Mật khẩu phải từ {PasswordMin} đến {PasswordMax} ký tự.";
        if (p.Any(char.IsWhiteSpace)) return en ? "Password must not contain spaces." : "Mật khẩu không được chứa khoảng trắng.";
        if (!p.Any(char.IsUpper)) return en ? "Password needs at least 1 uppercase letter." : "Mật khẩu cần ít nhất 1 chữ hoa.";
        if (!p.Any(char.IsLower)) return en ? "Password needs at least 1 lowercase letter." : "Mật khẩu cần ít nhất 1 chữ thường.";
        if (!p.Any(char.IsDigit)) return en ? "Password needs at least 1 digit." : "Mật khẩu cần ít nhất 1 chữ số.";
        if (p.All(char.IsLetterOrDigit)) return en ? "Password needs at least 1 special character (e.g. !@#$%)." : "Mật khẩu cần ít nhất 1 ký tự đặc biệt (vd !@#$%).";
        return null;
    }
}
