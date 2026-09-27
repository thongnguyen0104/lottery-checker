using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LotteryChecker.Api.Services;

/// <summary>Tiền xử lý ảnh trước khi đưa cho OCR cục bộ — chọn bằng <c>Ocr:LocalPreprocess</c>.</summary>
public enum LocalPreprocess
{
    /// <summary>A. Ảnh màu đã xoay + thu nhỏ, không lọc gì thêm.</summary>
    Original,
    /// <summary>B. Ảnh xám.</summary>
    Grayscale,
    /// <summary>C. Ảnh xám + tăng tương phản.</summary>
    Contrast,
    /// <summary>D. Ảnh xám + tương phản + nhị phân hoá (2 màu) — kiểu cũ, vốn chỉnh cho Tesseract.</summary>
    Binarize,
}

public class ImagePreprocessor
{
    private const int MaxWidth = 1600;

    /// <summary>Gói free OCR.space chặn file ≥ 1MB.</summary>
    private const int CloudMaxBytes = 1_000_000;

    public ImagePreprocessor(IConfiguration config)
    {
        // Mặc định Original: đo trên 8 vé thật (backend/TestData), PP-OCRv5 đọc đúng số vé 8/8 với
        // ảnh màu nhưng chỉ 5/8 với ảnh nhị phân hoá (kiểu cũ, vốn chỉnh cho Tesseract) — nhị phân
        // hoá làm vỡ nét số in cách điệu trên nền hoa văn. Đo lại bằng /api/admin/ocr-benchmark.
        LocalMode = config.GetValue<LocalPreprocess?>("Ocr:LocalPreprocess") ?? LocalPreprocess.Original;

        // Mặc định Contrast: trên 8 vé thật, Original đọc đủ 3 trường 6/8, Contrast chỉ 5/8 nhưng
        // lấp đúng 2 vé Original thiếu ngày (vd Vĩnh Long "25-09-202") → gộp lại 8/8. Rỗng/"None" = tắt.
        var retry = config["Ocr:RetryPreprocess"];
        RetryMode = retry is null ? LocalPreprocess.Contrast
            : Enum.TryParse<LocalPreprocess>(retry, ignoreCase: true, out var mode) && mode != LocalMode ? mode
            : null;
    }

    /// <summary>Kiểu tiền xử lý cho OCR cục bộ trong luồng /api/scan.</summary>
    public LocalPreprocess LocalMode { get; }

    /// <summary>
    /// Kiểu tiền xử lý cho lượt đọc lại khi lượt chính thiếu/sai ngày hoặc đài (null = không đọc lại).
    /// Chỉ để LẤP trường thiếu — xem <see cref="TicketTextParser.FillMissingFromRetry"/>.
    /// </summary>
    public LocalPreprocess? RetryMode { get; }

    /// <summary>
    /// Giải mã ảnh upload MỘT lần, xoay theo EXIF, thu nhỏ nếu rộng hơn 1600px. Ảnh cho cloud
    /// OCR KHÔNG làm ở đây — chỉ dựng khi OCR cục bộ không qua validate
    /// (<see cref="PreparedTicketImage.EncodeForCloud"/>), để vé đọc tốt khỏi tốn công nén JPEG.
    /// </summary>
    public PreparedTicketImage Load(Stream input)
    {
        var image = Image.Load<Rgba32>(input);
        try
        {
            var resized = Normalize(image);
            return new PreparedTicketImage(image, resized);
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Ảnh upload gửi NGUYÊN cho cloud được không, khỏi giải mã + nén lại: đúng thứ FE gửi — JPEG
    /// &lt;1MB, rộng ≤1600px, đã xoay đúng chiều (canvas đã áp EXIF) — thì giải mã rồi nén lại chỉ
    /// tốn CPU mà ảnh không tốt hơn. Chỉ đọc header (vài ms). Ảnh khác (PNG/HEIC, quá to, còn cờ
    /// xoay EXIF — vd gọi API trực tiếp bằng ảnh gốc điện thoại) thì false → đi đường Load thường.
    /// </summary>
    public static bool CanSendAsIs(byte[] image)
    {
        if (image.Length >= CloudMaxBytes) return false;
        try
        {
            var info = Image.Identify(image);
            if (info.Metadata.DecodedImageFormat is not JpegFormat || info.Width > MaxWidth) return false;
            var exif = info.Metadata.ExifProfile;
            return exif == null || !exif.TryGetValue(ExifTag.Orientation, out var o) || o.Value is 0 or 1;
        }
        catch (Exception)
        {
            return false;   // header hỏng → để Load báo lỗi như mọi ảnh khác
        }
    }

    /// <summary>Ảnh 2 màu dạng PNG — dùng cho endpoint debug Tesseract.</summary>
    public byte[] Preprocess(Stream input)
    {
        using var prepared = Load(input);
        return EncodePng(prepared.GetLocal(LocalPreprocess.Binarize));
    }

    /// <summary>Ảnh JPEG &lt;1MB cho cloud OCR — dùng cho endpoint debug.</summary>
    public byte[] PrepareForCloud(Stream input)
    {
        using var prepared = Load(input);
        return prepared.EncodeForCloud();
    }

    /// <summary>
    /// Xoay theo EXIF (ảnh chụp từ điện thoại thường bị xoay 90°) + thu nhỏ nếu quá to.
    /// FE đã thu về ≤1600px nên thường KHÔNG resize ở đây — chỉ là chốt an toàn cho client cũ
    /// hoặc gọi API trực tiếp. Trả true nếu có resize.
    /// </summary>
    private static bool Normalize(Image<Rgba32> image)
    {
        image.Mutate(x => x.AutoOrient());
        if (image.Width <= MaxWidth) return false;

        var ratio = (float)MaxWidth / image.Width;
        image.Mutate(x => x.Resize(MaxWidth, (int)(image.Height * ratio)));
        return true;
    }

    /// <summary>PNG nén nhanh: ảnh này chỉ sống vài trăm ms trong RAM, tốn CPU nén kỹ là phí.</summary>
    internal static byte[] EncodePng(Image<Rgba32> image)
    {
        using var ms = new MemoryStream();
        image.SaveAsPng(ms, new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed });
        return ms.ToArray();
    }

    /// <summary>
    /// JPEG &lt;1MB cho OCR.space. Ảnh ≤1600px ở chất lượng 85 thường chỉ vài trăm KB → 1 lần
    /// encode là xong. Chỉ khi vượt 1MB mới tìm nhị phân chất lượng (tối đa 3 lần encode nữa)
    /// thay vì dò tuần tự 85→70→55→40.
    /// </summary>
    public static byte[] EncodeJpegUnderLimit(Image<Rgba32> image, int maxBytes = CloudMaxBytes)
    {
        const int preferred = 85, floor = 40;

        var first = EncodeJpeg(image, preferred);
        if (first.Length < maxBytes) return first;

        byte[]? best = null;
        int lo = floor, hi = preferred - 1;
        for (var i = 0; i < 3 && lo <= hi; i++)
        {
            var q = (lo + hi) / 2;
            var bytes = EncodeJpeg(image, q);
            if (bytes.Length < maxBytes) { best = bytes; lo = q + 1; }  // vừa → thử nét hơn
            else hi = q - 1;                                           // vẫn to → giảm nữa
        }
        return best ?? EncodeJpeg(image, floor);
    }

    private static byte[] EncodeJpeg(Image<Rgba32> image, int quality)
    {
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms, new JpegEncoder { Quality = quality });
        return ms.ToArray();
    }
}

