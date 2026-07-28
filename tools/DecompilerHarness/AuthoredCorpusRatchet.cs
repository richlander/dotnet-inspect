using System.Globalization;
using System.Security.Cryptography;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Compares one authored-corpus run against the newest comparable row in the
/// EVIL trend store and reports the metrics that moved the wrong way.
///
/// This exists because the benchmark's own exit contract conflated two different
/// questions. <em>Measurement integrity</em> ("is this run trustworthy?") is
/// correctly a hard failure. <em>Quality level</em> ("how good is the decompiler
/// today?") is the thing under measurement, and gating it on perfection
/// (<c>invalid == 0</c>) makes the exit code a constant: it reads identically at
/// 56.7% valid and at 40% valid, so it detects no regression at all (#3245). The
/// ratchet judges quality by movement against a recorded baseline instead.
///
/// The band is <b>zero</b> — every metric ratchets strictly. Two runs of the same
/// commit against the same pinned pool were measured bit-identical on every
/// counted metric, so a tolerance band would not be absorbing instrument noise;
/// it would be the harness declining to report code-attributable movement, which
/// the harness-boundary rule forbids.
/// </summary>
static class AuthoredCorpusRatchet
{
    /// <summary>
    /// The subset of a run that decides whether two runs measure the same thing.
    /// Comparing runs that do not share these is arithmetic, not a trend.
    /// </summary>
    internal readonly record struct RunKey(
        int Evaluated,
        int PoolMatched,
        int PoolTotal,
        string? SweepManifestSha256)
    {
        public static RunKey From(HistoryRun run)
            => new(run.Evaluated, run.PoolMatched, run.PoolTotal, run.SweepManifestSha256);

        /// <summary>
        /// True when both runs measured the same corpus over the same pool.
        ///
        /// <para>The sweep-manifest hash is governed by the <em>baseline</em>: when the
        /// recorded row identifies its pool, a run that cannot identify its own is not
        /// comparable to it. The weaker "check only when both sides have one" rule was
        /// unsound. It rested on the claim that a drifted pool always surfaces as
        /// unmatched rows or unresolved identities — but a package resolving to a newer
        /// version that still carries the same method identities resolves cleanly,
        /// produces no drift, and would have been compared against numbers measured on
        /// different code. Refusing to compare is the safe direction: it is a loud skip,
        /// not a silent pass.</para>
        ///
        /// <para>Methodology deliberately is <em>not</em> part of this key. It governs
        /// how <c>productBodyDefect</c> is computed and nothing else, so folding it in
        /// here discarded three perfectly comparable metrics at every version bump —
        /// which is what made the tracked-store gate vacuous. It is applied per metric
        /// in <see cref="Build"/> instead.</para>
        /// </summary>
        public bool IsComparableTo(RunKey other, out string mismatch)
        {
            if (Evaluated != other.Evaluated)
                return Fail($"evaluated {other.Evaluated} vs {Evaluated}", out mismatch);
            if (PoolMatched != other.PoolMatched || PoolTotal != other.PoolTotal)
                return Fail($"pool {other.PoolMatched}/{other.PoolTotal} vs {PoolMatched}/{PoolTotal}", out mismatch);
            if (other.SweepManifestSha256 is { } theirs
                && !string.Equals(SweepManifestSha256, theirs, StringComparison.Ordinal))
            {
                return Fail(
                    $"sweepManifestSha256 {theirs} vs {SweepManifestSha256 ?? "(none supplied)"}",
                    out mismatch);
            }

            mismatch = "";
            return true;

            static bool Fail(string reason, out string mismatch)
            {
                mismatch = reason;
                return false;
            }
        }
    }

    /// <summary>
    /// The ratcheted quality metrics for one run.
    ///
    /// <para><see cref="Valid"/> is the exact valid-row count, not a percentage.
    /// Percentages were the first design and were wrong: the store records
    /// <c>validPct</c> to one decimal, so 6,802/12,000 and 6,801/12,000 both read as
    /// 56.7 and a genuine lost row could pass a "zero tolerance" ratchet. Since
    /// <c>evaluated</c> is equal by the comparability key, the exact count carries the
    /// same meaning with none of the rounding.</para>
    ///
    /// <para><see cref="ProductBodyDefect"/> is nullable because rows predating the
    /// invalid breakdown did not record it; absent means <em>not measured</em>, never
    /// zero. <see cref="Methodology"/> travels with it because it defines how that one
    /// number was computed.</para>
    /// </summary>
    internal sealed record RunMetrics(int? Valid, int Correct, int Invalid, int? ProductBodyDefect, int Methodology)
    {
        public static RunMetrics From(HistoryRun run)
            => new(
                run.ValidDifferent is { } validDifferent ? run.Correct + validDifferent.Total : null,
                run.Correct,
                run.Invalid,
                run.InvalidBreakdown?.ProductBodyDefect,
                run.Methodology);
    }

