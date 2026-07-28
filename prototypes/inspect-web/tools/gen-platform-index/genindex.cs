// Offline generator for the static platform-assembly/facade index (Pillar A).
// SRM-only, metadata-only (no assembly loading). Downloads Microsoft.NETCore.App
// ref + runtime packs for net6.0-net10.0 and NETStandard ref packs, enumerates
// each assembly, detects facades (ExportedType-only) and their impl target, and
// emits a compact TSV. This is an offline build tool, not part of the WASM app.
//
// Usage: dotnet run genindex.cs -- <output.tsv>

using System.Reflection;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;

var outputPath = args.Length > 0 ? args[0] : "platform-index.tsv";
var cacheDir = Path.Combine(Path.GetTempPath(), "inspect-pack-cache");
Directory.CreateDirectory(cacheDir);

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

const string RefPackId = "microsoft.netcore.app.ref";
const string RuntimePackId = "microsoft.netcore.app.runtime.linux-x64";
const string AspNetRefPackId = "microsoft.aspnetcore.app.ref";
const string AspNetRuntimePackId = "microsoft.aspnetcore.app.runtime.linux-x64";

var rows = new List<Row>();

// --- Shared frameworks: net6.0 .. net10.0 ---------------------------------
// Each shared framework is its own runtime pack. Microsoft.NETCore.App is the
// base (CoreCLR); Microsoft.AspNetCore.App layers on top with the routing/
// hosting/Microsoft.Extensions.* surface. Rows carry a pack label so a per-pack
// consumer (e.g. the resident-pack overview count) does not conflate the two.
await AddSharedFrameworkAsync(RefPackId, RuntimePackId, "netcore.app");
await AddSharedFrameworkAsync(AspNetRefPackId, AspNetRuntimePackId, "aspnetcore.app");

