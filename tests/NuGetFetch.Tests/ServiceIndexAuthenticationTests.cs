using System.Net;
using System.Net.Http.Headers;
using System.Text;
using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

/// <summary>
/// Covers which requests in a version lookup actually carry the source's credential.
/// </summary>
/// <remarks>
/// Resolving a package from a non-nuget.org feed takes two requests: the V3 service index, to
/// discover the <c>PackageBaseAddress</c> endpoint, and then the flat-container version index.
/// A private feed authenticates both. These tests pin which of the two the credential reaches.
/// </remarks>
public sealed class ServiceIndexAuthenticationTests
{
    private const string IndexUrl = "https://feed.example/v3/index.json";
    private const string FlatContainer = "https://feed.example/v3/flat2/";
    private const string VersionsUrl = "https://feed.example/v3/flat2/contoso/index.json";

    private static readonly PackageSourceCredential Credential = new("pat", "s3cret");

    [Fact]
    public async Task VersionIndexRequest_CarriesTheCredential()
    {
        RecordingHandler handler = new()
        {
            [IndexUrl] = ServiceIndex,
            [VersionsUrl] = """{"versions":["1.0.0","2.0.0"]}""",
        };

        NuGetClient client = new(new HttpClient(handler));

        IReadOnlyList<string> versions = await client.GetVersionsAsync(
            "contoso", IndexUrl, Credential, TestContext.Current.CancellationToken);

        Assert.Equal(["1.0.0", "2.0.0"], versions);
        Assert.Equal("pat:s3cret", handler.DecodedAuthFor(VersionsUrl));
    }

    [Fact]
    public async Task ServiceIndexRequest_CarriesTheCredential()
    {
        RecordingHandler handler = new()
        {
            [IndexUrl] = ServiceIndex,
            [VersionsUrl] = """{"versions":["1.0.0"]}""",
        };

        NuGetClient client = new(new HttpClient(handler));

        await client.GetVersionsAsync("contoso", IndexUrl, Credential, TestContext.Current.CancellationToken);

        // The request must genuinely have been made, or the missing header proves nothing.
        Assert.Contains(IndexUrl, handler.Requested);
        Assert.Equal("pat:s3cret", handler.DecodedAuthFor(IndexUrl));
    }

    [Fact]
    public async Task AuthenticatedServiceIndex_Succeeds()
    {
        RecordingHandler handler = new()
        {
            [IndexUrl] = ServiceIndex,
            [VersionsUrl] = """{"versions":["1.0.0"]}""",
        };

        NuGetClient client = new(new HttpClient(handler));

        IReadOnlyList<string> versions = await client.GetVersionsAsync(
            "contoso",
            IndexUrl,
            Credential,
            TestContext.Current.CancellationToken);

        Assert.Equal(["1.0.0"], versions);
        Assert.Equal("pat:s3cret", handler.DecodedAuthFor(IndexUrl));
    }