    /// <summary>
    /// One ratcheted metric. <see cref="HigherIsBetter"/> carries the goal direction
    /// so the comparison cannot silently invert for a lower-is-better metric.
    /// </summary>
    internal sealed record Metric(string Name, double Baseline, double Current, bool HigherIsBetter)
    {
        public bool Regressed => HigherIsBetter ? Current < Baseline : Current > Baseline;

        public string Describe()
        {
            string direction = HigherIsBetter ? ">=" : "<=";
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{Name} {Format(Baseline)} -> {Format(Current)} (want {direction} {Format(Baseline)})");
        }

        static string Format(double value)
            => value == Math.Floor(value)
                ? ((long)value).ToString(CultureInfo.InvariantCulture)
                : value.ToString("F1", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The outcome of a ratchet attempt. Exactly one of <see cref="Metrics"/> and
    /// <see cref="SkipReason"/> is populated, so a skip can never be mistaken for a
    /// clean comparison.
    /// </summary>
    internal sealed record Comparison(HistoryRun? Baseline, IReadOnlyList<Metric> Metrics, string? SkipReason)
    {
        public bool Skipped => SkipReason is not null;

        public IReadOnlyList<Metric> Regressions
            => Metrics.Where(metric => metric.Regressed).ToArray();

        public static Comparison Skip(string reason) => new(null, [], reason);
    }

    /// <summary>
    /// Compares <paramref name="current"/> against the newest row in
    /// <paramref name="baselines"/> that shares its <see cref="RunKey"/>. Returns a
    /// skip (not a pass and not a failure) when no row is comparable, naming why the
    /// newest candidate was rejected so an operator can tell "nothing to compare
    /// against" from "compared and clean".
    /// </summary>
    internal static Comparison Compare(RunKey currentKey, RunMetrics current, IReadOnlyList<HistoryRun> baselines)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(baselines);

        HistoryRun? baseline = null;
        string? newestMismatch = null;
        for (int index = baselines.Count - 1; index >= 0; index--)
        {
            if (currentKey.IsComparableTo(RunKey.From(baselines[index]), out string mismatch))
            {
                baseline = baselines[index];
                break;
            }

            newestMismatch ??= $"{baselines[index].Date ?? "(undated)"}: {mismatch}";
        }

        if (baseline is null)
        {
            return Comparison.Skip(baselines.Count == 0
                ? "baseline holds no runs"
                : $"no comparable baseline row (newest candidate {newestMismatch})");
        }

        return new Comparison(baseline, Build(RunMetrics.From(baseline), current), SkipReason: null);
    }

    /// <summary>
    /// Ratchets the newest row of a trend store against the newest earlier row it is
    /// comparable with. This is the store's own append gate: the history before it is
    /// data recorded without a ratchet, not a contract, so only the new row is judged.
    /// </summary>
    internal static Comparison CompareNewestRow(IReadOnlyList<HistoryRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        if (runs.Count < 2)
            return Comparison.Skip("store holds fewer than two runs");

        var newest = runs[^1];
        return Compare(RunKey.From(newest), RunMetrics.From(newest), runs.Take(runs.Count - 1).ToArray());
    }

    static IReadOnlyList<Metric> Build(RunMetrics baseline, RunMetrics current)
    {
        var metrics = new List<Metric>
        {
            new("correct", baseline.Correct, current.Correct, HigherIsBetter: true),
            new("invalid", baseline.Invalid, current.Invalid, HigherIsBetter: false),
        };

        // Exact counts, so a sub-0.05pp loss cannot hide behind a rounded percentage.
        if (baseline.Valid is { } baselineValid && current.Valid is { } currentValid)
            metrics.Insert(0, new("valid", baselineValid, currentValid, HigherIsBetter: true));

        // productBodyDefect is the product signal (raw invalid is ~92% harness
        // shell-reconstruction noise), but it is a *lower bound* whose meaning is
        // defined by the methodology version, so it ratchets only when both sides
        // measured it the same way. The other three metrics are methodology-
        // independent and keep ratcheting across a version bump.
        if (baseline.ProductBodyDefect is { } baselineDefects
            && current.ProductBodyDefect is { } currentDefects
            && baseline.Methodology == current.Methodology)
        {
            metrics.Add(new("productBodyDefect", baselineDefects, currentDefects, HigherIsBetter: false));
        }

        return metrics;
    }

    /// <summary>
    /// The recorded form of a pool's identity: the first 8 bytes of the sweep
    /// manifest's SHA-256, lowercase hex. This is the definition going forward. The
    /// store's existing hashes were recorded by hand, so if any was produced a
    /// different way it will not match — and the result is a loud skip, never a false
    /// pass, which is the direction this must fail in.
    /// </summary>
    internal static string PoolManifestDigest(string manifestPath)
    {
        using var stream = File.OpenRead(manifestPath);
        return Convert.ToHexStringLower(SHA256.HashData(stream).AsSpan(0, 8));
    }

