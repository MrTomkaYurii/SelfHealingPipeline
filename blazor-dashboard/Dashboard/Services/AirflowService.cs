using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dashboard.Services;

public record DagRunInfo(string RunId, string State, DateTime? StartDate, DateTime? EndDate);
public record TaskInfo(string TaskId, string State, DateTime? StartDate, DateTime? EndDate);

public class AirflowService
{
    private readonly IHttpClientFactory  _factory;
    private readonly IConfiguration     _config;
    private readonly IWebHostEnvironment _env;

    // Кешований Bearer-токен (Airflow 3.x SimpleAuthManager)
    private static string?  _token;
    private static DateTime _tokenExpiry = DateTime.MinValue;
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public AirflowService(IHttpClientFactory factory, IConfiguration config, IWebHostEnvironment env)
    {
        _factory = factory;
        _config  = config;
        _env     = env;
    }

    private string Base => (_config["AirflowUrl"] ?? "http://localhost:8080").TrimEnd('/');

    // ── Trigger ──────────────────────────────────────────────────────────────

    public async Task<(string runId, DateTime startTime)> TriggerDagAsync(
        string dagId, object conf, CancellationToken ct = default)
    {
        var client = await AuthClientAsync(ct);
        var body   = JsonSerializer.Serialize(new { logical_date = DateTime.UtcNow.ToString("o"), conf });
        var resp   = await client.PostAsync($"{Base}/api/v2/dags/{dagId}/dagRuns",
                         new StringContent(body, Encoding.UTF8, "application/json"), ct);
        var json   = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Airflow {(int)resp.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var runId = (root.TryGetProperty("dag_run_id", out var r1) ? r1.GetString() : null)
                 ?? (root.TryGetProperty("run_id",     out var r2) ? r2.GetString() : null)
                 ?? throw new Exception("run_id відсутній у відповіді Airflow");

        var startTime = ParseDate(root, "start_date") ?? ParseDate(root, "logical_date") ?? DateTime.UtcNow;

        return (runId, startTime);
    }

    // ── Run status ───────────────────────────────────────────────────────────

    public async Task<DagRunInfo> GetDagRunAsync(
        string dagId, string runId, CancellationToken ct = default)
    {
        var client = await AuthClientAsync(ct);
        var resp   = await client.GetAsync($"{Base}/api/v2/dags/{dagId}/dagRuns/{runId}", ct);
        var json   = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"GetDagRun {(int)resp.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new DagRunInfo(
            RunId:     runId,
            State:     root.TryGetProperty("state", out var s) ? s.GetString() ?? "unknown" : "unknown",
            StartDate: ParseDate(root, "start_date"),
            EndDate:   ParseDate(root, "end_date")
        );
    }

    // ── Task instances ───────────────────────────────────────────────────────

    public async Task<List<TaskInfo>> GetTaskInstancesAsync(
        string dagId, string runId, CancellationToken ct = default)
    {
        var client = await AuthClientAsync(ct);
        var resp   = await client.GetAsync(
            $"{Base}/api/v2/dags/{dagId}/dagRuns/{runId}/taskInstances", ct);
        var json   = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode) return [];

        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement
            : doc.RootElement.TryGetProperty("task_instances", out var ti) ? ti
            : default;

        var result = new List<TaskInfo>();
        if (arr.ValueKind != JsonValueKind.Array) return result;

        foreach (var item in arr.EnumerateArray())
            result.Add(new TaskInfo(
                TaskId:    item.TryGetProperty("task_id", out var t) ? t.GetString() ?? "" : "",
                State:     item.TryGetProperty("state",   out var s) ? s.GetString() ?? "none" : "none",
                StartDate: ParseDate(item, "start_date"),
                EndDate:   ParseDate(item, "end_date")
            ));

        return result;
    }

    // ── Logs ─────────────────────────────────────────────────────────────────

    public async Task<string> GetTaskLogsAsync(
        string dagId, string runId, string taskId, int tryNumber = 1, CancellationToken ct = default)
    {
        var client = await AuthClientAsync(ct);
        var resp   = await client.GetAsync(
            $"{Base}/api/v2/dags/{dagId}/dagRuns/{runId}/taskInstances/{taskId}/logs/{tryNumber}", ct);

        if (!resp.IsSuccessStatusCode) return "";
        var raw = await resp.Content.ReadAsStringAsync(ct);

        // Airflow 3 може повертати JSON { "content": "..." } або plain text
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("content", out var c))
                return c.GetString() ?? raw;
        }
        catch { }

        return raw;
    }

    // ── Progress parsing ─────────────────────────────────────────────────────

    public (int current, int total) ParseAnalysisProgress(string logs)
    {
        var matches = Regex.Matches(logs, @"Analyzed (\d+)/(\d+) reviews");
        if (matches.Count == 0) return (0, 0);
        var last = matches[^1];
        return (int.Parse(last.Groups[1].Value), int.Parse(last.Groups[2].Value));
    }

    // ── Cancel ───────────────────────────────────────────────────────────────

    public async Task CancelDagRunAsync(string dagId, string runId, CancellationToken ct = default)
    {
        var client = await AuthClientAsync(ct);
        var body   = JsonSerializer.Serialize(new { state = "failed" });
        var req    = new HttpRequestMessage(HttpMethod.Patch,
            $"{Base}/api/v2/dags/{dagId}/dagRuns/{runId}")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        await client.SendAsync(req, ct);
    }

    // ── Output file ──────────────────────────────────────────────────────────

    public string GetOutputFilePath(string businessId)
    {
        var basePath  = _config["PipelineBasePath"] ?? "../../airflow-pipeline";
        var outputDir = Path.GetFullPath(Path.Combine(_env.ContentRootPath, basePath, "output"));
        var filePath  = Path.Combine(outputDir, $"business-analysis_{businessId}-result.json");
        return System.IO.File.Exists(filePath) ? filePath : "";
    }

    // ── Auth ─────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AuthClientAsync(CancellationToken ct)
    {
        var token  = await GetTokenAsync(ct);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token != null && DateTime.UtcNow < _tokenExpiry) return _token;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_token != null && DateTime.UtcNow < _tokenExpiry) return _token;

            var password = ReadPassword();
            var client   = _factory.CreateClient();
            var body     = JsonSerializer.Serialize(new { username = "admin", password });
            var resp     = await client.PostAsync($"{Base}/auth/token",
                               new StringContent(body, Encoding.UTF8, "application/json"), ct);
            var json     = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Airflow auth failed {(int)resp.StatusCode}: {json}");

            using var doc = JsonDocument.Parse(json);
            _token = doc.RootElement.TryGetProperty("access_token", out var t)
                ? t.GetString()! : throw new Exception("access_token missing");
            _tokenExpiry = DateTime.UtcNow.AddMinutes(55);
            return _token;
        }
        finally { _tokenLock.Release(); }
    }

    private string ReadPassword()
    {
        var basePath = _config["PipelineBasePath"] ?? "../../airflow-pipeline";
        var file     = Path.GetFullPath(Path.Combine(_env.ContentRootPath, basePath,
                          "simple_auth_manager_passwords.json.generated"));

        if (!System.IO.File.Exists(file))
            throw new FileNotFoundException($"Airflow auth file not found: {file}");

        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(file));
        return dict?["admin"] ?? throw new Exception("admin key not found in auth file");
    }

    private static DateTime? ParseDate(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null
            ? DateTime.TryParse(v.GetString(), out var d) ? d : null
            : null;
}
