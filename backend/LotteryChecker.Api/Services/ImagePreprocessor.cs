using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LotteryChecker.Api.Services;

public class ImagePreprocessor
{
    public byte[] Preprocess(Stream input)
    {
        using var image = Image.Load<Rgba32>(input);

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
}
