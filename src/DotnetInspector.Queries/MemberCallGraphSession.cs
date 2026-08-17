using System.Collections.Immutable;

using Analysis = ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Metadata;
using DotnetInspector.Services;

namespace DotnetInspector.Queries;

/// <summary>
/// The cumulative work tiers of a progressive member call graph.
/// </summary>
public enum CallGraphTier
{
    Callees,
    Callers,
    CrossLibrary,
}

/// <summary>
/// One assembly that could not contribute call-graph evidence.
/// </summary>
public abstract record MemberCallGraphAcquisitionFailure(
    ResolvedAssemblyReference Assembly)
{
    /// <summary>The workspace rejected the participant image.</summary>
    public sealed record Rejected(
        ResolvedAssemblyReference Assembly,
        CandidateOpenFailure Failure)
        : MemberCallGraphAcquisitionFailure(Assembly);

    /// <summary>The acquired image could not be decoded for body analysis.</summary>
    public sealed record InvalidImage(
        ResolvedAssemblyReference Assembly,
        Exception Error)
        : MemberCallGraphAcquisitionFailure(Assembly);
}

/// <summary>
/// A required call-graph participant could not produce body evidence.
/// </summary>
public sealed class MemberCallGraphAcquisitionException
    : InvalidOperationException
{
    public MemberCallGraphAcquisitionException(
        IEnumerable<MemberCallGraphAcquisitionFailure> failures)
        : base("One or more call-graph participants could not be acquired.")
    {
        ArgumentNullException.ThrowIfNull(failures);
        Failures = [.. failures];
        if (Failures.IsEmpty)
        {
            throw new ArgumentException(
                "At least one acquisition failure is required.",
                nameof(failures));
        }
    }

    public ImmutableArray<MemberCallGraphAcquisitionFailure> Failures
    {
        get;
    }
}

/// <summary>
/// Materialized call-graph roots and the evidence state used to produce them.
/// </summary>
public sealed record MemberCallGraphView(
    CallGraphTier Tier,
    Analysis.CallTreeNode? CalleeRoot,
    Analysis.CallTreeNode? CallerRoot)
{
    /// <summary>The selected member's module identity.</summary>
    public Guid FocusModuleVersionId { get; init; }

    /// <summary>The selected member's MethodDef token.</summary>
    public int FocusMethodToken { get; init; }

    /// <summary>
    /// Every physical call site originating in the selected member, retained
    /// from the same index that produced the graph roots. The annotated-member
    /// integration test gates this reuse boundary.
    /// </summary>
    public ImmutableArray<Analysis.DirectCall> FocusCallSites { get; init; } =
        [];

    /// <summary>
    /// Compact ownership evidence retained from the same body indexes that
    /// produced this graph layer.
    /// </summary>
    public ImmutableArray<Analysis.ArrayPoolOwnershipMethodEvidence>
        OwnershipEvidence { get; init; } = [];

    /// <summary>Whether ownership-flow production was requested.</summary>
    public bool OwnershipFlowAvailable { get; init; }

    public Analysis.CatalogCallGraphDiagnostics Diagnostics { get; init; } =
        Analysis.CatalogCallGraphDiagnostics.Empty;
}

/// <summary>
/// Options for one progressively acquired member call graph.
/// </summary>
public sealed record MemberCallGraphOptions
{
    public int Depth { get; init; } = 3;
    public int MaxNodes { get; init; } = 25;
    public Analysis.LibraryBodyAnalysisFeatures Features { get; init; } =
        Analysis.LibraryBodyAnalysisFeatures.MethodEvidence
        | Analysis.LibraryBodyAnalysisFeatures.Allocations;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Depth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxNodes, 1);
        if ((Features & ~Analysis.LibraryBodyAnalysisFeatures.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(Features));
        if (Features == Analysis.LibraryBodyAnalysisFeatures.None)
        {
            throw new ArgumentException(
                "Progressive call graphs require method evidence.");
        }
        if ((Features & Analysis.LibraryBodyAnalysisFeatures.LeakTriage) != 0)
        {
            throw new ArgumentException(
                "Progressive call graphs do not support Leak Triage because their first tier is body-scoped.");
        }
    }
}

