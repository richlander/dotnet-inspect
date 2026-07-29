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
// Bad input is reported, not thrown. An unhandled exception here leaves exit 134,
// which is the shape this file exists to remove: a caller reading the exit code sees
// a crash where the contract promises a stated refusal.
const string UsageText =
    "Usage: dotnet run eng/prepare-decompiler-package-sweep.cs -- "
    + "<output-directory> [start-rank] [package-count] [--resolve-latest] [--refresh-pin]";
if (positional.Length is < 1 or > 3 || args.Any(argument =>
        argument.StartsWith("--", StringComparison.Ordinal)
        && argument is not ("--refresh-pin" or "--resolve-latest")))
{
    Console.Error.WriteLine(UsageText);
    Environment.ExitCode = 2;
    return;
}

string root = FindRepositoryRoot(Directory.GetCurrentDirectory());
string outputDirectory = Path.GetFullPath(positional[0]);
int startRank = 1;
int packageCount = 10;
if (positional.Length >= 2 && !int.TryParse(positional[1], out startRank))
{
    Console.Error.WriteLine($"Start rank '{positional[1]}' is not a number.{Environment.NewLine}{UsageText}");
    Environment.ExitCode = 2;
    return;
}

if (positional.Length >= 3 && !int.TryParse(positional[2], out packageCount))
{
    Console.Error.WriteLine($"Package count '{positional[2]}' is not a number.{Environment.NewLine}{UsageText}");
    Environment.ExitCode = 2;
    return;
}

if (startRank <= 0)
{
    Console.Error.WriteLine("Start rank must be positive.");
    Environment.ExitCode = 2;
    return;
}

if (packageCount is <= 0 or > 100)
{
    Console.Error.WriteLine("Package count must be between 1 and 100.");
    Environment.ExitCode = 2;
    return;
}

string sourcePath = Path.Combine(root, "docs", "data", "nuget-top-packages.json");
var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
};
var jsonContext = new PackageSweepJsonContext(jsonOptions);
string pinPath = Path.Combine(root, "docs", "data", "nuget-top-packages.lock.json");

// Reported rather than thrown, for the same reason as the usage errors below: a
// caller cannot tell exit 134 from a crash, and unreadable input is a refusal the
// contract makes on purpose. That covers text these files are not -- a JSON scalar
// deserializes to null without throwing, and a syntax error throws -- because either
// way the sweep has no list and no pin to work from.
List<PackageListEntry>? parsedList = null;
PackagePinFile? pinFile = null;
string? unreadable = null;
try
{
    parsedList = JsonSerializer.Deserialize<List<PackageListEntry>>(
        await File.ReadAllTextAsync(sourcePath),
        jsonContext.ListPackageListEntry);
    if (parsedList is null)
    {
        unreadable = $"Package list '{sourcePath}' is not a list of packages.";
    }
    else if (File.Exists(pinPath))
    {
        pinFile = JsonSerializer.Deserialize<PackagePinFile>(
            await File.ReadAllTextAsync(pinPath),
            jsonContext.PackagePinFile);
        if (pinFile is null)
        {
            unreadable = $"Pin file '{pinPath}' is not a pin file.";
        }
    }
}
catch (JsonException ex)
{
    unreadable = $"Could not parse '{(parsedList is null ? sourcePath : pinPath)}': {ex.Message}";
}

if (unreadable is not null)
{
    Console.Error.WriteLine(unreadable);
    Environment.ExitCode = 2;
    return;
}

var packageList = parsedList;

if (packageList.Any(entry => entry.Rank <= 0 || string.IsNullOrWhiteSpace(entry.Package)))
{
    Console.Error.WriteLine($"Package list '{sourcePath}' contains an invalid entry.");
    Environment.ExitCode = 2;
    return;
}

if (packageList.Select(entry => entry.Rank).Distinct().Count() != packageList.Count)
{
    Console.Error.WriteLine($"Package list '{sourcePath}' contains duplicate ranks.");
    Environment.ExitCode = 2;
    return;
}
// Distinct ranks are not distinct packages. One package listed at two ranks acquires
// the same assembly twice, so the pool is a hundred slots holding ninety-nine
// packages -- and every count in sight still says a hundred. A padded pool measures
// one package's methods twice, which skews the ratchet exactly like a shortened one.
if (packageList
        .Select(entry => entry.Package)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count() != packageList.Count)
{
    string duplicates = string.Join(", ", packageList
        .GroupBy(entry => entry.Package, StringComparer.OrdinalIgnoreCase)
        .Where(group => group.Count() > 1)
        .Select(group => $"{group.Key} (ranks {string.Join("/", group.Select(entry => entry.Rank))})"));
    Console.Error.WriteLine(
        $"Package list '{sourcePath}' ranks the same package more than once: {duplicates}.");
    Environment.ExitCode = 2;
    return;
}

