using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.MetadataPrimitives;
using Inspector.Findings;

namespace ILInspector.Research;

/// <summary>
/// Why one targeted body-signal comparison could not select its endpoint
/// methods.
/// </summary>
public enum BodySignalTargetFailureKind
{
    /// <summary>
    /// A resolved endpoint has no physical method address, so it selects no
    /// Analysis method.
    /// </summary>
    EndpointWithoutMethodAddress,

    /// <summary>
    /// A resolved endpoint's address selects no method in that side's Analysis
    /// method population, so Research could issue no body identity for it.
    /// </summary>
    EndpointMethodNotInAnalysis,

    /// <summary>
    /// A resolved target's counterpart could not be evaluated, so the
    /// correspondence is incomplete.
    /// </summary>
    CounterpartUnavailable,

    /// <summary>
    /// A blocked target domain produced no completed endpoint set.
    /// </summary>
    DomainUnavailable,
}

/// <summary>
/// One typed failure of a targeted body-signal comparison, retaining the exact
/// correspondence outcome it came from.
/// </summary>
public sealed class BodySignalTargetFailure
{
    internal BodySignalTargetFailure(
        BodySignalTargetFailureKind kind,
        ResearchTargetCorrespondenceOutcome correspondence,
        ResearchTargetAttempt? attempt,
        string summary)
    {
        Kind = kind;
        Correspondence = correspondence;
        Attempt = attempt;
        Summary = summary;
    }

    /// <summary>The closed failure case.</summary>
    public BodySignalTargetFailureKind Kind { get; }

    /// <summary>The correspondence outcome that failed to select methods.</summary>
    public ResearchTargetCorrespondenceOutcome Correspondence { get; }

    /// <summary>
    /// The resolved attempt that selected no Analysis method, or
    /// <see langword="null"/> for a blocked domain.
    /// </summary>
    public ResearchTargetAttempt? Attempt { get; }

    /// <summary>Human-readable explanation; not identity.</summary>
    public string Summary { get; }
}

/// <summary>
/// The result of one targeted body-signal comparison: either the comparison of
/// exactly the endpoint methods Research correspondence selected, or every
/// typed failure that prevented it.
/// </summary>
public abstract class BodySignalTargetComparisonOutcome
{
    private BodySignalTargetComparisonOutcome(
        ImmutableArray<ResearchTargetCorrespondenceOutcome> correspondences)
        => Correspondences = correspondences;

    /// <summary>
    /// Every correspondence outcome of the resolution, including one-sided
    /// and absent outcomes, so callers never see them collapsed.
    /// </summary>
    public ImmutableArray<ResearchTargetCorrespondenceOutcome> Correspondences
    {
        get;
    }

    /// <summary>Every selected endpoint method was compared.</summary>
    public sealed class Compared : BodySignalTargetComparisonOutcome
    {
        internal Compared(
            ImmutableArray<ResearchTargetCorrespondenceOutcome> correspondences,
            ResearchComparison comparison)
            : base(correspondences)
            => Comparison = comparison;

        /// <summary>
        /// The comparison of exactly the selected endpoint methods. It is
        /// empty only when no correspondence outcome selected any endpoint.
        /// </summary>
        public ResearchComparison Comparison { get; }
    }

    /// <summary>At least one correspondence outcome selected no method.</summary>
    public sealed class Failed : BodySignalTargetComparisonOutcome
    {
        internal Failed(
            ImmutableArray<ResearchTargetCorrespondenceOutcome> correspondences,
            ImmutableArray<BodySignalTargetFailure> failures)
            : base(correspondences)
            => Failures = failures;

        /// <summary>Every typed failure, in correspondence order.</summary>
        public ImmutableArray<BodySignalTargetFailure> Failures { get; }
    }
}

