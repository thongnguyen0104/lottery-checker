using FluentAssertions;
using LotteryChecker.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LotteryChecker.Tests;

// Cỡ ảnh FE gửi lên theo đường đọc chính. Sai ở đây: local nhận ảnh nhỏ → đọc sai mà vẫn qua
// validate; hoặc Gemini nhận ảnh to → chậm vô ích.
public class ScanUploadOptionsTests
{
    private static IConfiguration Config(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? []).Build();

    [Fact(DisplayName = "1. Local bật → 1600px q0.85 (giữ độ chính xác OCR cục bộ)")]
    public void Local_Defaults() =>
        ScanUploadOptions.From(Config(), localEnabled: true).Should().Be(new ScanUploadOptions(1600, 0.85));

    [Fact(DisplayName = "2. Local tắt (cloud đọc thẳng) → 1280px q0.7")]
    public void Cloud_Defaults() =>
        ScanUploadOptions.From(Config(), localEnabled: false).Should().Be(new ScanUploadOptions(1280, 0.70));

    [Fact(DisplayName = "3. Cấu hình ghi đè được từng chế độ, không lẫn sang chế độ kia")]
    public void Config_Overrides()
    {
        var config = Config(new()
        {
            ["Ocr:Upload:Cloud:MaxWidth"] = "1024",
            ["Ocr:Upload:Cloud:Quality"] = "0.6",
        });

        ScanUploadOptions.From(config, localEnabled: false).Should().Be(new ScanUploadOptions(1024, 0.6));
        ScanUploadOptions.From(config, localEnabled: true).Should().Be(new ScanUploadOptions(1600, 0.85));
    }

    [Fact(DisplayName = "4. Giá trị vô lý (quality kiểu phần trăm, width quá to/nhỏ) → kẹp về khoảng an toàn")]
    public void OutOfRange_IsClamped()
    {
        var config = Config(new()
        {
            ["Ocr:Upload:Cloud:MaxWidth"] = "4000",
            ["Ocr:Upload:Cloud:Quality"] = "70",
            ["Ocr:Upload:Local:MaxWidth"] = "100",
            ["Ocr:Upload:Local:Quality"] = "0.1",
        });

        ScanUploadOptions.From(config, localEnabled: false).Should().Be(new ScanUploadOptions(1600, 0.95));
        ScanUploadOptions.From(config, localEnabled: true).Should().Be(new ScanUploadOptions(480, 0.5));
    }
}
