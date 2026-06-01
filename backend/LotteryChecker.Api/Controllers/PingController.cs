using Microsoft.AspNetCore.Mvc;

namespace LotteryChecker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { message = "pong", time = DateTime.UtcNow });
}