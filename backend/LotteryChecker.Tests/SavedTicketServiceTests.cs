using FluentAssertions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static LotteryChecker.Api.Services.SavedTicketService;

namespace LotteryChecker.Tests;

// Vé đã lưu: kiểm tra khi lưu, chốt khi có kết quả, báo ở chuông, hết hạn, nhắc lĩnh thưởng.
public class SavedTicketServiceTests
{
    // Thứ Tư — MN xổ Đồng Nai, Cần Thơ, Sóc Trăng.
    private static readonly DateOnly Day = new(2026, 9, 30);
    private const string Prov = "DongNai";

    private static DateTimeOffset Vn(DateOnly d, int h, int m = 0) =>
        new(d.Year, d.Month, d.Day, h, m, 0, TimeSpan.FromHours(7));

    private sealed class FakePusher : INotificationPusher
    {
        public List<(int UserId, NotificationService.NotificationDto N)> Sent { get; } = [];
        public Task PushAsync(int userId, NotificationService.NotificationDto n, CancellationToken ct)
        {
            Sent.Add((userId, n));
            return Task.CompletedTask;
        }
    }

    private sealed class Env
    {
        private readonly string _name = Guid.NewGuid().ToString();
        public MovableTimeProvider Clock { get; } = new(Vn(Day, 10));
        public FakePusher Pusher { get; } = new();

