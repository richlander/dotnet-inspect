using DotnetInspector.Models;
using DotnetInspector.Metadata;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Tests;

/// <summary>
/// Validates that library versions are displayed as 3-part versions
/// (e.g., "10.0.1") rather than 4-part PE assembly versions (e.g., "10.0.0.0").
/// Version priority: PlatformVersion → InformationalVersion prefix → AssemblyVersion → FileVersion.
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

    private static bool IsThreePartVersion(string version)
    {
        // Strip prerelease suffix (e.g., "11.0.0-preview.1.26104.118" → "11.0.0")
        var baseParts = version.Split('-')[0].Split('.');
        return baseParts.Length == 3 && baseParts.All(p => int.TryParse(p, out _));
    }

    // --- Priority chain tests ---

    [Fact]
    public void ResolveVersion_PlatformVersion_WinsOverAll()
    {
        var inspection = new LibraryInspection
        {
            FileName = "Test.dll", FileType = "dll", FileSize = 1024,
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = "Test",
                AssemblyVersion = "10.0.0.0",
                InformationalVersion = "10.0.0+abc",
                FileVersion = "10.0.0.1"
            },
            PlatformVersion = "10.0.1"
        };

        Assert.Equal("10.0.1", ExtractVersionField(SerializeCompact(inspection)));
    }

    [Fact]
    public void ResolveVersion_InformationalVersion_UsedWhenNoPlatformVersion()
    {
        var inspection = new LibraryInspection
        {
            FileName = "Test.dll", FileType = "dll", FileSize = 1024,
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = "Test",
                AssemblyVersion = "13.0.0.0",
                InformationalVersion = "13.0.4+4e13299d",
                FileVersion = "13.0.4.25416"
            }
        };

        Assert.Equal("13.0.4", ExtractVersionField(SerializeCompact(inspection)));
    }

    [Fact]
    public void ResolveVersion_InformationalVersion_PreservesPrerelease()
    {
        var inspection = new LibraryInspection
        {
            FileName = "Test.dll", FileType = "dll", FileSize = 1024,
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = "Test",
                AssemblyVersion = "9.0.0.0",
                InformationalVersion = "9.0.0-preview.1+abc123"
            }
        };

        Assert.Equal("9.0.0-preview.1", ExtractVersionField(SerializeCompact(inspection)));
    }

    [Fact]
    public void ResolveVersion_AssemblyVersion_FallbackWhenNoInformational()
    {
        var inspection = new LibraryInspection
        {
            FileName = "Test.dll", FileType = "dll", FileSize = 1024,
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = "Test",
                AssemblyVersion = "2.1.0.0",
                FileVersion = "2.1.3.0"
            }
        };

        Assert.Equal("2.1.0.0", ExtractVersionField(SerializeCompact(inspection)));
    }

    [Fact]
    public void ResolveVersion_FileVersion_LastResort()
    {
        var inspection = new LibraryInspection
        {
            FileName = "Test.dll", FileType = "dll", FileSize = 1024,
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = "Test",
                FileVersion = "1.2.3.0"
            }
        };

        Assert.Equal("1.2.3.0", ExtractVersionField(SerializeCompact(inspection)));
    }

    // --- Platform integration tests ---

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
            FileName = "System.Text.Json.dll", FileType = "dll", FileSize = 1024,
            Source = SourceKind.Platform,
            PlatformVersion = version,
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = "System.Text.Json",
                AssemblyVersion = "10.0.0.0",
                TargetFramework = ".NETCoreApp,Version=v10.0"
            }
        };

        var displayed = ExtractVersionField(SerializeCompact(inspection));
        Assert.Equal(version, displayed);
        Assert.True(IsThreePartVersion(displayed), $"Expected 3-part version, got: {displayed}");
    }

    // --- Detailed view tests ---

    [Fact]
    public void DetailedView_ShowsVersionInformationalAndAssemblyVersion()
    {
        var inspection = new LibraryInspection
        {
            FileName = "Test.dll", FileType = "dll", FileSize = 1024,
            PlatformVersion = "10.0.1",
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = "Test",
                AssemblyVersion = "10.0.0.0",
                InformationalVersion = "10.0.1+abc123"
            }
        };

        var view = new LibraryInspectionView(inspection);
        var context = new MarkoutContext();
        var output = context.Serialize(view).TrimEnd();

        Assert.Contains("Version: 10.0.1", output);
        Assert.Contains("Informational Version: 10.0.1+abc123", output);
        Assert.Contains("Assembly Version: 10.0.0.0", output);
    }
}
