using Dashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace Dashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalyticsController : ControllerBase
{
    private readonly AnalyticsService _svc;

    public AnalyticsController(AnalyticsService svc) => _svc = svc;

    [HttpGet("summary/{businessId}")]
    public async Task<IActionResult> GetSummary(string businessId, CancellationToken ct)
    {
        try   { return Ok(await _svc.GetSentimentSummaryAsync(businessId)); }
        catch (FileNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (Exception ex)             { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("monthly-trend/{businessId}")]
    public async Task<IActionResult> GetMonthlyTrend(string businessId)
    {
        try   { return Ok(await _svc.GetMonthlyTrendAsync(businessId)); }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("heatmap/{businessId}")]
    public async Task<IActionResult> GetHeatmap(string businessId)
    {
        try   { return Ok(await _svc.GetHeatmapAsync(businessId)); }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("rating-trend/{businessId}")]
    public async Task<IActionResult> GetRatingTrend(string businessId)
    {
        try   { return Ok(await _svc.GetRatingTrendAsync(businessId)); }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("top-reviewers/{businessId}")]
    public async Task<IActionResult> GetTopReviewers(string businessId)
    {
        try   { return Ok(await _svc.GetTopReviewersAsync(businessId)); }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("user-scatter/{businessId}")]
    public async Task<IActionResult> GetUserScatter(string businessId)
    {
        try   { return Ok(await _svc.GetUserScatterAsync(businessId)); }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("usefulness/{businessId}")]
    public async Task<IActionResult> GetUsefulness(string businessId)
    {
        try   { return Ok(await _svc.GetUsefulnessAsync(businessId)); }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("length-distribution/{businessId}")]
    public async Task<IActionResult> GetLengthDistribution(string businessId)
    {
        try   { return Ok(await _svc.GetLengthDistributionAsync(businessId)); }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("result-files")]
    public async Task<IActionResult> GetResultFiles()
    {
        try   { return Ok(await _svc.GetResultFilesAsync()); }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }
}
