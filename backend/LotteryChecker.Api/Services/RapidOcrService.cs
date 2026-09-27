using System.Runtime.InteropServices;
using LotteryChecker.Api.Models;
using RapidOcrNet;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Engine OCR cục bộ dùng PP-OCRv5 mobile (latin) chạy qua ONNX Runtime.
///
/// Vì sao có thêm engine này bên cạnh Tesseract: đo trên ảnh vé mẫu thì nó đọc ĐÚNG dãy 6 chữ
/// số cách điệu mà Tesseract đọc sai, và nhanh hơn ~4,5 lần (≈0,9s so với ≈4,1s). Model chỉ
/// ~13MB, không cần mạng, có native cho linux-arm64 nên chạy được trên VM prod.
///
/// Bộ từ điển latin không có dấu tiếng Việt, nên tên đài đọc ra là chữ không dấu — không sao,
/// <see cref="ProvinceMatcher"/> vốn đã bỏ dấu trước khi so khớp.
/// </summary>
public sealed class RapidOcrService : ITicketOcrEngine, IDisposable
{
    private readonly RapidOcr _ocr = new();
    private readonly TicketTextParser _parser;

    public RapidOcrService(TicketTextParser parser, IConfiguration config,
                           ILogger<RapidOcrService> log)
    {
        _parser = parser;

        // Model nằm cạnh binary (package tự copy vào models/v5 khi build lẫn publish). Phải
        // dùng AppContext.BaseDirectory chứ không để đường dẫn tương đối: systemd chạy service
        // với thư mục làm việc khác, đường dẫn tương đối sẽ trỏ trật.
        var dir = Path.Combine(AppContext.BaseDirectory, RapidOcr.ModelsFolderName, RapidOcr.ModelsVersion);
        var numThread = config.GetValue<int?>("Ocr:Onnx:NumThread") ?? 0;   // 0 = để ONNX tự chọn

        _ocr.InitModels(
            Path.Combine(dir, RapidOcr.DefaultDetModelPath),
            Path.Combine(dir, RapidOcr.DefaultClsModelPath),
            Path.Combine(dir, RapidOcr.DefaultRecModelPath),
            Path.Combine(dir, RapidOcr.DefaultKeysFilePath),
            numThread);

        _options = BuildOptions(config);

        // In đủ options (record tự ToString mọi thuộc tính): nhìn log là biết server đang chạy
        // cấu hình OCR nào, khỏi đoán khi so tốc độ giữa các máy.
        log.LogInformation("OCR ONNX (PP-OCRv5) đã nạp model từ {Dir}. Options: {Options}", dir, _options);
    }

    private readonly RapidOcrOptions _options;

    /// <summary>Options đang dùng (sau khi áp cấu hình) — để benchmark báo lại / làm gốc thử biến thể.</summary>
    public RapidOcrOptions Options => _options;

    /// <summary>DoAngle đang dùng (sau khi áp cấu hình) — để benchmark báo lại.</summary>
    public bool DoAngle => _options.DoAngle;

    /// <summary>
    /// Mặc định của thư viện + các ghi đè trong <c>Ocr:Onnx</c>. Ô nào để trống thì giữ mặc định.
    /// </summary>
    private static RapidOcrOptions BuildOptions(IConfiguration config)
    {
        var o = RapidOcrOptions.Default;

        // DoAngle = chạy thêm model phân loại chiều cho TỪNG dòng chữ (xử lý dòng lộn ngược).
        // Ảnh đã AutoOrient theo EXIF thì dòng hiếm khi ngược → tắt được để bớt 1 model/dòng.
        if (config.GetValue<bool?>("Ocr:Onnx:DoAngle") is { } doAngle)
            o = o with { DoAngle = doAngle };

        // ImgResize = cạnh ảnh được thu về trước khi đưa vào model DÒ vùng chữ (mặc định thư viện
        // 1024). Đây mới là núm quyết định tốc độ bước dò — MaxSideLen đo ra không ảnh hưởng.
        // Đo trên 8 vé thật (máy dev): 1024 → 712ms, 768 → 528ms; số vé đúng 8/8 ở cả hai,
        // số vé qua thẳng validate như nhau (5/8), không vé nào qua validate mà sai.
        if (config.GetValue<int?>("Ocr:Onnx:ImgResize") is { } imgResize and > 0)
            o = o with { ImgResize = imgResize };

        // Số dòng chữ nhận dạng song song. Mặc định thư viện = 1 (lần lượt từng dòng, ~19 dòng/vé);
        // để trống/0 = tự chọn theo số lõi, tối đa 4 (đo: 4 luồng cắt phần nhận dạng ~350 → ~225ms;
        // hơn 4 không nhanh thêm vì ONNX vốn đã tự chia luồng bên trong mỗi lượt chạy).
        // UnClipRatio = nới khung chữ bao nhiêu sau khi model dò ra (mặc định thư viện 1.6). Khung
        // hẹp quá thì cụt ký tự cuối dòng — đã gặp: ngày vé Vĩnh Long đọc thành "25-09-202".
        if (config.GetValue<float?>("Ocr:Onnx:UnClipRatio") is { } unclip and > 0)
            o = o with { UnClipRatio = unclip };

        var recPar = config.GetValue<int?>("Ocr:Onnx:RecParallelism") ?? 0;
        o = o with { RecMaxDegreeOfParallelism = recPar > 0 ? recPar : Math.Clamp(Environment.ProcessorCount, 1, 4) };

        return o;
    }

