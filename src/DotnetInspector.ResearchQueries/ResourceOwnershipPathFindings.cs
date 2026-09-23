using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Metadata;
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
                foreach (ApplicableOwnershipUse applicable in ApplicableUses(
                    acquisition.Uses,
                    acquisition.Obligation.ResourceKinds))
                {
                    if (!applicable.IsComplete)
                    {
                        limits |=
                            AnnotatedCallGraphOwnershipLimit.AnalysisFailure;
                    }
                    if (!TryAdvance(
                            projection.Focus.Id,
                            acquisition.Obligation,
                            resourceKind,
                            sinkParameterIndex: -1,
                            [],
                            acquisition.IsComplete
                                && focus.IsComplete
                                && applicable.IsComplete,
                            ImmutableHashSet<(
                                int Node,
                                int Parameter,
                                ResourceOwnershipGenericContext
                                    GenericContext)>
                                .Empty,
                            ResourceOwnershipGenericContext.Empty,
                            applicable.Use))
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

            foreach (ApplicableOwnershipUse applicable in ApplicableUses(
                parameter.Uses,
                state.Obligation.ResourceKinds,
                state.GenericContext))
            {
                if (!applicable.IsComplete)
                {
                    limits |=
                        AnnotatedCallGraphOwnershipLimit.AnalysisFailure;
                }
                if (!TryAdvance(
                        state.NodeId,
                        state.Obligation,
                        state.ResourceKind,
                        state.ParameterIndex,
                        state.Steps,
                        state.IsComplete
                            && evidence.IsComplete
                            && parameter.IsComplete
                            && applicable.IsComplete,
                        state.Visited,
                        state.GenericContext,
                        applicable.Use))
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
            ImmutableHashSet<(
                int Node,
                int Parameter,
                ResourceOwnershipGenericContext GenericContext)> visited,
            ResourceOwnershipGenericContext genericContext,
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
            ResourceOwnershipGenericContext nextGenericContext =
                genericContext.ForCall(use);
            var stateKey =
                (
                    row.Edge.To,
                    use.CalleeParameterIndex,
                    nextGenericContext);
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
                    visited.Add(stateKey),
                    nextGenericContext));
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

    static IEnumerable<ApplicableOwnershipUse> ApplicableUses(
        ImmutableArray<ResourceOwnershipUse> uses,
        ImmutableArray<ResourceOccurrenceResourceKind> obligationDomain,
        ResourceOwnershipGenericContext genericContext =
            default)
    {
        foreach (IGrouping<(int Offset, int Parameter),
            ResourceOwnershipUse> group in uses.GroupBy(use =>
                (use.ILOffset, use.CalleeParameterIndex)))
        {
            (ResourceOwnershipUse Use, ResourceDomainMatch Match)[]
                constrained =
            [
                .. group.Where(use =>
                    !use.ResourceKinds.IsEmpty)
                    .Select(use =>
                        (
                            use,
                            DomainsMatch(
                                use.ResourceKinds,
                                obligationDomain,
                                genericContext))),
            ];
            ResourceOwnershipUse[] matched =
            [
                .. constrained
                    .Where(static candidate =>
                        candidate.Match == ResourceDomainMatch.Match)
                    .Select(static candidate => candidate.Use),
            ];
            if (matched.Length > 0)
            {
                foreach (ResourceOwnershipUse use in matched)
                    yield return new(use, IsComplete: true);
                continue;
            }

            bool complete = !constrained.Any(static candidate =>
                candidate.Match == ResourceDomainMatch.Incomplete);
            foreach (ResourceOwnershipUse use in group.Where(static use =>
                use.ResourceKinds.IsEmpty))
            {
                yield return new(use, complete);
            }
        }
    }

    static ResourceDomainMatch DomainsMatch(
        ImmutableArray<ResourceOccurrenceResourceKind> left,
        ImmutableArray<ResourceOccurrenceResourceKind> right,
        ResourceOwnershipGenericContext genericContext)
    {
        if (left.Length != right.Length)
            return ResourceDomainMatch.NoMatch;
        return Combine(
            left.Zip(right).Select(pair =>
            ResourceKindsMatch(
                pair.First,
                pair.Second,
                genericContext)));
    }

    static ResourceDomainMatch ResourceKindsMatch(
        ResourceOccurrenceResourceKind left,
        ResourceOccurrenceResourceKind right,
        ResourceOwnershipGenericContext genericContext)
    {
        if (left.Identity != right.Identity
            || left.Arguments.Length != right.Arguments.Length)
        {
            return ResourceDomainMatch.NoMatch;
        }
        return Combine(
            left.Arguments.Zip(right.Arguments).Select(pair =>
            ResourceTypesMatch(
                pair.First,
                pair.Second,
                genericContext)));
    }

    static ResourceDomainMatch ResourceTypesMatch(
        ResourceOccurrenceType left,
        ResourceOccurrenceType right,
        ResourceOwnershipGenericContext genericContext)
    {
        if (left.Type.Kind is TypeRefKind.GenericParameter
            or TypeRefKind.MethodGenericParameter)
        {
            ResourceEffectGenericVariableKind kind =
                left.Type.Kind == TypeRefKind.GenericParameter
                    ? ResourceEffectGenericVariableKind.Type
                    : ResourceEffectGenericVariableKind.Method;
            return genericContext.TryGetArgument(
                    kind,
                    left.Type.GenericParameterIndex,
                    out ResourceOwnershipGenericContextArgument argument)
                ? argument.ExactType is { } exact
                    ? ResourceTypesMatch(
                        exact,
                        right,
                        ResourceOwnershipGenericContext.Empty)
                    : ResourceDomainMatch.Incomplete
                : ResourceDomainMatch.Incomplete;
        }

        if (!TypeShapesMatch(left.Type, right.Type)
            || left.DefiningAssembly != right.DefiningAssembly
            || left.DefinitionKind != right.DefinitionKind
            || left.GenericScopeKind != right.GenericScopeKind
            || left.Forwarding.Length != right.Forwarding.Length
            || !left.Forwarding.Zip(right.Forwarding).All(pair =>
                ForwardingHopsMatch(pair.First, pair.Second))
            || left.Arguments.Length != right.Arguments.Length
            || (left.Element is null) != (right.Element is null))
        {
            return ResourceDomainMatch.NoMatch;
        }

        var matches =
            new List<ResourceDomainMatch>(
                left.Arguments.Length + (left.Element is null ? 0 : 1));
        if (left.Element is not null)
        {
            matches.Add(
                ResourceTypesMatch(
                    left.Element,
                    right.Element!,
                    genericContext));
        }
        matches.AddRange(
            left.Arguments.Zip(right.Arguments).Select(pair =>
                ResourceTypesMatch(
                    pair.First,
                    pair.Second,
                    genericContext)));
        return Combine(matches);
    }

    static bool TypeShapesMatch(TypeRef left, TypeRef right)
    {
        if (left.Kind != right.Kind
            || left.Rank != right.Rank
            || left.GenericParameterIndex
                != right.GenericParameterIndex
            || left.Namespace != right.Namespace
            || left.Name != right.Name
            || left.Assembly != right.Assembly
            || (left.ElementType is null)
                != (right.ElementType is null)
            || left.TypeArguments.Length
                != right.TypeArguments.Length)
        {
            return false;
        }
        if (left.Resolution?.Type is { } leftName
            && right.Resolution?.Type is { } rightName
            && !leftName.Equals(rightName))
        {
            return false;
        }
        if (left.ElementType is not null
            && !TypeShapesMatch(
                left.ElementType,
                right.ElementType!))
        {
            return false;
        }
        return left.TypeArguments.Zip(right.TypeArguments).All(pair =>
            TypeShapesMatch(pair.First, pair.Second));
    }

    static ResourceDomainMatch Combine(
        IEnumerable<ResourceDomainMatch> matches)
    {
        bool incomplete = false;
        foreach (ResourceDomainMatch match in matches)
        {
            if (match == ResourceDomainMatch.NoMatch)
                return ResourceDomainMatch.NoMatch;
            incomplete |= match == ResourceDomainMatch.Incomplete;
        }
        return incomplete
            ? ResourceDomainMatch.Incomplete
            : ResourceDomainMatch.Match;
    }

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
            $"resource:{ResourceKindKey(witness.ResourceKind)}:"
            + $"{acquisition.Method.ModuleVersionId:N}:"
            + $"{acquisition.Method.MetadataToken:X8}:"
            + $"{acquisition.ILOffset:X8}:"
            + $"{path}:{(int)witness.Outcome}:"
            + $"{witness.SinkModuleVersionId:N}:"
            + $"{witness.SinkMethodToken:X8}:"
            + $"{witness.SinkParameterIndex}:"
            + $"{witness.SinkOffset:X8}");
    }

    static string ResourceKindKey(
        ResourceOccurrenceResourceKind kind)
    {
        var builder = new StringBuilder();
        AppendPart(builder, kind.Identity.Value);
        AppendInt(builder, kind.Arguments.Length);
        foreach (ResourceOccurrenceType argument in kind.Arguments)
            AppendType(builder, argument);
        return builder.ToString();
    }

    static void AppendType(
        StringBuilder builder,
        ResourceOccurrenceType type)
    {
        AppendInt(builder, (int)type.Type.Kind);
        AppendInt(builder, type.Type.Rank);
        AppendInt(builder, type.Type.GenericParameterIndex);
        AppendTypeName(builder, type.Type);
        AppendAssembly(builder, type.DefiningAssembly);
        AppendInt(builder, (int?)type.DefinitionKind);
        AppendInt(builder, (int?)type.GenericScopeKind);
        AppendInt(builder, type.Element is null ? 0 : 1);
        if (type.Element is not null)
            AppendType(builder, type.Element);
        AppendInt(builder, type.Arguments.Length);
        foreach (ResourceOccurrenceType argument in type.Arguments)
            AppendType(builder, argument);
        AppendInt(builder, type.Forwarding.Length);
        foreach (ResourceOccurrenceTypeForwardingHop hop
            in type.Forwarding)
        {
            AppendAssembly(builder, hop.SourceAssembly);
            AppendAssembly(builder, hop.TargetReference);
            AppendInt(builder, hop.Declarations.Length);
            foreach (var declaration in hop.Declarations)
                AppendInt(builder, declaration.Value);
        }
    }

    static void AppendTypeName(
        StringBuilder builder,
        TypeRef type)
    {
        TypeRef named = type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType!
            : type;
        if (named.Kind == TypeRefKind.Definition
            && named.Resolution?.Type is { } name)
        {
            AppendInt(builder, 1);
            AppendPart(builder, name.ToEscapedFullName());
            return;
        }

        AppendInt(builder, 0);
        AppendPart(builder, named.Assembly);
        AppendPart(builder, named.Namespace);
        AppendPart(builder, named.Name);
    }

    static void AppendTypeRefIdentity(
        StringBuilder builder,
        TypeRef type)
    {
        AppendInt(builder, (int)type.Kind);
        AppendInt(builder, type.Rank);
        AppendInt(builder, type.GenericParameterIndex);
        AppendTypeName(builder, type);
        AppendTypeOrigin(builder, type.Resolution?.Origin);
        AppendInt(builder, type.ElementType is null ? 0 : 1);
        if (type.ElementType is not null)
            AppendTypeRefIdentity(builder, type.ElementType);
        AppendInt(builder, type.TypeArguments.Length);
        foreach (TypeRef argument in type.TypeArguments)
            AppendTypeRefIdentity(builder, argument);
    }

    static void AppendTypeOrigin(
        StringBuilder builder,
        TypeReferenceOrigin? origin)
    {
        switch (origin)
        {
            case TypeReferenceOrigin.AssemblyReference reference:
                AppendInt(builder, 1);
                AppendAssembly(builder, reference.Assembly);
                break;
            case TypeReferenceOrigin.CurrentAssembly current:
                AppendInt(builder, 2);
                AppendAssembly(builder, current.Assembly);
                break;
            case TypeReferenceOrigin.IntrinsicCoreLibrary:
                AppendInt(builder, 3);
                break;
            case TypeReferenceOrigin.ModuleReference module:
                AppendInt(builder, 4);
                AppendPart(builder, module.ModuleName);
                break;
            default:
                AppendInt(builder, 0);
                break;
        }
    }

    static void AppendAssembly(
        StringBuilder builder,
        AssemblyReferenceIdentity? assembly)
    {
        AppendInt(builder, assembly is null ? 0 : 1);
        if (assembly is null)
            return;
        AppendPart(builder, assembly.Name);
        AppendPart(builder, assembly.Version?.ToString());
        AppendPart(builder, assembly.Culture);
        AppendPart(builder, assembly.PublicKeyToken);
    }

    static void AppendInt(StringBuilder builder, int? value) =>
        AppendPart(
            builder,
            value?.ToString(CultureInfo.InvariantCulture));

    static void AppendPart(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append("-1:");
            return;
        }
        builder.Append(
            value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
    }

    readonly struct ResourceOwnershipGenericContext
        : IEquatable<ResourceOwnershipGenericContext>
    {
        internal ResourceOwnershipGenericContext(
            ImmutableArray<ResourceOwnershipGenericContextArgument>
                typeArguments,
            ImmutableArray<ResourceOwnershipGenericContextArgument>
                methodArguments) =>
            (TypeArguments, MethodArguments) =
                (typeArguments, methodArguments);

        internal static ResourceOwnershipGenericContext Empty { get; } =
            new([], []);

        internal ImmutableArray<ResourceOwnershipGenericContextArgument>
            TypeArguments { get; }
        internal ImmutableArray<ResourceOwnershipGenericContextArgument>
            MethodArguments { get; }

        internal bool TryGetArgument(
            ResourceEffectGenericVariableKind kind,
            int index,
            out ResourceOwnershipGenericContextArgument argument)
        {
            ImmutableArray<ResourceOwnershipGenericContextArgument>
                arguments =
                kind == ResourceEffectGenericVariableKind.Type
                    ? TypeArguments
                    : MethodArguments;
            if (!arguments.IsDefault
                && index >= 0
                && index < arguments.Length)
            {
                argument = arguments[index];
                return true;
            }
            argument = default;
            return false;
        }

        internal ResourceOwnershipGenericContext ForCall(
            ResourceOwnershipUse use)
        {
            return new(
                InstantiateArguments(use.DeclaringTypeArguments),
                InstantiateArguments(use.MethodArguments));
        }

        ImmutableArray<ResourceOwnershipGenericContextArgument>
            InstantiateArguments(
            ImmutableArray<ILInspector.Analysis
                .ResourceOwnershipGenericArgument> arguments) =>
            arguments.IsDefaultOrEmpty
                ? []
                :
                [
                    .. arguments.Select(InstantiateArgument),
                ];

        ResourceOwnershipGenericContextArgument InstantiateArgument(
            ILInspector.Analysis.ResourceOwnershipGenericArgument
                argument)
        {
            if (argument.Type.Kind is TypeRefKind.GenericParameter
                    or TypeRefKind.MethodGenericParameter
                && TryGetArgument(
                    argument.Type.Kind == TypeRefKind.GenericParameter
                        ? ResourceEffectGenericVariableKind.Type
                        : ResourceEffectGenericVariableKind.Method,
                    argument.Type.GenericParameterIndex,
                    out ResourceOwnershipGenericContextArgument existing))
            {
                return existing;
            }

            TypeRef instantiated = argument.Type.Instantiate(
                Types(TypeArguments),
                Types(MethodArguments));
            return ResourceOwnershipGenericContextArgument.Create(
                instantiated,
                argument.ExactType);
        }

        static ImmutableArray<TypeRef> Types(
            ImmutableArray<ResourceOwnershipGenericContextArgument>
                arguments) =>
            arguments.IsDefaultOrEmpty
                ? []
                :
                [
                    .. arguments.Select(static argument =>
                        argument.Type),
                ];

        public bool Equals(ResourceOwnershipGenericContext other) =>
            (TypeArguments.IsDefault ? [] : TypeArguments)
                .AsSpan()
                .SequenceEqual(
                    (other.TypeArguments.IsDefault
                        ? []
                        : other.TypeArguments).AsSpan())
            && (MethodArguments.IsDefault ? [] : MethodArguments)
                .AsSpan()
                .SequenceEqual(
                    (other.MethodArguments.IsDefault
                        ? []
                        : other.MethodArguments).AsSpan());

        public override bool Equals(object? obj) =>
            obj is ResourceOwnershipGenericContext other
            && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (ResourceOwnershipGenericContextArgument argument
                in TypeArguments.IsDefault
                ? []
                : TypeArguments)
            {
                hash.Add(argument);
            }
            hash.Add(TypeArguments.IsDefault ? 0 : TypeArguments.Length);
            foreach (ResourceOwnershipGenericContextArgument argument
                in MethodArguments.IsDefault
                ? []
                : MethodArguments)
            {
                hash.Add(argument);
            }
            hash.Add(
                MethodArguments.IsDefault
                    ? 0
                    : MethodArguments.Length);
            return hash.ToHashCode();
        }
    }

    readonly record struct ResourceOwnershipGenericContextArgument(
        TypeRef Type,
        ResourceOccurrenceType? ExactType,
        string Identity)
    {
        internal static ResourceOwnershipGenericContextArgument Create(
            TypeRef type,
            ResourceOccurrenceType? exactType)
        {
            var builder = new StringBuilder();
            AppendTypeRefIdentity(builder, type);
            if (exactType is not null)
                AppendType(builder, exactType);
            return new(type, exactType, builder.ToString());
        }
    }

    readonly record struct ApplicableOwnershipUse(
        ResourceOwnershipUse Use,
        bool IsComplete);

    enum ResourceDomainMatch
    {
        NoMatch,
        Match,
        Incomplete,
    }

    sealed record PathState(
        int NodeId,
        int ParameterIndex,
        ResourceOccurrenceRoot.Acquisition Obligation,
        ResourceOccurrenceResourceKind ResourceKind,
        ImmutableArray<ResourceOwnershipPathStep> Steps,
        bool IsComplete,
        ImmutableHashSet<(
            int Node,
            int Parameter,
            ResourceOwnershipGenericContext GenericContext)> Visited,
        ResourceOwnershipGenericContext GenericContext);
}
