using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LotteryChecker.Api.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LotteryChecker.Api.Services;

/// <summary>Cách đọc lại khi lượt OCR chính thiếu/sai ngày hoặc đài — chọn bằng <c>Ocr:RetryStrategy</c>.</summary>
public enum RetryStrategy
{
    /// <summary>Đọc lại CẢ ảnh bằng kiểu lọc khác (~0,5–0,7s trên máy dev).</summary>
    Full,
    /// <summary>
    /// Chỉ cắt các dòng lượt chính nghi là ngày / tên đài, nới khung rồi đọc riêng từng dòng.
    /// Không lấp đủ thì mới đọc lại cả ảnh như <see cref="Full"/>.
    /// </summary>
    Lines,
}

/// <param name="Filled">Trường đã lấp: "date" | "province".</param>
/// <param name="CroppedLines">Số dòng đã cắt ra đọc lại (0 = không dùng cách cắt dòng).</param>
/// <param name="UsedFull">Có phải đọc lại cả ảnh không.</param>
public sealed record RetryOutcome(IReadOnlyList<string> Filled, int CroppedLines, bool UsedFull);

/// <summary>
/// Đọc lại để lấp ngày/đài mà lượt OCR chính đọc thiếu/sai. Luôn chỉ LẤP, không đè — luật gộp ở
/// <see cref="TicketTextParser.FillMissingFromRetry"/>.
///
/// Vì sao có cách cắt dòng: lượt chính đã tìm ra khung từng dòng chữ, và lỗi hay gặp là khung
/// hơi hẹp làm cụt ký tự cuối (ngày vé Vĩnh Long "25-09-202"). Đọc lại đúng dòng đó với khung
/// nới rộng rẻ hơn nhiều so với chạy lại model dò vùng chữ + nhận dạng ~16 dòng trên cả ảnh.
/// </summary>
public sealed class LocalRetryReader
{
    // Dòng có dạng "dd-MM-..." (cả khi năm bị cụt) — ứng viên cho ngày mở thưởng.
    private static readonly Regex DateLike = new(@"\d{1,2}\s*[-/.]\s*\d{1,2}\s*[-/.]", RegexOptions.Compiled);

    private readonly TicketTextParser _parser;
    private readonly ProvinceMatcher _provinces;
    private readonly TicketResultValidator _validator;

    public LocalRetryReader(TicketTextParser parser, ProvinceMatcher provinces,
                            TicketResultValidator validator, IConfiguration config)
    {
        _parser = parser;
        _provinces = provinces;
        _validator = validator;
        // Mặc định Lines: trên 8 vé thật chính xác y Full (8/8 đủ trường, pass 7/8, không vé nào qua
        // mà sai) nhưng phần đọc lại chỉ ~+275ms thay vì ~+555ms. Đo lại: ocr-benchmark?grid=false.
        Strategy = config.GetValue<RetryStrategy?>("Ocr:RetryStrategy") ?? RetryStrategy.Lines;
    }

    public RetryStrategy Strategy { get; }

    /// <summary>Lượt chính có trường nào đọc lại được không (số vé thì không — đã có cloud lo).</summary>
    public static bool Needed(TicketValidation check) => !check.DateOk || !check.ProvinceOk;

    /// <summary>
    /// Phóng to dòng cắt ra bao nhiêu lần trước khi đọc. Đo trên 8 vé thật: ×1 chỉ cứu được ngày bị
    /// cụt (Vĩnh Long); ×2 cứu thêm tên đài chữ cách điệu (TPHCM "NÓ CHÍ MINN" → khớp nguyên văn,
    /// Bình Phước) — bằng đúng số vé đọc lại cả ảnh cứu được, mà chỉ ~+150ms thay vì ~+540ms.
    /// </summary>
    private const int LineScale = 2;

