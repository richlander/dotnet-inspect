using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using DotnetInspector.Packages;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Package read demand gates 16 to 18 (docs/design/package-read-demand.md):
/// <c>diff --history</c> realizes each version cell by range, bounded by the
/// cell's asset demand, over the real <c>Avalonia</c> 11.3.14 and 12.1.2
/// archives (13.0 MB and 10.1 MB, each with <c>ref/net8.0</c> and
/// <c>lib/net8.0</c> folders beside other target frameworks).
/// </summary>
public sealed partial class ConfiguredPayloadAcquisitionTests
{
    private const string AvaloniaId = "Avalonia";
    private static readonly string[] AvaloniaHistoryVersions = ["11.3.14", "12.1.2"];

    /// <summary>
    /// Gates 16 and 18: a Metadata history reads each version's
    /// <c>ref/net8.0</c> folder and nothing else from its archive, its
    /// findings equal the complete path's, and the same history again makes
    /// no package request.
    /// </summary>
    [Fact]
    public async Task DiffHistory_MetadataCells_ReadOnlyTheSurfaceFolderByRange()
    {
        Dictionary<string, byte[]> packages = await ReadAvaloniaHistoryPackagesAsync();
        string[] history =
        [
            "diff", "--history",
            "--package", $"{AvaloniaId}@{AvaloniaHistoryVersions[0]}..{AvaloniaHistoryVersions[1]}",
            "--type", "Avalonia.Controls.Button",
            "--finding", "api.member",
            "--at", "all",
            "--tfm", "net8.0",
            "--source", FirstFeed,
            "--tips", "q",
        ];

        string complete = await RunCompleteHistoryAsync(packages, history);

        var feed = new RangeHonoringHistoryFeedHandler(FirstFeed, AvaloniaId, packages);
        UseFeed(feed);
        var ranged = await RunCommandAsync(history);

        Assert.True(ranged.Exit == 0, ranged.Error);
        Assert.Equal(complete, ranged.Output);
        Assert.Contains("## Evaluations", ranged.Output, StringComparison.Ordinal);
        // One abandoned size probe per version, then ranged reads only.
        Assert.Equal(AvaloniaHistoryVersions.Length, feed.FullPackageResponses);
        AssertEachCellReadsOnly(feed, packages, ["ref/net8.0"]);

        // Gate 18: the entry cache holds every folder the history read.
        int rangedResponses = feed.RangedResponses;
        long served = feed.PackageBytesServed;
        var warm = await RunCommandAsync(history);

        Assert.True(warm.Exit == 0, warm.Error);
        Assert.Equal(ranged.Output, warm.Output);
        Assert.Equal(AvaloniaHistoryVersions.Length, feed.FullPackageResponses);
        Assert.Equal(rangedResponses, feed.RangedResponses);
        Assert.Equal(served, feed.PackageBytesServed);
    }

