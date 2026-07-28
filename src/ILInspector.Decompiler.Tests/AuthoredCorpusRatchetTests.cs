using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
            ValidDifferent: new HistoryRunValidDifferent(validDifferent, validDifferent, 0, 0, 0, 0),
            Invalid: invalid,
            InvalidBreakdown: productBodyDefect is { } defects
                ? new HistoryRunInvalidBreakdown(defects, 0, 0)
                : null,
            Unsupported: 0,
            Drift: 0,
            InputsComplete: true,
            SweepManifestSha256: null,
            PoolSha256: sha,
            MethodologyVersion: methodology,
            NotFull: 0,
            UnknownOutcome: 0,
            CorpusSha256: corpusSha);

    static AuthoredCorpusRatchet.RunMetrics Metrics(
        int? valid = 6802,
        int correct = 1576,
        int invalid = 5198,
        int? productBodyDefect = 326,
        int methodology = 2,
        bool identified = true)
        => new(valid, correct, invalid, productBodyDefect, methodology, MethodologyStated: true, Identified: identified);

    /// <summary>
    /// The contract an ordinary caller gets: whatever the presence of a baseline
    /// selects. Tests that mean <c>--integrity-only</c> name it explicitly instead.
    /// </summary>
    static AuthoredCorpusExitContract.QualityContract Contract(AuthoredCorpusRatchet.Comparison? ratchet)
        => AuthoredCorpusExitContract.ContractFor(integrityOnly: false, ratchet);

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
    /// silent pass. A live run always states its pool, so absence means a recorded
    /// row that predates pool identity.
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
    /// The same rule for the corpus hash, which a reviewer found had no symmetry test
    /// of its own: relaxing <c>CorpusSha256</c> to "compare only when both sides state
    /// one" passed the entire suite, and let a row recording no corpus identity be
    /// compared against a run that had one. Absence is a value here too, in both
    /// directions.
    /// </summary>
    [Theory]
    [InlineData(null, "5f2b1c9d0e3a4b68")]
    [InlineData("5f2b1c9d0e3a4b68", null)]
    public void Ratchet_UnknownCorpusNeverEqualsAKnownOne(string? runCorpusSha, string? baselineCorpusSha)
    {
        var comparison = AuthoredCorpusRatchet.Compare(
            Key(corpusSha: runCorpusSha),
            Metrics(),
            [Row(corpusSha: baselineCorpusSha)]);

        Assert.True(comparison.Skipped);
        Assert.Contains("corpusSha256", comparison.SkipReason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Recording a bucket is not the same as the buckets adding up. A reviewer supplied
    /// a baseline claiming 100 evaluated whose buckets summed to 99 — a lost target,
    /// which reports a lower <c>invalid</c> for having measured less — and the
    /// presence-only check accepted it, returning <c>RATCHET OK</c> and exit 0.
    /// </summary>
    [Fact]
    public void Ratchet_BaselineWhoseBucketsDoNotSumToEvaluatedIsNotTrustworthy()
    {
        var shortByOne = Row() with { Evaluated = 12001 };

        Assert.False(AuthoredCorpusRatchet.IsTrustworthy(shortByOne));
        Assert.True(AuthoredCorpusRatchet.IsTrustworthy(Row()));

        var comparison = AuthoredCorpusRatchet.Compare(Key(evaluated: 12001), Metrics(), [shortByOne]);
        Assert.True(comparison.Skipped);
    }

    /// <summary>
    /// The same rule one level down: the valid sub-buckets must account for the
    /// valid-different total, or the row has lost rows inside a bucket that still adds
    /// up at the top level.
    /// </summary>
    [Fact]
    public void Ratchet_BaselineWhoseValidSubBucketsDoNotSumIsNotTrustworthy()
    {
        var row = Row();
        var skewed = row with
        {
            ValidDifferent = new HistoryRunValidDifferent(row.ValidDifferent!.Total, 1, 0, 0, 0, 0),
        };

        Assert.False(AuthoredCorpusRatchet.IsTrustworthy(skewed));
    }

    /// <summary>
    /// A baseline that cannot state a metric the run states must not be compared at
    /// all. <see cref="AuthoredCorpusRatchet"/> only emits a metric when both sides have
    /// a number, so a reviewer's row with <c>invalidBreakdown: null</c> produced a clean
    /// three-metric <c>RATCHET OK</c> while the product signal went unchecked — a pass
    /// that had quietly stopped ratcheting the metric the trend store exists to track.
    /// </summary>
    [Fact]
    public void Ratchet_BaselineMissingAMetricTheRunHasIsNotComparable()
    {
        var comparison = AuthoredCorpusRatchet.Compare(Key(), Metrics(), [Row(productBodyDefect: null)]);

        Assert.True(comparison.Skipped);
        Assert.Contains("invalidBreakdown", comparison.SkipReason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pool digest must be reproducible, or the ratchet skips on every run and the
    /// gate is permanently red — as uninformative as the permanently green one it
    /// replaces. Staging the same assemblies to a different directory is the normal
    /// case (every CI run does it), so identity is content plus file name, never path.
    /// </summary>
    [Fact]
    public void PoolDigest_IdentifiesContent_NotWhereItWasStaged()
    {
        using var pool = new TempPool();
        string monday = pool.Write("first", "A.dll", [1, 2, 3]);
        string restaged = pool.Write("second", "A.dll", [1, 2, 3]);
        string rebuilt = pool.Write("third", "A.dll", [1, 2, 4]);

        Assert.Equal(AuthoredCorpusRatchet.PoolDigest([monday]), AuthoredCorpusRatchet.PoolDigest([restaged]));
        Assert.NotEqual(AuthoredCorpusRatchet.PoolDigest([monday]), AuthoredCorpusRatchet.PoolDigest([rebuilt]));
        Assert.Equal(64, AuthoredCorpusRatchet.PoolDigest([monday]).Length);
    }

    /// <summary>
    /// The identity covers the whole pool, in any order. A reviewer showed the previous
    /// manifest-derived digest could not do this: <c>eng/prepare-evil-corpus.sh</c>
    /// measures the union of the package sweep and a fixed set of real-world
    /// assemblies, but the manifest described only the sweep, so changing the other
    /// half left the identity unchanged.
    /// </summary>
    [Fact]
    public void PoolDigest_CoversEveryAssemblyAndIgnoresOrder()
    {
        using var pool = new TempPool();
        string sweep = pool.Write("sweep", "Sweep.dll", [1, 1, 1]);
        string realWorld = pool.Write("real", "RealWorld.dll", [2, 2, 2]);
        string changed = pool.Write("real2", "RealWorld.dll", [3, 3, 3]);

        Assert.Equal(
            AuthoredCorpusRatchet.PoolDigest([sweep, realWorld]),
            AuthoredCorpusRatchet.PoolDigest([realWorld, sweep]));
        Assert.NotEqual(
            AuthoredCorpusRatchet.PoolDigest([sweep, realWorld]),
            AuthoredCorpusRatchet.PoolDigest([sweep, changed]));
        Assert.NotEqual(
            AuthoredCorpusRatchet.PoolDigest([sweep, realWorld]),
            AuthoredCorpusRatchet.PoolDigest([sweep]));
    }

    /// <summary>
    /// A one-file pool cannot forge a two-file pool's identity. The composition is
    /// built from fixed-width digests precisely so that it cannot be ambiguous: a Linux
    /// file name may contain both the field separator (<c>:</c>) and the record
    /// separator (<c>\n</c>), so interpolating the name raw would let a single
    /// adversarially-named file produce the identity string of a pool it is not.
    ///
    /// <para>The forged name below spells out a complete second record. Under raw
    /// interpolation the two pools hash the same; under the digested composition they
    /// cannot, because the whole forged name collapses into one fixed-width field.</para>
    /// </summary>
    [Fact]
    public void PoolDigest_CannotBeForgedByAFileNameThatSpellsASeparator()
    {
        using var pool = new TempPool();
        string honest = pool.Write("honest-a", "A.dll", [1, 1, 1]);
        string second = pool.Write("honest-b", "B.dll", [2, 2, 2]);

        // "A.dll:<sha of A's bytes>\nB.dll" — a name that closes A's record and opens B's.
        string forgedName = $"A.dll:{Convert.ToHexStringLower(SHA256.HashData(new byte[] { 1, 1, 1 }))}\nB.dll";
        string forged = pool.Write("forge", forgedName, [2, 2, 2]);

        Assert.NotEqual(
            AuthoredCorpusRatchet.PoolDigest([honest, second]),
            AuthoredCorpusRatchet.PoolDigest([forged]));
    }

    /// <summary>
    /// The digests are an integrity gate, so they are full SHA-256 and not truncated.
    /// An earlier revision kept the first eight bytes; a reviewer pointed out that a
    /// 64-bit identity falls to a birthday attack in roughly 2^32 operations, which
    /// would let a pool or corpus be swapped underneath a recorded baseline while its
    /// recorded identity still matched.
    /// </summary>
    [Fact]
    public void Digests_AreFullSha256_NotTruncated()
    {
        using var pool = new TempPool();
        string assembly = pool.Write("full", "A.dll", [7, 7, 7]);

        Assert.Equal(64, AuthoredCorpusRatchet.PoolDigest([assembly]).Length);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(new byte[] { 7, 7, 7 })),
            AuthoredCorpusRatchet.CorpusDigest(assembly));
    }

    /// <summary>
    /// The seam that keeps identity and measurement from drifting: whatever
    /// <c>SelectPool</c> chose is exactly what it identified. Computing the two
    /// separately is what let a reviewer reorder two byte-distinct assemblies sharing
    /// an identity and change the measurement without changing the digest, and that
    /// mistake was invisible to this suite because the benchmark cannot be linked into
    /// it. Returning both from one call moves the property somewhere a test can reach.
    /// </summary>
    [Fact]
    public void SelectPool_IdentifiesExactlyWhatItSelected()
    {
        using var pool = new TempPool();
        string a = pool.Write("a", "A.dll", [1, 1, 1]);
        string shadowedA = pool.Write("a2", "A.dll", [9, 9, 9]);
        string b = pool.Write("b", "B.dll", [2, 2, 2]);
        string ignored = pool.Write("c", "C.dll", [3, 3, 3]);

        static string? Identify(string path)
            => Path.GetFileName(path) is "C.dll" ? null : Path.GetFileName(path);

        var selected = AuthoredCorpusRatchet.SelectPool([a, shadowedA, b, ignored], Identify);

        // First path per identity wins; the shadowed duplicate and the unmeasured
        // assembly are absent from both the selection and the identity.
        Assert.Equal([a, b], selected.Assemblies);
        Assert.Equal(["A.dll", "B.dll"], selected.Identities);
        Assert.Equal(AuthoredCorpusRatchet.PoolDigest(selected.Assemblies), selected.Sha256);

        // The reviewer's exploit: swapping which duplicate comes first changes the
        // measurement, so it must change the identity.
        var swapped = AuthoredCorpusRatchet.SelectPool([shadowedA, a, b, ignored], Identify);

        Assert.Equal([shadowedA, b], swapped.Assemblies);
        Assert.NotEqual(selected.Sha256, swapped.Sha256);
    }

    /// <summary>
    /// A run that selects nothing identifies no pool. The absent digest is what the
    /// caller keys its integrity failure off; inventing one here would give a run that
    /// measured nothing an identity it could be compared under.
    /// </summary>
    [Fact]
    public void SelectPool_IdentifiesNoPoolWhenItSelectedNothing()
    {
        var selected = AuthoredCorpusRatchet.SelectPool(["irrelevant.dll"], _ => null);

        Assert.Empty(selected.Assemblies);
        Assert.Null(selected.Sha256);
    }

    /// <summary>A run that measured nothing identifies no pool, and says so.</summary>
    [Fact]
    public void PoolDigest_RefusesARunThatMeasuredNoAssemblies()
        => Assert.Throws<InvalidOperationException>(() => AuthoredCorpusRatchet.PoolDigest([]));

    /// <summary>
    /// The benchmark's own wiring, not just the seam it is supposed to use. Everything
    /// above tests <see cref="AuthoredCorpusRatchet"/>, and a reviewer's challenge —
    /// find a semantic change no test catches — was answered by reverting the call site
    /// to digest the supplied assemblies again. That passed the whole suite, because
    /// <c>AuthoredCorpusBenchmark</c> was not linked into this project. It is now, so
    /// the property is gated where it actually lives.
    ///
    /// <para>The exploit reproduced: two byte-distinct copies of one assembly, which
    /// therefore share an identity. Evaluation measures whichever comes first, so the
    /// two orders must not report the same pool.</para>
    /// </summary>
    [Fact]
    public void Benchmark_IdentifiesThePoolItMeasured_NotThePoolItWasHanded()
    {
        using var pool = new TempPool();
        string original = typeof(AuthoredCorpusRatchetTests).Assembly.Location;
        string identity = AuthoredSourceHarvest.ReadAssemblyIdentity(original).Name;
        byte[] bytes = File.ReadAllBytes(original);

        string first = pool.Write("first", "Copy.dll", bytes);
        string second = pool.Write("second", "Copy.dll", [.. bytes, 0, 0, 0, 0]);
        string corpus = pool.Write("corpus", "corpus.jsonl", Encoding.UTF8.GetBytes(
            $$"""{"assembly":"{{identity}}","assemblyVersion":"1.0.0.0","tfm":"release","type":"T","method":"M","overload":0,"signature":"`0()","metadataToken":1,"parameterNames":[],"source":"class T { }"}"""));

        string firstOrder = PoolIdentityOf([first, second], corpus);
        string secondOrder = PoolIdentityOf([second, first], corpus);

        Assert.Equal(AuthoredCorpusRatchet.PoolDigest([first]), firstOrder);
        Assert.Equal(AuthoredCorpusRatchet.PoolDigest([second]), secondOrder);
        Assert.NotEqual(firstOrder, secondOrder);
    }

    /// <summary>
    /// An erased row is a dropped row, not an absent one. A reviewer replaced one corpus
    /// row with whitespace and the run reported one fewer row, zero malformed, and
    /// <c>inputsComplete: true</c> — the denominator quietly shortened, which is exactly
    /// the shape of defect the integrity half of the exit contract exists to catch.
    ///
    /// <para>This runs the real benchmark rather than <c>ReadCorpus</c> alone, because
    /// the reviewer's other point was that the wiring was ungated: they changed
    /// <c>InputsComplete</c> to ignore <c>malformedRows</c> and the whole suite still
    /// passed.</para>
    /// </summary>
    [Fact]
    public void Benchmark_CountsAnErasedCorpusRowAsMalformed()
    {
        using var pool = new TempPool();
        string original = typeof(AuthoredCorpusRatchetTests).Assembly.Location;
        string identity = AuthoredSourceHarvest.ReadAssemblyIdentity(original).Name;
        string assembly = pool.Write("only", "Copy.dll", File.ReadAllBytes(original));

        string row = $$"""{"assembly":"{{identity}}","assemblyVersion":"1.0.0.0","tfm":"release","type":"T","method":"M","overload":0,"signature":"`0()","metadataToken":1,"parameterNames":[],"source":"class T { }"}""";
        string intact = pool.Write("intact", "corpus.jsonl", Encoding.UTF8.GetBytes($"{row}\n{row}\n"));
        string erased = pool.Write("erased", "corpus.jsonl", Encoding.UTF8.GetBytes($"{row}\n   \n"));

        using var sound = JsonDocument.Parse(RunForJson([assembly], intact));
        using var shortened = JsonDocument.Parse(RunForJson([assembly], erased));

        Assert.Equal(0, sound.RootElement.GetProperty("malformedRows").GetInt32());
        Assert.True(sound.RootElement.GetProperty("inputsComplete").GetBoolean());

        Assert.Equal(1, shortened.RootElement.GetProperty("malformedRows").GetInt32());
        Assert.False(shortened.RootElement.GetProperty("inputsComplete").GetBoolean());
    }

    /// <summary>
    /// The exit code, end to end, on the same erased corpus. A shortened denominator has
    /// to fail even under <c>--integrity-only</c>, which declines to judge quality but
    /// never declines to judge whether the measurement happened.
    /// </summary>
    [Fact]
    public void Benchmark_FailsIntegrityOnAnErasedCorpusRow()
    {
        using var pool = new TempPool();
        string original = typeof(AuthoredCorpusRatchetTests).Assembly.Location;
        string identity = AuthoredSourceHarvest.ReadAssemblyIdentity(original).Name;
        string assembly = pool.Write("only", "Copy.dll", File.ReadAllBytes(original));

        string row = $$"""{"assembly":"{{identity}}","assemblyVersion":"1.0.0.0","tfm":"release","type":"T","method":"M","overload":0,"signature":"`0()","metadataToken":1,"parameterNames":[],"source":"class T { }"}""";
        string erased = pool.Write("erased", "corpus.jsonl", Encoding.UTF8.GetBytes($"{row}\n\n"));

        int exit = AuthoredCorpusBenchmark.Run(
            [assembly], erased, json: false, integrityOnly: true, output: new StringWriter());

        Assert.Equal(1, exit);
    }

    /// <summary>Runs the benchmark for its JSON and reports the pool it identified.</summary>
    static string PoolIdentityOf(string[] assemblies, string corpusPath)
    {
        using var report = JsonDocument.Parse(RunForJson(assemblies, corpusPath));
        return report.RootElement.GetProperty("poolSha256").GetString()!;
    }

    /// <summary>
    /// Runs the real benchmark and returns the JSON report it wrote. The writer is
    /// passed in rather than swapped onto <see cref="Console"/>: process-global console
    /// state is shared with every other test class, and under xunit's parallel runner
    /// (two threads here) capturing it made these assertions intermittently read another
    /// class's output.
    /// </summary>
    static string RunForJson(string[] assemblies, string corpusPath)
    {
        var captured = new StringWriter();
        AuthoredCorpusBenchmark.Run(assemblies, corpusPath, json: true, integrityOnly: true, output: captured);
        return captured.ToString();
    }

    sealed class TempPool : IDisposable
    {
        readonly string root = Directory.CreateTempSubdirectory("ratchet-pool").FullName;

        public string Write(string folder, string name, byte[] content)
        {
            string directory = Path.Combine(root, folder);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name);
            File.WriteAllBytes(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(root, recursive: true);
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
    /// productBodyDefect is absent on rows predating the invalid breakdown, which are
    /// also the rows predating the methodology stamp. Absent means not measured, so it
    /// drops out rather than reading as zero — which would report every later run as a
    /// massive regression. A row that claims the *current* methodology and still omits
    /// the breakdown is malformed, not historical, and is refused instead
    /// (see <see cref="Ratchet_BaselineMissingAMetricTheRunHasIsNotComparable"/>).
    /// </summary>
    [Fact]
    public void Ratchet_UnrecordedProductDefectsAreOmittedNotTreatedAsZero()
    {
        var comparison = AuthoredCorpusRatchet.Compare(
            Key(sha: null, corpusSha: null),
            Metrics(productBodyDefect: 326, identified: false),
            [Row(productBodyDefect: null, methodology: null, sha: null, corpusSha: null)]);

        Assert.False(comparison.Skipped, comparison.SkipReason);
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
            [
                Row(date: "2026-07-24", evaluated: 11000, validDifferent: 4226, correct: 1539, invalid: 5235),
                Row(date: "2026-07-26"),
            ]);

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
        Assert.Equal(1, AuthoredCorpusExitContract.ExitCode(sound, invalid: 0, clean, Contract(clean)));
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
        Assert.True(AuthoredCorpusExitContract.QualityHeld(invalid: 5198, skipped, Contract(skipped)));
        Assert.False(sound);
        Assert.Equal(1, AuthoredCorpusExitContract.ExitCode(sound, invalid: 5198, skipped, Contract(skipped)));
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

        Assert.True(AuthoredCorpusExitContract.QualityHeld(invalid: 5198, skipped, Contract(skipped)));
        Assert.False(AuthoredCorpusExitContract.RatchetReachedAVerdict(skipped));
        Assert.Equal(1, AuthoredCorpusExitContract.ExitCode(measurementIsSound: true, invalid: 5198, skipped, Contract(skipped)));
        Assert.Equal(1, AuthoredCorpusExitContract.ExitCode(measurementIsSound: false, invalid: 0, skipped, Contract(skipped)));
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
        Assert.Equal(0, AuthoredCorpusExitContract.ExitCode(measurementIsSound: true, invalid: 5198, held, Contract(held)));
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

        Assert.Equal(0, AuthoredCorpusExitContract.ExitCode(measurementIsSound: true, invalid: 5198, held, Contract(held)));
    }

    [Fact]
    public void ExitContract_RegressionFailsEvenWhenMeasurementIsSound()
    {
        var regressed = AuthoredCorpusRatchet.Compare(Key(), Metrics(correct: 800), [Row()]);

        Assert.NotEmpty(regressed.Regressions);
        Assert.Equal(1, AuthoredCorpusExitContract.ExitCode(measurementIsSound: true, invalid: 5198, regressed, Contract(regressed)));
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
        Assert.Equal(expected, AuthoredCorpusExitContract.ExitCode(measurementIsSound: true, invalid, ratchet: null, Contract(null)));
    }

    /// <summary>
    /// The three quality contracts are selected in one place, and "no baseline" is not
    /// a way to spell "do not judge quality". A reviewer found the weekly lane had been
    /// wired by simply dropping <c>--ratchet-baseline</c>, which silently selected
    /// <see cref="AuthoredCorpusExitContract.QualityContract.Perfection"/> — a contract
    /// this ~5,200-invalid corpus cannot meet — so the job would have failed every week
    /// forever and filed a scheduled-failure issue each time.
    /// </summary>
    [Fact]
    public void ExitContract_OmittingABaselineSelectsPerfection_NotSilence()
    {
        var held = AuthoredCorpusRatchet.Compare(Key(), Metrics(), [Row()]);

        Assert.Equal(
            AuthoredCorpusExitContract.QualityContract.Perfection,
            AuthoredCorpusExitContract.ContractFor(integrityOnly: false, ratchet: null));
        Assert.Equal(
            AuthoredCorpusExitContract.QualityContract.Ratchet,
            AuthoredCorpusExitContract.ContractFor(integrityOnly: false, held));
        Assert.Equal(
            AuthoredCorpusExitContract.QualityContract.NotJudged,
            AuthoredCorpusExitContract.ContractFor(integrityOnly: true, ratchet: null));
    }

    /// <summary>
    /// What the weekly lane actually asks for: a sound measurement of a corpus with
    /// thousands of invalid rows exits 0, because the lane makes no quality claim at
    /// all. This is the case that is red by construction under every other contract.
    /// </summary>
    [Fact]
    public void ExitContract_IntegrityOnlyPassesOnASoundRunOfAnImperfectCorpus()
    {
        Assert.Equal(1, AuthoredCorpusExitContract.ExitCode(
            measurementIsSound: true, invalid: 5198, ratchet: null,
            AuthoredCorpusExitContract.QualityContract.Perfection));

        Assert.Equal(0, AuthoredCorpusExitContract.ExitCode(
            measurementIsSound: true, invalid: 5198, ratchet: null,
            AuthoredCorpusExitContract.QualityContract.NotJudged));
    }

    /// <summary>
    /// Declining to judge quality is not a way to launder an untrustworthy run. The
    /// integrity half is the whole of what an integrity-only lane claims, so it must
    /// still be able to fail — otherwise the lane is the permanently green gate this
    /// PR exists to remove, one level down.
    /// </summary>
    [Fact]
    public void ExitContract_IntegrityOnlyStillFailsAnUnsoundRun()
    {
        Assert.Equal(1, AuthoredCorpusExitContract.ExitCode(
            measurementIsSound: false, invalid: 0, ratchet: null,
            AuthoredCorpusExitContract.QualityContract.NotJudged));
    }

    /// <summary>
    /// The ratchet contract cannot be selected without a comparison to judge with.
    /// Silently treating that as a pass is how a mis-wired caller would get a green
    /// gate it never earned, so it throws instead.
    /// </summary>
    [Fact]
    public void ExitContract_RatchetContractWithoutAComparisonIsARefusal()
    {
        Assert.Throws<ArgumentException>(() => AuthoredCorpusExitContract.QualityHeld(
            invalid: 0, ratchet: null, AuthoredCorpusExitContract.QualityContract.Ratchet));
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
        var tracked = AuthoredCorpusHistoryCardTests.TrackedHistory();
        var comparison = AuthoredCorpusRatchet.CompareNewestRow(tracked);
        var newest = tracked[^1];
        var baseline = tracked[^2];

        Assert.False(comparison.Skipped, comparison.SkipReason);
        Assert.Empty(comparison.Regressions);

        // Naming the metrics, rather than asserting the list is merely non-empty, is
        // what keeps this gate honest: a metric is compared only when both rows state
        // it, so one that quietly drops out leaves a green comparison that ratchets
        // less than it appears to — which is how a reviewer shed a productBodyDefect
        // regression by bumping methodologyVersion. A methodology change is the one
        // legitimate reason for the drop (the metric's meaning changed), so pin that
        // reason: any other cause fails here.
        string[] expected = newest.Methodology == baseline.Methodology
            ? ["valid", "correct", "invalid", "productBodyDefect"]
            : ["valid", "correct", "invalid"];

        // Today the store's baseline predates methodology stamping, so this takes the
        // second branch and productBodyDefect is not yet ratcheted — a real gap. It
        // closes on the append after next, not the next one: see
        // TrackedHistory_RecordsNoRunIdentity_SoTheNextAppendCannotRatchet. Nothing can
        // re-open it once closed, because
        // TrackedHistory_NewestRowStatesTheMethodologyTheCodeProduces refuses an
        // unstamped newest row.
        Assert.Equal(expected, comparison.Metrics.Select(metric => metric.Name));
    }

    /// <summary>
    /// A mode only preempts a gate that was actually requested.
    ///
    /// <para>This is the conjunction, and it is tested here rather than left at the call
    /// site because leaving it there produced a regression worse than the bug it fixed:
    /// every mode in the harness refused, since each one preempted a gate nobody had
    /// asked for. <c>--history-card</c> alone, and even
    /// <c>--benchmark-authored-corpus</c> alone (preempting the unrequested verify
    /// gate), exited 1. The scheduled lane could not have run.</para>
    ///
    /// <para>It survived my own verification because I checked exit codes and not
    /// messages: the harness exited 1, which is what a missing corpus also does. That is
    /// this PR's own subject matter — an exit code that reads the same for two different
    /// reasons — so the rule and its conjunction now live together where a test sees
    /// both.</para>
    /// </summary>
    [Theory]
    // The selected flags, then the refusal expected (null = proceed).
    [InlineData("--history-card", null)]
    [InlineData("--not-my-type", null)]
    [InlineData("--benchmark-authored-corpus", null)]
    [InlineData("--verify-authored-corpus", null)]
    [InlineData("--history-card,--benchmark-authored-corpus", "--history-card")]
    [InlineData("--history-card,--verify-authored-corpus", "--history-card")]
    [InlineData("--not-my-type,--verify-authored-corpus", "--not-my-type")]
    [InlineData("--benchmark-authored-corpus,--verify-authored-corpus", "--benchmark-authored-corpus")]
    public void PreemptedGateRefusal_AppliesOnlyToARequestedGate(string selected, string? expectedPreempting)
    {
        var order = DispatchOrder(selected.Split(','));

        string? refusal = AuthoredCorpusExitContract.PreemptedGateRefusal(order, Gates);

        if (expectedPreempting is null)
        {
            Assert.Null(refusal);
        }
        else
        {
            Assert.NotNull(refusal);
            Assert.StartsWith(expectedPreempting + " runs instead of", refusal, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// No mode selected on its own is refused.
    ///
    /// <para>Enumerated over every flag in the dispatch order rather than sampled. The
    /// regression this guards refused <em>all</em> of them, and a sampled test that
    /// happened to pick a gate would have missed which ones.</para>
    /// </summary>
    [Fact]
    public void PreemptedGateRefusal_RefusesNoModeOnItsOwn()
    {
        var refused = new List<string>();

        foreach (var (flag, _) in DispatchOrder("--history-card"))
        {
            if (AuthoredCorpusExitContract.PreemptedGateRefusal(DispatchOrder([flag]), Gates) is not null)
                refused.Add(flag);
        }

        Assert.Empty(refused);
    }

    /// <summary>
    /// The scheduled lane's own flag combination proceeds.
    ///
    /// <para>`deep-inspect.yml` invokes `--benchmark-authored-corpus &lt;corpus&gt;
    /// --integrity-only`. The regression above refused exactly that, so the lane this PR
    /// adds could never have run. Naming the caller's combination is what turns "the
    /// rule is correct" into "the caller works".</para>
    /// </summary>
    [Fact]
    public void PreemptedGateRefusal_LetsTheScheduledLaneRun()
    {
        var order = DispatchOrder(["--benchmark-authored-corpus"]);

        Assert.Null(AuthoredCorpusExitContract.PreemptedGateRefusal(order, Gates));
    }

    /// <summary>The gates the harness protects, in the order it names them.</summary>
    static readonly string[] Gates = ["--benchmark-authored-corpus", "--verify-authored-corpus"];

    /// <summary>
    /// A mode earlier in the dispatch order preempts the gate; one later does not.
    ///
    /// <para>Preemption means the gate does not run <em>at all</em> and the process exits
    /// 0 having measured nothing — the permanently-green failure of #3245 one level up.
    /// Both authored-corpus gates now derive their refusal from a single ordered list, so
    /// the prefix rule is the whole rule and is worth pinning on its own.</para>
    /// </summary>
    [Theory]
    // The selected mode, the gate, and the flag expected to preempt it (null = reached).
    [InlineData("--history-card", "--benchmark-authored-corpus", "--history-card")]
    [InlineData("--history-card", "--verify-authored-corpus", "--history-card")]
    [InlineData("--benchmark-authored-corpus", "--verify-authored-corpus", "--benchmark-authored-corpus")]
    [InlineData("--verify-authored-corpus", "--benchmark-authored-corpus", null)]
    [InlineData("--benchmark-authored-corpus", "--benchmark-authored-corpus", null)]
    [InlineData("--not-my-type", "--benchmark-authored-corpus", "--not-my-type")]
    public void PreemptingMode_IsTheFirstSelectedModeAheadOfTheGate(
        string selected, string gate, string? expected)
    {
        (string Flag, bool Selected)[] order = DispatchOrder(selected);

        Assert.Equal(expected, AuthoredCorpusExitContract.FindPreemptingMode(order, gate));
    }

    /// <summary>
    /// The earliest selected mode wins, not merely some selected mode: the refusal names
    /// what actually runs, so a caller is not sent to investigate a flag that would never
    /// have executed.
    /// </summary>
    [Fact]
    public void PreemptingMode_NamesTheEarliestSelectedMode()
    {
        (string Flag, bool Selected)[] order =
        [
            ("--history-card", true),
            ("--not-my-type", true),
            ("--benchmark-authored-corpus", true),
        ];

        Assert.Equal(
            "--history-card",
            AuthoredCorpusExitContract.FindPreemptingMode(order, "--benchmark-authored-corpus"));
    }

    /// <summary>
    /// A gate missing from the dispatch order is an error, not "unpreemptable".
    ///
    /// <para>Returning <see langword="null"/> for an absent gate would read as "nothing
    /// runs ahead of it" — a green answer produced by the gate having silently dropped
    /// out of the list, which is the exact false green this rule exists to prevent.</para>
    /// </summary>
    [Fact]
    public void PreemptingMode_RefusesAGateThatIsNotInTheDispatchOrder()
    {
        (string Flag, bool Selected)[] order = [("--history-card", false)];

        Assert.Throws<ArgumentException>(
            () => AuthoredCorpusExitContract.FindPreemptingMode(order, "--benchmark-authored-corpus"));
    }

    /// <summary>Both authored-corpus gates sit in the dispatch order, so both are gated.</summary>
    [Theory]
    [InlineData("--benchmark-authored-corpus")]
    [InlineData("--verify-authored-corpus")]
    public void PreemptingMode_CoversBothAuthoredCorpusGates(string gate)
    {
        (string Flag, bool Selected)[] order = DispatchOrder(selected: "--history-card");

        Assert.Equal("--history-card", AuthoredCorpusExitContract.FindPreemptingMode(order, gate));
    }

    /// <summary>
    /// The test's copy of the dispatch order is the harness's copy.
    ///
    /// <para>Restating a list the product owns is how four flags went missing from the
    /// refusal in an earlier round, so a copy that can drift is not acceptable even in a
    /// test. Pinning it against the source means a mode added to the harness fails here
    /// until the fixture is updated, rather than silently narrowing what these tests
    /// prove.</para>
    /// </summary>
    [Fact]
    public void DispatchOrder_MatchesTheHarnessDispatchOrder()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "tools", "DecompilerHarness", "Program.cs"));

        int start = source.IndexOf("dispatchOrder =", StringComparison.Ordinal);
        Assert.True(start >= 0, "Program.cs no longer declares dispatchOrder.");
        int open = source.IndexOf('[', start);
        int close = source.IndexOf("];", open, StringComparison.Ordinal);

        string[] declared = [.. source[open..close]
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("(\"", StringComparison.Ordinal))
            .Select(line => line[2..line.IndexOf('"', 2)])];

        Assert.Equal(DispatchOrder("--history-card").Select(entry => entry.Flag), declared);
    }

    /// <summary>The harness's dispatch order, with exactly one mode selected.</summary>
    static (string Flag, bool Selected)[] DispatchOrder(string selected) => DispatchOrder([selected]);

    /// <summary>The harness's dispatch order, with the named modes selected.</summary>
    static (string Flag, bool Selected)[] DispatchOrder(string[] selected)
    {
        string[] flags =
        [
            "--fixture-source-inventory",
            "--history-card",
            "--generated-fixtures",
            "--fuzz-signatures",
            "--return-to-sender-catalog",
            "--emit-inverse-ledger",
            "--assertion-scan",
            "--validity-check",
            "--validity-predicate-scan",
            "--fidelity-check",
            "--return-to-sender",
            "--return-address",
            "--not-my-type",
            "--enumerate-real-methods",
            "--harvest-authored-corpus",
            "--harvest-evil-corpus",
            "--benchmark-authored-corpus",
            "--verify-authored-corpus",
        ];

        foreach (string flag in selected)
            Assert.Contains(flag, flags);

        return [.. flags.Select(flag => (flag, selected.Contains(flag, StringComparer.Ordinal)))];
    }

    /// <summary>
    /// The whole report — including the ratchet outcome and the contract verdict —
    /// reaches the writer the caller passed.
    ///
    /// <para>Threading a <see cref="TextWriter"/> through the benchmark was itself a fix,
    /// for tests that raced by swapping process-global <c>Console.Out</c> under a
    /// two-thread runner. It threaded the writer through the top of the report and left
    /// the last two lines — the ratchet report and the contract verdict — writing to
    /// <c>Console.Out</c>. Those are the two lines that say what the run <em>concluded</em>,
    /// so the capture was missing precisely the part worth asserting on, and still
    /// raced.</para>
    ///
    /// <para>Asserting on the verdict line, rather than merely that something was
    /// written, is what makes this catch that shape: the top of the report was captured
    /// correctly the whole time.</para>
    /// </summary>
    [Fact]
    public void Benchmark_WritesTheContractVerdictToTheCallersWriter()
    {
        using var pool = new TempPool();
        string original = typeof(AuthoredCorpusRatchetTests).Assembly.Location;
        string identity = AuthoredSourceHarvest.ReadAssemblyIdentity(original).Name;
        string assembly = pool.Write("only", "Copy.dll", File.ReadAllBytes(original));
        string corpus = pool.Write("corpus", "corpus.jsonl", Encoding.UTF8.GetBytes(
            $$"""{"assembly":"{{identity}}","assemblyVersion":"1.0.0.0","tfm":"release","type":"T","method":"M","overload":0,"signature":"`0()","metadataToken":1,"parameterNames":[],"source":"class T { }"}"""));

        var captured = new StringWriter();
        AuthoredCorpusBenchmark.Run([assembly], corpus, json: false, integrityOnly: true, output: captured);

        Assert.Contains("[integrity-only]", captured.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>AuthoredCorpusBenchmark</c> names <c>Console.Out</c> exactly once, where it
    /// defaults the caller's writer.
    ///
    /// <para>This is a source pin rather than a behavioural assertion because the defect
    /// it guards is an <em>omission</em>: a report line added later that writes to the
    /// global console instead of the injected writer. A behavioural test only catches
    /// the lines it happens to assert on, which is exactly how two such lines survived
    /// the round-five fix that introduced the writer. Any new global write fails here
    /// whether or not anyone thought to assert on it.</para>
    ///
    /// <para>If this fails, do not raise the count — route the new write through the
    /// <c>output</c> parameter. <c>Console.Error</c> is deliberately not pinned: the
    /// side channel stays on stderr in both modes so <c>--json</c> emits parseable JSON
    /// on stdout.</para>
    /// </summary>
    [Fact]
    public void Benchmark_WritesToTheGlobalConsoleOnlyWhereItDefaultsTheWriter()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "tools", "DecompilerHarness", "AuthoredCorpusBenchmark.cs"));

        var mentions = source
            .Split('\n')
            .Where(line => line.Contains("Console.Out", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .ToArray();

        Assert.Equal(["output ??= Console.Out;"], mentions);
    }

    static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing dotnet-inspect.slnx.");
    }

    /// <summary>
    /// Every combination of <c>--help</c> and the three gate flags, enumerated.
    ///
    /// <para>A gate flag that is silently ignored is a permanently green gate, which is
    /// exactly the failure this whole change exists to remove (#3245). <c>--help</c>
    /// answering before flag validation made every gate flag ignorable, and it was fixed
    /// one flag at a time: first the two ratchet options, then
    /// <c>--benchmark-authored-corpus</c> itself, which a reviewer found still exiting 0
    /// on a nonexistent corpus. Each fix was verified by hand against the real binary and
    /// then had no gate, so reverting either one left the suite green.</para>
    ///
    /// <para>Sixteen rows is the cheapest way to stop finding these one at a time. The
    /// only row that may print usage is the one that asks for nothing else; the only rows
    /// that may proceed are those with no <c>--help</c> and no dangling or contradictory
    /// gate flag.</para>
    /// </summary>
    [Theory]
    // benchmark, verify, baseline, integrityOnly, help -> disposition
    [InlineData(false, false, false, false, false, "Proceed")]
    [InlineData(true, false, false, false, false, "Proceed")]
    [InlineData(false, true, false, false, false, "Proceed")]
    [InlineData(true, false, true, false, false, "Proceed")]
    [InlineData(true, false, false, true, false, "Proceed")]
    [InlineData(false, true, false, true, false, "Refuse")]
    [InlineData(false, false, true, false, false, "Refuse")]
    [InlineData(false, false, false, true, false, "Refuse")]
    [InlineData(false, true, true, false, false, "Refuse")]
    [InlineData(false, false, true, true, false, "Refuse")]
    [InlineData(true, false, true, true, false, "Refuse")]
    [InlineData(false, false, false, false, true, "PrintUsage")]
    [InlineData(true, false, false, false, true, "Refuse")]
    [InlineData(false, true, false, false, true, "Refuse")]
    [InlineData(true, true, false, false, true, "Refuse")]
    [InlineData(false, false, true, false, true, "Refuse")]
    [InlineData(true, false, true, false, true, "Refuse")]
    [InlineData(false, false, false, true, true, "Refuse")]
    [InlineData(true, false, false, true, true, "Refuse")]
    [InlineData(false, true, false, true, true, "Refuse")]
    [InlineData(false, false, true, true, true, "Refuse")]
    [InlineData(true, true, true, true, true, "Refuse")]
    public void GateFlags_AreJudgedBeforeAnyModeDispatches(
        bool benchmark, bool verify, bool baseline, bool integrityOnly, bool help, string expected)
    {
        var verdict = AuthoredCorpusExitContract.JudgeGateFlags(help, benchmark, verify, baseline, integrityOnly);

        Assert.Equal(expected, verdict.Disposition.ToString());

        // A refusal that says nothing is a refusal a caller cannot act on.
        if (verdict.Disposition == AuthoredCorpusExitContract.FlagDisposition.Refuse)
            Assert.False(string.IsNullOrWhiteSpace(verdict.Message));
        else
            Assert.Null(verdict.Message);
    }

    /// <summary>
    /// Usage is printed for exactly one input: <c>--help</c> with no gate flag at all.
    ///
    /// <para>Enumerated exhaustively over all thirty-two combinations rather than
    /// sampled, because the defect this guards has now appeared three times, each time
    /// one flag over from the last fix — the two ratchet options, then
    /// <c>--benchmark-authored-corpus</c>, then <c>--verify-authored-corpus</c>. Sampling
    /// is what let it keep moving.</para>
    /// </summary>
    [Fact]
    public void GateFlags_PrintUsageForExactlyOneCombination()
    {
        var printsUsage = new List<string>();

        for (int bits = 0; bits < 32; bits++)
        {
            bool help = (bits & 1) != 0;
            bool benchmark = (bits & 2) != 0;
            bool verify = (bits & 4) != 0;
            bool baseline = (bits & 8) != 0;
            bool integrityOnly = (bits & 16) != 0;

            var verdict = AuthoredCorpusExitContract.JudgeGateFlags(help, benchmark, verify, baseline, integrityOnly);
            if (verdict.Disposition == AuthoredCorpusExitContract.FlagDisposition.PrintUsage)
                printsUsage.Add($"help={help} benchmark={benchmark} verify={verify} baseline={baseline} integrityOnly={integrityOnly}");
        }

        Assert.Equal(
            ["help=True benchmark=False verify=False baseline=False integrityOnly=False"],
            printsUsage);
    }

    /// <summary>
    /// The one row above that carries the whole point: asking for the gate and for usage
    /// at once must not print usage. Named separately because it is the regression a
    /// reviewer actually found, and a named test says so in the failure output.
    /// </summary>
    [Fact]
    public void GateFlags_HelpDoesNotPreemptTheAuthoredCorpusGate()
    {
        foreach (var (benchmark, verify) in new[] { (true, false), (false, true) })
        {
            var verdict = AuthoredCorpusExitContract.JudgeGateFlags(
                showHelp: true,
                benchmarkAuthoredCorpus: benchmark,
                verifyAuthoredCorpus: verify,
                ratchetBaselineSupplied: false,
                integrityOnly: false);

            Assert.Equal(AuthoredCorpusExitContract.FlagDisposition.Refuse, verdict.Disposition);
        }
    }

    /// <summary>
    /// No row in the tracked store records run identity, so the next append — which
    /// will come from a live run, and a live run always records both digests — is
    /// <em>not</em> comparable to the row it lands on, and
    /// <see cref="TrackedHistory_NewestRowDoesNotRegressAgainstItsBaseline"/> will fail
    /// with a skip. That is the ratchet working, not breaking: it refuses to certify a
    /// comparison it cannot make. But an appender deserves to learn it from a test that
    /// names the remedy rather than from a red merge lane, so this pins the gap.
    ///
    /// <para><b>If this test fails, the store has crossed the bootstrap</b> — delete it
    /// and delete this paragraph. Until then, the first identified append is expected to
    /// be red, and the appender must land it together with a second identified run over
    /// the same pool and corpus (the two together form the first ratchetable pair). The
    /// historical rows cannot be back-filled: their pools and corpora were archived
    /// out-of-tree and the artifacts are gone.</para>
    ///
    /// <para>The obvious cheaper fix — let a baseline that records no identity compare
    /// against anything — is unsound and was rejected twice. <c>--ratchet-baseline</c>
    /// reads a caller-supplied file, so that rule would let any baseline opt out of
    /// identity and then compare clean against a run over a wholly different corpus.
    /// <see cref="AuthoredCorpusRatchet.RunKey.IsComparableTo"/> therefore compares both
    /// digests symmetrically, absence included.</para>
    ///
    /// <para>Tracked as #3362.</para>
    /// </summary>
    [Fact]
    public void TrackedHistory_RecordsNoRunIdentity_SoTheNextAppendCannotRatchet()
    {
        var tracked = AuthoredCorpusHistoryCardTests.TrackedHistory();

        Assert.All(tracked, row =>
        {
            Assert.Null(row.PoolSha256);
            Assert.Null(row.CorpusSha256);
        });

        // And name the consequence, so this test fails if the bootstrap is crossed by
        // some route that leaves the rows unidentified but the comparison sound.
        var live = new AuthoredCorpusRatchet.RunKey(
            Evaluated: tracked[^1].Evaluated,
            PoolMatched: tracked[^1].PoolMatched,
            PoolTotal: tracked[^1].PoolTotal,
            PoolSha256: new string('a', 64),
            CorpusSha256: new string('b', 64));

        Assert.False(
            live.IsComparableTo(AuthoredCorpusRatchet.RunKey.From(tracked[^1]), out string mismatch));
        Assert.Contains("(none recorded)", mismatch, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tracked store's rows are copied verbatim from a run, and a run stamps the
    /// methodology constant the code was built with (<c>AuthoredCorpusBenchmark</c>'s
    /// constant is an alias for <see cref="SpanAttribution.MethodologyVersion"/>, which
    /// owns the rule) — so a row may not claim a methodology the code never produced,
    /// and may not go unstamped.
    ///
    /// <para>Without this, the ratchet's one legitimate reason to drop a metric becomes
    /// an escape hatch: a reviewer regressed <c>productBodyDefect</c> by 1,000 and hid
    /// it simply by writing a higher <c>methodologyVersion</c> into the appended row.
    /// The comparison then read that as a methodology bump, shed the metric, and
    /// reported no regressions.</para>
    /// </summary>
    [Fact]
    public void TrackedHistory_NewestRowStatesTheMethodologyTheCodeProduces()
    {
        var newest = AuthoredCorpusHistoryCardTests.TrackedHistory()[^1];

        Assert.Equal(SpanAttribution.MethodologyVersion, newest.Methodology);
    }

    /// <summary>
    /// The same escape hatch, closed from the product side for live runs. A row that
    /// states any <c>methodologyVersion</c> came from a run that measured
    /// <c>productBodyDefect</c>, so omitting the metric makes it malformed rather than
    /// historical — whichever version it claims, above, at, or below the run's.
    ///
    /// <para>Two reviewers reached this from opposite directions: one shed the metric
    /// by claiming a <em>newer</em> methodology, the other by claiming an arbitrary
    /// 999. Both worked because the rule compared methodology <em>values</em>. Only
    /// rows recorded before the metric existed may omit it, and those state no version
    /// at all, so the rule is now structural.</para>
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(999)]
    public void Ratchet_BaselineCannotShedAMetricByClaimingAnyMethodology(int claimed)
    {
        var comparison = AuthoredCorpusRatchet.Compare(
            Key(), Metrics(methodology: 2), [Row(productBodyDefect: null, methodology: claimed)]);

        Assert.True(comparison.Skipped);
        Assert.Contains("invalidBreakdown", comparison.SkipReason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same structural rule, applied to the row being judged rather than the one it
    /// is judged against. Guarding only the baseline left the shorter path open: a
    /// reviewer dropped <c>invalidBreakdown</c> from the appended row itself, kept its
    /// <c>methodologyVersion</c>, and the tracked-store gate passed green with the
    /// product metric silently unratcheted.
    ///
    /// <para><c>Build</c> emits a metric only when both sides state it, so an omission
    /// on <em>either</em> side shrinks the comparison while it still prints
    /// <c>RATCHET OK</c>. This is what makes the "self-healing" claim about the store's
    /// unstamped baseline true rather than merely hoped for: the next append cannot
    /// both stamp a methodology and decline to measure it.</para>
    /// </summary>
    [Fact]
    public void Ratchet_ARowStatingAMethodologyCannotShedTheMetricFromItsOwnSide()
    {
        var comparison = AuthoredCorpusRatchet.CompareNewestRow(
            [Row(date: "2026-07-26"), Row(date: "2026-07-27", productBodyDefect: null, methodology: 2)]);

        Assert.True(comparison.Skipped);
        Assert.Contains("invalidBreakdown", comparison.SkipReason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The trend store's <c>validDifferent</c> member carries the sub-buckets *and*
    /// their total, and the total is the number the ratchet's <c>valid</c> metric is
    /// built from. The run emitted the parts without the sum, so an author assembling a
    /// row from this output could record a total of 0 — which a reviewer read as an
    /// unratcheted 5,000-row hole.
    ///
    /// <para>It is not a hole: <c>PartitionCloses</c> rejects such a row as unsound and
    /// the gate skips loudly, exit 1. But a loud failure caused by the shape of our own
    /// output is still our defect, and it is the same shape that produced the one
    /// untrustworthy row already in the store.</para>
    /// </summary>
    [Fact]
    public void Benchmark_EmitsAValidBreakdownARowCanBeBuiltFrom()
    {
        using var pool = new TempPool();
        string original = typeof(AuthoredCorpusRatchetTests).Assembly.Location;
        string identity = AuthoredSourceHarvest.ReadAssemblyIdentity(original).Name;
        string assembly = pool.Write("only", "Copy.dll", File.ReadAllBytes(original));
        string corpus = pool.Write("corpus", "corpus.jsonl", Encoding.UTF8.GetBytes(
            $$"""{"assembly":"{{identity}}","assemblyVersion":"1.0.0.0","tfm":"release","type":"T","method":"M","overload":0,"signature":"`0()","metadataToken":1,"parameterNames":[],"source":"class T { }"}"""));

        using var report = JsonDocument.Parse(RunForJson([assembly], corpus));
        var breakdown = report.RootElement.GetProperty("validBreakdown");

        Assert.Equal(
            report.RootElement.GetProperty("validDifferent").GetInt32(),
            breakdown.GetProperty("total").GetInt32());

        // Round-trips into the row member it is copied into, total included.
        var member = JsonSerializer.Deserialize<HistoryRunValidDifferent>(
            breakdown.GetRawText(),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;

        Assert.Equal(report.RootElement.GetProperty("validDifferent").GetInt32(), member.Total);
    }

    /// <summary>
    /// The one row that may omit the metric: recorded before it existed, and stating no
    /// methodology to prove it. It still ratchets the three methodology-independent
    /// metrics, which is the whole reason the omission is tolerated rather than fatal.
    /// </summary>
    [Fact]
    public void Ratchet_ARowPredatingTheMetricStillRatchetsTheRest()
    {
        // Grandfathered on both counts: no methodology stamp and no run identity. Such
        // a row is only ever comparable to another row from the same era, because a run
        // that identifies itself is not comparable to one that does not — which is why
        // the run key here is hashless too.
        var unstamped = Row(productBodyDefect: null, methodology: null, sha: null, corpusSha: null);
        var comparison = AuthoredCorpusRatchet.Compare(
            Key(sha: null, corpusSha: null),
            Metrics(methodology: 2, identified: false),
            [unstamped]);

        Assert.False(comparison.Skipped, comparison.SkipReason);
        Assert.Equal(["valid", "correct", "invalid"], comparison.Metrics.Select(metric => metric.Name));

        // And a row that identifies itself cannot claim the same grandfathering: an
        // author who deletes both the stamp and the breakdown from a fresh baseline is
        // shedding the metric, not recording history.
        var fabricated = AuthoredCorpusRatchet.Compare(
            Key(), Metrics(methodology: 2), [Row(productBodyDefect: null, methodology: null)]);

        Assert.True(fabricated.Skipped);
        Assert.Contains("invalidBreakdown", fabricated.SkipReason!, StringComparison.Ordinal);
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
        // The rows the regression sheds move into invalid, so the partition still
        // closes: this must fail for being *worse*, not for having measured less.
        runs.Add(newest with
        {
            Date = "2026-07-31",
            ValidDifferent = newest.ValidDifferent! with
            {
                Total = newest.ValidDifferent!.Total - 100,
                FrontierIlExact = newest.ValidDifferent!.FrontierIlExact - 100,
            },
            Correct = 800,
            Invalid = newest.Invalid + 100 + (newest.Correct - 800),
            InvalidBreakdown = new HistoryRunInvalidBreakdown(2328, 0, 0),
        });

        var comparison = AuthoredCorpusRatchet.CompareNewestRow(runs);

        Assert.False(comparison.Skipped);
        Assert.Equal(newest.Date, comparison.Baseline!.Date);
        Assert.Equal(
            ["valid", "correct", "invalid", "productBodyDefect"],
            comparison.Regressions.Select(metric => metric.Name));
    }

}
