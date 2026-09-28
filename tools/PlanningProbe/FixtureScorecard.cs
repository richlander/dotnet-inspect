using System.Collections;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.PerformanceOracles;
using Exp = ILInspector.Analysis.Planning.Experiments;
using Row = DotnetInspector.PerformanceOracles.MethodTextRow;

// The Producer Planning performance scorecard through the NLinq oracle
// fixture. The fixture supplies the LINQ and NLinq columns for a method
// selection; this consumer registers Old and Planner beside them.
//
//   fixture-scorecard async  check|time [--rounds N] [--budget-ms N] [--tsv <path>] <dll>...
//     Async methods: Old (the legacy MethodClassificationScanner rows, then
//     LINQ), LINQ, NLinq, and Planner. The legacy code answers this question,
//     so all four columns exist. Legacy rows spell the declaring type and
//     signature differently, so rows are compared by method name, in order.
//   fixture-scorecard public check|time ... <dll>...
//     Public methods: LINQ, NLinq, and Planner. No legacy path answers this
//     question, so it has no Old column.
static class FixtureScorecard
{
    public static int Run(string[] args)
    {
        if (args.Length < 2 || args[1] is not ("async" or "public")
            || !ScorecardCommandLine.TryParse(args[2..], out ScorecardOptions? options, out string? error))
        {
            Console.Error.WriteLine("usage: fixture-scorecard async|public " + ScorecardCommandLine.Usage["usage: ".Length..]);
            return 2;
        }

        bool isAsync = args[1] == "async";
        var shape = new ScorecardShape(Exp.Postcard.N, Exp.Postcard.WindowFirst, Exp.Postcard.WindowLast);
        IReadOnlyList<ScorecardAsset<PEReader>> assets = MethodPopulation.LoadAssets(options!.Assets);
        var pathOf = new Dictionary<PEReader, string>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < assets.Count; i++)
            pathOf[assets[i].Asset] = options.Assets[i];

        ScorecardColumn<PEReader, Row> oracle;
        ScorecardColumn<PEReader, Row>[] columns;
        IEqualityComparer<Row>? rows = null;
        if (isAsync)
        {
            oracle = MethodPopulation<AsyncMethodSelection>.NLinqColumn(shape);
            columns =
            [
                new("Old", (closing, pe) => Convert(Exp.Postcard.AsyncOld(Name(closing), pe))),
                MethodPopulation<AsyncMethodSelection>.LinqColumn(shape),
                oracle,
                new("Planner", (closing, pe) => Convert(Exp.Postcard.AsyncPlanner(Name(closing), pathOf[pe], pe))),
            ];
            rows = ByMethodName.Instance;
        }
        else
        {
            oracle = PublicMethods.NLinqColumn(shape);
            columns =
            [
                PublicMethods.LinqColumn(shape),
                oracle,
                new("Planner", (closing, pe) => Convert(Exp.Postcard.After(Name(closing), pathOf[pe], pe))),
            ];
        }

        try
        {
            ScorecardCheck check = Scorecard.Check(assets, oracle, columns, MethodPopulation.RowText, rows);
            foreach (ScorecardMismatch mismatch in check.Mismatches)
                Console.WriteLine($"mismatch\t{mismatch.Asset}\t{shape.Label(mismatch.Closing)}\t{mismatch.Column}\t{mismatch.Answer}\toracle={mismatch.OracleAnswer}");
            Console.WriteLine($"# answers: {check.Compared} compared, {check.Mismatches.Count} mismatches, {check.WindowFailures.Count} strict-window failures");
            if (!check.Agrees)
                return 1;
            foreach (string asset in check.WindowFailures)
                Console.WriteLine($"window-failed\t{asset}\t{shape.Label(ScorecardClosing.Window)}\tevery column fails: the window does not exist");
            if (options.Command == ScorecardCommand.Check)
                return 0;

            IReadOnlyList<ScorecardCell> cells = Scorecard.Measure(assets, columns, options.Timing, progress => Console.Error.WriteLine(progress));
            if (options.TsvPath is { } tsvPath)
            {
                using StreamWriter tsv = File.CreateText(tsvPath);
                Scorecard.WriteTsv(cells, tsv);
            }

            Console.Write(Scorecard.Report(cells, oracle.Name, shape));
            return 0;
        }
        finally
        {
            foreach (ScorecardAsset<PEReader> asset in assets)
                asset.Asset.Dispose();
        }
    }

    struct AsyncMethodSelection : IMethodSelection
    {
        public readonly bool IsSelected(MetadataReader reader, TypeDefinition type, MethodDefinition method) =>
            Exp.AsyncMethodScope.IsCounted(reader, type, method);
    }

    sealed class ByMethodName : IEqualityComparer<Row>
    {
        public static ByMethodName Instance { get; } = new();

        public bool Equals(Row x, Row y) => x.Name == y.Name;

        public int GetHashCode(Row row) => row.Name.GetHashCode(StringComparison.Ordinal);
    }

    static string Name(ScorecardClosing closing) => closing switch
    {
        ScorecardClosing.Exists => "exists",
        ScorecardClosing.Count => "count",
        ScorecardClosing.Head => "head",
        ScorecardClosing.Tail => "tail",
        ScorecardClosing.Rows => "rows",
        ScorecardClosing.Window => "window",
        _ => throw new ArgumentOutOfRangeException(nameof(closing)),
    };

    // The experiment's rows are viewed, not copied, so conversion adds no
    // per-row work inside a timed call.
    static ScorecardAnswer<Row> Convert(Exp.PostcardAnswer answer) =>
        answer.Failed ? ScorecardAnswer<Row>.OfWindowFailure()
        : answer.Exists is bool exists ? ScorecardAnswer<Row>.OfExists(exists)
        : answer.Count is int count ? ScorecardAnswer<Row>.OfCount(count)
        : ScorecardAnswer<Row>.OfRows(new RowView(answer.Rows!));

    sealed class RowView(IReadOnlyList<Exp.MethodTextRow> rows) : IReadOnlyList<Row>
    {
        public Row this[int index] => Map(rows[index]);

        public int Count => rows.Count;

        public IEnumerator<Row> GetEnumerator()
        {
            foreach (Exp.MethodTextRow row in rows)
                yield return Map(row);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        static Row Map(Exp.MethodTextRow row) => new(row.Name, row.DeclaringType, row.Signature);
    }
}
