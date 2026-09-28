using System.Collections.Immutable;

using Inspector.Text;
using QuerySpace.Composition;

namespace DotnetInspector.Sections;

public enum SourceLineDemand
{
    ExactCount,
    BoundedRows,
    UnboundedRows,
}

public enum SourceLineExecutionStrategy
{
    Complete,
    ColdPull,
}

public sealed class SourceLineDeliveryProfile
{
    public SourceLineDeliveryProfile(
        string identity,
        string policyGeneration,
        int exactCountThresholdUtf16,
        int boundedRowsThresholdUtf16,
        int unboundedRowsThresholdUtf16)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(
            exactCountThresholdUtf16);
        ArgumentOutOfRangeException.ThrowIfNegative(
            boundedRowsThresholdUtf16);
        ArgumentOutOfRangeException.ThrowIfNegative(
            unboundedRowsThresholdUtf16);

        Identity = identity;
        PolicyGeneration = policyGeneration;
        ExactCountThresholdUtf16 = exactCountThresholdUtf16;
        BoundedRowsThresholdUtf16 = boundedRowsThresholdUtf16;
        UnboundedRowsThresholdUtf16 =
            unboundedRowsThresholdUtf16;
    }

    public string Identity { get; }

    public string PolicyGeneration { get; }

    public int ExactCountThresholdUtf16 { get; }

    public int BoundedRowsThresholdUtf16 { get; }

    public int UnboundedRowsThresholdUtf16 { get; }

    internal int Threshold(SourceLineDemand demand) =>
        demand switch
        {
            SourceLineDemand.ExactCount =>
                ExactCountThresholdUtf16,
            SourceLineDemand.BoundedRows =>
                BoundedRowsThresholdUtf16,
            SourceLineDemand.UnboundedRows =>
                UnboundedRowsThresholdUtf16,
            _ => throw new ArgumentOutOfRangeException(
                nameof(demand)),
        };
}

public sealed class SourceLineExecutionBounds
{
    public static SourceLineExecutionBounds Production { get; } =
        new(
            maximumCandidateRows: 256,
            maximumUtf16CodeUnits: 32_768,
            maximumJsonEncodedUtf8Bytes: 65_536);

    public SourceLineExecutionBounds(
        int maximumCandidateRows,
        int maximumUtf16CodeUnits,
        int maximumJsonEncodedUtf8Bytes)
    {
        Limits = new DecodedTextPullLimits(
            maximumCandidateRows,
            maximumUtf16CodeUnits,
            maximumJsonEncodedUtf8Bytes);
    }

    public int MaximumCandidateRows =>
        Limits.MaximumCandidateRows;

    public int MaximumUtf16CodeUnits =>
        Limits.MaximumUtf16CodeUnits;

    public int MaximumJsonEncodedUtf8Bytes =>
        Limits.MaximumJsonEncodedUtf8Bytes;

    internal DecodedTextPullLimits Limits { get; }
}

public readonly struct SourceLineCandidate
{
    private readonly DecodedTextLineSlice _line;

    internal SourceLineCandidate(DecodedTextLineSlice line)
    {
        _line = line;
    }

    public int Number => _line.Number;

    public int Start => _line.Start;

    public ReadOnlyMemory<char> Content => _line.Content;

    public SourceViewLineTerminator Terminator =>
        SourceLineVocabulary.SourceTerminator(_line.Terminator);

    public string TerminatorText => _line.TerminatorText;

    public int Utf16CodeUnits => _line.Utf16CodeUnits;

    public int JsonEncodedUtf8Bytes =>
        _line.JsonEncodedUtf8Bytes;

    internal DecodedTextLineSlice Line => _line;

    internal SourceViewLine Project() =>
        new(
            Number,
            Start,
            Content.ToString(),
            Terminator);
}

public static class SourceLineVocabulary
{
    public const string ContentKey = "Content";

    private static readonly RowQueryVocabulary<SourceLineCandidate>
        s_vocabulary =
            RowQueryVocabulary<SourceLineCandidate>.Create(
                RowQueryVocabularyIdentity.Create(),
                [ContentQueryKey()],
                []);

    private static readonly ForwardRowQueryPlan<SourceLineCandidate>
        s_emptyPlan =
            CreateEmptyPlan();

