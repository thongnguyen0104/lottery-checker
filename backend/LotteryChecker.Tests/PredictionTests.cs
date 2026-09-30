using FluentAssertions;
using LotteryChecker.Api.Services;
using Row = LotteryChecker.Api.Services.PredictionService.Row;

namespace LotteryChecker.Tests;

// Thống kê lô 2 số cuối cho màn Dự đoán.
public class PredictionTests
{
    private static readonly DateOnly D1 = new(2026, 9, 7), D2 = new(2026, 9, 14), D3 = new(2026, 9, 21);

    [Fact(DisplayName = "Xac suat = so ky co so do / tong so ky; gan = so ky lien chua ve")]
    public void Build_ComputesProbabilityAndGap()
    {
        var history = new List<Row>
        {
            new(D1, "8", "12"), new(D1, "7", "412"),   // 12 về 2 lần trong CÙNG kỳ → tính 1 kỳ
            new(D2, "8", "12"),
            new(D3, "8", "34"),
        };

        var p = PredictionService.Build("TPHCM", history, null);

        p.Draws.Should().Be(3);
        p.From.Should().Be("2026-09-07");
        p.To.Should().Be("2026-09-21");
        var s12 = p.All.Single(s => s.Number == "12");
        s12.Hits.Should().Be(3);
        s12.Draws.Should().Be(2);
        s12.Probability.Should().BeApproximately(2 / 3.0, 0.001);
        s12.Gap.Should().Be(1);                         // kỳ gần nhất (D3) chưa về
        p.All.Single(s => s.Number == "34").Gap.Should().Be(0);
        p.All.Single(s => s.Number == "99").Gap.Should().Be(3);  // chưa về lần nào = cả 3 kỳ
        p.Top[0].Number.Should().Be("12");
        p.Top.Should().HaveCount(PredictionService.TopCount);
    }

    [Fact(DisplayName = "Goi y DB: chu so ve nhieu nhat o tung vi tri, hoa thi lay ky gan nhat")]
    public void Build_SpecialGuess()
    {
        var history = new List<Row>
        {
            new(D1, "DB", "111111"),
            new(D2, "DB", "123456"),
            new(D3, "DB", "923450"),
        };

        PredictionService.Build("TPHCM", history, null).Special.Should().Be("123450");
    }

    [Fact(DisplayName = "Chua co du lieu: khong goi y so nao")]
    public void Build_Empty()
    {
        var p = PredictionService.Build("TPHCM", [], null);
        p.Draws.Should().Be(0);
        p.Top.Should().BeEmpty();
        p.Special.Should().BeNull();
        p.All.Should().HaveCount(100);
    }
}
