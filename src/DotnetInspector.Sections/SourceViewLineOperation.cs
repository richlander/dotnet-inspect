using Inspector.Text;
using QueryOverflow;
using QuerySpace.Composition;

namespace DotnetInspector.Sections;

public static class SourceViewLineExecutionPolicy
{
    public const int MaximumCandidateRows = 256;
    public const int MaximumUtf16CodeUnits = 32_768;
    public const int MaximumJsonEncodedUtf8Bytes = 65_536;
}

public enum SourceViewLineOperationState
{
    Created,
    Active,
    Completed,
    Failed,
    Cancelled,
}

public sealed class SourceViewLineOperationIdentity
{
    internal SourceViewLineOperationIdentity()
    {
    }

    public override string ToString() =>
        nameof(SourceViewLineOperationIdentity);
}

public sealed class SourceViewLineContinuation
{
    private readonly int _generation;

    internal SourceViewLineContinuation(
        SourceViewLineOperationIdentity operation,
        SourceViewBinding binding,
        int generation)
    {
        Operation = operation;
        Binding = binding;
        _generation = generation;
    }

    public SourceViewLineOperationIdentity Operation { get; }

    public SourceViewBinding Binding { get; }

    internal bool Matches(
        SourceViewLineOperationIdentity operation,
        SourceViewBinding binding,
        int generation) =>
        ReferenceEquals(Operation, operation)
        && ReferenceEquals(Binding, binding)
        && _generation == generation;
}

public sealed class SourceViewLineSegment
{
    private readonly int _count;

    internal SourceViewLineSegment(
        IReadOnlyList<SourceViewLine> rows,
        bool hasCount,
        int count,
        SourceViewLineContinuation? continuation)
    {
        Rows = rows;
        HasCount = hasCount;
        _count = count;
        Continuation = continuation;
    }

    public IReadOnlyList<SourceViewLine> Rows { get; }

    public bool HasCount { get; }

    public int Count =>
        HasCount
            ? _count
            : throw new InvalidOperationException(
                "This Source line segment has no terminal Count result.");

    public SourceViewLineContinuation? Continuation { get; }

    public bool IsComplete => Continuation is null;
}

public sealed class SourceViewLineOperationAdmission
{
    private SourceViewLineOperationAdmission(
        SourceViewLineOperation? operation,
        QueryOverflowDeclineReason? declineReason)
    {
        Operation = operation;
        DeclineReason = declineReason;
    }

    public bool IsAccepted => Operation is not null;

    public SourceViewLineOperation? Operation { get; }

    public QueryOverflowDeclineReason? DeclineReason { get; }

    internal static SourceViewLineOperationAdmission Accepted(
        SourceViewLineOperation operation) =>
        new(operation, declineReason: null);

    internal static SourceViewLineOperationAdmission Declined(
        QueryOverflowDeclineReason reason) =>
        new(operation: null, reason);
}

public sealed class SourceViewLineOperation
{
    private readonly QueryOverflowExecution<SourceViewLine> _execution;
    private DecodedTextPosition _position;
    private int _continuationGeneration;

    private SourceViewLineOperation(
        InspectionEnvelope<SourceView> inspection,
        QueryOverflowPlan<SourceViewLine> plan)
    {
        Inspection = inspection;
        Identity = new();
        _execution = plan.Start();
        _position = inspection.Content.Document.Start;
    }

    public SourceViewLineOperationIdentity Identity { get; }

    public InspectionEnvelope<SourceView> Inspection { get; }

    public SourceViewLineOperationState State { get; private set; } =
        SourceViewLineOperationState.Created;

    public int CandidateRowsConsumed => _execution.CandidateRowsConsumed;

    public int PublishedRows => _execution.PublishedRows;

    public static SourceViewLineOperationAdmission AdmitRows(
        InspectionEnvelope<SourceView> inspection,
        ResolvedRowQueryPlan<SourceViewLine> rowPlan)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(rowPlan);
        ValidatePlan(rowPlan);

