using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Services;
using LotteryChecker.Api.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Draw = LotteryChecker.Api.Services.VietlottStatsService.Draw;

namespace LotteryChecker.Tests;

// Kết quả + thống kê Vietlott Mega 6/45, Power 6/55. Nguồn cào là minhngoc.com.vn (xem ghi chú đầu
// VietlottScraper.cs) — mỗi URL chỉ 1 ngày, ngày không có kỳ quay thì trang tự "nhảy" về kỳ gần nhất trước đó.
public class VietlottTests
{
    /// <summary>Dựng HTML tối thiểu khớp với các Regex trong VietlottScraper (rút gọn, không phải trang thật).
    /// ky null = mô phỏng kỳ đầu tiên (minhngoc không in "Kỳ vé" cho trang này).</summary>
    private static string Page(DateOnly actualDate, int? ky, int[] numbers, int? bonus)
    {
        var lis = new StringBuilder();
        for (var i = 0; i < numbers.Length; i++)
            lis.Append($"""<li><div class="finnish{i + 1} bool">{numbers[i]:00}</div></li>""");
        if (bonus is { } b)
            lis.Append($"""<li class="number_device"></li><li class="number_special"><div class="finnish7 bool">{b:00}</div></li>""");

        return $"""
            <span id="DT6X45_KY_VE">{(ky is { } k ? $"#{k:00000}" : "")}</span> | Ng&agrave;y quay thưởng {actualDate:dd/MM/yyyy}
            <ul class="result-number">{lis}</ul>
            """;
    }

    [Fact(DisplayName = "Doc trang 6/45: ky, ngay, 6 so")]
    public void ParseDay_Mega()
    {
        var d = VietlottScraper.ParseDay(Page(new DateOnly(2026, 10, 11), 1574, [3, 9, 16, 29, 30, 44], null), VietlottScraper.Mega);

        d!.DrawId.Should().Be(1574);
        d.DrawDate.Should().Be(new DateOnly(2026, 10, 11));
        d.Numbers.Should().Equal(3, 9, 16, 29, 30, 44);
        d.Bonus.Should().BeNull();
    }

    [Fact(DisplayName = "Doc trang 6/55: so thu 7 la so dac biet")]
    public void ParseDay_Power()
    {
        var d = VietlottScraper.ParseDay(Page(new DateOnly(2026, 10, 10), 1409, [12, 29, 42, 48, 54, 55], 18), VietlottScraper.Power);

        d!.DrawId.Should().Be(1409);
        d.Numbers.Should().Equal(12, 29, 42, 48, 54, 55);
        d.Bonus.Should().Be(18);
    }

    [Fact(DisplayName = "Trang khong co so Ky ve (ky dau tien) -> gan DrawId = 1")]
    public void ParseDay_MissingKy_IsFirstDraw() =>
        VietlottScraper.ParseDay(Page(new DateOnly(2016, 7, 20), null, [2, 17, 33, 37, 38, 45], null), VietlottScraper.Mega)!
            .DrawId.Should().Be(1);

    [Theory(DisplayName = "Trang hong / thieu du lieu -> null, khong ghi rac")]
    [InlineData("")]
    [InlineData("<html><body>Không tìm thấy</body></html>")]
    public void ParseDay_Malformed_ReturnsNull(string html) =>
        VietlottScraper.ParseDay(html, VietlottScraper.Mega).Should().BeNull();

    [Fact(DisplayName = "So ngoai khoang (vd 99 > 45) -> null")]
    public void ParseDay_NumberOutOfRange_ReturnsNull() =>
        VietlottScraper.ParseDay(Page(new DateOnly(2026, 10, 11), 1574, [3, 9, 16, 29, 30, 99], null), VietlottScraper.Mega)
            .Should().BeNull();