/// <summary>
/// Compares body signals for exactly the endpoint methods that
/// <see cref="ResearchTargetResolver"/> correspondence outcomes select.
/// </summary>
/// <remarks>
/// <para>
/// Cross-version pairing comes from the correspondence key, never from a
/// body-signal method key or display text. A <c>Paired</c> outcome selects
/// exactly one method on each side by resolved address (module version id and
/// MethodDef token) from that side's admitted <see cref="BodySignalAnalysisInput"/>.
/// A one-sided outcome compares its present endpoint against a
/// <c>SubjectAbsent</c> inspection; an absent outcome selects nothing and stays
/// visible in <see cref="BodySignalTargetComparisonOutcome.Correspondences"/>.
/// </para>
/// <para>
/// A resolved endpoint whose address selects no Analysis method, and a
/// blocked correspondence, are typed failures, never an empty comparison.
/// <c>BodySignalComparison_SelectsMembersOnlyFromCorrespondence</c> and
/// <c>BodySignalComparison_ResolvedTargetWithoutAnalysisMethodIsTypedFailure</c>
/// gate these properties.
/// </para>
/// </remarks>
public static class BodySignalTargetComparison
{
    /// <summary>
    /// Compares the endpoint methods selected by every correspondence outcome
    /// of <paramref name="resolution"/>.
    /// </summary>
    /// <param name="population">The admitted body-signal population.</param>
    /// <param name="resolution">
    /// The target resolution produced for exactly that population.
    /// </param>
    /// <param name="retainedComparisons">
    /// Analysis Finding descriptors whose typed comparisons are retained:
    /// allocation, call-site, or unsafety.
    /// </param>
    public static BodySignalTargetComparisonOutcome Compare(
        ResearchAdmittedPopulation population,
        ResearchTargetResolution resolution,
        IEnumerable<FindingDescriptor> retainedComparisons)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(retainedComparisons);
        if (population.Profile != ResearchComparisonProfile.BodySignal)
        {
            throw new ArgumentException(
                "Targeted body-signal comparison requires an admitted "
                    + "body-signal population.",
                nameof(population));
        }
        if (!ReferenceEquals(resolution.Operation, population.Operation))
        {
            throw new ArgumentException(
                "The resolution must belong to the admitted population.",
                nameof(resolution));
        }

        BodySignalRetention retention = BodySignalRetention.From(
            retainedComparisons);
        var failures = ImmutableArray.CreateBuilder<BodySignalTargetFailure>();
        List<SelectedBodySignalPair> pairs = [];
        HashSet<(MethodIdentity?, MethodIdentity?)> selected = [];
        foreach (ResearchTargetCorrespondenceOutcome outcome
            in resolution.Correspondences)
        {
            SelectedBodySignalEndpoint? before = null;
            SelectedBodySignalEndpoint? after = null;
            bool complete = true;
            switch (outcome)
            {
                case ResearchTargetCorrespondenceOutcome.Paired paired:
                    complete &= TrySelect(population, outcome, paired.Before, failures, out before);
                    complete &= TrySelect(population, outcome, paired.After, failures, out after);
                    break;
                case ResearchTargetCorrespondenceOutcome.BeforeOnly beforeOnly:
                    complete &= TrySelect(population, outcome, beforeOnly.Before, failures, out before);
                    break;
                case ResearchTargetCorrespondenceOutcome.AfterOnly afterOnly:
                    complete &= TrySelect(population, outcome, afterOnly.After, failures, out after);
                    break;
                case ResearchTargetCorrespondenceOutcome.Absent:
                    continue;
                case ResearchTargetCorrespondenceOutcome.CounterpartUnavailable unavailable:
                    failures.Add(CounterpartFailure(unavailable));
                    continue;
                case ResearchTargetCorrespondenceOutcome.DomainUnavailable blocked:
                    failures.Add(new BodySignalTargetFailure(
                        BodySignalTargetFailureKind.DomainUnavailable,
                        outcome,
                        attempt: null,
                        $"The target domain '{blocked.Domain.Key.Identity.Name}' is "
                            + $"unavailable ({blocked.Taint.Kind})"
                            + AttemptDiagnostics(blocked.Taint.Attempts)));
                    continue;
                default:
                    throw new InvalidOperationException(
                        "Unknown Research target correspondence outcome.");
            }

            if (complete && selected.Add((before?.Method, after?.Method)))
                pairs.Add(new SelectedBodySignalPair(before, after));
        }

