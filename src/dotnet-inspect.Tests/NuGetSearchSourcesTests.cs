using System.Net;
using System.Net.Http.Headers;
using DotnetInspector.Packages;
using NuGetFetch;
using NuGetSource = NuGetFetch.PackageSource;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for searching non-nuget.org feeds: SearchQueryService discovery from a V3 service index,
/// the origin scope that keeps feed credentials from following a feed-supplied URL, and the
/// refusal to render an unsearchable feed set as "no packages found". Issue #3417.
/// </summary>
public class NuGetSearchSourcesTests
{
    private const string IndexUrl = "https://feed.example/v3/index.json";
    private const string SearchUrl = "https://feed.example/v3/query";

    private static string ServiceIndex(string searchId) => $$"""
        {
          "version": "3.0.0",
          "resources": [
            { "@id": "https://feed.example/v3/flat2/", "@type": "PackageBaseAddress/3.0.0" },
            { "@id": "{{searchId}}", "@type": "SearchQueryService/3.5.0" }
          ]
        }
        """;

    [Fact]
    public async Task GetSearchQueryServiceAsync_DiscoversVersionedSearchResource()
    {
        var handler = new RouteHandler { [IndexUrl] = ServiceIndex(SearchUrl) };
        using var client = new HttpClient(handler);

        string? discovered = await PackageExtractor.GetSearchQueryServiceAsync(
            client, new NuGetSource("contoso", IndexUrl));

        Assert.Equal(SearchUrl, discovered);
    }

    [Fact]
    public async Task GetSearchQueryServiceAsync_FeedWithoutSearchResource_ReturnsNull()
    {
        const string flatContainerOnly = """
            { "version": "3.0.0", "resources": [
              { "@id": "https://feed.example/v3/flat2/", "@type": "PackageBaseAddress/3.0.0" } ] }
            """;
        var handler = new RouteHandler { [IndexUrl] = flatContainerOnly };
        using var client = new HttpClient(handler);

        string? discovered = await PackageExtractor.GetSearchQueryServiceAsync(
            client, new NuGetSource("contoso", IndexUrl));

        Assert.Null(discovered);
    }

    [Fact]
    public async Task GetSearchQueryServiceAsync_LocalFolderSource_ReturnsNull()
    {
        using var client = new HttpClient(new RouteHandler());

        string? discovered = await PackageExtractor.GetSearchQueryServiceAsync(
            client, new NuGetSource("local", @"D:\packages"));

        Assert.Null(discovered);
    }

