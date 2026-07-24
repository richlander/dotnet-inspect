using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Corpus")]
public class AuthoredCorpusHistoryCardTests
{
    const string SampleHistory = """
        {"date":"2026-07-20","commit":null,"poolMatched":26,"poolTotal":26,"evaluated":12000,"validPct":56.6,"correct":1501,"validDifferent":{"total":5290,"frontierIlExact":3097,"frontierIlDiff":2181},"invalid":5209,"invalidBreakdown":null,"unsupported":0,"drift":0,"honest":true,"sweepManifestSha256":null}
        {"date":"2026-07-24","commit":"16c0687f","poolMatched":26,"poolTotal":26,"evaluated":12000,"validPct":56.2,"correct":1539,"validDifferent":{"total":5202,"frontierIlExact":3055,"frontierIlDiff":2137},"invalid":5259,"invalidBreakdown":{"productBodyDefect":306,"harnessShellReconstruction":4826,"unclassified":127},"unsupported":0,"drift":0,"honest":true,"sweepManifestSha256":"0a7eded85c3e1410"}
        {"date":"2026-07-30","commit":"deadbeef","poolMatched":26,"poolTotal":26,"evaluated":12000,"validPct":57.4,"correct":1600,"validDifferent":{"total":5100,"frontierIlExact":3000,"frontierIlDiff":2100},"invalid":5180,"invalidBreakdown":{"productBodyDefect":250,"harnessShellReconstruction":4810,"unclassified":120},"unsupported":0,"drift":0,"honest":true,"sweepManifestSha256":"abc123"}
        """;

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
}
