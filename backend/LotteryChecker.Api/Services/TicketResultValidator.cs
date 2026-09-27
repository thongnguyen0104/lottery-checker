using LotteryChecker.Api.Models;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Quyết định kết quả OCR cục bộ đã đủ tin để trả luôn, hay phải gọi cloud OCR (chậm ~5s).
///
/// Vì sao không chỉ nhìn confidence: confidence là trung bình điểm MỌI ký tự trên vé (cả chữ
/// quảng cáo, mã vạch...), nên có thể cao mà số vé vẫn sai, hoặc thấp chỉ vì vài dòng chữ nhỏ
/// mờ trong khi số vé đọc rất rõ. Luật nghiệp vụ (số vé 6 chữ số không mơ hồ, ngày hợp lý,
/// đài có thật) bắt được lỗi mà confidence bỏ sót; confidence chỉ là chốt chặn thêm.
/// </summary>
public class TicketResultValidator
{
    private readonly TimeProvider _time;
    private readonly ValidationOptions _opt;

    public TicketResultValidator(TimeProvider time, IConfiguration config)
    {
        _time = time;
        _opt = config.GetSection("Ocr:Validation").Get<ValidationOptions>() ?? new ValidationOptions();
    }

    public TicketValidation Validate(TicketInfo info)
    {
        var reasons = new List<string>();

        // Số vé: đúng 6 chữ số (parser đã chỉ nhận \d{6}, kiểm lại cho chắc), không hoà phiếu,
        // không phải đoán bằng cách sửa O→0/I→1/S→5.
        var numberOk = true;
        if (info.TicketNumber is not { Length: 6 } n || !n.All(char.IsAsciiDigit))
        { numberOk = false; reasons.Add("number_missing"); }
        else if (info.TicketNumberAmbiguous)
        { numberOk = false; reasons.Add("number_ambiguous"); }
        else if (info.TicketNumberNormalized)
        { numberOk = false; reasons.Add("number_normalized"); }

        // Ngày: parse được (parser đã kiểm ngày/tháng/năm hợp lệ) và nằm trong khoảng hợp lý so
        // với hôm nay. Ngày lệch xa thường là OCR đọc nhầm (vd 2026 → 2028) hoặc dính số khác.
        var dateOk = true;
        if (info.DrawDate is not { } date)
        { dateOk = false; reasons.Add("date_missing"); }
        else if (!IsPlausibleDrawDate(date))
        { dateOk = false; reasons.Add("date_out_of_range"); }

        // Đài: phải khớp NGUYÊN VĂN một đài được hỗ trợ; khớp gần đúng dễ nhầm đài gần tên
        // (vd "binh dinh" ↔ "binh duong" chỉ lệch 2 ký tự).
        var provinceOk = true;
        if (info.Province == null)
        { provinceOk = false; reasons.Add("province_missing"); }
        else if (!info.ProvinceExact)
        { provinceOk = false; reasons.Add("province_fuzzy"); }
        else if (info.ProvinceAmbiguous)
        { provinceOk = false; reasons.Add("province_ambiguous"); }

        var confidenceOk = info.OcrConfidence >= _opt.MinConfidence;
        if (!confidenceOk) reasons.Add("low_confidence");

        return new TicketValidation(numberOk, dateOk, provinceOk, confidenceOk, reasons);
    }

    /// <summary>
    /// Vé chắc chắn đã hết hạn → gọi cloud (2–8s) cũng vô ích, kết quả cuối vẫn là "Vé hết hạn".
    /// Chỉ khi: lỗi DUY NHẤT là ngày ngoài khoảng, ngày ở QUÁ KHỨ và quá hạn lĩnh thưởng, và ngày
    /// đọc giống nhau ở ≥2 chỗ trên vé. Điều kiện cuối chặn trường hợp OCR đọc nhầm năm của vé
    /// còn hạn (đã gặp: 2026 → 2025 ở 1 chỗ) — khi đó vẫn gọi cloud như bình thường.
    /// </summary>
    public bool IsConfidentlyExpired(TicketInfo info, TicketValidation check) =>
        check.Reasons is ["date_out_of_range"]
        && info.DrawDate is { } date
        && info.DrawDateVotes >= 2
        && DrawSchedule.IsExpired(date, DrawSchedule.NowVn(_time));

    /// <summary>
    /// Có đáng gọi cloud OCR không. Chỉ khi SỐ VÉ có rủi ro (không hợp lệ, hoặc confidence thấp
    /// nên số vé cũng có thể sai): số vé là thứ khó sửa tay và gõ sai là dò sai. Đài/ngày chưa
    /// chắc thì user chọn lại trên form trong 2 giây — nhanh hơn chờ cloud (1–9s, hay lỗi/bị
    /// throttle ở gói free). Trên 8 vé thật, local đọc đúng số vé 8/8 → không vé nào phải gọi cloud.
    /// </summary>
    public static bool CloudCanHelp(TicketValidation check) => !check.NumberOk || !check.ConfidenceOk;

