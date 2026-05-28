using Dashboard.Controllers;
using Dashboard.Client.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace Dashboard.Tests;

/// <summary>
/// Tests for PipelineRunsController — verifies health status logic,
/// JSON parsing, path traversal protection, and ordering.
/// </summary>
public class PipelineRunsControllerTests : IDisposable
{
    private readonly string _tempOutput;

    public PipelineRunsControllerTests()
    {
        _tempOutput = Path.Combine(Path.GetTempPath(), "PipelineRunsTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempOutput);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempOutput))
            Directory.Delete(_tempOutput, recursive: true);
    }

    private PipelineRunsController CreateController(string? outputPath = null)
    {
        var path = outputPath ?? _tempOutput;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PipelineOutputPath"] = path
            })
            .Build();

        var env = new FakeWebHostEnvironment(Path.GetTempPath());
        return new PipelineRunsController(config, env);
    }

    /// <summary>Writes a minimal valid pipeline-run JSON to the output directory.</summary>
    private string WriteRunFile(string fileName, double successRate,
        int processed = 100, int success = 95, int healed = 0, int degraded = 5)
    {
        var run = new
        {
            run_info = new
            {
                timestamp = "2024-01-15T10:30:00",
                batch_size = processed,
                offset = 0,
                input_file = "yelp.json"
            },
            totals = new
            {
                processed,
                success,
                healed,
                degraded
            },
            rates = new
            {
                success_rate = successRate,
                healing_rate = 0.0,
                degradation_rate = 1.0 - successRate
            },
            sentiment_distribution = new { POSITIVE = 50, NEGATIVE = 30, NEUTRAL = 20 },
            healing_statistics      = new { },
            star_sentiment_correlation = new { },
            average_confidence = new { success = 0.9, healed = 0.8, degraded = 0.5 },
            results = Array.Empty<object>()
        };

        var path = Path.Combine(_tempOutput, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(run));
        return path;
    }

    // ── GetRuns — missing directory ───────────────────────────────────────────

    [Fact]
    public async Task GetRuns_ReturnsEmptyList_WhenDirectoryMissing()
    {
        var controller = CreateController(Path.Combine(Path.GetTempPath(), "no-such-dir-" + Guid.NewGuid()));
        var result = await controller.GetRuns();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<PipelineRunSummary>>(ok.Value);
        Assert.Empty(list);
    }

    // ── GetRuns — JSON parsing ────────────────────────────────────────────────

    [Fact]
    public async Task GetRuns_ParsesSingleFile_Correctly()
    {
        WriteRunFile("run_001.json", successRate: 0.97,
            processed: 100, success: 97, healed: 2, degraded: 3);

        var result = await controller.GetRuns();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<PipelineRunSummary>>(ok.Value);

        Assert.Single(list);
        var s = list[0];
        Assert.Equal("run_001.json", s.FileName);
        Assert.Equal(100, s.Processed);
        Assert.Equal(97, s.Success);
        Assert.Equal(2, s.Healed);
        Assert.Equal(3, s.Degraded);
        Assert.Equal(0.97, s.SuccessRate, precision: 10);
    }

    // ── GetRuns — health status thresholds ───────────────────────────────────

    [Theory]
    [InlineData(1.00, "HEALTHY")]
    [InlineData(0.97, "HEALTHY")]
    [InlineData(0.95, "HEALTHY")]   // boundary: exactly 0.95 → HEALTHY
    [InlineData(0.94, "WARNING")]
    [InlineData(0.85, "WARNING")]
    [InlineData(0.80, "WARNING")]   // boundary: exactly 0.80 → WARNING
    [InlineData(0.79, "CRITICAL")]
    [InlineData(0.50, "CRITICAL")]
    [InlineData(0.00, "CRITICAL")]
    public async Task GetRuns_HealthStatus_MatchesThresholds(double rate, string expectedStatus)
    {
        // Clean directory between theory runs
        foreach (var f in Directory.GetFiles(_tempOutput)) File.Delete(f);

        WriteRunFile($"run_{rate:F2}.json", successRate: rate);

        var result = await controller.GetRuns();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<PipelineRunSummary>>(ok.Value);

        Assert.Single(list);
        Assert.Equal(expectedStatus, list[0].HealthStatus);
    }

    // ── GetRuns — malformed JSON skipped ─────────────────────────────────────

    [Fact]
    public async Task GetRuns_SkipsMalformedJson_Silently()
    {
        File.WriteAllText(Path.Combine(_tempOutput, "bad.json"), "{ not valid json !!!");
        WriteRunFile("good.json", successRate: 0.95);

        var result = await controller.GetRuns();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<PipelineRunSummary>>(ok.Value);

        // Only the valid file should appear
        Assert.Single(list);
        Assert.Equal("good.json", list[0].FileName);
    }

    [Fact]
    public async Task GetRuns_SkipsNullDeserializedFile_Silently()
    {
        // JSON that parses but deserializes to null-like structure
        File.WriteAllText(Path.Combine(_tempOutput, "null.json"), "null");
        WriteRunFile("valid.json", successRate: 0.90);

        var result = await controller.GetRuns();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<PipelineRunSummary>>(ok.Value);

        Assert.Single(list);
        Assert.Equal("valid.json", list[0].FileName);
    }

    // ── GetRuns — ordering ────────────────────────────────────────────────────

    [Fact]
    public async Task GetRuns_ReturnsFiles_OrderedByFileNameDescending()
    {
        WriteRunFile("run_2024_01_01.json", successRate: 0.90);
        WriteRunFile("run_2024_06_15.json", successRate: 0.85);
        WriteRunFile("run_2024_03_10.json", successRate: 0.95);

        var result = await controller.GetRuns();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<PipelineRunSummary>>(ok.Value);

        Assert.Equal(3, list.Count);
        Assert.Equal("run_2024_06_15.json", list[0].FileName);
        Assert.Equal("run_2024_03_10.json", list[1].FileName);
        Assert.Equal("run_2024_01_01.json", list[2].FileName);
    }

    // ── GetRun — path traversal protection ───────────────────────────────────

    [Theory]
    [InlineData("../secret.json")]
    [InlineData("..\\secret.json")]
    [InlineData("../../etc/passwd")]
    public async Task GetRun_ReturnsBadRequest_ForPathTraversal(string maliciousName)
    {
        var result = await controller.GetRun(maliciousName);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── GetRun — file not found ───────────────────────────────────────────────

    [Fact]
    public async Task GetRun_ReturnsNotFound_WhenFileMissing()
    {
        var result = await controller.GetRun("nonexistent.json");
        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── GetRun — valid file ───────────────────────────────────────────────────

    [Fact]
    public async Task GetRun_ReturnsPipelineRun_ForValidFile()
    {
        WriteRunFile("run_detail.json", successRate: 0.92,
            processed: 50, success: 46, healed: 3, degraded: 4);

        var result = await controller.GetRun("run_detail.json");
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var run = Assert.IsType<PipelineRun>(ok.Value);

        Assert.Equal(50, run.Totals.Processed);
        Assert.Equal(46, run.Totals.Success);
        Assert.Equal(3, run.Totals.Healed);
        Assert.Equal(4, run.Totals.Degraded);
        Assert.Equal(0.92, run.Rates.SuccessRate, precision: 10);
    }

    // ── convenience: controller instance with default output path ─────────────

    private PipelineRunsController? _lazyController;

    private PipelineRunsController controller =>
        _lazyController ??= CreateController();
}
