using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Which progressive layer of a member call graph a <see cref="MemberCallGraphView"/> carries
/// (issue #3266). The layers are cumulative and ordered by acquisition cost: each unlocks more of
/// the graph by decoding more bodies. The value names the layer that was unlocked, not a direction
/// — at <c>depth &gt; 1</c> the <see cref="CrossLibrary"/> layer lets both a caller chain and a
/// callee chain cross a package boundary.
/// </summary>
public enum CallGraphTier
{
    /// <summary>
    /// Outbound (callee) half only, from a scoped single-body build that decodes just the selected
    /// member. The callee tree is bounded at depth 1 (the immediate callees) because no other body
    /// is decoded yet; callers are not available.
    /// </summary>
    Callees,

    /// <summary>
    /// Adds the inbound (caller) half and deepens the callee tree, from a full decode of the
    /// selected member's own assembly. Expansion is bounded at the assembly edge.
    /// </summary>
    Callers,

    /// <summary>
    /// Adds the in-scope caller/callee packages, so both the caller tree and the callee tree can
    /// cross a library boundary up to the configured depth.
    /// </summary>
    CrossLibrary,
}

/// <summary>
/// One progressive snapshot of a member call graph: the selected overload as both an outbound
/// (callee) and inbound (caller) tree root. Presentation-free — a host renders these roots with its
/// own per-section tree rendering, or projects them with
/// <c>ILInspector.CallGraph.CallGraphMermaid.Render(CallerRoot, CalleeRoot)</c> ("with or without
/// mermaid"). Either root may be null (there is no caller root in the first <see cref="CallGraphTier.Callees"/>
/// layer); both are the same selected member.
/// </summary>
public sealed record MemberCallGraphView(
    CallGraphTier Tier,
    Analysis.CallTreeNode? CalleeRoot,
    Analysis.CallTreeNode? CallerRoot);

/// <summary>
/// Progressive acquisition seam for a single member's call graph (issue #3266). Serves the graph in
/// three cumulative layers so a host can paint the cheap outbound half first and fill in the
/// expensive caller tiers as they land:
/// <list type="number">
///   <item><see cref="Callees"/> — a scoped single-body build (decode only the target body).</item>
///   <item><see cref="Callers"/> — a full decode of the target assembly (intra-library callers, deeper callees).</item>
///   <item><see cref="CrossLibrary"/> — decode the in-scope packages so both directions cross library boundaries.</item>
/// </list>
///
/// <para><b>No duplicated work.</b> At most two target-assembly indexes are ever built — the scoped
/// single-body build and the full build — plus one build per cross-library package, and each is
/// built at most once and then reused for callees, callers, and any Mermaid projection. The scoped
/// build exists only for the progressive first paint: a consumer that wants the whole graph calls
/// <see cref="Callers"/> or <see cref="CrossLibrary"/> directly and pays exactly one full build,
/// with callees derived for free from it (the scoped build is never made). Once the full build
/// lands it supersedes the scoped one, which is never rebuilt.</para>
///
/// <para>Drive it by pull — <see cref="Callees"/>/<see cref="Callers"/>/<see cref="CrossLibrary"/>,
/// or the lazy <see cref="Tiers"/> stream — or by push, via <see cref="RunAsync"/> raising
/// <see cref="LayerReady"/> per layer then <see cref="Completed"/>. The push path is a thin wrapper
/// over the same memoized pull core, so the two never double the work. A single instance assumes one
/// logical consumer; do not mix concurrent pull calls with <see cref="RunAsync"/>.</para>
/// </summary>
public sealed class ProgressiveMemberCallGraph
{
    readonly string _assemblyPath;
    readonly int _memberToken;
    readonly Func<string, IAssemblyReferenceResolver?> _resolverFactory;
    readonly IReadOnlyList<string> _crossLibraryAssemblies;
    readonly int _depth;
    readonly int _maxNodes;

    // These memoized session/scope fields are intentionally unsynchronized: the class contract is a
    // single logical consumer (see the type doc), so pull calls and RunAsync never touch them
    // concurrently and each index is still built at most once. If a future consumer needs concurrent
    // pull+push, switch these to Lazy<T> with ExecutionAndPublication.
    MethodBodyInspectionSession? _scopedSession;
    MethodBodyInspectionSession? _fullSession;
    List<Analysis.LibraryBodyIndex>? _crossScopes;