    [Fact]
    public async Task SearchAsync_ExplicitSource_SearchesDiscoveredEndpoint()
    {
        var handler = new RouteHandler
        {
            [IndexUrl] = ServiceIndex(SearchUrl),
            [SearchUrl] = """{"data":[{"id":"Contoso.Internal","version":"1.2.3"}],"totalHits":"0"}"""
        };
        using var client = new HttpClient(handler);

        NuGetSearchOutcome outcome = await NuGetSearchService.SearchAsync(
            client, "Contoso", sourceOptions: new NuGetSourceOptions { Sources = [IndexUrl] });

        Assert.Empty(outcome.Failures);
        NuGetSearchResult result = Assert.Single(outcome.Results);
        Assert.Equal("Contoso.Internal", result.PackageId);
        Assert.Contains(handler.Requested, u => u.StartsWith(SearchUrl + "?", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_UnsearchableSource_ThrowsRatherThanReportingNoResults()
    {
        // Every configured source failed. An empty list here would render as
        // "No packages found", which is the opposite of what happened.
        var handler = new RouteHandler { [IndexUrl] = """{"version":"3.0.0","resources":[]}""" };
        using var client = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await NuGetSearchService.SearchAsync(
                client, "Contoso", sourceOptions: new NuGetSourceOptions { Sources = [IndexUrl] }));

        Assert.Contains("No configured NuGet source could be searched", ex.Message);
        Assert.Contains("no searchable endpoint", ex.Message);
    }

    [Fact]
    public async Task SearchAsync_MalformedSearchBody_SurfacesAsFailureNotEmptyResults()
    {
        var handler = new RouteHandler
        {
            [IndexUrl] = ServiceIndex(SearchUrl),
            [SearchUrl] = "<html>sign in</html>"
        };
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await NuGetSearchService.SearchAsync(
                client, "Contoso", sourceOptions: new NuGetSourceOptions { Sources = [IndexUrl] }));
    }

    [Fact]
    public async Task SearchAsync_OneSourceFails_ReportsFailureAlongsideResults()
    {
        const string goodIndex = "https://good.example/v3/index.json";
        const string goodSearch = "https://good.example/v3/query";
        const string badIndex = "https://bad.example/v3/index.json";

        var handler = new RouteHandler
        {
            [goodIndex] = $$"""
                { "version": "3.0.0", "resources": [
                  { "@id": "{{goodSearch}}", "@type": "SearchQueryService/3.5.0" } ] }
                """,
            [goodSearch] = """{"data":[{"id":"Good.Package","version":"1.0.0"}]}""",
            [badIndex] = """{"version":"3.0.0","resources":[]}"""
        };
        using var client = new HttpClient(handler);
        using var config = new TempNuGetConfig([("good", goodIndex), ("bad", badIndex)]);

        NuGetSearchOutcome outcome = await NuGetSearchService.SearchAsync(
            client, "q", sourceOptions: new NuGetSourceOptions { ConfigFile = config.Path });

        Assert.Single(outcome.Results);
        string failure = Assert.Single(outcome.Failures);
        Assert.Contains("bad.example", failure);
    }

    [Fact]
    public async Task SearchAsync_DuplicateAcrossSources_ReturnedOnce()
    {
        const string indexA = "https://a.example/v3/index.json";
        const string indexB = "https://b.example/v3/index.json";
        const string searchA = "https://a.example/v3/query";
        const string searchB = "https://b.example/v3/query";
        const string body = """{"data":[{"id":"Shared.Package","version":"1.0.0"}]}""";

        var handler = new RouteHandler
        {
            [indexA] = $$"""{"resources":[{"@id":"{{searchA}}","@type":"SearchQueryService"}]}""",
            [indexB] = $$"""{"resources":[{"@id":"{{searchB}}","@type":"SearchQueryService"}]}""",
            [searchA] = body,
            [searchB] = body
        };
        using var client = new HttpClient(handler);
        using var config = new TempNuGetConfig([("a", indexA), ("b", indexB)]);

        NuGetSearchOutcome outcome = await NuGetSearchService.SearchAsync(
            client, "Shared", sourceOptions: new NuGetSourceOptions { ConfigFile = config.Path });

        Assert.Empty(outcome.Failures);
        Assert.Single(outcome.Results);
    }

    [Fact]
    public async Task SearchAsync_CredentialedFeed_SendsCredentialsToSameOriginEndpoint()
    {
        using var config = new TempNuGetConfig(IndexUrl);
        var handler = new RouteHandler
        {
            [IndexUrl] = ServiceIndex(SearchUrl),
            [SearchUrl] = """{"data":[]}"""
        };
        using var client = new HttpClient(handler);

        await NuGetSearchService.SearchAsync(
            client, "q", sourceOptions: new NuGetSourceOptions { ConfigFile = config.Path });

        AuthenticationHeaderValue? sent = handler.AuthFor(SearchUrl);
        Assert.NotNull(sent);
        Assert.Equal("Basic", sent.Scheme);
    }

    [Fact]
    public async Task SearchAsync_CredentialedFeed_WithholdsCredentialsFromForeignEndpoint()
    {
        // The service index is the only thing naming the search endpoint, so a hostile or
        // compromised feed can point it anywhere. Credentials must not follow.
        const string foreignSearch = "https://attacker.example/collect";
        using var config = new TempNuGetConfig(IndexUrl);
        var handler = new RouteHandler
        {
            [IndexUrl] = ServiceIndex(foreignSearch),
            [foreignSearch] = """{"data":[]}"""
        };
        using var client = new HttpClient(handler);

        await NuGetSearchService.SearchAsync(
            client, "q", sourceOptions: new NuGetSourceOptions { ConfigFile = config.Path });

        Assert.NotNull(handler.AuthFor(IndexUrl));
        Assert.Null(handler.AuthFor(foreignSearch));
    }

    [Theory]
    [InlineData("https://feed.example/v3/index.json", "https://feed.example/v3/query", true)]
    [InlineData("https://feed.example/v3/index.json", "https://FEED.EXAMPLE/other", true)]
    [InlineData("https://feed.example/v3/index.json", "https://attacker.example/query", false)]
    [InlineData("https://feed.example/v3/index.json", "http://feed.example/query", false)]
    [InlineData("https://feed.example/v3/index.json", "https://feed.example:8443/query", false)]
    [InlineData("https://feed.example/v3/index.json", "https://sub.feed.example/query", false)]
    [InlineData("https://xn--bcher-kva.example/i.json", "https://b\u00fccher.example/query", true)]
    [InlineData("https://b\u00fccher.example/i.json", "https://xn--bcher-kva.example/query", true)]
    [InlineData("https://[::1]/i.json", "https://[0:0:0:0:0:0:0:1]/query", true)]
    [InlineData("https://feed.example/v3/index.json", null, false)]
    [InlineData("https://feed.example/v3/index.json", "/relative", false)]
    [InlineData(@"D:\packages", "https://feed.example/query", false)]
    public void IsSameOrigin_ComparesSchemeHostAndPort(string? sourceUrl, string? endpointUrl, bool expected)
    {
        Assert.Equal(expected, NuGetCredentialScope.IsSameOrigin(sourceUrl, endpointUrl));
    }

    [Fact]
    public void AuthFor_SourceWithoutCredentials_ReturnsNullWithoutReporting()
    {
        List<string> log = [];
        var source = new NuGetSource("contoso", IndexUrl);

        Assert.Null(NuGetCredentialScope.AuthFor(source, "https://attacker.example/x", log.Add));
        Assert.Empty(log);
    }

    [Fact]
    public void AuthFor_ForeignEndpoint_WithholdsAndReports()
    {
        List<string> log = [];
        var source = new NuGetSource("contoso", IndexUrl, new PackageSourceCredential("user", "pass"));

        Assert.Null(NuGetCredentialScope.AuthFor(source, "https://attacker.example/x", log.Add));
        Assert.Contains(log, m => m.Contains("Withholding credentials", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolveSources_MissingExplicitConfig_ThrowsRatherThanDefaultingToNuGetOrg()
    {
        // Silently ignoring a config the user named and searching nuget.org instead reports
        // someone else's packages as the answer, with exit code 0.
        string missing = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.config");

        var ex = Assert.Throws<FileNotFoundException>(() =>
            NuGetSourceResolver.ResolveSources(new NuGetSourceOptions { ConfigFile = missing }));

        Assert.Contains(missing, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveSources_MalformedExplicitConfig_ThrowsRatherThanDefaultingToNuGetOrg()
    {
        string path = Path.Combine(Path.GetTempPath(), $"broken-{Guid.NewGuid():N}.config");
        File.WriteAllText(path, "<configuration><packageSources>");
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                NuGetSourceResolver.ResolveSources(new NuGetSourceOptions { ConfigFile = path }));

            Assert.Contains("not valid XML", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResolveSources_ValidExplicitConfig_ReturnsItsSources()
    {
        using var config = new TempNuGetConfig(IndexUrl);

        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(
            new NuGetSourceOptions { ConfigFile = config.Path });

        NuGetSource source = Assert.Single(sources);
        Assert.Equal(IndexUrl, source.Url);
    }

    [Fact]
    public void ResolveSources_MultipleExplicitSources_ReplacesDefaults()
    {
        // --source documents itself as "replaces defaults", but more than one value used to be
        // forwarded as *additional* sources, which re-entered config resolution and searched
        // feeds the user never named. Asserting the exact set proves nothing was merged in:
        // config resolution falls back to nuget.org even on a machine with no nuget.config.
        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(new NuGetSourceOptions
        {
            Sources = ["https://a.example/v3/index.json", "https://b.example/v3/index.json"]
        });

        Assert.Equal(
            ["https://a.example/v3/index.json", "https://b.example/v3/index.json"],
            sources.Select(s => s.Url));
    }

    [Fact]
    public void ResolveSources_ExplicitSourceWithAddSource_KeepsBoth()
    {
        // SourceResolver's explicit-source fast path returned early, dropping additional sources.
        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(new NuGetSourceOptions
        {
            Sources = ["https://a.example/v3/index.json"],
            AdditionalSources = ["https://b.example/v3/index.json"]
        });

        Assert.Equal(
            ["https://a.example/v3/index.json", "https://b.example/v3/index.json"],
            sources.Select(s => s.Url));
    }

    [Fact]
    public void ResolveSources_ExplicitSource_AdoptsConfiguredCredentials()
    {
        // Naming an authenticated feed with --source must still pick up the credentials the
        // user declared for that same URL, or the feature cannot reach an authenticated feed.
        using var config = new TempNuGetConfig(IndexUrl);

        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(new NuGetSourceOptions
        {
            Sources = [IndexUrl],
            ConfigFile = config.Path
        });

        NuGetSource source = Assert.Single(sources);
        Assert.NotNull(source.GetAuthHeader());
        Assert.Equal("contoso", source.Name);
    }

    [Fact]
    public void ResolveSources_ExplicitSourceNotInConfig_HasNoCredentials()
    {
        using var config = new TempNuGetConfig(IndexUrl);

        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(new NuGetSourceOptions
        {
            Sources = ["https://unrelated.example/v3/index.json"],
            ConfigFile = config.Path
        });

        NuGetSource source = Assert.Single(sources);
        Assert.Null(source.GetAuthHeader());
    }

    [Fact]
    public async Task SearchAsync_MultipleExplicitSources_SearchesOnlyThose()
    {
        const string indexA = "https://a.example/v3/index.json";
        const string indexB = "https://b.example/v3/index.json";
        const string searchA = "https://a.example/v3/query";
        const string searchB = "https://b.example/v3/query";

        var handler = new RouteHandler
        {
            [indexA] = $$"""{"resources":[{"@id":"{{searchA}}","@type":"SearchQueryService"}]}""",
            [indexB] = $$"""{"resources":[{"@id":"{{searchB}}","@type":"SearchQueryService"}]}""",
            [searchA] = """{"data":[{"id":"A.Package","version":"1.0.0"}]}""",
            [searchB] = """{"data":[{"id":"B.Package","version":"1.0.0"}]}"""
        };
        using var client = new HttpClient(handler);

        NuGetSearchOutcome outcome = await NuGetSearchService.SearchAsync(
            client, "q", sourceOptions: new NuGetSourceOptions { Sources = [indexA, indexB] });

        Assert.Empty(outcome.Failures);
        Assert.Equal(2, outcome.Results.Count);

        // Nothing beyond the two named feeds was contacted — in particular not nuget.org.
        Assert.All(handler.Requested, url =>
            Assert.True(
                url.StartsWith("https://a.example/", StringComparison.Ordinal)
                || url.StartsWith("https://b.example/", StringComparison.Ordinal),
                $"unexpected request to {url}"));
    }

    /// <summary>Writes a nuget.config naming the given sources, and deletes it on dispose.</summary>
    private sealed class TempNuGetConfig : IDisposable
    {
        public string Path { get; }

        /// <summary>Declares one credentialed source named "contoso".</summary>
        public TempNuGetConfig(string sourceUrl)
            : this([("contoso", sourceUrl)], credentialedSource: "contoso")
        {
        }

        public TempNuGetConfig(
            IReadOnlyList<(string Name, string Url)> sources,
            string? credentialedSource = null)
        {
            string adds = string.Join(
                Environment.NewLine,
                sources.Select(s => $"""    <add key="{s.Name}" value="{s.Url}" />"""));

            string credentials = credentialedSource is null ? "" : $"""
                  <packageSourceCredentials>
                    <{credentialedSource}>
                      <add key="Username" value="user" />
                      <add key="ClearTextPassword" value="pass" />
                    </{credentialedSource}>
                  </packageSourceCredentials>
                """;

            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"nuget-{Guid.NewGuid():N}.config");
            File.WriteAllText(Path, $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                {adds}
                  </packageSources>
                {credentials}
                </configuration>
                """);
        }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
                // Best-effort cleanup of a temp file.
            }
        }
    }

    /// <summary>
    /// Serves canned bodies by URL (ignoring the query string) and records what was asked for,
    /// including the Authorization header each request carried. Unknown URLs return 404.
    /// </summary>
    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _routes = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string Url, AuthenticationHeaderValue? Auth)> _requests = [];

        public string this[string url] { set => _routes[url] = value; }

        public IReadOnlyList<string> Requested => _requests.Select(r => r.Url).ToList();

        public AuthenticationHeaderValue? AuthFor(string url) =>
            _requests.FirstOrDefault(r => WithoutQuery(r.Url).Equals(url, StringComparison.OrdinalIgnoreCase)).Auth;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            _requests.Add((url, request.Headers.Authorization));

            HttpResponseMessage response = _routes.TryGetValue(WithoutQuery(url), out string? body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") };

            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        private static string WithoutQuery(string url)
        {
            int q = url.IndexOf('?', StringComparison.Ordinal);
            return q < 0 ? url : url[..q];
        }
    }
}
