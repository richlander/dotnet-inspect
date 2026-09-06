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
    public void GalleryCatalog_PreservesSourceOwnedSuggestionsAndOrders()
    {
        BrowserGalleryDiscoveryCatalog catalog =
            BrowserPackageQueryOperations.GalleryCatalog();

        Assert.Equal(NuGetGalleryDiscoveryCatalog.PackageType.Id, catalog.PackageType.Id);
        Assert.Equal(NuGetGalleryDiscoveryCatalog.PackageType.Label, catalog.PackageType.Label);
        Assert.Equal(NuGetGalleryDiscoveryCatalog.PackageType.Summary, catalog.PackageType.Summary);
        Assert.Equal(
            NuGetGalleryDiscoveryCatalog.PackageType.Suggestions.Select(value =>
                (value.Value.Name, value.Label)),
            catalog.PackageType.Suggestions.Select(value =>
                (value.Value, value.Label)));
        Assert.Equal(
            NuGetGalleryDiscoveryCatalog.Orders.Select(order =>
                (order.Id, order.Label, order.Summary)),
            catalog.Orders.Select(order =>
                (order.Id, order.Label, order.Summary)));
    }

    [Theory]
    [InlineData("", NuGetGalleryDiscoveryOrder.MostDownloaded)]
    [InlineData("  ", NuGetGalleryDiscoveryOrder.MostDownloaded)]
    [InlineData("json parser", NuGetGalleryDiscoveryOrder.Relevance)]
    public void GalleryPlan_PreservesInputCapacityAndLocalMatchLimit(
        string text,
        NuGetGalleryDiscoveryOrder expectedOrder)
    {
        var accepted = Assert.IsType<PackageQueryPlanResult.Accepted>(
            BrowserPackageQueryOperations.Plan(
                text,
                [],
                maximumCandidates: 200,
                maximumMatches: 10,
                includePrerelease: false,
                packageType: NuGetGalleryPackageType.DotnetTool.Name));

        Assert.NotNull(accepted.Plan.GalleryRequest);
        Assert.Equal(200, accepted.Plan.GalleryRequest.Capacity);
        Assert.Equal(10, accepted.Plan.MaximumMatches);
        Assert.Equal(expectedOrder, accepted.Plan.GalleryRequest.Order);
        Assert.Equal(NuGetGalleryPackageType.DotnetTool, accepted.Plan.GalleryRequest.PackageType);
        Assert.Empty(accepted.Plan.Facets);
    }

    [Fact]
    public void GalleryPlan_UsesOpaqueOrderIdentityWithoutRewritingInspectionFacets()
    {
        var accepted = Assert.IsType<PackageQueryPlanResult.Accepted>(
            BrowserPackageQueryOperations.Plan(
                "",
                [PackageQuery.ToolFacetId],
                maximumCandidates: 200,
                maximumMatches: 10,
                includePrerelease: true,
                sourceOrderId: NuGetGalleryDiscoveryCatalog.Relevance.Id));

        Assert.NotNull(accepted.Plan.GalleryRequest);
        Assert.Null(accepted.Plan.GalleryRequest.PackageType);
        Assert.True(accepted.Plan.GalleryRequest.IncludePrerelease);
        Assert.Equal(NuGetGalleryDiscoveryOrder.Relevance, accepted.Plan.GalleryRequest.Order);
        Assert.Equal(PackageQuery.ToolFacetId, Assert.Single(accepted.Plan.Facets).Id);
        Assert.Throws<ArgumentException>(() =>
            BrowserPackageQueryOperations.Plan("", [], 200, 10, false, sourceOrderId: "relevance"));
    }

    [Fact]
    public void Project_GalleryCompletionRetainsFiniteInputAndEstimatedPopulation()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(PackageSourceAssociation.Create());
        var summary = new PackageQuerySummary(
            new InertString(TextPolicy.Prose, ""),
            source.Source,
            CandidateLimit: 200,
            MatchLimit: 100,
            Candidates: 3,
            Matches: 3,
            Failures: 0,
            PackageQueryCompletionKind.GalleryResponseComplete)
        {
            SourceCandidates = 3,
            EstimatedTotalHits = 8_000,
        };

        BrowserPackageQueryCompletion completion =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Completed(summary)).Completion!;

        Assert.Equal(BrowserPackageQueryCompletionKind.GalleryResponseComplete, completion.Kind);
        Assert.Equal(200, completion.CandidateLimit);
        Assert.Equal(3, completion.SourceCandidates);
        Assert.Equal(8_000, completion.EstimatedTotalHits);
    }

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

    [Fact]
    public async Task PumpAsync_ConsumerWaitOutlivesActiveWorkBudget()
    {
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        var emitted = new List<BrowserPackageQueryEvent>();
        Task<BrowserPackageQueryEvent> pumping =
            BrowserPackageWorkspace.RunPackageOperationAsync(
                deadline => BrowserPackageQueryOperations.PumpAsync(
                    CountedMatchEvents(() => { }),
                    matchCredit,
                    emitted.Add,
                    deadline.Token,
                    deadline),
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken);

        Assert.Single(emitted);
        await Task.Delay(1200, TestContext.Current.CancellationToken);
        Assert.False(pumping.IsCompleted);
        Assert.Single(emitted);

        Assert.True(matchCredit.TryAdd(2));
        BrowserPackageQueryEvent completed = await pumping;
        Assert.Equal(3, emitted.Count);
        Assert.Equal(BrowserPackageQueryEventKind.Completed, completed.Kind);
    }

    [Fact]
    public async Task PumpAsync_ActiveWorkExpiryDoesNotPublishUncreditedMatch()
    {
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        using var callerCancellation = new CancellationTokenSource();
        var emitted = new List<BrowserPackageQueryEvent>();
        int established = 0;

        await Assert.ThrowsAsync<TimeoutException>(
            () => BrowserPackageWorkspace.RunPackageOperationAsync(
                deadline => BrowserPackageQueryOperations.PumpAsync(
                    CountedMatchEvents(() =>
                    {
                        if (++established == 2)
                        {
                            while (!deadline.HasExpired)
                                Thread.SpinWait(100);
                        }
                    }),
                    matchCredit,
                    emitted.Add,
                    deadline.Token,
                    deadline),
                TimeSpan.FromMilliseconds(100),
                callerCancellation.Token));

        Assert.Equal(2, established);
        Assert.Single(emitted);
        Assert.False(callerCancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task PumpAsync_CallerCancellationStillReleasesBudgetPausedWait()
    {
        using var callerCancellation = new CancellationTokenSource();
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        var emitted = new List<BrowserPackageQueryEvent>();
        Task<BrowserPackageQueryEvent> pumping =
            BrowserPackageWorkspace.RunPackageOperationAsync(
                deadline => BrowserPackageQueryOperations.PumpAsync(
                    CountedMatchEvents(() => { }),
                    matchCredit,
                    emitted.Add,
                    deadline.Token,
                    deadline),
                TimeSpan.FromSeconds(1),
                callerCancellation.Token);

        Assert.Single(emitted);
        callerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pumping);
        Assert.Equal(
            ["Contoso.1", "Contoso.2"],
            emitted.Select(item => item.Row!.PackageId));
    }

    [Fact]
    public async Task PackageOperation_ConsumerWaitDoesNotResetSpentBudget()
    {
        await Assert.ThrowsAsync<TimeoutException>(
            () => BrowserPackageWorkspace.RunPackageOperationAsync<int>(
                async deadline =>
                {
                    while (deadline.Remaining > TimeSpan.FromMilliseconds(500))
                        Thread.SpinWait(100);
                    TimeSpan remaining = deadline.Remaining;

                    await deadline.WaitForConsumerAsync(token =>
                        new ValueTask(Task.Delay(1200, token)));

                    Assert.True(deadline.Remaining <= remaining);
                    Assert.False(deadline.Token.IsCancellationRequested);
                    await Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token);
                    return 0;
                },
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken));
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
