using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Queries;
using NLinq;

namespace DotnetInspector.PerformanceOracles.Tests;

/// <summary>
/// Pins the method-definition source to System.Linq over real metadata, and
/// the scorecard's answer check and summary to their definitions.
/// </summary>
public sealed class ScorecardTests
{
    [Fact]
    public void MethodDefinitionRows_YieldsEveryMethodTypeByType()
    {
        using var pe = new PEReader(File.OpenRead(typeof(ScorecardTests).Assembly.Location));
        MetadataReader reader = pe.GetMetadataReader();
        int[] expected = [.. reader.TypeDefinitions
            .SelectMany(t => reader.GetTypeDefinition(t).GetMethods())
            .Select(m => System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(m))];

        var pulled = new List<int>();
        var rows = new MethodDefinitionRows(reader);
        while (true)
        {
            MethodDefinitionRow row = rows.TryGetNext(out bool hasMore);
            if (!hasMore)
                break;
            pulled.Add(row.Token);
        }

        Assert.NotEmpty(expected);
        Assert.Equal(expected, pulled);
        Assert.Equal(expected.Length, new MethodDefinitionRows(reader).CountFold<MethodDefinitionRows, MethodDefinitionRow>());
    }

    [Fact]
    public void MethodDefinitionRows_FoldContinuesAfterAPartialPull()
    {
        using var pe = new PEReader(File.OpenRead(typeof(ScorecardTests).Assembly.Location));
        MetadataReader reader = pe.GetMetadataReader();
        int total = new MethodDefinitionRows(reader).CountFold<MethodDefinitionRows, MethodDefinitionRow>();

        var rows = new MethodDefinitionRows(reader);
        for (int i = 0; i < 3; i++)
            rows.TryGetNext(out _);

        Assert.Equal(total - 3, rows.CountFold<MethodDefinitionRows, MethodDefinitionRow>());
    }

    [Fact]
    public void PublicMethods_NLinqAndLinqColumnsAgreeOnRealAssemblies()
    {
        // System.Reflection.Metadata has far more than 110 selected methods, so
        // its window succeeds; this test assembly has fewer, so its window
        // must fail the same way in both columns.
        var shape = new ScorecardShape();
        using var large = new PEReader(File.OpenRead(typeof(MetadataReader).Assembly.Location));
        using var small = new PEReader(File.OpenRead(typeof(ScorecardTests).Assembly.Location));
        ScorecardAsset<PEReader>[] assets = [new("System.Reflection.Metadata", large), new("tests", small)];
        ScorecardColumn<PEReader, MethodTextRow> oracle = PublicMethods.NLinqColumn(shape);

        ScorecardCheck check = Scorecard.Check(assets, oracle, [oracle, PublicMethods.LinqColumn(shape)], PublicMethods.RowText);
        Assert.Empty(check.Mismatches);
        Assert.Equal(["tests"], check.WindowFailures);
        Assert.Equal(11, oracle.Answer(ScorecardClosing.Window, large).Rows!.Count);
        Assert.True(oracle.Answer(ScorecardClosing.Window, small).WindowFailed);
    }

    [Fact]
    public void ACustomSelection_GetsAgreeingNLinqAndLinqColumns()
    {
        // An enablement scores its own population by supplying a selection.
        var shape = new ScorecardShape(N: 3, WindowFirst: 2, WindowLast: 4);
        using var pe = new PEReader(File.OpenRead(typeof(MetadataReader).Assembly.Location));
        ScorecardAsset<PEReader>[] assets = [new("System.Reflection.Metadata", pe)];
        ScorecardColumn<PEReader, MethodTextRow> oracle = MethodPopulation<StaticMethodSelection>.NLinqColumn(shape);

        ScorecardCheck check = Scorecard.Check(
            assets,
            oracle,
            [oracle, MethodPopulation<StaticMethodSelection>.LinqColumn(shape)],
            MethodPopulation.RowText);

        Assert.True(check.Agrees);
        int publicCount = PublicMethods.NLinqColumn(shape).Answer(ScorecardClosing.Count, pe).Count!.Value;
        int staticCount = oracle.Answer(ScorecardClosing.Count, pe).Count!.Value;
        Assert.InRange(staticCount, 1, publicCount - 1);
    }

