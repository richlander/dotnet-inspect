using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NLinq;

namespace DotnetInspector.PerformanceOracles.Tests;

/// <summary>
/// Pins the method-definition source to System.Linq over real metadata, and
/// the postcard's answer check and summary to their definitions.
/// </summary>
public sealed class PostcardTests
{
    [Fact]
    public void MethodDefinitionRows_YieldsEveryMethodTypeByType()
    {
        using var pe = new PEReader(File.OpenRead(typeof(PostcardTests).Assembly.Location));
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
        using var pe = new PEReader(File.OpenRead(typeof(PostcardTests).Assembly.Location));
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
        var shape = new PostcardShape();
        using var large = new PEReader(File.OpenRead(typeof(MetadataReader).Assembly.Location));
        using var small = new PEReader(File.OpenRead(typeof(PostcardTests).Assembly.Location));
        PostcardAsset<PEReader>[] assets = [new("System.Reflection.Metadata", large), new("tests", small)];
        PostcardColumn<PEReader, MethodTextRow> oracle = PublicMethods.NLinqColumn(shape);

        PostcardCheck check = Postcard.Check(assets, oracle, [oracle, PublicMethods.LinqColumn(shape)], PublicMethods.RowText);
        Assert.Empty(check.Mismatches);
        Assert.Equal(["tests"], check.WindowFailures);
        Assert.Equal(11, oracle.Answer(PostcardClosing.Window, large).Rows!.Count);
        Assert.True(oracle.Answer(PostcardClosing.Window, small).WindowFailed);
    }

