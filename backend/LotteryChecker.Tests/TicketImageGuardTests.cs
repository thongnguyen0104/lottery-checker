using FluentAssertions;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Xunit;

namespace LotteryChecker.Tests;

public class TicketImageGuardTests
{
    private readonly TicketImageGuard _guard = new();

    [Fact(DisplayName = "Có số vé thì được coi là vé số")]
    public void Accept_WhenTicketNumberExists()
    {
        var info = new TicketInfo { TicketNumber = "123456", RawText = "abc" };
        _guard.IsLikelyTicket(info).Should().BeTrue();
    }

    [Fact(DisplayName = "Không có số nhưng có từ khoá vé số thì vẫn chấp nhận")]
    public void Accept_WhenLotteryKeywordsDetected()
    {
        var info = new TicketInfo { RawText = "Xổ số kiến thiết miền nam" };
        _guard.IsLikelyTicket(info).Should().BeTrue();
    }

    [Fact(DisplayName = "Ảnh không liên quan bị từ chối")]
    public void Reject_WhenNoTicketSignals()
    {
        var info = new TicketInfo { RawText = "Ảnh bàn làm việc và ly cà phê" };
        _guard.IsLikelyTicket(info).Should().BeFalse();
    }
}
