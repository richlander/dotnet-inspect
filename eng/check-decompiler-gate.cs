// Compares the failing tests of a decompiler gate run against the pinned
// known-red list, so the gate can run pre-merge while known failures are
// still open. Drift in either direction is an error:
//
//   * a failure that is not pinned  -> new breakage, the gate did its job
//   * a pinned test that passed     -> the fix landed, retire the pin
//   * a pinned test that never ran  -> the pin is dead (renamed/deleted test)
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
    Console.Error.WriteLine("the run crashed before reporting, which is a failure, not an empty pass.");
    return 2;
}

if (!File.Exists(pinPath))
{
    Console.Error.WriteLine($"error: known-red file not found: {pinPath}");
    return 2;
}

var doc = XDocument.Load(resultsPath);
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

var ran = tests.Select(TestName).ToHashSet(StringComparer.Ordinal);
var failed = tests
    .Where(t => string.Equals((string?)t.Attribute("result"), "Fail", StringComparison.Ordinal))
    .Select(TestName)
    .ToHashSet(StringComparer.Ordinal);

var pinned = File.ReadAllLines(pinPath)
    .Select(line => line.Trim())
    .Where(line => line.Length > 0 && !line.StartsWith('#'))
    .ToHashSet(StringComparer.Ordinal);

var newFailures = failed.Except(pinned, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
var nowPassing = pinned.Intersect(ran, StringComparer.Ordinal)
    .Except(failed, StringComparer.Ordinal)
    .Order(StringComparer.Ordinal)
    .ToList();
var deadPins = partial
    ? []
    : pinned.Except(ran, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

Console.WriteLine($"Decompiler gate: {tests.Count} tests, {failed.Count} failed, {pinned.Count} pinned known-red.");
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

if (newFailures.Count == 0 && nowPassing.Count == 0 && deadPins.Count == 0)
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
