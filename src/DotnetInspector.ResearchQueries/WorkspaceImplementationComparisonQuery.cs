using System.Collections.Immutable;

using Inspector.Findings;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>One realized workspace side and its complete comparison bindings.</summary>
public sealed class WorkspaceImplementationComparisonSide
{
    public WorkspaceImplementationComparisonSide(
        AssemblyContextGroup group,
        AssemblyContextParticipant root,
        IEnumerable<ImplementationComparisonBinding> bindings,
        AssemblyResolutionScope resolutionScope = AssemblyResolutionScope.Any)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(bindings);
        if (!Enum.IsDefined(resolutionScope))
            throw new ArgumentOutOfRangeException(nameof(resolutionScope));

        Group = group;
        Root = root;
        Bindings = [.. bindings];
        ResolutionScope = resolutionScope;
        if (!Group.Participants.Any(participant =>
                ReferenceEquals(participant, Root)))
        {
            throw new ArgumentException(
                "The designated root must belong to the assembly context group.",
                nameof(root));
        }
        if (Bindings.Any(binding => binding is null)
            || Bindings.Length != Group.Participants.Length
            || Bindings.Select(binding => binding.Assembly.Registration)
                .Distinct(ReferenceEqualityComparer.Instance)
                .Count() != Bindings.Length
            || Bindings.Any(binding => !Group.Participants.Any(participant =>
                ReferenceEquals(
                    participant.Assembly.Registration,
                    binding.Assembly.Registration))))
        {
            throw new ArgumentException(
                "Bindings must cover every group participant exactly once.",
                nameof(bindings));
        }
    }

    public AssemblyContextGroup Group { get; }
    public AssemblyContextParticipant Root { get; }
    public ImmutableArray<ImplementationComparisonBinding> Bindings { get; }
    public AssemblyResolutionScope ResolutionScope { get; }
}

/// <summary>One exact carried-member comparison over two realized workspace sides.</summary>
public sealed class WorkspaceImplementationComparisonRequest
{
    public WorkspaceImplementationComparisonRequest(
        WorkspaceImplementationComparisonSide before,
        WorkspaceImplementationComparisonSide after,
        MetadataTypeDefinitionName declaringType,
        MemberTargetSelector selector,
        IEnumerable<ResearchProducerKind> producers)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(producers);

        Before = before;
        After = after;
        DeclaringType = declaringType;
        Selector = selector;
        Producers = [.. producers];
        if (Producers.IsEmpty
            || Producers.Any(kind => !Enum.IsDefined(kind))
            || Producers.Distinct().Count() != Producers.Length)
        {
            throw new ArgumentException(
                "Select a nonempty, distinct set of C# and/or IL producers.",
                nameof(producers));
        }
    }

    public WorkspaceImplementationComparisonSide Before { get; }
    public WorkspaceImplementationComparisonSide After { get; }
    public MetadataTypeDefinitionName DeclaringType { get; }
    public MemberTargetSelector Selector { get; }
    public ImmutableArray<ResearchProducerKind> Producers { get; }
}

/// <summary>
/// One native Metadata forwarder observation and the exact composition hop
/// that consumed it.
/// </summary>
public sealed class WorkspaceTypeForwarderUse
{
    internal WorkspaceTypeForwarderUse(
        QueryComparisonSide side,
        int hopIndex,
        QueryComparisonInputId sourceInput,
        Finding<TypeForwarderInfo> finding)
    {
        Side = side;
        HopIndex = hopIndex;
        SourceInput = sourceInput;
        Finding = finding;
    }

    public QueryComparisonSide Side { get; }
    public int HopIndex { get; }
    public QueryComparisonInputId SourceInput { get; }
    public Finding<TypeForwarderInfo> Finding { get; }
}

/// <summary>Completed target composition and its native producer-session outcome.</summary>
public sealed class WorkspaceImplementationComparisonPublication
{
    internal WorkspaceImplementationComparisonPublication(
        WorkspaceResearchTargetWorkItem workItem,
        ImmutableArray<WorkspaceTypeForwarderUse> forwarders,
        ResearchProducerSessionOutcome outcome)
    {
        WorkItem = workItem;
        Forwarders = forwarders;
        Outcome = outcome;
    }

