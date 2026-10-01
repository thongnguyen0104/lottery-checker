using FluentAssertions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static LotteryChecker.Api.Services.ShopService;

namespace LotteryChecker.Tests;

// Bản đồ điểm bán: kiểm tra dữ liệu, hạn mức, gần / trùng, tự ẩn khi bị báo cáo, đánh giá, vé trúng, thông báo.
public class ShopServiceTests
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
    // Bến Thành, TP.HCM.
    private const double Lat = 10.7725, Lng = 106.6980;
    // Thứ Hai 2026-09-28 20:00 giờ VN — đã qua giờ xổ.
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 13, 0, 0, TimeSpan.Zero);

    private static (ShopService Shops, NotificationService Notes, FakePusher Pusher, AppDbContext Db) New(int dailyLimit = 10)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        foreach (var (id, name) in new[] { (Owner, "owner"), (Alice, "alice"), (Bob, "bob"), (Carol, "carol") })
            db.Users.Add(new User { Id = id, Username = name });
        db.SaveChanges();
        var clock = new FixedClock(Now);
        var pusher = new FakePusher();
        return (new ShopService(db, clock, new ShopOptions { DailyCreateLimit = dailyLimit, AutoHideReports = 3 }),
                new NotificationService(db, pusher, clock, NullLogger<NotificationService>.Instance), pusher, db);
    }

    private static ShopInput Input(double lat = Lat, double lng = Lng, ShopType type = ShopType.Agency, string? phone = null,
                                   bool consent = false, string name = "Đại lý Minh Ngọc") =>
        new(name, type, lat, lng, "Chợ Bến Thành", phone, consent, 360, 1260, null);

    // ~1m theo vĩ độ.
    private static double Meters(double m) => m / 111_320;

    [Fact(DisplayName = "Validate: ngoai VN, ten ngan, gio sai bi tu choi; hop le thi null")]
    public void Validate_Basics()
    {
        Validate(Input(), false).Should().BeNull();
        Validate(Input(lat: 35.68, lng: 139.69), false).Should().NotBeNull();   // Tokyo
        Validate(Input(name: "A"), false).Should().NotBeNull();
        Validate(Input() with { OpensAtMin = 360, ClosesAtMin = null }, false).Should().NotBeNull();
        Validate(Input() with { OpensAtMin = 2000, ClosesAtMin = 100 }, false).Should().NotBeNull();
    }

    [Fact(DisplayName = "SDT: nguoi ban dao khong duoc co; dai ly can tick dong y + dung dinh dang")]
    public void Validate_Phone()
    {
        Validate(Input(type: ShopType.Street, phone: "0901234567", consent: true), false).Should().NotBeNull();
        Validate(Input(phone: "0901234567"), false).Should().NotBeNull();                  // thiếu đồng ý
        Validate(Input(phone: "12345", consent: true), false).Should().NotBeNull();        // sai định dạng
        Validate(Input(phone: "+84 90 123 4567", consent: true), false).Should().BeNull();
        NormalizePhone("+84 90.123-4567").Should().Be("0901234567");
    }

    [Fact(DisplayName = "Tao diem: luu SDT da chuan hoa; nguoi ban dao thi bo SDT")]
    public async Task Create_NormalizesPhone()
    {
        var (shops, _, _, _) = New();
        (await shops.CreateAsync(Input(phone: "+84 90 123 4567", consent: true), Owner, default)).Phone.Should().Be("0901234567");
        (await shops.CreateAsync(Input(type: ShopType.Street, phone: "0901234567", consent: true), Owner, default)).Phone.Should().BeNull();
    }

    [Fact(DisplayName = "Moi user toi da N diem / 24h")]
    public async Task DailyLimit()
    {
        var (shops, _, _, _) = New(dailyLimit: 2);
        await shops.CreateAsync(Input(), Owner, default);
        await shops.CreateAsync(Input(), Owner, default);
        var act = () => shops.CreateAsync(Input(), Owner, default);
        (await act.Should().ThrowAsync<ShopException>()).Which.Status.Should().Be(429);
        await shops.CreateAsync(Input(), Alice, default);   // người khác không bị ảnh hưởng
    }

    [Fact(DisplayName = "Nearby: diem cach 20m co, 50m khong; gan nhat truoc")]
    public async Task Nearby_Radius()
    {
        var (shops, _, _, _) = New();
        await shops.CreateAsync(Input(lat: Lat + Meters(50), name: "Xa"), Owner, default);
        await shops.CreateAsync(Input(lat: Lat + Meters(20), name: "Gần"), Owner, default);
        await shops.CreateAsync(Input(lat: Lat + Meters(5), name: "Sát"), Owner, default);

        var near = await shops.NearbyAsync(Lat, Lng, DuplicateRadiusM, default);
        near.Select(x => x.Name).Should().Equal("Sát", "Gần");
        near[1].DistanceM.Should().BeApproximately(20, 1);
    }

    [Fact(DisplayName = "Bbox: chi diem trong khung, khong co diem an; loc theo loai")]
    public async Task Bbox_Filters()
    {
        var (shops, _, _, _) = New();
        await shops.CreateAsync(Input(), Owner, default);
        await shops.CreateAsync(Input(type: ShopType.Vietlott, name: "Vietlott Q1"), Owner, default);
        await shops.CreateAsync(Input(lat: 21.03, lng: 105.85, name: "Hà Nội"), Owner, default);

        (await shops.QueryBboxAsync(10.7, 106.6, 10.8, 106.8, null, false, default)).Should().HaveCount(2);
        (await shops.QueryBboxAsync(10.7, 106.6, 10.8, 106.8, [ShopType.Vietlott], false, default))
            .Should().ContainSingle().Which.Name.Should().Be("Vietlott Q1");
    }

    [Fact(DisplayName = "Bao cao: 3 nguoi khac nhau -> an; bao lai khong tinh; admin khoi phuc -> reset")]
    public async Task Reports_AutoHide()
    {
        var (shops, _, _, db) = New();
        var s = await shops.CreateAsync(Input(), Owner, default);
        var report = new ReportInput(ShopReportReason.NotExist, null);

        (await shops.ReportAsync(s.Id, report, Alice, default)).Should().BeFalse();
        var again = () => shops.ReportAsync(s.Id, report, Alice, default);
        (await again.Should().ThrowAsync<ShopException>()).Which.Status.Should().Be(409);
        var own = () => shops.ReportAsync(s.Id, report, Owner, default);
        await own.Should().ThrowAsync<ShopException>();

        (await shops.ReportAsync(s.Id, report, Bob, default)).Should().BeFalse();
        (await shops.ReportAsync(s.Id, report, Carol, default)).Should().BeTrue();

        (await shops.QueryBboxAsync(10, 106, 11, 107, null, false, default)).Should().BeEmpty();
        (await shops.GetAsync(s.Id, Alice, false, default)).Should().BeNull();       // người khác không thấy
        (await shops.GetAsync(s.Id, Owner, false, default)).Should().NotBeNull();    // người tạo vẫn thấy
        (await shops.ListReportedAsync(default)).Should().ContainSingle().Which.Reports.Should().HaveCount(3);

        await shops.SetStatusAsync(s.Id, ShopStatus.Visible, default);
        (await shops.QueryBboxAsync(10, 106, 11, 107, null, false, default)).Should().ContainSingle();
        (await db.Shops.SingleAsync()).ReportCount.Should().Be(0);
        // Đã xử lý rồi thì báo lại được (tính lại từ 1).
        (await shops.ReportAsync(s.Id, report, Alice, default)).Should().BeFalse();
    }

    [Fact(DisplayName = "Danh gia: upsert cap nhat trung binh; xoa thi tru lai")]
    public async Task Reviews_Rating()
    {
        var (shops, _, _, _) = New();
        var s = await shops.CreateAsync(Input(), Owner, default);

        (await shops.UpsertReviewAsync(s.Id, new ReviewInput(5, "Vui vẻ"), Alice, default)).IsNew.Should().BeTrue();
        var bob = await shops.UpsertReviewAsync(s.Id, new ReviewInput(2, null), Bob, default);
        bob.Rating.Should().Be(3.5);
        var edited = await shops.UpsertReviewAsync(s.Id, new ReviewInput(4, "Sửa"), Bob, default);
        edited.IsNew.Should().BeFalse();
        edited.Rating.Should().Be(4.5);
        edited.RatingCount.Should().Be(2);

        var (rating, count) = await shops.DeleteReviewAsync(edited.Id, Bob, false, default);
        (rating, count).Should().Be((5.0, 1));
        var bad = () => shops.UpsertReviewAsync(s.Id, new ReviewInput(6, null), Bob, default);
        await bad.Should().ThrowAsync<ShopException>();
    }

    [Fact(DisplayName = "Ve trung: dai phai co ky quay ngay do; khong bao trung 2 lan")]
    public async Task Wins_Validate()
    {
        var (shops, _, _, _) = New();
        var s = await shops.CreateAsync(Input(), Owner, default);
        var monday = new DateOnly(2026, 9, 28);

        (await shops.AddWinAsync(s.Id, new WinInput(monday, "TPHCM", "G8"), Alice, default)).Win.PrizeTier.Should().Be("G8");
        var dup = () => shops.AddWinAsync(s.Id, new WinInput(monday, "TPHCM", "G8"), Alice, default);
        (await dup.Should().ThrowAsync<ShopException>()).Which.Status.Should().Be(409);

        var wrongDay = () => shops.AddWinAsync(s.Id, new WinInput(monday, "BenTre", "G8"), Alice, default);   // Bến Tre quay thứ Ba
        await wrongDay.Should().ThrowAsync<ShopException>();
        var future = () => shops.AddWinAsync(s.Id, new WinInput(monday.AddDays(1), "BenTre", "G8"), Alice, default);
        await future.Should().ThrowAsync<ShopException>();
        var badTier = () => shops.AddWinAsync(s.Id, new WinInput(monday, "TPHCM", "Jackpot"), Alice, default);
        await badTier.Should().ThrowAsync<ShopException>();

        await shops.AddWinAsync(s.Id, new WinInput(monday, Vietlott, "Jackpot"), Bob, default);
        await shops.AddWinAsync(s.Id, new WinInput(monday, "MB", "DB"), Bob, default);
        (await shops.GetAsync(s.Id, null, false, default))!.WinCount.Should().Be(3);
    }

    [Fact(DisplayName = "Sua/xoa: chi nguoi tao hoac admin")]
    public async Task Edit_Permissions()
    {
        var (shops, _, _, db) = New();
        var s = await shops.CreateAsync(Input(), Owner, default);

        var alice = () => shops.UpdateAsync(s.Id, Input(name: "Đổi tên"), Alice, false, default);
        (await alice.Should().ThrowAsync<ShopException>()).Which.Status.Should().Be(403);
        (await shops.UpdateAsync(s.Id, Input(name: "Admin sửa"), Alice, true, default)).Name.Should().Be("Admin sửa");

        await shops.UpsertReviewAsync(s.Id, new ReviewInput(5, null), Alice, default);
        await shops.DeleteAsync(s.Id, Owner, false, default);
        (await db.Shops.CountAsync()).Should().Be(0);
        (await db.ShopReviews.CountAsync()).Should().Be(0);
    }

    [Fact(DisplayName = "Lich su sua: moi lan tao/sua ghi 1 ban, moi nhat truoc, ghi dung nguoi sua")]
    public async Task History_Revisions()
    {
        var (shops, _, _, _) = New();
        var s = await shops.CreateAsync(Input(), Owner, default);
        await shops.UpdateAsync(s.Id, Input(name: "Đổi tên lần 1"), Owner, false, default);
        await shops.UpdateAsync(s.Id, Input(name: "Admin sửa") with { Phone = null }, Alice, true, default);

        var h = (await shops.HistoryAsync(s.Id, default))!;
        h.Select(x => x.Action).Should().Equal(ShopRevisionAction.Updated, ShopRevisionAction.Updated, ShopRevisionAction.Created);
        h.Select(x => x.Username).Should().Equal("alice", "owner", "owner");
        h[0].Snapshot.Name.Should().Be("Admin sửa");
        h[2].Snapshot.Name.Should().Be("Đại lý Minh Ngọc");
        (await shops.HistoryAsync(Guid.NewGuid(), default)).Should().BeNull();
    }

    [Fact(DisplayName = "Xac nhan con ban: moi nguoi tinh 1 lan, bam lai ngay khong tang")]
    public async Task Confirm_Once()
    {
        var (shops, _, _, _) = New();
        var s = await shops.CreateAsync(Input(), Owner, default);
        (await shops.ConfirmAsync(s.Id, Alice, default)).ConfirmCount.Should().Be(1);
        (await shops.ConfirmAsync(s.Id, Alice, default)).ConfirmCount.Should().Be(1);
        (await shops.ConfirmAsync(s.Id, Bob, default)).ConfirmCount.Should().Be(2);
        (await shops.GetAsync(s.Id, Alice, false, default))!.ConfirmedRecently.Should().BeTrue();
    }

    [Fact(DisplayName = "Thong bao: danh gia / ve trung bao nguoi tao; tu danh gia khong bao; sua khong bao lai")]
    public async Task Notifications_ForOwner()
    {
        var (shops, notes, pusher, _) = New();
        var s = await shops.CreateAsync(Input(), Owner, default);

        var own = await shops.UpsertReviewAsync(s.Id, new ReviewInput(5, "Của tôi"), Owner, default);
        await notes.OnShopReviewAsync(own.Id, default);
        pusher.Sent.Should().BeEmpty();

        var r = await shops.UpsertReviewAsync(s.Id, new ReviewInput(4, "Bán vui vẻ"), Alice, default);
        await notes.OnShopReviewAsync(r.Id, default);
        await notes.OnShopReviewAsync(r.Id, default);   // gọi lại (sửa đánh giá) không báo lần 2
        var (_, win) = await shops.AddWinAsync(s.Id, new WinInput(new DateOnly(2026, 9, 28), "TPHCM", "G1"), Bob, default);
        await notes.OnShopWinAsync(win, default);

        pusher.Sent.Should().HaveCount(2).And.OnlyContain(x => x.UserId == Owner);
        pusher.Sent[0].N.Kind.Should().Be(NotificationKind.ShopReview);
        pusher.Sent[0].N.Stars.Should().Be(4);
        pusher.Sent[0].N.ShopPublicId.Should().Be(s.Id);
        pusher.Sent[1].N.Snippet.Should().Be("TPHCM|G1|2026-09-28");

        var list = await notes.ListAsync(Owner, default);
        list.Items.Select(x => x.Kind).Should().Equal(NotificationKind.ShopWin, NotificationKind.ShopReview);
        list.Unread.Should().Be(2);

        await shops.DeleteReviewAsync(r.Id, Alice, false, default);
        (await notes.ListAsync(Owner, default)).Unread.Should().Be(1);
    }
}