    /// <summary>
    /// Các trường của kết quả CUỐI (sau merge cloud nếu có) mà user nên kiểm tra lại trên form:
    /// "number" | "date" | "province". Ngày cũ ở quá khứ KHÔNG tính — FE đã báo "Vé hết hạn".
    /// </summary>
    public IReadOnlyList<string> FieldsToReview(TicketInfo info)
    {
        var check = Validate(info);
        var fields = new List<string>();
        if (!check.NumberOk) fields.Add("number");
        var oldPastDate = info.DrawDate is { } d && d < DateOnly.FromDateTime(DrawSchedule.NowVn(_time));
        if (!check.DateOk && !oldPastDate) fields.Add("date");
        if (!check.ProvinceOk) fields.Add("province");
        return fields;
    }

    /// <summary>
    /// Kết quả CUỐI đủ chắc để FE dò luôn, bỏ qua bước user xác nhận. Chặt hơn "needsReview rỗng":
    /// <list type="bullet">
    /// <item>Số vé hợp lệ và từ nguồn đáng tin: cloud đọc, hoặc OCR cục bộ qua cả ngưỡng confidence.
    /// needsReview không xét confidence — cloud lỗi/không trả số thì số vé confidence thấp vẫn "sạch".</item>
    /// <item>Đài khớp nguyên văn, không mơ hồ.</item>
    /// <item>Ngày trong khoảng hợp lý, hoặc cũ mà đọc khớp ở ≥2 chỗ (dò ra "Vé hết hạn"). Ngày cũ chỉ
    /// đọc được 1 chỗ có thể là nhầm năm của vé còn hạn — needsReview không đánh dấu nên phải chặn ở đây.</item>
    /// </list>
    /// </summary>
    /// <param name="localCheck">Kết quả validate OCR cục bộ TRƯỚC khi gộp cloud; null = OCR cục bộ không chạy.</param>
    public bool CanAutoCheck(TicketInfo info, TicketValidation? localCheck)
    {
        var check = Validate(info);
        var numberTrusted = info.TicketNumberFromCloud || localCheck is { NumberOk: true, ConfidenceOk: true };
        var dateSure = check.DateOk
            || (info.DrawDate is { } date && info.DrawDateVotes >= 2 && DrawSchedule.IsExpired(date, DrawSchedule.NowVn(_time)));
        return check.NumberOk && numberTrusted && check.ProvinceOk && dateSure;
    }

    /// <summary>Ngày mở thưởng nằm trong khoảng hợp lý quanh hôm nay (giờ VN).</summary>
    public bool IsPlausibleDrawDate(DateOnly date)
    {
        var today = DateOnly.FromDateTime(DrawSchedule.NowVn(_time));
        return date >= today.AddDays(-_opt.MaxDaysPast) && date <= today.AddDays(_opt.MaxDaysAhead);
    }

    /// <summary>
    /// Ngày CÓ THỂ là ngày xổ của vé đang cầm: không quá MaxDaysAhead ở tương lai (vé chỉ bán trước
    /// tối đa chừng đó). Ngày cũ vẫn tính là có thể — vé hết hạn là có thật. Ngày tương lai xa thì
    /// chắc chắn đọc nhầm (đã gặp: cắt dòng đọc lại năm 2026 → 2096).
    /// </summary>
    public bool IsPossibleDrawDate(DateOnly date) =>
        date <= DateOnly.FromDateTime(DrawSchedule.NowVn(_time)).AddDays(_opt.MaxDaysAhead);

    private sealed class ValidationOptions
    {
        /// <summary>Ngưỡng confidence (0..1) của engine cục bộ để được bỏ qua cloud.</summary>
        public double MinConfidence { get; set; } = 0.80;
        /// <summary>Vé mở thưởng quá bao nhiêu ngày trước thì coi là OCR đọc sai ngày.</summary>
        public int MaxDaysPast { get; set; } = 60;
        /// <summary>Vé bán trước ngày xổ tối đa bao nhiêu ngày.</summary>
        public int MaxDaysAhead { get; set; } = 14;
    }
}

/// <param name="Reasons">Mã lý do không qua (rỗng khi Passed) — trả về API để thống kê vì sao phải gọi cloud.</param>
public sealed record TicketValidation(
    bool NumberOk, bool DateOk, bool ProvinceOk, bool ConfidenceOk, IReadOnlyList<string> Reasons)
{
    public bool Passed => NumberOk && DateOk && ProvinceOk && ConfidenceOk;
}