    /// <summary>
    /// Renders the ratchet verdict. A skip is printed as loudly as a failure, because
    /// a gate that quietly compares nothing is the defect this replaces.
    /// </summary>
    internal static void Report(Comparison comparison, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(output);

        output.WriteLine();
        if (comparison.Skipped)
        {
            output.WriteLine($"  RATCHET SKIPPED: {comparison.SkipReason}");
            output.WriteLine("    (nothing was compared, so this run is a FAILURE: a baseline was");
            output.WriteLine("     demanded and no verdict could be produced. Refresh the corpus or");
            output.WriteLine("     correct the baseline; this is not a decompiler regression.)");
            return;
        }

        string against = comparison.Baseline!.Date ?? "(undated)";
        string commit = comparison.Baseline!.Commit ?? "(baseline)";
        var regressions = comparison.Regressions;
        output.WriteLine(regressions.Count == 0
            ? $"  RATCHET OK vs {against} ({commit}):"
            : $"  RATCHET FAILED vs {against} ({commit}):");
        foreach (var metric in comparison.Metrics)
            output.WriteLine($"    {(metric.Regressed ? "REGRESSED" : "held     ")}  {metric.Describe()}");

        output.WriteLine(
            "    note: productBodyDefect is a lower bound on decompiler-caused body");
        output.WriteLine(
            "    defects (~5.9% oracle coverage), so read its movement as a floor.");
    }
}

/// <summary>
/// The authored-corpus benchmark's exit contract, as a pure function over the facts
/// that decide it.
///
/// It lives here, apart from the benchmark, for one reason: the benchmark type pulls
/// in the whole harvest/probe graph, so a decision left inside it could only be
/// exercised by running the full 12,000-row corpus. That is precisely how the
/// original defect survived — the contract was never directly tested, and a gate no
/// test can reach is a gate that can rot green. <see cref="AuthoredCorpusRatchetTests"/>
/// covers every branch below.
/// </summary>
static class AuthoredCorpusExitContract
{
    /// <summary>
    /// Whether the run measured the corpus it was asked to measure. A row whose
    /// assembly was not supplied, a row that failed to parse, or an empty run all mean
    /// the denominator is not the one the corpus describes.
    ///
    /// <para>Malformed rows are here, and not merely logged, because dropping one
    /// silently shrinks <c>evaluated</c> — which then fails the ratchet's comparability
    /// key, producing a skip, which is <em>green</em>. A corpus that quietly loses rows
    /// would therefore have disarmed the gate rather than tripping it.</para>
    /// </summary>
    internal static bool InputsComplete(int unmatchedRows, int malformedRows, int evaluated)
        => unmatchedRows == 0 && malformedRows == 0 && evaluated > 0;

    /// <summary>
    /// Whether the run is trustworthy at all. These conditions do not say the
    /// decompiler got worse; they say the number this run produced must not be
    /// compared to anything. A ratchet result — pass, fail, or skip — never rescues a
    /// run that fails here.
    /// </summary>
    internal static bool MeasurementIsSound(
        bool inputsComplete,
        bool partitionClosed,
        int drift,
        int unsupported,
        int unknownOutcome)
        => inputsComplete && partitionClosed && drift == 0 && unsupported == 0 && unknownOutcome == 0;

    /// <summary>
    /// Whether quality held. Without a baseline this is the historical contract —
    /// success requires zero invalid rows, which the trend store's append procedure
    /// documents as exiting 1 by design. With a baseline, quality is judged by movement
    /// against it. A skip has no quality opinion at all; that case is
    /// <see cref="RatchetReachedAVerdict"/>'s, not this one's.
    /// </summary>
    internal static bool QualityHeld(int invalid, AuthoredCorpusRatchet.Comparison? ratchet)
        => ratchet is null
            ? invalid == 0
            : ratchet.Regressions.Count == 0;

    /// <summary>
    /// Whether the gate the caller asked for actually ran.
    ///
    /// <para>A skip is not evidence of a regression — but it is not evidence of
    /// anything else either, and exiting 0 on it would rebuild the exact defect this
    /// file exists to remove: a gate that reports success having compared nothing. The
    /// weekly caller makes that concrete. Its pool is resolved from current top-N
    /// package versions, so it <em>will</em> drift from the recorded manifest; on a
    /// green skip the job would then pass forever while measuring nothing, and the
    /// silence would look exactly like health.</para>
    ///
    /// <para>So passing <c>--ratchet-baseline</c> is a demand for a verdict, and "I
    /// could not produce one" is a failure of that demand. The remedy is a corpus
    /// refresh or a corrected baseline, never a product change — which is why
    /// <see cref="AuthoredCorpusRatchet.Report"/> prints the rejected candidate and the
    /// field that did not line up. A run with no baseline to compare against simply
    /// does not pass the flag.</para>
    /// </summary>
    internal static bool RatchetReachedAVerdict(AuthoredCorpusRatchet.Comparison? ratchet)
        => ratchet is null || !ratchet.Skipped;

    internal static int ExitCode(bool measurementIsSound, int invalid, AuthoredCorpusRatchet.Comparison? ratchet)
        => measurementIsSound && RatchetReachedAVerdict(ratchet) && QualityHeld(invalid, ratchet) ? 0 : 1;
}
