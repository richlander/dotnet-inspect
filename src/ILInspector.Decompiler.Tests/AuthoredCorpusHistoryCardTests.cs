using System.Text.Json;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Corpus")]
public class AuthoredCorpusHistoryCardTests
{
    const string SampleHistory = """
        {"date":"2026-07-20","commit":null,"poolMatched":26,"poolTotal":26,"evaluated":12000,"validPct":56.6,"correct":1501,"validDifferent":{"total":5290,"frontierIlExact":3097,"frontierIlDiff":2181},"invalid":5209,"invalidBreakdown":null,"unsupported":0,"drift":0,"inputsComplete":true,"sweepManifestSha256":null}
        {"date":"2026-07-24","commit":"16c0687f","poolMatched":26,"poolTotal":26,"evaluated":12000,"validPct":56.2,"correct":1539,"validDifferent":{"total":5202,"frontierIlExact":3055,"frontierIlDiff":2137,"lowering":6,"knownTaste":4,"frontierIlNoVerdict":0},"invalid":5259,"invalidBreakdown":{"productBodyDefect":306,"harnessShellReconstruction":4826,"unclassified":127},"unsupported":0,"drift":0,"inputsComplete":true,"sweepManifestSha256":"0a7eded85c3e1410","notFull":0,"unknownOutcome":0}
        {"date":"2026-07-30","commit":"deadbeef","poolMatched":26,"poolTotal":26,"evaluated":12000,"validPct":57.4,"correct":1600,"validDifferent":{"total":5100,"frontierIlExact":3000,"frontierIlDiff":2100,"lowering":0,"knownTaste":0,"frontierIlNoVerdict":0},"invalid":5180,"invalidBreakdown":{"productBodyDefect":250,"harnessShellReconstruction":4810,"unclassified":120},"unsupported":0,"drift":0,"inputsComplete":true,"sweepManifestSha256":"abc123","notFull":0,"unknownOutcome":0}
        """;

    /// <summary>
    /// Rows recorded before the run JSON carried the full partition, whose missing
    /// sub-buckets are not recoverable from any retained artifact. Every other row
    /// must serialize the complete partition. Keyed by date because these rows
    /// predate the commit field.
    /// </summary>
    static readonly string[] GrandfatheredIncompleteRows = ["2026-07-20"];

    static IReadOnlyList<HistoryRun> Parse()
        => AuthoredCorpusHistoryCard.ParseHistory(SampleHistory.Split('\n'));

    [Fact]
    public void ParseHistory_ReadsTypedFieldsIncludingNullableBreakdown()
    {
        var runs = Parse();

        Assert.Equal(3, runs.Count);
        Assert.Null(runs[0].Commit);
        Assert.Null(runs[0].InvalidBreakdown);
        Assert.Equal("16c0687f", runs[1].Commit);
        Assert.Equal(306, runs[1].InvalidBreakdown!.ProductBodyDefect);
        Assert.Equal(4826, runs[1].InvalidBreakdown!.HarnessShellReconstruction);
        Assert.Equal("0a7eded85c3e1410", runs[1].SweepManifestSha256);
    }

