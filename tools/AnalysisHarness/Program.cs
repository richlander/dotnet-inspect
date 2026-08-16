using ILInspector.AnalysisHarness;

const string Usage =
    """
    analysis-harness <mode>

      --generated-fixtures [<selector>|list] [--json] [--keep]
          Materialize the analysis fixture catalogue into temporary assemblies and grade each
          target method against its expected-signal ledger entry using the real analyzer.
          <selector>  a fixture id, id prefix, or tag; omit (or 'all') to run every fixture.
          list        list catalogue entries and expected outcomes without building.

      --corpus-list <file> [--diff-corpus-baseline <file>] [--emit-corpus-snapshot <file>] [--json]
          Sweep the analyzer over a corpus (one assembly path per line in <file>) and report the
          stability card. With --diff-corpus-baseline, compare against a committed baseline and
          exit nonzero on a REGRESSION (an assembly that stops opening, or an analyzer-diagnostic
          increase); signal-count DRIFT is reported but not a failure. --emit-corpus-snapshot
          writes the snapshot JSON (use it to (re)generate a baseline).

      --paydirt-recall <assembly> [--reference <file>]
          Layer 3 recall: check that every committed reference paydirt site for the assembly still
          surfaces as a loop+high triage candidate. Exit nonzero on a missing site (recall
          regression). Defaults to corpus/paydirt-reference.json.

      --historical-performance-recall [<file>]
          Acquire the exact NuGet before/after cells in the committed historical performance
          reference and verify their member+shape counts. This explicit mode uses the network
          when packages are not cached. Defaults to corpus/historical-performance-reference.json.

      --precision-sample <assembly> [--top N] [--json]
          Layer 3 precision: emit the top-N triage candidates as a labeling worksheet for sampled
          true/false-positive judgement. No automatic oracle.

      --clone-corpus <assembly> [--relationship-ledger <file>] [--json]
          Grade product-owned structural clone comparison and exact discovery against the
          committed closed-world corpus. The harness resolves typed metadata identities and does
          not reconstruct candidate retrieval, normalization, correspondence, or verification.

      --clone-census <assembly> [--seed <0xMethodDef|Type::Method>] [--top N]
          Run bounded product-owned exact discovery over every MethodDef in one assembly.
          Reports exact families, receipts, suppression, and an optional seed-to-family drill-in.
          --max-methods and --max-comparisons override the product admission/comparison limits.

      --allocation-readout <file> [--top N] [--json]
          Sweep a corpus list and aggregate allocation occurrence/opportunity metadata
          distributions: allocation, path, path confidence, post dominance, escape, shape, and
          cross-tabs. Text output prints the top-N buckets per dimension; JSON emits every bucket.

      --caller-loop-census <file> [--max-depth N] [--top N] [--json]
          Measurement-only census of Performance Triage rows reachable downstream from a caller's
          loop invocation. Reports direct/transitive/none/beyond-bound populations, nearest depth,
          deterministic witness paths, provenance, and shape/confidence/local-multiplicity cross-tabs.

      --deferred-callback-census <file> [--max-depth N] [--top N] [--json]
          Measurement-only census of in-loop function loads that form an exact delegate construction
          and immediate consumer/registration shape. Separates proven immediate Invoke calls from
          trusted framework registration and unknown consumers, then reports downstream triage rows.

      --recursive-traversal-census <file> [--max-depth N] [--top N] [--json]
          Measurement-only census of Performance Triage rows reached from methods with an exact
          in-loop self-recursive call. Reports the recursion edge, downstream invocation witness,
          depth, provenance, and unchanged local multiplicity/loop semantics.

      --allocation-parity <expected-annotations.json> <actual-annotations.json> [--json]
          Compare allocation annotations from the legacy decompiler classifier and a candidate
          occurrence-derived projection. The gate is exact on id, IL offset, detail, and
          conditionality; non-allocation annotations are ignored.

      --annotation-parity <category> <expected-annotations.json> <actual-annotations.json> [--json]
          Compare annotation rows for one category (Allocation, Unsafety, Lifetime).

      --leak-triage <file> [--top N] [--tsv | --jsonl]
          Sweep the fail-closed ArrayPool leak-triage analyzer over a corpus (one assembly path
          per line in <file>) and report where it fires: total findings, the shape histogram, and
          example methods per assembly, as a Markout card. Default is Markdown; --tsv and --jsonl
          select the tabular formats (--json is an alias for --jsonl). This is the evidence engine
          for correctness-oriented #1992 work - the analyzer is precision-first, so an empty
          findings card means recall is the next lever. --top bounds examples per assembly.

      --leak-actionability <file> [--top N] [--tsv | --jsonl]
          Report Analysis-owned `analysis.resource-lifecycle` findings by actionability (#2439):
          untrusted-actionable (an exact boundary reads/decodes/parses external input),
          trusted-low-actionability (only in-memory transforms), or unknown. The harness owns
          corpus orchestration and reporting, not boundary attribution or classification. Formats
          match --leak-triage; --top bounds examples per class.

      --memorypool-lifecycle <file> [--top N] [--tsv | --jsonl]
          MemoryPool lifecycle census (#2439 Slice 3), measurement-only: find every
          MemoryPool<T>.Rent site (one assembly path per line in <file>), track the returned
          IMemoryOwner<T> through the reaching-definitions def/use web, and bucket each site as
          disposed-in-scope (using/finally), exception-path-leak-candidate (disposed only on the
          normal path), normal-path-leak-candidate (never disposed, never escapes),
          ownership-transfer-suppressed (returned/stored/passed onward), or
          incomplete-or-ambiguous-suppressed. Precision-first; changes no analyzer behavior.
          Formats match --leak-triage; --top bounds examples per class.

      Common: --json machine-readable output; --keep keep generated fixture projects.
    """;

