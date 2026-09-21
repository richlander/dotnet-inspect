using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using Markout;
using NuGetFetch;

namespace DotnetInspector.Presentation.Tests;

public sealed class EcosystemChangeReportPresentationTests
{
    const string ServiceIndex =
        "https://api.nuget.org/v3/index.json";
    const string Catalog =
        "https://api.nuget.org/v3/catalog0/index.json";
    const string Page =
        "https://api.nuget.org/v3/catalog0/page0.json";

    static readonly DateTimeOffset ReferenceTime =
        Utc(2026, 10, 14);
    static readonly DateTimeOffset From =
        ReferenceTime - TimeSpan.FromDays(42);

    [Fact]
    public async Task CollectAndSerialize_PreservesEvidenceAndTimeBases()
    {
        string affectedLeaf = Leaf("affected");
        string fixedLeaf = Leaf("fixed");
        string fallbackLeaf = Leaf("fallback");
        string deletedLeaf = Leaf("deleted");
        var catalog = StandardCatalog(
            Item(
                affectedLeaf,
                "Microsoft.Extensions.AI",
                "1.0.0",
                From + TimeSpan.FromDays(10)),
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
                deletedLeaf,
                "Microsoft.Extensions.AI",
                "1.1.0",
                From + TimeSpan.FromDays(30),
                "nuget:PackageDelete"));
        DateTimeOffset createdReceipt =
            From + TimeSpan.FromDays(15);
        DateTimeOffset fallbackReceipt =
            From + TimeSpan.FromDays(16);
        catalog[fixedLeaf] = Json(Details(
            "Microsoft.Extensions.AI",
            "1.1.0",
            From + TimeSpan.FromDays(20),
            createdReceipt,
            createdReceipt));
        catalog[fallbackLeaf] = Json(Details(
            "Contoso.Fallback",
            "2.0.0",
            From + TimeSpan.FromDays(25),
            fallbackReceipt,
            created: null));
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
                        "GHSA-dddd-eeee-ffff"))));
        var advisoryService = new GitHubNuGetAdvisoryService(
            advisoryClient,
            timeProvider: new FixedTimeProvider(ReferenceTime));
        EcosystemChangeReportPlan plan =
            EcosystemChangeReportPlan.Resolve(
                new EcosystemChangeReportRequest(
                    new EcosystemChangePackageSelection.PackageSet(
                        "package-set.real-witness",
                        [
                            new PackageCoordinate(
                                "Microsoft.Extensions.AI"),
                            new PackageCoordinate("Contoso.Fallback"),
                        ])),
                new FixedTimeProvider(ReferenceTime));

        EcosystemChangeReportDocument document =
            await EcosystemChangeReportPresentation.CollectAsync(
                EcosystemChangeReportQuery.ExecuteAsync(
                    source,
                    advisoryService,
                    plan,
                    TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            EcosystemChangeReportDocument.CurrentSchemaVersion,
            document.SchemaVersion);
        Assert.True(document.Request.UsedDefaultInterval);
        Assert.Equal(From, document.Request.FromExclusive);
        Assert.Equal(
            ReferenceTime,
            document.Request.ThroughInclusive);
        Assert.Equal(
            ["Microsoft.Extensions.AI", "Contoso.Fallback"],
            document.Request.PackageScope.PackageIds);
        Assert.Equal(4, document.Rows.Length);

        EcosystemChangeReportRowPresentation deletion =
            Assert.Single(
                document.Rows,
                row => row.CatalogActivity.CatalogKind
                    == NuGetCatalogEventKind.Delete);
        Assert.Equal(
            EcosystemChangeActivityKind.DeletionObserved,
            deletion.CatalogActivity.Activity);
        Assert.Equal(
            EcosystemSecurityReleaseStatus.DeleteActivityUnevaluable,
            deletion.SecurityReleaseStatus);
        Assert.Null(deletion.SecurityRelease);

        EcosystemChangeReportRowPresentation created =
            Assert.Single(
                document.Rows,
                row => row.CatalogActivity.Version == "1.1.0"
                    && row.CatalogActivity.CatalogKind
                        == NuGetCatalogEventKind.Details);
        Assert.Equal(
            NuGetCatalogPackageReceiptBasis.Created,
            created.PackageReceipt!.Basis);
        Assert.Equal(createdReceipt, created.PackageReceipt.ReceivedAt);
        Assert.NotNull(created.SecurityRelease);
        Assert.Equal(
            "GHSA-AAAA-BBBB-CCCC",
            Assert.Single(created.SecurityRelease.Advisories).GhsaId);

        EcosystemChangeReportRowPresentation fallback =
            Assert.Single(
                document.Rows,
                row => row.CatalogActivity.Version == "2.0.0");
        Assert.Equal(
            NuGetCatalogPackageReceiptBasis.PublishedFallback,
            fallback.PackageReceipt!.Basis);
        Assert.Equal(fallbackReceipt, fallback.PackageReceipt.ReceivedAt);

        EcosystemChangeReportRowPresentation affected =
            Assert.Single(
                document.Rows,
                row => row.CatalogActivity.Version == "1.0.0");
        Assert.Single(affected.CurrentAdvisoryContext.Advisories);
        Assert.Empty(affected.FixedVersionEvidence.Advisories);
        Assert.Null(affected.SecurityRelease);

        string json = EcosystemChangeReportJson.Serialize(document);
        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement root = parsed.RootElement;
        Assert.Equal(
            "PackageSet",
            root.GetProperty("request")
                .GetProperty("package_scope")
                .GetProperty("kind")
                .GetString());
        Assert.Equal(
            "PublishedFallback",
            root.GetProperty("rows")[1]
                .GetProperty("package_receipt")
                .GetProperty("basis")
                .GetString());
        Assert.Equal(
            "2026-09-09T16:04:11+00:00",
            root.GetProperty("rows")[1]
                .GetProperty("fixed_version_evidence")
                .GetProperty("advisories")[0]
                .GetProperty("published_at")
                .GetString());
        Assert.Equal(
            fallbackReceipt,
            EcosystemChangeReportJson.Deserialize(json)
                .Rows[1].PackageReceipt!.ReceivedAt);
        Assert.DoesNotContain(
            '\n',
            EcosystemChangeReportJson.Serialize(document, compact: true));

        string markdown = MarkoutSerializer.Serialize(
            EcosystemChangeReportView.Create(document),
            EcosystemChangeReportViewContext.Default);
        Assert.Contains(
            "# package-set.real-witness",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "## Activity",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "Snapshot observed",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "Deletion observed",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "PublishedFallback",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "## Advisory evidence",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "Current advisory context",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "Exact first patched",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "Security release",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "2026-09-09T16:04:11.0000000+00:00",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            Time(fallbackReceipt),
            markdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartialFailureAndMissingTerminalStayVisible()
    {
        DateTimeOffset through =
            ReferenceTime - TimeSpan.FromDays(1);
        DateTimeOffset horizon =
            through - TimeSpan.FromDays(1);
        var catalog = StandardCatalog(
            horizon,
            Item(
                Leaf("partial"),
                "Microsoft.Extensions.AI",
                "1.0.0",
                From + TimeSpan.FromDays(10)));
        using INuGetCatalogPackageSourceClient source =
            CreateSource(catalog);
        using var advisoryClient = new HttpClient(
            new SingleResponseHandler(HttpStatusCode.Forbidden, "[]"));
        var advisoryService =
            new GitHubNuGetAdvisoryService(advisoryClient);
        EcosystemChangeReportPlan plan =
            EcosystemChangeReportPlan.Resolve(
                new EcosystemChangeReportRequest(
                    new EcosystemChangePackageSelection.PackagePrefix(
                        new PackagePrefixDeclaration(
                            "Microsoft.Extensions.")),
                    new NuGetCatalogRequest(From, through)),
                new FixedTimeProvider(ReferenceTime));

        IReadOnlyList<EcosystemChangeReportEvent> events =
            await EcosystemChangeReportQuery.ExecuteToArrayAsync(
                source,
                advisoryService,
                plan,
                TestContext.Current.CancellationToken);
        EcosystemChangeReportDocument document =
            EcosystemChangeReportPresentation.Create(events);
        var sink = new RecordingNonterminalSink();
        InspectionEnvelope<EcosystemChangeReportDocument> inspection =
            await EcosystemChangeReportInspection.ExecuteAsync(
                IgnoreCancellation(events),
                sink,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            EcosystemChangeReportCompletionKind.Partial,
            document.Summary.Completion);
        Assert.Equal(
            EcosystemChangeReportJson.Serialize(document, compact: true),
            EcosystemChangeReportJson.Serialize(
                inspection.Content,
                compact: true));
        InspectionPortableProjection.NonProjectable share =
            Assert.IsType<InspectionPortableProjection.NonProjectable>(inspection.PortableProjection);
        Assert.Equal("package-changes/share", share.Path);
        Assert.Empty(inspection.Diagnostics);
        Assert.Equal(
            events.Count(static item =>
                item is not EcosystemChangeReportEvent.Completed),
            sink.Events.Count);
        Assert.Equal(
            document.Rows.Select(Serialize),
            sink.Events
                .OfType<EcosystemChangeReportNonterminalEvent.Row>()
                .Select(static item => Serialize(item.Value)));
        Assert.Equal(
            document.Failures.Select(Serialize),
            sink.Events
                .OfType<EcosystemChangeReportNonterminalEvent.Failure>()
                .Select(static item => Serialize(item.Value)));
        Assert.Equal(
            document.Progress,
            sink.Events
                .OfType<EcosystemChangeReportNonterminalEvent.Progress>()
                .Select(static item => item.Value));
        Assert.False(document.Request.UsedDefaultInterval);
        Assert.Equal(ReferenceTime, document.Request.ReferenceTime);
        Assert.Equal(through, document.Request.ThroughInclusive);
        Assert.Equal(
            EcosystemChangePackageScopeKind.PackagePrefix,
            document.Request.PackageScope.Kind);
        Assert.Equal(
            "Microsoft.Extensions.",
            document.Request.PackageScope.Prefix);
        Assert.Equal(horizon, document.Summary.CapturedHorizon);
        Assert.Equal(
            NuGetCatalogCompletion.SourceHorizonReached,
            document.Summary.CatalogCompletion);
        Assert.False(document.Summary.AdvisoryEvidence.Complete);
        Assert.Equal(
            GitHubNuGetAdvisoryAvailability.Unavailable,
            Assert.Single(document.Rows)
                .CurrentAdvisoryContext.Availability);
        EcosystemChangeReportFailurePresentation failure =
            Assert.Single(document.Failures);
        Assert.Equal(
            EcosystemChangeFailureProvider.Advisory,
            failure.Provider);
        Assert.Equal(
            GitHubNuGetAdvisoryFailureKind.RateLimitOrForbidden,
            failure.AdvisoryFailure);

        string markdown = MarkoutSerializer.Serialize(
            EcosystemChangeReportView.Create(document),
            EcosystemChangeReportViewContext.Default);
        Assert.Contains(
            "advisory evidence incomplete",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "Catalog: SourceHorizonReached",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            Time(horizon),
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            Time(through),
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            Time(ReferenceTime),
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "Explicit",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "RateLimitOrForbidden",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "Unavailable",
            markdown,
            StringComparison.Ordinal);

        string json = EcosystemChangeReportJson.Serialize(document);
        Assert.Contains(
            "\"completion\": \"Partial\"",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"advisory_failure\": \"RateLimitOrForbidden\"",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"catalog_completion\": \"SourceHorizonReached\"",
            json,
            StringComparison.Ordinal);

        InvalidOperationException missingTerminal =
            Assert.Throws<InvalidOperationException>(() =>
                EcosystemChangeReportPresentation.Create(
                    events.Where(static item =>
                        item is not EcosystemChangeReportEvent.Completed)));
        Assert.Contains(
            "without terminal completion",
            missingTerminal.Message,
            StringComparison.Ordinal);

        InvalidOperationException missingFailure =
            Assert.Throws<InvalidOperationException>(() =>
                EcosystemChangeReportPresentation.Create(
                    events.Where(static item =>
                        item
                            is not EcosystemChangeReportEvent.Failure.Advisory)));
        Assert.Contains(
            "failure events do not match",
            missingFailure.Message,
            StringComparison.Ordinal);

        InvalidOperationException afterTerminal =
            Assert.Throws<InvalidOperationException>(() =>
                EcosystemChangeReportPresentation.Create(
                    events.Append(
                        new EcosystemChangeReportEvent.Progress(
                            new EcosystemChangeReportProgress(
                                EcosystemChangeReportProgressPhase.Catalog,
                                Completed: 1,
                                Total: 1)))));
        Assert.Contains(
            "after terminal completion",
            afterTerminal.Message,
            StringComparison.Ordinal);
        var afterTerminalSink = new RecordingNonterminalSink();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EcosystemChangeReportInspection.ExecuteAsync(
                IgnoreCancellation(
                    events.Append(
                        new EcosystemChangeReportEvent.Progress(
                            new EcosystemChangeReportProgress(
                                EcosystemChangeReportProgressPhase.Catalog,
                                Completed: 1,
                                Total: 1)))),
                afterTerminalSink,
                TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(sink.Events.Count, afterTerminalSink.Events.Count);

        using INuGetCatalogPackageSourceClient foreignSource =
            CreateSource(StandardCatalog(
                horizon,
                Item(
                    Leaf("foreign"),
                    "Microsoft.Extensions.AI",
                    "1.0.0",
                    From + TimeSpan.FromDays(10))));
        using var foreignAdvisoryClient = new HttpClient(
            new SingleResponseHandler(HttpStatusCode.Forbidden, "[]"));
        IReadOnlyList<EcosystemChangeReportEvent> foreignEvents =
            await EcosystemChangeReportQuery.ExecuteToArrayAsync(
                foreignSource,
                new GitHubNuGetAdvisoryService(foreignAdvisoryClient),
                plan,
                TestContext.Current.CancellationToken);
        EcosystemChangeReportEvent.Row foreignRow =
            Assert.Single(
                foreignEvents.OfType<EcosystemChangeReportEvent.Row>());
        InvalidOperationException foreignSourceIdentity =
            Assert.Throws<InvalidOperationException>(() =>
                EcosystemChangeReportPresentation.Create(
                    events.Select(item =>
                        item is EcosystemChangeReportEvent.Row
                            ? foreignRow
                            : item)));
        Assert.Contains(
            "terminal source identity",
            foreignSourceIdentity.Message,
            StringComparison.Ordinal);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            EcosystemChangeReportPresentation.CollectAsync(
                IgnoreCancellation(events),
                cancelled.Token));
    }

    static INuGetCatalogPackageSourceClient CreateSource(
        RouteHandler handler)
    {
        IPackageSourceClient source = PackageSourceClientFactory.Create(
            PackageSourceDescriptor.NuGetV3(
                "nuget.org",
                "NuGet.org",
                new Uri(ServiceIndex)),
            PackageSourceAssociation.Create(),
            handler);
        return Assert.IsAssignableFrom<INuGetCatalogPackageSourceClient>(
            source);
    }

    static RouteHandler StandardCatalog(params string[] items) =>
        StandardCatalog(ReferenceTime, items);

    static RouteHandler StandardCatalog(
        DateTimeOffset horizon,
        params string[] items) =>
        new()
        {
            [ServiceIndex] = Json(
                $$"""
                {"version":"3.0.0","resources":[
                {"@id":"{{Catalog}}","@type":"Catalog/3.0.0"}]}
                """),
            [Catalog] = Json(
                $$"""
                {"commitId":"index",
                "commitTimeStamp":"{{Time(horizon)}}",
                "count":1,"items":[{
                "@id":"{{Page}}","commitId":"page",
                "commitTimeStamp":"{{Time(horizon)}}",
                "count":999}]}
                """),
            [Page] = Json(
                $$"""
                {"commitId":"page",
                "commitTimeStamp":"{{Time(horizon)}}",
                "count":{{items.Length}},"parent":"{{Catalog}}",
                "items":[{{string.Join(',', items)}}]}
                """),
        };

    static async IAsyncEnumerable<EcosystemChangeReportEvent>
        IgnoreCancellation(
            IEnumerable<EcosystemChangeReportEvent> events)
    {
        foreach (EcosystemChangeReportEvent item in events)
        {
            yield return item;
            await Task.Yield();
        }
    }

    sealed class RecordingNonterminalSink
        : IEcosystemChangeReportNonterminalSink
    {
        internal List<EcosystemChangeReportNonterminalEvent> Events { get; } =
            [];

        public ValueTask ReportAsync(
            EcosystemChangeReportNonterminalEvent reportEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(reportEvent);
            return ValueTask.CompletedTask;
        }
    }

    static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value);

    static string Item(
        string leafUrl,
        string packageId,
        string version,
        DateTimeOffset timestamp,
        string kind = "nuget:PackageDetails") =>
        $$"""
        {"@id":"{{leafUrl}}","@type":"{{kind}}",
        "commitId":"commit","commitTimeStamp":"{{Time(timestamp)}}",
        "nuget:id":"{{packageId}}","nuget:version":"{{version}}"}
        """;

    static string Details(
        string packageId,
        string version,
        DateTimeOffset commitTimestamp,
        DateTimeOffset published,
        DateTimeOffset? created)
    {
        string createdProperty = created is { } value
            ? ",\"created\":\"" + Time(value) + "\""
            : "";
        return $$"""
        {"@type":["PackageDetails","catalog:Permalink"],
        "catalog:commitId":"commit",
        "catalog:commitTimeStamp":"{{Time(commitTimestamp)}}",
        "id":"{{packageId}}","version":"{{version}}",
        "published":"{{Time(published)}}"{{createdProperty}}}
        """;
    }

    static string AdvisoryPage(params string[] advisories) =>
        $"[{string.Join(',', advisories)}]";

    static string Advisory(
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

    static string Leaf(string name) =>
        $"https://api.nuget.org/v3/catalog0/data/{name}.json";

    static string Time(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    static DateTimeOffset Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    static RouteResponse Json(string body) =>
        new(HttpStatusCode.OK, body);

    sealed record RouteResponse(
        HttpStatusCode StatusCode,
        string Body);

    sealed class RouteHandler : HttpMessageHandler
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

    sealed class SingleResponseHandler(
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

    sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
