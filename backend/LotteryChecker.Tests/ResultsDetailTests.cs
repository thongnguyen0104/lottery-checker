using FluentAssertions;
using LotteryChecker.Api.Controllers;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LotteryChecker.Tests;

// GET /api/results/{date}/{province}: bảng kết quả chi tiết 1 đài cho màn "Kết quả xổ số".
public class ResultsDetailTests
{
    private static readonly DateOnly Date = new(2026, 9, 26);

    private static AppDbContext NewDb()
    {
        var opt = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(opt);
    }

    private static LotteryResult Row(string province, string tier, string number) => new()
    {
        DrawDate = Date, Region = "MN", Province = province, PrizeTier = tier, Number = number
    };

    [Fact(DisplayName = "Giai xep DB, 1..8; so trong cung giai giu thu tu insert; khong lan dai khac")]
    public async Task Detail_OrdersTiers_KeepsNumberOrder()
    {
        var db = NewDb();
        // Insert lộn xộn như trang nguồn (G.8 trước, ĐB sau) + 1 đài khác cùng ngày.
        db.LotteryResults.AddRange(
            Row("TPHCM", "8", "56"),
            Row("TPHCM", "4", "11111"),
            Row("LongAn", "DB", "999999"),
            Row("TPHCM", "4", "22222"),
            Row("TPHCM", "DB", "123456"),
            Row("TPHCM", "4", "00333"));
        await db.SaveChangesAsync();

        var res = await new ResultsController(db).Detail(Date, "TPHCM", CancellationToken.None);

        var dto = res.Value!;
        dto.DrawDate.Should().Be("2026-09-26");
        dto.Region.Should().Be("MN");
        dto.Prizes.Select(p => p.Tier).Should().Equal("DB", "4", "8");
        dto.Prizes[0].Numbers.Should().Equal("123456");
        dto.Prizes[1].Numbers.Should().Equal("11111", "22222", "00333");
    }

    [Fact(DisplayName = "Chua co ket qua dai/ngay do -> 404 kem thong bao")]
    public async Task Detail_NoRows_NotFound()
    {
        var db = NewDb();
        db.LotteryResults.Add(Row("TPHCM", "DB", "123456"));
        await db.SaveChangesAsync();

        var res = await new ResultsController(db).Detail(Date.AddDays(-1), "TPHCM", CancellationToken.None);

        res.Result.Should().BeOfType<NotFoundObjectResult>();
    }
}
