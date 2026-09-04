using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using DotnetInspector.Core;
using DotnetInspector.Services;
using ILInspector.Findings;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// The source-oracle candidate ledger: an explicit, network-bound measurement of which
/// whole source files could be enrolled in the authored-source oracle next, and in what
/// order they would add the most new C# syntax coverage.
///
/// <para><strong>What it measures.</strong> For every scanned assembly it reads the
/// complete portable-PDB MethodDef to primary-document mapping <em>before</em> acquiring
/// any source, intersects that mapping with the real-method targets, attempts each
/// eligible target, and evaluates the captured rows through the same source-oracle
/// evaluator the enrolled gate uses. Each checksum-pinned file identity — the
/// <c>(sourceUrl, checksumAlgorithm, checksum)</c> triple — is then Enrolled,
/// Qualified, Rejected, or Unevaluable, and the qualifying files are ranked greedily by
/// how many syntax features they add on top of an accepted enrolled-oracle
/// benchmark report.</para>
///
/// <para><strong>Why the denominator comes from the PDB.</strong> Deriving file
/// membership from successful harvest records would silently erase eligible targets that
/// source acquisition skipped. The mapping census is computed first and never reconciled
/// against harvest success.</para>
///
/// <para><strong>What it is not.</strong> It is a measurement mode, not a gate. A
/// rejected candidate and a source fetch that did not answer are typed data and leave the
/// exit code at zero. Only measurement integrity fails the run: no usable assembly or
/// target, a failed PDB mapping census, an evaluation count or correlation mismatch, a
/// baseline report that does not carry accepted enrolled-oracle evidence, or a run in
/// which no checksum-identified file was evaluated at all.</para>
///
/// <para><strong>Scope honesty.</strong> Qualification is eligible-method completeness
/// for the scanned assembly set, not a claim that every C# declaration in the file was
/// checked. Every file publishes <c>mappedMembers</c>, <c>eligibleTargets</c>,
/// <c>evaluatedTargets</c>, and <c>unevaluatedMappedMembers</c> so the difference is
/// readable rather than implied.</para>
/// </summary>
static class SourceOracleCandidateLedger
{
    /// <summary>The candidate ledger's own report schema version.</summary>
    public const int LedgerVersion = 2;

    // ---------------------------------------------------------------- identities

    /// <summary>
    /// One checksum-pinned source-file identity. The same triple the enrolled
    /// source-oracle manifest registers, so a candidate promoted out of this ledger keys
    /// identically.
    /// </summary>
    internal readonly record struct FileIdentity(
        string SourceUrl,
        string ChecksumAlgorithm,
        string Checksum)
    {
        public static FileIdentity Create(string sourceUrl, string algorithm, string checksum)
            => new(sourceUrl, algorithm.ToUpperInvariant(), checksum.ToUpperInvariant());
    }

    /// <summary>One member identity, in the coordinates the oracle manifest uses.</summary>
    internal sealed record MemberIdentity(
        string Assembly,
        Guid ModuleVersionId,
        int MetadataToken,
        string Type,
        string Method,
        int Overload)
    {
        public override string ToString()
            => $"{Assembly}/{ModuleVersionId}:0x{MetadataToken:X8}:{Type}::{Method}#{Overload}";
    }

    /// <summary>
    /// One scanned assembly. The denominator every count in the report is relative to is
    /// exactly this set, so its identity is recorded rather than its path.
    /// </summary>
    internal sealed record ScannedAssembly(
        string Name,
        string Version,
        Guid ModuleVersionId,
        string Sha256);

    // ------------------------------------------------------------------ outcomes

    /// <summary>
    /// The four disjoint families a candidate rejection belongs to. The distinction that
    /// matters is <see cref="Acquisition"/> versus the rest: an acquisition reason means
    /// the target was never measured, so the file it belongs to is Unevaluable rather
    /// than Rejected.
    /// </summary>
    internal enum RejectionFamily
    {
        /// <summary>The target was measured and is structurally ineligible.</summary>
        Structural,

        /// <summary>The target could not be measured; the file is Unevaluable.</summary>
        Acquisition,

        /// <summary>The target was evaluated and the decompiled body is not good enough.</summary>
        Quality,

        /// <summary>The captured Printer body could not be inventoried.</summary>
        Inventory,
    }

    /// <summary>
    /// Every stable reason a file or one of its members is not a candidate. Codes are
    /// serialized, so they are contract; <see cref="Code"/> is the single spelling.
    /// </summary>
    internal enum CandidateReason
    {
        /// <summary>The file has no real-method target at all, so nothing would be gated.</summary>
        NoEligibleTargets,

        /// <summary>The authored member body could not be sliced out of the source.</summary>
        BodyExtractionFailed,

        /// <summary>No pre-normalization Printer body exists for the member.</summary>
        PrinterBodyIneligible,

        /// <summary>The member is not a compile-back-able target, so it cannot be judged.</summary>
        UnsupportedTarget,

        /// <summary>The decompiler did not produce a Full body that the source oracle can compare.</summary>
        DecompilerNotFull,

        /// <summary>The portable PDB maps no primary document for the member.</summary>
        NoPdbSourceMapping,

        /// <summary>The mapped document carries no URL plus checksum to pin content to.</summary>
        NoImmutableSourceIdentity,

        /// <summary>The authoritative source did not arrive.</summary>
        SourceUnavailable,

        /// <summary>Source acquisition failed outright.</summary>
        SourceAcquisitionFailed,

        /// <summary>The reconstructed C# does not compile and bind.</summary>
        NotValid,

        /// <summary>The reconstructed C# is valid but does not match the authored body.</summary>
        NotCorrect,

        /// <summary>The body matches only after source normalization.</summary>
        PrinterDifferent,

        /// <summary>No Printer comparison was recorded for the member.</summary>
        PrinterNotRecorded,

        /// <summary>The captured Printer body is at an unsupported comparison version.</summary>
        PrinterVersionUnsupported,

        /// <summary>A mapped eligible target produced no evaluation result at all.</summary>
        EvaluationMissing,

        /// <summary>The Printer body could not be parsed for its syntax inventory.</summary>
        InventoryParseFailed,
    }

    internal static RejectionFamily FamilyOf(CandidateReason reason)
        => reason switch
        {
            CandidateReason.NoEligibleTargets
                or CandidateReason.BodyExtractionFailed
                or CandidateReason.PrinterBodyIneligible
                or CandidateReason.UnsupportedTarget
                or CandidateReason.DecompilerNotFull => RejectionFamily.Structural,
            CandidateReason.NoPdbSourceMapping
                or CandidateReason.NoImmutableSourceIdentity
                or CandidateReason.SourceUnavailable
                or CandidateReason.SourceAcquisitionFailed
                or CandidateReason.EvaluationMissing => RejectionFamily.Acquisition,
            CandidateReason.NotValid
                or CandidateReason.NotCorrect
                or CandidateReason.PrinterDifferent
                or CandidateReason.PrinterNotRecorded
                or CandidateReason.PrinterVersionUnsupported => RejectionFamily.Quality,
            CandidateReason.InventoryParseFailed => RejectionFamily.Inventory,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unclassified candidate reason."),
        };

