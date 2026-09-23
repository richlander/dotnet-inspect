using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.CallGraph;
using Inspector.Findings;

namespace DotnetInspector.Queries;

/// <summary>Bounds and resource selection for one ownership composition.</summary>
public sealed record ResourceOwnershipSearchOptions
{
    public int MaxWitnesses { get; init; } = 32;
    public int MaxPaths { get; init; } = 1024;
    public ResourceKindIdentity? ResourceKind { get; init; }

    public static ResourceOwnershipSearchOptions ArrayPool { get; } =
        new()
        {
            ResourceKind = ArrayPoolResourceEffectModel.BufferKind,
        };

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxWitnesses, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxPaths, 1);
    }
}

/// <summary>One physical forwarding call joined to a stable graph edge row.</summary>
public readonly record struct ResourceOwnershipPathStep(
    int EdgeRow,
    Guid CallerModuleVersionId,
    int CallerMethodToken,
    int ILOffset,
    int OperandToken,
    int CalleeParameterIndex);

/// <summary>A terminal outcome for one carried ownership obligation.</summary>
public enum ResourceOwnershipPathOutcome
{
    Released,
    Stored,
    ReturnedToCaller,
}

/// <summary>
/// A bounded path from one typed resource acquisition to a proven ownership
/// outcome.
/// </summary>
public sealed record ResourceOwnershipPathWitness
{
    readonly ImmutableArray<ResourceOwnershipPathStep> _steps;

    public ResourceOwnershipPathWitness(
        ResourceOccurrenceRoot.Acquisition Obligation,
        ResourceOccurrenceResourceKind ResourceKind,
        ImmutableArray<ResourceOwnershipPathStep> Steps,
        ResourceOwnershipPathOutcome Outcome,
        Guid SinkModuleVersionId,
        int SinkMethodToken,
        int SinkParameterIndex,
        int SinkOffset,
        bool IsComplete)
    {
        this.Obligation = Obligation
            ?? throw new ArgumentNullException(nameof(Obligation));
        this.ResourceKind = ResourceKind
            ?? throw new ArgumentNullException(nameof(ResourceKind));
        if (Steps.IsDefault)
        {
            throw new ArgumentException(
                "Path steps must be initialized.",
                nameof(Steps));
        }
        _steps = Steps;
        this.Outcome = Outcome;
        this.SinkModuleVersionId = SinkModuleVersionId;
        this.SinkMethodToken = SinkMethodToken;
        this.SinkParameterIndex = SinkParameterIndex;
        ArgumentOutOfRangeException.ThrowIfNegative(SinkOffset);
        this.SinkOffset = SinkOffset;
        this.IsComplete = IsComplete;
    }

    public ResourceOccurrenceRoot.Acquisition Obligation { get; }
    public ResourceOccurrenceResourceKind ResourceKind { get; }
    public ImmutableArray<ResourceOwnershipPathStep> Steps
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
    public ResourceOwnershipPathOutcome Outcome { get; }
    public Guid SinkModuleVersionId { get; }
    public int SinkMethodToken { get; }
    public int SinkParameterIndex { get; }
    public int SinkOffset { get; }
    public bool IsComplete { get; }

    public bool Equals(ResourceOwnershipPathWitness? other) =>
        other is not null
        && Equals(Obligation, other.Obligation)
        && Equals(ResourceKind, other.ResourceKind)
        && Outcome == other.Outcome
        && SinkModuleVersionId == other.SinkModuleVersionId
        && SinkMethodToken == other.SinkMethodToken
        && SinkParameterIndex == other.SinkParameterIndex
        && SinkOffset == other.SinkOffset
        && IsComplete == other.IsComplete
        && Steps.AsSpan().SequenceEqual(other.Steps.AsSpan());

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Obligation);
        hash.Add(ResourceKind);
        hash.Add(Outcome);
        hash.Add(SinkModuleVersionId);
        hash.Add(SinkMethodToken);
        hash.Add(SinkParameterIndex);
        hash.Add(SinkOffset);
        hash.Add(IsComplete);
        foreach (ResourceOwnershipPathStep step in Steps)
            hash.Add(step);
        return hash.ToHashCode();
    }
}

