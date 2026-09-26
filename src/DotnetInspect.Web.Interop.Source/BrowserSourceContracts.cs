using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Sections;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using InertText;
using Inspector.Findings;
using ILInspector.Research;

namespace DotnetInspect.Web.Interop.Source;

/// <summary>
/// The source facade's browser wire contract.
/// </summary>
/// <remarks>
/// Every record here is declared and source-generated inside
/// <c>DotnetInspect.Web.Interop.Source</c>. The annotated-source document embeds a call-graph
/// target, and this facade declares its own transport for it rather than importing the call-graph
/// facade's; <c>ProductionFacadeWireContexts_AreAssemblyLocal</c> gates that ownership.
/// </remarks>
public sealed record BrowserSource(
    string Provider,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString Provenance,
    string? Url,
    string? PdbSourceLimitation,
    string Text);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserMemberSourcePartKind>))]
public enum BrowserMemberSourcePartKind
{
    Member,
    XmlDocumentation,
    Attributes,
    Signature,
    Body,
}

public sealed record BrowserMemberSourceSpan(
    int Start,
    int Length,
    int StartLine,
    int EndLine,
    string LeadingIndentation)
{
    public int End => checked(Start + Length);
}

public sealed record BrowserMemberSourcePart(
    BrowserMemberSourcePartKind Kind,
    BrowserMemberSourceSpan[] Spans);

public sealed record BrowserMemberSource(
    BrowserSource Source,
    BrowserMemberSourcePart[] Parts);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserAnnotatedSourceMedium>))]
public enum BrowserAnnotatedSourceMedium
{
    CSharp,
    Il,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserAnnotatedSourceCapabilityUnavailableReason>))]
public enum BrowserAnnotatedSourceCapabilityUnavailableReason
{
    NotProjected,
    ContextUnavailable,
}

public sealed record BrowserAnnotatedSourceCapabilityAvailability
{
    public BrowserAnnotatedSourceCapabilityAvailability(
        bool Available,
        BrowserAnnotatedSourceCapabilityUnavailableReason? UnavailableReason)
    {
        if (Available == (UnavailableReason is not null))
        {
            throw new ArgumentException(
                Available
                    ? "An available capability cannot carry an unavailable reason."
                    : "An unavailable capability must carry an unavailable reason.",
                nameof(UnavailableReason));
        }

        this.Available = Available;
        this.UnavailableReason = UnavailableReason;
    }

    public bool Available { get; }
    public BrowserAnnotatedSourceCapabilityUnavailableReason? UnavailableReason { get; }
}

public sealed record BrowserAnnotatedSourceViewerCatalog
{
    private readonly int[] _defaultFindingIds;
    private readonly BrowserAnnotatedSourceMedium[] _supportedMedia;
    private readonly string[] _invocationLikeNodeKinds;
    private readonly BrowserAnnotatedSourceInvocationDestination[]
        _invocationDestinations;

    public BrowserAnnotatedSourceViewerCatalog(
        int[] DefaultFindingIds,
        BrowserAnnotatedSourceMedium[] SupportedMedia,
        string[] InvocationLikeNodeKinds,
        BrowserAnnotatedSourceCapabilityAvailability FindingEvidence,
        BrowserAnnotatedSourceCapabilityAvailability Destinations,
        BrowserAnnotatedSourceCapabilityAvailability CallRelationships,
        BrowserAnnotatedSourceCallCycleInspection CallCycles,
        BrowserAnnotatedSourceSynchronousCompletionInspection
            SynchronousCompletions,
        BrowserAnnotatedSourceAwaitCompletionPathInspection
            AwaitCompletionPaths,
        BrowserAnnotatedSourceAllocationExceptionPathInspection
            AllocationExceptionPaths,
        BrowserAnnotatedSourceLocalThrowPathInspection
            LocalThrowPaths,
        BrowserAnnotatedSourceInvocationDestination[] InvocationDestinations)
    {
        ArgumentNullException.ThrowIfNull(DefaultFindingIds);
        ArgumentNullException.ThrowIfNull(SupportedMedia);
        ArgumentNullException.ThrowIfNull(InvocationLikeNodeKinds);
        ArgumentNullException.ThrowIfNull(FindingEvidence);
        ArgumentNullException.ThrowIfNull(Destinations);
        ArgumentNullException.ThrowIfNull(CallRelationships);
        ArgumentNullException.ThrowIfNull(CallCycles);
        ArgumentNullException.ThrowIfNull(SynchronousCompletions);
        ArgumentNullException.ThrowIfNull(AwaitCompletionPaths);
        ArgumentNullException.ThrowIfNull(AllocationExceptionPaths);
        ArgumentNullException.ThrowIfNull(LocalThrowPaths);
        ArgumentNullException.ThrowIfNull(InvocationDestinations);
        if (!Destinations.Available && InvocationDestinations.Length > 0)
        {
            throw new ArgumentException(
                "Unavailable destinations cannot carry projected rows.",
                nameof(InvocationDestinations));
        }

        _defaultFindingIds = [.. DefaultFindingIds];
        _supportedMedia = [.. SupportedMedia];
        _invocationLikeNodeKinds = [.. InvocationLikeNodeKinds];
        _invocationDestinations = [.. InvocationDestinations];
        this.FindingEvidence = FindingEvidence;
        this.Destinations = Destinations;
        this.CallRelationships = CallRelationships;
        this.CallCycles = CallCycles;
        this.SynchronousCompletions = SynchronousCompletions;
        this.AwaitCompletionPaths = AwaitCompletionPaths;
        this.AllocationExceptionPaths = AllocationExceptionPaths;
        this.LocalThrowPaths = LocalThrowPaths;
    }

