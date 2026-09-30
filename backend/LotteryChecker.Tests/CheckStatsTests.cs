using FluentAssertions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LotteryChecker.Tests;

// Thống kê vé đã dò toàn hệ thống.
public class CheckStatsTests
{
    private static readonly DateOnly Date = new(2026, 9, 26);

    private static CheckStats NewStats() => new(
        new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
        TimeProvider.System, NullLogger<CheckStats>.Instance);

    private static ScanResult Result(string number, decimal prize = 0, CheckStatus status = CheckStatus.Checked) => new()
    {
        ExtractedNumber = number, DrawDate = Date, Province = "TPHCM", Status = status,
        IsWinner = prize > 0, TotalPrize = prize,
    };

    [Fact(DisplayName = "Dem ve, ve trung, tong tien; dò lai cung ve khong dem trung")]
    public async Task Record_CountsOncePerTicket()
    {
        var stats = NewStats();
        await stats.RecordAsync(Result("123456", 2_000_000_000m), default);
        await stats.RecordAsync(Result("123456", 2_000_000_000m), default);   // dò lại
        await stats.RecordAsync(Result("111111"), default);
        await stats.RecordAsync(Result("222222", 100_000m), default);

        (await stats.GetAsync(default)).Should().Be(new CheckStats.Summary(3, 2, 2_000_100_000));
    }

    [Theory(DisplayName = "Ve chua xo / chua co ket qua / het han khong tinh")]
    [InlineData(CheckStatus.NotDrawnYet)]
    [InlineData(CheckStatus.NoData)]
    [InlineData(CheckStatus.Expired)]
    public async Task Record_IgnoresUnchecked(CheckStatus status)
    {
        var stats = NewStats();
        await stats.RecordAsync(Result("123456", status: status), default);
        (await stats.GetAsync(default)).Tickets.Should().Be(0);
    }
}
