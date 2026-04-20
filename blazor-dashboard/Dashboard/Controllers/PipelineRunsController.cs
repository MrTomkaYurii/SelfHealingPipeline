using Microsoft.AspNetCore.Mvc;
using Dashboard.Client.Models;
using System.Text.Json;

namespace Dashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PipelineRunsController : ControllerBase
{
    private readonly string _outputPath;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public PipelineRunsController(IConfiguration configuration, IWebHostEnvironment env)
    {
        var configPath = configuration["PipelineOutputPath"] ?? "../../airflow-pipeline/output";
        _outputPath = Path.IsPathRooted(configPath)
            ? configPath
            : Path.GetFullPath(Path.Combine(env.ContentRootPath, configPath));
    }

    [HttpGet]
    public async Task<ActionResult<List<PipelineRunSummary>>> GetRuns()
    {
        var summaries = new List<PipelineRunSummary>();
        if (!Directory.Exists(_outputPath))
            return Ok(summaries);

        foreach (var file in Directory.GetFiles(_outputPath, "*.json").OrderByDescending(f => f))
        {
            try
            {
                var json = await System.IO.File.ReadAllTextAsync(file);
                var run = JsonSerializer.Deserialize<PipelineRun>(json, JsonOptions);
                if (run == null) continue;

                var rate = run.Rates.SuccessRate;
                summaries.Add(new PipelineRunSummary
                {
                    FileName = Path.GetFileName(file),
                    Timestamp = run.RunInfo.Timestamp,
                    BatchSize = run.RunInfo.BatchSize,
                    Offset = run.RunInfo.Offset,
                    Processed = run.Totals.Processed,
                    Success = run.Totals.Success,
                    Healed = run.Totals.Healed,
                    Degraded = run.Totals.Degraded,
                    SuccessRate = rate,
                    HealthStatus = rate >= 0.95 ? "HEALTHY" : rate >= 0.80 ? "WARNING" : "CRITICAL"
                });
            }
            catch { /* skip malformed files */ }
        }
        return Ok(summaries);
    }

    [HttpGet("{filename}")]
    public async Task<ActionResult<PipelineRun>> GetRun(string filename)
    {
        var filePath = Path.Combine(_outputPath, filename);
        var fullPath = Path.GetFullPath(filePath);
        var fullOutput = Path.GetFullPath(_outputPath);

        if (!fullPath.StartsWith(fullOutput))
            return BadRequest("Invalid filename");
        if (!System.IO.File.Exists(filePath))
            return NotFound();

        var json = await System.IO.File.ReadAllTextAsync(filePath);
        var run = JsonSerializer.Deserialize<PipelineRun>(json, JsonOptions);
        return Ok(run);
    }
}
