using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LotteryChecker.Api.Services;

public class ImagePreprocessor
{
    public byte[] Preprocess(Stream input)
    {
        using var image = Image.Load<Rgba32>(input);

        // Xoay ảnh theo EXIF orientation (ảnh chụp từ điện thoại thường bị xoay 90°)
        image.Mutate(x => x.AutoOrient());

        // Resize nếu quá to (giữ tỉ lệ, max 1600px chiều dài)
        if (image.Width > 1600)
        {
            var ratio = 1600f / image.Width;
            image.Mutate(x => x.Resize((int)(image.Width * ratio),
                                       (int)(image.Height * ratio)));
        }

        image.Mutate(x => x
            .Grayscale()
            .Contrast(1.3f)
            .BinaryThreshold(0.5f));

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Chuẩn bị ảnh cho cloud OCR: KHÔNG nhị phân hoá (OCR.space đọc ảnh xám/màu tốt hơn),
    /// chỉ xoay đúng chiều + thu nhỏ vừa phải rồi nén JPEG để &lt; 1MB (giới hạn gói free OCR.space).
    /// </summary>
    public byte[] PrepareForCloud(Stream input)
    {
        using var image = Image.Load<Rgba32>(input);
        image.Mutate(x => x.AutoOrient());

        if (image.Width > 1600)
        {
            var ratio = 1600f / image.Width;
            image.Mutate(x => x.Resize((int)(image.Width * ratio),
                                       (int)(image.Height * ratio)));
        }

        // Hạ chất lượng dần đến khi < 1MB (gói free OCR.space chặn file ≥ 1MB).
        foreach (var quality in new[] { 85, 70, 55, 40 })
        {
            using var ms = new MemoryStream();
            image.SaveAsJpeg(ms, new JpegEncoder { Quality = quality });
            if (ms.Length < 1_000_000 || quality == 40)
                return ms.ToArray();
        }

        // không bao giờ tới đây (vòng lặp luôn return ở quality==40)
        using var fallback = new MemoryStream();
        image.SaveAsJpeg(fallback, new JpegEncoder { Quality = 40 });
        return fallback.ToArray();
    }
}
