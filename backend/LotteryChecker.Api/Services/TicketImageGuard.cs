using System.Text.RegularExpressions;
using LotteryChecker.Api.Models;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Chặn ảnh không liên quan trước bước dò kết quả: chỉ cho đi tiếp khi ảnh có dấu hiệu là vé số.
/// Đây là guard nghiệp vụ, không thay thế OCR.
/// </summary>
public sealed class TicketImageGuard
{
    private static readonly Regex TicketKeyword = new(
        @"\b(xo\s*so|xổ\s*số|ki[eê]n\s*thi[eế]t|ve\s*so|vé\s*số|giai|giải|dac\s*biet|đặc\s*biệt|xskt)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public bool IsLikelyTicket(TicketInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.TicketNumber)) return true;
        if (info.DrawDate != null && !string.IsNullOrWhiteSpace(info.Province)) return true;
        return !string.IsNullOrWhiteSpace(info.RawText) && TicketKeyword.IsMatch(info.RawText);
    }
}
