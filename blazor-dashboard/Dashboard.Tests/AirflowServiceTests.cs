using Dashboard.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Dashboard.Tests;

/// <summary>
/// Tests for AirflowService — focuses on methods that don't require
/// a live Airflow instance: ParseAnalysisProgress and GetOutputFilePath.
/// </summary>
public class AirflowServiceTests
{
    private AirflowService CreateService(Dictionary<string, string?>? config = null)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? new Dictionary<string, string?>
            {
                ["AirflowUrl"] = "http://localhost:8080",
                ["PipelineBasePath"] = "../../airflow-pipeline"
            })
            .Build();

        var factory = new FakeHttpClientFactory();
        var env = new FakeWebHostEnvironment(Path.GetTempPath());
        return new AirflowService(factory, cfg, env);
    }

    // ── ParseAnalysisProgress ─────────────────────────────────────────────────

    [Fact]
    public void ParseAnalysisProgress_ReturnsZero_WhenNoMatch()
    {
        var svc = CreateService();
        var (current, total) = svc.ParseAnalysisProgress("No progress info here.");
        Assert.Equal(0, current);
        Assert.Equal(0, total);
    }

    [Fact]
    public void ParseAnalysisProgress_ParsesSingleEntry()
    {
        var svc = CreateService();
        var (current, total) = svc.ParseAnalysisProgress("Analyzed 42/100 reviews");
        Assert.Equal(42, current);
        Assert.Equal(100, total);
    }

    [Fact]
    public void ParseAnalysisProgress_ReturnsLastEntry_WhenMultiple()
    {
        var svc = CreateService();
        var logs = "Analyzed 10/100 reviews\nAnalyzed 50/100 reviews\nAnalyzed 100/100 reviews";
        var (current, total) = svc.ParseAnalysisProgress(logs);
        Assert.Equal(100, current);
        Assert.Equal(100, total);
    }

    [Fact]
    public void ParseAnalysisProgress_ReturnsZero_ForEmptyString()
    {
        var svc = CreateService();
        var (current, total) = svc.ParseAnalysisProgress(string.Empty);
        Assert.Equal(0, current);
        Assert.Equal(0, total);
    }

    [Fact]
    public void ParseAnalysisProgress_HandlesLargeNumbers()
    {
        var svc = CreateService();
        var (current, total) = svc.ParseAnalysisProgress("Analyzed 9999/10000 reviews");
        Assert.Equal(9999, current);
        Assert.Equal(10000, total);
    }

    // ── GetOutputFilePath ─────────────────────────────────────────────────────

    [Fact]
    public void GetOutputFilePath_ReturnsEmpty_WhenFileDoesNotExist()
    {
        var svc = CreateService();
        var path = svc.GetOutputFilePath("nonexistent-business-id");
        Assert.Equal(string.Empty, path);
    }

    [Fact]
    public void GetOutputFilePath_ReturnsPath_WhenFileExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "airflow-output-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(tempDir, "output"));
        var businessId = "test123";
        var filePath = Path.Combine(tempDir, "output", $"business-analysis_{businessId}-result.json");
        File.WriteAllText(filePath, "{}");

        try
        {
            var svc = CreateService(new Dictionary<string, string?>
            {
                ["AirflowUrl"] = "http://localhost:8080",
                ["PipelineBasePath"] = tempDir
            });

            var result = svc.GetOutputFilePath(businessId);
            Assert.NotEmpty(result);
            Assert.Contains(businessId, result);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}

/// <summary>Minimal IHttpClientFactory for unit tests.</summary>
internal class FakeHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new HttpClient();
}
