using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using ILInspector.Decompiler;
using ILInspector.Research;
using InertText;
using Analysis = ILInspector.Analysis;

using DotnetInspect.Web;
using DotnetInspect.Web.Interop.Source;

namespace DotnetInspect.Web.Interop.Source;

/// <summary>
/// Annotated source. The returned document and its viewer contract are the capability being
/// requested, so it stays with source; Analysis facts embedded in that product document do not
/// transfer ownership to another adapter.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class SourceExports
{
    /// <summary>
    /// One member's portable <c>AnnotatedSourceDocument</c>, produced by
    /// <see cref="AssemblyContextMemberProjectionQuery"/> over the participant that owns the
    /// member's implementation. The document is serialized by its owning product context, so the
    /// payload is the same artifact the CLI emits and the viewer validates.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryMemberAnnotatedSource(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string typeQueryId,
        string memberName,
        string memberSignature,
        string selectorKey,
        int metadataToken,
        string styleOptionsJson)
    {
        MemberSourceProjection source = await ProjectMemberAsync(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            typeQueryId,
            memberName,
            memberSignature,
            selectorKey,
            metadataToken,
            styleOptionsJson,
            factRows: false);
        BrowserAnnotatedSource annotated = BrowserAnnotatedSource.Create(
            source.Document,
            source.Signature,
            source.Provenance,
            source.ContextLimitation,
            source.InvocationDestinations,
            source.DestinationUnavailableReason,
            findingEvidenceDocuments: null,
            findingEvidence: null,
            findingEvidenceUnavailableReason:
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
            callRelationships: source.CallRelationships,
            callRelationshipsUnavailableReason:
                source.CallRelationshipsUnavailableReason,
            callCycles: source.CallCycles,
            callCyclesUnavailableReason:
                source.CallCyclesUnavailableReason,
            synchronousCompletions:
                source.SynchronousCompletions,
            synchronousCompletionsUnavailableReason:
                source.SynchronousCompletionsUnavailableReason,
            awaitCompletionPaths:
                source.AwaitCompletionPaths,
            awaitCompletionPathsUnavailableReason:
                source.AwaitCompletionPathsUnavailableReason,
            allocationExceptionPaths:
                source.AllocationExceptionPaths,
            allocationExceptionPathsUnavailableReason:
                source.AllocationExceptionPathsUnavailableReason,
            localThrowPaths: source.LocalThrowPaths,
            localThrowPathsUnavailableReason:
                source.LocalThrowPathsUnavailableReason);
        return JsonSerializer.Serialize(
            annotated,
            BrowserSourceJsonContext.Default.BrowserAnnotatedSource);
    }

    /// <summary>
    /// One Research-issued Finding census projected through its Facts and Annotated Source views.
    /// The receipt scopes every non-null fact-row key and every document fact-id sidecar key.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryMemberFindingCensus(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string typeQueryId,
        string memberName,
        string memberSignature,
        string selectorKey,
        int metadataToken,
        string styleOptionsJson)
    {
        MemberSourceProjection source = await ProjectMemberAsync(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            typeQueryId,
            memberName,
            memberSignature,
            selectorKey,
            metadataToken,
            styleOptionsJson,
            factRows: true);
        BrowserMemberFindingCensus census = BrowserMemberFindingCensus.Create(
            source.Projection.FactCensusReceipt,
            source.Projection.Facts,
            source.Document,
            source.Signature,
            source.Projection.SourceDocumentFactIdentities,
            source.Provenance,
            source.ContextLimitation,
            source.InvocationDestinations,
            source.DestinationUnavailableReason,
            source.FindingEvidenceDocuments,
            source.FindingEvidence,
            source.FindingEvidenceUnavailableReason,
            source.CallRelationships,
            source.CallRelationshipsUnavailableReason,
            source.CallCycles,
            source.CallCyclesUnavailableReason,
            source.SynchronousCompletions,
            source.SynchronousCompletionsUnavailableReason,
            source.AwaitCompletionPaths,
            source.AwaitCompletionPathsUnavailableReason,
            source.AllocationExceptionPaths,
            source.AllocationExceptionPathsUnavailableReason,
            source.LocalThrowPaths,
            source.LocalThrowPathsUnavailableReason);
        return JsonSerializer.Serialize(
            census,
            BrowserSourceJsonContext.Default.BrowserMemberFindingCensus);
    }

    static async Task<MemberSourceProjection> ProjectMemberAsync(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string typeQueryId,
        string memberName,
        string memberSignature,
        string selectorKey,
        int metadataToken,
        string styleOptionsJson,
        bool factRows)
    {
        await using BrowserMemberResolution.ScopedResolution resolved =
            await BrowserMemberResolution.ImplementationMemberAsync(
                packageId,
                version,
                targetFramework,
                assemblyName,
                typeIdentity,
                memberName,
                selectorKey,
                metadataToken);
        BrowserInspectionScope scope = resolved.Scope;
        BrowserWorkspaceParticipant participant = resolved.ImplementationParticipant;
        Analysis.CallGraphMemberResolution resolution = resolved.Member;

        AssemblyMemberProjection projection = BrowserSurfaceProjection.Require(
            scope.UseImplementationParticipant(
                participant,
                (group, member) => AssemblyContextMemberProjectionQuery.ExecuteParticipant(
                    group,
                    member,
                    new AssemblyContextMemberProjectionRequest(
                        typeQueryId,
                        memberName,
                        MethodToken: resolution.BodyToken,
                        SourceDocument: true,
                        FactRows: factRows,
                        FindingEvidence: factRows,
                        InvocationDestinations: true,
                        AnalysisFeatures: factRows
                            ? Analysis.LibraryBodyAnalysisFeatures.Default
                                | Analysis.LibraryBodyAnalysisFeatures.LocalThrows
                            : Analysis.LibraryBodyAnalysisFeatures.Default,
                        PrinterOptions: BrowserStyleOptions.Resolve(styleOptionsJson),
                        CallRelationships: factRows,
                        CallCycles: factRows,
                        SynchronousCompletions: factRows,
                        AwaitCompletionPaths: factRows,
                        AllocationExceptionPaths: factRows,
                        LocalThrowPaths: factRows))),
            $"Annotated source for '{typeQueryId}.{memberName}'");

        if (projection.Projection.SourceDocument is not { } document)
        {
            IReadOnlyList<DecompilerDiagnostic> diagnostics =
                projection.Projection.SourceDocumentFailure?.Diagnostics ?? [];
            throw new InvalidOperationException(
                diagnostics.Count > 0
                    ? $"Annotated source projection failed: "
                        + string.Join("; ", diagnostics.Select(item => item.ToString()))
                    : "Annotated source projection produced no document.");
        }

        BrowserAnnotatedSourceInvocationDestination[]? destinations =
            projection.ContextLimitation is null
                ?
                [
                    .. projection.InvocationDestinations.Select(destination =>
                        new BrowserAnnotatedSourceInvocationDestination(
                            destination.NodeId,
                            BrowserSourceWireProjection.Project(
                                BrowserCallGraphProjection.Target(
                                    destination.Target,
                                    [participant.Assembly.Identity],
                                    null,
                                    scope.SurfaceParticipants)))),
                ]
                : null;
        BrowserAnnotatedSourceFindingEvidenceDocument[]?
            findingEvidenceDocuments = null;
        BrowserAnnotatedSourceFindingEvidence[]? findingEvidence = null;
        if (projection.FindingEvidence is { } projectedFindingEvidence)
        {
            BrowserCalleeEvidenceDocumentProjectionResult documentProjection =
                BrowserCalleeEvidenceDocumentProjection.Project(
                    projectedFindingEvidence);
            findingEvidenceDocuments = documentProjection.Documents;
            findingEvidence =
            [
                .. projectedFindingEvidence.Select(evidence =>
                {
                    BrowserCallGraphTarget target =
                        BrowserSourceWireProjection.Project(
                            BrowserCallGraphProjection.Target(
                                evidence.Member,
                                participant.Assembly.Identity,
                                $"finding-{evidence.FactId}",
                                scope.SurfaceParticipants));
                    BrowserCalleeEvidenceDocumentReference documentReference =
                        BrowserCalleeEvidenceDocumentProjection.Reference(
                            evidence,
                            documentProjection);
                    return new BrowserAnnotatedSourceFindingEvidence(
                        evidence.FactId,
                        evidence.InstanceKey.Value,
                        FullyQualifiedMemberName(evidence.Member),
                        target,
                        EvidenceState(evidence.State),
                        [
                            .. evidence.AggregateInputs.Select(input =>
                                new BrowserCostCalleeEvidenceInput(
                                    AggregateInputKind(input.Kind),
                                    input.Value)),
                        ],
                        [
                            .. evidence.Coordinates.Select(coordinate =>
                                new BrowserAnnotatedSourceFindingEvidenceCoordinate(
                                    coordinate.Location.ILOffset
                                        ?? throw new InvalidOperationException(
                                            "Instruction evidence carried no IL offset."),
                                    EvidenceKind(coordinate.Kind))),
                        ],
                        documentReference.DocumentId,
                        documentReference.NodeIds,
                        documentReference.UnavailableReason);
                }),
            ];
        }
        BrowserAnnotatedSourceCallRelationship[]? callRelationships = null;
        if (projection.CallRelationships is { } projectedRelationships)
        {
            callRelationships =
            [
                .. projectedRelationships.Relationships.Select(
                    relationship =>
                        new BrowserAnnotatedSourceCallRelationship(
                            relationship.Occurrence.EdgeRow,
                            relationship.Occurrence.FactId,
                            relationship.Occurrence.ModuleVersionId,
                            relationship.Occurrence.CallerToken,
                            relationship.Occurrence.ILOffset,
                            relationship.Occurrence.OperandToken,
                            CallKind(relationship.Occurrence.Kind),
                            relationship.Occurrence.InLoop,
                            BrowserSourceWireProjection.Project(
                                BrowserCallGraphProjection.Target(
                                    relationship.Target,
                                    [participant.Assembly.Identity],
                                    null,
                                    scope.SurfaceParticipants)))),
            ];
        }
        BrowserAnnotatedSourceCallCycleInspection? callCycles = null;
        if (projection.CallCycles is { } projectedCycles)
        {
            callCycles = new BrowserAnnotatedSourceCallCycleInspection(
                Available: true,
                UnavailableReason: null,
                projectedCycles.IsComplete,
                [
                    .. CycleLimits(projectedCycles.Limits),
                ],
                [
                    .. projectedCycles.Findings.Select(finding =>
                        new BrowserAnnotatedSourceCallCycle(
                            finding.Key.IdentityKey,
                            finding.Ordinal,
                            [.. finding.EdgeRows],
                            [.. finding.FactIds],
                            [
                                .. finding.Targets.Select(target =>
                                    BrowserSourceWireProjection.Project(
                                        BrowserCallGraphProjection.Target(
                                            target,
                                            [participant.Assembly.Identity],
                                            null,
                                            scope.SurfaceParticipants))),
                            ])),
                ]);
        }
        BrowserAnnotatedSourceSynchronousCompletion[]?
            synchronousCompletions = null;
        if (projection.SynchronousCompletions
            is { } projectedSynchronousCompletions)
        {
            synchronousCompletions =
            [
                .. projectedSynchronousCompletions.Select(observation =>
                    new BrowserAnnotatedSourceSynchronousCompletion(
                        observation.FactId,
                        SynchronousCompletionKind(observation.Kind))),
            ];
        }
        BrowserAnnotatedSourceAwaitCompletionPath[]?
            awaitCompletionPaths = null;
        if (projection.AwaitCompletionPaths
            is { } projectedAwaitCompletionPaths)
        {
            awaitCompletionPaths =
            [
                .. projectedAwaitCompletionPaths.Select(observation =>
                    new BrowserAnnotatedSourceAwaitCompletionPath(
                        observation.NodeId)),
            ];
        }
        BrowserAnnotatedSourceAllocationExceptionPath[]?
            allocationExceptionPaths = null;
        if (projection.AllocationExceptionPaths
            is { } projectedAllocationExceptionPaths)
        {
            allocationExceptionPaths =
            [
                .. projectedAllocationExceptionPaths.Select(observation =>
                    new BrowserAnnotatedSourceAllocationExceptionPath(
                        observation.FactId,
                        ProjectAllocationExceptionPathKind(
                            observation.Kind))),
            ];
        }
        BrowserAnnotatedSourceLocalThrowPathInspection? localThrowPaths = null;
        if (projection.LocalThrowPaths is { } projectedLocalThrowPaths)
        {
            localThrowPaths =
                new BrowserAnnotatedSourceLocalThrowPathInspection(
                    Available: true,
                    UnavailableReason: null,
                    projectedLocalThrowPaths.IsComplete,
                    [
                        .. projectedLocalThrowPaths.Boundaries
                            .GroupBy(static boundary => boundary.Kind)
                            .Select(group =>
                                new BrowserAnnotatedSourceLocalThrowPathBoundary(
                                    ProjectLocalThrowPathBoundaryKind(
                                        group.Key),
                                    group.Max(static boundary =>
                                        boundary.Value))),
                    ],
                    new BrowserAnnotatedSourceLocalThrowPathLimits(
                        projectedLocalThrowPaths.Limits.MaximumDepth,
                        projectedLocalThrowPaths.Limits.MaximumNodes,
                        projectedLocalThrowPaths.Limits.MaximumEdges,
                        projectedLocalThrowPaths.Limits.MaximumPaths),
                    new BrowserAnnotatedSourceLocalThrowPathReceipt(
                        projectedLocalThrowPaths.Receipt.DestinationSearches,
                        projectedLocalThrowPaths.Receipt.SearchNodes,
                        projectedLocalThrowPaths.Receipt.SearchedEdges,
                        projectedLocalThrowPaths.Receipt
                            .ObservedReachablePairs,
                        projectedLocalThrowPaths.Receipt.ReturnedPaths),
                    [
                        .. projectedLocalThrowPaths.Paths.Select(path =>
                            new BrowserAnnotatedSourceLocalThrowPath(
                                [.. path.FactIds],
                                [
                                    .. path.Targets.Select(target =>
                                        BrowserSourceWireProjection.Project(
                                            BrowserCallGraphProjection.Target(
                                                target,
                                                [participant.Assembly.Identity],
                                                null,
                                                scope.SurfaceParticipants))),
                                ],
                                [
                                    .. path.TerminalThrows.Select(site =>
                                        new BrowserAnnotatedSourceLocalThrowSite(
                                            site.ExceptionType
                                                .ToQualifiedDisplayString(),
                                            site.Definition.ModuleVersionId,
                                            site.Definition.Definition.Value,
                                            site.ConstructionOffset,
                                            site.ConstructorToken,
                                            site.ThrowOffset)),
                                ])),
                    ]);
        }

        return new MemberSourceProjection(
            projection.Projection,
            document,
            new InertString(TextPolicy.Field, memberSignature),
            PackageProvenance("Annotated by dotnet-inspect from", participant),
            projection.ContextLimitation is { } limitation
                ? $"{limitation.Kind}: {limitation.Detail}"
                : null,
            destinations,
            destinations is null
                ? BrowserAnnotatedSourceCapabilityUnavailableReason.ContextUnavailable
                : BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
            findingEvidenceDocuments,
            findingEvidence,
            findingEvidence is null
                ? projection.ContextLimitation is null
                    ? BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected
                    : BrowserAnnotatedSourceCapabilityUnavailableReason.ContextUnavailable
                : BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
            callRelationships,
            callRelationships is null
                ? projection.ContextLimitation is null
                    ? BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected
                    : BrowserAnnotatedSourceCapabilityUnavailableReason.ContextUnavailable
                : BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
            callCycles,
            callCycles is null
                ? projection.ContextLimitation is null
                    ? BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected
                    : BrowserAnnotatedSourceCapabilityUnavailableReason.ContextUnavailable
                : BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
            synchronousCompletions,
            synchronousCompletions is null
                ? projection.ContextLimitation is null
                    ? BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected
                    : BrowserAnnotatedSourceCapabilityUnavailableReason.ContextUnavailable
                : BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
            awaitCompletionPaths,
            BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
            allocationExceptionPaths,
            allocationExceptionPaths is null
                ? projection.ContextLimitation is null
                    ? BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected
                    : BrowserAnnotatedSourceCapabilityUnavailableReason.ContextUnavailable
                : BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
            localThrowPaths,
            localThrowPaths is null
                ? projection.ContextLimitation is null
                    ? BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected
                    : BrowserAnnotatedSourceCapabilityUnavailableReason.ContextUnavailable
                : BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected);
    }

    static string FullyQualifiedMemberName(Analysis.MethodIdentity member)
    {
        string genericParameters = member.GenericArity == 0
            ? ""
            : $"<{string.Join(", ", member.GenericParameterNames)}>";
        return $"{member.DeclaringType.ToQualifiedDisplayString()}.{member.Name}"
            + $"{genericParameters}({string.Join(", ", member.ParameterTypes.Select(
                type => type.ToQualifiedDisplayString()))})";
    }

    static BrowserCalleeEvidenceState EvidenceState(
    ResearchFindingEvidenceState state) =>
    state switch
    {
        ResearchFindingEvidenceState.Instruction =>
            BrowserCalleeEvidenceState.Instruction,
        ResearchFindingEvidenceState.Method =>
            BrowserCalleeEvidenceState.Method,
        ResearchFindingEvidenceState.InstructionUnavailable =>
            BrowserCalleeEvidenceState.InstructionUnavailable,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    static BrowserCostCalleeEvidenceInputKind AggregateInputKind(
    CallSiteCostEvidenceInputKind kind) =>
    kind switch
    {
        CallSiteCostEvidenceInputKind.AllocationInLoop =>
            BrowserCostCalleeEvidenceInputKind.AllocationInLoop,
        CallSiteCostEvidenceInputKind.Reflection =>
            BrowserCostCalleeEvidenceInputKind.Reflection,
        CallSiteCostEvidenceInputKind.CallInLoop =>
            BrowserCostCalleeEvidenceInputKind.CallInLoop,
        CallSiteCostEvidenceInputKind.RootReach =>
            BrowserCostCalleeEvidenceInputKind.RootReach,
        CallSiteCostEvidenceInputKind.DirectCallers =>
            BrowserCostCalleeEvidenceInputKind.DirectCallers,
        CallSiteCostEvidenceInputKind.LoopCalls =>
            BrowserCostCalleeEvidenceInputKind.LoopCalls,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    static BrowserCalleeEvidenceKind EvidenceKind(
    CallSiteEvidenceKind kind) =>
    kind switch
        {
            CallSiteEvidenceKind.ExceptionConstruction =>
                BrowserCalleeEvidenceKind.ExceptionConstruction,
            CallSiteEvidenceKind.Localloc =>
                BrowserCalleeEvidenceKind.Localloc,
            CallSiteEvidenceKind.Calli =>
                BrowserCalleeEvidenceKind.Calli,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static BrowserAnnotatedSourceCallKind CallKind(
        Analysis.CallKind kind) =>
        kind switch
        {
            Analysis.CallKind.Call =>
                BrowserAnnotatedSourceCallKind.Call,
            Analysis.CallKind.CallVirtual =>
                BrowserAnnotatedSourceCallKind.CallVirtual,
            Analysis.CallKind.NewObject =>
                BrowserAnnotatedSourceCallKind.NewObject,
            Analysis.CallKind.LoadFunction =>
                BrowserAnnotatedSourceCallKind.LoadFunction,
            Analysis.CallKind.LoadVirtualFunction =>
                BrowserAnnotatedSourceCallKind.LoadVirtualFunction,
            Analysis.CallKind.CallIndirect =>
                BrowserAnnotatedSourceCallKind.CallIndirect,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static BrowserSynchronousCompletionKind SynchronousCompletionKind(
        Analysis.SynchronousCompletionKind kind) =>
        kind switch
        {
            Analysis.SynchronousCompletionKind.TaskWait =>
                BrowserSynchronousCompletionKind.TaskWait,
            Analysis.SynchronousCompletionKind.TaskResult =>
                BrowserSynchronousCompletionKind.TaskResult,
            Analysis.SynchronousCompletionKind.TaskAwaiterGetResult =>
                BrowserSynchronousCompletionKind.TaskAwaiterGetResult,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static BrowserAllocationExceptionPathKind ProjectAllocationExceptionPathKind(
        AllocationExceptionPathKind kind) =>
        kind switch
        {
            AllocationExceptionPathKind.ThrownValue =>
                BrowserAllocationExceptionPathKind.ThrownValue,
            AllocationExceptionPathKind.ExceptionHandler =>
                BrowserAllocationExceptionPathKind.ExceptionHandler,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static BrowserAnnotatedSourceLocalThrowPathBoundaryKind
        ProjectLocalThrowPathBoundaryKind(
            AssemblyMemberLocalThrowPathBoundaryKind kind) =>
        kind switch
        {
            AssemblyMemberLocalThrowPathBoundaryKind.AnalysisIncomplete =>
                BrowserAnnotatedSourceLocalThrowPathBoundaryKind
                    .AnalysisIncomplete,
            AssemblyMemberLocalThrowPathBoundaryKind.TraversalBoundary =>
                BrowserAnnotatedSourceLocalThrowPathBoundaryKind
                    .TraversalBoundary,
            AssemblyMemberLocalThrowPathBoundaryKind
                .PartialMethodEvidenceScope =>
                BrowserAnnotatedSourceLocalThrowPathBoundaryKind
                    .PartialMethodEvidenceScope,
            AssemblyMemberLocalThrowPathBoundaryKind.UnresolvedLocalCalls =>
                BrowserAnnotatedSourceLocalThrowPathBoundaryKind
                    .UnresolvedLocalCalls,
            AssemblyMemberLocalThrowPathBoundaryKind
                .UnattributedGeneratedBodies =>
                BrowserAnnotatedSourceLocalThrowPathBoundaryKind
                    .UnattributedGeneratedBodies,
            AssemblyMemberLocalThrowPathBoundaryKind.DepthLimit =>
                BrowserAnnotatedSourceLocalThrowPathBoundaryKind.DepthLimit,
            AssemblyMemberLocalThrowPathBoundaryKind.NodeBudget =>
                BrowserAnnotatedSourceLocalThrowPathBoundaryKind.NodeBudget,
            AssemblyMemberLocalThrowPathBoundaryKind.EdgeBudget =>
                BrowserAnnotatedSourceLocalThrowPathBoundaryKind.EdgeBudget,
            AssemblyMemberLocalThrowPathBoundaryKind.PathBudget =>
                BrowserAnnotatedSourceLocalThrowPathBoundaryKind.PathBudget,
            AssemblyMemberLocalThrowPathBoundaryKind
                .IncompleteLocalThrowEvidence =>
                BrowserAnnotatedSourceLocalThrowPathBoundaryKind
                    .IncompleteLocalThrowEvidence,
            AssemblyMemberLocalThrowPathBoundaryKind
                .IncompleteCorrespondence =>
                BrowserAnnotatedSourceLocalThrowPathBoundaryKind
                    .IncompleteCorrespondence,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static IEnumerable<BrowserAnnotatedSourceCallCycleLimit> CycleLimits(
        AnnotatedCallGraphCycleLimit limits)
    {
        if (limits.HasFlag(
                AnnotatedCallGraphCycleLimit.TraversalBoundary))
        {
            yield return
                BrowserAnnotatedSourceCallCycleLimit.TraversalBoundary;
        }
        if (limits.HasFlag(
                AnnotatedCallGraphCycleLimit.IncompleteCorrespondence))
        {
            yield return
                BrowserAnnotatedSourceCallCycleLimit.IncompleteCorrespondence;
        }
        if (limits.HasFlag(
                AnnotatedCallGraphCycleLimit.WitnessBudget))
        {
            yield return
                BrowserAnnotatedSourceCallCycleLimit.WitnessBudget;
        }
        if (limits.HasFlag(
                AnnotatedCallGraphCycleLimit.PathBudget))
        {
            yield return
                BrowserAnnotatedSourceCallCycleLimit.PathBudget;
        }
        if (limits.HasFlag(
                AnnotatedCallGraphCycleLimit.AnalysisFailure))
        {
            yield return
                BrowserAnnotatedSourceCallCycleLimit.AnalysisFailure;
        }
    }

    private sealed record MemberSourceProjection(
        MemberProjectionResult Projection,
        AnnotatedSourceDocument Document,
        InertString Signature,
        InertString Provenance,
        string? ContextLimitation,
        BrowserAnnotatedSourceInvocationDestination[]? InvocationDestinations,
        BrowserAnnotatedSourceCapabilityUnavailableReason DestinationUnavailableReason,
        BrowserAnnotatedSourceFindingEvidenceDocument[]?
            FindingEvidenceDocuments,
        BrowserAnnotatedSourceFindingEvidence[]? FindingEvidence,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            FindingEvidenceUnavailableReason,
        BrowserAnnotatedSourceCallRelationship[]? CallRelationships,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            CallRelationshipsUnavailableReason,
        BrowserAnnotatedSourceCallCycleInspection? CallCycles,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            CallCyclesUnavailableReason,
        BrowserAnnotatedSourceSynchronousCompletion[]?
            SynchronousCompletions,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            SynchronousCompletionsUnavailableReason,
        BrowserAnnotatedSourceAwaitCompletionPath[]?
            AwaitCompletionPaths,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            AwaitCompletionPathsUnavailableReason,
        BrowserAnnotatedSourceAllocationExceptionPath[]?
            AllocationExceptionPaths,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            AllocationExceptionPathsUnavailableReason,
        BrowserAnnotatedSourceLocalThrowPathInspection?
            LocalThrowPaths,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            LocalThrowPathsUnavailableReason);
}