    internal static string Code(CandidateReason reason)
        => reason switch
        {
            CandidateReason.NoEligibleTargets => "no-eligible-targets",
            CandidateReason.BodyExtractionFailed => "body-extraction-failed",
            CandidateReason.PrinterBodyIneligible => "printer-body-ineligible",
            CandidateReason.UnsupportedTarget => "unsupported-target",
            CandidateReason.DecompilerNotFull => "decompiler-not-full",
            CandidateReason.NoPdbSourceMapping => "no-pdb-source-mapping",
            CandidateReason.NoImmutableSourceIdentity => "no-immutable-source-identity",
            CandidateReason.SourceUnavailable => "source-unavailable",
            CandidateReason.SourceAcquisitionFailed => "source-acquisition-failed",
            CandidateReason.NotValid => "not-valid",
            CandidateReason.NotCorrect => "not-correct",
            CandidateReason.PrinterDifferent => "printer-different",
            CandidateReason.PrinterNotRecorded => "printer-not-recorded",
            CandidateReason.PrinterVersionUnsupported => "printer-version-unsupported",
            CandidateReason.EvaluationMissing => "evaluation-missing",
            CandidateReason.InventoryParseFailed => "inventory-parse-failed",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unnamed candidate reason."),
        };

    /// <summary>A file's verdict for the scanned assembly set.</summary>
    internal enum CandidateStatus
    {
        /// <summary>
        /// The file qualifies now, and its recognized immutable source URL is already in
        /// the accepted baseline.
        /// </summary>
        Enrolled,

        /// <summary>Every eligible target is Printer exact at the supported version.</summary>
        Qualified,

        /// <summary>The file was measured and does not qualify.</summary>
        Rejected,

        /// <summary>At least one eligible target was never measured.</summary>
        Unevaluable,
    }

    // --------------------------------------------------------------- ledger input

    /// <summary>
    /// One primary portable-PDB member mapping, as read from the complete census
    /// <em>before</em> any source acquisition.
    /// </summary>
    /// <param name="File">
    /// The immutable identity of the member's primary document, or <see langword="null"/>
    /// when the PDB maps no primary document or the document has no immutable identity.
    /// </param>
    /// <param name="MappingReason">
    /// Why <paramref name="File"/> is absent. Non-null exactly when it is null.
    /// </param>
    internal sealed record CensusMember(
        MemberIdentity Member,
        FileIdentity? File,
        CandidateReason? MappingReason,
        bool Eligible);

    /// <summary>
    /// What one eligible, file-attributed target produced.
    /// </summary>
    /// <param name="Reason">
    /// <see langword="null"/> when the target was captured, evaluated, judged Printer
    /// exact, and inventoried. Otherwise the disqualifying reason.
    /// </param>
    /// <param name="Evaluated">
    /// Whether the target reached the source-oracle evaluation at all. A structural or
    /// acquisition rejection did not.
    /// </param>
    /// <param name="Features">
    /// The member's observed syntax features. Empty unless <paramref name="Reason"/> is
    /// null.
    /// </param>
    internal sealed record TargetOutcome(
        MemberIdentity Member,
        FileIdentity File,
        CandidateReason? Reason,
        bool Evaluated,
        IReadOnlyList<string> Features);

    /// <summary>Everything the pure ledger judges, with no source text and no paths.</summary>
    internal sealed record LedgerInput(
        IReadOnlyList<ScannedAssembly> Assemblies,
        IReadOnlyList<CensusMember> Members,
        IReadOnlyList<TargetOutcome> Outcomes);

    // -------------------------------------------------------------------- baseline

    /// <summary>
    /// The accepted enrolled-oracle benchmark evidence the ranking is incremental to.
    /// Provenance and text digest are retained, but no local path is carried into the
    /// candidate report.
    /// </summary>
    internal sealed record Baseline(
        string Digest,
        string Date,
        string Commit,
        string SourceStateAtBuild,
        bool SourceRevisionMatchesHead,
        bool SourceDirty,
        string? CorpusSha256,
        string? PoolSha256,
        int FilesRegistered,
        int SyntaxInventoryVersion,
        IReadOnlyList<string> Features,
        IReadOnlyList<string> EnrolledSourceUrls);

    // ---------------------------------------------------------------------- report

    internal sealed record ScannedAssemblyReport(
        [property: JsonRequired] string Name,
        [property: JsonRequired] string Version,
        [property: JsonRequired] string ModuleVersionId,
        [property: JsonRequired] string Sha256);

    internal sealed record BaselineReport(
        [property: JsonRequired] string Digest,
        [property: JsonRequired] string Date,
        [property: JsonRequired] string Commit,
        [property: JsonRequired] string SourceStateAtBuild,
        [property: JsonRequired] bool SourceRevisionMatchesHead,
        [property: JsonRequired] bool SourceDirty,
        [property: JsonRequired] string? CorpusSha256,
        [property: JsonRequired] string? PoolSha256,
        [property: JsonRequired] int FilesRegistered,
        [property: JsonRequired] int SyntaxInventoryVersion,
        [property: JsonRequired] int FeatureCount,
        [property: JsonRequired] IReadOnlyList<string> Features,
        [property: JsonRequired] IReadOnlyList<string> EnrolledSourceUrls);

    internal sealed record ReasonCountReport(
        [property: JsonRequired] string Name,
        [property: JsonRequired] int Count);

    internal sealed record CandidateFileReport(
        [property: JsonRequired] string SourceUrl,
        [property: JsonRequired] string ChecksumAlgorithm,
        [property: JsonRequired] string Checksum,
        [property: JsonRequired] string Status,
        [property: JsonRequired] string? RejectionFamily,
        [property: JsonRequired] IReadOnlyList<string> Reasons,
        [property: JsonRequired] int MappedMembers,
        [property: JsonRequired] int EligibleTargets,
        [property: JsonRequired] int EvaluatedTargets,
        [property: JsonRequired] int UnevaluatedMappedMembers,
        [property: JsonRequired] IReadOnlyList<string> Assemblies,
        [property: JsonRequired] bool SharedAcrossAssemblies,
        [property: JsonRequired] int? Rank,
        [property: JsonRequired] int? IncrementalFeatureCount,
        [property: JsonRequired] IReadOnlyList<string> IncrementalFeatures,
        [property: JsonRequired] IReadOnlyList<string> Features,
        [property: JsonRequired] IReadOnlyList<string> Members);

    internal sealed record UnattributedTargetReport(
        [property: JsonRequired] string Member,
        [property: JsonRequired] string Reason);

