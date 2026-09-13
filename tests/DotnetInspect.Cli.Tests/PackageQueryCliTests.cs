using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using InertText;
using NuGetFetch;
using NuGetFetch.Plugins;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class PackageQueryCliTests
{
    internal static PackageQueryMatch ContainmentMatch(string text)
    {
        using var source = Source(out _);
        return new(
            new PackageQueryPackage(text, text, [], null, null, source.Source),
            PackageQueryFacetTier.Nuspec,
            [new(PackageQuery.VerifiedFacetId, new InertString(TextPolicy.Field, text))]);
    }

    [Fact]
    public void DiscoveryValues_LowerToTheInitialToolFacetSet()
    {
        string[] expected =
        {
            PackageQuery.ToolFacetId,
            PackageQuery.ToolV1FacetId,
            PackageQuery.ToolV2FacetId,
        };
        Assert.Equal(expected, PackageQueryOptions.QueryFacet.Values);
        foreach (string facet in expected)
        {
            Assert.True(
                PackageQueryOptions.TryCreate(
                    "Contoso.*",
                    [$"facet={facet}"],
                    nuspecOnly: false,
                    take: null,
                    rowSelection: null,
                    includePrerelease: false,
                    out PackageQueryOptions? options,
                    out OptionError error),
                error.ToString());
            Assert.Equal(
                facet,
                Assert.Single(options!.Plan.Facets).Id);
            Assert.Equal(
                PackageQuery.MaximumPackageContentCandidates,
                options.Plan.MaximumCandidates);
        }
    }

    [Theory]
    [InlineData("facet!=package.query.dotnet-tool", "supports --where")]
    [InlineData("downloads>=1000000", "supports --where")]
    [InlineData("facet=package.query.unknown", "not available")]
    [InlineData("", "Empty")]
    public void InvalidSelections_FailBeforeExecution(string expression, string message)
    {
        Assert.False(PackageQueryOptions.TryCreate(
            "Contoso.*",
            [expression],
            nuspecOnly: false,
            take: null,
            rowSelection: null,
            includePrerelease: false,
            out PackageQueryOptions? options,
            out OptionError error));
        Assert.Null(options);
        Assert.Contains(message, error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NuspecOnly_RejectsPackageContentFacets()
    {
        Assert.False(PackageQueryOptions.TryCreate(
            "Contoso.*",
            [$"facet={PackageQuery.ToolFacetId}"],
            nuspecOnly: true,
            take: null,
            rowSelection: null,
            includePrerelease: false,
            out PackageQueryOptions? options,
            out OptionError error));
        Assert.Null(options);
        Assert.Contains("cannot be combined with --nuspec-only", error.ToString());
    }

    [Fact]
    public void NuspecOnly_AllowsMetadataOnlyQuery()
    {
        Assert.True(PackageQueryOptions.TryCreate(
            "Contoso.*",
            [],
            nuspecOnly: true,
            take: null,
            rowSelection: null,
            includePrerelease: false,
            out PackageQueryOptions? options,
            out OptionError error),
            error.ToString());
        Assert.Empty(options!.Plan.Facets);
        Assert.Equal(
            PackageQuery.DefaultMaximumCandidates,
            options.Plan.MaximumCandidates);
    }

    [Fact]
    public void ProductPlanner_OwnsCompatibilityAndDuplicateRejection()
    {
        Assert.False(PackageQueryOptions.TryCreate("Contoso.*",
            ["facet=package.query.dotnet-tool", "facet=package.query.dotnet-tool"],
            false, null, null, false, out _, out _));
        Assert.True(PackageQueryOptions.TryCreate("Contoso.*",
            ["facet=package.query.dotnet-tool-v1", "facet=package.query.dotnet-tool-v2"],
            false, null, null, false, out var options, out var error), error.ToString());
        Assert.Equal(2, options!.Plan.Facets.Length);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1001, false)]
    [InlineData(21, true)]
    public void InvalidCandidateBudgets_AreRejected(int take, bool toolFacet)
    {
        string[] facets = toolFacet
            ? [$"facet={PackageQuery.ToolFacetId}"]
            : [];
        Assert.False(PackageQueryOptions.TryCreate(
            "Contoso.*",
            facets,
            false,
            take,
            null,
            false,
            out _,
            out _));
    }

    [Fact]
    public void CliPlan_PreservesAbsentMatchBudget()
    {
        Assert.True(PackageQueryOptions.TryCreate(
            "Contoso.*",
            [],
            false,
            300,
            null,
            false,
            out var options,
            out var error),
            error.ToString());
        Assert.Equal(300, options!.Plan.MaximumCandidates);
        Assert.Null(options.Plan.MaximumMatches);
    }

    [Fact]
    public void SemanticHeadWithoutTake_BoundsDirectRowsAndMatches()
    {
        RowSelectionIntent<string> head = RowSelectionIntent<string>.Create(
            [RowSelectionIntentOperation<string>.Head(2)]);
        Assert.True(PackageQueryOptions.TryCreate(
            "Contoso.*",
            [],
            nuspecOnly: false,
            take: null,
            rowSelection: head,
            includePrerelease: false,
            out var options,
            out var error),
            error.ToString());
        Assert.Equal(2, options!.Plan.MaximumCandidates);
        Assert.Equal(2, options.Plan.MaximumMatches);
        Assert.True(options.SemanticHeadPushedDown);
    }

    [Fact]
    public void SemanticHeadWithFacet_BoundsMatchesWithinContentCandidateCeiling()
    {
        RowSelectionIntent<string> head = RowSelectionIntent<string>.Create(
            [RowSelectionIntentOperation<string>.Head(2)]);
        Assert.True(PackageQueryOptions.TryCreate(
            "Contoso.*",
            [$"facet={PackageQuery.ToolFacetId}"],
            nuspecOnly: false,
            take: null,
            rowSelection: head,
            includePrerelease: false,
            out var options,
            out var error),
            error.ToString());
        Assert.Equal(
            PackageQuery.MaximumPackageContentCandidates,
            options!.Plan.MaximumCandidates);
        Assert.Equal(2, options.Plan.MaximumMatches);
        Assert.True(options.SemanticHeadPushedDown);
    }

    [Fact]
    public void ExplicitTake_PreventsSemanticHeadPushdown()
    {
        RowSelectionIntent<string> head = RowSelectionIntent<string>.Create(
            [RowSelectionIntentOperation<string>.Head(2)]);
        Assert.True(PackageQueryOptions.TryCreate(
            "Contoso.*",
            [],
            nuspecOnly: false,
            take: 100,
            rowSelection: head,
            includePrerelease: false,
            out var options,
            out var error),
            error.ToString());
        Assert.Equal(100, options!.Plan.MaximumCandidates);
        Assert.Null(options.Plan.MaximumMatches);
        Assert.False(options.SemanticHeadPushedDown);
    }

    [Theory]
    [InlineData("Contoso.*", PackageQueryOptions.MaximumCandidates)]
    [InlineData("Contoso.First", 1)]
    public void SemanticHeadAboveWorkLimit_RemainsSemanticOnly(
        string input,
        int expectedCandidateLimit)
    {
        RowSelectionIntent<string> head = RowSelectionIntent<string>.Create(
            [RowSelectionIntentOperation<string>.Head(1_001)]);
        Assert.True(PackageQueryOptions.TryCreate(
            input,
            [],
            nuspecOnly: false,
            take: null,
            rowSelection: head,
            includePrerelease: false,
            out var options,
            out var error),
            error.ToString());
        Assert.Equal(
            expectedCandidateLimit,
            options!.Plan.MaximumCandidates);
        Assert.Null(options.Plan.MaximumMatches);
        Assert.False(options.SemanticHeadPushedDown);
    }

    [Fact]
    public async Task SemanticHeadPushdown_StopsAtRequestedRowsWithoutWarning()
    {
        RowSelectionIntent<string> head = RowSelectionIntent<string>.Create(
            [RowSelectionIntentOperation<string>.Head(2)]);
        Assert.True(PackageQueryOptions.TryCreate(
            "Contoso.*",
            [],
            nuspecOnly: false,
            take: null,
            rowSelection: head,
            includePrerelease: false,
            out var options,
            out var error),
            error.ToString());

        using var source = Source(out _);
        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(
                options! with
                {
                    RowSelection = head,
                    Tabular = true,
                    Tsv = true,
                },
                source,
                null));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Contoso.First", result.Output);
        Assert.Contains("Contoso.Second", result.Output);
        Assert.DoesNotContain("Contoso.Third", result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task NuspecOnly_RejectsContentFacetBeforeAcquisition()
    {
        var result = await Run(
            "package",
            "query",
            "Contoso.*",
            "--nuspec-only",
            "--where",
            $"facet={PackageQuery.ToolFacetId}");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("cannot be combined with --nuspec-only", result.Error);
    }

    [Theory]
    [InlineData("--where", "facet=package.query.dotnet-tool")]
    [InlineData("--take", "20")]
    [InlineData("--nuspec-only", null)]
    public async Task QueryDiscovery_RejectsExecutionGestures(string flag, string? value)
    {
        var result = await Run(
            ["package", "query", "-Q", "Packages", flag,
                .. value is null ? [] : new[] { value }]);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("does not execute", result.Error);
    }

    [Fact]
    public async Task PatternlessFindPrefix_UsesPackageQueryGuidance()
    {
        var result = await Run("find", "--package-prefix", "Contoso.");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("requires a type or member pattern", result.Error);
        Assert.Contains("package query", result.Error);
        Assert.Empty(result.Output);
    }

    [Theory]
    [InlineData("--where", "facet=package.query.dotnet-tool")]
    [InlineData("-S", "Packages")]
    public async Task ApiFindRejectsPackageQuerySelectors(
        string option,
        string value)
    {
        var result = await Run(
            "find",
            "JsonDocument",
            "--platform",
            option,
            value);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Unrecognized command or argument", result.Error);
        Assert.Contains(option, result.Error);
    }
    [Fact]
    public async Task RemovedPackageSearch_UsesPackageQueryGuidance()
    {
        var result = await Run("package", "search", "Contoso");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("'package search' has been removed", result.Error);
        Assert.Contains("package query", result.Error);
    }

    [Fact]
    public async Task PackageQueryRejectsSourceOverridesBeforeAcquisition()
    {
        var result = await Run(
            "package",
            "query",
            "Contoso.*",
            "--source",
            "https://example.invalid/index.json");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("NuGet.org", result.Error);
    }

    [Fact]
    public async Task DataDiscovery_UsesPackageQuerySchemaWithoutAcquisition()
    {
        var query = await Run(
            "package",
            "query",
            "-D",
            "Packages",
            "--json");
        Assert.Equal(0, query.ExitCode);
        Assert.Contains("Evidence", query.Output);
    }

    [Fact]
    public async Task SemanticHeadRunsAfterAllCandidatesAndKeepsOnePackagePerRow()
    {
        using var source = Source(out var fixture);
        var result = await ConsoleCapture.RunAsync(() => PackageQueryCommand.ExecuteAsync(
            Options(PackageQuery.HasDependenciesFacetId) with
            {
                RowSelection = RowSelectionIntent<string>.Create(
                    [RowSelectionIntentOperation<string>.Head(1)]),
            },
            source,
            null));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Contoso.Second", result.Output);
        Assert.DoesNotContain("Contoso.First", result.Output);
        Assert.DoesNotContain("Contoso.Third", result.Output);
        Assert.Equal(3, fixture.ManifestRequests);
        Assert.Equal(0, fixture.PackageRequests);
        Assert.Empty(result.Error);
        Assert.Equal(2, result.Output.TrimEnd().Split('\n').Length);
    }

    [Fact]
    public async Task SemanticHead_PreservesALaterCandidateFailure()
    {
        using var source = Source(out var fixture);
        fixture.MissingManifest = "contoso.third";

        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(
                Options(PackageQuery.HasDependenciesFacetId) with
                {
                    RowSelection = Head(1),
                },
                source,
                null));

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(3, fixture.ManifestRequests);
        Assert.Contains("Contoso.Second", result.Output);
        Assert.DoesNotContain("Contoso.Third", result.Output);
        Assert.Contains("ManifestAcquisition", result.Error);
    }

    [Theory]
    [InlineData("markdown")]
    [InlineData("tsv")]
    [InlineData("jsonl")]
    [InlineData("json")]
    [InlineData("count")]
    public async Task OutputModes_UseTheSameWindowedMatches(string format)
    {
        using var source = Source(out _);
        var options = Options(PackageQuery.HasDependenciesFacetId) with
        {
            RowSelection = RowSelectionIntent<string>.Create(
                [RowSelectionIntentOperation<string>.Head(1)]),
            Count = format == "count",
            Tabular = format is "tsv" or "jsonl",
            Tsv = format == "tsv",
            Jsonl = format == "jsonl",
            JsonOutput = format == "json",
        };
        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(options, source, null));
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        if (format == "count")
        {
            Assert.Equal("1", result.Output.Trim());
            return;
        }
        Assert.Contains("Contoso.Second", result.Output);
        Assert.DoesNotContain("Contoso.Third", result.Output);
        if (format == "json")
        {
            using var json = JsonDocument.Parse(result.Output);
            Assert.Single(json.RootElement.GetProperty("packages").EnumerateArray());
        }
        if (format == "jsonl")
        {
            using var json = JsonDocument.Parse(result.Output);
            Assert.Equal("1.0.0", json.RootElement.GetProperty("version").GetString());
            Assert.NotEmpty(json.RootElement.GetProperty("evidence").GetString()!);
        }
    }

    [Fact]
    public async Task CandidateBudget_StopsBeforeAFilteredMatchAndDisclosesTheBoundary()
    {
        using var source = Source(out var fixture);
        PackageQueryOptions query = Options(
            PackageQuery.HasDependenciesFacetId,
            maximumCandidates: 1);
        var result = await ConsoleCapture.RunAsync(() => PackageQueryCommand.ExecuteAsync(
            query with { Count = true },
            source, null));
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal(1, fixture.ManifestRequests);
        Assert.Contains("Cannot count Package Query rows", result.Error);
        Assert.Contains("CandidateLimitReached", result.Error);
    }

    [Fact]
    public async Task StrictWindowFailure_RetainsCandidateBoundDisclosure()
    {
        using var source = Source(out var fixture);
        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(
                Options(
                    PackageQuery.HasDependenciesFacetId,
                    maximumCandidates: 1) with
                {
                    RowSelection = RowSelectionIntent<string>.Create(
                        [RowSelectionIntentOperation<string>.Window(1, 2)]),
                },
                source,
                null));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal(1, fixture.ManifestRequests);
        Assert.Contains(
            "Package Query row selection stage 1",
            result.Error);
        Assert.Contains("CandidateLimitReached", result.Error);
    }

    [Fact]
    public async Task PartialManifestFailure_RetainsMatchesAndNonzeroExit()
    {
        using var source = Source(out var fixture);
        fixture.MissingManifest = "contoso.third";
        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(Options(PackageQuery.HasDependenciesFacetId), source, null));
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Contoso.Second", result.Output);
        Assert.Contains("ManifestAcquisition", result.Error);
        Assert.DoesNotContain("Contoso.Third", result.Output);
    }

    [Fact]
    public async Task EmptySuccessAndSearchFailureRemainDistinct()
    {
        using var source = Source(out var fixture);
        var options = Options(PackageQuery.VerifiedFacetId) with { Count = true };
        var empty = await ConsoleCapture.RunAsync(() => PackageQueryCommand.ExecuteAsync(options, source, null));
        Assert.Equal(0, empty.ExitCode);
        Assert.Equal("0", empty.Output.Trim());
        Assert.Empty(empty.Error);
        fixture.SearchFails = true;
        var failed = await ConsoleCapture.RunAsync(() => PackageQueryCommand.ExecuteAsync(options, source, null));
        Assert.Equal(1, failed.ExitCode);
        Assert.Contains("Cannot count Package Query rows", failed.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContentProvider_UsesAdmittedArchiveAndDisposesTransport(bool invalidArchive)
    {
        using var source = Source(out var fixture);
        fixture.InvalidArchive = invalidArchive;
        using var operation = new NuGetOperationContext();
        await using var provider = ContentProvider(fixture, operation);
        PackageQueryOptions query = Options(
            PackageQuery.NoDependenciesFacetId,
            PackageQuery.EmbeddedSkillFacetId);
        var result = await ConsoleCapture.RunAsync(() => PackageQueryCommand.ExecuteAsync(
            query, source, provider));
        Assert.Equal(invalidArchive ? 1 : 0, result.ExitCode);
        Assert.Equal(1, fixture.PackageRequests);
        Assert.True(fixture.Payload!.Disposed);
        if (invalidArchive)
            Assert.Contains("PackageContentAcquisition", result.Error);
        else
        {
            Assert.Contains("Contoso.First", result.Output);
            Assert.Contains("PackageContent", result.Output);
            Assert.DoesNotContain("Contoso.Second", result.Output);
        }
    }

    [Fact]
    public async Task ContentProvider_RetainsAuthorityStorageThroughUseAndThenCleansIt()
    {
        using var source = Source(out var fixture);
        using var operation = new NuGetOperationContext();
        string root;
        await using (var provider = ContentProvider(fixture, operation))
        {
            var package = new PackageQueryPackage("Contoso.First", "1.0.0", [], null, null, source.Source);
            var result = Assert.IsType<PackageQueryContentResult.Available>(
                await provider.GetContentAsync(package, CancellationToken.None));
            root = Assert.IsType<string>(result.Content.RootPath);
            Assert.True(Directory.Exists(root));
        }
        Assert.False(Directory.Exists(root));
        Assert.True(fixture.Payload!.Disposed);
    }

    [Fact]
    public async Task CancellationDoesNotBecomeAnEmptySuccess()
    {
        using var source = Source(out _);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PackageQueryCommand.ExecuteAsync(Options(PackageQuery.HasDependenciesFacetId),
                source, null, cancellation.Token));
    }

    private static PackageQueryOptions Options(
        string facet,
        int? maximumCandidates = null) =>
        Options([facet], maximumCandidates);

    private static PackageQueryOptions Options(
        string firstFacet,
        string secondFacet,
        int? maximumCandidates = null) =>
        Options([firstFacet, secondFacet], maximumCandidates);

    private static PackageQueryOptions Options(
        IReadOnlyCollection<string> facets,
        int? maximumCandidates)
    {
        bool requiresContent = PackageQuery.Facets.Any(facet =>
            facets.Contains(facet.Id)
            && facet.Tier == PackageQueryFacetTier.PackageContent);
        int candidateLimit = maximumCandidates
            ?? (requiresContent
                ? PackageQuery.MaximumPackageContentCandidates
                : PackageQuery.DefaultMaximumCandidates);
        PackageQueryPlanResult result = PackageQuery.PlanInput(
            "Contoso.*",
            facets,
            candidateLimit,
            maximumMatches: null);
        var accepted = Assert.IsType<PackageQueryPlanResult.Accepted>(result);
        return new()
        {
            Plan = accepted.Plan,
            Tabular = true,
            Tsv = true,
        };
    }

    private static RowSelectionIntent<string> Head(int count) =>
        RowSelectionIntent<string>.Create(
            [RowSelectionIntentOperation<string>.Head(count)]);

    private static Task<(int ExitCode, string Output, string Error)> Run(params string[] args) =>
        ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
            return CommandLineBuilder.InvokeAsync(root.Parse(processed), processed);
        });

    private static IPackageSourceClient Source(out FakeSource fixture)
    {
        FakeSource? created = null;
        var source = PackageSourceClientFactory.CreateCustom(
            PackageSourceDescriptor.NuGetGallery, PackageSourceAssociation.Create(),
            factory => created = new FakeSource(factory));
        fixture = created!;
        return source;
    }

    private static PackageQueryCommand.ContentProvider ContentProvider(
        FakeSource fixture, NuGetOperationContext operation) =>
        new(new DesktopPackageSourceComposition(
            TimeSpan.FromSeconds(10), new UnavailableCredentials(),
            (_, _) => new PayloadHandler(fixture)), operation);

    private sealed class UnavailableCredentials : ICredentialSource
    {
        public bool HasCredentialSources => false;
        public Task<PackageSourceCredential?> GetCredentialsAsync(
            Uri uri, bool isRetry, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The public Gallery fixture does not require credentials.");
    }

    private sealed class PayloadHandler(FakeSource fixture) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("globalcdn.nuget.org", request.RequestUri!.Host);
            Assert.Equal("/packages/contoso.first.1.0.0.nupkg", request.RequestUri.AbsolutePath);
            var result = await fixture.GetPackageAsync("Contoso.First", "1.0.0", cancellationToken);
            return new(System.Net.HttpStatusCode.OK) { Content = new StreamContent(result.Value!.Content) };
        }
    }

    private sealed class FakeSource(PackageSourceResultFactory results) : IPackageSourceClient
    {
        public int ManifestRequests { get; private set; }
        public int PackageRequests { get; private set; }
        public string? MissingManifest { get; set; }
        public bool SearchFails { get; set; }
        public bool InvalidArchive { get; set; }
        public TrackedStream? Payload { get; private set; }
        public PackageSourceResultIdentity Source => results.Source;
        public PackageSourceCapabilities Capabilities => PackageSourceCapabilities.Search
            | PackageSourceCapabilities.Manifest | PackageSourceCapabilities.PackagePayload;

        public Task<PackageSourceOperationResult<PackageSearchResult>> SearchByPrefixAsync(
            string prefix, int take = 100, bool prerelease = false,
            CancellationToken cancellationToken = default, NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SearchResult[] rows = [new("Contoso.First", "1.0.0"), new("Contoso.Second", "1.0.0"), new("Contoso.Third", "1.0.0")];
            return Task.FromResult(SearchFails ? results.FailedSearch(PackageSourceFailureKind.Transport)
                : results.SucceededSearch(results.Search([.. rows.Take(take)],
                    take < rows.Length ? PackageSearchTruncationReason.RequestedLimit : PackageSearchTruncationReason.None)));
        }

        public Task<PackageSourceOperationResult<PackageSourceManifest>> GetManifestAsync(
            string packageId, string version, CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManifestRequests++;
            var coordinate = PackageSourceCoordinate.Create(packageId, version);
            return Task.FromResult(coordinate.PackageId == MissingManifest
                ? results.FailedManifest(coordinate, PackageSourceFailureKind.NotFound)
                : results.SucceededManifest(coordinate, results.Manifest(coordinate, Manifest(packageId))));
        }

        public Task<PackageSourceOperationResult<PackageSourcePayload>> GetPackageAsync(
            string packageId, string version, CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageRequests++;
            using var bytes = new MemoryStream();
            if (!InvalidArchive)
            {
                using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
                {
                    using (var manifest = zip.CreateEntry($"{packageId}.nuspec").Open())
                        manifest.Write(Manifest(packageId));
                    using var skill = new StreamWriter(zip.CreateEntry("skills/example/SKILL.md").Open());
                    skill.Write("A package skill.");
                }
            }
            Payload = new TrackedStream(bytes.ToArray());
            var coordinate = PackageSourceCoordinate.Create(packageId, version);
            return Task.FromResult(results.SucceededPackage(coordinate,
                results.Payload(coordinate, PackageSourcePayloadKind.Package, Payload, Payload.Length)));
        }

        private static byte[] Manifest(string id)
        {
            string dependencies = id.Equals("Contoso.First", StringComparison.OrdinalIgnoreCase) ? ""
                : "<dependency id=\"Dependency.One\" version=\"1.0.0\"/><dependency id=\"Dependency.Two\" version=\"2.0.0\"/>";
            return Encoding.UTF8.GetBytes($"""
                <package><metadata><id>{id}</id><version>1.0.0</version><authors>Contoso</authors>
                <description>CLI query fixture</description><dependencies>{dependencies}</dependencies>
                </metadata></package>
                """);
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(string query, int take = 20,
            bool prerelease = false, CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) => throw new NotSupportedException();
        public Task<PackageSourceOperationResult<PackageVersionResult>> GetVersionsAsync(string packageId,
            CancellationToken cancellationToken = default, NuGetOperationContext? operationContext = null) => throw new NotSupportedException();
        public Task<PackageSourceOperationResult<PackageSourcePayload>> TryGetSymbolsAsync(string packageId,
            string version, CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) => throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class TrackedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
