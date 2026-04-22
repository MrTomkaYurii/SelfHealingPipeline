namespace Dashboard.Client.Models;

public class ImportStatusDto
{
    public bool IsRunning       { get; set; }
    public bool IsCompleted     { get; set; }
    public bool IsCancelled     { get; set; }
    public double OverallProgress { get; set; }
    public List<FileStatusDto> Files { get; set; } = new();
    public List<string> Log     { get; set; } = new();
}

public class FileStatusDto
{
    public string TableName { get; set; } = "";
    public string Status    { get; set; } = "pending";
    public string Message   { get; set; } = "";
}
