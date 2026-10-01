using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Caching.Memory;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Cấu hình mục "Storage" — bucket chứa ảnh Blog. Mặc định là Oracle Object Storage (Always Free 20GB)
/// qua API tương thích S3; đặt ServiceUrl thì trỏ được sang S3-compatible khác (vd. Cloudflare R2).
/// Secret KHÔNG để trong appsettings: dev dùng user-secrets, prod dùng env Storage__SecretKey.
/// </summary>
public class StorageOptions
{
    /// <summary>Object Storage Namespace của tenancy (Console → Tenancy details).</summary>
    public string Namespace { get; set; } = "";
    /// <summary>Home region, vd. ap-singapore-1.</summary>
    public string Region { get; set; } = "";
    public string Bucket { get; set; } = "";
    /// <summary>Customer Secret Key (Console → User → Customer secret keys).</summary>
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    /// <summary>Để trống = https://{Namespace}.compat.objectstorage.{Region}.oraclecloud.com</summary>
    public string ServiceUrl { get; set; } = "";
    /// <summary>RAM tối đa cho cache ảnh đã tải từ bucket.</summary>
    public int MaxCacheMb { get; set; } = 200;

    public string EffectiveServiceUrl => !string.IsNullOrWhiteSpace(ServiceUrl)
        ? ServiceUrl
        : $"https://{Namespace}.compat.objectstorage.{Region}.oraclecloud.com";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Bucket) && !string.IsNullOrWhiteSpace(AccessKey) && !string.IsNullOrWhiteSpace(SecretKey)
        && (!string.IsNullOrWhiteSpace(ServiceUrl) || (!string.IsNullOrWhiteSpace(Namespace) && !string.IsNullOrWhiteSpace(Region)));
}

/// <summary>Ảnh không hợp lệ (không phải ảnh / quá nhiều điểm ảnh) — controller trả 422.</summary>
public class InvalidBlogImageException(string message) : Exception(message);

/// <summary>
/// Kho ảnh Blog trên object storage — VM không ghi file nào xuống đĩa. Đọc ảnh cũng đi qua đây (có
/// cache RAM riêng) vì gói Always Free của Oracle chỉ ~50k request API/tháng: cho trình duyệt tải thẳng
/// từ bucket thì mỗi lượt xem tốn 1 request.
/// </summary>
public sealed class BlogImageStorage : IDisposable
{
    /// <summary>Cạnh dài tối đa lưu lại — đủ nét cho màn hình điện thoại / laptop.</summary>
    public const int MaxEdge = 1600;
    /// <summary>Chặn "bom giải nén": file vài trăm KB nhưng khai kích thước khổng lồ.</summary>
    public const long MaxPixels = 40_000_000;
    public const int WebpQuality = 80;
    public const string CacheControl = "public, max-age=31536000, immutable";

    private readonly StorageOptions _opt;
    private readonly AmazonS3Client? _s3;
    private readonly MemoryCache _cache;
    private readonly ILogger<BlogImageStorage> _log;

    public BlogImageStorage(IConfiguration config, ILogger<BlogImageStorage> log)
    {
        _log = log;
        _opt = config.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
        // Cache riêng (không dùng IMemoryCache chung của app): SizeLimit bắt MỌI entry khai Size.
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = Math.Max(1, _opt.MaxCacheMb) * 1024L * 1024 });
        if (!_opt.IsConfigured) return;

        _s3 = new AmazonS3Client(new BasicAWSCredentials(_opt.AccessKey, _opt.SecretKey), new AmazonS3Config
        {
            ServiceURL = _opt.EffectiveServiceUrl,
            AuthenticationRegion = string.IsNullOrWhiteSpace(_opt.Region) ? "auto" : _opt.Region,
            ForcePathStyle = true,
            // AWSSDK v4 mặc định gửi checksum CRC32 mà S3-compatible (Oracle, R2) có thể không nhận.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            Timeout = TimeSpan.FromSeconds(20),
        });
    }

    public bool Enabled => _s3 != null;

    /// <summary>
    /// Kiểm tra đúng là ảnh, xoay theo EXIF, bỏ hết metadata (EXIF có cả toạ độ GPS nơi chụp), thu cạnh
    /// dài về <see cref="MaxEdge"/>, mã hoá WebP. Ném <see cref="InvalidBlogImageException"/> khi không dùng được.
    /// </summary>
    public static byte[] Process(byte[] input)
    {
        Image decoded;
        try
        {
            // Đọc kích thước trước (không giải mã điểm ảnh) rồi mới Load.
            var info = Image.Identify(input);
            if ((long)info.Width * info.Height > MaxPixels) throw new InvalidBlogImageException("too many pixels");
            decoded = Image.Load(input);
        }
        catch (Exception e) when (e is ImageFormatException or ArgumentException)
        {
            throw new InvalidBlogImageException("not an image");
        }

        using var image = decoded;
        // Ảnh động (GIF/WebP nhiều khung) → giữ khung đầu.
        while (image.Frames.Count > 1) image.Frames.RemoveFrame(1);
        image.Mutate(x =>
        {
            x.AutoOrient();
            if (Math.Max(image.Width, image.Height) > MaxEdge)
                x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(MaxEdge, MaxEdge) });
        });
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.IccProfile = null;

        using var ms = new MemoryStream();
        image.Save(ms, new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = WebpQuality });
        return ms.ToArray();
    }

    public async Task PutAsync(string key, byte[] webp, CancellationToken ct)
    {
        using var body = new MemoryStream(webp, writable: false);
        var req = new PutObjectRequest
        {
            BucketName = _opt.Bucket, Key = key, InputStream = body, ContentType = "image/webp",
            AutoCloseStream = false,
            // Oracle không nhận kiểu gửi chunked "STREAMING-AWS4-HMAC-SHA256-PAYLOAD".
            UseChunkEncoding = false,
        };
        req.Headers.CacheControl = CacheControl;
        await S3.PutObjectAsync(req, ct);
        Cache(key, webp);
    }

    /// <summary>Bytes ảnh; null = không có trên bucket.</summary>
    public async Task<byte[]?> GetAsync(string key, CancellationToken ct)
    {
        if (_cache.TryGetValue(key, out byte[]? hit)) return hit;
        try
        {
            using var res = await S3.GetObjectAsync(_opt.Bucket, key, ct);
            using var ms = new MemoryStream();
            await res.ResponseStream.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            Cache(key, bytes);
            return bytes;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Xoá trên bucket (xoá object không tồn tại vẫn tính là xong).</summary>
    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        _cache.Remove(key);
        await S3.DeleteObjectAsync(_opt.Bucket, key, ct);
    }

    public void Evict(string key) => _cache.Remove(key);

    private void Cache(string key, byte[] bytes) =>
        _cache.Set(key, bytes, new MemoryCacheEntryOptions { Size = bytes.Length, SlidingExpiration = TimeSpan.FromDays(3) });

    private AmazonS3Client S3 => _s3 ?? throw new InvalidOperationException("Storage chưa cấu hình.");

    public void Dispose()
    {
        _s3?.Dispose();
        _cache.Dispose();
    }
}
