using LotteryChecker.Api.Data;
using LotteryChecker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Services;

public class LotteryMatcher
{
    private readonly AppDbContext _db;

    public LotteryMatcher(AppDbContext db) => _db = db;

    public async Task<ScanResult> Match(string ticket, DateOnly date, string province, CancellationToken ct)
    {
        var results = await _db.LotteryResults
            .Where(r => r.DrawDate == date && r.Province == province)
            .ToListAsync(ct);

        if (results.Count == 0)
            return new ScanResult
            {
                ExtractedNumber = ticket,
                DrawDate = date,
                Province = province,
                IsWinner = false
            };

        var winnings = new List<WinningPrize>();

        // 1. Giải ĐB (exact 6-digit) — và 2 giải phụ suy ra từ ĐB
        var db = results.FirstOrDefault(r => r.PrizeTier == "DB");
        if (db is { Number.Length: 6 } && ticket.Length == 6)
        {
            if (db.Number == ticket)
            {
                winnings.Add(new WinningPrize("Giải Đặc Biệt", 2_000_000_000m));
                // Trúng ĐB rồi thì KHÔNG xét Phụ ĐB / Khuyến khích nữa
            }
            else if (ticket[1..] == db.Number[1..] && ticket[0] != db.Number[0])
            {
                winnings.Add(new WinningPrize("Giải Phụ Đặc Biệt", 50_000_000m));
            }
            else if (ticket[0] == db.Number[0])
            {
                int diffCount = 0;
                for (int i = 1; i < 6; i++)
                    if (ticket[i] != db.Number[i]) diffCount++;
                if (diffCount == 1)
                    winnings.Add(new WinningPrize("Giải Khuyến Khích", 6_000_000m));
            }
        }

        // 2. Giải 1–8: so N chữ số cuối với mỗi số trúng. KHÔNG return — collect đủ.
        foreach (var r in results.Where(x => x.PrizeTier != "DB"))
        {
            if (r.Number.Length > ticket.Length) continue;
            var lastN = ticket[^r.Number.Length..];
            if (lastN == r.Number)
            {
                winnings.Add(new WinningPrize(GetTierName(r.PrizeTier),
                                              GetPrizeAmount(r.PrizeTier)));
            }
        }

        return new ScanResult
        {
            ExtractedNumber = ticket,
            DrawDate = date,
            Province = province,
            IsWinner = winnings.Count > 0,
            Winnings = winnings,
            TotalPrize = winnings.Sum(w => w.Amount)
        };
    }

    private static string GetTierName(string tier) => tier switch
    {
        "1" => "Giải Nhất", "2" => "Giải Nhì", "3" => "Giải Ba", "4" => "Giải Tư",
        "5" => "Giải Năm", "6" => "Giải Sáu", "7" => "Giải Bảy", "8" => "Giải Tám",
        _   => $"Giải {tier}"
    };

    private static decimal GetPrizeAmount(string tier) => tier switch
    {
        "1" => 30_000_000m, "2" => 15_000_000m, "3" => 10_000_000m, "4" => 3_000_000m,
        "5" => 1_000_000m,  "6" => 400_000m,    "7" => 200_000m,    "8" => 100_000m,
        _   => 0m
    };
}
