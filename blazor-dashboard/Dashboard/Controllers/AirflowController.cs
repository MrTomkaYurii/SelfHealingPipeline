using System.Text.Json;
using Dashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace Dashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AirflowController : ControllerBase
{
    private readonly AirflowService      _svc;
    private readonly IHttpClientFactory  _http;
    private readonly IConfiguration      _config;

    private static readonly string DagId = "business_analysis_pipeline";

    public AirflowController(AirflowService svc, IHttpClientFactory http, IConfiguration config)
    {
        _svc    = svc;
        _http   = http;
        _config = config;
    }

    // ── Airflow availability ─────────────────────────────────────────────────

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var url = (_config["AirflowUrl"] ?? "http://localhost:8080").TrimEnd('/');
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var client = _http.CreateClient();
            await client.GetAsync(url, cts.Token);
            return Ok(new { available = true, url });
        }
        catch
        {
            return Ok(new { available = false, url });
        }
    }

    // ── Trigger DAG ──────────────────────────────────────────────────────────

    [HttpPost("trigger-dag")]
    public async Task<IActionResult> TriggerDag([FromBody] TriggerDagRequest req, CancellationToken ct)
    {
        try
        {
            var fileName      = Path.GetFileName(req.FilePath);
            var containerPath = $"/opt/airflow/input/{fileName}";

            var conf = new
            {
                input_file   = containerPath,
                ollama_model = req.OllamaModel,
                batch_size   = req.BatchSize > 0 ? req.BatchSize : 0,
                offset       = req.Offset
            };

            var (runId, startTime) = await _svc.TriggerDagAsync(DagId, conf, ct);
            return Ok(new { runId, startTime = startTime.ToString("o"), error = "" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { runId = "", startTime = "", error = ex.Message });
        }
    }

    // ── Run status ───────────────────────────────────────────────────────────

    [HttpGet("dag-run/{runId}")]
    public async Task<IActionResult> GetDagRun(string runId, CancellationToken ct)
    {
        try
        {
            var info = await _svc.GetDagRunAsync(DagId, runId, ct);
            return Ok(new
            {
                runId     = info.RunId,
                state     = info.State,
                startDate = info.StartDate?.ToString("o"),
                endDate   = info.EndDate?.ToString("o"),
                error     = ""
            });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    // ── Task instances ───────────────────────────────────────────────────────

    [HttpGet("task-instances/{runId}")]
    public async Task<IActionResult> GetTaskInstances(string runId, CancellationToken ct)
    {
        try
        {
            var tasks = await _svc.GetTaskInstancesAsync(DagId, runId, ct);
            return Ok(tasks.Select(t => new
            {
                taskId    = t.TaskId,
                state     = t.State,
                startDate = t.StartDate?.ToString("o"),
                endDate   = t.EndDate?.ToString("o")
            }));
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    // ── Task logs + progress ─────────────────────────────────────────────────

    [HttpGet("task-logs/{runId}/{taskId}")]
    public async Task<IActionResult> GetTaskLogs(string runId, string taskId, CancellationToken ct)
    {
        try
        {
            var logs               = await _svc.GetTaskLogsAsync(DagId, runId, taskId, 1, ct);
            var (current, total)   = _svc.ParseAnalysisProgress(logs);
            var lastLines          = logs.Split('\n')
                                        .Where(l => !string.IsNullOrWhiteSpace(l))
                                        .TakeLast(20)
                                        .ToList();
            return Ok(new { content = logs, current, total, lastLines, error = "" });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    // ── Cancel ───────────────────────────────────────────────────────────────

    [HttpPost("cancel/{runId}")]
    public async Task<IActionResult> Cancel(string runId, CancellationToken ct)
    {
        try
        {
            await _svc.CancelDagRunAsync(DagId, runId, ct);
            return Ok();
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    // ── Output file ──────────────────────────────────────────────────────────

    [HttpGet("output-file/{businessId}")]
    public IActionResult GetOutputFile(string businessId)
    {
        var filePath = _svc.GetOutputFilePath(businessId);
        if (string.IsNullOrEmpty(filePath))
            return Ok(new { filePath = "", fileName = "", error = "Файл результату ще не створено" });

        return Ok(new { filePath, fileName = Path.GetFileName(filePath), error = "" });
    }
}

public class TriggerDagRequest
{
    public string FilePath    { get; set; } = "";
    public string OllamaModel { get; set; } = "llama3.2";
    public int    LineCount   { get; set; }
    public int    BatchSize   { get; set; } = 0;
    public int    Offset      { get; set; } = 0;
}
