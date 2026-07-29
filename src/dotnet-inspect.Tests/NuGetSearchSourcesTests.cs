using System.CommandLine;
using System.Net;
using System.Net.Http.Headers;
using DotnetInspector.CommandLine;
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

    [Theory]
    [InlineData("package", "Newtonsoft.Json")]
    [InlineData("package", "search", "Newtonsoft.Json")]
    [InlineData("type", "JsonSerializer", "--package", "System.Text.Json")]
    public void UnusableNuGetConfig_IsRejectedAtParseTime(params string[] leadingArgs)
    {
        // ResolveSources throws for an unusable explicit config, but only `package search` wraps
        // its invocation in a try/catch. Without a parse-time validator on the shared option, every
        // other source-aware command reports the same mistake as an unhandled stack trace. This
        // test is the gate on that wiring: it fails if the validator is removed from SharedOptions.
        string missing = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.config");

        var result = CommandLineBuilder.CreateRootCommand()
            .Parse([.. leadingArgs, "--nugetconfig", missing]);

        Assert.Contains(result.Errors, e => e.Message.Contains(missing, StringComparison.Ordinal));
    }

    [Theory]
    // Same endpoint under canonicalization the URL grammar allows.
    [InlineData("https://feed.example/v3/index.json", "https://FEED.example/v3/index.json", true)]
    [InlineData("https://feed.example/v3/", "https://feed.example/v3", true)]
    [InlineData("https://feed.example:443/v3/index.json", "https://feed.example/v3/index.json", true)]
    [InlineData("https://bücher.example/v3/index.json", "https://xn--bcher-kva.example/v3/index.json", true)]
    // Percent-escape hex digits are case-insensitive per RFC 3986, and Uri preserves whichever
    // spelling the caller wrote, so comparing raw would withhold credentials from the user's feed.
    [InlineData("https://feed.example/a%2fb/index.json", "https://feed.example/a%2Fb/index.json", true)]
    [InlineData("https://feed.example/v3/index.json?k=a%2fb", "https://feed.example/v3/index.json?k=a%2Fb", true)]
    // The escape still denotes a different character than the literal it encodes.
    [InlineData("https://feed.example/a%2fb/index.json", "https://feed.example/a/b/index.json", false)]
    // Different endpoints. Paths and queries are case-sensitive over HTTP, so folding them would
    // hand one feed's credentials to another feed on the same host.
    [InlineData("https://feed.example/FeedA/index.json", "https://feed.example/feeda/index.json", false)]
    [InlineData("https://feed.example/v3/index.json?id=A", "https://feed.example/v3/index.json?id=a", false)]
    [InlineData("https://feed.example/v3/index.json", "https://feed.example/v4/index.json", false)]
    [InlineData("https://feed.example/v3/index.json", "http://feed.example/v3/index.json", false)]
    [InlineData("https://feed.example/v3/index.json", "https://feed.example:8443/v3/index.json", false)]
    [InlineData("https://feed.example/v3/index.json", "https://feed.example.attacker.com/v3/index.json", false)]
    public void IsSameEndpoint_DistinguishesFeedsThatDifferOutsideTheOrigin(
        string a, string b, bool expected)
    {
        Assert.Equal(expected, NuGetCredentialScope.IsSameEndpoint(a, b));
    }

    [Fact]
    public void ResolveSources_ExplicitSourceMatchingConfig_AdoptsCredentialsAndKeepsTheGivenUrl()
    {
        using var config = new TempNuGetConfig(IndexUrl);

        // Same endpoint, spelled with a host case the user chose.
        string asTyped = IndexUrl.Replace("feed.example", "FEED.example", StringComparison.Ordinal);

        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(
            new NuGetSourceOptions { ConfigFile = config.Path, Sources = [asTyped] });

        NuGetSource source = Assert.Single(sources);
        Assert.NotNull(source.Credential);
        // The request must go where the user pointed it, not to the config's spelling.
        Assert.Equal(asTyped, source.Url);
    }

    [Fact]
    public void ResolveSources_ExplicitSourceOnAnotherPath_DoesNotAdoptCredentials()
    {
        using var config = new TempNuGetConfig(IndexUrl);

        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(
            new NuGetSourceOptions
            {
                ConfigFile = config.Path,
                Sources = ["https://feed.example/PRIVATE/index.json"],
            });

        Assert.Null(Assert.Single(sources).Credential);
    }

    [Fact]
    public void ResolveSources_WellFormedConfigDeclaringNoSources_ThrowsRatherThanDefaultingToNuGetOrg()
    {
        // Well-formed XML is not enough: any XML file parses, including a .csproj passed by
        // mistake, and SourceResolver would then substitute nuget.org and answer with packages
        // from a feed the user never chose.
        string path = Path.Combine(Path.GetTempPath(), $"notaconfig-{Guid.NewGuid():N}.config");
        File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup /></Project>");
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                NuGetSourceResolver.ResolveSources(new NuGetSourceOptions { ConfigFile = path }));

            Assert.Contains("no usable package sources", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResolveConfiguredSources_NoConfiguredSources_DoesNotSubstituteNuGetOrg()
    {
        string path = Path.Combine(Path.GetTempPath(), $"empty-{Guid.NewGuid():N}.config");
        File.WriteAllText(path, "<configuration><packageSources><clear /></packageSources></configuration>");
        try
        {
            Assert.Empty(NuGetFetch.SourceResolver.ResolveConfiguredSources(path));

            // The fallback still belongs to ResolveSources, which discovered configs rely on.
            Assert.Equal("nuget.org", Assert.Single(NuGetFetch.SourceResolver.ResolveSources(configPath: path)).Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DescribeConfigProblem_UnreadableConfig_ReportsRatherThanThrows()
    {
        // The caller is a parse-time validator, which runs outside every handler in Program.cs.
        // A throw here is not a reportable error, it is a process crash with a raw stack trace.
        string path = Path.Combine(Path.GetTempPath(), $"locked-{Guid.NewGuid():N}.config");
        File.WriteAllText(path, $"""
            <configuration><packageSources><add key="a" value="{IndexUrl}" /></packageSources></configuration>
            """);
        try
        {
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                string? problem = NuGetSourceResolver.DescribeConfigProblem(path);

                Assert.NotNull(problem);
                Assert.Contains("could not be read", problem, StringComparison.Ordinal);
            }

            // Same file, once the lock is gone, is usable.
            Assert.Null(NuGetSourceResolver.DescribeConfigProblem(path));
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

    /// <summary>
    /// Regression: source resolution ran only when a source option was passed, so a NuGet.Config
    /// discovered from the working directory was ignored and search silently went to nuget.org —
    /// the exact symptom issue #3417 reported for an authenticated feed configured that way.
    /// </summary>
    [Fact]
    public void ResolveSources_NoOptions_HonorsDiscoveredConfig()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"discover-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "NuGet.Config"), """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="contoso" value="https://contoso.example/v3/index.json" />
                  </packageSources>
                </configuration>
                """);

            List<PackageSource> sources = NuGetSourceResolver.ResolveSources(null, dir);

            PackageSource only = Assert.Single(sources);
            Assert.Equal("https://contoso.example/v3/index.json", only.Url);
            Assert.False(only.IsNuGetOrg);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Two configured entries differing only by a trailing slash are separate entries that may
    /// carry separate credentials. Slash tolerance decides candidacy, never authorization, so an
    /// exact spelling wins outright rather than the first slash-tolerant candidate.
    /// </summary>
    [Fact]
    public void ResolveSources_TrailingSlashAmbiguity_PrefersExactSpelling()
    {
        const string bare = "https://feed.example/v3/index.json";
        const string slashed = "https://feed.example/v3/index.json/";

        using var config = new TempNuGetConfig(
            [("bare", bare), ("slashed", slashed)], credentialedSource: "bare");

        List<PackageSource> sources = NuGetSourceResolver.ResolveSources(
            new NuGetSourceOptions { Sources = [slashed], ConfigFile = config.Path });

        PackageSource only = Assert.Single(sources);
        Assert.Equal(slashed, only.Url);
        Assert.Equal("slashed", only.Name);
        Assert.Null(only.Credential);
        Assert.Null(only.GetAuthHeader());
    }

    /// <summary>
    /// With no exact spelling to prefer, an ambiguous slash-tolerant match adopts no credential at
    /// all. Picking either candidate would be a guess that could send one entry's secret to the
    /// other's spelling.
    /// </summary>
    [Fact]
    public void ResolveSources_TrailingSlashAmbiguity_WithoutExactMatch_AdoptsNoCredential()
    {
        using var config = new TempNuGetConfig(
            [("bare", "https://feed.example/v3/index.json"),
             ("slashed", "https://feed.example/v3/index.json/")],
            credentialedSource: "bare");

        List<PackageSource> sources = NuGetSourceResolver.ResolveSources(
            new NuGetSourceOptions
            {
                Sources = ["https://feed.example/v3/index.json//"],
                ConfigFile = config.Path
            });

        PackageSource only = Assert.Single(sources);
        Assert.Equal("explicit", only.Name);
        Assert.Null(only.Credential);
        Assert.Null(only.GetAuthHeader());
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
