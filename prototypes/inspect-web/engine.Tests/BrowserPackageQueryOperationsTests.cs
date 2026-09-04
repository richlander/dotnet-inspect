using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using InertText;
using NuGetFetch;

using InspectWeb.Engine.PackageFacade;

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
            Assert.Equal(
                expected.CombinesWithinSelectionGroup,
                actual.CombinesWithinSelectionGroup);
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
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        var failure = new PackageQueryFailure(
            "Contoso.Bad",
            "1.0.0",
            source.Source,
            PackageQueryFailureKind.ManifestAcquisition,
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
        BrowserPackageQueryEvent projectedProgress =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Progress(
                    new PackageQueryProgress(
                        PackageQueryProgressPhase.Manifest,
                        Completed: 3,
                        Limit: 20)));
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
        Assert.Equal(
            BrowserPackageQueryEventKind.Progress,
            projectedProgress.Kind);
        Assert.Equal(
            BrowserPackageQueryProgressPhase.Manifest,
            projectedProgress.Progress!.Phase);
        Assert.Equal(3, projectedProgress.Progress.Completed);
        Assert.Equal(20, projectedProgress.Progress.Limit);
    }

    [Fact]
    public void Project_PreservesPackageContentTierAndFailure()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageProfileMatch package = new(
            "Contoso.Tool",
            "1.0.0",
            [],
            TotalDownloads: 42,
            Verified: false,
            source.Source,
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
            source.Source,
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
            BrowserPackageJsonContext.Default.BrowserPackageQueryEvent);

        Assert.Equal(queryEvent, roundTripped);
    }

    [Fact]
    public async Task Coordinator_SupersedesAndCancelsSourceWork()
    {
        using BrowserPackageQueryOperationLease first =
            await BrowserPackageQueryOperationCoordinator.BeginAsync(
                initialMatchCredit: 20);

        ValueTask<BrowserPackageQueryOperationLease> secondPending =
            BrowserPackageQueryOperationCoordinator.BeginAsync(
                initialMatchCredit: 20);
        Assert.True(first.CancellationToken.IsCancellationRequested);

        first.Dispose();
        using BrowserPackageQueryOperationLease second = await secondPending;
        Assert.False(second.CancellationToken.IsCancellationRequested);

        BrowserPackageQueryOperationCoordinator.CancelCurrent();
        Assert.True(second.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Coordinator_AddsMatchCreditOnlyToTheCurrentOperation()
    {
        using BrowserPackageQueryOperationLease operation =
            await BrowserPackageQueryOperationCoordinator.BeginAsync(
                initialMatchCredit: 1);

        await operation.MatchCredit.WaitAsync(
            TestContext.Current.CancellationToken);
        Assert.True(
            BrowserPackageQueryOperationCoordinator.RequestCurrentMatches(2));
        await operation.MatchCredit.WaitAsync(
            TestContext.Current.CancellationToken);
        await operation.MatchCredit.WaitAsync(
            TestContext.Current.CancellationToken);

        operation.Dispose();
        Assert.False(
            BrowserPackageQueryOperationCoordinator.RequestCurrentMatches(1));
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
                matchCredit: null,
                _ => { },
                TestContext.Current.CancellationToken));

        Assert.Contains("facet IDs are unknown", error.Message);
    }

    [Fact]
    public async Task PumpAsync_EmitsOnlyNonterminalEventsAndReturnsCompletion()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageQueryEvent.Progress progress = new(
            new PackageQueryProgress(
                PackageQueryProgressPhase.Search,
                Completed: 0,
                Limit: 1));
        PackageQueryEvent.Failure failure = new(
            new PackageQueryFailure(
                PackageId: null,
                Version: null,
                source.Source,
                PackageQueryFailureKind.Search,
                "search unavailable"));
        PackageQueryEvent.Completed completed = new(
            new PackageQuerySummary(
                new InertString(TextPolicy.Field, "Contoso."),
                source.Source,
                CandidateLimit: 20,
                MatchLimit: 20,
                Candidates: 0,
                Matches: 0,
                Failures: 1,
                PackageQueryCompletionKind.Failed));
        var emitted = new List<BrowserPackageQueryEvent>();

        BrowserPackageQueryEvent returned =
            await BrowserPackageQueryOperations.PumpAsync(
                Events(progress, failure, completed),
                matchCredit: null,
                emitted.Add,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                BrowserPackageQueryEventKind.Progress,
                BrowserPackageQueryEventKind.Failure,
            ],
            emitted.Select(item => item.Kind));
        Assert.Equal(BrowserPackageQueryEventKind.Completed, returned.Kind);
    }

    [Fact]
    public async Task PumpAsync_RejectsAnEventAfterCompletion()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageQueryEvent.Completed completed = new(
            new PackageQuerySummary(
                new InertString(TextPolicy.Field, "Contoso."),
                source.Source,
                CandidateLimit: 20,
                MatchLimit: 20,
                Candidates: 0,
                Matches: 0,
                Failures: 0,
                PackageQueryCompletionKind.Exhausted));
        PackageQueryEvent.Progress lateProgress = new(
            new PackageQueryProgress(
                PackageQueryProgressPhase.Manifest,
                Completed: 1,
                Limit: 20));
        var emitted = new List<BrowserPackageQueryEvent>();

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPackageQueryOperations.PumpAsync(
                    Events(completed, lateProgress),
                    matchCredit: null,
                    emitted.Add,
                    TestContext.Current.CancellationToken));

        Assert.Contains("after completion", error.Message);
        Assert.Empty(emitted);
    }

    [Fact]
    public async Task PumpAsync_PausesMatchDeliveryUntilCreditIsReplenished()
    {
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 2);
        var emitted = new List<BrowserPackageQueryEvent>();
        int established = 0;
        Task<BrowserPackageQueryEvent> pumping =
            BrowserPackageQueryOperations.PumpAsync(
                CountedMatchEvents(() => established++),
                matchCredit,
                emitted.Add,
                TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => established == 3);
        Assert.Equal(2, emitted.Count);
        Assert.False(pumping.IsCompleted);

        Assert.True(matchCredit.TryAdd(1));
        BrowserPackageQueryEvent completed = await pumping;

        Assert.Equal(3, emitted.Count);
        Assert.All(
            emitted,
            item => Assert.Equal(
                BrowserPackageQueryEventKind.Match,
                item.Kind));
        Assert.Equal(BrowserPackageQueryEventKind.Completed, completed.Kind);
    }

    [Fact]
    public async Task PumpAsync_ReadsCompletionWithoutAdditionalMatchCredit()
    {
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        var emitted = new List<BrowserPackageQueryEvent>();

        BrowserPackageQueryEvent completed =
            await BrowserPackageQueryOperations.PumpAsync(
                Events(MatchEvent("Contoso.One"), CompletedEvent()),
                matchCredit,
                emitted.Add,
                TestContext.Current.CancellationToken);

        Assert.Single(emitted);
        Assert.Equal(BrowserPackageQueryEventKind.Completed, completed.Kind);
    }

    [Fact]
    public async Task PumpAsync_CancellationReleasesAWaitingMatch()
    {
        using var cancellation = new CancellationTokenSource();
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        var emitted = new List<BrowserPackageQueryEvent>();
        int established = 0;
        Task<BrowserPackageQueryEvent> pumping =
            BrowserPackageQueryOperations.PumpAsync(
                CountedMatchEvents(() => established++),
                matchCredit,
                emitted.Add,
                cancellation.Token);

        await WaitUntilAsync(() => established == 2);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pumping);
        Assert.Equal(
            ["Contoso.1", "Contoso.2"],
            emitted.Select(item => item.Row!.PackageId));
    }

    static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    static async IAsyncEnumerable<PackageQueryEvent> CountedMatchEvents(
        Action established)
    {
        for (int index = 1; index <= 3; index++)
        {
            established();
            yield return MatchEvent($"Contoso.{index}");
        }
        yield return CompletedEvent(matches: 3);
        await Task.CompletedTask;
    }

    static async IAsyncEnumerable<PackageQueryEvent> Events(
        params PackageQueryEvent[] events)
    {
        await Task.CompletedTask;
        foreach (PackageQueryEvent queryEvent in events)
            yield return queryEvent;
    }

    static PackageQueryEvent MatchEvent(string packageId)
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        var package = new PackageProfileMatch(
            packageId,
            "1.0.0",
            [],
            TotalDownloads: 42,
            Verified: false,
            source.Source,
            Manifest(packageId, "1.0.0", isToolPackage: false));
        return new PackageQueryEvent.Match(
            new PackageQueryMatch(
                package,
                PackageQueryFacetTier.Nuspec,
                [
                    new PackageQueryEvidence(
                        "package.query.source-verified",
                        new InertString(TextPolicy.Prose, "Matched.")),
                ]));
    }

    static PackageQueryEvent CompletedEvent(int matches = 1)
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        return new PackageQueryEvent.Completed(
            new PackageQuerySummary(
                new InertString(TextPolicy.Field, "Contoso."),
                source.Source,
                CandidateLimit: 20,
                MatchLimit: 20,
                Candidates: matches,
                Matches: matches,
                Failures: 0,
                PackageQueryCompletionKind.Exhausted));
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
