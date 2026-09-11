#:project ../src/NuGetFetch/NuGetFetch.csproj
#:project ../src/DotnetInspector.Services/DotnetInspector.Services.csproj

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Services;
using NuGetFetch;

const string PlatformVersion = "11.0.0-preview.6.26359.118";
if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Usage: dotnet run eng/prepare-signature-decode-corpus.cs -- "
        + "<dotnet-root-containing-preview-6-packs> <new-output-directory>");
    return 2;
}

string dotnetRoot = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
string pinPath = Path.GetFullPath("docs/data/nuget-top-packages.lock.json");
string[] platformRoots =
[
    $"shared/Microsoft.NETCore.App/{PlatformVersion}",
    $"shared/Microsoft.AspNetCore.App/{PlatformVersion}",
    $"packs/Microsoft.NETCore.App.Ref/{PlatformVersion}/ref/net11.0",
    $"packs/Microsoft.AspNetCore.App.Ref/{PlatformVersion}/ref/net11.0",
];
foreach (string relative in platformRoots)
{
    if (!Directory.Exists(Path.Combine(dotnetRoot, relative)))
    {
        Console.Error.WriteLine($"Required pinned platform directory is absent: {relative}");
        return 2;
    }
}
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
{
    Console.Error.WriteLine("The output directory must be new or empty.");
    return 2;
}

using JsonDocument pin = JsonDocument.Parse(await File.ReadAllTextAsync(pinPath));
JsonElement packages = pin.RootElement.GetProperty("packages");
if (packages.GetArrayLength() != 100)
    throw new InvalidDataException("The baseline requires exactly 100 pinned package entries.");

Directory.CreateDirectory(output);
Directory.CreateDirectory(Path.Combine(output, "assemblies"));
var records = new List<AssemblyRecord>();
var packageRecords = new List<PackageRecord>();
foreach (string relative in platformRoots)
{
    foreach (string path in Directory.EnumerateFiles(
        Path.Combine(dotnetRoot, relative), "*.dll").Order(StringComparer.Ordinal))
    {
        await AddAssembly(path, "platform", $"{relative}/{Path.GetFileName(path)}");
    }
}

using var http = new HttpClient();
var client = new NuGetClient(http);
var packageHashes = new HashSet<string>(StringComparer.Ordinal);
foreach (JsonElement package in packages.EnumerateArray())
{
    string id = package.GetProperty("package").GetString()
        ?? throw new InvalidDataException("A pinned package ID is missing.");
    string version = package.GetProperty("version").GetString()
        ?? throw new InvalidDataException("A pinned package version is missing.");
    string archivePath = Path.Combine(output, "current.nupkg");
    DirectoryInfo extracted = Directory.CreateTempSubdirectory("signature-census-");
    try
    {
        await client.DownloadToFileAsync(id, version, archivePath);
        string archiveHash = await HashFile(archivePath);
        PackageExtractor.Extract(archivePath, extracted.FullName);
        var selection = TfmSelector.SelectPackageLibrary(extracted.FullName, id, null);
        if (package.GetProperty("status").GetString() == "pinned")
        {
            string expected = package.GetProperty("sha256").GetString()
                ?? throw new InvalidDataException("A pinned primary assembly hash is missing.");
            if (!selection.IsSelected
                || !string.Equals(
                    await HashFile(selection.Paths[0]), expected, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The primary assembly pin differs for {id}@{version}.");
            }
        }

        int assemblies = 0;
        string lib = Path.Combine(extracted.FullName, "lib");
        if (Directory.Exists(lib))
        {
            foreach (string path in Directory.EnumerateFiles(
                lib, "*.dll", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(extracted.FullName, path)
                    .Replace(Path.DirectorySeparatorChar, '/');
                string hash = await HashFile(path);
                assemblies++;
                if (packageHashes.Add(hash))
                    await AddAssembly(path, "packages", $"{id}/{version}/{relative}", hash);
            }
        }
        packageRecords.Add(new(id, version, archiveHash, assemblies));
        Console.WriteLine($"{packageRecords.Count}/100: {id}@{version}: {assemblies} lib assemblies");
    }
    finally
    {
        File.Delete(archivePath);
        extracted.Delete(recursive: true);
    }
}

AssemblyRecord[] ordered = records.OrderBy(record => record.Tier, StringComparer.Ordinal)
    .ThenBy(record => record.Identity, StringComparer.Ordinal).ToArray();
TierRecord[] tiers = ordered.GroupBy(record => record.Tier)
    .Select(group => new TierRecord(
        group.Key,
        group.Count(),
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Concat(group.Select(record => record.Sha256)
                .Order(StringComparer.Ordinal).Select(hash => hash + "\n")))))))
    .ToArray();
var manifest = new CorpusManifest(1, PlatformVersion,
    "SHA-256 of ordinal-sorted lowercase file SHA-256 values, each followed by LF",
    await HashFile(pinPath),
    packageRecords.ToArray(), tiers, ordered);
await File.WriteAllTextAsync(Path.Combine(output, "manifest.json"),
    JsonSerializer.Serialize(manifest, CorpusJsonContext.Default.CorpusManifest));
Console.WriteLine(JsonSerializer.Serialize(tiers, CorpusJsonContext.Default.TierRecordArray));
return 0;

async Task<string> HashFile(string path)
{
    await using FileStream stream = File.OpenRead(path);
    return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
}

async Task AddAssembly(string path, string tier, string identity, string? knownHash = null)
{
    string hash = knownHash ?? await HashFile(path);
    string relative = $"assemblies/{hash}.dll";
    string destination = Path.Combine(output, relative);
    if (!File.Exists(destination))
        File.Copy(path, destination);
    records.Add(new(tier, identity, relative, hash));
}

sealed record AssemblyRecord(string Tier, string Identity, string Path, string Sha256);
sealed record PackageRecord(string Package, string Version, string Sha256, int LibAssemblies);
sealed record TierRecord(string Tier, int Assemblies, string OrderedSha256);
sealed record CorpusManifest(int SchemaVersion, string PlatformVersion,
    string FingerprintAlgorithm, string PinSha256,
    PackageRecord[] Packages, TierRecord[] Tiers, AssemblyRecord[] Assemblies);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(CorpusManifest))]
[JsonSerializable(typeof(TierRecord[]))]
partial class CorpusJsonContext : JsonSerializerContext;
