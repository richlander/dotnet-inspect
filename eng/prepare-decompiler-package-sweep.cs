#:project ../src/DotnetInspector.Core/DotnetInspector.Core.csproj
#:project ../src/DotnetInspector.Packages/DotnetInspector.Packages.csproj
#:project ../src/DotnetInspector.Services/DotnetInspector.Services.csproj

using System.Text;
using System.Security.Cryptography;
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
    + "<output-directory> [start-rank] [package-count] [--resolve-latest] [--refresh-pin]"
    + "\n   or: dotnet run eng/prepare-decompiler-package-sweep.cs -- --validate-pin <pin-file>..."
    + "\n   or: dotnet run eng/prepare-decompiler-package-sweep.cs -- --list-pin-rules";

// The names of the shape rules above, one per line, so that the suite holding them can
// ask which rules exist instead of keeping its own list of them. Round fourteen added a
// rule to PinRules with no tampered pin file behind it and the suite stayed green: a
// check no input ever reached, gated by nothing. Coverage is now a set equality against
// what this prints, so both directions fail -- a rule with no case, and a case naming a
// rule that is gone. It reads nothing and acquires nothing.
if (args is ["--list-pin-rules"])
{
    // Prefixed, like the verdicts are, because `dotnet run` puts build diagnostics on
    // stdout: a bare word per line would let a new compiler warning read as a rule.
    foreach (var (name, _) in PinRules())
        Console.Out.WriteLine($"Pin rule '{name}'.");

    Environment.ExitCode = 0;
    return;
}

// Rounds nine, ten and eleven each found the same thing: a rule EvilPoolPinTests
// enforced on the pin file that this sweep did not, so a file the suite refused ran
// here to exit 0. Two lists of rules over one file, and only one of them can stop a
// run. This mode makes the sweep's list the only list -- the suite hands it tampered
// pins and asserts the refusals, instead of restating the rules and drifting. It reads
// files and acquires nothing, so it needs no repository, no network and no cache.
// It takes many paths because the suite has many cases and one process is cheap.
//
// Selected by the first argument, not by containment: an output directory named
// '--validate-pin' used to switch the whole run into validation mode, and filtering the
// word out of the argument list meant a pin file by that name could never be named at
// all. Position says which one a caller meant; a set membership test cannot.
if (args is ["--validate-pin", ..])
{
    string[] candidates = args[1..];
    if (candidates.Length == 0
        || candidates.Any(candidate => candidate.StartsWith("--", StringComparison.Ordinal)))
    {
        Console.Error.WriteLine(UsageText);
        Environment.ExitCode = 2;
        return;
    }

    bool anyMalformed = false;
    foreach (string candidate in candidates)
    {
        string? problem = ValidatePinFile(candidate);
        anyMalformed |= problem is not null;
        // One line per path, in the order given, and never more than one: the reason can
        // quote the file's own bytes (a parser error echoes the text it choked on), and a
        // reason containing a newline could otherwise print a second line reading exactly
        // like a verdict. A caller matching output to input by position rather than by
        // parsing prose is then reading what this loop decided, not what a pin file wrote.
        Console.Out.WriteLine(OneLine(problem is null
            ? $"Pin file '{candidate}' is well formed."
            : $"Pin file '{candidate}' {problem}."));
    }

    Environment.ExitCode = anyMalformed ? 2 : 0;
    return;
}

if (positional.Length is < 1 or > 3 || args.Any(argument =>
        argument.StartsWith("--", StringComparison.Ordinal)
        && argument is not ("--refresh-pin" or "--resolve-latest")))
{
    Console.Error.WriteLine(UsageText);
    Environment.ExitCode = 2;
    return;
}

string? root = FindRepositoryRoot(Directory.GetCurrentDirectory());
if (root is null)
{
    // Run from outside a clone, this used to throw. The sweep reads its list and pin
    // from the repository, so it is a stated refusal like every other input problem.
    Console.Error.WriteLine(
        $"Could not find the repository root from '{Directory.GetCurrentDirectory()}'; "
        + "run this from inside the dotnet-inspect repository.");
    Environment.ExitCode = 2;
    return;
}
string outputDirectory;
try
{
    // An unset shell variable forwards as an empty argument, which is length one and
    // sails past the usage gate. Path.GetFullPath then threw, before the guard that
    // reports an unusable output directory ever ran -- so the one bad argument a caller
    // is most likely to produce by accident was the one that core-dumped.
    outputDirectory = Path.GetFullPath(positional[0]);
}
catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
{
    Console.Error.WriteLine(
        $"Output directory '{positional[0]}' is not a usable path: {ex.Message}"
        + $"{Environment.NewLine}{UsageText}");
    Environment.ExitCode = 2;
    return;
}

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
var jsonContext = SweepJsonContext();
string pinPath = Path.Combine(root, "docs", "data", "nuget-top-packages.lock.json");

