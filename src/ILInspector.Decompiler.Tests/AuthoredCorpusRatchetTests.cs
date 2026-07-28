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
        string? sha = "0a7eded85c3e1410",
        string? corpusSha = "c0117050c0117050")
        => new(evaluated, poolMatched, poolTotal, sha, corpusSha);

    static HistoryRun Row(
        string date = "2026-07-26",
        int validDifferent = 5226,
        int correct = 1576,
        int invalid = 5198,
        int? productBodyDefect = 326,
        int evaluated = 12000,
        int poolMatched = 26,
        int poolTotal = 26,
        int? methodology = 2,
        string? sha = "0a7eded85c3e1410",
        string? corpusSha = "c0117050c0117050")
        => new(
            Date: date,
            Commit: "14781e8d",
            PoolMatched: poolMatched,
            PoolTotal: poolTotal,
            Evaluated: evaluated,
            ValidPct: 0,
            Correct: correct,
            ValidDifferent: new HistoryRunValidDifferent(validDifferent, 0, 0, 0, 0, 0),
            Invalid: invalid,
            InvalidBreakdown: productBodyDefect is { } defects
                ? new HistoryRunInvalidBreakdown(defects, 0, 0)
                : null,
            Unsupported: 0,
            Drift: 0,
            InputsComplete: true,
            SweepManifestSha256: sha,
            MethodologyVersion: methodology,
            NotFull: 0,
            UnknownOutcome: 0,
            CorpusSha256: corpusSha);

    static AuthoredCorpusRatchet.RunMetrics Metrics(
        int? valid = 6802,
        int correct = 1576,
        int invalid = 5198,
        int? productBodyDefect = 326,
        int methodology = 2)
        => new(valid, correct, invalid, productBodyDefect, methodology);

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
            Metrics(valid: 4800, correct: 800, invalid: 7000, productBodyDefect: 2328),
            [Row()]);

        Assert.False(comparison.Skipped);
        Assert.Equal(
            ["valid", "correct", "invalid", "productBodyDefect"],
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
    [InlineData(6801, 1576, 5198, 326, "valid")]
    [InlineData(6802, 1575, 5198, 326, "correct")]
    [InlineData(6802, 1576, 5199, 326, "invalid")]
    [InlineData(6802, 1576, 5198, 327, "productBodyDefect")]
    public void Ratchet_BandIsZero_OneStepTheWrongWayFails(
        int valid, int correct, int invalid, int productBodyDefect, string expected)
    {
        var comparison = AuthoredCorpusRatchet.Compare(
            Key(),
            Metrics(valid, correct, invalid, productBodyDefect),
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
            Metrics(valid: 7200, correct: 2000, invalid: 4000, productBodyDefect: 100),
            [Row()]);

        Assert.Empty(comparison.Regressions);
        Assert.Equal(4, comparison.Metrics.Count);
    }

    /// <summary>
    /// The reason <c>valid</c> is an exact count and not <c>validPct</c>. The store
    /// records the percentage to one decimal, so 6,802/12,000 (56.6833) and
    /// 6,801/12,000 (56.675) both read as 56.7: a genuinely lost valid row would clear
    /// a "zero tolerance" ratchet on the rounded figure. On the exact count it cannot.
    /// </summary>
    [Fact]
    public void Ratchet_LostRowInvisibleToRoundedPercentIsCaughtOnTheExactCount()
    {
        Assert.Equal(
            Math.Round(100.0 * 6802 / 12000, 1),
            Math.Round(100.0 * 6801 / 12000, 1));

        var comparison = AuthoredCorpusRatchet.Compare(
            Key(),
            Metrics(valid: 6801, correct: 1575),
            [Row()]);

        Assert.Contains(comparison.Regressions, metric => metric.Name == "valid");
    }

    /// <summary>
    /// A row that never recorded <c>validDifferent</c> cannot show its partition closed,
    /// so it is not a baseline at all — the exact valid count is unreconstructable and
    /// the run's soundness unconfirmable. The metric-level nullability behind
    /// <c>valid</c> stays as defence in depth, but this is the gate that actually stops
    /// such a row being compared to.
    /// </summary>
    [Fact]
    public void Ratchet_RowWithoutAClosedPartitionIsNotABaseline()
    {
        var row = Row() with { ValidDifferent = null };

        Assert.False(AuthoredCorpusRatchet.IsTrustworthy(row));

        var comparison = AuthoredCorpusRatchet.Compare(Key(), Metrics(), [row]);

        Assert.True(comparison.Skipped);
        Assert.Contains("measurement not sound", comparison.SkipReason!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(11999, 26, 26, "0a7eded85c3e1410")]
    [InlineData(12000, 25, 26, "0a7eded85c3e1410")]
    [InlineData(12000, 26, 27, "0a7eded85c3e1410")]
    [InlineData(12000, 26, 26, "deadbeefdeadbeef")]
    [InlineData(12000, 26, 26, null)]
    public void Ratchet_IncomparableRunSkipsLoudly_RatherThanComparingUnlikeThings(
        int evaluated, int poolMatched, int poolTotal, string? sha)
    {
        var comparison = AuthoredCorpusRatchet.Compare(
            Key(evaluated, poolMatched, poolTotal, sha),
            Metrics(valid: 4800, correct: 800, invalid: 7000, productBodyDefect: 2328),
            [Row()]);

        Assert.True(comparison.Skipped);
        Assert.Empty(comparison.Metrics);
        Assert.Contains("no comparable baseline row", comparison.SkipReason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The baseline governs the pool hash. A run that cannot identify its pool is not
    /// comparable to a row that can — even though every count lines up — because a
    /// package resolving to a newer version with the same method identities matches
    /// cleanly, drifts nothing, and would otherwise be ratcheted against numbers
    /// measured on different code. Refusing is the safe direction: a loud skip, not a
    /// silent pass. This is why the CLI has <c>--ratchet-pool-manifest</c>.
    /// </summary>
    [Fact]
    public void Ratchet_RunThatCannotIdentifyItsPoolWillNotBorrowABaselineThatCan()
    {
        var comparison = AuthoredCorpusRatchet.Compare(Key(sha: null), Metrics(correct: 1), [Row()]);

        Assert.True(comparison.Skipped);
        Assert.Contains("(none recorded)", comparison.SkipReason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reason identity hashes compare symmetrically rather than being governed by
    /// the baseline. Under the baseline-governs rule a run whose pool <em>mismatched</em>
    /// the newest row did not stop there: it fell through to an older row recording no
    /// hash at all and compared clean against an unidentified pool. The tracked store
    /// holds exactly such a row (2026-07-20), so this was reachable in production.
    /// </summary>
    [Fact]
    public void Ratchet_MismatchedPoolDoesNotFallThroughToAnUnidentifiedOlderRow()
    {
        var comparison = AuthoredCorpusRatchet.Compare(
            Key(sha: "deadbeefdeadbeef"),
            Metrics(valid: 1, correct: 1, invalid: 9999, productBodyDefect: 9999),
            [Row(date: "2026-07-20", sha: null), Row(date: "2026-07-26")]);

        Assert.True(comparison.Skipped);
        Assert.Empty(comparison.Regressions);
    }

    /// <summary>
    /// Counts do not identify a corpus. Swapping in a different 12,000 rows — or editing
    /// one row's authored body — preserves <c>evaluated</c> and the pool, so without its
    /// own identity the substituted measurement compared clean.
    /// </summary>
    [Fact]
    public void Ratchet_DifferentCorpusWithTheSameShapeIsNotComparable()
    {
        var comparison = AuthoredCorpusRatchet.Compare(Key(corpusSha: "0000000011111111"), Metrics(), [Row()]);

        Assert.True(comparison.Skipped);
        Assert.Contains("corpusSha256", comparison.SkipReason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Absence is a distinct value on both identity hashes, in both directions, so
    /// neither side can borrow the other's identity by declining to state its own.
    /// </summary>
    [Theory]
    [InlineData(null, "0a7eded85c3e1410")]
    [InlineData("0a7eded85c3e1410", null)]
    public void Ratchet_UnknownIdentityNeverEqualsAKnownOne(string? runSha, string? baselineSha)
    {
        var comparison = AuthoredCorpusRatchet.Compare(Key(sha: runSha), Metrics(), [Row(sha: baselineSha)]);

        Assert.True(comparison.Skipped);
    }

    /// <summary>
    /// The pool digest must be reproducible, or the ratchet skips on every run and the
    /// gate is permanently red — as uninformative as the permanently green one it
    /// replaces. The sweep manifest carries <c>generatedAtUtc</c> and per-package
    /// <c>fromCache</c>, so hashing the file cannot deliver that; the digest is taken
    /// over the resolved package identities instead.
    /// </summary>
    [Fact]
    public void PoolManifestDigest_IgnoresEverythingButTheAssembliesMeasured()
    {
        string monday = WriteManifest("2026-07-26T00:00:00Z", fromCache: false);
        string tuesday = WriteManifest("2026-07-27T09:31:12Z", fromCache: true);
        string repooled = WriteManifest("2026-07-26T00:00:00Z", fromCache: false, version: "8.0.1");
        try
        {
            Assert.Equal(
                AuthoredCorpusRatchet.PoolManifestDigest(monday),
                AuthoredCorpusRatchet.PoolManifestDigest(tuesday));
            Assert.NotEqual(
                AuthoredCorpusRatchet.PoolManifestDigest(monday),
                AuthoredCorpusRatchet.PoolManifestDigest(repooled));
            Assert.Equal(16, AuthoredCorpusRatchet.PoolManifestDigest(monday).Length);
        }
        finally
        {
            File.Delete(monday);
            File.Delete(tuesday);
            File.Delete(repooled);
        }

        static string WriteManifest(string generatedAtUtc, bool fromCache, string version = "8.0.0")
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            File.WriteAllText(
                path,
                "{\"schemaVersion\":1,\"generatedAtUtc\":\"" + generatedAtUtc
                + "\",\"packages\":[{\"rank\":1,\"resolvedPackage\":\"Newtonsoft.Json\","
                + "\"resolvedVersion\":\"" + version + "\",\"tfm\":\"net8.0\",\"fromCache\":"
                + (fromCache ? "true" : "false") + "}]}");
            return path;
        }
    }

    /// <summary>A manifest that resolved nothing identifies no pool, and says so.</summary>
    [Fact]
    public void PoolManifestDigest_RefusesAManifestThatResolvedNoPackages()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(path, "{\"schemaVersion\":1,\"packages\":[]}");
        try
        {
            Assert.Throws<InvalidOperationException>(() => AuthoredCorpusRatchet.PoolManifestDigest(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Methodology governs how <c>productBodyDefect</c> is computed and nothing else.
    /// It used to sit in the comparability key, which discarded three sound metrics at
    /// every version bump — and, because every store row after the bump was therefore
    /// incomparable, is what made the tracked-store gate a permanent skip. Across a
    /// bump the other three must still ratchet.
    /// </summary>
    [Fact]
    public void Ratchet_MethodologyBumpRetiresOnlyProductBodyDefect()
    {
        var comparison = AuthoredCorpusRatchet.Compare(
            Key(),
            Metrics(valid: 4800, correct: 800, invalid: 7000, productBodyDefect: 2328, methodology: 2),
            [Row(methodology: 1)]);

        Assert.False(comparison.Skipped);
        Assert.Equal(
            ["valid", "correct", "invalid"],
            comparison.Metrics.Select(metric => metric.Name));
        Assert.Equal(3, comparison.Regressions.Count);
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
    /// The newest comparable row wins, not the newest row outright: a corpus resize in
    /// between must not silently retarget the ratchet at an incomparable baseline.
    /// </summary>
    [Fact]
    public void Ratchet_PicksNewestComparableRow_SkippingIncomparableOnes()
    {
        var comparison = AuthoredCorpusRatchet.Compare(
            Key(evaluated: 11000),
            Metrics(valid: 6802, correct: 1600),
            [Row(date: "2026-07-24", evaluated: 11000, correct: 1539), Row(date: "2026-07-26")]);

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
    /// The exit contract's integrity half. Every one of these says the run is not
    /// trustworthy, so it fails regardless of how good the numbers look — and
    /// regardless of what the ratchet concluded.
    /// </summary>
    [Theory]
    [InlineData(false, true, 0, 0, 0)]
    [InlineData(true, false, 0, 0, 0)]
    [InlineData(true, true, 1, 0, 0)]
    [InlineData(true, true, 0, 1, 0)]
    [InlineData(true, true, 0, 0, 1)]
    public void ExitContract_UntrustworthyRunFails_EvenWithACleanRatchet(
        bool inputsComplete, bool partitionClosed, int drift, int unsupported, int unknownOutcome)
    {
        bool sound = AuthoredCorpusExitContract.MeasurementIsSound(
            inputsComplete, partitionClosed, drift, unsupported, unknownOutcome);
        var clean = AuthoredCorpusRatchet.Compare(Key(), Metrics(), [Row()]);

        Assert.False(sound);
        Assert.Empty(clean.Regressions);
        Assert.Equal(1, AuthoredCorpusExitContract.ExitCode(sound, invalid: 0, clean));
    }

    [Fact]
    public void ExitContract_SoundRunRequiresEveryIntegrityCondition()
    {
        Assert.True(AuthoredCorpusExitContract.MeasurementIsSound(true, true, 0, 0, 0));
    }

    /// <summary>
    /// A corpus row that failed to parse is an integrity failure, not a logged
    /// curiosity. It shrinks <c>evaluated</c>, which makes the run incomparable, which
    /// makes the ratchet skip — and a skip exits 0. Left out of this predicate, a
    /// corpus quietly losing rows would <em>disarm</em> the gate instead of tripping
    /// it, which is precisely the shape of the defect #3245 removes.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 12000, true)]
    [InlineData(1, 0, 12000, false)]
    [InlineData(0, 1, 11999, false)]
    [InlineData(0, 0, 0, false)]
    public void ExitContract_InputsAreCompleteOnlyWhenNoRowWasLost(
        int unmatchedRows, int malformedRows, int evaluated, bool expected)
    {
        Assert.Equal(
            expected,
            AuthoredCorpusExitContract.InputsComplete(unmatchedRows, malformedRows, evaluated));
    }

    /// <summary>
    /// End to end for the case above: one dropped row, everything else pristine, a
    /// ratchet that skips because the shortened corpus no longer matches its baseline.
    /// Every individual signal reads benign; the run must still fail.
    /// </summary>
    [Fact]
    public void ExitContract_DroppedCorpusRowFails_EvenThoughTheRatchetSkipsGreen()
    {
        var skipped = AuthoredCorpusRatchet.Compare(Key(evaluated: 11999), Metrics(), [Row()]);
        // Even setting the skip aside, integrity alone must fail this run.
        bool sound = AuthoredCorpusExitContract.MeasurementIsSound(
            AuthoredCorpusExitContract.InputsComplete(unmatchedRows: 0, malformedRows: 1, evaluated: 11999),
            partitionClosed: true,
            drift: 0,
            unsupported: 0,
            unknownOutcome: 0);

        Assert.True(skipped.Skipped);
        Assert.True(AuthoredCorpusExitContract.QualityHeld(invalid: 5198, skipped));
        Assert.False(sound);
        Assert.Equal(1, AuthoredCorpusExitContract.ExitCode(sound, invalid: 5198, skipped));
    }

    /// <summary>
    /// A skip fails. It carries no quality opinion — <see cref="AuthoredCorpusExitContract.QualityHeld"/>
    /// is vacuously true on it — but exiting 0 having compared nothing is the defect
    /// #3245 removes, rebuilt one level up. Passing <c>--ratchet-baseline</c> demands a
    /// verdict; "none available" fails that demand.
    ///
    /// <para>The weekly caller is why this is not pedantry: its pool is resolved from
    /// current package versions and will drift off the recorded manifest, so a green
    /// skip would leave the job passing forever while measuring nothing.</para>
    /// </summary>
    [Fact]
    public void ExitContract_SkipFails_BecauseAGateThatComparedNothingIsNotAPass()
    {
        var skipped = AuthoredCorpusRatchet.Comparison.Skip("no comparable row");

        Assert.True(AuthoredCorpusExitContract.QualityHeld(invalid: 5198, skipped));
        Assert.False(AuthoredCorpusExitContract.RatchetReachedAVerdict(skipped));
        Assert.Equal(1, AuthoredCorpusExitContract.ExitCode(measurementIsSound: true, invalid: 5198, skipped));
        Assert.Equal(1, AuthoredCorpusExitContract.ExitCode(measurementIsSound: false, invalid: 0, skipped));
    }

    /// <summary>
    /// The converse, so the rule above cannot be read as "any ratchet object fails":
    /// a real comparison that held exits 0, and no baseline at all leaves the
    /// historical contract untouched.
    /// </summary>
    [Fact]
    public void ExitContract_OnlySkipsFail_AVerdictAndNoBaselineBothStillPass()
    {
        var held = AuthoredCorpusRatchet.Compare(Key(), Metrics(), [Row()]);

        Assert.True(AuthoredCorpusExitContract.RatchetReachedAVerdict(held));
        Assert.True(AuthoredCorpusExitContract.RatchetReachedAVerdict(null));
        Assert.Equal(0, AuthoredCorpusExitContract.ExitCode(measurementIsSound: true, invalid: 5198, held));
    }

    /// <summary>
    /// The headline fix, at the contract level: with a baseline it holds, a run with
    /// thousands of invalid rows succeeds. Under the old contract this was the
    /// permanently-1 case that made the exit code read identically at 56.7% valid and
    /// at 40% valid.
    /// </summary>
    [Fact]
    public void ExitContract_NonZeroInvalidPassesWhenItHoldsItsBaseline()
    {
        var held = AuthoredCorpusRatchet.Compare(Key(), Metrics(), [Row()]);

        Assert.Equal(0, AuthoredCorpusExitContract.ExitCode(measurementIsSound: true, invalid: 5198, held));
    }

    [Fact]
    public void ExitContract_RegressionFailsEvenWhenMeasurementIsSound()
    {
        var regressed = AuthoredCorpusRatchet.Compare(Key(), Metrics(correct: 800), [Row()]);

        Assert.NotEmpty(regressed.Regressions);
        Assert.Equal(1, AuthoredCorpusExitContract.ExitCode(measurementIsSound: true, invalid: 5198, regressed));
    }

    /// <summary>
    /// Without a baseline the historical contract is untouched, so the trend store's
    /// documented append run keeps exiting 1 while invalid is non-zero. Adding the
    /// ratchet must not silently repurpose the default exit code.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(5198, 1)]
    public void ExitContract_WithoutABaselineTheHistoricalPerfectionContractStands(int invalid, int expected)
    {
        Assert.Equal(expected, AuthoredCorpusExitContract.ExitCode(measurementIsSound: true, invalid, ratchet: null));
    }

    /// <summary>
    /// The tracked trend store's own append gate: the newest recorded row must not regress
    /// against the newest earlier row it is comparable with. Rows before it were
    /// recorded without a ratchet and are data, not a contract, so only the new row is
    /// judged — which is why this passes today even though the store contains an
    /// earlier step where raw invalid rose.
    ///
    /// <para><see cref="Assert.False(bool)"/> on <c>Skipped</c> is the load-bearing
    /// half. <c>Assert.Empty(Regressions)</c> alone passes for a skip, so this gate was
    /// vacuous while methodology sat in the comparability key: the newest row was the
    /// only v2 row, nothing was comparable, and a hand-appended regression that also
    /// nudged <c>poolMatched</c> landed green. Asserting the comparison actually
    /// happened is what makes the emptiness mean something.</para>
    /// </summary>
    [Fact]
    public void TrackedHistory_NewestRowDoesNotRegressAgainstItsBaseline()
    {
        var comparison = AuthoredCorpusRatchet.CompareNewestRow(AuthoredCorpusHistoryCardTests.TrackedHistory());

        Assert.False(comparison.Skipped, comparison.SkipReason);
        Assert.NotEmpty(comparison.Metrics);
        Assert.Empty(comparison.Regressions);
    }

    /// <summary>
    /// The exact bypass a reviewer built against the gate above, and the reason the
    /// ratchet refuses untrustworthy rows outright rather than only ratcheting quality.
    ///
    /// <para>Append a row that sheds one row from <c>invalid</c> into <c>unsupported</c>
    /// and flips <c>inputsComplete</c>. Every quality metric holds or improves —
    /// <c>invalid</c> went <em>down</em> — so a pure quality ratchet waves it through.
    /// But the run measured less, and reporting absence as progress is precisely the
    /// failure this file exists to remove.</para>
    /// </summary>
    [Fact]
    public void TrackedHistory_RowThatMeasuredLessCannotPassByLookingBetter()
    {
        var runs = AuthoredCorpusHistoryCardTests.TrackedHistory().ToList();
        var newest = runs[^1];
        var bypass = newest with
        {
            Date = "2026-07-28",
            Invalid = newest.Invalid - 1,
            Unsupported = newest.Unsupported + 1,
            InputsComplete = false,
        };

        // The quality half alone sees nothing wrong: invalid fell, everything else held.
        Assert.Empty(AuthoredCorpusRatchet
            .Compare(AuthoredCorpusRatchet.RunKey.From(bypass), AuthoredCorpusRatchet.RunMetrics.From(bypass), [newest])
            .Regressions);

        runs.Add(bypass);
        var comparison = AuthoredCorpusRatchet.CompareNewestRow(runs);

        Assert.True(comparison.Skipped);
        Assert.Contains("did not record a sound measurement", comparison.SkipReason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same rule applied to the other side of the comparison: an untrustworthy row
    /// is not a baseline either. Ratcheting against a run that measured less would set
    /// the bar by how much was missed.
    /// </summary>
    [Fact]
    public void Ratchet_UntrustworthyBaselineRowIsNotSelected()
    {
        var comparison = AuthoredCorpusRatchet.Compare(
            Key(),
            Metrics(),
            [Row(date: "2026-07-27", correct: 1) with { Drift = 1 }]);

        Assert.True(comparison.Skipped);
        Assert.Contains("measurement not sound", comparison.SkipReason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every row in the tracked store must record a sound measurement, so an appended
    /// run that measured less is rejected where it is written rather than only when it
    /// happens to be the newest. Asserted as set equality: the 2026-07-20 row predates
    /// <c>unknownOutcome</c> and so cannot be confirmed sound, and a later backfill of
    /// it must fail here rather than silently shrink the exception list.
    /// </summary>
    [Fact]
    public void TrackedHistory_OnlyTheUnconfirmableRowIsNotTrustworthy()
    {
        var unconfirmable = AuthoredCorpusHistoryCardTests.TrackedHistory()
            .Where(run => !AuthoredCorpusRatchet.IsTrustworthy(run))
            .Select(run => run.Date ?? "(undated)")
            .ToArray();

        Assert.Equal(["2026-07-20"], unconfirmable);
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
            ValidDifferent = newest.ValidDifferent! with { Total = newest.ValidDifferent!.Total - 100 },
            Correct = 800,
            InvalidBreakdown = new HistoryRunInvalidBreakdown(2328, 0, 0),
        });

        var comparison = AuthoredCorpusRatchet.CompareNewestRow(runs);

        Assert.False(comparison.Skipped);
        Assert.Equal(newest.Date, comparison.Baseline!.Date);
        Assert.Equal(
            ["valid", "correct", "productBodyDefect"],
            comparison.Regressions.Select(metric => metric.Name));
    }
}
