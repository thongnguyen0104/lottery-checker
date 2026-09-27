using System.Globalization;
using FluentAssertions;
using LotteryChecker.Api.Services;
using LotteryChecker.Api.Workers;
using Xunit;

namespace LotteryChecker.Tests;

// Scraper bỏ qua cả cột đài nếu ProvinceMatcher không nhận ra tên → mất data âm thầm
// (Bình Phước từng bị như vậy). Khoá lại tên đài đúng như xosodaiphat.com hiển thị.
public class ScheduleTests
{
    [Theory(DisplayName = "1. Tên cột đài trên xosodaiphat.com → đúng mã đài")]
    [InlineData("TPHCM", "TPHCM")]
    [InlineData("Đồng Tháp", "DongThap")]   [InlineData("Cà Mau", "CaMau")]
    [InlineData("Bến Tre", "BenTre")]       [InlineData("Vũng Tàu", "VungTau")]   [InlineData("Bạc Liêu", "BacLieu")]
    [InlineData("Đồng Nai", "DongNai")]     [InlineData("Cần Thơ", "CanTho")]     [InlineData("Sóc Trăng", "SocTrang")]
    [InlineData("Tây Ninh", "TayNinh")]     [InlineData("An Giang", "AnGiang")]   [InlineData("Bình Thuận", "BinhThuan")]
    [InlineData("Vĩnh Long", "VinhLong")]   [InlineData("Bình Dương", "BinhDuong")] [InlineData("Trà Vinh", "TraVinh")]
    [InlineData("Long An", "LongAn")]       [InlineData("Bình Phước", "BinhPhuoc")] [InlineData("Hậu Giang", "HauGiang")]
    [InlineData("Tiền Giang", "TienGiang")] [InlineData("Kiên Giang", "KienGiang")] [InlineData("Đà Lạt", "DaLat")]
    public void SiteHeader_MapsToCode(string header, string code)
    {
        new ProvinceMatcher().FindBestMatch(header).Should().Be(code);
    }

    [Fact(DisplayName = "2. Lịch xổ MN: thứ 7 có 4 đài gồm Bình Phước; mọi mã đài trong lịch đều hợp lệ")]
    public void Schedule_IsConsistent()
    {
        var saturday = new DateOnly(2026, 9, 26);
        DrawSchedule.MnProvincesOn(saturday).Should().Equal("TPHCM", "LongAn", "BinhPhuoc", "HauGiang");

        var week = Enumerable.Range(0, 7).Select(i => saturday.AddDays(i));
        week.SelectMany(DrawSchedule.MnProvincesOn).Should().OnlyContain(c => ProvinceMatcher.AllCodes.Contains(c));
    }

    // Worker cào kết quả: 16:45 lần đầu, thiếu đài thì 10' thử lại (chỉ hôm nay) tới 20:00,
    // đủ rồi / hết giờ thì chờ 16:45 hôm sau. Giờ ở đây là giờ VN.
    [Theory(DisplayName = "3. Lịch worker cào kết quả: 16:45, thử lại mỗi 10' tới 20:00")]
    [InlineData("2026-09-27 08:00", false, "2026-09-27 16:45", false)] // sáng: chờ tới giờ kết quả lên web
    [InlineData("2026-09-27 16:30", true,  "2026-09-27 16:45", false)] // "đủ" trước 16:45 không tính
    [InlineData("2026-09-27 16:45", false, "2026-09-27 16:55", true)]  // vừa cào mà web chưa đủ
    [InlineData("2026-09-27 19:50", false, "2026-09-27 20:00", true)]  // lần thử cuối đúng 20:00
    [InlineData("2026-09-27 19:55", false, "2026-09-28 16:45", false)] // thử nữa là quá 20:00 → thôi
    [InlineData("2026-09-27 17:05", true,  "2026-09-28 16:45", false)] // đủ rồi → hôm sau
    [InlineData("2026-09-27 23:30", false, "2026-09-28 16:45", false)] // khởi động khuya, thiếu → hôm sau cào bù
    public void Worker_NextRun(string now, bool todayComplete, string expectedAt, bool expectedRetry)
    {
        static DateTime At(string s) => DateTime.ParseExact(s, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        var (at, isRetry) = DailyResultFetchWorker.NextRun(At(now), todayComplete);

        at.Should().Be(At(expectedAt));
        isRetry.Should().Be(expectedRetry);
    }
}
