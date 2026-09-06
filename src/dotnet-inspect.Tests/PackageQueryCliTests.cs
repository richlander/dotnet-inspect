using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using InertText;
using NuGetFetch;
using NuGetFetch.Plugins;

namespace DotnetInspector.Tests;

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
    public void DiscoveryValues_LowerToExactlyTheProductFacets()
    {
        Assert.Equal(PackageQuery.Facets.Select(facet => facet.Id),
            PackageQueryOptions.QueryFacet.Values);
        foreach (var facet in PackageQuery.Facets)
        {
            var options = Options(facet.Id, content: facet.Tier == PackageQueryFacetTier.PackageContent);
            Assert.Equal(facet.Id, Assert.Single(options.PackageQuery!.Plan.Facets).Id);
        }
    }

    [Theory]
    [InlineData("facet!=package.query.dotnet-tool", "supports --where")]
    [InlineData("downloads>=1000000", "supports --where")]
    [InlineData("facet=package.query.unknown", "Unknown")]
    [InlineData("facet=package.query.dotnet-tool-v2", "--package-content")]
    [InlineData("", "Empty")]
    public void InvalidSelections_FailBeforeExecution(string expression, string message)
    {
        Assert.False(PackageQueryOptions.TryCreate("Contoso.", [expression], false,
            null, null, false, null, out var options, out var error));
        Assert.Null(options);
        Assert.Contains(message, error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductPlanner_OwnsCompatibilityAndDuplicateRejection()
    {
        Assert.False(PackageQueryOptions.TryCreate("Contoso.",
            ["facet=package.query.has-dependencies", "facet=package.query.no-dependencies"],
            false, null, null, false, null, out _, out _));
        Assert.False(PackageQueryOptions.TryCreate("Contoso.",
            ["facet=package.query.dotnet-tool", "facet=package.query.dotnet-tool"],
            false, null, null, false, null, out _, out _));
        Assert.True(PackageQueryOptions.TryCreate("Contoso.",
            ["facet=package.query.dotnet-tool-v1", "facet=package.query.dotnet-tool-v2"],
            true, null, null, false, null, out var options, out var error), error.ToString());
        Assert.Equal(2, options!.Plan.Facets.Length);
    }

    [Theory]
    [InlineData(0, null, false)]
    [InlineData(1001, null, false)]
    [InlineData(null, 0, false)]
    [InlineData(null, 1001, false)]
    [InlineData(21, null, true)]
    public void InvalidBudgets_AreRejected(int? candidates, int? matches, bool content)
    {
        string facet = content ? PackageQuery.EmbeddedSkillFacetId : PackageQuery.VerifiedFacetId;
        Assert.False(PackageQueryOptions.TryCreate("Contoso.", [$"facet={facet}"],
            content, candidates, matches, false, null, out _, out _));
    }

    [Fact]
    public void Count_PreservesCandidateBudgetAndRejectsExplicitMatchBudget()
    {
        Assert.True(PackageQueryOptions.TryCreate("Contoso.",
            ["facet=package.query.has-dependencies"], false, 300, null, true, null,
            out var options, out var error), error.ToString());
        Assert.Equal(300, options!.Plan.MaximumCandidates);
        Assert.Equal(300, options.Plan.MaximumMatches);
        Assert.False(PackageQueryOptions.TryCreate("Contoso.",
            [], false, null, 1, true, null, out _, out error));
        Assert.Contains("--matches", error.ToString());
    }

    [Theory]
    [InlineData("--where", "facet=package.query.dotnet-tool")]
    [InlineData("--candidates", "20")]
    [InlineData("--matches", "5")]
    [InlineData("--package-content", null)]
    public async Task QueryDiscovery_RejectsExecutionGestures(string flag, string? value)
    {
        var result = await Run(["find", "-Q", "Packages", flag, .. value is null ? [] : new[] { value }]);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("does not execute", result.Error);
    }

    [Theory]
    [InlineData("Type", "--where", "facet=package.query.dotnet-tool")]
    [InlineData("Type", "--candidates", "2")]
    [InlineData("Type", "--matches", "2")]
    public async Task ApiSearchRejectsQueryGesturesBeforePrefixExpansion(
        string pattern, string flag, string value)
    {
        var result = await Run("find", pattern, "--package-prefix", "Contoso.", flag, value);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("require patternless", result.Error);
        Assert.Empty(result.Output);
    }

    [Theory]
    [InlineData("--package", "Contoso.")]
    [InlineData("--library", "/missing/query.dll")]
    [InlineData("--source", "https://example.invalid/index.json")]
    [InlineData("-t", "2")]
    public async Task UnsupportedScopesFailBeforeAcquisition(string flag, string value)
    {
        var result = await Run("find", "--package-prefix", "Contoso.",
            "--where", "facet=package.query.has-dependencies", flag, value);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.DoesNotContain("Unhandled", result.Error);
    }

    [Fact]
    public async Task DataDiscovery_UsesTheSelectedModeWithoutAcquisition()
    {
        var profile = await Run("find", "--package-prefix", "Contoso.", "-S", "Packages", "-D", "Packages", "--json");
        var query = await Run("find", "--package-prefix", "Contoso.", "-S", "Packages",
            "--where", "facet=package.query.has-dependencies", "-D", "Packages", "--json");
        Assert.Equal(0, profile.ExitCode);
        Assert.Equal(0, query.ExitCode);
        Assert.Contains("Dependency", profile.Output);
        Assert.DoesNotContain("Dependency Version", query.Output);
        Assert.Contains("Evidence", query.Output);
    }

    [Fact]
    public async Task Execution_FiltersBeforeMatchLimitAndKeepsOnePackagePerRow()
    {
        using var source = Source(out var fixture);
        var result = await ConsoleCapture.RunAsync(() => PackageQueryCommand.ExecuteAsync(
            Options(PackageQuery.HasDependenciesFacetId, matches: 1), source, null));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Contoso.Second", result.Output);
        Assert.DoesNotContain("Contoso.First", result.Output);
        Assert.DoesNotContain("Contoso.Third", result.Output);
        Assert.Equal(2, fixture.ManifestRequests);
        Assert.Equal(0, fixture.PackageRequests);
        Assert.Contains("MatchLimitReached", result.Error);
        Assert.Equal(2, result.Output.TrimEnd().Split('\n').Length);
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
            Rows = RowWindow.Head(1),
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
        Assert.True(PackageQueryOptions.TryCreate("Contoso.",
            ["facet=package.query.has-dependencies"], false, 1, null, true, null,
            out var query, out var error), error.ToString());
        var result = await ConsoleCapture.RunAsync(() => PackageQueryCommand.ExecuteAsync(
            Options(PackageQuery.HasDependenciesFacetId) with { PackageQuery = query, Count = true },
            source, null));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("0", result.Output.Trim());
        Assert.Equal(1, fixture.ManifestRequests);
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
        Assert.Contains("Search", failed.Error);
        Assert.Contains("Failed", failed.Error);
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
        Assert.True(PackageQueryOptions.TryCreate("Contoso.",
            ["facet=package.query.no-dependencies", "facet=package.query.embedded-skill"],
            true, null, null, false, null, out var query, out var error), error.ToString());
        var result = await ConsoleCapture.RunAsync(() => PackageQueryCommand.ExecuteAsync(
            Options(PackageQuery.EmbeddedSkillFacetId, content: true) with { PackageQuery = query }, source, provider));
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

    private static FindOptions Options(string facet, bool content = false, int? matches = null)
    {
        Assert.True(PackageQueryOptions.TryCreate("Contoso.", [$"facet={facet}"],
            content, null, matches, false, null, out var query, out var error), error.ToString());
        return new() { PackagePrefix = "Contoso.", PackageQuery = query, Tabular = true, Tsv = true };
    }

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
