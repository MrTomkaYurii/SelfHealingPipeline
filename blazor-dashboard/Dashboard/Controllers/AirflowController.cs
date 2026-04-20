using Microsoft.AspNetCore.Mvc;

namespace Dashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AirflowController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;

    public AirflowController(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _config = config;
    }

    [HttpGet("status")]
    public async Task<ActionResult<AirflowStatusResult>> GetStatus()
    {
        var url = _config["AirflowUrl"] ?? "http://localhost:8080/";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var client = _httpFactory.CreateClient();
            var response = await client.GetAsync(url, cts.Token);
            return Ok(new AirflowStatusResult { Available = true, Url = url });
        }
        catch
        {
            return Ok(new AirflowStatusResult { Available = false, Url = url });
        }
    }
}

public record AirflowStatusResult
{
    public bool Available { get; init; }
    public string Url { get; init; } = "";
}