// Reported rather than thrown, for the same reason as the usage errors below: a
// caller cannot tell exit 134 from a crash, and unreadable input is a refusal the
// contract makes on purpose. That covers text these files are not -- a JSON scalar
// deserializes to null without throwing, and a syntax error throws -- because either
// way the sweep has no list and no pin to work from.
//
// Both files are read by ReadBoundedText and the pin by ReadPinFile, which is the same
// function --validate-pin calls. Round twelve bounded the read that --validate-pin
// uses and left this path on File.ReadAllTextAsync, so a pin file too large to
// validate was still large enough to sweep with: the validator refused it at exit 2
// and the run it was validating accepted it at exit 0. Sharing Malformed made the two
// agree about a pin's shape while they still disagreed about which bytes were the pin.
List<PackageListEntry>? parsedList = null;
PackagePinFile? pinFile = null;
string? unreadable = null;

var (listText, listProblem) = ReadBoundedText(sourcePath);
if (listProblem is not null)
{
    unreadable = $"Package list '{sourcePath}' {listProblem}.";
}
else
{
    try
    {
        parsedList = JsonSerializer.Deserialize(listText!, jsonContext.ListPackageListEntry);
        if (parsedList is null)
            unreadable = $"Package list '{sourcePath}' is not a list of packages.";
    }
    catch (JsonException ex)
    {
        unreadable = $"Package list '{sourcePath}' could not be parsed: {ex.Message.TrimEnd('.')}.";
    }
}

if (unreadable is null && File.Exists(pinPath))
{
    string? pinProblem;
    (pinFile, pinProblem) = ReadPinFile(pinPath);
    if (pinProblem is not null)
        unreadable = $"Pin file '{pinPath}' {pinProblem}.";
}

if (unreadable is not null)
{
    Console.Error.WriteLine(unreadable);
    Environment.ExitCode = 2;
    return;
}

List<PackageListEntry> packageList = parsedList!;

// Checked before anything dereferences an entry. The deserializer will happily put a
// null in the list, and the pin file has guarded this since round two while its sibling
// did not -- so the very first validation dereferenced it and exited 134, in the one
// file whose stated purpose is that no input reaches 134.
if (packageList.Any(entry => entry is null))
{
    Console.Error.WriteLine($"Package list '{sourcePath}' contains a null entry.");
    Environment.ExitCode = 2;
    return;
}

if (packageList.Any(entry => entry.Rank <= 0 || string.IsNullOrWhiteSpace(entry.Package)))
{
    Console.Error.WriteLine($"Package list '{sourcePath}' contains an invalid entry.");
    Environment.ExitCode = 2;
    return;
}

