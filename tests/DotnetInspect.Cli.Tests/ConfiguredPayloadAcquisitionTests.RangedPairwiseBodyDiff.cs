using System.Security.Cryptography;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Package endpoint scope gate 12
/// (docs/design/package-endpoint-scope.md#pathological-cases-and-gates): the
/// body views of pairwise <c>diff --package ID@A..B</c> read each endpoint's
/// implementation participants, never reference stubs, with or without
/// <c>--tfm</c>.
/// </summary>
public sealed partial class ConfiguredPayloadAcquisitionTests
{
    private const string UnsafeId = "System.Runtime.CompilerServices.Unsafe";

    /// <summary>
    /// The body views over System.Text.Json 9.0.0 and 10.0.0, which ship no
    /// <c>ref/</c>: Analysis Diff and Implementation Diff narrowed to one
    /// Type, a member-targeted analysis set that reads method bodies, and
    /// string literals over the one Library.
    /// </summary>
    private static readonly string[][] SystemTextJsonBodyViews =
    [
        ["--type", "System.Text.Json.Utf8JsonWriter", "-S", "Analysis Diff"],
        ["--type", "System.Text.Json.Utf8JsonWriter", "-S", "Implementation Diff"],
        [
            "--type", "System.Text.Json.JsonSerializer",
            "--member", "SerializeAsync:1",
            "--analysis", "allocation,il",
        ],
        ["--analysis", "string-literals", "--where", "Literal contains Json", "--json"],
    ];

    /// <summary>
    /// Gate 12 for a package without <c>ref/</c>: the implementation
    /// participants are the surface folder's assemblies, so the body views
    /// read only <c>lib/net8.0</c> by range, and the output equals the legacy
    /// oracle's.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_BodyViews_ReadImplementationParticipantsAndEqualLegacy()
    {
        Dictionary<string, byte[]> packages = await ReadSystemTextJsonPackagesAsync();
        string[][] requests =
            [.. SystemTextJsonBodyViews.Select(PairwiseSystemTextJsonDiff)];
        string[] legacy = await RunLegacyPairwiseDiffsAsync(
            SystemTextJsonId, packages, requests);

        var feed = new RangeHonoringHistoryFeedHandler(FirstFeed, SystemTextJsonId, packages);
        UseFeed(feed);
        for (int view = 0; view < requests.Length; view++)
        {
            var result = await RunCommandAsync([.. requests[view], "--verbose"]);

            Assert.True(legacy[view] == result.Output, result.Error);
            RecordedOutput.AssertEqual("PackageEndpointScope", result.Output, result.Error, view);
            Assert.Equal(
                2,
                CountOccurrences(
                    result.Error,
                    "(1 surface assembly and 1 implementation assembly for net8.0;"));
        }
        AssertEachCellReadsOnly(feed, packages, ["lib/net8.0"]);
    }

    /// <summary>
    /// Gate 12 for string literals over two Libraries: the real NUnit 4.1.0
    /// and 4.2.2 archives ship <c>nunit.framework.dll</c> and
    /// <c>nunit.framework.legacy.dll</c> in <c>lib/net6.0</c>. Each Library is
    /// its own subject, read as an implementation participant, and the
    /// output equals the legacy oracle's.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_StringLiteralsOverTwoLibraries_ReadParticipantsAndEqualLegacy()
    {
        Dictionary<string, byte[]> packages = await ReadNUnitPackagesAsync();
        string[][] requests =
        [
            .. new[] { "--json", "--table" }.Select(format => new[]
            {
                "diff",
                "--package", $"{NUnitId}@4.1.0..4.2.2",
                "--tfm", "net6.0",
                "--source", FirstFeed,
                "--analysis", "string-literals",
                "--where", "Literal contains {0}",
                format,
            }),
        ];
        string[] legacy = await RunLegacyPairwiseDiffsAsync(NUnitId, packages, requests);

        UseFeed(new RangeHonoringHistoryFeedHandler(FirstFeed, NUnitId, packages));
        for (int view = 0; view < requests.Length; view++)
        {
            var result = await RunCommandAsync([.. requests[view], "--verbose"]);

            Assert.True(result.Exit == 0, result.Error);
            Assert.True(legacy[view] == result.Output, result.Error);
            RecordedOutput.AssertEqual("PackageEndpointScope", result.Output, result.Error, view);
            Assert.Equal(
                2,
                CountOccurrences(
                    result.Error,
                    "(2 surface assemblies and 2 implementation assemblies for net6.0;"));
        }
        Assert.Contains("nunit.framework.dll", legacy[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// Gate 12 for a package that ships <c>ref/</c>: the real
    /// System.Runtime.CompilerServices.Unsafe 4.7.1 and 5.0.0 archives. The
    /// legacy path compares the reference stubs without <c>--tfm</c>, which
    /// carry the compiler's <c>Microsoft.CodeAnalysis.EmbeddedAttribute</c>,
    /// and fails with <c>--tfm</c> on its doubled selection. The scope
    /// compares the <c>lib/</c> implementation either way.
    /// </summary>
    [Fact]
    public async Task PairwiseDiff_BodyViewsOfAPackageWithRef_CompareTheImplementation()
    {
        var hashes = new Dictionary<string, string>
        {
            ["4.7.1"] = "52fca80d5f0ed286371cf1b519b039e9855dbf04c611f8d8479816d4eec82b85",
            ["5.0.0"] = "9de0114a92cf5333f110a849470a01ce13f12be70a222b4bc7b58162772c620a",
        };
        var packages = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach ((string version, string hash) in hashes)
        {
            byte[] package = await File.ReadAllBytesAsync(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "RealAssets",
                    "PackageEndpointScope",
                    $"system.runtime.compilerservices.unsafe.{version}.nupkg"),
                TestContext.Current.CancellationToken);
            Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(package)));
            packages.Add(version, package);
        }
        string[] request =
        [
            "diff",
            "--package", $"{UnsafeId}@4.7.1..5.0.0",
            "--source", FirstFeed,
            "-S", "Implementation Diff",
        ];
        string[] legacy = await RunLegacyPairwiseDiffsAsync(UnsafeId, packages, [request]);
        Assert.Contains("Microsoft.CodeAnalysis.EmbeddedAttribute", legacy[0], StringComparison.Ordinal);

        UseFeed(new RangeHonoringHistoryFeedHandler(FirstFeed, UnsafeId, packages));
        (string[] Arguments, string Framework)[] targets =
        [
            (request, "netcoreapp2.0"),
            ([.. request, "--tfm", "netstandard2.0"], "netstandard2.0"),
        ];
        for (int view = 0; view < targets.Length; view++)
        {
            (string[] arguments, string framework) = targets[view];
            var result = await RunCommandAsync([.. arguments, "--verbose"]);

            Assert.True(result.Exit == 0, result.Error);
            RecordedOutput.AssertEqual("PackageEndpointScope", result.Output, result.Error, view);
            Assert.Equal(
                2,
                CountOccurrences(
                    result.Error,
                    $"(1 surface assembly and 1 implementation assembly for {framework};"));
            Assert.DoesNotContain("EmbeddedAttribute", result.Output, StringComparison.Ordinal);
            Assert.Contains(
                "System.Runtime.Versioning.NonVersionableAttribute.#ctor()",
                result.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                "System.Runtime.CompilerServices.Unsafe.SkipInit&#96;1(!!0&amp;) | C# |  | added |",
                result.Output,
                StringComparison.Ordinal);
        }
    }
}
