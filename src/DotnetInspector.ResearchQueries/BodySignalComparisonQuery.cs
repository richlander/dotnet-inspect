using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.Research;
using Inspector.Findings;

namespace DotnetInspector.Queries;

/// <summary>
/// Idless borrowed body-signal evidence for one submitted assembly input: the
/// acquisition-owned descriptor and resolver the Analysis execution was
/// produced from, and the focused Analysis results of that one execution.
/// Validated by population sealing.
/// </summary>
/// <remarks>
/// The binding is declared by the ResearchQueries companion because its
/// Analysis value is Research-owned; core Queries owns the
/// <see cref="QueryComparisonProfile.BodySignal"/> profile and sealing.
/// </remarks>
public sealed record BodySignalComparisonBinding(
    ResolvedAssemblyReference Assembly,
    IAssemblyReferenceResolver Resolver,
    BodySignalAnalysisInput Analysis);

/// <summary>
/// Old/new body-signal bindings plus either typed member selections or
/// whole-assembly type filters.
/// </summary>
/// <param name="Old">The Before-side bindings.</param>
/// <param name="New">The After-side bindings.</param>
/// <param name="TypeFilters">
/// Host type filters for an untargeted whole-assembly comparison.
/// </param>
/// <param name="MemberSelections">
/// Typed member selections. When nonempty, the comparison selects members only
/// from Research target-correspondence outcomes and ignores
/// <paramref name="TypeFilters"/>.
/// </param>
/// <param name="RetainedComparisons">
/// Analysis Finding descriptors whose typed comparisons are retained.
/// </param>
public sealed record BodySignalComparisonInput(
    IReadOnlyList<BodySignalComparisonBinding> Old,
    IReadOnlyList<BodySignalComparisonBinding> New,
    IReadOnlySet<string>? TypeFilters = null,
    IReadOnlyList<ComparisonMemberSelection>? MemberSelections = null,
    IReadOnlyList<FindingDescriptor>? RetainedComparisons = null);

/// <summary>
/// The body-signal comparison, or one typed reason a targeted comparison
/// produced none. A failure is never represented as an empty comparison.
/// </summary>
public abstract class BodySignalComparisonResult
{
    private BodySignalComparisonResult() { }

    /// <summary>The comparison completed.</summary>
    public sealed class Compared : BodySignalComparisonResult
    {
        internal Compared(
            ResearchComparison comparison,
            ResearchTargetResolution? resolution,
            ImmutableArray<ResearchTargetCorrespondenceOutcome> correspondences)
        {
            Comparison = comparison;
            Resolution = resolution;
            Correspondences = correspondences;
        }

        public ResearchComparison Comparison { get; }

        /// <summary>
        /// The Research target resolution of a targeted comparison, or
        /// <see langword="null"/> for an untargeted whole-assembly comparison.
        /// </summary>
        public ResearchTargetResolution? Resolution { get; }

        /// <summary>
        /// Every Research correspondence outcome for a targeted comparison,
        /// including one-sided and absent outcomes; empty for an untargeted
        /// whole-assembly comparison, which makes no target request.
        /// </summary>
        public ImmutableArray<ResearchTargetCorrespondenceOutcome> Correspondences { get; }
    }

    /// <summary>The submitted bindings could not be sealed.</summary>
    public sealed class PopulationRejected : BodySignalComparisonResult
    {
        internal PopulationRejected(QueryPopulationRejection rejection) => Rejection = rejection;
        public QueryPopulationRejection Rejection { get; }
    }

    /// <summary>The sealed population could not be associated with Research admission.</summary>
    public sealed class ProjectionRejected : BodySignalComparisonResult
    {
        internal ProjectionRejected(QueryPopulationProjectionRejection reason) => Reason = reason;
        public QueryPopulationProjectionRejection Reason { get; }
    }

    /// <summary>Research admission rejected the population.</summary>
    public sealed class AdmissionRejected : BodySignalComparisonResult
    {
        internal AdmissionRejected(ResearchAdmissionRejection rejection) => Rejection = rejection;
        public ResearchAdmissionRejection Rejection { get; }
    }

    /// <summary>Research target planning rejected the selections.</summary>
    public sealed class PlanningRejected : BodySignalComparisonResult
    {
        internal PlanningRejected(ResearchTargetPlanningRejection rejection) => Rejection = rejection;
        public ResearchTargetPlanningRejection Rejection { get; }
    }

    /// <summary>
    /// At least one correspondence outcome selected no Analysis method or was
    /// blocked.
    /// </summary>
    public sealed class TargetFailed : BodySignalComparisonResult
    {
        internal TargetFailed(
            ImmutableArray<ResearchTargetCorrespondenceOutcome> correspondences,
            ImmutableArray<BodySignalTargetFailure> failures)
        {
            Correspondences = correspondences;
            Failures = failures;
        }

        public ImmutableArray<ResearchTargetCorrespondenceOutcome> Correspondences { get; }
        public ImmutableArray<BodySignalTargetFailure> Failures { get; }
    }
}