    public int[] DefaultFindingIds => [.. _defaultFindingIds];
    public BrowserAnnotatedSourceMedium[] SupportedMedia => [.. _supportedMedia];
    public string[] InvocationLikeNodeKinds => [.. _invocationLikeNodeKinds];
    public BrowserAnnotatedSourceInvocationDestination[] InvocationDestinations =>
        [.. _invocationDestinations];
    public BrowserAnnotatedSourceCapabilityAvailability FindingEvidence { get; }
    public BrowserAnnotatedSourceCapabilityAvailability Destinations { get; }
    public BrowserAnnotatedSourceCapabilityAvailability CallRelationships { get; }
    public BrowserAnnotatedSourceCallCycleInspection CallCycles { get; }
    public BrowserAnnotatedSourceSynchronousCompletionInspection
        SynchronousCompletions { get; }
    public BrowserAnnotatedSourceAwaitCompletionPathInspection
        AwaitCompletionPaths { get; }
    public BrowserAnnotatedSourceAllocationExceptionPathInspection
        AllocationExceptionPaths { get; }
    public BrowserAnnotatedSourceLocalThrowPathInspection
        LocalThrowPaths { get; }
}

public sealed record BrowserAnnotatedSourceInvocationDestination(
    int NodeId,
    BrowserCallGraphTarget Target);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserAnnotatedSourceCallKind>))]
public enum BrowserAnnotatedSourceCallKind
{
    Call,
    CallVirtual,
    NewObject,
    LoadFunction,
    LoadVirtualFunction,
    CallIndirect,
}

/// <summary>
/// One physical <c>call.edge</c> Finding joined to its stable graph row and
/// typed target.
/// </summary>
public sealed record BrowserAnnotatedSourceCallRelationship(
    int EdgeRow,
    int FactId,
    Guid ModuleVersionId,
    int CallerToken,
    int IlOffset,
    int OperandToken,
    BrowserAnnotatedSourceCallKind Kind,
    bool InLoop,
    BrowserCallGraphTarget Target);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserAnnotatedSourceCallCycleLimit>))]
public enum BrowserAnnotatedSourceCallCycleLimit
{
    TraversalBoundary,
    IncompleteCorrespondence,
    WitnessBudget,
    PathBudget,
    AnalysisFailure,
}

/// <summary>
/// One observed focus cycle. The first edge is anchored to every physical
/// <c>call.edge</c> Finding for that logical relationship.
/// </summary>
public sealed record BrowserAnnotatedSourceCallCycle
{
    private readonly int[] _edgeRows;
    private readonly int[] _factIds;
    private readonly BrowserCallGraphTarget[] _targets;

    public BrowserAnnotatedSourceCallCycle(
        string FindingKey,
        int Ordinal,
        int[] EdgeRows,
        int[] FactIds,
        BrowserCallGraphTarget[] Targets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FindingKey);
        ArgumentNullException.ThrowIfNull(EdgeRows);
        ArgumentNullException.ThrowIfNull(FactIds);
        ArgumentNullException.ThrowIfNull(Targets);
        this.FindingKey = FindingKey;
        this.Ordinal = Ordinal;
        _edgeRows = [.. EdgeRows];
        _factIds = [.. FactIds];
        _targets = [.. Targets];
    }

    public string FindingKey { get; }
    public int Ordinal { get; }
    public int[] EdgeRows => [.. _edgeRows];
    public int[] FactIds => [.. _factIds];
    public BrowserCallGraphTarget[] Targets => [.. _targets];
}

/// <summary>
/// Observed focus cycles and the independent completeness state of the bounded
/// operation that produced them.
/// </summary>
public sealed record BrowserAnnotatedSourceCallCycleInspection
{
    private readonly BrowserAnnotatedSourceCallCycleLimit[] _limits;
    private readonly BrowserAnnotatedSourceCallCycle[] _findings;

    public BrowserAnnotatedSourceCallCycleInspection(
        bool Available,
        BrowserAnnotatedSourceCapabilityUnavailableReason? UnavailableReason,
        bool IsComplete,
        BrowserAnnotatedSourceCallCycleLimit[] Limits,
        BrowserAnnotatedSourceCallCycle[] Findings)
    {
        ArgumentNullException.ThrowIfNull(Limits);
        ArgumentNullException.ThrowIfNull(Findings);
        if (Available == (UnavailableReason is not null))
        {
            throw new ArgumentException(
                "Cycle availability requires exactly one of Available or UnavailableReason.");
        }
        if (!Available
            && (IsComplete || Limits.Length > 0 || Findings.Length > 0))
        {
            throw new ArgumentException(
                "Unavailable call cycles cannot carry findings or completeness state.");
        }
        if (Available != (IsComplete == (Limits.Length == 0)))
        {
            throw new ArgumentException(
                "Available call-cycle completeness must match its limit set.");
        }

        this.Available = Available;
        this.UnavailableReason = UnavailableReason;
        this.IsComplete = IsComplete;
        _limits = [.. Limits];
        _findings = [.. Findings];
    }

    public bool Available { get; }
    public BrowserAnnotatedSourceCapabilityUnavailableReason? UnavailableReason { get; }
    public bool IsComplete { get; }
    public BrowserAnnotatedSourceCallCycleLimit[] Limits => [.. _limits];
    public BrowserAnnotatedSourceCallCycle[] Findings => [.. _findings];
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserSynchronousCompletionKind>))]
public enum BrowserSynchronousCompletionKind
{
    TaskWait,
    TaskResult,
    TaskAwaiterGetResult,
}

/// <summary>
/// One exact physical <c>call.edge</c> Finding whose framework member
/// synchronously observes task completion.
/// </summary>
public sealed record BrowserAnnotatedSourceSynchronousCompletion(
    int FactId,
    BrowserSynchronousCompletionKind Kind);

/// <summary>
/// Positive synchronous-completion observations. Empty carries no absence
/// claim about blocking behavior outside the classified structures.
/// </summary>
public sealed record BrowserAnnotatedSourceSynchronousCompletionInspection
{
    private readonly BrowserAnnotatedSourceSynchronousCompletion[]
        _observations;

