using FluentAssertions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LotteryChecker.Tests;

// TimeProvider giả: luôn trả về 1 mốc thời gian cố định để test giờ xổ.
internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

// Phân biệt 3 trạng thái: chưa tới giờ xổ / chưa có kết quả / đã dò thật.
public class CheckStatusTests
{
    private static AppDbContext NewDb()
    {
        var opt = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(opt);
    }

    // Mốc giờ VN (UTC+7) — không phụ thuộc múi giờ của máy chạy test.
    private static TimeProvider AtVn(DateOnly date, int hour, int minute) =>
        new FixedTimeProvider(new DateTimeOffset(
            date.Year, date.Month, date.Day, hour, minute, 0, TimeSpan.FromHours(7)));

    [Fact(DisplayName = "6. Ve hom nay nhung moi 10:00 (dai MN xo 16:15) -> NotDrawnYet")]
    public async Task Today_BeforeDrawTime_NotDrawnYet()
    {
        var db = NewDb();
        var date = new DateOnly(2026, 8, 23);
        var matcher = new LotteryMatcher(db, AtVn(date, 10, 0));

        var r = await matcher.Match("123456", date, "TPHCM", CancellationToken.None);

        r.Status.Should().Be(CheckStatus.NotDrawnYet);
        r.IsWinner.Should().BeFalse();
        r.DrawsAt.Should().Be(new DateTime(2026, 8, 23, 16, 15, 0));
    }

    [Fact(DisplayName = "7. Ve ngay mai -> NotDrawnYet")]
    public async Task FutureDate_NotDrawnYet()
    {
        var db = NewDb();
        var today = new DateOnly(2026, 8, 23);
        var matcher = new LotteryMatcher(db, AtVn(today, 20, 0));

        var r = await matcher.Match("123456", today.AddDays(1), "TPHCM", CancellationToken.None);

        r.Status.Should().Be(CheckStatus.NotDrawnYet);
    }

    [Fact(DisplayName = "8. 16:30 - MN da xo nhung dai Mien Trung (17:15) thi chua")]
    public async Task MienTrung_DrawsLater()
    {
        var db = NewDb();
        var date = new DateOnly(2026, 8, 23);
        var matcher = new LotteryMatcher(db, AtVn(date, 16, 30));

        var mn = await matcher.Match("123456", date, "TPHCM", CancellationToken.None);
        var mt = await matcher.Match("123456", date, "Hue", CancellationToken.None);

        mn.Status.Should().Be(CheckStatus.NoData);        // da xo, chi la chua co data
        mt.Status.Should().Be(CheckStatus.NotDrawnYet);   // 17:15 moi xo
    }

    [Fact(DisplayName = "9. Da xo nhung DB trong -> NoData, KHONG bao khong trung")]
    public async Task Drawn_NoRows_NoData()
    {
        var db = NewDb();
        var date = new DateOnly(2026, 8, 22);
        var matcher = new LotteryMatcher(db, AtVn(new DateOnly(2026, 8, 23), 10, 0));

        var r = await matcher.Match("123456", date, "TPHCM", CancellationToken.None);

        r.Status.Should().Be(CheckStatus.NoData);
        r.IsWinner.Should().BeFalse();
    }

    [Fact(DisplayName = "10. Da xo + co data -> Checked, do binh thuong")]
    public async Task Drawn_WithRows_Checked()
    {
        var db = NewDb();
        var date = new DateOnly(2026, 8, 22);
        db.LotteryResults.Add(new LotteryResult
            { DrawDate = date, Province = "TPHCM", PrizeTier = "DB", Number = "123456" });
        await db.SaveChangesAsync();

        var matcher = new LotteryMatcher(db, AtVn(new DateOnly(2026, 8, 23), 10, 0));
        var r = await matcher.Match("123456", date, "TPHCM", CancellationToken.None);

        r.Status.Should().Be(CheckStatus.Checked);
        r.IsWinner.Should().BeTrue();
        r.TotalPrize.Should().Be(2_000_000_000m);
    }
}
