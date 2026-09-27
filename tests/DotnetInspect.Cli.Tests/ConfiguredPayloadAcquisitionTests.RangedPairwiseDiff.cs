using System.IO.Compression;
using System.Security.Cryptography;

using DotnetInspect.Cli.Commands;
using DotnetInspector.Packages;

using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Package endpoint scope gates 5 to 7
/// (docs/design/package-endpoint-scope.md#pathological-cases-and-gates):
/// Library API Diff over pairwise <c>diff --package ID@A..B</c> opens each
/// endpoint as a package endpoint scope read by range, every other API view
/// keeps the legacy endpoints, and all output is byte-identical to the legacy
/// extraction path, which is the oracle.
/// </summary>
public sealed partial class ConfiguredPayloadAcquisitionTests
{
    private const string SystemTextJsonId = "System.Text.Json";
    private static readonly string[] SystemTextJsonVersions = ["9.0.0", "10.0.0"];

    /// <summary>
    /// Library API Diff over the real System.Text.Json 9.0.0 and 10.0.0
    /// archives (1.9 MB and 2.2 MB, one Library in <c>lib/net8.0</c>, no
    /// <c>ref/</c>): the whole comparison, and the same comparison narrowed
    /// to one Type.
    /// </summary>
    private static readonly string[][] SystemTextJsonLibraryApiDiffs =
    [
        [],
        ["--type", "System.Text.Json.JsonSerializer"],
    ];

    /// <summary>
    /// The merged-surface API views over the same archives, which the
    /// endpoint scope doesn't serve at this step: API changes (a member target
    /// leaves the Library API Diff route) and API Finding Transitions.
    /// </summary>
    private static readonly string[][] SystemTextJsonMergedSurfaceViews =
    [
        ["--member", "System.Text.Json.JsonSerializer.SerializeAsync:1"],
        ["--type", "System.Text.Json.JsonSerializer", "--finding", "api.member"],
    ];

    /// <summary>
    /// Gates 5 and 6: Library API Diff output equals the legacy path's; the
    /// cold run reads each endpoint's <c>lib/net8.0</c> and root folders and
    /// nothing else by range, and the next run answers from the entry cache
    /// with no package request.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_LibraryApiDiff_ReadsOnlyTheSurfaceFolderByRangeAndEqualsLegacy()
    {
        Dictionary<string, byte[]> packages = await ReadSystemTextJsonPackagesAsync();
        string[][] requests =
            [.. SystemTextJsonLibraryApiDiffs.Select(PairwiseSystemTextJsonDiff)];
        string[] legacy = await RunLegacyPairwiseDiffsAsync(
            SystemTextJsonId, packages, requests);

        var feed = new RangeHonoringHistoryFeedHandler(FirstFeed, SystemTextJsonId, packages);
        UseFeed(feed);
        for (int view = 0; view < requests.Length; view++)
        {
            int ranged = feed.RangedResponses;
            int full = feed.FullPackageResponses;
            long served = feed.PackageBytesServed;
            var result = await RunCommandAsync([.. requests[view], "--verbose"]);

            Assert.True(legacy[view] == result.Output, result.Error);
            Assert.Equal(
                2,
                CountOccurrences(result.Error, "Using package endpoint scope for System.Text.Json@"));
            Assert.DoesNotContain("takes the legacy path", result.Error, StringComparison.Ordinal);
            if (view == 0)
            {
                // One abandoned size probe per endpoint, then ranged reads of
                // the root folder (the admission check) and the surface
                // folder only.
                Assert.Equal(SystemTextJsonVersions.Length, feed.FullPackageResponses);
                AssertEachCellReadsOnly(feed, packages, ["lib/net8.0", ""]);
                continue;
            }

            // Gate 6: the entry cache answers the same endpoints again.
            Assert.Equal(ranged, feed.RangedResponses);
            Assert.Equal(full, feed.FullPackageResponses);
            Assert.Equal(served, feed.PackageBytesServed);
        }

        long cold = feed.PackageBytesServed;
        Assert.True(
            cold < packages.Values.Sum(static package => (long)package.Length) / 2,
            $"served {cold} package bytes");
    }

