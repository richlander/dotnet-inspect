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

if (corpusList is not null)
    return RunCorpus(corpusList, diffBaseline, emitSnapshot, json);

if (fixturesMode || list)
    return RunFixtures(fixtureSelector, list, json, keep);

Console.Error.WriteLine(Usage);
return 2;

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