    ProgressiveMemberCallGraph(
        string assemblyPath,
        int memberToken,
        Func<string, IAssemblyReferenceResolver?> resolverFactory,
        IReadOnlyList<string> crossLibraryAssemblies,
        int depth,
        int maxNodes)
    {
        _assemblyPath = assemblyPath;
        _memberToken = memberToken;
        _resolverFactory = resolverFactory;
        _crossLibraryAssemblies = crossLibraryAssemblies;
        _depth = Math.Max(1, depth);
        _maxNodes = Math.Max(1, maxNodes);
    }

    /// <summary>
    /// Opens a progressive call graph rooted at <paramref name="memberToken"/> in
    /// <paramref name="assemblyPath"/>. <paramref name="resolverFactory"/> supplies a reference
    /// resolver per assembly path (the selected assembly and each cross-library scope);
    /// <paramref name="crossLibraryAssemblies"/> are the packages consulted for the
    /// <see cref="CrossLibrary"/> layer. <paramref name="depth"/> bounds the walk in both
    /// directions (levels of callers up, callees down) and <paramref name="maxNodes"/> caps each
    /// tree's node budget. No index is built until a layer is requested.
    /// </summary>
    public static ProgressiveMemberCallGraph Open(
        string assemblyPath,
        int memberToken,
        Func<string, IAssemblyReferenceResolver?> resolverFactory,
        IReadOnlyList<string>? crossLibraryAssemblies = null,
        int depth = 3,
        int maxNodes = 25)
    {
        ArgumentNullException.ThrowIfNull(assemblyPath);
        ArgumentNullException.ThrowIfNull(resolverFactory);
        return new ProgressiveMemberCallGraph(
            assemblyPath,
            memberToken,
            resolverFactory,
            crossLibraryAssemblies ?? [],
            depth,
            maxNodes);
    }

    /// <summary>True when cross-library packages were supplied, so a <see cref="CrossLibrary"/> layer exists.</summary>
    public bool HasCrossLibraryScope => _crossLibraryAssemblies.Count > 0;

    /// <summary>
    /// Layer 1: the outbound callee tree from a scoped single-body build. Bounded at depth 1 while
    /// only the scoped build exists; if the full build has already landed, the callee tree reaches
    /// the configured depth and no scoped build is made.
    /// </summary>
    public MemberCallGraphView Callees()
    {
        if (_fullSession is not null)
        {
            var full = _fullSession.BodyIndex.BuildCallTree(_memberToken, maxDepth: _depth, maxNodes: _maxNodes);
            return new MemberCallGraphView(CallGraphTier.Callees, full, CallerRoot: null);
        }

        // Scoped first paint: only the target body was decoded, so its immediate callees appear but
        // their own bodies are unknown. A single-body build reports an undecoded in-assembly callee
        // as a Leaf (no outbound edges found), which would falsely claim the callee is terminal;
        // restamp those depth-1 callees as DepthLimited (bounded/unknown) until a fuller tier
        // decodes them.
        var scopedRoot = ScopedOrFullIndex.BuildCallTree(_memberToken, maxDepth: 1, maxNodes: _maxNodes);
        return new MemberCallGraphView(CallGraphTier.Callees, MarkImmediateCalleesBounded(scopedRoot), CallerRoot: null);
    }

    // The scoped single-body build cannot see any callee's own body, so it reports every immediate
    // callee that resolves in-assembly as a Leaf. Restamp those as DepthLimited so the first paint
    // never asserts a callee is terminal; External and other boundary statuses are already correct.
    static Analysis.CallTreeNode MarkImmediateCalleesBounded(Analysis.CallTreeNode root)
    {
        if (root.Children.IsDefaultOrEmpty)
            return root;

        var children = root.Children
            .Select(child => child.Status == Analysis.CallTreeStatus.Leaf
                ? child with { Status = Analysis.CallTreeStatus.DepthLimited }
                : child)
            .ToImmutableArray();
        return root with { Children = children };
    }

