using MsdlProxy;

var builder = WebApplication.CreateSlimBuilder(args);

var allowedOrigins = (Environment.GetEnvironmentVariable("MSDL_PROXY_ALLOWED_ORIGINS")
        ?? "https://dotnet-inspect.ca,https://coreclr.dotnet-inspect.ca,https://dotnet-inspect.net")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

const string CorsPolicy = "msdl-proxy";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
            .WithMethods("GET")
            .AllowAnyHeader());
});

builder.Services.AddHttpClient(MsdlClient.Name, client =>
{
    // A generous but bounded timeout: MSDL can be slow, but a proxy request
    // must not hang indefinitely and tie up Container Apps capacity.
    client.Timeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();
app.UseCors(CorsPolicy);

// Results.Text avoids requiring JSON reflection metadata that the AOT-slim
// builder does not register by default (Results.Ok<string> would throw at
// request time -- there is no test host mode that would catch that, since
// it only manifests once JSON serialization actually runs).
app.MapGet("/healthz", () => Results.Text("ok"));

// Mirrors the exact MSDL request shape SymbolPackageDownloader already
// builds client-side: /download/symbols/{pdbFileName}/{symbolKey}/{pdbFileName}.
// This proxy exists solely to bypass MSDL's non-CORS-compliant redirect hop
// (see docs/design/untrusted-data-threat-model.md for the acquisition threat
// model this endpoint has to satisfy). The host is always the fixed MSDL
// host below -- the client never supplies a URL or host, only the two path
// segments MSDL itself expects, each independently validated. This makes an
// open-redirect/SSRF outcome structurally impossible rather than merely
// filtered.
app.MapGet("/msdl/{pdbFileName}/{symbolKey}",
    async (string pdbFileName, string symbolKey, IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken) =>
    {
        if (!MsdlRequestValidator.IsValidPdbFileName(pdbFileName)
            || !MsdlRequestValidator.IsValidSymbolKey(symbolKey))
        {
            return Results.Text("Invalid pdbFileName or symbolKey.", statusCode: StatusCodes.Status400BadRequest);
        }

        var client = httpClientFactory.CreateClient(MsdlClient.Name);
        return await MsdlClient.ProxySymbolAsync(client, pdbFileName, symbolKey, cancellationToken);
    });

app.Run();