        ImmutableArray<ResearchTargetCorrespondenceOutcome> correspondences =
            resolution.Correspondences;
        return failures.Count > 0
            ? new BodySignalTargetComparisonOutcome.Failed(
                correspondences,
                failures.ToImmutable())
            : new BodySignalTargetComparisonOutcome.Compared(
                correspondences,
                ResearchDiff.CompareSelectedBodySignals(pairs, retention));
    }

    // A resolved method-role target that Analysis cannot identify is the
    // endpoint that selects no Analysis method; its healthy counterpart is
    // merely unavailable.
    static BodySignalTargetFailure CounterpartFailure(
        ResearchTargetCorrespondenceOutcome.CounterpartUnavailable outcome)
    {
        ResearchTargetOutcome.Resolved target = outcome.Target;
        if (outcome.Taint.Kind == ResearchTargetTaintKind.BodyIdentityUnavailable
            && outcome.Taint.Attempts.Contains(outcome.Attempt))
        {
            return new BodySignalTargetFailure(
                BodySignalTargetFailureKind.EndpointMethodNotInAnalysis,
                outcome,
                outcome.Attempt,
                $"The {outcome.Attempt.Request.Side} target "
                    + $"'{target.Anchor.StableSelector}' resolved to "
                    + $"{AddressText(target.Address)}, which selects no "
                    + "method in that side's Analysis population.");
        }

        return new BodySignalTargetFailure(
            BodySignalTargetFailureKind.CounterpartUnavailable,
            outcome,
            outcome.Attempt,
            $"The {outcome.Attempt.Request.Side} target "
                + $"'{target.Anchor.StableSelector}' has no evaluable "
                + $"counterpart ({outcome.Taint.Kind})"
                + AttemptDiagnostics(outcome.Taint.Attempts));
    }

    static string AddressText(MetadataMethodAddress? address)
        => address is { } value
            ? $"MethodDef 0x{value.Token:X8}"
            : "a member without a method body";

    static string AttemptDiagnostics(ImmutableArray<ResearchTargetAttempt> attempts)
    {
        string[] messages =
        [
            .. attempts
                .Select(static attempt => attempt.Outcome switch
                {
                    ResearchTargetOutcome.NotFound notFound =>
                        notFound.MetadataDiagnostic?.Message
                            ?? notFound.ResearchDiagnostic?.Summary,
                    ResearchTargetOutcome.Ambiguous ambiguous =>
                        ambiguous.Diagnostic.Message,
                    ResearchTargetOutcome.Rejected rejected =>
                        rejected.Diagnostic.Message,
                    ResearchTargetOutcome.Unavailable unavailable =>
                        unavailable.Diagnostic.Summary,
                    ResearchTargetOutcome.Failed failed =>
                        failed.Diagnostic.Summary,
                    _ => null,
                })
                .OfType<string>()
                .Distinct(StringComparer.Ordinal),
        ];
        return messages.Length == 0
            ? "."
            : $": {string.Join("; ", messages)}";
    }

    static bool TrySelect(
        ResearchAdmittedPopulation population,
        ResearchTargetCorrespondenceOutcome outcome,
        ResearchCorrespondingTarget endpoint,
        ImmutableArray<BodySignalTargetFailure>.Builder failures,
        out SelectedBodySignalEndpoint? selected)
    {
        selected = null;
        var occurrence = (BodySignalComparisonInputOccurrence)population
            .GetInput(endpoint.Attempt.Request.Input)
            .Occurrence;
        BodySignalAnalysisInput analysis = occurrence.Analysis;
        if (endpoint.Target.Address is not MetadataMethodAddress address)
        {
            failures.Add(new BodySignalTargetFailure(
                BodySignalTargetFailureKind.EndpointWithoutMethodAddress,
                outcome,
                endpoint.Attempt,
                $"The {endpoint.Side} target "
                    + $"'{endpoint.Target.Anchor.StableSelector}' has no "
                    + "method body to compare."));
            return false;
        }

        MethodIdentity? method = null;
        if (address.ModuleVersionId
            == analysis.MethodPopulation.ModuleIdentity.ModuleVersionId)
        {
            foreach (MethodIdentity candidate
                in analysis.MethodPopulation.DeclaredMethods)
            {
                if (candidate.MetadataToken != address.Token)
                    continue;
                if (method is not null)
                {
                    method = null;
                    break;
                }
                method = candidate;
            }
        }

        if (method is null)
        {
            failures.Add(new BodySignalTargetFailure(
                BodySignalTargetFailureKind.EndpointMethodNotInAnalysis,
                outcome,
                endpoint.Attempt,
                $"The {endpoint.Side} target "
                    + $"'{endpoint.Target.Anchor.StableSelector}' resolved to "
                    + $"{AddressText(address)}, which selects no "
                    + "method in that side's Analysis population."));
            return false;
        }

        selected = new SelectedBodySignalEndpoint(analysis, method);
        return true;
    }
}

