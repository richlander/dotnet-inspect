using System.Globalization;
using System.Net;
using System.Text;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public class EcosystemChangeReportQueryTests
{
    private const string ServiceIndex =
        "https://api.nuget.org/v3/index.json";
    private const string Catalog =
        "https://api.nuget.org/v3/catalog0/index.json";
    private const string Page1 =
        "https://api.nuget.org/v3/catalog0/page0.json";
    private const string Page2 =
        "https://api.nuget.org/v3/catalog0/page1.json";

    private static readonly DateTimeOffset ReferenceTime =
        Utc(2026, 10, 14);
    private static readonly DateTimeOffset From =
        ReferenceTime - TimeSpan.FromDays(42);

    [Fact]
    public void PlanFreezesDefaultAndExplicitIntervals()
    {
        EcosystemChangePackageSelection selection =
            PackageSet("Microsoft.Extensions.AI");
        var request = new EcosystemChangeReportRequest(selection);

        EcosystemChangeReportPlan defaultPlan =
            EcosystemChangeReportPlan.Resolve(
                request,
                new FixedTimeProvider(ReferenceTime));
        var explicitInterval = new NuGetCatalogRequest(
            Utc(2026, 10, 1),
            Utc(2026, 10, 8));
        EcosystemChangeReportPlan explicitPlan =
            EcosystemChangeReportPlan.Resolve(
                new EcosystemChangeReportRequest(
                    selection,
                    explicitInterval),
                new FixedTimeProvider(ReferenceTime));

        Assert.True(defaultPlan.UsedDefaultInterval);
        Assert.Equal(ReferenceTime, defaultPlan.ReferenceTime);
        Assert.Equal(From, defaultPlan.Interval.FromExclusive);
        Assert.Equal(
            ReferenceTime,
            defaultPlan.Interval.ThroughInclusive);
        Assert.False(explicitPlan.UsedDefaultInterval);
        Assert.Same(explicitInterval, explicitPlan.Interval);
        Assert.Equal(ReferenceTime, explicitPlan.ReferenceTime);
    }

    [Fact]
    public async Task ComposesCurrentAndFixedEvidenceWithReceiptBasis()
    {
        string affectedLeaf = Leaf("affected");
        string fixedLeaf = Leaf("fixed");
        string fallbackLeaf = Leaf("fallback");
        string outsideLeaf = Leaf("outside");
        var catalog = StandardCatalog(
            Item(
                affectedLeaf,
                "Microsoft.Extensions.AI",
                "1.0.0",
                From + TimeSpan.FromDays(5)),
            Item(
                fixedLeaf,
                "Microsoft.Extensions.AI",
                "1.1.0",
                From + TimeSpan.FromDays(20)),
            Item(
                fallbackLeaf,
                "Contoso.Fallback",
                "2.0.0",
                From + TimeSpan.FromDays(25)),
            Item(
                outsideLeaf,
                "Contoso.Outside",
                "3.0.0",
                From + TimeSpan.FromDays(30)),
            Item(
                Leaf("scope-noise"),
                "Noise.Package",
                "9.0.0",
                From + TimeSpan.FromDays(35)));
        catalog[fixedLeaf] = Json(Details(
            "Microsoft.Extensions.AI",
            "1.1.0",
            From + TimeSpan.FromDays(20),
            From + TimeSpan.FromDays(10),
            From + TimeSpan.FromDays(10)));
        catalog[fallbackLeaf] = Json(Details(
            "Contoso.Fallback",
            "2.0.0",
            From + TimeSpan.FromDays(25),
            From + TimeSpan.FromDays(12),
            created: null));
        catalog[outsideLeaf] = Json(Details(
            "Contoso.Outside",
            "3.0.0",
            From + TimeSpan.FromDays(30),
            From - TimeSpan.FromDays(1),
            From - TimeSpan.FromDays(1)));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(catalog);
        using var advisoryClient = new HttpClient(
            new SingleResponseHandler(
                HttpStatusCode.OK,
                AdvisoryPage(
                    Advisory(
                        "Microsoft.Extensions.AI",
                        "< 1.1.0",
                        "1.1.0",
                        "GHSA-aaaa-bbbb-cccc"),
                    Advisory(
                        "Contoso.Fallback",
                        "< 2.0.0",
                        "2.0.0",
                        "GHSA-dddd-eeee-ffff"),
                    Advisory(
                        "Contoso.Outside",
                        "< 3.0.0",
                        "3.0.0",
                        "GHSA-gggg-hhhh-jjjj"))));
        var advisoryService = new GitHubNuGetAdvisoryService(
            advisoryClient,
            timeProvider: new FixedTimeProvider(ReferenceTime));
        var selection =
            new EcosystemChangePackageSelection.PackageSet(
                "package-set.real-witness",
                [
                    new PackageCoordinate("Microsoft.Extensions.AI"),
                    new PackageCoordinate("Contoso.Fallback"),
                    new PackageCoordinate("Contoso.Outside"),
                ]);
        EcosystemChangeReportPlan plan =
            EcosystemChangeReportPlan.Resolve(
                new EcosystemChangeReportRequest(selection),
                new FixedTimeProvider(ReferenceTime));

        IReadOnlyList<EcosystemChangeReportEvent> events =
            await EcosystemChangeReportQuery.ExecuteToArrayAsync(
                source,
                advisoryService,
                plan,
                TestContext.Current.CancellationToken);
        EcosystemChangeReportRow[] rows = Rows(events);
        EcosystemChangeReportSummary summary = Summary(events);

        Assert.Equal(4, rows.Length);
        Assert.Equal(
            ["Contoso.Outside", "Contoso.Fallback",
                "Microsoft.Extensions.AI", "Microsoft.Extensions.AI"],
            rows.Select(row => row.CatalogEvent.PackageId));
        Assert.Equal(
            EcosystemSecurityReleaseStatus.ReceiptOutsideInterval,
            rows[0].SecurityReleaseStatus);
        Assert.Null(rows[0].SecurityRelease);
        Assert.Equal(
            EcosystemSecurityReleaseStatus.EvidencedInInterval,
            rows[1].SecurityReleaseStatus);
        Assert.Equal(
            NuGetCatalogPackageReceiptBasis.PublishedFallback,
            rows[1].SecurityRelease!.Receipt.Basis);
        Assert.Equal(
            EcosystemSecurityReleaseStatus.EvidencedInInterval,
            rows[2].SecurityReleaseStatus);
        Assert.Equal(
            NuGetCatalogPackageReceiptBasis.Created,
            rows[2].SecurityRelease!.Receipt.Basis);
        Assert.Single(rows[3].CurrentAdvisoryContext.Advisories);
        Assert.Equal(
            EcosystemSecurityReleaseStatus
                .CheckedNoFixedVersionAssociation,
            rows[3].SecurityReleaseStatus);
        Assert.Equal(3, summary.ReceiptCandidates);
        Assert.Equal(3, summary.ReceiptRequests);
        Assert.Equal(3, summary.ReceiptSuccesses);
        Assert.Same(source.Source, summary.Source);
        Assert.All(rows, row => Assert.Same(source.Source, row.Source));
        Assert.Same(selection, summary.Plan.Request.PackageSelection);
        Assert.Equal(EcosystemChangeReportCompletionKind.Complete,
            summary.Completion);
    }

    [Fact]
    public async Task SecurityPredicateRunsBeforeSemanticHead()
    {
        string fixedLeaf = Leaf("fixed-filter");
        var catalog = StandardCatalog(
            Item(
                Leaf("affected-filter"),
                "Example.Filter",
                "1.0.0",
                From + TimeSpan.FromDays(10)),
            Item(
                fixedLeaf,
                "Example.Filter",
                "1.1.0",
                From + TimeSpan.FromDays(20)),
            Item(
                Leaf("newest-filter"),
                "Example.Filter",
                "2.0.0",
                From + TimeSpan.FromDays(30)));
        catalog[fixedLeaf] = Json(Details(
            "Example.Filter",
            "1.1.0",
            From + TimeSpan.FromDays(20),
            From + TimeSpan.FromDays(15),
            From + TimeSpan.FromDays(15)));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(catalog);
        using var advisoryClient = new HttpClient(
            new SingleResponseHandler(
                HttpStatusCode.OK,
                AdvisoryPage(Advisory(
                    "Example.Filter",
                    "< 1.1.0",
                    "1.1.0",
                    "GHSA-aaaa-bbbb-cccc"))));
        var service = new GitHubNuGetAdvisoryService(advisoryClient);
        EcosystemChangeReportPlan plan =
            EcosystemChangeReportPlan.Resolve(
                new EcosystemChangeReportRequest(
                    PackageSet("Example.Filter"),
                    securitySelection:
                        EcosystemChangeSecuritySelection.SecurityRelevant,
                    maximumRows: 1),
                new FixedTimeProvider(ReferenceTime));

        IReadOnlyList<EcosystemChangeReportEvent> events =
            await EcosystemChangeReportQuery.ExecuteToArrayAsync(
                source,
                service,
                plan,
                TestContext.Current.CancellationToken);
        EcosystemChangeReportRow row = Assert.Single(Rows(events));
        EcosystemChangeReportSummary summary = Summary(events);

        Assert.Equal("1.1.0", row.CatalogEvent.Version);
        Assert.NotNull(row.SecurityRelease);
        Assert.Equal(2, summary.EligibleRowCount);
        Assert.True(summary.ResultLimitReached);
        Assert.Equal(
            EcosystemChangeReportCompletionKind.ResultLimitReached,
            summary.Completion);
    }

    [Fact]
    public async Task PrefixScopeRetainsNewestCandidatesAndRepeatedEvents()
    {
        var catalog = StandardCatalog(
            Item(
                Leaf("drop-before-repeat"),
                "Example.Dropped",
                "1.0.0",
                From + TimeSpan.FromDays(1),
                commitId: "zero"),
            Item(
                Leaf("oldest"),
                "Example.Repeat",
                "1.0.0",
                From + TimeSpan.FromDays(5),
                commitId: "one"),
            Item(
                Leaf("middle"),
                "Example.Repeat",
                "1.0.0",
                From + TimeSpan.FromDays(10),
                commitId: "two"),
            Item(
                Leaf("newest"),
                "Example.Other",
                "2.0.0",
                From + TimeSpan.FromDays(15),
                commitId: "three"),
            Item(
                Leaf("noise"),
                "Different.Package",
                "9.0.0",
                From + TimeSpan.FromDays(20)));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(catalog);
        using var advisoryClient = new HttpClient(
            new SingleResponseHandler(HttpStatusCode.OK, "[]"));
        var service = new GitHubNuGetAdvisoryService(advisoryClient);
        var prefix = new EcosystemChangePackageSelection.PackagePrefix(
            new PackagePrefixDeclaration("Example."));
        EcosystemChangeReportPlan plan =
            EcosystemChangeReportPlan.Resolve(
                new EcosystemChangeReportRequest(
                    prefix,
                    maximumRows: 3,
                    maximumCandidateEvents: 3,
                    maximumReceiptRequests: 3),
                new FixedTimeProvider(ReferenceTime));

        IReadOnlyList<EcosystemChangeReportEvent> events =
            await EcosystemChangeReportQuery.ExecuteToArrayAsync(
                source,
                service,
                plan,
                TestContext.Current.CancellationToken);
        EcosystemChangeReportRow[] rows = Rows(events);
        EcosystemChangeReportSummary summary = Summary(events);

        Assert.Equal(["three", "two", "one"],
            rows.Select(row => row.CatalogEvent.CommitId));
        Assert.Equal(4, summary.MatchingEventCount);
        Assert.Equal(3, summary.RetainedEventCount);
        Assert.True(summary.CandidateLimitReached);
        Assert.Equal(
            EcosystemChangeReportCompletionKind.Partial,
            summary.Completion);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task PreservesCheckedEmptyAndUnavailableEvidence(
        HttpStatusCode advisoryStatus)
    {
        var catalog = StandardCatalog(
            Item(
                Leaf("availability"),
                "Example.Availability",
                "1.0.0",
                From + TimeSpan.FromDays(10)));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(catalog);
        using var advisoryClient = new HttpClient(
            new SingleResponseHandler(advisoryStatus, "[]"));
        var service = new GitHubNuGetAdvisoryService(advisoryClient);
        EcosystemChangeReportPlan plan =
            EcosystemChangeReportPlan.Resolve(
                new EcosystemChangeReportRequest(
                    PackageSet("Example.Availability")),
                new FixedTimeProvider(ReferenceTime));

        IReadOnlyList<EcosystemChangeReportEvent> events =
            await EcosystemChangeReportQuery.ExecuteToArrayAsync(
                source,
                service,
                plan,
                TestContext.Current.CancellationToken);
        EcosystemChangeReportRow row = Assert.Single(Rows(events));
        EcosystemChangeReportSummary summary = Summary(events);

        if (advisoryStatus == HttpStatusCode.OK)
        {
            Assert.Equal(
                GitHubNuGetAdvisoryAvailability.Complete,
                row.CurrentAdvisoryContext.Availability);
            Assert.Equal(
                EcosystemSecurityReleaseStatus
                    .CheckedNoFixedVersionAssociation,
                row.SecurityReleaseStatus);
            Assert.Empty(events.OfType<
                EcosystemChangeReportEvent.Failure.Advisory>());
            Assert.Equal(
                EcosystemChangeReportCompletionKind.Complete,
                summary.Completion);
        }
        else
        {
            Assert.Equal(
                GitHubNuGetAdvisoryAvailability.Unavailable,
                row.CurrentAdvisoryContext.Availability);
            Assert.Equal(
                EcosystemSecurityReleaseStatus
                    .FixedVersionEvidenceUnavailable,
                row.SecurityReleaseStatus);
            Assert.Single(events.OfType<
                EcosystemChangeReportEvent.Failure.Advisory>());
            Assert.Contains(
                events.TakeWhile(item =>
                        item is not EcosystemChangeReportEvent
                            .Failure.Advisory),
                item => item is EcosystemChangeReportEvent.Row);
            Assert.Equal(1, summary.CurrentContextUnevaluableRows);
            Assert.Equal(1, summary.SecurityReleaseUnevaluableRows);
            Assert.Equal(
                EcosystemChangeReportCompletionKind.Partial,
                summary.Completion);
        }
    }

    [Fact]
    public async Task ReceiptFailuresLimitsAndDeletesRemainDistinct()
    {
        var catalog = StandardCatalog(
            Item(
                Leaf("limited"),
                "Example.Limited",
                "1.0.0",
                From + TimeSpan.FromDays(10)),
            Item(
                Leaf("missing"),
                "Example.Missing",
                "2.0.0",
                From + TimeSpan.FromDays(20)),
            Item(
                Leaf("deleted"),
                "Example.Deleted",
                "3.0.0",
                From + TimeSpan.FromDays(30),
                kind: "nuget:PackageDelete"));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(catalog);
        using var advisoryClient = new HttpClient(
            new SingleResponseHandler(
                HttpStatusCode.OK,
                AdvisoryPage(
                    Advisory(
                        "Example.Limited",
                        "< 1.0.0",
                        "1.0.0",
                        "GHSA-aaaa-bbbb-cccc"),
                    Advisory(
                        "Example.Missing",
                        "< 2.0.0",
                        "2.0.0",
                        "GHSA-dddd-eeee-ffff"),
                    Advisory(
                        "Example.Deleted",
                        "< 3.0.0",
                        "3.0.0",
                        "GHSA-gggg-hhhh-jjjj"))));
        var service = new GitHubNuGetAdvisoryService(advisoryClient);
        EcosystemChangeReportPlan plan =
            EcosystemChangeReportPlan.Resolve(
                new EcosystemChangeReportRequest(
                    PackageSet(
                        "Example.Limited",
                        "Example.Missing",
                        "Example.Deleted"),
                    maximumReceiptRequests: 1),
                new FixedTimeProvider(ReferenceTime));

        IReadOnlyList<EcosystemChangeReportEvent> events =
            await EcosystemChangeReportQuery.ExecuteToArrayAsync(
                source,
                service,
                plan,
                TestContext.Current.CancellationToken);
        EcosystemChangeReportRow[] rows = Rows(events);
        EcosystemChangeReportSummary summary = Summary(events);

        Assert.Equal(
            EcosystemSecurityReleaseStatus.DeleteActivityUnevaluable,
            rows[0].SecurityReleaseStatus);
        Assert.Equal(
            EcosystemChangeActivityKind.DeletionObserved,
            rows[0].Activity);
        Assert.Equal(
            EcosystemSecurityReleaseStatus.ReceiptFailure,
            rows[1].SecurityReleaseStatus);
        Assert.Equal(
            EcosystemSecurityReleaseStatus.ReceiptLimitReached,
            rows[2].SecurityReleaseStatus);
        Assert.Equal(2, summary.ReceiptCandidates);
        Assert.Equal(1, summary.ReceiptRequests);
        Assert.Equal(0, summary.ReceiptSuccesses);
        Assert.Single(summary.ReceiptFailures);
        Assert.True(summary.ReceiptLimitReached);
        Assert.Equal(3, summary.SecurityReleaseUnevaluableRows);
        Assert.Single(events.OfType<
            EcosystemChangeReportEvent.Failure.PackageReceipt>());
        Assert.Equal(
            EcosystemChangeReportCompletionKind.Partial,
            summary.Completion);
    }

    [Fact]
    public async Task CatalogBoundKeepsPartialRowsAndTypedCompletion()
    {
        var catalog = TwoPageCatalog(
            Item(
                Leaf("first-page"),
                "Example.Bounded",
                "1.0.0",
                From + TimeSpan.FromDays(5)),
            Item(
                Leaf("second-page"),
                "Example.Bounded",
                "2.0.0",
                From + TimeSpan.FromDays(25)));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(
                catalog,
                new NuGetFetchOptions { MaxCatalogPages = 1 });
        using var advisoryClient = new HttpClient(
            new SingleResponseHandler(HttpStatusCode.OK, "[]"));
        var service = new GitHubNuGetAdvisoryService(advisoryClient);
        EcosystemChangeReportPlan plan =
            EcosystemChangeReportPlan.Resolve(
                new EcosystemChangeReportRequest(
                    PackageSet("Example.Bounded")),
                new FixedTimeProvider(ReferenceTime));

        IReadOnlyList<EcosystemChangeReportEvent> events =
            await EcosystemChangeReportQuery.ExecuteToArrayAsync(
                source,
                service,
                plan,
                TestContext.Current.CancellationToken);
        EcosystemChangeReportSummary summary = Summary(events);

        Assert.Single(Rows(events));
        Assert.Equal(
            NuGetCatalogCompletion.PageLimitReached,
            summary.CatalogCompletion);
        Assert.Equal(
            EcosystemChangeReportCompletionKind.Partial,
            summary.Completion);
    }

    [Fact]
    public async Task SourceHorizonRemainsDistinctFromRequestedEnd()
    {
        DateTimeOffset horizon = ReferenceTime - TimeSpan.FromDays(1);
        var catalog = StandardCatalog(
            horizon,
            Item(
                Leaf("horizon"),
                "Example.Horizon",
                "1.0.0",
                horizon));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(catalog);
        using var advisoryClient = new HttpClient(
            new SingleResponseHandler(HttpStatusCode.OK, "[]"));
        var service = new GitHubNuGetAdvisoryService(advisoryClient);
        EcosystemChangeReportPlan plan =
            EcosystemChangeReportPlan.Resolve(
                new EcosystemChangeReportRequest(
                    PackageSet("Example.Horizon")),
                new FixedTimeProvider(ReferenceTime));

        IReadOnlyList<EcosystemChangeReportEvent> events =
            await EcosystemChangeReportQuery.ExecuteToArrayAsync(
                source,
                service,
                plan,
                TestContext.Current.CancellationToken);
        EcosystemChangeReportSummary summary = Summary(events);

        Assert.Equal(horizon, summary.CapturedHorizon);
        Assert.Equal(
            NuGetCatalogCompletion.SourceHorizonReached,
            summary.CatalogCompletion);
        Assert.Equal(
            EcosystemChangeReportCompletionKind.Partial,
            summary.Completion);
    }

    [Fact]
    public async Task ExplicitIntervalKeepsExclusiveAndInclusiveBoundaries()
    {
        DateTimeOffset through = From + TimeSpan.FromDays(7);
        var catalog = StandardCatalog(
            through,
            Item(
                Leaf("exclusive"),
                "Example.Boundary",
                "1.0.0",
                From),
            Item(
                Leaf("inclusive"),
                "Example.Boundary",
                "2.0.0",
                through));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(catalog);
        using var advisoryClient = new HttpClient(
            new SingleResponseHandler(HttpStatusCode.OK, "[]"));
        var service = new GitHubNuGetAdvisoryService(advisoryClient);
        EcosystemChangeReportPlan plan =
            EcosystemChangeReportPlan.Resolve(
                new EcosystemChangeReportRequest(
                    PackageSet("Example.Boundary"),
                    new NuGetCatalogRequest(From, through)),
                new FixedTimeProvider(ReferenceTime));

        IReadOnlyList<EcosystemChangeReportEvent> events =
            await EcosystemChangeReportQuery.ExecuteToArrayAsync(
                source,
                service,
                plan,
                TestContext.Current.CancellationToken);
        EcosystemChangeReportRow row = Assert.Single(Rows(events));

        Assert.Equal("2.0.0", row.CatalogEvent.Version);
    }

    [Fact]
    public async Task ConsumerCancellationAfterPartialRowsStaysCancellation()
    {
        var catalog = StandardCatalog(
            Item(
                Leaf("cancel-old"),
                "Example.Cancel",
                "1.0.0",
                From + TimeSpan.FromDays(5)),
            Item(
                Leaf("cancel-new"),
                "Example.Cancel",
                "2.0.0",
                From + TimeSpan.FromDays(10)));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(catalog);
        using var advisoryClient = new HttpClient(
            new SingleResponseHandler(HttpStatusCode.OK, "[]"));
        var service = new GitHubNuGetAdvisoryService(advisoryClient);
        EcosystemChangeReportPlan plan =
            EcosystemChangeReportPlan.Resolve(
                new EcosystemChangeReportRequest(
                    PackageSet("Example.Cancel")),
                new FixedTimeProvider(ReferenceTime));
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        await using IAsyncEnumerator<EcosystemChangeReportEvent> enumerator =
#pragma warning disable xUnit1051
            EcosystemChangeReportQuery.ExecuteAsync(
                    source,
                    service,
                    plan,
                    cancellation.Token)
#pragma warning restore xUnit1051
                .GetAsyncEnumerator();

        while (await enumerator.MoveNextAsync()
            && enumerator.Current is not EcosystemChangeReportEvent.Row)
        {
        }
        Assert.IsType<EcosystemChangeReportEvent.Row>(enumerator.Current);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await enumerator.MoveNextAsync().AsTask());
    }

    private static EcosystemChangePackageSelection.PackageSet PackageSet(
        params string[] packageIds) =>
        new(
            "package-set.test",
            packageIds.Select(static packageId =>
                new PackageCoordinate(packageId)));

    private static EcosystemChangeReportRow[] Rows(
        IReadOnlyList<EcosystemChangeReportEvent> events) =>
        [.. events
            .OfType<EcosystemChangeReportEvent.Row>()
            .Select(static item => item.Value)];

    private static EcosystemChangeReportSummary Summary(
        IReadOnlyList<EcosystemChangeReportEvent> events) =>
        Assert.Single(events.OfType<
            EcosystemChangeReportEvent.Completed>()).Summary;

    private static INuGetCatalogPackageSourceClient CreateSource(
        RouteHandler handler,
        NuGetFetchOptions? options = null)
    {
        IPackageSourceClient source = PackageSourceClientFactory.Create(
            PackageSourceDescriptor.NuGetV3(
                "nuget.org",
                "NuGet.org",
                new Uri(ServiceIndex)),
            PackageSourceAssociation.Create(),
            handler,
            options);
        Assert.Equal(PackageProducerIdentity.NuGetOrg, source.Source.Producer);
        return Assert.IsAssignableFrom<INuGetCatalogPackageSourceClient>(
            source);
    }

    private static RouteHandler StandardCatalog(params string[] items) =>
        StandardCatalog(ReferenceTime, items);

    private static RouteHandler StandardCatalog(
        DateTimeOffset horizon,
        params string[] items) =>
        new()
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(IndexDocument(
                horizon,
                Page(Page1, horizon))),
            [Page1] = Json(PageDocument(
                Catalog,
                horizon,
                items)),
        };

    private static RouteHandler TwoPageCatalog(
        string firstItem,
        string secondItem)
    {
        DateTimeOffset firstHorizon = From + TimeSpan.FromDays(10);
        return new()
        {
            [ServiceIndex] = Json(
                ServiceDocument(
                    $$"""{"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}""")),
            [Catalog] = Json(IndexDocument(
                ReferenceTime,
                Page(Page1, firstHorizon),
                Page(Page2, ReferenceTime))),
            [Page1] = Json(PageDocument(
                Catalog,
                firstHorizon,
                firstItem)),
            [Page2] = Json(PageDocument(
                Catalog,
                ReferenceTime,
                secondItem)),
        };
    }

    private static string ServiceDocument(string resources) =>
        $$"""{"version":"3.0.0","resources":[{{resources}}]}""";

    private static string IndexDocument(
        DateTimeOffset horizon,
        params string[] pages) =>
        $$"""
        {"commitId":"index","commitTimeStamp":"{{Stamp(horizon)}}",
        "count":{{pages.Length}},"items":[{{string.Join(',', pages)}}]}
        """;

    private static string Page(
        string url,
        DateTimeOffset horizon) =>
        $$"""
        {"@id":"{{url}}","commitId":"page",
        "commitTimeStamp":"{{Stamp(horizon)}}","count":999}
        """;

    private static string PageDocument(
        string parent,
        DateTimeOffset horizon,
        params string[] items) =>
        $$"""
        {"commitId":"page","commitTimeStamp":"{{Stamp(horizon)}}",
        "count":{{items.Length}},"parent":"{{parent}}",
        "items":[{{string.Join(',', items)}}]}
        """;

    private static string Item(
        string leafUrl,
        string packageId,
        string version,
        DateTimeOffset timestamp,
        string kind = "nuget:PackageDetails",
        string commitId = "commit") =>
        $$"""
        {"@id":"{{leafUrl}}","@type":"{{kind}}",
        "commitId":"{{commitId}}","commitTimeStamp":"{{Stamp(timestamp)}}",
        "nuget:id":"{{packageId}}","nuget:version":"{{version}}"}
        """;

    private static string Details(
        string packageId,
        string version,
        DateTimeOffset commitTimestamp,
        DateTimeOffset published,
        DateTimeOffset? created)
    {
        string createdProperty = created is { } value
            ? ",\"created\":\"" + Stamp(value) + "\""
            : "";
        return $$"""
        {"@type":["PackageDetails","catalog:Permalink"],
        "catalog:commitId":"commit",
        "catalog:commitTimeStamp":"{{Stamp(commitTimestamp)}}",
        "id":"{{packageId}}","version":"{{version}}",
        "published":"{{Stamp(published)}}"{{createdProperty}}}
        """;
    }

    private static string AdvisoryPage(params string[] advisories) =>
        $"[{string.Join(',', advisories)}]";

    private static string Advisory(
        string packageId,
        string range,
        string fixedVersion,
        string ghsaId) =>
        $$"""
        {
          "ghsa_id":"{{ghsaId}}",
          "cve_id":"CVE-2026-1234",
          "type":"reviewed",
          "severity":"high",
          "published_at":"2026-09-09T16:04:11Z",
          "updated_at":"2026-09-09T18:00:00Z",
          "withdrawn_at":null,
          "vulnerabilities":[{
            "package":{"ecosystem":"nuget","name":"{{packageId}}"},
            "vulnerable_version_range":"{{range}}",
            "first_patched_version":"{{fixedVersion}}"
          }]
        }
        """;

    private static string Leaf(string name) =>
        $"https://api.nuget.org/v3/catalog0/data/{name}.json";

    private static string Stamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    private static RouteResponse Json(string body) =>
        new(HttpStatusCode.OK, body);

    private sealed record RouteResponse(
        HttpStatusCode StatusCode,
        string Body);

    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, RouteResponse> _routes =
            new(StringComparer.Ordinal);

        public RouteResponse this[string url]
        {
            set => _routes[url] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            RouteResponse response = _routes.TryGetValue(
                url,
                out RouteResponse? configured)
                ? configured
                : new RouteResponse(HttpStatusCode.NotFound, "{}");
            return Task.FromResult(new HttpResponseMessage(
                response.StatusCode)
            {
                Content = new StringContent(
                    response.Body,
                    Encoding.UTF8,
                    "application/json"),
                RequestMessage = request,
            });
        }
    }

    private sealed class SingleResponseHandler(
        HttpStatusCode statusCode,
        string body)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    body,
                    Encoding.UTF8,
                    "application/json"),
                RequestMessage = request,
            });
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
