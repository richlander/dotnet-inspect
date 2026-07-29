#:project ../src/DotnetInspector.Core/DotnetInspector.Core.csproj
#:project ../src/DotnetInspector.Packages/DotnetInspector.Packages.csproj
#:project ../src/DotnetInspector.Services/DotnetInspector.Services.csproj

using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using DotnetInspector.Services;

// The pool must be reproducible. Resolving "latest" on every run meant a fresh
// sweep measured different code than any recorded run, so its pool identity could
// never match a baseline's and the authored-corpus ratchet had nothing to compare
// against (#3353). Versions now come from a committed lockfile; refreshing it is a
// deliberate, reviewable act that records a new baseline.
bool refreshPin = args.Contains("--refresh-pin");
bool resolveLatest = args.Contains("--resolve-latest") || refreshPin;
string[] positional = args
    .Where(argument => argument is not ("--refresh-pin" or "--resolve-latest"))
    .ToArray();
if (positional.Length is < 1 or > 3 || args.Any(argument =>
        argument.StartsWith("--", StringComparison.Ordinal)
        && argument is not ("--refresh-pin" or "--resolve-latest")))
{
    throw new ArgumentException(
        "Usage: dotnet run eng/prepare-decompiler-package-sweep.cs -- "
        + "<output-directory> [start-rank] [package-count] [--resolve-latest] [--refresh-pin]");
}

string root = FindRepositoryRoot(Directory.GetCurrentDirectory());
string outputDirectory = Path.GetFullPath(positional[0]);
int startRank = positional.Length >= 2 ? int.Parse(positional[1]) : 1;
int packageCount = positional.Length >= 3 ? int.Parse(positional[2]) : 10;
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

string pinPath = Path.Combine(root, "docs", "data", "nuget-top-packages.lock.json");
var pinFile = File.Exists(pinPath)
    ? JsonSerializer.Deserialize<PackagePinFile>(
        await File.ReadAllTextAsync(pinPath),
        jsonContext.PackagePinFile)
      ?? throw new InvalidDataException($"Could not read pin file '{pinPath}'.")
    : null;
if (pinFile is not null)
{
    if (pinFile.Packages.Any(pin => string.IsNullOrWhiteSpace(pin.Package)))
        throw new InvalidDataException($"Pin file '{pinPath}' contains an entry without a package name.");
    if (pinFile.Packages.Any(pin => pin.Status == "pinned" && string.IsNullOrWhiteSpace(pin.Version)))
        throw new InvalidDataException($"Pin file '{pinPath}' pins a package without a version.");
    if (pinFile.Packages.Select(pin => pin.Package).Distinct(StringComparer.OrdinalIgnoreCase).Count()
        != pinFile.Packages.Count)
        throw new InvalidDataException($"Pin file '{pinPath}' pins the same package twice.");
}
else if (!resolveLatest)
{
    // Refusing is the point. Falling back to "latest" when the pin is missing would
    // reproduce exactly the drift the pin exists to remove, and would do it silently.
    // Reported and returned rather than thrown: an unhandled exception here exits 134,
    // and #3349 spent a round on a gate that core-dumped where it promised a reported
    // failure.
    Console.Error.WriteLine(
        $"Error: pin file '{pinPath}' not found, so the sweep cannot be reproducible. "
        + "Pass --refresh-pin to record one, or --resolve-latest for a deliberately unpinned run.");
    Environment.ExitCode = 1;
    return;
}

var pins = (pinFile?.Packages ?? [])
    .ToDictionary(pin => pin.Package, StringComparer.OrdinalIgnoreCase);

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

