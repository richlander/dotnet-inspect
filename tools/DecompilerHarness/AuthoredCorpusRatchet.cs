using System.Globalization;

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
        int Methodology,
        string? SweepManifestSha256)
    {
        public static RunKey From(HistoryRun run)
            => new(run.Evaluated, run.PoolMatched, run.PoolTotal, run.Methodology, run.SweepManifestSha256);

        /// <summary>
        /// True when both runs measured the same corpus, the same pool, and the same
        /// methodology. The sweep-manifest hash is checked only when <em>both</em>
        /// sides recorded one: a live benchmark run is not given the pool manifest,
        /// so requiring it unconditionally would make the live path skip forever,
        /// which is the permanently-green failure this gate exists to prevent. A pool
        /// that drifts under a live run shows up as unmatched rows, which the
        /// integrity half already fails on.
        /// </summary>
        public bool IsComparableTo(RunKey other, out string mismatch)
        {
            if (Evaluated != other.Evaluated)
                return Fail($"evaluated {other.Evaluated} vs {Evaluated}", out mismatch);
            if (PoolMatched != other.PoolMatched || PoolTotal != other.PoolTotal)
                return Fail($"pool {other.PoolMatched}/{other.PoolTotal} vs {PoolMatched}/{PoolTotal}", out mismatch);
            if (Methodology != other.Methodology)
                return Fail($"methodologyVersion {other.Methodology} vs {Methodology}", out mismatch);
            if (SweepManifestSha256 is { } mine
                && other.SweepManifestSha256 is { } theirs
                && !string.Equals(mine, theirs, StringComparison.Ordinal))
            {
                return Fail($"sweepManifestSha256 {theirs} vs {mine}", out mismatch);
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
    /// The ratcheted quality metrics for one run. <see cref="ProductBodyDefect"/> is
    /// nullable because rows predating the invalid breakdown did not record it;
    /// absent means <em>not measured</em>, never zero.
    /// </summary>
    internal sealed record RunMetrics(double ValidPct, int Correct, int Invalid, int? ProductBodyDefect)
    {
        public static RunMetrics From(HistoryRun run)
            => new(run.ValidPct, run.Correct, run.Invalid, run.InvalidBreakdown?.ProductBodyDefect);
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
            // The store records validPct at one decimal place. Comparing a
            // full-precision current value against a rounded baseline would report a
            // regression for a run that did not move, so both sides are compared at
            // the precision the store actually preserves.
            new("validPct", Round(baseline.ValidPct), Round(current.ValidPct), HigherIsBetter: true),
            new("correct", baseline.Correct, current.Correct, HigherIsBetter: true),
            new("invalid", baseline.Invalid, current.Invalid, HigherIsBetter: false),
        };

        // productBodyDefect is the product signal (raw invalid is ~92% harness
        // shell-reconstruction noise), but it is a *lower bound* on decompiler-caused
        // body defects, so it ratchets only when both sides measured it.
        if (baseline.ProductBodyDefect is { } baselineDefects && current.ProductBodyDefect is { } currentDefects)
            metrics.Add(new("productBodyDefect", baselineDefects, currentDefects, HigherIsBetter: false));

        return metrics;
    }

    static double Round(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

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
            output.WriteLine("    (nothing was compared — this run proves no regression either way)");
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