if (args.Length == 0)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

string? fixtureSelector = null;
bool fixturesMode = false;
string? corpusList = null;
string? diffBaseline = null;
string? emitSnapshot = null;
bool json = false;
bool keep = false;
bool list = false;
string? recallAssembly = null;
string? historicalPerformanceReference = null;
string? referenceFile = null;
string? precisionAssembly = null;
string? cloneCorpusAssembly = null;
string? relationshipLedger = null;
bool cloneCorpusSpecified = false;
bool relationshipLedgerSpecified = false;
string? cloneCensusAssembly = null;
string? cloneCensusSeed = null;
bool cloneCensusSpecified = false;
bool cloneCensusSeedSpecified = false;
bool cloneMaximumMethodsSpecified = false;
bool cloneMaximumComparisonsSpecified = false;
int cloneMaximumMethods = 50_000;
int cloneMaximumComparisons = 100_000;
string? allocationReadoutList = null;
string? callerLoopCensusList = null;
string? deferredCallbackCensusList = null;
string? recursiveTraversalCensusList = null;
string? allocationParityExpected = null;
string? allocationParityActual = null;
string? annotationParityCategory = null;
string? annotationParityExpected = null;
string? annotationParityActual = null;
string? leakTriageList = null;
string? leakActionabilityList = null;
string? memoryPoolLifecycleList = null;
bool tsv = false;
bool jsonl = false;
int top = 20;
bool topArgumentValid = true;
int maxDepth = 4;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--generated-fixtures":
            fixturesMode = true;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                fixtureSelector = args[++i];
            break;
        case "--corpus-list":
            corpusList = NextValue(args, ref i);
            break;
        case "--diff-corpus-baseline":
            diffBaseline = NextValue(args, ref i);
            break;
        case "--emit-corpus-snapshot":
            emitSnapshot = NextValue(args, ref i);
            break;
        case "--paydirt-recall":
            recallAssembly = NextValue(args, ref i);
            break;
        case "--historical-performance-recall":
            historicalPerformanceReference =
                NextPathValue(args, ref i)
                ?? Path.Combine(
                    AppContext.BaseDirectory,
                    "corpus",
                    "historical-performance-reference.json");
            break;
        case "--precision-sample":
            precisionAssembly = NextValue(args, ref i);
            break;
        case "--clone-corpus":
            cloneCorpusSpecified = true;
            cloneCorpusAssembly = NextPathValue(args, ref i);
            break;
        case "--relationship-ledger":
            relationshipLedgerSpecified = true;
            relationshipLedger = NextPathValue(args, ref i);
            break;
        case "--clone-census":
            cloneCensusSpecified = true;
            cloneCensusAssembly = NextPathValue(args, ref i);
            break;
        case "--seed":
            cloneCensusSeedSpecified = true;
            cloneCensusSeed = NextPathValue(args, ref i);
            break;
        case "--max-methods":
            cloneMaximumMethodsSpecified = true;
            if (NextPathValue(args, ref i) is not { } methodLimit
                || !int.TryParse(methodLimit, out cloneMaximumMethods)
                || cloneMaximumMethods < 1)
            {
                Console.Error.WriteLine(
                    "--max-methods requires a positive integer.");
                return 2;
            }
            break;
        case "--max-comparisons":
            cloneMaximumComparisonsSpecified = true;
            if (NextPathValue(args, ref i) is not { } comparisonLimit
                || !int.TryParse(
                    comparisonLimit,
                    out cloneMaximumComparisons)
                || cloneMaximumComparisons < 1)
            {
                Console.Error.WriteLine(
                    "--max-comparisons requires a positive integer.");
                return 2;
            }
            break;
        case "--allocation-readout":
            allocationReadoutList = NextValue(args, ref i);
            break;
        case "--caller-loop-census":
            callerLoopCensusList = NextValue(args, ref i);
            break;
        case "--deferred-callback-census":
            deferredCallbackCensusList = NextValue(args, ref i);
            break;
        case "--recursive-traversal-census":
            recursiveTraversalCensusList = NextValue(args, ref i);
            break;
        case "--allocation-parity":
            allocationParityExpected = NextPathValue(args, ref i);
            allocationParityActual = NextPathValue(args, ref i);
            break;
        case "--annotation-parity":
            annotationParityCategory = NextValue(args, ref i);
            annotationParityExpected = NextPathValue(args, ref i);
            annotationParityActual = NextPathValue(args, ref i);
            break;
        case "--leak-triage":
            leakTriageList = NextPathValue(args, ref i);
            break;
        case "--leak-actionability":
            leakActionabilityList = NextPathValue(args, ref i);
            break;
        case "--memorypool-lifecycle":
            memoryPoolLifecycleList = NextPathValue(args, ref i);
            break;
        case "--tsv":
            tsv = true;
            break;
        case "--jsonl":
            jsonl = true;
            break;
        case "--reference":
            referenceFile = NextValue(args, ref i);
            break;
        case "--top":
            if (NextPathValue(args, ref i) is not { } t
                || !int.TryParse(t, out top))
            {
                topArgumentValid = false;
            }
            break;
        case "--max-depth":
            if (NextValue(args, ref i) is not { } depth
                || !int.TryParse(depth, out maxDepth)
                || maxDepth < 1)
            {
                Console.Error.WriteLine("--max-depth requires a positive integer.");
                return 2;
            }
            break;
        case "list":
            list = true;
            break;
        case "--json":
            json = true;
            break;
        case "--keep":
            keep = true;
            break;
        case "-h" or "--help":
            Console.WriteLine(Usage);
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            Console.Error.WriteLine(Usage);
            return 2;
    }
}

