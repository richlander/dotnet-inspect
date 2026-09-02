using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using InertText;
using NuGetFetch;

namespace InspectWeb.Engine.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserPackageQueryOperationsTests
{
    [Fact]
    public void Facets_MatchProductCatalogOrderAndMetadata()
    {
        BrowserPackageQueryFacetCatalog catalog =
            BrowserPackageQueryOperations.Facets();

        Assert.Equal(PackageQuery.Facets.Length, catalog.Facets.Length);
        for (int index = 0; index < catalog.Facets.Length; index++)
        {
            PackageQueryFacetDescriptor expected = PackageQuery.Facets[index];
            BrowserPackageQueryFacetDescriptor actual = catalog.Facets[index];
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Label, actual.Label);
            Assert.Equal(expected.Summary, actual.Summary);
            Assert.Equal(expected.Weight, actual.Weight);
            Assert.Equal(expected.SelectionGroupId, actual.SelectionGroupId);
            Assert.Equal(expected.DisplayGroupId, actual.DisplayGroupId);
            Assert.Equal(expected.DisplayGroupLabel, actual.DisplayGroupLabel);
            Assert.Equal(
                expected.Tier == PackageQueryFacetTier.Nuspec
                    ? BrowserPackageQueryFacetTier.Nuspec
                    : BrowserPackageQueryFacetTier.PackageContent,
                actual.Tier);
        }
    }

    [Fact]
    public void Project_PreservesFailureAndCompletionEvidence()
    {
        var failure = new PackageQueryFailure(
            "Contoso.Bad",
            "1.0.0",
            PackageSourceIdentity.NuGetOrg,
            PackageQueryFailureKind.ManifestAcquisition,
            "manifest unavailable");
        var summary = new PackageQuerySummary(
            new InertString(TextPolicy.Field, "Contoso."),
            PackageSourceIdentity.NuGetOrg,
            CandidateLimit: 200,
            MatchLimit: 100,
            Candidates: 5,
            Matches: 2,
            Failures: 1,
            PackageQueryCompletionKind.CandidateLimitReached);

        BrowserPackageQueryEvent projectedFailure =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Failure(failure));
        BrowserPackageQueryEvent projectedCompletion =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Completed(summary));

        Assert.Equal(BrowserPackageQueryEventKind.Failure, projectedFailure.Kind);
        Assert.Equal(
            BrowserPackageQueryFailureKind.ManifestAcquisition,
            projectedFailure.Failure!.Kind);
        Assert.Equal("manifest unavailable", projectedFailure.Failure.Message);
        Assert.Equal(
            BrowserPackageQueryEventKind.Completed,
            projectedCompletion.Kind);
        Assert.Equal(
            BrowserPackageQueryCompletionKind.CandidateLimitReached,
            projectedCompletion.Completion!.Kind);
        Assert.Equal(200, projectedCompletion.Completion.CandidateLimit);
        Assert.Equal(100, projectedCompletion.Completion.MatchLimit);
    }

    [Fact]
    public void Project_PreservesPackageContentTierAndFailure()
    {
        PackageProfileMatch package = new(
            "Contoso.Tool",
            "1.0.0",
            [],
            TotalDownloads: 42,
            Verified: false,
            PackageSourceIdentity.NuGetOrg,
            Manifest(
                "Contoso.Tool",
                "1.0.0",
                isToolPackage: true));
        var match = new PackageQueryMatch(
            package,
            PackageQueryFacetTier.PackageContent,
            [
                new PackageQueryEvidence(
                    PackageQuery.ToolV2FacetId,
                    new InertString(
                        TextPolicy.Prose,
                        "DotnetToolSettings.xml declares v2.")),
            ]);
        var failure = new PackageQueryFailure(
            "Contoso.Bad",
            "1.0.0",
            PackageSourceIdentity.NuGetOrg,
            PackageQueryFailureKind.PackageContentAcquisition,
            "package payload unavailable");

        BrowserPackageQueryEvent projectedMatch =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Match(match));
        BrowserPackageQueryEvent projectedFailure =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Failure(failure));

        Assert.Equal(
            BrowserPackageQueryFacetTier.PackageContent,
            projectedMatch.Row!.Tier);
        Assert.Equal(
            BrowserPackageQueryFailureKind.PackageContentAcquisition,
            projectedFailure.Failure!.Kind);
    }

    [Fact]
    public void Serialize_RoundTripsThroughBrowserJsonContext()
    {
        var queryEvent = new BrowserPackageQueryEvent(
            BrowserPackageQueryEventKind.Completed,
            Row: null,
            Failure: null,
            Completion: new BrowserPackageQueryCompletion(
                "Contoso.",
                PackageSourceIdentity.NuGetOrg.Value,
                CandidateLimit: 200,
                MatchLimit: 100,
                Candidates: 5,
                Matches: 2,
                Failures: 0,
                BrowserPackageQueryCompletionKind.Exhausted));

        string json = BrowserPackageQueryOperations.Serialize(queryEvent);
        BrowserPackageQueryEvent? roundTripped = JsonSerializer.Deserialize(
            json,
            BrowserJsonContext.Default.BrowserPackageQueryEvent);

        Assert.Equal(queryEvent, roundTripped);
    }

    [Fact]
    public async Task Coordinator_SupersedesAndCancelsSourceWork()
    {
        using BrowserPackageQueryOperationLease first =
            await BrowserPackageQueryOperationCoordinator.BeginAsync();

        ValueTask<BrowserPackageQueryOperationLease> secondPending =
            BrowserPackageQueryOperationCoordinator.BeginAsync();
        Assert.True(first.CancellationToken.IsCancellationRequested);

        first.Dispose();
        using BrowserPackageQueryOperationLease second = await secondPending;
        Assert.False(second.CancellationToken.IsCancellationRequested);

        BrowserPackageQueryOperationCoordinator.CancelCurrent();
        Assert.True(second.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsInvalidPlansBeforeStartingSourceWork()
    {
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BrowserPackageQueryOperations.ExecuteAsync(
                "Contoso.",
                ["package.query.unknown"],
                maximumCandidates: 200,
                maximumMatches: 100,
                includePrerelease: false,
                _ => { },
                TestContext.Current.CancellationToken));

        Assert.Contains("facet IDs are unknown", error.Message);
    }

    static PackageManifestFacts Manifest(
        string packageId,
        string version,
        bool isToolPackage) =>
        new(
            PackageSourceCoordinate.Create(packageId, version),
            "nuspec",
            Description: null,
            Authors: null,
            Repository: null,
            RepositoryType: null,
            RepositoryCommit: null,
            License: null,
            LicenseUrl: null,
            PackageTypes: isToolPackage ? ["DotnetTool"] : [],
            IsToolPackage: isToolPackage,
            ReadmeFile: null,
            DependencyGroups: []);
}
