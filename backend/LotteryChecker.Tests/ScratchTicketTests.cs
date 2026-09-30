using FluentAssertions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static LotteryChecker.Api.Services.ScratchTicketService;

namespace LotteryChecker.Tests;

// Đồng hồ tua được: mua lúc sáng, tua qua giờ xổ rồi chốt.
internal sealed class MovableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = utcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}

// Vé cào 2 số: bán theo ngày, trừ/cộng số dư, chốt theo giải tám, hoàn tiền khi đài không có kết quả.
public class ScratchTicketTests
{
    // Thứ Tư — MN xổ Đồng Nai, Cần Thơ, Sóc Trăng.
    private static readonly DateOnly Day = new(2026, 9, 30);

    private static DateTimeOffset Vn(DateOnly d, int h, int m = 0) =>
        new(d.Year, d.Month, d.Day, h, m, 0, TimeSpan.FromHours(7));

    private sealed class Env
    {
        private readonly string _name = Guid.NewGuid().ToString();
        public MovableTimeProvider Clock { get; } = new(Vn(Day, 10));
        public AppDbContext NewDb() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_name).Options);
        public ScratchTicketService Service() => new(NewDb(), Clock, NullLogger<ScratchTicketService>.Instance);

        public int AddUser(long balance)
        {
            using var db = NewDb();
            var u = new User { Username = $"user{Guid.NewGuid():N}"[..20], Balance = balance };
            db.Users.Add(u);
            db.SaveChanges();
            return u.Id;
        }

        public long Balance(int userId) { using var db = NewDb(); return db.Users.Single(x => x.Id == userId).Balance; }

        public void AddEighth(DateOnly date, string province, string number)
        {
            using var db = NewDb();
            db.LotteryResults.Add(new LotteryResult { DrawDate = date, Province = province, Region = "MN", PrizeTier = "8", Number = number });
            db.SaveChanges();
        }
    }

    [Theory(DisplayName = "Ngay ban: truoc 16:00 ban hom nay, tu 16:00 ban ngay mai")]
    [InlineData(15, 59, 0)]
    [InlineData(16, 0, 1)]
    [InlineData(23, 30, 1)]
    public void SalesDay_ClosesBeforeDraw(int h, int m, int addDays) =>
        SalesDay(Day.ToDateTime(new TimeOnly(h, m))).Should().Be(Day.AddDays(addDays));

    [Fact(DisplayName = "Mua: tru tien, tao ve so 2 chu so khong trung nhau, ghi so giao dich")]
    public async Task Buy_DeductsAndCreatesTickets()
    {
        var env = new Env();
        var uid = env.AddUser(100_000);

        var (result, error) = await env.Service().BuyAsync(uid, new BuyRequest("cantho", Day, 3), default);

        error.Should().Be(BuyError.None);
        result!.Balance.Should().Be(70_000);
        result.Tickets.Should().HaveCount(3).And.OnlyContain(t => t.Province == "CanTho" && t.Status == ScratchTicketStatus.Pending);
        result.Tickets.Select(t => t.Number).Should().OnlyHaveUniqueItems().And.OnlyContain(n => n.Length == 2 && n.All(char.IsDigit));
        env.Balance(uid).Should().Be(70_000);
        using var db = env.NewDb();
        db.WalletTransactions.Single().Should().Match<WalletTransaction>(t =>
            t.Kind == WalletTransactionKind.Purchase && t.Amount == -30_000 && t.BalanceAfter == 70_000);
    }

    [Fact(DisplayName = "Mua: khong du so du -> tu choi, khong tru tien, khong tao ve")]
    public async Task Buy_InsufficientBalance()
    {
        var env = new Env();
        var uid = env.AddUser(15_000);

        (await env.Service().BuyAsync(uid, new BuyRequest("CanTho", Day, 2), default))
            .Error.Should().Be(BuyError.InsufficientBalance);
        env.Balance(uid).Should().Be(15_000);
        using var db = env.NewDb();
        db.ScratchTickets.Should().BeEmpty();
    }

    [Theory(DisplayName = "Mua: sai so luong / dai khong xo hom do / sai ngay dang ban -> tu choi")]
    [InlineData("CanTho", 0, 0, BuyError.BadQuantity)]
    [InlineData("CanTho", 0, MaxQuantity + 1, BuyError.BadQuantity)]
    [InlineData("TPHCM", 0, 1, BuyError.BadProvince)]
    [InlineData("CanTho", 1, 1, BuyError.Closed)]
    public async Task Buy_Rejects(string province, int addDays, int quantity, BuyError expected)
    {
        var env = new Env();
        var uid = env.AddUser(1_000_000);
        (await env.Service().BuyAsync(uid, new BuyRequest(province, Day.AddDays(addDays), quantity), default))
            .Error.Should().Be(expected);
        env.Balance(uid).Should().Be(1_000_000);
    }

    [Fact(DisplayName = "Qua gio ngung ban ma FE van gui ngay cu -> Closed")]
    public async Task Buy_AfterCutoff_Closed()
    {
        var env = new Env();
        var uid = env.AddUser(100_000);
        env.Clock.Now = Vn(Day, 16, 5);
        (await env.Service().BuyAsync(uid, new BuyRequest("CanTho", Day, 1), default)).Error.Should().Be(BuyError.Closed);
    }

    [Fact(DisplayName = "Chot: trung giai tam cong 30 lan gia ve, chot lai khong cong them")]
    public async Task Settle_PaysWinnerOnce()
    {
        var env = new Env();
        var uid = env.AddUser(20_000);
        var tickets = (await env.Service().BuyAsync(uid, new BuyRequest("SocTrang", Day, 2), default)).Result!.Tickets;
        env.Balance(uid).Should().Be(0);

        // Chưa có kết quả → vẫn chờ
        (await env.Service().SettleAsync(null, default)).Should().Be(0);

        env.Clock.Now = Vn(Day, 17);
        env.AddEighth(Day, "SocTrang", tickets[0].Number);
        (await env.Service().SettleAsync(null, default)).Should().Be(2);
        (await env.Service().SettleAsync(null, default)).Should().Be(0);

        env.Balance(uid).Should().Be(Prize);
        var list = (await env.Service().ListAsync(uid, 1, default)).Items;
        list.Single(t => t.Id == tickets[0].Id).Should().Match<TicketDto>(t =>
            t.Status == ScratchTicketStatus.Won && t.Prize == 300_000 && t.WinningNumber == tickets[0].Number);
        list.Single(t => t.Id == tickets[1].Id).Status.Should().Be(ScratchTicketStatus.Lost);
        using var db = env.NewDb();
        db.WalletTransactions.Count(t => t.Kind == WalletTransactionKind.Win).Should().Be(1);
    }

    [Fact(DisplayName = "Chot: dai qua 3 ngay khong co ket qua -> hoan tien ve")]
    public async Task Settle_RefundsWhenNoResult()
    {
        var env = new Env();
        var uid = env.AddUser(10_000);
        await env.Service().BuyAsync(uid, new BuyRequest("DongNai", Day, 1), default);

        env.Clock.Now = Vn(Day.AddDays(RefundAfterDays), 20);
        (await env.Service().SettleAsync(uid, default)).Should().Be(0);   // mới 3 ngày — còn chờ

        env.Clock.Now = Vn(Day.AddDays(RefundAfterDays + 1), 9);
        (await env.Service().SettleAsync(uid, default)).Should().Be(1);
        env.Balance(uid).Should().Be(10_000);
        (await env.Service().ListAsync(uid, 1, default)).Items.Single().Status.Should().Be(ScratchTicketStatus.Refunded);
    }

    [Fact(DisplayName = "Cao: chi danh dau ve da chot, cua dung chu ve")]
    public async Task Scratch_OnlyOwnSettledTickets()
    {
        var env = new Env();
        var uid = env.AddUser(10_000);
        var other = env.AddUser(0);
        var id = (await env.Service().BuyAsync(uid, new BuyRequest("CanTho", Day, 1), default)).Result!.Tickets[0].Id;

        (await env.Service().ScratchAsync(uid, id, default))!.Scratched.Should().BeFalse();   // chưa xổ
        (await env.Service().ScratchAsync(other, id, default)).Should().BeNull();

        env.Clock.Now = Vn(Day, 17);
        env.AddEighth(Day, "CanTho", "00");
        await env.Service().SettleAsync(null, default);
        (await env.Service().ScratchAsync(uid, id, default))!.Scratched.Should().BeTrue();
    }

    [Fact(DisplayName = "Admin cong tien: tang so du + ghi so; user khong ton tai -> null")]
    public async Task TopUp()
    {
        var env = new Env();
        var uid = env.AddUser(5_000);
        string username;
        using (var db = env.NewDb()) username = db.Users.Single(x => x.Id == uid).Username;

        (await new Wallet(env.NewDb(), env.Clock).TopUpAsync(username.ToUpperInvariant(), 100_000, "tặng", default))
            .Should().Be(105_000);
        (await new Wallet(env.NewDb(), env.Clock).TopUpAsync("khongcoai123", 1, null, default)).Should().BeNull();
        using var check = env.NewDb();
        check.WalletTransactions.Single().Should().Match<WalletTransaction>(t =>
            t.Kind == WalletTransactionKind.TopUp && t.Amount == 100_000 && t.BalanceAfter == 105_000);
    }

    [Fact(DisplayName = "So du la concurrency token: 2 noi cung sua thi noi luu sau bi chan")]
    public void Balance_IsConcurrencyToken()
    {
        var env = new Env();
        var uid = env.AddUser(10_000);
        using var a = env.NewDb();
        using var b = env.NewDb();
        a.Users.Single(x => x.Id == uid).Balance -= 10_000;
        b.Users.Single(x => x.Id == uid).Balance -= 10_000;
        a.SaveChanges();
        b.Invoking(x => x.SaveChanges()).Should().Throw<DbUpdateConcurrencyException>();
        env.Balance(uid).Should().Be(0);
    }
}