    public static RowQueryVocabulary<SourceLineCandidate> Vocabulary =>
        s_vocabulary;

    public static ForwardRowQueryPlan<SourceLineCandidate> EmptyPlan =>
        s_emptyPlan;

    public static RowQueryResolutionResult<SourceLineCandidate> Resolve(
        RowQueryIntent intent) =>
        RowQueryResolver.Resolve(s_vocabulary, intent);

    internal static bool Owns(
        ForwardRowQueryPlan<SourceLineCandidate> plan) =>
        ReferenceEquals(
            plan.ResolvedPlan.VocabularyIdentity,
            s_vocabulary.Identity);

    internal static SourceViewLineTerminator SourceTerminator(
        DecodedTextLineTerminator terminator) =>
        terminator switch
        {
            DecodedTextLineTerminator.None =>
                SourceViewLineTerminator.None,
            DecodedTextLineTerminator.CarriageReturnLineFeed =>
                SourceViewLineTerminator.CarriageReturnLineFeed,
            DecodedTextLineTerminator.CarriageReturn =>
                SourceViewLineTerminator.CarriageReturn,
            DecodedTextLineTerminator.LineFeed =>
                SourceViewLineTerminator.LineFeed,
            DecodedTextLineTerminator.NextLine =>
                SourceViewLineTerminator.NextLine,
            DecodedTextLineTerminator.LineSeparator =>
                SourceViewLineTerminator.LineSeparator,
            DecodedTextLineTerminator.ParagraphSeparator =>
                SourceViewLineTerminator.ParagraphSeparator,
            _ => throw new InvalidOperationException(
                "Unknown decoded-text line terminator."),
        };

    private static ForwardRowQueryPlan<SourceLineCandidate>
        CreateEmptyPlan()
    {
        RowQueryResolutionResult<SourceLineCandidate> resolution =
            Resolve(RowQueryIntent.Empty);
        ResolvedRowQueryPlan<SourceLineCandidate> resolved =
            resolution.Plan
            ?? throw new InvalidOperationException(
                "The empty Source line query did not resolve.");
        if (!ForwardRowQueryPlanner.TryCreate(
                resolved,
                out ForwardRowQueryPlan<SourceLineCandidate>? plan))
        {
            throw new InvalidOperationException(
                "The empty Source line query is not forward executable.");
        }

        return plan;
    }

    private static RowQueryKey<SourceLineCandidate>
        ContentQueryKey() =>
        RowQueryKey<SourceLineCandidate>.Create(
            RowQueryKeyIdentity.Create(),
            ContentKey,
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            static row =>
                RowQueryValue<ReadOnlyMemory<char>>.Present(
                    row.Content),
            BindContent);

    private static Predicate<ReadOnlyMemory<char>>? BindContent(
        RowQueryOperator operation,
        RowQueryValueToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        string pattern = token.Text;
        return operation switch
        {
            RowQueryOperator.Equals =>
                value => RowQueryText.Matches(
                    value.Span,
                    pattern.AsSpan()),
            RowQueryOperator.NotEquals =>
                value => !RowQueryText.Matches(
                    value.Span,
                    pattern.AsSpan()),
            _ => null,
        };
    }
}

public sealed record SourceLineExecutionObservation(
    SourceLineExecutionStrategy Strategy,
    SourceLineDemand Demand,
    int PullCount,
    int CandidateLinesScanned,
    int SelectedLines,
    int ProjectedRows);

public sealed record SourceLineExecutionSelection(
    SourceLineExecutionStrategy Strategy,
    SourceLineDemand Demand,
    int DecodedUtf16Length,
    string? DeliveryProfileIdentity,
    string? PolicyGeneration,
    int? ThresholdUtf16);

public sealed class SourceLineExecutionBatch
{
    internal SourceLineExecutionBatch(
        ImmutableArray<SourceViewLine> rows,
        bool isComplete,
        int? exactCount)
    {
        Rows = rows;
        IsComplete = isComplete;
        ExactCount = exactCount;
    }

    public ImmutableArray<SourceViewLine> Rows { get; }

    public bool IsComplete { get; }

    public int? ExactCount { get; }
}

