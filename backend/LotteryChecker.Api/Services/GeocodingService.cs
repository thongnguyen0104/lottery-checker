using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Tìm địa chỉ / đổi toạ độ → địa chỉ qua Nominatim (OpenStreetMap). Đi qua máy chủ thay vì gọi thẳng từ
/// trình duyệt để: giữ đúng policy Nominatim (User-Agent riêng, tối đa 1 request/giây cho CẢ app) và cache
/// kết quả. Lỗi / quá hạn → trả rỗng, FE vẫn cho người dùng tự gõ địa chỉ.
/// </summary>
public class GeocodingService(HttpClient http, IMemoryCache cache, IConfiguration config, ILogger<GeocodingService> log)
{
    public const int MaxQueryLength = 120;
    public const int SearchLimit = 6;
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(24);
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    // Dùng chung mọi instance (HttpClient typed là transient): xếp hàng để không vượt 1 request/giây.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastCall = DateTime.MinValue;

    public record Place(string Label, double Lat, double Lng);

    private string BaseUrl => (config["Geo:NominatimUrl"] ?? "https://nominatim.openstreetmap.org").TrimEnd('/');

    public async Task<Place[]> SearchAsync(string q, CancellationToken ct)
    {
        q = q.Trim();
        if (q.Length < 3) return [];
        if (q.Length > MaxQueryLength) q = q[..MaxQueryLength];
        var url = $"{BaseUrl}/search?format=jsonv2&countrycodes=vn&accept-language=vi&limit={SearchLimit}&q={Uri.EscapeDataString(q)}";
        return await CachedAsync($"geo:s:{q.ToLowerInvariant()}", url, json =>
            json.ValueKind == JsonValueKind.Array
                ? json.EnumerateArray().Select(ToPlace).OfType<Place>().ToArray()
                : [], ct) ?? [];
    }

    public async Task<Place?> ReverseAsync(double lat, double lng, CancellationToken ct)
    {
        // Làm tròn ~11m: ghim xê dịch chút vẫn trúng cache.
        var la = Math.Round(lat, 4).ToString(CultureInfo.InvariantCulture);
        var ln = Math.Round(lng, 4).ToString(CultureInfo.InvariantCulture);
        var url = $"{BaseUrl}/reverse?format=jsonv2&accept-language=vi&zoom=18&lat={la}&lon={ln}";
        var r = await CachedAsync($"geo:r:{la},{ln}", url, json => ToPlace(json) is { } p ? new[] { p } : [], ct);
        return r?.FirstOrDefault();
    }

    private async Task<Place[]?> CachedAsync(string key, string url, Func<JsonElement, Place[]> parse, CancellationToken ct)
    {
        if (cache.TryGetValue(key, out Place[]? hit)) return hit;
        await Gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(key, out hit)) return hit;   // người xếp hàng trước đã tra giùm
            var wait = _lastCall + MinInterval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastCall = DateTime.UtcNow;

            using var res = await http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
            {
                log.LogWarning("Nominatim trả {Status} cho {Url}", (int)res.StatusCode, url);
                return null;
            }
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var places = parse(doc.RootElement);
            cache.Set(key, places, CacheFor);
            return places;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            log.LogWarning(e, "Gọi Nominatim lỗi");
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static Place? ToPlace(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object
            || !e.TryGetProperty("lat", out var lat) || !e.TryGetProperty("lon", out var lon)
            || !double.TryParse(lat.GetString(), CultureInfo.InvariantCulture, out var la)
            || !double.TryParse(lon.GetString(), CultureInfo.InvariantCulture, out var ln))
            return null;
        var label = e.TryGetProperty("display_name", out var d) ? d.GetString() ?? "" : "";
        // Bỏ ", Việt Nam" và mã bưu chính cuối chuỗi cho gọn.
        var parts = label.Split(", ").Where(p => p != "Việt Nam" && !p.All(char.IsDigit)).ToArray();
        return new Place(string.Join(", ", parts), la, ln);
    }
}
