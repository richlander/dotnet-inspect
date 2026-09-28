using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.PortableExecutable;
using DotnetInspector.PerformanceOracles;
using Exp = ILInspector.Analysis.Planning.Experiments;
using Row = DotnetInspector.PerformanceOracles.MethodTextRow;

// The Producer Planning postcard through the NLinq oracle fixture: the
// fixture's public-methods population supplies the NLinq oracle and the
// System.Linq reference, and this consumer registers the Planner (After) and
// the Naive Planner (the same planner asked for Rows, then closed with LINQ)
// beside them.
//
//   fixture-postcard check <dll>...
//   fixture-postcard time [--rounds N] [--budget-ms N] [--tsv <path>] <dll>...
static class FixturePostcard
{
    public static int Run(string[] args)
    {
        var timing = new PostcardTiming();
        string? tsvPath = null;
        var paths = new List<string>();
        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--rounds":
                    timing = timing with { Rounds = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--budget-ms":
                    timing = timing with { BudgetMilliseconds = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--tsv":
                    tsvPath = args[++i];
                    break;
                default:
                    paths.Add(args[i]);
                    break;
            }
        }

        // The experiment's postcard sizes; the fixture's shape must match.
        var shape = new PostcardShape(Exp.Postcard.N, Exp.Postcard.WindowFirst, Exp.Postcard.WindowLast);
        var pathOf = new Dictionary<PEReader, string>(ReferenceEqualityComparer.Instance);
        var assets = new List<PostcardAsset<PEReader>>();
        foreach (string path in paths)
        {
            var pe = new PEReader(ImmutableArray.Create(File.ReadAllBytes(path)));
            pathOf[pe] = path;
            assets.Add(new(Path.GetFileNameWithoutExtension(path), pe));
        }

        PostcardColumn<PEReader, Row> oracle = PublicMethods.NLinqColumn(shape);
        PostcardColumn<PEReader, Row>[] columns =
        [
            new("Naive Planner", (closing, pe) => Convert(Exp.Postcard.NaivePlanner(Name(closing), pathOf[pe], pe))),
            PublicMethods.LinqColumn(shape),
            oracle,
            new("Planner", (closing, pe) => Convert(Exp.Postcard.After(Name(closing), pathOf[pe], pe))),
        ];

        IReadOnlyList<PostcardMismatch> mismatches = Postcard.Check(assets, oracle, columns, PublicMethods.RowText);
        foreach (PostcardMismatch mismatch in mismatches)
            Console.WriteLine($"mismatch\t{mismatch.Asset}\t{mismatch.Closing}\t{mismatch.Column}\t{mismatch.Answer}\toracle={mismatch.OracleAnswer}");
        Console.WriteLine($"# answers: {assets.Count * Postcard.Closings.Count * (columns.Length - 1)} compared, {mismatches.Count} mismatches");
        if (mismatches.Count != 0)
            return 1;
        if (args[1] == "check")
            return 0;

        IReadOnlyList<PostcardCell> cells = Postcard.Measure(assets, columns, timing, progress => Console.Error.WriteLine(progress));
        if (tsvPath is not null)
        {
            using StreamWriter tsv = File.CreateText(tsvPath);
            Postcard.WriteTsv(cells, tsv);
        }

        Console.Write(Postcard.SummaryTable(cells, oracle.Name));
        return 0;
    }

    static string Name(PostcardClosing closing) => closing switch
    {
        PostcardClosing.Exists => "exists",
        PostcardClosing.Count => "count",
        PostcardClosing.Head => "head",
        PostcardClosing.Tail => "tail",
        PostcardClosing.Rows => "rows",
        PostcardClosing.Window => "window",
        _ => throw new ArgumentOutOfRangeException(nameof(closing)),
    };

    // The experiment's rows are viewed, not copied, so conversion adds no
    // per-row work inside a timed call.
    static PostcardAnswer<Row> Convert(Exp.PostcardAnswer answer) =>
        answer.Failed ? PostcardAnswer<Row>.OfWindowFailure()
        : answer.Exists is bool exists ? PostcardAnswer<Row>.OfExists(exists)
        : answer.Count is int count ? PostcardAnswer<Row>.OfCount(count)
        : PostcardAnswer<Row>.OfRows(new RowView(answer.Rows!));

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