if (cloneCorpusSpecified && cloneCorpusAssembly is null)
{
    Console.Error.WriteLine("--clone-corpus requires an assembly path.");
    return 2;
}
if (relationshipLedgerSpecified && relationshipLedger is null)
{
    Console.Error.WriteLine("--relationship-ledger requires a file path.");
    return 2;
}
if (relationshipLedgerSpecified && !cloneCorpusSpecified)
{
    Console.Error.WriteLine(
        "--relationship-ledger requires --clone-corpus.");
    return 2;
}
if (cloneCensusSpecified && cloneCensusAssembly is null)
{
    Console.Error.WriteLine("--clone-census requires an assembly path.");
    return 2;
}
if (cloneCensusSeedSpecified && cloneCensusSeed is null)
{
    Console.Error.WriteLine("--seed requires a selector.");
    return 2;
}
if ((cloneCensusSeedSpecified
        || cloneMaximumMethodsSpecified
        || cloneMaximumComparisonsSpecified)
    && !cloneCensusSpecified)
{
    Console.Error.WriteLine(
        "--seed, --max-methods, and --max-comparisons require "
            + "--clone-census.");
    return 2;
}
if (cloneCensusSpecified && (!topArgumentValid || top < 1))
{
    Console.Error.WriteLine("--top requires a positive integer.");
    return 2;
}