/// <summary>
/// Ảnh vé đã giải mã + xoay + thu nhỏ, giữ trong RAM suốt request để dựng các phiên bản khác
/// (cho OCR cục bộ, cho cloud) mà không giải mã lại. Ảnh gốc không bao giờ bị sửa tại chỗ.
/// </summary>
public sealed class PreparedTicketImage : IDisposable
{
    private readonly Image<Rgba32> _normalized;
    private Image<Rgba32>? _local;
    private LocalPreprocess? _localMode;

    internal PreparedTicketImage(Image<Rgba32> normalized, bool resized)
    {
        _normalized = normalized;
        Resized = resized;
    }

    /// <summary>Server có phải thu nhỏ không (false = FE đã gửi ảnh ≤1600px).</summary>
    public bool Resized { get; }
    public int Width => _normalized.Width;
    public int Height => _normalized.Height;

    /// <summary>
    /// Ảnh cho OCR cục bộ theo kiểu tiền xử lý. Original trả thẳng ảnh đã chuẩn hoá (không
    /// clone); các kiểu khác lọc trên bản clone để ảnh gốc còn nguyên cho cloud. Thuộc sở hữu
    /// của object này — người gọi KHÔNG dispose.
    /// </summary>
    public Image<Rgba32> GetLocal(LocalPreprocess mode)
    {
        if (mode == LocalPreprocess.Original) return _normalized;
        if (_local != null && _localMode == mode) return _local;

        _local?.Dispose();
        _local = CreateFiltered(mode);
        _localMode = mode;
        return _local;
    }

    /// <summary>
    /// Luôn lọc ra bản MỚI (không cache, kể cả Original cũng clone) — người gọi phải dispose.
    /// Dùng cho benchmark để mỗi lượt đo đúng chi phí tiền xử lý của một request.
    /// </summary>
    public Image<Rgba32> CreateFiltered(LocalPreprocess mode) =>
        mode == LocalPreprocess.Original ? _normalized.Clone() : _normalized.Clone(x => Filter(x, mode));

    /// <summary>
    /// Cắt một vùng (đã kẹp trong mép ảnh) rồi lọc theo <paramref name="mode"/> — ảnh MỚI, người
    /// gọi phải dispose. Dùng để đọc lại riêng một dòng chữ nghi ngờ (LocalRetryReader).
    /// </summary>
    public Image<Rgba32> CropFiltered(Rectangle area, LocalPreprocess mode)
    {
        var r = Rectangle.Intersect(area, new Rectangle(0, 0, Width, Height));
        return _normalized.Clone(x => Filter(x.Crop(r), mode));
    }

    private static void Filter(IImageProcessingContext x, LocalPreprocess mode)
    {
        if (mode == LocalPreprocess.Original) return;
        x.Grayscale();
        if (mode >= LocalPreprocess.Contrast) x.Contrast(1.3f);
        if (mode == LocalPreprocess.Binarize) x.BinaryThreshold(0.5f);
    }

    /// <summary>
    /// JPEG &lt;1MB cho cloud OCR, từ ảnh màu chưa lọc (OCR.space đọc ảnh màu/xám tốt hơn ảnh 2 màu).
    /// </summary>
    public byte[] EncodeForCloud() => ImagePreprocessor.EncodeJpegUnderLimit(_normalized);

    public void Dispose()
    {
        _local?.Dispose();
        _normalized.Dispose();
    }
}
