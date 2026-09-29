using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using LotteryChecker.Api.Models;
using LotteryChecker.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LotteryChecker.Tests;

// Gemini đọc thẳng ảnh → JSON {số vé, ngày, đài}. Sai ở đây là hoặc đưa giá trị rác lên form như
// thể đã chắc, hoặc làm vỡ /api/scan khi Gemini lỗi — cả hai đều tệ hơn không dùng Gemini.
public class GeminiTicketReaderTests
{
    // HttpMessageHandler giả: ghi lại request, trả response dựng sẵn.
    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            RequestBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (GeminiTicketReader Reader, FakeHandler Handler) Reader(
        string responseBody, HttpStatusCode status = HttpStatusCode.OK, string? apiKey = "test-key",
        bool enabled = true, string? thinkingLevel = "minimal")
    {
        var handler = new FakeHandler(status, responseBody);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:Enabled"] = enabled.ToString(),
            ["Gemini:ApiKey"] = apiKey,
            ["Gemini:Model"] = "gemini-3.1-flash-lite",
            ["Gemini:ThinkingLevel"] = thinkingLevel,
            ["Gemini:RetryDelayMs"] = "0",   // 5xx sẽ được gọi lại — test không cần chờ 1s thật
        }).Build();
        return (new GeminiTicketReader(new HttpClient(handler), config, NullLogger<GeminiTicketReader>.Instance), handler);
    }

    // Trả lần lượt từng response trong danh sách (hết thì lặp lại cái cuối), đếm số lượt gọi.
    private sealed class SequenceHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var (status, body) = responses[Math.Min(Calls++, responses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static GeminiTicketReader Reader(SequenceHandler handler, int? maxRetries = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Gemini:Enabled"] = "true", ["Gemini:ApiKey"] = "test-key", ["Gemini:RetryDelayMs"] = "0",
        };
        if (maxRetries is { } n) settings["Gemini:MaxRetries"] = n.ToString();
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new GeminiTicketReader(new HttpClient(handler), config, NullLogger<GeminiTicketReader>.Instance);
    }

    private const string Overloaded = """{"error":{"code":503,"message":"The model is overloaded. Please try again later.","status":"UNAVAILABLE"}}""";

    // Response generateContent thật có dạng candidates[0].content.parts[].text = chuỗi JSON.
    private static string GeminiResponse(string ticketJson) => JsonSerializer.Serialize(new
    {
        candidates = new[] { new { content = new { role = "model", parts = new[] { new { text = ticketJson } } }, finishReason = "STOP" } },
    });

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xD9];

    [Fact(DisplayName = "1. JSON hợp lệ → đủ số/ngày/đài, đài coi là khớp nguyên văn, số vé đánh dấu từ cloud")]
    public async Task ValidJson_MapsAllFields()
    {
        var (reader, _) = Reader(GeminiResponse("""{"ticketNumber":"288921","drawDate":"2026-06-05","province":"BinhDuong"}"""));

        var info = await reader.ReadAsync(Jpeg);

        info.Should().NotBeNull();
        info!.TicketNumber.Should().Be("288921");
        info.DrawDate.Should().Be(new DateOnly(2026, 6, 5));
        info.Province.Should().Be("BinhDuong");
        info.ProvinceExact.Should().BeTrue();
        info.ProvinceAmbiguous.Should().BeFalse();
        info.TicketNumberFromCloud.Should().BeTrue();
        info.OcrConfidence.Should().Be(GeminiTicketReader.AssumedConfidence);
    }

    [Fact(DisplayName = "1b. Ảnh nhiều vé → đủ từng vé theo thứ tự, bỏ vé trùng và vé trống, gửi schema mảng")]
    public async Task ManyTickets_MapsEachTicket()
    {
        var (reader, handler) = Reader(GeminiResponse("""
            {"tickets":[
              {"ticketNumber":"988501","drawDate":"2026-09-27","province":"TienGiang"},
              {"ticketNumber":"988842","drawDate":"2026-09-26","province":"TPHCM"},
              {"ticketNumber":"988842","drawDate":"2026-09-26","province":"TPHCM"},
              {"ticketNumber":null,"drawDate":null,"province":null},
              {"ticketNumber":"631042","drawDate":"2026-09-26","province":"Atlantis"}
            ]}
            """));

        var result = await reader.TryReadManyAsync(Jpeg);

        result.Error.Should().BeNull();
        result.Tickets!.Select(t => t.TicketNumber).Should().Equal("988501", "988842", "631042");
        result.Tickets![2].Province.Should().BeNull();   // đài lạ → null như vé đơn
        handler.RequestBody.Should().Contain("\"ARRAY\"");
    }

    [Fact(DisplayName = "1c. Ảnh nhiều vé mà Gemini trả JSON không có mảng tickets → bad_json")]
    public async Task ManyTickets_MissingArray_IsBadJson()
    {
        var (reader, _) = Reader(GeminiResponse("""{"ticketNumber":"988501"}"""));

        var result = await reader.TryReadManyAsync(Jpeg);

        result.Tickets.Should().BeNull();
        result.Error.Should().Be("bad_json");
    }

    [Fact(DisplayName = "2. Giá trị sai định dạng (5 chữ số, ngày không có thật, đài lạ) → null, không đưa rác lên form")]
    public async Task InvalidValues_BecomeNull()
    {
        var (reader, _) = Reader(GeminiResponse("""{"ticketNumber":"28892","drawDate":"2026-13-40","province":"Atlantis"}"""));

        var info = await reader.ReadAsync(Jpeg);

        info!.TicketNumber.Should().BeNull();
        info.DrawDate.Should().BeNull();
        info.Province.Should().BeNull();
        info.ProvinceExact.Should().BeFalse();
        info.TicketNumberFromCloud.Should().BeFalse();
    }

    [Fact(DisplayName = "3. Số vé có dấu cách (\"288 921\"), mã đài khác hoa/thường, bọc ```json → vẫn đọc được")]
    public async Task TolerantParsing()
    {
        var (reader, _) = Reader(GeminiResponse("```json\n{\"ticketNumber\":\"288 921\",\"drawDate\":\"2026-09-26\",\"province\":\"tphcm\"}\n```"));

        var info = await reader.ReadAsync(Jpeg);

        info!.TicketNumber.Should().Be("288921");
        info.Province.Should().Be("TPHCM");
    }

    [Fact(DisplayName = "4. Gemini trả null cho trường không đọc được → trường đó null, các trường khác vẫn giữ")]
    public async Task NullFields_AreKeptNull()
    {
        var (reader, _) = Reader(GeminiResponse("""{"ticketNumber":"417523","drawDate":null,"province":null}"""));

        var info = await reader.ReadAsync(Jpeg);

        info!.TicketNumber.Should().Be("417523");
        info.DrawDate.Should().BeNull();
        info.Province.Should().BeNull();
    }

    [Theory(DisplayName = "5. HTTP lỗi (quota 429, key sai 400/403, 500) → null, không ném exception")]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task HttpError_ReturnsNull(HttpStatusCode status)
    {
        var (reader, _) = Reader("""{"error":{"code":429,"message":"quota"}}""", status);
        (await reader.ReadAsync(Jpeg)).Should().BeNull();
    }

    [Fact(DisplayName = "6. Bị chặn (không có candidates) hoặc text không phải JSON → null")]
    public async Task BlockedOrGarbage_ReturnsNull()
    {
        var (blocked, _) = Reader("""{"promptFeedback":{"blockReason":"SAFETY"}}""");
        (await blocked.ReadAsync(Jpeg)).Should().BeNull();

        var (garbage, _) = Reader(GeminiResponse("xin lỗi, tôi không đọc được ảnh"));
        (await garbage.ReadAsync(Jpeg)).Should().BeNull();
    }

    [Theory(DisplayName = "7. Tắt hoặc thiếu ApiKey → không gọi mạng, trả null")]
    [InlineData(false, "test-key")]
    [InlineData(true, "")]
    [InlineData(true, null)]
    public async Task Disabled_DoesNotCallApi(bool enabled, string? apiKey)
    {
        var (reader, handler) = Reader(GeminiResponse("{}"), apiKey: apiKey, enabled: enabled);

        reader.IsEnabled.Should().BeFalse();
        (await reader.ReadAsync(Jpeg)).Should().BeNull();
        handler.Request.Should().BeNull();
    }

    [Fact(DisplayName = "8. Request: đúng endpoint model, key ở header (không ở URL), ảnh base64, JSON schema, thinkingLevel")]
    public async Task Request_IsWellFormed()
    {
        var (reader, handler) = Reader(GeminiResponse("""{"ticketNumber":null,"drawDate":null,"province":null}"""));

        await reader.ReadAsync(Jpeg);

        handler.Request!.RequestUri!.ToString().Should().Be(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite:generateContent");
        handler.Request.RequestUri.Query.Should().NotContain("key");
        handler.Request.Headers.GetValues("x-goog-api-key").Should().Equal("test-key");

        using var body = JsonDocument.Parse(handler.RequestBody!);
        var parts = body.RootElement.GetProperty("contents")[0].GetProperty("parts");
        var inline = parts[0].GetProperty("inlineData");
        inline.GetProperty("mimeType").GetString().Should().Be("image/jpeg");
        inline.GetProperty("data").GetString().Should().Be(Convert.ToBase64String(Jpeg));
        parts[1].GetProperty("text").GetString().Should().Contain("XỔ SỐ KIẾN THIẾT");

        var gen = body.RootElement.GetProperty("generationConfig");
        gen.GetProperty("responseMimeType").GetString().Should().Be("application/json");
        gen.GetProperty("responseSchema").GetProperty("properties").GetProperty("province")
            .GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(ProvinceMatcher.AllCodes);
        gen.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString().Should().Be("minimal");
        gen.TryGetProperty("mediaResolution", out _).Should().BeFalse();   // không cấu hình → không gửi
    }

    [Fact(DisplayName = "9. ThinkingLevel rỗng → không gửi thinkingConfig (dùng mặc định của model)")]
    public async Task EmptyThinkingLevel_IsOmitted()
    {
        var (reader, handler) = Reader(GeminiResponse("{}"), thinkingLevel: "");

        await reader.ReadAsync(Jpeg);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        body.RootElement.GetProperty("generationConfig").TryGetProperty("thinkingConfig", out _).Should().BeFalse();
    }

    [Fact(DisplayName = "10. TryReadAsync báo đúng mã lỗi — để /api/scan cho thấy VÌ SAO phải lùi về OCR.space")]
    public async Task TryRead_ReportsErrorCode()
    {
        (await Reader("""{"error":{"code":503}}""", HttpStatusCode.ServiceUnavailable).Reader.TryReadAsync(Jpeg))
            .Error.Should().Be("http_503");
        (await Reader("""{"promptFeedback":{"blockReason":"SAFETY"}}""").Reader.TryReadAsync(Jpeg))
            .Error.Should().Be("empty");
        (await Reader(GeminiResponse("xin lỗi, tôi không đọc được ảnh")).Reader.TryReadAsync(Jpeg))
            .Error.Should().Be("bad_json");
        (await Reader("{}", enabled: false).Reader.TryReadAsync(Jpeg)).Error.Should().Be("disabled");

        var ok = await Reader(GeminiResponse("""{"ticketNumber":"417523","drawDate":null,"province":null}""")).Reader.TryReadAsync(Jpeg);
        ok.Error.Should().BeNull();
        ok.Info!.TicketNumber.Should().Be("417523");
    }

    [Fact(DisplayName = "12. 503 (model quá tải) rồi 200 → tự gọi lại 1 lần và đọc được, ghi lại lỗi đã gọi lại")]
    public async Task Overloaded_IsRetriedOnce()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.ServiceUnavailable, Overloaded),
            (HttpStatusCode.OK, GeminiResponse("""{"ticketNumber":"417523","drawDate":"2026-09-25","province":"VinhLong"}""")));

        var result = await Reader(handler).TryReadAsync(Jpeg);

        handler.Calls.Should().Be(2);
        result.Error.Should().BeNull();
        result.Info!.TicketNumber.Should().Be("417523");
        result.Retried.Should().Equal("http_503");
    }

    [Fact(DisplayName = "13. 503 mãi → dừng sau MaxRetries (mặc định 1) rồi trả lỗi, để lùi về OCR.space")]
    public async Task Overloaded_GivesUpAfterMaxRetries()
    {
        var handler = new SequenceHandler((HttpStatusCode.ServiceUnavailable, Overloaded));

        var result = await Reader(handler).TryReadAsync(Jpeg);

        handler.Calls.Should().Be(2);
        result.Info.Should().BeNull();
        result.Error.Should().Be("http_503");
        result.Retried.Should().Equal("http_503");
    }

    [Theory(DisplayName = "14. 429 (hết quota), 400/403 (request/key sai) → KHÔNG gọi lại; MaxRetries=0 → tắt gọi lại")]
    [InlineData(HttpStatusCode.TooManyRequests, 1)]
    [InlineData(HttpStatusCode.BadRequest, 1)]
    [InlineData(HttpStatusCode.Forbidden, 1)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 0)]
    public async Task NonTransientOrRetryOff_IsNotRetried(HttpStatusCode status, int maxRetries)
    {
        var handler = new SequenceHandler((status, """{"error":{}}"""));

        var result = await Reader(handler, maxRetries).TryReadAsync(Jpeg);

        handler.Calls.Should().Be(1);
        result.Error.Should().Be($"http_{(int)status}");
        result.Retried.Should().BeEmpty();
    }

    // Gemini treo không trả lời: handler chờ tới khi HttpClient tự huỷ vì hết Timeout.
    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        }
    }

    [Fact(DisplayName = "11. Hết HttpClient.Timeout → \"timeout\", không ném exception (khác với user huỷ request)")]
    public async Task Timeout_IsReportedNotThrown()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:Enabled"] = "true", ["Gemini:ApiKey"] = "test-key",
        }).Build();
        var http = new HttpClient(new HangingHandler()) { Timeout = TimeSpan.FromMilliseconds(50) };
        var reader = new GeminiTicketReader(http, config, NullLogger<GeminiTicketReader>.Instance);

        var result = await reader.TryReadAsync(Jpeg);

        result.Info.Should().BeNull();
        result.Error.Should().Be("timeout");
        result.Retried.Should().BeEmpty("đã chờ hết Timeout rồi — gọi lại là bắt user chờ gấp đôi");
    }
}