List<string> selectedModes = [];
if (fixturesMode || list)
    selectedModes.Add("--generated-fixtures");
if (corpusList is not null)
    selectedModes.Add("--corpus-list");
if (recallAssembly is not null)
    selectedModes.Add("--paydirt-recall");
if (historicalPerformanceReference is not null)
    selectedModes.Add("--historical-performance-recall");
if (precisionAssembly is not null)
    selectedModes.Add("--precision-sample");
if (cloneCorpusSpecified)
    selectedModes.Add("--clone-corpus");
if (cloneCensusSpecified)
    selectedModes.Add("--clone-census");
if (allocationReadoutList is not null)
    selectedModes.Add("--allocation-readout");
if (callerLoopCensusList is not null)
    selectedModes.Add("--caller-loop-census");
if (deferredCallbackCensusList is not null)
    selectedModes.Add("--deferred-callback-census");
if (recursiveTraversalCensusList is not null)
    selectedModes.Add("--recursive-traversal-census");
if (allocationParityExpected is not null)
    selectedModes.Add("--allocation-parity");
if (annotationParityExpected is not null)
    selectedModes.Add("--annotation-parity");
if (leakTriageList is not null)
    selectedModes.Add("--leak-triage");
if (leakActionabilityList is not null)
    selectedModes.Add("--leak-actionability");
if (memoryPoolLifecycleList is not null)
    selectedModes.Add("--memorypool-lifecycle");
if (selectedModes.Count > 1)
{
    Console.Error.WriteLine(
        "Analysis harness modes are mutually exclusive: "
            + string.Join(", ", selectedModes)
            + ".");
    return 2;
}

// --tsv/--jsonl are tabular-format selectors for the leak cards; other modes use --json.
// Reject them elsewhere rather than silently accepting-and-ignoring them.
if ((tsv || jsonl) && leakTriageList is null && leakActionabilityList is null && memoryPoolLifecycleList is null)
{
    Console.Error.WriteLine("--tsv and --jsonl apply only to --leak-triage / --leak-actionability / --memorypool-lifecycle; other modes use --json.");
    return 2;
}

if (recallAssembly is not null)
    return RunRecall(recallAssembly, referenceFile);

if (historicalPerformanceReference is not null)
    return await HistoricalPerformanceRecall.RunAsync(
        historicalPerformanceReference);

if (precisionAssembly is not null)
    return RunPrecision(precisionAssembly, top);

if (cloneCorpusAssembly is not null)
    return RunCloneCorpus(cloneCorpusAssembly, relationshipLedger, json);

if (cloneCensusAssembly is not null)
{
    return RunCloneCensus(
        cloneCensusAssembly,
        cloneCensusSeed,
        cloneMaximumMethods,
        cloneMaximumComparisons,
        top,
        json);
}

if (allocationReadoutList is not null)
    return RunAllocationReadout(allocationReadoutList, top, json);

if (callerLoopCensusList is not null)
    return RunCallerLoopCensus(callerLoopCensusList, maxDepth, top, json);

if (deferredCallbackCensusList is not null)
    return RunDeferredCallbackCensus(deferredCallbackCensusList, maxDepth, top, json);

if (recursiveTraversalCensusList is not null)
    return RunRecursiveTraversalCensus(recursiveTraversalCensusList, maxDepth, top, json);

if (allocationParityExpected is not null)
    return RunAnnotationParity("Allocation", allocationParityExpected, allocationParityActual, json);

if (annotationParityExpected is not null)
    return RunAnnotationParity(annotationParityCategory ?? "", annotationParityExpected, annotationParityActual, json);

if (leakTriageList is not null)
    return RunLeakTriage(leakTriageList, top, tsv, jsonl || json);

if (leakActionabilityList is not null)
    return RunLeakActionability(leakActionabilityList, top, tsv, jsonl || json);

if (memoryPoolLifecycleList is not null)
    return RunMemoryPoolLifecycle(memoryPoolLifecycleList, top, tsv, jsonl || json);

if (corpusList is not null)
    return RunCorpus(corpusList, diffBaseline, emitSnapshot, json);

if (fixturesMode || list)
    return RunFixtures(fixtureSelector, list, json, keep);

Console.Error.WriteLine(Usage);
return 2;

