using LotteryChecker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Data;

// SMOKE-TEST FIXTURE ONLY (dev). KHÔNG phải seed 1.152 dòng thật (task scraper/seed sau).
// Chỉ chạy khi Development + cờ config Seed:SmokeFixture=true + bảng đang rỗng.
public static class DevSmokeSeed
{
    public static async Task SeedSmokeFixtureIfEmptyAsync(AppDbContext db)
    {
        if (await db.LotteryResults.AnyAsync()) return;

        var d = new DateOnly(2026, 6, 2);
        db.LotteryResults.AddRange(
            new LotteryResult { DrawDate = d, Region = "MN", Province = "TPHCM", PrizeTier = "DB", Number = "123456" },
            new LotteryResult { DrawDate = d, Region = "MN", Province = "TPHCM", PrizeTier = "8",  Number = "56" },
            new LotteryResult { DrawDate = d, Region = "MN", Province = "TPHCM", PrizeTier = "7",  Number = "456" },
            new LotteryResult { DrawDate = d, Region = "MN", Province = "TPHCM", PrizeTier = "5",  Number = "3456" });
        await db.SaveChangesAsync();
    }
}
