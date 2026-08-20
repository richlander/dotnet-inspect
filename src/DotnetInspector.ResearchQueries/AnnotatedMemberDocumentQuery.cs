using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>
/// Inputs for composing one already-acquired graph layer with portable
/// annotated source for the selected member.
/// </summary>
public sealed record AnnotatedMemberDocumentInput(
    MetadataSource Source,
    MemberCallGraphView CallGraph,
    AnnotationStage Stage = AnnotationStage.Raised,
    PrinterOptions? PrinterOptions = null,
    CallGraphCycleSearchOptions? CycleSearchOptions = null,
    ArrayPoolOwnershipSearchOptions? OwnershipSearchOptions = null);

/// <summary>
/// One physical call occurrence joined to both a stable graph edge row and a
/// portable source fact. The fact reaches source through the document's sole
/// fact-to-node target relation.
/// </summary>
public readonly record struct AnnotatedCallGraphOccurrence(
    int EdgeRow,
    int FactId,
    Guid ModuleVersionId,
    int CallerToken,
    int ILOffset,
    int OperandToken,
    CallKind Kind,
    bool InLoop);

/// <summary>
/// Reasons an annotated cycle census cannot prove that no other focus cycle
/// exists. Positive findings remain valid under every limit.
/// </summary>
[Flags]
public enum AnnotatedCallGraphCycleLimit
{
    None = 0,
    TraversalBoundary = 1,
    IncompleteCorrespondence = 2,
    WitnessBudget = 4,
    PathBudget = 8,
    AnalysisFailure = 16,
}

/// <summary>
/// Focus-member cycle findings and the independent completeness state of the
/// operation that produced them.
/// </summary>
public sealed record AnnotatedCallGraphCycleInspection(
    ImmutableArray<Finding<CallGraphCycleWitness>> Findings,
    AnnotatedCallGraphCycleLimit Limits)
{
    public bool IsComplete => Limits == AnnotatedCallGraphCycleLimit.None;
}

/// <summary>One bounded graph layer attached to an annotated source document.</summary>
public sealed record AnnotatedCallGraphOverlay(
    CallGraphTier Tier,
    CallGraphProjection Projection,
    ImmutableArray<AnnotatedCallGraphOccurrence> Occurrences,
    AnnotatedCallGraphCycleInspection Cycles,
    AnnotatedCallGraphOwnershipInspection Ownership,
    CatalogCallGraphDiagnostics Diagnostics);

/// <summary>
/// Portable annotated source plus a bounded, format-neutral relationship
/// overlay over that source.
/// </summary>
public sealed record AnnotatedMemberDocument(
    AnnotatedSourceDocument Source,
    AnnotatedCallGraphOverlay CallGraph);

/// <summary>Result of composing source and graph evidence.</summary>
public abstract record AnnotatedMemberDocumentResult
{
    private AnnotatedMemberDocumentResult()
    {
    }

    public sealed record Complete(AnnotatedMemberDocument Document)
        : AnnotatedMemberDocumentResult;

    public sealed record Failed(DecompilerResult Failure)
        : AnnotatedMemberDocumentResult;
}

/// <summary>
/// Composes an already-acquired progressive graph layer with one portable
/// source document. It performs no graph or Analysis acquisition.
/// </summary>
/// <remarks>
/// <c>AnnotatedMemberDocument_ReusesCalleeLayerAndMapsEveryPhysicalCallSite</c>
/// gates graph-session reuse and the unchanged graph build counts.
/// <c>RequirementsNone_DoesNotResolveAnAssemblyContext</c> gates the Research
/// acquisition boundary used by the call-only profile.
/// </remarks>
public static class AnnotatedMemberDocumentQuery
{
    public static InspectionQuery<AnnotatedMemberDocumentResult> Definition
        { get; } =
        new("Annotated member document", InspectionCost.NetworkFree);

    public static AnnotatedMemberDocumentResult Execute(
        AnnotatedMemberDocumentInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Source);
        ArgumentNullException.ThrowIfNull(input.CallGraph);

        MemberCallGraphView graphView = input.CallGraph;
        if (graphView.CalleeRoot is null)
        {
            return Failure(
                "A call relationship overlay requires a callee topology.");
        }
        if (graphView.FocusModuleVersionId
                != input.Source.ModuleVersionId)
        {
            return Failure(
                "The annotated source module does not match the call-graph focus.");
        }