// Gộp kết quả Gemini vào local khi local bật và Gemini là fallback — cùng luật với OCR.space.
public class MergeFromCloudInfoTests
{
    private static TicketInfo Gemini(string? number, DateOnly? date, string? province) => new()
    {
        TicketNumber = number, TicketNumberFromCloud = number != null,
        DrawDate = date, DrawDateVotes = date != null ? 1 : 0,
        Province = province, ProvinceExact = province != null,
        RawText = "{...}", OcrConfidence = GeminiTicketReader.AssumedConfidence,
    };

    [Fact(DisplayName = "1. Số vé local rủi ro → lấy số Gemini; đài local gần đúng → lấy đài Gemini; confidence giữ của local")]
    public void RiskyLocal_TakesGeminiNumberAndProvince()
    {
        var local = new TicketInfo
        {
            TicketNumber = "200921", TicketNumberNormalized = true,
            DrawDate = new DateOnly(2026, 6, 5), DrawDateVotes = 2,
            Province = "BinhDinh", ProvinceExact = false, OcrConfidence = 0.7,
        };

        var merged = TicketTextParser.MergeFromCloudInfo(local,
            Gemini("288921", new DateOnly(2026, 6, 4), "BinhDuong"), preferCloudNumber: true);

        merged.TicketNumber.Should().Be("288921");
        merged.TicketNumberFromCloud.Should().BeTrue();
        merged.TicketNumberNormalized.Should().BeFalse();
        merged.Province.Should().Be("BinhDuong");
        merged.ProvinceExact.Should().BeTrue();
        merged.DrawDate.Should().Be(new DateOnly(2026, 6, 5), "ngày local đã có và không ai cho phép thay");
        merged.OcrConfidence.Should().Be(0.7);
        merged.CloudText.Should().Be("{...}");
    }