async Task AddSharedFrameworkAsync(string refPackId, string runtimePackId, string pack)
{
    foreach (var major in new[] { 6, 7, 8, 9, 10 })
    {
        var tfm = $"net{major}.0";
        Console.Error.WriteLine($"== {tfm} ({pack}) ==");

        var refVersion = await ResolveVersionAsync(refPackId, major);
        var runtimeVersion = await ResolveVersionAsync(runtimePackId, major);
        if (refVersion is null || runtimeVersion is null)
        {
            Console.Error.WriteLine($"  skip {tfm}: ref={refVersion ?? "?"} runtime={runtimeVersion ?? "?"}");
            continue;
        }
        Console.Error.WriteLine($"  ref={refVersion}  runtime={runtimeVersion}");

        // Reference assemblies give the logical public API surface per assembly name.
        var refPack = await GetPackAsync(refPackId, refVersion);
        var refInfo = ReadPack(refPack, name =>
            name.StartsWith($"ref/{tfm}/", StringComparison.OrdinalIgnoreCase) &&
            name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

        // Runtime assemblies reveal which physical files are facades + their targets.
        var runtimePack = await GetPackAsync(runtimePackId, runtimeVersion);
        var runtimeInfo = ReadPack(runtimePack, name =>
            name.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase) &&
            name.Contains($"/lib/{tfm}/", StringComparison.OrdinalIgnoreCase) &&
            name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

        MergeInto(rows, tfm, pack, refInfo, runtimeInfo);
        Console.Error.WriteLine($"  ref assemblies={refInfo.Count}  runtime assemblies={runtimeInfo.Count}");
    }
}

// --- netstandard ref packs ------------------------------------------------
await AddNetStandardAsync("netstandard.library.ref", null, "netstandard2.1", "ref/netstandard2.1/");
await AddNetStandardAsync("netstandard.library", "2.0.3", "netstandard2.0", "build/netstandard2.0/ref/");

async Task AddNetStandardAsync(string packId, string? pinnedVersion, string tfm, string prefix)
{
    Console.Error.WriteLine($"== {tfm} ({packId}) ==");
    var version = pinnedVersion ?? await ResolveLatestStableAsync(packId);
    if (version is null) { Console.Error.WriteLine($"  skip {tfm}: no version"); return; }
    Console.Error.WriteLine($"  version={version}");
    var pack = await GetPackAsync(packId, version);
    var info = ReadPack(pack, name =>
        name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
    MergeInto(rows, tfm, "netstandard", info, new Dictionary<string, AsmInfo>(StringComparer.OrdinalIgnoreCase));
    Console.Error.WriteLine($"  assemblies={info.Count}");
}

// --- Emit TSV -------------------------------------------------------------
var sb = new StringBuilder();
sb.Append("tfm\tpack\tassembly\tfile\tkind\tforwardsTo\tversion\tpublicTypes\n");
foreach (var r in rows
    .OrderBy(r => TfmSortKey(r.Tfm))
    .ThenBy(r => r.Pack, StringComparer.OrdinalIgnoreCase)
    .ThenBy(r => r.Assembly, StringComparer.OrdinalIgnoreCase))
{
    sb.Append($"{r.Tfm}\t{r.Pack}\t{r.Assembly}\t{r.File}\t{r.Kind}\t{r.ForwardsTo}\t{r.Version}\t{r.PublicTypes}\n");
}
File.WriteAllText(outputPath, sb.ToString());
Console.Error.WriteLine($"\nWrote {rows.Count} rows -> {outputPath} ({new FileInfo(outputPath).Length} bytes)");

// ==========================================================================

void MergeInto(List<Row> sink, string tfm, string pack,
    Dictionary<string, AsmInfo> refInfo, Dictionary<string, AsmInfo> runtimeInfo)
{
    var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var k in refInfo.Keys) files.Add(k);
    foreach (var k in runtimeInfo.Keys) files.Add(k);

    foreach (var file in files)
    {
        refInfo.TryGetValue(file, out var r);
        runtimeInfo.TryGetValue(file, out var rt);
        var assembly = Path.GetFileNameWithoutExtension(file);

        string kind;
        string forwardsTo = "";
        // Facade detection prefers the physical (runtime) assembly, but for packs
        // with no runtime counterpart (netstandard) the ref assembly's own
        // type-forwards are authoritative.
        var physical = rt ?? r;
        if (physical is not null && physical.TopLevelPublicTypes == 0 && physical.ForwardCount > 0)
        {
            kind = "facade";
            forwardsTo = physical.DominantForwardTarget ?? "";
        }
        else if (rt is not null)
        {
            kind = "impl";
        }
        else
        {
            kind = "ref"; // present only in the ref/targeting pack
        }

        // Logical public API count: prefer the ref assembly; fall back to runtime.
        var publicTypes = r?.TopLevelPublicTypes ?? rt?.TopLevelPublicTypes ?? 0;
        var version = r?.Version ?? rt?.Version ?? "";

        sink.Add(new Row(tfm, pack, assembly, file, kind, forwardsTo, version, publicTypes));
    }
}

Dictionary<string, AsmInfo> ReadPack(byte[] packBytes, Func<string, bool> match)
{
    var result = new Dictionary<string, AsmInfo>(StringComparer.OrdinalIgnoreCase);
    using var stream = new MemoryStream(packBytes, writable: false);
    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
    foreach (var entry in archive.Entries)
    {
        if (!match(entry.FullName)) continue;
        var file = Path.GetFileName(entry.FullName);
        using var es = entry.Open();
        using var ms = new MemoryStream();
        es.CopyTo(ms);
        var info = ReadAssembly(ms.ToArray());
        if (info is not null)
            result[file] = info; // last write wins (rid dirs are equivalent)
    }
    return result;
}

AsmInfo? ReadAssembly(byte[] bytes)
{
    try
    {
        using var pe = new PEReader(new MemoryStream(bytes, writable: false));
        if (!pe.HasMetadata) return null;
        var reader = pe.GetMetadataReader();

        int publicTypes = 0;
        foreach (var handle in reader.TypeDefinitions)
        {
            var def = reader.GetTypeDefinition(handle);
            if (reader.GetString(def.Name) == "<Module>") continue;
            var attr = def.Attributes & TypeAttributes.VisibilityMask;
            if (attr == TypeAttributes.Public) // top-level public only
                publicTypes++;
        }

        var forwardCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var handle in reader.ExportedTypes)
        {
            var exported = reader.GetExportedType(handle);
            if (exported.Implementation.Kind != HandleKind.AssemblyReference) continue;
            var asmRef = reader.GetAssemblyReference((AssemblyReferenceHandle)exported.Implementation);
            var target = reader.GetString(asmRef.Name);
            forwardCounts[target] = forwardCounts.GetValueOrDefault(target) + 1;
        }

        string version = "";
        if (reader.IsAssembly)
        {
            var asm = reader.GetAssemblyDefinition();
            version = asm.Version.ToString();
        }

        string? dominant = forwardCounts.Count == 0
            ? null
            : forwardCounts.OrderByDescending(kv => kv.Value).First().Key;

        return new AsmInfo(publicTypes, forwardCounts.Values.Sum(), dominant, version);
    }
    catch
    {
        return null;
    }
}

