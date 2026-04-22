using Dashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Dashboard.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthDbService auth) : ControllerBase
{
    public record RegisterRequest(string Username, string Password);
    public record LoginRequest(string Username, string Password);

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        var (ok, error) = await auth.RegisterAsync(req.Username, req.Password);
        if (!ok) return BadRequest(new { error });
        return Ok(new { message = "Реєстрацію успішно завершено" });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var (ok, token, error) = await auth.LoginAsync(req.Username, req.Password);
        if (!ok) return Unauthorized(new { error });
        return Ok(new { token, username = req.Username.Trim() });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var token = Request.Headers["Authorization"].ToString()["Bearer ".Length..].Trim();
        await auth.RevokeTokenAsync(token);
        return Ok(new { message = "Вихід виконано" });
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var username = User.FindFirstValue(ClaimTypes.Name) ?? "";
        return Ok(new { username });
    }
}