    [Fact(DisplayName = "2. Số vé local chắc (preferCloudNumber=false) và đài local nguyên văn → giữ nguyên local")]
    public void SureLocal_IsKept()
    {
        var local = new TicketInfo { TicketNumber = "388003", Province = "TPHCM", ProvinceExact = true };

        var merged = TicketTextParser.MergeFromCloudInfo(local, Gemini("388008", null, "HauGiang"),
                                                         preferCloudNumber: false);

        merged.TicketNumber.Should().Be("388003");
        merged.TicketNumberFromCloud.Should().BeFalse();
        merged.Province.Should().Be("TPHCM");
    }

    [Fact(DisplayName = "3. Ngày local trống → lấy ngày Gemini; ngày local có → chỉ thay khi replaceDateIf cho phép")]
    public void Date_ReplacedOnlyWhenAllowed()
    {
        var geminiDate = new DateOnly(2026, 9, 25);

        TicketTextParser.MergeFromCloudInfo(new TicketInfo(), Gemini(null, geminiDate, null))
            .DrawDate.Should().Be(geminiDate);

        var oldDate = new DateOnly(2025, 9, 25);
        TicketTextParser.MergeFromCloudInfo(new TicketInfo { DrawDate = oldDate }, Gemini(null, geminiDate, null),
                                            replaceDateIf: _ => false)
            .DrawDate.Should().Be(oldDate);
        TicketTextParser.MergeFromCloudInfo(new TicketInfo { DrawDate = oldDate }, Gemini(null, geminiDate, null),
                                            replaceDateIf: _ => true)
            .DrawDate.Should().Be(geminiDate);
    }
}

