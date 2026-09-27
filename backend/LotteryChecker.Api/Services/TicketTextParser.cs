using System.Text.RegularExpressions;
using LotteryChecker.Api.Models;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Biến text OCR thô thành <see cref="TicketInfo"/> (số vé, ngày, đài).
///
/// Vì sao tách khỏi engine OCR: phần "đọc chữ" có nhiều lựa chọn (Tesseract, ONNX PP-OCR,
/// cloud OCR), còn phần "hiểu tờ vé" thì chỉ có một và phải giống hệt nhau ở mọi engine —
/// nếu mỗi engine tự chọn số vé theo cách riêng thì đổi engine là đổi luôn kết quả, không
/// còn so sánh được engine nào đọc tốt hơn.
/// </summary>
public class TicketTextParser
{
    private readonly ProvinceMatcher _provinces;

    public TicketTextParser(ProvinceMatcher provinces) => _provinces = provinces;

    /// <summary>Trích số vé/ngày/đài từ text OCR.</summary>
    public TicketInfo Parse(string text, double confidence)
    {
        var number = AnalyzeTicketNumber(text);
        var province = _provinces.Match(text);
        var (date, dateVotes) = AnalyzeDate(text);
        return new TicketInfo
        {
            RawText = text,
            TicketNumber = number.Number,
            TicketNumberAmbiguous = number.Ambiguous,
            TicketNumberNormalized = number.Normalized,
            DrawDate = date,
            DrawDateVotes = dateVotes,
            Province = province?.Code,
            ProvinceExact = province?.Exact ?? false,
            ProvinceAmbiguous = province?.Ambiguous ?? false,
            OcrConfidence = confidence
        };
    }

    public readonly record struct TicketNumberReading(string? Number, bool Ambiguous, bool Normalized);

    /// <summary>
    /// Chọn số vé như <see cref="ReadTicketNumber"/>, kèm hai tín hiệu độ chắc chắn:
    /// hoà phiếu giữa các bản đọc, và số vé phải nhờ sửa ký tự nhầm mới có.
    /// </summary>
    public static TicketNumberReading AnalyzeTicketNumber(string text)
    {
        var clean = SixDigitCandidates(text).ToList();
        var number = PickTicketNumber(clean);
        if (number != null)
            return new TicketNumberReading(number, IsTie(clean), Normalized: false);

        // Chỉ khi KHÔNG có bản đọc sạch nào mới thử sửa ký tự nhầm — và đánh dấu Normalized để
        // validator không coi đó là kết quả chắc chắn.
        var fixedUp = NormalizedCandidates(text).ToList();
        var fromFixed = PickTicketNumber(fixedUp);
        return new TicketNumberReading(fromFixed, fromFixed != null && IsTie(fixedUp), fromFixed != null);
    }

    // Ký tự OCR hay đọc nhầm với chữ số trên font số in đậm.
    private static readonly Dictionary<char, char> DigitLookalikes = new()
    {
        ['O'] = '0', ['o'] = '0', ['I'] = '1', ['l'] = '1', ['|'] = '1', ['S'] = '5',
    };

    /// <summary>
    /// Token 6 ký tự đứng riêng (không dính chữ/số khác) gồm chữ số + ký tự nhầm, trong đó ít
    /// nhất 4 ký tự đã là chữ số — tức gần như chắc chắn là một dãy số bị đọc nhầm vài ký tự,
    /// chứ không phải một từ. Chặt hơn <see cref="SixDigitCandidates"/> (vốn cho phép dính chữ,
    /// vd "288921D") vì ở đây ta đang ĐOÁN, không được biến chữ thường thành số.
    /// </summary>
    public static IEnumerable<string> NormalizedCandidates(string text) =>
        Regex.Matches(text, @"(?<![\p{L}\p{N}])[0-9OoIlS|]{6}(?![\p{L}\p{N}])")
            .Select(m => m.Value)
            .Where(t => t.Count(char.IsDigit) >= 4 && t.Any(c => !char.IsDigit(c)))
            .Select(t => new string(t.Select(c => DigitLookalikes.GetValueOrDefault(c, c)).ToArray()));

