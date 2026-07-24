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
    public void Render_MovementUsesPerMetricPolarity()
    {
        // Latest (250 product, 5180 invalid, 1600 correct, 57.4%) vs previous
        // (306 product, 5259 invalid, 1539 correct, 56.2%): every headline metric
        // improves, and lower-is-better metrics must read "improved" when they drop.
        string card = AuthoredCorpusHistoryCard.Render(Parse(), window: 0);

        Assert.Contains("| Valid % | 56.2% | 57.4% | +1.2% | improved |", card, StringComparison.Ordinal);
        Assert.Contains("| Correct | 1539 | 1600 | +61 | improved |", card, StringComparison.Ordinal);
        Assert.Contains("| Invalid (raw) | 5259 | 5180 | −79 | improved |", card, StringComparison.Ordinal);
        Assert.Contains("| Product defects | 306 | 250 | −56 | improved |", card, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_MovementIsNotApplicableWhenPreviousLacksProductSplit()
    {
        // Window of two spans the null-breakdown baseline and the first split run,
        // so the product-defect delta cannot be computed and must read n/a rather
        // than treating the missing split as zero.
        string card = AuthoredCorpusHistoryCard.Render(Parse().Take(2).ToArray(), window: 2);

        Assert.Contains("| Product defects | — | 306 | — | n/a |", card, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WindowKeepsMostRecentRuns()
    {
        string card = AuthoredCorpusHistoryCard.Render(Parse(), window: 2);

        Assert.DoesNotContain("2026-07-20", card, StringComparison.Ordinal);
        Assert.Contains("2026-07-24", card, StringComparison.Ordinal);
        Assert.Contains("2026-07-30", card, StringComparison.Ordinal);
        Assert.Contains("Showing 2 of 3 recorded run(s).", card, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_SingleRunOmitsMovementTable()
    {
        string card = AuthoredCorpusHistoryCard.Render(Parse().Take(1).ToArray(), window: 0);

        Assert.DoesNotContain("Movement", card, StringComparison.Ordinal);
    }
}