    /// <summary>
    /// API changes and API Finding Transitions take the legacy path before
    /// any package read, under every host: nothing is read by range, and the
    /// output equals the legacy path's.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_MergedSurfaceViews_ReadNothingByRangeAndEqualLegacy()
    {
        Dictionary<string, byte[]> packages = await ReadSystemTextJsonPackagesAsync();
        string[][] requests =
            [.. SystemTextJsonMergedSurfaceViews.Select(PairwiseSystemTextJsonDiff)];
        string[] legacy = await RunLegacyPairwiseDiffsAsync(
            SystemTextJsonId, packages, requests);

        var feed = new RangeHonoringHistoryFeedHandler(FirstFeed, SystemTextJsonId, packages);
        UseFeed(feed);
        for (int view = 0; view < requests.Length; view++)
        {
            var result = await RunCommandAsync([.. requests[view], "--verbose"]);

            Assert.True(legacy[view] == result.Output, result.Error);
            Assert.DoesNotContain(
                "package endpoint", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, feed.RangedResponses);
        }
    }

    /// <summary>
    /// Gate 7: offline, with both endpoints in the local package cache, the
    /// API views give the same output; offline requests take the legacy path.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_OfflineWithCachedEndpoints_GivesTheSameOutput()
    {
        Dictionary<string, byte[]> packages = await ReadSystemTextJsonPackagesAsync();
        string[][] requests =
            [.. SystemTextJsonLibraryApiDiffs.Concat(SystemTextJsonMergedSurfaceViews)
                .Select(PairwiseSystemTextJsonDiff)];
        string[] online = await RunLegacyPairwiseDiffsAsync(
            SystemTextJsonId, packages, requests, keepCache: true);

        bool wasOffline = CoreHttpClientFactory.IsOffline;
        try
        {
            CoreHttpClientFactory.Initialize(
                new HttpClientFactoryOptions { Offline = true });
            UseFeed(new RejectNetworkHandler(new HttpClientHandler()));
            for (int view = 0; view < requests.Length; view++)
            {
                var offline = await RunCommandAsync(requests[view]);
                Assert.True(online[view] == offline.Output, offline.Error);
            }
        }
        finally
        {
            CoreHttpClientFactory.Initialize(
                new HttpClientFactoryOptions { Offline = wasOffline });
        }
    }

    /// <summary>
    /// The legacy selector merges every <c>lib</c>, <c>ref</c>, and
    /// <c>tools</c> DLL whose folder names the requested framework, so for
    /// the real Avalonia archives (<c>ref/net8.0</c> beside
    /// <c>lib/net8.0</c>) it selects more than the endpoint scope's surface.
    /// Those requests are excluded from the scope route before any surface
    /// folder is read, stay byte-identical because the legacy path serves
    /// them, and cost no network when repeated.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_LegacySelectionBeyondTheSurface_TakesTheLegacyPath()
    {
        Dictionary<string, byte[]> packages = await ReadAvaloniaHistoryPackagesAsync();
        string[] request =
        [
            "diff",
            "--package", $"{AvaloniaId}@{AvaloniaHistoryVersions[0]}..{AvaloniaHistoryVersions[1]}",
            "--type", "Avalonia.Controls.Button",
            "--tfm", "net8.0",
            "--source", FirstFeed,
            "--tips", "q",
        ];
        string[] legacy = await RunLegacyPairwiseDiffsAsync(AvaloniaId, packages, [request]);

        var feed = new RangeHonoringHistoryFeedHandler(FirstFeed, AvaloniaId, packages);
        UseFeed(feed);
        var result = await RunCommandAsync([.. request, "--verbose"]);

        Assert.True(legacy[0] == result.Output, result.Error);
        // Admission is decided from the archive directory: both endpoints'
        // checks run to completion, and their ranged reads before the legacy
        // download touch the root folder only, never a surface folder.
        AssertEachCellReadsOnly(feed, packages, [""]);
        foreach (string version in AvaloniaHistoryVersions)
        {
            Assert.Contains(
                $"Package endpoint {AvaloniaId}@{version} takes the legacy path: "
                    + "the legacy selector picks [lib/net8.0/Avalonia.Base.dll",
                result.Error,
                StringComparison.Ordinal);
        }

        // The same request again makes no package request at all: both
        // directories are in the entry cache and both archives in the
        // legacy cache.
        int ranged = feed.RangedResponses;
        int full = feed.FullPackageResponses;
        long served = feed.PackageBytesServed;
        var warm = await RunCommandAsync([.. request, "--verbose"]);

        Assert.True(legacy[0] == warm.Output, warm.Error);
        Assert.Equal(ranged, feed.RangedResponses);
        Assert.Equal(full, feed.FullPackageResponses);
        Assert.Equal(served, feed.PackageBytesServed);
    }