    public BrowserAnnotatedSourceSynchronousCompletionInspection(
        bool Available,
        BrowserAnnotatedSourceCapabilityUnavailableReason? UnavailableReason,
        BrowserAnnotatedSourceSynchronousCompletion[] Observations)
    {
        ArgumentNullException.ThrowIfNull(Observations);
        if (Available == (UnavailableReason is not null))
        {
            throw new ArgumentException(
                "Synchronous completion availability requires exactly one of Available or UnavailableReason.");
        }
        if (!Available && Observations.Length > 0)
        {
            throw new ArgumentException(
                "Unavailable synchronous completions cannot carry observations.");
        }

        this.Available = Available;
        this.UnavailableReason = UnavailableReason;
        _observations = [.. Observations];
    }

    public bool Available { get; }
    public BrowserAnnotatedSourceCapabilityUnavailableReason? UnavailableReason { get; }
    public BrowserAnnotatedSourceSynchronousCompletion[] Observations =>
        [.. _observations];
}

/// <summary>
/// One Decompiler-issued classic <c>await</c> node whose inline and
/// suspension/resume paths were proven before reconstruction.
/// </summary>
public sealed record BrowserAnnotatedSourceAwaitCompletionPath(int NodeId);

/// <summary>
/// Positive classic-await completion-path observations. Empty carries no
/// absence claim about unsupported or declined lowerings.
/// </summary>
public sealed record BrowserAnnotatedSourceAwaitCompletionPathInspection
{
    private readonly BrowserAnnotatedSourceAwaitCompletionPath[] _observations;

    public BrowserAnnotatedSourceAwaitCompletionPathInspection(
        bool Available,
        BrowserAnnotatedSourceCapabilityUnavailableReason? UnavailableReason,
        BrowserAnnotatedSourceAwaitCompletionPath[] Observations)
    {
        ArgumentNullException.ThrowIfNull(Observations);
        if (Available == (UnavailableReason is not null))
        {
            throw new ArgumentException(
                "Await completion-path availability requires exactly one of Available or UnavailableReason.");
        }
        if (!Available && Observations.Length > 0)
        {
            throw new ArgumentException(
                "Unavailable await completion paths cannot carry observations.");
        }

        this.Available = Available;
        this.UnavailableReason = UnavailableReason;
        _observations = [.. Observations];
    }

    public bool Available { get; }
    public BrowserAnnotatedSourceCapabilityUnavailableReason? UnavailableReason { get; }
    public BrowserAnnotatedSourceAwaitCompletionPath[] Observations =>
        [.. _observations];
}

[JsonConverter(typeof(JsonStringEnumConverter<
    BrowserAllocationExceptionPathKind>))]
public enum BrowserAllocationExceptionPathKind
{
    ThrownValue,
    ExceptionHandler,
}

/// <summary>
/// One exact allocation Finding Analysis placed on exception-related control
/// flow.
/// </summary>
public sealed record BrowserAnnotatedSourceAllocationExceptionPath(
    int FactId,
    BrowserAllocationExceptionPathKind Kind);

/// <summary>
/// Positive allocation exception-path observations. Empty carries no absence
/// claim about allocations or exception behavior.
/// </summary>
public sealed record BrowserAnnotatedSourceAllocationExceptionPathInspection
{
    private readonly BrowserAnnotatedSourceAllocationExceptionPath[]
        _observations;

    public BrowserAnnotatedSourceAllocationExceptionPathInspection(
        bool Available,
        BrowserAnnotatedSourceCapabilityUnavailableReason? UnavailableReason,
        BrowserAnnotatedSourceAllocationExceptionPath[] Observations)
    {
        ArgumentNullException.ThrowIfNull(Observations);
        if (Available == (UnavailableReason is not null))
        {
            throw new ArgumentException(
                "Allocation exception-path availability requires exactly one of Available or UnavailableReason.");
        }
        if (!Available && Observations.Length > 0)
        {
            throw new ArgumentException(
                "Unavailable allocation exception paths cannot carry observations.");
        }

        this.Available = Available;
        this.UnavailableReason = UnavailableReason;
        _observations = [.. Observations];
    }

    public bool Available { get; }
    public BrowserAnnotatedSourceCapabilityUnavailableReason? UnavailableReason
    {
        get;
    }
    public BrowserAnnotatedSourceAllocationExceptionPath[] Observations =>
        [.. _observations];
}

[JsonConverter(typeof(JsonStringEnumConverter<
    BrowserAnnotatedSourceLocalThrowPathBoundaryKind>))]
public enum BrowserAnnotatedSourceLocalThrowPathBoundaryKind
{
    AnalysisIncomplete,
    TraversalBoundary,
    PartialMethodEvidenceScope,
    UnresolvedLocalCalls,
    UnattributedGeneratedBodies,
    DepthLimit,
    NodeBudget,
    EdgeBudget,
    PathBudget,
    IncompleteLocalThrowEvidence,
    IncompleteCorrespondence,
}

public sealed record BrowserAnnotatedSourceLocalThrowPathBoundary(
    BrowserAnnotatedSourceLocalThrowPathBoundaryKind Kind,
    int Value);

public sealed record BrowserAnnotatedSourceLocalThrowSite(
    string ExceptionType,
    Guid DefinitionModuleVersionId,
    int DefinitionToken,
    int ConstructionOffset,
    int ConstructorToken,
    int ThrowOffset);

public sealed record BrowserAnnotatedSourceLocalThrowPath
{
    private readonly int[] _factIds;
    private readonly BrowserCallGraphTarget[] _targets;
    private readonly BrowserAnnotatedSourceLocalThrowSite[] _terminalThrows;

    public BrowserAnnotatedSourceLocalThrowPath(
        int[] FactIds,
        BrowserCallGraphTarget[] Targets,
        BrowserAnnotatedSourceLocalThrowSite[] TerminalThrows)
    {
        ArgumentNullException.ThrowIfNull(FactIds);
        ArgumentNullException.ThrowIfNull(Targets);
        ArgumentNullException.ThrowIfNull(TerminalThrows);
        _factIds = [.. FactIds];
        _targets = [.. Targets];
        _terminalThrows = [.. TerminalThrows];
    }

