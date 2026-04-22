using Dashboard.Client.Models;
using Dashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace Dashboard.Controllers;

[ApiController]
[Route("api/business-analysis")]
public class BusinessAnalysisController : ControllerBase
{
    private readonly BusinessService _svc;
    public BusinessAnalysisController(BusinessService svc) => _svc = svc;

    [HttpGet("businesses")]
    public async Task<IActionResult> GetBusinesses(
        [FromQuery] string? search = null,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        try
        {
            var list = await _svc.GetBusinessesAsync(search, Math.Clamp(limit, 1, 500), ct);
            return Ok(list);
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("export/{businessId}")]
    public async Task<IActionResult> Export(string businessId, CancellationToken ct)
    {
        try
        {
            var reviews = await _svc.GetReviewsByBusinessIdAsync(businessId, ct);
            if (reviews.Count == 0)
                return Ok(new ExportResultDto { Error = "Для цього бізнесу немає відгуків" });

            var filePath = await _svc.SaveReviewsToJsonAsync(businessId, reviews, ct);
            return Ok(new ExportResultDto
            {
                FilePath    = filePath,
                FileName    = Path.GetFileName(filePath),
                ReviewCount = reviews.Count,
                CreatedAt   = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { error = "Запит скасовано" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
