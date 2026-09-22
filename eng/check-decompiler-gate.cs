// Compares the failing cases of a decompiler gate run against the pinned
// known-red list, so the gate can run pre-merge while known failures are
// still open. Drift in either direction is an error:
//
//   * a failure that is not pinned  -> new breakage, the gate did its job
//   * a pinned case that passed     -> the fix landed, retire the pin
//   * a pinned case that never ran  -> the pin is dead (renamed/deleted case)
//   * a gate test that did not run  -> coverage silently disappeared
//   * an expected class is absent   -> the preset stopped selecting it
//   * a discovered case has no execution -> the report is incomplete
//   * a case runs more than once         -> discovery was delayed or the run retried
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
// comes from a separate MTP JSON-RPC discovery request over the same preset.
// MTP emits the same TestNodeUid through its execution message bus, so every
// discovered case must execute exactly once.
//
// "Exactly once" is load-bearing. xUnit falls back to delayed enumeration when
// theory data is not serializable: discovery then emits one case ID for the
// method, and execution starts several tests under that same ID. Comparing sets
// alone would call that complete. Counting executions per ID rejects the
// fallback rather than trusting --pre-enumerate-theories.
//
// Pins use `Namespace.Class.Method [TestNodeUid]`, so one red theory case
// never exempts its siblings. Only "Pass" counts as passing. A gate test that
// is skipped is neither
// passing nor failing, and treating it as either is how a gate becomes
// vacuous: an unpinned skip would report an exact match, and a pinned skip
// would be reported as a landed fix, prompting removal of the pin that was the
// last thing naming the test.
//
// Usage:
//   dotnet run eng/check-decompiler-gate.cs -- <results.xml> <execution.jsonl> \
//       <known-red.txt> <expected-classes.txt> <discovery.jsonl> [--partial]
//
// Produce the discovery receipt with the *same* preset as the run, so the two
// cannot drift:
//   dotnet run --project tests/ILInspector.Decompiler.Tests -c Release --no-build -- \
//       --gate-discovery-receipt discovery.jsonl --gate pre-merge \
//       --pre-enumerate-theories on --no-ansi
//   DOTNET_INSPECT_DECOMPILER_TEST_RECEIPT=execution.jsonl \
//       dotnet run --project tests/ILInspector.Decompiler.Tests -c Release --no-build -- \
//       --gate pre-merge --pre-enumerate-theories on --no-ansi \
//       --auto-reporters off --report-xunit-xml --report-xunit-xml-filename results.xml
//
// --partial suppresses the dead-pin, expected-class, and completeness checks,
// for developers running a subset of the gate classes locally. CI always runs
// the full preset and must not pass it.

using System.Text.Json;
using System.Xml.Linq;

var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
bool partial = args.Contains("--partial", StringComparer.Ordinal);

if (positional.Length != 5)
{
    Console.Error.WriteLine(
        "usage: check-decompiler-gate <results.xml> <execution.jsonl> <known-red.txt> "
            + "<expected-classes.txt> <discovery.jsonl> [--partial]");
    return 2;
}

string resultsPath = positional[0];
string eventsPath = positional[1];
string pinPath = positional[2];
string expectedClassesPath = positional[3];
string discoveredPath = positional[4];

if (!File.Exists(resultsPath))
{
    Console.Error.WriteLine($"error: results file not found: {resultsPath}");
    Console.Error.WriteLine("The gate run must produce an MTP xUnit report. A missing file means");
    Console.Error.WriteLine("the run crashed, hung, or was killed before reporting, which is a");
    Console.Error.WriteLine("failure, not an empty pass.");
    return 2;
}

if (!File.Exists(pinPath))
{
    Console.Error.WriteLine($"error: known-red file not found: {pinPath}");
    return 2;
}

