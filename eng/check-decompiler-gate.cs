// Compares the failing tests of a decompiler gate run against the pinned
// known-red list, so the gate can run pre-merge while known failures are
// still open. Drift in either direction is an error:
//
//   * a failure that is not pinned  -> new breakage, the gate did its job
//   * a pinned test that passed     -> the fix landed, retire the pin
//   * a pinned test that never ran  -> the pin is dead (renamed/deleted test)
//   * a gate test that did not run  -> coverage silently disappeared
//   * an expected class is absent   -> the preset stopped selecting it
//   * a discovered test has no row  -> the report is incomplete
//
// The last two are different properties and both are needed.
//
// The class inventory proves the *preset* still selects the classes it is
// supposed to. It cannot prove the report is whole, because it only asks
// whether each class contributed at least one row.
//
// Completeness needs a reference the report cannot forge, and the report's own
// summary counters are not one: they are written by the same run. A report
// containing four of fourteen tests and honestly declaring total="4" is
// internally consistent and tells the checker nothing. So the expected test set
// comes from a separate `-list methods` discovery pass over the same preset,
// and every discovered test must appear in the results.
//
// Only "Pass" counts as passing. A gate test that is skipped is neither
// passing nor failing, and treating it as either is how a gate becomes
// vacuous: an unpinned skip would report an exact match, and a pinned skip
// would be reported as a landed fix, prompting removal of the pin that was the
// last thing naming the test.
//
// Usage:
//   dotnet run eng/check-decompiler-gate.cs -- <results.xml> <known-red.txt> \
//       <expected-classes.txt> <discovered-tests.json> [--partial]
//
// Produce the discovery listing with the *same* preset as the run, so the two
// cannot drift:
//   dotnet run --project src/ILInspector.Decompiler.Tests -c Release --no-build -- \
//       --gate pre-merge -noColor -list methods/json > discovered-tests.json
//
// --partial suppresses the dead-pin, expected-class, and completeness checks,
// for developers running a subset of the gate classes locally. CI always runs
// the full preset and must not pass it.

using System.Text.Json;
using System.Xml.Linq;

var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
bool partial = args.Contains("--partial", StringComparer.Ordinal);

if (positional.Length != 4)
{
    Console.Error.WriteLine(
        "usage: check-decompiler-gate <results.xml> <known-red.txt> <expected-classes.txt> "
            + "<discovered-tests.json> [--partial]");
    return 2;
}

string resultsPath = positional[0];
string pinPath = positional[1];
string expectedClassesPath = positional[2];
string discoveredPath = positional[3];

if (!File.Exists(resultsPath))
{
    Console.Error.WriteLine($"error: results file not found: {resultsPath}");
    Console.Error.WriteLine("The gate run must produce XML via '-xml <path>'. A missing file means");
    Console.Error.WriteLine("the run crashed, hung, or was killed before reporting, which is a");
    Console.Error.WriteLine("failure, not an empty pass.");
    return 2;
}

if (!File.Exists(pinPath))
{
    Console.Error.WriteLine($"error: known-red file not found: {pinPath}");
    return 2;
}

if (!File.Exists(expectedClassesPath))
{
    Console.Error.WriteLine($"error: expected-classes file not found: {expectedClassesPath}");
    return 2;
}

if (!File.Exists(discoveredPath))
{
    Console.Error.WriteLine($"error: discovered-tests file not found: {discoveredPath}");
    Console.Error.WriteLine("Without it there is no reference for what the run should have contained,");
    Console.Error.WriteLine("and an incomplete report cannot be distinguished from a complete one.");
    return 2;
}

XDocument doc;
try
{
    doc = XDocument.Load(resultsPath);
}
catch (System.Xml.XmlException ex)
{
    Console.Error.WriteLine($"error: results file is not well-formed XML: {resultsPath}");
    Console.Error.WriteLine($"  {ex.Message}");
    Console.Error.WriteLine("A truncated report usually means the run was killed mid-write.");
    return 2;
}

var tests = doc.Descendants("test").ToList();

if (tests.Count == 0)
{
    Console.Error.WriteLine("error: the results file contains no tests.");
    Console.Error.WriteLine("A gate that selected nothing is not a gate that passed.");
    return 2;
}