    internal sealed record Report(
        [property: JsonRequired] string Date,
        [property: JsonRequired] string Commit,
        [property: JsonRequired] string SourceStateAtBuild,
        [property: JsonRequired] bool SourceRevisionMatchesHead,
        [property: JsonRequired] bool SourceDirty,
        [property: JsonRequired] int LedgerVersion,
        [property: JsonRequired] int PrinterComparisonVersion,
        [property: JsonRequired] int SyntaxInventoryVersion,
        [property: JsonRequired] BaselineReport Baseline,
        [property: JsonRequired] IReadOnlyList<ScannedAssemblyReport> Assemblies,
        [property: JsonRequired] int MappedMembers,
        [property: JsonRequired] int EligibleTargets,
        [property: JsonRequired] int EvaluatedTargets,
        [property: JsonRequired] int FilesObserved,
        [property: JsonRequired] int FilesEnrolled,
        [property: JsonRequired] int FilesQualified,
        [property: JsonRequired] int FilesRejected,
        [property: JsonRequired] int FilesUnevaluable,
        [property: JsonRequired] int UnmappedEligibleTargets,
        [property: JsonRequired] int UnidentifiedSourceEligibleTargets,
        [property: JsonRequired] IReadOnlyList<ReasonCountReport> RejectionFamilies,
        [property: JsonRequired] IReadOnlyList<ReasonCountReport> RejectionReasons,
        [property: JsonRequired] IReadOnlyList<CandidateFileReport> Files,
        [property: JsonRequired] IReadOnlyList<UnattributedTargetReport> UnattributedTargets);

    // ------------------------------------------------------------------ pure build

    /// <summary>
    /// Judges every checksum-pinned file identity in <paramref name="input"/> and ranks the
    /// qualifying ones by incremental syntax coverage over
    /// <paramref name="baseline"/>.
    ///
    /// <para>Pure: no acquisition, no evaluation, no clock, no file system. Everything
    /// that decides a status or a rank is an argument, which is what lets the
    /// classification and ranking gates run in-process without a network.</para>
    /// </summary>
    internal static Report Build(
        LedgerInput input,
        Baseline baseline,
        AuthoredCorpusHistoryStore.BenchmarkProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(provenance);

        var outcomesByMember = new Dictionary<MemberIdentity, TargetOutcome>();
        foreach (var outcome in input.Outcomes)
        {
            if (!outcomesByMember.TryAdd(outcome.Member, outcome))
            {
                throw new ArgumentException(
                    $"Target {outcome.Member} was evaluated more than once.",
                    nameof(input));
            }
        }

        var files = new Dictionary<FileIdentity, FileAccumulator>();
        var unattributed = new List<UnattributedTargetReport>();
        foreach (var member in input.Members)
        {
            if (member.File is not { } file)
            {
                if (member.Eligible)
                {
                    unattributed.Add(new UnattributedTargetReport(
                        member.Member.ToString(),
                        Code(member.MappingReason
                            ?? CandidateReason.NoPdbSourceMapping)));
                }
                continue;
            }

            if (!files.TryGetValue(file, out var accumulator))
            {
                accumulator = new FileAccumulator(file);
                files[file] = accumulator;
            }

            accumulator.Add(member, outcomesByMember);
        }

        var enrolledSourceUrls = baseline.EnrolledSourceUrls.ToHashSet(StringComparer.Ordinal);
        var judged = files.Values
            .Select(accumulator => accumulator.Judge())
            .Select(file =>
                file.Status == CandidateStatus.Qualified
                    && enrolledSourceUrls.Contains(file.File.SourceUrl)
                    && SourceLinkUrls.IsImmutable(file.File.SourceUrl)
                        ? file with { Status = CandidateStatus.Enrolled }
                        : file)
            .ToList();

        var ranking = Rank(judged, baseline.Features);

        var reasonCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var familyCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in judged)
        {
            foreach (var reason in file.Reasons)
            {
                reasonCounts.TryGetValue(Code(reason), out int count);
                reasonCounts[Code(reason)] = count + 1;
            }

            if (file.Family is { } family)
            {
                familyCounts.TryGetValue(family.ToString(), out int count);
                familyCounts[family.ToString()] = count + 1;
            }
        }

        var fileReports = judged
            .Select(file => file.ToReport(ranking.GetValueOrDefault(file.File)))
            .OrderBy(report => report.Rank is null ? 1 : 0)
            .ThenBy(report => report.Rank ?? 0)
            .ThenBy(report => report.Status, StringComparer.Ordinal)
            .ThenBy(report => report.SourceUrl, StringComparer.Ordinal)
            .ThenBy(report => report.ChecksumAlgorithm, StringComparer.Ordinal)
            .ThenBy(report => report.Checksum, StringComparer.Ordinal)
            .ToArray();

