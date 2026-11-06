using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Pinned literal <c>package --content</c> requests use House acquisition and
/// Workspace admission. README and Skill controls retain gates 11 to 13 of
/// <c>docs/design/package-read-demand.md</c>.
/// </summary>
public sealed partial class ConfiguredPayloadAcquisitionTests
{
    private const string NewtonsoftId = "Newtonsoft.Json";
    private const string NewtonsoftVersion = "13.0.4";

    [Fact]
    public async Task PackageCommand_LiteralNuspec_RealSystemTextJsonUsesExactContent()
    {
        byte[] package = (await ReadSystemTextJsonPackagesAsync())["10.0.0"];
        var feed = new RangeHonoringFeedHandler(
            FirstFeed, SystemTextJsonId, package, version: "10.0.0");
        UseFeed(feed);
        const string Entry = "System.Text.Json.nuspec";
        byte[] expected = ReadEntry(package, Entry);
        string expectedText = Encoding.UTF8.GetString(expected)
            .Replace("\uFEFF", "\\uFEFF", StringComparison.Ordinal)
            .ReplaceLineEndings("\n");
        string[] arguments =
        [
            "package", $"{SystemTextJsonId}@10.0.0", "--source", FirstFeed,
            "--path", Entry.ToLowerInvariant(), "--content",
        ];

        var raw = await RunCommandAsync([.. arguments, "--raw"]);

        Assert.True(raw.Exit == 0, raw.Error);
        Assert.Equal(
            expectedText.Trim(),
            raw.Output.Trim());
        Assert.True(
            feed.PackageBytesServed < 200_000,
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");
        int requests = feed.FullPackageResponses + feed.RangedResponses;

        var blocks = await RunCommandAsync(arguments);
        var jsonl = await RunCommandAsync([.. arguments, "--jsonl"]);
        string outputPath = Path.Combine(_root, "System.Text.Json.nuspec");
        var exported = await RunCommandAsync(
            [.. arguments, "--out", outputPath]);

        Assert.True(blocks.Exit == 0, blocks.Error);
        Assert.Contains(
            $"------------ system.text.json :: {Entry} ------------",
            blocks.Output, StringComparison.Ordinal);
        Assert.True(jsonl.Exit == 0, jsonl.Error);
        using JsonDocument json = JsonDocument.Parse(jsonl.Output);
        Assert.Equal(
            Entry, json.RootElement.GetProperty("path").GetString());
        Assert.Equal(
            Encoding.UTF8.GetString(expected),
            json.RootElement.GetProperty("content").GetString());
        Assert.True(exported.Exit == 0, exported.Error);
        Assert.Equal(expected, File.ReadAllBytes(outputPath));
        Assert.Equal(
            requests, feed.FullPackageResponses + feed.RangedResponses);
    }

    [Fact]
    public async Task PackageCommand_LiteralFile_PreservesNonMarkdownBytesAndText()
    {
        string id = $"Documents.Literal.{Guid.NewGuid():N}";
        const string Text =
            "{\"url\":\"https://github.com/example/project/blob/main/config.json\"}";
        byte[] text = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(Text)];
        byte[] binary = [0, 1, 0xfe, 0xff, 0x80];
        byte[] package = CreatePackage(
            id, "README",
            library: [1, 2, 3],
            extraEntries:
            [
                ("content/config.json", text),
                ("content/payload.bin", binary),
                ("content/filler.bin", RandomNumberGenerator.GetBytes(2 * 1024 * 1024)),
            ]);
        var feed = new RangeHonoringFeedHandler(FirstFeed, id, package);
        UseFeed(feed);
        var raw = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "--path", "content/config.json", "--content", "--raw"]);
        string outputPath = Path.Combine(_root, "payload.bin");
        var exported = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "--path", "content/payload.bin", "--content", "--out", outputPath]);

        Assert.True(raw.Exit == 0, raw.Error);
        Assert.Equal("\\uFEFF" + Text, raw.Output.TrimEnd());
        Assert.True(exported.Exit == 0, exported.Error);
        Assert.Equal(binary, File.ReadAllBytes(outputPath));
        Assert.True(
            feed.PackageBytesServed < 200_000,
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");
    }

    [Theory]
    [InlineData(false, "found 0")]
    [InlineData(true, "Could not acquire the selected package file.")]
    public async Task PackageCommand_LiteralFile_UnavailableSelectionDoesNotExtract(
        bool ambiguous,
        string expectedError)
    {
        string id = $"Documents.LiteralMissing.{Guid.NewGuid():N}";
        byte[] package = CreatePackage(
            id, "README",
            extraEntries: ambiguous
                ?
                [
                    ("content/config.json", "{}"u8.ToArray()),
                    ("content/CONFIG.json", "[]"u8.ToArray()),
                    ("content/filler.bin", RandomNumberGenerator.GetBytes(2 * 1024 * 1024)),
                ]
                : [("content/filler.bin", RandomNumberGenerator.GetBytes(2 * 1024 * 1024))]);
        var feed = new RangeHonoringFeedHandler(FirstFeed, id, package);
        UseFeed(feed);
        string outputPath = Path.Combine(_root, "missing.json");

        var result = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "--path", "content/config.json", "--content", "--out", outputPath]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(expectedError, result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));
        Assert.True(
            feed.PackageBytesServed < 128 * 1024 + AbandonedProbeReadBound,
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");
    }

    [Fact]
    public async Task PackageCommand_LiteralFile_AcquisitionFailureDoesNotRetryExtractor()
    {
        string id = $"Documents.BadArchive.{Guid.NewGuid():N}";
        var feed = new RangeHonoringFeedHandler(
            FirstFeed, id, "not a package"u8.ToArray());
        UseFeed(feed);

        var result = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "--path", "content/config.json", "--content", "--raw"]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Could not acquire the selected package file.",
            result.Error, StringComparison.Ordinal);
        Assert.Equal(1, feed.FullPackageResponses);
    }

    [Fact]
    public async Task PackageCommand_LiteralDirectoryNameRetainsDirectorySemantics()
    {
        string id = $"Documents.Directory.{Guid.NewGuid():N}";
        byte[] package = CreatePackage(
            id, "README",
            extraEntries:
            [
                ("docs.v1/guide.txt", "Directory content"u8.ToArray()),
                ("docs.v1/nested/other.txt", "Not an immediate child"u8.ToArray()),
            ]);
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source, id, () => new ByteArrayContent(package), requests));

        var result = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "--path", "docs.v1", "--content", "--raw"]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Equal("Directory content", result.Output.Trim());
    }