    // Top 2 bản đọc (sau lọc số tròn) cùng số phiếu → không đủ căn cứ chọn.
    private static bool IsTie(IEnumerable<string> candidates)
    {
        var counts = candidates
            .Where(c => c.Length == 6 && c.All(char.IsDigit) && !c.EndsWith("0000"))
            .GroupBy(x => x).Select(g => g.Count())
            .OrderByDescending(n => n).Take(2).ToArray();
        return counts.Length == 2 && counts[0] == counts[1];
    }

    /// <summary>
    /// Hợp nhất kết quả OCR cục bộ với text từ cloud OCR (chỉ chạy khi local KHÔNG qua validate).
    ///
    /// Số vé: lấy của cloud khi <paramref name="preferCloudNumber"/> — tức số vé local không hợp lệ
    /// hoặc độ tin cậy local thấp. Nếu local chỉ hỏng ở đài/ngày mà số vé vẫn chắc, giữ số local:
    /// PP-OCRv5 đọc đúng số cách điệu, đè bằng cloud chỉ thêm rủi ro. Đài/ngày: vá chỗ local
    /// trống hoặc chỉ khớp gần đúng.
    /// </summary>
    /// <param name="replaceDateIf">
    /// Ngày local bị validator bác (vd lệch quá xa hôm nay) → cho ngày cloud thay, nhưng CHỈ khi
    /// ngày cloud thoả điều kiện này. Không có điều kiện thì một vé cũ thật (ngày local đúng mà
    /// ngoài khoảng) sẽ bị ngày cloud đọc sai đè lên — đã xảy ra với vé thật: 05-6 → 04-6.
    /// </param>
    public TicketInfo MergeFromCloudText(TicketInfo local, string cloudText,
                                         bool preferCloudNumber = true,
                                         Func<DateOnly, bool>? replaceDateIf = null)
    {
        local.CloudText = cloudText;

        var cloudNumber = ReadTicketNumber(cloudText);
        if (cloudNumber != null && (preferCloudNumber || local.TicketNumber == null))
        {
            local.TicketNumber = cloudNumber;
            local.TicketNumberFromCloud = true;
            local.TicketNumberAmbiguous = false;
            local.TicketNumberNormalized = false;
        }

        if (local.Province == null || !local.ProvinceExact || local.ProvinceAmbiguous)
        {
            var cloudProvince = _provinces.Match(cloudText);
            // Chỉ thay khi cloud CHẮC hơn (khớp nguyên văn, không mơ hồ); còn lại chỉ lấy khi local trống.
            var cloudIsSure = cloudProvince is { Exact: true, Ambiguous: false };
            if (cloudProvince != null && (cloudIsSure || local.Province == null))
            {
                local.Province = cloudProvince.Code;
                local.ProvinceExact = cloudProvince.Exact;
                local.ProvinceAmbiguous = cloudProvince.Ambiguous;
            }
        }

        var (cloudDate, cloudVotes) = AnalyzeDate(cloudText);
        if (cloudDate is { } cd && (local.DrawDate == null || replaceDateIf?.Invoke(cd) == true))
        {
            local.DrawDate = cd;
            local.DrawDateVotes = cloudVotes;
        }
        return local;
    }

