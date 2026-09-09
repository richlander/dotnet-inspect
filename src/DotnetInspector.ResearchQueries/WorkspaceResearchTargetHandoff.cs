using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>
/// An existing live Research pair authorized by two inert side-local receipts.
/// This handoff-only work item is not a composition result or retained composition currency.
/// </summary>
public sealed class WorkspaceResearchTargetWorkItem
{
    internal WorkspaceResearchTargetWorkItem(
        WorkspaceResearchTargetCompositionReceipt before,
        WorkspaceResearchTargetCompositionReceipt after,
        ResearchTargetCorrespondenceOutcome.Paired correspondence)
    {
        Before = before;
        After = after;
        Correspondence = correspondence;
    }

    public WorkspaceResearchTargetCompositionReceipt Before { get; }
    public WorkspaceResearchTargetCompositionReceipt After { get; }
    public ResearchTargetCorrespondenceOutcome.Paired Correspondence { get; }
}

public abstract class WorkspaceResearchTargetHandoffResult
{
    private WorkspaceResearchTargetHandoffResult() { }

    public sealed class Paired : WorkspaceResearchTargetHandoffResult
    {
        internal Paired(WorkspaceResearchTargetWorkItem workItem) => WorkItem = workItem;
        public WorkspaceResearchTargetWorkItem WorkItem { get; }
    }

    public sealed class Unavailable : WorkspaceResearchTargetHandoffResult
    {
        internal Unavailable(ResearchTargetCorrespondenceOutcome correspondence) => Correspondence = correspondence;
        public ResearchTargetCorrespondenceOutcome Correspondence { get; }
    }

    public sealed class Rejected : WorkspaceResearchTargetHandoffResult
    {
        internal Rejected(ResearchTargetCorrespondenceOutcome correspondence) => Correspondence = correspondence;
        public ResearchTargetCorrespondenceOutcome Correspondence { get; }
    }
}

/// <summary>Consumes correspondence; it neither establishes a pair nor executes a producer.</summary>
public static class WorkspaceResearchTargetHandoff
{
    public static WorkspaceResearchTargetHandoffResult Execute(
        WorkspaceResearchTargetCompositionReceipt before,
        WorkspaceResearchTargetCompositionReceipt after,
        ResearchTargetResolution resolution,
        ResearchTargetCorrespondenceOutcome correspondence)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(correspondence);
        if (before.Side != QueryComparisonSide.Before || after.Side != QueryComparisonSide.After
            || !ReferenceEquals(before.Operation, after.Operation)
            || !ReferenceEquals(before.Question, after.Question)
            || !ReferenceEquals(before.Scope, after.Scope)
            || !ReferenceEquals(before.Scope.Operation, resolution.Operation)
            || !ReferenceEquals(correspondence.Scope, before.Scope)
            || !resolution.Correspondences.Any(item => ReferenceEquals(item, correspondence))
            || resolution.Attempts.Count(item => ReferenceEquals(item.Id, before.EffectiveAttemptId)) != 1
            || resolution.Attempts.Count(item => ReferenceEquals(item.Id, after.EffectiveAttemptId)) != 1
            || resolution.Censuses.Count(item => WorkspaceResearchTargetEvidenceProjection.Matches(before.Census, item)) != 1
            || resolution.Censuses.Count(item => WorkspaceResearchTargetEvidenceProjection.Matches(after.Census, item)) != 1)
            return new WorkspaceResearchTargetHandoffResult.Rejected(correspondence);

        if (correspondence is not ResearchTargetCorrespondenceOutcome.Paired paired
            || before.Census.Health != ResearchTargetCensusHealth.Healthy
            || after.Census.Health != ResearchTargetCensusHealth.Healthy
            || !ReferenceEquals(before.Domain, after.Domain))
            return new WorkspaceResearchTargetHandoffResult.Unavailable(correspondence);

        if (!ReferenceEquals(paired.Scope, before.Scope)
            || !ReferenceEquals(paired.DomainId, before.Domain)
            || !ReferenceEquals(paired.Before.Attempt.Id, before.EffectiveAttemptId)
            || !ReferenceEquals(paired.After.Attempt.Id, after.EffectiveAttemptId)
            || paired.Before.Side != ResearchComparisonSide.Before
            || paired.After.Side != ResearchComparisonSide.After)
            return new WorkspaceResearchTargetHandoffResult.Rejected(correspondence);

        return new WorkspaceResearchTargetHandoffResult.Paired(new(before, after, paired));
    }
}