if (!File.Exists(eventsPath))
{
    Console.Error.WriteLine($"error: execution receipt not found: {eventsPath}");
    Console.Error.WriteLine("The gate run must enable the decompiler MTP receipt consumer. Without");
    Console.Error.WriteLine("MTP TestNodeUid values, case completeness is unprovable.");
    return 2;
}

if (!File.Exists(expectedClassesPath))
{
    Console.Error.WriteLine($"error: expected-classes file not found: {expectedClassesPath}");
    return 2;
}

if (!File.Exists(discoveredPath))
{
    Console.Error.WriteLine($"error: discovery receipt not found: {discoveredPath}");
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

// Identity for XML decisions -- pins, outcomes, and class coverage -- comes
// from the structured type/method attributes, not the display name.
// Splitting the two was a real bug: a row whose name said one thing and whose
// method attribute said another could pass a new failure off as a pinned one.
// The display name carries theory arguments and honors -methodDisplayOptions,
// so it is a presentation string, not an identity.
static string? TestName(XElement test)
{
    string? type = (string?)test.Attribute("type");
    string? method = (string?)test.Attribute("method");
    bool hasType = !string.IsNullOrWhiteSpace(type);
    bool hasMethod = !string.IsNullOrWhiteSpace(method);

    if (hasType && hasMethod)
        return $"{type}.{method}";

    // Partial or absent structured identity is malformed. The display name is
    // presentation, not a fallback identity.
    return null;
}

static string? TestClass(XElement test)
{
    string? type = (string?)test.Attribute("type");
    return string.IsNullOrWhiteSpace(type) ? null : type;
}

static string? JsonString(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out JsonElement value)
        || value.ValueKind != JsonValueKind.String)
    {
        return null;
    }

    string? text = value.GetString();
    return string.IsNullOrWhiteSpace(text) ? null : text;
}

static string CaseLabel(
    string caseId,
    IReadOnlyDictionary<string, string> discovered,
    IReadOnlyDictionary<string, string> reported)
{
    if (discovered.TryGetValue(caseId, out string? method)
        || reported.TryGetValue(caseId, out method))
    {
        return $"{method} [{caseId}]";
    }

    return caseId;
}

var notExecuted = new SortedDictionary<string, string>(StringComparer.Ordinal);
var executedClasses = new HashSet<string>(StringComparer.Ordinal);
var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
var xmlOutcomeOccurrences = new Dictionary<(string Method, string Result), int>();
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

    occurrences[name] = occurrences.GetValueOrDefault(name) + 1;

    string result = (string?)test.Attribute("result") ?? "(no result attribute)";
    var outcomeKey = (name, result);
    xmlOutcomeOccurrences[outcomeKey] =
        xmlOutcomeOccurrences.GetValueOrDefault(outcomeKey) + 1;
    switch (result)
    {
        case "Fail":
        case "Pass":
            break;
        default:
            notExecuted[name] = result;
            break;
    }

    if (result is "Pass" or "Fail" && TestClass(test) is string cls)
        executedClasses.Add(cls);
}

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

// A class counts as covered only if it executed something. A class present in
// the report solely as skips is a coverage hole, and is already reported as
// one above.
var coveredClasses = executedClasses;