async Task<string?> ResolveVersionAsync(string packId, int major)
{
    var versions = await GetVersionsAsync(packId);
    if (versions is null) return null;
    var prefix = $"{major}.";
    return versions.LastOrDefault(v => v.StartsWith(prefix, StringComparison.Ordinal) && !v.Contains('-'))
        ?? versions.LastOrDefault(v => v.StartsWith(prefix, StringComparison.Ordinal));
}

async Task<string?> ResolveLatestStableAsync(string packId)
{
    var versions = await GetVersionsAsync(packId);
    return versions?.LastOrDefault(v => !v.Contains('-')) ?? versions?.LastOrDefault();
}

async Task<string[]?> GetVersionsAsync(string packId)
{
    try
    {
        var url = $"https://api.nuget.org/v3-flatcontainer/{Uri.EscapeDataString(packId)}/index.json";
        var bytes = await http.GetByteArrayAsync(url);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.GetProperty("versions").EnumerateArray()
            .Select(e => e.GetString()).Where(v => !string.IsNullOrWhiteSpace(v)).Cast<string>().ToArray();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  version lookup failed for {packId}: {ex.Message}");
        return null;
    }
}

async Task<byte[]> GetPackAsync(string packId, string version)
{
    var cacheFile = Path.Combine(cacheDir, $"{packId}.{version}.nupkg");
    if (File.Exists(cacheFile))
        return await File.ReadAllBytesAsync(cacheFile);
    var url = $"https://api.nuget.org/v3-flatcontainer/{Uri.EscapeDataString(packId)}/" +
              $"{Uri.EscapeDataString(version)}/{Uri.EscapeDataString(packId)}.{Uri.EscapeDataString(version)}.nupkg";
    Console.Error.WriteLine($"  downloading {url}");
    var bytes = await http.GetByteArrayAsync(url);
    await File.WriteAllBytesAsync(cacheFile, bytes);
    return bytes;
}

static int TfmSortKey(string tfm)
{
    if (tfm.StartsWith("netstandard", StringComparison.Ordinal))
        return 1000 + (tfm == "netstandard2.0" ? 0 : 1);
    if (tfm.StartsWith("net", StringComparison.Ordinal) &&
        int.TryParse(tfm.AsSpan(3).ToString().Split('.')[0], out var major))
        return major;
    return 9999;
}

record Row(string Tfm, string Pack, string Assembly, string File, string Kind, string ForwardsTo, string Version, int PublicTypes);
record AsmInfo(int TopLevelPublicTypes, int ForwardCount, string? DominantForwardTarget, string Version);
