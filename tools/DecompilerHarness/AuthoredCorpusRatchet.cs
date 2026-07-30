using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
        string? PoolSha256,
        string? CorpusSha256)
    {
        public static RunKey From(HistoryRun run)
            => new(run.Evaluated, run.PoolMatched, run.PoolTotal, run.PoolSha256, run.CorpusSha256);

        /// <summary>
        /// True when both runs measured the same corpus over the same pool.
        ///
        /// <para>Both identity hashes compare <em>symmetrically</em>, including absence:
        /// unknown never equals known. An earlier rule checked the pool hash only when
        /// both sides recorded one, which was unsound — a package resolving to a newer
        /// version that still carries the same method identities resolves cleanly,
        /// produces no drift, and would have been compared against numbers measured on
        /// different code. Governing it by the baseline alone was also unsound, and for
        /// a subtler reason: a run whose hash <em>mismatched</em> the newest row simply
        /// fell through to an older row that recorded no hash at all, turning a drifted
        /// pool back into a green comparison against an unidentified one. Symmetry is
        /// the only rule with no such fallthrough. Refusing to compare is the safe
        /// direction: a loud skip, never a silent pass.</para>
        ///
        /// <para><see cref="CorpusSha256"/> exists because the counts alone do not
        /// identify the corpus. Replacing a row's authored body — or the whole 12,000
        /// rows — while keeping the row count and the pool left the key intact, so a
        /// wholly different measurement compared clean.</para>
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
            if (!string.Equals(PoolSha256, other.PoolSha256, StringComparison.Ordinal))
            {
                return Fail(
                    $"poolSha256 {Show(other.PoolSha256)} vs {Show(PoolSha256)}",
                    out mismatch);
            }

            if (!string.Equals(CorpusSha256, other.CorpusSha256, StringComparison.Ordinal))
                return Fail($"corpusSha256 {Show(other.CorpusSha256)} vs {Show(CorpusSha256)}", out mismatch);

            mismatch = "";
            return true;

            static bool Fail(string reason, out string mismatch)
            {
                mismatch = reason;
                return false;
            }

            static string Show(string? hash) => hash ?? "(none recorded)";
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
    internal sealed record RunMetrics(
        int? Valid,
        int Correct,
        int Invalid,
        int? ProductBodyDefect,
        int Methodology,
        bool MethodologyStated = true,
        bool Identified = true)
    {
        public static RunMetrics From(HistoryRun run)
            => new(
                run.ValidDifferent is { } validDifferent ? run.Correct + validDifferent.Total : null,
                run.Correct,
                run.Invalid,
                run.InvalidBreakdown?.ProductBodyDefect,
                run.Methodology,
                run.MethodologyVersion is not null,
                run.PoolSha256 is not null || run.CorpusSha256 is not null);
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
    /// Whether a recorded row is a number worth comparing to.
    ///
    /// <para>The ratchet judges <em>quality</em>, and quality metrics only mean
    /// something on a run whose <em>measurement</em> was sound. A row that shed rows
    /// into <c>drift</c>, <c>unsupported</c>, or <c>unknownOutcome</c> reports a lower
    /// <c>invalid</c> for having measured less — which the ratchet would read as an
    /// improvement. That is the same "looks like progress, is actually absence" shape
    /// the whole file exists to remove, so an untrustworthy row is neither judged nor
    /// used as a baseline.</para>
    ///
    /// <para><c>unknownOutcome</c> must be recorded and zero, not merely absent. Absent
    /// means the run did not report the field, so its soundness cannot be confirmed —
    /// and an unconfirmable row is not a baseline. Partition closure is pinned
    /// separately, as set equality, by <c>AuthoredCorpusHistoryCardTests</c>.</para>
    ///
    /// <para>A recorded identity must also be <em>well formed</em>. Rows are appended by
    /// hand, and every other field a human copies is already checked rather than
    /// trusted — the buckets must close, the breakdown must pair with its methodology.
    /// The digests were the exception, and they are the fields that decide whether any
    /// comparison happens at all: <see cref="RunKey.IsComparableTo"/> compares them as
    /// opaque strings, so a row recording <c>""</c> or a typo does not read as absent,
    /// it reads as an identity — and two rows carrying the same malformed identity
    /// compare clean, which is a fabricated pool identity wearing the shape of a real
    /// one. Absence is honest and stays allowed; a malformed identity is not.</para>
    /// </summary>
    internal static bool IsTrustworthy(HistoryRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return IdentityIsWellFormed(run.PoolSha256)
            && IdentityIsWellFormed(run.CorpusSha256)
            && AuthoredCorpusExitContract.MeasurementIsSound(
                run.InputsComplete,
                PartitionCloses(run),
                run.Drift,
                run.Unsupported,
                run.UnknownOutcome ?? -1);
    }

    /// <summary>
    /// Whether a recorded identity is absent, or is a digest <see cref="Digest"/> could
    /// actually have produced: 64 lowercase hex characters. Case matters, because the
    /// comparison is ordinal — an uppercase copy of a real digest would be a distinct
    /// identity for the same pool, which reads as drift that never happened.
    /// </summary>
    internal static bool IdentityIsWellFormed(string? sha256)
    {
        if (sha256 is null)
            return true;

        if (sha256.Length != 64)
            return false;

        foreach (char c in sha256)
        {
            if (c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether a row's buckets actually account for every evaluated target, at every
    /// level, with counts that could describe a real run.
    ///
    /// <para>Recording a bucket is not the same as the buckets adding up, and the
    /// difference is the whole point: a row claiming 100 evaluated whose buckets sum to
    /// 99 has lost a target, and a lost target is a row that reports a lower
    /// <c>invalid</c> for having measured less. A reviewer landed exactly that baseline
    /// past the earlier presence-only check and got <c>RATCHET OK</c> with exit 0, so
    /// this compares the sums rather than trusting that the fields exist.</para>
    ///
    /// <para>The <c>invalidBreakdown</c> partition is checked on the same terms as the
    /// other two. It is optional, because rows predate it — but a row that states it
    /// and does not close it is not grandfathered, it is wrong. A later reviewer forged
    /// a row pairing <c>invalid: 0</c> with <c>productBodyDefect: 100</c>; it closed
    /// both partitions this once checked, became comparable, and set a
    /// <c>productBodyDefect</c> threshold of 100 that a real regression could hide
    /// under. A row that omits the breakdown entirely cannot launder anything, because
    /// <see cref="StatesEveryMetric"/> refuses an identified baseline that is silent on
    /// the product metric.</para>
    ///
    /// <para>Closure and non-negativity are each load-bearing and neither implies the
    /// other. Forging the threshold above <em>requires</em> a negative bucket once
    /// closure is enforced — pushing <c>productBodyDefect</c> to 1000 on a 100-target
    /// run forces some other top-level bucket to -900 for the total to land — so
    /// dropping either check reopens the hole.</para>
    /// </summary>
    static bool PartitionCloses(HistoryRun run)
    {
        if (!run.CountsAreNonNegative)
            return false;

        if (run.TopLevelSum != run.Evaluated)
            return false;

        if (run.ValidDifferent is not { } validDifferent || validDifferent.SubBucketSum != validDifferent.Total)
            return false;

        return run.InvalidBreakdown is not { } breakdown || breakdown.Sum == run.Invalid;
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
            var candidate = baselines[index];
            if (!IsTrustworthy(candidate))
            {
                newestMismatch ??= $"{candidate.Date ?? "(undated)"}: measurement not sound";
                continue;
            }

            if (!currentKey.IsComparableTo(RunKey.From(candidate), out string mismatch))
            {
                newestMismatch ??= $"{candidate.Date ?? "(undated)"}: {mismatch}";
                continue;
            }

            if (!StatesEveryMetric(current, RunMetrics.From(candidate), out string missing))
            {
                newestMismatch ??= $"{candidate.Date ?? "(undated)"}: does not record {missing}, so that metric could not ratchet";
                continue;
            }

            baseline = candidate;
            break;
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
        if (!IsTrustworthy(newest))
        {
            return Comparison.Skip(
                $"newest run ({newest.Date ?? "(undated)"}) did not record a sound measurement, "
                + "so its quality metrics mean nothing");
        }

        return Compare(RunKey.From(newest), RunMetrics.From(newest), runs.Take(runs.Count - 1).ToArray());
    }

    /// <summary>
    /// Whether a candidate baseline can state every metric the current run states.
    ///
    /// <para><see cref="Build"/> only emits a metric when both sides have a number for
    /// it, so a baseline missing one produces a comparison that ratchets fewer metrics
    /// than the run has — and reports <c>RATCHET OK</c> either way. A reviewer landed a
    /// row with <c>invalidBreakdown: null</c> and got a clean three-metric pass while
    /// the product signal went unchecked. Dropping a metric is only legitimate when a
    /// methodology bump redefined it, which is why that one case is excluded here
    /// rather than silently tolerated everywhere.</para>
    /// </summary>
    static bool StatesEveryMetric(RunMetrics current, RunMetrics candidate, out string missing)
    {
        if (current.Valid is not null && candidate.Valid is null)
        {
            missing = "validDifferent";
            return false;
        }

        // A run that states a methodology measured this metric, so a row carrying one
        // and omitting the metric is malformed, not historical. Only rows recorded
        // before the metric existed — which carry no methodologyVersion at all — may
        // omit it, and that is a structural fact about the row rather than a value it
        // chose. Comparing methodology *values* instead let a reviewer shed the metric
        // by writing an arbitrary version (999) into an otherwise comparable baseline;
        // a narrower value comparison would still admit the same trick one version down.
        //
        // The rule holds for *both* rows. Guarding only the baseline left the shorter
        // path open: a reviewer dropped invalidBreakdown from the appended row itself,
        // kept methodologyVersion, and the tracked-store gate passed green with the
        // product metric silently unratcheted. Build only emits a metric both sides can
        // state, so an omission anywhere shrinks the comparison while it still prints
        // RATCHET OK.
        if (MustStateTheProductMetric(candidate) || MustStateTheProductMetric(current))
        {
            missing = "invalidBreakdown";
            return false;
        }

        missing = "";
        return true;
    }

    /// <summary>
    /// Whether a row is required to state <c>productBodyDefect</c>.
    ///
    /// <para>Only rows recorded before the metric existed may omit it, and trusting the
    /// absent methodology stamp alone to prove that was not enough: a reviewer shed the
    /// metric from a freshly fabricated baseline simply by deleting <em>both</em> fields
    /// rather than claiming a version. Absence is not evidence of age when the author
    /// chooses what to write.</para>
    ///
    /// <para>The rows that predate the metric also predate run identity, so a row that
    /// identifies itself is demonstrably not one of them. That is checkable rather than
    /// asserted, and it costs nothing: a live run always records both digests, so a
    /// baseline comparable to one must record them too (<see cref="RunKey.IsComparableTo"/>
    /// compares their presence symmetrically). A fabricated baseline therefore cannot
    /// be both comparable and grandfathered.</para>
    /// </summary>
    static bool MustStateTheProductMetric(RunMetrics row)
        => row.ProductBodyDefect is null && (row.MethodologyStated || row.Identified);

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
    /// A pool's identity: the assemblies the run actually measured, each named and
    /// content-hashed, sorted and digested.
    ///
    /// <para>Earlier revisions derived this from the sweep manifest, and a reviewer
    /// showed that could not identify the pool. The manifest describes only the sweep
    /// half — <c>eng/prepare-evil-corpus.sh</c> measures the <em>union</em> of the
    /// sweep and a fixed set of real-world assemblies — so changing the real-world half
    /// left the identity unchanged. It also listed packages that resolved but produced
    /// no assembly, so a pool where a package failed hashed the same as one where it
    /// succeeded.</para>
    ///
    /// <para>Taking the identity from the inputs themselves removes all of that: it is
    /// the bytes that were decompiled, so it cannot describe a different pool than the
    /// one measured, and it needs no flag. That last point is not cosmetic — an
    /// identity that depends on the caller remembering an argument is the same shape as
    /// the gate nobody invoked (#3245). File content, not path, because the pool is
    /// staged to a different directory on every run.</para>
    /// </summary>
    internal static string PoolDigest(IReadOnlyList<string> assemblyPaths)
    {
        ArgumentNullException.ThrowIfNull(assemblyPaths);

        // Both halves are fixed-width digests, so the composition cannot be ambiguous.
        // Interpolating the file name raw would not be safe: a Linux file name may
        // contain ':' and '\n', so a single file named to embed a separator could forge
        // the identity string of a two-file pool.
        var identities = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in assemblyPaths)
        {
            string name = Digest(Encoding.UTF8.GetBytes(Path.GetFileName(path)));
            identities.Add($"{name}:{Digest(File.ReadAllBytes(path))}");
        }

        if (identities.Count == 0)
            throw new InvalidOperationException("The run measured no assemblies, so it identifies no pool.");

        return Digest(Encoding.UTF8.GetBytes(string.Join("\n", identities)));
    }

    /// <summary>
    /// The set of assemblies a run will measure, together with the identity of that
    /// set. The two are produced here, from one list, so they cannot describe different
    /// pools — which is the only reason this type exists rather than a plain list.
    /// </summary>
    /// <param name="Sha256">
    /// The identity of <paramref name="Assemblies"/>, or <see langword="null"/> when the
    /// selection is empty — a run that measures nothing identifies no pool, and the
    /// caller reports that as an integrity failure rather than comparing against it.
    /// </param>
    internal sealed record MeasuredPool(IReadOnlyList<string> Assemblies, IReadOnlyList<string> Identities, string? Sha256);

    /// <summary>
    /// Selects the assemblies a run measures and identifies that exact set.
    ///
    /// <para>Evaluation takes the first path offered for each assembly identity and
    /// ignores the rest, so the selection is order-sensitive. A reviewer showed what
    /// happens when the identity is taken from the supplied list instead: two
    /// byte-distinct assemblies sharing an identity could be reordered to change which
    /// one was measured while the digest stayed put, and a B-first run compared clean
    /// against an A-first baseline.</para>
    ///
    /// <para>Returning the selection and its digest together is what makes that
    /// unrepeatable. The caller has no second list to reach for, so the identity cannot
    /// drift from the measurement without deleting this seam outright — and
    /// <c>SelectPool_IdentifiesExactlyWhatItSelected</c> would notice.</para>
    /// </summary>
    /// <param name="supplied">Paths as handed to the run, in the order they will be tried.</param>
    /// <param name="identify">
    /// Returns the assembly identity to measure this path under, or <see langword="null"/>
    /// if the run will not measure it at all (missing file, or no corpus rows).
    /// </param>
    internal static MeasuredPool SelectPool(IReadOnlyList<string> supplied, Func<string, string?> identify)
    {
        ArgumentNullException.ThrowIfNull(supplied);
        ArgumentNullException.ThrowIfNull(identify);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var assemblies = new List<string>();
        var identities = new List<string>();
        foreach (var path in supplied)
        {
            if (identify(path) is not { } identity || !seen.Add(identity))
                continue;

            assemblies.Add(path);
            identities.Add(identity);
        }

        return new MeasuredPool(
            assemblies,
            identities,
            assemblies.Count == 0 ? null : PoolDigest(assemblies));
    }

    /// <summary>
    /// The corpus's identity: a digest of its exact bytes. The corpus is a pinned
    /// vendored artifact, so byte equality is the right expectation and any edit —
    /// including one that preserves the row count — must retarget the comparison.
    /// </summary>
    internal static string CorpusDigest(string corpusPath) => Digest(File.ReadAllBytes(corpusPath));

    /// <summary>
    /// Full SHA-256, lowercase hex. Deliberately untruncated: these digests are an
    /// integrity gate, and a 64-bit identity falls to a birthday attack in about 2^32
    /// operations — minutes of commodity GPU time — which would let a pool or corpus be
    /// swapped underneath a recorded baseline while the identity still matched. The
    /// only cost of the full value is column width in a store nothing reads by hand.
    /// </summary>
    static string Digest(ReadOnlySpan<byte> content)
        => Convert.ToHexStringLower(SHA256.HashData(content));

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
    /// <summary>What a flag combination asks the harness to do before any mode runs.</summary>
    internal enum FlagDisposition
    {
        /// <summary>The flags are consistent; dispatch normally.</summary>
        Proceed,

        /// <summary>The caller asked only for usage; print it and exit 0.</summary>
        PrintUsage,

        /// <summary>The flags contradict each other; refuse with <see cref="FlagVerdict.Message"/>.</summary>
        Refuse,
    }

    /// <summary>The disposition of one flag combination, and why.</summary>
    internal readonly record struct FlagVerdict(FlagDisposition Disposition, string? Message);

    /// <summary>
    /// Judges the gate flags against <c>--help</c> before any mode dispatches.
    ///
    /// <para>This lives here rather than inline in <c>Program.cs</c> because
    /// <c>Program.cs</c> cannot be linked into the test project — it owns an entry
    /// point — and a rule that no test can reach is a rule that can be deleted without
    /// anything noticing. That is not hypothetical: the fix for
    /// <c>--benchmark-authored-corpus &lt;missing&gt; --help</c> exiting 0 was verified
    /// once by hand against the real binary, and reverting it afterwards left the whole
    /// suite green. Sixteen combinations are cheap to enumerate once the decision is a
    /// function.</para>
    ///
    /// <para>The shape of the bug in every case: a gate flag that is silently ignored is
    /// a permanently green gate, which is the failure these flags exist to remove
    /// (#3245). <c>--help</c> answering first made every gate flag ignorable one flag at
    /// a time — first the two ratchet options, then the gate flag itself.</para>
    /// </summary>
    internal static FlagVerdict JudgeGateFlags(
        bool showHelp,
        bool benchmarkAuthoredCorpus,
        bool verifyAuthoredCorpus,
        bool ratchetBaselineSupplied,
        bool integrityOnly)
    {
        // Both corpus gates are taken separately rather than pre-combined by the caller.
        // Pre-combining put the `||` in Program.cs, which no test can reach: tampering it
        // away left the whole suite green even though `--verify-authored-corpus --help`
        // then exited 0 again. Every term of this rule has to live where a test can see
        // it, or the rule is only as strong as the last person to read the call site.
        bool anyGateFlag = benchmarkAuthoredCorpus || verifyAuthoredCorpus || ratchetBaselineSupplied || integrityOnly;

        if (showHelp && !anyGateFlag)
            return new FlagVerdict(FlagDisposition.PrintUsage, null);

        if (ratchetBaselineSupplied && !benchmarkAuthoredCorpus)
            return Refuse("--ratchet-baseline applies to --benchmark-authored-corpus; it has no effect on its own.");

        if (showHelp)
            return Refuse("--help does not run a gate; drop the ratchet flags to read usage.");

        if (integrityOnly && !benchmarkAuthoredCorpus)
            return Refuse("--integrity-only applies to --benchmark-authored-corpus; it has no effect on its own.");

        // Asking for a quality verdict and declining to judge quality are contradictory
        // demands, and silently honouring one of them would make the exit code mean
        // something the caller did not ask for.
        if (integrityOnly && ratchetBaselineSupplied)
            return Refuse("--integrity-only and --ratchet-baseline are contradictory: one declines to judge quality, the other demands a verdict on it.");

        return new FlagVerdict(FlagDisposition.Proceed, null);

        static FlagVerdict Refuse(string message) => new(FlagDisposition.Refuse, message);
    }

    /// <summary>
    /// The gates <see cref="PreemptedGateRefusal"/> protects: every mode whose whole
    /// purpose is to measure something and report a verdict on it.
    ///
    /// <para>This is a declaration rather than a literal at the call site because review
    /// deleted <c>--verify-authored-corpus</c> from that literal and the suite stayed
    /// green while the real binary went back to exiting 0 having verified nothing. Every
    /// other piece of this rule had already been moved here for the same reason; the
    /// argument naming <em>what</em> to protect was the last piece still out of reach.
    /// <c>ProtectedGates_AreTheAuthoredCorpusGates</c> pins the contents and
    /// <c>ProtectedGates_AreWhatTheHarnessActuallyPasses</c> pins that the harness passes
    /// this list and not a fresh literal, so dropping a gate fails in one place and
    /// routing around the list fails in the other.</para>
    /// </summary>
    /// <para>Immutable, not <c>static readonly string[]</c>. A readonly array field is
    /// only readonly in its reference: <c>ProtectedGates[1] = "..."</c> would rewrite the
    /// list before the call, and the contents pin runs in the test process where no such
    /// write happened, so it would not notice.</para>
    internal static readonly ImmutableArray<string> ProtectedGates =
        ["--benchmark-authored-corpus", "--verify-authored-corpus"];

    /// <summary>
    /// Every mode the harness dispatches, in dispatch order.
    ///
    /// <para>This is product code, and <see cref="PreemptedGateRefusal"/> refuses to
    /// judge a dispatch order that does not match it, because the alternative was tried
    /// and failed. The coverage of the black-box tests used to be tied to the harness by
    /// <em>parsing <c>Program.cs</c> for its dispatch list</em>, and review round eleven
    /// defeated that by placing a commented-out copy of the old list above the live
    /// declaration: the parse found the decoy, the expected names still matched, a new
    /// mode entered the live list untested, and
    /// <c>--black-box-gap --benchmark-authored-corpus &lt;corpus&gt;</c> exited 0 having
    /// discarded the requested gate while the whole suite stayed green.</para>
    ///
    /// <para>One review round earlier, the same technique had been deleted from a sibling
    /// test for being wrong in both directions. It was kept here on the reasoning that a
    /// name mirror "can false-red but cannot false-green". That reasoning was wrong, and
    /// the lesson is narrower than "avoid source parsing": a test that reads source text
    /// is guessing at what the program does, and a decoy is always available. Naming the
    /// modes here, where the running binary checks them, cannot be decoyed.</para>
    ///
    /// <para><strong>What this does and does not guarantee.</strong> Both operands of
    /// that check are declarations: this list, and the harness's <c>dispatchOrder</c>.
    /// Comparing them keeps <c>dispatchOrder</c> honest, so the refusal below sees every
    /// mode the harness admits to having — that is all it is for. It does <em>not</em>
    /// see the dispatch itself, which is an <c>if</c>-cascade neither list is derived
    /// from. Review round twelve demonstrated the gap: a mode with a parse case and a
    /// handler in the cascade, but absent from both lists, discarded a requested gate at
    /// exit 0 with the suite green. That hole is closed by
    /// <see cref="GateExitedWithoutRunning"/>, which does not consult any list.</para>
    /// </summary>
    internal static readonly ImmutableArray<string> DispatchModes =
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

    /// <summary>
    /// The refusal owed when a protected gate was requested and the process is about to
    /// report success without the gate having run, or <see langword="null"/> if none.
    ///
    /// <para>Every other defense in this file names something: the modes that exist, the
    /// order they dispatch in, the gates that are protected. Naming is why they kept
    /// failing. A rule that lists what may preempt a gate is only as complete as the
    /// list, and eleven review rounds produced a new way to be absent from one — an
    /// argument dropped at the call site, a decoy above the declaration, a mode dispatched
    /// from an <c>if</c>-cascade that no list is derived from.</para>
    ///
    /// <para>This check names nothing. It observes two facts the harness knows about
    /// itself — the gate was asked for, and the gate ran — and refuses the one
    /// combination that is #3245: <em>exit 0 without having measured.</em> A mode added
    /// anywhere, declared or not, that returns before the gate leaves
    /// <paramref name="gateDispatched"/> false and is caught, because the property being
    /// checked is the outcome rather than the route to it.</para>
    ///
    /// <para>A non-zero exit is deliberately not checked. The failure being closed is a
    /// gate that reports success without measuring; a process that already failed has
    /// said so, and re-reporting it here would bury the real error — the refusal below,
    /// or an argument validation — under a second, vaguer one.</para>
    /// </summary>
    internal static string? GateExitedWithoutRunning(int exitCode, bool gateRequested, bool gateDispatched)
        => exitCode == 0 && gateRequested && !gateDispatched
            ? "A protected gate was requested but never ran, and the harness was about to "
              + "report success. Some other mode returned first. This is the failure the gate "
              + "exists to prevent, so it is an error rather than a silent success."
            : null;

    /// <summary>
    /// Whether a drift run measured what it claimed to.
    ///
    /// <para>Malformed rows are the reason this exists. <c>ReadCorpus</c> logged and
    /// discarded them, and nothing counted them, so <c>--fail-on-drift</c> judged the
    /// surviving rows and exited 0 — a fail-closed gate reporting success over a corpus it
    /// had silently shortened. Review round thirteen demonstrated it with one valid row
    /// and one line of invalid JSON: <c>corpusRows: 1, verified: 1, honest: true</c>,
    /// exit 0. That is #3245 in the other gate, and the benchmark had already learned the
    /// same lesson — it counts malformed rows into <c>inputsComplete</c>.</para>
    ///
    /// <para>A row that cannot be parsed is not a row that verified. It is a row nobody
    /// looked at, and a gate that cannot say how many of those there were is not
    /// measuring the corpus it was given.</para>
    /// </summary>
    internal static bool DriftMeasurementIsSound(int malformedRows, int unmatchedRows, int evaluatedRows)
        => malformedRows == 0 && unmatchedRows == 0 && evaluatedRows > 0;

    /// <summary>
    /// The drift gate's exit code.
    ///
    /// <para>Both of the drift reporters computed this themselves, in duplicate — the
    /// text card and the JSON payload each had their own copy of the same expression. A
    /// rule spelled twice is a rule that can be fixed once, and every round of this review
    /// has found some version of that. It is spelled here, once, and both call it.</para>
    ///
    /// <para>Unsound measurement fails whether or not <c>--fail-on-drift</c> was passed:
    /// the report-only mode is a diagnostic about drift, not a licence to misreport how
    /// much of the corpus was read.</para>
    /// </summary>
    internal static int DriftExitCode(bool measurementIsSound, bool failOnDrift, int drifted, int unavailable)
        => measurementIsSound && !(failOnDrift && (drifted > 0 || unavailable > 0)) ? 0 : 1;

    /// <summary>
    /// Set to <c>1</c> to inject the defect <see cref="GateExitedWithoutRunning"/> exists
    /// to catch: the harness returns 0 after parsing, before any mode dispatches.
    ///
    /// <para>The check is reachable only when the harness is broken, so no ordinary
    /// invocation exercises it and nothing would notice if its wiring were deleted. Review
    /// round twelve reached the real hole by hand-editing a mode into the dispatch cascade;
    /// this makes that exploit a supported input, so a test can keep running it.</para>
    ///
    /// <para>It is deliberately an environment variable rather than a flag. A flag would
    /// be a mode, and would need declaring in the very lists this check exists in order to
    /// not depend on. It lives here rather than in the harness because the test assembly
    /// cannot see the harness's own types — the project reference that builds the binary
    /// sets <c>ReferenceOutputAssembly="false"</c>, which is what keeps these tests
    /// black-box.</para>
    /// </summary>
    internal const string SimulatePreemptionVariable = "DOTNET_INSPECT_HARNESS_SIMULATE_PREEMPTION";

    /// <summary>
    /// The refusal owed by the flag combination, or <see langword="null"/> if none.
    ///
    /// <para>A gate is preempted when a mode earlier in the dispatch order is also
    /// selected: that mode does not run the gate second, it runs instead of it, and the
    /// process exits 0 having measured nothing. That is the permanently-green failure of
    /// #3245 one level up — a CI lane that grew a second flag would stop gating and
    /// report success.</para>
    ///
    /// <para>The whole rule lives here, <em>including the conjunction that a gate must
    /// actually be requested</em>. The first version left that conjunction at the call
    /// site, testing only the search, and the result was worse than the bug it fixed:
    /// every mode in the harness refused, because each one preempted a gate nobody had
    /// asked for. It exited 1 for a wrong reason, which an exit-code-only check reads as
    /// correct. A rule split between a tested function and an untested caller is only as
    /// strong as the caller.</para>
    ///
    /// <para>Throws when a named gate is absent from the order, because a gate that has
    /// silently dropped out would otherwise be reported as unpreemptable — the exact
    /// false green this exists to prevent.</para>
    /// </summary>
    internal static string? PreemptedGateRefusal(
        IReadOnlyList<(string Flag, bool Selected)> dispatchOrder,
        IReadOnlyList<string> gates)
    {
        // The caller's order must be the declared one. A mode the harness dispatches but
        // never declares is a mode no test knows to cover, which is how a gate goes
        // ungated while the suite stays green; failing here makes the binary say so on
        // every invocation rather than only on the combination nobody wrote a case for.
        if (!dispatchOrder.Select(entry => entry.Flag).SequenceEqual(DispatchModes))
        {
            throw new ArgumentException(
                "The dispatch order does not match AuthoredCorpusExitContract.DispatchModes. "
                    + "A mode was added, removed, or reordered in the harness without "
                    + "declaring it, so nothing knows to test it. Declared: "
                    + string.Join(", ", DispatchModes)
                    + ". Received: "
                    + string.Join(", ", dispatchOrder.Select(entry => entry.Flag))
                    + ".",
                nameof(dispatchOrder));
        }

        foreach (string gate in gates)
        {
            int position = IndexOfGate(dispatchOrder, gate);
            if (!dispatchOrder[position].Selected)
                continue;

            for (int index = 0; index < position; index++)
            {
                if (dispatchOrder[index].Selected)
                    return PreemptedGateMessage(dispatchOrder[index].Flag, gate);
            }
        }

        return null;
    }

    /// <summary>
    /// The first selected mode that dispatches strictly before <paramref name="gate"/>,
    /// or <see langword="null"/> if the gate is reached.
    ///
    /// <para>Taking the dispatch order as data, and each gate's exposure as a prefix of
    /// it, is deliberate. The first fix for preemption was a hand-maintained array
    /// written next to one gate, and review then found the <em>other</em>
    /// authored-corpus gate entirely unprotected — plus, one round earlier, four flags
    /// missing from the array itself. A list both gates read cannot protect one and
    /// forget the other, and a mode inserted into it is covered without anyone
    /// remembering to.</para>
    /// </summary>
    internal static string? FindPreemptingMode(
        IReadOnlyList<(string Flag, bool Selected)> dispatchOrder,
        string gate)
    {
        int position = IndexOfGate(dispatchOrder, gate);
        for (int index = 0; index < position; index++)
        {
            if (dispatchOrder[index].Selected)
                return dispatchOrder[index].Flag;
        }

        return null;
    }

    static int IndexOfGate(IReadOnlyList<(string Flag, bool Selected)> dispatchOrder, string gate)
    {
        for (int index = 0; index < dispatchOrder.Count; index++)
        {
            if (string.Equals(dispatchOrder[index].Flag, gate, StringComparison.Ordinal))
                return index;
        }

        throw new ArgumentException($"{gate} is not in the dispatch order, so it cannot be gated.", nameof(gate));
    }

    /// <summary>The refusal a preempted gate reports. Shared so both gates say the same thing.</summary>
    internal static string PreemptedGateMessage(string preempting, string gate)
        => $"{preempting} runs instead of {gate}, so the gate would report success without measuring anything. Run them separately.";

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
    /// Which quality claim a run's exit code makes.
    ///
    /// <para>All three are named because they produce identical-looking exit codes and
    /// a caller can otherwise select one by accident. That is not hypothetical: the
    /// weekly lane was first wired with no baseline in order to get an integrity-only
    /// gate, and silently got <see cref="Perfection"/> instead — a contract the corpus
    /// cannot satisfy, so the job would have failed every week forever and filed an
    /// issue each time. Permanently red reports exactly as much as permanently green.
    /// Making the choice explicit is what stops the next caller repeating it.</para>
    /// </summary>
    internal enum QualityContract
    {
        /// <summary>
        /// No baseline was offered: success requires zero invalid rows. This is the
        /// historical contract, which the trend store's append procedure documents as
        /// exiting 1 by design — it is how a run records a row without claiming to have
        /// passed a gate.
        /// </summary>
        Perfection,

        /// <summary>
        /// A baseline was offered: quality is judged by movement against it.
        /// </summary>
        Ratchet,

        /// <summary>
        /// The caller asked for measurement integrity only, so the exit code makes no
        /// quality claim whatsoever. Legitimate for a lane that cannot yet ratchet
        /// (see the weekly caller and #3353), and reported in the run output and JSON
        /// so a green result cannot be misread as a quality pass.
        /// </summary>
        NotJudged,
    }

    /// <summary>
    /// The one place a contract is selected, so the flags and the exit code cannot
    /// disagree about which claim is being made.
    /// </summary>
    internal static QualityContract ContractFor(bool integrityOnly, AuthoredCorpusRatchet.Comparison? ratchet)
        => integrityOnly ? QualityContract.NotJudged
            : ratchet is null ? QualityContract.Perfection
            : QualityContract.Ratchet;

    /// <summary>
    /// Whether quality held, under whichever contract the caller selected. A skip has
    /// no quality opinion at all; that case is <see cref="RatchetReachedAVerdict"/>'s,
    /// not this one's.
    /// </summary>
    internal static bool QualityHeld(int invalid, AuthoredCorpusRatchet.Comparison? ratchet, QualityContract contract)
        => contract switch
        {
            QualityContract.NotJudged => true,
            QualityContract.Perfection => invalid == 0,
            QualityContract.Ratchet => ratchet is { } comparison
                ? comparison.Regressions.Count == 0
                : throw new ArgumentException("The ratchet contract requires a comparison.", nameof(ratchet)),
            _ => throw new ArgumentOutOfRangeException(nameof(contract)),
        };

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

    internal static int ExitCode(
        bool measurementIsSound,
        int invalid,
        AuthoredCorpusRatchet.Comparison? ratchet,
        QualityContract contract)
        => measurementIsSound && RatchetReachedAVerdict(ratchet) && QualityHeld(invalid, ratchet, contract) ? 0 : 1;
}