    public int[] FactIds => [.. _factIds];
    public BrowserCallGraphTarget[] Targets => [.. _targets];
    public BrowserAnnotatedSourceLocalThrowSite[] TerminalThrows =>
        [.. _terminalThrows];
}

public sealed record BrowserAnnotatedSourceLocalThrowPathLimits(
    int MaximumDepth,
    int MaximumNodes,
    int MaximumEdges,
    int MaximumPaths);

public sealed record BrowserAnnotatedSourceLocalThrowPathReceipt(
    int DestinationSearches,
    int SearchNodes,
    int SearchedEdges,
    int ObservedReachablePairs,
    int ReturnedPaths);

public sealed record BrowserAnnotatedSourceLocalThrowPathInspection
{
    private readonly BrowserAnnotatedSourceLocalThrowPathBoundary[] _boundaries;
    private readonly BrowserAnnotatedSourceLocalThrowPath[] _paths;

    public BrowserAnnotatedSourceLocalThrowPathInspection(
        bool Available,
        BrowserAnnotatedSourceCapabilityUnavailableReason? UnavailableReason,
        bool IsComplete,
        BrowserAnnotatedSourceLocalThrowPathBoundary[] Boundaries,
        BrowserAnnotatedSourceLocalThrowPathLimits? Limits,
        BrowserAnnotatedSourceLocalThrowPathReceipt? Receipt,
        BrowserAnnotatedSourceLocalThrowPath[] Paths)
    {
        ArgumentNullException.ThrowIfNull(Boundaries);
        ArgumentNullException.ThrowIfNull(Paths);
        if (Available == (UnavailableReason is not null))
        {
            throw new ArgumentException(
                "Local throw path availability requires exactly one of Available or UnavailableReason.");
        }
        if (!Available
            && (IsComplete
                || Boundaries.Length > 0
                || Limits is not null
                || Receipt is not null
                || Paths.Length > 0))
        {
            throw new ArgumentException(
                "Unavailable local throw paths cannot carry evidence or completeness state.");
        }
        if (Available
            && (Limits is null
                || Receipt is null
                || IsComplete != (Boundaries.Length == 0)))
        {
            throw new ArgumentException(
                "Available local throw paths require limits, a receipt, and matching completeness.");
        }

        this.Available = Available;
        this.UnavailableReason = UnavailableReason;
        this.IsComplete = IsComplete;
        _boundaries = [.. Boundaries];
        this.Limits = Limits;
        this.Receipt = Receipt;
        _paths = [.. Paths];
    }

    public bool Available { get; }
    public BrowserAnnotatedSourceCapabilityUnavailableReason? UnavailableReason
    {
        get;
    }
    public bool IsComplete { get; }
    public BrowserAnnotatedSourceLocalThrowPathBoundary[] Boundaries =>
        [.. _boundaries];
    public BrowserAnnotatedSourceLocalThrowPathLimits? Limits { get; }
    public BrowserAnnotatedSourceLocalThrowPathReceipt? Receipt { get; }
    public BrowserAnnotatedSourceLocalThrowPath[] Paths => [.. _paths];
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCalleeEvidenceKind>))]
public enum BrowserCalleeEvidenceKind
{
    ExceptionConstruction,
    Localloc,
    Calli,
}

public sealed record BrowserAnnotatedSourceFindingEvidenceCoordinate(
    int IlOffset,
    BrowserCalleeEvidenceKind Kind);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCalleeEvidenceState>))]
public enum BrowserCalleeEvidenceState
{
    Instruction,
    Method,
    InstructionUnavailable,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCostCalleeEvidenceInputKind>))]
public enum BrowserCostCalleeEvidenceInputKind
{
    AllocationInLoop,
    Reflection,
    CallInLoop,
    RootReach,
    DirectCallers,
    LoopCalls,
}

public sealed record BrowserCostCalleeEvidenceInput(
    BrowserCostCalleeEvidenceInputKind Kind,
    int? Value);

public sealed record BrowserAnnotatedSourceFindingEvidenceDocument(
    int Id,
    JsonElement Document);

/// <summary>
/// One exact caller Finding joined to method-qualified evidence in its physical callee.
/// </summary>
public sealed record BrowserAnnotatedSourceFindingEvidence(
    int FactId,
    int InstanceKey,
    string Member,
    BrowserCallGraphTarget Target,
    BrowserCalleeEvidenceState State,
    BrowserCostCalleeEvidenceInput[] AggregateInputs,
    BrowserAnnotatedSourceFindingEvidenceCoordinate[] Coordinates,
    int? DocumentId,
    int[] NodeIds,
    string? UnavailableReason);

public sealed record BrowserMemberFindingFact(
    string Member,
    int? IlOffset,
    int? CSharpLine,
    string Anchor,
    string Category,
    string Id,
    string? Detail,
    string Conditionality,
    int? InstanceKey);

public sealed record BrowserSourceFactInstance(
    int FactId,
    int InstanceKey);

/// <summary>
/// One Research-issued member Finding census transported across its Facts and Annotated Source
/// projections. The receipt scopes every non-null fact-row key and every source fact instance.
/// </summary>
public sealed record BrowserMemberFindingCensus
{
    private BrowserMemberFindingCensus(
        string FactCensusReceipt,
        BrowserMemberFindingFact[] Facts,
        BrowserAnnotatedSource AnnotatedSource,
        BrowserSourceFactInstance[] SourceFactInstances)
    {
        this.FactCensusReceipt = FactCensusReceipt;
        this.Facts = Facts;
        this.AnnotatedSource = AnnotatedSource;
        this.SourceFactInstances = SourceFactInstances;
    }

    public string FactCensusReceipt { get; }
    public BrowserMemberFindingFact[] Facts { get; }
    public BrowserAnnotatedSource AnnotatedSource { get; }
    public BrowserSourceFactInstance[] SourceFactInstances { get; }