int unreproducible = 0;
var results = new List<SweepPackageResult>(selected.Length);
var assemblies = new List<string>(selected.Length);
foreach (var entry in selected)
{
    PackageExtractionResult? package = null;
    int resultIndex = results.Count;
    try
    {
        pins.TryGetValue(entry.Package, out var pin);
        if (pin is not null && pin.Status == "no-library" && !resolveLatest)
        {
            // Skipped without acquiring: the pin already records that this version
            // contributes no assembly, so fetching it could only confirm that at the
            // cost of a network round trip -- and a later version quietly starting to
            // ship a library would change the pool, which is what refreshing the pin is
            // for.
            results.Add(Failed(
                entry, "no-library-by-pin", pin.Detail, entry.Package, pin.Version, pin.Tfm));
            continue;
        }

        if (pin is null && !resolveLatest)
        {
            // A selected package the pin does not cover is a hole in the pool's
            // identity, not a missing nicety: the run would measure whatever shipped
            // today. Recorded and failed rather than skipped.
            results.Add(Failed(entry, "unpinned", $"'{entry.Package}' is not in {Path.GetFileName(pinPath)}."));
            Console.Error.WriteLine(
                $"rank {entry.Rank}: {entry.Package}: not pinned; run with --refresh-pin to record a version.");
            unreproducible++;
            continue;
        }

        var outcome = await PackageExtractor.ExtractPackageAsync(
            HttpClientFactory.Shared,
            entry.Package,
            tempDirPrefix: "decompiler-package-sweep",
            version: pin?.Version,
            forceLatest: pin is null);
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

        // A pin is only a pin if the thing that arrived is the thing named. NuGet can
        // serve a different version for a request it considers equivalent, and the
        // selected TFM can move when TfmSelector changes even though the package did
        // not -- both change the assemblies measured, which is what the pool identity
        // is for.
        if (pin is not null)
        {
            string? mismatch =
                !string.Equals(package.Version, pin.Version, StringComparison.OrdinalIgnoreCase)
                    ? $"pinned version {pin.Version}, got {package.Version ?? "none"}"
                    : pin.Tfm is not null
                        && !string.Equals(selection.Tfm, pin.Tfm, StringComparison.OrdinalIgnoreCase)
                        ? $"pinned TFM {pin.Tfm}, got {selection.Tfm ?? "none"}"
                        : null;
            if (mismatch is not null)
            {
                results.Add(Failed(
                    entry, "pin-mismatch", mismatch, resolvedPackage, package.Version, selection.Tfm,
                    package.FromCache));
                Console.Error.WriteLine($"rank {entry.Rank}: {entry.Package}: {mismatch}");
                unreproducible++;
                continue;
            }
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
        if (package?.TempDir is null)
        {
            RecordCleanup("not-required", detail: null);
        }
        else
        {
            try
            {
                Directory.Delete(package.TempDir, recursive: true);
                RecordCleanup("deleted", detail: null);
            }
            catch (IOException ex)
            {
                RecordCleanupFailure(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                RecordCleanupFailure(ex);
            }
        }
    }

    void RecordCleanupFailure(Exception exception)
    {
        string detail = $"{exception.GetType().Name}: {exception.Message}";
        RecordCleanup("failed", detail);
        Console.Error.WriteLine(
            $"rank {entry.Rank}: {entry.Package}: temporary-directory cleanup failed: {detail}");
    }

    void RecordCleanup(string status, string? detail)
    {
        if (results.Count > resultIndex)
        {
            results[resultIndex] = results[resultIndex] with
            {
                CleanupStatus = status,
                CleanupDetail = detail,
            };
        }
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

if (refreshPin)
{
    // Merged, not replaced. A refresh over a rank window (--refresh-pin 1 3) rewrote
    // the file with only those three packages and silently dropped the other 88 --
    // a shortened pin is a pool that quietly stops being reproducible where nobody
    // is looking. Packages outside this run's selection keep the version they had.
    var merged = new Dictionary<string, PackagePin>(pins, StringComparer.OrdinalIgnoreCase);
    foreach (var result in results)
    {
        PackagePin? recordedPin = result.Status switch
        {
            "selected" when result.ResolvedVersion is not null =>
                new PackagePin(result.RequestedPackage, result.ResolvedVersion, result.Tfm),
            // A package that reproducibly yields no library is pinned as such, so the
            // EVIL sweep neither acquires it nor fails over it. Nine of the top hundred
            // are meta-packages or have an ambiguous primary library; without this they
            // would read as "unpinned" forever and the pool could never be clean.
            "library-unavailable" => new PackagePin(
                result.RequestedPackage, result.ResolvedVersion, result.Tfm, "no-library", result.Detail),
            // Acquisition failures say nothing reproducible about the package, only
            // about the network at the time, so they record nothing: an existing pin
            // keeps its version, and a package that had none stays unpinned. Leaving it
            // unpinned is the safe direction -- the next sweep refuses it rather than
            // measuring a version nobody chose. An earlier draft spelled this as
            // "?? merged[key]", which threw KeyNotFoundException and exited 134 for
            // exactly the package this arm exists to handle.
            _ => null,
        };

        if (recordedPin is not null)
            merged[result.RequestedPackage] = recordedPin;
    }

    var recorded = merged.Values
        .OrderBy(pin => pin.Package, StringComparer.Ordinal)
        .ToArray();
    // Rank lives in nuget-top-packages.json and is deliberately not repeated here.
    // Two files stating the same rank is two things to keep in step, and the one that
    // drifts is the one nothing reads.
    await File.WriteAllTextAsync(
        pinPath,
        JsonSerializer.Serialize(
            // No timestamp: the file is a pure function of the pins, so re-recording an
            // unchanged pool produces a byte-identical file and a diff means something
            // actually moved. The sweep manifest already carries generatedAtUtc, and
            // #3349 found that hashing a file with a timestamp in it yields an identity
            // that never repeats.
            new PackagePinFile(SchemaVersion: 1, Packages: recorded),
            jsonContext.PackagePinFile) + Environment.NewLine);
    Console.WriteLine($"Recorded {recorded.Length} pinned packages in {Path.GetRelativePath(root, pinPath)}.");
}

Console.WriteLine(
    $"Selected {assemblies.Count} of {selected.Length} requested packages; "
    + $"manifest: {Path.Combine(outputDirectory, "manifest.json")}");
if (assemblies.Count == 0)
    Environment.ExitCode = 1;
if (unreproducible > 0)
{
    // Not merely reported. A pool that silently measured something other than what was
    // pinned is the defect this file exists to prevent, so it ends the run.
    Console.Error.WriteLine(
        $"{unreproducible} package(s) could not be acquired as pinned; the pool is not reproducible.");
    Environment.ExitCode = 1;
}

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

/// <summary>
/// A committed pin of the exact package versions the sweep acquires, so that two runs
/// measure the same code and their pool identities can be compared.
/// </summary>
sealed record PackagePinFile(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("packages")] IReadOnlyList<PackagePin> Packages);

/// <summary>
/// One ranked package's pinned outcome. <c>Status</c> is <c>pinned</c> when the package
/// yields a library, or <c>no-library</c> when it reproducibly does not -- a
/// meta-package with no assemblies, or one whose primary library is ambiguous. Recording
/// the second kind is what lets the sweep tell "known to contribute nothing" apart from
/// "nobody pinned this", which are the same absence but opposite meanings.
/// </summary>
sealed record PackagePin(
    [property: JsonPropertyName("package")] string Package,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("tfm")] string? Tfm,
    [property: JsonPropertyName("status")] string Status = "pinned",
    [property: JsonPropertyName("detail")] string? Detail = null);

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
    bool? FromCache,
    string? CleanupStatus = null,
    string? CleanupDetail = null);

[JsonSerializable(typeof(List<PackageListEntry>))]
[JsonSerializable(typeof(PackagePinFile))]
[JsonSerializable(typeof(PackageSweepManifest))]
sealed partial class PackageSweepJsonContext : JsonSerializerContext;
