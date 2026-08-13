using System.Net;
using DotnetInspector.Core;
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

    /// <summary>
    /// A bidirectional override is not a control character, so a
    /// control-character test admits it and it then reorders every message and
    /// coordinate it appears in. The target grammar is an allow list, so the
    /// whole non-ASCII class is outside it by construction.
    /// </summary>
    [Theory]
    [InlineData("net10.0\u202e")]
    [InlineData("\u202enet10.0")]
    [InlineData("net10.0\u200b")]
    [InlineData("net10.0\u2066x")]
    [InlineData("net\u00a010.0")]
    [InlineData("nét10.0")]
    [InlineData("net 10.0")]
    [InlineData("net10.0/../etc")]
    [InlineData("net10.0\\x")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("-net10.0")]
    [InlineData("net10.0-")]
    [InlineData(".net10.0")]
    [InlineData("net10..0")]
    [InlineData("net10.-0")]
    [InlineData("net10.0?x=1")]
    [InlineData("net10.0#x")]
    [InlineData("")]
    [InlineData(" net10.0")]
    [InlineData("net10.0 ")]
    public async Task Coordinate_RejectsATargetOutsideTheGrammar(string target)
    {
        using var client = new HttpClient(new FailingHandler());

        Assert.False(PackageCoordinateResolver.IsAcquisitionTargetText(target));
        Assert.IsType<PackageCoordinateResolution.Invalid>(
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate("Example", "1.0.0", target),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken));
        Assert.IsType<PackageCoordinateResolution.Invalid>(
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate("Example", "1.0.0", "net10.0", target),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Coordinate_RejectsATargetAboveTheLengthBound()
    {
        Assert.False(
            PackageCoordinateResolver.IsAcquisitionTargetText(
                new string(
                    'a',
                    PackageCoordinateResolver.MaxAcquisitionTargetLength + 1)));
        Assert.True(
            PackageCoordinateResolver.IsAcquisitionTargetText(
                new string(
                    'a',
                    PackageCoordinateResolver.MaxAcquisitionTargetLength)));
    }

    /// <summary>
    /// The close positive for the grammar: it is a bound on shape, not a
    /// narrowing of the frameworks and runtimes this product consumes.
    /// </summary>
    [Theory]
    [InlineData("net10.0")]
    [InlineData("net8.0")]
    [InlineData("netstandard2.0")]
    [InlineData("netcoreapp3.1")]
    [InlineData("net481")]
    [InlineData("net8.0-windows")]
    [InlineData("net8.0-windows10.0.19041.0")]
    [InlineData("net9.0-android34.0")]
    [InlineData("net8.0-ios17.2")]
    [InlineData("uap10.0")]
    [InlineData("xamarin.ios")]
    [InlineData("monoandroid12.0")]
    [InlineData("portable-net45+win8+wpa81")]
    [InlineData("browser-wasm")]
    [InlineData("linux-x64")]
    [InlineData("linux-musl-arm64")]
    [InlineData("osx.13-arm64")]
    [InlineData("win10-x64")]
    [InlineData("alpine.3.18-x64")]
    [InlineData("tizen.6.0.0-armel")]
    [InlineData("any")]
    public async Task Coordinate_AcceptsRealTargetSpellings(string target)
    {
        using var client = new HttpClient(new FailingHandler());

        Assert.True(PackageCoordinateResolver.IsAcquisitionTargetText(target));
        var resolved = Assert.IsType<PackageCoordinateResolution.Resolved>(
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate("Example", "1.0.0", target, target),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken));
        Assert.Equal(target, resolved.Coordinate.Framework);
        Assert.Equal(target, resolved.Coordinate.RuntimeIdentifier);
    }

    /// <summary>
    /// The rejected version text is the value that just failed a grammar, so it
    /// is the most hostile string the resolver has seen. Quoting it into the
    /// message reopens on the error path the channel the check just closed.
    /// </summary>
    [Theory]
    [InlineData("1.0.0\u001b[31m")]
    [InlineData("\u202e1.0.0")]
    [InlineData("latest")]
    [InlineData("1.0.*")]
    [InlineData("[1.0.0]")]
    public async Task InvalidVersion_MessageDoesNotQuoteTheRejectedText(
        string version)
    {
        using var client = new HttpClient(new FailingHandler());

        var invalid = Assert.IsType<PackageCoordinateResolution.Invalid>(
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate("Example", version),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.DoesNotContain(version, invalid.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('\u001b', invalid.Message);
        Assert.DoesNotContain('\u202e', invalid.Message);
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

    /// <summary>
    /// A flat-container base can carry a signature in its query. Appending the
    /// version-index path as text puts that path inside the query value, so the
    /// request asks the container root for nothing; the path has to be appended
    /// to the path, with the signature preserved.
    /// </summary>
    [Fact]
    public async Task FloatingCoordinate_WithASignedFlatContainerBase_Resolves()
    {
        var handler = new SignedFlatContainerHandler();
        using var client = new HttpClient(handler);

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate(SignedFlatContainerHandler.PackageId),
                [SignedFlatContainerHandler.Feed],
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var resolved =
            Assert.IsType<PackageCoordinateResolution.Resolved>(resolution);
        Assert.Equal("1.5.0", resolved.Coordinate.Version);

        string indexRequest = Assert.Single(
            handler.Requests,
            url => url.Contains("/flat/", StringComparison.Ordinal));
        var requested = new Uri(indexRequest);
        Assert.Equal(
            $"/flat/{SignedFlatContainerHandler.PackageId}/index.json",
            requested.AbsolutePath);
        Assert.Equal("?sig=abc", requested.Query);
    }

    /// <summary>
    /// Floating resolution logs the version-index URL before the retry helper
    /// ever runs, so the signature a signed flat-container base carries reaches
    /// a log line on the resolution path, not only the download path.
    /// </summary>
    [Fact]
    public async Task FloatingResolution_SignedIndexUrl_NeverReachesALogLine()
    {
        const string secret = "s3cr3t-signature-value";
        var handler = new SignedFlatContainerHandler(secret);
        using var client = new HttpClient(handler);
        List<string> logs = [];

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate(SignedFlatContainerHandler.PackageId),
                [SignedFlatContainerHandler.Feed],
                log: line =>
                {
                    lock (logs)
                        logs.Add(line);
                },
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsType<PackageCoordinateResolution.Resolved>(resolution);

        // The signature travelled on the wire, exactly as the feed declared it.
        Assert.Contains(
            handler.Requests,
            url => url.Contains(secret, StringComparison.Ordinal));

        // And nowhere else. The "Fetching versions from" line is the one this
        // covers; it is emitted before the shared retry helper sees the URL.
        Assert.Contains(
            logs,
            line => line.Contains(
                "Fetching versions from",
                StringComparison.Ordinal));
        Assert.All(
            logs,
            line => Assert.DoesNotContain(
                secret,
                line,
                StringComparison.Ordinal));
    }

    /// <summary>
    /// The legacy CLI contract, preserved: a package published only as previews
    /// is still the package the user named, so the shared version policy falls
    /// back to the newest prerelease when a feed carries no stable release.
    /// This is the <c>Aspire.OpenAI</c> shape — a real flat-container index
    /// whose every version is a preview — and tightening the shared helper made
    /// <c>dotnet inspect package Aspire.OpenAI</c> fail from a cold cache.
    /// </summary>
    [Fact]
    public async Task LegacyResolution_WithOnlyPrereleases_FallsBackToTheNewestPrerelease()
    {
        using var client = new HttpClient(
            new ListedVersionsHandler("9.0.0-preview.1", "9.0.0-preview.2"));

        PackageVersionResolution? resolution =
            await DotnetInspector.Packages.PackageExtractor.ResolveLatestVersionAsync(
                client,
                ListedVersionsHandler.PackageId,
                [NuGetOrg],
                log: null,
                skipCache: true,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("9.0.0-preview.2", resolution?.Version);
    }

    [Fact]
    public async Task LegacyResolution_WithAStableRelease_PrefersIt()
    {
        using var client = new HttpClient(
            new ListedVersionsHandler("1.0.0", "2.0.0-beta"));

        PackageVersionResolution? resolution =
            await DotnetInspector.Packages.PackageExtractor.ResolveLatestVersionAsync(
                client,
                ListedVersionsHandler.PackageId,
                [NuGetOrg],
                log: null,
                skipCache: true,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("1.0.0", resolution?.Version);
    }

    [Theory]
    [InlineData(false, "1.0.0")]
    [InlineData(true, "2.0.0-beta")]
    public async Task FloatingCoordinate_AppliesStablePreferenceAcrossSources(
        bool includePrerelease,
        string expectedVersion)
    {
        using var client = new HttpClient(new SplitVersionSourcesHandler());

        var resolved = Assert.IsType<PackageCoordinateResolution.Resolved>(
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate(SplitVersionSourcesHandler.PackageId),
                [
                    SplitVersionSourcesHandler.PreviewSource,
                    SplitVersionSourcesHandler.StableSource,
                ],
                includePrerelease: includePrerelease,
                requireStableFloating: true,
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal(expectedVersion, resolved.Coordinate.Version);
        Assert.Equal(
            includePrerelease
                ? SplitVersionSourcesHandler.PreviewSource
                : SplitVersionSourcesHandler.StableSource,
            Assert.Single(resolved.Coordinate.Sources));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FloatingCoordinate_RequiresEveryAuthorizedSourceToAnswer(
        bool malformedVersionIndex)
    {
        using var client = new HttpClient(
            new IncompleteVersionSourcesHandler(
                malformedVersionIndex));

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate(
                    IncompleteVersionSourcesHandler.PackageId),
                [
                    IncompleteVersionSourcesHandler.IncompleteSource,
                    IncompleteVersionSourcesHandler.AvailableSource,
                ],
                requireStableFloating: true,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<PackageCoordinateResolution.Unavailable>(
                resolution);
        Assert.Contains(
            "complete version set",
            unavailable.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The CLI's own floating resolution reaches this resolver, so the
    /// workspace's stricter rule must be opt-in rather than the default. This
    /// is the shape that failed Windows CI twice from a cold cache: a
    /// preview-only feed resolving to nothing for
    /// <c>dotnet inspect package Aspire.OpenAI</c>.
    /// </summary>
    [Fact]
    public async Task LegacyResolverEntry_WithOnlyPrereleases_StillResolves()
    {
        using var client = new HttpClient(
            new ListedVersionsHandler("9.0.0-preview.1", "9.0.0-preview.2"));

        var resolved = Assert.IsType<PackageCoordinateResolution.Resolved>(
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate(ListedVersionsHandler.PackageId),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal("9.0.0-preview.2", resolved.Coordinate.Version);
        Assert.Equal(NuGetOrg, Assert.Single(resolved.Coordinate.Sources));
    }

    /// <summary>
    /// The workspace contract is stricter than the CLI's, and it is enforced on
    /// the answer rather than on the discovery path — so a cache entry a legacy
    /// caller wrote after taking that fallback cannot carry a prerelease into a
    /// context that did not ask for one.
    /// </summary>
    [Fact]
    public async Task FloatingCoordinate_WithAPrewarmedLegacyCache_IsStillUnavailable()
    {
        string cachePath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-version-cache-{Guid.NewGuid():N}");
        CoreCache.Initialize("dotnet-inspect-test", cachePath);
        using var client = new HttpClient(
            new ListedVersionsHandler("9.0.0-preview.1", "9.0.0-preview.2"));
        using var offline = new HttpClient(new FailingHandler());

        try
        {
            // Warm the shared version cache exactly as the CLI would: the
            // legacy resolution falls back, and writes that answer under the
            // stable key.
            Assert.Equal(
                "9.0.0-preview.2",
                (await DotnetInspector.Packages.PackageExtractor.ResolveLatestVersionAsync(
                    client,
                    ListedVersionsHandler.PackageId,
                    [NuGetOrg],
                    log: null,
                    skipCache: false,
                    cancellationToken: TestContext.Current.CancellationToken))?.Version);

            PackageCoordinateResolution resolution =
                await PackageCoordinateResolver.ResolveAsync(
                    // A client that refuses every request: the only answer
                    // available now is the warmed cache entry, so reaching an
                    // Unavailable proves the workspace rule filtered a cached
                    // legacy fallback rather than re-querying the feed.
                    offline,
                    new PackageCoordinate(ListedVersionsHandler.PackageId),
                    [NuGetOrg],
                    useVersionCache: true,
                    requireStableFloating: true,
                    cancellationToken:
                        TestContext.Current.CancellationToken);

            var unavailable =
                Assert.IsType<PackageCoordinateResolution.Unavailable>(resolution);
            Assert.Contains("stable", unavailable.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(cachePath))
                Directory.Delete(cachePath, recursive: true);
        }
    }

    /// <summary>
    /// With prereleases disabled the workspace answer is the newest stable
    /// version or nothing. Falling back to "the newest anything" would turn "no
    /// stable release exists" into a silent prerelease selection, which is the
    /// one outcome the flag exists to prevent.
    /// </summary>
    [Fact]
    public async Task FloatingCoordinate_WithOnlyPrereleases_IsUnavailable()
    {
        using var client = new HttpClient(
            new ListedVersionsHandler("1.0.0-beta", "2.0.0-beta"));

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate(ListedVersionsHandler.PackageId),
                [NuGetOrg],
                requireStableFloating: true,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsType<PackageCoordinateResolution.Unavailable>(resolution);
    }

    [Fact]
    public async Task FloatingCoordinate_WithOnlyPrereleases_ResolvesWhenIncluded()
    {
        using var client = new HttpClient(
            new ListedVersionsHandler("1.0.0-beta", "2.0.0-beta"));

        var resolved = Assert.IsType<PackageCoordinateResolution.Resolved>(
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate(ListedVersionsHandler.PackageId),
                [NuGetOrg],
                includePrerelease: true,
                requireStableFloating: true,
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal("2.0.0-beta", resolved.Coordinate.Version);
    }

    [Theory]
    [InlineData(false, "1.0.0")]
    [InlineData(true, "2.0.0-beta")]
    public async Task FloatingCoordinate_WithMixedVersions_HonoursThePrereleaseFlag(
        bool includePrerelease,
        string expected)
    {
        using var client = new HttpClient(
            new ListedVersionsHandler("1.0.0", "2.0.0-beta"));

        var resolved = Assert.IsType<PackageCoordinateResolution.Resolved>(
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate(ListedVersionsHandler.PackageId),
                [NuGetOrg],
                includePrerelease: includePrerelease,
                requireStableFloating: true,
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal(expected, resolved.Coordinate.Version);
    }

    /// <summary>
    /// An exact pin still names whatever version it names. The stable-only rule
    /// governs what floating <em>discovery</em> may select, not what a caller
    /// may pin.
    /// </summary>
    [Fact]
    public async Task ExactPrereleasePin_ResolvesWithoutTheFlag()
    {
        using var client = new HttpClient(new FailingHandler());

        var resolved = Assert.IsType<PackageCoordinateResolution.Resolved>(
            await PackageCoordinateResolver.ResolveAsync(
                client,
                new PackageCoordinate("Example", "1.0.0-beta"),
                [NuGetOrg],
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal("1.0.0-beta", resolved.Coordinate.Version);
    }

    /// <summary>
    /// Serves nuget.org's flat-container index and registration page for one
    /// package, with every version listed.
    /// </summary>
    sealed class ListedVersionsHandler(params string[] versions)
        : HttpMessageHandler
    {
        internal const string PackageId = "listed.package";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            if (url.Equals(
                $"https://api.nuget.org/v3-flatcontainer/{PackageId}/index.json",
                StringComparison.OrdinalIgnoreCase))
            {
                return Json(
                    $$"""{"versions":[{{string.Join(",", versions.Select(v => $"\"{v}\""))}}]}""");
            }

            if (url.Equals(
                $"https://api.nuget.org/v3/registration5-gz-semver2/{PackageId}/index.json",
                StringComparison.OrdinalIgnoreCase))
            {
                string entries = string.Join(
                    ",",
                    versions.Select(v =>
                        "{\"catalogEntry\":{\"version\":\""
                        + v
                        + "\",\"listed\":true}}"));
                return Json($$"""{"items":[{"items":[{{entries}}]}]}""");
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));

            static Task<HttpResponseMessage> Json(string body) =>
                Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body),
                    });
        }
    }

    sealed class SignedFlatContainerHandler : HttpMessageHandler
    {
        internal const string PackageId = "signed.package";
        internal static readonly PackageSource Feed =
            new("signed", "https://feed.test/v3/index.json");

        readonly string _signature;
        readonly string _indexUrl;
        readonly List<string> _requests = [];

        internal SignedFlatContainerHandler(string signature = "abc")
        {
            _signature = signature;
            _indexUrl =
                $"https://feed.test/flat/{PackageId}/index.json?sig={signature}";
        }

        internal IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                    return [.. _requests];
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            lock (_requests)
                _requests.Add(url);

            if (url.Equals(Feed.Url, StringComparison.Ordinal))
            {
                return Json(
                    $$"""
                    {"resources":[{"@id":"https://feed.test/flat?sig={{_signature}}","@type":"PackageBaseAddress/3.0.0"}]}
                    """);
            }

            return url.Equals(_indexUrl, StringComparison.Ordinal)
                ? Json("""{"versions":["1.0.0","1.5.0"]}""")
                : Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.NotFound));

            static Task<HttpResponseMessage> Json(string body) =>
                Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body),
                    });
        }
    }

    sealed class SplitVersionSourcesHandler : HttpMessageHandler
    {
        internal const string PackageId = "split.package";
        internal static readonly PackageSource PreviewSource =
            new("preview", "https://preview.test/v3/index.json");
        internal static readonly PackageSource StableSource =
            new("stable", "https://stable.test/v3/index.json");

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            return url switch
            {
                "https://preview.test/v3/index.json" =>
                    Json(
                        """{"resources":[{"@id":"https://preview.test/flat","@type":"PackageBaseAddress/3.0.0"}]}"""),
                "https://stable.test/v3/index.json" =>
                    Json(
                        """{"resources":[{"@id":"https://stable.test/flat","@type":"PackageBaseAddress/3.0.0"}]}"""),
                $"https://preview.test/flat/{PackageId}/index.json" =>
                    Json("""{"versions":["2.0.0-beta"]}"""),
                $"https://stable.test/flat/{PackageId}/index.json" =>
                    Json("""{"versions":["1.0.0"]}"""),
                _ => Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.NotFound)),
            };

            static Task<HttpResponseMessage> Json(string body) =>
                Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body),
                    });
        }
    }

    sealed class IncompleteVersionSourcesHandler(
        bool malformedVersionIndex)
        : HttpMessageHandler
    {
        internal const string PackageId = "partial.package";
        internal static readonly PackageSource IncompleteSource =
            new("incomplete", "https://incomplete.test/v3/index.json");
        internal static readonly PackageSource AvailableSource =
            new("available", "https://available.test/v3/index.json");

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            return url switch
            {
                "https://incomplete.test/v3/index.json"
                    when !malformedVersionIndex =>
                    Task.FromResult(
                        new HttpResponseMessage(
                            HttpStatusCode.ServiceUnavailable)),
                "https://incomplete.test/v3/index.json" =>
                    Json(
                        """{"resources":[{"@id":"https://incomplete.test/flat","@type":"PackageBaseAddress/3.0.0"}]}"""),
                $"https://incomplete.test/flat/{PackageId}/index.json" =>
                    Json("""{"versions":"not-an-array"}"""),
                "https://available.test/v3/index.json" =>
                    Json(
                        """{"resources":[{"@id":"https://available.test/flat","@type":"PackageBaseAddress/3.0.0"}]}"""),
                $"https://available.test/flat/{PackageId}/index.json" =>
                    Json("""{"versions":["1.0.0"]}"""),
                _ => Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.NotFound)),
            };

            static Task<HttpResponseMessage> Json(string body) =>
                Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body),
                    });
        }
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
