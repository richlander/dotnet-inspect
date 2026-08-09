using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.CallGraph;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Queries.Tests;

public sealed class ProgressiveMemberCallGraphTests
{
    static string CallerPath =>
        FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
    static string TargetPath =>
        FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
    static string TargetV2Path =>
        FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath();

    static int MemberToken(
        string assemblyPath,
        string typeName,
        string methodName)
    {
        Analysis.LibraryBodyIndex index =
            Analysis.LibraryBodyIndex.Open(assemblyPath);
        return index.Methods.First(
            method => method.DeclaringType.Name == typeName
                && method.Name == methodName).MetadataToken;
    }

    static string TargetAssemblyName() =>
        Analysis.LibraryBodyIndex.Open(TargetPath)
            .Methods.First().AssemblyName;

    static Analysis.CallTreeNode Child(
        Analysis.CallTreeNode node,
        string name) =>
        node.Children.Single(child => child.Member.Name == name);

    [Fact]
    public void Callees_ScopedFirstPaint_BuildsScopedIndexOnly()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int run = MemberToken(CallerPath, "Entry", "Run");
        using var graph = new ProgressiveMemberCallGraph(
            context.Group,
            context.Sources[0].Assembly,
            run);

        MemberCallGraphView view = graph.Callees();

        Assert.Equal(CallGraphTier.Callees, view.Tier);
        Assert.Null(view.CallerRoot);
        Assert.Equal("Run", view.CalleeRoot!.Member.Name);
        Analysis.CallTreeNode ping = Child(view.CalleeRoot, "Ping");
        Assert.Equal(Analysis.CallTreeStatus.External, ping.Status);
        Assert.Empty(ping.Children);
        Assert.Equal(
            new ProgressiveMemberCallGraphBuildCounts(1, 0, 0),
            graph.BuildCounts);
        Assert.Equal(1, context.Sources[0].OpenCount);
        Assert.Equal(0, context.Sources[1].OpenCount);
    }

    [Fact]
    public void Callees_ScopedFirstPaint_MarksInAssemblyCalleeBounded()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int runOuter = MemberToken(CallerPath, "Entry", "RunOuter");
        using var graph = new ProgressiveMemberCallGraph(
            context.Group,
            context.Sources[0].Assembly,
            runOuter);

        MemberCallGraphView view = graph.Callees();

        Assert.Equal(
            Analysis.CallTreeStatus.DepthLimited,
            Child(view.CalleeRoot!, "Run").Status);
    }

    [Fact]
    public void DirectFullTier_SkipsScopedAndLaterCalleesReusesFull()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int runOuter = MemberToken(CallerPath, "Entry", "RunOuter");
        using var graph = new ProgressiveMemberCallGraph(
            context.Group,
            context.Sources[0].Assembly,
            runOuter);

        MemberCallGraphView crossLibrary = graph.CrossLibrary();
        MemberCallGraphView callees = graph.Callees();

        Assert.NotNull(crossLibrary.CallerRoot);
        Analysis.CallTreeNode run = Child(callees.CalleeRoot!, "Run");
        Assert.Contains(
            run.Children,
            child => child.Member.Name == "Ping");
        Assert.Equal(
            new ProgressiveMemberCallGraphBuildCounts(0, 1, 1),
            graph.BuildCounts);
        Assert.All(
            context.Sources,
            source => Assert.Equal(1, source.OpenCount));
    }

    [Fact]
    public void Tiers_ShareSnapshotsAndBuildEachIndexAtMostOnce()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int runOuter = MemberToken(CallerPath, "Entry", "RunOuter");
        using var graph = new ProgressiveMemberCallGraph(
            context.Group,
            context.Sources[0].Assembly,
            runOuter);

        MemberCallGraphView[] first = [.. graph.Tiers()];
        MemberCallGraphView[] second = [.. graph.Tiers()];

        Assert.Equal(
            [
                CallGraphTier.Callees,
                CallGraphTier.Callers,
                CallGraphTier.CrossLibrary,
            ],
            first.Select(view => view.Tier));
        Assert.Equal(
            first.Select(view => view.Tier),
            second.Select(view => view.Tier));
        Assert.Equal(
            new ProgressiveMemberCallGraphBuildCounts(1, 1, 1),
            graph.BuildCounts);
        Assert.All(
            context.Sources,
            source => Assert.Equal(1, source.OpenCount));

        Analysis.CallTreeNode ping =
            Child(Child(first[2].CalleeRoot!, "Run"), "Ping");
        Assert.Equal(TargetAssemblyName(), ping.Perf?.Source);
    }

    [Fact]
    public void DuplicateImages_BuildOneCrossLibraryIndex()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath, TargetPath);
        int run = MemberToken(CallerPath, "Entry", "Run");
        using var graph = new ProgressiveMemberCallGraph(
            context.Group,
            context.Sources[0].Assembly,
            run);

        _ = graph.CrossLibrary();

        Assert.Equal(
            new ProgressiveMemberCallGraphBuildCounts(0, 1, 1),
            graph.BuildCounts);
        Assert.All(
            context.Sources,
            source => Assert.Equal(1, source.OpenCount));
    }

    [Fact]
    public void StreamOnlyParticipants_CanBuildCrossLibraryGraph()
    {
        using GraphContext context =
            GraphContext.CreateStreamOnly(CallerPath, TargetPath);
        int runOuter = MemberToken(CallerPath, "Entry", "RunOuter");
        using var graph = new ProgressiveMemberCallGraph(
            context.Group,
            context.Sources[0].Assembly,
            runOuter);

        MemberCallGraphView view = graph.CrossLibrary();

        Analysis.CallTreeNode ping =
            Child(Child(view.CalleeRoot!, "Run"), "Ping");
        Assert.Equal(TargetAssemblyName(), ping.Perf?.Source);
        Assert.All(context.Sources, source => Assert.Null(source.Assembly.Path));
        Assert.All(
            context.Sources,
            source => Assert.Equal(1, source.OpenCount));
    }

    [Fact]
    public void CrossLibrary_AcquisitionFailureIsTypedAndCached()
    {
        using GraphContext context =
            GraphContext.CreateWithFailingParticipant(
                CallerPath,
                TargetPath);
        int run = MemberToken(CallerPath, "Entry", "Run");
        using var graph = new ProgressiveMemberCallGraph(
            context.Group,
            context.Sources[0].Assembly,
            run);

        MemberCallGraphAcquisitionException first =
            Assert.Throws<MemberCallGraphAcquisitionException>(
                graph.CrossLibrary);
        MemberCallGraphAcquisitionException second =
            Assert.Throws<MemberCallGraphAcquisitionException>(
                graph.CrossLibrary);

        Assert.IsType<MemberCallGraphAcquisitionFailure.Rejected>(
            Assert.Single(first.Failures));
        Assert.IsType<MemberCallGraphAcquisitionFailure.Rejected>(
            Assert.Single(second.Failures));
        Assert.Equal(1, context.Sources[1].OpenCount);
        Assert.Equal(
            new ProgressiveMemberCallGraphBuildCounts(0, 1, 0),
            graph.BuildCounts);
    }

    [Fact]
    public void WorkspaceDisposal_DisposesOwnedGraphBeforeSnapshots()
    {
        GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int run = MemberToken(CallerPath, "Entry", "Run");
        var graph = new ProgressiveMemberCallGraph(
            context.Group,
            context.Sources[0].Assembly,
            run);
        _ = graph.CrossLibrary();
        Assert.True(context.Group.RetainedImageBytes > 0);

        context.Workspace.Dispose();

        Assert.Equal(0, context.Group.RetainedImageBytes);
        Assert.Throws<ObjectDisposedException>(graph.Callees);
        graph.Dispose();
    }

    [Fact]
    public void OptionsRejectFeatureSetsThatCannotProduceScopedGraph()
    {
        using GraphContext context = GraphContext.Create(CallerPath);
        int run = MemberToken(CallerPath, "Entry", "Run");

        Assert.Throws<ArgumentException>(
            () => new ProgressiveMemberCallGraph(
                context.Group,
                context.Sources[0].Assembly,
                run,
                new()
                {
                    Features =
                        Analysis.LibraryBodyAnalysisFeatures.None,
                }));
        Assert.Throws<ArgumentException>(
            () => new ProgressiveMemberCallGraph(
                context.Group,
                context.Sources[0].Assembly,
                run,
                new()
                {
                    Features =
                        Analysis.LibraryBodyAnalysisFeatures.LeakTriage,
                }));
    }

    [Fact]
    public void CrossLibrary_VersionSkewRetainsIncompleteDiagnostics()
    {
        using GraphContext context =
            GraphContext.Create(TargetV2Path, CallerPath);
        int ping = MemberToken(TargetV2Path, "Api", "Ping");
        using var graph = new ProgressiveMemberCallGraph(
            context.Group,
            context.Sources[0].Assembly,
            ping);

        MemberCallGraphView view = graph.CrossLibrary();

        Assert.DoesNotContain(
            view.CallerRoot!.Children,
            child => child.Member.Name == "Run");
        Assert.True(view.Diagnostics.IsIncomplete);
        Assert.True(view.Diagnostics.IncompleteEdgeCount > 0);
    }

    [Fact]
    public void Projection_DoesNotAcquireOrBuildMoreIndexes()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int run = MemberToken(CallerPath, "Entry", "Run");
        using var graph = new ProgressiveMemberCallGraph(
            context.Group,
            context.Sources[0].Assembly,
            run);
        MemberCallGraphView view = graph.CrossLibrary();
        ProgressiveMemberCallGraphBuildCounts before = graph.BuildCounts;

        CallGraphProjection first = CallGraphProjection.Create(
            view.CallerRoot,
            view.CalleeRoot);
        CallGraphProjection second = CallGraphProjection.Create(
            view.CallerRoot,
            view.CalleeRoot);

        Assert.NotEmpty(first.Nodes);
        Assert.Equal(
            first.Nodes.Select(node => (node.Id, node.Label, node.Kind)),
            second.Nodes.Select(node => (node.Id, node.Label, node.Kind)));
        Assert.Equal(first.Edges.Length, second.Edges.Length);
        Assert.Equal(before, graph.BuildCounts);
        Assert.All(
            context.Sources,
            source => Assert.Equal(1, source.OpenCount));
    }

    [Fact]
    public async Task RunAsync_RaisesLayersInOrderAndCompletes()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int runOuter = MemberToken(CallerPath, "Entry", "RunOuter");
        using var graph = new ProgressiveMemberCallGraph(
            context.Group,
            context.Sources[0].Assembly,
            runOuter);
        var layers = new List<CallGraphTier>();
        int completed = 0;
        graph.LayerReady += (_, view) => layers.Add(view.Tier);
        graph.Completed += (_, _) => completed++;

        await graph.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                CallGraphTier.Callees,
                CallGraphTier.Callers,
                CallGraphTier.CrossLibrary,
            ],
            layers);
        Assert.Equal(1, completed);
        Assert.Equal(
            new ProgressiveMemberCallGraphBuildCounts(1, 1, 1),
            graph.BuildCounts);
    }

    [Fact]
    public void RunAsync_CancellationAfterFirstLayerSkipsFullBuild()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int runOuter = MemberToken(CallerPath, "Entry", "RunOuter");
        using var graph = new ProgressiveMemberCallGraph(
            context.Group,
            context.Sources[0].Assembly,
            runOuter);
        using var cancellation = new CancellationTokenSource();
        var layers = new List<CallGraphTier>();
        graph.LayerReady += (_, view) =>
        {
            layers.Add(view.Tier);
            cancellation.Cancel();
        };

        Task task = graph.RunAsync(cancellation.Token);

        Assert.ThrowsAny<OperationCanceledException>(
            () => task.GetAwaiter().GetResult());
        Assert.Equal([CallGraphTier.Callees], layers);
        Assert.Equal(
            new ProgressiveMemberCallGraphBuildCounts(1, 0, 0),
            graph.BuildCounts);
    }

    sealed class GraphContext : IDisposable
    {
        GraphContext(
            InspectionWorkspace workspace,
            AssemblyContextGroup group,
            TestSource[] sources)
        {
            Workspace = workspace;
            Group = group;
            Sources = sources;
        }

        internal InspectionWorkspace Workspace { get; }
        internal AssemblyContextGroup Group { get; }
        internal TestSource[] Sources { get; }

        internal static GraphContext Create(params string[] paths) =>
            CreateCore(streamOnly: false, failingIndex: null, paths);

        internal static GraphContext CreateStreamOnly(
            params string[] paths) =>
            CreateCore(streamOnly: true, failingIndex: null, paths);

        internal static GraphContext CreateWithFailingParticipant(
            params string[] paths) =>
            CreateCore(streamOnly: false, failingIndex: 1, paths);

        static GraphContext CreateCore(
            bool streamOnly,
            int? failingIndex,
            params string[] paths)
        {
            TestSource[] sources = paths
                .Select(
                    (path, index) => TestSource.Create(
                        path,
                        streamOnly,
                        failingIndex == index))
                .ToArray();
            var policy =
                new SourceRelativeAssemblyGroupBindingPolicy(
                    sources.Select(source => (
                        source.Assembly,
                        Policy: (IAssemblyBindingPolicy)
                            new AssemblyDependencyResolver(
                                new(
                                    source.SourcePath)
                                {
                                    PreferImplementationAssemblies = true,
                                    AllowPlatformAssemblyVersionRollForward =
                                        true,
                                }))));
            var workspace = new InspectionWorkspace();
            AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                    sources.Select(
                        source => new AssemblyContextParticipant(
                            source.Assembly,
                            policy)));
            return new(workspace, group, sources);
        }

        public void Dispose() => Workspace.Dispose();
    }

    sealed class TestSource
    {
        int _openCount;
        readonly byte[]? _content;
        readonly bool _fails;

        TestSource(
            string sourcePath,
            ResolvedAssemblyReference assembly,
            byte[]? content,
            bool fails)
        {
            SourcePath = sourcePath;
            Assembly = assembly;
            _content = content;
            _fails = fails;
        }

        internal string SourcePath { get; }
        internal ResolvedAssemblyReference Assembly { get; }
        internal int OpenCount => Volatile.Read(ref _openCount);

        internal static TestSource Create(
            string sourcePath,
            bool streamOnly,
            bool fails)
        {
            ResolvedAssemblyReference source =
                ResolvedAssemblyReference.CreateFromPath(
                    sourcePath,
                    AssemblyResolutionProvenance.Local(
                        "progressive graph test source"));
            byte[]? content =
                streamOnly ? File.ReadAllBytes(sourcePath) : null;
            TestSource? testSource = null;
            ResolvedAssemblyReference assembly =
                ResolvedAssemblyReference.Create(
                    source.Identity,
                    streamOnly ? null : sourcePath,
                    () => testSource!.Open(),
                    source.Provenance,
                    source.LastWriteTimeUtc);
            testSource = new TestSource(
                sourcePath,
                assembly,
                content,
                fails);
            return testSource;
        }

        Stream Open()
        {
            Interlocked.Increment(ref _openCount);
            if (_fails)
                throw new IOException("Synthetic graph participant failure.");
            return _content is null
                ? File.OpenRead(SourcePath)
                : new MemoryStream(_content, writable: false);
        }
    }
}