// Khi local tắt, ảnh FE gửi đi thẳng cho cloud — chỉ khi chắc chắn ảnh đã "sạch" như FE gửi.
public class CanSendAsIsTests
{
    private static byte[] Jpeg(int width, int height, ushort? orientation = null)
    {
        using var img = new Image<Rgba32>(width, height, Color.White);
        if (orientation is { } o)
        {
            img.Metadata.ExifProfile = new ExifProfile();
            img.Metadata.ExifProfile.SetValue(ExifTag.Orientation, o);
        }
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms, new JpegEncoder { Quality = 80 });
        return ms.ToArray();
    }

    [Fact(DisplayName = "1. JPEG ≤1600px, không cờ xoay (đúng thứ FE gửi) → gửi nguyên")]
    public void FrontendJpeg_IsSentAsIs() => ImagePreprocessor.CanSendAsIs(Jpeg(1200, 1600)).Should().BeTrue();

    [Fact(DisplayName = "2. EXIF Orientation=1 (bình thường) → vẫn gửi nguyên")]
    public void NormalOrientation_IsSentAsIs() => ImagePreprocessor.CanSendAsIs(Jpeg(800, 600, 1)).Should().BeTrue();

    [Fact(DisplayName = "3. Còn cờ xoay EXIF (ảnh gốc điện thoại) → phải xử lý lại")]
    public void RotatedExif_IsNotSentAsIs() => ImagePreprocessor.CanSendAsIs(Jpeg(800, 600, 6)).Should().BeFalse();

    [Fact(DisplayName = "4. Rộng hơn 1600px → phải thu nhỏ trước")]
    public void TooWide_IsNotSentAsIs() => ImagePreprocessor.CanSendAsIs(Jpeg(2000, 1000)).Should().BeFalse();

    [Fact(DisplayName = "5. PNG hoặc dữ liệu hỏng → không gửi nguyên")]
    public void NonJpeg_IsNotSentAsIs()
    {
        using var img = new Image<Rgba32>(100, 100, Color.White);
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        ImagePreprocessor.CanSendAsIs(ms.ToArray()).Should().BeFalse();
        ImagePreprocessor.CanSendAsIs([1, 2, 3, 4]).Should().BeFalse();
    }
}
