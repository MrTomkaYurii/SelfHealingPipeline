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

        if (depth >= 5) return node;

        foreach (var dir in Directory.GetDirectories(path).OrderBy(d => Path.GetFileName(d)))
        {
            try { node.Children.Add(BuildNode(dir, depth + 1)); }
            catch { }
        }

        foreach (var file in Directory.GetFiles(path).OrderBy(f => f))
        {
            var fi = new FileInfo(file);
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