    /// <summary>
    /// Đọc lại theo <paramref name="strategy"/> (mặc định <see cref="Strategy"/> trong cấu hình).
    /// <paramref name="mode"/> = kiểu lọc ảnh cho lượt đọc lại.
    /// </summary>
    public RetryOutcome Run(TicketInfo primary, TicketValidation check, PreparedTicketImage prepared,
                            LocalPreprocess mode, Func<Image<Rgba32>, TicketInfo> extract,
                            RetryStrategy? strategy = null)
    {
        if ((strategy ?? Strategy) == RetryStrategy.Full)
            return new RetryOutcome(RetryFull(primary, check, prepared, mode, extract), 0, true);

        var (filled, cropped) = RetryLines(primary, check, prepared, mode, extract, LineScale);
        var after = filled.Count > 0 ? _validator.Validate(primary) : check;
        if (!Needed(after)) return new RetryOutcome(filled, cropped, false);

        var more = RetryFull(primary, after, prepared, mode, extract);
        return new RetryOutcome([.. filled, .. more], cropped, true);
    }

    /// <summary>Đọc lại cả ảnh bằng kiểu lọc <paramref name="mode"/>.</summary>
    public IReadOnlyList<string> RetryFull(TicketInfo primary, TicketValidation check, PreparedTicketImage prepared,
                                           LocalPreprocess mode, Func<Image<Rgba32>, TicketInfo> extract)
    {
        using var image = prepared.CreateFiltered(mode);
        return TicketTextParser.FillMissingFromRetry(primary, extract(image), check,
                                                   _validator.IsPlausibleDrawDate, _validator.IsPossibleDrawDate);
    }

    /// <summary>
    /// Chỉ đọc lại các dòng nghi ngờ của lượt chính. CroppedLines = 0 khi engine không trả khung
    /// dòng (Tesseract) hoặc không có dòng nào đáng nghi.
    /// </summary>
    public (IReadOnlyList<string> Filled, int CroppedLines) RetryLines(
        TicketInfo primary, TicketValidation check, PreparedTicketImage prepared,
        LocalPreprocess mode, Func<Image<Rgba32>, TicketInfo> extract, int scale = 1)
    {
        if (primary.Lines is not { Count: > 0 } lines) return ([], 0);

        var picked = lines
            .Where(l => (!check.DateOk && DateLike.IsMatch(l.Text))
                        || (!check.ProvinceOk && LooksLikeProvinceLine(l.Text)))
            .ToList();
        if (picked.Count == 0) return ([], 0);

        var texts = new List<string>(picked.Count);
        foreach (var line in picked)
        {
            using var crop = prepared.CropFiltered(Widen(line), mode);
            if (scale > 1) crop.Mutate(x => x.Resize(crop.Width * scale, crop.Height * scale));
            texts.Add(extract(crop).RawText ?? string.Empty);
        }

        var retry = _parser.Parse(string.Join('\n', texts), primary.OcrConfidence);
        return (TicketTextParser.FillMissingFromRetry(primary, retry, check,
                                                    _validator.IsPlausibleDrawDate, _validator.IsPossibleDrawDate),
                picked.Count);
    }

    // Nới ngang nhiều (ký tự cuối dòng hay bị cụt), dọc nửa chiều cao dòng.
    private static Rectangle Widen(OcrLine l)
    {
        var padX = Math.Max(l.Height, l.Width / 5);
        var padY = l.Height / 2;
        return new Rectangle(l.X - padX, l.Y - padY, l.Width + 2 * padX, l.Height + 2 * padY);
    }

    // Dòng tiêu đề "XỔ SỐ KIẾN THIẾT <ĐÀI>" hoặc dòng tự nó khớp được một đài.
    private bool LooksLikeProvinceLine(string text) =>
        Fold(text).Contains("KIEN THIET", StringComparison.Ordinal) || _provinces.Match(text) != null;

    private static string Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToUpperInvariant(c == 'đ' || c == 'Đ' ? 'D' : c));
        return sb.ToString();
    }
}
