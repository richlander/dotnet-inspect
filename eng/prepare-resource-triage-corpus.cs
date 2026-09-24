#:project ../src/DotnetInspector.Networking/DotnetInspector.Networking.csproj
#:project ../src/DotnetInspector.Packages/DotnetInspector.Packages.csproj
#:project ../src/DotnetInspector.Services/DotnetInspector.Services.csproj

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using DotnetInspector.Networking;
using DotnetInspector.Packages;
using DotnetInspector.Services;

if (args.Length > 2)
{
    throw new ArgumentException(
        "Usage: dotnet run eng/prepare-resource-triage-corpus.cs -- "
        + "[output-file] [manifest-jsonl]");
}

(string Id, string Version)[] packages =
[
    ("QuanTAlib", "0.1.0"),
    ("System.Text.Json", "5.0.2"),
    ("MessagePack", "2.5.192"),
    ("MimeKit", "4.8.0"),
    ("ZLinq", "1.4.9"),
    ("Pipelines.Sockets.Unofficial", "2.2.8"),
    ("Npgsql", "8.0.4"),
    ("prometheus-net", "8.2.1"),
    ("TouchSocket", "3.1.5"),
];

string destination = Path.GetFullPath(
    Path.Combine("artifacts", "resource-triage-corpus"));
if (Directory.Exists(destination))
    Directory.Delete(destination, recursive: true);
Directory.CreateDirectory(destination);
string packageDestination = Path.Combine(destination, "packages");

HttpClientFactory.Initialize(new HttpClientFactoryOptions());
NuGetCache.Initialize("dotnet-inspect");

var assemblies = new List<string>(packages.Length);
var manifest = new List<string>(packages.Length);
foreach (var (id, version) in packages)
{
    PackageExtractionResult? package = null;
    try
    {
        var outcome = await PackageExtractor.ExtractPackageAsync(
            HttpClientFactory.Shared,
            $"{id}@{version}");
        if (!outcome.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Could not acquire {id}@{version}: {outcome.ErrorMessage}");
        }

        package = outcome.Result;
        var selection = TfmSelector.SelectPackageLibrary(
            package!.ExtractPath,
            id,
            requestedLibrary: null);
        if (!selection.IsSelected)
        {
            throw new InvalidOperationException(
                $"Could not select one primary library for {id}@{version}: "
                + selection.Status);
        }

        string source = selection.Paths[0];
        string target = Path.Combine(destination, Path.GetFileName(source));
        File.Copy(source, target);
        assemblies.Add(target);
        if (args.Length == 2)
        {
            if (package.NupkgPath is not { } nupkgPath
                || !File.Exists(nupkgPath))
            {
                throw new InvalidOperationException(
                    $"Package archive provenance is unavailable for "
                    + $"{id}@{version}.");
            }
            Directory.CreateDirectory(packageDestination);
            string packageTarget = Path.Combine(
                packageDestination,
                Path.GetFileName(nupkgPath));
            File.Copy(nupkgPath, packageTarget);
            manifest.Add(
                ManifestRow(
                    id,
                    version,
                    Path.GetRelativePath(
                            package.ExtractPath,
                            source)
                        .Replace('\\', '/'),
                    Path.GetFileName(target),
                    Sha256(target),
                    Sha256(packageTarget),
                    Path.GetRelativePath(
                            Environment.CurrentDirectory,
                            packageTarget)
                        .Replace('\\', '/')));
        }
    }
    finally
    {
        if (package?.TempDir is not null)
            Directory.Delete(package.TempDir, recursive: true);
    }
}

assemblies.Sort(StringComparer.Ordinal);
if (args.Length == 1)
    await File.WriteAllLinesAsync(args[0], assemblies);
else if (args.Length == 2)
{
    await File.WriteAllLinesAsync(args[0], assemblies);
    await File.WriteAllLinesAsync(args[1], manifest);
}
else
    foreach (string assembly in assemblies)
        Console.WriteLine(assembly);

static string Sha256(string path)
{
    using FileStream stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream))
        .ToLowerInvariant();
}

static string ManifestRow(
    string packageId,
    string version,
    string selectedAsset,
    string assemblyFile,
    string assemblySha256,
    string packageSha256,
    string packageFile)
{
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream))
    {
        writer.WriteStartObject();
        writer.WriteString("package_id", packageId);
        writer.WriteString("version", version);
        writer.WriteString("selected_asset", selectedAsset);
        writer.WriteString("assembly_file", assemblyFile);
        writer.WriteString("assembly_sha256", assemblySha256);
        writer.WriteString("package_sha256", packageSha256);
        writer.WriteString("package_file", packageFile);
        writer.WriteEndObject();
    }
    return Encoding.UTF8.GetString(stream.ToArray());
}