// A package id is an id, not a package reference. The extractor accepts "id@version",
// so a list entry spelled that way acquired the embedded version while --resolve-latest
// claimed to be sampling what ships today -- and, because the two spellings are
// different strings, the same package could be ranked twice and pad the pool straight
// past the duplicate check. Refusing the spelling is narrower than teaching every
// downstream check to parse it.
var unbare = packageList.Where(entry => !IsBarePackageId(entry.Package)).ToArray();
if (unbare.Length > 0)
{
    Console.Error.WriteLine(
        $"Package list '{sourcePath}' names packages that are not bare NuGet ids: "
        + $"{string.Join(", ", unbare.Select(entry => $"'{entry.Package}' (rank {entry.Rank})"))}.");
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

// The pin's shape was checked by ReadPinFile, which is where a malformed pin is
// refused -- for the sweep and for --validate-pin alike, because it is one function.
// So reaching here with no pin means there is no pin file at all.
if (pinFile is null && !resolveLatest)
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

// Take() shortens silently, and every completeness check downstream is stated against
// the window that was actually selected -- so a list one entry shorter than the
// request produced a pool one assembly shorter and called it complete. The caller
// asked for a number of packages; supplying fewer is a refusal, not a smaller pool.
if (selected.Length != packageCount)
{
    Console.Error.WriteLine(
        $"Ranks {startRank}-{selected[^1].Rank} supply {selected.Length} of the "
        + $"{packageCount} packages requested; the list ranks {packageList.Count}.");
    Environment.ExitCode = 2;
    return;
}

// A count is not a window. Ranks are required to be positive and distinct, not
// contiguous, so a list missing rank 2 answered a request for ranks 1-2 with ranks 1
// and 3 -- the right number of packages, every one of them pinned, and a pool that is
// not the one the caller named. The caller asks for a rank range; the range is what
// must arrive.
// Nullable rather than a default sentinel. (0, 0) cannot name a real gap today --
// startRank is refused at or below zero and every rank must be positive -- but that
// makes "no gap" depend on two validations several hundred lines apart, and this
// change has already been bitten twice by a quantity that could stand for two things.
var gap = Enumerable.Range(0, selected.Length)
    .Where(index => selected[index].Rank != startRank + index)
    .Select(index => ((int Expected, int Got)?)(startRank + index, selected[index].Rank))
    .FirstOrDefault();
if (gap is { } missing)
{
    Console.Error.WriteLine(
        $"The list does not rank {missing.Expected}; ranks {startRank}-"
        + $"{startRank + packageCount - 1} were requested and rank {missing.Got} arrived "
        + "in its place.");
    Environment.ExitCode = 2;
    return;
}

// Every selected package is checked against its pin at the end, and a package whose
// pin the sweep does not understand can reach no outcome there -- it would simply
// never be accounted for, failing the run after a hundred acquisitions with a message
// about arithmetic. Refusing up front, before any acquisition, names the actual
// problem: the pin does not cover the window.
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

string packageDirectory = Path.Combine(outputDirectory, "packages");
try
{
    Directory.CreateDirectory(outputDirectory);
    Directory.CreateDirectory(packageDirectory);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
{
    // Stated, like every other refusal here. An output directory the process cannot
    // create is the caller's argument being wrong, and exit 134 tells the caller
    // nothing except that something died.
    Console.Error.WriteLine($"Could not create output directory '{outputDirectory}': {ex.Message}");
    Environment.ExitCode = 2;
    return;
}

// The same isolation knobs the CLI already reads (src/dotnet-inspect/Program.cs),
// honored here so a caller can point this sweep at a cache of its own. Without them the
// sweep reaches the developer's shared caches and the network unconditionally, which is
// why its two central properties -- that the pin binds, and that the copies land where
// told -- could only ever be evidenced by hand-run probes (#3560). Those properties are
// now gated by EvilPoolSweepGateTests, which runs this file offline against a scratch
// cache. This is not a test backdoor: it is one program catching up to a convention the rest of the tool follows,
// with the CLI's meanings unchanged. An isolated session skips the shared NuGet cache
// and, absent an explicit directory, gets its own. Reading them costs nothing when they
// are unset, which is the case for every real sweep.
bool offline = string.Equals(
    Environment.GetEnvironmentVariable("DOTNET_INSPECT_OFFLINE"), "1", StringComparison.Ordinal);
string? sessionName = Environment.GetEnvironmentVariable("DOTNET_INSPECT_ISOLATED");
if (string.IsNullOrWhiteSpace(sessionName))
    sessionName = null;
bool isolated = sessionName != null;
string? cacheBasePath = Environment.GetEnvironmentVariable("DOTNET_INSPECT_CACHE_DIR");
if (isolated && cacheBasePath == null)
    cacheBasePath = Path.Combine(Path.GetTempPath(), $"dotnet-inspect-{sessionName}");

HttpClientFactory.Initialize(offline);
NuGetCache.Initialize("dotnet-inspect", cacheBasePath, skipNuGetCache: isolated);

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
        PackageExtractionOutcome outcome;
        try
        {
            outcome = await PackageExtractor.ExtractPackageAsync(
                HttpClientFactory.Shared,
                entry.Package,
                tempDirPrefix: "decompiler-package-sweep",
                version: honorPin ? pin!.Version : null,
                forceLatest: !honorPin);
        }
        catch (ArgumentException ex)
        {
            // A backstop behind the version-shape check above: the extractor validates
            // its own path components and throws, and a throw here exits 134 --
            // indistinguishable from a crash, over an input the pin file supplied.
            //
            // Ungated, deliberately: IsBareVersion and IsBarePackageId already refuse
            // every spelling that reaches this, so no input the sweep will accept can
            // make the extractor throw, and no black-box case can arrange one. It stays
            // because the two shape checks and this catch guard the same 134 from
            // opposite sides, and the cheap side to be wrong on is this one. Measured:
            // disabling this catch leaves every case in EvilPoolSweepGateTests green.
            results.Add(Failed(entry, "acquisition-failed", ex.Message));
            Console.Error.WriteLine(
                $"rank {entry.Rank}: {entry.Package}: acquisition failed: {ex.Message}");
            continue;
        }

        if (!outcome.IsSuccess)
        {
            results.Add(Failed(entry, "acquisition-failed", outcome.ErrorMessage));
            Console.Error.WriteLine(
                $"rank {entry.Rank}: {entry.Package}: acquisition failed: {outcome.ErrorMessage}");
            continue;
        }

        package = outcome.Result!;
        string resolvedPackage = package.PackageName ?? entry.Package;

        // The id gate above refuses the one spelling known to do this, but the check
        // that matters is made against what came back: a pool entry acquired under a
        // different identity than the one selected is not the package the list ranked,
        // whatever spelling got it there.
        //
        // Ungated, and not gateable by EvilPoolSweepGateTests: those cases run offline
        // against a seeded cache, and the cache-hit path echoes the requested name back
        // as PackageName (PackageExtractor.AcquireResolvedPackageAsync), so the two sides
        // of this comparison are the same string by construction. It has teeth only on
        // the download path, where the name comes from what was served. Measured:
        // deleting this check outright leaves every case in that class green.
        if (!string.Equals(resolvedPackage, entry.Package, StringComparison.OrdinalIgnoreCase))
        {
            results.Add(Failed(
                entry, "identity-mismatch",
                $"selected '{entry.Package}' but acquired '{resolvedPackage}'",
                resolvedPackage, package.Version, null, package.FromCache));
            Console.Error.WriteLine(
                $"rank {entry.Rank}: {entry.Package}: acquired '{resolvedPackage}' instead.");
            continue;
        }

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
            // The recorded detail is compared too: a wiped or truncated cache entry
            // makes a package that ships libraries look like one that ships none, and
            // "NoAssemblies" is exactly what an emptied extraction reports. The detail
            // names the candidates the pin was recorded over, so a package that has
            // lost them no longer confirms.
            if (honorPin && pin!.Status == "no-library"
                && string.Equals(package.Version, pin.Version, StringComparison.OrdinalIgnoreCase)
                && string.Equals(detail, pin.Detail, StringComparison.Ordinal))
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
        string source = selection.Paths[0];

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

        string destinationDirectory = Path.Combine(
            packageDirectory,
            $"{entry.Rank:D3}-{SafePathSegment(entry.Package)}",
            SafePathSegment(package.Version ?? "unknown"));
        Directory.CreateDirectory(destinationDirectory);
        string destination = Path.Combine(
            destinationDirectory,
            Path.GetFileName(source));
        // Copied through a fresh sibling rather than onto the name, for the same reason
        // the metadata writes are. File.Copy opens the destination and writes through a
        // symlink sitting at it, so a link planted in the output directory took seven
        // hundred kilobytes of assembly onto whatever it pointed at while the sweep
        // reported success -- the arbitrary-file overwrite the metadata writes were
        // hardened against, still open on the ninety-one writes per sweep that carry the
        // pool itself.
        var copy = await CopyOntoOrReport(source, destination);
        string? uncopied = copy.Error;
        if (uncopied is not null)
        {
            // Recorded as this package failing rather than thrown away. A copy that did
            // not happen is a package that is not in the pool, and a pool short a
            // package must be short in the accounting too.
            results.Add(Failed(
                entry, "copy-failed", uncopied, resolvedPackage, package.Version,
                selection.Tfm, package.FromCache) with { WriteTemporary = copy.TemporaryFate });
            Console.Error.WriteLine($"rank {entry.Rank}: {entry.Package}: {uncopied}");
            continue;
        }

        destination = Path.GetFullPath(destination);

        // Hashed after the copy, over the copy. A version and a TFM describe the
        // request; only the hash describes the file, and a NuGet cache entry whose
        // contents were replaced satisfies both of the other two. Hashing the source
        // and then copying it leaves an interval a cache writer can land in, and the
        // file in the pool is the one that gets measured -- so that is the one whose
        // bytes must match.
        //
        // That last step is reasoning, not a gated property, and is marked so rather
        // than left to read as covered: every case in EvilPoolSweepGateTests tampers
        // with the cache entry, so source and destination carry the same bytes and no
        // black-box case can tell which was measured. Hashing the source here leaves
        // the whole class green. What is gated is that the hash is compared at all.
        string assemblySha = Convert.ToHexStringLower(
            SHA256.HashData(File.ReadAllBytes(destination)));
        if (honorPin && pin is not null
            && !string.Equals(assemblySha, pin.Sha256, StringComparison.Ordinal))
        {
            // Removed rather than left behind. assemblies.txt is written from the
            // in-memory list so a stray file cannot enter the pool, but leaving a
            // rejected assembly in packages/ invites a reader to measure it by hand.
            File.Delete(destination);
            results.Add(Failed(
                entry, "pin-mismatch", $"pinned sha256 {pin.Sha256}, got {assemblySha}",
                resolvedPackage, package.Version, selection.Tfm, package.FromCache));
            Console.Error.WriteLine(
                $"rank {entry.Rank}: {entry.Package}: pinned sha256 {pin.Sha256}, "
                + $"got {assemblySha}");
            continue;
        }

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
            package.FromCache,
            Sha256: assemblySha));
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
string assembliesPath = Path.Combine(outputDirectory, "assemblies.txt");
string? unwritable = (await ReplaceTextOrReport(
    assembliesPath,
    string.Concat(assemblies.Select(assembly => assembly + Environment.NewLine)))).Error;