/// <summary>
/// Bounds one outgoing call neighborhood over the session's workspace group.
/// </summary>
public sealed record MemberCallGraphCalleeNeighborhoodRequest
{
    public MemberCallGraphCalleeNeighborhoodRequest(
        int maxDepth,
        int maxNodes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxNodes, 1);
        MaxDepth = maxDepth;
        MaxNodes = maxNodes;
    }

    public int MaxDepth { get; }
    public int MaxNodes { get; }
}

/// <summary>
/// Acquires and retains the minimum cumulative Analysis state required for
/// progressively richer member call graphs.
/// </summary>
/// <remarks>
/// The first tier builds at most one body-scoped target index. A later tier
/// builds at most one full target index and one full index per distinct
/// cross-library image. Calling a lower tier after a full tier reuses the full
/// index. One catalog generation and one physical graph serve both traversal
/// directions. Projection never performs acquisition or another graph walk.
///
/// The owning <see cref="AssemblyContextGroup"/> disposes this session's graph
/// and catalog before releasing retained image snapshots. The type is intended
/// for one logical consumer and does not support concurrent calls.
/// <c>Tiers_ShareSnapshotsAndBuildEachIndexAtMostOnce</c>,
/// <c>DirectFullTier_SkipsScopedAndLaterCalleesReusesFull</c>,
/// <c>DuplicateImages_BuildOneCrossLibraryIndex</c>, and
/// <c>WorkspaceDisposal_DisposesOwnedGraphBeforeSnapshots</c> gate these
/// acquisition, reuse, and lifetime properties.
/// </remarks>
public sealed class MemberCallGraphSession : IDisposable
{
    readonly AssemblyContextGroup _group;
    readonly AssemblyContextParticipant _root;
    readonly int _memberToken;
    readonly MemberCallGraphOptions _options;
    readonly Dictionary<AssemblyAcquisitionRegistration, IndexBuildResult>
        _crossIndexes = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<AssemblyImageIdentity, IndexBuildResult.Available>
        _fullIndexesByImage = [];
    IndexBuildResult? _scopedRoot;
    IndexBuildResult? _fullRoot;
    Analysis.CatalogCallGraphScope? _catalogScope;
    ImmutableArray<MemberCallGraphAcquisitionFailure>
        _crossLibraryFailures;
    bool _crossLibraryBuilt;
    bool _disposed;
    int _scopedTargetIndexBuilds;
    int _fullTargetIndexBuilds;
    int _crossLibraryIndexBuilds;

    public MemberCallGraphSession(
        AssemblyContextGroup group,
        ResolvedAssemblyReference rootAssembly,
        int memberToken,
        MemberCallGraphOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(rootAssembly);
        options ??= new MemberCallGraphOptions();
        options.Validate();

        _root = group.Participants.SingleOrDefault(
                participant => ReferenceEquals(
                    participant.Assembly.Registration,
                    rootAssembly.Registration))
            ?? throw new ArgumentException(
                "The call-graph root must be a participant in the assembly context group.",
                nameof(rootAssembly));
        _group = group;
        _memberToken = memberToken;
        _options = options;
        _group.RegisterOwnedResource(this);
    }

    public bool HasCrossLibraryScope => _group.Participants.Length > 1;

    /// <summary>
    /// Builds only the selected body unless a full target index already exists.
    /// </summary>
    public MemberCallGraphView Callees() =>
        Execute(CalleesCore);

    /// <summary>
    /// Builds the full target index once and reuses it for both directions.
    /// </summary>
    public MemberCallGraphView Callers() =>
        Execute(CallersCore);

    /// <summary>
    /// Builds each distinct participant image once and uses one shared catalog
    /// graph for caller and callee traversal.
    /// </summary>
    public MemberCallGraphView CrossLibrary() =>
        Execute(CrossLibraryCore);

    /// <summary>
    /// Projects one bounded outgoing call neighborhood through every acquired
    /// participant in this session's assembly context group.
    /// </summary>
    public InspectionGraphDocument CrossLibraryCalleeNeighborhood(
        MemberCallGraphCalleeNeighborhoodRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Execute(() =>
            CrossLibraryCalleeNeighborhoodCore(request));
    }

