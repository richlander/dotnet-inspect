using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
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
    public void PackageSurfaceSerialization_PreservesCompleteSettlementBaseline()
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
        var result = new BrowserPackageLoadResult(
            BrowserPackageWireProjection.Project(inspection),
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