    [Theory]
    [InlineData("JsonSer")]
    [InlineData("System.Text.Json.JsonSer")]
    [InlineData("Serializer")]
    [InlineData("JsonSerialiser")]
    [InlineData("JsonSerialiser<T>")]
    [InlineData("Json*")]
    [InlineData("NoSuchTypePattern")]
    public void TypeFindPopulationColumns_AgreeOnRealTypePopulation(string pattern)
    {
        var shape = new ScorecardShape(N: 3, WindowFirst: 2, WindowLast: 4);
        IReadOnlyList<ScorecardAsset<TypeFindPopulationScorecardAsset>> assets =
            TypeFindPopulationScorecard.LoadAssets(
                pattern,
                [typeof(System.Text.Json.JsonSerializer).Assembly.Location]);
        ScorecardColumn<TypeFindPopulationScorecardAsset, TypeFindPopulationScorecardRow> oracle =
            TypeFindPopulationScorecard.NLinqColumn(shape);

        ScorecardCheck check = Scorecard.Check(
            assets,
            oracle,
            [
                TypeFindPopulationScorecard.LinqColumn(shape),
                oracle,
                TypeFindPopulationScorecard.SelectorColumn(shape),
            ],
            TypeFindPopulationScorecard.RowText);

        Assert.True(check.Agrees, string.Join(Environment.NewLine, check.Mismatches));
    }

    [Fact]
    public void TypeFindPopulationColumns_PreserveFirstAssociationForDuplicateNames()
    {
        TypeFindPopulationCandidate<int>[] candidates =
        [
            new(41, "Example.JsonSerializer"),
            new(99, "Example.JsonSerializer"),
            new(7, "Example.JsonSerializerContext"),
        ];
        var shape = new ScorecardShape(N: 1, WindowFirst: 1, WindowLast: 1);
        ScorecardAsset<TypeFindPopulationScorecardAsset>[] assets =
        [
            new("duplicates", new("JsonSer", candidates)),
        ];
        ScorecardColumn<TypeFindPopulationScorecardAsset, TypeFindPopulationScorecardRow> oracle =
            TypeFindPopulationScorecard.NLinqColumn(shape);

        ScorecardCheck check = Scorecard.Check(
            assets,
            oracle,
            [
                TypeFindPopulationScorecard.LinqColumn(shape),
                oracle,
                TypeFindPopulationScorecard.SelectorColumn(shape),
            ],
            TypeFindPopulationScorecard.RowText);

        Assert.True(check.Agrees, string.Join(Environment.NewLine, check.Mismatches));
        ScorecardAnswer<TypeFindPopulationScorecardRow> rows =
            oracle.Answer(ScorecardClosing.Rows, assets[0].Asset);
        Assert.Equal([41, 7], rows.Rows!.Select(static row => row.Association));
    }

    struct StaticMethodSelection : IMethodSelection
    {
        public readonly bool IsSelected(MetadataReader reader, TypeDefinition type, MethodDefinition method) =>
            default(PublicMethodSelection).IsSelected(reader, type, method)
            && (method.Attributes & System.Reflection.MethodAttributes.Static) != 0;
    }