    /// <summary>
    /// Gate 17: an Analysis history reads each version's surface and
    /// implementation folders, <c>ref/net8.0</c> and <c>lib/net8.0</c>, and
    /// nothing else, and its findings equal the complete path's.
    /// </summary>
    [Fact]
    public async Task DiffHistory_AnalysisCells_ReadTheSurfaceAndImplementationFoldersByRange()
    {
        Dictionary<string, byte[]> packages = await ReadAvaloniaHistoryPackagesAsync();
        string[] history =
        [
            "diff", "--history",
            "--package", $"{AvaloniaId}@{AvaloniaHistoryVersions[0]}..{AvaloniaHistoryVersions[1]}",
            "--type", "Avalonia.Controls.Button",
            "--member", "OnClick",
            "--all",
            "--finding", "analysis.allocation",
            "--at", "all",
            "--tfm", "net8.0",
            "--source", FirstFeed,
            "--tips", "q",
        ];

        string complete = await RunCompleteHistoryAsync(packages, history);

        var feed = new RangeHonoringHistoryFeedHandler(FirstFeed, AvaloniaId, packages);
        UseFeed(feed);
        var ranged = await RunCommandAsync(history);

        Assert.True(ranged.Exit == 0, ranged.Error);
        Assert.Equal(complete, ranged.Output);
        Assert.Contains("| Complete |", ranged.Output, StringComparison.Ordinal);
        Assert.Equal(AvaloniaHistoryVersions.Length, feed.FullPackageResponses);
        AssertEachCellReadsOnly(feed, packages, ["ref/net8.0", "lib/net8.0"]);
    }

#if DEBUG
    /// <summary>
    /// Transfer receipt delivery for a ranged history: each version cell of
    /// an archive above the size cut records an abandoned size probe, the
    /// directory tail, and entry spans, and a repeated history reads the
    /// entry cache with no request.
    /// </summary>
    [Fact]
    public async Task DiffHistoryEvidenceEnvelope_RangedCellsRecordTheirReads()
    {
        Dictionary<string, byte[]> packages = await ReadAvaloniaHistoryPackagesAsync();
        NuGetCache.Initialize(
            "dotnet-inspect-test", Path.Combine(_root, "cache-ranged"), skipNuGetCache: true);
        var feed = new RangeHonoringHistoryFeedHandler(FirstFeed, AvaloniaId, packages);
        UseFeed(feed);
        string cold = Path.Combine(_root, "ranged-history-evidence.json");
        string warm = Path.Combine(_root, "ranged-history-evidence-warm.json");
        string[] history =
        [
            "diff", "--history",
            "--package", $"{AvaloniaId}@{AvaloniaHistoryVersions[0]}..{AvaloniaHistoryVersions[1]}",
            "--type", "Avalonia.Controls.Button",
            "--finding", "api.type",
            "--at", "all",
            "--tfm", "net8.0",
            "--source", FirstFeed,
            "--tips", "q",
        ];

        var first = await RunCommandAsync([.. history, "--evidence-envelope", cold]);
        var second = await RunCommandAsync([.. history, "--evidence-envelope", warm]);

        Assert.True(first.Exit == 0, first.Error);
        Assert.True(second.Exit == 0, second.Error);
        Assert.Equal(first.Output, second.Output);
        foreach (JsonElement transfer in await ReadTransfersAsync(cold))
        {
            Assert.Equal("Ranged", transfer.GetProperty("path").GetString());
            string[] purposes =
            [
                .. transfer.GetProperty("requests").EnumerateArray()
                    .Select(static request => request.GetProperty("purpose").GetString()!),
            ];
            Assert.Equal(["SizeProbe", "DirectoryTail"], purposes[..2]);
            Assert.NotEmpty(purposes[2..]);
            Assert.All(purposes[2..], static purpose => Assert.Equal("EntrySpan", purpose));
        }
        foreach (JsonElement transfer in await ReadTransfersAsync(warm))
        {
            Assert.Equal("EntryCache", transfer.GetProperty("path").GetString());
            Assert.Empty(transfer.GetProperty("requests").EnumerateArray());
        }

        static async Task<JsonElement[]> ReadTransfersAsync(string sidecar)
        {
            using JsonDocument document = JsonDocument.Parse(
                await File.ReadAllTextAsync(sidecar, TestContext.Current.CancellationToken));
            JsonElement[] transfers =
            [
                .. document.RootElement.GetProperty("evidence").GetProperty("acquisitions")
                    .EnumerateArray()
                    .Select(static entry => entry.GetProperty("transfer").Clone()),
            ];
            Assert.Equal(AvaloniaHistoryVersions.Length, transfers.Length);
            return transfers;
        }
    }
#endif

    /// <summary>
    /// Runs a history on a separate cache through a feed that ignores
    /// <c>Range</c>, so every cell takes the complete download, and returns
    /// its output. The ranged run that follows uses a fresh cache.
    /// </summary>
    private async Task<string> RunCompleteHistoryAsync(
        Dictionary<string, byte[]> packages,
        string[] history)
    {
        NuGetCache.Initialize(
            "dotnet-inspect-test", Path.Combine(_root, "cache-complete"), skipNuGetCache: true);
        var feed = new RangeHonoringHistoryFeedHandler(
            FirstFeed, AvaloniaId, packages, ignoreRange: true);
        UseFeed(feed);
        var result = await RunCommandAsync(history);
        Assert.True(result.Exit == 0, result.Error);
        Assert.Equal(0, feed.RangedResponses);

        NuGetCache.Initialize(
            "dotnet-inspect-test", Path.Combine(_root, "cache-ranged"), skipNuGetCache: true);
        return result.Output;
    }

