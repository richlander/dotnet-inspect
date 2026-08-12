using System.Security.Cryptography;
using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Core;
using DotnetInspector.Fixtures;
using DotnetInspector.Sections;
using ILInspector.SourceLink;

namespace DotnetInspector.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task Library_MalformedSourceLink_ReportsMapAndPathDiagnostics()
    {
        string path = FixtureCatalog.SourceLinkMalformed.AssemblyPath();

        var signals = await RunAppAsync(
            "library", path, "-S", "Signals", "--tips", "q");
        var diagnostics = await RunAppAsync(
            "library", path, "-S", "SourceLink: Diagnostics", "--tips", "q");
        var paths = await RunAppAsync(
            "library", path, "-S", "Non-normalized Paths", "--tips", "q");

        Assert.Equal(0, signals.Exit);
        Assert.Contains("Present (unusable)", signals.Output, StringComparison.Ordinal);
        Assert.Contains("2 rejected mappings", signals.Output, StringComparison.Ordinal);
        Assert.Equal(0, diagnostics.Exit);
        Assert.Contains("Map error", diagnostics.Output, StringComparison.Ordinal);
        Assert.Contains("Rejected mapping", diagnostics.Output, StringComparison.Ordinal);
        Assert.Contains("/src/*", diagnostics.Output, StringComparison.Ordinal);
        Assert.Contains("/_evil/*", diagnostics.Output, StringComparison.Ordinal);
        Assert.Equal(0, paths.Exit);
        Assert.Contains("SourceLink: /src/*", paths.Output, StringComparison.Ordinal);
        Assert.Contains("SourceLink: /_evil/*", paths.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("PDB Path:", paths.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Library_MalformedSourceLink_ReportsStructuredDiagnostics()
    {
        string path = FixtureCatalog.SourceLinkMalformed.AssemblyPath();

        var result = await RunAppAsync("library", path, "--json");

        Assert.Equal(0, result.Exit);
        using JsonDocument json = JsonDocument.Parse(result.Output);
        JsonElement root = json.RootElement;
        Assert.Equal(
            "Unusable",
            root.GetProperty("source_link_map").GetProperty("status").GetString());
        string?[] rejectedKeys =
        [
            .. root.GetProperty("source_link_map")
                .GetProperty("rejected_keys")
                .EnumerateArray()
                .Select(static value => value.GetString()),
        ];
        Assert.Equal(2, rejectedKeys.Length);
        Assert.Contains("/src/*", rejectedKeys);
        Assert.Contains("/_evil/*", rejectedKeys);

        string?[] nonNormalizedPaths =
        [
            .. root.GetProperty("non_normalized_paths")
                .EnumerateArray()
                .Select(static value => value.GetString()),
        ];
        Assert.Equal(2, nonNormalizedPaths.Length);
        Assert.Contains("SourceLink: /src/*", nonNormalizedPaths);
        Assert.Contains("SourceLink: /_evil/*", nonNormalizedPaths);
    }

    [Fact]
    public void SourceLinkAudit_NormalizedFixtureStaysClean()
    {
        LibraryDebugInfo audit =
            SourceLinkInspector.InspectDll(
                FixtureCatalog.SourceLinkNormalized.AssemblyPath());

        Assert.Equal(SourceLinkMapStatus.Usable, audit.SourceLinkMap.Status);
        Assert.True(audit.HasNormalizedPaths);
        Assert.Null(audit.NonNormalizedPaths);
        Assert.False(audit.SourceLinkMap.HasDiagnostics);
    }

    [Fact]
    public async Task Library_SourceLinkDiagnostics_IgnoresPreDiagnosticsEffectiveCache()
    {
        const string legacyCategory = "effective-v21";
        const string currentCategory = "effective-v22";
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"sourcelink-diagnostics-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, "Probe.dll");
        string pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        File.Copy(FixtureCatalog.SourceLinkMalformed.AssemblyPath(), assemblyPath);
        File.Copy(
            Path.ChangeExtension(FixtureCatalog.SourceLinkMalformed.AssemblyPath(), ".pdb"),
            pdbPath);

        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath)));
        string key = LibraryCommand.BuildEffectiveCacheKey(
            assemblyPath,
            hash,
            hasSourceLink: true);
        string legacyCache = CoreCache.GetFilePath(legacyCategory, key, extension: "tsv");
        string currentCache = CoreCache.GetFilePath(currentCategory, key, extension: "tsv");

        try
        {
            DeleteIfPresent(currentCache);
            CoreCache.Set(legacyCategory, key, "Library Info\n", extension: "tsv");

            var (exit, output, error) = await RunAppAsync(
                "library", assemblyPath,
                "-D", "--effective", "--tree",
                "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains(SectionNames.SourceLinkDiagnostics, output);
        }
        finally
        {
            DeleteIfPresent(legacyCache);
            DeleteIfPresent(currentCache);
            Directory.Delete(directory, recursive: true);
        }

        static void DeleteIfPresent(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