    public WorkspaceResearchTargetWorkItem WorkItem { get; }
    public WorkspaceResearchTargetCompositionReceipt Before => WorkItem.Before;
    public WorkspaceResearchTargetCompositionReceipt After => WorkItem.After;
    public ImmutableArray<WorkspaceTypeForwarderUse> Forwarders { get; }
    public ResearchProducerSessionOutcome Outcome { get; }
}

public enum WorkspaceImplementationComparisonHandoffFailureKind
{
    MissingCorrespondence,
    Unavailable,
    Rejected,
    MultiplePairs,
}

/// <summary>The complete typed outcome of one workspace implementation comparison.</summary>
public abstract class WorkspaceImplementationComparisonResult
{
    private WorkspaceImplementationComparisonResult() { }

    public sealed class Published : WorkspaceImplementationComparisonResult
    {
        internal Published(WorkspaceImplementationComparisonPublication publication)
            => Publication = publication;

        public WorkspaceImplementationComparisonPublication Publication { get; }
    }

    public sealed class PopulationRejected : WorkspaceImplementationComparisonResult
    {
        internal PopulationRejected(QueryPopulationRejection rejection)
            => Rejection = rejection;

        public QueryPopulationRejection Rejection { get; }
    }

    public sealed class ParticipantImageUnavailable : WorkspaceImplementationComparisonResult
    {
        internal ParticipantImageUnavailable(
            QueryComparisonSide side,
            int participantIndex,
            AssemblyReferenceIdentity assembly,
            CandidateOpenFailure failure)
        {
            Side = side;
            ParticipantIndex = participantIndex;
            Assembly = assembly;
            Failure = failure;
        }

        public QueryComparisonSide Side { get; }
        public int ParticipantIndex { get; }
        public AssemblyReferenceIdentity Assembly { get; }
        public CandidateOpenFailure Failure { get; }
    }

    public sealed class ProjectionRejected : WorkspaceImplementationComparisonResult
    {
        internal ProjectionRejected(QueryPopulationProjectionRejection rejection)
            => Rejection = rejection;

        public QueryPopulationProjectionRejection Rejection { get; }
    }

    public sealed class AdmissionRejected : WorkspaceImplementationComparisonResult
    {
        internal AdmissionRejected(ResearchAdmissionRejection rejection)
            => Rejection = rejection;

        public ResearchAdmissionRejection Rejection { get; }
    }

    public sealed class PlanningRejected : WorkspaceImplementationComparisonResult
    {
        internal PlanningRejected(ResearchTargetPlanningRejection rejection)
            => Rejection = rejection;

        public ResearchTargetPlanningRejection Rejection { get; }
    }

    public sealed class CompositionUnavailable : WorkspaceImplementationComparisonResult
    {
        internal CompositionUnavailable(
            QueryComparisonSide side,
            WorkspaceResearchTargetCompositionResult.Unavailable result,
            WorkspaceResearchTargetCompositionReceipt? completedSide,
            ImmutableArray<WorkspaceTypeForwarderUse> forwarders)
        {
            Side = side;
            Result = result;
            CompletedSide = completedSide;
            Forwarders = forwarders;
        }

        public QueryComparisonSide Side { get; }
        public WorkspaceResearchTargetCompositionResult.Unavailable Result { get; }
        public WorkspaceResearchTargetCompositionReceipt? CompletedSide { get; }
        public ImmutableArray<WorkspaceTypeForwarderUse> Forwarders { get; }
    }

    public sealed class CompositionRejected : WorkspaceImplementationComparisonResult
    {
        internal CompositionRejected(
            QueryComparisonSide side,
            WorkspaceResearchTargetCompositionResult.Rejected result,
            WorkspaceResearchTargetCompositionReceipt? completedSide,
            ImmutableArray<WorkspaceTypeForwarderUse> forwarders)
        {
            Side = side;
            Result = result;
            CompletedSide = completedSide;
            Forwarders = forwarders;
        }

        public QueryComparisonSide Side { get; }
        public WorkspaceResearchTargetCompositionResult.Rejected Result { get; }
        public WorkspaceResearchTargetCompositionReceipt? CompletedSide { get; }
        public ImmutableArray<WorkspaceTypeForwarderUse> Forwarders { get; }
    }

    public sealed class HandoffFailed : WorkspaceImplementationComparisonResult
    {
        internal HandoffFailed(
            WorkspaceImplementationComparisonHandoffFailureKind kind,
            WorkspaceResearchTargetCompositionReceipt before,
            WorkspaceResearchTargetCompositionReceipt after,
            ImmutableArray<WorkspaceResearchTargetHandoffResult> outcomes,
            ImmutableArray<WorkspaceTypeForwarderUse> forwarders)
        {
            Kind = kind;
            Before = before;
            After = after;
            Outcomes = outcomes;
            Forwarders = forwarders;
        }

