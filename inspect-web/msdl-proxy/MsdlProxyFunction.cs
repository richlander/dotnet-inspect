using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace MsdlProxy;

public sealed class MsdlProxyFunction(IHttpClientFactory httpClientFactory)
{
    [Function("MsdlProxy")]
    public async Task<IActionResult> GetSymbolAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "msdl/{pdbFileName}/{symbolKey}")]
        HttpRequest request,
        string pdbFileName,
        string symbolKey,
        CancellationToken cancellationToken)
    {
        ApplySecurityHeaders(request.HttpContext.Response);
        if (!MsdlRequestValidator.IsValidPdbFileName(pdbFileName)
            || !MsdlRequestValidator.IsValidSymbolKey(symbolKey))
        {
            return new BadRequestObjectResult(
                "Invalid pdbFileName or symbolKey.");
        }

        HttpClient client = httpClientFactory.CreateClient(MsdlClient.Name);
        return await MsdlClient.ProxySymbolAsync(
            client,
            pdbFileName,
            symbolKey,
            cancellationToken);
    }

    [Function("MsdlProxyHealth")]
    public IActionResult Health(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "healthz")]
        HttpRequest request)
    {
        ApplySecurityHeaders(request.HttpContext.Response);
        return new ContentResult
        {
            Content = "ok",
            ContentType = "text/plain",
            StatusCode = StatusCodes.Status200OK,
        };
    }

    private static void ApplySecurityHeaders(HttpResponse response)
    {
        IHeaderDictionary headers = response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Frame-Options"] = "DENY";
        headers["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains";
    }
}
