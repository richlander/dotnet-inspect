#:project ../src/DotnetInspector.Core/DotnetInspector.Core.csproj
#:project ../src/DotnetInspector.Packages/DotnetInspector.Packages.csproj
#:project ../src/DotnetInspector.Services/DotnetInspector.Services.csproj

using DotnetInspector.Core;
using DotnetInspector.Packages;
using DotnetInspector.Services;

if (args.Length > 1)
    throw new ArgumentException("Usage: dotnet run eng/prepare-resource-triage-corpus.cs -- [output-file]");

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

HttpClientFactory.Initialize();
NuGetCache.Initialize("dotnet-inspect");

var assemblies = new List<string>(packages.Length);
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
else
    foreach (string assembly in assemblies)
        Console.WriteLine(assembly);