        public WorkspaceImplementationComparisonHandoffFailureKind Kind { get; }
        public WorkspaceResearchTargetCompositionReceipt Before { get; }
        public WorkspaceResearchTargetCompositionReceipt After { get; }
        public ImmutableArray<WorkspaceResearchTargetHandoffResult> Outcomes { get; }
        public ImmutableArray<WorkspaceTypeForwarderUse> Forwarders { get; }
    }

    public sealed class Cancelled : WorkspaceImplementationComparisonResult;
}

/// <summary>
/// Composes two caller-designated workspace roots to exact effective Research
/// targets, then runs the existing local producer session.
/// </summary>
public static class WorkspaceImplementationComparisonQuery
{
    public static InspectionQuery<WorkspaceImplementationComparisonResult> Definition { get; } =
        new("Workspace implementation comparison", InspectionCost.Unbounded);

    public static WorkspaceImplementationComparisonResult Execute(
        WorkspaceImplementationComparisonRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryRetain(
                    request.Before,
                    out ImmutableArray<ImplementationComparisonBinding> beforeBindings,
                    out int failedIndex,
                    out AssemblyReferenceIdentity? failedAssembly,
                    out CandidateOpenFailure? failure))
            {
                return new WorkspaceImplementationComparisonResult
                    .ParticipantImageUnavailable(
                        QueryComparisonSide.Before,
                        failedIndex,
                        failedAssembly!,
                        failure!);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryRetain(
                    request.After,
                    out ImmutableArray<ImplementationComparisonBinding> afterBindings,
                    out failedIndex,
                    out failedAssembly,
                    out failure))
            {
                return new WorkspaceImplementationComparisonResult
                    .ParticipantImageUnavailable(
                        QueryComparisonSide.After,
                        failedIndex,
                        failedAssembly!,
                        failure!);
            }
            QueryPopulationSealingOutcome sealing =
                QueryComparisonPopulationSealer.Execute(
                    new ImplementationComparisonPopulationRequest(
                        beforeBindings,
                        afterBindings));
            if (sealing is QueryPopulationSealingOutcome.Rejected rejected)
            {
                return new WorkspaceImplementationComparisonResult
                    .PopulationRejected(rejected.Rejection);
            }

            var population =
                (QueryComparisonPopulation<ImplementationComparisonBinding>)
                    ((QueryPopulationSealingOutcome.Sealed)sealing).Population;
            WorkspaceResearchTargetPlanningOutcome planning =
                WorkspaceResearchTargetPlanningQuery.Execute(
                    population,
                    request.DeclaringType,
                    request.Selector,
                    cancellationToken);
            if (planning is WorkspaceResearchTargetPlanningOutcome.Rejected projection)
            {
                return new WorkspaceImplementationComparisonResult
                    .ProjectionRejected(projection.Reason);
            }
            if (planning is WorkspaceResearchTargetPlanningOutcome.AdmissionRejected admission)
            {
                return new WorkspaceImplementationComparisonResult
                    .AdmissionRejected(admission.Rejection);
            }
            if (planning is WorkspaceResearchTargetPlanningOutcome.PlanningRejected planRejected)
            {
                return new WorkspaceImplementationComparisonResult
                    .PlanningRejected(planRejected.Rejection);
            }

            WorkspaceResearchTargetPlan plan =
                ((WorkspaceResearchTargetPlanningOutcome.Planned)planning).Plan;
            WorkspaceResearchTargetCompositionResult beforeResult = plan.Compose(
                request.Before.Group,
                request.Before.Root,
                QueryComparisonSide.Before,
                request.Before.ResolutionScope,
                cancellationToken);
            if (beforeResult is WorkspaceResearchTargetCompositionResult.Unavailable beforeUnavailable)
            {
                return new WorkspaceImplementationComparisonResult
                    .CompositionUnavailable(
                        QueryComparisonSide.Before,
                        beforeUnavailable,
                        completedSide: null,
                        Forwarders(
                            QueryComparisonSide.Before,
                            beforeUnavailable.Evidence,
                            request.DeclaringType));
            }
            if (beforeResult is WorkspaceResearchTargetCompositionResult.Rejected beforeRejected)
            {
                return new WorkspaceImplementationComparisonResult
                    .CompositionRejected(
                        QueryComparisonSide.Before,
                        beforeRejected,
                        completedSide: null,
                        Forwarders(
                            QueryComparisonSide.Before,
                            beforeRejected.Evidence,
                            request.DeclaringType));
            }

