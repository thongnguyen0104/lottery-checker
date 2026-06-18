using FluentAssertions;
using LotteryChecker.Api.Services;
using Xunit;

namespace LotteryChecker.Tests;

public class TicketNumberPickerTests
{
    [Fact(DisplayName = "1. Chọn số OCR đọc ra nhiều nhất; số tròn bị loại")]
    public void MostFrequent_Wins_RoundFiltered()
    {
        OcrService.PickTicketNumber(new[] { "288921", "288921", "400000" })
            .Should().Be("288921");
    }

    [Fact(DisplayName = "2. Bản đọc trội nhất thắng dù có bản đọc lệch khác")]
    public void MostFrequent_AmongVaried()
    {
        OcrService.PickTicketNumber(new[] { "200921", "200921", "288921" })
            .Should().Be("200921");
    }

    [Theory(DisplayName = "3. Số tròn (mệnh giá/giá tiền) → null")]
    [InlineData("400000")]
    [InlineData("100000")]
    public void RoundNumbers_AreRejected(string n)
    {
        OcrService.PickTicketNumber(new[] { n }).Should().BeNull();
    }

    [Fact(DisplayName = "4. Số vé hợp lệ bắt đầu 20xxxx KHÔNG bị loại (regression)")]
    public void Prefix20_IsKept()
    {
        OcrService.PickTicketNumber(new[] { "205432" }).Should().Be("205432");
    }

    [Fact(DisplayName = "5. Không có ứng viên → null")]
    public void Empty_ReturnsNull()
    {
        OcrService.PickTicketNumber(System.Array.Empty<string>()).Should().BeNull();
    }

    [Fact(DisplayName = "6. Một ứng viên hợp lệ duy nhất → trả nó")]
    public void Single_Candidate_Returned()
    {
        OcrService.PickTicketNumber(new[] { "873265" }).Should().Be("873265");
    }

    [Fact(DisplayName = "7. ReadTicketNumber: lấy số 6 chữ số dù dính hậu tố chữ 'D'")]
    public void ReadTicketNumber_HandlesLetterSuffix()
    {
        OcrService.ReadTicketNumber("Soan tin: XSBD\n288921 D\nGiai dac biet")
            .Should().Be("288921");
    }

    [Fact(DisplayName = "8. ReadTicketNumber: trích số vé từ text cloud OCR thật")]
    public void ReadTicketNumber_FromRealCloudText()
    {
        const string cloud = "XỔ SỐ KIẾN THIẾT BÌNH DƯƠNG\nSoạn tin: XSBD gửi đến 997\n288921 D\nJ5-6-204\n6-2026";
        OcrService.ReadTicketNumber(cloud).Should().Be("288921");
    }

    [Fact(DisplayName = "9. ReadTicketNumber: không có số 6 chữ số → null")]
    public void ReadTicketNumber_NoSixDigits_ReturnsNull()
    {
        OcrService.ReadTicketNumber("khong co so ve hop le 123 4567").Should().BeNull();
    }
}
