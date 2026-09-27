using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// The exact <c>package --content --out</c> export of a root README or a
/// Skill reads its document by range: gates 11 to 13 of
/// <c>docs/design/package-read-demand.md</c>.
/// </summary>
public sealed partial class ConfiguredPayloadAcquisitionTests
{
    private const string NewtonsoftId = "Newtonsoft.Json";
    private const string NewtonsoftVersion = "13.0.4";

    /// <summary>
    /// Gates 11 and 13, real asset Newtonsoft.Json 13.0.4 (2.5 MB, root
    /// README.md): the cold export is the size probe, the directory tail, and
    /// one span for the root folder, and writes the README's exact bytes; the
    /// second export is answered by the entry cache with no package request.
    /// </summary>
    [Fact]
    public async Task PackageCommand_ReadmeExport_RealNewtonsoftArchive_ReadsTheRootFolderByRange()
    {
        byte[] package = await ReadNewtonsoftAsync();
        var feed = new RangeHonoringFeedHandler(
            FirstFeed, NewtonsoftId, package, version: NewtonsoftVersion);
        UseFeed(feed);
        string outputPath = Path.Combine(_root, "newtonsoft-README.md");
        string[] export =
        [
            "package", $"{NewtonsoftId}@{NewtonsoftVersion}", "--source", FirstFeed,
            "--path", "README.md", "--content", "--out", outputPath,
            "--verbose", "--tips", "q",
        ];

        var cold = await RunCommandAsync(export);

        Assert.True(cold.Exit == 0, cold.Error);
        Assert.Empty(cold.Output);
        Assert.Equal(ReadEntry(package, "README.md"), File.ReadAllBytes(outputPath));
        // The one complete response is the size probe, abandoned before its
        // body; then the directory tail and one span for the root folder.
        Assert.Equal(1, feed.FullPackageResponses);
        Assert.True(2 == feed.RangedResponses, string.Join("; ", feed.Ranges));
        // The root folder's entries are the archive's first 10.8 KB and its
        // signature, which lies inside the tail.
        string span = feed.Ranges.Skip(1).Single();
        Assert.Equal(0, long.Parse(span.Split('-')[0]));
        Assert.True(long.Parse(span.Split('-')[1]) < 64 * 1024, span);
        Assert.True(
            feed.PackageBytesServed < 200_000,
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");
        Assert.Contains("6 of 24 entries", cold.Error, StringComparison.Ordinal);
        Assert.Contains(
            "Ranged, 3 package requests",
            cold.Error,
            StringComparison.Ordinal);

        File.Delete(outputPath);
        int ranged = feed.RangedResponses;
        long served = feed.PackageBytesServed;
        var warm = await RunCommandAsync(export);

        Assert.True(warm.Exit == 0, warm.Error);
        Assert.Equal(ReadEntry(package, "README.md"), File.ReadAllBytes(outputPath));
        Assert.Contains("from the entry cache", warm.Error, StringComparison.Ordinal);
        Assert.Contains(
            "EntryCache, 0 package requests, 0 bytes received",
            warm.Error,
            StringComparison.Ordinal);
        Assert.Equal(1, feed.FullPackageResponses);
        Assert.Equal(ranged, feed.RangedResponses);
        Assert.Equal(served, feed.PackageBytesServed);
    }

    /// <summary>
    /// Gate 11's equivalence: a source that ignores <c>Range</c> falls back
    /// to the complete archive, and the export writes the same bytes.
    /// </summary>
    [Fact]
    public async Task PackageCommand_ReadmeExport_CompleteFallbackWritesTheSameBytes()
    {
        byte[] package = await ReadNewtonsoftAsync();
        var feed = new RangeHonoringFeedHandler(
            FirstFeed, NewtonsoftId, package, ignoreRange: true, version: NewtonsoftVersion);
        UseFeed(feed);
        string outputPath = Path.Combine(_root, "complete-README.md");

        var result = await RunCommandAsync(
            ["package", $"{NewtonsoftId}@{NewtonsoftVersion}", "--source", FirstFeed,
                "--path", "README.md", "--content", "--out", outputPath,
                "--verbose", "--tips", "q"]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains("RangeIgnored", result.Error, StringComparison.Ordinal);
        Assert.Equal(ReadEntry(package, "README.md"), File.ReadAllBytes(outputPath));
    }

    /// <summary>
    /// Gate 12, a boundary fixture modeled on the Skill layout of the real
    /// CrestApps.AgentSkills.Mcp.OrchardCore 1.2.0 package (a Skill folder
    /// with a <c>references/</c> subfolder beside other Skills), padded above
    /// the size cut because that package is under it: the export reads the
    /// root folder and that Skill's folder only, subfolder included.
    /// </summary>
    [Fact]
    public async Task PackageCommand_SkillExport_ReadsTheRootAndSkillFoldersOnly()
    {
        string id = $"Documents.Skill.{Guid.NewGuid():N}";
        const string Skill = """
            ---
            name: demo
            description: Demo package skill.
            ---

            # Demo
            """;
        byte[] package = CreatePackage(
            id,
            "skill package README",
            extraEntries:
            [
                ("skills/other/SKILL.md", Encoding.UTF8.GetBytes("# Other")),
                ("content/filler.bin", RandomNumberGenerator.GetBytes(2 * 1024 * 1024)),
                ("skills/demo/SKILL.md", Encoding.UTF8.GetBytes(Skill)),
                ("skills/demo/references/guide.md", Encoding.UTF8.GetBytes("# Guide")),
            ]);
        var feed = new RangeHonoringFeedHandler(FirstFeed, id, package);
        UseFeed(feed);
        string outputPath = Path.Combine(_root, "skill-SKILL.md");

        var result = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "--path", "skills/demo/SKILL.md", "--content", "--out", outputPath,
                "--verbose", "--tips", "q"]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Output);
        Assert.Equal(Skill, File.ReadAllText(outputPath));
        // The nuspec and README (root), and the Skill with its reference;
        // neither the other Skill nor the filler.
        Assert.Contains("4 of 6 entries", result.Error, StringComparison.Ordinal);
        Assert.Equal(1, feed.FullPackageResponses);
        Assert.True(
            feed.PackageBytesServed < 128 * 1024 + AbandonedProbeReadBound,
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");
    }

    /// <summary>
    /// A missing Skill above the cut is the export's existing visible
    /// failure: the directory does not list it, and nothing is written.
    /// </summary>
    [Fact]
    public async Task PackageCommand_SkillExport_MissingSkillFailsVisibly()
    {
        string id = $"Documents.Missing.{Guid.NewGuid():N}";
        byte[] package = CreatePackage(
            id,
            "missing skill README",
            extraEntries: [("content/filler.bin", RandomNumberGenerator.GetBytes(2 * 1024 * 1024))]);
        var feed = new RangeHonoringFeedHandler(FirstFeed, id, package);
        UseFeed(feed);
        string outputPath = Path.Combine(_root, "missing-SKILL.md");

        var result = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "--path", "skills/missing/SKILL.md", "--content", "--out", outputPath,
                "--tips", "q"]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("found 0", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));
        Assert.True(
            feed.PackageBytesServed < 128 * 1024 + AbandonedProbeReadBound,
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");
    }

    private static async Task<byte[]> ReadNewtonsoftAsync()
    {
        byte[] package = await File.ReadAllBytesAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "DocumentDemand",
                "newtonsoft.json.13.0.4.nupkg"),
            TestContext.Current.CancellationToken);
        Assert.Equal(
            "f09081d457405baf35a973fa0c50d6bf272ed683f2568c5a620a49da952f6529",
            Convert.ToHexStringLower(SHA256.HashData(package)));
        return package;
    }

    private static byte[] ReadEntry(byte[] package, string path)
    {
        using var archive = new ZipArchive(new MemoryStream(package));
        using Stream entry = archive.GetEntry(path)!.Open();
        using var buffer = new MemoryStream();
        entry.CopyTo(buffer);
        return buffer.ToArray();
    }
}
