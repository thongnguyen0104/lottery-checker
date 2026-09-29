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
    public static string? UsernameError(string u)
    {
        if (u.Length < UsernameMin || u.Length > UsernameMax)
            return $"Tên đăng nhập phải từ {UsernameMin} đến {UsernameMax} ký tự.";
        if (!UsernameChars().IsMatch(u))
            return "Tên đăng nhập chỉ gồm chữ không dấu, số, dấu chấm hoặc gạch dưới.";
        return null;
    }

    /// <summary>Mật khẩu chặt: 8–64 ký tự, có chữ hoa, chữ thường, số, ký tự đặc biệt, không khoảng trắng.</summary>
    public static string? PasswordError(string? p)
    {
        p ??= "";
        if (p.Length < PasswordMin || p.Length > PasswordMax)
            return $"Mật khẩu phải từ {PasswordMin} đến {PasswordMax} ký tự.";
        if (p.Any(char.IsWhiteSpace)) return "Mật khẩu không được chứa khoảng trắng.";
        if (!p.Any(char.IsUpper)) return "Mật khẩu cần ít nhất 1 chữ hoa.";
        if (!p.Any(char.IsLower)) return "Mật khẩu cần ít nhất 1 chữ thường.";
        if (!p.Any(char.IsDigit)) return "Mật khẩu cần ít nhất 1 chữ số.";
        if (p.All(char.IsLetterOrDigit)) return "Mật khẩu cần ít nhất 1 ký tự đặc biệt (vd !@#$%).";
        return null;
    }
}
