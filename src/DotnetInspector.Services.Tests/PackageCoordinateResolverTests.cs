using System.Net;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Coordinate resolution for workspace members: a floating coordinate goes
/// through the product's listing-aware source and version policy, and an exact
/// pin performs no discovery at all.
/// </summary>
[Collection(CoreCacheCollection.Name)]
public sealed class PackageCoordinateResolverTests
{
    static readonly PackageSource NuGetOrg = PackageSource.NuGetOrg;

    [Fact]
    public async Task FloatingCoordinate_SelectsLatestListedStableVersion()
    {
        using var client = new HttpClient(new NuGetOrgHandler());

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate(
                    "UnlistedPkg",
                    Framework: "net10.0",
                    RuntimeIdentifier: "browser-wasm"),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken);
        var resolved =
            Assert.IsType<PackageCoordinateResolution.Resolved>(resolution);

        // 2.0.0 is the highest version the flat container reports and is
        // unlisted; the listed 1.5.0 is the floating answer.
        Assert.Equal("unlistedpkg", resolved.Coordinate.PackageId);
        Assert.Equal("1.5.0", resolved.Coordinate.Version);
        Assert.Equal("net10.0", resolved.Coordinate.Framework);
        Assert.Equal(
            "browser-wasm",
            resolved.Coordinate.RuntimeIdentifier);
        Assert.True(resolved.Coordinate.WasFloating);
        Assert.Equal(NuGetOrg, Assert.Single(resolved.Coordinate.Sources));
    }

    [Fact]
    public async Task FloatingCoordinate_WithPrerelease_SelectsLatestListedVersion()
    {
        using var client = new HttpClient(new NuGetOrgHandler());

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate("UnlistedPkg"),
                [NuGetOrg],
                includePrerelease: true,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        var resolved =
            Assert.IsType<PackageCoordinateResolution.Resolved>(resolution);

        Assert.Equal("1.5.0", resolved.Coordinate.Version);
        Assert.True(resolved.Coordinate.WasFloating);
    }

    [Fact]
    public async Task ExactCoordinate_PreservesUnlistedVersionWithoutDiscovery()
    {
        using var client = new HttpClient(new FailingHandler());

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate("UnlistedPkg", "2.0.0"),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken);
        var resolved =
            Assert.IsType<PackageCoordinateResolution.Resolved>(resolution);

        Assert.Equal("2.0.0", resolved.Coordinate.Version);
        Assert.False(resolved.Coordinate.WasFloating);
        Assert.Equal(NuGetOrg, Assert.Single(resolved.Coordinate.Sources));
    }

    [Fact]
    public void Validate_ReportsCoordinateShapeWithoutASource()
    {
        Assert.Null(
            PackageCoordinateResolver.Validate(
                new PackageCoordinate("Example", "1.0.0", "net10.0")));
        Assert.NotNull(
            PackageCoordinateResolver.Validate(
                new PackageCoordinate("Example", "latest")));
    }

    [Theory]
    [InlineData("1.0", "1.0.0")]
    [InlineData("1.0.0.0", "1.0.0")]
    [InlineData("1.0.0-BETA", "1.0.0-beta")]
    public async Task ExactCoordinate_UsesCanonicalVersion(
        string version,
        string expected)
    {
        using var client = new HttpClient(new FailingHandler());

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate("Example", version),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken);
        var resolved =
            Assert.IsType<PackageCoordinateResolution.Resolved>(resolution);

        Assert.Equal(expected, resolved.Coordinate.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.0.*")]
    [InlineData("1.0.0-beta.*")]
    [InlineData("1.0.0..2.0.0")]
    [InlineData("[1.0.0]")]
    [InlineData("1.0.0+build")]
    [InlineData(" 1.0.0 ")]
    public async Task ExactCoordinate_RejectsNonExactVersion(string version)
    {
        using var client = new HttpClient(new FailingHandler());

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate("Example", version),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsType<PackageCoordinateResolution.Invalid>(resolution);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" Example")]
    [InlineData("Example ")]
    public async Task Coordinate_RejectsInvalidPackageId(string packageId)
    {
        using var client = new HttpClient(new FailingHandler());

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate(packageId, "1.0.0"),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsType<PackageCoordinateResolution.Invalid>(resolution);
    }

    [Theory]
    [InlineData("../../admin")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("../")]
    [InlineData("sample/nested")]
    [InlineData("sample\\nested")]
    [InlineData("sample?version=1")]
    [InlineData("sample#fragment")]
    [InlineData("sample%2e%2e")]
    [InlineData("sample:1")]
    [InlineData("sample@feed")]
    [InlineData("https://feed.test/sample")]
    [InlineData("sample\u0007package")]
    [InlineData("sample\u0000package")]
    [InlineData("sample\npackage")]
    [InlineData("sample package")]
    [InlineData(".sample")]
    [InlineData("sample.")]
    [InlineData("-sample")]
    [InlineData("sample-")]
    [InlineData("sample..package")]
    [InlineData("sample.-package")]
    [InlineData("sämple")]
    public async Task Coordinate_RejectsAPackageIdOutsideTheGrammar(
        string packageId)
    {
        using var client = new HttpClient(new FailingHandler());

        // Exact and floating alike: the grammar decides before any source,
        // cache, or network step, and the throwing handler proves it.
        Assert.IsType<PackageCoordinateResolution.Invalid>(
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate(packageId, "1.0.0"),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken));
        Assert.IsType<PackageCoordinateResolution.Invalid>(
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate(packageId),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken));
        Assert.NotNull(
            PackageCoordinateResolver.Validate(
                new PackageCoordinate(packageId)));
    }

    [Fact]
    public async Task Coordinate_RejectsAPackageIdAboveTheLengthBound()
    {
        using var client = new HttpClient(new FailingHandler());
        string tooLong = new(
            'a',
            PackageCoordinateResolver.MaxPackageIdLength + 1);

        Assert.IsType<PackageCoordinateResolution.Invalid>(
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate(tooLong, "1.0.0"),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken));
        Assert.Null(
            PackageCoordinateResolver.Validate(
                new PackageCoordinate(
                    tooLong[..PackageCoordinateResolver.MaxPackageIdLength],
                    "1.0.0")));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("Newtonsoft.Json")]
    [InlineData("Microsoft.Extensions.DependencyInjection.Abstractions")]
    [InlineData("runtime.win-x64.Microsoft.NETCore.App")]
    [InlineData("xunit.v3")]
    [InlineData("NETStandard.Library")]
    [InlineData("Foo_Bar")]
    [InlineData("a_.b-c")]
    public void Coordinate_AcceptsRealPackageIds(string packageId)
    {
        // The close negative for the grammar: it is a bound on shape, not a
        // narrowing of the ids NuGet actually publishes.
        Assert.True(PackageCoordinateResolver.IsCanonicalPackageId(packageId));
        Assert.Null(
            PackageCoordinateResolver.Validate(
                new PackageCoordinate(packageId, "1.0.0")));
    }

    [Theory]
    [InlineData("net10.0\u0007", null)]
    [InlineData(null, "browser-wasm\u0001")]
    public async Task Coordinate_RejectsAControlBearingTarget(
        string? framework,
        string? runtimeIdentifier)
    {
        using var client = new HttpClient(new FailingHandler());

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate(
                    "Example",
                    "1.0.0",
                    framework,
                    runtimeIdentifier),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsType<PackageCoordinateResolution.Invalid>(resolution);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData(" net10.0", null)]
    [InlineData(null, "")]
    [InlineData(null, "browser-wasm ")]
    public async Task Coordinate_RejectsInvalidAcquisitionTarget(
        string? framework,
        string? runtimeIdentifier)
    {
        using var client = new HttpClient(new FailingHandler());

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate(
                    "Example",
                    "1.0.0",
                    framework,
                    runtimeIdentifier),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsType<PackageCoordinateResolution.Invalid>(resolution);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1.0.0")]
    public async Task Coordinate_WithNoAuthorizedSource_IsUnavailable(
        string? version)
    {
        using var client = new HttpClient(new FailingHandler());

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate("Example", version),
                [],
                cancellationToken:
                    TestContext.Current.CancellationToken);

        // An empty authorized set resolves nothing: this overload reads no
        // ambient NuGet configuration and never reinstates a default feed,
        // which the throwing handler would otherwise reveal.
        Assert.IsType<PackageCoordinateResolution.Unavailable>(resolution);
    }

    [Fact]
    public async Task FloatingResolution_DoesNotConsultTheCandidateCacheByDefault()
    {
        var handler = new NuGetOrgHandler();
        using var client = new HttpClient(handler);

        Assert.IsType<PackageCoordinateResolution.Resolved>(
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate("UnlistedPkg"),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken));
        int afterFirst = handler.RequestCount;

        Assert.IsType<PackageCoordinateResolution.Resolved>(
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate("UnlistedPkg"),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken));

        // Both attempts asked the feed: a browser host has no on-disk
        // candidate cache, so the default path must not depend on one.
        Assert.True(afterFirst > 0);
        Assert.Equal(afterFirst * 2, handler.RequestCount);
    }

    [Fact]
    public async Task InvalidPin_PrecedesSourceAvailability()
    {
        using var client = new HttpClient(new FailingHandler());

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate("Example", "latest"),
                [],
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsType<PackageCoordinateResolution.Invalid>(resolution);
    }

    [Fact]
    public async Task FloatingCoordinate_WithUnavailableListing_IsUnavailable()
    {
        using var client = new HttpClient(new NotFoundHandler());

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate("Example"),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsType<PackageCoordinateResolution.Unavailable>(resolution);
    }

    [Fact]
    public async Task SourcePolicy_WithInvalidConfig_IsUnavailable()
    {
        using var client = new HttpClient(new FailingHandler());
        string missingConfig = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.config");

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveUsingSourcePolicyAsync(
                client,
                new PackageCoordinate("Example", "1.0.0"),
                new NuGetSourceOptions { ConfigFile = missingConfig },
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<PackageCoordinateResolution.Unavailable>(resolution);
        Assert.Contains("not found", unavailable.Message);
    }

    [Fact]
    public async Task ExactCoordinate_ObservesCancellationBeforeSourceWork()
    {
        using var client = new HttpClient(new FailingHandler());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate("Example", "1.0.0"),
                [NuGetOrg],
                cancellationToken: cancellation.Token));
    }

    sealed class NuGetOrgHandler : HttpMessageHandler
    {
        int _requestCount;

        internal int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            string url = request.RequestUri!.ToString();
            string? body;
            if (url.Equals(
                "https://api.nuget.org/v3-flatcontainer/unlistedpkg/index.json",
                StringComparison.OrdinalIgnoreCase))
            {
                body = """{"versions":["1.5.0","2.0.0"]}""";
            }
            else if (url.Equals(
                "https://api.nuget.org/v3/registration5-gz-semver2/unlistedpkg/index.json",
                StringComparison.OrdinalIgnoreCase))
            {
                body =
                    """
                    {"items":[{"items":[
                      {"catalogEntry":{"version":"1.5.0","listed":true}},
                      {"catalogEntry":{"version":"2.0.0","listed":false}}
                    ]}]}
                    """;
            }
            else
            {
                body = null;
            }

            return Task.FromResult(
                body is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body),
                    });
        }
    }

    sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Exact package coordinate performed discovery: {request.RequestUri}");
    }
}
