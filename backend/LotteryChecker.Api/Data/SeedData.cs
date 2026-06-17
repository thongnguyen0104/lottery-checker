using LotteryChecker.Api.Models;

namespace LotteryChecker.Api.Data;

// Seed 1 đài/ngày đầy đủ cơ cấu giải XSKT Miền Nam (18 số/đài/ngày) để test/demo ở dev.
// ĐB cố định = 123456, giải tám = 56 → vé 123456 trúng kép (ĐB + Giải Tám).
public static class SeedData
{
    public static async Task SeedIfEmptyAsync(AppDbContext db)
    {
        if (db.LotteryResults.Any()) return;

        var date = new DateOnly(2026, 6, 2);
        const string province = "TPHCM";
        var rng = new Random(42); // seed cố định để test ổn định

        var rows = new List<LotteryResult>();

        // Cơ cấu MN: (PrizeTier, số chữ số, số lượng, số đầu cố định để test)
        var schema = new (string Tier, int Digits, int Count, string? FixedFirst)[]
        {
            ("DB", 6, 1, "123456"),  // ĐB cố định để test trúng exact
            ("1",  5, 1, null),
            ("2",  5, 1, null),
            ("3",  5, 2, null),
            ("4",  5, 7, null),
            ("5",  4, 1, null),
            ("6",  4, 3, null),
            ("7",  3, 1, null),
            ("8",  2, 1, "56"),      // giải tám = "56" để test trúng kép với ĐB 123456
        };

        foreach (var (tier, digits, count, fixedFirst) in schema)
        {
            for (int i = 0; i < count; i++)
            {
                var number = i == 0 && fixedFirst != null
                    ? fixedFirst
                    : rng.Next(0, (int)Math.Pow(10, digits)).ToString().PadLeft(digits, '0');

                rows.Add(new LotteryResult
                {
                    DrawDate = date,
                    Region = "MN",
                    Province = province,
                    PrizeTier = tier,
                    Number = number
                });
            }
        }

        db.LotteryResults.AddRange(rows);
        await db.SaveChangesAsync();
    }
}