/// <summary>Generic ownership Findings plus independent completeness state.</summary>
public sealed record ResourceOwnershipPathInspection(
    ImmutableArray<Finding<ResourceOwnershipPathWitness>> Findings,
    AnnotatedCallGraphOwnershipLimit Limits)
{
    public bool IsComplete =>
        Limits == AnnotatedCallGraphOwnershipLimit.None;
}

/// <summary>
/// Composes retained generic ownership summaries over an already-created
/// call-graph projection. It performs no body, graph, effect, or source
/// acquisition.
/// </summary>
public static class ResourceOwnershipPathFindings
{
    public static ResourceOwnershipPathInspection Inspect(
        MemberCallGraphView graphView,
        CallGraphProjection projection,
        ResourceOwnershipSearchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graphView);
        ArgumentNullException.ThrowIfNull(projection);
        options ??= new ResourceOwnershipSearchOptions();
        options.Validate();

        if (!graphView.ResourceOwnershipAvailable)
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
        if (projection.HasAnalysisFailureBoundary
            || !graphView.ResourceOwnershipPublicationComplete)
        {
            limits |= AnnotatedCallGraphOwnershipLimit.AnalysisFailure;
        }
        if (graphView.Diagnostics.IsIncomplete)
        {
            limits |= AnnotatedCallGraphOwnershipLimit
                .IncompleteCorrespondence;
        }

        ResourceOwnershipMethodSummary? focus =
            graphView.ResourceOwnershipSummaries.SingleOrDefault(summary =>
                summary.Method.ModuleVersionId
                    == graphView.FocusModuleVersionId
                && summary.Method.MetadataToken
                    == graphView.FocusMethodToken);
        if (focus is null)
        {
            return new(
                [],
                limits | AnnotatedCallGraphOwnershipLimit.BodyUnavailable);
        }
        if (!focus.IsComplete)
        {
            limits |= AnnotatedCallGraphOwnershipLimit.AnalysisFailure;
        }

        var witnesses =
            ImmutableArray.CreateBuilder<ResourceOwnershipPathWitness>();
        var queue = new Queue<PathState>();
        int exploredPaths = 0;