        return new Report(
            provenance.Date,
            provenance.Commit,
            provenance.SourceStateAtBuild,
            provenance.SourceRevisionMatchesHead,
            provenance.SourceDirty,
            LedgerVersion,
            AuthoredSourceOracleManifest.PrinterComparisonVersion,
            PrinterSyntaxInventory.Version,
            new BaselineReport(
                baseline.Digest,
                baseline.Date,
                baseline.Commit,
                baseline.SourceStateAtBuild,
                baseline.SourceRevisionMatchesHead,
                baseline.SourceDirty,
                baseline.CorpusSha256,
                baseline.PoolSha256,
                baseline.FilesRegistered,
                baseline.SyntaxInventoryVersion,
                baseline.Features.Count,
                baseline.Features,
                baseline.EnrolledSourceUrls),
            [.. input.Assemblies
                .Select(assembly => new ScannedAssemblyReport(
                    assembly.Name,
                    assembly.Version,
                    assembly.ModuleVersionId.ToString(),
                    assembly.Sha256))
                .OrderBy(assembly => assembly.Name, StringComparer.Ordinal)
                .ThenBy(assembly => assembly.ModuleVersionId, StringComparer.Ordinal)],
            input.Members.Count(member =>
                member.MappingReason != CandidateReason.NoPdbSourceMapping),
            input.Members.Count(member => member.Eligible),
            input.Outcomes.Count(outcome => outcome.Evaluated),
            judged.Count,
            judged.Count(file => file.Status == CandidateStatus.Enrolled),
            judged.Count(file => file.Status == CandidateStatus.Qualified),
            judged.Count(file => file.Status == CandidateStatus.Rejected),
            judged.Count(file => file.Status == CandidateStatus.Unevaluable),
            unattributed.Count(row => row.Reason == Code(CandidateReason.NoPdbSourceMapping)),
            unattributed.Count(row => row.Reason == Code(CandidateReason.NoImmutableSourceIdentity)),
            [.. familyCounts.Select(entry => new ReasonCountReport(entry.Key, entry.Value))],
            [.. reasonCounts.Select(entry => new ReasonCountReport(entry.Key, entry.Value))],
            fileReports,
            [.. unattributed
                .OrderBy(row => row.Member, StringComparer.Ordinal)
                .ThenBy(row => row.Reason, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Greedy maximum-incremental-coverage ranking, seeded with the baseline's observed
    /// features.
    ///
    /// <para>Deterministic by construction: ties break on total feature count descending,
    /// then eligible-target count descending, then source URL ordinal. A file whose every
    /// feature is already covered still ranks, after every positive-gain file, because
    /// the choice is gain-first.</para>
    /// </summary>
    static Dictionary<FileIdentity, RankAssignment> Rank(
        IReadOnlyList<JudgedFile> judged,
        IReadOnlyList<string> baselineFeatures)
    {
        var covered = new HashSet<string>(baselineFeatures, StringComparer.Ordinal);
        var remaining = judged
            .Where(file => file.Status == CandidateStatus.Qualified)
            .ToList();
        var ranking = new Dictionary<FileIdentity, RankAssignment>();

        int rank = 1;
        while (remaining.Count > 0)
        {
            JudgedFile? best = null;
            int bestGain = -1;
            foreach (var file in remaining)
            {
                int gain = file.Features.Count(feature => !covered.Contains(feature));
                if (best is null
                    || gain > bestGain
                    || (gain == bestGain && IsPreferredTieBreak(file, best)))
                {
                    best = file;
                    bestGain = gain;
                }
            }

            var selected = best!;
            string[] incremental = [.. selected.Features
                .Where(feature => !covered.Contains(feature))
                .Order(StringComparer.Ordinal)];
            ranking[selected.File] = new RankAssignment(rank, incremental);
            covered.UnionWith(selected.Features);
            remaining.Remove(selected);
            rank++;
        }

        return ranking;
    }

    static bool IsPreferredTieBreak(JudgedFile candidate, JudgedFile incumbent)
    {
        if (candidate.Features.Count != incumbent.Features.Count)
            return candidate.Features.Count > incumbent.Features.Count;
        if (candidate.EligibleTargets != incumbent.EligibleTargets)
            return candidate.EligibleTargets > incumbent.EligibleTargets;
        int result = string.CompareOrdinal(
            candidate.File.SourceUrl,
            incumbent.File.SourceUrl);
        if (result != 0)
            return result < 0;
        result = string.CompareOrdinal(
            candidate.File.ChecksumAlgorithm,
            incumbent.File.ChecksumAlgorithm);
        return result != 0
            ? result < 0
            : string.CompareOrdinal(
                candidate.File.Checksum,
                incumbent.File.Checksum) < 0;
    }

    sealed record RankAssignment(int Rank, IReadOnlyList<string> IncrementalFeatures);

    sealed record JudgedFile(
        FileIdentity File,
        CandidateStatus Status,
        RejectionFamily? Family,
        IReadOnlyList<CandidateReason> Reasons,
        int MappedMembers,
        int EligibleTargets,
        int EvaluatedTargets,
        IReadOnlyList<string> Assemblies,
        bool SharedAcrossAssemblies,
        IReadOnlyList<string> Features,
        IReadOnlyList<string> Members)
    {
        public CandidateFileReport ToReport(RankAssignment? rank)
            => new(
                File.SourceUrl,
                File.ChecksumAlgorithm,
                File.Checksum,
                Status.ToString(),
                Family?.ToString(),
                [.. Reasons.Select(Code)],
                MappedMembers,
                EligibleTargets,
                EvaluatedTargets,
                MappedMembers - EvaluatedTargets,
                Assemblies,
                SharedAcrossAssemblies,
                rank?.Rank,
                rank is null ? null : rank.IncrementalFeatures.Count,
                rank?.IncrementalFeatures ?? [],
                Features,
                Members);
    }

    sealed class FileAccumulator(FileIdentity file)
    {
        readonly List<CensusMember> _members = [];
        readonly List<TargetOutcome> _outcomes = [];
        readonly SortedSet<string> _assemblies = new(StringComparer.Ordinal);
        readonly HashSet<Guid> _modules = [];

        public void Add(
            CensusMember member,
            IReadOnlyDictionary<MemberIdentity, TargetOutcome> outcomes)
        {
            _members.Add(member);
            _assemblies.Add(member.Member.Assembly);
            _modules.Add(member.Member.ModuleVersionId);
            if (!member.Eligible)
                return;

            // A mapped eligible target with no outcome is not a shorter denominator: it is
            // a member nobody looked at, which makes the file Unevaluable.
            _outcomes.Add(
                outcomes.TryGetValue(member.Member, out var outcome)
                    ? outcome
                    : new TargetOutcome(
                        member.Member,
                        file,
                        CandidateReason.EvaluationMissing,
                        Evaluated: false,
                        Features: []));
        }

        public JudgedFile Judge()
        {
            var reasons = new SortedSet<CandidateReason>();
            if (_outcomes.Count == 0)
                reasons.Add(CandidateReason.NoEligibleTargets);
            foreach (var outcome in _outcomes)
            {
                if (outcome.Reason is { } reason)
                    reasons.Add(reason);
            }

            RejectionFamily? family = reasons.Count == 0
                ? null
                : DominantFamily(reasons);
            CandidateStatus status = family switch
            {
                null => CandidateStatus.Qualified,
                RejectionFamily.Acquisition => CandidateStatus.Unevaluable,
                _ => CandidateStatus.Rejected,
            };

            var features = new SortedSet<string>(StringComparer.Ordinal);
            if (status == CandidateStatus.Qualified)
            {
                foreach (var outcome in _outcomes)
                    features.UnionWith(outcome.Features);
            }

            return new JudgedFile(
                file,
                status,
                family,
                [.. reasons],
                _members.Count,
                _outcomes.Count,
                _outcomes.Count(outcome => outcome.Evaluated),
                [.. _assemblies],
                _assemblies.Count > 1 || _modules.Count > 1,
                [.. features],
                [.. _outcomes
                    .Select(outcome => outcome.Member.ToString())
                    .Order(StringComparer.Ordinal)]);
        }

        /// <summary>
        /// Which family decides the file's status. Acquisition wins: while any eligible
        /// target is unmeasured, the file has not been shown to fail anything.
        /// </summary>
        static RejectionFamily DominantFamily(IEnumerable<CandidateReason> reasons)
        {
            var families = reasons.Select(FamilyOf).ToArray();
            if (families.Contains(RejectionFamily.Acquisition))
                return RejectionFamily.Acquisition;
            if (families.Contains(RejectionFamily.Structural))
                return RejectionFamily.Structural;
            return families.Contains(RejectionFamily.Quality)
                ? RejectionFamily.Quality
                : RejectionFamily.Inventory;
        }
    }

    // ------------------------------------------------------------- baseline intake

    /// <summary>
    /// Reads the baseline authored-corpus benchmark report and accepts it only when it
    /// carries the required enrolled-oracle measurement evidence.
    ///
    /// <para>The baseline is the benchmark report, not the manifest, and the distinction
    /// is the whole point: a manifest declares what someone expects to be enrolled, while
    /// a passing report with an evaluated syntax inventory is the observed feature set an
    /// enrolled run actually produced. Ranking against declarations would let a candidate
    /// claim incremental coverage over features nothing has ever demonstrated.</para>
    /// </summary>
    internal static bool TryReadBaseline(
        string path,
        out Baseline? baseline,
        out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        baseline = null;
        error = null;

        if (!File.Exists(path))
        {
            error = $"Baseline source-oracle report not found: {path}";
            return false;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Baseline source-oracle report could not be read: {path}: {ex.Message}";
            return false;
        }

        return TryParseBaseline(text, Digest(text), out baseline, out error);
    }

    /// <summary>
    /// The acceptance contract, separated from the file read so it is directly testable:
    /// every rejection below is a way an incomplete or contradictory report could
    /// otherwise be mistaken for enrolled evidence.
    /// </summary>
    internal static bool TryParseBaseline(
        string text,
        string digest,
        out Baseline? baseline,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(text);
        baseline = null;
        error = null;

        AuthoredCorpusBenchmark.Report? report;
        try
        {
            report = JsonSerializer.Deserialize<AuthoredCorpusBenchmark.Report>(
                text,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    AllowDuplicateProperties = false,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                });
        }
        catch (JsonException ex)
        {
            error = $"Baseline source-oracle report is not a current benchmark report: {ex.Message}";
            return false;
        }

        if (report is null)
        {
            error = "Baseline source-oracle report is empty.";
            return false;
        }

        if (!AuthoredCorpusExitContract.ReportInputsAreComplete(report))
        {
            error = "Baseline source-oracle report did not measure complete inputs, "
                + "so its observed features are not the enrolled set.";
            return false;
        }

        if (report.SourceOracleManifest is not { } manifest)
        {
            error = "Baseline source-oracle report judged no source-oracle manifest, "
                + "so no file is enrolled in it.";
            return false;
        }

        if (!manifest.Passed)
        {
            error = "Baseline source-oracle report did not pass its source-oracle gate.";
            return false;
        }
        if (manifest.FilesRegistered <= 0)
        {
            error = "Baseline source-oracle report must contain at least one enrolled file.";
            return false;
        }
        if (manifest.Failures is not { Count: 0 })
        {
            error = "Baseline source-oracle report claims to pass while retaining failures.";
            return false;
        }
        if (manifest.FilesValid != manifest.FilesRegistered
            || manifest.FilesCorrect != manifest.FilesRegistered
            || manifest.PrinterExactRequired != manifest.FilesRegistered
            || manifest.PrinterExactPassing != manifest.FilesRegistered)
        {
            error = "Baseline source-oracle report claims to pass with contradictory "
                + "Valid, Correct, or Printer-exact file counts.";
            return false;
        }
        if (manifest.FilesRegistered > report.TargetsEvaluated
            || manifest.FilesRegistered > report.Correct
            || manifest.FilesRegistered > report.PrinterExact)
        {
            error = "Baseline source-oracle report claims more enrolled files than "
                + "its evaluated, Correct, or Printer-exact row evidence can support.";
            return false;
        }

        if (manifest.SyntaxInventoryEvaluated != true)
        {
            error = "Baseline source-oracle report did not evaluate a syntax inventory, "
                + "so it publishes no observed feature set to rank against.";
            return false;
        }

        if (manifest.SyntaxInventoryVersion != PrinterSyntaxInventory.Version)
        {
            error = $"Baseline syntax inventory version "
                + $"{manifest.SyntaxInventoryVersion?.ToString() ?? "<absent>"} is unsupported; "
                + $"expected {PrinterSyntaxInventory.Version}.";
            return false;
        }

        if (report.PrinterComparisonVersion != AuthoredSourceOracleManifest.PrinterComparisonVersion)
        {
            error = $"Baseline printer comparison version {report.PrinterComparisonVersion} "
                + $"is unsupported; expected "
                + $"{AuthoredSourceOracleManifest.PrinterComparisonVersion}.";
            return false;
        }

        if (manifest.ObservedFeatures is not { } features)
        {
            error = "Baseline source-oracle report publishes no observed features.";
            return false;
        }

        if (features.Any(string.IsNullOrWhiteSpace))
        {
            error = "Baseline observed features contain an empty feature.";
            return false;
        }

        if (features.Distinct(StringComparer.Ordinal).Count() != features.Count)
        {
            error = "Baseline observed features contain a duplicate.";
            return false;
        }

        if (!features.SequenceEqual(features.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            error = "Baseline observed features are not ordinal-sorted.";
            return false;
        }

        if (manifest.FileInventory is not { } fileInventory
            || fileInventory.Count != manifest.FilesRegistered)
        {
            error = "Baseline source-oracle report does not identify every enrolled file.";
            return false;
        }
        if (manifest.FilesInventoryTracked != manifest.FilesRegistered)
        {
            error = "Baseline source-oracle report did not track syntax inventory for "
                + "every enrolled file.";
            return false;
        }
        if (fileInventory.Any(file =>
            file is null
            || !file.PrinterExact
            || string.IsNullOrWhiteSpace(file.SourceUrl)))
        {
            error = "Baseline source-oracle report contains a file that is not "
                + "Printer exact or lacks a source URL.";
            return false;
        }
        string[] enrolledSourceUrls = [.. fileInventory
            .Select(static file => file.SourceUrl)
            .Order(StringComparer.Ordinal)];
        if (enrolledSourceUrls.Distinct(StringComparer.Ordinal).Count()
            != enrolledSourceUrls.Length)
        {
            error = "Baseline source-oracle report contains a duplicate enrolled source URL.";
            return false;
        }
        var supportedSourceUrls = report.Rows
            .Where(static row =>
                string.Equals(
                    row.Outcome,
                    nameof(ReturnToSenderSourceOutcome.ValidMatch),
                    StringComparison.Ordinal)
                && string.Equals(
                    row.PrinterExact,
                    nameof(PrinterExactOutcome.Exact),
                    StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(row.SourceFile))
            .Select(static row => row.SourceFile!)
            .ToHashSet(StringComparer.Ordinal);
        if (enrolledSourceUrls.Any(sourceUrl => !supportedSourceUrls.Contains(sourceUrl)))
        {
            error = "Baseline source-oracle report contains an enrolled file without "
                + "Correct, Printer-exact row evidence naming its source URL.";
            return false;
        }

        var fileFeatureUnion = new SortedSet<string>(StringComparer.Ordinal);
        foreach (AuthoredSourceOracleManifest.FileInventoryEntry file in fileInventory)
        {
            if (file.Features is not { } fileFeatures
                || fileFeatures.Count == 0
                || fileFeatures.Any(string.IsNullOrWhiteSpace))
            {
                error = "Baseline source-oracle report contains an enrolled file with "
                    + "an absent or empty syntax feature set.";
                return false;
            }
            if (fileFeatures.Distinct(StringComparer.Ordinal).Count()
                != fileFeatures.Count)
            {
                error = "Baseline source-oracle report contains duplicate per-file "
                    + "syntax features.";
                return false;
            }
            if (!fileFeatures.SequenceEqual(
                    fileFeatures.Order(StringComparer.Ordinal),
                    StringComparer.Ordinal))
            {
                error = "Baseline source-oracle report contains per-file syntax features "
                    + "that are not ordinal-sorted.";
                return false;
            }
            fileFeatureUnion.UnionWith(fileFeatures);
        }
        if (!features.SequenceEqual(fileFeatureUnion, StringComparer.Ordinal))
        {
            error = "Baseline observed features do not equal the union of enrolled-file "
                + "syntax features.";
            return false;
        }

        baseline = new Baseline(
            digest,
            report.Date,
            report.Commit,
            report.SourceStateAtBuild,
            report.SourceRevisionMatchesHead,
            report.SourceDirty,
            report.CorpusSha256,
            report.PoolSha256,
            manifest.FilesRegistered,
            manifest.SyntaxInventoryVersion.Value,
            features,
            enrolledSourceUrls);
        return true;
    }

    // ------------------------------------------------------------------ PDB census

    /// <summary>
    /// Whether a mapped document can pin content immutably: a resolved URL plus a
    /// checksum algorithm and checksum.
    /// </summary>
    internal static bool HasImmutableIdentity(SourceDocumentObservation? document)
        => document is { ResolvedUrl.Length: > 0, ChecksumAlgorithm.Length: > 0, Checksum.Length: > 0 };

    /// <summary>
    /// The complete MethodDef to primary-document census for one assembly, computed from
    /// the portable PDB before any source is acquired.
    ///
    /// <para>Primary selection matches
    /// <see cref="PdbSourceAcquisition.AcquireMemberAsync"/> exactly —
    /// <c>IsPrimaryDocument</c> descending, then <c>DocumentRowId</c> — because a census
    /// that picked a different document than acquisition would attribute a member's
    /// result to a file the acquisition never read.</para>
    /// </summary>
    internal static bool TryCensus(
        SourceLinkService source,
        ScannedAssembly assembly,
        IReadOnlyDictionary<int, RealMethodTargetEnumerator.RealMethodTarget> eligibleTargets,
        out IReadOnlyList<CensusMember> members,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(eligibleTargets);

        members = [];
        error = null;
        var subject = new FindingSubject(assembly.Name, assembly.Name);

        var memberInspection = SourceLinkFindings.InspectMemberSources(source, subject, query: null);
        if (memberInspection.Value is FindingInspection<MemberSourceObservation>.Absent memberAbsent)
        {
            error = $"{assembly.Name}: portable-PDB member source mapping is unavailable "
                + $"({memberAbsent.Detail ?? "no detail"}).";
            return false;
        }
        if (memberInspection.Value is FindingInspection<MemberSourceObservation>.Failed memberFailed)
        {
            error = $"{assembly.Name}: portable-PDB member source mapping failed "
                + $"({memberFailed.Error.Reason}).";
            return false;
        }

        var documentInspection = SourceLinkFindings.InspectSourceDocuments(source, subject, query: null);
        if (documentInspection.Value is FindingInspection<SourceDocumentObservation>.Absent documentAbsent)
        {
            error = $"{assembly.Name}: portable-PDB source documents are unavailable "
                + $"({documentAbsent.Detail ?? "no detail"}).";
            return false;
        }
        if (documentInspection.Value is FindingInspection<SourceDocumentObservation>.Failed documentFailed)
        {
            error = $"{assembly.Name}: portable-PDB source documents failed "
                + $"({documentFailed.Error.Reason}).";
            return false;
        }

        var documents = ((FindingInspection<SourceDocumentObservation>.Complete)documentInspection.Value)
            .Findings
            .Select(static finding => finding.Payload)
            .ToArray();
        var mappings = ((FindingInspection<MemberSourceObservation>.Complete)memberInspection.Value)
            .Findings
            .Select(static finding => finding.Payload)
            .ToArray();

        var census = new List<CensusMember>();
        var mappedTokens = new HashSet<int>();
        foreach (var group in mappings
            .GroupBy(static mapping => mapping.MetadataToken)
            .OrderBy(static group => group.Key))
        {
            mappedTokens.Add(group.Key);
            var primary = group
                .OrderByDescending(static mapping => mapping.IsPrimaryDocument)
                .ThenBy(static mapping => mapping.DocumentRowId)
                .First();
            // An eligible target's identity is the one the oracle keys on; a mapped member
            // that is not a real-method target still counts toward the file's mapped-member
            // scope, and is named from its PDB anchor.
            bool eligible = eligibleTargets.TryGetValue(
                group.Key,
                out RealMethodTargetEnumerator.RealMethodTarget? target);
            var member = new MemberIdentity(
                assembly.Name,
                assembly.ModuleVersionId,
                group.Key,
                target?.Type ?? primary.Anchor.TypeFullName,
                target?.Method ?? primary.Anchor.MemberName,
                target?.Overload ?? 0);
            var document = SelectMappedDocument(primary, documents);
            census.Add(HasImmutableIdentity(document)
                ? new CensusMember(
                    member,
                    FileIdentity.Create(
                        document!.ResolvedUrl!,
                        document.ChecksumAlgorithm!,
                        document.Checksum!),
                    null,
                    eligible)
                : new CensusMember(
                    member,
                    null,
                    CandidateReason.NoImmutableSourceIdentity,
                    eligible));
        }

        foreach (var entry in eligibleTargets
            .Where(entry => !mappedTokens.Contains(entry.Key))
            .OrderBy(static entry => entry.Key))
        {
            RealMethodTargetEnumerator.RealMethodTarget target = entry.Value;
            census.Add(new CensusMember(
                new MemberIdentity(
                    assembly.Name,
                    assembly.ModuleVersionId,
                    target.MetadataToken,
                    target.Type,
                    target.Method,
                    target.Overload),
                null,
                CandidateReason.NoPdbSourceMapping,
                Eligible: true));
        }

        members = [.. census.OrderBy(static member => member.Member.MetadataToken)];
        return true;
    }

    /// <summary>
    /// The single document a mapping points at, or <see langword="null"/> when the
    /// portable PDB does not identify one uniquely. Mirrors
    /// <c>PdbSourceAcquisition.SelectMappedDocument</c>, which is internal to the
    /// services assembly.
    /// </summary>
    static SourceDocumentObservation? SelectMappedDocument(
        MemberSourceObservation mapping,
        IReadOnlyList<SourceDocumentObservation> documents)
    {
        SourceDocumentObservation? match = null;
        foreach (var candidate in documents)
        {
            if (candidate.DocumentRowId != mapping.DocumentRowId
                || !string.Equals(candidate.OriginalPath, mapping.OriginalPath, StringComparison.Ordinal))
            {
                continue;
            }

            if (match is not null)
                return null;

            match = candidate;
        }

        return match;
    }

    // ------------------------------------------------------------------- text card

    internal static void WriteCard(Report report, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(output);

        output.WriteLine("SOURCE-ORACLE CANDIDATE LEDGER");
        output.WriteLine();
        output.WriteLine($"  assemblies scanned    : {report.Assemblies.Count}");
        foreach (var assembly in report.Assemblies)
            output.WriteLine($"    {assembly.Name} {assembly.Version} {assembly.ModuleVersionId}");
        output.WriteLine($"  mapped members        : {report.MappedMembers}");
        output.WriteLine($"  eligible targets      : {report.EligibleTargets}");
        output.WriteLine($"  evaluated targets     : {report.EvaluatedTargets}");
        output.WriteLine($"  unmapped eligible     : {report.UnmappedEligibleTargets}");
        output.WriteLine($"  unidentified source   : {report.UnidentifiedSourceEligibleTargets}");
        output.WriteLine();
        output.WriteLine($"  files observed        : {report.FilesObserved}");
        output.WriteLine($"    already enrolled    : {report.FilesEnrolled}");
        output.WriteLine($"    qualified           : {report.FilesQualified}");
        output.WriteLine($"    rejected            : {report.FilesRejected}");
        output.WriteLine($"    unevaluable         : {report.FilesUnevaluable}");
        output.WriteLine();
        output.WriteLine("  Rejection families (files):");
        if (report.RejectionFamilies.Count == 0)
        {
            output.WriteLine("    (none)");
        }
        else
        {
            foreach (var family in report.RejectionFamilies)
                output.WriteLine($"    {family.Name,-14} : {family.Count}");
        }

        output.WriteLine();
        output.WriteLine("  Rejection reasons (files):");
        if (report.RejectionReasons.Count == 0)
        {
            output.WriteLine("    (none)");
        }
        else
        {
            foreach (var reason in report.RejectionReasons)
                output.WriteLine($"    {reason.Name,-28} : {reason.Count}");
        }

        output.WriteLine();
        output.WriteLine(
            $"  baseline features     : {report.Baseline.FeatureCount} "
            + $"(commit {ShortCommit(report.Baseline.Commit)}, "
            + $"{report.Baseline.FilesRegistered} enrolled file(s))");
        output.WriteLine();
        output.WriteLine("  Ranked qualified candidates:");
        var ranked = report.Files.Where(file => file.Rank is not null).ToArray();
        if (ranked.Length == 0)
        {
            output.WriteLine("    (none — no file qualified for this assembly set)");
        }
        else
        {
            foreach (var file in ranked)
            {
                output.WriteLine(
                    $"    #{file.Rank,-3} +{file.IncrementalFeatureCount} new / "
                    + $"{file.Features.Count} total feature(s), "
                    + $"{file.EligibleTargets} eligible member(s)"
                    + (file.SharedAcrossAssemblies ? " [shared]" : ""));
                output.WriteLine($"          {file.SourceUrl}");
            }
        }
    }

    static string ShortCommit(string commit)
        => commit.Length > 8 ? commit[..8] : commit;

    internal static string SerializeReport(Report report)
        => JsonSerializer.Serialize(
            report,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            });

    // ------------------------------------------------------------------- execution

    public static int Run(
        IReadOnlyList<string> assemblies,
        string baselineReportPath,
        bool json,
        IReadOnlyList<string>? repositoryPaths = null,
        TextWriter? output = null)
        => RunAsync(assemblies, baselineReportPath, json, repositoryPaths, output)
            .GetAwaiter()
            .GetResult();

    static async Task<int> RunAsync(
        IReadOnlyList<string> assemblies,
        string baselineReportPath,
        bool json,
        IReadOnlyList<string>? repositoryPaths,
        TextWriter? output)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        output ??= Console.Out;

        AuthoredCorpusHistoryStore.BenchmarkProvenance provenance;
        try
        {
            provenance = AuthoredCorpusHistoryStore.CaptureBenchmarkProvenance();
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"Could not establish candidate-ledger provenance: {ex.Message}");
            return 1;
        }

        if (!TryReadBaseline(baselineReportPath, out Baseline? baseline, out string? baselineError))
        {
            Console.Error.WriteLine(baselineError);
            return 1;
        }

        if (assemblies.Count == 0)
        {
            Console.Error.WriteLine("The candidate ledger requires at least one input assembly.");
            return 1;
        }

        HttpClientFactory.Initialize(new HttpClientFactoryOptions());
        using var httpClient = HttpClientFactory.CreateClient();
        var fetcher = new SourceFetcher(HttpClientFactory.SharedUntrustedFetch);

        var scanned = new List<ScannedAssembly>();
        var members = new List<CensusMember>();
        var outcomes = new List<TargetOutcome>();

        foreach (string assemblyPath in assemblies)
        {
            if (!await ScanAssemblyAsync(
                    assemblyPath,
                    httpClient,
                    fetcher,
                    repositoryPaths,
                    scanned,
                    members,
                    outcomes))
            {
                return 1;
            }
        }

        if (MeasurementFailure(
                scanned.Count,
                members.Count(member => member.Eligible && member.File is not null),
                outcomes.Count(outcome => outcome.Evaluated)) is { } failure)
        {
            Console.Error.WriteLine(failure);
            return 1;
        }

        var report = Build(
            new LedgerInput(scanned, members, outcomes),
            baseline!,
            provenance);

        if (json)
            output.WriteLine(SerializeReport(report));
        else
            WriteCard(report, output);

        return 0;
    }

    /// <summary>
    /// The measurement-integrity failure this run owes, or <see langword="null"/> when it
    /// measured what it claims to have measured.
    ///
    /// <para>A pure function, and separate from the candidate verdicts, because these are
    /// the only conditions that make the exit code non-zero. Everything else the ledger
    /// finds — a rejected file, an outage that leaves a file Unevaluable — is data. A run
    /// that decided nothing at all is not: reporting "0 qualified" out of nothing
    /// measured would read exactly like a scan that found no good candidates.</para>
    /// </summary>
    internal static string? MeasurementFailure(
        int scannedAssemblies,
        int fileAttributedEligibleTargets,
        int evaluatedTargets)
    {
        if (scannedAssemblies == 0)
            return "No input assembly produced a usable PDB census, so nothing was measured.";
        if (fileAttributedEligibleTargets == 0)
        {
            return "No checksum-identified source file carried an eligible target, "
                + "so there was no candidate denominator to measure.";
        }

        return evaluatedTargets == 0
            ? "No checksum-identified file was evaluated: every eligible target failed "
                + "before the source oracle, so the run decided nothing."
            : null;
    }

    /// <summary>
    /// Scans one assembly: enumerate targets, census the PDB, attempt every eligible
    /// mapped target, evaluate the captured rows, and classify each outcome. Returns
    /// <see langword="false"/> for a measurement-integrity failure only.
    /// </summary>
    static async Task<bool> ScanAssemblyAsync(
        string assemblyPath,
        HttpClient httpClient,
        SourceFetcher fetcher,
        IReadOnlyList<string>? repositoryPaths,
        List<ScannedAssembly> scanned,
        List<CensusMember> members,
        List<TargetOutcome> outcomes)
    {
        IReadOnlyList<RealMethodTargetEnumerator.RealMethodTarget> targets;
        try
        {
            targets = RealMethodTargetEnumerator.Enumerate(assemblyPath);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or BadImageFormatException
            or InvalidOperationException)
        {
            Console.Error.WriteLine(
                $"Could not enumerate real-method targets in '{assemblyPath}' "
                + $"({ex.GetType().Name}: {ex.Message}).");
            return false;
        }

        if (targets.Count == 0)
        {
            Console.Error.WriteLine($"'{assemblyPath}' has no real-method target to measure.");
            return false;
        }

        (string name, string version) = AuthoredSourceHarvest.ReadAssemblyIdentity(assemblyPath);
        ScannedAssembly assembly;
        try
        {
            assembly = new ScannedAssembly(
                name,
                version,
                AuthoredSourceHarvest.ReadModuleVersionId(assemblyPath),
                Digest(await File.ReadAllBytesAsync(assemblyPath)));
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or BadImageFormatException)
        {
            Console.Error.WriteLine(
                $"Could not identify '{assemblyPath}' ({ex.GetType().Name}: {ex.Message}).");
            return false;
        }

        SourceLinkService source;
        try
        {
            source = SourceLinkService.Open(assemblyPath);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or BadImageFormatException
            or InvalidOperationException)
        {
            Console.Error.WriteLine(
                $"Could not open source metadata for '{assemblyPath}' "
                + $"({ex.GetType().Name}: {ex.Message}).");
            return false;
        }
        using var sourceScope = source;
        try
        {
            await AuthoredRebuildFidelity.AcquirePdbAsync(source, httpClient);
        }
        catch (Exception ex) when (
            AuthoredRebuildFidelity.IsPdbAcquisitionFailure(ex))
        {
            Console.Error.WriteLine(
                $"Could not acquire a portable PDB for '{assemblyPath}' "
                + $"({ex.GetType().Name}: {ex.Message}).");
            return false;
        }

        var byToken = targets.ToDictionary(static target => target.MetadataToken);
        if (!TryCensus(
                source,
                assembly,
                byToken,
                out IReadOnlyList<CensusMember> census,
                out string? censusError))
        {
            Console.Error.WriteLine(censusError);
            return false;
        }

        if (scanned.Any(existing =>
            existing.ModuleVersionId == assembly.ModuleVersionId))
        {
            Console.Error.WriteLine(
                $"Assembly MVID {assembly.ModuleVersionId} was supplied more than once.");
            return false;
        }

        scanned.Add(assembly);
        members.AddRange(census);

        var identity = new AuthoredSourceHarvest.HarvestIdentity(
            assembly.Name,
            assembly.Version,
            assembly.ModuleVersionId,
            AuthoredSourceHarvest.InferTfm(assemblyPath));
        var captured = new List<AuthoredSourceHarvest.CorpusRecord>();
        var capturedMembers = new List<(MemberIdentity Member, FileIdentity File)>();

        foreach (var member in census)
        {
            if (!member.Eligible || member.File is not { } file)
                continue;

            var attempt = await AuthoredSourceHarvest.TryHarvestAsync(
                source,
                identity,
                byToken[member.Member.MetadataToken],
                fetcher,
                evil: false,
                repositoryPaths);
            if (attempt.Record is not { } record)
            {
                if (attempt.Reason is CandidateReason.NoPdbSourceMapping
                    or CandidateReason.NoImmutableSourceIdentity)
                {
                    Console.Error.WriteLine(
                        $"{member.Member}: acquisition contradicted the PDB census "
                        + $"({Code(attempt.Reason.Value)}).");
                    return false;
                }
                outcomes.Add(new TargetOutcome(
                    member.Member,
                    file,
                    attempt.Reason
                        ?? throw new InvalidOperationException(
                            "A harvest attempt without a record must carry a reason."),
                    Evaluated: false,
                    Features: []));
                continue;
            }

            if (record.SourceUrl is not { Length: > 0 }
                || record.ChecksumAlgorithm is not { Length: > 0 }
                || record.Checksum is not { Length: > 0 })
            {
                Console.Error.WriteLine(
                    $"{member.Member}: a captured row lost its immutable source identity.");
                return false;
            }
            FileIdentity capturedFile = FileIdentity.Create(
                record.SourceUrl,
                record.ChecksumAlgorithm,
                record.Checksum);
            if (capturedFile != file)
            {
                Console.Error.WriteLine(
                    $"{member.Member}: acquisition resolved a different immutable file "
                    + "than the PDB census.");
                return false;
            }

            captured.Add(record);
            capturedMembers.Add((member.Member, file));
        }

        if (captured.Count == 0)
            return true;

        if (!AuthoredCorpusSourceEvaluator.TryEvaluate(
                assemblyPath,
                captured,
                out IReadOnlyList<AuthoredSourceOracleManifest.EvaluatedRow> rows,
                out string? evaluationError))
        {
            Console.Error.WriteLine(evaluationError);
            return false;
        }

        if (rows.Any(row =>
            row.Result.Outcome == ReturnToSenderSourceOutcome.SourceUnavailable
            && IsComparableCompileBackStatus(row.Result.CompileBackStatus)))
        {
            Console.Error.WriteLine(
                $"{assembly.Name}: source-oracle evaluation lost a correlated "
                + "captured source row.");
            return false;
        }

        for (int i = 0; i < rows.Count; i++)
            outcomes.Add(Classify(capturedMembers[i].Member, capturedMembers[i].File, rows[i]));

        return true;
    }

    /// <summary>
    /// Turns one evaluated row into a typed outcome, in nesting order: Valid, then
    /// Correct, then Printer exact at the supported version, then a parseable syntax
    /// inventory. Only a row that clears all four contributes features.
    /// </summary>
    internal static TargetOutcome Classify(
        MemberIdentity member,
        FileIdentity file,
        AuthoredSourceOracleManifest.EvaluatedRow row)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(row);

        TargetOutcome Rejected(CandidateReason reason)
            => new(member, file, reason, Evaluated: true, Features: []);

        switch (row.Result.Outcome)
        {
            case ReturnToSenderSourceOutcome.ValidMatch:
                break;
            case ReturnToSenderSourceOutcome.ValidDifferent:
                return Rejected(CandidateReason.NotCorrect);
            case ReturnToSenderSourceOutcome.SourceUnavailable:
                if (IsComparableCompileBackStatus(row.Result.CompileBackStatus))
                {
                    throw new InvalidOperationException(
                        "A captured source row became unavailable during evaluation.");
                }
                return Rejected(CandidateReason.DecompilerNotFull);
            case ReturnToSenderSourceOutcome.UnsupportedTarget:
                return Rejected(CandidateReason.UnsupportedTarget);
            default:
                return Rejected(CandidateReason.NotValid);
        }

        if (row.Record.PrinterBody is not { Length: > 0 } printerBody)
            return Rejected(CandidateReason.PrinterBodyIneligible);
        if (row.Record.PrinterBodyVersion != AuthoredSourceOracleManifest.PrinterComparisonVersion)
            return Rejected(CandidateReason.PrinterVersionUnsupported);

        switch (row.Result.PrinterExact)
        {
            case PrinterExactOutcome.Exact:
                break;
            case PrinterExactOutcome.Different:
                return Rejected(CandidateReason.PrinterDifferent);
            default:
                return Rejected(CandidateReason.PrinterNotRecorded);
        }

        return PrinterSyntaxInventory.TryCollect(
            printerBody,
            out IReadOnlyList<string> features,
            out _)
            ? new TargetOutcome(member, file, null, Evaluated: true, Features: features)
            : Rejected(CandidateReason.InventoryParseFailed);
    }

    static bool IsComparableCompileBackStatus(
        FidelityCheck.CompileBackStatus? status)
        => status is
            FidelityCheck.CompileBackStatus.Exact
            or FidelityCheck.CompileBackStatus.OpcodeDiff
            or FidelityCheck.CompileBackStatus.OperandDiff;

    static string Digest(string text)
        => Digest(System.Text.Encoding.UTF8.GetBytes(text));

    static string Digest(byte[] bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
