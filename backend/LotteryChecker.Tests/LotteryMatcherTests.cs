using FluentAssertions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LotteryChecker.Tests;

public class LotteryMatcherTests
{
    private static AppDbContext NewDb()
    {
        var opt = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(opt);
    }

    private static async Task SeedAsync(AppDbContext db, DateOnly date, string province,
        string dbNumber, string giai8Number)
    {
        db.LotteryResults.Add(new LotteryResult
            { DrawDate = date, Province = province, PrizeTier = "DB", Number = dbNumber });
        db.LotteryResults.Add(new LotteryResult
            { DrawDate = date, Province = province, PrizeTier = "8", Number = giai8Number });
        await db.SaveChangesAsync();
    }

    [Fact(DisplayName = "1. ĐB exact match → đúng 2 tỷ")]
    public async Task DB_Exact_Returns_2Billion()
    {
        var db = NewDb();
        var date = new DateOnly(2026, 6, 2);
        await SeedAsync(db, date, "TPHCM", "123456", "99");

        var r = await new LotteryMatcher(db).Match("123456", date, "TPHCM", CancellationToken.None);

        r.IsWinner.Should().BeTrue();
        r.Winnings.Should().Contain(w => w.TierName == "Giải Đặc Biệt" && w.Amount == 2_000_000_000m);
    }

    [Fact(DisplayName = "2. Phụ ĐB (5 số cuối khớp, sai chữ số đầu) → 50tr, KHÔNG trúng ĐB")]
    public async Task PhuDB_Returns_50Million()
    {
        var db = NewDb();
        var date = new DateOnly(2026, 6, 2);
        await SeedAsync(db, date, "TPHCM", "123456", "99");

        var r = await new LotteryMatcher(db).Match("923456", date, "TPHCM", CancellationToken.None);

        r.Winnings.Should().ContainSingle(w => w.TierName == "Giải Phụ Đặc Biệt" && w.Amount == 50_000_000m);
        r.Winnings.Should().NotContain(w => w.TierName == "Giải Đặc Biệt");
    }

    [Fact(DisplayName = "3. Khuyến khích (sai 1 vị trí trong [1..5]) → 6tr")]
    public async Task KhuyenKhich_Returns_6Million()
    {
        var db = NewDb();
        var date = new DateOnly(2026, 6, 2);
        await SeedAsync(db, date, "TPHCM", "123456", "99");

        // Sai vị trí thứ 3 (4 thành 9): 123956 vs 123456
        var r = await new LotteryMatcher(db).Match("123956", date, "TPHCM", CancellationToken.None);

        r.Winnings.Should().ContainSingle(w => w.TierName == "Giải Khuyến Khích" && w.Amount == 6_000_000m);
    }

    [Fact(DisplayName = "4. Sai 2 vị trí → KHÔNG trúng Khuyến khích (regression)")]
    public async Task TwoDigitsDiff_NotKhuyenKhich()
    {
        var db = NewDb();
        var date = new DateOnly(2026, 6, 2);
        await SeedAsync(db, date, "TPHCM", "123456", "99");

        // 199956 vs 123456 — chữ số đầu khớp, sai 3 vị trí
        var r = await new LotteryMatcher(db).Match("199956", date, "TPHCM", CancellationToken.None);

        r.Winnings.Should().NotContain(w => w.TierName == "Giải Khuyến Khích");
        r.Winnings.Should().NotContain(w => w.TierName == "Giải Phụ Đặc Biệt");
    }

    [Fact(DisplayName = "5. Trúng ĐB + Giải Tám cùng lúc → tổng = 2 tỷ + 100k")]
    public async Task DB_Plus_Giai8_Stacks()
    {
        var db = NewDb();
        var date = new DateOnly(2026, 6, 2);
        // ĐB=123456, Giải 8=56 → vé 123456 trúng cả 2
        await SeedAsync(db, date, "TPHCM", "123456", "56");

        var r = await new LotteryMatcher(db).Match("123456", date, "TPHCM", CancellationToken.None);

        r.Winnings.Should().HaveCount(2);
        r.TotalPrize.Should().Be(2_000_100_000m);
    }
}