    /// <summary>
    /// A fallback below the ranged size cut: the real NUnit 4.1.0 and 4.2.2
    /// archives (0.7 MB each) ship two Libraries in <c>lib/net6.0</c>, so
    /// Library API Diff doesn't apply. The admission check reads each
    /// directory by range whatever the size, so no archive is downloaded
    /// before the legacy path downloads it once; the output equals the
    /// legacy oracle's.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_FallbackBelowTheSizeCut_DownloadsEachArchiveOnce()
    {
        const string NUnitId = "NUnit";
        var hashes = new Dictionary<string, string>
        {
            ["4.1.0"] = "b2bce3d257f645e2b0e354e78a06707fcaea28a373191455ae0377851fef463a",
            ["4.2.2"] = "fb4392ebb2136a5986f5aad80a0405ffe61b98f467077a4823291ec3492fa027",
        };
        var packages = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach ((string version, string hash) in hashes)
        {
            byte[] package = await File.ReadAllBytesAsync(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "RealAssets",
                    "PackageEndpointScope",
                    $"nunit.{version}.nupkg"),
                TestContext.Current.CancellationToken);
            Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(package)));
            Assert.True(package.Length < 1_000_000);
            packages.Add(version, package);
        }
        string[] request =
        [
            "diff",
            "--package", $"{NUnitId}@4.1.0..4.2.2",
            "--tfm", "net6.0",
            "--source", FirstFeed,
            "--tips", "q",
        ];
        string[] legacy = await RunLegacyPairwiseDiffsAsync(NUnitId, packages, [request]);

        var feed = new RangeHonoringHistoryFeedHandler(FirstFeed, NUnitId, packages);
        UseFeed(feed);
        var result = await RunCommandAsync([.. request, "--verbose"]);

        Assert.True(legacy[0] == result.Output, result.Error);
        Assert.Equal(
            2,
            CountOccurrences(result.Error, "takes the legacy path: its surface has 2 Libraries"));
        // Each archive's body is read whole once, by the legacy download; the
        // admission check read only its directory and root folder by range.
        long archives = packages.Values.Sum(static package => (long)package.Length);
        Assert.True(
            feed.FullBodyBytesRead >= archives
                && feed.FullBodyBytesRead <= archives + (archives / 4),
            $"complete-response bodies read {feed.FullBodyBytesRead} bytes: "
                + string.Join(", ", feed.FullBodyReadsByResponse()));
        AssertEachCellReadsOnly(feed, packages, [""]);
    }

    /// <summary>
    /// Both endpoints already in a NuGet global-packages folder: the legacy
    /// path answers Library API Diff from it with no network, so the scope
    /// route steps aside before any request and the output equals the
    /// legacy oracle's.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_EndpointsInGlobalPackages_MakeNoRequest()
    {
        Dictionary<string, byte[]> packages = await ReadSystemTextJsonPackagesAsync();
        string globalRoot = Path.Combine(_root, "global-packages");
        foreach ((string version, byte[] package) in packages)
        {
            string directory = Path.Combine(
                globalRoot, SystemTextJsonId.ToLowerInvariant(), version);
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(
                Path.Combine(directory, $"{SystemTextJsonId.ToLowerInvariant()}.{version}.nupkg"),
                package,
                TestContext.Current.CancellationToken);
            using (var archive = new ZipArchive(new MemoryStream(package)))
                archive.ExtractToDirectory(directory);
            await File.WriteAllTextAsync(
                Path.Combine(directory, ".nupkg.metadata"),
                "{\"version\":2,\"source\":\"" + FirstFeed + "\"}",
                TestContext.Current.CancellationToken);
        }

        string? previousGlobalRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Environment.SetEnvironmentVariable("NUGET_PACKAGES", globalRoot);
        NuGetCache.Initialize(
            "dotnet-inspect-test", Path.Combine(_root, "cache-global"), skipNuGetCache: false);
        try
        {
            UseFeed(new RejectNetworkHandler(new HttpClientHandler()));
            string[] request = PairwiseSystemTextJsonDiff([]);
            DiffCommand.LegacyPackageEndpointsForTesting.Value = true;
            (int Exit, string Output, string Error) legacy;
            try
            {
                legacy = await RunCommandAsync(request);
            }
            finally
            {
                DiffCommand.LegacyPackageEndpointsForTesting.Value = false;
            }
            Assert.True(legacy.Exit == 0, legacy.Error);

            var result = await RunCommandAsync([.. request, "--verbose"]);

            Assert.True(result.Exit == 0, result.Error);
            Assert.Equal(legacy.Output, result.Output);
            Assert.Contains(
                "endpoints are in the local package cache",
                result.Error,
                StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", previousGlobalRoot);
        }
    }

    private static string[] PairwiseSystemTextJsonDiff(string[] view) =>
    [
        "diff",
        "--package", $"{SystemTextJsonId}@{SystemTextJsonVersions[0]}..{SystemTextJsonVersions[1]}",
        "--tfm", "net8.0",
        "--source", FirstFeed,
        "--tips", "q",
        .. view,
    ];

    /// <summary>
    /// Runs each request through the legacy endpoint path, the identity
    /// oracle, on its own cache and a feed that serves complete archives,
    /// then leaves a fresh cache for the run under test unless
    /// <paramref name="keepCache"/> keeps the legacy one.
    /// </summary>
    private async Task<string[]> RunLegacyPairwiseDiffsAsync(
        string id,
        Dictionary<string, byte[]> packages,
        string[][] requests,
        bool keepCache = false)
    {
        NuGetCache.Initialize(
            "dotnet-inspect-test", Path.Combine(_root, "cache-legacy"), skipNuGetCache: true);
        var feed = new RangeHonoringHistoryFeedHandler(FirstFeed, id, packages, ignoreRange: true);
        UseFeed(feed);
        var outputs = new string[requests.Length];
        DiffCommand.LegacyPackageEndpointsForTesting.Value = true;
        try
        {
            for (int index = 0; index < requests.Length; index++)
            {
                var result = await RunCommandAsync([.. requests[index], "--verbose"]);
                Assert.True(result.Output.Length > 0, result.Error);
                Assert.DoesNotContain(
                    "package endpoint", result.Error, StringComparison.OrdinalIgnoreCase);
                outputs[index] = result.Output;
            }
        }
        finally
        {
            DiffCommand.LegacyPackageEndpointsForTesting.Value = false;
        }
        Assert.Equal(0, feed.RangedResponses);

        if (!keepCache)
        {
            NuGetCache.Initialize(
                "dotnet-inspect-test", Path.Combine(_root, "cache-scoped"), skipNuGetCache: true);
        }
        return outputs;
    }

    private static async Task<Dictionary<string, byte[]>> ReadSystemTextJsonPackagesAsync()
    {
        var hashes = new Dictionary<string, string>
        {
            ["9.0.0"] = "68ce43878a242e70eff78d339a8140cd19aa66b7bcdee401b46388989a6822a9",
            ["10.0.0"] = "67200c213df240325acd800e6c0957992706b21183f4c55981898ba87c880aad",
        };
        var packages = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string version in SystemTextJsonVersions)
        {
            byte[] package = await File.ReadAllBytesAsync(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "RealAssets",
                    "PackageEndpointScope",
                    $"system.text.json.{version}.nupkg"),
                TestContext.Current.CancellationToken);
            Assert.Equal(hashes[version], Convert.ToHexStringLower(SHA256.HashData(package)));
            packages.Add(version, package);
        }
        return packages;
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal);
            index >= 0;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
