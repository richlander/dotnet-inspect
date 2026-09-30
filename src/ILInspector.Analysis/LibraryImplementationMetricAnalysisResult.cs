using System.Collections.Immutable;

namespace ILInspector.Analysis;

public sealed record ImplementationMetricExceptionRegionCounts(
    int CatchCount,
    int FilterCount,
    int FinallyCount,
    int FaultCount);

public sealed record ImplementationMetricLocalEvidence(
    int DeclaredCount,
    string? IncompleteReason)
{
    public bool IsComplete => IncompleteReason is null;
}

public sealed record ImplementationMetricInstructionShape(
    int InstructionCount,
    int DistinctOpcodeCount);

public sealed record ImplementationMetricControlFlow(
    int BasicBlockCount,
    int BranchCount,
    int ConditionalBranchCount,
    int SwitchCount,
    int SwitchTargetCount,
    int LoopCount)
{
    public int NormalFlowCyclomaticComplexity =>
        1 + ConditionalBranchCount - SwitchCount + SwitchTargetCount;
}

public sealed record ImplementationMetricDirectCalls(
    int InvocationCount,
    int DistinctTargetCount,
    string? IncompleteReason)
{
    public bool IsComplete => IncompleteReason is null;
}

public sealed record ImplementationMetricSiblingRelationships(
    ImmutableArray<OverloadCallRelationship> Relationships,
    ImmutableArray<AnalysisDiagnostic> Diagnostics)
{
    public bool IsComplete => Diagnostics.IsEmpty;
}

public sealed record MethodImplementationMetricEvidence(
    MethodIdentity Method,
    MethodIdentity EvidenceMethod,
    int? ILBytes,
    ImplementationMetricExceptionRegionCounts? ExceptionRegions,
    ImplementationMetricLocalEvidence? Locals,
    ImplementationMetricInstructionShape? InstructionShape,
    ImplementationMetricControlFlow? ControlFlow,
    ImplementationMetricDirectCalls? DirectCalls)
{
    internal bool DirectCallCollectionAttempted { get; init; }

    internal bool DirectCallCollectionComplete { get; init; }
}

public sealed record ImplementationMetricStageParticipation(
    ImplementationMetricWorkStage Stage,
    ImplementationMetricKind MetricCauses,
    LibraryBodyAnalysisFeatures FeatureCauses,
    int AttemptedBodies,
    int CompletedBodies,
    int FailedBodies);

public sealed record ImplementationMetricParticipationReceipt(
    ImplementationMetricKind RequestedMetrics,
    ImplementationMetricFactKind RequiredFacts,
    ImplementationMetricWorkStage PlannedStages,
    bool HasCompleteStageParticipation,
    ImmutableArray<ImplementationMetricStageParticipation>
        ActualStages,
    ImplementationMetricWorkBudgetSnapshot? Work);

public sealed class LibraryImplementationMetricAnalysisResult
{
    internal LibraryImplementationMetricAnalysisResult(
        LibraryBodyAnalysisReceipt receipt,
        bool wasRequested,
        ImplementationMetricParticipationReceipt? participation,
        ImmutableArray<MethodIdentity> declaredMethods,
        ImmutableArray<MethodIdentity> managedMethodBodies,
        ImmutableArray<MethodImplementationMetricEvidence> bodies,
        ImplementationMetricSiblingRelationships? siblingRelationships,
        ImmutableArray<AnalysisDiagnostic> diagnostics)
    {
        Receipt = receipt;
        WasRequested = wasRequested;
        Participation = participation;
        DeclaredMethods = declaredMethods;
        ManagedMethodBodies = managedMethodBodies;
        Bodies = bodies;
        SiblingRelationships = siblingRelationships;
        Diagnostics = diagnostics;
    }

    public LibraryBodyAnalysisReceipt Receipt { get; }

    public bool WasRequested { get; }

    public ImplementationMetricParticipationReceipt? Participation
    { get; }

    public ImmutableArray<MethodIdentity> DeclaredMethods { get; }

    public ImmutableArray<MethodIdentity> ManagedMethodBodies { get; }

    public ImmutableArray<MethodImplementationMetricEvidence> Bodies
    { get; }

    public ImplementationMetricSiblingRelationships?
        SiblingRelationships
    { get; }

    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }
}

internal sealed record ImplementationMetricStageParticipationSnapshot(
    ImmutableArray<ImplementationMetricStageParticipation> Stages);

internal sealed class ImplementationMetricExecutionRecorder
{
    readonly object _gate = new();
    readonly ImplementationMetricAnalysisPlan _plan;
    readonly LibraryBodyAnalysisFeatures _requestedFeatures;
    readonly Dictionary<
        ImplementationMetricWorkStage,
        MutableParticipation> _stages = [];

    internal ImplementationMetricExecutionRecorder(
        ImplementationMetricAnalysisPlan plan,
        LibraryBodyAnalysisFeatures requestedFeatures)
    {
        _plan = plan;
        _requestedFeatures = requestedFeatures;
    }

    internal StageAttempt Start(
        ImplementationMetricWorkStage stage)
    {
        lock (_gate)
        {
            MutableParticipation participation =
                GetOrCreate(stage);
            if (_plan.WorkStages.HasFlag(stage))
            {
                participation.MetricCauses |=
                    _plan.MetricCausesFor(stage);
            }
            participation.FeatureCauses |=
                _requestedFeatures;
            participation.AttemptedBodies++;
        }
        return new(this, stage);
    }

    internal ImplementationMetricStageParticipationSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new(
            [
                .. _stages
                    .OrderBy(static pair => pair.Key)
                    .Select(static pair =>
                        pair.Value.Snapshot(pair.Key)),
            ]);
        }
    }

    void Complete(ImplementationMetricWorkStage stage)
    {
        lock (_gate)
            GetOrCreate(stage).CompletedBodies++;
    }

    void Fail(ImplementationMetricWorkStage stage)
    {
        lock (_gate)
            GetOrCreate(stage).FailedBodies++;
    }

    MutableParticipation GetOrCreate(
        ImplementationMetricWorkStage stage)
    {
        if (!_stages.TryGetValue(
                stage,
                out MutableParticipation? participation))
        {
            participation = new();
            _stages.Add(stage, participation);
        }
        return participation;
    }

    internal sealed class StageAttempt(
        ImplementationMetricExecutionRecorder owner,
        ImplementationMetricWorkStage stage)
        : IDisposable
    {
        bool _completed;

        internal void Complete()
        {
            if (_completed)
                return;
            owner.Complete(stage);
            _completed = true;
        }

        public void Dispose()
        {
            if (!_completed)
                owner.Fail(stage);
        }
    }

    sealed class MutableParticipation
    {
        internal ImplementationMetricKind MetricCauses;
        internal LibraryBodyAnalysisFeatures FeatureCauses;
        internal int AttemptedBodies;
        internal int CompletedBodies;
        internal int FailedBodies;

        internal ImplementationMetricStageParticipation Snapshot(
            ImplementationMetricWorkStage stage) =>
            new(
                stage,
                MetricCauses,
                FeatureCauses,
                AttemptedBodies,
                CompletedBodies,
                FailedBodies);
    }
}
