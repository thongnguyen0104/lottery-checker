using LotteryChecker.Api.Models;

namespace LotteryChecker.Api.Services;

/// <summary>Ảnh Blog: xử lý + đẩy lên bucket + ghi DB; xoá trên bucket trước rồi mới xoá dòng DB.</summary>
public class BlogImageService(BlogImageStorage storage, BlogService blog, TimeProvider clock, ILogger<BlogImageService> log)
{
    public bool Enabled => storage.Enabled;

    /// <summary>Ném <see cref="InvalidBlogImageException"/> khi không phải ảnh dùng được.</summary>
    public async Task<BlogService.ImageDto> UploadAsync(byte[] input, string ownerKey, CancellationToken ct)
    {
        // ImageSharp giải mã/mã hoá đồng bộ, tốn CPU — đẩy ra thread pool cho khỏi chiếm luồng request.
        var webp = await Task.Run(() => BlogImageStorage.Process(input), ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var key = $"blog/{now:yyyy}/{now:MM}/{Guid.NewGuid():D}.webp";
        await storage.PutAsync(key, webp, ct);
        return await blog.AddImageAsync(key, ownerKey, webp.Length, ct);
    }

    /// <summary>
    /// Xoá ảnh trên bucket rồi xoá dòng DB. Ảnh nào xoá bucket lỗi thì giữ dòng (vẫn mồ côi) để
    /// BlogImageCleanupWorker thử lại sau. Trả số ảnh đã xoá xong.
    /// </summary>
    public async Task<int> PurgeAsync(IReadOnlyList<BlogImage> images, CancellationToken ct)
    {
        var done = new List<BlogImage>();
        foreach (var img in images)
        {
            try
            {
                await storage.DeleteAsync(img.Key, ct);
                done.Add(img);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                storage.Evict(img.Key);
                log.LogWarning(e, "Không xoá được ảnh blog {Key} trên bucket — để worker thử lại.", img.Key);
            }
        }
        if (done.Count > 0) await blog.RemoveImageRowsAsync(done, ct);
        return done.Count;
    }
}
