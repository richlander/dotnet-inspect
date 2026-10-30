using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries;

public sealed record AnnotatedSourceDiffDocumentRequest
{
    public AnnotatedSourceDiffDocumentRequest(
        WorkspaceImplementationComparisonSide Before,
        WorkspaceImplementationComparisonSide After,
        MetadataTypeDefinitionName DeclaringType,
        MemberTargetSelector Selector,
        bool IncludeIl = false)
    {
        ArgumentNullException.ThrowIfNull(Before);
        ArgumentNullException.ThrowIfNull(After);
        ArgumentNullException.ThrowIfNull(DeclaringType);
        ArgumentNullException.ThrowIfNull(Selector);
        this.Before = Before;
        this.After = After;
        this.DeclaringType = DeclaringType;
        this.Selector = Selector;
        this.IncludeIl = IncludeIl;
    }

    public WorkspaceImplementationComparisonSide Before { get; }
    public WorkspaceImplementationComparisonSide After { get; }
    public MetadataTypeDefinitionName DeclaringType { get; }
    public MemberTargetSelector Selector { get; }
    public bool IncludeIl { get; }
}

public enum AnnotatedSourceDiffDocumentQueryUnavailability
{
    DivergentTerminalDomains,
    MissingCorrespondence,
    MultipleCorrespondences,
    CorrespondenceRejected,
}

public abstract record AnnotatedSourceDiffDocumentQueryResult
{
    private AnnotatedSourceDiffDocumentQueryResult() { }

    public sealed record Published(
        AnnotatedSourceDiffDocument Document,
        ResearchTargetCorrespondenceOutcome Correspondence)
        : AnnotatedSourceDiffDocumentQueryResult;

    public sealed record Absent(
        ResearchTargetCorrespondenceOutcome.Absent Correspondence)
        : AnnotatedSourceDiffDocumentQueryResult;

    public sealed record Unavailable(
        AnnotatedSourceDiffDocumentQueryUnavailability Reason,
        WorkspaceResearchTargetCompositionReceipt? Before,
        WorkspaceResearchTargetCompositionReceipt? After,
        ImmutableArray<ResearchTargetCorrespondenceOutcome> Correspondences,
        ImmutableArray<WorkspaceTypeForwarderUse> Forwarders)
        : AnnotatedSourceDiffDocumentQueryResult;

    public sealed record PreparationFailed(
        WorkspaceImplementationComparisonResult Failure)
        : AnnotatedSourceDiffDocumentQueryResult;

    public sealed record Cancelled
        : AnnotatedSourceDiffDocumentQueryResult;
}

public static class AnnotatedSourceDiffDocumentQuery
{
    public static InspectionQuery<AnnotatedSourceDiffDocumentQueryResult>
        Definition { get; } =
            new("Annotated source diff document", InspectionCost.Unbounded);

