using DotnetInspector.Models;
using DotnetInspector.Metadata;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Tests;

/// <summary>
/// Validates that library versions are displayed as 3-part versions
/// (e.g., "10.0.1") rather than 4-part PE assembly versions (e.g., "10.0.0.0").
/// </summary>
public class VersionDisplayTests
{
    private static string SerializeCompact(LibraryInspection inspection)
    {
        var view = new LibraryInspectionView(inspection);
        var context = new MarkoutContext();
        return context.Serialize(view).TrimEnd();
    }

    private static string ExtractVersionField(string output)
    {
        var compactLine = output.Split('\n').First(l => l.Contains('|'));
        var versionSegment = compactLine.Split('|').First(s => s.Trim().StartsWith("Version:"));
        return versionSegment.Split(':')[1].Trim();
    }

    private static bool IsThreePartVersion(string version) =>
        version.Split('.').Length == 3 && version.Split('.').All(p => int.TryParse(p, out _));

    [Fact]
    public void PlatformVersion_IsThreePart()
    {
        var (_, _, version, error) = PlatformResolver.ResolveAssembly(
            "System.Text.Json", useRuntimeAssemblies: true);

        Assert.Null(error);
        Assert.NotNull(version);
        Assert.True(IsThreePartVersion(version!), $"Expected 3-part version, got: {version}");
    }

    [Fact]
    public void PlatformVersion_MatchesRuntimeDirectory()
    {
        var (_, _, version, error) = PlatformResolver.ResolveAssembly(
            "System.Text.Json", useRuntimeAssemblies: true);

        Assert.Null(error);
        Assert.NotNull(version);

        var sharedDir = PlatformResolver.GetSharedDirectory();
        Assert.NotNull(sharedDir);
        var runtimeDir = Path.Combine(sharedDir!, "Microsoft.NETCore.App", version!);
        Assert.True(Directory.Exists(runtimeDir), $"Runtime directory should exist: {runtimeDir}");
    }

    [Fact]
    public void PlatformAssembly_CompactView_ShowsThreePartVersion()
    {
        var (_, _, version, error) = PlatformResolver.ResolveAssembly(
            "System.Text.Json", useRuntimeAssemblies: true);
        Assert.Null(error);

        var inspection = new LibraryInspection
        {
            FileName = "System.Text.Json.dll",
            FileType = "dll",
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = "System.Text.Json",
                AssemblyVersion = "10.0.0.0",
                TargetFramework = ".NETCoreApp,Version=v10.0"
            },
            FileSize = 1024,
            Source = "Platform (runtime)",
            PlatformVersion = version
        };

        var displayed = ExtractVersionField(SerializeCompact(inspection));

        Assert.Equal(version, displayed);
        Assert.True(IsThreePartVersion(displayed), $"Expected 3-part version, got: {displayed}");
    }

    [Fact]
    public void NuGetAssembly_CompactView_ShowsThreePartVersion()
    {
        // Simulate a NuGet-resolved assembly with PlatformVersion set
        // to the package version discovered from the extract path
        var inspection = new LibraryInspection
        {
            FileName = "Newtonsoft.Json.dll",
            FileType = "dll",
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = "Newtonsoft.Json",
                AssemblyVersion = "13.0.0.0",
                TargetFramework = ".NETCoreApp,Version=v6.0"
            },
            FileSize = 1024,
            Source = "NuGet",
            PlatformVersion = "13.0.3"
        };

        var displayed = ExtractVersionField(SerializeCompact(inspection));

        Assert.True(IsThreePartVersion(displayed), $"Expected 3-part version, got: {displayed}");
    }

    [Fact]
    public void NuGetAssembly_PlatformVersionSet_PrefersItOverAssemblyVersion()
    {
        var inspection = new LibraryInspection
        {
            FileName = "Example.dll",
            FileType = "dll",
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = "Example",
                AssemblyVersion = "2.0.0.0",
            },
            FileSize = 1024,
            Source = "NuGet",
            PlatformVersion = "2.0.5"
        };

        var displayed = ExtractVersionField(SerializeCompact(inspection));

        Assert.Equal("2.0.5", displayed);
        Assert.DoesNotContain("2.0.0.0", SerializeCompact(inspection).Split('\n').First(l => l.Contains('|')));
    }

    [Fact]
    public void FileAssembly_NoPlatformVersion_FallsBackToAssemblyVersion()
    {
        var inspection = new LibraryInspection
        {
            FileName = "MyLib.dll",
            FileType = "dll",
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = "MyLib",
                AssemblyVersion = "2.1.0.0",
                TargetFramework = ".NETCoreApp,Version=v8.0"
            },
            FileSize = 1024,
            Source = "File"
        };

        var displayed = ExtractVersionField(SerializeCompact(inspection));

        Assert.Equal("2.1.0.0", displayed);
    }
}
