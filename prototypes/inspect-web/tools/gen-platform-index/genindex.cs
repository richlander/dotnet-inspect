using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGet.Versioning;

var outputPath = args.Length > 0 ? args[0] : "platform-index.json";
var cacheDir = Path.Combine(Path.GetTempPath(), "inspect-pack-cache");
Directory.CreateDirectory(cacheDir);

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
var versionsByPackage = new Dictionary<string, NuGetVersion[]>(StringComparer.Ordinal);
var targets = new List<CatalogTarget>();
var families = new[]
{
    new Family("netcore.app", "microsoft.netcore.app.ref", "microsoft.netcore.app.runtime.linux-x64"),
    new Family("aspnetcore.app", "microsoft.aspnetcore.app.ref", "microsoft.aspnetcore.app.runtime.linux-x64"),
};

foreach (var major in new[] { 6, 7, 8, 9, 10, 11 })
{
    var tfm = $"net{major}.0";
    HashSet<NuGetVersion>? common = null;
    foreach (var package in families.SelectMany(family => new[] { family.ReferencePackage, family.RuntimePackage }))
    {
        HashSet<NuGetVersion> versions = new((await GetVersionsAsync(package))
            .Where(version => version.Major == major && version.Minor == 0),
            VersionComparer.VersionRelease);
        if (common is null) common = versions;
        else common.IntersectWith(versions);
    }
    var selected = common?.OrderByDescending(version => version, VersionComparer.VersionRelease).FirstOrDefault()
        ?? throw new InvalidDataException($"No common reference/runtime pack version for {tfm}.");
    var version = selected.ToNormalizedString();
    Console.Error.WriteLine($"== {tfm} @ {version} ==");
    var rows = new List<Row>();
    foreach (var family in families)
    {
        var reference = ReadPack(await GetPackAsync(family.ReferencePackage, version), name =>
            name.StartsWith($"ref/{tfm}/", StringComparison.OrdinalIgnoreCase)
            && !name[$"ref/{tfm}/".Length..].Contains('/')
            && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase),
            AssemblyResolutionProvenance.Package(family.ReferencePackage, version, tfm, null));
        var runtimeBytes = await GetPackAsync(family.RuntimePackage, version);
        var content = new InMemoryPackageContent(runtimeBytes, fromCache: false,
            producerKey: "https://api.nuget.org/v3/index.json");
        var selection = PackageAssetSelector.SelectPlatformPack(content, tfm, "linux-x64");
        if (selection is not PackageAssetSelection.Selected selectedAssets)
            throw new InvalidDataException($"Cannot select {family.RuntimePackage}@{version}: {selection}.");
        var entries = selectedAssets.Universe.Assets.Select(asset => asset.EntryPath)
            .ToHashSet(StringComparer.Ordinal);
        var runtime = ReadPack(runtimeBytes, entries.Contains,
            AssemblyResolutionProvenance.Package(family.RuntimePackage, version, tfm, "linux-x64"));
        if (reference.Count == 0 || runtime.Count == 0)
            throw new InvalidDataException($"Missing managed reference/runtime inventory for {family.Pack} {tfm}@{version}.");
        MergeInto(rows, tfm, version, family.Pack, reference, runtime);
        Console.Error.WriteLine($"  {family.Pack}: {reference.Count} reference, {runtime.Count} runtime libraries");
    }
    targets.Add(new(tfm, version, Ordered(rows)));
}

await AddNetStandardAsync("netstandard.library.ref", "2.1.0", "netstandard2.1", "ref/netstandard2.1/");
await AddNetStandardAsync("netstandard.library", "2.0.3", "netstandard2.0", "build/netstandard2.0/ref/");

await using (var output = File.Create(outputPath))
await using (var writer = new StreamWriter(output))
{
    await writer.WriteLineAsync("{\"schemaVersion\":1,\"defaultFramework\":\"net11.0\",\"targets\":[");
    for (var i = 0; i < targets.Count; i++)
    {
        var target = targets[i];
        await writer.WriteLineAsync(
            $"{{\"tfm\":{JsonSerializer.Serialize(target.Tfm, CatalogJsonContext.Default.String)},\"version\":{JsonSerializer.Serialize(target.Version, CatalogJsonContext.Default.String)},\"rows\":[");
        for (var j = 0; j < target.Rows.Length; j++)
        {
            await writer.WriteAsync(JsonSerializer.Serialize(target.Rows[j], CatalogJsonContext.Default.Row));
            await writer.WriteLineAsync(j + 1 == target.Rows.Length ? "" : ",");
        }
        await writer.WriteLineAsync(i + 1 == targets.Count ? "]}" : "]},");
    }
    await writer.WriteLineAsync("]}");
}
Console.Error.WriteLine($"Wrote {targets.Sum(target => target.Rows.Length)} libraries -> {outputPath}.");

async Task AddNetStandardAsync(string package, string version, string tfm, string prefix)
{
    var reference = ReadPack(await GetPackAsync(package, version), name =>
        name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && !name[prefix.Length..].Contains('/')
        && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase),
        AssemblyResolutionProvenance.Package(package, version, tfm, null));
    if (reference.Count == 0)
        throw new InvalidDataException($"Missing reference inventory for {tfm}@{version}.");
    var rows = new List<Row>();
    MergeInto(rows, tfm, version, "netstandard", reference, new(StringComparer.OrdinalIgnoreCase));
    targets.Add(new(tfm, version, Ordered(rows)));
}

