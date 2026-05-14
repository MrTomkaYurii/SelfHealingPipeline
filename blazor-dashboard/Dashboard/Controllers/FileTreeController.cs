using Microsoft.AspNetCore.Mvc;
using Dashboard.Client.Models;

namespace Dashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileTreeController : ControllerBase
{
    private readonly string _basePath;

    public FileTreeController(IConfiguration config, IWebHostEnvironment env)
    {
        var configPath = config["PipelineBasePath"] ?? "../../airflow-pipeline";
        _basePath = Path.IsPathRooted(configPath)
            ? configPath
            : Path.GetFullPath(Path.Combine(env.ContentRootPath, configPath));
    }

    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".venv", "venv", "__pycache__", ".git", "node_modules",
        "bin", "obj", ".vs", ".vscode", "logs", "scripts"
    };

    private static readonly HashSet<string> SkipExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".db", ".db-shm", ".db-wal", ".pyc", ".pyo",
        ".generated", ".cfg", ".ini"
    };

    private static readonly HashSet<string> SkipFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "simple_auth_manager_passwords.json.generated"
    };

    [HttpGet]
    public ActionResult<FileNode> GetTree()
    {
        if (!Directory.Exists(_basePath))
            return NotFound($"Directory not found: {_basePath}");
        return Ok(BuildNode(_basePath, depth: 0));
    }

    private static FileNode BuildNode(string path, int depth)
    {
        var di = new DirectoryInfo(path);
        var node = new FileNode
        {
            Name = di.Name,
            FullPath = path,
            IsDirectory = true,
            LastModified = di.LastWriteTime
        };

        if (depth >= 4) return node;

        foreach (var dir in Directory.GetDirectories(path).OrderBy(Path.GetFileName))
        {
            if (SkipDirs.Contains(Path.GetFileName(dir))) continue;
            try { node.Children.Add(BuildNode(dir, depth + 1)); }
            catch { }
        }

        foreach (var file in Directory.GetFiles(path).OrderBy(f => f))
        {
            var fi = new FileInfo(file);
            if (SkipExtensions.Contains(fi.Extension)) continue;
            if (SkipFileNames.Contains(fi.Name)) continue;
            node.Children.Add(new FileNode
            {
                Name = fi.Name,
                FullPath = file,
                IsDirectory = false,
                SizeBytes = fi.Length,
                LastModified = fi.LastWriteTime
            });
        }

        return node;
    }
}