    internal static BrowserMemberFindingCensus Create(
        FindingCensusReceipt? receipt,
        IReadOnlyList<FactRow>? facts,
        AnnotatedSourceDocument document,
        InertString signature,
        IReadOnlyList<AnnotatedSourceFactIdentity>? sourceFactIdentities,
        InertString provenance,
        string? contextLimitation,
        BrowserAnnotatedSourceInvocationDestination[]?
            invocationDestinations = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            destinationUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceFindingEvidenceDocument[]?
            findingEvidenceDocuments = null,
        BrowserAnnotatedSourceFindingEvidence[]?
            findingEvidence = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            findingEvidenceUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceCallRelationship[]?
            callRelationships = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            callRelationshipsUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceCallCycleInspection? callCycles = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            callCyclesUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceSynchronousCompletion[]?
            synchronousCompletions = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            synchronousCompletionsUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceAwaitCompletionPath[]?
            awaitCompletionPaths = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            awaitCompletionPathsUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceAllocationExceptionPath[]?
            allocationExceptionPaths = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            allocationExceptionPathsUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceLocalThrowPathInspection?
            localThrowPaths = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            localThrowPathsUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected)
    {
        if (receipt is not { IsDefault: false } censusReceipt)
            throw new InvalidOperationException(
                "Member Finding census produced no non-default receipt.");
        if (facts is null)
            throw new InvalidOperationException(
                "Member Finding census produced no Facts projection.");
        ArgumentNullException.ThrowIfNull(document);
        if (sourceFactIdentities is null)
        {
            throw new InvalidOperationException(
                "Member Finding census produced no Annotated Source identity sidecar.");
        }

        var factKeys = new HashSet<int>();
        var projectedFacts = new BrowserMemberFindingFact[facts.Count];
        for (int index = 0; index < facts.Count; index++)
        {
            FactRow fact = facts[index];
            bool hasReceipt = fact.CensusReceipt is not null;
            bool hasKey = fact.InstanceKey is not null;
            if (hasReceipt != hasKey)
            {
                throw new InvalidOperationException(
                    $"Member Finding census Facts row {index} carries an incomplete identity.");
            }

            int? keyValue = null;
            if (fact.CensusReceipt is { } factReceipt
                && fact.InstanceKey is { } factKey)
            {
                if (factReceipt != censusReceipt)
                {
                    throw new InvalidOperationException(
                        $"Member Finding census Facts row {index} carries a different receipt.");
                }
                if (factKey.IsDefault || !factKeys.Add(factKey.Value))
                {
                    throw new InvalidOperationException(
                        $"Member Finding census Facts row {index} carries an invalid or duplicate instance key.");
                }
                keyValue = factKey.Value;
            }

            projectedFacts[index] = new BrowserMemberFindingFact(
                fact.Member,
                fact.ILOffset,
                fact.CSharpLine,
                fact.Anchor,
                fact.Category,
                fact.Id,
                fact.Detail,
                fact.Conditionality,
                keyValue);
        }

        var bodyFactIds = document.Facts
            .Where(static fact => fact.Origin == AnnotatedSourceFactOrigin.Body)
            .Select(static fact => fact.Id)
            .ToHashSet();
        var sourceFactIds = new HashSet<int>();
        var sourceKeys = new HashSet<int>();
        var projectedIdentities =
            new BrowserSourceFactInstance[sourceFactIdentities.Count];
        for (int index = 0; index < sourceFactIdentities.Count; index++)
        {
            AnnotatedSourceFactIdentity identity =
                sourceFactIdentities[index];
            if (identity.CensusReceipt != censusReceipt)
            {
                throw new InvalidOperationException(
                    $"Member Finding census source identity {index} carries a different receipt.");
            }
            if (identity.InstanceKey.IsDefault
                || !sourceKeys.Add(identity.InstanceKey.Value))
            {
                throw new InvalidOperationException(
                    $"Member Finding census source identity {index} carries an invalid or duplicate instance key.");
            }
            if (!sourceFactIds.Add(identity.FactId)
                || !bodyFactIds.Contains(identity.FactId))
            {
                throw new InvalidOperationException(
                    $"Member Finding census source identity {index} carries an invalid or duplicate fact id.");
            }

            projectedIdentities[index] = new BrowserSourceFactInstance(
                identity.FactId,
                identity.InstanceKey.Value);
        }

        if (!bodyFactIds.SetEquals(sourceFactIds))
        {
            throw new InvalidOperationException(
                "Member Finding census source identities do not cover the document body facts.");
        }
        if (!factKeys.SetEquals(sourceKeys))
        {
            throw new InvalidOperationException(
                "Member Finding census Facts and Annotated Source identities do not describe the same instances.");
        }
        ValidateFindingEvidence(
            document,
            projectedIdentities,
            findingEvidenceDocuments,
            findingEvidence);

        return new BrowserMemberFindingCensus(
            censusReceipt.ToString(),
            projectedFacts,
            BrowserAnnotatedSource.Create(
                document,
                signature,
                provenance,
                contextLimitation,
                invocationDestinations,
                destinationUnavailableReason,
                findingEvidenceDocuments,
                findingEvidence,
                findingEvidenceUnavailableReason,
                callRelationships,
                callRelationshipsUnavailableReason,
                callCycles,
                callCyclesUnavailableReason,
                synchronousCompletions,
                synchronousCompletionsUnavailableReason,
                awaitCompletionPaths,
                awaitCompletionPathsUnavailableReason,
                allocationExceptionPaths,
                allocationExceptionPathsUnavailableReason,
                localThrowPaths,
                localThrowPathsUnavailableReason),
            projectedIdentities);
    }