static int RunRecall(string assembly, string? referenceFile)
{
    if (!File.Exists(assembly)) { Console.Error.WriteLine($"Assembly not found: {assembly}"); return 2; }
    string refPath = referenceFile ?? Path.Combine(AppContext.BaseDirectory, "corpus", "paydirt-reference.json");
    if (!File.Exists(refPath)) { Console.Error.WriteLine($"Reference not found: {refPath}"); return 2; }
    string name = Path.GetFileName(assembly);
    var all = PrecisionRecall.ReferencesFromJson(File.ReadAllText(refPath));
    var forAssembly = all.Where(r => r.Assembly == name).ToList();
    if (forAssembly.Count == 0)
    {
        Console.Error.WriteLine($"No reference paydirt sites for '{name}' in {refPath} — nothing to recall-check.");
        return 2;
    }
    var result = PrecisionRecall.CheckRecall(assembly, forAssembly);
    Console.Write(PrecisionRecall.FormatRecall(name, result));
    return result.Passed ? 0 : 1;
}

static int RunPrecision(string assembly, int top)
{
    if (!File.Exists(assembly)) { Console.Error.WriteLine($"Assembly not found: {assembly}"); return 2; }
    Console.WriteLine(PrecisionRecall.ToJson(PrecisionRecall.Sample(assembly, top)));
    return 0;
}

static int RunCloneCorpus(
    string assembly,
    string? relationshipLedger,
    bool json)
{
    if (!File.Exists(assembly))
    {
        Console.Error.WriteLine($"Assembly not found: {assembly}");
        return 2;
    }
    string ledger =
        relationshipLedger
        ?? Path.Combine(
            AppContext.BaseDirectory,
            "corpus",
            "structural-clone-relationships.json");
    if (!File.Exists(ledger))
    {
        Console.Error.WriteLine($"Relationship ledger not found: {ledger}");
        return 2;
    }

    StructuralCloneCorpusReport report = StructuralCloneCorpus.Run(
        assembly,
        StructuralCloneCorpus.Load(File.ReadAllText(ledger)));
    Console.Write(
        json
            ? StructuralCloneCorpus.ToJson(report) + Environment.NewLine
            : StructuralCloneCorpus.Format(report));
    return report.Success ? 0 : 1;
}

static int RunCloneCensus(
    string assembly,
    string? seed,
    int maximumMethods,
    int maximumComparisons,
    int top,
    bool json)
{
    if (!File.Exists(assembly))
    {
        Console.Error.WriteLine($"Assembly not found: {assembly}");
        return 2;
    }

    try
    {
        StructuralCloneCensusReport report = StructuralCloneCensus.Run(
            assembly,
            seed,
            maximumMethods,
            maximumComparisons);
        Console.Write(
            json
                ? StructuralCloneCensus.ToJson(report)
                    + Environment.NewLine
                : StructuralCloneCensus.Format(report, top));
        return report.Success ? 0 : 1;
    }
    catch (Exception ex) when (
        ex is InvalidDataException
            or BadImageFormatException
            or IOException
            or UnauthorizedAccessException)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }
}

static int RunAllocationReadout(string corpusList, int top, bool json)
{
    if (!File.Exists(corpusList))
    {
        Console.Error.WriteLine($"Corpus list not found: {corpusList}");
        return 2;
    }

    var paths = File.ReadAllLines(corpusList)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith('#'))
        .ToList();
    if (paths.Count == 0)
    {
        Console.Error.WriteLine($"Corpus list is empty: {corpusList}");
        return 2;
    }

    var readout = AllocationMetadataReadout.Measure(paths);
    Console.Write(json ? AllocationMetadataReadout.ToJson(readout) : AllocationMetadataReadout.FormatCard(readout, top));
    if (json)
        Console.WriteLine();
    return 0;
}

static int RunCallerLoopCensus(string corpusList, int maxDepth, int top, bool json)
{
    if (!File.Exists(corpusList))
    {
        Console.Error.WriteLine($"Corpus list not found: {corpusList}");
        return 2;
    }

    var paths = File.ReadAllLines(corpusList)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith('#'))
        .ToList();
    if (paths.Count == 0)
    {
        Console.Error.WriteLine($"Corpus list is empty: {corpusList}");
        return 2;
    }

    var report = CallerLoopCensus.Measure(paths, maxDepth);
    Console.Write(json ? CallerLoopCensus.ToJson(report) : CallerLoopCensus.FormatCard(report, top));
    if (json)
        Console.WriteLine();
    return report.Failed == 0 ? 0 : 1;
}