    [Fact]
    public void AssetsSharingAFileName_KeepDistinctIdentitiesThroughCheckAndTime()
    {
        // The same assembly copied into two directories: equal file names,
        // distinct assets. Neither check nor time may merge or collide them.
        string root = Path.Combine(Path.GetTempPath(), "scorecard-" + Guid.NewGuid().ToString("N"));
        string source = typeof(ScorecardTests).Assembly.Location;
        string first = Path.Combine(root, "first", "Methods.dll");
        string second = Path.Combine(root, "second", "Methods.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        File.Copy(source, first);
        File.Copy(source, second);
        IReadOnlyList<ScorecardAsset<PEReader>> assets = PublicMethods.LoadAssets([first, second]);
        try
        {
            Assert.Equal(["first/Methods", "second/Methods"], assets.Select(a => a.Name));

            var shape = new ScorecardShape();
            ScorecardColumn<PEReader, MethodTextRow> oracle = PublicMethods.NLinqColumn(shape);
            ScorecardColumn<PEReader, MethodTextRow>[] columns = [PublicMethods.LinqColumn(shape), oracle];

            ScorecardCheck check = Scorecard.Check(assets, oracle, columns, PublicMethods.RowText);
            Assert.True(check.Agrees);
            Assert.Equal(assets.Count * Scorecard.Closings.Count, check.Compared);
            Assert.Equal(["first/Methods", "second/Methods"], check.WindowFailures);

            IReadOnlyList<ScorecardCell> cells = Scorecard.Measure(
                assets,
                columns,
                new ScorecardTiming(Rounds: 1, Warmup: 0, BudgetMilliseconds: 1, MinSamples: 1, MaxSamples: 1));
            Assert.Equal(2 * Scorecard.Closings.Count * columns.Length, cells.Count);
            Assert.Equal([0, 1], cells.Select(c => c.AssetIndex).Distinct());

            ScorecardSummary count = Scorecard.Summarize(cells, oracle.Name).Single(s => s.Closing == ScorecardClosing.Count && s.Column == "LINQ");
            Assert.Equal(2, count.Assets);
            string report = Scorecard.Report(cells, oracle.Name, shape);
            Assert.Contains("| first/Methods | Count |", report);
            Assert.Contains("| second/Methods | Count |", report);
        }
        finally
        {
            foreach (ScorecardAsset<PEReader> asset in assets)
                asset.Asset.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(new[] { "/x/a.dll", "/y/b.dll" }, new[] { "a", "b" })]
    [InlineData(new[] { "/x/lib/a.dll", "/y/lib/a.dll" }, new[] { "x/lib/a", "y/lib/a" })]
    [InlineData(new[] { "/x/a.dll", "/x/a.exe" }, new[] { "a #1", "a #2" })]
    [InlineData(new[] { "/x/a.dll", "/x/a.dll" }, new[] { "a #1", "a #2" })]
    public void AssetNames_AreDistinctAndAsShortAsPossible(string[] paths, string[] expected)
    {
        Assert.Equal(expected, ScorecardAssetNames.FromPaths(paths));
    }

    [Fact]
    public void Check_ReportsOnlyColumnsThatDisagreeWithTheOracle()
    {
        ScorecardAsset<int[]>[] assets = [new("small", [1, 2, 3]), new("empty", [])];
        var oracle = new ScorecardColumn<int[], int>("NLinq", Answer);
        var agrees = new ScorecardColumn<int[], int>("Planner", Answer);
        var truncates = new ScorecardColumn<int[], int>(
            "Truncating",
            (closing, input) => closing == ScorecardClosing.Window
                ? ScorecardAnswer<int>.OfRows([.. input.Skip(1).Take(2)])
                : Answer(closing, input));

        IReadOnlyList<ScorecardMismatch> mismatches =
            Scorecard.Check(assets, oracle, [oracle, agrees, truncates], Text).Mismatches;

        // A truncated window is not the oracle's strict failure.
        Assert.Equal(
            [
                new ScorecardMismatch("small", ScorecardClosing.Window, "Truncating", Scorecard.Describe(ScorecardAnswer<int>.OfRows([2, 3]), Text), "fail"),
                new ScorecardMismatch("empty", ScorecardClosing.Window, "Truncating", Scorecard.Describe(ScorecardAnswer<int>.OfRows([]), Text), "fail"),
            ],
            mismatches);
    }

    [Fact]
    public void Check_ComparesRowsNotTheirDisplayHash()
    {
        // Two different one-row answers that collide under the 32-bit FNV-1a
        // display hash: the check must still report the mismatch.
        IReadOnlyList<int> left = [40189];
        IReadOnlyList<int> right = [797186];
        Assert.Equal(
            Scorecard.Describe(ScorecardAnswer<int>.OfRows(left), Text),
            Scorecard.Describe(ScorecardAnswer<int>.OfRows(right), Text));

        ScorecardAsset<int[]>[] assets = [new("collision", [1])];
        var oracle = new ScorecardColumn<int[], int>("NLinq", (closing, _) => ScorecardAnswer<int>.OfRows(left));
        var other = new ScorecardColumn<int[], int>("Planner", (closing, _) => ScorecardAnswer<int>.OfRows(right));

        ScorecardCheck check = Scorecard.Check(assets, oracle, [oracle, other], Text);

        Assert.Equal(Scorecard.Closings.Count, check.Mismatches.Count);
        Assert.False(check.Agrees);
    }

    [Fact]
    public void Check_RecordsStrictWindowFailuresAndRequiresEveryColumnToFail()
    {
        ScorecardAsset<int[]>[] assets = [new("short", [1, 2]), new("long", [1, 2, 3, 4, 5])];
        var oracle = new ScorecardColumn<int[], int>("NLinq", Answer);
        var agrees = new ScorecardColumn<int[], int>("Planner", Answer);

        ScorecardCheck agreeing = Scorecard.Check(assets, oracle, [oracle, agrees], Text);
        Assert.True(agreeing.Agrees);
        Assert.Equal(["short"], agreeing.WindowFailures);

        // A column that succeeds where the oracle's window fails is a mismatch.
        var succeeds = new ScorecardColumn<int[], int>(
            "Lenient",
            (closing, input) => closing == ScorecardClosing.Window && input.Length < Shape.WindowLast
                ? ScorecardAnswer<int>.OfRows([.. input.Skip(Shape.WindowSkip)])
                : Answer(closing, input));
        ScorecardCheck disagreeing = Scorecard.Check(assets, oracle, [oracle, succeeds], Text);
        ScorecardMismatch mismatch = Assert.Single(disagreeing.Mismatches);
        Assert.Equal(("short", ScorecardClosing.Window, "fail"), (mismatch.Asset, mismatch.Closing, mismatch.OracleAnswer));
    }

    [Fact]
    public void Measure_NeverTimesAFailedStrictWindow_AndSummaryExcludesIt()
    {
        ScorecardAsset<int[]>[] assets = [new("short", [1, 2]), new("long", [1, 2, 3, 4, 5])];
        ScorecardColumn<int[], int>[] columns = [new("NLinq", Answer), new("Planner", Answer)];

        IReadOnlyList<ScorecardCell> cells = Scorecard.Measure(
            assets,
            columns,
            new ScorecardTiming(Rounds: 2, Warmup: 1, BudgetMilliseconds: 1, MinSamples: 2, MaxSamples: 4));

        ScorecardCell failed = cells.Single(c => c.Asset == "short" && c.Closing == ScorecardClosing.Window && c.Column == "Planner");
        Assert.True(failed.WindowFailed);
        Assert.Empty(failed.RoundMedians);
        Assert.Throws<InvalidOperationException>(() => failed.Median);

        ScorecardSummary window = Scorecard.Summarize(cells, "NLinq").Single(s => s.Closing == ScorecardClosing.Window && s.Column == "Planner");
        Assert.Equal((1, 1), (window.Assets, window.FailedExcluded));

        string report = Scorecard.Report(cells, "NLinq", Shape);
        Assert.Contains("| Rows(2..4) | 1 (1 failed, excluded) |", report);
        Assert.Contains("| short | Rows(2..4) | fail | fail |", report);
    }

    [Fact]
    public void Report_LabelsClosingsWithTheirParametersAndShowsAbsoluteMedians()
    {
        ScorecardCell[] cells =
        [
            .. Scorecard.Closings.SelectMany(closing => (ScorecardCell[])
            [
                new(0, "a", closing, "NLinq", [10]),
                new(0, "a", closing, "Planner", [5]),
            ]),
        ];

        string report = Scorecard.Report(cells, "NLinq", new ScorecardShape(N: 6, WindowFirst: 100, WindowLast: 110));

        Assert.Contains("| Head(6) | 1 | 1.00× | 0.50× (0.50–0.50) |", report);
        Assert.Contains("| Tail(6) |", report);
        Assert.Contains("| Rows(100..110) |", report);
        Assert.Contains("| a | Count | 10.0 | 5.0 |", report);
    }

    [Fact]
    public void Describe_DistinguishesEveryAnswerKind()
    {
        Assert.Equal("true", Scorecard.Describe(ScorecardAnswer<int>.OfExists(true), Text));
        Assert.Equal("c=3", Scorecard.Describe(ScorecardAnswer<int>.OfCount(3), Text));
        Assert.Equal("fail", Scorecard.Describe(ScorecardAnswer<int>.OfWindowFailure(), Text));
        Assert.StartsWith("n=2;h=", Scorecard.Describe(ScorecardAnswer<int>.OfRows([1, 2]), Text));
        Assert.NotEqual(
            Scorecard.Describe(ScorecardAnswer<int>.OfRows([1, 2]), Text),
            Scorecard.Describe(ScorecardAnswer<int>.OfRows([2, 1]), Text));
    }

    [Fact]
    public void Summarize_IsTheGeometricMeanOfRatiosToTheOracle()
    {
        ScorecardCell[] cells =
        [
            .. Scorecard.Closings.SelectMany(closing => (ScorecardCell[])
            [
                new(0, "a", closing, "NLinq", [10]),
                new(0, "a", closing, "Planner", [5]),
                new(1, "b", closing, "NLinq", [10]),
                new(1, "b", closing, "Planner", [20]),
            ]),
        ];

        var summary = Scorecard.Summarize(cells, "NLinq").Single(s => s.Closing == ScorecardClosing.Count && s.Column == "Planner");

        Assert.Equal(1.0, summary.GeometricMean, 9);
        Assert.Equal(0.5, summary.Min, 9);
        Assert.Equal(2.0, summary.Max, 9);
    }

    [Fact]
    public void Measure_TimesEveryCellOncePerRound()
    {
        ScorecardAsset<int[]>[] assets = [new("small", [1, 2, 3, 4])];
        ScorecardColumn<int[], int>[] columns = [new("NLinq", Answer), new("Planner", Answer)];

        IReadOnlyList<ScorecardCell> cells = Scorecard.Measure(
            assets,
            columns,
            new ScorecardTiming(Rounds: 3, Warmup: 1, BudgetMilliseconds: 1, MinSamples: 2, MaxSamples: 4));

        Assert.Equal(Scorecard.Closings.Count * columns.Length, cells.Count);
        Assert.All(cells, cell => Assert.Equal(3, cell.RoundMedians.Count));
    }

    static readonly ScorecardShape Shape = new(N: 2, WindowFirst: 2, WindowLast: 4);

    // A reference column over System.Linq: the answers every agreeing column must give.
    static ScorecardAnswer<int> Answer(ScorecardClosing closing, int[] input) => closing switch
    {
        ScorecardClosing.Exists => ScorecardAnswer<int>.OfExists(input.Length != 0),
        ScorecardClosing.Count => ScorecardAnswer<int>.OfCount(input.Length),
        ScorecardClosing.Head => ScorecardAnswer<int>.OfRows([.. input.Take(Shape.N)]),
        ScorecardClosing.Tail => ScorecardAnswer<int>.OfRows([.. input.TakeLast(Shape.N)]),
        ScorecardClosing.Rows => ScorecardAnswer<int>.OfRows(input),
        ScorecardClosing.Window => input.Length >= Shape.WindowLast
            ? ScorecardAnswer<int>.OfRows([.. input.Skip(Shape.WindowSkip).Take(Shape.WindowTake)])
            : ScorecardAnswer<int>.OfWindowFailure(),
        _ => throw new ArgumentOutOfRangeException(nameof(closing)),
    };

    static string Text(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
