using DuckDB.NET.Data;

namespace Dashboard.Services;

public enum FileImportStatus { Pending, Running, Done, Error, Skipped }

public class FileImportState
{
    public string TableName { get; set; } = "";
    public string FilePath  { get; set; } = "";
    public FileImportStatus Status  { get; set; } = FileImportStatus.Pending;
    public string Message   { get; set; } = "";
}

public class ImportStatusDto
{
    public bool IsRunning   { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsCancelled { get; set; }
    public double OverallProgress { get; set; }
    public List<FileStatusDto> Files { get; set; } = new();
    public List<string> Log { get; set; } = new();
}

public class FileStatusDto
{
    public string TableName { get; set; } = "";
    public string Status    { get; set; } = "pending";
    public string Message   { get; set; } = "";
}

public class YelpImportService
{
    private static readonly string InputPath =
        @"C:\Git\SelfHealingPipeline\airflow-pipeline\input";
    private static readonly string DbPath =
        @"C:\Git\SelfHealingPipeline\airflow-pipeline\input\yelp.duckdb";

    private static readonly (string Table, string File)[] FileMappings =
    [
        ("business", "yelp_academic_dataset_business.json"),
        ("review",   "yelp_academic_dataset_review.json"),
        ("user",     "yelp_academic_dataset_user.json"),
        ("tip",      "yelp_academic_dataset_tip.json"),
        ("checkin",  "yelp_academic_dataset_checkin.json"),
    ];

    private readonly List<FileImportState> _files;
    private readonly List<string> _log = [];
    private bool _isRunning   = false;
    private bool _isCompleted = false;
    private bool _isCancelled = false;
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();

    public YelpImportService()
    {
        _files = FileMappings.Select(m => new FileImportState
        {
            TableName = m.Table,
            FilePath  = Path.Combine(InputPath, m.File)
        }).ToList();
    }

    public ImportStatusDto GetStatus()
    {
        lock (_lock)
        {
            int done = _files.Count(f => f.Status is
                FileImportStatus.Done or FileImportStatus.Error or FileImportStatus.Skipped);

            return new ImportStatusDto
            {
                IsRunning      = _isRunning,
                IsCompleted    = _isCompleted,
                IsCancelled    = _isCancelled,
                OverallProgress = _files.Count > 0 ? (double)done / _files.Count : 0,
                Files = _files.Select(f => new FileStatusDto
                {
                    TableName = f.TableName,
                    Status    = f.Status.ToString().ToLower(),
                    Message   = f.Message
                }).ToList(),
                Log = _log.TakeLast(300).ToList()
            };
        }
    }

    public bool TryStart(bool forceReset)
    {
        lock (_lock)
        {
            if (_isRunning) return false;
            if (forceReset) ResetState();
            _isRunning   = true;
            _isCompleted = false;
            _isCancelled = false;
            _cts = new CancellationTokenSource();
        }
        _ = Task.Run(() => RunImportAsync(_cts.Token));
        return true;
    }

    public void Cancel() => _cts?.Cancel();

    public void Reset()
    {
        lock (_lock)
        {
            if (_isRunning) return;
            ResetState();
        }
    }

    private void ResetState()
    {
        foreach (var f in _files) { f.Status = FileImportStatus.Pending; f.Message = ""; }
        _log.Clear();
        _isCompleted = false;
        _isCancelled = false;
    }

    private async Task RunImportAsync(CancellationToken ct)
    {
        Log("=== Початок імпорту Yelp Dataset ===");

        try
        {
            using var conn = new DuckDBConnection($"Data Source={DbPath}");
            conn.Open();

            for (int i = 0; i < _files.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var f = _files[i];
                lock (_lock) { f.Status = FileImportStatus.Running; }
                Log($"[{i + 1}/{_files.Count}] Імпорт '{f.TableName}'...");

                if (!File.Exists(f.FilePath))
                {
                    lock (_lock) { f.Status = FileImportStatus.Skipped; f.Message = "Файл не знайдено"; }
                    Log($"  ⚠ Файл не знайдено: {f.FilePath}");
                    continue;
                }

                try
                {
                    var safePath = f.FilePath.Replace('\\', '/');
                    using var cmd = conn.CreateCommand();

                    cmd.CommandText = $"DROP TABLE IF EXISTS \"{f.TableName}\"";
                    cmd.ExecuteNonQuery();
                    ct.ThrowIfCancellationRequested();

                    cmd.CommandText = $"CREATE TABLE \"{f.TableName}\" AS SELECT * FROM read_json_auto('{safePath}')";
                    Log($"  Зчитую файл (може зайняти кілька хвилин)...");
                    await Task.Run(() => cmd.ExecuteNonQuery(), ct);
                    ct.ThrowIfCancellationRequested();

                    cmd.CommandText = $"SELECT COUNT(*) FROM \"{f.TableName}\"";
                    var count = cmd.ExecuteScalar();

                    lock (_lock) { f.Status = FileImportStatus.Done; f.Message = $"{count:N0} рядків"; }
                    Log($"  ✓ '{f.TableName}': {count:N0} рядків");
                }
                catch (OperationCanceledException)
                {
                    lock (_lock) { f.Status = FileImportStatus.Error; f.Message = "Скасовано"; }
                    throw;
                }
                catch (Exception ex)
                {
                    lock (_lock) { f.Status = FileImportStatus.Error; f.Message = ex.Message; }
                    Log($"  ✗ Помилка '{f.TableName}': {ex.Message}");
                }
            }

            if (!ct.IsCancellationRequested)
            {
                Log("Створення індексів...");
                await CreateIndexesAsync(conn, ct);
                Log("=== Імпорт завершено ===");
            }
        }
        catch (OperationCanceledException)
        {
            Log("=== Імпорт скасовано ===");
        }
        catch (Exception ex)
        {
            Log($"=== Критична помилка: {ex.Message} ===");
        }
        finally
        {
            lock (_lock)
            {
                _isRunning   = false;
                _isCompleted = !ct.IsCancellationRequested;
                _isCancelled = ct.IsCancellationRequested;
            }
        }
    }

    private async Task CreateIndexesAsync(DuckDBConnection conn, CancellationToken ct)
    {
        (string Idx, string Table, string Col)[] indexes =
        [
            ("idx_review_business", "review", "business_id"),
            ("idx_review_user",     "review", "user_id"),
            ("idx_tip_business",    "tip",    "business_id"),
            ("idx_tip_user",        "tip",    "user_id"),
        ];

        foreach (var (idx, table, col) in indexes)
        {
            if (ct.IsCancellationRequested) break;
            var tableState = _files.FirstOrDefault(f => f.TableName == table);
            if (tableState?.Status != FileImportStatus.Done) continue;

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"CREATE INDEX IF NOT EXISTS {idx} ON \"{table}\"({col})";
                await Task.Run(() => cmd.ExecuteNonQuery(), ct);
                Log($"  ✓ Індекс {idx}");
            }
            catch (Exception ex)
            {
                Log($"  ⚠ Індекс {idx}: {ex.Message}");
            }
        }
    }

    private void Log(string msg)
    {
        lock (_lock)
        {
            _log.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
        }
    }
}