    public static AnnotatedSourceDiffDocumentQueryResult Execute(
        AnnotatedSourceDiffDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            WorkspaceImplementationComparisonPlanPreparationResult planResult =
                WorkspaceImplementationComparisonQuery.PreparePlan(
                    request.Before,
                    request.After,
                    request.DeclaringType,
                    request.Selector,
                    cancellationToken);
            if (planResult is
                WorkspaceImplementationComparisonPlanPreparationResult.Failed failed)
            {
                return new AnnotatedSourceDiffDocumentQueryResult
                    .PreparationFailed(failed.Result);
            }

            WorkspaceResearchTargetPlan plan =
                ((WorkspaceImplementationComparisonPlanPreparationResult.Prepared)
                    planResult).Plan;
            SideComposition beforeComposition = Compose(
                request.Before,
                QueryComparisonSide.Before,
                plan,
                cancellationToken);
            if (beforeComposition.Rejected is { } beforeRejected)
            {
                return new AnnotatedSourceDiffDocumentQueryResult
                    .PreparationFailed(
                        new WorkspaceImplementationComparisonResult
                            .CompositionRejected(
                                QueryComparisonSide.Before,
                                beforeRejected,
                                completedSide: null,
                                beforeComposition.Forwarders));
            }
            SideComposition afterComposition = Compose(
                request.After,
                QueryComparisonSide.After,
                plan,
                cancellationToken);
            ImmutableArray<WorkspaceTypeForwarderUse> forwarders =
            [
                .. beforeComposition.Forwarders,
                .. afterComposition.Forwarders,
            ];
            if (afterComposition.Rejected is { } afterRejected)
            {
                return new AnnotatedSourceDiffDocumentQueryResult
                    .PreparationFailed(
                        new WorkspaceImplementationComparisonResult
                            .CompositionRejected(
                                QueryComparisonSide.After,
                                afterRejected,
                                beforeComposition.Receipt,
                                forwarders));
            }
            ResearchTargetDomainId? terminalDomain =
                beforeComposition.Domain ?? afterComposition.Domain;
            if (terminalDomain is null)
            {
                SideComposition unavailable =
                    beforeComposition.Unavailable is not null
                        ? beforeComposition
                        : afterComposition;
                return new AnnotatedSourceDiffDocumentQueryResult
                    .PreparationFailed(
                        new WorkspaceImplementationComparisonResult
                            .CompositionUnavailable(
                                unavailable.Side,
                                unavailable.Unavailable!,
                                beforeComposition.Receipt,
                                forwarders));
            }
            if (beforeComposition.Domain is { } beforeDomain
                && afterComposition.Domain is { } afterDomain
                && !ReferenceEquals(beforeDomain, afterDomain))
            {
                ImmutableArray<ResearchTargetCorrespondenceOutcome>
                    divergentCorrespondences =
                [
                    .. Correspondences(plan, beforeDomain),
                    .. Correspondences(plan, afterDomain),
                ];
                return Unavailable(
                    AnnotatedSourceDiffDocumentQueryUnavailability
                        .DivergentTerminalDomains,
                    beforeComposition,
                    afterComposition,
                    forwarders,
                    divergentCorrespondences);
            }

            ImmutableArray<ResearchTargetCorrespondenceOutcome> correspondences =
                Correspondences(plan, terminalDomain);
            if (correspondences.IsEmpty)
            {
                return Unavailable(
                    AnnotatedSourceDiffDocumentQueryUnavailability
                        .MissingCorrespondence,
                    beforeComposition,
                    afterComposition,
                    forwarders,
                    []);
            }
            if (correspondences.Length != 1)
            {
                return Unavailable(
                    AnnotatedSourceDiffDocumentQueryUnavailability
                        .MultipleCorrespondences,
                    beforeComposition,
                    afterComposition,
                    forwarders,
                    correspondences);
            }

            ResearchTargetCorrespondenceOutcome correspondence =
                correspondences[0];
            if (correspondence is ResearchTargetCorrespondenceOutcome.Absent absent)
                return new AnnotatedSourceDiffDocumentQueryResult.Absent(absent);

            AnnotatedSourceDiffSide beforeSide;
            AnnotatedSourceDiffSide afterSide;
            switch (correspondence)
            {
                case ResearchTargetCorrespondenceOutcome.Paired paired:
                    if (beforeComposition.Receipt is not { } before
                        || afterComposition.Receipt is not { } after
                        || paired.Before.Target.Address is null
                        || paired.After.Target.Address is null)
                    {
                        return Unavailable(
                            AnnotatedSourceDiffDocumentQueryUnavailability
                                .CorrespondenceRejected,
                            beforeComposition,
                            afterComposition,
                            forwarders,
                            correspondences);
                    }
                    if (WorkspaceResearchTargetHandoff.Execute(
                            before,
                            after,
                            plan.Resolution,
                            paired)
                        is not WorkspaceResearchTargetHandoffResult.Paired)
                    {
                        return Unavailable(
                            AnnotatedSourceDiffDocumentQueryUnavailability
                                .CorrespondenceRejected,
                            beforeComposition,
                            afterComposition,
                            forwarders,
                            correspondences);
                    }
                    beforeSide = Project(
                        request.Before,
                        before,
                        paired.Before,
                        plan,
                        request,
                        cancellationToken);
                    afterSide = Project(
                        request.After,
                        after,
                        paired.After,
                        plan,
                        request,
                        cancellationToken);
                    break;
                case ResearchTargetCorrespondenceOutcome.BeforeOnly beforeOnly:
                    if (beforeComposition.Receipt is not { } beforeReceipt
                        || !ValidTarget(beforeReceipt, beforeOnly.Before)
                        || beforeOnly.Before.Target.Address is null)
                    {
                        return Unavailable(
                            AnnotatedSourceDiffDocumentQueryUnavailability
                                .CorrespondenceRejected,
                            beforeComposition,
                            afterComposition,
                            forwarders,
                            correspondences);
                    }
                    beforeSide = Project(
                        request.Before,
                        beforeReceipt,
                        beforeOnly.Before,
                        plan,
                        request,
                        cancellationToken);
                    afterSide = AnnotatedSourceDiffSide.Absent();
                    break;
                case ResearchTargetCorrespondenceOutcome.AfterOnly afterOnly:
                    if (afterComposition.Receipt is not { } afterReceipt
                        || !ValidTarget(afterReceipt, afterOnly.After)
                        || afterOnly.After.Target.Address is null)
                    {
                        return Unavailable(
                            AnnotatedSourceDiffDocumentQueryUnavailability
                                .CorrespondenceRejected,
                            beforeComposition,
                            afterComposition,
                            forwarders,
                            correspondences);
                    }
                    beforeSide = AnnotatedSourceDiffSide.Absent();
                    afterSide = Project(
                        request.After,
                        afterReceipt,
                        afterOnly.After,
                        plan,
                        request,
                        cancellationToken);
                    break;
                case ResearchTargetCorrespondenceOutcome
                    .CounterpartUnavailable counterpart:
                    beforeSide = AnnotatedSourceDiffSide.Unavailable(
                        AnnotatedSourceDiffSideReason.CounterpartUnavailable,
                        counterpart.Taint.Kind.ToString());
                    afterSide = AnnotatedSourceDiffSide.Unavailable(
                        AnnotatedSourceDiffSideReason.CounterpartUnavailable,
                        counterpart.Taint.Kind.ToString());
                    break;
                case ResearchTargetCorrespondenceOutcome
                    .DomainUnavailable domain:
                    beforeSide = AnnotatedSourceDiffSide.Unavailable(
                        AnnotatedSourceDiffSideReason.DomainUnavailable,
                        domain.Taint.Kind.ToString());
                    afterSide = AnnotatedSourceDiffSide.Unavailable(
                        AnnotatedSourceDiffSideReason.DomainUnavailable,
                        domain.Taint.Kind.ToString());
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown Research correspondence outcome.");
            }

            return new AnnotatedSourceDiffDocumentQueryResult.Published(
                AnnotatedSourceDiffDocument.Create(
                    new(
                        DisplayName(request.DeclaringType),
                        request.Selector.NormalizedSelector),
                    beforeSide,
                    afterSide,
                    ProjectForwarders(forwarders),
                    request.IncludeIl),
                correspondence);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return new AnnotatedSourceDiffDocumentQueryResult.Cancelled();
        }
    }

    static AnnotatedSourceDiffDocumentQueryResult.Unavailable Unavailable(
        AnnotatedSourceDiffDocumentQueryUnavailability reason,
        SideComposition before,
        SideComposition after,
        ImmutableArray<WorkspaceTypeForwarderUse> forwarders,
        ImmutableArray<ResearchTargetCorrespondenceOutcome> correspondences)
        => new(
            reason,
            before.Receipt,
            after.Receipt,
            correspondences,
            forwarders);

    static ImmutableArray<ResearchTargetCorrespondenceOutcome>
        Correspondences(
            WorkspaceResearchTargetPlan plan,
            ResearchTargetDomainId domain)
        =>
        [
            .. plan.Resolution.Correspondences.Where(
                item => ReferenceEquals(item.Scope, plan.Scope.Id)
                    && ReferenceEquals(item.DomainId, domain)),
        ];

    static SideComposition Compose(
        WorkspaceImplementationComparisonSide side,
        QueryComparisonSide comparisonSide,
        WorkspaceResearchTargetPlan plan,
        CancellationToken cancellationToken)
    {
        WorkspaceResearchTargetCompositionResult result = plan.Compose(
            side.Group,
            side.Root,
            comparisonSide,
            side.ResolutionScope,
            cancellationToken);
        return result switch
        {
            WorkspaceResearchTargetCompositionResult.Composed composed =>
                new(
                    comparisonSide,
                    composed.Receipt,
                    composed.Receipt.Domain,
                    Unavailable: null,
                    Rejected: null,
                    WorkspaceImplementationComparisonQuery.Forwarders(
                        comparisonSide,
                        composed.Receipt.Evidence,
                        plan.Population)),
            WorkspaceResearchTargetCompositionResult.Unavailable unavailable =>
                new(
                    comparisonSide,
                    Receipt: null,
                    unavailable.Census?.DomainId,
                    unavailable,
                    Rejected: null,
                    WorkspaceImplementationComparisonQuery.Forwarders(
                        comparisonSide,
                        unavailable.Evidence,
                        plan.Population)),
            WorkspaceResearchTargetCompositionResult.Rejected rejected =>
                new(
                    comparisonSide,
                    Receipt: null,
                    rejected.Census?.DomainId,
                    Unavailable: null,
                    rejected,
                    WorkspaceImplementationComparisonQuery.Forwarders(
                        comparisonSide,
                        rejected.Evidence,
                        plan.Population)),
            _ => throw new InvalidOperationException(
                "Unknown workspace target composition outcome."),
        };
    }

    static AnnotatedSourceDiffSide Project(
        WorkspaceImplementationComparisonSide side,
        WorkspaceResearchTargetCompositionReceipt receipt,
        ResearchCorrespondingTarget target,
        WorkspaceResearchTargetPlan plan,
        AnnotatedSourceDiffDocumentRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidTarget(receipt, target)
            || target.Target.Address is not { } address)
        {
            throw new InvalidOperationException(
                "An occupied correspondence side must match its exact composition receipt.");
        }

        ImplementationComparisonBinding binding =
            plan.Population.Inputs.Single(input =>
                ReferenceEquals(
                    input.Id,
                    receipt.TerminalInput)).Binding;
        AssemblyContextParticipant participant =
            side.Group.Participants.Single(candidate =>
                ReferenceEquals(
                    candidate.Assembly.Registration,
                    binding.Assembly.Registration));
        var endpoint = new AnnotatedSourceDiffEndpoint(
            participant.Assembly.Identity.Name,
            address.ModuleVersionId,
            address.Token,
            target.Target.Role);
        AssemblyContextEntry<AssemblyMemberProjection> projection =
            AssemblyContextMemberProjectionQuery.ExecuteParticipant(
                side.Group,
                participant,
                new(
                    DisplayName(request.DeclaringType),
                    request.Selector.NormalizedSelector,
                    MethodToken: address.Token,
                    SourceDocument: true));
        cancellationToken.ThrowIfCancellationRequested();
        switch (projection)
        {
            case AssemblyContextEntry<AssemblyMemberProjection>.Available available:
                MemberProjectionResult result = available.Value.Projection;
                if (result.SourceDocument is { } document)
                {
                    if (result.SelectedMethodToken != address.Token)
                    {
                        return AnnotatedSourceDiffSide.Failed(
                            endpoint,
                            AnnotatedSourceDiffSideReason.ProjectionFailed,
                            "The projection selected a different MethodDef token.");
                    }
                    return AnnotatedSourceDiffSide.Present(endpoint, document);
                }
                string? detail = result.SourceDocumentFailure is { } failure
                    ? string.Join(
                        "; ",
                        failure.Diagnostics.Select(static diagnostic =>
                            diagnostic.ToString()))
                    : null;
                return result.SourceDocumentFailureKind switch
                {
                    MemberProjectionSourceDocumentFailureKind.NoManagedBody =>
                        AnnotatedSourceDiffSide.NotApplicable(endpoint, detail),
                    _ => AnnotatedSourceDiffSide.Failed(
                        endpoint,
                        AnnotatedSourceDiffSideReason.ProjectionFailed,
                        detail ?? "Source-document projection produced no document."),
                };
            case AssemblyContextEntry<AssemblyMemberProjection>.Rejected rejected:
                return AnnotatedSourceDiffSide.Failed(
                    endpoint,
                    AnnotatedSourceDiffSideReason.ProjectionRejected,
                    rejected.Failure.ToString());
            case AssemblyContextEntry<AssemblyMemberProjection>.Failed failed:
                return AnnotatedSourceDiffSide.Failed(
                    endpoint,
                    AnnotatedSourceDiffSideReason.ProjectionFailed,
                    $"{failed.Error.GetType().Name}: {failed.Error.Message}");
            default:
                throw new InvalidOperationException(
                    "Unknown assembly-context projection outcome.");
        }
    }

    static bool ValidTarget(
        WorkspaceResearchTargetCompositionReceipt receipt,
        ResearchCorrespondingTarget target)
        => ReferenceEquals(target.Attempt.Id, receipt.EffectiveAttemptId)
            && target.Side
                == (receipt.Side == QueryComparisonSide.Before
                    ? ResearchComparisonSide.Before
                    : ResearchComparisonSide.After)
            && ReferenceEquals(target.CorrespondenceKey.Domain, receipt.Domain)
            && ReferenceEquals(target.CorrespondenceKey.Scope, receipt.Scope);

    static ImmutableArray<AnnotatedSourceDiffForwarder> ProjectForwarders(
        ImmutableArray<WorkspaceTypeForwarderUse> forwarders)
        =>
        [
            .. forwarders
                .Select(static forwarder =>
                    new AnnotatedSourceDiffForwarder(
                        forwarder.Side == QueryComparisonSide.Before
                            ? AnnotatedSourceDiffSideKind.Before
                            : AnnotatedSourceDiffSideKind.After,
                        forwarder.HopIndex,
                        forwarder.Finding.Payload.TypeName,
                        forwarder.Finding.Payload.TargetAssembly))
                .OrderBy(static forwarder => forwarder.Side)
                .ThenBy(static forwarder => forwarder.HopIndex)
                .ThenBy(
                    static forwarder => forwarder.TypeName,
                    StringComparer.Ordinal)
                .ThenBy(
                    static forwarder => forwarder.TargetAssembly,
                    StringComparer.Ordinal),
        ];

    static string DisplayName(MetadataTypeDefinitionName name)
        => string.IsNullOrEmpty(name.Namespace)
            ? string.Join('+', name.Segments)
            : $"{name.Namespace}.{string.Join('+', name.Segments)}";

    sealed record SideComposition(
        QueryComparisonSide Side,
        WorkspaceResearchTargetCompositionReceipt? Receipt,
        ResearchTargetDomainId? Domain,
        WorkspaceResearchTargetCompositionResult.Unavailable? Unavailable,
        WorkspaceResearchTargetCompositionResult.Rejected? Rejected,
        ImmutableArray<WorkspaceTypeForwarderUse> Forwarders);
}
