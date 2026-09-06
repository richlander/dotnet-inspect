using System.Net;
using System.Text;
using System.Text.Json;
using DotnetInspector.SourceSelection;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageQueryInputTests
{
    [Theory]
    [InlineData("Newtonsoft.Json", false, "Newtonsoft.Json")]
    [InlineData("Newtonsoft", false, "Newtonsoft")]
    [InlineData("Newtonsoft.*", true, "Newtonsoft.")]
    [InlineData("Newtonsoft*", true, "Newtonsoft")]
    [InlineData("  Newtonsoft.Json  ", false, "Newtonsoft.Json")]
    public void PlanInputDistinguishesIdsFromExplicitPrefixes(
        string text, bool prefix, string expected)
    {
        PackageQueryPlan plan = Accepted(PackageQuery.PlanInput(text));
        Assert.Null(plan.GalleryRequest);
        Assert.Equal(expected, plan.Prefix.ToString());
        if (prefix)
        {
            var input = Assert.IsType<SourceSelector.PackagePrefix>(plan.PackageInput);
            Assert.Equal(expected, input.Request.Prefix);
            Assert.Equal(200, input.Request.MaxPackages);
        }
        else
        {
            var input = Assert.IsType<SourceSelector.Package>(plan.PackageInput);
            Assert.Equal(expected, input.Coordinate.PackageId);
            Assert.Null(input.Coordinate.Version);
            Assert.Equal(1, plan.MaximumCandidates);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("*")]
    [InlineData("Newton*soft")]
    [InlineData("Newtonsoft**")]
    [InlineData("json parser")]
    [InlineData("Newtonsoft.Json@1.0.0")]
    [InlineData("Newtonsoft\u001b")]
    public void InvalidInputDoesNotBecomeAPlan(string text)
    {
        var rejected = Assert.IsType<PackageQueryPlanResult.Rejected>(
            PackageQuery.PlanInput(text));
        Assert.Equal(
            PackageQueryRequestFailureReason.InvalidPackageInput,
            rejected.Failure.Reason);
    }

    [Fact]
    public void ExactContentInputUsesOneCandidateWithoutWeakeningFacetValidation()
    {
        PackageQueryPlan plan = Accepted(PackageQuery.PlanInput(
            "Newtonsoft.Json", [PackageQuery.EmbeddedSkillFacetId]));
        Assert.Equal(1, plan.MaximumCandidates);
        Assert.IsType<PackageQueryPlanResult.Rejected>(PackageQuery.PlanInput(
            "Newtonsoft.*", [PackageQuery.EmbeddedSkillFacetId]));
        Assert.IsType<PackageQueryPlanResult.Rejected>(PackageQuery.PlanInput(
            "Newtonsoft.Json", [PackageQuery.HasDependenciesFacetId, PackageQuery.NoDependenciesFacetId]));
    }

    [Theory]
    [InlineData(false, "1.0.0")]
    [InlineData(true, "3.0.0-preview.1")]
    public async Task ExactIdUsesVersionAndListingEvidenceWithoutSearchOrManifest(
        bool includePrerelease, string expectedVersion)
    {
        using var handler = new InputHandler();
        using var source = Source(handler);
        var events = await PackageQuery.ExecuteToArrayAsync(
            source,
            Accepted(PackageQuery.PlanInput(
                "Newtonsoft.Json", maximumMatches: 1, includePrerelease: includePrerelease)),
            TestContext.Current.CancellationToken);

        PackageQueryMatch match = Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal("Newtonsoft.Json", match.Package.PackageId);
        Assert.Equal(expectedVersion, match.Package.Version);
        Assert.Same(source.Source, match.Package.Source);
        Assert.Null(match.Package.Manifest);
        Assert.Null(match.Package.TotalDownloads);
        Assert.Null(match.Package.Verified);
        Assert.Equal(PackageQueryFacetTier.SearchMetadata, match.Tier);
        Assert.Equal(PackageQuery.ExactPackageEvidenceId, Assert.Single(match.Evidence).Id);
        Assert.Equal(PackageQueryEvidenceScope.Query, Assert.Single(match.Evidence).Scope);
        Assert.Equal(PackageQueryCompletionKind.ExactPackageComplete, Summary(events).Completion);
        Assert.Equal(1, Summary(events).Candidates);
        Assert.Equal(1, Summary(events).SourceCandidates);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, uri => Assert.EndsWith("/newtonsoft.json/index.json", uri.AbsolutePath));
        Assert.Contains("flatcontainer", handler.Requests[0].AbsolutePath);
        Assert.Contains("registration", handler.Requests[1].AbsolutePath);
    }

    [Theory]
    [InlineData("Newtonsoft")]
    [InlineData("Newtonsoft.Json")]
    public async Task MissingExactIdDoesNotFallBackToRelatedSearchResults(string packageId)
    {
        using var handler = new InputHandler { Exists = false };
        using var source = Source(handler);
        var events = await PackageQuery.ExecuteToArrayAsync(
            source, Accepted(PackageQuery.PlanInput(packageId)),
            TestContext.Current.CancellationToken);

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Empty(events.OfType<PackageQueryEvent.Failure>());
        Assert.Equal(PackageQueryCompletionKind.ExactPackageComplete, Summary(events).Completion);
        Assert.Equal(0, Summary(events).Candidates);
        Assert.Equal(0, Summary(events).SourceCandidates);
        Assert.Contains("flatcontainer", Assert.Single(handler.Requests).AbsolutePath);
    }

    [Fact]
    public async Task UnknownListingStateIsFailureRatherThanAbsence()
    {
        using var handler = new InputHandler { RegistrationAvailable = false };
        using var source = Source(handler);
        var events = await PackageQuery.ExecuteToArrayAsync(
            source, Accepted(PackageQuery.PlanInput("Newtonsoft.Json")),
            TestContext.Current.CancellationToken);

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Single(events.OfType<PackageQueryEvent.Failure>());
        Assert.Equal(PackageQueryCompletionKind.Failed, Summary(events).Completion);
        Assert.Equal(0, Summary(events).Candidates);
    }

    [Fact]
    public async Task ExactStableSelectionDoesNotFallBackToPrerelease()
    {
        using var handler = new InputHandler { Versions = [new("3.0.0-preview.1", true)] };
        using var source = Source(handler);
        var events = await PackageQuery.ExecuteToArrayAsync(
            source, Accepted(PackageQuery.PlanInput("Newtonsoft.Json")),
            TestContext.Current.CancellationToken);
        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Empty(events.OfType<PackageQueryEvent.Failure>());
        Assert.Equal(PackageQueryCompletionKind.ExactPackageComplete, Summary(events).Completion);
    }

    [Fact]
    public async Task ExactManifestFacetUsesSelectedCoordinate()
    {
        using var handler = new InputHandler();
        using var source = Source(handler);
        var events = await PackageQuery.ExecuteToArrayAsync(
            source,
            Accepted(PackageQuery.PlanInput("Newtonsoft.Json", [PackageQuery.HasDependenciesFacetId])),
            TestContext.Current.CancellationToken);
        PackageQueryMatch match = Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        Assert.NotNull(match.Package.Manifest);
        Assert.Equal(PackageQueryFacetTier.Nuspec, match.Tier);
        Assert.Equal("Fixture package.", match.Package.Description);
        Assert.Equal("/v3-flatcontainer/newtonsoft.json/1.0.0/newtonsoft.json.nuspec",
            handler.Requests[^1].AbsolutePath);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Theory]
    [InlineData("Newtonsoft.*", 1)]
    [InlineData("Newtonsoft*", 2)]
    public async Task PrefixBasicRowsUseLiteralMatchingWithoutManifests(string spelling, int count)
    {
        using var handler = new InputHandler();
        using var source = Source(handler);
        var events = await PackageQuery.ExecuteToArrayAsync(
            source, Accepted(PackageQuery.PlanInput(spelling)),
            TestContext.Current.CancellationToken);
        var matches = events.OfType<PackageQueryEvent.Match>().ToArray();
        Assert.Equal(count, matches.Length);
        Assert.All(matches, match =>
        {
            Assert.Null(match.Value.Package.Manifest);
            Assert.Equal(PackageQueryFacetTier.SearchMetadata, match.Value.Tier);
        });
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, uri => Assert.Contains("query", uri.AbsolutePath));
        Assert.Empty(events.OfType<PackageQueryEvent.Failure>());
        Assert.Equal(PackageQueryCompletionKind.Exhausted, Summary(events).Completion);
    }

    [Fact]
    public async Task PrefixMatchLimitStopsBeforeTheNextSourcePage()
    {
        using var handler = new InputHandler
        {
            SearchIds = [.. Enumerable.Range(0, 100).Select(i => $"Newtonsoft.Item{i}")],
            TotalHits = 200,
        };
        using var source = Source(handler);
        var events = await PackageQuery.ExecuteToArrayAsync(
            source,
            Accepted(PackageQuery.PlanInput("Newtonsoft.*", maximumMatches: 1)),
            TestContext.Current.CancellationToken);
        Assert.Single(events.OfType<PackageQueryEvent.Match>());
        Assert.Single(handler.Requests);
        Assert.Equal(PackageQueryCompletionKind.MatchLimitReached, Summary(events).Completion);
    }

    [Fact]
    public async Task PrefixLateSourceFailureRetainsRowsWithoutClaimingCompletion()
    {
        using var handler = new InputHandler
        {
            SearchIds = [.. Enumerable.Range(0, 100).Select(i => $"Newtonsoft.Item{i}")],
            TotalHits = 200,
            FailLaterSearchPages = true,
        };
        using var source = Source(handler);
        var events = await PackageQuery.ExecuteToArrayAsync(
            source, Accepted(PackageQuery.PlanInput("Newtonsoft.*", maximumMatches: 200)),
            TestContext.Current.CancellationToken);
        Assert.Equal(100, events.OfType<PackageQueryEvent.Match>().Count());
        Assert.Single(events.OfType<PackageQueryEvent.Failure>());
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(PackageQueryCompletionKind.Failed, Summary(events).Completion);
        Assert.Equal(100, Summary(events).Candidates);
    }

    [Fact]
    public async Task ExactResolutionReportsSourceReadyBeforeManifestWork()
    {
        using var handler = new InputHandler();
        using var source = Source(handler);
        await using var events = PackageQuery.ExecuteAsync(
            source,
            Accepted(PackageQuery.PlanInput("Newtonsoft.Json", [PackageQuery.HasDependenciesFacetId])),
            TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await events.MoveNextAsync());
        Assert.Equal(0, Assert.IsType<PackageQueryEvent.Progress>(events.Current).Value.Completed);
        Assert.Empty(handler.Requests);
        Assert.True(await events.MoveNextAsync());
        PackageQueryProgress progress = Assert.IsType<PackageQueryEvent.Progress>(events.Current).Value;
        Assert.Equal(PackageQueryProgressPhase.Search, progress.Phase);
        Assert.Equal(1, progress.Completed);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath.EndsWith(".nuspec", StringComparison.Ordinal));
    }

    static PackageQueryPlan Accepted(PackageQueryPlanResult result) =>
        Assert.IsType<PackageQueryPlanResult.Accepted>(result).Plan;

    static PackageQuerySummary Summary(IEnumerable<PackageQueryEvent> events) =>
        Assert.Single(events.OfType<PackageQueryEvent.Completed>()).Value;

    static INuGetGalleryPackageSourceClient Source(HttpMessageHandler handler) =>
        PackageSourceClientFactory.CreateGallery(PackageSourceAssociation.Create(), handler);

    sealed record VersionEntry(string Version, bool Listed);

    sealed class InputHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public bool Exists { get; init; } = true;
        public bool RegistrationAvailable { get; init; } = true;
        public VersionEntry[] Versions { get; init; } =
            [new("1.0.0", true), new("2.0.0", false), new("3.0.0-preview.1", true)];
        public string[] SearchIds { get; init; } = ["Newtonsoft.Json", "NewtonsoftOther"];
        public int TotalHits { get; init; } = 2;
        public bool FailLaterSearchPages { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Uri uri = request.RequestUri!;
            Requests.Add(uri);
            if (uri.AbsolutePath.Contains("registration", StringComparison.Ordinal))
            {
                return Task.FromResult(RegistrationAvailable
                    ? Json(new
                    {
                        items = new[]
                        {
                            new
                            {
                                items = Versions.Select(version => new
                                {
                                    catalogEntry = new
                                    {
                                        id = "Newtonsoft.Json",
                                        version = version.Version,
                                        listed = version.Listed,
                                    },
                                }),
                            },
                        },
                    })
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
            }
            if (uri.AbsolutePath.EndsWith(".nuspec", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        <package><metadata><id>Newtonsoft.Json</id><version>1.0.0</version>
                        <authors>Fixture</authors><description>Fixture package.</description>
                        <dependencies><dependency id="Example.Dependency" version="1.0.0" /></dependencies>
                        </metadata></package>
                        """, Encoding.UTF8, "application/xml"),
                });
            }
            if (uri.AbsolutePath.Contains("flatcontainer", StringComparison.Ordinal))
            {
                return Task.FromResult(Exists
                    ? Json(new { versions = Versions.Select(version => version.Version) })
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
            }
            if (uri.AbsolutePath.Contains("query", StringComparison.Ordinal))
            {
                bool firstPage = uri.Query.Contains("skip=0&", StringComparison.Ordinal);
                if (!firstPage && FailLaterSearchPages)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
                string[] ids = firstPage ? SearchIds : [];
                return Task.FromResult(Json(new
                {
                    totalHits = TotalHits,
                    data = ids.Select(id => new
                    {
                        id,
                        version = "1.0.0",
                        description = "Search fixture.",
                        owners = new[] { "Fixture" },
                        totalDownloads = 100,
                        verified = true,
                    }),
                }));
            }
            throw new InvalidOperationException($"Unexpected fixture request: {uri}");
        }

        static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json"),
            };
    }
}