    private static async Task<Dictionary<string, byte[]>> ReadAvaloniaHistoryPackagesAsync()
    {
        var hashes = new Dictionary<string, string>
        {
            ["11.3.14"] = "1ce0bd27f4b320c42755259ea47d82ae855997e98d2e696ec0a885e4b6f673c7",
            ["12.1.2"] = "99987414c63ac3993346a84a852006df963ff839f96d140557ee31f8850db08f",
        };
        var packages = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string version in AvaloniaHistoryVersions)
        {
            byte[] package = await File.ReadAllBytesAsync(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "RealAssets",
                    "ApiMatching",
                    $"avalonia.{version}.nupkg"),
                TestContext.Current.CancellationToken);
            Assert.Equal(hashes[version], Convert.ToHexStringLower(SHA256.HashData(package)));
            packages.Add(version, package);
        }
        return packages;
    }

    /// <summary>
    /// Each cell's reads stay inside its allowed folders: every absolute span
    /// starts at an allowed entry, and any other bytes it crosses are a run
    /// no longer than the ranged merge gap (64 KiB), the price of one span
    /// instead of two across interleaved entries. The spans and the directory
    /// tail together read every direct entry of each allowed folder: the
    /// folder unit.
    /// </summary>
    private static void AssertEachCellReadsOnly(
        RangeHonoringHistoryFeedHandler feed,
        Dictionary<string, byte[]> packages,
        string[] folders)
    {
        const long MergeGap = 64 * 1024;
        foreach ((string version, byte[] package) in packages)
        {
            IReadOnlyList<ZipEntryExtent> entries = ZipEntryExtent.Read(package);
            RangeRead[] reads = [.. feed.Reads.Where(read => read.Version == version)];
            long tailStart = reads.Where(static read => read.Suffix).Min(static read => read.Start);
            RangeRead[] spans = [.. reads.Where(static read => !read.Suffix)];
            Assert.NotEmpty(spans);

            var read = new HashSet<string>(StringComparer.Ordinal);
            foreach (RangeRead span in spans)
            {
                ZipEntryExtent[] crossed =
                [
                    .. entries.Where(entry => entry.Start <= span.End && span.Start <= entry.End),
                ];
                Assert.True(
                    folders.Contains(crossed[0].Folder, StringComparer.Ordinal)
                        && crossed[0].Start == span.Start,
                    $"{version}: span {span.Start}-{span.End} starts in '{crossed[0].Name}'.");
                long otherRun = 0;
                foreach (ZipEntryExtent entry in crossed)
                {
                    if (folders.Contains(entry.Folder, StringComparer.Ordinal))
                    {
                        otherRun = 0;
                        if (entry.End <= span.End)
                            read.Add(entry.Name);
                        continue;
                    }
                    otherRun += Math.Min(entry.End, span.End) - Math.Max(entry.Start, span.Start) + 1;
                    Assert.True(
                        otherRun <= MergeGap,
                        $"{version}: span {span.Start}-{span.End} reads {otherRun} bytes outside "
                            + $"the allowed folders, through '{entry.Name}'.");
                }
            }

            foreach (string folder in folders)
            {
                string[] expected =
                [
                    .. entries
                        .Where(entry => entry.Folder == folder && entry.Start < tailStart)
                        .Select(static entry => entry.Name),
                ];
                Assert.NotEmpty(expected);
                Assert.All(
                    expected,
                    name => Assert.True(read.Contains(name), $"{version}: '{name}' was not read."));
            }
        }
    }

    private sealed record RangeRead(string Version, long Start, long End, bool Suffix);

    /// <summary>
    /// One archive entry's local record, from its local header to the byte
    /// before the next record (or the central directory), with its folder.
    /// </summary>
    private sealed record ZipEntryExtent(string Name, long Start, long End)
    {
        public string Folder =>
            Name.LastIndexOf('/') is var slash and >= 0 ? Name[..slash] : string.Empty;

        public static IReadOnlyList<ZipEntryExtent> Read(byte[] zip)
        {
            int end = zip.Length - 22;
            while (end >= 0
                && BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(end)) != 0x06054b50)
            {
                end--;
            }
            Assert.True(end >= 0, "The archive has no end of central directory record.");
            int count = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(end + 10));
            long directory = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(end + 16));

            var starts = new List<(string Name, long Start)>(count);
            int position = checked((int)directory);
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> header = zip.AsSpan(position);
                Assert.Equal(0x02014b50u, BinaryPrimitives.ReadUInt32LittleEndian(header));
                int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
                int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[30..]);
                int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
                long offset = BinaryPrimitives.ReadUInt32LittleEndian(header[42..]);
                starts.Add((Encoding.UTF8.GetString(header.Slice(46, nameLength)), offset));
                position += 46 + nameLength + extraLength + commentLength;
            }

            starts.Sort(static (left, right) => left.Start.CompareTo(right.Start));
            var entries = new List<ZipEntryExtent>(count);
            for (int i = 0; i < starts.Count; i++)
            {
                long next = i + 1 < starts.Count ? starts[i + 1].Start : directory;
                entries.Add(new(starts[i].Name, starts[i].Start, next - 1));
            }
            return entries;
        }
    }

    /// <summary>
    /// A v3 feed serving several versions of one package, whose flat
    /// container honors single <c>Range</c> requests with <c>206</c>,
    /// <c>Content-Range</c>, and an <c>ETag</c> (or ignores them), recording
    /// each version's reads.
    /// </summary>
    private sealed class RangeHonoringHistoryFeedHandler(
        string source,
        string id,
        IReadOnlyDictionary<string, byte[]> packages,
        bool ignoreRange = false) : HttpMessageHandler
    {
        private long _packageBytesServed;
        private int _rangedResponses;
        private int _fullPackageResponses;

        public long PackageBytesServed => Interlocked.Read(ref _packageBytesServed);

        public int RangedResponses => Volatile.Read(ref _rangedResponses);

        public int FullPackageResponses => Volatile.Read(ref _fullPackageResponses);

        public ConcurrentQueue<RangeRead> Reads { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            string flat = new Uri(new Uri(source), "flat2/").AbsoluteUri;
            string lower = id.ToLowerInvariant();
            if (url == source)
            {
                return Task.FromResult(Respond(
                    request,
                    HttpStatusCode.OK,
                    new StringContent($$"""
                        {"version":"3.0.0","resources":[
                          {"@id":"{{flat}}","@type":"PackageBaseAddress/3.0.0"}
                        ]}
                        """)));
            }
            if (url == $"{flat}{lower}/index.json")
            {
                return Task.FromResult(Respond(
                    request,
                    HttpStatusCode.OK,
                    new StringContent(JsonSerializer.Serialize(
                        new { versions = packages.Keys.ToArray() }))));
            }

            string? version = packages.Keys.SingleOrDefault(candidate =>
                url == $"{flat}{lower}/{candidate}/{lower}.{candidate}.nupkg");
            if (version is null)
                throw new InvalidOperationException($"Unexpected history-feed request: {url}");
            byte[] package = packages[version];
            var etag = new EntityTagHeaderValue($"\"history-{version}\"");

            RangeItemHeaderValue? range = request.Headers.Range?.Ranges.SingleOrDefault();
            if (ignoreRange || range is null)
            {
                Interlocked.Increment(ref _fullPackageResponses);
                HttpResponseMessage full = Respond(
                    request,
                    HttpStatusCode.OK,
                    new ByteArrayContent(package));
                full.Content.Headers.ContentLength = package.Length;
                full.Headers.ETag = etag;
                return Task.FromResult(full);
            }

            long start;
            long end;
            bool suffix = range.From is null;
            if (suffix)
            {
                long length = Math.Min(range.To!.Value, package.Length);
                start = package.Length - length;
                end = package.Length - 1;
            }
            else
            {
                start = range.From!.Value;
                end = Math.Min(range.To ?? package.Length - 1, package.Length - 1);
            }

            int count = checked((int)(end - start + 1));
            Reads.Enqueue(new(version, start, end, suffix));
            Interlocked.Increment(ref _rangedResponses);
            Interlocked.Add(ref _packageBytesServed, count);
            var content = new ByteArrayContent(package, (int)start, count);
            content.Headers.ContentRange =
                new ContentRangeHeaderValue(start, end, package.Length);
            HttpResponseMessage partial = Respond(
                request, HttpStatusCode.PartialContent, content);
            partial.Headers.ETag = etag;
            partial.Headers.AcceptRanges.Add("bytes");
            return Task.FromResult(partial);
        }

        private static HttpResponseMessage Respond(
            HttpRequestMessage request,
            HttpStatusCode status,
            HttpContent content) =>
            new(status)
            {
                Content = content,
                RequestMessage = request,
            };
    }
}