    [Fact]
    public void Check_ReportsOnlyColumnsThatDisagreeWithTheOracle()
    {
        PostcardAsset<int[]>[] assets = [new("small", [1, 2, 3]), new("empty", [])];
        var oracle = new PostcardColumn<int[], int>("NLinq", Answer);
        var agrees = new PostcardColumn<int[], int>("After", Answer);
        var truncates = new PostcardColumn<int[], int>(
            "Truncating",
            (closing, input) => closing == PostcardClosing.Window
                ? PostcardAnswer<int>.OfRows([.. input.Skip(1).Take(2)])
                : Answer(closing, input));

        IReadOnlyList<PostcardMismatch> mismatches =
            Postcard.Check(assets, oracle, [oracle, agrees, truncates], Text).Mismatches;

        // A truncated window is not the oracle's strict failure.
        Assert.Equal(
            [
                new PostcardMismatch("small", PostcardClosing.Window, "Truncating", Postcard.Describe(PostcardAnswer<int>.OfRows([2, 3]), Text), "fail"),
                new PostcardMismatch("empty", PostcardClosing.Window, "Truncating", Postcard.Describe(PostcardAnswer<int>.OfRows([]), Text), "fail"),
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
            Postcard.Describe(PostcardAnswer<int>.OfRows(left), Text),
            Postcard.Describe(PostcardAnswer<int>.OfRows(right), Text));

        PostcardAsset<int[]>[] assets = [new("collision", [1])];
        var oracle = new PostcardColumn<int[], int>("NLinq", (closing, _) => PostcardAnswer<int>.OfRows(left));
        var other = new PostcardColumn<int[], int>("After", (closing, _) => PostcardAnswer<int>.OfRows(right));

        PostcardCheck check = Postcard.Check(assets, oracle, [oracle, other], Text);

        Assert.Equal(Postcard.Closings.Count, check.Mismatches.Count);
        Assert.False(check.Agrees);
    }

    [Fact]
    public void Check_RecordsStrictWindowFailuresAndRequiresEveryColumnToFail()
    {
        PostcardAsset<int[]>[] assets = [new("short", [1, 2]), new("long", [1, 2, 3, 4, 5])];
        var oracle = new PostcardColumn<int[], int>("NLinq", Answer);
        var agrees = new PostcardColumn<int[], int>("After", Answer);

        PostcardCheck agreeing = Postcard.Check(assets, oracle, [oracle, agrees], Text);
        Assert.True(agreeing.Agrees);
        Assert.Equal(["short"], agreeing.WindowFailures);

        // A column that succeeds where the oracle's window fails is a mismatch.
        var succeeds = new PostcardColumn<int[], int>(
            "Lenient",
            (closing, input) => closing == PostcardClosing.Window && input.Length < Shape.WindowLast
                ? PostcardAnswer<int>.OfRows([.. input.Skip(Shape.WindowSkip)])
                : Answer(closing, input));
        PostcardCheck disagreeing = Postcard.Check(assets, oracle, [oracle, succeeds], Text);
        PostcardMismatch mismatch = Assert.Single(disagreeing.Mismatches);
        Assert.Equal(("short", PostcardClosing.Window, "fail"), (mismatch.Asset, mismatch.Closing, mismatch.OracleAnswer));
    }

    [Fact]
    public void Measure_NeverTimesAFailedStrictWindow_AndSummaryExcludesIt()
    {
        PostcardAsset<int[]>[] assets = [new("short", [1, 2]), new("long", [1, 2, 3, 4, 5])];
        PostcardColumn<int[], int>[] columns = [new("NLinq", Answer), new("After", Answer)];

        IReadOnlyList<PostcardCell> cells = Postcard.Measure(
            assets,
            columns,
            new PostcardTiming(Rounds: 2, Warmup: 1, BudgetMilliseconds: 1, MinSamples: 2, MaxSamples: 4));

        PostcardCell failed = cells.Single(c => c.Asset == "short" && c.Closing == PostcardClosing.Window && c.Column == "After");
        Assert.True(failed.WindowFailed);
        Assert.Empty(failed.RoundMedians);
        Assert.Throws<InvalidOperationException>(() => failed.Median);

        PostcardSummary window = Postcard.Summarize(cells, "NLinq").Single(s => s.Closing == PostcardClosing.Window && s.Column == "After");
        Assert.Equal((1, 1), (window.Assets, window.FailedExcluded));

        string report = Postcard.Report(cells, "NLinq", Shape);
        Assert.Contains("| Rows(2..4) | 1 (1 failed, excluded) |", report);
        Assert.Contains("| short | Rows(2..4) | fail | fail |", report);
    }

    [Fact]
    public void Report_LabelsClosingsWithTheirParametersAndShowsAbsoluteMedians()
    {
        PostcardCell[] cells =
        [
            .. Postcard.Closings.SelectMany(closing => (PostcardCell[])
            [
                new("a", closing, "NLinq", [10]),
                new("a", closing, "After", [5]),
            ]),
        ];

        string report = Postcard.Report(cells, "NLinq", new PostcardShape(N: 6, WindowFirst: 100, WindowLast: 110));

        Assert.Contains("| Head(6) | 1 | 1.00× | 0.50× (0.50–0.50) |", report);
        Assert.Contains("| Tail(6) |", report);
        Assert.Contains("| Rows(100..110) |", report);
        Assert.Contains("| a | Count | 10.0 | 5.0 |", report);
    }

    [Fact]
    public void Describe_DistinguishesEveryAnswerKind()
    {
        Assert.Equal("true", Postcard.Describe(PostcardAnswer<int>.OfExists(true), Text));
        Assert.Equal("c=3", Postcard.Describe(PostcardAnswer<int>.OfCount(3), Text));
        Assert.Equal("fail", Postcard.Describe(PostcardAnswer<int>.OfWindowFailure(), Text));
        Assert.StartsWith("n=2;h=", Postcard.Describe(PostcardAnswer<int>.OfRows([1, 2]), Text));
        Assert.NotEqual(
            Postcard.Describe(PostcardAnswer<int>.OfRows([1, 2]), Text),
            Postcard.Describe(PostcardAnswer<int>.OfRows([2, 1]), Text));
    }

    [Fact]
    public void Summarize_IsTheGeometricMeanOfRatiosToTheOracle()
    {
        PostcardCell[] cells =
        [
            .. Postcard.Closings.SelectMany(closing => (PostcardCell[])
            [
                new("a", closing, "NLinq", [10]),
                new("a", closing, "After", [5]),
                new("b", closing, "NLinq", [10]),
                new("b", closing, "After", [20]),
            ]),
        ];

        var summary = Postcard.Summarize(cells, "NLinq").Single(s => s.Closing == PostcardClosing.Count && s.Column == "After");

        Assert.Equal(1.0, summary.GeometricMean, 9);
        Assert.Equal(0.5, summary.Min, 9);
        Assert.Equal(2.0, summary.Max, 9);
    }

    [Fact]
    public void Measure_TimesEveryCellOncePerRound()
    {
        PostcardAsset<int[]>[] assets = [new("small", [1, 2, 3, 4])];
        PostcardColumn<int[], int>[] columns = [new("NLinq", Answer), new("After", Answer)];

        IReadOnlyList<PostcardCell> cells = Postcard.Measure(
            assets,
            columns,
            new PostcardTiming(Rounds: 3, Warmup: 1, BudgetMilliseconds: 1, MinSamples: 2, MaxSamples: 4));

        Assert.Equal(Postcard.Closings.Count * columns.Length, cells.Count);
        Assert.All(cells, cell => Assert.Equal(3, cell.RoundMedians.Count));
    }

    static readonly PostcardShape Shape = new(N: 2, WindowFirst: 2, WindowLast: 4);

    // A reference column over System.Linq: the answers every agreeing column must give.
    static PostcardAnswer<int> Answer(PostcardClosing closing, int[] input) => closing switch
    {
        PostcardClosing.Exists => PostcardAnswer<int>.OfExists(input.Length != 0),
        PostcardClosing.Count => PostcardAnswer<int>.OfCount(input.Length),
        PostcardClosing.Head => PostcardAnswer<int>.OfRows([.. input.Take(Shape.N)]),
        PostcardClosing.Tail => PostcardAnswer<int>.OfRows([.. input.TakeLast(Shape.N)]),
        PostcardClosing.Rows => PostcardAnswer<int>.OfRows(input),
        PostcardClosing.Window => input.Length >= Shape.WindowLast
            ? PostcardAnswer<int>.OfRows([.. input.Skip(Shape.WindowSkip).Take(Shape.WindowTake)])
            : PostcardAnswer<int>.OfWindowFailure(),
        _ => throw new ArgumentOutOfRangeException(nameof(closing)),
    };

    static string Text(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