    /// <summary>
    /// Lấp ngày/đài mà lượt OCR chính đọc thiếu/sai bằng lượt đọc lại trên ảnh lọc khác (vd tăng
    /// tương phản). Mỗi kiểu lọc hỏng ở một vé khác nhau — Contrast đọc đúng ngày vé Vĩnh Long mà
    /// Original cụt năm, nhưng lại đọc năm 2026 → 2028 ở vé Bình Dương mà Original đọc đúng — nên
    /// CHỈ lấp trường validator đã bác (<paramref name="check"/>), không bao giờ đè trường đã đúng.
    /// Số vé + confidence giữ nguyên lượt chính (số vé có rủi ro thì đã có cloud lo).
    /// Trả về các trường đã lấp: "date" | "province".
    /// </summary>
    /// <param name="isPlausibleDate">
    /// Lượt chính CÓ ngày mà ngoài khoảng → chỉ thay khi ngày lượt lại hợp lý; không thì một vé cũ
    /// thật (ngày đúng mà ngoài khoảng) sẽ bị ngày đọc sai đè lên. Cùng lý do với MergeFromCloudText.
    /// </param>
    /// <param name="isPossibleDate">
    /// Lượt chính KHÔNG có ngày → nhận cả ngày ngoài khoảng (vé cũ thật → báo hết hạn), nhưng
    /// không nhận ngày không thể có (tương lai xa): điền nó vào thì lượt đọc sau không đè được nữa.
    /// </param>
    public static IReadOnlyList<string> FillMissingFromRetry(TicketInfo primary, TicketInfo retry,
                                                             TicketValidation check,
                                                             Func<DateOnly, bool> isPlausibleDate,
                                                             Func<DateOnly, bool> isPossibleDate)
    {
        var filled = new List<string>();

        if (!check.DateOk && retry.DrawDate is { } rd
            && (isPlausibleDate(rd) || (primary.DrawDate == null && isPossibleDate(rd))))
        {
            primary.DrawDate = rd;
            primary.DrawDateVotes = retry.DrawDateVotes;
            filled.Add("date");
        }

        // Như cloud: chỉ thay khi lượt lại CHẮC hơn (khớp nguyên văn, không mơ hồ); còn lại chỉ lấy khi trống.
        var retrySure = retry is { Province: not null, ProvinceExact: true, ProvinceAmbiguous: false };
        if (!check.ProvinceOk && retry.Province != null && (retrySure || primary.Province == null))
        {
            primary.Province = retry.Province;
            primary.ProvinceExact = retry.ProvinceExact;
            primary.ProvinceAmbiguous = retry.ProvinceAmbiguous;
            filled.Add("province");
        }

        // Chỉ để debug (benchmark includeText): thấy được cả chữ của lượt đọc lại.
        primary.RawText = $"{primary.RawText}\n--- đọc lại ---\n{retry.RawText}";
        return filled;
    }

    /// <summary>Trích số vé 6 chữ số tốt nhất từ một đoạn text OCR.</summary>
    public static string? ReadTicketNumber(string text) =>
        PickTicketNumber(SixDigitCandidates(text));

    // 6 chữ số, KHÔNG bị bao bởi chữ số khác (nhưng cho phép kề chữ cái, vd "288921D").
    public static IEnumerable<string> SixDigitCandidates(string text) =>
        Regex.Matches(text, @"(?<!\d)\d{6}(?!\d)").Select(m => m.Value);

    /// <summary>Chọn số vé 6 chữ số tốt nhất từ các ứng viên OCR (tần suất + voting theo vị trí).</summary>
    public static string? PickTicketNumber(IEnumerable<string> candidates)
    {
        var clean = candidates
            .Where(c => c.Length == 6 && c.All(char.IsDigit))
            .Where(c => !c.EndsWith("0000")) // loại số tròn (mệnh giá/giá tiền); GIỮ số bắt đầu 19/20 vì vé hợp lệ
            .ToList();
        if (clean.Count == 0) return null;
        // Số vé in lặp nhiều lần trên vé → chọn số 6 chữ số OCR đọc ra NHIỀU NHẤT.
        // (Không ghép/vote theo vị trí: trên vé font cách điệu dễ "đoán bừa" ra số sai mà vẫn tự tin.)
        return clean.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
    }

