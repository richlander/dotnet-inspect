using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using InertText;
using NuGetFetch;

using DotnetInspect.Web.Interop.Package;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserEngineBoundaryTests
{
    private const string SettlementPackageId = "System.Text.Json";
    private const string SettlementStableVersion = "8.0.5";
    private const string SettlementPreviewVersion = "9.0.0-preview.7.24405.7";

    private static async Task<BrowserPackageSurface> QueryPackageSurface(
        string packageId,
        string version,
        string framework)
    {
        BrowserPackageLoadResult result =
            Assert.IsType<BrowserPackageLoadResult>(
                JsonSerializer.Deserialize(
                    await PackageExports.QueryPackage(
                        packageId,
                        version,
                        framework),
                    BrowserPackageJsonContext.Default.BrowserPackageLoadResult));
        return Assert.IsType<BrowserPackageSurface>(result.Surface);
    }

    private static async Task<string> QueryPackageSurfaceJson(
        string packageId,
        string version,
        string framework) =>
        JsonSerializer.Serialize(
            await QueryPackageSurface(packageId, version, framework),
            BrowserPackageJsonContext.Default.BrowserPackageSurface);

    [Fact]
    public async Task PackageAcquisition_FloatingRootUsesAuthoritativeVersionsAndKeepsRequestEnvelope()
    {
        var handler = new GalleryPackageHandler(
            SettlementPackageId,
            SettlementStableVersion,
            PackageDocuments(1),
            discoveryVersions:
            [
                (SettlementStableVersion, true),
                (SettlementPreviewVersion, true),
            ]);
        using IPackageSourceClient source = Gallery(handler);

        BrowserPackageAcquisition latest = Assert.IsType<
            BrowserPackageAcquisitionResult.Acquired>(
            await BrowserPackageWorkspace.AcquireWithSettlementAsync(
                SettlementPackageId,
                version: null,
                source,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken,
                epochWork: null)).Acquisition;
        BrowserPackageAcquisition exact = Assert.IsType<
            BrowserPackageAcquisitionResult.Acquired>(
            await BrowserPackageWorkspace.AcquireWithSettlementAsync(
                SettlementPackageId,
                SettlementStableVersion,
                source,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken,
                epochWork: null)).Acquisition;

        Assert.Equal(SettlementStableVersion, latest.Package.Version);
        var latestResult = Assert.IsType<PackageVersionSettlementOutcome.Settled>(
            latest.VersionSettlement.Content).Result;
        Assert.Equal(
            SettlementPackageId.ToLowerInvariant(),
            latestResult.Request.PackageId);
        Assert.Null(latestResult.Request.Version);
        Assert.Equal(
            PackageVersionDiscoveryFreshness.RefreshedForRequest,
            latestResult.Freshness);
        Assert.Equal(
            new PackageVersionInfo(SettlementStableVersion, Listed: true),
            Assert.Single(latestResult.Listings));

        var exactResult = Assert.IsType<PackageVersionSettlementOutcome.Settled>(
            exact.VersionSettlement.Content).Result;
        Assert.Equal(
            SettlementPackageId.ToLowerInvariant(),
            exactResult.Request.PackageId);
        Assert.Equal(SettlementStableVersion, exactResult.Request.Version);
        Assert.Null(exactResult.Freshness);
        Assert.Empty(exactResult.Listings);
        Assert.Empty(exactResult.SourceListings);
        Assert.Same(
            latest.Package.Content.GenerationIdentity,
            exact.Package.Content.GenerationIdentity);

        Assert.Equal(
            [
                "https://globalcdn.nuget.org/v3-flatcontainer/system.text.json/index.json",
                "https://globalcdn.nuget.org/v3/registration5-gz-semver2/system.text.json/index.json",
                "https://globalcdn.nuget.org/packages/system.text.json.8.0.5.nupkg",
            ],
            handler.Requested);
    }

    [Fact]
    public async Task PackageAcquisition_PreviewOnlyLatestRemainsVisibleFailure()
    {
        var handler = new GalleryPackageHandler(
            SettlementPackageId,
            SettlementPreviewVersion,
            PackageDocuments(1),
            discoveryVersions: [(SettlementPreviewVersion, true)]);
        using IPackageSourceClient source = Gallery(handler);

        var result = Assert.IsType<BrowserPackageAcquisitionResult.NotSettled>(
            await BrowserPackageWorkspace.AcquireWithSettlementAsync(
                SettlementPackageId,
                version: null,
                source,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken,
                epochWork: null));
        var failure = Assert.IsType<PackageVersionSettlementOutcome.NotSettled>(
            result.VersionSettlement.Content).Failure;

        Assert.Equal(PackageVersionSettlementFailureKind.NoMatch, failure.Kind);
        Assert.Contains(
            "No listed version satisfies",
            failure.Reason.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.IsType<InspectionShare.NonProjectable>(
            result.VersionSettlement.Share);
        Assert.Empty(result.VersionSettlement.Diagnostics);
        Assert.Equal(2, handler.Requested.Count);
        Assert.DoesNotContain(
            handler.Requested,
            request => request.EndsWith(".nupkg", StringComparison.Ordinal));

        var wireResult = new BrowserPackageLoadResult(
            BrowserPackageWireProjection.Project(
                result.VersionSettlement),
            PackageInfo: null,
            Surface: null);
        string json = JsonSerializer.Serialize(
            wireResult,
            BrowserPackageJsonContext.Default.BrowserPackageLoadResult);
        BrowserPackageLoadResult roundTripped =
            Assert.IsType<BrowserPackageLoadResult>(
                JsonSerializer.Deserialize(
                    json,
                    BrowserPackageJsonContext.Default.BrowserPackageLoadResult));
        Assert.Null(roundTripped.Surface);
        Assert.Equal(
            BrowserPackageVersionSettlementOutcomeKind.NotSettled,
            roundTripped.VersionSettlement.Content.Kind);
        Assert.Equal(
            "NoMatch",
            roundTripped.VersionSettlement.Content.Failure!.Kind);
    }

    [Fact]
    public async Task PackageRealization_LatestFailureRetainsNormalizedRequest()
    {
        var handler = new GalleryPackageHandler(
            SettlementPackageId,
            SettlementPreviewVersion,
            PackageDocuments(1),
            discoveryVersions: [(SettlementPreviewVersion, true)]);
        using IPackageSourceClient source = Gallery(handler);

        var result = Assert.IsType<BrowserPackageRealizationResult.NotSettled>(
            await BrowserPackageWorkspace.RealizeWithSettlementAsync(
                SettlementPackageId,
                "latest",
                "net11.0",
                source,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));
        var failure = Assert.IsType<PackageVersionSettlementOutcome.NotSettled>(
            result.VersionSettlement.Content).Failure;

        Assert.Equal(
            SettlementPackageId.ToLowerInvariant(),
            failure.Request.PackageId);
        Assert.Null(failure.Request.Version);
        Assert.Equal(PackageVersionSettlementFailureKind.NoMatch, failure.Kind);
        Assert.DoesNotContain(
            handler.Requested,
            request => request.EndsWith(".nupkg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PackageRealization_SharesOneHouseGenerationWithPackageInfoAndRoot()
    {
        string packageId = $"package.info.browser.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        byte[] assembly = File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location);
        byte[] archive = PackageEntries(
            ($"{packageId}.nuspec", System.Text.Encoding.UTF8.GetBytes(
                $"""
                <package>
                  <metadata>
                    <id>{packageId}</id>
                    <version>{version}</version>
                    <authors>Example</authors>
                    <description>Example</description>
                  </metadata>
                </package>
                """)),
            ("lib/net11.0/Browser.Package.dll", assembly),
            ("ref/net11.0/Browser.Package.dll", assembly),
            ("runtimes/win/lib/net11.0/Browser.Package.dll", assembly));
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            archive);
        using IPackageSourceClient source = Gallery(handler);

        BrowserPackageRealization realization = Assert.IsType<
            BrowserPackageRealizationResult.Realized>(
            await BrowserPackageWorkspace.RealizeWithSettlementAsync(
                packageId,
                version,
                "net11.0",
                source,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken)).Realization;

        PackageInfoMeasurements measurements = realization.PackageInfo.Content;
        Assert.Equal(PackageInfoMeasurementStatus.Measured, measurements.Status);
        Assert.Equal(archive.LongLength, measurements.CompressedPackageBytes);
        Assert.Equal(
            "net11.0",
            measurements.SelectedTargetFramework!.ToString());
        IReadOnlyList<InertString> folders =
            measurements.SelectedTargetFrameworkFolders
            ?? throw new InvalidOperationException(
                "Measured Package Info did not retain its selected-TFM folders.");
        Assert.Equal(
            ["lib", "ref", "runtimes"],
            folders.Select(folder => folder.ToString()));
        Assert.Equal(1, measurements.SelectedLibraryCount);
        Assert.Equal(assembly.LongLength, measurements.SelectedLibraryPayloadBytes);
        PackageRootBinding binding = Assert.IsType<PackageRootBinding>(
            realization.Coordinate.Binding);
        Assert.Same(
            binding.ContentGenerationIdentity,
            measurements.Generation);
        Assert.Equal(
            measurements.SelectionReceipt!.RequestedTargetFramework,
            realization.Coordinate.Root.RequestedTargetFramework);
        Assert.Equal(
            measurements.SelectedLibraryCount,
            realization.Coordinate.Root.AssetSelection.Assets.Count);
        Assert.Equal(
            [$"https://globalcdn.nuget.org/packages/{packageId}.{version}.nupkg"],
            handler.Requested);
    }

    [Fact]
    public async Task PackageRealization_RepeatedRequestJoinsWorkspaceBuiltFromFirstExactRoot()
    {
        string packageId = $"package.info.rejoin.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        byte[] assembly = File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location);
        byte[] archive = PackageEntries(
            ($"{packageId}.nuspec", System.Text.Encoding.UTF8.GetBytes(
                $"""
                <package>
                  <metadata>
                    <id>{packageId}</id>
                    <version>{version}</version>
                    <authors>Example</authors>
                    <description>Example</description>
                  </metadata>
                </package>
                """)),
            ("lib/net11.0/Browser.Package.dll", assembly));
        var handler = new GalleryPackageHandler(packageId, version, archive);
        using IPackageSourceClient source = Gallery(handler);

        BrowserPackageRealization first = Assert.IsType<
            BrowserPackageRealizationResult.Realized>(
            await BrowserPackageWorkspace.RealizeWithSettlementAsync(
                packageId,
                version,
                "net11.0",
                source,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken)).Realization;
        BrowserPackageRealization second = Assert.IsType<
            BrowserPackageRealizationResult.Realized>(
            await BrowserPackageWorkspace.RealizeWithSettlementAsync(
                packageId,
                version,
                "net11.0",
                source,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken)).Realization;

        PackageRootBinding firstBinding =
            Assert.IsType<PackageRootBinding>(first.Coordinate.Binding);
        PackageRootBinding secondBinding =
            Assert.IsType<PackageRootBinding>(second.Coordinate.Binding);
        Assert.NotSame(
            firstBinding.SelectionIdentity,
            secondBinding.SelectionIdentity);
        Assert.Same(
            first.Coordinate.Package.Content.GenerationIdentity,
            second.Coordinate.Package.Content.GenerationIdentity);

        await using BrowserScopeLease<BrowserInspectionScope> firstLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                first,
                TestContext.Current.CancellationToken);
        await using BrowserScopeLease<BrowserInspectionScope> secondLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                second,
                TestContext.Current.CancellationToken);

        Assert.Same(firstLease.Scope, secondLease.Scope);
        PackageRootBinding retainedBinding =
            Assert.IsType<PackageRootBinding>(
                Assert.Single(firstLease.Scope.Coordinates).Binding);
        Assert.Same(
            firstBinding.SelectionIdentity,
            retainedBinding.SelectionIdentity);
        Assert.NotSame(
            secondBinding.SelectionIdentity,
            retainedBinding.SelectionIdentity);
        Assert.Single(handler.Requested);
    }

    [Fact]
    public async Task PackageRealization_ConcurrentRequestsShareOneHouseOperation()
    {
        string packageId = $"package.info.concurrent.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        var payloadRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            PackageDocuments(1),
            payloadRelease: payloadRelease.Task);
        using IPackageSourceClient source = Gallery(handler);

        Task<BrowserPackageRealizationResult> firstTask =
            BrowserPackageWorkspace.RealizeWithSettlementAsync(
                packageId,
                version,
                "net11.0",
                source,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        await handler.PayloadReadStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        Task<BrowserPackageRealizationResult> secondTask =
            BrowserPackageWorkspace.RealizeWithSettlementAsync(
                packageId,
                version,
                "net11.0",
                source,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        payloadRelease.SetResult();

        BrowserPackageRealization first = Assert.IsType<
            BrowserPackageRealizationResult.Realized>(await firstTask).Realization;
        BrowserPackageRealization second = Assert.IsType<
            BrowserPackageRealizationResult.Realized>(await secondTask).Realization;

        Assert.Same(first, second);
        Assert.Single(handler.Requested);
    }

    [Fact]
    public async Task PackageRealization_ReusesLegacyAcquisitionGenerationForSameSource()
    {
        string packageId = $"package.info.legacy-cache.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        byte[] archive = PackageDocuments(1);
        var handler = new GalleryPackageHandler(packageId, version, archive);
        using IPackageSourceClient source = Gallery(handler);

        BrowserPackage legacy = await BrowserPackageWorkspace.AcquireAsync(
            packageId,
            version,
            source,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken,
            epochWork: null);
        BrowserPackageRealization realization = Assert.IsType<
            BrowserPackageRealizationResult.Realized>(
            await BrowserPackageWorkspace.RealizeWithSettlementAsync(
                packageId,
                version,
                "net11.0",
                source,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken)).Realization;

        Assert.False(legacy.Content.FromCache);
        Assert.True(realization.Coordinate.Package.Content.FromCache);
        Assert.Equal(
            NuGetCache.GetSourceKey(PackageSource.NuGetOrg.Url),
            legacy.Content.ProducerKey);
        Assert.Equal(
            source.Source.Producer.Key,
            realization.Coordinate.Package.Content.ProducerKey);
        Assert.Same(
            legacy.Content.GenerationIdentity,
            realization.Coordinate.Package.Content.GenerationIdentity);
        Assert.Single(handler.Requested);
    }

    [Fact]
    public async Task LegacyAcquisition_ReusesHouseGenerationOnlyForSameSource()
    {
        string packageId = $"package.info.house-cache.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        byte[] firstArchive = PackageDocuments(1);
        byte[] secondArchive = PackageDocuments(2);
        var firstHandler = new GalleryPackageHandler(
            packageId,
            version,
            firstArchive);
        var secondHandler = new GalleryPackageHandler(
            packageId,
            version,
            secondArchive);
        using IPackageSourceClient firstSource = Gallery(firstHandler);
        using IPackageSourceClient secondSource = Gallery(secondHandler);

        BrowserPackageRealization realization = Assert.IsType<
            BrowserPackageRealizationResult.Realized>(
            await BrowserPackageWorkspace.RealizeWithSettlementAsync(
                packageId,
                version,
                "net11.0",
                firstSource,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken)).Realization;
        BrowserPackage sameSource = await BrowserPackageWorkspace.AcquireAsync(
            packageId,
            version,
            firstSource,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken,
            epochWork: null);
        BrowserPackage otherSource = await BrowserPackageWorkspace.AcquireAsync(
            packageId,
            version,
            secondSource,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken,
            epochWork: null);

        Assert.True(sameSource.Content.FromCache);
        Assert.False(otherSource.Content.FromCache);
        Assert.Equal(
            NuGetCache.GetSourceKey(PackageSource.NuGetOrg.Url),
            sameSource.Content.ProducerKey);
        Assert.Same(
            realization.Coordinate.Package.Content.GenerationIdentity,
            sameSource.Content.GenerationIdentity);
        Assert.NotSame(
            sameSource.Content.GenerationIdentity,
            otherSource.Content.GenerationIdentity);
        Assert.Single(firstHandler.Requested);
        Assert.Single(secondHandler.Requested);
    }

    [Fact]
    public void PackageInfoWireProjection_MatchesSharedEnvelope()
    {
        var inspection =
            new InspectionEnvelope<PackageVersionSettlementOutcome>(
                new PackageVersionSettlementOutcome.Settled(
                    new(
                        new PackageCoordinate(
                            SettlementPackageId.ToLowerInvariant(),
                            SettlementStableVersion),
                        PackageSourceCoordinate.Create(
                            SettlementPackageId,
                            SettlementStableVersion),
                        IncludePrerelease: false,
                        Freshness: null,
                        Listings: [],
                        SourceListings: [])),
                new InspectionShare.NonProjectable(
                    "package-version-settlement/share",
                    "No canonical Workspace share projection."),
                [
                    new InspectionDiagnostic(
                        "package-version-settlement.source-failure",
                        InspectionDiagnosticSeverity.Warning,
                        "A neighboring source was unavailable."),
                ]);
        var packageInfo =
            new InspectionEnvelope<PackageInfoMeasurements>(
                new PackageInfoMeasurements(
                    PackageInfoMeasurementStatus.Measured,
                    SettlementPackageId,
                    SettlementStableVersion,
                    compressedPackageBytes: 4096,
                    selectedTargetFramework:
                        InertString.FromEncoded(
                            TextPolicy.Field,
                            "net8.0"),
                    availableTargetFrameworks:
                    [
                        InertString.FromEncoded(TextPolicy.Field, "net8.0"),
                        InertString.FromEncoded(TextPolicy.Field, "net7.0"),
                        new InertString(
                            TextPolicy.Field,
                            "net6.0\u202Ehostile"),
                    ],
                    selectedTargetFrameworkFolders:
                    [
                        InertString.FromEncoded(TextPolicy.Field, "lib"),
                        InertString.FromEncoded(TextPolicy.Field, "runtimes"),
                        InertString.FromEncoded(
                            TextPolicy.Field,
                            "\\u202Ehostile"),
                    ],
                    selectedLibraryPayloadBytes: 2048,
                    selectedLibraryCount: 2,
                    detail: null,
                    unavailableReason: null),
                new InspectionShare.NonProjectable(
                    "package-info-measurements/share",
                    "No canonical Workspace share projection."),
                [
                    new InspectionDiagnostic(
                        "package-info-measurements.source-failure",
                        InspectionDiagnosticSeverity.Warning,
                        "A package source was unavailable."),
                ]);
        var result = new BrowserPackageLoadResult(
            BrowserPackageWireProjection.Project(inspection),
            BrowserPackageWireProjection.Project(packageInfo),
            new BrowserPackageSurface(
                SettlementPackageId,
                SettlementStableVersion,
                Frameworks: [],
                ActiveFramework: "",
                Icon: null,
                DefaultAssemblyId: null,
                CompileLibrary: new BrowserCompileLibraryAvailability(
                    BrowserCompileLibraryStatus.NoCompileAssets,
                    TargetFramework: null,
                    Message: null),
                Assemblies: [],
                Types: [],
                Accessibility: [],
                TotalMembers: 0,
                Documents: [],
                InspectionErrors: [],
                InspectionError: null));

        string json = JsonSerializer.Serialize(
            result,
            BrowserPackageJsonContext.Default.BrowserPackageLoadResult);
        BrowserPackageLoadResult roundTripped =
            Assert.IsType<BrowserPackageLoadResult>(
                JsonSerializer.Deserialize(
                    json,
                    BrowserPackageJsonContext.Default.BrowserPackageLoadResult));

        Assert.NotNull(roundTripped.Surface);
        BrowserPackageInfoMeasurementInspection packageInfoBaseline =
            Assert.IsType<BrowserPackageInfoMeasurementInspection>(
                roundTripped.PackageInfo);
        Assert.Equal("Measured", packageInfoBaseline.Content.Status);
        Assert.Equal(4096, packageInfoBaseline.Content.CompressedPackageBytes);
        Assert.Equal("net8.0", packageInfoBaseline.Content.SelectedTargetFramework);
        Assert.Equal(
            ["lib", "runtimes", "\\u202Ehostile"],
            Assert.IsType<string[]>(
                packageInfoBaseline.Content.SelectedTargetFrameworkFolders));
        Assert.Equal(2048, packageInfoBaseline.Content.SelectedLibraryPayloadBytes);
        Assert.Equal(2, packageInfoBaseline.Content.SelectedLibraryCount);
        Assert.Equal(
            ["net8.0", "net7.0", @"net6.0\u202Ehostile"],
            Assert.IsType<string[]>(
                packageInfoBaseline.Content.AvailableTargetFrameworks));
        Assert.True(packageInfoBaseline.Content.HasSelectedSlice);
        Assert.Equal(
            BrowserInspectionShareKind.NonProjectable,
            packageInfoBaseline.Share.Kind);
        Assert.Equal(
            "package-info-measurements.source-failure",
            Assert.Single(packageInfoBaseline.Diagnostics).Code);
        BrowserPackageVersionSettlementInspection baseline =
            roundTripped.VersionSettlement;
        Assert.Equal(
            BrowserPackageVersionSettlementOutcomeKind.Settled,
            baseline.Content.Kind);
        Assert.Equal(
            SettlementStableVersion,
            baseline.Content.Result!.Coordinate.Version);
        Assert.Equal(
            SettlementStableVersion,
            baseline.Content.Result.Request.Version);
        Assert.Null(baseline.Content.Result.Freshness);
        Assert.Empty(baseline.Content.Result.Listings);
        Assert.Empty(baseline.Content.Result.SourceListings);
        Assert.Equal(
            BrowserInspectionShareKind.NonProjectable,
            baseline.Share.Kind);
        Assert.Equal(
            "package-version-settlement/share",
            baseline.Share.Path);
        BrowserInspectionDiagnostic diagnostic =
            Assert.Single(baseline.Diagnostics);
        Assert.Equal(
            "package-version-settlement.source-failure",
            diagnostic.Code);
        Assert.Equal("Warning", diagnostic.Severity);
    }
}
