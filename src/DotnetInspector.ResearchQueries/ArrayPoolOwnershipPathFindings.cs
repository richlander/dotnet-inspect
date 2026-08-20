using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Findings;

namespace DotnetInspector.Queries;

/// <summary>Bounds for one ownership-path composition.</summary>
public sealed record ArrayPoolOwnershipSearchOptions
{
    public int MaxWitnesses { get; init; } = 32;
    public int MaxPaths { get; init; } = 1024;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxWitnesses, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxPaths, 1);
    }
}

/// <summary>
/// Reasons an ownership-path census cannot prove that no other path exists.
/// Positive witnesses remain valid under every limit.
/// </summary>
[Flags]
public enum AnnotatedCallGraphOwnershipLimit
{
    None = 0,
    NotRequested = 1,
    TraversalBoundary = 2,
    IncompleteCorrespondence = 4,
    BodyUnavailable = 8,
    AnalysisFailure = 16,
    WitnessBudget = 32,
    PathBudget = 64,
}

/// <summary>One physical forwarding call joined to a stable graph edge row.</summary>
public readonly record struct ArrayPoolOwnershipPathStep(
    int EdgeRow,
    Guid CallerModuleVersionId,
    int CallerMethodToken,
    int ILOffset,
    int OperandToken,
    int CalleeParameterIndex);

/// <summary>
/// A bounded path from one ArrayPool rent to a proven ownership outcome.
/// </summary>
public sealed record ArrayPoolOwnershipPathWitness
{
    ImmutableArray<ArrayPoolOwnershipPathStep> _steps;

    public ArrayPoolOwnershipPathWitness(
        int RentOffset,
        ImmutableArray<ArrayPoolOwnershipPathStep> Steps,
        ArrayPoolOwnershipUseKind Outcome,
        Guid SinkModuleVersionId,
        int SinkMethodToken,
        int SinkParameterIndex,
        int SinkOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(RentOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(SinkOffset);
        this.RentOffset = RentOffset;
        if (Steps.IsDefault)
            throw new ArgumentException(
                "Path steps must be initialized.",
                nameof(Steps));
        _steps = Steps;
        this.Outcome = Outcome;
        this.SinkModuleVersionId = SinkModuleVersionId;
        this.SinkMethodToken = SinkMethodToken;
        this.SinkParameterIndex = SinkParameterIndex;
        this.SinkOffset = SinkOffset;
    }

    public int RentOffset { get; }
    public ImmutableArray<ArrayPoolOwnershipPathStep> Steps
    {
        get => _steps;
        init
        {
            if (value.IsDefault)
            {
                throw new ArgumentException(
                    "Path steps must be initialized.",
                    nameof(Steps));
            }
            _steps = value;
        }
    }
    public ImmutableArray<int> EdgeRows =>
        [.. Steps.Select(static step => step.EdgeRow)];
    public ArrayPoolOwnershipUseKind Outcome { get; }
    public Guid SinkModuleVersionId { get; }
    public int SinkMethodToken { get; }
    public int SinkParameterIndex { get; }
    public int SinkOffset { get; }

    public bool Equals(ArrayPoolOwnershipPathWitness? other) =>
        other is not null
        && RentOffset == other.RentOffset
        && Outcome == other.Outcome
        && SinkModuleVersionId == other.SinkModuleVersionId
        && SinkMethodToken == other.SinkMethodToken
        && SinkParameterIndex == other.SinkParameterIndex
        && SinkOffset == other.SinkOffset
        && Steps.AsSpan().SequenceEqual(other.Steps.AsSpan());

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RentOffset);
        hash.Add(Outcome);
        hash.Add(SinkModuleVersionId);
        hash.Add(SinkMethodToken);
        hash.Add(SinkParameterIndex);
        hash.Add(SinkOffset);
        foreach (ArrayPoolOwnershipPathStep step in Steps)
            hash.Add(step);
        return hash.ToHashCode();
    }
}

/// <summary>Ownership Findings plus independent completeness state.</summary>
public sealed record AnnotatedCallGraphOwnershipInspection(
    ImmutableArray<Finding<ArrayPoolOwnershipPathWitness>> Findings,
    AnnotatedCallGraphOwnershipLimit Limits)
{
    public bool IsComplete =>
        Limits == AnnotatedCallGraphOwnershipLimit.None;
}

/// <summary>
/// Composes retained body evidence over an already-created call-graph
/// projection. It performs no body, graph, or source acquisition.
/// </summary>
public static class ArrayPoolOwnershipPathFindings
{
    public static AnnotatedCallGraphOwnershipInspection Inspect(
        MemberCallGraphView graphView,
        CallGraphProjection projection,
        ArrayPoolOwnershipSearchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graphView);
        ArgumentNullException.ThrowIfNull(projection);
        options ??= new ArrayPoolOwnershipSearchOptions();
        options.Validate();

