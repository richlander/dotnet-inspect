#:project ../src/DotnetInspect.Web.Interop.Metadata/DotnetInspect.Web.Interop.Metadata.csproj
#:property EnablePreviewFeatures=true
#:property AssemblyName=DotnetInspect.Web.Tests

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspect.Web;
using DotnetInspect.Web.Interop.Metadata;

// NativeAOT probe of the complete website Fast Diff export. Entry persistence
// models the browser acquisition branch, not JS Cache Storage wall time.
if (args.Length != 6 || args[5] is not ("api" or "body"))
{
    Console.Error.WriteLine("Usage: <probe> <package> <before> <after> <framework> <asset-id> <api|body>");
    return 2;
}
#pragma warning disable CA1416
BrowserPackageWorkspace.ConfigurePackageEntryPersistence(new ProbeEntryPersistence());
var request = new BrowserLibraryFastDiffRequest(BrowserLibraryFastDiffSchema.Version,
    args[0], args[2], args[1], args[3], args[4],
    args[5] == "api" ? BrowserFastDiffAxes.Api : BrowserFastDiffAxes.ApiAndBody);
string json = JsonSerializer.Serialize(request, ProbeJsonContext.Default.BrowserLibraryFastDiffRequest);
Console.WriteLine("cache\ttotalMs\tcomparedTypes\tchangedTypes\tsha256");
for (int i = 0; i < 2; i++)
{
    var watch = Stopwatch.StartNew();
    string result = await MetadataExports.QueryLibraryFastDiff($"probe-{i}", json);
    using var document = JsonDocument.Parse(result);
    watch.Stop();
    JsonElement root = document.RootElement;
    if (root.GetProperty("kind").GetString() != "Succeeded")
        throw new InvalidOperationException(result);
    JsonElement value = root.GetProperty("value");
    string? directory = Environment.GetEnvironmentVariable("WEB_FAST_DIFF_RESULT_DIR");
    if (directory is not null)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{i}.json"), result);
    }
    Console.WriteLine(string.Join('\t', i == 0 ? "cold" : "warm",
        watch.Elapsed.TotalMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
        value.GetProperty("comparedTypeCount").GetInt32(), value.GetProperty("types").GetArrayLength(),
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(result)))));
}
return 0;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserLibraryFastDiffRequest))]
internal partial class ProbeJsonContext : JsonSerializerContext;

internal sealed class ProbeEntryPersistence : IBrowserPackageEntryPersistence
{
    readonly Dictionary<string, byte[]> _entries = new(StringComparer.Ordinal);
    long _bytes;
    public bool IsPersistent => false;
    public ValueTask<byte[]?> ReadAsync(string key)
    {
        lock (_entries)
            return ValueTask.FromResult(_entries.TryGetValue(key, out var value) ? value.ToArray() : null);
    }
    public ValueTask PublishAsync(string key, ReadOnlyMemory<byte> content)
    {
        lock (_entries)
        {
            long previous = _entries.TryGetValue(key, out var value) ? value.Length : 0;
            if (_bytes - previous + content.Length > 128L * 1024 * 1024)
                throw new InvalidOperationException("The probe entry-cache budget was exceeded.");
            _entries[key] = content.ToArray();
            _bytes = _bytes - previous + content.Length;
        }
        return ValueTask.CompletedTask;
    }
}
