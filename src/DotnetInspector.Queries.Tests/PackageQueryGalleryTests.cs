using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using DotnetInspector.Packages;
using DotnetInspector.RowSelection;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageQueryGalleryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("json")]
    [InlineData(" owner:somebody tags:\"a b\" ")]
    [InlineData("System.*")]
    public void PlanGalleryPreservesSourceInputAndDoesNotInterpretPrefixes(string? text)
    {
        var request = new NuGetGalleryDiscoveryRequest(
            PackageSourceDescriptor.NuGetGallery, 200, text);
        PackageQueryPlan plan = Accepted(PackageQuery.PlanGallery(request, maximumMatches: 7));
        Assert.Same(request, plan.GalleryRequest);
        Assert.Equal(text, plan.GalleryRequest!.Text);
        Assert.Equal("", plan.Prefix.ToString());
        Assert.Equal(200, plan.MaximumCandidates);
        Assert.Equal(7, plan.MaximumMatches);
        Assert.Empty(plan.Facets);
        Assert.Null(Accepted(PackageQuery.Plan(new PackageQueryRequest("System."))).GalleryRequest);
    }

    [Theory]
    [InlineData("\u202Ehidden")]
    [InlineData("json\u001b[2J")]
    [InlineData("json\0")]
    public void PlanGalleryRejectsNonInertTextWithoutEchoingIt(string text)
    {
        var result = PackageQuery.PlanGallery(
            new NuGetGalleryDiscoveryRequest(PackageSourceDescriptor.NuGetGallery, 20, text));
        var failure = Assert.IsType<PackageQueryPlanResult.Rejected>(result).Failure;
        Assert.Equal(PackageQueryRequestFailureReason.InvalidSearchText, failure.Reason);
        Assert.DoesNotContain(text, failure.Message);
    }

    [Fact]
    public void GalleryAndPrefixReuseTheFacetAndMatchBoundPlanner()
    {
        string[][] selections =
        [
            [""],
            ["unknown"],
            [PackageQuery.ToolFacetId, PackageQuery.ToolFacetId],
            [PackageQuery.HasDependenciesFacetId, PackageQuery.NoDependenciesFacetId],
            [PackageQuery.ToolFacetId, PackageQuery.ToolV2FacetId],
            [PackageQuery.EmbeddedSkillFacetId],
            [.. Enumerable.Repeat(PackageQuery.ToolFacetId, 10)],
        ];
        foreach (string[] selected in selections)
        {
            var prefix = Assert.IsType<PackageQueryPlanResult.Rejected>(PackageQuery.Plan(
                new PackageQueryRequest("System.", selected, MaximumCandidates: 21)));
            var gallery = Assert.IsType<PackageQueryPlanResult.Rejected>(PackageQuery.PlanGallery(
                new NuGetGalleryDiscoveryRequest(PackageSourceDescriptor.NuGetGallery, 21), selected));
            Assert.Equal(prefix.Failure.Reason, gallery.Failure.Reason);
            Assert.Equal(prefix.Failure.FacetIds, gallery.Failure.FacetIds);
        }
        Assert.Equal(
            PackageQueryRequestFailureReason.InvalidMatchLimit,
            Assert.IsType<PackageQueryPlanResult.Rejected>(PackageQuery.PlanGallery(
                new NuGetGalleryDiscoveryRequest(PackageSourceDescriptor.NuGetGallery, 20),
                maximumMatches: 0)).Failure.Reason);
        Assert.Equal(20, Accepted(PackageQuery.PlanGallery(
            new NuGetGalleryDiscoveryRequest(PackageSourceDescriptor.NuGetGallery, 20),
            [PackageQuery.ToolV1FacetId, PackageQuery.ToolV2FacetId])).MaximumCandidates);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(5, 3)]
    [InlineData(3, 10)]
    [InlineData(5, 5)]
    public async Task MetadataSelectionEqualsHeadOverTheFullResponseIncludingTies(
        int count, int maximumMatches)
    {
        string[] ids = [.. Enumerable.Range(0, count).Select(i => $"Sample.{i}")];
        string[] rows = [.. ids.Select(id => Row(id, downloads: 10))];
        using var handler = new GalleryHandler(Envelope(rows));
        using var source = Source(handler);
        var request = new NuGetGalleryDiscoveryRequest(
            PackageSourceDescriptor.NuGetGallery, 5,
            packageType: NuGetGalleryPackageType.DotnetTool);
        var events = await PackageQuery.ExecuteToArrayAsync(
            source, Accepted(PackageQuery.PlanGallery(request, maximumMatches: maximumMatches)),
            TestContext.Current.CancellationToken);

        var expected = RowSelectionExecutor.Apply(
            ids,
            RowSelectionPlan<string>.Create([RowSelectionStage<string>.Head(maximumMatches)]));
        var matches = events.OfType<PackageQueryEvent.Match>().Select(e => e.Value).ToArray();
        Assert.Equal(expected.Values, matches.Select(match => match.Package.PackageId));
        Assert.All(matches, match =>
        {
            Assert.Equal(PackageQueryFacetTier.SearchMetadata, match.Tier);
            Assert.Null(match.Package.Manifest);
            Assert.Null(match.Package.Verified);
            Assert.Equal(10, match.Package.TotalDownloads);
            Assert.Same(source.Source, match.Package.Source);
            Assert.DoesNotContain(match.Evidence, item => item.Id == PackageQuery.PrefixEvidenceId);
            Assert.Contains(match.Evidence, item => item.Id == PackageQuery.GalleryPackageTypeEvidenceId);
            Assert.Contains(match.Evidence, item => item.Id == PackageQuery.GalleryOrderEvidenceId);
            Assert.DoesNotContain(match.Evidence, item => item.Id == PackageQuery.ToolFacetId);
            Assert.All(match.Evidence, item =>
            {
                Assert.Equal(PackageQueryEvidenceScope.Query, item.Scope);
                Assert.Null(item.Summary);
            });
        });
        var summary = Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(count, summary.SourceCandidates);
        Assert.Equal(5, summary.CandidateLimit);
        Assert.Equal(maximumMatches, summary.MatchLimit);
        Assert.Equal(Math.Min(count, maximumMatches), summary.Candidates);
        Assert.Equal(Math.Min(count, maximumMatches), summary.Matches);
        Assert.Equal(500, summary.EstimatedTotalHits);
        Assert.Equal(
            count > maximumMatches
                ? PackageQueryCompletionKind.MatchLimitReached
                : PackageQueryCompletionKind.GalleryResponseComplete,
            summary.Completion);
        Assert.Contains("take=5", Assert.Single(handler.Requests));
        Assert.Equal([PackageQueryProgressPhase.Search],
            events.OfType<PackageQueryEvent.Progress>().Select(e => e.Value.Phase).Distinct());
    }

    [Fact]
    public async Task CapacityDependentRankingDoesNotReplaceHeadWithSmallerAcquisition()
    {
        // Indexed membership is A, B, C; auxiliary lifetime order within K=3 is B, C, A.
        string fullResponse = Envelope(
            Row("B", 600), Row("C", 500), Row("A", 10));
        using var fullHandler = new GalleryHandler(fullResponse);
        using var fullSource = Source(fullHandler);
        var fullEvents = await PackageQuery.ExecuteToArrayAsync(
            fullSource,
            Accepted(PackageQuery.PlanGallery(
                new NuGetGalleryDiscoveryRequest(PackageSourceDescriptor.NuGetGallery, 3),
                maximumMatches: 1)),
            TestContext.Current.CancellationToken);
        using var smallerHandler = new GalleryHandler(Envelope(Row("A", 10)));
        using var smallerSource = Source(smallerHandler);
        var smallerEvents = await PackageQuery.ExecuteToArrayAsync(
            smallerSource,
            Accepted(PackageQuery.PlanGallery(
                new NuGetGalleryDiscoveryRequest(PackageSourceDescriptor.NuGetGallery, 1),
                maximumMatches: 1)),
            TestContext.Current.CancellationToken);

        Assert.Equal("B", Assert.Single(fullEvents.OfType<PackageQueryEvent.Match>()).Value.Package.PackageId);
        Assert.Equal("A", Assert.Single(smallerEvents.OfType<PackageQueryEvent.Match>()).Value.Package.PackageId);
        Assert.Contains("take=3", Assert.Single(fullHandler.Requests));
        Assert.Contains("take=1", Assert.Single(smallerHandler.Requests));
        Assert.Equal(3, Assert.IsType<PackageQueryEvent.Completed>(fullEvents[^1]).Value.SourceCandidates);
    }

    [Fact]
    public async Task ManifestSelectionIsLocalAndStopsEnrichmentAfterHeadOfMatches()
    {
        string response = Envelope(Row("Library", 30), Row("Tool", 20), Row("AnotherTool", 10));
        var manifests = new Dictionary<string, string>
        {
            ["library"] = Manifest("Library"),
            ["tool"] = Manifest("Tool", tool: true),
            ["anothertool"] = Manifest("AnotherTool", tool: true),
        };
        using var handler = new GalleryHandler(response, manifests);
        using var source = Source(handler);
        var request = new NuGetGalleryDiscoveryRequest(PackageSourceDescriptor.NuGetGallery, 3);
        var events = await PackageQuery.ExecuteToArrayAsync(source,
            Accepted(PackageQuery.PlanGallery(request, [PackageQuery.ToolFacetId], maximumMatches: 1)),
            TestContext.Current.CancellationToken);
        using var allHandler = new GalleryHandler(response, manifests);
        using var allSource = Source(allHandler);
        var allEvents = await PackageQuery.ExecuteToArrayAsync(allSource,
            Accepted(PackageQuery.PlanGallery(request, [PackageQuery.ToolFacetId])),
            TestContext.Current.CancellationToken);
        var reference = RowSelectionExecutor.Apply(
            allEvents.OfType<PackageQueryEvent.Match>().Select(e => e.Value.Package.PackageId).ToArray(),
            RowSelectionPlan<string>.Create([RowSelectionStage<string>.Head(1)]));

        PackageQueryMatch match = Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal(reference.Values, [match.Package.PackageId]);
        Assert.Equal("Tool", match.Package.PackageId);
        Assert.True(match.Package.Manifest!.IsToolPackage);
        Assert.Null(match.Package.Verified);
        Assert.Equal(PackageQueryFacetTier.Nuspec, match.Tier);
        Assert.Contains(match.Evidence, e => e.Id == PackageQuery.ToolFacetId);
        Assert.DoesNotContain("packageType=", handler.Requests[0]);
        Assert.Contains("take=3", handler.Requests[0]);
        Assert.Equal(3, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, r => r.Contains("anothertool"));
        PackageQuerySummary summary = Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(3, summary.SourceCandidates);
        Assert.Equal(2, summary.Candidates);
        Assert.Equal(PackageQueryCompletionKind.MatchLimitReached, summary.Completion);
    }

    [Theory]
    [InlineData(PackageQuery.VerifiedFacetId)]
    [InlineData(PackageQuery.MillionDownloadsFacetId)]
    public async Task UnavailableMetadataDoesNotSatisfySourceFactFacets(string facet)
    {
        using var handler = new GalleryHandler(Envelope(Row("Sample")), new()
        {
            ["sample"] = Manifest("Sample"),
        });
        using var source = Source(handler);
        var events = await PackageQuery.ExecuteToArrayAsync(source,
            Accepted(PackageQuery.PlanGallery(
                new NuGetGalleryDiscoveryRequest(PackageSourceDescriptor.NuGetGallery, 10, "sample"),
                [facet])),
            TestContext.Current.CancellationToken);
        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Empty(events.OfType<PackageQueryEvent.Failure>());
        Assert.Equal(PackageQueryCompletionKind.GalleryResponseComplete,
            Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value.Completion);
    }

    [Fact]
    public async Task ExplicitManifestEnrichmentRetainsAbsentSourceMetadata()
    {
        using var handler = new GalleryHandler(Envelope(Row("Sample")), new()
        {
            ["sample"] = Manifest("Sample"),
        });
        using var source = Source(handler);
        var events = await PackageQuery.ExecuteToArrayAsync(source,
            Accepted(PackageQuery.PlanGallery(
                new NuGetGalleryDiscoveryRequest(PackageSourceDescriptor.NuGetGallery, 10, "sample"),
                [PackageQuery.NoDependenciesFacetId])),
            TestContext.Current.CancellationToken);
        var match = Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        Assert.NotNull(match.Package.Manifest);
        Assert.Null(match.Package.TotalDownloads);
        Assert.Null(match.Package.Verified);
        Assert.Empty(match.Package.Owners);
        Assert.Equal(PackageQueryFacetTier.Nuspec, match.Tier);
    }

    [Fact]
    public async Task ContentProviderIsExplicitAndReceivesRealManifestWithOptionalMetadata()
    {
        using var handler = new GalleryHandler(Envelope(Row("Tool")), new()
        {
            ["tool"] = Manifest("Tool", tool: true),
        });
        using var source = Source(handler);
        PackageQueryPlan plan = Accepted(PackageQuery.PlanGallery(
            new NuGetGalleryDiscoveryRequest(PackageSourceDescriptor.NuGetGallery, 20, "tool"),
            [PackageQuery.ToolV2FacetId]));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await PackageQuery.ExecuteToArrayAsync(source, plan, TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
        var provider = new ContentProvider();

        var events = await PackageQuery.ExecuteToArrayAsync(source, plan, provider,
            TestContext.Current.CancellationToken);

        var match = Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal(PackageQueryFacetTier.PackageContent, match.Tier);
        Assert.Contains(match.Evidence, e => e.Id == PackageQuery.ToolV2FacetId);
        var package = Assert.Single(provider.Requests);
        Assert.True(package.Manifest!.IsToolPackage);
        Assert.Null(package.TotalDownloads);
        Assert.Null(package.Verified);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task AcquisitionFailureIsVisibleAndPublishesNoPartialInput()
    {
        using var handler = new GalleryHandler($$"""{"data":[{{Row("Good", 10)}},null]}""");
        using var source = Source(handler);
        var events = await PackageQuery.ExecuteToArrayAsync(source,
            Accepted(PackageQuery.PlanGallery(
                new NuGetGalleryDiscoveryRequest(PackageSourceDescriptor.NuGetGallery, 10))),
            TestContext.Current.CancellationToken);
        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Equal(PackageQueryFailureKind.Search,
            Assert.Single(events.OfType<PackageQueryEvent.Failure>()).Value.Kind);
        var summary = Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(0, summary.Candidates);
        Assert.Null(summary.SourceCandidates);
        Assert.Null(summary.EstimatedTotalHits);
        Assert.Equal(PackageQueryCompletionKind.Failed, summary.Completion);
    }

    [Fact]
    public async Task ManifestFailureKeepsFiniteInputAccountingAndContinues()
    {
        using var handler = new GalleryHandler(Envelope(Row("Missing", 20), Row("Good", 10)), new()
        {
            ["good"] = Manifest("Good", tool: true),
        });
        using var source = Source(handler);
        var events = await PackageQuery.ExecuteToArrayAsync(source,
            Accepted(PackageQuery.PlanGallery(
                new NuGetGalleryDiscoveryRequest(PackageSourceDescriptor.NuGetGallery, 10),
                [PackageQuery.ToolFacetId])),
            TestContext.Current.CancellationToken);
        Assert.Single(events.OfType<PackageQueryEvent.Match>());
        Assert.Equal(PackageQueryFailureKind.ManifestAcquisition,
            Assert.Single(events.OfType<PackageQueryEvent.Failure>()).Value.Kind);
        var summary = Assert.IsType<PackageQueryEvent.Completed>(events[^1]).Value;
        Assert.Equal(2, summary.SourceCandidates);
        Assert.Equal(2, summary.Candidates);
        Assert.Equal(1, summary.Failures);
        Assert.Equal(PackageQueryCompletionKind.GalleryResponseComplete, summary.Completion);
    }

    [Fact]
    public async Task CancellationBetweenMatchesStopsFurtherEnrichment()
    {
        using var handler = new GalleryHandler(Envelope(Row("First", 20), Row("Second", 10)), new()
        {
            ["first"] = Manifest("First", tool: true),
            ["second"] = Manifest("Second", tool: true),
        });
        using var source = Source(handler);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var plan = Accepted(PackageQuery.PlanGallery(
            new NuGetGalleryDiscoveryRequest(PackageSourceDescriptor.NuGetGallery, 10),
            [PackageQuery.ToolFacetId]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in PackageQuery.ExecuteAsync(source, plan, cancellation.Token))
                if (item is PackageQueryEvent.Match)
                    cancellation.Cancel();
        });
        Assert.Equal(2, handler.Requests.Count);
    }

    private static PackageQueryPlan Accepted(PackageQueryPlanResult result) =>
        Assert.IsType<PackageQueryPlanResult.Accepted>(result).Plan;

    private static INuGetGalleryPackageSourceClient Source(HttpMessageHandler handler) =>
        PackageSourceClientFactory.CreateGallery(PackageSourceAssociation.Create(), handler);

    private static string Row(string id, long? downloads = null) => $$"""
        {"PackageRegistration":{"Id":"{{id}}"{{(downloads is null ? "" : $",\"DownloadCount\":{downloads}")}}},
        "Version":"1.0.0","NormalizedVersion":"1.0.0"}
        """;

    private static string Envelope(params string[] rows) =>
        $$"""{"totalHits":500,"data":[{{string.Join(',', rows)}}]}""";

    private static string Manifest(string id, bool tool = false) => $$"""
        <package><metadata><id>{{id}}</id><version>1.0.0</version>
        <authors>Example</authors><description>Example</description>
        {{(tool ? """<packageTypes><packageType name="DotnetTool" /></packageTypes>""" : "")}}
        </metadata></package>
        """;

    private sealed class GalleryHandler(
        string response,
        Dictionary<string, string>? manifests = null) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Uri uri = request.RequestUri!;
            Requests.Add(uri.AbsoluteUri);
            string? body = null;
            if (uri.AbsolutePath == "/search/query")
                body = response;
            else if (uri.AbsolutePath.EndsWith(".nuspec", StringComparison.Ordinal))
                manifests?.TryGetValue(uri.AbsolutePath.Split('/')[2], out body);
            else
                Assert.Fail($"Unexpected acquisition: {uri}");
            return Task.FromResult(new HttpResponseMessage(
                body is null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = new StringContent(body ?? "", Encoding.UTF8),
            });
        }
    }

    private sealed class ContentProvider : IPackageQueryContentProvider
    {
        public List<PackageQueryPackage> Requests { get; } = [];

        public ValueTask<PackageQueryContentResult> GetContentAsync(
            PackageQueryPackage package, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(package);
            return ValueTask.FromResult<PackageQueryContentResult>(
                new PackageQueryContentResult.Available(new ToolContent()));
        }
    }

    private sealed class ToolContent : IPackageContent
    {
        public string? RootPath => null;
        public string? NupkgPath => null;
        public bool FromCache => false;
        public string ProducerKey => "nuget.org";
        public bool RequiresArchiveTreeMatch => false;
        public IEnumerable<string> EnumerateEntries() => ["tools/DotnetToolSettings.xml"];
        public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream)
        {
            stream = null;
            return false;
        }
        public bool TryOpenEntry(string relativePath, [NotNullWhen(true)] out Stream? stream) =>
            TryOpenEntry(relativePath, long.MaxValue, out stream);
        public bool TryOpenEntry(
            string relativePath, long maxExpandedBytes, [NotNullWhen(true)] out Stream? stream)
        {
            stream = new MemoryStream(Encoding.UTF8.GetBytes(
                """<DotNetCliTool Version="2"><Commands /></DotNetCliTool>"""));
            return true;
        }
    }
}
