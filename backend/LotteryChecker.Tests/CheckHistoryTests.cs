using FluentAssertions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LotteryChecker.Tests;

// Lịch sử dò vé của từng tài khoản (trang Tài khoản).
public class CheckHistoryTests
{
    private static readonly DateOnly Date = new(2026, 9, 26);

    private static CheckHistory NewHistory() => new(
        new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
        TimeProvider.System, NullLogger<CheckHistory>.Instance);

    private static ScanResult Result(string number, decimal prize = 0, CheckStatus status = CheckStatus.Checked) => new()
    {
        ExtractedNumber = number, DrawDate = Date, Province = "TPHCM", Status = status,
        IsWinner = prize > 0, TotalPrize = prize,
    };

    [Fact(DisplayName = "Ghi moi luot do ke ca do lai va ve chua xo; moi nhat truoc; chi thay cua minh")]
    public async Task Record_KeepsEveryCheckPerUser()
    {
        var h = NewHistory();
        await h.RecordAsync(1, Result("111111"), default);
        await h.RecordAsync(1, Result("111111"), default);                       // dò lại
        await h.RecordAsync(1, Result("222222", status: CheckStatus.NotDrawnYet), default);
        await h.RecordAsync(2, Result("333333"), default);                       // tài khoản khác

        var page = await h.ListAsync(1, 1, default);
        page.Items.Select(x => x.TicketNumber).Should().Equal("222222", "111111", "111111");
        page.Items[0].Status.Should().Be(CheckStatus.NotDrawnYet);
        page.HasMore.Should().BeFalse();
    }

    [Fact(DisplayName = "Phan trang: HasMore dung, trang sau noi tiep")]
    public async Task List_Pages()
    {
        var h = NewHistory();
        for (var i = 0; i < CheckHistory.PageSize + 3; i++)
            await h.RecordAsync(1, Result(i.ToString("D6")), default);

        var p1 = await h.ListAsync(1, 1, default);
        var p2 = await h.ListAsync(1, 2, default);
        p1.Items.Should().HaveCount(CheckHistory.PageSize);
        p1.HasMore.Should().BeTrue();
        p2.Items.Should().HaveCount(3);
        p2.HasMore.Should().BeFalse();
        p1.Items.Select(x => x.Id).Intersect(p2.Items.Select(x => x.Id)).Should().BeEmpty();
    }

    [Fact(DisplayName = "Tong ket: do lai ve trung khong nhan tien len")]
    public async Task Summary_CountsEachWinningTicketOnce()
    {
        var h = NewHistory();
        await h.RecordAsync(1, Result("123456", 2_000_000m), default);
        await h.RecordAsync(1, Result("123456", 2_000_000m), default);   // dò lại vé trúng
        await h.RecordAsync(1, Result("654321", 100_000m), default);
        await h.RecordAsync(1, Result("111111"), default);
        await h.RecordAsync(2, Result("999999", 5_000_000m), default);

        (await h.GetSummaryAsync(1, default)).Should().Be(new CheckHistory.Summary(4, 2, 2_100_000));
    }
}
