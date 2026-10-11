using FluentAssertions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LotteryChecker.Tests;

// Cờ tính năng: mặc định tắt, admin luôn xem trước được, bật/tắt có hiệu lực ngay (không đợi cache).
public class FeatureFlagsTests
{
    private static FeatureFlags New() => new(
        new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
        new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);

    [Fact(DisplayName = "Chua co dong nao = theo DefaultOn (chi Blog bat); admin van dung duoc")]
    public async Task DefaultOff_AdminPreview()
    {
        var flags = New();
        (await flags.IsEnabledAsync(FeatureFlags.ScratchTickets, default)).Should().BeFalse();
        (await flags.IsEnabledAsync(FeatureFlags.Blog, default)).Should().BeTrue();
        (await flags.AvailableAsync(false, default)).Where(x => x.Value).Select(x => x.Key)
            .Should().BeEquivalentTo([FeatureFlags.Blog]);
        (await flags.AvailableAsync(true, default)).Values.Should().AllBeEquivalentTo(true);
        (await flags.AvailableAsync(false, default)).Keys.Should().BeEquivalentTo(FeatureFlags.All.Select(d => d.Key));
        (await flags.ListAsync(false, default)).Single(f => f.Key == FeatureFlags.Blog).Enabled.Should().BeTrue();
    }

    [Fact(DisplayName = "Co mac dinh bat van tat duoc, roi bat lai")]
    public async Task DefaultOn_CanBeTurnedOff()
    {
        var flags = New();
        await flags.SetAsync(FeatureFlags.Blog, false, "superadmin", default);
        (await flags.IsEnabledAsync(FeatureFlags.Blog, default)).Should().BeFalse();
        (await flags.AvailableAsync(false, default))[FeatureFlags.Blog].Should().BeFalse();
        (await flags.AvailableAsync(true, default))[FeatureFlags.Blog].Should().BeTrue();

        await flags.SetAsync(FeatureFlags.Blog, true, "superadmin", default);
        (await flags.IsEnabledAsync(FeatureFlags.Blog, default)).Should().BeTrue();
    }

    [Fact(DisplayName = "Bat/tat co hieu luc ngay du da doc qua cache; ghi nguoi doi")]
    public async Task Set_TakesEffectImmediately()
    {
        var flags = New();
        (await flags.IsEnabledAsync(FeatureFlags.CheckHistory, default)).Should().BeFalse();   // nạp cache

        (await flags.SetAsync(FeatureFlags.CheckHistory, true, "superadmin", default)).Should().BeTrue();
        (await flags.IsEnabledAsync(FeatureFlags.CheckHistory, default)).Should().BeTrue();
        (await flags.IsEnabledAsync(FeatureFlags.ScratchTickets, default)).Should().BeFalse();
        (await flags.ListAsync(false, default)).Single(f => f.Key == FeatureFlags.CheckHistory)
            .Should().Match<FeatureFlags.FlagDto>(f => f.Enabled && f.UpdatedBy == "superadmin" && f.UpdatedAt != null);

        await flags.SetAsync(FeatureFlags.CheckHistory, false, "superadmin", default);
        (await flags.IsEnabledAsync(FeatureFlags.CheckHistory, default)).Should().BeFalse();
    }

    [Fact(DisplayName = "Key la khong bat duoc")]
    public async Task Set_UnknownKey() =>
        (await New().SetAsync("hackFeature", true, "x", default)).Should().BeFalse();
}
