using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspector.Packages;

using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Package endpoint scope gates 5 to 7 and 9 to 11
/// (docs/design/package-endpoint-scope.md#pathological-cases-and-gates):
/// every API view of pairwise <c>diff --package ID@A..B</c> opens each
/// endpoint as a package endpoint scope read by range, and its output is
/// byte-identical to the output recorded while the legacy extraction path
/// was the oracle (<see cref="RecordedOutput"/>).
/// </summary>
public sealed partial class ConfiguredPayloadAcquisitionTests
{
    private const string SystemTextJsonId = "System.Text.Json";
    private const string NUnitId = "NUnit";
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
    /// The merged-surface API views over the same archives: API changes (a
    /// member target leaves the Library API Diff route), API Finding
    /// Transitions, and an API analysis set with its default views, which
    /// reads the merged surface though each endpoint is one Library.
    /// </summary>
    private static readonly string[][] SystemTextJsonMergedSurfaceViews =
    [
        ["--member", "System.Text.Json.JsonSerializer.SerializeAsync:1"],
        ["--type", "System.Text.Json.JsonSerializer", "--analysis", "api", "-S", "Transitions"],
        ["--type", "System.Text.Json.JsonSerializer", "--analysis", "api"],
    ];

    /// <summary>
    /// Gates 5 and 6: Library API Diff output equals the recording; the cold
    /// run reads each endpoint's <c>lib/net8.0</c> folder and nothing
    /// else by range, and the next run answers from the entry cache with no
    /// package request.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_LibraryApiDiff_ReadsOnlyTheSurfaceFolderByRange()
    {
        Dictionary<string, byte[]> packages = await ReadSystemTextJsonPackagesAsync();
        string[][] requests =
            [.. SystemTextJsonLibraryApiDiffs.Select(PairwiseSystemTextJsonDiff)];

        var feed = new RangeHonoringHistoryFeedHandler(FirstFeed, SystemTextJsonId, packages);
        UseFeed(feed);
        for (int view = 0; view < requests.Length; view++)
        {
            int ranged = feed.RangedResponses;
            int full = feed.FullPackageResponses;
            long served = feed.PackageBytesServed;
            var result = await RunCommandAsync([.. requests[view], "--verbose"]);

            RecordedOutput.AssertEqual("PackageEndpointScope", result.Output, result.Error, view);
            Assert.Equal(
                2,
                CountOccurrences(result.Error, "Using package endpoint scope for System.Text.Json@"));
            if (view == 0)
            {
                // The directory first, then ranged reads of the surface
                // folder only; no complete request.
                Assert.Equal(0, feed.FullPackageResponses);
                AssertEachCellReadsOnly(feed, packages, ["lib/net8.0"]);
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
    /// Gate 11: API changes and API Finding Transitions read each endpoint's
    /// merged participant surface: only the surface folder is read by range,
    /// and the output equals the recording.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_MergedSurfaceViews_ReadOnlyTheSurfaceFolderByRange()
    {
        Dictionary<string, byte[]> packages = await ReadSystemTextJsonPackagesAsync();
        string[][] requests =
            [.. SystemTextJsonMergedSurfaceViews.Select(PairwiseSystemTextJsonDiff)];

        var feed = new RangeHonoringHistoryFeedHandler(FirstFeed, SystemTextJsonId, packages);
        UseFeed(feed);
        for (int view = 0; view < requests.Length; view++)
        {
            var result = await RunCommandAsync([.. requests[view], "--verbose"]);

            RecordedOutput.AssertEqual("PackageEndpointScope", result.Output, result.Error, view);
            Assert.Equal(
                2,
                CountOccurrences(result.Error, "Using package endpoint scope for System.Text.Json@"));
        }
        AssertEachCellReadsOnly(feed, packages, ["lib/net8.0"]);
    }

    /// <summary>
    /// Gate 16: the legacy route is retired. Every online view, API and
    /// body alike, opens both endpoints through the package endpoint scope
    /// and never announces the <c>AssemblySet</c> route, which only an
    /// offline request with an endpoint only discovery can settle takes.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_OnlineViews_NeverTakeTheAssemblySetRoute()
    {
        Dictionary<string, byte[]> packages = await ReadSystemTextJsonPackagesAsync();
        UseFeed(new RangeHonoringHistoryFeedHandler(FirstFeed, SystemTextJsonId, packages));
        string[][] views =
        [
            .. SystemTextJsonLibraryApiDiffs,
            .. SystemTextJsonMergedSurfaceViews,
            .. SystemTextJsonBodyViews,
            ["--type", "System.Text.Json.JsonSerializerOptions", "--member", ".ctor:1", "-S", "Implementation Diff"],
            ["--type", "System.Text.Json.JsonSerializerOptions", "--member", ".ctor:1", "-S", "Annotated Source Diff", "--json"],
        ];

        foreach (string[] view in views)
        {
            var result = await RunCommandAsync([.. PairwiseSystemTextJsonDiff(view), "--verbose"]);

            string request = string.Join(' ', view);
            Assert.True(result.Exit == 0, $"{request}: {result.Error}");
            Assert.True(
                CountOccurrences(result.Error, "Using package endpoint scope for System.Text.Json@") == 2,
                $"{request}: {result.Error}");
            Assert.DoesNotContain($"Comparing {SystemTextJsonId} v", result.Error, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Gate 7: offline, with both endpoints in the local package cache from
    /// an online run, the API views give the recorded output, and each opens
    /// both exact pins through the scope, with and without <c>--tfm</c>.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_OfflineWithCachedEndpoints_GivesTheSameOutput()
    {
        Dictionary<string, byte[]> packages = await ReadSystemTextJsonPackagesAsync();
        string[][] requests =
            [.. SystemTextJsonLibraryApiDiffs.Concat(SystemTextJsonMergedSurfaceViews)
                .Select(PairwiseSystemTextJsonDiff),
            PairwiseSystemTextJsonDiff([], targetFramework: null)];
        // The online runs fill the local package cache with complete
        // archives.
        UseFeed(new RangeHonoringHistoryFeedHandler(
            FirstFeed, SystemTextJsonId, packages, ignoreRange: true));
        foreach (string[] request in requests)
        {
            var online = await RunCommandAsync(request);
            Assert.True(online.Exit == 0, online.Error);
        }

        bool wasOffline = CoreHttpClientFactory.IsOffline;
        try
        {
            CoreHttpClientFactory.Initialize(
                new HttpClientFactoryOptions { Offline = true });
            UseFeed(new RejectNetworkHandler(new HttpClientHandler()));
            for (int view = 0; view < requests.Length; view++)
            {
                var offline = await RunCommandAsync([.. requests[view], "--verbose"]);
                RecordedOutput.AssertEqual("PackageEndpointScope", offline.Output, offline.Error, view);
                // Exact pins open through the scope offline, from the
                // legacy-compatible store, with no request.
                Assert.Equal(
                    2,
                    CountOccurrences(offline.Error, "Using package endpoint scope for System.Text.Json@"));
            }
        }
        finally
        {
            CoreHttpClientFactory.Initialize(
                new HttpClientFactoryOptions { Offline = wasOffline });
        }
    }

    /// <summary>
    /// Gate 11: the legacy selector merges every <c>lib</c>, <c>ref</c>, and
    /// <c>tools</c> DLL whose folder names the requested framework, so for
    /// the real Avalonia archives (<c>ref/net8.0</c> beside
    /// <c>lib/net8.0</c>) it reported change rows twice and an ambiguous
    /// generic-constraint binding as an inspection failure. The endpoint
    /// scope's merged surface is the House's compile selection, so a row
    /// repeats only for overloads and the comparison is complete; only that
    /// selection is read by range, and the same request again makes no
    /// package request.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_MultiLibrarySurface_ReportsEachChangeOnce()
    {
        Dictionary<string, byte[]> packages = await ReadAvaloniaHistoryPackagesAsync();
        string[] request =
        [
            "diff",
            "--package", $"{AvaloniaId}@{AvaloniaHistoryVersions[0]}..{AvaloniaHistoryVersions[1]}",
            "--tfm", "net8.0",
            "--source", FirstFeed,
            "--markdown",
        ];
        var feed = new RangeHonoringHistoryFeedHandler(FirstFeed, AvaloniaId, packages);
        UseFeed(feed);
        var result = await RunCommandAsync([.. request, "--verbose"]);
        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            "| Summary | **Summary:** 504 breaking, 582 additive, 220 potentially breaking across 432 types |",
            result.Output,
            StringComparison.Ordinal);
        // Only overloads repeat a row. The legacy selector's doubled
        // selection repeated nearly every row of a Type both folders ship.
        Assert.Equal(34, DuplicatedChangeRows(result.Output));
        Assert.DoesNotContain("metadata inspection failed", result.Output, StringComparison.Ordinal);
        Assert.Equal(
            2,
            CountOccurrences(result.Error, "surface assemblies for net8.0;"));
        AssertEachCellReadsOnly(feed, packages, ["ref/net8.0"]);

        int ranged = feed.RangedResponses;
        int full = feed.FullPackageResponses;
        long served = feed.PackageBytesServed;
        var warm = await RunCommandAsync([.. request, "--verbose"]);

        Assert.Equal(result.Output, warm.Output);
        Assert.Equal(ranged, feed.RangedResponses);
        Assert.Equal(full, feed.FullPackageResponses);
        Assert.Equal(served, feed.PackageBytesServed);
    }

    /// <summary>
    /// Gate 11 below the ranged size cut: the real NUnit 4.1.0 and 4.2.2
    /// archives (0.7 MB each) ship two Libraries in <c>lib/net6.0</c>, so
    /// the API changes read each endpoint's merged surface. Each archive is
    /// read once, directory first, and the output equals the recording.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_TwoLibrariesBelowTheSizeCut_DownloadEachArchiveOnce()
    {
        Dictionary<string, byte[]> packages = await ReadNUnitPackagesAsync();
        string[] request =
        [
            "diff",
            "--package", $"{NUnitId}@4.1.0..4.2.2",
            "--tfm", "net6.0",
            "--source", FirstFeed,
        ];
        var feed = new RangeHonoringHistoryFeedHandler(FirstFeed, NUnitId, packages);
        UseFeed(feed);
        var result = await RunCommandAsync([.. request, "--verbose"]);

        RecordedOutput.AssertEqual("PackageEndpointScope", result.Output, result.Error);
        Assert.Equal(
            2,
            CountOccurrences(result.Error, "(2 surface assemblies for net6.0;"));
        // Below the cut, each archive's directory is read first, then its
        // missing prefix, and the archive is assembled complete: every byte
        // once (docs/design/package-cache-policy.md#directory-first).
        long archives = packages.Values.Sum(static package => (long)package.Length);
        Assert.Equal(0, feed.FullPackageResponses);
        Assert.Equal(archives, feed.PackageBytesServed);
    }

    /// <summary>
    /// Both endpoints already in a NuGet global-packages folder: the scope
    /// opens both from it through the legacy-compatible store with no
    /// request, and the output equals the recording.
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

            var result = await RunCommandAsync([.. request, "--verbose"]);

            Assert.True(result.Exit == 0, result.Error);
            RecordedOutput.AssertEqual("PackageEndpointScope", result.Output, result.Error);
            Assert.Equal(
                2,
                CountOccurrences(result.Error, "Using package endpoint scope for System.Text.Json@"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", previousGlobalRoot);
        }
    }

    /// <summary>
    /// Gate 9: without <c>--tfm</c>, each endpoint selects the framework
    /// <c>package</c> selects for it, through the House's owner-default
    /// compile selection: <c>net9.0</c> for 9.0.0 and <c>net10.0</c> for
    /// 10.0.0, each read by range, and the versions name both. An explicit
    /// <c>--tfm</c> applies to both endpoints and names neither. Both equal
    /// the recording.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_WithoutTfm_EachEndpointSelectsItsOwnFramework()
    {
        Dictionary<string, byte[]> packages = await ReadSystemTextJsonPackagesAsync();
        string[][] requests =
        [
            PairwiseSystemTextJsonDiff(["--markdown"], targetFramework: null),
            PairwiseSystemTextJsonDiff(["--markdown"], targetFramework: "net9.0"),
        ];
        var feed = new RangeHonoringHistoryFeedHandler(FirstFeed, SystemTextJsonId, packages);
        UseFeed(feed);
        var ownerDefault = await RunCommandAsync([.. requests[0], "--verbose"]);

        RecordedOutput.AssertEqual("PackageEndpointScope", ownerDefault.Output, ownerDefault.Error, 0);
        Assert.Contains(
            "**9.0.0** (net9.0) → **10.0.0** (net10.0)",
            ownerDefault.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "Using package endpoint scope for System.Text.Json@9.0.0 (1 surface assembly for net9.0;",
            ownerDefault.Error,
            StringComparison.Ordinal);
        Assert.Contains(
            "Using package endpoint scope for System.Text.Json@10.0.0 (1 surface assembly for net10.0;",
            ownerDefault.Error,
            StringComparison.Ordinal);
        foreach ((string version, string folder) in new[] { ("9.0.0", "lib/net9.0"), ("10.0.0", "lib/net10.0") })
        {
            AssertEachCellReadsOnly(
                feed,
                new Dictionary<string, byte[]> { [version] = packages[version] },
                [folder]);
        }

        var explicitTarget = await RunCommandAsync([.. requests[1], "--verbose"]);

        RecordedOutput.AssertEqual("PackageEndpointScope", explicitTarget.Output, explicitTarget.Error, 1);
        Assert.Contains("**9.0.0** → **10.0.0**", explicitTarget.Output, StringComparison.Ordinal);
        Assert.Equal(
            2,
            CountOccurrences(explicitTarget.Error, "surface assembly for net9.0;"));

        // The owner default is the framework package selects for each version.
        foreach (string version in SystemTextJsonVersions)
        {
            (string selected, string framework) = await PackageSelectionAsync(version);
            Assert.Equal(version, selected);
            Assert.Contains(
                $"Using package endpoint scope for System.Text.Json@{version} (1 surface assembly for {framework};",
                ownerDefault.Error,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// An endpoint that does not open is reported from its typed failure by
    /// the scope route, never by the legacy path: a version no source
    /// supplies, a pattern no listed version matches, and a framework the
    /// package has no compile assets for.
    /// When both endpoints fail, whichever fails first cancels its sibling
    /// and is reported, so a framework mismatch names either version.
    /// </summary>
    [Theory]
    [InlineData(
        "9.0.0..99.0.0",
        null,
        "Error resolving v99.0.0: Package 'System.Text.Json' version '99.0.0' could not be acquired.")]
    [InlineData(
        "9.0.0..99.*",
        null,
        "Error resolving v99.*: Package 'System.Text.Json' selection '99.*' matched no available version.")]
    [InlineData(
        "9.0.0..10.0.0",
        "net2.0",
        "No assemblies found for target framework 'net2.0' in package 'System.Text.Json@")]
    public async Task PairwiseDiff_EndpointFailure_IsReportedByTheScope(
        string range,
        string? targetFramework,
        string expected)
    {
        Dictionary<string, byte[]> packages = await ReadSystemTextJsonPackagesAsync();
        UseFeed(new RangeHonoringHistoryFeedHandler(
            FirstFeed, SystemTextJsonId, packages, unlistedArchivesNotFound: true));

        var result = await RunCommandAsync(
        [
            "diff",
            "--package", $"{SystemTextJsonId}@{range}",
            .. targetFramework is null ? (string[])[] : ["--tfm", targetFramework],
            "--source", FirstFeed,
            "--verbose",
        ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.True(result.Error.Contains(expected, StringComparison.Ordinal), result.Error);
        Assert.Contains("did not open", result.Error, StringComparison.Ordinal);
        // The offline discovery route announces its comparison before it
        // resolves.
        Assert.DoesNotContain($"Comparing {SystemTextJsonId} v", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A source that doesn't answer is reported by the scope route with its
    /// failure, and says the package may still exist, as the legacy path's
    /// feed-failure report did.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_UnreadableSource_IsReportedByTheScope()
    {
        UseFeed(new UnreadableSourceHandler());

        var result = await RunCommandAsync(
        [
            "diff",
            "--package", $"{SystemTextJsonId}@9.0.0..10.0.0",
            "--source", FirstFeed,
            "--verbose",
        ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.True(
            result.Error.Contains(
                $"Package '{SystemTextJsonId}' version '9.0.0' could not be acquired.",
                StringComparison.Ordinal)
                || result.Error.Contains(
                    $"Package '{SystemTextJsonId}' version '10.0.0' could not be acquired.",
                    StringComparison.Ordinal),
            result.Error);
        Assert.Contains(
            "The package may exist; the source was not readable. Retry, or check the source URL.",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain($"Comparing {SystemTextJsonId} v", result.Error, StringComparison.Ordinal);
    }

    private sealed class UnreadableSourceHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("The source refused the connection.");
    }

    /// <summary>
    /// Gate 10: a <c>*</c> pattern and <c>latest</c> settle in the House, as
    /// <c>package</c> settles them, once per endpoint. The feed lists a
    /// prerelease above the highest stable version, so a pattern, which
    /// includes prereleases, and <c>latest</c>, which doesn't, settle
    /// differently. The output equals the recording, which keeps the
    /// spelling the request used.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_FloatingVersions_SettleInTheHouse()
    {
        Dictionary<string, byte[]> packages = await ReadSystemTextJsonPackagesAsync();
        const string Prerelease = "10.1.0-preview.1";
        packages.Add(Prerelease, RepackAs(packages["10.0.0"], Prerelease));
        (string From, string To)[] endpoints = [("9.0.0", "*"), ("9.*", "latest"), ("9.0.0", "10.*")];
        string[][] requests =
        [
            .. endpoints.Select(static pair => new[]
            {
                "diff", "--package", $"{SystemTextJsonId}@{pair.From}..{pair.To}",
                "--tfm", "net8.0", "--source", FirstFeed,
            }),
        ];
        var feed = new RangeHonoringHistoryFeedHandler(FirstFeed, SystemTextJsonId, packages);
        UseFeed(feed);
        var results = new List<(int Exit, string Output, string Error)>();
        foreach (string[] request in requests)
            results.Add(await RunCommandAsync([.. request, "--verbose"]));

        var settlements = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int view = 0; view < requests.Length; view++)
        {
            var result = results[view];
            RecordedOutput.AssertEqual("PackageEndpointScope", result.Output, result.Error, view);
            Assert.Equal(
                2,
                CountOccurrences(result.Error, "Using package endpoint scope for System.Text.Json@"));
            foreach (string spelling in new[] { endpoints[view].From, endpoints[view].To })
            {
                if (!spelling.Contains('*', StringComparison.Ordinal) && spelling != "latest")
                    continue;
                settlements[spelling] = await PackageSelectionVersionAsync(spelling);
                string settled = $"Package endpoint System.Text.Json@{spelling} settled to {settlements[spelling]}.";
                Assert.Equal(1, CountOccurrences(result.Error, settled));
            }
        }

        Assert.Equal(Prerelease, settlements["*"]);
        Assert.Equal("10.0.0", settlements["latest"]);
    }

    /// <summary>
    /// The scope route's demands: exact pins, <c>latest</c>, and <c>*</c>
    /// patterns online; an absent <c>--tfm</c> is the owner default. Offline,
    /// an endpoint only discovery can settle sends the whole request to the
    /// offline discovery route. A request the House can't express is an
    /// error.
    /// </summary>
    [Theory]
    [InlineData(false, "9.0.0", "10.0.0", null, "scope")]
    [InlineData(false, "9.0.0", "latest", "net8.0", "scope")]
    [InlineData(false, "9.*", "10.0.0", null, "scope")]
    [InlineData(true, "9.0.0", "10.0.0", null, "scope")]
    [InlineData(true, "9.0.0", "10.0.0", "net8.0", "scope")]
    [InlineData(true, "9.0.0", "latest", "net8.0", "discovery")]
    [InlineData(true, "9.*", "10.0.0", "net8.0", "discovery")]
    [InlineData(false, "9.0.0", "10.0.0", "all", "error")]
    [InlineData(false, "9.0.0", "not-a-version", null, "error")]
    public void PairwiseDiff_ScopeSelection_FollowsThePackageGrammar(
        bool offline,
        string from,
        string to,
        string? targetFramework,
        string route)
    {
        bool wasOffline = CoreHttpClientFactory.IsOffline;
        try
        {
            CoreHttpClientFactory.Initialize(
                new HttpClientFactoryOptions { Offline = offline });
            PackageEndpointDiffSelection? selection =
                PackageEndpointDiffSession.TryCreateSelection(
                    SystemTextJsonId, from, to, targetFramework, out string? error);

            Assert.Equal(
                route,
                selection is not null ? "scope" : error is null ? "discovery" : "error");
            if (selection is not null)
            {
                Assert.Equal(
                    targetFramework is null
                        ? PackageHouseTargetSelectionMode.OwnerDefault
                        : PackageHouseTargetSelectionMode.Exact,
                    selection.Target.Mode);
            }
        }
        finally
        {
            CoreHttpClientFactory.Initialize(
                new HttpClientFactoryOptions { Offline = wasOffline });
        }
    }

    /// <summary>
    /// The version and framework <c>package</c> selects for one spelling,
    /// read from its typed Package Info projection.
    /// </summary>
    private async Task<(string Version, string Framework)> PackageSelectionAsync(string spelling)
    {
        var result = await RunCommandAsync(
            ["package", $"{SystemTextJsonId}@{spelling}", "--source", FirstFeed,
                "-S", "Package Info", "--json"]);
        Assert.True(result.Exit == 0, result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement package = document.RootElement;
        return (
            package.GetProperty("version").GetString()!,
            package.GetProperty("package_info_measurements")
                .GetProperty("selected_target_framework").GetString()!);
    }

    private async Task<string> PackageSelectionVersionAsync(string spelling) =>
        (await PackageSelectionAsync(spelling)).Version;

    /// <summary>
    /// The same archive published as another version: its nuspec names that
    /// version, and the repository signature, which no longer matches, is
    /// dropped.
    /// </summary>
    private static byte[] RepackAs(byte[] package, string version)
    {
        var stream = new MemoryStream();
        stream.Write(package);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry(".signature.p7s")?.Delete();
            ZipArchiveEntry nuspec = archive.Entries.Single(
                static entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
            string text;
            using (var reader = new StreamReader(nuspec.Open()))
                text = reader.ReadToEnd();
            nuspec.Delete();
            int open = text.IndexOf("<version>", StringComparison.Ordinal) + "<version>".Length;
            int close = text.IndexOf("</version>", open, StringComparison.Ordinal);
            ZipArchiveEntry replaced = archive.CreateEntry(nuspec.FullName);
            using var writer = new StreamWriter(replaced.Open());
            writer.Write(string.Concat(text.AsSpan(0, open), version, text.AsSpan(close)));
        }
        return stream.ToArray();
    }

    private static string[] PairwiseSystemTextJsonDiff(string[] view) =>
        PairwiseSystemTextJsonDiff(view, "net8.0");

    private static string[] PairwiseSystemTextJsonDiff(string[] view, string? targetFramework) =>
    [
        "diff",
        "--package", $"{SystemTextJsonId}@{SystemTextJsonVersions[0]}..{SystemTextJsonVersions[1]}",
        .. targetFramework is null ? (string[])[] : ["--tfm", targetFramework],
        "--source", FirstFeed,
        .. view,
    ];

    /// <summary>
    /// The real NUnit 4.1.0 and 4.2.2 archives, each below the ranged size
    /// cut, which ship two Libraries in <c>lib/net6.0</c>.
    /// </summary>
    private static async Task<Dictionary<string, byte[]>> ReadNUnitPackagesAsync()
    {
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
        return packages;
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

    /// <summary>
    /// The change rows that repeat within one Type's section of a rendered
    /// diff.
    /// </summary>
    private static int DuplicatedChangeRows(string output)
    {
        int duplicates = 0;
        var section = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in output.Split('\n'))
        {
            if (line.StartsWith('#'))
                section.Clear();
            else if (line.StartsWith("- ", StringComparison.Ordinal) && !section.Add(line))
                duplicates++;
        }
        return duplicates;
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