// Cross-check the report against its own declared summary. This is an
// *internal consistency* check only: it catches a report that was truncated or
// rewritten so that its rows no longer match its own counters. It deliberately
// does not claim to prove completeness -- the counters come from the same run,
// so an honestly-declared partial report satisfies them. The discovery
// comparison below is what proves completeness.
static int Sum(IEnumerable<XElement> assemblies, string attribute) =>
    assemblies.Sum(a => int.TryParse((string?)a.Attribute(attribute), out int n) ? n : 0);

var assemblies = doc.Descendants("assembly").ToList();
int declaredTotal = Sum(assemblies, "total");
int declaredPassed = Sum(assemblies, "passed");
int declaredFailed = Sum(assemblies, "failed");
int declaredSkipped = Sum(assemblies, "skipped");
int declaredNotRun = Sum(assemblies, "not-run");
int declaredErrors = Sum(assemblies, "errors");

if (declaredTotal != tests.Count)
{
    Console.Error.WriteLine(
        $"error: the report declares {declaredTotal} tests but contains {tests.Count} <test> elements.");
    Console.Error.WriteLine("The report is truncated or was rewritten. It cannot clear the gate.");
    return 2;
}

if (declaredSkipped != 0 || declaredNotRun != 0 || declaredErrors != 0)
{
    Console.Error.WriteLine(
        $"error: the report declares {declaredSkipped} skipped, {declaredNotRun} not-run, "
            + $"and {declaredErrors} errored tests.");
    Console.Error.WriteLine("Gate tests must run. Fix the environment or the test; do not skip it.");
    return 2;
}

int actualPassed = tests.Count(t => (string?)t.Attribute("result") == "Pass");
int actualFailed = tests.Count(t => (string?)t.Attribute("result") == "Fail");

if (declaredPassed != actualPassed || declaredFailed != actualFailed)
{
    Console.Error.WriteLine(
        $"error: the report declares {declaredPassed} passed and {declaredFailed} failed, "
            + $"but contains {actualPassed} passing and {actualFailed} failing <test> elements.");
    Console.Error.WriteLine("The report contradicts itself and cannot be trusted to describe the run.");
    return 2;
}

static string? TestName(XElement test)
{
    string? name = (string?)test.Attribute("name");
    if (!string.IsNullOrWhiteSpace(name))
        return name;
    string? type = (string?)test.Attribute("type");
    string? method = (string?)test.Attribute("method");
    if (!string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(method))
        return $"{type}.{method}";
    return null;
}

var passed = new HashSet<string>(StringComparer.Ordinal);
var failed = new HashSet<string>(StringComparer.Ordinal);
var notExecuted = new SortedDictionary<string, string>(StringComparer.Ordinal);
// Method identity taken from the report's own type/method attributes rather
// than parsed out of the display name. Display names carry theory arguments,
// honor -methodDisplayOptions, and can contain the very characters a parser
// would split on; type/method are unambiguous.
var executedMethods = new HashSet<string>(StringComparer.Ordinal);
var executedClasses = new HashSet<string>(StringComparer.Ordinal);
int unidentified = 0;

foreach (var test in tests)
{
    string? name = TestName(test);
    if (name is null)
    {
        // A result we cannot attribute to a test is not a result we can clear.
        unidentified++;
        continue;
    }

    string result = (string?)test.Attribute("result") ?? "(no result attribute)";
    switch (result)
    {
        case "Fail":
            failed.Add(name);
            break;
        case "Pass":
            passed.Add(name);
            break;
        default:
            notExecuted[name] = result;
            break;
    }

    if (result is "Pass" or "Fail")
    {
        string? type = (string?)test.Attribute("type");
        string? method = (string?)test.Attribute("method");
        if (!string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(method))
        {
            executedMethods.Add($"{type}.{method}");
            executedClasses.Add(type);
        }
        else
        {
            // Fall back to the display name only when the structured identity is
            // absent. Cutting at the first '(' strips theory arguments; the last
            // '.' separates class from method.
            int paren = name.IndexOf('(', StringComparison.Ordinal);
            string bare = paren >= 0 ? name[..paren] : name;
            executedMethods.Add(bare);
            int dot = bare.LastIndexOf('.');
            executedClasses.Add(dot > 0 ? bare[..dot] : bare);
        }
    }
}

// A test that both passed and failed in one report (a retry) is treated as
// failing: the gate reports the worst observed outcome, never the best.
passed.ExceptWith(failed);