    [Fact]
    public async Task UnauthorizedVersionIndex_IsNotReportedAsMissingPackage()
    {
        // Only 404 means "no such package". A 401 must stay distinguishable from it, or an
        // unreadable private feed looks exactly like a typo in the package id.
        RecordingHandler handler = new()
        {
            [IndexUrl] = ServiceIndex,
        };
        handler.Unauthorized(VersionsUrl);

        NuGetClient client = new(new HttpClient(handler));

        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetVersionsAsync("contoso", IndexUrl, Credential, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task MissingPackage_ReturnsEmptyRatherThanThrowing()
    {
        RecordingHandler handler = new()
        {
            [IndexUrl] = ServiceIndex,
        };

        NuGetClient client = new(new HttpClient(handler));

        IReadOnlyList<string> versions = await client.GetVersionsAsync(
            "contoso", IndexUrl, Credential, TestContext.Current.CancellationToken);

        Assert.Empty(versions);
    }

    [Fact]
    public async Task NoncanonicalNuGetOrgHostUsesConfiguredServiceIndex()
    {
        const string Source =
            "https://globalcdn.nuget.org/private/v3/index.json";
        const string Flat =
            "https://globalcdn.nuget.org/private/flat/";
        const string Package =
            "https://globalcdn.nuget.org/private/flat/contoso/1.0.0/contoso.1.0.0.nupkg";
        RecordingHandler handler = new()
        {
            [Source] = $$"""
                {
                  "version": "3.0.0",
                  "resources": [
                    { "@id": "{{Flat}}", "@type": "PackageBaseAddress/3.0.0" }
                  ]
                }
                """,
            [Package] = "package bytes",
        };
        NuGetClient client = new(new HttpClient(handler));

        await using Stream package = await client.DownloadAsync(
            "contoso",
            "1.0.0",
            Source,
            Credential,
            TestContext.Current.CancellationToken);

        Assert.Contains(Source, handler.Requested);
        Assert.Contains(Package, handler.Requested);
        Assert.DoesNotContain(
            handler.Requested,
            url => url.StartsWith(
                NuGetClient.NuGetOrgFlatContainer,
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal("pat:s3cret", handler.DecodedAuthFor(Package));
    }

    [Fact]
    public async Task CrossOriginDiscoveredResources_DoNotCarryTheCredential()
    {
        const string CrossOriginFlat =
            "https://cdn.example/v3/flat/";
        const string CrossOriginVersions =
            "https://cdn.example/v3/flat/contoso/index.json";
        const string CrossOriginPackage =
            "https://cdn.example/v3/flat/contoso/1.0.0/contoso.1.0.0.nupkg";
        RecordingHandler handler = new()
        {
            [IndexUrl] = $$"""
                {
                  "version": "3.0.0",
                  "resources": [
                    {
                      "@id": "{{CrossOriginFlat}}",
                      "@type": "PackageBaseAddress/3.0.0"
                    }
                  ]
                }
                """,
            [CrossOriginVersions] = """{"versions":["1.0.0"]}""",
            [CrossOriginPackage] = "package bytes",
        };
        NuGetClient client = new(new HttpClient(handler));

        _ = await client.GetVersionsAsync(
            "contoso",
            IndexUrl,
            Credential,
            TestContext.Current.CancellationToken);
        await using Stream package = await client.DownloadAsync(
            "contoso",
            "1.0.0",
            IndexUrl,
            Credential,
            TestContext.Current.CancellationToken);

        Assert.Equal("pat:s3cret", handler.DecodedAuthFor(IndexUrl));
        Assert.Null(handler.AuthFor(CrossOriginVersions));
        Assert.Null(handler.AuthFor(CrossOriginPackage));
    }

    [Theory]
    [InlineData(
        "https://feed.example/v3/index.json",
        "https://feed.example:8443/v3/flat/")]
    [InlineData(
        "https://feed.example:8443/v3/index.json",
        "http://feed.example:8443/v3/flat/")]
    public async Task DifferentOriginComponents_DoNotCarryTheCredential(
        string source,
        string flatContainer)
    {
        string versions =
            $"{flatContainer}contoso/index.json";
        RecordingHandler handler = new()
        {
            [source] = $$"""
                {
                  "version": "3.0.0",
                  "resources": [
                    {
                      "@id": "{{flatContainer}}",
                      "@type": "PackageBaseAddress/3.0.0"
                    }
                  ]
                }
                """,
            [versions] = """{"versions":["1.0.0"]}""",
        };
        NuGetClient client = new(new HttpClient(handler));

        _ = await client.GetVersionsAsync(
            "contoso",
            source,
            Credential,
            TestContext.Current.CancellationToken);

        Assert.Equal("pat:s3cret", handler.DecodedAuthFor(source));
        Assert.Null(handler.AuthFor(versions));
    }

    [Fact]
    public async Task IdnEquivalentOriginCarriesTheCredential()
    {
        const string UnicodeIndex =
            "https://bücher.example/v3/index.json";
        const string PunycodeIndex =
            "https://xn--bcher-kva.example/v3/index.json";
        const string PunycodeFlat =
            "https://xn--bcher-kva.example/v3/flat/";
        const string PunycodeVersions =
            "https://xn--bcher-kva.example/v3/flat/contoso/index.json";
        RecordingHandler handler = new()
        {
            [PunycodeIndex] = $$"""
                {
                  "version": "3.0.0",
                  "resources": [
                    {
                      "@id": "{{PunycodeFlat}}",
                      "@type": "PackageBaseAddress/3.0.0"
                    }
                  ]
                }
                """,
            [PunycodeVersions] = """{"versions":["1.0.0"]}""",
        };
        NuGetClient client = new(new HttpClient(handler));

        _ = await client.GetVersionsAsync(
            "contoso",
            UnicodeIndex,
            Credential,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "pat:s3cret",
            handler.DecodedAuthFor(PunycodeVersions));
        Assert.Equal(
            "pat:s3cret",
            handler.DecodedAuthFor(PunycodeIndex));
    }

    private const string ServiceIndex = $$"""
        {
          "version": "3.0.0",
          "resources": [
            { "@id": "{{FlatContainer}}", "@type": "PackageBaseAddress/3.0.0" }
          ]
        }
        """;

    /// <summary>
    /// Serves canned bodies by URL and records the Authorization header of every request.
    /// Unknown URLs return 404; URLs registered via <see cref="Unauthorized"/> return 401.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _routes = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unauthorized = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string Url, AuthenticationHeaderValue? Auth)> _requests = [];

        public string this[string url] { set => _routes[url] = value; }

        public void Unauthorized(string url) => _unauthorized.Add(url);

        public IReadOnlyList<string> Requested => _requests.Select(r => r.Url).ToList();

        public AuthenticationHeaderValue? AuthFor(string url) =>
            _requests.FirstOrDefault(r => r.Url.Equals(url, StringComparison.OrdinalIgnoreCase)).Auth;

        public string? DecodedAuthFor(string url)
        {
            AuthenticationHeaderValue? header = AuthFor(url);

            return header?.Parameter is null
                ? null
                : Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            _requests.Add((url, request.Headers.Authorization));

            HttpStatusCode status = _unauthorized.Contains(url)
                ? HttpStatusCode.Unauthorized
                : _routes.ContainsKey(url) ? HttpStatusCode.OK : HttpStatusCode.NotFound;

            HttpResponseMessage response = new(status)
            {
                Content = new StringContent(status == HttpStatusCode.OK ? _routes[url] : ""),
                RequestMessage = request,
            };

            return Task.FromResult(response);
        }
    }
}
