using DotnetInspector.PerformanceOracles;
using ILInspector.Analysis.Planning;

// method-body-demand-scorecard check <assembly>...
// method-body-demand-scorecard time [--rounds N] [--budget-ms N]
//   [--tsv <path>] <assembly>...
if (!ScorecardCommandLine.TryParse(
        args,
        out ScorecardOptions? options,
        out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

var shape = new ScorecardShape();
ScorecardColumn<
    PreparedMethodBodies,
    MethodThrowPresenceSummary>[] columns =
        MethodBodyDemandPrototype.Columns();
ScorecardColumn<PreparedMethodBodies, MethodThrowPresenceSummary> oracle =
    columns[0];
IReadOnlyList<ScorecardAsset<PreparedMethodBodies>> assets =
    PreparedMethodBodies.LoadAssets(options!.Assets);

try
{
    WritePlan("single-throw", MethodBodyAnalyzerPlans.ThrowPresence);
    WritePlan("forward-shallow", MethodBodyAnalyzerPlans.ForwardShallow);
    WritePlan("mixed-classifiers", MethodBodyAnalyzerPlans.Mixed);

    bool exactAgreement = true;
    foreach (ScorecardAsset<PreparedMethodBodies> asset in assets)
    {
        foreach (MethodThrowPresenceMismatch mismatch
            in asset.Asset.CheckThrowPresenceAgreement())
        {
            exactAgreement = false;
            Console.WriteLine(
                $"body-mismatch\t{asset.Name}\t{mismatch.BodyIndex}\t"
                + $"streaming={mismatch.Streaming}\t"
                + $"lazy-shallow={mismatch.LazyShallow}\t"
                + $"eager-decoded={mismatch.EagerDecoded}");
        }
        foreach (StableGetterMismatch mismatch
            in asset.Asset.CheckStableGetterAgreement())
        {
            exactAgreement = false;
            Console.WriteLine(
                $"stable-getter-mismatch\t{asset.Name}\t"
                + $"0x{mismatch.MethodToken:X8}\t"
                + $"eager={mismatch.Eager}\t"
                + $"lazy-shallow={mismatch.LazyShallow}");
        }
        foreach (FlowProbeMismatch mismatch
            in asset.Asset.CheckFlowProbeAgreement())
        {
            exactAgreement = false;
            Console.WriteLine(
                $"flow-probe-mismatch\t{asset.Name}\t"
                + $"body={mismatch.BodyIndex}\t"
                + $"kind={mismatch.Kind}\t"
                + $"position={mismatch.Position}\t"
                + $"eager={mismatch.Eager}\t"
                + $"lazy-shallow={mismatch.LazyShallow}");
        }
        foreach (ClassifierQueryMismatch mismatch
            in asset.Asset.CheckClassifierQueryAgreement())
        {
            exactAgreement = false;
            Console.WriteLine(
                $"classifier-query-mismatch\t{asset.Name}\t"
                + $"body={mismatch.BodyIndex}\t"
                + $"eager-separate={mismatch.EagerSeparate}\t"
                + $"eager-fused={mismatch.EagerFused}\t"
                + $"planned={mismatch.Planned}");
        }
    }

    ScorecardCheck check = Scorecard.Check(
        assets,
        oracle,
        columns,
        MethodBodyDemandPrototype.RowText,
        closings: MethodBodyDemandPrototype.Closings);
    foreach (ScorecardMismatch mismatch in check.Mismatches)
    {
        Console.WriteLine(
            $"mismatch\t{mismatch.Asset}\t{mismatch.Column}\t"
            + $"{mismatch.Answer}\toracle={mismatch.OracleAnswer}");
    }

    foreach (ScorecardAsset<PreparedMethodBodies> asset in assets)
    {
        MethodThrowPresenceSummary summary = AssertSummary(
            oracle.Answer(ScorecardClosing.Rows, asset.Asset));
        Console.WriteLine($"answer\t{asset.Name}\t{summary}");
    }

    Console.WriteLine(
        $"# method-body demand: {check.Compared} compared, "
        + $"{check.Mismatches.Count} mismatches");

    ScorecardColumn<
        PreparedMethodBodies,
        StableGetterSummary>[] stableColumns =
            StableGetterPrototype.Columns();
    ScorecardCheck stableCheck = Scorecard.Check(
        assets,
        stableColumns[0],
        stableColumns,
        StableGetterPrototype.RowText,
        closings: StableGetterPrototype.Closings);
    foreach (ScorecardAsset<PreparedMethodBodies> asset in assets)
    {
        StableGetterSummary summary = AssertSummary(
            stableColumns[0].Answer(
                ScorecardClosing.Rows,
                asset.Asset),
            "stable getter");
        Console.WriteLine(
            $"stable-getter-answer\t{asset.Name}\t{summary}");
    }
    Console.WriteLine(
        $"# stable getter: {stableCheck.Compared} compared, "
        + $"{stableCheck.Mismatches.Count} mismatches");

    ScorecardColumn<
        PreparedMethodBodies,
        FlowProbeSummary>[] flowColumns =
            FlowProbePrototype.Columns();
    ScorecardCheck flowCheck = Scorecard.Check(
        assets,
        flowColumns[0],
        flowColumns,
        FlowProbePrototype.RowText,
        closings: FlowProbePrototype.Closings);
    foreach (ScorecardAsset<PreparedMethodBodies> asset in assets)
    {
        FlowProbeSummary summary = AssertSummary(
            flowColumns[0].Answer(
                ScorecardClosing.Rows,
                asset.Asset),
            "flow probe");
        Console.WriteLine(
            $"flow-probe-answer\t{asset.Name}\t{summary}");
    }
    Console.WriteLine(
        $"# flow probe: {flowCheck.Compared} compared, "
        + $"{flowCheck.Mismatches.Count} mismatches");

    ScorecardColumn<
        PreparedMethodBodies,
        ClassifierQuerySummary>[] classifierColumns =
            ClassifierQueryPrototype.Columns();
    ScorecardCheck classifierCheck = Scorecard.Check(
        assets,
        classifierColumns[0],
        classifierColumns,
        ClassifierQueryPrototype.RowText,
        closings: ClassifierQueryPrototype.Closings);
    foreach (ScorecardAsset<PreparedMethodBodies> asset in assets)
    {
        ClassifierQuerySummary summary = AssertSummary(
            classifierColumns[0].Answer(
                ScorecardClosing.Rows,
                asset.Asset),
            "classifier query");
        Console.WriteLine(
            $"classifier-query-answer\t{asset.Name}\t{summary}");
    }
    Console.WriteLine(
        $"# classifier query: {classifierCheck.Compared} compared, "
        + $"{classifierCheck.Mismatches.Count} mismatches");

    if (!exactAgreement
        || !check.Agrees
        || !stableCheck.Agrees
        || !flowCheck.Agrees
        || !classifierCheck.Agrees)
    {
        return 1;
    }
    if (options.Command == ScorecardCommand.Check)
        return 0;

    IReadOnlyList<ScorecardCell> cells = Scorecard.Measure(
        assets,
        columns,
        options.Timing,
        progress => Console.Error.WriteLine(progress),
        MethodBodyDemandPrototype.Closings);
    Console.WriteLine("# Single-classifier query: throw presence");
    Console.Write(Scorecard.Report(cells, oracle.Name, shape));

    IReadOnlyList<ScorecardCell> stableCells = Scorecard.Measure(
        assets,
        stableColumns,
        options.Timing,
        progress => Console.Error.WriteLine(
            $"stable getter: {progress}"),
        StableGetterPrototype.Closings);
    Console.WriteLine("# Stable getter");
    Console.Write(
        Scorecard.Report(
            stableCells,
            stableColumns[0].Name,
            shape));

    IReadOnlyList<ScorecardCell> flowCells = Scorecard.Measure(
        assets,
        flowColumns,
        options.Timing,
        progress => Console.Error.WriteLine(
            $"flow probe: {progress}"),
        FlowProbePrototype.Closings);
    Console.WriteLine("# Flow probe");
    Console.Write(
        Scorecard.Report(
            flowCells,
            flowColumns[0].Name,
            shape));

    IReadOnlyList<ScorecardCell> classifierCells =
        Scorecard.Measure(
            assets,
            classifierColumns,
            options.Timing,
            progress => Console.Error.WriteLine(
                $"classifier query: {progress}"),
            ClassifierQueryPrototype.Closings);
    Console.WriteLine(
        "# Multi-classifier query: throw, call, allocation, "
        + "stable getter, and bounded flow");
    Console.Write(
        Scorecard.Report(
            classifierCells,
            classifierColumns[0].Name,
            shape));
    if (options.TsvPath is { } tsvPath)
    {
        using StreamWriter tsv = File.CreateText(tsvPath);
        Scorecard.WriteTsv(
            [
                .. PrefixColumns("single-throw", cells),
                .. PrefixColumns("stable-getter", stableCells),
                .. PrefixColumns("flow-probe", flowCells),
                .. PrefixColumns("mixed-classifiers", classifierCells),
            ],
            tsv);
    }
    return 0;
}
finally
{
    foreach (ScorecardAsset<PreparedMethodBodies> asset in assets)
        asset.Asset.Dispose();
}

static T AssertSummary<T>(
    ScorecardAnswer<T> answer,
    string scenario = "method-body demand")
{
    if (answer.Rows is not { Count: 1 } rows)
    {
        throw new InvalidOperationException(
            $"{scenario} answer must contain one summary.");
    }

    return rows[0];
}

static void WritePlan(
    string name,
    MethodBodyAnalyzerPlan plan) =>
    Console.WriteLine(
        $"plan\t{name}\tanalyzers="
        + string.Join(",", plan.Analyzers.Select(
            static analyzer => analyzer.Identity))
        + $"\taccess={plan.Demand.Access}"
        + $"\tdetail={plan.Demand.Detail}"
        + $"\tsource={plan.Source}");

static IEnumerable<ScorecardCell> PrefixColumns(
    string scenario,
    IReadOnlyList<ScorecardCell> cells)
{
    foreach (ScorecardCell cell in cells)
        yield return cell with { Column = $"{scenario}/{cell.Column}" };
}