#if DEBUG
    [Theory]
    [InlineData(false, "not exact file export")]
    [InlineData(true, "must name distinct files")]
    public async Task PackageCommand_LiteralFileEvidence_RejectsIncompatibleDestinationsBeforeAcquisition(
        bool structured,
        string expectedError)
    {
        int transports = 0;
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
        {
            transports++;
            throw new InvalidOperationException("Rejected evidence requested transport.");
        });
        string outputPath = Path.Combine(_root, "conflicting-output.json");
        string[] arguments =
        [
            "package", "Documents.Evidence@1.0.0", "--source", FirstFeed,
            "--path", "content/config.json", "--content",
            "--out", outputPath, "--evidence-envelope", outputPath,
        ];

        var result = await RunCommandAsync(
            structured ? [.. arguments, "--jsonl"] : arguments);

        Assert.Equal(1, result.Exit);
        Assert.Contains(expectedError, result.Error, StringComparison.Ordinal);
        Assert.Equal(0, transports);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task PackageCommand_LiteralFileEvidence_PublishesRelativeFileName()
    {
        string id = $"Documents.RelativeEvidence.{Guid.NewGuid():N}";
        byte[] package = CreatePackage(
            id, "README",
            extraEntries: [("content/config.json", "{}"u8.ToArray())]);
        UseFeed(new RangeHonoringFeedHandler(FirstFeed, id, package));
        string sidecar = $"literal-evidence-{Guid.NewGuid():N}.json";
        string fullPath = Path.GetFullPath(sidecar);
        try
        {
            var result = await RunCommandAsync(
                ["package", $"{id}@{Version}", "--source", FirstFeed,
                    "--path", "content/config.json", "--content", "--raw",
                    "--evidence-envelope", sidecar]);

            Assert.True(result.Exit == 0, result.Error);
            Assert.Contains(fullPath, result.Error, StringComparison.Ordinal);
            Assert.True(File.Exists(fullPath));
        }
        finally
        {
            File.Delete(fullPath);
        }
    }

    [Fact]
    public async Task PackageCommand_LiteralFileEvidence_RecordsAdmissionAndTransfer()
    {
        string id = $"Documents.LiteralEvidence.{Guid.NewGuid():N}";
        byte[] package = CreatePackage(
            id, "README",
            library: [1, 2, 3],
            extraEntries:
            [
                ("content/config.json", "{\"value\":1}"u8.ToArray()),
                ("content/filler.bin", RandomNumberGenerator.GetBytes(2 * 1024 * 1024)),
            ]);
        var feed = new RangeHonoringFeedHandler(FirstFeed, id, package);
        UseFeed(feed);
        string sidecar = Path.Combine(_root, "literal-evidence.json");
        string warmSidecar = Path.Combine(_root, "literal-evidence-warm.json");
        string[] arguments =
        [
            "package", $"{id}@{Version}", "--source", FirstFeed,
            "--path", "content/config.json", "--content", "--raw",
        ];

        var captured = await RunCommandAsync(
            [.. arguments, "--evidence-envelope", sidecar]);
        var ordinary = await RunCommandAsync(arguments);
        var warm = await RunCommandAsync(
            [.. arguments, "--evidence-envelope", warmSidecar]);

        Assert.True(captured.Exit == 0, captured.Error);
        Assert.True(ordinary.Exit == 0, ordinary.Error);
        Assert.True(warm.Exit == 0, warm.Error);
        Assert.Equal(ordinary.Output, captured.Output);
        Assert.Equal(ordinary.Output, warm.Output);
        using JsonDocument json = JsonDocument.Parse(
            await File.ReadAllTextAsync(sidecar, TestContext.Current.CancellationToken));
        Assert.Equal(
            "package-file-content",
            json.RootElement.GetProperty("result_kind").GetString());
        JsonElement evidence = json.RootElement.GetProperty("evidence");
        Assert.Equal(
            "Admitted", evidence.GetProperty("query_admission").GetString());
        Assert.False(evidence.TryGetProperty("library_selection", out _));
        Assert.Equal(0, evidence.GetProperty("surface_library_count").GetInt32());
        Assert.Equal(0, evidence.GetProperty("implementation_library_count").GetInt32());
        JsonElement acquisition = evidence.GetProperty("acquisition");
        Assert.Equal(
            id.ToLowerInvariant(), acquisition.GetProperty("package_id").GetString());
        Assert.Equal(
            "Ranged", acquisition.GetProperty("transfer").GetProperty("path").GetString());
        using JsonDocument cached = JsonDocument.Parse(
            await File.ReadAllTextAsync(warmSidecar, TestContext.Current.CancellationToken));
        JsonElement transfer = cached.RootElement.GetProperty("evidence")
            .GetProperty("acquisition").GetProperty("transfer");
        Assert.Equal("EntryCache", transfer.GetProperty("path").GetString());
        Assert.Equal(0, transfer.GetProperty("bytes_received").GetInt32());
        Assert.Empty(transfer.GetProperty("requests").EnumerateArray());
    }