static Row[] Ordered(List<Row> rows) => [.. rows
    .OrderBy(row => row.Pack, StringComparer.Ordinal)
    .ThenBy(row => row.Assembly, StringComparer.OrdinalIgnoreCase)];

static void MergeInto(List<Row> rows, string tfm, string packVersion, string pack,
    Dictionary<string, AssemblyInfo> reference, Dictionary<string, AssemblyInfo> runtime)
{
    foreach (var name in reference.Keys.Union(runtime.Keys, StringComparer.OrdinalIgnoreCase))
    {
        reference.TryGetValue(name, out var contract);
        runtime.TryGetValue(name, out var implementation);
        var physical = implementation ?? contract
            ?? throw new InvalidDataException($"Missing metadata for {name}.");
        var facade = implementation?.IsFacade == true;
        rows.Add(new(
            tfm, pack, physical.Name, physical.File,
            facade ? "facade" : implementation is not null ? "impl" : "ref",
            facade ? physical.DominantForwardTarget : null,
            physical.Version, contract?.PublicTypes ?? physical.PublicTypes,
            contract is not null, implementation is not null, packVersion));
    }
}

static Dictionary<string, AssemblyInfo> ReadPack(
    byte[] bytes, Func<string, bool> match, AssemblyResolutionProvenance provenance)
{
    var result = new Dictionary<string, AssemblyInfo>(StringComparer.OrdinalIgnoreCase);
    using var stream = new MemoryStream(bytes, writable: false);
    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
    foreach (var entry in archive.Entries.Where(entry => match(entry.FullName)))
    {
        using var content = entry.Open();
        using var image = new MemoryStream();
        content.CopyTo(image);
        image.Position = 0;
        using var pe = new PEReader(image);
        if (!pe.HasMetadata) continue;
        var reader = pe.GetMetadataReader();
        if (!reader.IsAssembly || reader.MetadataKind != MetadataKind.Ecma335)
            throw new InvalidDataException($"Unsupported platform library {entry.FullName}.");
        var definition = reader.GetAssemblyDefinition();
        var name = reader.GetString(definition.Name);
        var imageBytes = image.ToArray();
        var descriptor = ResolvedAssemblyReference.Create(
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader), path: null,
            () => new MemoryStream(imageBytes, writable: false), provenance);
        var classification = AssemblySurfaceClassifier.Classify(descriptor) switch
        {
            AssemblySurfaceClassificationOutcome.Classified classified => classified.Classification,
            AssemblySurfaceClassificationOutcome.Rejected rejected =>
                throw new InvalidDataException($"{entry.FullName}: {rejected.Failure.Detail}"),
            _ => throw new InvalidDataException("Unknown assembly classification outcome."),
        };
        var dominant = AssemblyDetailScanner.ScanTypeForwarders(pe)
            .GroupBy(forwarder => forwarder.TargetAssembly, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key).FirstOrDefault();
        if (!result.TryAdd(name, new(name, Path.GetFileName(entry.FullName), definition.Version.ToString(),
            classification.MeaningfulPublicTypeCount, classification.Kind == AssemblySurfaceKind.Facade, dominant)))
            throw new InvalidDataException($"Duplicate managed assembly {name} in the platform pack.");
    }
    return result;
}

async Task<NuGetVersion[]> GetVersionsAsync(string package)
{
    if (versionsByPackage.TryGetValue(package, out var cached)) return cached;
    using var document = JsonDocument.Parse(await http.GetByteArrayAsync(
        $"https://api.nuget.org/v3-flatcontainer/{package}/index.json"));
    var versions = document.RootElement.GetProperty("versions").EnumerateArray()
        .Select(value => NuGetVersion.Parse(value.GetString()
            ?? throw new InvalidDataException($"Null version in {package}."))).ToArray();
    versionsByPackage.Add(package, versions);
    return versions;
}

async Task<byte[]> GetPackAsync(string package, string version)
{
    var cacheFile = Path.Combine(cacheDir, $"{package}.{version}.nupkg");
    if (File.Exists(cacheFile)) return await File.ReadAllBytesAsync(cacheFile);
    var url = $"https://api.nuget.org/v3-flatcontainer/{package}/{version}/{package}.{version}.nupkg";
    Console.Error.WriteLine($"  downloading {package}@{version}");
    var bytes = await http.GetByteArrayAsync(url);
    await File.WriteAllBytesAsync(cacheFile, bytes);
    return bytes;
}

record Family(string Pack, string ReferencePackage, string RuntimePackage);
record CatalogTarget(string Tfm, string Version, Row[] Rows);
record Row(string Tfm, string Pack, string Assembly, string File, string Kind,
    string? ForwardsTo, string Version, int PublicTypes, bool InReferencePack, bool HasImplementation, string PackVersion);
record AssemblyInfo(string Name, string File, string Version, int PublicTypes, bool IsFacade, string? DominantForwardTarget);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Row))]
[JsonSerializable(typeof(string))]
partial class CatalogJsonContext : JsonSerializerContext;
