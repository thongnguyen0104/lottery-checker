using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LotteryChecker.Api.Services;

/// <param name="Ambiguous">Có 2+ đài khác nhau khớp ngang điểm — Code chỉ là đoán, cần kiểm tra lại.</param>
public sealed record ProvinceMatch(string Code, bool Exact, bool Ambiguous = false);

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
        {"da lat", "DaLat"}, {"lam dong", "LamDong"}, {"binh phuoc", "BinhPhuoc"},
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

    public string? FindBestMatch(string ocrText) => Match(ocrText)?.Code;

    /// <summary>
    /// Như <see cref="FindBestMatch"/> nhưng cho biết khớp kiểu gì. <c>Exact</c> = tên đài có
    /// nguyên văn trong text; false = chỉ khớp gần đúng (Levenshtein ≤ 2) — đủ để điền sẵn cho
    /// user sửa, nhưng chưa đủ chắc để bỏ qua cloud OCR (xem <see cref="TicketResultValidator"/>).
    /// </summary>
    public ProvinceMatch? Match(string ocrText)
    {
        // Gộp mọi khoảng trắng/xuống dòng thành 1 dấu cách: OCR hay tách tên đài ra 2 dòng
        // ("BINH\nDUONG"), nếu không gộp thì "binh duong" không bao giờ khớp.
        var normalized = Whitespace.Replace(RemoveDiacritics(ocrText).ToLowerInvariant(), " ");

        var exact = BestExactMatch(normalized);
        if (exact != null) return exact;

        return BestFuzzyMatch(normalized);
    }

    /// <summary>
    /// Khớp gần đúng khi OCR đọc lệch vài ký tự ("dong thiap" → DongThap). So CỤM có đúng số từ
    /// như tên đài (tên 2 từ so với cặp từ liền nhau), ngưỡng chặt theo độ dài: ≤1 ký tự với tên
    /// &lt; 8 ký tự, ≤2 với tên dài hơn. Bản cũ so TỪNG TỪ với ngưỡng 2 → từ "hai" (số đọc bằng
    /// chữ trên vé) khớp "hanoi" và vé Đồng Tháp thành MB.
    /// </summary>
    private static ProvinceMatch? BestFuzzyMatch(string normalized)
    {
        var words = NonLetters.Split(PrinterSegment.Replace(normalized, " "))
            .Where(w => w.Length > 0).ToArray();

        string? bestCode = null;
        var bestDist = int.MaxValue;
        var tie = false;
        foreach (var (key, code) in Provinces)
        {
            if (key.Length < 5) continue;   // tên quá ngắn ("mb", "hue") → gần đúng là đoán bừa
            var n = key.Count(c => c == ' ') + 1;
            var maxDist = key.Length >= 8 ? 2 : 1;

            for (var i = 0; i + n <= words.Length; i++)
            {
                var window = string.Join(' ', words, i, n);
                if (Math.Abs(window.Length - key.Length) > maxDist) continue;
                var d = Levenshtein(window, key);
                if (d > maxDist || d > bestDist) continue;
                if (d < bestDist) { bestDist = d; bestCode = code; tie = false; }
                else if (code != bestCode) tie = true;
            }
        }
        return bestCode == null ? null : new ProvinceMatch(bestCode, Exact: false, Ambiguous: tie);
    }

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    // Một key = một regex có ranh giới từ: tránh "mb" khớp giữa "number", "hue" khớp giữa "thue".
    private static readonly (Regex Pattern, string Code)[] ExactPatterns = Provinces
        .Select(p => (new Regex($@"\b{Regex.Escape(p.Key)}\b", RegexOptions.Compiled), p.Value))
        .ToArray();

    // Vé in tên đài ngay sau "xổ số kiến thiết" — tín hiệu mạnh nhất cho biết đây là đài phát hành.
    private static readonly Regex IssuerContext = new(@"(kien thiet|xo so)\W{0,3}$", RegexOptions.Compiled);

    // Dòng nhà in ("In tại CTY CP In Tổng hợp Cần Thơ", "XN In Tài Chính TP.HCM") nêu tên tỉnh
    // KHÔNG phải đài phát hành — vé Vĩnh Long/Hậu Giang in ở Cần Thơ từng bị nhận nhầm thành CanTho.
    private static readonly Regex PrinterContext =
        new(@"\b(in tai|in tong hop|co phan in|xn in|cty in)\b.{0,40}$", RegexOptions.Compiled);

    // Cả đoạn dòng nhà in — bị xoá hẳn trước khi khớp gần đúng.
    private static readonly Regex PrinterSegment =
        new(@"\b(in tai|in tong hop|co phan in|xn in|cty in)\b.{0,40}", RegexOptions.Compiled);

    private static readonly Regex NonLetters = new(@"[^a-z]+", RegexOptions.Compiled);

    /// <summary>
    /// Chấm điểm mọi đài khớp nguyên văn thay vì lấy đài đầu tiên theo thứ tự từ điển:
    /// +1 mỗi lần xuất hiện, +3 nếu đứng ngay sau "kiến thiết"/"xổ số", bỏ qua lần xuất hiện
    /// nằm trong dòng nhà in. Hai đài khác nhau cùng điểm cao nhất → Ambiguous (không đoán).
    /// </summary>
    private static ProvinceMatch? BestExactMatch(string normalized)
    {
        var scores = new Dictionary<string, int>();
        foreach (var (pattern, code) in ExactPatterns)
        {
            foreach (System.Text.RegularExpressions.Match m in pattern.Matches(normalized))
            {
                var before = normalized[Math.Max(0, m.Index - 60)..m.Index];
                if (PrinterContext.IsMatch(before)) continue;
                var score = 1 + (IssuerContext.IsMatch(before) ? 3 : 0);
                scores[code] = scores.GetValueOrDefault(code) + score;
            }
        }
        if (scores.Count == 0) return null;

        var ranked = scores.OrderByDescending(s => s.Value).ToArray();
        var ambiguous = ranked.Length > 1 && ranked[0].Value == ranked[1].Value;
        return new ProvinceMatch(ranked[0].Key, Exact: true, Ambiguous: ambiguous);
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