static int RunDeferredCallbackCensus(string corpusList, int maxDepth, int top, bool json)
{
    if (!File.Exists(corpusList))
    {
        Console.Error.WriteLine($"Corpus list not found: {corpusList}");
        return 2;
    }

    var paths = File.ReadAllLines(corpusList)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith('#'))
        .ToList();
    if (paths.Count == 0)
    {
        Console.Error.WriteLine($"Corpus list is empty: {corpusList}");
        return 2;
    }

    var report = DeferredCallbackCensus.Measure(paths, maxDepth);
    Console.Write(json ? DeferredCallbackCensus.ToJson(report) : DeferredCallbackCensus.FormatCard(report, top));
    if (json)
        Console.WriteLine();
    return report.Failed == 0 ? 0 : 1;
}

static int RunRecursiveTraversalCensus(string corpusList, int maxDepth, int top, bool json)
{
    if (!File.Exists(corpusList))
    {
        Console.Error.WriteLine($"Corpus list not found: {corpusList}");
        return 2;
    }

    var paths = File.ReadAllLines(corpusList)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith('#'))
        .ToList();
    if (paths.Count == 0)
    {
        Console.Error.WriteLine($"Corpus list is empty: {corpusList}");
        return 2;
    }

    var report = RecursiveTraversalCensus.Measure(paths, maxDepth);
    Console.Write(json ? RecursiveTraversalCensus.ToJson(report) : RecursiveTraversalCensus.FormatCard(report, top));
    if (json)
        Console.WriteLine();
    return report.Failed == 0 ? 0 : 1;
}

static int RunLeakTriage(string corpusList, int top, bool tsv, bool jsonl)
{
    if (!File.Exists(corpusList))
    {
        Console.Error.WriteLine($"Corpus list not found: {corpusList}");
        return 2;
    }

    if (tsv && jsonl)
    {
        Console.Error.WriteLine("--tsv and --jsonl are mutually exclusive.");
        return 2;
    }

    LeakTriageFormat format = jsonl ? LeakTriageFormat.Jsonl : tsv ? LeakTriageFormat.Tsv : LeakTriageFormat.Markdown;

    var paths = File.ReadAllLines(corpusList)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith('#'))
        .ToList();
    if (paths.Count == 0)
    {
        Console.Error.WriteLine($"Corpus list is empty: {corpusList}");
        return 2;
    }

    var report = LeakTriageSensor.Measure(paths, examplesPerAssembly: top);
    Console.Write(LeakTriageSensor.Format(report, top, format));
    return 0;
}

static int RunLeakActionability(string corpusList, int top, bool tsv, bool jsonl)
{
    if (!File.Exists(corpusList))
    {
        Console.Error.WriteLine($"Corpus list not found: {corpusList}");
        return 2;
    }

    if (tsv && jsonl)
    {
        Console.Error.WriteLine("--tsv and --jsonl are mutually exclusive.");
        return 2;
    }

    LeakActionabilityFormat format = jsonl ? LeakActionabilityFormat.Jsonl : tsv ? LeakActionabilityFormat.Tsv : LeakActionabilityFormat.Markdown;

    var paths = File.ReadAllLines(corpusList)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith('#'))
        .ToList();
    if (paths.Count == 0)
    {
        Console.Error.WriteLine($"Corpus list is empty: {corpusList}");
        return 2;
    }

    var report = LeakActionabilitySensor.Measure(paths, examplesPerAssembly: top);
    Console.Write(LeakActionabilitySensor.Format(report, top, format));
    return 0;
}

static int RunMemoryPoolLifecycle(string corpusList, int top, bool tsv, bool jsonl)
{
    if (!File.Exists(corpusList))
    {
        Console.Error.WriteLine($"Corpus list not found: {corpusList}");
        return 2;
    }

    if (tsv && jsonl)
    {
        Console.Error.WriteLine("--tsv and --jsonl are mutually exclusive.");
        return 2;
    }

    MemoryPoolLifecycleFormat format = jsonl ? MemoryPoolLifecycleFormat.Jsonl : tsv ? MemoryPoolLifecycleFormat.Tsv : MemoryPoolLifecycleFormat.Markdown;

    var paths = File.ReadAllLines(corpusList)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith('#'))
        .ToList();
    if (paths.Count == 0)
    {
        Console.Error.WriteLine($"Corpus list is empty: {corpusList}");
        return 2;
    }

    var report = MemoryPoolLifecycleSensor.Measure(paths, examplesPerAssembly: top);
    Console.Write(MemoryPoolLifecycleSensor.Format(report, top, format));
    return 0;
}