    static void ValidateFindingEvidence(
        AnnotatedSourceDocument document,
        IReadOnlyList<BrowserSourceFactInstance> sourceFactInstances,
        IReadOnlyList<BrowserAnnotatedSourceFindingEvidenceDocument>?
            findingEvidenceDocuments,
        IReadOnlyList<BrowserAnnotatedSourceFindingEvidence>? findingEvidence)
    {
        if (findingEvidence is null)
        {
            if (findingEvidenceDocuments is { Count: > 0 })
            {
                throw new InvalidOperationException(
                    "Member Finding census carries callee documents without evidence rows.");
            }
            return;
        }

        Dictionary<int, AnnotatedSourceDocument> evidenceDocuments =
            ValidateFindingEvidenceDocuments(findingEvidenceDocuments);

        Dictionary<int, int> instanceKeyByFactId =
            sourceFactInstances.ToDictionary(
                identity => identity.FactId,
                identity => identity.InstanceKey);
        HashSet<int> eligibleFactIds =
        [
            .. document.Facts
                .Where(fact =>
                    fact.Origin == AnnotatedSourceFactOrigin.Body
                    && fact.Descriptor is
                        "cost.callee"
                        or "semantics.callee"
                        or "safety.callee")
                .Select(fact => fact.Id),
        ];
        var evidenceFactIds = new HashSet<int>();
        var evidenceKeys = new HashSet<int>();
        var referencedDocumentIds = new HashSet<int>();
        for (int index = 0; index < findingEvidence.Count; index++)
        {
            BrowserAnnotatedSourceFindingEvidence evidence =
                findingEvidence[index]
                    ?? throw new InvalidOperationException(
                        $"Member Finding census evidence row {index} is null.");
            if (!eligibleFactIds.Contains(evidence.FactId)
                || !instanceKeyByFactId.TryGetValue(
                    evidence.FactId,
                    out int expectedKey)
                || expectedKey != evidence.InstanceKey
                || !evidenceFactIds.Add(evidence.FactId)
                || !evidenceKeys.Add(evidence.InstanceKey))
            {
                throw new InvalidOperationException(
                    $"Member Finding census evidence row {index} carries an invalid or duplicate fact identity.");
            }
            if (string.IsNullOrWhiteSpace(evidence.Member)
                || evidence.Target is not { } target
                || string.IsNullOrWhiteSpace(target.Assembly)
                || string.IsNullOrWhiteSpace(target.TypeFullName)
                || string.IsNullOrWhiteSpace(target.TypeDefinitionId)
                || string.IsNullOrWhiteSpace(target.MemberName)
                || string.IsNullOrWhiteSpace(target.ReturnType)
                || string.IsNullOrWhiteSpace(target.SelectorKey)
                || target.ParameterTypes is null
                || target.GenericArity < 0
                || target.MetadataToken is not int token
                || (token & 0xFF000000) != 0x06000000
                || !string.Equals(
                    target.Kind,
                    "method",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Member Finding census evidence row {index} carries no callee member target.");
            }
            if (evidence.Coordinates is null
                || evidence.AggregateInputs is null
                || evidence.NodeIds is null
                || evidence.Coordinates.Any(coordinate =>
                    coordinate is null || coordinate.IlOffset < 0)
                || evidence.NodeIds.Any(nodeId => nodeId < 0)
                || evidence.NodeIds.Distinct().Count()
                    != evidence.NodeIds.Length)
            {
                throw new InvalidOperationException(
                    $"Member Finding census evidence row {index} carries invalid coordinates or node ids.");
            }

            bool methodEvidence = document.Facts[evidence.FactId].Descriptor
                == "cost.callee";
            bool unavailable =
                !string.IsNullOrWhiteSpace(evidence.UnavailableReason);
            AnnotatedSourceDocument? evidenceDocument = null;
            if (evidence.DocumentId is int referencedDocumentId)
            {
                if (!evidenceDocuments.TryGetValue(
                    referencedDocumentId,
                    out evidenceDocument))
                {
                    throw new InvalidOperationException(
                        $"Member Finding census evidence row {index} names no callee document.");
                }
                referencedDocumentIds.Add(referencedDocumentId);
            }
            if (methodEvidence)
            {
                ValidateMethodFindingEvidence(
                    evidence,
                    evidenceDocument,
                    index);
            }
            else
            {
                ValidateInstructionFindingEvidence(
                    evidence,
                    evidenceDocument,
                    unavailable,
                    index);
            }
        }

        if (!eligibleFactIds.SetEquals(evidenceFactIds))
        {
            throw new InvalidOperationException(
                "Member Finding census evidence does not cover every callee Finding.");
        }
        if (!evidenceDocuments.Keys.ToHashSet().SetEquals(referencedDocumentIds))
        {
            throw new InvalidOperationException(
                "Member Finding census carries an unreferenced callee document.");
        }
    }

    static void ValidateMethodFindingEvidence(
        BrowserAnnotatedSourceFindingEvidence evidence,
        AnnotatedSourceDocument? evidenceDocument,
        int index)
    {
        if (evidence.State != BrowserCalleeEvidenceState.Method
            || evidence.Coordinates.Length != 0
            || evidenceDocument is not null
            || evidence.DocumentId is not null
            || evidence.NodeIds.Length != 0
            || evidence.UnavailableReason is not null
            || evidence.AggregateInputs.Length == 0)
        {
            throw new InvalidOperationException(
                $"Method-level member Finding evidence row {index} carries an instruction projection or incomplete aggregate inputs.");
        }

        int previousKind = -1;
        foreach (BrowserCostCalleeEvidenceInput input in evidence.AggregateInputs)
        {
            if (input is null
                || !Enum.IsDefined(input.Kind)
                || (int)input.Kind <= previousKind)
            {
                throw new InvalidOperationException(
                    $"Method-level member Finding evidence row {index} carries invalid or unordered aggregate inputs.");
            }
            previousKind = (int)input.Kind;

            bool counted = input.Kind is
                BrowserCostCalleeEvidenceInputKind.Reflection
                or BrowserCostCalleeEvidenceInputKind.RootReach
                or BrowserCostCalleeEvidenceInputKind.DirectCallers
                or BrowserCostCalleeEvidenceInputKind.LoopCalls;
            if (counted != (input.Value is int value && value > 0))
            {
                throw new InvalidOperationException(
                    $"Method-level member Finding evidence row {index} carries an invalid aggregate input value.");
            }
        }
    }

    static void ValidateInstructionFindingEvidence(
        BrowserAnnotatedSourceFindingEvidence evidence,
        AnnotatedSourceDocument? evidenceDocument,
        bool unavailable,
        int index)
    {
        if (evidence.State == BrowserCalleeEvidenceState.Method
            || !Enum.IsDefined(evidence.State)
            || evidence.AggregateInputs.Length != 0
            || (evidence.State == BrowserCalleeEvidenceState.Instruction
                && evidence.Coordinates.Length == 0)
            || (evidence.State
                    == BrowserCalleeEvidenceState.InstructionUnavailable
                && (evidence.Coordinates.Length != 0 || !unavailable)))
        {
            throw new InvalidOperationException(
                $"Instruction-level member Finding evidence row {index} carries an invalid evidence state.");
        }

        if (unavailable)
        {
            if (evidence.NodeIds.Length != 0)
            {
                throw new InvalidOperationException(
                    $"Unavailable member Finding evidence row {index} cannot carry node ids.");
            }
            if (evidenceDocument is not null
                && evidence.Coordinates.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Unavailable member Finding evidence row {index} cannot carry a document without coordinates.");
            }
            if (evidenceDocument is not null
                && FindEvidenceNodeIds(
                    evidenceDocument,
                    evidence,
                    index,
                    out _) is not null)
            {
                throw new InvalidOperationException(
                    $"Member Finding census evidence row {index} is unavailable despite exact serialized correspondence.");
            }
        }
        else if (evidenceDocument is null
            || evidence.Coordinates.Length == 0
            || evidence.NodeIds.Length == 0)
        {
            throw new InvalidOperationException(
                $"Available member Finding evidence row {index} requires a document, coordinates, and node ids.");
        }
        else
        {
            int[] expectedNodeIds =
                FindEvidenceNodeIds(
                    evidenceDocument,
                    evidence,
                    index,
                    out string? failure)
                    ?? throw new InvalidOperationException(failure);
            if (!evidence.NodeIds.SequenceEqual(expectedNodeIds))
            {
                throw new InvalidOperationException(
                    $"Member Finding census evidence row {index} node ids "
                        + "do not equal its exact coordinate matches.");
            }
        }
    }

    static Dictionary<int, AnnotatedSourceDocument>
        ValidateFindingEvidenceDocuments(
            IReadOnlyList<BrowserAnnotatedSourceFindingEvidenceDocument>?
                findingEvidenceDocuments)
    {
        var result = new Dictionary<int, AnnotatedSourceDocument>();
        if (findingEvidenceDocuments is null)
            return result;

        for (int index = 0; index < findingEvidenceDocuments.Count; index++)
        {
            BrowserAnnotatedSourceFindingEvidenceDocument entry =
                findingEvidenceDocuments[index]
                    ?? throw new InvalidOperationException(
                        $"Member Finding census callee document {index} is null.");
            if (entry.Id < 0 || result.ContainsKey(entry.Id))
            {
                throw new InvalidOperationException(
                    $"Member Finding census callee document {index} has an invalid or duplicate id.");
            }
            AnnotatedSourceDocument document =
                entry.Document.Deserialize(
                    AnnotatedSourceDocumentCompactJsonContext.Default
                        .AnnotatedSourceDocument)
                ?? throw new InvalidOperationException(
                    $"Member Finding census callee document {index} carries no document.");
            result.Add(entry.Id, document);
        }
        return result;
    }

    static int[]? FindEvidenceNodeIds(
        AnnotatedSourceDocument document,
        BrowserAnnotatedSourceFindingEvidence evidence,
        int evidenceIndex,
        out string? failure)
    {
        var matchedNodeIds = new List<int>();
        foreach (
            BrowserAnnotatedSourceFindingEvidenceCoordinate coordinate
            in evidence.Coordinates)
        {
            string expectedKind = coordinate.Kind switch
            {
                BrowserCalleeEvidenceKind.ExceptionConstruction =>
                    "ObjectCreationExpression",
                BrowserCalleeEvidenceKind.Localloc =>
                    "StackAllocationExpression",
                BrowserCalleeEvidenceKind.Calli =>
                    "IndirectInvocationExpression",
                _ => throw new InvalidOperationException(
                    $"Member Finding census evidence row {evidenceIndex} carries an unknown evidence kind."),
            };
            AnnotatedSourceNode[] matches =
            [
                .. document.Nodes.Where(node =>
                    node.Medium == SourceLineKind.CSharp
                    && string.Equals(
                        node.Kind,
                        expectedKind,
                        StringComparison.Ordinal)
                    && node.Provenance?.IlOffsets.Contains(
                        coordinate.IlOffset) == true),
            ];
            if (matches.Length != 1)
            {
                failure =
                    $"Member Finding census evidence row {evidenceIndex} coordinate "
                        + $"IL_{coordinate.IlOffset:X4} matches {matches.Length} "
                        + $"{expectedKind} nodes.";
                return null;
            }
            matchedNodeIds.Add(matches[0].Id);
        }

        failure = null;
        return
        [
            .. matchedNodeIds.Distinct().Order(),
        ];
    }
}

/// <summary>
/// One call-graph target reached from an annotated-source invocation node. The identity is
/// produced by the product's call-graph projection and carried verbatim; this facade owns only
/// the transport record.
/// </summary>
public sealed record BrowserCallGraphTarget(
    string Id,
    string Assembly,
    string? AssemblyVersion,
    string? AssemblyCulture,
    string? AssemblyPublicKeyToken,
    string TypeFullName,
    string? TypeMetadataId,
    string? TypeDefinitionId,
    string MemberName,
    string[] ParameterTypes,
    string ReturnType,
    int GenericArity,
    int? MetadataToken,
    string SelectorKey,
    string Kind,
    string? PlatformPack,
    string? SurfaceAssemblyId);

/// <summary>
/// The annotated-source envelope: the product's portable <c>AnnotatedSourceDocument</c> serialized
/// by its owning <c>AnnotatedSourceDocumentJsonContext</c>, the product-issued viewer catalog, and
/// the provenance of the artifact it was raised from. The document travels as a
/// <see cref="JsonElement"/> so the wire shape stays exactly the one the viewer's model validates —
/// the host neither reshapes nor renames a field.
/// </summary>
/// <param name="ContextLimitation">
/// Set when the projection's whole-assembly fact context was narrower than a complete one, so a
/// short fact list is never mistaken for an honest absence of facts.
/// </param>
public sealed record BrowserAnnotatedSource
{
    private readonly BrowserAnnotatedSourceFindingEvidenceDocument[]
        _findingEvidenceDocuments;
    private readonly BrowserAnnotatedSourceFindingEvidence[]
        _findingEvidence;
    private readonly BrowserAnnotatedSourceCallRelationship[]
        _callRelationships;

