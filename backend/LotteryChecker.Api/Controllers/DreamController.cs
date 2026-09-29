using LotteryChecker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LotteryChecker.Api.Controllers;

[ApiController]
[Authorize] // Luận số giấc mơ thuộc 6 Số May Mắn — chỉ cho tài khoản đã đăng nhập
public class DreamController : ControllerBase
{
    public const string RateLimitPolicy = "dream";
    private const int MaxMessageLength = 500;

    private readonly DreamInterpreter _interpreter;

    public DreamController(DreamInterpreter interpreter) => _interpreter = interpreter;

    public record DreamRequest(string? Message);

    /// <summary>Luận số giấc mơ → số tham khảo tra từ sổ mơ (xem <see cref="DreamInterpreter"/>).</summary>
    [HttpPost("/api/ai/dream")]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<ActionResult<DreamInterpreter.DreamResult>> Interpret(DreamRequest req, CancellationToken ct)
    {
        var message = req.Message?.Trim();
        if (string.IsNullOrEmpty(message))
            return BadRequest(new { error = "Bạn hãy kể lại giấc mơ trước đã." });
        if (message.Length > MaxMessageLength)
            return BadRequest(new { error = $"Mô tả dài quá, tối đa {MaxMessageLength} ký tự." });

        return await _interpreter.InterpretAsync(message, ct);
    }
}
