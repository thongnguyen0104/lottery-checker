using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using LotteryChecker.Api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LotteryChecker.Tests;

// Nguyên tắc của Luận số: con số chỉ đến từ sổ mơ, AI chỉ chọn mục. Sai ở đây là AI bịa được số,
// hoặc Gemini lỗi làm vỡ cả tính năng.
public class DreamInterpreterTests
{
    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            RequestBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static string GeminiBody(object answer) => JsonSerializer.Serialize(new
    {
        candidates = new[] { new { content = new { parts = new[] { new { text = JsonSerializer.Serialize(answer) } } } } },
    });

    private static (DreamInterpreter, FakeHandler) Create(string body, HttpStatusCode status = HttpStatusCode.OK,
                                                          string? apiKey = "test-key")
    {
        var handler = new FakeHandler(status, body);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = apiKey,
            ["Gemini:RetryDelayMs"] = "0",
        }).Build();
        var interpreter = new DreamInterpreter(new HttpClient(handler), new DreamBook(),
            new MemoryCache(new MemoryCacheOptions()), config, NullLogger<DreamInterpreter>.Instance);
        return (interpreter, handler);
    }

    [Fact]
    public void Embedded_book_loads_with_unique_keys_and_two_digit_numbers()
    {
        var book = new DreamBook();
        book.Entries.Should().HaveCount(134);   // 40 canonical + 94 mở rộng có số ("ma" không số bị bỏ)
        book.Keys.Should().OnlyHaveUniqueItems();
        book.Find("ran")!.Numbers.Should().Equal("32", "72");
    }

    [Fact]
    public async Task Numbers_come_from_book_not_from_ai()
    {
        // AI cố nhét số vào summary và trả thêm khoá lạ — số vẫn phải tra từ sổ mơ, khoá lạ bị bỏ.
        var (sut, _) = Create(GeminiBody(new { summary = "Rắn trắng, số 47", keys = new[] { "ran", "khong_co", "meo_nha" }, explanation = "..." }));

        var r = await sut.InterpretAsync("Tôi thấy một thứ dài dài trườn qua sân");

        r.Source.Should().Be("ai");
        r.Entries.Select(e => e.Key).Should().Equal("ran", "meo_nha");
        r.MainNumber.Should().Be("32");
        r.SecondaryNumbers.Should().Equal("72", "14", "54", "94");
    }

    [Fact]
    public async Task Request_sends_book_labels_only()
    {
        var (sut, handler) = Create(GeminiBody(new { summary = "", keys = Array.Empty<string>(), explanation = "" }));
        await sut.InterpretAsync("mơ thấy thứ lạ");

        // Sổ mơ chỉ đi qua enum tên mục — không kèm khoá, alias hay số (tiết kiệm token, AI không thấy số).
        var body = System.Text.RegularExpressions.Regex.Unescape(handler.RequestBody!);
        body.Should().Contain("\"Rắn\"").And.Contain("\"enum\"")
            .And.NotContain("meo_nha").And.NotContain("rắn hổ mang").And.NotContain("\"72\"");
    }

    [Fact]
    public async Task Local_match_is_used_first_without_calling_gemini()
    {
        var (sut, handler) = Create(GeminiBody(new { summary = "", keys = new[] { "ran" }, explanation = "" }));

        var r = await sut.InterpretAsync("Đêm qua tôi mơ thấy mèo rừng đuổi con gà");

        handler.Calls.Should().Be(0);
        r.Source.Should().Be("local");
        // "mèo rừng" khớp mục dài, không ra thêm "mèo nhà".
        r.Entries.Select(e => e.Key).Should().Equal("meo_rung", "ga");
        r.MainNumber.Should().Be("18");
    }

    [Fact]
    public async Task Gemini_error_with_no_local_match_gives_no_number()
    {
        var (sut, _) = Create("{}", HttpStatusCode.TooManyRequests);

        var r = await sut.InterpretAsync("Tôi thấy một thứ dài dài trườn qua sân");

        r.Source.Should().Be("local");
        r.AiError.Should().Be("http_429");
        r.MainNumber.Should().BeNull();
    }

    // Trả lời theo model trong URL: model chính lỗi / không chọn được mục → hỏi model dự phòng.
    private sealed class PerModelHandler(Dictionary<string, (HttpStatusCode, string)> byModel) : HttpMessageHandler
    {
        public List<string> Models { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var model = byModel.Keys.First(m => request.RequestUri!.AbsolutePath.Contains($"/models/{m}:"));
            Models.Add(model);
            var (status, body) = byModel[model];
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]   // model chính lỗi
    [InlineData(HttpStatusCode.OK)]                   // model chính trả lời nhưng không chọn được mục
    public async Task Falls_back_to_next_model_when_primary_fails_or_finds_nothing(HttpStatusCode primaryStatus)
    {
        var handler = new PerModelHandler(new()
        {
            ["main"] = (primaryStatus, GeminiBody(new { summary = "?", keys = Array.Empty<string>(), explanation = "" })),
            ["backup"] = (HttpStatusCode.OK, GeminiBody(new { summary = "Rắn", keys = new[] { "ran" }, explanation = "" })),
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = "k",
            ["Gemini:Model"] = "main",
            ["Gemini:MaxRetries"] = "0",
            ["DreamChat:FallbackModels:0"] = "backup",
        }).Build();
        var sut = new DreamInterpreter(new HttpClient(handler), new DreamBook(),
            new MemoryCache(new MemoryCacheOptions()), config, NullLogger<DreamInterpreter>.Instance);

        var r = await sut.InterpretAsync("Tôi thấy một thứ dài dài trườn qua sân");

        handler.Models.Should().Equal("main", "backup");
        r.Source.Should().Be("ai");
        r.MainNumber.Should().Be("32");
    }

    [Fact]
    public async Task DreamChat_model_overrides_scan_model()
    {
        var handler = new PerModelHandler(new()
        {
            ["dream"] = (HttpStatusCode.OK, GeminiBody(new { summary = "Rắn", keys = new[] { "ran" }, explanation = "" })),
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = "k",
            ["Gemini:Model"] = "scan",
            ["DreamChat:Model"] = "dream",
        }).Build();
        var sut = new DreamInterpreter(new HttpClient(handler), new DreamBook(),
            new MemoryCache(new MemoryCacheOptions()), config, NullLogger<DreamInterpreter>.Instance);

        await sut.InterpretAsync("Tôi thấy một thứ dài dài trườn qua sân");

        handler.Models.Should().Equal("dream");
    }

    [Fact]
    public async Task No_key_uses_local_only_and_never_invents_number()
    {
        var (sut, handler) = Create("{}", apiKey: null);

        var r = await sut.InterpretAsync("Hôm nay trời đẹp, tôi thấy con rắn");

        handler.Calls.Should().Be(0);
        r.Source.Should().Be("local");
        r.AiError.Should().BeNull();
        r.Entries.Select(e => e.Key).Should().Equal("ran");
    }

    [Fact]
    public async Task Unmatched_message_gives_no_number()
    {
        var (sut, _) = Create("{}", apiKey: null);
        var r = await sut.InterpretAsync("Tôi đi làm muộn");
        r.Entries.Should().BeEmpty();
        r.MainNumber.Should().BeNull();
    }

    [Fact]
    public async Task Same_message_is_cached()
    {
        var (sut, handler) = Create(GeminiBody(new { summary = "Rắn", keys = new[] { "ran" }, explanation = "" }));
        await sut.InterpretAsync("mơ thấy thứ lạ");
        await sut.InterpretAsync("  Mơ   thấy thứ lạ ");
        handler.Calls.Should().Be(1);
    }
}
