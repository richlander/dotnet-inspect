using System.Text.Json;
using DotnetInspector.Fixtures;
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
        Assert.Contains("1 rejected mapping", signals.Output, StringComparison.Ordinal);
        Assert.Equal(0, diagnostics.Exit);
        Assert.Contains("Map error", diagnostics.Output, StringComparison.Ordinal);
        Assert.Contains("Rejected mapping", diagnostics.Output, StringComparison.Ordinal);
        Assert.Contains("/src/*", diagnostics.Output, StringComparison.Ordinal);
        Assert.Equal(0, paths.Exit);
        Assert.Contains("SourceLink: /src/*", paths.Output, StringComparison.Ordinal);
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
        Assert.Equal(
            "/src/*",
            Assert.Single(
                root.GetProperty("source_link_map")
                    .GetProperty("rejected_keys")
                    .EnumerateArray()
                    .ToArray())
                .GetString());
        Assert.Equal(
            "SourceLink: /src/*",
            Assert.Single(
                root.GetProperty("non_normalized_paths")
                    .EnumerateArray()
                    .ToArray())
                .GetString());
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
}