if (unwritable is not null)
{
    Console.Error.WriteLine(unwritable);
    Environment.ExitCode = 2;
    return;
}

// The pool is made to hold what this run recorded, rather than merely being described
// by it. Writing assemblies.txt from the in-memory list keeps a stray file out of the
// record; nothing kept one out of the pool, and the pool is reused by design -- the
// corpus script passes the same output directory every time so that the paths in an
// earlier assemblies.txt stay valid. So a package that was pooled once and is refused,
// re-versioned, or dropped from the list later left its assembly behind, unrecorded and
// indistinguishable from a current one, for as long as the directory survived. Measured
// before this existed: a second run whose subject could not be acquired recorded the
// lead alone and left the first run's assembly sitting in packages/.
//
// Held against the record rather than by clearing the directory first. Clearing is the
// obvious spelling and the wrong one: it also removes whatever a caller had put at a
// destination, which is the seam two of this sweep's gates use to make a copy fail, and
// a fix that silently disarms the tests for the code it touches is worse than the leak.
// Reconciling afterwards touches only what the run did not record.
//
// Scoped to packages/, which is the sweep's own: it names every child and records what
// it put there. Files elsewhere under the output directory belong to the caller.
// Deletions are announced -- a leftover the sweep removes is still a fault somewhere,
// and a silent cleanup is how one goes unnoticed for a release.
var recordedAssemblies = new HashSet<string>(
    assemblies.Select(Path.GetFullPath), StringComparer.Ordinal);
