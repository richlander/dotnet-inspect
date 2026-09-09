using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>The complete planning session or one typed projection, admission, or planning rejection.</summary>
public abstract class WorkspaceResearchTargetPlanningOutcome
{
    private WorkspaceResearchTargetPlanningOutcome() { }

    public sealed class Planned : WorkspaceResearchTargetPlanningOutcome
    {
        internal Planned(WorkspaceResearchTargetPlan plan) => Plan = plan;
        public WorkspaceResearchTargetPlan Plan { get; }
    }

    public sealed class Rejected : WorkspaceResearchTargetPlanningOutcome
    {
        internal Rejected(QueryPopulationProjectionRejection reason) => Reason = reason;
        public QueryPopulationProjectionRejection Reason { get; }
    }

    public sealed class AdmissionRejected : WorkspaceResearchTargetPlanningOutcome
    {
        internal AdmissionRejected(ResearchAdmissionRejection rejection) => Rejection = rejection;
        public ResearchAdmissionRejection Rejection { get; }
    }

    public sealed class PlanningRejected : WorkspaceResearchTargetPlanningOutcome
    {
        internal PlanningRejected(ResearchTargetPlanningRejection rejection) => Rejection = rejection;
        public ResearchTargetPlanningRejection Rejection { get; }
    }
}

/// <summary>Plans one carried member selection over exactly one sealed comparison population.</summary>
public static class WorkspaceResearchTargetPlanningQuery
{
    public static InspectionQuery<WorkspaceResearchTargetPlanningOutcome> Definition { get; } =
        new("Workspace Research target planning", InspectionCost.Unbounded);

    /// <summary>Assigns every input the implementation role and resolves one complete carried scope.</summary>
    /// <exception cref="OperationCanceledException">Cancellation propagates without publishing a partial plan.</exception>
    public static WorkspaceResearchTargetPlanningOutcome Execute(
        QueryComparisonPopulation<ImplementationComparisonBinding> population,
        MetadataTypeDefinitionName declaringType,
        MemberTargetSelector selector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(selector);
        cancellationToken.ThrowIfCancellationRequested();

        QueryPopulationProjectionOutcome projection = QueryPopulationProjection.Execute(population);
        cancellationToken.ThrowIfCancellationRequested();
        return projection switch
        {
            QueryPopulationProjectionOutcome.Projected projected =>
                Resolve(population, projected.Population, declaringType, selector, cancellationToken),
            QueryPopulationProjectionOutcome.Rejected rejected =>
                new WorkspaceResearchTargetPlanningOutcome.Rejected(rejected.Reason),
            QueryPopulationProjectionOutcome.AdmissionRejected rejected =>
                new WorkspaceResearchTargetPlanningOutcome.AdmissionRejected(rejected.Rejection),
            _ => throw new InvalidOperationException("Unknown Queries population projection outcome."),
        };
    }

    static WorkspaceResearchTargetPlanningOutcome Resolve(
        QueryComparisonPopulation<ImplementationComparisonBinding> population,
        ProjectedQueryPopulation projected,
        MetadataTypeDefinitionName declaringType,
        MemberTargetSelector selector,
        CancellationToken cancellationToken)
    {
        ResearchComparisonQuestionId question = projected.Receipt.Questions[population.Question];
        var request = new ResearchTargetPlanningRequest(
            projected.Admission,
            projected.Admission.Inputs.Select(input =>
                new ResearchTargetInputRoleAssignment(input, ResearchTargetInputRole.Implementation)),
            [new ResearchCarriedMemberSelection(question, declaringType.ToMetadataFullName(), selector)]);
        ResearchTargetPlanningOutcome outcome = ResearchTargetResolver.Resolve(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return outcome switch
        {
            ResearchTargetPlanningOutcome.Planned planned =>
                new WorkspaceResearchTargetPlanningOutcome.Planned(
                    new(population, projected, planned.Resolution, declaringType)),
            ResearchTargetPlanningOutcome.Rejected rejected =>
                new WorkspaceResearchTargetPlanningOutcome.PlanningRejected(rejected.Rejection),
            _ => throw new InvalidOperationException("Unknown Research target planning outcome."),
        };
    }
}

/// <summary>A live planning session that composes only its original population and Research resolution.</summary>
/// <remarks>
/// This session borrows the sealed population's resources; it is orchestration input, not an inert
/// composition receipt. Keep those resources and the workspace groups alive while composing.
/// </remarks>
public sealed class WorkspaceResearchTargetPlan
{
    readonly ProjectedQueryPopulation _projected;

    internal WorkspaceResearchTargetPlan(
        QueryComparisonPopulation<ImplementationComparisonBinding> population,
        ProjectedQueryPopulation projected,
        ResearchTargetResolution resolution,
        MetadataTypeDefinitionName declaringType)
    {
        Population = population;
        _projected = projected;
        Resolution = resolution;
        Scope = resolution.Scopes.Single();
        DeclaringType = declaringType;
    }

    public QueryComparisonPopulation<ImplementationComparisonBinding> Population { get; }
    public QueryComparisonQuestionId Question => Population.Question;
    public ResearchTargetResolution Resolution { get; }
    public ResearchTargetScope Scope { get; }
    public MetadataTypeDefinitionName DeclaringType { get; }
    internal ProjectedQueryPopulation Projected => _projected;

    /// <summary>
    /// Composes one side using the caller-selected domain and census from this session.
    /// Exact group membership, population correspondence, and Research identities remain validated.
    /// </summary>
    public WorkspaceResearchTargetCompositionResult Compose(
        AssemblyContextGroup group,
        AssemblyContextParticipant root,
        QueryComparisonSide side,
        ResearchTargetDomain domain,
        ResearchTargetDomainSideCensus census,
        AssemblyResolutionScope resolutionScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        cancellationToken.ThrowIfCancellationRequested();
        var request = new WorkspaceResearchTargetCompositionRequest(
            group, root, group.BindingPolicyVersion, Population, _projected, Resolution,
            Question, side, Scope, DeclaringType, domain, census, resolutionScope);
        return WorkspaceResearchTargetCompositionQuery.Execute(request, cancellationToken);
    }

    /// <summary>
    /// Resolves the root first, then derives its exact owner-issued terminal
    /// domain and side census from this plan.
    /// </summary>
    public WorkspaceResearchTargetCompositionResult Compose(
        AssemblyContextGroup group,
        AssemblyContextParticipant root,
        QueryComparisonSide side,
        AssemblyResolutionScope resolutionScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        cancellationToken.ThrowIfCancellationRequested();
        var request = new WorkspaceResearchTargetCompositionRequest(
            group, root, group.BindingPolicyVersion, Population, _projected, Resolution,
            Question, side, Scope, DeclaringType, resolutionScope);
        return WorkspaceResearchTargetCompositionQuery.Execute(request, cancellationToken);
    }
}
