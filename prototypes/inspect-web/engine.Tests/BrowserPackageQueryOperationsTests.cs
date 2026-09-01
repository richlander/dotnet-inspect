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
            Assert.Equal(BrowserPackageQueryFacetTier.Nuspec, actual.Tier);
        }
    }

    [Fact]
    public void Project_PreservesFailureAndCompletionEvidence()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        var failure = new PackageProfileFailure(
            "Contoso.Bad",
            "1.0.0",
            source.Source,
            PackageProfileFailureKind.ManifestAcquisition,
            "manifest unavailable");
        var summary = new PackageQuerySummary(
            new InertString(TextPolicy.Field, "Contoso."),
            source.Source,
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
        var profile = new PackageProfileMatch(
            "Contoso.Package",
            "1.0.0",
            [],
            42,
            Verified: true,
            source.Source,
            new PackageManifestFacts(
                PackageSourceCoordinate.Create(
                    "Contoso.Package",
                    "1.0.0"),
                ManifestVersion: "nuspec",
                Description: null,
                Authors: null,
                Repository: null,
                RepositoryType: null,
                RepositoryCommit: null,
                License: null,
                LicenseUrl: null,
                PackageTypes: [],
                IsToolPackage: false,
                ReadmeFile: null,
                DependencyGroups: []));
        BrowserPackageQueryEvent projectedMatch =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Match(
                    new PackageQueryMatch(
                        profile,
                        PackageQueryFacetTier.Nuspec,
                        [])));
        string expectedProducer =
            source.Source.Producer.Display.ToString();

        Assert.Equal(
            "https://api.nuget.org:443/v3/index.json",
            expectedProducer);
        Assert.Equal(BrowserPackageQueryEventKind.Failure, projectedFailure.Kind);
        Assert.Equal(
            BrowserPackageQueryFailureKind.ManifestAcquisition,
            projectedFailure.Failure!.Kind);
        Assert.Equal("manifest unavailable", projectedFailure.Failure.Message);
        Assert.Equal(
            expectedProducer,
            projectedFailure.Failure.Producer);
        Assert.Equal(
            BrowserPackageQueryEventKind.Completed,
            projectedCompletion.Kind);
        Assert.Equal(
            BrowserPackageQueryCompletionKind.CandidateLimitReached,
            projectedCompletion.Completion!.Kind);
        Assert.Equal(
            expectedProducer,
            projectedCompletion.Completion.Producer);
        Assert.Equal(200, projectedCompletion.Completion.CandidateLimit);
        Assert.Equal(100, projectedCompletion.Completion.MatchLimit);
        Assert.Equal(
            expectedProducer,
            projectedMatch.Row!.Producer);
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
                PackageProducerIdentity.NuGetOrg.Display.ToString(),
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
}