try
{
    foreach (string pooled in Directory.GetFiles(packageDirectory, "*", SearchOption.AllDirectories))
    {
        if (recordedAssemblies.Contains(Path.GetFullPath(pooled)))
            continue;

        File.Delete(pooled);
        Console.Error.WriteLine(
            $"removed '{pooled}' from the pool: this sweep did not record it.");
    }
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    // Reported, and the run fails: the pool now holds something the record does not
    // name, which is the state this whole step exists to prevent. Exiting 0 here would
    // hand a consumer a pool the manifest misdescribes.
    Console.Error.WriteLine(
        $"Could not reconcile the pool under '{packageDirectory}' with the record: {ex.Message}");
    Environment.ExitCode = 1;
}

var manifest = new PackageSweepManifest(
    SchemaVersion: 1,
    GeneratedAtUtc: DateTimeOffset.UtcNow,
    Source: Path.GetRelativePath(root, sourcePath),
    StartRank: startRank,
    RequestedPackageCount: packageCount,
    SelectedPackageCount: assemblies.Count,
    Packages: results);
string manifestPath = Path.Combine(outputDirectory, "manifest.json");
unwritable = (await ReplaceTextOrReport(
    manifestPath,
    JsonSerializer.Serialize(manifest, jsonContext.PackageSweepManifest) + Environment.NewLine)).Error;
if (unwritable is not null)
{
    Console.Error.WriteLine(unwritable);
    Environment.ExitCode = 2;
    return;
}

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
            // The hash is recorded, not optional: the pin is refused later without one,
            // so a "selected" result that somehow carries none records nothing rather
            // than writing a pin file this tool would then reject.
            "selected" when result.ResolvedVersion is not null && result.Sha256 is not null =>
                new PackagePin(
                    result.RequestedPackage, result.ResolvedVersion, result.Tfm,
                    Sha256: result.Sha256),
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
    unwritable = (await ReplaceTextOrReport(
        pinPath,
        JsonSerializer.Serialize(
            // No timestamp: the file is a pure function of the pins, so re-recording an
            // unchanged pool produces a byte-identical file and a diff means something
            // actually moved. The sweep manifest already carries generatedAtUtc, and
            // #3349 found that hashing a file with a timestamp in it yields an identity
            // that never repeats.
            new PackagePinFile(SchemaVersion: 1, Packages: recorded),
            jsonContext.PackagePinFile) + Environment.NewLine)).Error;
    if (unwritable is not null)
    {
        // A refresh that cannot write the pin has not refreshed anything, and the file
        // the next sweep reads is the old one. Saying so beats exiting 134.
        Console.Error.WriteLine(unwritable);
        Environment.ExitCode = 2;
        return;
    }

    Console.WriteLine($"Recorded {recorded.Length} pinned packages in {Path.GetRelativePath(root, pinPath)}.");
}

Console.WriteLine(
    $"Selected {assemblies.Count} of {selected.Length} requested packages; "
    + $"manifest: {Path.Combine(outputDirectory, "manifest.json")}");
// Only when there is no pin to ask. An empty pool is the sole signal available to a
// deliberately unpinned run, but under a pin it is the pin that says how many
// assemblies a window owes -- and a window of nothing but "no-library" entries owes
// none, so failing it would refuse a window the pin describes perfectly.
// "None" is only wrong when the window owed some. A refresh over a window holding
// nothing but packages that reproducibly ship no library selected nothing and was
// right to -- failing it refused a perfect window. Counting what was owed keeps the
// signal that matters: a window whose packages do have libraries, yielding no
// assemblies, is still a failure, so a refresh in which every acquisition fell over
// cannot pass itself off as a window of meta-packages.
int owed = results.Count(result => result.Status != "library-unavailable");
if (resolveLatest && assemblies.Count == 0 && owed > 0)
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

// One set of options for both readers. --validate-pin exists so that the sweep's rules
// about a pin are the only rules; two serializer configurations would put the drift back
// one layer down, where "equivalent by inspection" is the same argument that failed three
// times.
static PackageSweepJsonContext SweepJsonContext() =>
    new(new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    });

// Collapses everything that could start a new output line into a space, so one verdict
// occupies one line no matter what the file it describes contains.
static string OneLine(string text) =>
    new([.. text.Select(c => char.IsControl(c) ? ' ' : c)]);

// Reading and shape-checking a pin file in one place, so --validate-pin and the sweep
// proper cannot disagree about what a well-formed pin is. Returns null when the file
// is usable, otherwise the reason, phrased to follow "Pin file '<path>' ".
static string? ValidatePinFile(string path) => ReadPinFile(path).Problem;