        CallGraphProjection projection = CallGraphProjection.Create(
            graphView.CallerRoot,
            graphView.CalleeRoot);
        AnnotatedCallGraphCycleInspection cycles =
            CallGraphCycleFindings.Inspect(
                graphView,
                projection,
                input.CycleSearchOptions);
        AnnotatedCallGraphOwnershipInspection ownership =
            ArrayPoolOwnershipPathFindings.Inspect(
                graphView,
                projection,
                input.OwnershipSearchOptions);
        var mappedCalls =
            ImmutableArray.CreateBuilder<(
                DirectCall Call,
                CallGraphRow Row)>();
        bool focusIsBudgetLimited =
            graphView.CalleeRoot.Status
                is CallTreeStatus.DepthLimited
                or CallTreeStatus.Truncated;
        foreach (DirectCall call in graphView.FocusCallSites)
        {
            CallGraphRowMatch match =
                projection.FindFocusCalleeRow(call, out CallGraphRow row);
            if (match == CallGraphRowMatch.Found)
            {
                mappedCalls.Add((call, row));
                continue;
            }
            if (match == CallGraphRowMatch.NotProjected
                && focusIsBudgetLimited)
            {
                continue;
            }

            return Failure(
                match == CallGraphRowMatch.Ambiguous
                    ? $"Call site IL_{call.ILOffset:X4} maps to more than one stable graph edge."
                    : $"Call site IL_{call.ILOffset:X4} could not be joined to one stable graph edge.");
        }

        var sourceProjection = ResearchViews.ProjectMember(
            new ResearchViews.MemberProjectionRequest(
                input.Source,
                projection.Focus.Member.DeclaringType
                    .ToQualifiedDisplayString(),
                projection.Focus.Member.Name,
                AnnotatedStage: input.Stage,
                Registry: ResearchFactRegistry.CallRelationships,
                MethodToken: graphView.FocusMethodToken,
                PrinterOptions: input.PrinterOptions,
                SourceDocument: true,
                CallSites:
                [
                    .. mappedCalls.Select(mapped => mapped.Call),
                ]));
        if (sourceProjection.SourceDocument is not { } source)
        {
            return new AnnotatedMemberDocumentResult.Failed(
                sourceProjection.SourceDocumentFailure
                ?? DecompilerResult.Failure(
                    DiagnosticIds.InternalError,
                    "Annotated source production returned no document."));
        }

        if (graphView.FocusMethodToken
                != sourceProjection.SelectedMethodToken)
        {
            return Failure(
                "The annotated source member does not match the call-graph focus.");
        }

        var factsByOffset = source.Facts
            .Where(fact =>
                fact.Descriptor
                    == ResearchFactRegistry.CallRelationshipDescriptorId)
            .ToDictionary(fact => fact.SourceOffset);
        var occurrences =
            ImmutableArray.CreateBuilder<AnnotatedCallGraphOccurrence>(
                mappedCalls.Count);
        foreach ((DirectCall call, CallGraphRow row) in mappedCalls)
        {
            if (!factsByOffset.TryGetValue(
                    call.ILOffset,
                    out AnnotatedSourceFact fact))
            {
                return Failure(
                    $"Call site IL_{call.ILOffset:X4} has no portable source fact.");
            }

            occurrences.Add(
                new AnnotatedCallGraphOccurrence(
                    row.Number,
                    fact.Id,
                    call.EvidenceMethod.ModuleVersionId,
                    call.EvidenceMethod.MetadataToken,
                    call.ILOffset,
                    call.OperandToken,
                    call.Kind,
                    call.InLoop));
        }

        return new AnnotatedMemberDocumentResult.Complete(
            new AnnotatedMemberDocument(
                source,
                new AnnotatedCallGraphOverlay(
                    graphView.Tier,
                    projection,
                    occurrences.MoveToImmutable(),
                    cycles,
                    ownership,
                    graphView.Diagnostics)));
    }

    static AnnotatedMemberDocumentResult.Failed Failure(
        string message) =>
        new(
            DecompilerResult.Failure(
                DiagnosticIds.InternalError,
                message));
}

/// <summary>
/// Composes projection-owned cycle witnesses as one-version Findings without
/// acquiring or rebuilding graph or Analysis state.
/// </summary>
public static class CallGraphCycleFindings
{
    public static FindingDescriptor Descriptor { get; } =
        new("call.cycle", "Call cycle");

    public static AnnotatedCallGraphCycleInspection Inspect(
        MemberCallGraphView graphView,
        CallGraphProjection projection,
        CallGraphCycleSearchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graphView);
        ArgumentNullException.ThrowIfNull(projection);

