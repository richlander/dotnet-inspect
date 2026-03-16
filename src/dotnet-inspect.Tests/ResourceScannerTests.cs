using DotnetInspector.Metadata;

namespace DotnetInspector.Tests;

public class ResourceScannerTests
{
    [Fact]
    public void Scan_FindsEmbeddedResources()
    {
        // dotnet-inspect.dll has SKILL.md as an embedded resource
        var testDir = Path.GetDirectoryName(typeof(ResourceScannerTests).Assembly.Location)!;
        var targetPath = Path.Combine(testDir, "dotnet-inspect.dll");
        Assert.True(File.Exists(targetPath), $"dotnet-inspect.dll not found at {targetPath}");

        using var stream = File.OpenRead(targetPath);
        var resources = ResourceScanner.Scan(stream);

        Assert.True(resources.Count >= 1);
        Assert.Contains(resources, r => r.Name.Contains("SKILL.md"));

        // All should be embedded with non-zero sizes
        var skill = resources.First(r => r.Name.Contains("SKILL.md"));
        Assert.True(skill.IsEmbedded);
        Assert.True(skill.Size > 0);
        Assert.True(skill.IsPublic);
    }

    [Fact]
    public void Scan_AssemblyWithoutResources_ReturnsEmpty()
    {
        var testDir = Path.GetDirectoryName(typeof(ResourceScannerTests).Assembly.Location)!;
        var targetPath = Path.Combine(testDir, "DotnetInspector.Metadata.dll");
        Assert.True(File.Exists(targetPath));

        using var stream = File.OpenRead(targetPath);
        var resources = ResourceScanner.Scan(stream);

        Assert.Empty(resources);
    }

    [Fact]
    public void ExtractAll_ExtractsResourcesToDirectory()
    {
        var testDir = Path.GetDirectoryName(typeof(ResourceScannerTests).Assembly.Location)!;
        var targetPath = Path.Combine(testDir, "dotnet-inspect.dll");
        Assert.True(File.Exists(targetPath));

        var outputDir = Path.Combine(Path.GetTempPath(), $"resource-test-{Guid.NewGuid():N}");
        try
        {
            using var stream = File.OpenRead(targetPath);
            var extracted = ResourceScanner.ExtractAll(stream, outputDir);

            Assert.True(extracted.Count >= 2);

            var skillPath = extracted.FirstOrDefault(p => p.Contains("SKILL.md"));
            Assert.NotNull(skillPath);
            Assert.True(File.Exists(skillPath));

            var content = File.ReadAllText(skillPath);
            Assert.True(content.Length > 0);
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }
}