var pinned = File.ReadAllLines(pinPath)
    .Select(line => line.Trim())
    .Where(line => line.Length > 0 && !line.StartsWith('#'))
    .ToHashSet(StringComparer.Ordinal);

var expectedClasses = File.ReadAllLines(expectedClassesPath)
    .Select(line => line.Trim())
    .Where(line => line.Length > 0 && !line.StartsWith('#'))
    .ToHashSet(StringComparer.Ordinal);

if (expectedClasses.Count == 0)
{
    Console.Error.WriteLine($"error: no expected classes listed in {expectedClassesPath}.");
    Console.Error.WriteLine("An empty inventory would clear any report, however incomplete.");
    return 2;
}

var executed = new HashSet<string>(passed, StringComparer.Ordinal);
executed.UnionWith(failed);

// A class counts as covered only if it executed something. A class present in
// the report solely as skips is a coverage hole, and is already reported as
// one above.
var coveredClasses = executedClasses;

var missingClasses = partial
    ? []
    : expectedClasses.Except(coveredClasses, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

// Completeness, against a reference the results file cannot forge. Discovery
// enumerates what the preset selects without running anything, so a run that
// was cut short, filtered down, or rewritten has a smaller result set than the
// listing regardless of how self-consistent its own counters are.
//
// This is *method*-granular, because no `-list` mode enumerates theory cases:
// a theory with five cases lists once, so a run that lost four of them would
// still satisfy this check. Method granularity is only sufficient while the
// gate classes declare no theories, which is not an assumption --
// GateExpectedClassesTests.PreMergeGateClasses_DeclareNoTheories enforces it,
// and adding a theory to a gate class fails that test rather than quietly
// weakening this one.
List<string> missingTests = [];
List<string> unexpectedTests = [];

if (!partial)
{
    HashSet<string> discovered;
    try
    {
        using var listing = JsonDocument.Parse(File.ReadAllText(discoveredPath));
        if (listing.RootElement.ValueKind != JsonValueKind.Array)
        {
            Console.Error.WriteLine($"error: {discoveredPath} is not a JSON array of test names.");
            Console.Error.WriteLine("Produce it with '-noColor -list methods/json'.");
            return 2;
        }

        discovered = listing.RootElement
            .EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.Ordinal);
    }
    catch (JsonException ex)
    {
        Console.Error.WriteLine($"error: {discoveredPath} is not well-formed JSON: {ex.Message}");
        Console.Error.WriteLine("A truncated listing would understate what the run owed, so this");
        Console.Error.WriteLine("cannot be treated as an empty expectation.");
        return 2;
    }

    if (discovered.Count == 0)
    {
        Console.Error.WriteLine($"error: {discoveredPath} lists no tests.");
        Console.Error.WriteLine("Discovery matched nothing, so every report would satisfy it. A filter");
        Console.Error.WriteLine("naming a renamed or deleted class discovers nothing and exits 0 --");
        Console.Error.WriteLine("that is a broken preset, not an empty gate that passed.");
        return 2;
    }

    // Discovery lists methods; identity comes from the report's type/method
    // attributes, so theory arguments and display-name options cannot confuse
    // the comparison.
    missingTests = discovered.Except(executedMethods, StringComparer.Ordinal)
        .Order(StringComparer.Ordinal).ToList();
    unexpectedTests = executedMethods.Except(discovered, StringComparer.Ordinal)
        .Order(StringComparer.Ordinal).ToList();
}