        QueryOverflowAdmission<SourceViewLine> admission =
            QueryOverflowPlan<SourceViewLine>.AdmitRows(
                rowPlan,
                static row => row);
        return Create(inspection, admission);
    }

    public static SourceViewLineOperationAdmission AdmitCount(
        InspectionEnvelope<SourceView> inspection,
        ResolvedRowQueryPlan<SourceViewLine> rowPlan)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(rowPlan);
        ValidatePlan(rowPlan);

        QueryOverflowAdmission<SourceViewLine> admission =
            QueryOverflowPlan<SourceViewLine>.AdmitCount(rowPlan);
        return Create(inspection, admission);
    }

    public bool TryPull(
        SourceViewLineContinuation? continuation,
        int finalRowCredit,
        out SourceViewLineSegment? segment) =>
        TryPull(
            continuation,
            finalRowCredit,
            out segment,
            CancellationToken.None);

    public bool TryPull(
        SourceViewLineContinuation? continuation,
        int finalRowCredit,
        out SourceViewLineSegment? segment,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(finalRowCredit);
        if (State is SourceViewLineOperationState.Completed)
        {
            if (continuation is not null)
            {
                throw new ArgumentException(
                    "A completed Source line operation does not accept a continuation.",
                    nameof(continuation));
            }
            segment = null;
            return false;
        }
        if (State is SourceViewLineOperationState.Failed
            or SourceViewLineOperationState.Cancelled)
        {
            throw new InvalidOperationException(
                "A terminal Source line operation cannot pull more rows.");
        }

        ValidateContinuation(continuation);
        if (cancellationToken.IsCancellationRequested)
        {
            Cancel();
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (!_execution.TryRequestInput(
                SourceViewLineExecutionPolicy.MaximumCandidateRows,
                finalRowCredit,
                out QueryOverflowInputRequest request))
        {
            segment = null;
            return false;
        }

        try
        {
            DecodedTextBatch batch =
                Inspection.Content.Document.Pull(
                    _position,
                    new DecodedTextPullLimits(
                        request.MaximumCandidateRows,
                        SourceViewLineExecutionPolicy
                            .MaximumUtf16CodeUnits,
                        SourceViewLineExecutionPolicy
                            .MaximumJsonEncodedUtf8Bytes));
            SourceViewLine[] candidates =
            [
                .. batch.Lines.Select(SourceViewLineProjection.Create),
            ];
            QueryOverflowStep<SourceViewLine> step =
                _execution.Advance(
                    request,
                    candidates,
                    batch.IsComplete);
            CommitPosition(batch, step.ConsumedRows);

            SourceViewLineContinuation? next = null;
            if (step.State is not QueryOverflowExecutionState.Completed)
            {
                _continuationGeneration =
                    checked(_continuationGeneration + 1);
                next = new(
                    Identity,
                    Inspection.Content.Binding,
                    _continuationGeneration);
                State = SourceViewLineOperationState.Active;
            }
            else
            {
                State = SourceViewLineOperationState.Completed;
            }

            segment = new(
                step.Rows,
                step.HasCount,
                step.HasCount ? step.Count : 0,
                next);
            return true;
        }
        catch
        {
            Fail();
            throw;
        }
    }

    public void Cancel()
    {
        if (State is SourceViewLineOperationState.Completed
            or SourceViewLineOperationState.Failed
            or SourceViewLineOperationState.Cancelled)
        {
            throw new InvalidOperationException(
                "A terminal Source line operation cannot be cancelled.");
        }

        _execution.Cancel();
        State = SourceViewLineOperationState.Cancelled;
    }

    private static SourceViewLineOperationAdmission Create(
        InspectionEnvelope<SourceView> inspection,
        QueryOverflowAdmission<SourceViewLine> admission) =>
        admission.Plan is { } plan
            ? SourceViewLineOperationAdmission.Accepted(
                new(inspection, plan))
            : SourceViewLineOperationAdmission.Declined(
                admission.DeclineReason
                    ?? throw new InvalidOperationException(
                        "A declined QueryOverflow plan has no reason."));

    private static void ValidatePlan(
        ResolvedRowQueryPlan<SourceViewLine> rowPlan)
    {
        if (!SourceViewLineVocabulary.Owns(rowPlan))
        {
            throw new ArgumentException(
                "The resolved row plan does not belong to the Source line vocabulary.",
                nameof(rowPlan));
        }
    }

    private void ValidateContinuation(
        SourceViewLineContinuation? continuation)
    {
        if (_continuationGeneration == 0)
        {
            if (continuation is not null)
            {
                throw new ArgumentException(
                    "A new Source line operation does not accept a continuation.",
                    nameof(continuation));
            }
            return;
        }

        if (continuation is null
            || !continuation.Matches(
                Identity,
                Inspection.Content.Binding,
                _continuationGeneration))
        {
            throw new ArgumentException(
                "The continuation does not match this Source line operation, binding, and generation.",
                nameof(continuation));
        }
    }

    private void CommitPosition(
        DecodedTextBatch batch,
        int consumedRows)
    {
        if (consumedRows <= 0
            || consumedRows > batch.Lines.Length)
        {
            throw new InvalidOperationException(
                "QueryOverflow consumed an invalid decoded-text prefix.");
        }

        if (consumedRows == batch.Lines.Length)
        {
            if (batch.Continuation is { } continuation)
                _position = continuation;
            return;
        }

        DecodedTextBatch consumed =
            Inspection.Content.Document.Pull(
                _position,
                DecodedTextPullLimits.ForCandidateRows(consumedRows));
        _position = consumed.Continuation
            ?? throw new InvalidOperationException(
                "A short QueryOverflow consumption unexpectedly exhausted the decoded Source document.");
    }

    private void Fail()
    {
        if (_execution.State is not QueryOverflowExecutionState.Completed
            and not QueryOverflowExecutionState.Failed
            and not QueryOverflowExecutionState.Cancelled)
        {
            _execution.Fail();
        }
        State = SourceViewLineOperationState.Failed;
    }
}