/// <summary>
/// The one way this sweep turns a path into a pin. Both callers -- the run and
/// --validate-pin -- go through here, so there is no second opinion to drift from:
/// same bytes, same bound, same parser, same shape rules, same wording.
/// </summary>
static (PackagePinFile? Pin, string? Problem) ReadPinFile(string path)
{
    var (text, problem) = ReadBoundedText(path);
    if (problem is not null)
        return (null, problem);

    PackagePinFile? pinFile;
    try
    {
        pinFile = JsonSerializer.Deserialize(text!, SweepJsonContext().PackagePinFile);
    }
    catch (JsonException ex)
    {
        return (null, $"could not be parsed: {ex.Message.TrimEnd('.')}");
    }

    if (pinFile is null)
        return (null, "is not a pin file");

    string? malformed = Malformed(pinFile);
    return malformed is null ? (pinFile, null) : (null, malformed);
}

/// <summary>
/// Reads a whole input file, or says why it could not. Returns the reason phrased to
/// follow "<c>&lt;kind&gt; '&lt;path&gt;' </c>".
/// </summary>
static (string? Text, string? Problem) ReadBoundedText(string path)
{
    // A BOM is stripped by hand rather than by StreamReader's detection, because
    // detection reinstates a replacing decoder for the file it detects and that is the
    // decoder this refuses to use.
    ReadOnlySpan<byte> Utf8ByteOrderMark = [0xEF, 0xBB, 0xBF];
    var StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // A pin file is tens of kilobytes. File.ReadAllText has no ceiling, and '/dev/zero'
    // is infinite: reading it exited 134 with an OutOfMemoryException, a crash where the
    // contract promises a refusal, from the one argument --validate-pin invites a caller
    // to choose. Read one byte past the ceiling so that hitting it is a refusal rather
    // than a silent truncation to something that still parses.
    const int MaxBytes = 16 * 1024 * 1024;

    // Opening a FIFO for reading blocks in open(2) until a writer appears, and nothing
    // observable beforehand tells one apart: .NET reports Attributes Normal and Length 0
    // for a FIFO, /dev/zero, /dev/null and a real pin file alike, so the only way to
    // learn what a path is, is to read it. A stall is worse here than the crash the
    // ceiling above removes -- exit 134 at least reports, while a hang says nothing and
    // burns a CI job's entire timeout. The read gets its own thread rather than a pool
    // thread, so a saturated pool cannot spend the deadline before the read even starts
    // and turn a healthy file into a refusal.
    const int TimeoutSeconds = 5;

    // Not disposed on the timeout path: the abandoned read still holds this token, and
    // disposing it under that thread trades one problem for an ObjectDisposedException
    // nobody is waiting to catch.
    var cancellation = new CancellationTokenSource();
    try
    {
        var read = Task.Factory.StartNew(
            () => ReadAtMost(path, MaxBytes, cancellation.Token),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        // WaitAny and not Wait: Wait(TimeSpan) throws the AggregateException wrapping
        // whatever the read threw, which is not what the filter below catches, so a
        // directory named as a pin file exited 134 -- the crash this file exists to
        // remove, reintroduced by the fix for the hang. WaitAny reports completion
        // without inspecting the outcome, leaving GetResult below to raise the original
        // exception unwrapped.
        if (Task.WaitAny([read], TimeSpan.FromSeconds(TimeoutSeconds)) < 0)
        {
            // Cancelling releases a read that has started and is trickling. It cannot
            // release one still blocked in open(2), which no token reaches -- that
            // thread is stuck until the process ends, which is why the deadline is the
            // answer rather than the cleanup.
            cancellation.Cancel();
            return (null,
                $"was still being read after {TimeoutSeconds} seconds, so it is not a "
                + "file this sweep can read");
        }

        cancellation.Dispose();

        // Raises what the read threw, rather than the AggregateException wrapping it,
        // so the catch below sees the same exceptions a direct read would raise.
        byte[]? bytes = read.GetAwaiter().GetResult();
        if (bytes is null)
            return (null, $"is larger than {MaxBytes} bytes, which no input of this sweep is");

        // Strictly, and not through a StreamReader. The default decoder replaces every
        // byte it cannot make sense of with U+FFFD, so a pin file holding invalid UTF-8
        // inside a string parsed cleanly and validated as well formed at exit 0 -- the
        // sweep answering for a file it had silently rewritten, over a question this PR
        // exists to answer about bytes. JSON is UTF-8; bytes that are not are not a pin
        // file this sweep can read, and saying so beats reading something else.
        ReadOnlySpan<byte> content = bytes;
        if (content.StartsWith(Utf8ByteOrderMark))
            content = content[Utf8ByteOrderMark.Length..];

        try
        {
            return (StrictUtf8.GetString(content), null);
        }
        catch (DecoderFallbackException ex)
        {
            return (null, $"is not valid UTF-8: {ex.Message.TrimEnd('.')}");
        }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
        or ArgumentException or NotSupportedException)
    {
        return (null, $"could not be read: {ex.Message.TrimEnd('.')}");
    }
}

/// <summary>
/// Returns the file's bytes, or null when it holds more than <paramref name="maxBytes"/>.
///
/// <para>Grown a chunk at a time rather than allocated at the ceiling. Reading the
/// sixty-kilobyte lockfile used to allocate sixteen megabytes because that is what a
/// too-large file would need, and a read abandoned at the deadline held that buffer for
/// the life of the process on a thread nothing could reach.</para>
/// </summary>
static byte[]? ReadAtMost(string path, int maxBytes, CancellationToken cancellationToken)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var accumulated = new MemoryStream();
    var chunk = new byte[64 * 1024];

    // One byte past the ceiling decides the question: a file of exactly maxBytes reads
    // to its end and is returned, and one byte more leaves the loop with nothing.
    while (accumulated.Length <= maxBytes)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int read = stream.Read(chunk, 0, chunk.Length);
        if (read == 0)
            return accumulated.ToArray();

        accumulated.Write(chunk, 0, read);
    }

    return null;
}