        if (!graphView.OwnershipFlowAvailable)
        {
            return new(
                [],
                AnnotatedCallGraphOwnershipLimit.NotRequested);
        }

        AnnotatedCallGraphOwnershipLimit limits =
            AnnotatedCallGraphOwnershipLimit.None;
        if (projection.HasUnexploredTraversalBoundary)
        {
            limits |=
                AnnotatedCallGraphOwnershipLimit.TraversalBoundary;
        }
        if (projection.HasAnalysisFailureBoundary)
        {
            limits |= AnnotatedCallGraphOwnershipLimit.AnalysisFailure;
        }
        if (graphView.Diagnostics.IsIncomplete)
        {
            limits |= AnnotatedCallGraphOwnershipLimit
                .IncompleteCorrespondence;
        }

        ArrayPoolOwnershipMethodEvidence? focus =
            graphView.OwnershipEvidence.SingleOrDefault(evidence =>
                evidence.Method.ModuleVersionId
                    == graphView.FocusModuleVersionId
                && evidence.Method.MetadataToken
                    == graphView.FocusMethodToken);
        if (focus is null)
        {
            return new([], limits);
        }
        if (!focus.IsComplete)
        {
            limits |= AnnotatedCallGraphOwnershipLimit.AnalysisFailure;
        }

        var witnesses =
            ImmutableArray.CreateBuilder<ArrayPoolOwnershipPathWitness>();
        var queue = new Queue<PathState>();
        int exploredPaths = 0;

        foreach (ArrayPoolRentOwnership rent in focus.Rents
            .OrderBy(static rent => rent.RentOffset))
        {
            if (!rent.IsComplete)
            {
                limits |=
                    AnnotatedCallGraphOwnershipLimit.AnalysisFailure;
            }

            foreach (ArrayPoolOwnershipUse use in rent.Uses)
            {
                if (!TryAdvance(
                        projection.Focus.Id,
                        rent.RentOffset,
                        sinkParameterIndex: -1,
                        [],
                        ImmutableHashSet<(int Node, int Parameter)>
                            .Empty,
                        use))
                {
                    break;
                }
            }
        }

        while (queue.Count > 0)
        {
            if (exploredPaths >= options.MaxPaths)
            {
                limits |= AnnotatedCallGraphOwnershipLimit.PathBudget;
                break;
            }
            exploredPaths++;

            PathState state = queue.Dequeue();
            if (!TryGetEvidence(
                    state.NodeId,
                    out ArrayPoolOwnershipMethodEvidence? evidence))
            {
                limits |=
                    AnnotatedCallGraphOwnershipLimit.BodyUnavailable;
                continue;
            }
            if (!evidence!.IsComplete)
            {
                limits |=
                    AnnotatedCallGraphOwnershipLimit.AnalysisFailure;
            }

            ArrayPoolParameterOwnership? parameter =
                evidence.Parameters.SingleOrDefault(candidate =>
                    candidate.ParameterIndex == state.ParameterIndex);
            if (parameter is null)
            {
                limits |=
                    AnnotatedCallGraphOwnershipLimit.BodyUnavailable;
                continue;
            }
            if (!parameter.IsComplete)
            {
                limits |=
                    AnnotatedCallGraphOwnershipLimit.AnalysisFailure;
            }

            foreach (ArrayPoolOwnershipUse use in parameter.Uses)
            {
                if (!TryAdvance(
                        state.NodeId,
                        state.RentOffset,
                        state.ParameterIndex,
                        state.Steps,
                        state.Visited,
                        use))
                {
                    break;
                }
            }
        }

        var subject = new FindingSubject(
            $"{graphView.FocusModuleVersionId:N}|"
                + $"{graphView.FocusMethodToken:X8}",
            projection.Focus.Label);
        ImmutableArray<Finding<ArrayPoolOwnershipPathWitness>> findings =
        [
            .. witnesses.Select(
                (witness, ordinal) =>
                    new Finding<ArrayPoolOwnershipPathWitness>(
                        subject,
                        AnalysisFindings.ResourceLifecycleDescriptor,
                        WitnessKey(witness),
                        witness,
                        Ordinal: ordinal)),
        ];
        return new(findings, limits);