        foreach (ResourceOwnershipAcquisitionFlow acquisition
            in focus.Acquisitions.OrderBy(
                static flow => flow.Obligation.Call.ILOffset))
        {
            if (!acquisition.IsComplete)
            {
                limits |=
                    AnnotatedCallGraphOwnershipLimit.AnalysisFailure;
            }

            foreach (ResourceOccurrenceResourceKind resourceKind
                in acquisition.Obligation.ResourceKinds.Where(kind =>
                    options.ResourceKind is null
                    || kind.Identity == options.ResourceKind))
            {
                foreach (ResourceOwnershipUse use in ApplicableUses(
                    acquisition.Uses,
                    acquisition.Obligation.ResourceKinds))
                {
                    if (!TryAdvance(
                            projection.Focus.Id,
                            acquisition.Obligation,
                            resourceKind,
                            sinkParameterIndex: -1,
                            [],
                            acquisition.IsComplete && focus.IsComplete,
                            ImmutableHashSet<(int Node, int Parameter)>
                                .Empty,
                            use))
                    {
                        break;
                    }
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
                    out ResourceOwnershipMethodSummary? evidence))
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

            ResourceOwnershipParameterFlow? parameter =
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

            foreach (ResourceOwnershipUse use in ApplicableUses(
                parameter.Uses,
                state.Obligation.ResourceKinds))
            {
                if (!TryAdvance(
                        state.NodeId,
                        state.Obligation,
                        state.ResourceKind,
                        state.ParameterIndex,
                        state.Steps,
                        state.IsComplete
                            && evidence.IsComplete
                            && parameter.IsComplete,
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
        ImmutableArray<Finding<ResourceOwnershipPathWitness>> findings =
        [
            .. witnesses.Select(
                (witness, ordinal) =>
                    new Finding<ResourceOwnershipPathWitness>(
                        subject,
                        AnalysisFindings.ResourceLifecycleDescriptor,
                        WitnessKey(witness),
                        witness,
                        Ordinal: ordinal)),
        ];
        return new(findings, limits);

        bool TryAdvance(
            int callerNodeId,
            ResourceOccurrenceRoot.Acquisition obligation,
            ResourceOccurrenceResourceKind resourceKind,
            int sinkParameterIndex,
            ImmutableArray<ResourceOwnershipPathStep> steps,
            bool pathIsComplete,
            ImmutableHashSet<(int Node, int Parameter)> visited,
            ResourceOwnershipUse use)
        {
            if (!use.IsForwarded)
            {
                if (witnesses.Count >= options.MaxWitnesses)
                {
                    limits |=
                        AnnotatedCallGraphOwnershipLimit.WitnessBudget;
                    return false;
                }

                ResourceOwnershipMethodSummary sink =
                    callerNodeId == projection.Focus.Id
                        ? focus
                        : EvidenceForNode(callerNodeId)!;
                witnesses.Add(
                    new ResourceOwnershipPathWitness(
                        obligation,
                        resourceKind,
                        steps,
                        Outcome(use.Kind),
                        sink.Method.ModuleVersionId,
                        sink.Method.MetadataToken,
                        sinkParameterIndex,
                        use.ILOffset,
                        pathIsComplete));
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

            var step = new ResourceOwnershipPathStep(
                row.Number,
                call.EvidenceMethod.ModuleVersionId,
                call.EvidenceMethod.MetadataToken,
                call.ILOffset,
                call.OperandToken,
                use.CalleeParameterIndex);
            var stateKey =
                (row.Edge.To, use.CalleeParameterIndex);
            if (visited.Contains(stateKey))
                return true;

            queue.Enqueue(
                new PathState(
                    row.Edge.To,
                    use.CalleeParameterIndex,
                    obligation,
                    resourceKind,
                    steps.Add(step),
                    pathIsComplete,
                    visited.Add(stateKey)));
            return true;
        }

        ResourceOwnershipMethodSummary? EvidenceForNode(
            int nodeId)
        {
            CallGraphNode node = projection.Nodes[nodeId];
            ResourceOwnershipMethodSummary[] exact =
            [
                .. node.GraphEvidence
                    .Where(evidence =>
                        evidence.Storage.Kind
                            == GraphNodeStorageKind.Definition)
                    .SelectMany(graphEvidence =>
                        graphView.ResourceOwnershipSummaries.Where(summary =>
                            summary.Method.ModuleVersionId
                                == graphEvidence.Storage.ModuleVersionId
                            && summary.Method.MetadataToken
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

            if (!node.GraphEvidence.IsDefaultOrEmpty)
                return null;

            ResourceOwnershipMethodSummary[] structural =
            [
                .. graphView.ResourceOwnershipSummaries.Where(summary =>
                    GraphNodeIdentity.FromMember(summary.Member)
                    == GraphNodeIdentity.FromMember(node.Member)),
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
            out ResourceOwnershipMethodSummary? evidence)
        {
            evidence = EvidenceForNode(nodeId);
            return evidence is not null;
        }
    }

    static IEnumerable<ResourceOwnershipUse> ApplicableUses(
        ImmutableArray<ResourceOwnershipUse> uses,
        ImmutableArray<ResourceOccurrenceResourceKind> obligationDomain)
    {
        foreach (IGrouping<(int Offset, int Parameter),
            ResourceOwnershipUse> group in uses.GroupBy(use =>
                (use.ILOffset, use.CalleeParameterIndex)))
        {
            ResourceOwnershipUse[] constrained =
            [
                .. group.Where(use =>
                    !use.ResourceKinds.IsEmpty
                    && DomainsMatch(
                        use.ResourceKinds,
                        obligationDomain)),
            ];
            foreach (ResourceOwnershipUse use in constrained.Length > 0
                ? constrained
                : group.Where(static use =>
                    use.ResourceKinds.IsEmpty))
            {
                yield return use;
            }
        }
    }

    static bool DomainsMatch(
        ImmutableArray<ResourceOccurrenceResourceKind> left,
        ImmutableArray<ResourceOccurrenceResourceKind> right) =>
        left.Length == right.Length
        && left.Zip(right).All(pair =>
            ResourceKindsMatch(pair.First, pair.Second));

    static bool ResourceKindsMatch(
        ResourceOccurrenceResourceKind left,
        ResourceOccurrenceResourceKind right) =>
        left.Identity == right.Identity
        && left.Arguments.Length == right.Arguments.Length
        && left.Arguments.Zip(right.Arguments).All(pair =>
            ResourceTypesMatch(pair.First, pair.Second));

    static bool ResourceTypesMatch(
        ResourceOccurrenceType left,
        ResourceOccurrenceType right) =>
        left.Type.Equals(right.Type)
        && left.DefiningAssembly == right.DefiningAssembly
        && left.DefinitionKind == right.DefinitionKind
        && left.GenericScopeKind == right.GenericScopeKind
        && ((left.Element is null && right.Element is null)
            || (left.Element is not null
                && right.Element is not null
                && ResourceTypesMatch(left.Element, right.Element)))
        && left.Arguments.Length == right.Arguments.Length
        && left.Arguments.Zip(right.Arguments).All(pair =>
            ResourceTypesMatch(pair.First, pair.Second))
        && left.Forwarding.Length == right.Forwarding.Length
        && left.Forwarding.Zip(right.Forwarding).All(pair =>
            ForwardingHopsMatch(pair.First, pair.Second));

    static bool ForwardingHopsMatch(
        ResourceOccurrenceTypeForwardingHop left,
        ResourceOccurrenceTypeForwardingHop right) =>
        left.SourceAssembly == right.SourceAssembly
        && left.TargetReference == right.TargetReference
        && left.Declarations.AsSpan().SequenceEqual(
            right.Declarations.AsSpan());

    static ResourceOwnershipPathOutcome Outcome(
        ResourceOwnershipUseKind kind) =>
        kind switch
        {
            ResourceOwnershipUseKind.Released =>
                ResourceOwnershipPathOutcome.Released,
            ResourceOwnershipUseKind.Stored =>
                ResourceOwnershipPathOutcome.Stored,
            ResourceOwnershipUseKind.ReturnedToCaller =>
                ResourceOwnershipPathOutcome.ReturnedToCaller,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static FindingKey WitnessKey(
        ResourceOwnershipPathWitness witness)
    {
        string path = string.Join(
            ">",
            witness.Steps.Select(step =>
                $"{step.CallerModuleVersionId:N}:"
                + $"{step.CallerMethodToken:X8}:"
                + $"{step.ILOffset:X8}:"
                + $"{step.OperandToken:X8}:"
                + $"{step.CalleeParameterIndex}"));
        ResourceOccurrenceCallSite acquisition =
            witness.Obligation.Call;
        return new FindingKey(
            $"resource:{witness.ResourceKind.Identity.Value}:"
            + $"{acquisition.Method.ModuleVersionId:N}:"
            + $"{acquisition.Method.MetadataToken:X8}:"
            + $"{acquisition.ILOffset:X8}:"
            + $"{path}:{(int)witness.Outcome}:"
            + $"{witness.SinkModuleVersionId:N}:"
            + $"{witness.SinkMethodToken:X8}:"
            + $"{witness.SinkParameterIndex}:"
            + $"{witness.SinkOffset:X8}");
    }

    sealed record PathState(
        int NodeId,
        int ParameterIndex,
        ResourceOccurrenceRoot.Acquisition Obligation,
        ResourceOccurrenceResourceKind ResourceKind,
        ImmutableArray<ResourceOwnershipPathStep> Steps,
        bool IsComplete,
        ImmutableHashSet<(int Node, int Parameter)> Visited);
}