/// <summary>
/// Every way this sweep refuses a pin file's shape, in the order it applies them, each
/// under the name it is known by.
///
/// <para>A table rather than a run of <c>if</c>s so that the names are <em>derived from</em>
/// the rules rather than listed beside them. EvilPoolPinTests holds each rule with a
/// tampered pin file, and round fourteen found that a rule added here with no such file
/// was gated by nothing at all -- the suite stayed green over a check no input ever
/// reached. It now asks this sweep which rules exist (<c>--list-pin-rules</c>) and
/// requires a case per name, so a rule added without one fails and a name whose rule is
/// gone fails too. A separate array of names would drift from the checks exactly the way
/// the suite's copy of the rules drifted before <c>--validate-pin</c> replaced it.</para>
///
/// <para>Order is load-bearing: a null <c>packages</c> must be refused before anything
/// enumerates it, and a null entry before anything reads a field off one.</para>
/// </summary>
static (string Name, Func<PackagePinFile, string?> Refuse)[] PinRules() =>
[
    // The sweep writes 1 and reads 1. Ignoring the number meant a file written to a
    // later schema would be read with this schema's meaning -- fields silently absent
    // rather than refused, which is how a pin stops describing the pool without saying
    // so.
    ("schema", pinFile => pinFile.SchemaVersion != 1
        ? $"states schema version {pinFile.SchemaVersion}, which this sweep cannot read"
        : null),
    ("packages", pinFile => pinFile.Packages is null
        ? "states no packages"
        : null),
    ("null-entry", pinFile => pinFile.Packages?.Any(pin => pin is null) == true
        ? "contains a null entry"
        : null),
    ("blank-name", pinFile => pinFile.Packages?.Any(pin => pin is not null && string.IsNullOrWhiteSpace(pin.Package)) == true
        ? "contains an entry without a package name"
        : null),
    ("bare-id", pinFile => pinFile.Packages?.Any(pin => pin is not null && !IsBarePackageId(pin.Package)) == true
        ? "names a package that is not a bare NuGet id"
        : null),
    ("version", pinFile => pinFile.Packages?.Any(pin => pin is not null && !IsBareVersion(pin.Version)) == true
        ? "pins a package without a usable version"
        : null),
    ("status", pinFile => pinFile.Packages?.Any(pin => pin is not null && pin.Status is not ("pinned" or "no-library")) == true
        ? "pins a package with a status this sweep does not know"
        : null),
    // Required, not optional. An entry that may omit the hash is an entry that can
    // opt out of the check by omitting it, which is what a null TFM did before it was
    // made to match null.
    ("sha", pinFile => pinFile.Packages?.Any(pin => pin is not null && pin.Status == "pinned" && !IsSha256(pin.Sha256)) == true
        ? "pins a package without the sha256 of its assembly"
        : null),
    // A no-library entry has no assembly, so a hash on one describes bytes that are not
    // supposed to exist. Nothing downstream reads it, which is the problem: the entry
    // reads as pinning something while contributing nothing, and the contradiction
    // survives every later check. EvilPoolPinTests refuses it; so must the sweep.
    ("no-library-hash", pinFile => pinFile.Packages?.Any(pin => pin is not null && pin.Status == "no-library" && pin.Sha256 is not null) == true
        ? "pins a package as no-library but states an assembly hash"
        : null),
    ("duplicate", pinFile => pinFile.Packages is { } packages
            && packages.All(pin => pin is not null)
            && packages.Select(pin => pin.Package)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != packages.Count
        ? "pins the same package twice"
        : null),
];

static string? Malformed(PackagePinFile pinFile) =>
    PinRules().Select(rule => rule.Refuse(pinFile)).FirstOrDefault(problem => problem is not null);

// A version reaches the extractor, which validates it as a path component and throws.
// '../bad' exited 134 that way. NuGet versions are digits, dots, and the pre-release
// and build separators, and SemVer requires a numeric major -- so a leading digit and
// no '..' rule out the traversal spellings that survive the character set alone. '..'
// itself is all dots: the extractor does reject it, but as an acquisition failure
// (exit 1, "the pool is not reproducible") rather than as the malformed pin it is.
static bool IsBareVersion(string? version) =>
    !string.IsNullOrWhiteSpace(version)
    && char.IsAsciiDigit(version[0])
    && !version.Contains("..", StringComparison.Ordinal)
    && version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+');

static bool IsSha256(string? value) =>
    value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);

// NuGet ids are letters, digits, and the separators '.', '_' and '-'. Anything else --
// '@' most of all -- is a reference, a path, or a typo.
static bool IsBarePackageId(string? id) =>
    !string.IsNullOrWhiteSpace(id)
    && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