        bool TryAdvance(
            int callerNodeId,
            int rentOffset,
            int sinkParameterIndex,
            ImmutableArray<ArrayPoolOwnershipPathStep> steps,
            ImmutableHashSet<(int Node, int Parameter)> visited,
            ArrayPoolOwnershipUse use)
        {
            if (!use.IsForwarded)
            {
                if (witnesses.Count >= options.MaxWitnesses)
                {
                    limits |=
                        AnnotatedCallGraphOwnershipLimit.WitnessBudget;
                    return false;
                }

                ArrayPoolOwnershipMethodEvidence sink =
                    callerNodeId == projection.Focus.Id
                        ? focus
                        : EvidenceForNode(callerNodeId)!;
                witnesses.Add(
                    new ArrayPoolOwnershipPathWitness(
                        rentOffset,
                        steps,
                        use.Kind,
                        sink.Method.ModuleVersionId,
                        sink.Method.MetadataToken,
                        sinkParameterIndex,
                        use.ILOffset));
                return true;
            }

            if (use.Call is not { } call)
            {
                limits |=
                    AnnotatedCallGraphOwnershipLimit.AnalysisFailure;
                return true;
            }

            CallGraphRowMatch match = projection.FindCalleeRow(
                callerNodeId,
                call,
                out CallGraphRow row);
            if (match != CallGraphRowMatch.Found)
            {
                limits |= match == CallGraphRowMatch.Ambiguous
                    ? AnnotatedCallGraphOwnershipLimit
                        .IncompleteCorrespondence
                    : AnnotatedCallGraphOwnershipLimit
                        .TraversalBoundary;
                return true;
            }

            var step = new ArrayPoolOwnershipPathStep(
                row.Number,
                call.EvidenceMethod.ModuleVersionId,
                call.EvidenceMethod.MetadataToken,
                call.ILOffset,
                call.OperandToken,
                use.CalleeParameterIndex);
            var nextSteps = steps.Add(step);
            var stateKey =
                (row.Edge.To, use.CalleeParameterIndex);
            if (visited.Contains(stateKey))
                return true;

            queue.Enqueue(
                new PathState(
                    row.Edge.To,
                    use.CalleeParameterIndex,
                    rentOffset,
                    nextSteps,
                    visited.Add(stateKey)));
            return true;
        }

        ArrayPoolOwnershipMethodEvidence? EvidenceForNode(
            int nodeId)
        {
            CallGraphNode node = projection.Nodes[nodeId];
            ArrayPoolOwnershipMethodEvidence[] exact =
            [
                .. node.GraphEvidence
                    .Where(evidence =>
                        evidence.Storage.Kind
                            == GraphNodeStorageKind.Definition)
                    .SelectMany(graphEvidence =>
                        graphView.OwnershipEvidence.Where(evidence =>
                            evidence.Method.ModuleVersionId
                                == graphEvidence.Storage
                                    .ModuleVersionId
                            && evidence.Method.MetadataToken
                                == graphEvidence.Storage.MethodToken))
                    .Distinct(),
            ];
            if (exact.Length == 1)
                return exact[0];
            if (exact.Length > 1)
            {
                limits |=
                    AnnotatedCallGraphOwnershipLimit
                        .IncompleteCorrespondence;
                return null;
            }

            // Catalog projections retain physical evidence for every node.
            // A node with no matching definition is an external or unresolved
            // occurrence, not permission to borrow a same-shaped body from
            // another image.
            if (!node.GraphEvidence.IsDefaultOrEmpty)
                return null;

            ArrayPoolOwnershipMethodEvidence[] structural =
            [
                .. graphView.OwnershipEvidence.Where(evidence =>
                    GraphNodeIdentity.FromMember(
                        evidence.Member)
                    == GraphNodeIdentity.FromMember(
                        node.Member)),
            ];
            if (structural.Length == 1)
                return structural[0];
            if (structural.Length > 1)
            {
                limits |=
                    AnnotatedCallGraphOwnershipLimit
                        .IncompleteCorrespondence;
            }
            return null;
        }

        bool TryGetEvidence(
            int nodeId,
            out ArrayPoolOwnershipMethodEvidence? evidence)
        {
            evidence = EvidenceForNode(nodeId);
            return evidence is not null;
        }
    }

    static FindingKey WitnessKey(
        ArrayPoolOwnershipPathWitness witness)
    {
        string path = string.Join(
            ">",
            witness.Steps.Select(step =>
                $"{step.CallerModuleVersionId:N}:"
                + $"{step.CallerMethodToken:X8}:"
                + $"{step.ILOffset:X8}:"
                + $"{step.OperandToken:X8}:"
                + $"{step.CalleeParameterIndex}"));
        return new FindingKey(
            $"array-pool:{witness.RentOffset:X8}:"
            + $"{path}:{(int)witness.Outcome}:"
            + $"{witness.SinkModuleVersionId:N}:"
            + $"{witness.SinkMethodToken:X8}:"
            + $"{witness.SinkParameterIndex}:"
            + $"{witness.SinkOffset:X8}");
    }

    sealed record PathState(
        int NodeId,
        int ParameterIndex,
        int RentOffset,
        ImmutableArray<ArrayPoolOwnershipPathStep> Steps,
        ImmutableHashSet<(int Node, int Parameter)> Visited);
}