    /// <summary>
    /// Layer 2: the full target-assembly build — the intra-library caller tree plus the callee tree
    /// deepened to the configured depth. Reuses the full build if one already exists.
    /// </summary>
    public MemberCallGraphView Callers()
    {
        var index = FullIndex;
        var calleeRoot = index.BuildCallTree(_memberToken, maxDepth: _depth, maxNodes: _maxNodes);
        var callerRoot = index.BuildCallerTree(_memberToken, maxDepth: _depth, maxNodes: _maxNodes);
        return new MemberCallGraphView(CallGraphTier.Callers, calleeRoot, callerRoot);
    }

    /// <summary>
    /// Layer 3: extends both directions across the in-scope packages, so a caller or callee chain
    /// can cross a library boundary. Reuses the full target-assembly build and each scope build.
    /// </summary>
    public MemberCallGraphView CrossLibrary()
    {
        var index = FullIndex;
        var scopes = CrossScopes;
        var calleeRoot = index.BuildCallTree(_memberToken, scopes, maxDepth: _depth, maxNodes: _maxNodes);
        var callerRoot = index.BuildCallerTree(_memberToken, scopes, maxDepth: _depth, maxNodes: _maxNodes);
        return new MemberCallGraphView(CallGraphTier.CrossLibrary, calleeRoot, callerRoot);
    }

    /// <summary>
    /// Lazily streams the layers in order: <see cref="Callees"/>, then <see cref="Callers"/>, then
    /// <see cref="CrossLibrary"/> when cross-library packages were supplied. A host renders or
    /// re-renders per yielded snapshot.
    /// </summary>
    public IEnumerable<MemberCallGraphView> Tiers()
    {
        yield return Callees();
        yield return Callers();
        if (HasCrossLibraryScope)
            yield return CrossLibrary();
    }

    /// <summary>Raised on each new layer as it becomes available, in <see cref="Tiers"/> order.</summary>
    public event EventHandler<MemberCallGraphView>? LayerReady;

    /// <summary>Raised once after the last layer has been delivered.</summary>
    public event EventHandler? Completed;

    /// <summary>
    /// Non-blocking push driver: walks <see cref="Tiers"/> off the calling thread, raising
    /// <see cref="LayerReady"/> for each layer and then <see cref="Completed"/>. Cancellation is
    /// observed between layers.
    /// </summary>
    public Task RunAsync(CancellationToken cancellationToken = default)
        => Task.Run(
            () =>
            {
                // Observe cancellation before requesting each tier, so a cancelling LayerReady
                // handler stops the next (more expensive) build from running.
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

    // The full target-assembly index, built at most once. Supersedes the scoped build for every
    // later query, so callees derived here reach the full depth without a second decode.
    Analysis.LibraryBodyIndex FullIndex
        => (_fullSession ??= MethodBodyInspectionSession.Open(
            _assemblyPath,
            _resolverFactory(_assemblyPath),
            includeAllocations: true,
            includeOpportunities: false,
            bodyScope: null)).BodyIndex;

    // The scoped single-body index for the first paint — unless a full build already exists, in
    // which case that is reused (the scoped build is never made once superseded).
    Analysis.LibraryBodyIndex ScopedOrFullIndex
    {
        get
        {
            if (_fullSession is not null)
                return _fullSession.BodyIndex;

            return (_scopedSession ??= MethodBodyInspectionSession.Open(
                _assemblyPath,
                _resolverFactory(_assemblyPath),
                includeAllocations: true,
                includeOpportunities: false,
                bodyScope: new HashSet<int> { _memberToken })).BodyIndex;
        }
    }

    IReadOnlyList<Analysis.LibraryBodyIndex> CrossScopes => _crossScopes ??= BuildCrossScopes();

    List<Analysis.LibraryBodyIndex> BuildCrossScopes()
    {
        var scopes = new List<Analysis.LibraryBodyIndex>();
        foreach (var scopePath in _crossLibraryAssemblies)
        {
            // Surface acquisition failures instead of swallowing them: an unreadable or missing
            // cross-library package must fail the request, not silently yield a success-shaped graph
            // that hides the boundary-crossing edges it should have contributed.
            scopes.Add(MethodBodyInspectionSession.Open(
                scopePath,
                _resolverFactory(scopePath),
                includeAllocations: true,
                includeOpportunities: false).BodyIndex);
        }

        return scopes;
    }
}
