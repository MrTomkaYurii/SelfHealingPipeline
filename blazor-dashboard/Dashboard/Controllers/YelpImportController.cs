using Dashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace Dashboard.Controllers;

[ApiController]
[Route("api/yelp-import")]
public class YelpImportController : ControllerBase
{
    private readonly YelpImportService _svc;

    public YelpImportController(YelpImportService svc) => _svc = svc;

    [HttpGet("status")]
    public IActionResult Status() => Ok(_svc.GetStatus());

    [HttpPost("start")]
    public IActionResult Start([FromQuery] bool reset = false)
    {
        bool started = _svc.TryStart(reset);
        return started ? Ok() : Conflict(new { error = "Імпорт вже виконується" });
    }

    [HttpPost("cancel")]
    public IActionResult Cancel() { _svc.Cancel(); return Ok(); }

    [HttpPost("reset")]
    public IActionResult Reset() { _svc.Reset(); return Ok(); }
}
