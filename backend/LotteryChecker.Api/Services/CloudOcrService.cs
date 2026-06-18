using System.Text.Json;

namespace LotteryChecker.Api.Services;

/// <summary>
/// Đọc text từ ảnh bằng OCR đám mây (mặc định: OCR.space — miễn phí 25k lượt/tháng,
/// không cần thẻ tín dụng). Dùng để đọc SỐ VÉ in cách điệu mà Tesseract cục bộ đọc sai.
/// Thiết kế "best-effort": mọi lỗi (chưa cấu hình key, mạng, quota) → trả null,
/// luồng /api/scan vẫn chạy bằng kết quả Tesseract cục bộ (không bao giờ làm vỡ scan).
/// </summary>
public class CloudOcrService
{
    private readonly HttpClient _http;
    private readonly ILogger<CloudOcrService> _log;
    private readonly CloudOcrOptions _opt;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CloudOcrService(HttpClient http, IConfiguration config, ILogger<CloudOcrService> log)
    {
        _http = http;
        _log = log;
        _opt = config.GetSection("CloudOcr").Get<CloudOcrOptions>() ?? new CloudOcrOptions();
    }

    /// <summary>Bật khi có cấu hình hợp lệ (Enabled + ApiKey không rỗng).</summary>
    public bool IsEnabled => _opt.Enabled && !string.IsNullOrWhiteSpace(_opt.ApiKey);

    /// <summary>Gửi ảnh (JPEG/PNG, &lt;1MB cho gói free) lên cloud OCR, trả text đọc được hoặc null.</summary>
    public async Task<string?> ReadTextAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            _log.LogDebug("CloudOcr tắt hoặc thiếu ApiKey — bỏ qua.");
            return null;
        }

        try
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(_opt.ApiKey!), "apikey" },
                { new StringContent(_opt.Language), "language" },
                { new StringContent(_opt.Engine.ToString()), "OCREngine" },
                { new StringContent("true"), "scale" },          // upscale ảnh nhỏ → đọc nét hơn
                { new StringContent("false"), "isOverlayRequired" },
            };
            var file = new ByteArrayContent(imageBytes);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            form.Add(file, "file", "ticket.jpg");

            using var resp = await _http.PostAsync(_opt.Endpoint, form, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("CloudOcr HTTP {Status}: {Body}", (int)resp.StatusCode, Trunc(body));
                return null;
            }

            var parsed = JsonSerializer.Deserialize<OcrSpaceResponse>(body, JsonOpts);
            if (parsed is null || parsed.IsErroredOnProcessing || parsed.OCRExitCode != 1)
            {
                _log.LogWarning("CloudOcr lỗi xử lý: {Body}", Trunc(body));
                return null;
            }

            var text = parsed.ParsedResults?.FirstOrDefault()?.ParsedText;
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex)
        {
            // Không bao giờ ném lên trên: scan phải sống được dù cloud chết.
            _log.LogWarning(ex, "CloudOcr thất bại — fallback về OCR cục bộ.");
            return null;
        }
    }

    private static string Trunc(string s) => s.Length > 500 ? s[..500] : s;

    // ---- Cấu hình + DTO phản hồi OCR.space ----

    private sealed class CloudOcrOptions
    {
        public bool Enabled { get; set; }
        public string Provider { get; set; } = "OcrSpace";
        public string Endpoint { get; set; } = "https://api.ocr.space/parse/image";
        public string? ApiKey { get; set; }
        public string Language { get; set; } = "eng";  // số vé là chữ số → eng đủ; Engine 2 chỉ hỗ trợ eng
        public int Engine { get; set; } = 2;            // Engine 2 đọc font cách điệu tốt hơn Engine 1
    }

    private sealed class OcrSpaceResponse
    {
        public List<OcrSpaceResult>? ParsedResults { get; set; }
        public int OCRExitCode { get; set; }
        public bool IsErroredOnProcessing { get; set; }
    }

    private sealed class OcrSpaceResult
    {
        public string? ParsedText { get; set; }
    }
}
