using System.Collections.Immutable;

namespace ILInspector.Analysis;

internal sealed record ImplementationMetricExceptionRegionCounts(
    int CatchCount,
    int FilterCount,
    int FinallyCount,
    int FaultCount);

internal sealed record ImplementationMetricLocalEvidence(
    int DeclaredCount,
    string? IncompleteReason)
{
    internal bool IsComplete => IncompleteReason is null;
}

internal sealed record ImplementationMetricInstructionShape(
    int InstructionCount,
    int DistinctOpcodeCount);

internal sealed record ImplementationMetricControlFlow(
    int BasicBlockCount,
    int BranchCount,
    int ConditionalBranchCount,
    int SwitchCount,
    int SwitchTargetCount,
    int LoopCount)
{
    internal int NormalFlowCyclomaticComplexity =>
        1 + ConditionalBranchCount - SwitchCount + SwitchTargetCount;
}

internal sealed record ImplementationMetricDirectCalls(
    int InvocationCount,
    int DistinctTargetCount,
    string? IncompleteReason)
{
    internal bool IsComplete => IncompleteReason is null;
}

internal sealed record MethodImplementationMetricEvidence(
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

internal sealed record ImplementationMetricStageParticipation(
    ImplementationMetricWorkStage Stage,
    ImplementationMetricEvidenceKind EvidenceCauses,
    LibraryBodyAnalysisFeatures FeatureCauses,
    int AttemptedBodies,
    int CompletedBodies,
    int FailedBodies);

internal sealed record ImplementationMetricParticipationReceipt(
    ImplementationMetricEvidenceKind RequestedEvidence,
    ImplementationMetricEvidenceKind EffectiveEvidence,
    ImplementationMetricWorkStage PlannedStages,
    bool HasCompleteStageParticipation,
    ImmutableArray<ImplementationMetricStageParticipation>
        ActualStages,
    ImplementationMetricWorkBudgetSnapshot? Work);

internal sealed record LibraryImplementationMetricAnalysisResult(
    LibraryBodyAnalysisReceipt Receipt,
    bool WasRequested,
    ImplementationMetricParticipationReceipt? Participation,
    ImmutableArray<MethodIdentity> DeclaredMethods,
    ImmutableArray<MethodIdentity> ManagedMethodBodies,
    ImmutableArray<MethodImplementationMetricEvidence> Bodies,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

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
                participation.EvidenceCauses |=
                    _plan.EvidenceCausesFor(stage);
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
        internal ImplementationMetricEvidenceKind EvidenceCauses;
        internal LibraryBodyAnalysisFeatures FeatureCauses;
        internal int AttemptedBodies;
        internal int CompletedBodies;
        internal int FailedBodies;

        internal ImplementationMetricStageParticipation Snapshot(
            ImplementationMetricWorkStage stage) =>
            new(
                stage,
                EvidenceCauses,
                FeatureCauses,
                AttemptedBodies,
                CompletedBodies,
                FailedBodies);
    }
}
