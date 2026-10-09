#:project ../src/DotnetInspect.Web.Interop.Package/DotnetInspect.Web.Interop.Package.csproj
#:project ../src/DotnetInspect.Web.Interop.Library/DotnetInspect.Web.Interop.Library.csproj
#:property EnablePreviewFeatures=true

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotnetInspect.Web.Interop.Package;
using DotnetInspect.Web.Interop.Library;
using System.Text.Json.Serialization;

// Measures the public-export sequence used for Package -> Library Overview.
// legacy includes the broad Type navigation request launched alongside the
// exact Library API request. overview measures the demand-restricted flow.
if (args.Length is not (4 or 5) || args[3] is not ("legacy" or "overview"))
{
    Console.Error.WriteLine("Usage: <probe> <package> <version> <framework> <legacy|overview> [asset-id]");
    return 2;
}

#pragma warning disable CA1416 // Deliberate native-host measurement of Browser exports.
Console.WriteLine("package\tversion\tframework\tmode\tcache\ttotalMs\tsummaryMs\tbroadMs\tapiMs\tenablementsMs\tsummaryBytes\tbroadBytes\tapiBytes\tenablementsBytes\tapiSha256\tenablementsSha256\tresidentBytes");
for (int i = 0; i < 2; i++)
{
    Stopwatch total = Stopwatch.StartNew();
    (string summary, double summaryMs) = await Timed(() =>
        PackageExports.QueryPackageSummary(args[0], args[1], args[2]));
    using JsonDocument parsed = JsonDocument.Parse(summary);
    JsonElement libraries = parsed.RootElement.GetProperty("packageChildren")
        .GetProperty("content").GetProperty("libraries");
    string? library = parsed.RootElement.TryGetProperty("defaultLibraryId", out var selected)
        ? selected.GetString() : null;
    if (args.Length == 5) library = args[4];
    library ??= libraries.GetArrayLength() > 0
        ? libraries[0].GetProperty("assetId").GetString() : null;
    (string text, double ms) broad = ("", 0);
    (string text, double ms) api = ("", 0);
    (string text, double ms) enablements = ("", 0);
    if (library is not null)
    {
        Task<(string, double)>? broadTask = args[3] == "legacy"
            ? Timed(() => PackageExports.QueryPackage(args[0], args[1], args[2])) : null;
        Task<(string, double)> apiTask = Timed(() =>
            PackageExports.QueryLibraryApi(args[0], args[1], args[2], library));
        string request = JsonSerializer.Serialize(
            new BrowserLibraryInspectionRequest(
                new BrowserLibrarySelector(BrowserLibrarySelectorKind.Package,
                    new BrowserPackageLibrarySelector(args[0], args[1], args[2], library), null),
                new BrowserLibraryInspectionPlan(Enablements: true)),
            ProbeJsonContext.Default.BrowserLibraryInspectionRequest);
        enablements = await Timed(() => LibraryExports.InspectLibrary(request));
        api = await apiTask;
        if (broadTask is not null) broad = await broadTask;
    }
    total.Stop();
    if (Environment.GetEnvironmentVariable("WEB_LIBRARY_OPEN_RESULT_DIR") is { } output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, $"{args[0]}-{i}-summary.json"), summary);
        File.WriteAllText(Path.Combine(output, $"{args[0]}-{i}-api.json"), api.text);
        File.WriteAllText(Path.Combine(output, $"{args[0]}-{i}-enablements.json"), enablements.text);
    }
    using JsonDocument stats = JsonDocument.Parse(PackageExports.PackageCacheStats());
    Console.WriteLine(string.Join('\t', args[0], args[1], args[2], args[3],
        i == 0 ? "cold" : "warm", total.Elapsed.TotalMilliseconds.ToString("F1"),
        summaryMs.ToString("F1"), broad.ms.ToString("F1"), api.ms.ToString("F1"), enablements.ms.ToString("F1"),
        Encoding.UTF8.GetByteCount(summary), Encoding.UTF8.GetByteCount(broad.text),
        Encoding.UTF8.GetByteCount(api.text), Encoding.UTF8.GetByteCount(enablements.text),
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(api.text))),
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(enablements.text))),
        stats.RootElement.GetProperty("residentBytes").GetInt64()));
}
return 0;

static async Task<(string, double)> Timed(Func<Task<string>> operation)
{
    Stopwatch watch = Stopwatch.StartNew();
    string result = await operation();
    return (result, watch.Elapsed.TotalMilliseconds);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserLibraryInspectionRequest))]
internal partial class ProbeJsonContext : JsonSerializerContext;
