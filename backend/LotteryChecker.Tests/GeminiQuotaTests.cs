using LotteryChecker.Api.Services;
using Microsoft.Extensions.Configuration;

namespace LotteryChecker.Tests;

public class GeminiQuotaTests
{
    private static IConfiguration Config(int scan = 5, int dream = 5, int daily = 450) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:ScanPermitPerMinute"] = scan.ToString(),
            ["DreamChat:PermitPerMinute"] = dream.ToString(),
            ["Gemini:DailyPermitPerModel"] = daily.ToString(),
        }).Build();

    [Fact]
    public void Scan_And_Dream_Have_Separate_Minute_Limits()
    {
        using var quota = new GeminiQuota(Config(scan: 2, dream: 1));

        Assert.True(quota.TryAcquireScan("m"));
        Assert.True(quota.TryAcquireScan("m"));
        Assert.False(quota.TryAcquireScan("m"));   // hết lượt soi vé

        Assert.True(quota.TryAcquireDream("m"));   // luận số vẫn còn lượt riêng
        Assert.False(quota.TryAcquireDream("m"));
        Assert.True(quota.TryAcquireDream("backup"));   // model khác, quota khác
    }

    [Fact]
    public void Model_Cap_Is_Shared_By_Scan_And_Dream()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:ScanPermitPerMinute"] = "2",
            ["DreamChat:PermitPerMinute"] = "2",
            ["Gemini:PermitPerMinutePerModel"] = "3",
        }).Build();
        using var quota = new GeminiQuota(config);

        Assert.True(quota.TryAcquireScan("m"));
        Assert.True(quota.TryAcquireScan("m"));
        Assert.True(quota.TryAcquireDream("m"));
        Assert.False(quota.TryAcquireDream("m"));      // luận số còn lượt riêng nhưng model "m" chạm trần 3
        Assert.True(quota.TryAcquireDream("other"));   // model khác vẫn gọi được
    }

    [Fact]
    public void Daily_Limit_Is_Shared_Per_Model_And_Resets_On_Pacific_Day()
    {
        // 06:00 UTC = 23:00 hôm trước giờ Thái Bình Dương (PDT, UTC-7).
        var now = new DateTimeOffset(2026, 9, 28, 6, 0, 0, TimeSpan.Zero);
        using var quota = new GeminiQuota(Config(scan: 10, dream: 10, daily: 2), () => now);

        Assert.True(quota.TryAcquireScan("m"));
        Assert.True(quota.TryAcquireDream("m"));   // soi vé + luận số cộng chung ngày của model "m"
        Assert.False(quota.TryAcquireDream("m"));
        Assert.True(quota.TryAcquireDream("backup"));

        now = now.AddHours(1);                     // 00:00 giờ Thái Bình Dương → ngày mới
        Assert.True(quota.TryAcquireDream("m"));
    }
}
