using LotteryChecker.Api.Middleware;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LotteryChecker.Api.Controllers;

/// <summary>
/// Website con /s/{slug}: khách xem + gửi yêu cầu giữ vé (không thu tiền online); chủ site (tài khoản đã đăng nhập,
/// mỗi tài khoản 1 site) sửa nháp / publish / quản lý sản phẩm, bài viết, yêu cầu giữ vé.
/// Ảnh dùng chung bucket với Blog (BlogImageStorage), key tiền tố sites/.
/// </summary>
[ApiController]
[Route("api/sites")]
[RequireFeature(FeatureFlags.Sites)]
public class SitesController(SiteService sites, BlogImageStorage storage, NotificationService notifications,
                             ILogger<SitesController> log) : ControllerBase
{
    public const string WriteRateLimitPolicy = "sites-write";
    public const string ImageRateLimitPolicy = "sites-image";
    public const string ReserveRateLimitPolicy = "sites-reserve";

    private const int MaxImageRequestBytes = BlogController.MaxImageBytes + 64 * 1024;

    public record CreateRequest(string? Slug);
    public record StatusRequest(ReservationStatus Status);
    public record SiteStatusRequest(SiteStatus Status);

    [HttpGet("options")]
    public object Options() => new
    {
        imagesEnabled = storage.Enabled,
        maxImageBytes = BlogController.MaxImageBytes,
        themes = SiteService.Themes,
        blockTypes = SiteService.BlockTypes,
        slugMin = SiteService.SlugMin,
        slugMax = SiteService.SlugMax,
    };

    [HttpGet("slug-available")]
    public async Task<IActionResult> SlugAvailable([FromQuery] string? slug, CancellationToken ct)
    {
        if (SiteService.ValidateSlug(slug, Lang.IsEn(Request)) is { } error) return Ok(new { available = false, error });
        return Ok(new { available = !await sites.IsSlugTakenAsync(slug!, User.UserId(), ct) });
    }

    // ───────── Công khai ─────────

    /// <summary>preview=true: chủ site xem bản nháp.</summary>
    [HttpGet("public/{slug}")]
    public async Task<IActionResult> GetPublic(string slug, [FromQuery] bool preview = false, CancellationToken ct = default) =>
        await sites.GetPublicAsync(slug, User.UserId(), preview, ct) is { } s ? Ok(s) : SiteNotFound();

    [HttpGet("public/{slug}/posts")]
    public async Task<IActionResult> PublicPosts(string slug, CancellationToken ct) =>
        await sites.ListPublicPostsAsync(slug, ct) is { } list ? Ok(list) : SiteNotFound();

    [HttpGet("public/{slug}/posts/{postId:guid}")]
    public async Task<IActionResult> PublicPost(string slug, Guid postId, CancellationToken ct) =>
        await sites.GetPublicPostAsync(slug, postId, User.UserId(), ct) is { } p
            ? Ok(p)
            : NotFound(new { error = Lang.T(Request, "Không tìm thấy bài viết.", "Post not found.") });

    [HttpPost("public/{slug}/reservations")]
    [EnableRateLimiting(ReserveRateLimitPolicy)]
    public Task<IActionResult> Reserve(string slug, SiteService.ReservationInput body, CancellationToken ct) => Guard(async () =>
    {
        var uid = User.UserId();
        var key = uid is { } id ? $"u:{id}" : $"ip:{HttpContext.Connection.RemoteIpAddress}";
        var (publicId, rid, _) = await sites.ReserveAsync(slug, body, uid, key, ct);
        await notifications.OnSiteReservationAsync(rid, ct);
        return Ok(new { id = publicId });
    });

    [HttpPost("public/{slug}/report")]
    [EnableRateLimiting(WriteRateLimitPolicy)]
    public Task<IActionResult> Report(string slug, SiteService.ReportInput body, CancellationToken ct) => Run(async uid =>
        Ok(new { hidden = await sites.ReportAsync(slug, body, uid, ct) }));

    [HttpGet("images/{**key}")]
    public async Task<IActionResult> GetImage(string key, CancellationToken ct)
    {
        if (!storage.Enabled || !SiteService.ImageKeyPattern().IsMatch(key) || key.Length > 80
            || !await sites.CanViewImageAsync(key, User.UserId(), ct))
            return NotFound();
        var bytes = await storage.GetAsync(key, ct);
        if (bytes == null) return NotFound();
        Response.Headers.CacheControl = BlogImageStorage.CacheControl;
        return File(bytes, "image/webp");
    }

    // ───────── Site của tôi ─────────

    [HttpGet("mine")]
    public Task<IActionResult> Mine(CancellationToken ct) => Run(async uid =>
        Ok(new { site = await sites.GetMineAsync(uid, ct), pendingReservations = await sites.PendingReservationCountAsync(uid, ct) }));

    [HttpPost("mine")]
    [EnableRateLimiting(WriteRateLimitPolicy)]
    public Task<IActionResult> Create(CreateRequest body, CancellationToken ct) => Run(async uid =>
    {
        if (SiteService.ValidateSlug(body.Slug, Lang.IsEn(Request)) is { } error) return BadRequest(new { error });
        return Ok(await sites.CreateAsync(uid, body.Slug!, ct));
    });

    [HttpPut("mine/draft")]
    [EnableRateLimiting(WriteRateLimitPolicy)]
    public Task<IActionResult> SaveDraft(SiteService.SettingsInput body, CancellationToken ct) => Run(async uid =>
        Ok(await sites.SaveDraftAsync(uid, body, ct)));

    [HttpPost("mine/publish")]
    [EnableRateLimiting(WriteRateLimitPolicy)]
    public Task<IActionResult> Publish(CancellationToken ct) => Run(async uid => Ok(await sites.PublishAsync(uid, ct)));

    [HttpPost("images")]
    [EnableRateLimiting(ImageRateLimitPolicy)]
    [RequestSizeLimit(MaxImageRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImageRequestBytes)]
    public async Task<IActionResult> UploadImage(IFormFile? image, CancellationToken ct)
    {
        if (User.UserId() is not { } uid) return Unauthorized();
        if (!storage.Enabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = Lang.T(Request,
                "Máy chủ chưa bật đăng ảnh.", "Image uploads are not enabled on this server.") });
        if (image is not { Length: > 0 })
            return BadRequest(new { error = Lang.T(Request, "Chưa chọn ảnh.", "No image selected.") });
        if (image.Length > BlogController.MaxImageBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = Lang.T(Request, "Ảnh tối đa 5MB.", "Images must be 5MB or smaller.") });
        if (await sites.PendingImageCountAsync(uid, ct) >= SiteService.MaxPendingImagesPerUser)
            return BadRequest(new { error = Lang.T(Request,
                "Bạn có nhiều ảnh chưa dùng quá — lưu thay đổi hoặc chờ một lát rồi thử lại.",
                "Too many unused images — save your changes or try again later.") });

        using var ms = new MemoryStream((int)image.Length);
        await image.CopyToAsync(ms, ct);
        try
        {
            var webp = await Task.Run(() => BlogImageStorage.Process(ms.ToArray()), ct);
            var now = DateTime.UtcNow;
            var key = $"sites/{now:yyyy}/{now:MM}/{Guid.NewGuid():D}.webp";
            await storage.PutAsync(key, webp, ct);
            return Ok(await sites.AddImageAsync(key, uid, webp.Length, ct));
        }
        catch (InvalidBlogImageException)
        {
            return UnprocessableEntity(new { error = Lang.T(Request,
                "File này không đọc được như ảnh — thử ảnh JPG/PNG/WebP khác nhé.",
                "This file can't be read as an image — try another JPG/PNG/WebP.") });
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Upload ảnh website con lên bucket lỗi");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = Lang.T(Request,
                "Chưa lưu được ảnh, thử lại sau nhé.", "Couldn't save the image — please try again.") });
        }
    }

    [HttpGet("mine/products")]
    public Task<IActionResult> Products(CancellationToken ct) => Run(async uid => Ok(await sites.ListProductsAsync(uid, ct)));

    [HttpPost("mine/products")]
    [EnableRateLimiting(WriteRateLimitPolicy)]
    public Task<IActionResult> CreateProduct(SiteService.ProductInput body, CancellationToken ct) => Run(async uid =>
        Ok(await sites.SaveProductAsync(uid, null, body, ct)));

    [HttpPut("mine/products/{id:int}")]
    [EnableRateLimiting(WriteRateLimitPolicy)]
    public Task<IActionResult> UpdateProduct(int id, SiteService.ProductInput body, CancellationToken ct) => Run(async uid =>
        Ok(await sites.SaveProductAsync(uid, id, body, ct)));

    [HttpDelete("mine/products/{id:int}")]
    public Task<IActionResult> DeleteProduct(int id, CancellationToken ct) => Run(async uid =>
    {
        await sites.DeleteProductAsync(uid, id, ct);
        return NoContent();
    });

    [HttpGet("mine/posts")]
    public Task<IActionResult> Posts(CancellationToken ct) => Run(async uid => Ok(await sites.ListMyPostsAsync(uid, ct)));

    [HttpGet("mine/posts/{id:guid}")]
    public Task<IActionResult> GetPost(Guid id, CancellationToken ct) => Run(async uid => Ok(await sites.GetMyPostAsync(uid, id, ct)));

    [HttpPost("mine/posts")]
    [EnableRateLimiting(WriteRateLimitPolicy)]
    public Task<IActionResult> CreatePost(SiteService.PostInput body, CancellationToken ct) => Run(async uid =>
        Ok(await sites.SavePostAsync(uid, null, body, ct)));

    [HttpPut("mine/posts/{id:guid}")]
    [EnableRateLimiting(WriteRateLimitPolicy)]
    public Task<IActionResult> UpdatePost(Guid id, SiteService.PostInput body, CancellationToken ct) => Run(async uid =>
        Ok(await sites.SavePostAsync(uid, id, body, ct)));

    [HttpDelete("mine/posts/{id:guid}")]
    public Task<IActionResult> DeletePost(Guid id, CancellationToken ct) => Run(async uid =>
    {
        await sites.DeletePostAsync(uid, id, ct);
        return NoContent();
    });

    [HttpGet("mine/reservations")]
    public Task<IActionResult> Reservations([FromQuery] ReservationStatus? status, CancellationToken ct) => Run(async uid =>
        Ok(await sites.ListReservationsAsync(uid, status, ct)));

    [HttpPut("mine/reservations/{id:guid}")]
    public Task<IActionResult> SetReservationStatus(Guid id, StatusRequest body, CancellationToken ct) => Run(async uid =>
        Ok(await sites.SetReservationStatusAsync(uid, id, body.Status, ct)));

    // ───────── Admin ─────────

    [HttpGet("/api/admin/sites")]
    [AdminOnly]
    public Task<SiteService.AdminSiteDto[]> AdminList(CancellationToken ct) => sites.AdminListAsync(ct);

    [HttpPost("/api/admin/sites/{publicId:guid}/status")]
    [AdminOnly]
    public Task<IActionResult> SetStatus(Guid publicId, SiteStatusRequest body, CancellationToken ct) => Guard(async () =>
    {
        await sites.SetStatusAsync(publicId, body.Status, ct);
        return NoContent();
    });

    private NotFoundObjectResult SiteNotFound() =>
        NotFound(new { error = Lang.T(Request, "Không tìm thấy website (có thể đã bị ẩn).", "Site not found (it may be hidden).") });

    /// <summary>Cần đăng nhập.</summary>
    private Task<IActionResult> Run(Func<int, Task<IActionResult>> action)
    {
        if (User.UserId() is not { } uid)
            return Task.FromResult<IActionResult>(Unauthorized(new { error = Lang.T(Request,
                "Bạn cần đăng nhập để làm việc này.", "Please log in to do this.") }));
        return Guard(() => action(uid));
    }

    /// <summary>Lỗi nghiệp vụ (<see cref="SiteException"/>) → status + câu theo ngôn ngữ.</summary>
    private async Task<IActionResult> Guard(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (SiteException e)
        {
            return StatusCode(e.Status, new { error = Lang.T(Request, e.Vi, e.En) });
        }
    }
}