    [Fact]
    public void Render_ProjectsRunsTableAndProvenanceColumns()
    {
        string card = AuthoredCorpusHistoryCard.Render(Parse(), window: 0);

        Assert.Contains("# EVIL authored-corpus progress", card, StringComparison.Ordinal);
        Assert.Contains("| Date | Commit | Valid % | Correct | Invalid (raw) | Product defects | Harness noise |", card, StringComparison.Ordinal);
        // Null breakdown renders as an em dash, not a fabricated zero.
        Assert.Contains("| 2026-07-20 | (baseline) | 56.6% | 1501 | 5209 | — | — |", card, StringComparison.Ordinal);
        Assert.Contains("| 2026-07-30 | deadbeef | 57.4% | 1600 | 5180 | 250 | 4810 |", card, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_MovementPivotsMetricsWithPerMetricGoalAndStepGlyphs()
    {
        // Movement is the transpose of Runs: metrics are rows, runs are columns. Each row carries its
        // Goal, so Markout appends the goal glyph (↑/↓) to the label and a per-step polarity glyph (✓/✗)
        // to each column vs the previous populated one — replacing the old hand-computed Δ/Trend columns.
        string card = AuthoredCorpusHistoryCard.Render(Parse(), window: 0);

        Assert.Contains("| Metric | 2026-07-20 | 2026-07-24 | 2026-07-30 |", card, StringComparison.Ordinal);
        // Higher-is-better: 56.6→56.2 down (✗), 56.2→57.4 up (✓).
        Assert.Contains("| Valid % \u2191 | 56.6 | 56.2 \u2717 | 57.4 \u2713 |", card, StringComparison.Ordinal);
        Assert.Contains("| Correct \u2191 | 1501 | 1539 \u2713 | 1600 \u2713 |", card, StringComparison.Ordinal);
        // Lower-is-better: 5209→5259 up (✗), 5259→5180 down (✓).
        Assert.Contains("| Invalid (raw) \u2193 | 5209 | 5259 \u2717 | 5180 \u2713 |", card, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_MovementSkipsAbsentProductSplitWithoutFabricatingAStep()
    {
        // The baseline run predates the invalid breakdown, so its product-defect cell is absent (—/-) and
        // the first populated value carries no step glyph (no previous populated column to compare to);
        // the next step (306→250, lower-is-better) is an improvement (✓).
        string card = AuthoredCorpusHistoryCard.Render(Parse(), window: 0);

        Assert.Contains("| Product defects \u2193 | - | 306 | 250 \u2713 |", card, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_MovementWindowBoundsThePivotButRunsStaysUnbounded()
    {
        // Window bounds only the movement pivot to the most recent n runs; the Runs trend table always
        // lists every recorded run.
        string card = AuthoredCorpusHistoryCard.Render(Parse(), window: 2);

        // Runs table keeps the oldest run...
        Assert.Contains("| 2026-07-20 | (baseline) |", card, StringComparison.Ordinal);
        // ...but the movement pivot spans only the last two runs.
        Assert.Contains("| Metric | 2026-07-24 | 2026-07-30 |", card, StringComparison.Ordinal);
        Assert.DoesNotContain("| Metric | 2026-07-20 |", card, StringComparison.Ordinal);
        Assert.Contains("the last 2 are pivoted", card, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_SingleRunOmitsMovementTable()
    {
        string card = AuthoredCorpusHistoryCard.Render(Parse().Take(1).ToArray(), window: 0);

        Assert.DoesNotContain("## Movement", card, StringComparison.Ordinal);
        Assert.Contains("Only one recorded run so far", card, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WindowOfOneOverManyRunsDoesNotClaimOnlyOneRun()
    {
        // A movement window of 1 omits the pivot (a trend needs two), but there are still three
        // recorded runs above — the note must explain the window, not misstate the run count.
        string card = AuthoredCorpusHistoryCard.Render(Parse(), window: 1);

        Assert.DoesNotContain("## Movement", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Only one recorded run", card, StringComparison.Ordinal);
        Assert.Contains("Movement window is 1 run", card, StringComparison.Ordinal);
        // The Runs trend table still lists every run.
        Assert.Contains("| 2026-07-20 | (baseline) |", card, StringComparison.Ordinal);
        Assert.Contains("| 2026-07-30 | deadbeef |", card, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseHistory_RejectsRowMissingRequiredMetricsInsteadOfFabricatingZeros()
    {
        // A row missing core measured fields must fail loudly rather than
        // deserialize into an all-zero record that renders fabricated 0
        // metrics and false movement glyphs.
        Assert.Throws<JsonException>(
            () => AuthoredCorpusHistoryCard.ParseHistory(["""{"date":"2026-08-01","commit":"cafef00d"}"""]));
    }

    [Fact]
    public void ParseHistory_RejectsPartialInvalidBreakdownInsteadOfFabricatingZeros()
    {
        // A present-but-empty breakdown must not silently become {0,0,0};
        // only an absent (null) breakdown is a valid "not measured" signal.
        Assert.Throws<JsonException>(
            () => AuthoredCorpusHistoryCard.ParseHistory(
                ["""{"date":"2026-08-01","validPct":57.0,"correct":1610,"invalid":5170,"invalidBreakdown":{}}"""]));
    }

    [Fact]
    public void ParseHistory_RejectsNullRowInsteadOfSilentlyDroppingIt()
    {
        // A literal `null` line is well-formed JSON that deserializes to null;
        // it must fail loudly rather than vanish (which would hide a row and
        // silently corrupt the trend), unlike blank/whitespace lines.
        Assert.Throws<JsonException>(
            () => AuthoredCorpusHistoryCard.ParseHistory(
                ["""{"date":"2026-08-01","validPct":57.0,"correct":1610,"invalid":5170,"invalidBreakdown":null}""", "null"]));
    }

    [Fact]
    public void ParseHistory_AcceptsAbsentBreakdownAsNotMeasured()
    {
        // Regression guard: a null breakdown remains a legitimate pre-#3096
        // row and must still parse (rendered later as "—", never fabricated).
        var runs = AuthoredCorpusHistoryCard.ParseHistory(
            ["""{"date":"2026-08-01","validPct":57.0,"correct":1610,"invalid":5170,"invalidBreakdown":null}"""]);

        Assert.Single(runs);
        Assert.Null(runs[0].InvalidBreakdown);
    }

    [Fact]
    public void ParseHistory_DefaultsMethodologyToV1WhenAbsentAndReadsExplicitVersion()
    {
        var runs = AuthoredCorpusHistoryCard.ParseHistory(
        [
            """{"date":"2026-08-01","validPct":57.0,"correct":1610,"invalid":5170,"invalidBreakdown":null}""",
            """{"date":"2026-08-02","validPct":57.1,"correct":1620,"invalid":5160,"invalidBreakdown":{"productBodyDefect":471,"harnessShellReconstruction":4664,"unclassified":82},"methodologyVersion":2}""",
        ]);

        Assert.Equal(1, runs[0].Methodology);
        Assert.Equal(2, runs[1].Methodology);
    }

    [Fact]
    public void Render_RunsTableReportsMethodologyVersionColumn()
    {
        string card = AuthoredCorpusHistoryCard.Render(Parse(), window: 0);

        Assert.Contains("| Harness noise | Method |", card, StringComparison.Ordinal);
        // Sample history predates the field, so every run is v1.
        Assert.Contains("| 2026-07-30 | deadbeef | 57.4% | 1600 | 5180 | 250 | 4810 | v1 |", card, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_MovementSplitsProductDefectAcrossMethodologyBoundaryWithoutCharting()
    {
        // A window that straddles v1 -> v2 must not diff productBodyDefect across the boundary: the
        // metric splits into one row per version, each populated only for its own columns, so no
        // ✓/✗ step is charted from the v1 bound to the tighter v2 bound.
        var runs = AuthoredCorpusHistoryCard.ParseHistory(
        [
            """{"date":"2026-07-24","commit":"16c0687f","validPct":56.2,"correct":1539,"invalid":5259,"invalidBreakdown":{"productBodyDefect":306,"harnessShellReconstruction":4826,"unclassified":127}}""",
            """{"date":"2026-07-26","commit":"abec2dd7","validPct":56.5,"correct":1560,"invalid":5217,"invalidBreakdown":{"productBodyDefect":471,"harnessShellReconstruction":4664,"unclassified":82},"methodologyVersion":2}""",
        ]);

        string card = AuthoredCorpusHistoryCard.Render(runs, window: 0);

        Assert.Contains("| Product defects (v1 substitution lower bound) \u2193 | 306 | - |", card, StringComparison.Ordinal);
        Assert.Contains("| Product defects (v2 span-measured lower bound) \u2193 | - | 471 |", card, StringComparison.Ordinal);
        // The undivided row must not appear, and no polarity glyph may sit beside 471.
        Assert.DoesNotContain("| Product defects \u2193 |", card, StringComparison.Ordinal);
        Assert.DoesNotContain("471 \u2713", card, StringComparison.Ordinal);
        Assert.DoesNotContain("471 \u2717", card, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseHistory_ReadsTheFullValidDifferentPartition()
    {
        var runs = Parse();

        // The first row predates the added sub-buckets: they must read as null
        // (not recorded), never as a fabricated zero.
        Assert.False(runs[0].ValidDifferent!.IsComplete);
        Assert.Null(runs[0].ValidDifferent!.Lowering);
        Assert.Null(runs[0].ValidDifferent!.SubBucketSum);
        Assert.False(runs[0].TopLevelIsComplete);

        Assert.True(runs[1].ValidDifferent!.IsComplete);
        Assert.Equal(6, runs[1].ValidDifferent!.Lowering);
        Assert.Equal(4, runs[1].ValidDifferent!.KnownTaste);
        Assert.Equal(0, runs[1].ValidDifferent!.FrontierIlNoVerdict);
        Assert.Equal(runs[1].ValidDifferent!.Total, runs[1].ValidDifferent!.SubBucketSum);
        Assert.Equal(runs[1].Evaluated, runs[1].TopLevelSum);
    }

    /// <summary>
    /// The gate for the partition claim in <see cref="HistoryRunValidDifferent"/>:
    /// every complete row's sub-buckets sum to its total and its top-level buckets
    /// sum to <c>evaluated</c>. This runs against the tracked store, so a hand-appended
    /// row that drops a bucket fails here rather than silently shrinking the partition.
    /// </summary>
    [Fact]
    public void TrackedHistory_CompleteRows_PartitionExactly()
    {
        var runs = TrackedHistory();

        foreach (var run in runs.Where(run => run.ValidDifferent is { IsComplete: true }))
        {
            Assert.Equal(run.ValidDifferent!.Total, run.ValidDifferent!.SubBucketSum);
        }

        foreach (var run in runs.Where(run => run.TopLevelIsComplete))
        {
            Assert.Equal(run.Evaluated, run.TopLevelSum);
        }
    }

    /// <summary>
    /// Pins the set of rows allowed to omit the partition. Asserting set equality
    /// (not just membership) means a newly appended incomplete row fails, and a
    /// grandfathered entry that is later backfilled or removed also fails, so the
    /// list cannot go stale.
    /// </summary>
    [Fact]
    public void TrackedHistory_OnlyGrandfatheredRowsOmitThePartition()
    {
        var incomplete = TrackedHistory()
            .Where(run => run.ValidDifferent is not { IsComplete: true } || !run.TopLevelIsComplete)
            .Select(run => run.Date!)
            .ToArray();

        Assert.Equal(
            GrandfatheredIncompleteRows.OrderBy(date => date, StringComparer.Ordinal),
            incomplete.OrderBy(date => date, StringComparer.Ordinal));
    }

    static IReadOnlyList<HistoryRun> TrackedHistory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        string path = Path.Combine(directory.FullName, AuthoredCorpusHistoryCard.DefaultHistoryRelativePath);
        Assert.True(File.Exists(path), $"tracked history store not found at {path}");

        var runs = AuthoredCorpusHistoryCard.ParseHistory(File.ReadAllLines(path));
        Assert.NotEmpty(runs);
        return runs;
    }
}