    public string Name => "onnx-ppocrv5";

    public TicketInfo Extract(byte[] imageBytes)
    {
        using var bitmap = SKBitmap.Decode(imageBytes)
            ?? throw new InvalidOperationException("Không giải mã được ảnh cho OCR ONNX.");
        return Detect(bitmap, _options);
    }

    public TicketInfo Extract(Image<Rgba32> image) => Extract(image, _options.DoAngle);

    /// <summary>
    /// Chép pixel thẳng từ ImageSharp sang SKBitmap — thay cho encode PNG (ImageSharp) rồi
    /// decode lại (Skia), vốn tốn hàng trăm ms trên ảnh 1600px mà không thêm được gì.
    /// Dùng Bgra8888/Opaque cho giống hệt bitmap mà SKBitmap.Decode trả cho ảnh JPEG.
    /// <paramref name="doAngle"/> truyền riêng để endpoint benchmark so được bật/tắt.
    /// </summary>
    public TicketInfo Extract(Image<Rgba32> image, bool doAngle) =>
        Extract(image, doAngle == _options.DoAngle ? _options : _options with { DoAngle = doAngle });

    /// <summary>Đọc với options tuỳ ý — cho endpoint dò cấu hình (ocr-tuning).</summary>
    public TicketInfo Extract(Image<Rgba32> image, RapidOcrOptions options)
    {
        using var bitmap = new SKBitmap(
            new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var rowBytes = bitmap.RowBytes;
        var width = image.Width;

        image.ProcessPixelRows(rows =>
        {
            var pixels = bitmap.GetPixelSpan();   // Span không được capture vào lambda → lấy ở trong
            for (var y = 0; y < rows.Height; y++)
            {
                var dst = MemoryMarshal.Cast<byte, Bgra32>(pixels.Slice(y * rowBytes, width * 4));
                PixelOperations<Rgba32>.Instance.ToBgra32(Configuration.Default, rows.GetRowSpan(y), dst);
            }
        });

        return Detect(bitmap, options);
    }

    private TicketInfo Detect(SKBitmap bitmap, RapidOcrOptions options)
    {
        var result = _ocr.Detect(bitmap, options);

        // Độ tin cậy: trung bình điểm từng ký tự trên MỌI dòng đọc được. Cùng thang 0..1 với
        // GetMeanConfidence của Tesseract nên ngưỡng lowConfidence (0,55) dùng chung được.
        var scores = result.TextBlocks
            .Where(b => b.CharScores is { Length: > 0 })
            .SelectMany(b => b.CharScores!)
            .ToArray();
        var confidence = scores.Length > 0 ? scores.Average() : 0;

        var info = _parser.Parse(result.StrRes ?? string.Empty, confidence);
        // DbNetTime = model dò vùng chữ; phần còn lại của DetectTime = cắt + (xoay) + nhận dạng các
        // dòng (thời gian thực, đã tính song song). Đơn vị ms.
        info.EngineTimings = new()
        {
            ["ocrDetect"] = Math.Round(result.DbNetTime, 1),
            ["ocrRecognize"] = Math.Round(Math.Max(0, result.DetectTime - result.DbNetTime), 1),
            ["ocrLines"] = result.TextBlocks.Length,
        };
        info.Lines = result.TextBlocks
            .Where(b => b.BoxPoints is { Length: > 0 })
            .Select(b =>
            {
                int x0 = b.BoxPoints.Min(p => p.X), y0 = b.BoxPoints.Min(p => p.Y);
                return new OcrLine(b.Text ?? string.Empty, x0, y0,
                                   b.BoxPoints.Max(p => p.X) - x0, b.BoxPoints.Max(p => p.Y) - y0);
            })
            .ToArray();
        return info;
    }

    public void Dispose() => _ocr.Dispose();
}
