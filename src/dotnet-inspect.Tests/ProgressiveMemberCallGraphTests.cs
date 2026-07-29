using DotnetInspector.Inspectors;
using DotnetInspector.Fixtures;
using ILInspector.CallGraph;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests the progressive member call-graph acquisition seam (issue #3266):
/// <see cref="ProgressiveMemberCallGraph"/> serving callees, then callers, then the cross-library
/// tier, with the no-duplicated-build invariant. Shares the non-parallel
/// <c>IndexBuildGuard</c> collection so the process-wide
/// <see cref="MethodBodyInspectionSession.OpenCountForTests"/> counter stays reliable.
/// </summary>
[Collection("IndexBuildGuard")]
public class ProgressiveMemberCallGraphTests
{
    static string CallerPath => FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
    static string TargetPath => FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();

    static readonly Func<string, IAssemblyReferenceResolver?> NullResolver = _ => null;

    static int MemberToken(string assemblyPath, string typeName, string methodName)
    {
        var index = Analysis.LibraryBodyIndex.Open(assemblyPath);
        return index.Methods.First(method =>
            method.DeclaringType.Name == typeName && method.Name == methodName).MetadataToken;
    }

    static string TargetAssemblyName()
        => Analysis.LibraryBodyIndex.Open(TargetPath).Methods.First().AssemblyName;

    static Analysis.CallTreeNode Child(Analysis.CallTreeNode node, string name)
        => node.Children.Single(child => child.Member.Name == name);

    // Layer 1 is the cheap first paint: a scoped single-body build that decodes only the selected
    // member, so exactly one session opens, no caller root exists, and the callee tree is depth 1
    // (the callee across the package boundary is still an untagged external leaf).
    [Fact]
    public void Callees_ScopedFirstPaint_BuildsScopedIndexOnly()
    {
        int run = MemberToken(CallerPath, "Entry", "Run");
        var graph = ProgressiveMemberCallGraph.Open(CallerPath, run, NullResolver, [TargetPath]);

        MethodBodyInspectionSession.OpenCountForTests = 0;
        var view = graph.Callees();

        Assert.Equal(1, MethodBodyInspectionSession.OpenCountForTests);
        Assert.Equal(CallGraphTier.Callees, view.Tier);
        Assert.Null(view.CallerRoot);
        Assert.NotNull(view.CalleeRoot);
        Assert.Equal("Run", view.CalleeRoot!.Member.Name);

        var ping = Child(view.CalleeRoot, "Ping");
        Assert.Equal(Analysis.CallTreeStatus.External, ping.Status);
        Assert.Empty(ping.Children);
    }

    // The scoped first paint decodes only the target body, so an in-assembly callee whose own body
    // is not yet decoded must read as bounded/unknown (DepthLimited), never as a proven Leaf that
    // would falsely claim the callee is terminal.
    [Fact]
    public void Callees_ScopedFirstPaint_MarksInAssemblyCalleeBounded()
    {
        int runOuter = MemberToken(CallerPath, "Entry", "RunOuter");
        var graph = ProgressiveMemberCallGraph.Open(CallerPath, runOuter, NullResolver, [TargetPath]);

        var view = graph.Callees();

        var run = Child(view.CalleeRoot!, "Run");
        Assert.Equal(Analysis.CallTreeStatus.DepthLimited, run.Status);
    }

    // A consumer that wants the whole graph calls a later tier directly and pays exactly one full
    // build — the scoped build is never made — and gets both roots.
    [Fact]
    public void Callers_RequestedDirectly_BuildsExactlyOneFullIndex()
    {
        int run = MemberToken(CallerPath, "Entry", "Run");
        var graph = ProgressiveMemberCallGraph.Open(CallerPath, run, NullResolver, [TargetPath]);

        MethodBodyInspectionSession.OpenCountForTests = 0;
        var view = graph.Callers();

        Assert.Equal(1, MethodBodyInspectionSession.OpenCountForTests);
        Assert.Equal(CallGraphTier.Callers, view.Tier);
        Assert.NotNull(view.CallerRoot);
        Assert.NotNull(view.CalleeRoot);

        // Full intra-assembly build surfaces Run's own caller.
        Assert.Contains(view.CallerRoot!.Children, child => child.Member.Name == "RunOuter");
    }

    // Once the full build has landed, requesting the callee tier reuses it (no second build) and the
    // callee tree deepens past the scoped depth-1 bound to the configured depth.
    [Fact]
    public void Callees_AfterFullBuild_ReusesFullIndex_DeepensChain()
    {
        int runOuter = MemberToken(CallerPath, "Entry", "RunOuter");
        var graph = ProgressiveMemberCallGraph.Open(CallerPath, runOuter, NullResolver, [TargetPath]);

        MethodBodyInspectionSession.OpenCountForTests = 0;
        _ = graph.Callers();
        var view = graph.Callees();

        Assert.Equal(1, MethodBodyInspectionSession.OpenCountForTests);
        Assert.Null(view.CallerRoot);

        // RunOuter -> Run -> Ping: the reused full build expands Run past a scoped depth-1 stop.
        var run = Child(view.CalleeRoot!, "Run");
        Assert.Contains(run.Children, child => child.Member.Name == "Ping");
    }

    // The cross-library tier decodes the in-scope package and expands the callee chain across the
    // assembly boundary, tagging the boundary-crossing callee with its source assembly. Requested
    // directly it costs one full build plus one package build (no scoped build).
    [Fact]
    public void CrossLibrary_ExpandsCalleeChainAcrossBoundary_TagsSource()
    {
        int runOuter = MemberToken(CallerPath, "Entry", "RunOuter");
        var graph = ProgressiveMemberCallGraph.Open(CallerPath, runOuter, NullResolver, [TargetPath]);

        MethodBodyInspectionSession.OpenCountForTests = 0;
        var view = graph.CrossLibrary();

        Assert.Equal(2, MethodBodyInspectionSession.OpenCountForTests);
        Assert.Equal(CallGraphTier.CrossLibrary, view.Tier);
        Assert.NotNull(view.CallerRoot);

        var run = Child(view.CalleeRoot!, "Run");
        var ping = Child(run, "Ping");
        Assert.NotEqual(Analysis.CallTreeStatus.External, ping.Status);
        Assert.Equal(TargetAssemblyName(), ping.Perf?.Source);
    }

    // Streaming yields the three layers in acquisition order with no duplicated build: a scoped
    // build, a full build, and one package build — three session opens total.
    [Fact]
    public void Tiers_StreamInOrder_WithoutDuplicateBuilds()
    {
        int runOuter = MemberToken(CallerPath, "Entry", "RunOuter");
        var graph = ProgressiveMemberCallGraph.Open(CallerPath, runOuter, NullResolver, [TargetPath]);

        MethodBodyInspectionSession.OpenCountForTests = 0;
        var views = graph.Tiers().ToList();

        Assert.Equal(3, MethodBodyInspectionSession.OpenCountForTests);
        Assert.Equal(
            [CallGraphTier.Callees, CallGraphTier.Callers, CallGraphTier.CrossLibrary],
            views.Select(view => view.Tier));
        Assert.Null(views[0].CallerRoot);
        Assert.NotNull(views[1].CallerRoot);
        Assert.Equal(TargetAssemblyName(), Child(Child(views[2].CalleeRoot!, "Run"), "Ping").Perf?.Source);
    }

    // Without cross-library scope the stream stops after the caller tier.
    [Fact]
    public void Tiers_NoCrossLibraryScope_StopsAtCallers()
    {
        int run = MemberToken(CallerPath, "Entry", "Run");
        var graph = ProgressiveMemberCallGraph.Open(CallerPath, run, NullResolver);

        Assert.False(graph.HasCrossLibraryScope);
        Assert.Equal(
            [CallGraphTier.Callees, CallGraphTier.Callers],
            graph.Tiers().Select(view => view.Tier));
    }

    // The seam yields presentation-free roots that project through the shared graph projection.
    [Fact]
    public void Roots_RoundTripThroughCallGraphProjection()
    {
        int run = MemberToken(CallerPath, "Entry", "Run");
        var graph = ProgressiveMemberCallGraph.Open(CallerPath, run, NullResolver, [TargetPath]);

        var view = graph.CrossLibrary();
        var projection = CallGraphProjection.Create(view.CallerRoot, view.CalleeRoot);

        Assert.NotEmpty(projection.Nodes);
        Assert.Contains(projection.Nodes, n => n.Member.Name == "Run");
    }

    // The push driver raises LayerReady for each layer in order, then Completed once.
    [Fact]
    public async Task RunAsync_RaisesLayerReadyPerTier_ThenCompleted()
    {
        int runOuter = MemberToken(CallerPath, "Entry", "RunOuter");
        var graph = ProgressiveMemberCallGraph.Open(CallerPath, runOuter, NullResolver, [TargetPath]);

        var layers = new List<CallGraphTier>();
        int completed = 0;
        graph.LayerReady += (_, view) => layers.Add(view.Tier);
        graph.Completed += (_, _) => completed++;

        await graph.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [CallGraphTier.Callees, CallGraphTier.Callers, CallGraphTier.CrossLibrary],
            layers);
        Assert.Equal(1, completed);
    }

    // Cancellation is observed BEFORE each tier is acquired: cancelling in the first LayerReady
    // handler must stop the next (more expensive) tier from building at all.
    [Fact]
    public void RunAsync_CancelInFirstLayer_DoesNotBuildNextTier()
    {
        int runOuter = MemberToken(CallerPath, "Entry", "RunOuter");
        var graph = ProgressiveMemberCallGraph.Open(CallerPath, runOuter, NullResolver, [TargetPath]);

        using var cts = new CancellationTokenSource();
        var layers = new List<CallGraphTier>();
        graph.LayerReady += (_, view) => { layers.Add(view.Tier); cts.Cancel(); };

        MethodBodyInspectionSession.OpenCountForTests = 0;
        var task = graph.RunAsync(cts.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => task.GetAwaiter().GetResult());

        Assert.Equal([CallGraphTier.Callees], layers);
        // Only the scoped first-paint build ran; Callers() was never reached.
        Assert.Equal(1, MethodBodyInspectionSession.OpenCountForTests);
    }
}
