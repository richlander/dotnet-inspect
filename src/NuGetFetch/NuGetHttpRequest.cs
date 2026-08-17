namespace NuGetFetch;

internal static class NuGetHttpRequest
{
    private static readonly HttpRequestOptionsKey<bool> BrowserStreamingResponse =
        new("WebAssemblyEnableStreamingResponse");

    public static HttpRequestMessage CreateGet(string requestUri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Options.Set(BrowserStreamingResponse, true);
        return request;
    }
}
