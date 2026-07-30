// Compares the failing tests of a decompiler gate run against the pinned
// known-red list, so the gate can run pre-merge while known failures are
// still open. Drift in either direction is an error:
//
//   * a failure that is not pinned  -> new breakage, the gate did its job
//   * a pinned test that passed     -> the fix landed, retire the pin
//   * a pinned test that never ran  -> the pin is dead (renamed/deleted test)
//   * a gate test that did not run  -> coverage silently disappeared
//
// The last case is why only "Pass" counts as passing. A gate test that is
// skipped is neither passing nor failing, and treating it as either is how a
// gate becomes vacuous: an unpinned skip would report an exact match, and a
// pinned skip would be reported as a landed fix, prompting removal of the pin
// that was the last thing naming the test.
//
// Usage:
//   dotnet run eng/check-decompiler-gate.cs -- <results.xml> <known-red.txt> [--partial]
//
// --partial suppresses the dead-pin check, for developers running a subset of
// the gate classes locally. CI always runs the full preset and must not pass it.

using System.Xml.Linq;

var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
bool partial = args.Contains("--partial", StringComparer.Ordinal);

if (positional.Length != 2)
{
    Console.Error.WriteLine("usage: check-decompiler-gate <results.xml> <known-red.txt> [--partial]");
    return 2;
}

string resultsPath = positional[0];
string pinPath = positional[1];

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

static string TestName(XElement test)
{
    string? name = (string?)test.Attribute("name");
    if (!string.IsNullOrEmpty(name))
        return name;
    return $"{(string?)test.Attribute("type")}.{(string?)test.Attribute("method")}";
}

var passed = new HashSet<string>(StringComparer.Ordinal);
var failed = new HashSet<string>(StringComparer.Ordinal);
var notExecuted = new SortedDictionary<string, string>(StringComparer.Ordinal);

foreach (var test in tests)
{
    string name = TestName(test);
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

var executed = new HashSet<string>(passed, StringComparer.Ordinal);
executed.UnionWith(failed);

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
        + $"{notExecuted.Count} not executed, {pinned.Count} pinned known-red.");
Console.WriteLine();

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

if (newFailures.Count == 0 && notExecuted.Count == 0 && nowPassing.Count == 0 && deadPins.Count == 0)
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
