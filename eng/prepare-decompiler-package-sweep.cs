#:project ../src/DotnetInspector.Core/DotnetInspector.Core.csproj
#:project ../src/DotnetInspector.Packages/DotnetInspector.Packages.csproj
#:project ../src/DotnetInspector.Services/DotnetInspector.Services.csproj

using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using DotnetInspector.Services;

if (args.Length is < 1 or > 3)
{
    throw new ArgumentException(
        "Usage: dotnet run eng/prepare-decompiler-package-sweep.cs -- "
        + "<output-directory> [start-rank] [package-count]");
}

string root = FindRepositoryRoot(Directory.GetCurrentDirectory());
string outputDirectory = Path.GetFullPath(args[0]);
int startRank = args.Length >= 2 ? int.Parse(args[1]) : 1;
int packageCount = args.Length >= 3 ? int.Parse(args[2]) : 10;
if (startRank <= 0)
    throw new ArgumentOutOfRangeException(nameof(startRank), "Start rank must be positive.");
if (packageCount is <= 0 or > 100)
    throw new ArgumentOutOfRangeException(nameof(packageCount), "Package count must be between 1 and 100.");

string sourcePath = Path.Combine(root, "docs", "data", "nuget-top-packages.json");
var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
};
var jsonContext = new PackageSweepJsonContext(jsonOptions);
var packageList = JsonSerializer.Deserialize<List<PackageListEntry>>(
    await File.ReadAllTextAsync(sourcePath),
    jsonContext.ListPackageListEntry)
    ?? throw new InvalidDataException($"Could not read package list '{sourcePath}'.");
if (packageList.Any(entry => entry.Rank <= 0 || string.IsNullOrWhiteSpace(entry.Package)))
    throw new InvalidDataException($"Package list '{sourcePath}' contains an invalid entry.");
if (packageList.Select(entry => entry.Rank).Distinct().Count() != packageList.Count)
    throw new InvalidDataException($"Package list '{sourcePath}' contains duplicate ranks.");

var selected = packageList
    .Where(entry => entry.Rank >= startRank)
    .OrderBy(entry => entry.Rank)
    .Take(packageCount)
    .ToArray();
if (selected.Length == 0)
    throw new InvalidOperationException($"No packages were selected at or after rank {startRank}.");

Directory.CreateDirectory(outputDirectory);
string packageDirectory = Path.Combine(outputDirectory, "packages");
Directory.CreateDirectory(packageDirectory);

HttpClientFactory.Initialize();
NuGetCache.Initialize("dotnet-inspect");

var results = new List<SweepPackageResult>(selected.Length);
var assemblies = new List<string>(selected.Length);
foreach (var entry in selected)
{
    PackageExtractionResult? package = null;
    try
    {
        var outcome = await PackageExtractor.ExtractPackageAsync(
            HttpClientFactory.Shared,
            $"{entry.Package}@latest",
            tempDirPrefix: "decompiler-package-sweep",
            forceLatest: true);
        if (!outcome.IsSuccess)
        {
            results.Add(Failed(entry, "acquisition-failed", outcome.ErrorMessage));
            Console.Error.WriteLine(
                $"rank {entry.Rank}: {entry.Package}: acquisition failed: {outcome.ErrorMessage}");
            continue;
        }

        package = outcome.Result!;
        string resolvedPackage = package.PackageName ?? entry.Package;
        var selection = TfmSelector.SelectPackageLibrary(
            package.ExtractPath,
            resolvedPackage,
            requestedLibrary: null);
        if (!selection.IsSelected)
        {
            string detail = selection.CandidatePaths.Count > 0
                ? $"{selection.Status}: {string.Join(", ", selection.CandidatePaths.Select(Path.GetFileName))}"
                : selection.Status.ToString();
            results.Add(Failed(
                entry,
                "library-unavailable",
                detail,
                resolvedPackage,
                package.Version,
                selection.Tfm,
                package.FromCache));
            Console.Error.WriteLine(
                $"rank {entry.Rank}: {entry.Package}: primary library unavailable: {detail}");
            continue;
        }

        string source = selection.Paths[0];
        string destinationDirectory = Path.Combine(
            packageDirectory,
            $"{entry.Rank:D3}-{SafePathSegment(entry.Package)}",
            SafePathSegment(package.Version ?? "unknown"));
        Directory.CreateDirectory(destinationDirectory);
        string destination = Path.Combine(
            destinationDirectory,
            Path.GetFileName(source));
        File.Copy(source, destination, overwrite: true);
        destination = Path.GetFullPath(destination);
        assemblies.Add(destination);
        results.Add(new SweepPackageResult(
            entry.Rank,
            entry.Package,
            entry.Downloads,
            "selected",
            Detail: null,
            resolvedPackage,
            package.Version,
            selection.Tfm,
            Path.GetRelativePath(outputDirectory, destination),
            package.FromCache));
        Console.WriteLine(
            $"rank {entry.Rank}: {entry.Package}@{package.Version}: "
            + $"{selection.Tfm ?? "unknown TFM"} -> {Path.GetFileName(destination)}");
    }
    catch (HttpRequestException ex)
    {
        RecordProcessingFailure(entry, package, ex);
    }
    catch (InvalidDataException ex)
    {
        RecordProcessingFailure(entry, package, ex);
    }
    catch (IOException ex)
    {
        RecordProcessingFailure(entry, package, ex);
    }
    catch (UnauthorizedAccessException ex)
    {
        RecordProcessingFailure(entry, package, ex);
    }
    finally
    {
        if (package?.TempDir is not null)
            Directory.Delete(package.TempDir, recursive: true);
    }
}

