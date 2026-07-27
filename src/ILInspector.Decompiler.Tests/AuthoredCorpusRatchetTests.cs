using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Gates for the authored-corpus regression ratchet (#3245).
///
/// The defect these cover: the benchmark's exit code required <c>invalid == 0</c>,
/// which on a 12,000-row corpus sitting at ~5,200 invalid is permanently 1. It read
/// identically at 56.7% valid and at 40% valid, so it could not detect a regression.
/// The card tests next door were blind to the same movement — they assert the
/// partition <em>closes</em>, not that the metric <em>held</em> — so a hand-appended
/// row halving the quality landed green. <see cref="Ratchet_CatchesTheRegressionTheCardTestsMissed"/>
/// is that exact row, and it must fail here.
/// </summary>
[Trait("Area", "Corpus")]
public class AuthoredCorpusRatchetTests
{
    static AuthoredCorpusRatchet.RunKey Key(
        int evaluated = 12000,
        int poolMatched = 26,
        int poolTotal = 26,
        int methodology = 2,
        string? sha = "0a7eded85c3e1410")
        => new(evaluated, poolMatched, poolTotal, methodology, sha);

    static HistoryRun Row(
        string date = "2026-07-26",
        double validPct = 56.7,
        int correct = 1576,
        int invalid = 5198,
        int? productBodyDefect = 326,
        int evaluated = 12000,
        int poolMatched = 26,
        int poolTotal = 26,
        int? methodology = 2,
        string? sha = "0a7eded85c3e1410")
        => new(
            Date: date,
            Commit: "14781e8d",
            PoolMatched: poolMatched,
            PoolTotal: poolTotal,
            Evaluated: evaluated,
            ValidPct: validPct,
            Correct: correct,
            ValidDifferent: null,
            Invalid: invalid,
            InvalidBreakdown: productBodyDefect is { } defects
                ? new HistoryRunInvalidBreakdown(defects, 0, 0)
                : null,
            Unsupported: 0,
            Drift: 0,
            InputsComplete: true,
            SweepManifestSha256: sha,
            MethodologyVersion: methodology);

    static AuthoredCorpusRatchet.RunMetrics Metrics(
        double validPct = 56.7,
        int correct = 1576,
        int invalid = 5198,
        int? productBodyDefect = 326)
        => new(validPct, correct, invalid, productBodyDefect);

    /// <summary>
    /// The headline case. These are the exact numbers a reviewer appended to the
    /// tracked store to show the existing gates were blind: valid rate cut from 56.7%
    /// to 40%, correct halved, product defects up sevenfold. All 17 history-card tests
    /// stayed green on it. The ratchet must not.
    /// </summary>
    [Fact]
    public void Ratchet_CatchesTheRegressionTheCardTestsMissed()
    {
        var comparison = AuthoredCorpusRatchet.Compare(
            Key(),
            Metrics(validPct: 40.0, correct: 800, invalid: 7000, productBodyDefect: 2328),
            [Row()]);

        Assert.False(comparison.Skipped);
        Assert.Equal(
            ["validPct", "correct", "invalid", "productBodyDefect"],
            comparison.Regressions.Select(metric => metric.Name));
    }

    [Fact]
    public void Ratchet_HoldingEveryMetricIsClean()
    {
        var comparison = AuthoredCorpusRatchet.Compare(Key(), Metrics(), [Row()]);

        Assert.False(comparison.Skipped);
        Assert.Empty(comparison.Regressions);
    }

    /// <summary>
    /// The band is zero: a single row moving the wrong way is a regression. Anything
    /// wider would be the harness declining to report code-attributable movement,
    /// since two runs of one commit against one pinned pool measured bit-identical.
    /// </summary>
    [Theory]
    [InlineData(56.6, 1576, 5198, 326, "validPct")]
    [InlineData(56.7, 1575, 5198, 326, "correct")]
    [InlineData(56.7, 1576, 5199, 326, "invalid")]
    [InlineData(56.7, 1576, 5198, 327, "productBodyDefect")]
    public void Ratchet_BandIsZero_OneStepTheWrongWayFails(
        double validPct, int correct, int invalid, int productBodyDefect, string expected)
    {
        var comparison = AuthoredCorpusRatchet.Compare(
            Key(),
            Metrics(validPct, correct, invalid, productBodyDefect),
            [Row()]);

        Assert.Equal([expected], comparison.Regressions.Select(metric => metric.Name));
    }

    /// <summary>
    /// The goal direction is per metric, so an improvement is never reported as a
    /// regression just because the number went down (or up).
    /// </summary>
    [Fact]
    public void Ratchet_ImprovementInEveryDirectionIsClean()
    {
        var comparison = AuthoredCorpusRatchet.Compare(
            Key(),
            Metrics(validPct: 60.0, correct: 2000, invalid: 4000, productBodyDefect: 100),
            [Row()]);

        Assert.Empty(comparison.Regressions);
        Assert.Equal(4, comparison.Metrics.Count);
    }

    /// <summary>
    /// The store records validPct at one decimal place. Comparing a full-precision
    /// current value against the rounded baseline would fail a run that did not move,
    /// so both sides round to the precision the store preserves.
    /// </summary>
    [Fact]
    public void Ratchet_ValidPctComparesAtRecordedPrecision()
    {
        var comparison = AuthoredCorpusRatchet.Compare(Key(), Metrics(validPct: 56.6501), [Row(validPct: 56.7)]);

        Assert.Empty(comparison.Regressions);
    }

    [Theory]
    [InlineData(11999, 26, 26, 2, "0a7eded85c3e1410")]
    [InlineData(12000, 25, 26, 2, "0a7eded85c3e1410")]
    [InlineData(12000, 26, 27, 2, "0a7eded85c3e1410")]
    [InlineData(12000, 26, 26, 1, "0a7eded85c3e1410")]
    [InlineData(12000, 26, 26, 2, "deadbeefdeadbeef")]
    public void Ratchet_IncomparableRunSkipsLoudly_RatherThanComparingUnlikeThings(
        int evaluated, int poolMatched, int poolTotal, int methodology, string sha)
    {
        var comparison = AuthoredCorpusRatchet.Compare(
            Key(evaluated, poolMatched, poolTotal, methodology, sha),
            Metrics(validPct: 40.0, correct: 800, invalid: 7000, productBodyDefect: 2328),
            [Row()]);

        Assert.True(comparison.Skipped);
        Assert.Empty(comparison.Metrics);
        Assert.Contains("no comparable baseline row", comparison.SkipReason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A live benchmark run is not handed the pool's sweep manifest, so it carries no
    /// hash. Requiring one unconditionally would make the live path skip forever — a
    /// permanently green gate wearing a different hat.
    /// </summary>
    [Fact]
    public void Ratchet_AbsentManifestHashOnOneSideStillCompares()
    {
        var comparison = AuthoredCorpusRatchet.Compare(Key(sha: null), Metrics(correct: 1), [Row()]);

        Assert.False(comparison.Skipped);
        Assert.Contains(comparison.Regressions, metric => metric.Name == "correct");
    }

    /// <summary>
    /// productBodyDefect is absent on rows predating the invalid breakdown. Absent
    /// means not measured, so it drops out of the comparison rather than reading as
    /// zero — which would report every later run as a massive regression.
    /// </summary>
    [Fact]
    public void Ratchet_UnrecordedProductDefectsAreOmittedNotTreatedAsZero()
    {
        var comparison = AuthoredCorpusRatchet.Compare(
            Key(),
            Metrics(productBodyDefect: 326),
            [Row(productBodyDefect: null)]);

        Assert.False(comparison.Skipped);
        Assert.DoesNotContain(comparison.Metrics, metric => metric.Name == "productBodyDefect");
        Assert.Empty(comparison.Regressions);
    }

    /// <summary>
    /// The newest comparable row wins, not the newest row outright: a methodology bump
    /// in between must not silently retarget the ratchet at an incomparable baseline.
    /// </summary>
    [Fact]
    public void Ratchet_PicksNewestComparableRow_SkippingIncomparableOnes()
    {
        var comparison = AuthoredCorpusRatchet.Compare(
            Key(methodology: 1),
            Metrics(correct: 1600),
            [Row(date: "2026-07-24", methodology: 1, correct: 1539), Row(date: "2026-07-26", methodology: 2)]);

        Assert.False(comparison.Skipped);
        Assert.Equal("2026-07-24", comparison.Baseline!.Date);
        Assert.Empty(comparison.Regressions);
    }

    [Fact]
    public void Ratchet_EmptyBaselineSkipsWithItsOwnReason()
    {
        var comparison = AuthoredCorpusRatchet.Compare(Key(), Metrics(), []);

        Assert.True(comparison.Skipped);
        Assert.Equal("baseline holds no runs", comparison.SkipReason);
    }

    /// <summary>
    /// A skip is rendered as loudly as a failure. A gate that quietly compared nothing
    /// and reported success is the defect being replaced, so the report must say so
    /// rather than printing a clean-looking verdict.
    /// </summary>
    [Fact]
    public void Report_SkipSaysNothingWasCompared()
    {
        var writer = new StringWriter();
        AuthoredCorpusRatchet.Report(AuthoredCorpusRatchet.Comparison.Skip("store holds fewer than two runs"), writer);

        string text = writer.ToString();
        Assert.Contains("RATCHET SKIPPED: store holds fewer than two runs", text, StringComparison.Ordinal);
        Assert.Contains("nothing was compared", text, StringComparison.Ordinal);
        Assert.DoesNotContain("RATCHET OK", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The failing report names the metric that moved and the direction it owed, so an
    /// operator does not have to re-derive the regression from two JSON blobs.
    /// </summary>
    [Fact]
    public void Report_FailureNamesTheRegressedMetricAndItsGoal()
    {
        var writer = new StringWriter();
        var comparison = AuthoredCorpusRatchet.Compare(Key(), Metrics(correct: 800), [Row()]);
        AuthoredCorpusRatchet.Report(comparison, writer);

        string text = writer.ToString();
        Assert.Contains("RATCHET FAILED vs 2026-07-26 (14781e8d)", text, StringComparison.Ordinal);
        Assert.Contains("REGRESSED  correct 1576 -> 800 (want >= 1576)", text, StringComparison.Ordinal);
        Assert.Contains("held       invalid 5198 -> 5198 (want <= 5198)", text, StringComparison.Ordinal);
        // The lower-bound caveat travels with the number so its movement is not
        // over-read as a full census of decompiler-caused body defects.
        Assert.Contains("lower bound", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tracked store's own append gate: the newest recorded row must not regress
    /// against the newest earlier row it is comparable with. Rows before it were
    /// recorded without a ratchet and are data, not a contract, so only the new row is
    /// judged — which is why this passes today even though the store contains an
    /// earlier step where raw invalid rose.
    ///
    /// This is currently a <em>skip</em>: the newest row is the only one stamped
    /// methodologyVersion 2, and v1 and v2 productBodyDefect counts are not
    /// comparable by construction. The assertion is therefore deliberately written to
    /// accept either outcome except a regression, so it starts biting the moment a
    /// second v2 row lands rather than needing to be re-enabled by hand.
    /// </summary>
    [Fact]
    public void TrackedHistory_NewestRowDoesNotRegressAgainstItsBaseline()
    {
        var comparison = AuthoredCorpusRatchet.CompareNewestRow(AuthoredCorpusHistoryCardTests.TrackedHistory());

        Assert.Empty(comparison.Regressions);
    }

    /// <summary>
    /// Non-vacuity for the gate above: the tracked store, with the newest row replaced
    /// by the regressed one, must fail. Without this, a comparability key that stopped
    /// matching would leave the store gate permanently green and permanently silent.
    /// </summary>
    [Fact]
    public void TrackedHistory_GateIsNotVacuous_ARegressedAppendFails()
    {
        var runs = AuthoredCorpusHistoryCardTests.TrackedHistory().ToList();
        var newest = runs[^1];
        runs.Add(newest with
        {
            Date = "2026-07-31",
            ValidPct = 40.0,
            Correct = 800,
            InvalidBreakdown = new HistoryRunInvalidBreakdown(2328, 0, 0),
        });

        var comparison = AuthoredCorpusRatchet.CompareNewestRow(runs);

        Assert.False(comparison.Skipped);
        Assert.Equal(newest.Date, comparison.Baseline!.Date);
        Assert.Equal(
            ["validPct", "correct", "productBodyDefect"],
            comparison.Regressions.Select(metric => metric.Name));
    }
}