#endif

    [Theory]
    [InlineData(true, "README content")]
    [InlineData(false, "PACKAGE content")]
    public async Task PackageCommand_ReadmeSection_SelectsPrimaryDocumentByRange(
        bool includeReadme,
        string expected)
    {
        string id = $"Documents.Primary.{Guid.NewGuid():N}";
        byte[] package = CreatePrimaryDocumentPackage(
            id,
            includeReadme);
        var feed = new RangeHonoringFeedHandler(
            FirstFeed,
            id,
            package);
        UseFeed(feed);

        var result = await RunCommandAsync(
            [
                "package",
                $"{id}@{Version}",
                "--source",
                FirstFeed,
                "-S",
                "README",
                "--verbose",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Equal(expected, result.Output.Trim());
        Assert.Equal(1, feed.FullPackageResponses);
        Assert.True(feed.RangedResponses > 0);
        Assert.True(
            feed.PackageBytesServed
                < 128 * 1024 + AbandonedProbeReadBound,
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");

        int requests = feed.FullPackageResponses
            + feed.RangedResponses;
        var raw = await RunCommandAsync(
            [
                "package",
                $"{id}@{Version}",
                "--source",
                FirstFeed,
                "-S",
                "README",
                "--raw",
            ]);

        Assert.True(raw.Exit == 0, raw.Error);
        Assert.Equal(expected, raw.Output.Trim());
        Assert.Equal(
            requests,
            feed.FullPackageResponses + feed.RangedResponses);
    }

    /// <summary>
    /// Gates 11 and 13, real asset Newtonsoft.Json 13.0.4 (2.5 MB, root
    /// README.md): the cold file projection is the size probe, the directory
    /// tail, and one exact entry span, and writes the README's exact
    /// bytes; warm file, raw, separator, and JSONL projections are answered by
    /// the entry cache with no package request.
    /// </summary>
    [Fact]
    public async Task PackageCommand_ReadmeContent_RealNewtonsoftArchive_ReadsOnlyTheExactEntryByRange()
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
            "--verbose",
        ];

        var cold = await RunCommandAsync(export);

        Assert.True(cold.Exit == 0, cold.Error);
        Assert.Empty(cold.Output);
        Assert.Equal(ReadEntry(package, "README.md"), File.ReadAllBytes(outputPath));
        // The one complete response is the size probe, abandoned before its
        // body; then the directory tail and one exact entry span.
        Assert.Equal(1, feed.FullPackageResponses);
        Assert.True(2 == feed.RangedResponses, string.Join("; ", feed.Ranges));
        ZipEntryExtent readme = Assert.Single(
            ZipEntryExtent.Read(package),
            static entry => entry.Name == "README.md");
        string span = feed.Ranges.Skip(1).Single();
        Assert.Equal(readme.Start, long.Parse(span.Split('-')[0]));
        Assert.True(
            long.Parse(span.Split('-')[1]) >= readme.End,
            span);
        Assert.True(
            feed.PackageBytesServed < 200_000,
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");
        Assert.Contains("1 of 24 entries", cold.Error, StringComparison.Ordinal);
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

        var stdout = await RunCommandAsync(
            ["package", $"{NewtonsoftId}@{NewtonsoftVersion}", "--source", FirstFeed,
                "--path", "README.md", "--content", "--raw",
                "--prefer-rendered-urls",
                "--verbose"]);

        Assert.True(stdout.Exit == 0, stdout.Error);
        Assert.Equal(
            Encoding.UTF8.GetString(ReadEntry(package, "README.md"))
                .ReplaceLineEndings("\n")
                .Trim(),
            stdout.Output.ReplaceLineEndings("\n").Trim());
        Assert.Contains(
            "EntryCache, 0 package requests, 0 bytes received",
            stdout.Error,
            StringComparison.Ordinal);
        Assert.Equal(ranged, feed.RangedResponses);
        Assert.Equal(served, feed.PackageBytesServed);

        var blocks = await RunCommandAsync(
            ["package", $"{NewtonsoftId}@{NewtonsoftVersion}", "--source", FirstFeed,
                "--path", "README.md", "--content",
                "--verbose"]);

        Assert.True(blocks.Exit == 0, blocks.Error);
        Assert.Contains(
            "------------ newtonsoft.json :: README.md ------------",
            blocks.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "EntryCache, 0 package requests, 0 bytes received",
            blocks.Error,
            StringComparison.Ordinal);

        var jsonl = await RunCommandAsync(
            ["package", $"{NewtonsoftId}@{NewtonsoftVersion}", "--source", FirstFeed,
                "--path", "README.md", "--content", "--jsonl",
                "--verbose"]);

        Assert.True(jsonl.Exit == 0, jsonl.Error);
        using JsonDocument json = JsonDocument.Parse(jsonl.Output);
        Assert.Equal(
            "README.md",
            json.RootElement.GetProperty("path").GetString());
        Assert.False(string.IsNullOrEmpty(
            json.RootElement.GetProperty("content").GetString()));
        Assert.Contains(
            "EntryCache, 0 package requests, 0 bytes received",
            jsonl.Error,
            StringComparison.Ordinal);
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
                "--verbose"]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains("RangeIgnored", result.Error, StringComparison.Ordinal);
        Assert.Equal(ReadEntry(package, "README.md"), File.ReadAllBytes(outputPath));
    }

    /// <summary>
    /// Gate 12, a boundary fixture modeled on the Skill layout of the real
    /// CrestApps.AgentSkills.Mcp.OrchardCore 1.2.0 package (a Skill folder
    /// with a <c>references/</c> subfolder beside other Skills), padded above
    /// the size cut because that package is under it: the export reads only
    /// the exact Skill entry.
    /// </summary>
    [Fact]
    public async Task PackageCommand_SkillExport_ReadsOnlyTheExactEntry()
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
                "--path", "skills/demo/SKILL.md", "--content", "--raw",
                "--verbose"]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Equal(Skill.Trim(), result.Output.Trim());
        Assert.Contains("1 of 6 entries", result.Error, StringComparison.Ordinal);
        Assert.Equal(1, feed.FullPackageResponses);
        Assert.True(
            feed.PackageBytesServed < 128 * 1024 + AbandonedProbeReadBound,
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");

        var export = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "--path", "skills/demo/SKILL.md", "--content", "--out", outputPath,
                ]);

        Assert.True(export.Exit == 0, export.Error);
        Assert.Empty(export.Output);
        Assert.Equal(Skill, File.ReadAllText(outputPath));
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
                ]);

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
                "FileDemand",
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

    private static byte[] CreatePrimaryDocumentPackage(
        string id,
        bool includeReadme)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            WriteEntry(
                archive,
                $"{id}.nuspec",
                $"""
                    <package><metadata>
                      <id>{id}</id>
                      <version>{Version}</version>
                      <authors>Payload tests</authors>
                      <description>Primary document fixture</description>
                    </metadata></package>
                    """);
            WriteEntry(archive, "PACKAGE.md", "PACKAGE content");
            if (includeReadme)
                WriteEntry(archive, "README.md", "README content");
            using Stream padding = archive.CreateEntry(
                    "content/padding.bin",
                    CompressionLevel.NoCompression)
                .Open();
            padding.Write(
                RandomNumberGenerator.GetBytes(2 * 1024 * 1024));
        }
        return buffer.ToArray();
    }
}
