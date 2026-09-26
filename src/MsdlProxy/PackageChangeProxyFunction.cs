using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace MsdlProxy;

public sealed class PackageChangeProxyFunction(
    IHttpClientFactory httpClientFactory)
{
    [Function("NuGetPackageChangeProxy")]
    public async Task<IActionResult> GetNuGetAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "package-changes/nuget")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        ApplyResponseHeaders(request.HttpContext.Response);
        if (!PackageChangeProxyRequestValidator.TryCreateNuGetRequest(
                request.Query,
                out Uri? upstream))
        {
            return new BadRequestObjectResult(
                "The NuGet package-change request is invalid.");
        }

        HttpClient client =
            httpClientFactory.CreateClient(
                PackageChangeProxyClient.NuGetClientName);
        return await PackageChangeProxyClient.GetNuGetJsonAsync(
            client,
            upstream,
            cancellationToken).ConfigureAwait(false);
    }

    [Function("GitHubPackageChangeAdvisoryProxy")]
    public async Task<IActionResult> GetAdvisoriesAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "package-changes/advisories")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        ApplyResponseHeaders(request.HttpContext.Response);
        if (!PackageChangeProxyRequestValidator.TryCreateAdvisoryRequest(
                request.Query,
                out Uri? upstream))
        {
            return new BadRequestObjectResult(
                "The package-change advisory request is invalid.");
        }

        HttpClient client =
            httpClientFactory.CreateClient(
                PackageChangeProxyClient.AdvisoryClientName);
        return await PackageChangeProxyClient.GetAdvisoryJsonAsync(
            client,
            upstream,
            request.HttpContext.Response,
            cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyResponseHeaders(HttpResponse response)
    {
        IHeaderDictionary headers = response.Headers;
        headers.CacheControl = "no-store";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Frame-Options"] = "DENY";
        headers["Strict-Transport-Security"] =
            "max-age=63072000; includeSubDomains";
    }
}
