using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// One current library-body Analysis execution stage observed for diagnostic
/// participation evidence.
/// </summary>
public enum LibraryBodyAnalysisStage
{
    MethodEnumeration,
    ManagedBodyAcquisition,
    LocalSignatureDecode,
    CanonicalMethodContext,
    AllocationAnalysis,
    SafetyAnalysis,
    BodySignalAnalysis,
    CallAnalysis,
    StringMaterializationAnalysis,
    OptimizationOpportunityAnalysis,
    AsyncSiblingAnalysis,
    ResultAggregation,
}

/// <summary>Actual participation in one library-body Analysis stage.</summary>
public sealed record LibraryBodyAnalysisStageParticipation(
    LibraryBodyAnalysisStage Stage,
    int Attempts,
    int Completions,
    int Failures);

/// <summary>
/// Diagnostic evidence of the current physical stages one library-body
/// Analysis execution performed.
/// </summary>
public sealed record LibraryBodyAnalysisStageParticipationReceipt(
    ImmutableArray<LibraryBodyAnalysisStageParticipation> Stages)
{
    public LibraryBodyAnalysisStageParticipation For(
        LibraryBodyAnalysisStage stage) =>
        Stages.FirstOrDefault(entry => entry.Stage == stage)
        ?? new(stage, 0, 0, 0);
}

internal sealed class LibraryBodyAnalysisStageRecorder
{
    readonly object _gate = new();
    readonly Dictionary<LibraryBodyAnalysisStage, MutableParticipation>
        _stages = [];

    internal StageAttempt Start(LibraryBodyAnalysisStage stage)
    {
        Record(stage, attempts: 1, completions: 0, failures: 0);
        return new(this, stage);
    }

    internal void RecordCompleted(
        LibraryBodyAnalysisStage stage,
        int count)
    {
        Record(stage, count, count, failures: 0);
    }

    internal LibraryBodyAnalysisStageParticipationReceipt Snapshot()
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

    void Complete(LibraryBodyAnalysisStage stage) =>
        Record(stage, attempts: 0, completions: 1, failures: 0);

    void Fail(LibraryBodyAnalysisStage stage) =>
        Record(stage, attempts: 0, completions: 0, failures: 1);

    void Record(
        LibraryBodyAnalysisStage stage,
        int attempts,
        int completions,
        int failures)
    {
        lock (_gate)
        {
            if (!_stages.TryGetValue(
                    stage,
                    out MutableParticipation? participation))
            {
                participation = new();
                _stages.Add(stage, participation);
            }

            participation.Attempts += attempts;
            participation.Completions += completions;
            participation.Failures += failures;
        }
    }

    internal sealed class StageAttempt(
        LibraryBodyAnalysisStageRecorder owner,
        LibraryBodyAnalysisStage stage)
        : IDisposable
    {
        bool _finished;

        internal void Complete()
        {
            if (_finished)
                return;
            owner.Complete(stage);
            _finished = true;
        }

        public void Dispose()
        {
            if (_finished)
                return;
            owner.Fail(stage);
            _finished = true;
        }
    }

    sealed class MutableParticipation
    {
        internal int Attempts;
        internal int Completions;
        internal int Failures;

        internal LibraryBodyAnalysisStageParticipation Snapshot(
            LibraryBodyAnalysisStage stage) =>
            new(
                stage,
                Attempts,
                Completions,
                Failures);
    }
}
