using DotnetInspector.Fixtures;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Package endpoint scope gate 13
/// (docs/design/package-endpoint-scope.md#pathological-cases-and-gates): the
/// PDB source of pairwise <c>diff --package ID@A..B</c> reads each endpoint's
/// implementation participants in the groups that hold them, and settles
/// each participant's Portable PDB through the House that opened its
/// endpoint, never from a path beside an extracted assembly.
/// </summary>
public sealed partial class ConfiguredPayloadAcquisitionTests
{
    private const string SourceDiffId = "SourceDiffFixture";

    /// <summary>
    /// The SourceDiff fixture pair, packed with each assembly's adjacent
    /// Portable PDB. The selected member takes the pair route; two members
    /// take the batch route. Each equals the recording, made while the
    /// legacy oracle, which read the PDB beside the extracted assembly, was
    /// still the comparison.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_PdbSource_SettlesEachParticipantsPdbThroughItsHouse()
    {
        var packages = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["1.0.0"] = SourceDiffPackage("1.0.0", FixtureCatalog.SourceDiffPair.OldAssemblyPath()),
            ["2.0.0"] = SourceDiffPackage("2.0.0", FixtureCatalog.SourceDiffPair.NewAssemblyPath()),
        };
        string[][] requests =
        [
            PairwiseSourceDiff("--member", "Value"),
            PairwiseSourceDiff("--member", "Value", "--member", "Reordered"),
        ];
        UseFeed(new RangeHonoringHistoryFeedHandler(FirstFeed, SourceDiffId, packages));
        for (int view = 0; view < requests.Length; view++)
        {
            var result = await RunCommandAsync([.. requests[view], "--verbose"]);

            RecordedOutput.AssertEqual("PackageEndpointScope", result.Output, result.Error, view);
            Assert.Contains("| PDB Source |", result.Output, StringComparison.Ordinal);
            Assert.Equal(
                2,
                CountOccurrences(
                    result.Error,
                    "(1 surface assembly and 1 implementation assembly for net11.0;"));
            Assert.Contains("Loaded PDB: Portable, Settlement", result.Error, StringComparison.Ordinal);
            Assert.DoesNotContain("Standalone", result.Error, StringComparison.Ordinal);
        }
    }

    private static string[] PairwiseSourceDiff(params string[] members) =>
    [
        "diff",
        "--package", $"{SourceDiffId}@1.0.0..2.0.0",
        "--source", FirstFeed,
        "--type", "SourceDiffFixture.Counter",
        .. members,
        "-S", "Implementation Diff",
        "--pdb-source",
    ];

    private static byte[] SourceDiffPackage(string version, string assemblyPath) =>
        CreatePackage(
            SourceDiffId,
            "SourceDiff fixture",
            version: version,
            library: File.ReadAllBytes(assemblyPath),
            libraryName: "SourceDiffFixture.dll",
            pdb: File.ReadAllBytes(Path.ChangeExtension(assemblyPath, ".pdb")));
}
