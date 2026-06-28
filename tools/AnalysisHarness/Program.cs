using ILInspector.AnalysisHarness;

const string Usage =
    """
    analysis-harness --generated-fixtures [<selector>|list] [--json] [--keep]

      Materializes the analysis fixture catalogue into temporary assemblies and grades each
      target method against its expected-signal ledger entry using the real analyzer.

      <selector>   a fixture id, id prefix, or tag; omit (or 'all') to run every fixture.
      list         list catalogue entries and expected outcomes without building.
      --json       emit machine-readable JSON.
      --keep       keep the generated temporary projects for drill-down.
    """;

if (args.Length == 0)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

string? selector = null;
bool json = false;
bool keep = false;
bool list = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--generated-fixtures":
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                selector = args[++i];
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