            WorkspaceResearchTargetCompositionReceipt before =
                ((WorkspaceResearchTargetCompositionResult.Composed)beforeResult).Receipt;
            ImmutableArray<WorkspaceTypeForwarderUse> beforeForwarders =
                Forwarders(
                    QueryComparisonSide.Before,
                    before.Evidence,
                    request.DeclaringType);
            WorkspaceResearchTargetCompositionResult afterResult = plan.Compose(
                request.After.Group,
                request.After.Root,
                QueryComparisonSide.After,
                request.After.ResolutionScope,
                cancellationToken);
            if (afterResult is WorkspaceResearchTargetCompositionResult.Unavailable afterUnavailable)
            {
                return new WorkspaceImplementationComparisonResult
                    .CompositionUnavailable(
                        QueryComparisonSide.After,
                        afterUnavailable,
                        before,
                        [
                            .. beforeForwarders,
                            .. Forwarders(
                                QueryComparisonSide.After,
                                afterUnavailable.Evidence,
                                request.DeclaringType),
                        ]);
            }
            if (afterResult is WorkspaceResearchTargetCompositionResult.Rejected afterRejected)
            {
                return new WorkspaceImplementationComparisonResult
                    .CompositionRejected(
                        QueryComparisonSide.After,
                        afterRejected,
                        before,
                        [
                            .. beforeForwarders,
                            .. Forwarders(
                                QueryComparisonSide.After,
                                afterRejected.Evidence,
                                request.DeclaringType),
                        ]);
            }

            WorkspaceResearchTargetCompositionReceipt after =
                ((WorkspaceResearchTargetCompositionResult.Composed)afterResult).Receipt;
            ImmutableArray<WorkspaceTypeForwarderUse> forwarders =
            [
                .. beforeForwarders,
                .. Forwarders(
                    QueryComparisonSide.After,
                    after.Evidence,
                    request.DeclaringType),
            ];
            ImmutableArray<ResearchTargetCorrespondenceOutcome> correspondences =
            [
                .. plan.Resolution.Correspondences
                    .Where(item =>
                        ReferenceEquals(item.Scope, before.Scope)
                        && (ReferenceEquals(item.DomainId, before.Domain)
                            || ReferenceEquals(item.DomainId, after.Domain))),
            ];
            if (correspondences.IsEmpty)
            {
                return HandoffFailure(
                    WorkspaceImplementationComparisonHandoffFailureKind
                        .MissingCorrespondence,
                    before,
                    after,
                    [],
                    forwarders);
            }

            ImmutableArray<WorkspaceResearchTargetHandoffResult> handoffs =
            [
                .. correspondences.Select(item =>
                    WorkspaceResearchTargetHandoff.Execute(
                        before,
                        after,
                        plan.Resolution,
                        item)),
            ];
            if (handoffs.Any(item =>
                    item is WorkspaceResearchTargetHandoffResult.Rejected))
            {
                return HandoffFailure(
                    WorkspaceImplementationComparisonHandoffFailureKind.Rejected,
                    before,
                    after,
                    handoffs,
                    forwarders);
            }

            WorkspaceResearchTargetHandoffResult.Paired[] pairs =
                [.. handoffs.OfType<WorkspaceResearchTargetHandoffResult.Paired>()];
            if (pairs.Length == 0)
            {
                return HandoffFailure(
                    WorkspaceImplementationComparisonHandoffFailureKind.Unavailable,
                    before,
                    after,
                    handoffs,
                    forwarders);
            }
            if (pairs.Length != 1)
            {
                return HandoffFailure(
                    WorkspaceImplementationComparisonHandoffFailureKind.MultiplePairs,
                    before,
                    after,
                    handoffs,
                    forwarders);
            }