var missingClasses = partial
    ? []
    : expectedClasses.Except(coveredClasses, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

// Completeness, against a reference the results file cannot forge. MTP
// discovery enumerates what the preset selects without running it. The MTP
// execution consumer carries the same stable TestNodeUid while the test runs.
// This keeps case identity out of display names and makes serializable theories
// as strict as facts.
//
// A set comparison is not enough. When theory data is not serializable, xUnit
// emits one delayed discovery case and starts several tests under that one case
// ID. Every discovered case must therefore start exactly one test. Zero is a
// missing case; more than one is delayed enumeration or a retry, and both fail.
const string ReceiptSchema = "dotnet-inspect.decompiler-test-receipt.v1";
var reporterCaseMethods = new Dictionary<string, string>(StringComparer.Ordinal);
var reporterCaseXmlMethods = new Dictionary<string, string>(StringComparer.Ordinal);
var reporterCaseStarts = new Dictionary<string, int>(StringComparer.Ordinal);
var reporterCaseOutcomes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
var reporterProblems = new List<string>();
int reporterReceiptRows = 0;

foreach ((string line, int index) in File.ReadLines(eventsPath).Select((line, index) => (line, index)))
{
    if (string.IsNullOrWhiteSpace(line))
    {
        reporterProblems.Add($"line {index + 1}: empty receipt row");
        continue;
    }

    JsonDocument receiptDocument;
    try
    {
        receiptDocument = JsonDocument.Parse(line);
    }
    catch (JsonException ex)
    {
        reporterProblems.Add($"line {index + 1}: malformed JSON receipt ({ex.Message})");
        continue;
    }

    using (receiptDocument)
    {
        JsonElement root = receiptDocument.RootElement;
        reporterReceiptRows++;
        string? schema = JsonString(root, "schema");
        string? caseId = JsonString(root, "uid");
        string? className = JsonString(root, "class");
        string? methodName = JsonString(root, "method");
        string? methodSignature = JsonString(root, "methodSignature");
        string? state = JsonString(root, "state");
        if (schema != ReceiptSchema
            || caseId is null
            || className is null
            || methodName is null
            || methodSignature is null
            || state is null)
        {
            reporterProblems.Add(
                $"line {index + 1}: execution row lacks the expected schema, UID, class, method, or state");
            continue;
        }

        string method = $"{className}.{methodSignature}";
        string xmlMethod = $"{className}.{methodName}";
        if (reporterCaseMethods.TryGetValue(caseId, out string? priorMethod)
            && priorMethod != method)
        {
            reporterProblems.Add(
                $"case {caseId}: metadata changed from {priorMethod} to {method}");
        }
        else
        {
            reporterCaseMethods[caseId] = method;
        }

        if (reporterCaseXmlMethods.TryGetValue(caseId, out string? priorXmlMethod)
            && priorXmlMethod != xmlMethod)
        {
            reporterProblems.Add(
                $"case {caseId}: XML metadata changed from {priorXmlMethod} to {xmlMethod}");
        }
        else
        {
            reporterCaseXmlMethods[caseId] = xmlMethod;
        }

        if (state == "started")
        {
            reporterCaseStarts[caseId] =
                reporterCaseStarts.GetValueOrDefault(caseId) + 1;
            continue;
        }

        string? result = state switch
        {
            "passed" => "Pass",
            "failed" or "error" or "timeout" => "Fail",
            "skipped" => "Skip",
            "cancelled" => "NotRun",
            _ => null,
        };
        if (result is null)
        {
            reporterProblems.Add(
                $"line {index + 1}: unexpected execution state '{state}'");
            continue;
        }

        if (!reporterCaseOutcomes.TryGetValue(caseId, out List<string>? outcomes))
            reporterCaseOutcomes[caseId] = outcomes = [];
        outcomes.Add(result);
    }
}

if (reporterReceiptRows == 0)
    reporterProblems.Add("the execution receipt contains no MTP test-node rows");

var reporterMethodOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
foreach ((string caseId, int count) in reporterCaseStarts)
{
    if (!reporterCaseXmlMethods.TryGetValue(caseId, out string? method))
    {
        reporterProblems.Add($"case {caseId}: test started without test-case metadata");
        continue;
    }

    reporterMethodOccurrences[method] =
        reporterMethodOccurrences.GetValueOrDefault(method) + count;
}

foreach (string caseId in reporterCaseMethods.Keys.Except(reporterCaseStarts.Keys, StringComparer.Ordinal))
{
    if (reporterCaseOutcomes.ContainsKey(caseId))
        reporterProblems.Add($"case {caseId}: outcome was reported but no test started");
}

var reporterOutcomeOccurrences = new Dictionary<(string Method, string Result), int>();
foreach (string caseId in reporterCaseStarts.Keys.Union(reporterCaseOutcomes.Keys, StringComparer.Ordinal))
{
    int started = reporterCaseStarts.GetValueOrDefault(caseId);
    int outcomes = reporterCaseOutcomes.TryGetValue(caseId, out List<string>? values)
        ? values.Count
        : 0;
    if (started != outcomes)
        reporterProblems.Add($"case {caseId}: {started} tests started but {outcomes} outcomes were reported");

    if (!reporterCaseXmlMethods.TryGetValue(caseId, out string? method) || values is null)
        continue;

    foreach (string result in values)
    {
        var outcomeKey = (method, result);
        reporterOutcomeOccurrences[outcomeKey] =
            reporterOutcomeOccurrences.GetValueOrDefault(outcomeKey) + 1;
    }
}

int reporterStarted = reporterCaseStarts.Values.Sum();
if (reporterStarted != tests.Count)
{
    reporterProblems.Add(
        $"the execution receipt started {reporterStarted} tests but the XML contains {tests.Count} rows");
}

foreach (string method in occurrences.Keys.Union(reporterMethodOccurrences.Keys, StringComparer.Ordinal))
{
    int xmlCount = occurrences.GetValueOrDefault(method);
    int reporterCount = reporterMethodOccurrences.GetValueOrDefault(method);
    if (xmlCount != reporterCount)
    {
        reporterProblems.Add(
            $"{method}: XML contains {xmlCount} rows but the execution receipt started {reporterCount} tests");
    }
}

foreach (var key in xmlOutcomeOccurrences.Keys.Union(reporterOutcomeOccurrences.Keys))
{
    int xmlCount = xmlOutcomeOccurrences.GetValueOrDefault(key);
    int reporterCount = reporterOutcomeOccurrences.GetValueOrDefault(key);
    if (xmlCount != reporterCount)
    {
        reporterProblems.Add(
            $"{key.Method} {key.Result}: XML contains {xmlCount} rows but the execution receipt contains "
                + $"{reporterCount} outcomes");
    }
}

var discoveredCases = new Dictionary<string, string>(StringComparer.Ordinal);
var discoveryProblems = new List<string>();
foreach ((string line, int index) in File.ReadLines(discoveredPath).Select((line, index) => (line, index)))
{
    if (string.IsNullOrWhiteSpace(line))
    {
        discoveryProblems.Add($"line {index + 1}: empty receipt row");
        continue;
    }

    JsonDocument receiptDocument;
    try
    {
        receiptDocument = JsonDocument.Parse(line);
    }
    catch (JsonException ex)
    {
        discoveryProblems.Add(
            $"line {index + 1}: malformed JSON receipt ({ex.Message})");
        continue;
    }

    using (receiptDocument)
    {
        JsonElement root = receiptDocument.RootElement;
        string? schema = JsonString(root, "schema");
        string? caseId = JsonString(root, "uid");
        string? className = JsonString(root, "class");
        string? methodName = JsonString(root, "method");
        string? methodSignature = JsonString(root, "methodSignature");
        string? state = JsonString(root, "state");
        if (schema != ReceiptSchema
            || caseId is null
            || className is null
            || methodName is null
            || methodSignature is null
            || state != "discovered")
        {
            discoveryProblems.Add(
                $"line {index + 1}: discovery row lacks the expected schema, UID, class, method, or state");
            continue;
        }

        string method = $"{className}.{methodSignature}";
        if (!discoveredCases.TryAdd(caseId, method))
            discoveryProblems.Add($"line {index + 1}: duplicate case UID {caseId}");
    }
}

if (discoveredCases.Count == 0)
{
    Console.Error.WriteLine($"error: {discoveredPath} lists no test cases.");
    Console.Error.WriteLine("Discovery matched nothing, so every report would satisfy it.");
    Console.Error.WriteLine("That is a broken preset, not an empty gate that passed.");
    return 2;
}

List<string> missingCases = [];
List<string> unexpectedCases = [];
var repeatedCases = reporterCaseStarts
    .Where(kv => kv.Value != 1)
    .Select(kv => $"{CaseLabel(kv.Key, discoveredCases, reporterCaseMethods)} ({kv.Value} executions)")
    .Order(StringComparer.Ordinal)
    .ToList();
var mismatchedCaseMethods = discoveredCases.Keys
    .Intersect(reporterCaseMethods.Keys, StringComparer.Ordinal)
    .Where(caseId => discoveredCases[caseId] != reporterCaseMethods[caseId])
    .Select(caseId =>
        $"{caseId}: discovery={discoveredCases[caseId]}, execution={reporterCaseMethods[caseId]}")
    .Order(StringComparer.Ordinal)
    .ToList();

if (!partial)
{
    missingCases = discoveredCases.Keys.Except(reporterCaseStarts.Keys, StringComparer.Ordinal)
        .Select(caseId => CaseLabel(caseId, discoveredCases, reporterCaseMethods))
        .Order(StringComparer.Ordinal)
        .ToList();
    unexpectedCases = reporterCaseStarts.Keys.Except(discoveredCases.Keys, StringComparer.Ordinal)
        .Select(caseId => CaseLabel(caseId, discoveredCases, reporterCaseMethods))
        .Order(StringComparer.Ordinal)
        .ToList();
}

var passedCases = reporterCaseOutcomes
    .Where(kv => kv.Value.Contains("Pass", StringComparer.Ordinal))
    .Select(kv => CaseLabel(kv.Key, discoveredCases, reporterCaseMethods))
    .ToHashSet(StringComparer.Ordinal);
var failedCases = reporterCaseOutcomes
    .Where(kv => kv.Value.Contains("Fail", StringComparer.Ordinal))
    .Select(kv => CaseLabel(kv.Key, discoveredCases, reporterCaseMethods))
    .ToHashSet(StringComparer.Ordinal);
passedCases.ExceptWith(failedCases);

var reportedCases = new HashSet<string>(passedCases, StringComparer.Ordinal);
reportedCases.UnionWith(failedCases);
reportedCases.UnionWith(
    reporterCaseOutcomes
        .Where(kv => kv.Value.Any(result => result is "Skip" or "NotRun"))
        .Select(kv => CaseLabel(kv.Key, discoveredCases, reporterCaseMethods)));

var newFailures = failedCases.Except(pinned, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
var nowPassing = pinned.Intersect(passedCases, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
var deadPins = partial
    ? []
    : pinned.Except(reportedCases, StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToList();

Console.WriteLine(
    $"Decompiler gate: {tests.Count} cases, {actualPassed} passed, {actualFailed} failed, "
        + $"{tests.Count - actualPassed - actualFailed} not executed, "
        + $"{coveredClasses.Count}/{expectedClasses.Count} expected "
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

if (discoveryProblems.Count > 0)
{
    Console.WriteLine($"MALFORMED DISCOVERY ({discoveryProblems.Count}) — unusable case entries:");
    foreach (var problem in discoveryProblems)
        Console.WriteLine($"  {problem}");
    Console.WriteLine();
    Console.WriteLine("  Discovery is the independent completeness reference. Missing or duplicate");
    Console.WriteLine("  structured identities make that reference unsafe.");
    Console.WriteLine();
}

if (reporterProblems.Count > 0)
{
    Console.WriteLine($"MALFORMED EXECUTION RECEIPT ({reporterProblems.Count}) — receipt/XML disagreement:");
    foreach (var problem in reporterProblems.Order(StringComparer.Ordinal))
        Console.WriteLine($"  {problem}");
    Console.WriteLine();
    Console.WriteLine("  The MTP receipt supplies case identity while XML supplies outcomes.");
    Console.WriteLine("  They must describe the same execution before either can clear the gate.");
    Console.WriteLine();
}

if (missingCases.Count > 0)
{
    Console.WriteLine($"INCOMPLETE REPORT ({missingCases.Count}) — discovered cases that never executed:");
    foreach (var name in missingCases)
        Console.WriteLine($"  {name}");
    Console.WriteLine();
    Console.WriteLine("  Discovery says the preset selects these cases; execution never started");
    Console.WriteLine("  them. The run was cut short, filtered down, or rewritten.");
    Console.WriteLine();
}

if (repeatedCases.Count > 0)
{
    Console.WriteLine($"NON-ENUMERATED OR REPEATED CASES ({repeatedCases.Count}) — case ID did not run once:");
    foreach (var name in repeatedCases)
        Console.WriteLine($"  {name}");
    Console.WriteLine();
    Console.WriteLine("  Every discovered TestNodeUid must start exactly one test. More than one");
    Console.WriteLine("  means xUnit delayed theory enumeration (or retried a test), so discovery");
    Console.WriteLine("  did not independently enumerate every execution and cannot prove");
    Console.WriteLine("  completeness. Use serializable theory data or plain facts.");
    Console.WriteLine();
}

if (mismatchedCaseMethods.Count > 0)
{
    Console.WriteLine(
        $"CASE IDENTITY MISMATCHES ({mismatchedCaseMethods.Count}) — discovery and execution disagree:");
    foreach (var name in mismatchedCaseMethods)
        Console.WriteLine($"  {name}");
    Console.WriteLine();
    Console.WriteLine("  A stable case ID must name the same structured class and method in both");
    Console.WriteLine("  artifacts. Display names are deliberately not used as a fallback.");
    Console.WriteLine();
}

if (unexpectedCases.Count > 0)
{
    Console.WriteLine($"UNEXPECTED RESULTS ({unexpectedCases.Count}) — executed but never discovered:");
    foreach (var name in unexpectedCases)
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
    foreach (string method in newFailures
        .Select(name =>
        {
            int bracket = name.LastIndexOf(" [", StringComparison.Ordinal);
            return bracket > 0 ? name[..bracket] : null;
        })
        .OfType<string>()
        .Distinct(StringComparer.Ordinal))
    {
        foreach (XElement test in tests.Where(test =>
            (string?)test.Attribute("result") == "Fail"
            && TestName(test) == method))
        {
            string displayName = (string?)test.Attribute("name") ?? method;
            string? message = test.Descendants("message").FirstOrDefault()?.Value;
            Console.WriteLine($"  {displayName}");
            if (!string.IsNullOrWhiteSpace(message))
            {
                foreach (string line in message.Split('\n'))
                    Console.WriteLine($"    {line.TrimEnd('\r')}");
            }
            Console.WriteLine();
        }
    }
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
    Console.WriteLine("  un-gates the case it names.");
    Console.WriteLine();
}

if (deadPins.Count > 0)
{
    Console.WriteLine($"DEAD PINS ({deadPins.Count}) — pinned but not present in the run:");
    foreach (var name in deadPins)
        Console.WriteLine($"  {name}");
    Console.WriteLine();
    Console.WriteLine("  The case was renamed or deleted. Update the pin, or pass --partial if you");
    Console.WriteLine("  deliberately ran a subset.");
    Console.WriteLine();
}

if (newFailures.Count == 0
    && notExecuted.Count == 0
    && nowPassing.Count == 0
    && deadPins.Count == 0
    && missingClasses.Count == 0
    && discoveryProblems.Count == 0
    && reporterProblems.Count == 0
    && missingCases.Count == 0
    && unexpectedCases.Count == 0
    && repeatedCases.Count == 0
    && mismatchedCaseMethods.Count == 0
    && unidentified == 0)
{
    Console.WriteLine("OK: the failing set matches the known-red list exactly.");
    if (pinned.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"Reminder: {pinned.Count} gate case(s) are still red. They are pinned, not fixed.");
    }
    return 0;
}

return 1;