        public AppDbContext NewDb() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_name).Options);

        public SavedTicketService Service()
        {
            var db = NewDb();
            return new SavedTicketService(db, new LotteryMatcher(db, Clock), new CheckStats(db, Clock, NullLogger<CheckStats>.Instance),
                new NotificationService(db, Pusher, Clock, NullLogger<NotificationService>.Instance), Clock,
                NullLogger<SavedTicketService>.Instance);
        }

        public int AddUser()
        {
            using var db = NewDb();
            var u = new User { Username = $"u{Guid.NewGuid():N}"[..20] };
            db.Users.Add(u);
            db.SaveChanges();
            return u.Id;
        }

        /// <summary>Đủ 18 số: ĐB 999999, G8 "07", G7 "123", phần còn lại không trùng các vé test.</summary>
        public void AddResults(DateOnly date, string province = Prov, int rows = 18)
        {
            using var db = NewDb();
            var all = new List<(string Tier, string Number)>
            {
                ("DB", "999999"), ("8", "07"), ("7", "123"), ("6", "1111"), ("6", "2222"), ("6", "3333"), ("5", "4444"),
                ("4", "10001"), ("4", "10002"), ("4", "10003"), ("4", "10004"), ("4", "10005"), ("4", "10006"), ("4", "10007"),
                ("3", "20001"), ("3", "20002"), ("2", "30001"), ("1", "40001"),
            };
            db.LotteryResults.AddRange(all.Take(rows).Select(x => new LotteryResult
                { DrawDate = date, Province = province, Region = "MN", PrizeTier = x.Tier, Number = x.Number }));
            db.SaveChanges();
        }

        public SavedTicket Ticket(int id) { using var db = NewDb(); return db.SavedTickets.AsNoTracking().Single(x => x.Id == id); }
    }

    private static SaveRequest Req(string number, DateOnly? date = null, string province = Prov) => new(number, date ?? Day, province, null);

    [Theory(DisplayName = "Luu: so sai, dai khong phai MN, dai khong xo ngay do, qua han, qua xa -> tu choi")]
    [InlineData("12345", Prov, 0, SaveError.BadNumber)]
    [InlineData("12a456", Prov, 0, SaveError.BadNumber)]
    [InlineData("123456", "Abc", 0, SaveError.BadProvince)]
    [InlineData("123456", "DaNang", 0, SaveError.NotSouth)]
    [InlineData("123456", "MB", 0, SaveError.NotSouth)]
    [InlineData("123456", "TPHCM", 0, SaveError.BadDate)]
    [InlineData("123456", Prov, -35, SaveError.Expired)]
    [InlineData("123456", Prov, 21, SaveError.TooFar)]
    public async Task Save_Rejects(string number, string province, int addDays, SaveError expected)
    {
        var env = new Env();
        var (t, err) = await env.Service().SaveAsync(env.AddUser(), Req(number, Day.AddDays(addDays), province), default);
        t.Should().BeNull();
        err.Should().Be(expected);
    }

    [Fact(DisplayName = "Luu trung -> tra dong cu; ten dai khong phan biet hoa thuong")]
    public async Task Save_Idempotent()
    {
        var env = new Env();
        var uid = env.AddUser();
        var a = (await env.Service().SaveAsync(uid, Req("123456"), default)).Ticket!;
        var b = (await env.Service().SaveAsync(uid, Req("123456", province: "dongnai"), default)).Ticket!;
        b.Id.Should().Be(a.Id);
        a.Status.Should().Be(SavedTicketStatus.Waiting);
        a.Province.Should().Be(Prov);
        using var db = env.NewDb();
        db.SavedTickets.Count().Should().Be(1);
    }

    [Fact(DisplayName = "Vuot 100 ve dang cho -> LimitReached")]
    public async Task Save_Limit()
    {
        var env = new Env();
        var uid = env.AddUser();
        using (var db = env.NewDb())
        {
            db.SavedTickets.AddRange(Enumerable.Range(0, MaxWaiting).Select(i => new SavedTicket
                { UserId = uid, TicketNumber = i.ToString("D6"), DrawDate = Day, Province = Prov, Status = SavedTicketStatus.Waiting }));
            db.SaveChanges();
        }
        (await env.Service().SaveAsync(uid, Req("999998"), default)).Error.Should().Be(SaveError.LimitReached);
    }

    [Fact(DisplayName = "Luu ve da co ket qua -> chot ngay, khong tao thong bao")]
    public async Task Save_SettlesImmediately()
    {
        var env = new Env();
        env.Clock.Now = Vn(Day, 18);
        env.AddResults(Day);
        var uid = env.AddUser();

        var won = (await env.Service().SaveAsync(uid, Req("555123"), default)).Ticket!;
        won.Status.Should().Be(SavedTicketStatus.Won);
        won.Prize.Should().Be(200_000);
        won.PrizeTiers.Should().Equal("Giải Bảy");

        (await env.Service().SaveAsync(uid, Req("000000"), default)).Ticket!.Status.Should().Be(SavedTicketStatus.Lost);
        env.Pusher.Sent.Should().BeEmpty();
        using var db = env.NewDb();
        db.CheckedTickets.Count().Should().Be(2);   // thống kê cộng đồng vẫn ghi
        db.CheckHistory.Count().Should().Be(0);
    }

    [Fact(DisplayName = "Settle: chua du 18 so -> van Waiting; du -> chot dung tien, bao chuong moi ve")]
    public async Task Settle_Flow()
    {
        var env = new Env();
        var uid = env.AddUser();
        var svc = env.Service();
        var won = (await svc.SaveAsync(uid, Req("555123"), default)).Ticket!;
        var lost = (await svc.SaveAsync(uid, Req("000000"), default)).Ticket!;

        env.Clock.Now = Vn(Day, 17);
        env.AddResults(Day, rows: 10);
        (await env.Service().SettleAsync(default)).Should().Be(0);
        env.Ticket(won.Id).Status.Should().Be(SavedTicketStatus.Waiting);

        using (var db = env.NewDb()) { db.LotteryResults.RemoveRange(db.LotteryResults); db.SaveChanges(); }
        env.AddResults(Day);
        (await env.Service().SettleAsync(default)).Should().Be(2);

        env.Ticket(won.Id).Should().Match<SavedTicket>(t => t.Status == SavedTicketStatus.Won && t.Prize == 200_000 && t.SettledAt != null);
        env.Ticket(lost.Id).Status.Should().Be(SavedTicketStatus.Lost);

        env.Pusher.Sent.Select(x => x.N.Kind).Should().BeEquivalentTo([NotificationKind.TicketWon, NotificationKind.TicketLost]);

        // Chạy lại: không có gì để chốt, không báo trùng.
        (await env.Service().SettleAsync(default)).Should().Be(0);
        env.Pusher.Sent.Should().HaveCount(2);

        var list = await new NotificationService(env.NewDb(), env.Pusher, env.Clock, NullLogger<NotificationService>.Instance).ListAsync(uid, default);
        list.Unread.Should().Be(2);
        list.Items.Select(x => x.Snippet).Should().Contain($"555123|{Prov}|2026-09-30|200000");
    }

    [Fact(DisplayName = "Settle: chua den gio xo thi khong cham du DB co ket qua")]
    public async Task Settle_NotDrawnYet()
    {
        var env = new Env();
        var uid = env.AddUser();
        var t = (await env.Service().SaveAsync(uid, Req("555123"), default)).Ticket!;
        env.AddResults(Day);
        (await env.Service().SettleAsync(default)).Should().Be(0);
        env.Ticket(t.Id).Status.Should().Be(SavedTicketStatus.Waiting);
    }

    [Fact(DisplayName = "ExpireAsync: ve cho qua 30 ngay -> Expired; con han thi giu")]
    public async Task Expire()
    {
        var env = new Env();
        var uid = env.AddUser();
        using (var db = env.NewDb())
        {
            db.SavedTickets.AddRange(
                new SavedTicket { UserId = uid, TicketNumber = "111111", DrawDate = Day, Province = Prov, Status = SavedTicketStatus.Waiting },
                new SavedTicket { UserId = uid, TicketNumber = "222222", DrawDate = Day.AddDays(7), Province = Prov, Status = SavedTicketStatus.Waiting });
            db.SaveChanges();
        }
        env.Clock.Now = Vn(Day.AddDays(DrawSchedule.ClaimDays + 1), 9);
        (await env.Service().ExpireAsync(default)).Should().Be(1);
        using var check = env.NewDb();
        check.SavedTickets.Single(x => x.TicketNumber == "111111").Status.Should().Be(SavedTicketStatus.Expired);
        check.SavedTickets.Single(x => x.TicketNumber == "222222").Status.Should().Be(SavedTicketStatus.Waiting);
    }

    private static async Task<(Env Env, int TicketId)> WonTicket(bool claimed = false)
    {
        var env = new Env();
        var uid = env.AddUser();
        using var db = env.NewDb();
        var t = new SavedTicket { UserId = uid, TicketNumber = "555123", DrawDate = Day, Province = Prov, Status = SavedTicketStatus.Won,
                                  Prize = 200_000, ClaimedAt = claimed ? DateTime.UtcNow : null };
        db.SavedTickets.Add(t);
        await db.SaveChangesAsync();
        return (env, t.Id);
    }

    [Fact(DisplayName = "Nhac linh: D-6 khong nhac; D-5 nhac 1 lan; D-1 nhac lan 2")]
    public async Task Remind_Stages()
    {
        var (env, id) = await WonTicket();
        var deadline = DrawSchedule.ClaimDeadline(Day);

        env.Clock.Now = Vn(deadline.AddDays(-6), 10);
        (await env.Service().RemindAsync(default)).Should().Be(0);

        env.Clock.Now = Vn(deadline.AddDays(-5), 10);
        (await env.Service().RemindAsync(default)).Should().Be(1);
        (await env.Service().RemindAsync(default)).Should().Be(0);
        env.Ticket(id).ReminderStage.Should().Be(1);

        env.Clock.Now = Vn(deadline.AddDays(-1), 10);
        (await env.Service().RemindAsync(default)).Should().Be(1);
        (await env.Service().RemindAsync(default)).Should().Be(0);
        env.Ticket(id).ReminderStage.Should().Be(2);

        env.Pusher.Sent.Should().HaveCount(2);
        env.Pusher.Sent.Should().OnlyContain(x => x.N.Kind == NotificationKind.TicketExpiring);
    }

    [Fact(DisplayName = "Nhac linh: da linh thi khong nhac")]
    public async Task Remind_Claimed()
    {
        var (env, _) = await WonTicket(claimed: true);
        env.Clock.Now = Vn(DrawSchedule.ClaimDeadline(Day).AddDays(-1), 10);
        (await env.Service().RemindAsync(default)).Should().Be(0);
    }

    [Theory(DisplayName = "Nhac linh: ngoai 08:00-21:00 gio VN thi khong gui")]
    [InlineData(7, 59)]
    [InlineData(21, 0)]
    [InlineData(2, 0)]
    public async Task Remind_QuietHours(int h, int m)
    {
        var (env, _) = await WonTicket();
        env.Clock.Now = Vn(DrawSchedule.ClaimDeadline(Day).AddDays(-1), h, m);
        (await env.Service().RemindAsync(default)).Should().Be(0);
    }

    [Fact(DisplayName = "Danh dau da linh / xoa chi ve cua minh")]
    public async Task Claim_Delete_OwnOnly()
    {
        var (env, id) = await WonTicket();
        var owner = env.Ticket(id).UserId;
        var other = env.AddUser();
        (await env.Service().MarkClaimedAsync(other, id, true, default)).Should().BeNull();
        (await env.Service().MarkClaimedAsync(owner, id, true, default))!.ClaimedAt.Should().NotBeNull();
        (await env.Service().SummaryAsync(owner, default)).UnclaimedWins.Should().Be(0);
        (await env.Service().DeleteAsync(other, id, default)).Should().BeFalse();
        (await env.Service().DeleteAsync(owner, id, default)).Should().BeTrue();
    }
}
