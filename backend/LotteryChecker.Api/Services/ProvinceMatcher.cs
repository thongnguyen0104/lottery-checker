using System.Globalization;
using System.Text;

namespace LotteryChecker.Api.Services;

public class ProvinceMatcher
{
    private static readonly Dictionary<string, string> Provinces = new()
    {
        // Miền Nam (21 tỉnh, áp dụng cơ cấu giải chung)
        {"tphcm", "TPHCM"}, {"tp hcm", "TPHCM"}, {"ho chi minh", "TPHCM"},
        {"dong thap", "DongThap"}, {"ca mau", "CaMau"}, {"ben tre", "BenTre"},
        {"vung tau", "VungTau"}, {"bac lieu", "BacLieu"}, {"dong nai", "DongNai"},
        {"can tho", "CanTho"}, {"soc trang", "SocTrang"}, {"tay ninh", "TayNinh"},
        {"an giang", "AnGiang"}, {"binh thuan", "BinhThuan"}, {"vinh long", "VinhLong"},
        {"binh duong", "BinhDuong"}, {"tra vinh", "TraVinh"}, {"long an", "LongAn"},
        {"hau giang", "HauGiang"}, {"kien giang", "KienGiang"}, {"tien giang", "TienGiang"},
        {"da lat", "DaLat"}, {"lam dong", "LamDong"},
        // Miền Trung
        {"phu yen", "PhuYen"}, {"hue", "Hue"}, {"thua thien hue", "Hue"},
        {"dak lak", "DakLak"}, {"daklak", "DakLak"}, {"quang nam", "QuangNam"},
        {"khanh hoa", "KhanhHoa"}, {"da nang", "DaNang"}, {"binh dinh", "BinhDinh"},
        {"quang tri", "QuangTri"}, {"quang binh", "QuangBinh"}, {"gia lai", "GiaLai"},
        {"ninh thuan", "NinhThuan"}, {"kon tum", "KonTum"}, {"quang ngai", "QuangNgai"},
        // Miền Bắc (chỉ 1 đài chung) — KHÔNG dùng cơ cấu MN, xem §11
        {"mien bac", "MB"}, {"mb", "MB"}, {"ha noi", "MB"}, {"hanoi", "MB"},
    };

    public static IReadOnlyCollection<string> AllCodes => Provinces.Values.Distinct().ToArray();

    public string? FindBestMatch(string ocrText)
    {
        var normalized = RemoveDiacritics(ocrText).ToLowerInvariant();
        foreach (var (key, code) in Provinces)
        {
            if (normalized.Contains(key)) return code;
        }

        // Fallback: thử fuzzy với từng tỉnh (Levenshtein ≤ 2)
        foreach (var (key, code) in Provinces)
        {
            foreach (var word in normalized.Split(new[] { ' ', '\n', '\t' },
                                                  StringSplitOptions.RemoveEmptyEntries))
            {
                if (Levenshtein(word, key) <= 2 && key.Length >= 5) return code;
            }
        }
        return null;
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Replace('đ', 'd').Replace('Đ', 'D')
                 .Normalize(NormalizationForm.FormC);
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                                   d[i - 1, j - 1] + cost);
            }
        return d[a.Length, b.Length];
    }
}