    // Ngày: 28-05-2026, 28/05/2026, 28.05.2026, "ngày 28 tháng 5 năm 2026"
    public static DateOnly? ExtractDate(string text) => AnalyzeDate(text).Date;

    // Nới separator: OCR có thể chèn khoảng trắng/xuống dòng giữa các phần (vd "05-6\n\n2026"),
    // hoặc đọc "-" thành ":" (vé Bình Dương thật: "Mở ngày 25:9-2026").
    private static readonly Regex FullDate = new(@"(\d{1,2})[-/.:\s]{1,3}(\d{1,2})[-/.:\s]{1,5}(\d{4})");

    // Mảnh "tháng-năm" còn sót lại (vé Bình Dương thật: ngày đầy đủ "05-6-2026" + mảnh "6-2026").
    private static readonly Regex MonthYear = new(@"(?<!\d)(\d{1,2})[-/.:\s]{1,3}(\d{4})(?!\d)");

    /// <summary>
    /// Như <see cref="ExtractDate"/>, kèm số phiếu xác nhận THÁNG + NĂM của ngày đó (số chỗ trên vé
    /// đọc ra cùng tháng/năm). Vé in ngày 2–3 lần, nên ≥2 phiếu là bằng chứng OCR không đọc nhầm
    /// năm (lỗi đã gặp: 2026 → 2025 ở đúng 1 chỗ) — đủ cho quyết định "vé đã hết hạn".
    /// </summary>
    public static (DateOnly? Date, int Votes) AnalyzeDate(string text)
    {
        // Vé in ngày nhiều chỗ → lấy ngày HỢP LỆ xuất hiện nhiều nhất (hoà thì lấy cái gặp trước):
        // một chỗ đọc lệch (2026 → 2025) không át được các chỗ đọc đúng.
        var dates = new List<DateOnly>();
        foreach (Match m in FullDate.Matches(text))
            if (TryBuildDate(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, out var d1))
                dates.Add(d1);
        if (dates.Count > 0)
        {
            var top = dates.GroupBy(d => d).OrderByDescending(g => g.Count()).First().Key;

            // Phiếu = mọi lần đọc ra CÙNG tháng + năm: ngày đầy đủ (kể cả lệch ngày — mảnh "6-2026"
            // dính số rác phía trước thành "23\n6-2026" vẫn xác nhận đúng tháng/năm) cộng mảnh
            // tháng-năm lẻ. Xoá ngày đầy đủ trước khi đếm mảnh — không thì "05-6-2026" tự xác nhận
            // chính nó qua "6-2026" nằm bên trong.
            var sameMonthYear = dates.Count(d => d.Month == top.Month && d.Year == top.Year);
            var partials = MonthYear.Matches(FullDate.Replace(text, " ")).Count(m =>
                int.TryParse(m.Groups[1].Value, out var mm) && mm == top.Month
                && m.Groups[2].Value == top.Year.ToString());

            return (top, sameMonthYear + partials);
        }

        var m2 = Regex.Match(text,
            @"ng[àa]y\s*(\d{1,2}).*?th[áa]ng\s*(\d{1,2}).*?n[ăa]m\s*(\d{4})",
            RegexOptions.IgnoreCase);
        if (m2.Success && TryBuildDate(m2.Groups[1].Value, m2.Groups[2].Value,
                                       m2.Groups[3].Value, out var d2))
            return (d2, 1);

        return (null, 0);
    }

    private static bool TryBuildDate(string d, string m, string y, out DateOnly result)
    {
        result = default;
        if (int.TryParse(d, out var dd) && int.TryParse(m, out var mm)
            && int.TryParse(y, out var yy)
            && dd is >= 1 and <= 31 && mm is >= 1 and <= 12
            && yy is >= 2020 and <= 2099)
        {
            try { result = new DateOnly(yy, mm, dd); return true; }
            catch { return false; }
        }
        return false;
    }
}