/// <summary>
/// Writes <paramref name="content"/> to a fresh sibling and moves it onto
/// <paramref name="path"/>, or says why it could not.
///
/// <para>Never opens the destination for writing, which matters twice. A refresh
/// interrupted partway through a direct write leaves the lockfile truncated -- a pin
/// file that is neither the old one nor the new one, and the next sweep reads it. And
/// the destination is a name, not a file: something can replace it with a FIFO between
/// the read that checked it and the write, and opening that blocks until a reader
/// appears, which is the hang the read path already had to grow a deadline for. A move
/// onto the name replaces whatever is there instead of writing through it.</para>
/// </summary>
static async Task<WriteOutcome> ReplaceTextOrReport(string path, string content) =>
    await ReplaceOrReport(
        path, stream => stream.WriteAsync(Encoding.UTF8.GetBytes(content)).AsTask());

/// <summary>
/// Copies <paramref name="source"/> onto <paramref name="destination"/> without opening
/// the destination, or says why it could not.
/// </summary>
static async Task<WriteOutcome> CopyOntoOrReport(string source, string destination) =>
    await ReplaceOrReport(destination, async stream =>
    {
        await using var input = new FileStream(
            source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await input.CopyToAsync(stream);
    });

/// <summary>
/// The one way this sweep puts bytes at a name: into a fresh sibling nothing else can
/// hold, then moved onto the name. Every write goes through here, because the property
/// is not one a second implementation keeps -- File.Copy kept none of it.
/// </summary>
static async Task<WriteOutcome> ReplaceOrReport(string path, Func<FileStream, Task> write)
{
    // Random and created exclusively, not a name another process can predict. A
    // temporary named after the pid is a name anything with write access to the
    // directory can occupy first, and a write through a planted symlink lands wherever
    // that link points. CreateNew refuses to open anything that already exists, so the
    // sweep either makes this file itself or writes nothing.
    //
    // Reasoned, not gated, and marked as such. The destination half of this is gated --
    // EvilPoolSweepGateTests plants a symlink at the destination and requires a real file
    // afterwards -- but the temporary half is not, because the name is drawn inside this
    // function and never leaves it, so nothing outside can occupy it except by racing a
    // loop against the allocator. Treat the CreateNew above as load-bearing: it is the
    // whole of the protection, and no test would notice it becoming Create.
    string temporary = path + $".{Path.GetRandomFileName()}.tmp";
    bool created = false;
    try
    {
        await using (var stream = new FileStream(
            temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            created = true;
            await write(stream);
        }

        File.Move(temporary, path, overwrite: true);
        return new WriteOutcome(null, "moved");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        // A leftover temporary is worse than the failure it came from: it is a file
        // nothing reads and the next run collides with. Only one this run created,
        // though -- something already sitting at that name is not this sweep's to
        // remove, and deleting whatever is found there would be a worse answer than
        // the failure being reported.
        // Assumed left behind the moment it exists, and downgraded only by a delete that
        // returned. Derived the other way round -- "none" until something says otherwise --
        // a cleanup that stopped being called would report that no temporary was ever
        // created, which is the one answer that makes the leak invisible.
        string fate = created ? "left-behind" : "none";
        try
        {
            if (created)
            {
                File.Delete(temporary);
                fate = "removed";
            }
        }
        catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
        {
        }

        return new WriteOutcome($"Could not write '{path}': {ex.Message}", fate);
    }
}

static string? FindRepositoryRoot(string start)
{
    for (var directory = new DirectoryInfo(start);
        directory is not null;
        directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
            return directory.FullName;
    }

    return null;
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
    [property: JsonPropertyName("detail")] string? Detail = null,
    [property: JsonPropertyName("sha256")] string? Sha256 = null);

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
    string? CleanupDetail = null,
    string? Sha256 = null,
    string? WriteTemporary = null);

/// <summary>
/// What a write did, and what became of the temporary it writes through.
///
/// <para><c>Error</c> is the message, or null when the write landed.
/// <c>TemporaryFate</c> is <c>moved</c> when it became the file, <c>removed</c> when the
/// write failed after it existed and it was cleaned up, <c>left-behind</c> when that
/// cleanup itself failed, and <c>none</c> when the failure happened before it existed.
/// </para>
///
/// <para>Reported rather than inferred because the difference is not visible from
/// outside. A failure before the temporary exists and a failure after it is cleaned up
/// leave the same directory behind and the same message, so nothing downstream can tell
/// a cleanup that ran from one that was never needed -- including the operator reading
/// the manifest to find out whether a failed sweep left anything in the pool, which is
/// the question <c>left-behind</c> exists to answer out loud.</para>
/// </summary>
readonly record struct WriteOutcome(string? Error, string TemporaryFate);

[JsonSerializable(typeof(List<PackageListEntry>))]
[JsonSerializable(typeof(PackagePinFile))]
[JsonSerializable(typeof(PackageSweepManifest))]
sealed partial class PackageSweepJsonContext : JsonSerializerContext;
