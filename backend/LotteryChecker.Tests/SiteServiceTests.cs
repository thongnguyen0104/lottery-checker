using FluentAssertions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static LotteryChecker.Api.Services.SiteService;

namespace LotteryChecker.Tests;

// Website con: địa chỉ, nháp → publish, lọc HTML, sản phẩm + giữ vé, ảnh, báo cáo tự ẩn, thông báo chủ site.
public class SiteServiceTests
{
    private class FakePusher : INotificationPusher
    {
        public List<(int UserId, NotificationService.NotificationDto N)> Sent { get; } = [];
        public Task PushAsync(int userId, NotificationService.NotificationDto n, CancellationToken ct)
        {
            Sent.Add((userId, n));
            return Task.CompletedTask;
        }
    }

    private class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private const int Owner = 1, Alice = 2, Bob = 3, Carol = 4;
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 3, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken Ct = CancellationToken.None;
    private const string Img = "/api/sites/images/sites/2026/10/0d6c1c4e-1111-4222-8333-444455556666.webp";

    private static (SiteService Sites, NotificationService Notes, FakePusher Pusher, AppDbContext Db) New()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        foreach (var (id, name) in new[] { (Owner, "owner"), (Alice, "alice"), (Bob, "bob"), (Carol, "carol") })
            db.Users.Add(new User { Id = id, Username = name });
        db.SaveChanges();
        var clock = new FixedClock(Now);
        var pusher = new FakePusher();
        return (new SiteService(db, clock, new SiteOptions { AutoHideReports = 3, MaxPendingReservations = 2 }, new SiteHtmlSanitizer()),
                new NotificationService(db, pusher, clock, NullLogger<NotificationService>.Instance), pusher, db);
    }

    private static async Task<MineDto> Published(SiteService sites, string slug = "dai-ly-minh-chau")
    {
        var mine = await sites.CreateAsync(Owner, slug, Ct);
        return await sites.PublishAsync(Owner, Ct);
    }

    [Theory(DisplayName = "ValidateSlug: chu thuong, so, gach ngang; tu choi tu giu va sai dinh dang")]
    [InlineData("dai-ly-123", true)]
    [InlineData("ab", false)]
    [InlineData("Dai-Ly", false)]
    [InlineData("dai--ly", false)]
    [InlineData("-daily", false)]
    [InlineData("đại-lý", false)]
    [InlineData("admin", false)]
    [InlineData("ban-do", false)]
    public void ValidateSlug_Rules(string slug, bool ok) => (ValidateSlug(slug, false) == null).Should().Be(ok);

    [Fact(DisplayName = "Moi tai khoan 1 site; slug trung bi tu choi")]
    public async Task Create_OnePerUser_UniqueSlug()
    {
        var (sites, _, _, _) = New();
        var mine = await sites.CreateAsync(Owner, "minh-chau", Ct);
        mine.Draft.Name.Should().Be("owner");
        mine.Published.Should().BeNull();

        (await FluentActions.Awaiting(() => sites.CreateAsync(Owner, "khac", Ct)).Should().ThrowAsync<SiteException>()).Which.Status.Should().Be(409);
        (await FluentActions.Awaiting(() => sites.CreateAsync(Alice, "minh-chau", Ct)).Should().ThrowAsync<SiteException>()).Which.Status.Should().Be(409);
    }

    [Fact(DisplayName = "Nhap chua publish thi khach khong thay; publish xong moi thay, chu site xem truoc duoc nhap")]
    public async Task Draft_Publish_Preview()
    {
        var (sites, _, _, _) = New();
        var mine = await sites.CreateAsync(Owner, "minh-chau", Ct);
        (await sites.GetPublicAsync("minh-chau", Alice, false, Ct)).Should().BeNull();
        (await sites.GetPublicAsync("minh-chau", Owner, true, Ct)).Should().NotBeNull();
        (await sites.GetPublicAsync("minh-chau", Alice, true, Ct)).Should().BeNull();   // người khác không xem nháp

        await sites.PublishAsync(Owner, Ct);
        var after = await sites.SaveDraftAsync(Owner, new SettingsInput(null, mine.Draft with { Name = "Đại lý Minh Châu" }, null), Ct);
        after.HasUnpublishedChanges.Should().BeTrue();
        (await sites.GetPublicAsync("minh-chau", null, false, Ct))!.Config.Name.Should().Be("owner");   // khách vẫn thấy bản cũ
        (await sites.GetPublicAsync("minh-chau", Owner, true, Ct))!.Config.Name.Should().Be("Đại lý Minh Châu");

        (await sites.PublishAsync(Owner, Ct)).HasUnpublishedChanges.Should().BeFalse();
        (await sites.GetPublicAsync("minh-chau", null, false, Ct))!.Config.Name.Should().Be("Đại lý Minh Châu");
    }

    [Fact(DisplayName = "Normalize: mau sai, link Facebook la, anh ngoai bi tu choi; khoi la bi bo, khoi thieu them vao cuoi")]
    public async Task Normalize_Config()
    {
        var (sites, _, _, _) = New();
        var c = (await sites.CreateAsync(Owner, "minh-chau", Ct)).Draft;
        FluentActions.Invoking(() => sites.Normalize(c with { PrimaryColor = "red" })).Should().Throw<SiteException>();
        FluentActions.Invoking(() => sites.Normalize(c with { Facebook = "https://evil.com/x" })).Should().Throw<SiteException>();
        FluentActions.Invoking(() => sites.Normalize(c with { LogoUrl = "https://evil.com/a.png" })).Should().Throw<SiteException>();
        sites.Normalize(c with { LogoUrl = Img, Facebook = "https://www.facebook.com/minhchau" }).LogoUrl.Should().Be(Img);

        var n = sites.Normalize(c with { Blocks = [new("contact", true), new("evil", true), new("contact", false)] });
        n.Blocks.Select(b => b.Type).Should().Equal(["contact", .. BlockTypes.Where(t => t != "contact")]);
        n.Blocks[0].Enabled.Should().BeTrue();
        n.Blocks.Skip(1).Should().OnlyContain(b => !b.Enabled);
    }

    [Fact(DisplayName = "Sanitize: bo script, onerror, javascript:, anh ngoai; link them rel nofollow")]
    public void Sanitize_Html()
    {
        var s = new SiteHtmlSanitizer();
        var html = s.Sanitize("""
            <p onclick="x()">Chào <strong>bạn</strong><script>alert(1)</script></p>
            <img src="x" onerror="alert(1)"><img src="https://evil.com/t.gif"><img src="{IMG}">
            <a href="javascript:alert(1)">a</a><a href="https://vietlott.vn">b</a>
            <div style="color:red">div</div>
            """.Replace("{IMG}", Img));
        html.Should().NotContain("script").And.NotContain("onerror").And.NotContain("onclick").And.NotContain("javascript")
            .And.NotContain("evil.com").And.NotContain("style").And.NotContain("<div");
        html.Should().Contain("<strong>bạn</strong>").And.Contain($"src=\"{Img}\"").And.Contain("rel=\"nofollow noopener noreferrer\"");
    }

    [Fact(DisplayName = "Giu ve: kiem tra du lieu, gioi han dang cho, chu site nhan thong bao, xac nhan tru ton kho")]
    public async Task Reserve_Flow()
    {
        var (sites, notes, pusher, _) = New();
        await Published(sites);
        var p = await sites.SaveProductAsync(Owner, null, new ProductInput("Vé TP.HCM", null, 10_000, SiteProductKind.Traditional, null, 5), Ct);

        var bad = new ReservationInput(p.Id, "An", "123", 1, null);
        await FluentActions.Awaiting(() => sites.ReserveAsync("dai-ly-minh-chau", bad, null, "ip:1", Ct)).Should().ThrowAsync<SiteException>();
        await FluentActions.Awaiting(() => sites.ReserveAsync("dai-ly-minh-chau", bad with { Phone = "0901234567", Quantity = 6 }, null, "ip:1", Ct))
            .Should().ThrowAsync<SiteException>();   // vượt tồn kho
        await FluentActions.Awaiting(() => sites.ReserveAsync("dai-ly-minh-chau", bad with { Phone = "0901234567" }, Owner, "u:1", Ct))
            .Should().ThrowAsync<SiteException>();   // tự giữ vé site mình

        var ok = bad with { Phone = "0901234567", Quantity = 3 };
        var (id, rid, owner) = await sites.ReserveAsync("dai-ly-minh-chau", ok, null, "ip:1", Ct);
        owner.Should().Be(Owner);
        await notes.OnSiteReservationAsync(rid, Ct);
        pusher.Sent.Should().ContainSingle(x => x.UserId == Owner && x.N.Kind == NotificationKind.SiteReservation && x.N.SiteSlug == "dai-ly-minh-chau");
        (await notes.ListAsync(Owner, Ct)).Unread.Should().Be(1);

        await sites.ReserveAsync("dai-ly-minh-chau", ok with { Quantity = 1 }, null, "ip:1", Ct);
        (await FluentActions.Awaiting(() => sites.ReserveAsync("dai-ly-minh-chau", ok with { Quantity = 1 }, null, "ip:1", Ct))
            .Should().ThrowAsync<SiteException>()).Which.Status.Should().Be(429);

        (await sites.SetReservationStatusAsync(Owner, id, ReservationStatus.Confirmed, Ct)).Status.Should().Be(ReservationStatus.Confirmed);
        (await sites.ListProductsAsync(Owner, Ct)).Single().Stock.Should().Be(2);
        await sites.SetReservationStatusAsync(Owner, id, ReservationStatus.Cancelled, Ct);
        (await sites.ListProductsAsync(Owner, Ct)).Single().Stock.Should().Be(5);

        // Người khác không đổi được yêu cầu của site này.
        await FluentActions.Awaiting(() => sites.SetReservationStatusAsync(Alice, id, ReservationStatus.Completed, Ct)).Should().ThrowAsync<SiteException>();
    }

    [Fact(DisplayName = "Bai viet: nhap chi chu site thay; publish thi hien tren trang; dang cheo tao 1 bai Blog")]
    public async Task Posts_Flow()
    {
        var (sites, _, _, db) = New();
        await Published(sites);
        var draft = await sites.SavePostAsync(Owner, null, new PostInput("Khai trương", "<p>Mừng khai trương</p>", null, false), Ct);
        (await sites.GetPublicPostAsync("dai-ly-minh-chau", draft.Id, Alice, Ct)).Should().BeNull();
        (await sites.GetPublicPostAsync("dai-ly-minh-chau", draft.Id, Owner, Ct)).Should().NotBeNull();

        var pub = await sites.SavePostAsync(Owner, draft.Id, new PostInput("Khai trương", "<p>Mừng khai trương</p>", null, true, true), Ct);
        pub.CrossPosted.Should().BeTrue();
        await sites.SavePostAsync(Owner, draft.Id, new PostInput("Khai trương!", "<p>Mừng khai trương</p>", null, true, true), Ct);
        db.BlogPosts.Should().ContainSingle().Which.Content.Should().Contain($"/s/dai-ly-minh-chau/bai-viet/{draft.Id}");
        (await sites.GetPublicAsync("dai-ly-minh-chau", null, false, Ct))!.Posts.Should().ContainSingle(x => x.Title == "Khai trương!");

        await FluentActions.Awaiting(() => sites.SavePostAsync(Owner, null, new PostInput("Rỗng", "<script>x</script>", null, true), Ct))
            .Should().ThrowAsync<SiteException>();
    }

    [Fact(DisplayName = "Anh: dung trong nhap thi Used; go ra thi mo coi; chua dung chi nguoi upload xem")]
    public async Task Images_Sync()
    {
        var (sites, _, _, db) = New();
        var mine = await sites.CreateAsync(Owner, "minh-chau", Ct);
        await sites.AddImageAsync(Img["/api/sites/images/".Length..], Owner, 100, Ct);
        var key = Img["/api/sites/images/".Length..];
        (await sites.CanViewImageAsync(key, Alice, Ct)).Should().BeFalse();
        (await sites.CanViewImageAsync(key, Owner, Ct)).Should().BeTrue();

        await sites.SaveDraftAsync(Owner, new SettingsInput(null, mine.Draft with { LogoUrl = Img }, null), Ct);
        db.SiteImages.Single().Used.Should().BeTrue();
        (await sites.CanViewImageAsync(key, Alice, Ct)).Should().BeTrue();

        await sites.SaveDraftAsync(Owner, new SettingsInput(null, mine.Draft with { LogoUrl = null }, null), Ct);
        db.SiteImages.Single().Used.Should().BeFalse();
    }

    [Fact(DisplayName = "Chi gan duoc diem ban minh ghim")]
    public async Task LinkShop_OwnOnly()
    {
        var (sites, _, _, db) = New();
        var mine = await sites.CreateAsync(Owner, "minh-chau", Ct);
        var own = new ShopLocation { Name = "Của tôi", UserId = Owner, Lat = 10.77, Lng = 106.69, Address = "Q1" };
        var other = new ShopLocation { Name = "Của Alice", UserId = Alice, Lat = 10.78, Lng = 106.70, Address = "Q3" };
        db.Shops.AddRange(own, other);
        await db.SaveChangesAsync();

        await FluentActions.Awaiting(() => sites.SaveDraftAsync(Owner, new SettingsInput(null, mine.Draft, other.PublicId), Ct))
            .Should().ThrowAsync<SiteException>();
        (await sites.SaveDraftAsync(Owner, new SettingsInput(null, mine.Draft, own.PublicId), Ct)).Shop!.Name.Should().Be("Của tôi");
    }

    [Fact(DisplayName = "Du 3 nguoi bao cao thi tu an; admin hien lai thi xoa bao cao")]
    public async Task Report_AutoHide()
    {
        var (sites, _, _, db) = New();
        var mine = await Published(sites);
        await FluentActions.Awaiting(() => sites.ReportAsync("dai-ly-minh-chau", new(SiteReportReason.Scam, null), Owner, Ct))
            .Should().ThrowAsync<SiteException>();
        (await sites.ReportAsync("dai-ly-minh-chau", new(SiteReportReason.Scam, null), Alice, Ct)).Should().BeFalse();
        (await sites.ReportAsync("dai-ly-minh-chau", new(SiteReportReason.Scam, "lại"), Alice, Ct)).Should().BeFalse();   // cùng người
        (await sites.ReportAsync("dai-ly-minh-chau", new(SiteReportReason.Other, null), Bob, Ct)).Should().BeFalse();
        (await sites.ReportAsync("dai-ly-minh-chau", new(SiteReportReason.Inappropriate, null), Carol, Ct)).Should().BeTrue();
        (await sites.GetPublicAsync("dai-ly-minh-chau", null, false, Ct)).Should().BeNull();

        (await sites.AdminListAsync(Ct)).Single().Reports.Should().HaveCount(3);
        await sites.SetStatusAsync(mine.Id, SiteStatus.Active, Ct);
        (await sites.GetPublicAsync("dai-ly-minh-chau", null, false, Ct)).Should().NotBeNull();
        db.SiteReports.Should().OnlyContain(r => r.Resolved);
    }
}
