using System.IO.Compression;

using DotnetInspector.Fixtures;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Package endpoint scope gate 15
/// (docs/design/package-endpoint-scope.md#pathological-cases-and-gates): the
/// one endpoint failure that keeps the legacy path in step 4 part 6's first
/// part.
/// </summary>
public sealed partial class ConfiguredPayloadAcquisitionTests
{
    private const string ToolsOnlyId = "Diff.ToolsOnly";

    /// <summary>
    /// A package that ships its assemblies only under <c>tools/</c>, as
    /// <c>dotnet-ef</c> does, has no House compile selection under the owner
    /// default. Without <c>--tfm</c>, the request keeps the legacy path, whose
    /// selector takes those assemblies, and the output equals the legacy
    /// oracle's. With <c>--tfm</c>, the failure is reported by the scope.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_ToolsOnlyPackageWithoutTfm_KeepsTheLegacyPath()
    {
        var packages = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["1.0.0"] = ToolsOnlyPackage("1.0.0", FixtureCatalog.DiffPair.OldAssemblyPath()),
            ["2.0.0"] = ToolsOnlyPackage("2.0.0", FixtureCatalog.DiffPair.NewAssemblyPath()),
        };
        string[] request =
        [
            "diff",
            "--package", $"{ToolsOnlyId}@1.0.0..2.0.0",
            "--source", FirstFeed,
        ];
        string[] legacy = await RunLegacyPairwiseDiffsAsync(ToolsOnlyId, packages, [request]);
        Assert.Contains("Member 'GenericIdentity' was added", legacy[0], StringComparison.Ordinal);

        UseFeed(new RangeHonoringHistoryFeedHandler(FirstFeed, ToolsOnlyId, packages));
        var result = await RunCommandAsync([.. request, "--verbose"]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Equal(legacy[0], result.Output);
        Assert.Contains(
            "has no House compile selection; the request takes the legacy path.",
            result.Error,
            StringComparison.Ordinal);

        var targeted = await RunCommandAsync([.. request, "--tfm", "net8.0", "--verbose"]);

        Assert.Equal(1, targeted.Exit);
        Assert.Contains(
            $"No assemblies found for target framework 'net8.0' in package '{ToolsOnlyId}@",
            targeted.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("takes the legacy path", targeted.Error, StringComparison.Ordinal);
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