public sealed class SourceLineExecution : IDisposable
{
    private readonly QuerySpaceTerminalRequirement _terminal;
    private readonly ForwardRowQueryPlan<SourceLineCandidate> _plan;
    private readonly SourceLineExecutionBounds _bounds;
    private DecodedTextDocument? _document;
    private DecodedTextCursor _cursor;
    private ImmutableArray<PreparedRow> _completeRows;
    private int _completeRowIndex;
    private bool _selectionComplete;
    private bool _hasPending;
    private SourceLineCandidate _pending;
    private DecodedTextCursor _pendingNext;
    private bool _disposed;
    private int _pullCount;
    private int _candidateLinesScanned;
    private int _selectedLines;
    private int _projectedRows;

    private SourceLineExecution(
        DecodedTextDocument document,
        QuerySpaceTerminalRequirement terminal,
        ForwardRowQueryPlan<SourceLineCandidate> plan,
        SourceLineExecutionBounds bounds,
        SourceLineDeliveryProfile? deliveryProfile,
        CancellationToken cancellationToken)
    {
        _document =
            document
            ?? throw new ArgumentNullException(nameof(document));
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(bounds);
        _terminal = terminal;
        _plan = plan;
        _bounds = bounds;
        Selection =
            Select(
                document.Utf16Length,
                terminal,
                plan,
                deliveryProfile);
        _cursor = document.StartCursor;

        if (Strategy is SourceLineExecutionStrategy.Complete)
            PrepareComplete(cancellationToken);
    }

    public SourceLineExecutionSelection Selection { get; }

    public SourceLineExecutionStrategy Strategy =>
        Selection.Strategy;

    public SourceLineDemand Demand => Selection.Demand;

    public int DecodedUtf16Length =>
        Selection.DecodedUtf16Length;

    public string? DeliveryProfileIdentity =>
        Selection.DeliveryProfileIdentity;

    public string? PolicyGeneration =>
        Selection.PolicyGeneration;

    public int? SelectedThresholdUtf16 =>
        Selection.ThresholdUtf16;

    public SourceLineExecutionObservation Observation =>
        new(
            Strategy,
            Demand,
            _pullCount,
            _candidateLinesScanned,
            _selectedLines,
            _projectedRows);

    public static SourceLineExecution Create(
        DecodedTextDocument document,
        QuerySpaceTerminalRequirement terminal,
        ForwardRowQueryPlan<SourceLineCandidate> plan,
        SourceLineDeliveryProfile? deliveryProfile = null,
        SourceLineExecutionBounds? bounds = null,
        CancellationToken cancellationToken = default) =>
        new(
            document,
            terminal,
            plan,
            bounds ?? SourceLineExecutionBounds.Production,
            deliveryProfile,
            cancellationToken);

    public static SourceLineExecutionSelection Select(
        int decodedUtf16Length,
        QuerySpaceTerminalRequirement terminal,
        ForwardRowQueryPlan<SourceLineCandidate> plan,
        SourceLineDeliveryProfile? deliveryProfile = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            decodedUtf16Length);
        ArgumentNullException.ThrowIfNull(plan);
        if (!Enum.IsDefined(terminal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(terminal),
                terminal,
                "Unsupported Source line terminal.");
        }

        if (!SourceLineVocabulary.Owns(plan))
        {
            throw new ArgumentException(
                "The forward plan belongs to a different row vocabulary.",
                nameof(plan));
        }