    /// <summary>Trả HTML theo đúng ngày (dd-MM-yyyy) trong URL — ngày không khai báo thì fail test ngay
    /// (bắt được chỗ SyncAsync lỡ cào thêm ngày không mong đợi).</summary>
    private sealed class FakeMinhNgoc(Dictionary<DateOnly, string> byRequestedDate) : HttpMessageHandler
    {
        public List<DateOnly> Requested { get; } = [];
        private static readonly Regex DateRx = new(@"(\d{2})-(\d{2})-(\d{4})\.html$");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var m = DateRx.Match(request.RequestUri!.AbsolutePath);
            var date = new DateOnly(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value));
            Requested.Add(date);
            if (!byRequestedDate.TryGetValue(date, out var html))
                throw new KeyNotFoundException($"Test không khai báo HTML cho ngày {date} — SyncAsync cào ngày không mong đợi.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
        }
    }

    private static (VietlottScraper Scraper, AppDbContext Db, FakeMinhNgoc Handler) Env(Dictionary<DateOnly, string> pages)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var handler = new FakeMinhNgoc(pages);
        var scraper = new VietlottScraper(db, new HttpClient(handler), NullLogger<VietlottScraper>.Instance);
        VietlottScraper.PageDelay = TimeSpan.Zero;   // test không cần nghỉ giữa các trang
        return (scraper, db, handler);
    }

    [Fact(DisplayName = "Lui theo ngay: nhay qua ngay trong nho ngay tra ve trong trang, dung khi gap ky da biet")]
    public async Task SyncAsync_JumpsGapsAndStopsOnKnown()
    {
        var newest = new DateOnly(2026, 10, 11);   // kỳ #1574, có quay đúng hôm nay
        var older = new DateOnly(2026, 10, 9);     // kỳ #1573 — ngày 10/10 không quay nên sẽ nhảy về đây
        var pages = new Dictionary<DateOnly, string>
        {
            [newest] = Page(newest, 1574, [3, 9, 16, 29, 30, 44], null),
            [newest.AddDays(-1)] = Page(older, 1573, [5, 7, 12, 23, 32, 40], null),   // 10/10 → nhảy về 09/10
            [older.AddDays(-1)] = Page(newest, 1574, [3, 9, 16, 29, 30, 44], null),   // trước kỳ đầu → nhảy về MỚI NHẤT (đã biết)
        };
        var (scraper, db, handler) = Env(pages);

        var (saved, done) = await scraper.SyncAsync(VietlottScraper.Mega, newest, maxPages: 10, CancellationToken.None);

        saved.Should().Be(2);
        done.Should().BeTrue();
        handler.Requested.Should().Equal(newest, newest.AddDays(-1), older.AddDays(-1));   // không dò từng ngày trống
        db.VietlottDraws.Select(d => d.DrawId).Should().BeEquivalentTo([1574, 1573]);
    }

    [Fact(DisplayName = "Cao hang ngay: ky hom nay da co trong DB thi dung ngay, chi ton 1 request")]
    public async Task SyncAsync_AlreadyKnown_StopsImmediately()
    {
        var today = new DateOnly(2026, 10, 11);
        var pages = new Dictionary<DateOnly, string> { [today] = Page(today, 1574, [3, 9, 16, 29, 30, 44], null) };
        var (scraper, db, handler) = Env(pages);
        db.VietlottDraws.Add(new Api.Models.VietlottDraw { Game = VietlottScraper.Mega, DrawId = 1574, DrawDate = today, Numbers = "03,09,16,29,30,44" });
        db.SaveChanges();

        var (saved, done) = await scraper.SyncAsync(VietlottScraper.Mega, today, maxPages: 30, CancellationToken.None);

        saved.Should().Be(0);
        done.Should().BeTrue();
        handler.Requested.Should().HaveCount(1);
    }

    [Fact(DisplayName = "Backfill: dung dung o maxPages khi chua bat kip, lan sau cao tiep tu cho do")]
    public async Task SyncAsync_StopsAtQuota_NotDone()
    {
        var start = new DateOnly(2026, 10, 11);
        var pages = Enumerable.Range(0, 10)
            .ToDictionary(i => start.AddDays(-i), i => Page(start.AddDays(-i), 1574 - i, [1, 2, 3, 4, 5, 6], null));
        var (scraper, db, _) = Env(pages);

        var (saved1, done1) = await scraper.SyncAsync(VietlottScraper.Mega, start, maxPages: 3, CancellationToken.None);
        saved1.Should().Be(3);
        done1.Should().BeFalse();
        db.VietlottDraws.Should().HaveCount(3);

        // Lượt sau (giống VietlottBackfillWorker): tiếp tục từ kỳ cũ nhất đang có − 1 ngày.
        var oldest = db.VietlottDraws.Min(d => d.DrawDate);
        var (saved2, _) = await scraper.SyncAsync(VietlottScraper.Mega, oldest.AddDays(-1), maxPages: 3, CancellationToken.None);
        saved2.Should().Be(3);
        db.VietlottDraws.Should().HaveCount(6);
    }

    [Theory(DisplayName = "Ky quay ke tiep theo lich 18:00")]
    [InlineData("645", "2026-10-12 10:00", "2026-10-14")]   // T2 → T4
    [InlineData("645", "2026-10-14 17:59", "2026-10-14")]   // T4 trước giờ quay
    [InlineData("645", "2026-10-14 18:00", "2026-10-16")]   // T4 đã quay → T6
    [InlineData("655", "2026-10-12 10:00", "2026-10-13")]   // T2 → T3
    [InlineData("655", "2026-10-17 19:00", "2026-10-20")]   // T7 đã quay → T3
    public void NextDraw(string game, string now, string expected) =>
        VietlottScraper.NextDrawDate(game, DateTime.Parse(now)).ToString("yyyy-MM-dd").Should().Be(expected);

    [Fact(DisplayName = "Worker: thu tu 18:30, 15 phut/lan toi 21:00")]
    public void WorkerSchedule()
    {
        var day = new DateTime(2026, 10, 14);
        VietlottFetchWorker.NextRun(day.AddHours(9), false).Should().Be(day.AddHours(18.5));
        VietlottFetchWorker.NextRun(day.AddHours(18.5), false).Should().Be(day.AddHours(18.75));
        VietlottFetchWorker.NextRun(day.AddHours(18.5), true).Should().Be(day.AddDays(1).AddHours(18.5));
        VietlottFetchWorker.NextRun(day.AddHours(20.9), false).Should().Be(day.AddDays(1).AddHours(18.5));
    }

    private static List<Draw> MakeDraws(params int[][] sets) =>
        sets.Select((s, i) => new Draw(sets.Length - i, new DateOnly(2026, 1, 1).AddDays(-i * 2), s, null)).ToList();

    [Fact(DisplayName = "Thong ke: tan suat, gan, cap so, chan le")]
    public void Stats()
    {
        var draws = MakeDraws(                       // mới → cũ
            [1, 2, 3, 4, 5, 6],
            [1, 2, 10, 20, 30, 40],
            [7, 8, 9, 10, 11, 12]);

        var s = VietlottStatsService.Build("645", draws, 0, new DateOnly(2026, 1, 3));

        s.Draws.Should().Be(3);
        s.Baseline.Should().BeApproximately(6 / 45.0, 0.0001);
        s.JackpotOneIn.Should().Be(8_145_060);
        s.All.Should().HaveCount(45);
        var n1 = s.All.Single(x => x.Number == 1);
        n1.Hits.Should().Be(2);
        n1.Gap.Should().Be(0);
        s.All.Single(x => x.Number == 10).Gap.Should().Be(1);
        s.All.Single(x => x.Number == 45).Gap.Should().Be(3);
        s.Hot.Take(2).Select(x => x.Number).Should().BeEquivalentTo([1, 2]);
        s.Pairs[0].Should().Be(new VietlottStatsService.PairStat(1, 2, 2));
        s.OddCounts.Sum().Should().Be(3);
        s.OddCounts[3].Should().Be(2);               // {1..6} và {7..12} đều 3 lẻ
        s.Suggestions.Should().HaveCount(3).And.OnlyContain(x => x.Numbers.Length == 6 && x.Numbers.Distinct().Count() == 6);
        s.Backtest.Should().BeNull();                // < 30 kỳ: chưa kiểm chứng
    }

    [Fact(DisplayName = "Bo can bang: 3 le/3 chan, 3 nho/3 lon")]
    public void BalancedShape()
    {
        var rng = new Random(7);
        var draws = Enumerable.Range(0, 200)
            .Select(_ => Enumerable.Range(1, 55).OrderBy(_ => rng.Next()).Take(6).Order().ToArray()).ToArray();

        var s = VietlottStatsService.Build("655", MakeDraws(draws), 0, new DateOnly(2026, 1, 3));

        var b = s.Suggestions.Single(x => x.Kind == "balanced").Numbers;
        b.Count(n => n % 2 == 1).Should().Be(3);
        b.Count(n => n <= 27).Should().Be(3);
        s.Backtest!.Rows.Should().HaveCount(VietlottStatsService.BacktestDraws);
        s.Backtest.RandomExpectation.Should().BeApproximately(36 / 55.0, 0.001);
        s.Odds.Single(o => o.Match == 6).OneIn.Should().Be(28_989_675);
    }

    [Fact(DisplayName = "To hop C(n,k)")]
    public void Choose()
    {
        VietlottStatsService.Choose(45, 6).Should().Be(8_145_060);
        VietlottStatsService.Choose(55, 6).Should().Be(28_989_675);
        VietlottStatsService.Choose(6, 3).Should().Be(20);
    }
}
