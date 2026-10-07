#:project ../src/DotnetInspect.Web.Interop.Metadata/DotnetInspect.Web.Interop.Metadata.csproj
#:project ../src/DotnetInspect.Web.Interop.Package/DotnetInspect.Web.Interop.Package.csproj
#:property EnablePreviewFeatures=true

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotnetInspect.Web.Interop.Metadata;
using DotnetInspect.Web.Interop.Package;

if (args.Length is not (5 or 6) || (args.Length == 6 && args[5] != "summary") || args[4] is not ("overview" or "table" or "heap"))
{
    Console.Error.WriteLine("Usage: <probe> <package> <version> <framework> <asset-id> <overview|table|heap> [summary]");
    return 2;
}
#pragma warning disable CA1416 // Native-host measurement of production Browser exports.
Console.WriteLine("package\tversion\tframework\tasset\tterminal\tflow\tcache\tms\tjsonBytes\tsha256\tcardinality\tavailable\tresidentBytes");
for (int i = 0; i < 2; i++)
{
    Stopwatch watch = Stopwatch.StartNew();
    if (args.Length == 6)
        _ = await PackageExports.QueryPackageSummary(args[0], args[1], args[2]);
    string result = args[4] switch
    {
        "overview" => await MetadataExports.QueryPackageMetadata(args[0], args[1], args[2], args[3]),
        "table" => await MetadataExports.QueryPackageMetadataTable(args[0], args[1], args[2], args[3], "cli", 2, 1, 10),
        _ => await MetadataExports.QueryPackageHeapEntries(args[0], args[1], args[2], args[3], "cli", "String"),
    };
    using JsonDocument parsed = JsonDocument.Parse(result);
    JsonElement root = parsed.RootElement;
    int count = args[4] switch
    {
        "overview" => root.GetProperty("assemblies").GetArrayLength(),
        "table" => root.GetProperty("rows").GetArrayLength(),
        _ => root.GetProperty("entries").GetArrayLength(),
    };
    bool available = args[4] == "overview"
        ? root.GetProperty("inspectionError").ValueKind == JsonValueKind.Null
            && root.GetProperty("compileLibrary").GetProperty("status").GetString() == "Selected"
        : root.GetProperty("error").ValueKind == JsonValueKind.Null;
    watch.Stop();
    using JsonDocument stats = JsonDocument.Parse(PackageExports.PackageCacheStats());
    Console.WriteLine(string.Join('\t', args[0], args[1], args[2], args[3], args[4], args.Length == 6 ? "summary" : "direct",
        i == 0 ? "cold" : "warm", watch.Elapsed.TotalMilliseconds.ToString("F1"),
        Encoding.UTF8.GetByteCount(result), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(result))),
        count, available, stats.RootElement.GetProperty("residentBytes").GetInt64()));
    if (Environment.GetEnvironmentVariable("WEB_METADATA_RESULT_DIR") is { } output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, $"{args[4]}-{i}.json"), result);
    }
}
return 0;