        SourceLineDemand demand =
            terminal is QuerySpaceTerminalRequirement.Count
                ? SourceLineDemand.ExactCount
                : plan.MaximumResultRows is not null
                    ? SourceLineDemand.BoundedRows
                    : SourceLineDemand.UnboundedRows;
        int? threshold = deliveryProfile?.Threshold(demand);
        SourceLineExecutionStrategy strategy =
            threshold is int value
            && decodedUtf16Length >= value
                ? SourceLineExecutionStrategy.ColdPull
                : SourceLineExecutionStrategy.Complete;
        return new(
            strategy,
            demand,
            decodedUtf16Length,
            deliveryProfile?.Identity,
            deliveryProfile?.PolicyGeneration,
            threshold);
    }

    public SourceLineExecutionBatch Pull(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _pullCount = checked(_pullCount + 1);
            return Strategy is SourceLineExecutionStrategy.Complete
                ? PullComplete()
                : PullCold(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _document = null;
        _completeRows = default;
        _hasPending = false;
    }

    private void PrepareComplete(
        CancellationToken cancellationToken)
    {
        if (_terminal is QuerySpaceTerminalRequirement.Rows
            && _plan.ResolvedPlan.PredicateKeyIdentities.Count == 0
            && _plan.ResolvedPlan.SelectionPlan.Stages.Count == 0)
        {
            PrepareCompleteRows(cancellationToken);
            return;
        }

        DecodedTextDocument document = Document;
        var candidates = new List<PreparedCandidate>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!document.TryReadLine(
                    ref _cursor,
                    out DecodedTextLineSlice line))
            {
                break;
            }

            var candidate = new SourceLineCandidate(line);
            candidates.Add(
                new PreparedCandidate(
                    candidate,
                    candidate.Project()));
            _candidateLinesScanned =
                checked(_candidateLinesScanned + 1);
            _projectedRows = checked(_projectedRows + 1);
        }

        var selected = ImmutableArray.CreateBuilder<PreparedRow>();
        foreach (PreparedCandidate candidate in candidates)
        {
            if (_plan.IsSatisfied(_selectedLines))
                break;
            if (!_plan.Matches(candidate.Candidate))
                continue;

            _selectedLines = checked(_selectedLines + 1);
            if (_terminal is QuerySpaceTerminalRequirement.Rows)
            {
                int jsonEncodedUtf8Bytes =
                    MeasureJsonWithin(candidate.Candidate);
                selected.Add(
                    new PreparedRow(
                        candidate.Row,
                        candidate.Candidate.Utf16CodeUnits,
                        jsonEncodedUtf8Bytes));
            }
        }

        _completeRows = selected.ToImmutable();
        _selectionComplete = true;
    }

    private void PrepareCompleteRows(
        CancellationToken cancellationToken)
    {
        DecodedTextDocument document = Document;
        var rows = ImmutableArray.CreateBuilder<PreparedRow>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!document.TryReadLine(
                    ref _cursor,
                    out DecodedTextLineSlice line))
            {
                break;
            }

            var candidate = new SourceLineCandidate(line);
            int jsonEncodedUtf8Bytes =
                MeasureJsonWithin(candidate);
            rows.Add(
                new PreparedRow(
                    candidate.Project(),
                    candidate.Utf16CodeUnits,
                    jsonEncodedUtf8Bytes));
            _candidateLinesScanned =
                checked(_candidateLinesScanned + 1);
            _selectedLines = checked(_selectedLines + 1);
            _projectedRows = checked(_projectedRows + 1);
        }

        _completeRows = rows.ToImmutable();
        _selectionComplete = true;
    }

    private SourceLineExecutionBatch PullComplete()
    {
        if (_terminal is QuerySpaceTerminalRequirement.Count)
            return CompletedBatch();

        if (_completeRowIndex >= _completeRows.Length)
            return CompletedBatch();

        var rows = ImmutableArray.CreateBuilder<SourceViewLine>();
        int utf16CodeUnits = 0;
        int jsonEncodedUtf8Bytes = 0;
        while (_completeRowIndex < _completeRows.Length
            && rows.Count < _bounds.MaximumCandidateRows)
        {
            PreparedRow next = _completeRows[_completeRowIndex];
            bool exceedsBatch =
                utf16CodeUnits + (long)next.Utf16CodeUnits
                    > _bounds.MaximumUtf16CodeUnits
                || jsonEncodedUtf8Bytes
                    + (long)next.JsonEncodedUtf8Bytes
                    > _bounds.MaximumJsonEncodedUtf8Bytes;
            if (rows.Count != 0 && exceedsBatch)
                break;

            rows.Add(next.Row);
            utf16CodeUnits = checked(
                utf16CodeUnits + next.Utf16CodeUnits);
            jsonEncodedUtf8Bytes = checked(
                jsonEncodedUtf8Bytes
                + next.JsonEncodedUtf8Bytes);
            _completeRowIndex++;
        }

        bool complete =
            _completeRowIndex >= _completeRows.Length;
        return new(
            rows.ToImmutable(),
            complete,
            complete ? _selectedLines : null);
    }

    private SourceLineExecutionBatch PullCold(
        CancellationToken cancellationToken)
    {
        if (_selectionComplete)
            return CompletedBatch();

        var rows = ImmutableArray.CreateBuilder<SourceViewLine>();
        int candidatesThisPull = 0;
        int utf16CodeUnits = 0;
        int jsonEncodedUtf8Bytes = 0;
        while (candidatesThisPull
                < _bounds.MaximumCandidateRows
            && rows.Count < _bounds.MaximumCandidateRows
            && !_selectionComplete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_plan.IsSatisfied(_selectedLines))
            {
                _selectionComplete = true;
                break;
            }

            SourceLineCandidate candidate;
            DecodedTextCursor next;
            if (_hasPending)
            {
                candidate = _pending;
                next = _pendingNext;
            }
            else
            {
                next = _cursor;
                if (!Document.TryReadLine(
                        ref next,
                        out DecodedTextLineSlice line))
                {
                    _selectionComplete = true;
                    break;
                }

                candidate = new SourceLineCandidate(line);
                _candidateLinesScanned =
                    checked(_candidateLinesScanned + 1);
                candidatesThisPull++;
                if (!_plan.Matches(candidate))
                {
                    _cursor = next;
                    if (next.IsComplete)
                        _selectionComplete = true;
                    continue;
                }
            }

            if (_terminal is QuerySpaceTerminalRequirement.Count)
            {
                _hasPending = false;
                _cursor = next;
                _selectedLines = checked(_selectedLines + 1);
                if (_plan.IsSatisfied(_selectedLines)
                    || next.IsComplete)
                {
                    _selectionComplete = true;
                }
                continue;
            }

            int candidateUtf16 = candidate.Utf16CodeUnits;
            int candidateJson = candidate.JsonEncodedUtf8Bytes;
            bool oversized =
                candidateUtf16
                    > _bounds.MaximumUtf16CodeUnits
                || candidateJson
                    > _bounds.MaximumJsonEncodedUtf8Bytes;
            bool exceedsBatch =
                utf16CodeUnits + (long)candidateUtf16
                    > _bounds.MaximumUtf16CodeUnits
                || jsonEncodedUtf8Bytes + (long)candidateJson
                    > _bounds.MaximumJsonEncodedUtf8Bytes;
            if (rows.Count != 0 && (oversized || exceedsBatch))
            {
                SetPending(candidate, next);
                break;
            }

            if (oversized)
            {
                SetPending(candidate, next);
                candidate.Line.EnsureWithin(_bounds.Limits);
            }

            _hasPending = false;
            _cursor = next;
            _selectedLines = checked(_selectedLines + 1);
            rows.Add(candidate.Project());
            _projectedRows = checked(_projectedRows + 1);
            utf16CodeUnits = checked(
                utf16CodeUnits + candidateUtf16);
            jsonEncodedUtf8Bytes = checked(
                jsonEncodedUtf8Bytes + candidateJson);

            if (_plan.IsSatisfied(_selectedLines)
                || next.IsComplete)
            {
                _selectionComplete = true;
            }
        }

        return new(
            rows.ToImmutable(),
            _selectionComplete,
            _selectionComplete ? _selectedLines : null);
    }

    private SourceLineExecutionBatch CompletedBatch() =>
        new(
            ImmutableArray<SourceViewLine>.Empty,
            isComplete: true,
            _selectedLines);

    private DecodedTextDocument Document =>
        _document
        ?? throw new ObjectDisposedException(
            nameof(SourceLineExecution));

    private void SetPending(
        SourceLineCandidate candidate,
        DecodedTextCursor next)
    {
        _pending = candidate;
        _pendingNext = next;
        _hasPending = true;
    }

    private int MeasureJsonWithin(
        SourceLineCandidate candidate)
    {
        int jsonEncodedUtf8Bytes =
            candidate.JsonEncodedUtf8Bytes;
        if (candidate.Utf16CodeUnits
                > _bounds.MaximumUtf16CodeUnits
            || jsonEncodedUtf8Bytes
                > _bounds.MaximumJsonEncodedUtf8Bytes)
        {
            candidate.Line.EnsureWithin(_bounds.Limits);
        }

        return jsonEncodedUtf8Bytes;
    }

    private readonly record struct PreparedCandidate(
        SourceLineCandidate Candidate,
        SourceViewLine Row);

    private readonly record struct PreparedRow(
        SourceViewLine Row,
        int Utf16CodeUnits,
        int JsonEncodedUtf8Bytes);
}
