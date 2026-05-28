using Dashboard.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Dashboard.Client.Models;

namespace Dashboard.Tests;

/// <summary>
/// Tests for FileTreeController — verifies directory traversal,
/// skip lists, depth limit, and 404 for missing directories.
/// </summary>
public class FileTreeControllerTests : IDisposable
{
    private readonly string _tempRoot;

    public FileTreeControllerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "FileTreeTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private FileTreeController CreateController(string basePath)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PipelineBasePath"] = basePath
            })
            .Build();

        var env = new FakeWebHostEnvironment(_tempRoot);
        return new FileTreeController(config, env);
    }

    // ── 404 when directory missing ────────────────────────────────────────────

    [Fact]
    public void GetTree_ReturnsNotFound_WhenDirectoryMissing()
    {
        var controller = CreateController(Path.Combine(_tempRoot, "nonexistent"));
        var result = controller.GetTree();
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // ── basic structure ───────────────────────────────────────────────────────

    [Fact]
    public void GetTree_ReturnsRootNode_WhenDirectoryExists()
    {
        var controller = CreateController(_tempRoot);
        var result = controller.GetTree();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var node = Assert.IsType<FileNode>(ok.Value);
        Assert.True(node.IsDirectory);
    }

    [Fact]
    public void GetTree_IncludesFiles_InRoot()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "requirements.txt"), "pytest");
        File.WriteAllText(Path.Combine(_tempRoot, "README.md"), "# Test");

        var controller = CreateController(_tempRoot);
        var result = controller.GetTree();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var node = Assert.IsType<FileNode>(ok.Value);

        var names = node.Children.Select(c => c.Name).ToList();
        Assert.Contains("requirements.txt", names);
        Assert.Contains("README.md", names);
    }

    [Fact]
    public void GetTree_IncludesSubdirectory()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "dags"));
        File.WriteAllText(Path.Combine(_tempRoot, "dags", "pipeline.py"), "# dag");

        var controller = CreateController(_tempRoot);
        var result = controller.GetTree();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var node = Assert.IsType<FileNode>(ok.Value);

        var dagsNode = node.Children.FirstOrDefault(c => c.Name == "dags");
        Assert.NotNull(dagsNode);
        Assert.True(dagsNode!.IsDirectory);
        Assert.Contains(dagsNode.Children, c => c.Name == "pipeline.py");
    }

    // ── skip directories ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(".venv")]
    [InlineData("venv")]
    [InlineData("__pycache__")]
    [InlineData(".git")]
    [InlineData("node_modules")]
    [InlineData("logs")]
    [InlineData(".vscode")]
    [InlineData("bin")]
    [InlineData("obj")]
    public void GetTree_SkipsExcludedDirectory(string dirName)
    {
        var dir = Path.Combine(_tempRoot, dirName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "file.txt"), "should be hidden");

        var controller = CreateController(_tempRoot);
        var result = controller.GetTree();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var node = Assert.IsType<FileNode>(ok.Value);

        Assert.DoesNotContain(node.Children, c => c.Name == dirName);
    }

    // ── skip file extensions ──────────────────────────────────────────────────

    [Theory]
    [InlineData("airflow.db")]
    [InlineData("data.db-shm")]
    [InlineData("data.db-wal")]
    [InlineData("module.pyc")]
    [InlineData("config.cfg")]
    [InlineData("desktop.ini")]
    public void GetTree_SkipsExcludedFileExtensions(string fileName)
    {
        File.WriteAllText(Path.Combine(_tempRoot, fileName), "skip me");

        var controller = CreateController(_tempRoot);
        var result = controller.GetTree();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var node = Assert.IsType<FileNode>(ok.Value);

        Assert.DoesNotContain(node.Children, c => c.Name == fileName);
    }

    // ── depth limit ───────────────────────────────────────────────────────────

    [Fact]
    public void GetTree_RespectsDepthLimit()
    {
        // Create a 5-level deep structure
        var current = _tempRoot;
        for (int i = 0; i < 6; i++)
        {
            current = Path.Combine(current, $"level{i}");
            Directory.CreateDirectory(current);
            File.WriteAllText(Path.Combine(current, $"file{i}.txt"), $"depth {i}");
        }

        var controller = CreateController(_tempRoot);
        var result = controller.GetTree();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var node = Assert.IsType<FileNode>(ok.Value);

        // Navigate to depth 4 — should have no children (depth limit reached)
        var deep = node;
        int depth = 0;
        while (depth < 4 && deep.Children.Any(c => c.IsDirectory))
        {
            deep = deep.Children.First(c => c.IsDirectory);
            depth++;
        }

        // At max depth the directory node has no children
        var deepestDir = deep.Children.FirstOrDefault(c => c.IsDirectory);
        if (deepestDir != null)
            Assert.Empty(deepestDir.Children);
    }

    // ── file metadata ─────────────────────────────────────────────────────────

    [Fact]
    public void GetTree_FileNode_HasCorrectSizeAndName()
    {
        var content = "hello world";
        File.WriteAllText(Path.Combine(_tempRoot, "test.txt"), content);

        var controller = CreateController(_tempRoot);
        var result = controller.GetTree();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var node = Assert.IsType<FileNode>(ok.Value);

        var file = node.Children.First(c => c.Name == "test.txt");
        Assert.False(file.IsDirectory);
        Assert.True(file.SizeBytes > 0);
    }
}

/// <summary>Minimal IWebHostEnvironment for tests.</summary>
internal class FakeWebHostEnvironment : IWebHostEnvironment
{
    public FakeWebHostEnvironment(string contentRootPath)
    {
        ContentRootPath = contentRootPath;
        WebRootPath = contentRootPath;
        EnvironmentName = "Test";
        ApplicationName = "Dashboard.Tests";
        ContentRootFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(contentRootPath);
        WebRootFileProvider = ContentRootFileProvider;
    }

    public string ContentRootPath { get; set; }
    public string WebRootPath { get; set; }
    public string EnvironmentName { get; set; }
    public string ApplicationName { get; set; }
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; }
}
