namespace DotnetInspector.Services.Tests;

public class TfmSelectorTests : IDisposable
{
    private readonly string _tempDir;

    public TfmSelectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tfm-selector-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_SelectsHighestTfmAssemblies()
    {
        var netstandard = WriteDll("lib/netstandard2.0/MyLib.dll");
        var net8 = WriteDll("lib/net8.0/MyLib.dll");
        var net8Companion = WriteDll("lib/net8.0/MyLib.Companion.dll");
        WriteDll("lib/net8.0/fr/MyLib.resources.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir);

        Assert.Equal("net8.0", tfm);
        Assert.Equal(2, paths.Count);
        Assert.Contains(net8, paths);
        Assert.Contains(net8Companion, paths);
        Assert.DoesNotContain(netstandard, paths);
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_ExplicitTfm_SelectsMatchingAssemblies()
    {
        var net6 = WriteDll("lib/net6.0/MyLib.dll");
        WriteDll("lib/net8.0/MyLib.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir, "net6.0");

        Assert.Equal("net6.0", tfm);
        Assert.Equal([net6], paths);
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_ToolsLayoutExplicitTfm_SelectsMatchingAssemblies()
    {
        var tool = WriteDll("tools/net8.0/any/MyTool.dll");
        WriteDll("tools/net6.0/any/MyTool.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir, "net8.0");

        Assert.Equal("net8.0", tfm);
        Assert.Equal([tool], paths);
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_RefLayoutExplicitTfm_SelectsMatchingAssemblies()
    {
        var referenceAssembly = WriteDll("ref/net8.0/MyLib.dll");
        WriteDll("ref/net6.0/MyLib.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir, "net8.0");

        Assert.Equal("net8.0", tfm);
        Assert.Equal([referenceAssembly], paths);
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_ExplicitTfmWithoutMatches_ReturnsEmptySelection()
    {
        WriteDll("lib/net8.0/MyLib.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir, "net6.0");

        Assert.Equal("net6.0", tfm);
        Assert.Empty(paths);
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_AllTfms_ReturnsAllNonResourceAssemblies()
    {
        var net6 = WriteDll("lib/net6.0/MyLib.dll");
        var net8 = WriteDll("lib/net8.0/MyLib.dll");
        WriteDll("lib/net8.0/fr/MyLib.resources.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir, "all");

        Assert.Null(tfm);
        Assert.Equal([net6, net8], paths);
    }

    [Fact]
    public void GetPackageTfms_ReturnsDistinctTfmsInPriorityOrder()
    {
        WriteDll("lib/netstandard2.0/MyLib.dll");
        WriteDll("lib/net8.0/MyLib.dll");
        WriteDll("lib/net8.0/MyLib.Companion.dll");

        var tfms = TfmSelector.GetPackageTfms(_tempDir);

        Assert.Equal(["net8.0", "netstandard2.0"], tfms);
    }

    [Fact]
    public void SelectHighestAssembliesFromPackage_NoTfmLayout_ReturnsPackageAssemblies()
    {
        var root = WriteDll("MyLib.dll");

        var (paths, tfm) = TfmSelector.SelectHighestAssembliesFromPackage(_tempDir);

        Assert.Null(tfm);
        Assert.Equal([root], paths);
    }

    [Fact]
    public void FindAssemblyByTfm_UsesPackageNameMatch()
    {
        WriteDll("lib/net8.0/Companion.dll");
        var primary = WriteDll("lib/net8.0/MyPackage.dll");

        var result = TfmSelector.FindAssemblyByTfm(_tempDir, "net8.0", "MyPackage");

        Assert.Equal(primary, result);
    }

    [Fact]
    public void FindAssemblyByTfm_FiltersResourceAssemblies()
    {
        var primary = WriteDll("tools/net8.0/any/MyTool.dll");
        WriteDll("tools/net8.0/any/MyTool.resources.dll");

        var result = TfmSelector.FindAssemblyByTfm(_tempDir, "net8.0");

        Assert.Equal(primary, result);
    }

    [Fact]
    public void FindAssemblyByTfm_HandlesRuntimeSpecificLibLayout()
    {
        var runtimeAssembly = WriteDll("runtimes/linux-x64/lib/net8.0/MyRuntime.dll");

        var result = TfmSelector.FindAssemblyByTfm(_tempDir, "net8.0");

        Assert.Equal(runtimeAssembly, result);
    }

    private string WriteDll(string relativePath)
    {
        var path = Path.Combine(_tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }
}