            WorkspaceResearchTargetWorkItem workItem = pairs[0].WorkItem;
            ResearchProducerSessionOutcome outcome = ResearchProducerSession.Run(
                new ResearchProducerSessionRequest(
                    plan.Projected.Admission,
                    plan.Resolution,
                    request.Producers),
                cancellationToken);
            EnsurePublishedPair(outcome, workItem, request.Producers.Length);
            return new WorkspaceImplementationComparisonResult.Published(
                new(
                    workItem,
                    forwarders,
                    outcome));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return new WorkspaceImplementationComparisonResult.Cancelled();
        }
    }

    static WorkspaceImplementationComparisonResult.HandoffFailed HandoffFailure(
        WorkspaceImplementationComparisonHandoffFailureKind kind,
        WorkspaceResearchTargetCompositionReceipt before,
        WorkspaceResearchTargetCompositionReceipt after,
        ImmutableArray<WorkspaceResearchTargetHandoffResult> outcomes,
        ImmutableArray<WorkspaceTypeForwarderUse> forwarders)
        => new(kind, before, after, outcomes, forwarders);

    static bool TryRetain(
        WorkspaceImplementationComparisonSide side,
        out ImmutableArray<ImplementationComparisonBinding> bindings,
        out int failedIndex,
        out AssemblyReferenceIdentity? failedAssembly,
        out CandidateOpenFailure? failure)
    {
        var retained = ImmutableArray.CreateBuilder<ImplementationComparisonBinding>(
            side.Bindings.Length);
        for (int index = 0; index < side.Bindings.Length; index++)
        {
            ImplementationComparisonBinding binding = side.Bindings[index];
            AssemblyImageAccessResult<ResolvedAssemblyReference> result =
                side.Group.RetainAssemblyReference(binding.Assembly);
            if (result is AssemblyImageAccessResult<ResolvedAssemblyReference>.Rejected rejected)
            {
                bindings = [];
                failedIndex = index;
                failedAssembly = rejected.Assembly.Identity;
                failure = rejected.Failure;
                return false;
            }

            retained.Add(binding with
            {
                Assembly =
                    ((AssemblyImageAccessResult<ResolvedAssemblyReference>.Available)result)
                    .Value,
            });
        }

        bindings = retained.ToImmutable();
        failedIndex = -1;
        failedAssembly = null;
        failure = null;
        return true;
    }

    static ImmutableArray<WorkspaceTypeForwarderUse> Forwarders(
        QueryComparisonSide side,
        WorkspaceTypeResolutionEvidence? evidence,
        MetadataTypeDefinitionName declaringType)
    {
        if (evidence is not WorkspaceTypeResolutionEvidence.Available available)
            return [];
        ImmutableArray<WorkspaceMetadataEvidence.Hop> hops =
            available.Outcome.Hops;
        var uses = ImmutableArray.CreateBuilder<WorkspaceTypeForwarderUse>(
            hops.Length);
        for (int index = 0; index < hops.Length; index++)
        {
            WorkspaceMetadataEvidence.Hop hop = hops[index];
            QueryComparisonInputId? input =
                hop.SourceAssembly.Assembly.Registration.Input;
            if (input is null || input.Side != side)
            {
                throw new InvalidOperationException(
                    "A published forwarding hop must identify its exact side-local input.");
            }

            AssemblyReferenceIdentity identity =
                hop.SourceAssembly.Assembly.Identity;
            FindingInspection<TypeForwarderInfo> inspection =
                MetadataFindings.InspectTypeForwarders(
                    [
                        new(
                            declaringType.ToMetadataFullName(),
                            hop.TargetReference.Name),
                    ],
                    new FindingSubject(
                        (identity with { Version = null }).ToString(),
                        identity.Name));
            if (inspection.Value is not
                    FindingInspection<TypeForwarderInfo>.Complete
                    {
                        Findings.Length: 1,
                    } complete)
            {
                throw new InvalidOperationException(
                    "Metadata did not issue exactly one Finding for a followed forwarding hop.");
            }

            uses.Add(new(side, index, input, complete.Findings[0]));
        }
        return uses.ToImmutable();
    }

    static void EnsurePublishedPair(
        ResearchProducerSessionOutcome outcome,
        WorkspaceResearchTargetWorkItem workItem,
        int producerCount)
    {
        if (outcome is not ResearchProducerSessionOutcome.Completed completed)
            return;
        int count = completed.Completion.WorkItems.Count(item =>
            item.Basis is ResearchProducerWorkBasis.Correspondence basis
            && ReferenceEquals(basis.Outcome, workItem.Correspondence));
        if (count != producerCount)
        {
            throw new InvalidOperationException(
                "The producer completion did not retain every requested producer for the effective correspondence.");
        }
    }
}
