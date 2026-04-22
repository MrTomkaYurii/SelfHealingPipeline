namespace Dashboard.Client.Models;

public record BusinessDto(
    string BusinessId,
    string Name,
    string City,
    string State,
    double Stars,
    int ReviewCount,
    string Categories
);

public class ExportResultDto
{
    public string FilePath    { get; set; } = "";
    public string FileName    { get; set; } = "";
    public int    ReviewCount { get; set; }
    public string CreatedAt   { get; set; } = "";
    public string Error       { get; set; } = "";
}

public class AirflowTriggerResultDto
{
    public string DagRunId   { get; set; } = "";
    public string Status     { get; set; } = "";
    public string AirflowUrl { get; set; } = "";
    public string Error      { get; set; } = "";
}

// ── Tab 2 — Запуск DAG ──────────────────────────────────────────────────────

public class TriggerDagResultDto
{
    public string RunId     { get; set; } = "";
    public string StartTime { get; set; } = "";
    public string Error     { get; set; } = "";
}

public class DagRunDto
{
    public string  RunId     { get; set; } = "";
    public string  State     { get; set; } = "";
    public string? StartDate { get; set; }
    public string? EndDate   { get; set; }
    public string  Error     { get; set; } = "";
}

public class TaskInstanceDto
{
    public string  TaskId    { get; set; } = "";
    public string  State     { get; set; } = "";
    public string? StartDate { get; set; }
    public string? EndDate   { get; set; }
}

public class TaskLogsDto
{
    public string       Content   { get; set; } = "";
    public int          Current   { get; set; }
    public int          Total     { get; set; }
    public List<string> LastLines { get; set; } = [];
    public string       Error     { get; set; } = "";
}

public class OutputFileDto
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Error    { get; set; } = "";
}
