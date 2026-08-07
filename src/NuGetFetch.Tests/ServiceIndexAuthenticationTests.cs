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
    public async Task ServiceIndexRequest_DoesNotCarryTheCredential()
    {
        // Pins a real gap. GetPackageBaseAddressAsync takes no credential and issues a bare
        // GetStreamAsync, so the service index is always fetched anonymously. Against a feed
        // that authenticates its service index — Azure DevOps does — the lookup fails at the
        // discovery step and the credential is never even offered.
        //
        // The dotnet-inspect CLI does not hit this, because its package path does not go
        // through NuGetClient at all: PackageExtractor has its own service-index reader that
        // passes source.GetAuthHeader() on the discovery request. The gap is confined to
        // NuGetFetch, so it bites any other consumer of this library that supplies a
        // credential and reasonably expects it to be used.
        //
        // If GetPackageBaseAddressAsync learns to take a credential, this test should flip to
        // asserting the header is present.
        RecordingHandler handler = new()
        {
            [IndexUrl] = ServiceIndex,
            [VersionsUrl] = """{"versions":["1.0.0"]}""",
        };

        NuGetClient client = new(new HttpClient(handler));

        await client.GetVersionsAsync("contoso", IndexUrl, Credential, TestContext.Current.CancellationToken);

        // The request must genuinely have been made, or the missing header proves nothing.
        Assert.Contains(IndexUrl, handler.Requested);
        Assert.Null(handler.AuthFor(IndexUrl));
    }

    [Fact]
    public async Task AuthenticatedServiceIndex_FailsTheLookupEntirely()
    {
        // The consequence of the gap above, made concrete: a 401 on the service index is not
        // a "package not found" condition, and it is not swallowed here — it surfaces as an
        // HttpRequestException carrying the status. The status is therefore available to the
        // caller, which matters because the CLI currently reports this as "package not found".
        RecordingHandler handler = new()
        {
            [VersionsUrl] = """{"versions":["1.0.0"]}""",
        };
        handler.Unauthorized(IndexUrl);

        NuGetClient client = new(new HttpClient(handler));

        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetVersionsAsync("contoso", IndexUrl, Credential, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
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
