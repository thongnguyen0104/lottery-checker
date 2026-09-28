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
        book.Entries.Should().HaveCount(40);
        book.Keys.Should().OnlyHaveUniqueItems();
        book.Find("ran")!.Numbers.Should().Equal("32", "72");
    }

    [Fact]
    public async Task Numbers_come_from_book_not_from_ai()
    {
        // AI cố nhét số vào summary và trả thêm khoá lạ — số vẫn phải tra từ sổ mơ, khoá lạ bị bỏ.
        var (sut, _) = Create(GeminiBody(new { summary = "Rắn trắng, số 47", keys = new[] { "ran", "khong_co", "meo_nha" }, explanation = "..." }));

        var r = await sut.InterpretAsync("Tôi mơ thấy con trăn trắng và con mèo");

        r.Source.Should().Be("ai");
        r.Entries.Select(e => e.Key).Should().Equal("ran", "meo_nha");
        r.MainNumber.Should().Be("32");
        r.SecondaryNumbers.Should().Equal("72", "14", "54", "94");
    }

    [Fact]
    public async Task Request_sends_book_keys_but_not_numbers()
    {
        var (sut, handler) = Create(GeminiBody(new { summary = "", keys = Array.Empty<string>(), explanation = "" }));
        await sut.InterpretAsync("mơ thấy rắn");

        handler.RequestBody.Should().Contain("- ran: R").And.Contain("\"enum\"").And.NotContain("\"72\"");
    }

    [Fact]
    public async Task Gemini_error_falls_back_to_local_matching()
    {
        var (sut, _) = Create("{}", HttpStatusCode.TooManyRequests);

        var r = await sut.InterpretAsync("Đêm qua tôi mơ thấy mèo rừng đuổi con gà");

        r.Source.Should().Be("local");
        r.AiError.Should().Be("http_429");
        // "mèo rừng" khớp mục dài, không ra thêm "mèo nhà".
        r.Entries.Select(e => e.Key).Should().Equal("meo_rung", "ga");
        r.MainNumber.Should().Be("18");
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
        await sut.InterpretAsync("mơ thấy rắn");
        await sut.InterpretAsync("  Mơ   thấy rắn ");
        handler.Calls.Should().Be(1);
    }
}
