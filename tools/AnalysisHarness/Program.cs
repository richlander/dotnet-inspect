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

      --precision-sample <assembly> [--top N] [--json]
          Layer 3 precision: emit the top-N triage candidates as a labeling worksheet for sampled
          true/false-positive judgement. No automatic oracle.

      --allocation-readout <file> [--top N] [--json]
          Sweep a corpus list and aggregate allocation occurrence/opportunity metadata
          distributions: allocation, path, path confidence, post dominance, escape, shape, and
          cross-tabs. Text output prints the top-N buckets per dimension; JSON emits every bucket.

      --allocation-parity <expected-annotations.json> <actual-annotations.json> [--json]
          Compare allocation annotations from the legacy decompiler classifier and a candidate
          occurrence-derived projection. The gate is exact on id, IL offset, detail, and
          conditionality; non-allocation annotations are ignored.

      --annotation-parity <category> <expected-annotations.json> <actual-annotations.json> [--json]
          Compare annotation rows for one category (Allocation, Unsafety, Lifetime).

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
string? referenceFile = null;
string? precisionAssembly = null;
string? allocationReadoutList = null;
string? allocationParityExpected = null;
string? allocationParityActual = null;
string? annotationParityCategory = null;
string? annotationParityExpected = null;
string? annotationParityActual = null;
int top = 20;

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
        case "--precision-sample":
            precisionAssembly = NextValue(args, ref i);
            break;
        case "--allocation-readout":
            allocationReadoutList = NextValue(args, ref i);
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
        case "--reference":
            referenceFile = NextValue(args, ref i);
            break;
        case "--top":
            if (NextValue(args, ref i) is { } t && int.TryParse(t, out int n)) top = n;
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

if (recallAssembly is not null)
    return RunRecall(recallAssembly, referenceFile);

if (precisionAssembly is not null)
    return RunPrecision(precisionAssembly, top);

if (allocationReadoutList is not null)
    return RunAllocationReadout(allocationReadoutList, top, json);

if (allocationParityExpected is not null)
    return RunAnnotationParity("Allocation", allocationParityExpected, allocationParityActual, json);

if (annotationParityExpected is not null)
    return RunAnnotationParity(annotationParityCategory ?? "", annotationParityExpected, annotationParityActual, json);

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