if (pinFile is not null)
{
    // Reported, like every other refusal in this file. A malformed pin is a stated
    // refusal, and a caller cannot tell exit 134 from a crash.
    string? malformed =
        pinFile.Packages is null
            ? "states no packages"
            : pinFile.Packages.Any(pin => pin is null)
                ? "contains a null entry"
                : pinFile.Packages.Any(pin => string.IsNullOrWhiteSpace(pin.Package))
                    ? "contains an entry without a package name"
                    : pinFile.Packages.Any(pin => string.IsNullOrWhiteSpace(pin.Version))
                        ? "pins a package without a version"
                        : pinFile.Packages.Select(pin => pin.Package)
                            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != pinFile.Packages.Count
                            ? "pins the same package twice"
                            : null;
    if (malformed is not null)
    {
        Console.Error.WriteLine($"Pin file '{pinPath}' {malformed}.");
        Environment.ExitCode = 2;
        return;
    }
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
{
    Console.Error.WriteLine(
        $"No packages were selected at or after rank {startRank}; "
        + $"the list ranks {packageList.Count}.");
    Environment.ExitCode = 2;
    return;
}

// The pool's completeness is checked at the end by asking the pin what it owes, and
// that question only has an answer if every selected package has a pin the sweep
// understands. A package with no pin, or with a status this does not recognise, owes
// an unknown number of assemblies -- so its absence from the pool cancels against its
// absence from the total and the run reports success over a pool one package short.
// Refusing up front, before any acquisition, is what keeps "owed" well defined.
if (!resolveLatest)
{
    var uncovered = selected
        .Select(entry => (entry, pin: pins.GetValueOrDefault(entry.Package)))
        .Where(pair => pair.pin is null || pair.pin.Status is not ("pinned" or "no-library"))
        .ToArray();
    if (uncovered.Length > 0)
    {
        foreach (var (entry, pin) in uncovered)
        {
            Console.Error.WriteLine(pin is null
                ? $"rank {entry.Rank}: {entry.Package}: not pinned in {Path.GetFileName(pinPath)}; "
                    + "run with --refresh-pin to record a version."
                : $"rank {entry.Rank}: {entry.Package}: unknown pin status '{pin.Status}'.");
        }

        Console.Error.WriteLine(
            $"{uncovered.Length} of {selected.Length} selected packages are not covered by the pin; "
            + "the pool cannot be reproduced.");
        Environment.ExitCode = 1;
        return;
    }
}

Directory.CreateDirectory(outputDirectory);
string packageDirectory = Path.Combine(outputDirectory, "packages");
Directory.CreateDirectory(packageDirectory);

HttpClientFactory.Initialize();
NuGetCache.Initialize("dotnet-inspect");

// Counted where a package reaches the outcome its mode expects, not where it fails
// to. An incident counter has to be remembered at every failure site and silently
// passes the run when one forgets; this has a single site per outcome and a run that
// skips one comes up short, so forgetting fails closed.
int accountedFor = 0;
var results = new List<SweepPackageResult>(selected.Length);
var assemblies = new List<string>(selected.Length);
foreach (var entry in selected)
{
    PackageExtractionResult? package = null;
    int resultIndex = results.Count;
    try
    {
        pins.TryGetValue(entry.Package, out var pin);

        // --resolve-latest means what it says. Passing the pinned version here anyway
        // would make the discovery lane replay the pinned pool while its own comment
        // claimed it was sampling what ships today, and would leave --refresh-pin
        // unable to ever bump a version it had already recorded.
        bool honorPin = pin is not null && !resolveLatest;
        var outcome = await PackageExtractor.ExtractPackageAsync(
            HttpClientFactory.Shared,
            entry.Package,
            tempDirPrefix: "decompiler-package-sweep",
            version: honorPin ? pin!.Version : null,
            forceLatest: !honorPin);
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

            // A "no-library" pin is a claim about the package, so it is confirmed
            // against the package rather than believed. Nine of the top hundred are
            // meta-packages or have an ambiguous primary library; this is what makes
            // "contributes nothing" a checked outcome instead of a way to remove a
            // package from the pool by editing one word in a file.
            if (honorPin && pin!.Status == "no-library"
                && string.Equals(package.Version, pin.Version, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(Failed(
                    entry, "no-library-confirmed", detail, resolvedPackage, package.Version,
                    selection.Tfm, package.FromCache));
                accountedFor++;
                continue;
            }

            results.Add(Failed(
                entry,
                "library-unavailable",
                detail,
                resolvedPackage,
                package.Version,
                selection.Tfm,
                package.FromCache));
            // Without a pin to check against, this is a definite outcome rather than a
            // failure: the package was acquired and genuinely ships no primary library,
            // which is what a refresh records as "no-library". Acquiring nothing at all
            // is the failure, and it is counted nowhere.
            if (!honorPin)
            {
                accountedFor++;
            }

            Console.Error.WriteLine(
                $"rank {entry.Rank}: {entry.Package}: primary library unavailable: {detail}");
            continue;
        }

        // The other half of that claim: a package pinned as contributing nothing that
        // now does contribute changes the pool, so it fails rather than being quietly
        // absorbed.
        if (honorPin && pin!.Status == "no-library")
        {
            results.Add(Failed(
                entry, "pin-mismatch", "pinned as no-library but a primary library is available",
                resolvedPackage, package.Version, selection.Tfm, package.FromCache));
            Console.Error.WriteLine(
                $"rank {entry.Rank}: {entry.Package}: pinned as no-library but "
                + $"{Path.GetFileName(selection.Paths[0])} is available.");
            continue;
        }

        // A pin is only a pin if the thing that arrived is the thing named. NuGet can
        // serve a different version for a request it considers equivalent, and the
        // selected TFM can move when TfmSelector changes even though the package did
        // not -- both change the assemblies measured, which is what the pool identity
        // is for.
        if (honorPin && pin is not null)
        {
            string? mismatch =
                !string.Equals(package.Version, pin.Version, StringComparison.OrdinalIgnoreCase)
                    ? $"pinned version {pin.Version}, got {package.Version ?? "none"}"
                    // Compared even when the pin names no TFM. Skipping the check for a
                    // null pin TFM made it a wildcard that accepted whatever arrived,
                    // which is the one thing a pin must not do. Two packages in the pool
                    // genuinely select no TFM, and null matches null.
                    : !string.Equals(selection.Tfm, pin.Tfm, StringComparison.OrdinalIgnoreCase)
                        ? $"pinned TFM {pin.Tfm ?? "none"}, got {selection.Tfm ?? "none"}"
                        : null;
            if (mismatch is not null)
            {
                results.Add(Failed(
                    entry, "pin-mismatch", mismatch, resolvedPackage, package.Version, selection.Tfm,
                    package.FromCache));
                Console.Error.WriteLine($"rank {entry.Rank}: {entry.Package}: {mismatch}");
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
        accountedFor++;
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
// Only when there is no pin to ask. An empty pool is the sole signal available to a
// deliberately unpinned run, but under a pin it is the pin that says how many
// assemblies a window owes -- and a window of nothing but "no-library" entries owes
// none, so failing it would refuse a window the pin describes perfectly.
if (resolveLatest && assemblies.Count == 0)
{
    Console.Error.WriteLine("No assemblies were selected.");
    Environment.ExitCode = 1;
}

if (accountedFor != selected.Length)
{
    // Every selected package must have reached a definite outcome. Against the pin
    // that means the outcome the pin describes: a "pinned" entry an assembly at that
    // exact version and TFM, a "no-library" entry a confirmed absence at that version.
    // Resolving latest, it means the package was acquired and either yielded a library
    // or demonstrably ships none. A package that could not be acquired reaches no
    // outcome in either mode, so a refresh cannot quietly drop it from the pin.
    //
    // Comparing against the size of the selection rather than against a total derived
    // from the pin is the point -- a total read out of the same file the outcome is
    // judged against cancels a defect in that file, which is how a missing pin, and
    // then a flipped status, each exited 0 over a pool that was not the pinned one.
    string expectation = resolveLatest ? "could not be resolved" : "did not match the pin";
    Console.Error.WriteLine(
        $"{selected.Length - accountedFor} of {selected.Length} selected packages for ranks "
        + $"{startRank}-{selected[^1].Rank} {expectation}; the pool is not reproducible.");
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