assemblies.Sort(StringComparer.Ordinal);
await File.WriteAllLinesAsync(
    Path.Combine(outputDirectory, "assemblies.txt"),
    assemblies);
var manifest = new PackageSweepManifest(
    SchemaVersion: 1,
    GeneratedAtUtc: DateTimeOffset.UtcNow,
    Source: Path.GetRelativePath(root, sourcePath),
    StartRank: startRank,
    RequestedPackageCount: packageCount,
    SelectedPackageCount: assemblies.Count,
    Packages: results);
await File.WriteAllTextAsync(
    Path.Combine(outputDirectory, "manifest.json"),
    JsonSerializer.Serialize(manifest, jsonContext.PackageSweepManifest) + Environment.NewLine);

Console.WriteLine(
    $"Selected {assemblies.Count} of {selected.Length} requested packages; "
    + $"manifest: {Path.Combine(outputDirectory, "manifest.json")}");
if (assemblies.Count == 0)
    Environment.ExitCode = 1;

void RecordProcessingFailure(
    PackageListEntry entry,
    PackageExtractionResult? package,
    Exception exception)
{
    results.Add(Failed(
        entry,
        "processing-failed",
        $"{exception.GetType().Name}: {exception.Message}",
        package?.PackageName,
        package?.Version,
        fromCache: package?.FromCache));
    Console.Error.WriteLine(
        $"rank {entry.Rank}: {entry.Package}: processing failed: "
        + $"{exception.GetType().Name}: {exception.Message}");
}

static SweepPackageResult Failed(
    PackageListEntry entry,
    string status,
    string? detail,
    string? resolvedPackage = null,
    string? resolvedVersion = null,
    string? tfm = null,
    bool? fromCache = null)
    => new(
        entry.Rank,
        entry.Package,
        entry.Downloads,
        status,
        detail,
        resolvedPackage,
        resolvedVersion,
        tfm,
        AssemblyPath: null,
        fromCache);

static string FindRepositoryRoot(string start)
{
    for (var directory = new DirectoryInfo(start);
        directory is not null;
        directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
            return directory.FullName;
    }

    throw new InvalidOperationException(
        $"Could not find the repository root from '{start}'.");
}

static string SafePathSegment(string value)
{
    var invalid = Path.GetInvalidFileNameChars().ToHashSet();
    string sanitized = new(value
        .Select(character => invalid.Contains(character)
            || character is '/' or '\\'
                ? '_'
                : character)
        .ToArray());
    return sanitized is "" or "." or ".." ? "_" : sanitized;
}

sealed record PackageListEntry(
    [property: JsonPropertyName("rank")] int Rank,
    [property: JsonPropertyName("package")] string Package,
    [property: JsonPropertyName("downloads")] long Downloads);

sealed record PackageSweepManifest(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string Source,
    int StartRank,
    int RequestedPackageCount,
    int SelectedPackageCount,
    IReadOnlyList<SweepPackageResult> Packages);

sealed record SweepPackageResult(
    int Rank,
    string RequestedPackage,
    long Downloads,
    string Status,
    string? Detail,
    string? ResolvedPackage,
    string? ResolvedVersion,
    string? Tfm,
    string? AssemblyPath,
    bool? FromCache);

[JsonSerializable(typeof(List<PackageListEntry>))]
[JsonSerializable(typeof(PackageSweepManifest))]
sealed partial class PackageSweepJsonContext : JsonSerializerContext;