        CallGraphCycleSearchResult search =
            projection.FindFocusCycles(options);
        var subject = new FindingSubject(
            $"{graphView.FocusModuleVersionId:N}|{graphView.FocusMethodToken:X8}",
            projection.Focus.Label);
        string[] nodeKeys =
        [
            .. projection.Nodes.Select(NodeKey),
        ];
        HashSet<string> duplicateKeys =
        [
            .. nodeKeys
                .GroupBy(
                    static key => key,
                    StringComparer.Ordinal)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key),
        ];
        Dictionary<int, CallGraphRow> rowsByNumber =
            projection.Rows.ToDictionary(
                static row => row.Number);
        ImmutableArray<Finding<CallGraphCycleWitness>> findings =
        [
            .. search.Witnesses.Select(
                (witness, ordinal) =>
                    new Finding<CallGraphCycleWitness>(
                        subject,
                        Descriptor,
                        CycleKey(
                            projection,
                            witness,
                            nodeKeys,
                            duplicateKeys,
                            rowsByNumber),
                        witness,
                        Ordinal: ordinal)),
        ];

        AnnotatedCallGraphCycleLimit limits =
            SearchLimits(search.Limits);
        if (projection.HasUnexploredTraversalBoundary)
        {
            limits |=
                AnnotatedCallGraphCycleLimit.TraversalBoundary;
        }
        if (projection.HasAnalysisFailureBoundary)
        {
            limits |=
                AnnotatedCallGraphCycleLimit.AnalysisFailure;
        }
        if (graphView.Diagnostics.IsIncomplete)
        {
            limits |=
                AnnotatedCallGraphCycleLimit.IncompleteCorrespondence;
        }

        return new AnnotatedCallGraphCycleInspection(
            findings,
            limits);
    }

    static FindingKey CycleKey(
        CallGraphProjection projection,
        CallGraphCycleWitness witness,
        string[] nodeKeys,
        HashSet<string> duplicateKeys,
        IReadOnlyDictionary<int, CallGraphRow> rowsByNumber)
    {
        int current = projection.Focus.Id;
        var path = new List<string>(witness.EdgeRows.Length);
        foreach (int rowNumber in witness.EdgeRows)
        {
            CallGraphRow row = rowsByNumber[rowNumber];
            if (row.Edge.From != current)
            {
                throw new InvalidOperationException(
                    "A cycle witness does not form one directed graph path.");
            }

            current = row.Edge.To;
            string key = nodeKeys[current];
            path.Add(
                duplicateKeys.Contains(key)
                    ? $"{key}|{PhysicalKey(projection.Nodes[current])}"
                    : key);
        }
        if (current != projection.Focus.Id)
        {
            throw new InvalidOperationException(
                "A cycle witness does not return to the selected member.");
        }

        return new FindingKey(
            $"cycle:{string.Join(">", path.Select(LengthPrefixed))}");
    }

    static string NodeKey(CallGraphNode node)
    {
        ResearchSubjectKey subject =
            ResearchMemberIdentity.SubjectFromMember(
                node.Member);
        return $"{node.Member.DeclaringType.Assembly}|{subject.Id}";
    }

    static string PhysicalKey(CallGraphNode node) =>
        string.Join(
            ";",
            node.GraphEvidence
                .Select(static evidence => evidence.Storage)
                .OrderBy(static storage => storage.ModuleVersionId)
                .ThenBy(static storage => storage.Kind)
                .ThenBy(static storage => storage.MethodToken)
                .ThenBy(static storage => storage.ILOffset)
                .ThenBy(static storage => storage.OperandToken)
                .Select(static storage =>
                    $"{storage.ModuleVersionId:N}:"
                    + $"{(int)storage.Kind}:"
                    + $"{storage.MethodToken:X8}:"
                    + $"{storage.ILOffset:X8}:"
                    + $"{storage.OperandToken:X8}"));

    static string LengthPrefixed(string value) =>
        $"{value.Length}:{value}";

    static AnnotatedCallGraphCycleLimit SearchLimits(
        CallGraphCycleSearchLimit limits)
    {
        AnnotatedCallGraphCycleLimit result =
            AnnotatedCallGraphCycleLimit.None;
        if (limits.HasFlag(
                CallGraphCycleSearchLimit.WitnessBudget))
        {
            result |= AnnotatedCallGraphCycleLimit.WitnessBudget;
        }
        if (limits.HasFlag(
                CallGraphCycleSearchLimit.PathBudget))
        {
            result |= AnnotatedCallGraphCycleLimit.PathBudget;
        }
        return result;
    }

}