    private BrowserAnnotatedSource(
        JsonElement Document,
        InertString Signature,
        BrowserAnnotatedSourceViewerCatalog ViewerCatalog,
        InertString Provenance,
        string? ContextLimitation,
        BrowserAnnotatedSourceFindingEvidenceDocument[]
            FindingEvidenceDocuments,
        BrowserAnnotatedSourceFindingEvidence[] FindingEvidence,
        BrowserAnnotatedSourceCallRelationship[] CallRelationships)
    {
        this.Document = Document;
        this.Signature = Signature;
        this.ViewerCatalog = ViewerCatalog;
        this.Provenance = Provenance;
        this.ContextLimitation = ContextLimitation;
        _findingEvidenceDocuments = [.. FindingEvidenceDocuments];
        _findingEvidence = [.. FindingEvidence];
        _callRelationships = [.. CallRelationships];
    }

    public JsonElement Document { get; }
    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString Signature { get; }
    public BrowserAnnotatedSourceViewerCatalog ViewerCatalog { get; }
    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString Provenance { get; }
    public string? ContextLimitation { get; }
    public BrowserAnnotatedSourceFindingEvidenceDocument[]
        FindingEvidenceDocuments => [.. _findingEvidenceDocuments];
    public BrowserAnnotatedSourceFindingEvidence[] FindingEvidence =>
        [.. _findingEvidence];
    public BrowserAnnotatedSourceCallRelationship[] CallRelationships =>
        [.. _callRelationships];