/// <summary>One side's selected Analysis method and the results it reads.</summary>
internal sealed record SelectedBodySignalEndpoint(
    BodySignalAnalysisInput Analysis,
    MethodIdentity Method);

/// <summary>
/// Exactly the endpoint methods one correspondence outcome selected; a null
/// side is proven absent by that outcome.
/// </summary>
internal sealed record SelectedBodySignalPair(
    SelectedBodySignalEndpoint? Old,
    SelectedBodySignalEndpoint? New);

/// <summary>Which Analysis Finding comparisons a body-signal run retains.</summary>
internal readonly record struct BodySignalRetention(
    bool Allocations,
    bool CallSites,
    bool Unsafety)
{
    internal static BodySignalRetention From(IEnumerable<string> descriptorIds)
    {
        bool allocations = false;
        bool callSites = false;
        bool unsafety = false;
        foreach (string id in descriptorIds)
        {
            allocations |= id == AnalysisFindings.AllocationDescriptor.Id;
            callSites |= id == AnalysisFindings.CallSiteDescriptor.Id;
            unsafety |= id == AnalysisFindings.UnsafetyDescriptor.Id;
        }
        return new(allocations, callSites, unsafety);
    }

    internal static BodySignalRetention From(
        IEnumerable<FindingDescriptor> descriptors)
        => From(descriptors.Select(static descriptor =>
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            return descriptor.Id;
        }));
}

/// <summary>
/// Inspection topology of one compared body-signal subject. The untargeted
/// census compares two complete inspections; a targeted one-sided outcome
/// proves its missing side <c>SubjectAbsent</c>.
/// </summary>
internal readonly record struct BodySignalEndpointTopology(
    bool OldSubjectAbsent,
    bool NewSubjectAbsent)
{
    internal static BodySignalEndpointTopology Census => default;

    internal bool IsCensus => !OldSubjectAbsent && !NewSubjectAbsent;

    // Only one side can be absent, so no Present pair exists and no facet
    // reclassification applies.
    internal FindingComparison<T> Compare<T>(
        Func<IEnumerable<T>, FindingSubject, ImmutableArray<Finding<T>>> inspect,
        ImmutableArray<T> oldValues,
        ImmutableArray<T> newValues,
        ResearchSubjectKey subject)
        where T : notnull
    {
        var findingSubject = new FindingSubject(subject.Id, subject.Display);
        return FindingComparison.Compare(
            Inspection(inspect, oldValues, findingSubject, OldSubjectAbsent),
            Inspection(inspect, newValues, findingSubject, NewSubjectAbsent));
    }

    static FindingInspection<T> Inspection<T>(
        Func<IEnumerable<T>, FindingSubject, ImmutableArray<Finding<T>>> inspect,
        ImmutableArray<T> values,
        FindingSubject subject,
        bool absent)
        where T : notnull
        => absent
            ? new FindingInspection<T>.Absent(
                FindingInspectionAbsenceKind.SubjectAbsent,
                "Member is absent.")
            : new FindingInspection<T>.Complete(inspect(values, subject));
}