var newFailures = failed.Except(pinned, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
var nowPassing = pinned.Intersect(passed, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
var deadPins = partial
    ? []
    : pinned.Except(executed, StringComparer.Ordinal)
        .Except(notExecuted.Keys, StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToList();

Console.WriteLine(
    $"Decompiler gate: {tests.Count} tests, {passed.Count} passed, {failed.Count} failed, "
        + $"{notExecuted.Count} not executed, {coveredClasses.Count}/{expectedClasses.Count} expected "
        + $"classes covered, {pinned.Count} pinned known-red.");
Console.WriteLine();

if (unidentified > 0)
{
    Console.WriteLine($"UNIDENTIFIED RESULTS ({unidentified}) — <test> elements with no usable name:");
    Console.WriteLine();
    Console.WriteLine("  A result that cannot be attributed to a test is not a result that can");
    Console.WriteLine("  clear one. The report is malformed.");
    Console.WriteLine();
}

if (missingClasses.Count > 0)
{
    Console.WriteLine($"MISSING CLASSES ({missingClasses.Count}) — expected but nothing executed:");
    foreach (var name in missingClasses)
        Console.WriteLine($"  {name}");
    Console.WriteLine();
    Console.WriteLine($"  The report is incomplete. Either the run was cut short, or --gate");
    Console.WriteLine("  pre-merge no longer selects this class (renamed? deleted? preset edited?).");
    Console.WriteLine($"  A shrinking gate passes trivially, which is why {expectedClassesPath}");
    Console.WriteLine("  exists. Pass --partial if you deliberately ran a subset.");
    Console.WriteLine();
}

if (missingTests.Count > 0)
{
    Console.WriteLine($"INCOMPLETE REPORT ({missingTests.Count}) — discovered but absent from the results:");
    foreach (var name in missingTests)
        Console.WriteLine($"  {name}");
    Console.WriteLine();
    Console.WriteLine("  Discovery says the preset selects these; the report does not contain them.");
    Console.WriteLine("  The run was cut short or the report was rewritten. A report that is missing");
    Console.WriteLine("  tests cannot clear the gate no matter how consistent its own counters are.");
    Console.WriteLine();
}

if (unexpectedTests.Count > 0)
{
    Console.WriteLine($"UNEXPECTED RESULTS ({unexpectedTests.Count}) — in the results but never discovered:");
    foreach (var name in unexpectedTests)
        Console.WriteLine($"  {name}");
    Console.WriteLine();
    Console.WriteLine("  The results and the discovery listing describe different runs. They must be");
    Console.WriteLine("  produced from the same preset against the same build.");
    Console.WriteLine();
}

if (newFailures.Count > 0)
{
    Console.WriteLine($"NEW FAILURES ({newFailures.Count}) — not in {pinPath}:");
    foreach (var name in newFailures)
        Console.WriteLine($"  {name}");
    Console.WriteLine();
    Console.WriteLine("  This is the gate working. Fix the regression, or — only if the diff is");
    Console.WriteLine("  understood and intentional — docket it in the owning gate and, if it must");
    Console.WriteLine("  stay red, add it to the known-red list with an issue and a date.");
    Console.WriteLine();
}

if (notExecuted.Count > 0)
{
    Console.WriteLine($"NOT EXECUTED ({notExecuted.Count}) — selected but neither passed nor failed:");
    foreach (var (name, result) in notExecuted)
        Console.WriteLine($"  {name} (result=\"{result}\")");
    Console.WriteLine();
    Console.WriteLine("  A gate test that does not run is a coverage hole, not a pass. Either the");
    Console.WriteLine("  environment is missing something the gate needs, or the test was disabled.");
    Console.WriteLine("  Skipping is not an approved way to green this job; fix the environment or");
    Console.WriteLine("  the test.");
    Console.WriteLine();
}

if (nowPassing.Count > 0)
{
    Console.WriteLine($"STALE PINS ({nowPassing.Count}) — pinned known-red but passing:");
    foreach (var name in nowPassing)
        Console.WriteLine($"  {name}");
    Console.WriteLine();
    Console.WriteLine($"  Remove these from {pinPath}. A pin that outlives its failure silently");
    Console.WriteLine("  un-gates the test it names.");
    Console.WriteLine();
}

if (deadPins.Count > 0)
{
    Console.WriteLine($"DEAD PINS ({deadPins.Count}) — pinned but not present in the run:");
    foreach (var name in deadPins)
        Console.WriteLine($"  {name}");
    Console.WriteLine();
    Console.WriteLine("  The test was renamed or deleted. Update the pin, or pass --partial if you");
    Console.WriteLine("  deliberately ran a subset.");
    Console.WriteLine();
}

if (newFailures.Count == 0
    && notExecuted.Count == 0
    && nowPassing.Count == 0
    && deadPins.Count == 0
    && missingClasses.Count == 0
    && missingTests.Count == 0
    && unexpectedTests.Count == 0
    && unidentified == 0)
{
    Console.WriteLine("OK: the failing set matches the known-red list exactly.");
    if (pinned.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"Reminder: {pinned.Count} gate test(s) are still red. They are pinned, not fixed.");
    }
    return 0;
}

return 1;
