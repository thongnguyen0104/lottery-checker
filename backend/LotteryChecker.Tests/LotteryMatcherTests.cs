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

    // Cố định giờ VN = sáng hôm sau ngày xổ 02/06: không cố định thì test phụ thuộc đồng hồ máy
    // và tự hỏng khi vé mẫu quá 30 ngày (Expired).
    private static readonly TimeProvider DayAfterDraw =
        new FixedTimeProvider(new DateTimeOffset(2026, 6, 3, 10, 0, 0, TimeSpan.FromHours(7)));

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

        var r = await new LotteryMatcher(db, DayAfterDraw).Match("123456", date, "TPHCM", CancellationToken.None);

        r.IsWinner.Should().BeTrue();
        r.Winnings.Should().Contain(w => w.TierName == "Giải Đặc Biệt" && w.Amount == 2_000_000_000m);
    }

    [Fact(DisplayName = "2. Phụ ĐB (5 số cuối khớp, sai chữ số đầu) → 50tr, KHÔNG trúng ĐB")]
    public async Task PhuDB_Returns_50Million()
    {
        var db = NewDb();
        var date = new DateOnly(2026, 6, 2);
        await SeedAsync(db, date, "TPHCM", "123456", "99");

        var r = await new LotteryMatcher(db, DayAfterDraw).Match("923456", date, "TPHCM", CancellationToken.None);

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
        var r = await new LotteryMatcher(db, DayAfterDraw).Match("123956", date, "TPHCM", CancellationToken.None);

        r.Winnings.Should().ContainSingle(w => w.TierName == "Giải Khuyến Khích" && w.Amount == 6_000_000m);
    }

    [Fact(DisplayName = "4. Sai 2 vị trí → KHÔNG trúng Khuyến khích (regression)")]
    public async Task TwoDigitsDiff_NotKhuyenKhich()
    {
        var db = NewDb();
        var date = new DateOnly(2026, 6, 2);
        await SeedAsync(db, date, "TPHCM", "123456", "99");

        // 199956 vs 123456 — chữ số đầu khớp, sai 3 vị trí
        var r = await new LotteryMatcher(db, DayAfterDraw).Match("199956", date, "TPHCM", CancellationToken.None);

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

        var r = await new LotteryMatcher(db, DayAfterDraw).Match("123456", date, "TPHCM", CancellationToken.None);

        r.Winnings.Should().HaveCount(2);
        r.TotalPrize.Should().Be(2_000_100_000m);
    }

    [Fact(DisplayName = "6. Mọi vé sai đúng 1 chữ số so với ĐB → đúng 9 giải phụ ĐB + 45 giải khuyến khích")]
    public async Task OneDigitOff_Yields_9_PhuDB_And_45_KhuyenKhich()
    {
        var db = NewDb();
        var date = new DateOnly(2026, 6, 2);
        await SeedAsync(db, date, "TPHCM", "123456", "99");
        var matcher = new LotteryMatcher(db, DayAfterDraw);

        int phuDb = 0, kk = 0;
        for (int pos = 0; pos < 6; pos++)
            for (char d = '0'; d <= '9'; d++)
            {
                if (d == "123456"[pos]) continue;
                var chars = "123456".ToCharArray();
                chars[pos] = d;
                var r = await matcher.Match(new string(chars), date, "TPHCM", CancellationToken.None);

                // Mỗi vé chỉ trúng đúng 1 trong 2 giải phụ, không bao giờ cả hai.
                r.Winnings.Should().ContainSingle();
                var w = r.Winnings[0];
                if (pos == 0)
                {
                    w.TierName.Should().Be("Giải Phụ Đặc Biệt");
                    w.Amount.Should().Be(50_000_000m);
                    phuDb++;
                }
                else
                {
                    w.TierName.Should().Be("Giải Khuyến Khích");
                    w.Amount.Should().Be(6_000_000m);
                    kk++;
                }
            }

        phuDb.Should().Be(9);
        kk.Should().Be(45);
    }

    [Theory(DisplayName = "7. Sai chữ số hàng trăm ngàn + 1 chữ số khác → không trúng giải phụ nào")]
    [InlineData("923459")]
    [InlineData("093456")]
    public async Task FirstDigitPlusAnother_NoSubPrize(string ticket)
    {
        var db = NewDb();
        var date = new DateOnly(2026, 6, 2);
        await SeedAsync(db, date, "TPHCM", "123456", "99");

        var r = await new LotteryMatcher(db, DayAfterDraw).Match(ticket, date, "TPHCM", CancellationToken.None);

        r.IsWinner.Should().BeFalse();
    }

    [Fact(DisplayName = "8. Khuyến khích + Giải Tám cùng lúc → cộng dồn 6tr + 100k")]
    public async Task KhuyenKhich_Plus_Giai8_Stacks()
    {
        var db = NewDb();
        var date = new DateOnly(2026, 6, 2);
        await SeedAsync(db, date, "TPHCM", "123456", "56");

        var r = await new LotteryMatcher(db, DayAfterDraw).Match("123956", date, "TPHCM", CancellationToken.None);

        r.Winnings.Should().HaveCount(2);
        r.TotalPrize.Should().Be(6_100_000m);
    }
}
