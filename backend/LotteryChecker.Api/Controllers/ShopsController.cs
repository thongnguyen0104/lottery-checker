using System.Globalization;
using System.Text.RegularExpressions;
using LotteryChecker.Api.Data;
using LotteryChecker.Api.Middleware;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LotteryChecker.Api.Controllers;

/// <summary>
/// Bản đồ điểm bán vé số: khách xem được; ghim / xác nhận / đánh giá / báo vé trúng / báo cáo cần đăng nhập.
/// Ảnh dùng chung bucket với Blog (BlogImageStorage), key tiền tố shops/.
/// </summary>
[ApiController]
[Route("api/shops")]
[RequireFeature(FeatureFlags.ShopMap)]
public partial class ShopsController(ShopService shops, BlogImageStorage storage, NotificationService notifications,
                                     AppDbContext db, ILogger<ShopsController> log) : ControllerBase
{
    public const string WriteRateLimitPolicy = "shops-write";
    public const string ImageRateLimitPolicy = "shops-image";

    private const int MaxImageRequestBytes = BlogController.MaxImageBytes + 64 * 1024;

    public record StatusRequest(ShopStatus Status);

    [HttpGet("options")]
    public object Options() => new
    {
        imagesEnabled = storage.Enabled,
        maxImageBytes = BlogController.MaxImageBytes,
        duplicateRadiusM = ShopService.DuplicateRadiusM,
        xsktTiers = ShopService.XsktTiers,
        vietlottTiers = ShopService.VietlottTiers,
    };

    /// <summary>bbox = "minLat,minLng,maxLat,maxLng"; types = "Agency,Street".</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? bbox, [FromQuery] string? types, [FromQuery] bool hasWin = false,
                                          CancellationToken ct = default)
    {
        var b = (bbox ?? "").Split(',').Select(s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN).ToArray();
        if (b.Length != 4 || b.Any(v => !double.IsFinite(v)) || b[0] > b[2] || b[1] > b[3])
            return BadRequest(new { error = Lang.T(Request, "Khung bản đồ không hợp lệ.", "Invalid map bounds.") });
        var t = (types ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Enum.TryParse<ShopType>(s, true, out var v) ? v : (ShopType?)null).OfType<ShopType>().ToArray();
        return Ok(await shops.QueryBboxAsync(b[0], b[1], b[2], b[3], t, hasWin, ct));
    }

    [HttpGet("nearby")]
    public Task<ShopService.MarkerDto[]> Nearby([FromQuery] double lat, [FromQuery] double lng, [FromQuery] double radius = 3000,
                                                CancellationToken ct = default) =>
        shops.NearbyAsync(lat, lng, radius, ct);

    [HttpGet("search")]
    public Task<ShopService.MarkerDto[]> Search([FromQuery] string? q, [FromQuery] double? lat, [FromQuery] double? lng,
                                                CancellationToken ct = default) =>
        shops.SearchAsync(q ?? "", lat, lng, ct);

    [HttpGet("{publicId:guid}")]
    public async Task<IActionResult> Get(Guid publicId, CancellationToken ct) =>
        await shops.GetAsync(publicId, CurrentUserId(), await IsAdminAsync(ct), ct) is { } s
            ? Ok(s)
            : NotFound(new { error = Lang.T(Request, "Không tìm thấy điểm bán (có thể đã bị xoá).", "Shop not found (it may have been deleted).") });

    [HttpGet("images/{**key}")]
    public async Task<IActionResult> GetImage(string key, CancellationToken ct)
    {
        if (!storage.Enabled || !ImageKeyPattern().IsMatch(key) || !await shops.IsUsedImageAsync(key, ct))
            return NotFound();
        var bytes = await storage.GetAsync(key, ct);
        if (bytes == null) return NotFound();
        Response.Headers.CacheControl = BlogImageStorage.CacheControl;
        return File(bytes, "image/webp");
    }

    [GeneratedRegex(@"^shops/\d{4}/\d{2}/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.webp$")]
    private static partial Regex ImageKeyPattern();

    [HttpPost("images")]
    [EnableRateLimiting(ImageRateLimitPolicy)]
    [RequestSizeLimit(MaxImageRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImageRequestBytes)]
    public async Task<IActionResult> UploadImage(IFormFile? image, CancellationToken ct)
    {
        if (CurrentUserId() is not { } uid) return Unauthorized();
        if (!storage.Enabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = Lang.T(Request,
                "Máy chủ chưa bật đăng ảnh.", "Image uploads are not enabled on this server.") });
        if (image is not { Length: > 0 })
            return BadRequest(new { error = Lang.T(Request, "Chưa chọn ảnh.", "No image selected.") });
        if (image.Length > BlogController.MaxImageBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = Lang.T(Request, "Ảnh tối đa 5MB.", "Images must be 5MB or smaller.") });
        if (await shops.PendingImageCountAsync(uid, ct) >= ShopService.MaxPendingImagesPerUser)
            return BadRequest(new { error = Lang.T(Request,
                "Bạn có nhiều ảnh chưa dùng quá — lưu điểm bán hoặc chờ một lát rồi thử lại.",
                "Too many unused images — save the shop or try again later.") });

        using var ms = new MemoryStream((int)image.Length);
        await image.CopyToAsync(ms, ct);
        try
        {
            var webp = await Task.Run(() => BlogImageStorage.Process(ms.ToArray()), ct);
            var now = DateTime.UtcNow;
            var key = $"shops/{now:yyyy}/{now:MM}/{Guid.NewGuid():D}.webp";
            await storage.PutAsync(key, webp, ct);
            return Ok(await shops.AddImageAsync(key, uid, webp.Length, ct));
        }
        catch (InvalidBlogImageException)
        {
            return UnprocessableEntity(new { error = Lang.T(Request,
                "File này không đọc được như ảnh — thử ảnh JPG/PNG/WebP khác nhé.",
                "This file can't be read as an image — try another JPG/PNG/WebP.") });
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Upload ảnh điểm bán lên bucket lỗi");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = Lang.T(Request,
                "Chưa lưu được ảnh, thử lại sau nhé.", "Couldn't save the image — please try again.") });
        }
    }

    [HttpPost]
    [EnableRateLimiting(WriteRateLimitPolicy)]
    public Task<IActionResult> Create(ShopService.ShopInput body, CancellationToken ct) => Run(async uid =>
    {
        if (ShopService.Validate(body, Lang.IsEn(Request)) is { } error) return BadRequest(new { error });
        return Ok(await shops.CreateAsync(body, uid, ct));
    });

    [HttpPut("{publicId:guid}")]
    [EnableRateLimiting(WriteRateLimitPolicy)]
    public Task<IActionResult> Update(Guid publicId, ShopService.ShopInput body, CancellationToken ct) => Run(async uid =>
    {
        if (ShopService.Validate(body, Lang.IsEn(Request)) is { } error) return BadRequest(new { error });
        return Ok(await shops.UpdateAsync(publicId, body, uid, await IsAdminAsync(ct), ct));
    });

    [HttpDelete("{publicId:guid}")]
    public Task<IActionResult> Delete(Guid publicId, CancellationToken ct) => Run(async uid =>
    {
        await shops.DeleteAsync(publicId, uid, await IsAdminAsync(ct), ct);
        return NoContent();
    });

    [HttpPost("{publicId:guid}/confirm")]
    [EnableRateLimiting(WriteRateLimitPolicy)]
    public Task<IActionResult> Confirm(Guid publicId, CancellationToken ct) => Run(async uid =>
    {
        var (count, last) = await shops.ConfirmAsync(publicId, uid, ct);
        return Ok(new { confirmCount = count, lastConfirmedAt = last });
    });

    [HttpPut("{publicId:guid}/review")]
    [EnableRateLimiting(WriteRateLimitPolicy)]
    public Task<IActionResult> Review(Guid publicId, ShopService.ReviewInput body, CancellationToken ct) => Run(async uid =>
    {
        var r = await shops.UpsertReviewAsync(publicId, body, uid, ct);
        if (r.IsNew) await notifications.OnShopReviewAsync(r.Id, ct);
        return Ok(new { review = r.Review, rating = r.Rating, ratingCount = r.RatingCount });
    });

    [HttpDelete("reviews/{id:int}")]
    public Task<IActionResult> DeleteReview(int id, CancellationToken ct) => Run(async uid =>
    {
        var (rating, count) = await shops.DeleteReviewAsync(id, uid, await IsAdminAsync(ct), ct);
        return Ok(new { rating, ratingCount = count });
    });

    [HttpPost("{publicId:guid}/wins")]
    [EnableRateLimiting(WriteRateLimitPolicy)]
    public Task<IActionResult> AddWin(Guid publicId, ShopService.WinInput body, CancellationToken ct) => Run(async uid =>
    {
        var (win, id) = await shops.AddWinAsync(publicId, body, uid, ct);
        await notifications.OnShopWinAsync(id, ct);
        return Ok(win);
    });

    [HttpDelete("wins/{id:int}")]
    public Task<IActionResult> DeleteWin(int id, CancellationToken ct) => Run(async uid =>
        Ok(new { winCount = await shops.DeleteWinAsync(id, uid, await IsAdminAsync(ct), ct) }));

    [HttpPost("{publicId:guid}/report")]
    [EnableRateLimiting(WriteRateLimitPolicy)]
    public Task<IActionResult> Report(Guid publicId, ShopService.ReportInput body, CancellationToken ct) => Run(async uid =>
        Ok(new { hidden = await shops.ReportAsync(publicId, body, uid, ct) }));

    // ───────── Admin ─────────

    [HttpGet("/api/admin/shops/reported")]
    [AdminOnly]
    public Task<ShopService.ReportedDto[]> Reported(CancellationToken ct) => shops.ListReportedAsync(ct);

    [HttpGet("/api/admin/shops/{publicId:guid}/history")]
    [AdminOnly]
    public async Task<IActionResult> History(Guid publicId, CancellationToken ct) =>
        await shops.HistoryAsync(publicId, ct) is { } list
            ? Ok(list)
            : NotFound(new { error = Lang.T(Request, "Không tìm thấy điểm bán (có thể đã bị xoá).", "Shop not found (it may have been deleted).") });

    [HttpPost("/api/admin/shops/{publicId:guid}/status")]
    [AdminOnly]
    public Task<IActionResult> SetStatus(Guid publicId, StatusRequest body, CancellationToken ct) => Run(async _ =>
    {
        await shops.SetStatusAsync(publicId, body.Status, ct);
        return NoContent();
    });

    /// <summary>Cần đăng nhập; lỗi nghiệp vụ (<see cref="ShopException"/>) → status + câu theo ngôn ngữ.</summary>
    private async Task<IActionResult> Run(Func<int, Task<IActionResult>> action)
    {
        if (CurrentUserId() is not { } uid)
            return Unauthorized(new { error = Lang.T(Request, "Bạn cần đăng nhập để làm việc này.", "Please log in to do this.") });
        try
        {
            return await action(uid);
        }
        catch (ShopException e)
        {
            return StatusCode(e.Status, new { error = Lang.T(Request, e.Vi, e.En) });
        }
    }

    private async Task<bool> IsAdminAsync(CancellationToken ct) =>
        CurrentUserId() is { } uid && await db.Users.AnyAsync(x => x.Id == uid && x.IsAdmin, ct);

    private int? CurrentUserId() => User.UserId();
}

/// <summary>Tìm địa chỉ / toạ độ → địa chỉ (Nominatim qua máy chủ) cho bản đồ điểm bán.</summary>
[ApiController]
[Route("api/geo")]
[RequireFeature(FeatureFlags.ShopMap)]
[EnableRateLimiting(RateLimitPolicy)]
public class GeoController(GeocodingService geo) : ControllerBase
{
    public const string RateLimitPolicy = "geo";

    [HttpGet("search")]
    public Task<GeocodingService.Place[]> Search([FromQuery] string? q, CancellationToken ct) => geo.SearchAsync(q ?? "", ct);

    [HttpGet("reverse")]
    public async Task<IActionResult> Reverse([FromQuery] double lat, [FromQuery] double lng, CancellationToken ct)
    {
        if (!ShopService.InVietnam(lat, lng)) return Ok(null);
        return Ok(await geo.ReverseAsync(lat, lng, ct));
    }
}