static int RunAnnotationParity(string category, string expectedPath, string? actualPath, bool json)
{
    if (string.IsNullOrWhiteSpace(category))
    {
        Console.Error.WriteLine("--annotation-parity requires a category.");
        return 2;
    }
    if (actualPath is null)
    {
        Console.Error.WriteLine("annotation parity requires expected and actual annotation JSON files.");
        return 2;
    }
    if (!File.Exists(expectedPath)) { Console.Error.WriteLine($"Expected annotation JSON not found: {expectedPath}"); return 2; }
    if (!File.Exists(actualPath)) { Console.Error.WriteLine($"Actual annotation JSON not found: {actualPath}"); return 2; }

    var result = AllocationAnnotationParity.CompareJson(
        File.ReadAllText(expectedPath),
        File.ReadAllText(actualPath),
        category);
    Console.Write(json
        ? AllocationAnnotationParity.ToJson(result)
        : AllocationAnnotationParity.Format(result));
    if (json) Console.WriteLine();
    return result.Passed ? 0 : 1;
}

static int RunFixtures(string? selector, bool list, bool json, bool keep)
{
    if (list || selector is "list")
    {
        Console.Write(json
            ? AnalysisFixtureRunner.FormatListJson(AnalysisFixtureCatalog.All)
            : AnalysisFixtureRunner.FormatList(AnalysisFixtureCatalog.All));
        return 0;
    }

    var fixtures = AnalysisFixtureCatalog.Select(selector);
    if (fixtures.Count == 0)
    {
        Console.Error.WriteLine($"No analysis fixture IDs match '{selector}'. Use '--generated-fixtures list'.");
        return 2;
    }

    var run = AnalysisFixtureRunner.Run(fixtures, new AnalysisFixtureRunOptions(KeepArtifacts: keep));
    Console.Write(json ? AnalysisFixtureRunner.FormatJson(run) : AnalysisFixtureRunner.FormatReport(run));
    if (json)
        Console.WriteLine();
    return run.Passed ? 0 : 1;
}

static int RunCorpus(string corpusList, string? diffBaseline, string? emitSnapshot, bool json)
{
    if (!File.Exists(corpusList))
    {
        Console.Error.WriteLine($"Corpus list not found: {corpusList}");
        return 2;
    }

    var paths = File.ReadAllLines(corpusList)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith('#'))
        .ToList();
    if (paths.Count == 0)
    {
        Console.Error.WriteLine($"Corpus list is empty: {corpusList}");
        return 2;
    }

    var snapshot = AnalysisCorpusSensor.Measure(paths);

    if (emitSnapshot is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(emitSnapshot))!);
        File.WriteAllText(emitSnapshot, AnalysisCorpusSensor.ToJson(snapshot));
        Console.Error.WriteLine($"Wrote corpus snapshot: {emitSnapshot}");
    }

    if (diffBaseline is null)
    {
        Console.Write(json ? AnalysisCorpusSensor.ToJson(snapshot) : AnalysisCorpusSensor.FormatCard(snapshot));
        if (json) Console.WriteLine();
        return 0;
    }

    if (!File.Exists(diffBaseline))
    {
        Console.Error.WriteLine($"Baseline not found: {diffBaseline}");
        return 2;
    }

    var baseline = AnalysisCorpusSensor.FromJson(File.ReadAllText(diffBaseline));
    var diff = AnalysisCorpusSensor.Diff(baseline, snapshot);
    Console.Write(AnalysisCorpusSensor.FormatDiffCard(diff));
    return diff.HasRegression ? 1 : 0;
}

static string? NextValue(string[] args, ref int i)
    => i + 1 < args.Length ? args[++i] : null;

static string? NextPathValue(string[] args, ref int i)
    => i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
        ? args[++i]
        : null;