    internal static BrowserAnnotatedSource Create(
        AnnotatedSourceDocument document,
        InertString signature,
        InertString provenance,
        string? contextLimitation,
        BrowserAnnotatedSourceInvocationDestination[]?
            invocationDestinations = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            destinationUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceFindingEvidenceDocument[]?
            findingEvidenceDocuments = null,
        BrowserAnnotatedSourceFindingEvidence[]?
            findingEvidence = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            findingEvidenceUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceCallRelationship[]?
            callRelationships = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            callRelationshipsUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceCallCycleInspection? callCycles = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            callCyclesUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceSynchronousCompletion[]?
            synchronousCompletions = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            synchronousCompletionsUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceAwaitCompletionPath[]?
            awaitCompletionPaths = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            awaitCompletionPathsUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceAllocationExceptionPath[]?
            allocationExceptionPaths = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            allocationExceptionPathsUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceLocalThrowPathInspection?
            localThrowPaths = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            localThrowPathsUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            signature.ToString(),
            nameof(signature));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            provenance.ToString(),
            nameof(provenance));

        JsonElement serialized = SerializeDocument(document)!.Value;
        return new BrowserAnnotatedSource(
            serialized,
            signature,
            BrowserAnnotatedSourceViewerCatalogFactory.Create(
                document,
                invocationDestinations,
                destinationUnavailableReason,
                findingEvidence,
                findingEvidenceUnavailableReason,
                callRelationships,
                callRelationshipsUnavailableReason,
                callCycles,
                callCyclesUnavailableReason,
                synchronousCompletions,
                synchronousCompletionsUnavailableReason,
                awaitCompletionPaths,
                awaitCompletionPathsUnavailableReason,
                allocationExceptionPaths,
                allocationExceptionPathsUnavailableReason,
                localThrowPaths,
                localThrowPathsUnavailableReason),
            provenance,
            contextLimitation,
            findingEvidenceDocuments ?? [],
            findingEvidence ?? [],
            callRelationships ?? []);
    }

    internal static JsonElement? SerializeDocument(
        AnnotatedSourceDocument? document)
    {
        if (document is null)
            return null;
        using JsonDocument serialized = JsonDocument.Parse(
            JsonSerializer.Serialize(
                document,
                AnnotatedSourceDocumentCompactJsonContext.Default.AnnotatedSourceDocument));
        return serialized.RootElement.Clone();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserSource))]
[JsonSerializable(typeof(BrowserMemberSource))]
[JsonSerializable(typeof(BrowserTypeSourceResult))]
[JsonSerializable(typeof(BrowserTypeSourceEvidenceResult))]
[JsonSerializable(typeof(BrowserTypeExplorerResult))]
[JsonSerializable(typeof(BrowserTypeExplorerRequest))]
[JsonSerializable(typeof(BrowserTypeCodeView.ApiDeclarations))]
[JsonSerializable(typeof(BrowserTypeSourceCancellation))]
[JsonSerializable(typeof(BrowserMethodBodyTargetsResult))]
[JsonSerializable(typeof(BrowserMethodBodyComparisonResult))]
[JsonSerializable(typeof(BrowserMethodBodyComparisonRequest))]
[JsonSerializable(typeof(BrowserSourceComparisonRequest))]
[JsonSerializable(typeof(BrowserSourceComparisonResult))]
[JsonSerializable(typeof(BrowserAnnotatedSource))]
[JsonSerializable(typeof(BrowserMemberFindingCensus))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class BrowserSourceJsonContext : JsonSerializerContext;
