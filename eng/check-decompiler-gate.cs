// Compares the failing tests of a decompiler gate run against the pinned
// known-red list, so the gate can run pre-merge while known failures are
// still open. Drift in either direction is an error:
//
//   * a failure that is not pinned  -> new breakage, the gate did its job
//   * a pinned test that passed     -> the fix landed, retire the pin
//   * a pinned test that never ran  -> the pin is dead (renamed/deleted test)
//   * a gate test that did not run  -> coverage silently disappeared
//   * an expected class is absent   -> the report is incomplete
//
// The last case is what an inventory buys. Without it the checker could only
// judge the tests a report happens to contain, so a preset that stopped
// selecting a class, a renamed class, or a run killed partway would each
// produce a smaller report whose failing set still matched the pin list
// exactly, and pass.
//
// Only "Pass" counts as passing. A gate test that is skipped is neither
// passing nor failing, and treating it as either is how a gate becomes
// vacuous: an unpinned skip would report an exact match, and a pinned skip
// would be reported as a landed fix, prompting removal of the pin that was the
// last thing naming the test.
//
// Usage:
//   dotnet run eng/check-decompiler-gate.cs -- <results.xml> <known-red.txt> <expected-classes.txt> [--partial]
//
// --partial suppresses the dead-pin and expected-class checks, for developers
// running a subset of the gate classes locally. CI always runs the full preset
// and must not pass it.

using System.Xml.Linq;

var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
bool partial = args.Contains("--partial", StringComparer.Ordinal);

if (positional.Length != 3)
{
    Console.Error.WriteLine(
        "usage: check-decompiler-gate <results.xml> <known-red.txt> <expected-classes.txt> [--partial]");
    return 2;
}

string resultsPath = positional[0];
string pinPath = positional[1];
string expectedClassesPath = positional[2];

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
var coveredClasses = executed
    .Select(name =>
    {
        int paren = name.IndexOf('(', StringComparison.Ordinal);
        string bare = paren >= 0 ? name[..paren] : name;
        int dot = bare.LastIndexOf('.');
        return dot > 0 ? bare[..dot] : bare;
    })
    .ToHashSet(StringComparer.Ordinal);

var missingClasses = partial
    ? []
    : expectedClasses.Except(coveredClasses, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

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
