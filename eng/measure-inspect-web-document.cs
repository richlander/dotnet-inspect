#:project ../src/DotnetInspect.Web.Interop.Package/DotnetInspect.Web.Interop.Package.csproj
#:property EnablePreviewFeatures=true
#:property AssemblyName=DotnetInspect.Web.Tests

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotnetInspect.Web;
using DotnetInspect.Web.Interop.Package;

if (args.Length is not (3 or 4)
    || (args.Length == 4 && args[3] != "evidence"))
{
    Console.Error.WriteLine("Usage: <probe> <package> <version> <path> [evidence]");
    return 2;
}

#pragma warning disable CA1416 // Native-host execution of the production Browser export, not Wasm timing.
// The existing test identity permits the same entry-store seam as the Library-opening probe.
// Bounded memory persistence models entry availability, not JavaScript Cache Storage I/O.
BrowserPackageWorkspace.ConfigurePackageEntryPersistence(new ProbeEntryPersistence());
if (args.Length == 3)
    Console.WriteLine("cache\tms\tjsonBytes\ttextBytes\tsha256\tallocatedBytes");
for (int i = 0; i < 2; i++)
{
    if (args.Length == 4)
    {
#if DEBUG
        Console.WriteLine(await PackageExports.GetPackageDocumentWithEvidence(
            args[0], args[1], args[2]));
        continue;
#else
        throw new InvalidOperationException("Evidence capture requires a Debug export build.");
#endif
    }

    long allocated = GC.GetTotalAllocatedBytes(precise: true);
    Stopwatch watch = Stopwatch.StartNew();
    string result = await PackageExports.GetPackageDocument(
        args[0], args[1], args[2]);
    using JsonDocument document = JsonDocument.Parse(result);
    JsonElement root = document.RootElement;
    if (!string.Equals(root.GetProperty("path").GetString(), args[2],
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("The export returned another document.");
    }
    byte[] text = Encoding.UTF8.GetBytes(root.GetProperty("text").GetString()
        ?? throw new InvalidOperationException("The export returned no document text."));
    string hash = Convert.ToHexStringLower(SHA256.HashData(text));
    watch.Stop();
    allocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;
    Console.WriteLine(string.Join('\t',
        i == 0 ? "cold" : "warm",
        watch.Elapsed.TotalMilliseconds.ToString("F4", CultureInfo.InvariantCulture),
        Encoding.UTF8.GetByteCount(result),
        text.Length,
        hash,
        allocated));
}
return 0;

internal sealed class ProbeEntryPersistence : IBrowserPackageEntryPersistence
{
    readonly Dictionary<string, byte[]> _entries = new(StringComparer.Ordinal);
    long _bytes;

    public bool IsPersistent => false;

    public ValueTask<byte[]?> ReadAsync(string key)
    {
        lock (_entries)
            return ValueTask.FromResult(
                _entries.TryGetValue(key, out byte[]? value) ? value.ToArray() : null);
    }

    public ValueTask PublishAsync(string key, ReadOnlyMemory<byte> content)
    {
        lock (_entries)
        {
            long previous = _entries.TryGetValue(key, out byte[]? value) ? value.Length : 0;
            if (_bytes - previous + content.Length > 128L * 1024 * 1024)
                throw new InvalidOperationException("The probe entry-cache budget was exceeded.");
            _entries[key] = content.ToArray();
            _bytes = _bytes - previous + content.Length;
        }
        return ValueTask.CompletedTask;
    }
}
