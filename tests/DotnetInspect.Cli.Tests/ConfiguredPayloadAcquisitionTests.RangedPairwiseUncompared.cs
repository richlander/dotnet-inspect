using System.IO.Compression;

using DotnetInspector.Fixtures;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Package endpoint scope gate 15
/// (docs/design/package-endpoint-scope.md#pathological-cases-and-gates): the
/// layouts step 4 part 6's first part reports instead of comparing, as
/// intentional output changes: a package with only <c>tools/</c> or
/// <c>build/</c> assemblies, and, without <c>--tfm</c>, a package whose
/// default framework has an empty compile group.
/// </summary>
public sealed partial class ConfiguredPayloadAcquisitionTests
{
    private const string ToolsOnlyId = "Diff.ToolsOnly";
    private const string EmptyGroupId = "Diff.EmptyGroup";

    /// <summary>
    /// A package that ships its assemblies only under <c>tools/</c>, as
    /// <c>dotnet-ef</c> does, has no compile assets, so it isn't a diff
    /// candidate until the House selects those layouts. The scope reports it,
    /// with or without <c>--tfm</c>, and the request never takes the legacy
    /// path.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("net8.0")]
    public async Task PairwiseDiff_ToolsOnlyPackage_IsReportedByTheScope(string? targetFramework)
    {
        var packages = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["1.0.0"] = ToolsOnlyPackage("1.0.0", FixtureCatalog.DiffPair.OldAssemblyPath()),
            ["2.0.0"] = ToolsOnlyPackage("2.0.0", FixtureCatalog.DiffPair.NewAssemblyPath()),
        };
        UseFeed(new RangeHonoringHistoryFeedHandler(FirstFeed, ToolsOnlyId, packages));

        var result = await RunCommandAsync(
        [
            "diff",
            "--package", $"{ToolsOnlyId}@1.0.0..2.0.0",
            .. targetFramework is null ? (string[])[] : ["--tfm", targetFramework],
            "--source", FirstFeed,
            "--verbose",
        ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            $"Package '{ToolsOnlyId}@1.0.0' has no compile assets; "
                + "assemblies outside lib/ and ref/, such as tools/, build/, or analyzers/, aren't compared.",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain($"Comparing {ToolsOnlyId} v", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A package shaped like System.Memory 4.5: an empty compile group
    /// (<c>ref/netcoreapp2.1/_._</c>) for the framework the owner default
    /// selects, beside a <c>netstandard2.0</c> Library. Without <c>--tfm</c>
    /// the scope reports the empty group and names the frameworks to pass;
    /// with <c>--tfm netstandard2.0</c> the Library is compared.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_EmptyDefaultCompileGroup_IsReportedWithTheFrameworksToPass()
    {
        var packages = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["1.0.0"] = EmptyGroupPackage("1.0.0", FixtureCatalog.DiffPair.OldAssemblyPath()),
            ["2.0.0"] = EmptyGroupPackage("2.0.0", FixtureCatalog.DiffPair.NewAssemblyPath()),
        };
        UseFeed(new RangeHonoringHistoryFeedHandler(FirstFeed, EmptyGroupId, packages));
        string[] request =
        [
            "diff",
            "--package", $"{EmptyGroupId}@1.0.0..2.0.0",
            "--source", FirstFeed,
            "--verbose",
        ];

        var result = await RunCommandAsync(request);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            $"Package '{EmptyGroupId}@1.0.0' has an empty compile group for netcoreapp2.1, "
                + "the framework it selects by default, which provides the Library itself. "
                + "Use --tfm to compare it for one of: netstandard2.0.",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain($"Comparing {EmptyGroupId} v", result.Error, StringComparison.Ordinal);

        var targeted = await RunCommandAsync([.. request, "--tfm", "netstandard2.0"]);

        Assert.True(targeted.Exit == 0, targeted.Error);
        Assert.Contains("Member 'GenericIdentity' was added", targeted.Output, StringComparison.Ordinal);
    }

    private static byte[] EmptyGroupPackage(string version, string assemblyPath)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, $"{EmptyGroupId}.nuspec", $"""
                <package><metadata>
                  <id>{EmptyGroupId}</id><version>{version}</version>
                  <authors>Payload tests</authors>
                  <description>Empty default compile group diff fixture</description>
                </metadata></package>
                """);
            archive.CreateEntry("ref/netcoreapp2.1/_._");
            archive.CreateEntry("lib/netcoreapp2.1/_._");
            using Stream stream = archive.CreateEntry("lib/netstandard2.0/DiffFixtureSample.dll").Open();
            stream.Write(File.ReadAllBytes(assemblyPath));
        }
        return buffer.ToArray();
    }

    private static byte[] ToolsOnlyPackage(string version, string assemblyPath)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, $"{ToolsOnlyId}.nuspec", $"""
                <package><metadata>
                  <id>{ToolsOnlyId}</id><version>{version}</version>
                  <authors>Payload tests</authors>
                  <description>Tools-only diff fixture</description>
                </metadata></package>
                """);
            using Stream stream = archive.CreateEntry("tools/net8.0/any/DiffFixtureSample.dll").Open();
            stream.Write(File.ReadAllBytes(assemblyPath));
        }
        return buffer.ToArray();
    }
}