/// <summary>
/// Compares focused Analysis body signals across two already-acquired
/// assembly versions while retaining the Research-owned evidence and Finding
/// correspondence.
/// </summary>
/// <remarks>
/// A targeted comparison seals a body-signal population, admits it, resolves
/// the typed member selections through Research target planning, and compares
/// exactly the endpoint methods Research correspondence selects. An untargeted
/// comparison makes no target request and keeps whole-assembly method pairing.
/// </remarks>
public static class BodySignalComparisonQuery
{
    public static InspectionQuery<BodySignalComparisonResult> Definition { get; } =
        new("Body signal comparison", InspectionCost.Unbounded);

    /// <exception cref="OperationCanceledException">Cancellation propagates without a partial result.</exception>
    public static BodySignalComparisonResult Execute(
        BodySignalComparisonInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        QueryPopulationSealingOutcome sealedOutcome =
            Seal(input.Old, input.New, input.TypeFilters);
        if (sealedOutcome is QueryPopulationSealingOutcome.Rejected rejected)
            return new BodySignalComparisonResult.PopulationRejected(rejected.Rejection);
        var population = (QueryComparisonPopulation<BodySignalComparisonBinding>)
            ((QueryPopulationSealingOutcome.Sealed)sealedOutcome).Population;
        ImmutableArray<FindingDescriptor> retained = [.. input.RetainedComparisons ?? []];

        return input.MemberSelections is { Count: > 0 } selections
            ? Targeted(population, selections, retained, cancellationToken)
            : Untargeted(population, retained);
    }

    /// <summary>Seals the body-signal profile without inspecting borrowed content.</summary>
    internal static QueryPopulationSealingOutcome Seal(
        IReadOnlyList<BodySignalComparisonBinding?>? before,
        IReadOnlyList<BodySignalComparisonBinding?>? after,
        IReadOnlySet<string>? typeFilters)
        => QueryComparisonPopulationSealer.Seal(
            before,
            after,
            typeFilters,
            QueryComparisonProfile.BodySignal,
            static binding => binding.Assembly is null
                ? QueryPopulationRejectionKind.MissingAssembly
                : binding.Resolver is null
                    ? QueryPopulationRejectionKind.MissingResolver
                    : binding.Analysis is null
                        ? QueryPopulationRejectionKind.MissingAnalysis
                        : null);

    static BodySignalComparisonResult Untargeted(
        QueryComparisonPopulation<BodySignalComparisonBinding> population,
        ImmutableArray<FindingDescriptor> retained)
        => new BodySignalComparisonResult.Compared(
            ResearchDiff.Compare(
                new ResearchDiffInput([])
                {
                    BodySignalAnalyses = [.. population.Before.Select(static input => input.Binding.Analysis)],
                },
                new ResearchDiffInput([])
                {
                    BodySignalAnalyses = [.. population.After.Select(static input => input.Binding.Analysis)],
                },
                new ResearchDiffOptions(
                    ResearchChangeMechanism.BodySignals,
                    TypeFilters: population.TypeFilters)
                {
                    RetainedComparisonDescriptorIds = [.. retained.Select(static descriptor => descriptor.Id)],
                }),
            resolution: null,
            []);

    static BodySignalComparisonResult Targeted(
        QueryComparisonPopulation<BodySignalComparisonBinding> population,
        IReadOnlyList<ComparisonMemberSelection> selections,
        ImmutableArray<FindingDescriptor> retained,
        CancellationToken cancellationToken)
    {
        QueryResearchTargetPlanningStep step = QueryResearchTargetPlanner.Plan(
            population,
            selections,
            cancellationToken);
        switch (step)
        {
            case QueryResearchTargetPlanningStep.ProjectionRejected rejected:
                return new BodySignalComparisonResult.ProjectionRejected(rejected.Reason);
            case QueryResearchTargetPlanningStep.AdmissionRejected rejected:
                return new BodySignalComparisonResult.AdmissionRejected(rejected.Rejection);
            case QueryResearchTargetPlanningStep.PlanningRejected rejected:
                return new BodySignalComparisonResult.PlanningRejected(rejected.Rejection);
        }

        var resolved = (QueryResearchTargetPlanningStep.Resolved)step;
        cancellationToken.ThrowIfCancellationRequested();
        return BodySignalTargetComparison.Compare(
                resolved.Projected.Admission,
                resolved.Resolution,
                retained) switch
        {
            BodySignalTargetComparisonOutcome.Compared compared =>
                new BodySignalComparisonResult.Compared(
                    compared.Comparison,
                    resolved.Resolution,
                    compared.Correspondences),
            BodySignalTargetComparisonOutcome.Failed failed =>
                new BodySignalComparisonResult.TargetFailed(
                    failed.Correspondences,
                    failed.Failures),
            _ => throw new InvalidOperationException("Unknown body-signal target comparison outcome."),
        };
    }
}
