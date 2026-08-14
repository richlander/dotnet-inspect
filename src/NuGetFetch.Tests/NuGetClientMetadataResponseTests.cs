using System.Net;
using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

public class NuGetClientMetadataResponseTests
{
    private static readonly HttpRequestOptionsKey<bool> BrowserStreamingResponse =
        new("WebAssemblyEnableStreamingResponse");

    [Fact]
    public async Task GetVersionsAsync_AdvertisedOversizeBody_Throws()
    {
        var handler = new OversizeResponseHandler();
        using var client = new HttpClient(handler);
        var nuget = new NuGetClient(client);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => nuget.GetVersionsAsync(
                "Contoso.Package",
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(handler.StreamingRequested);
    }

    [Fact]
    public async Task GetPackageBaseAddressAsync_AdvertisedOversizeBody_Throws()
    {
        var handler = new OversizeResponseHandler();
        using var client = new HttpClient(handler);
        var nuget = new NuGetClient(client);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => nuget.GetPackageBaseAddressAsync(
                "https://feed.example/v3/index.json",
                TestContext.Current.CancellationToken));
        Assert.True(handler.StreamingRequested);
    }

    [Fact]
    public async Task GetLatestVersionAsync_AdvertisedOversizeBody_Throws()
    {
        var handler = new OversizeResponseHandler();
        using var client = new HttpClient(handler);
        var nuget = new NuGetClient(client);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => nuget.GetLatestVersionAsync(
                "Contoso.Package",
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(handler.StreamingRequested);
    }

    private sealed class OversizeResponseHandler : HttpMessageHandler
    {
        public bool StreamingRequested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            StreamingRequested = request.Options.TryGetValue(
                BrowserStreamingResponse,
                out bool enabled)
                && enabled;
            var content = new StringContent("{}");
            content.Headers.ContentLength = NuGetApi.MaxMetadataResponseBytes + 1;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content,
                    RequestMessage = request,
                });
        }
    }
}