    /// <summary>Lazily yields each cumulative graph tier in order.</summary>
    public IEnumerable<MemberCallGraphView> Tiers()
    {
        yield return Callees();
        yield return Callers();
        if (HasCrossLibraryScope)
            yield return CrossLibrary();
    }

    public event EventHandler<MemberCallGraphView>? LayerReady;

    public event EventHandler? Completed;

    public Task RunAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                LayerReady?.Invoke(this, Callees());

                cancellationToken.ThrowIfCancellationRequested();
                LayerReady?.Invoke(this, Callers());

                if (HasCrossLibraryScope)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    LayerReady?.Invoke(this, CrossLibrary());
                }

                cancellationToken.ThrowIfCancellationRequested();
                Completed?.Invoke(this, EventArgs.Empty);
            },
            cancellationToken);

    MemberCallGraphView CalleesCore()
    {
        if (_fullRoot is not null)
        {
            IndexBuildResult.Available full = Require(_fullRoot);
            return View(
                CallGraphTier.Callees,
                full,
                full.Index.BuildCallTree(
                    _memberToken,
                    _options.Depth,
                    _options.MaxNodes),
                callerRoot: null);
        }

        IndexBuildResult.Available scoped = Require(
            _scopedRoot ??= BuildIndex(
                _root,
                bodyScope: new HashSet<int> { _memberToken },
                retainAssembly: false,
                IndexBuildKind.ScopedTarget));
        Analysis.CallTreeNode root = scoped.Index.BuildCallTree(
            _memberToken,
            maxDepth: 1,
            maxNodes: _options.MaxNodes);
        return View(
            CallGraphTier.Callees,
            scoped,
            MarkImmediateCalleesBounded(root),
            callerRoot: null);
    }

    static Analysis.CallTreeNode MarkImmediateCalleesBounded(
        Analysis.CallTreeNode root)
    {
        if (root.Children.IsDefaultOrEmpty)
            return root;

        ImmutableArray<Analysis.CallTreeNode> children = root.Children
            .Select(
                child => child.Status == Analysis.CallTreeStatus.Leaf
                    ? child with
                    {
                        Status = Analysis.CallTreeStatus.DepthLimited,
                    }
                    : child)
            .ToImmutableArray();
        return root with { Children = children };
    }

    MemberCallGraphView CallersCore()
    {
        IndexBuildResult.Available root = Require(GetFullRoot());
        return View(
            CallGraphTier.Callers,
            root,
            root.Index.BuildCallTree(
                _memberToken,
                _options.Depth,
                _options.MaxNodes),
            root.Index.BuildCallerTree(
                _memberToken,
                _options.Depth,
                _options.MaxNodes));
    }

    MemberCallGraphView CrossLibraryCore()
    {
        IndexBuildResult.Available root = Require(GetFullRoot());
        EnsureCrossLibraryScope();
        ThrowIfCrossLibraryFailed();
        return View(
            CallGraphTier.CrossLibrary,
            root,
            root.Index.BuildCallTree(
                _memberToken,
                _catalogScope!,
                _options.Depth,
                _options.MaxNodes),
            root.Index.BuildCallerTree(
                _memberToken,
                _catalogScope!,
                _options.Depth,
                _options.MaxNodes),
            _catalogScope!.Diagnostics);
    }

    InspectionGraphDocument CrossLibraryCalleeNeighborhoodCore(
        MemberCallGraphCalleeNeighborhoodRequest request)
    {
        IndexBuildResult.Available root = Require(GetFullRoot());
        EnsureCrossLibraryScope();
        ThrowIfCrossLibraryFailed();
        Analysis.CallTreeNode calleeRoot =
            root.Index.BuildCallTree(
                _memberToken,
                _catalogScope!,
                request.MaxDepth,
                request.MaxNodes);
        return CallGraphInspectionGraphAdapter
            .CreateOutgoingNeighborhood(
                CallGraphProjection.FromCallees(calleeRoot),
                request.MaxDepth,
                request.MaxNodes,
                _catalogScope!.Diagnostics);
    }

    MemberCallGraphView View(
        CallGraphTier tier,
        IndexBuildResult.Available source,
        Analysis.CallTreeNode? calleeRoot,
        Analysis.CallTreeNode? callerRoot,
        Analysis.CatalogCallGraphDiagnostics? diagnostics = null)
    {
        Analysis.LibraryBodyIndex index = source.Index;
        IEnumerable<IndexBuildResult.Available> evidenceSources =
            tier == CallGraphTier.CrossLibrary
                ? _fullIndexesByImage.Values
                : [source];
        return new(tier, calleeRoot, callerRoot)
        {
            FocusModuleVersionId =
                source.ImageIdentity.ModuleVersionId,
            FocusMethodToken = _memberToken,
            FocusCallSites =
            [
                .. index.DirectCalls
                    .Where(call =>
                        call.Caller.MetadataToken == _memberToken)
                    .OrderBy(call => call.ILOffset)
                    .ThenBy(call => call.OperandToken),
            ],
            OwnershipEvidence =
            [
                .. evidenceSources
                    .SelectMany(item =>
                        item.Index.ArrayPoolOwnership),
            ],
            OwnershipFlowAvailable =
                (_options.Features
                    & Analysis.LibraryBodyAnalysisFeatures.OwnershipFlow)
                != 0,
            Diagnostics =
                diagnostics
                ?? Analysis.CatalogCallGraphDiagnostics.Empty,
        };
    }

    void EnsureCrossLibraryScope()
    {
        if (_crossLibraryBuilt)
            return;

        var failures =
            ImmutableArray.CreateBuilder<
                MemberCallGraphAcquisitionFailure>();
        foreach (AssemblyContextParticipant participant
            in _group.Participants)
        {
            if (ReferenceEquals(
                    participant.Assembly.Registration,
                    _root.Assembly.Registration))
            {
                continue;
            }

            if (!_crossIndexes.TryGetValue(
                    participant.Assembly.Registration,
                    out IndexBuildResult? result))
            {
                result = BuildIndex(
                    participant,
                    bodyScope: null,
                    retainAssembly: true,
                    IndexBuildKind.CrossLibrary);
                _crossIndexes.Add(
                    participant.Assembly.Registration,
                    result);
            }

            if (result is IndexBuildResult.Unavailable unavailable)
                failures.Add(unavailable.Failure);
        }

        _crossLibraryFailures = failures.ToImmutable();
        _crossLibraryBuilt = true;
        if (!_crossLibraryFailures.IsEmpty)
            return;

        IndexBuildResult.Available[] available =
            _fullIndexesByImage.Values.ToArray();
        var policy = new SourceRelativeAssemblyGroupBindingPolicy(
            available.Select(item => (
                item.Assembly,
                item.Participant.BindingPolicy)));
        _catalogScope = new Analysis.CatalogCallGraphScope(
            policy,
            available.Select(
                item => new Analysis.CatalogCallGraphParticipant(
                    item.Index,
                    item.Assembly)));
    }

    IndexBuildResult GetFullRoot()
    {
        if (_fullRoot is not null)
            return _fullRoot;

        _fullRoot = BuildIndex(
            _root,
            bodyScope: null,
            retainAssembly: true,
            IndexBuildKind.FullTarget);
        if (_fullRoot is IndexBuildResult.Available)
            _scopedRoot = null;
        return _fullRoot;
    }

    internal static bool IsInvalidImageException(Exception exception) =>
        exception is BadImageFormatException
            or ArgumentOutOfRangeException
            or OverflowException;

    IndexBuildResult BuildIndex(
        AssemblyContextParticipant participant,
        IReadOnlySet<int>? bodyScope,
        bool retainAssembly,
        IndexBuildKind buildKind)
    {
        AssemblyImageAccessResult<IndexBuildResult> access =
            _group.UseSnapshot<IndexBuildResult>(
                participant.Assembly,
                snapshot =>
                {
                    var imageIdentity = new AssemblyImageIdentity(
                        snapshot.Identity,
                        snapshot.ModuleVersionId);
                    if (bodyScope is null
                        && _fullIndexesByImage.TryGetValue(
                            imageIdentity,
                            out IndexBuildResult.Available? existing))
                    {
                        return existing;
                    }

                    IncrementBuildCount(buildKind);
                    try
                    {
                        Analysis.LibraryBodyIndex index =
                            Analysis.LibraryBodyIndex
                                .OpenFromPrefetchedImage(
                                    ParticipantName(participant),
                                    snapshot.Content,
                                    _options.Features,
                                    resolver: null,
                                    bodyScope);
                        ResolvedAssemblyReference assembly =
                            retainAssembly
                                ? snapshot.RetainAssemblyReference(
                                    participant.Assembly)
                                : participant.Assembly;
                        var available =
                            new IndexBuildResult.Available(
                                participant,
                                assembly,
                                imageIdentity,
                                index);
                        if (bodyScope is null)
                        {
                            _fullIndexesByImage.Add(
                                imageIdentity,
                                available);
                        }
                        return available;
                    }
                    catch (Exception ex)
                        when (IsInvalidImageException(ex))
                    {
                        return new IndexBuildResult.Unavailable(
                            new MemberCallGraphAcquisitionFailure
                                .InvalidImage(
                                    participant.Assembly,
                                    ex));
                    }
                });

        return access switch
        {
            AssemblyImageAccessResult<IndexBuildResult>.Available
                available => available.Value,
            AssemblyImageAccessResult<IndexBuildResult>.Rejected
                rejected => new IndexBuildResult.Unavailable(
                    new MemberCallGraphAcquisitionFailure.Rejected(
                        rejected.Assembly,
                        rejected.Failure)),
            _ => throw new InvalidOperationException(
                "Unknown assembly image access result."),
        };
    }

    static string ParticipantName(
        AssemblyContextParticipant participant) =>
        participant.Assembly.Path
        ?? participant.Assembly.Identity.Name;

    static IndexBuildResult.Available Require(
        IndexBuildResult result) =>
        result switch
        {
            IndexBuildResult.Available available => available,
            IndexBuildResult.Unavailable unavailable =>
                throw new MemberCallGraphAcquisitionException(
                    [unavailable.Failure]),
            _ => throw new InvalidOperationException(
                "Unknown call-graph index result."),
        };

    void ThrowIfCrossLibraryFailed()
    {
        if (!_crossLibraryFailures.IsEmpty)
        {
            throw new MemberCallGraphAcquisitionException(
                _crossLibraryFailures);
        }
    }

    TResult Execute<TResult>(
        Func<TResult> callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _group.UseContext(
            () =>
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return callback();
            });
    }

    void IncrementBuildCount(IndexBuildKind kind)
    {
        switch (kind)
        {
            case IndexBuildKind.ScopedTarget:
                _scopedTargetIndexBuilds++;
                break;
            case IndexBuildKind.FullTarget:
                _fullTargetIndexBuilds++;
                break;
            case IndexBuildKind.CrossLibrary:
                _crossLibraryIndexBuilds++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    internal MemberCallGraphBuildCounts BuildCounts =>
        new(
            _scopedTargetIndexBuilds,
            _fullTargetIndexBuilds,
            _crossLibraryIndexBuilds);

    internal Analysis.CatalogCallGraphScope? CatalogScope =>
        _catalogScope;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _catalogScope?.Dispose();
        _catalogScope = null;
        _crossIndexes.Clear();
        _fullIndexesByImage.Clear();
        _scopedRoot = null;
        _fullRoot = null;
        _group.UnregisterOwnedResource(this);
    }

    abstract record IndexBuildResult
    {
        internal sealed record Available(
            AssemblyContextParticipant Participant,
            ResolvedAssemblyReference Assembly,
            AssemblyImageIdentity ImageIdentity,
            Analysis.LibraryBodyIndex Index)
            : IndexBuildResult;

        internal sealed record Unavailable(
            MemberCallGraphAcquisitionFailure Failure)
            : IndexBuildResult;
    }

    readonly record struct AssemblyImageIdentity(
        AssemblyReferenceIdentity Identity,
        Guid ModuleVersionId);

    enum IndexBuildKind
    {
        ScopedTarget,
        FullTarget,
        CrossLibrary,
    }
}

internal readonly record struct MemberCallGraphBuildCounts(
    int ScopedTargetIndexes,
    int FullTargetIndexes,
    int CrossLibraryIndexes);
