using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.MalformedOwnershipFixtures;
using ILInspector.Analysis.UnoptimizedAsyncFixtures;
using ILInspector.CallGraph;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class LibraryBodyIndexTests
{

    [Fact]
    public void BuildCallerTree_RendersReverseEdgesForSelectedRoot()
    {
        var index = LibraryBodyIndex.Open(typeof(CallerTreeFixtures).Assembly.Location);
        var root = Assert.Single(index.Methods.Where(method => method.Name == nameof(CallerTreeFixtures.Inner)));

        var tree = index.BuildCallerTree(root.MetadataToken, maxDepth: 2, maxNodes: 10);

        Assert.Equal(nameof(CallerTreeFixtures.Inner), tree.Member.Name);
        Assert.Contains(tree.Children, child => child.Member.Name == nameof(CallerTreeFixtures.Mid));
        Assert.Contains(tree.Children.SelectMany(child => child.Children), child => child.Member.Name == nameof(CallerTreeFixtures.RootCall));
        Assert.Equal("target", tree.Perf?.RootKind);
    }

    [Fact]
    public void BuildCallerTree_MarksCallerNodeInLoop_WhenCallerInvokesTargetInLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);
        var root = Assert.Single(index.Methods.Where(method => method.Name == nameof(CallSiteFixtures.CallsConsoleWriteLine)));

        var tree = index.BuildCallerTree(root.MetadataToken, maxDepth: 2, maxNodes: 25);

        var loopingCaller = Assert.Single(tree.Children.Where(child =>
            child.Member.Name == nameof(CallSiteFixtures.CallsConsoleWriteLineInLoop)));
        Assert.True(loopingCaller.Perf?.InLoop);
    }

    // #1739 scope pin: the Caller Graph is a static `callvirt`-operand graph. It does not
    // expand runtime virtual-dispatch targets, so callers are attributed to the statically
    // declared operand (the virtual base method), never to the override reached at runtime.
    // This characterizes the owned scope boundary: it must flip deliberately if override /
    // devirtualization target expansion is ever added.
    [Fact]
    public void BuildCallerTree_VirtualDispatch_AttributesCallersToStaticOperand_NotOverride()
    {
        var index = LibraryBodyIndex.Open(typeof(VirtualDispatchCallers).Assembly.Location);

        // Both call sites resolve to VirtualDispatchBase.Work — including ViaDerived, whose
        // receiver is a runtime VirtualDispatchDerived but whose callvirt operand is the base.
        var baseRoot = Assert.Single(index.Methods.Where(method =>
            method.DeclaringType.Name == nameof(VirtualDispatchBase) && method.Name == nameof(VirtualDispatchBase.Work)));
        var baseTree = index.BuildCallerTree(baseRoot.MetadataToken, maxDepth: 2, maxNodes: 25);
        Assert.Contains(baseTree.Children, child => child.Member.Name == nameof(VirtualDispatchCallers.ViaBase));
        Assert.Contains(baseTree.Children, child => child.Member.Name == nameof(VirtualDispatchCallers.ViaDerived));

        // The override is attributed no callers: virtual dispatch to it is not inferred.
        var derivedRoot = Assert.Single(index.Methods.Where(method =>
            method.DeclaringType.Name == nameof(VirtualDispatchDerived) && method.Name == nameof(VirtualDispatchDerived.Work)));
        var derivedTree = index.BuildCallerTree(derivedRoot.MetadataToken, maxDepth: 2, maxNodes: 25);
        Assert.Equal("target", derivedTree.Perf?.RootKind);
        Assert.Empty(derivedTree.Children);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void BuildCallTrees_MarkOnlyOpenVirtualDispatchAsUnresolved()
    {
        var index =
            LibraryBodyIndex.Open(
                typeof(VirtualDispatchCallers).Assembly.Location);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                index.Path,
                AssemblyResolutionProvenance.Local(
                    "virtual-dispatch call-tree test"));
        using var catalog =
            new CatalogCallGraphScope(
                new AssemblyDependencyResolver(
                    new AssemblyDependencyResolutionOptions(
                        index.Path)),
                [new CatalogCallGraphParticipant(index, assembly)]);

        AssertDispatch(
            nameof(VirtualDispatchCallers.ViaBase),
            expectedUnresolved: true);
        AssertDispatch(
            nameof(VirtualDispatchCallers.ViaNonVirtual),
            expectedUnresolved: false);

        Assert.True(
            Assert.Single(
                index.DeclaredMethods,
                method => method.DeclaringType.Name
                        == nameof(VirtualDispatchBase)
                    && method.Name
                        == nameof(VirtualDispatchBase.Work))
            .IsVirtualDispatchOpen);
        Assert.False(
            Assert.Single(
                index.DeclaredMethods,
                method => method.DeclaringType.Name
                        == nameof(VirtualDispatchDerived)
                    && method.Name
                        == nameof(VirtualDispatchDerived.Work))
            .IsVirtualDispatchOpen);
        Assert.False(
            Assert.Single(
                index.DeclaredMethods,
                method => method.DeclaringType.Name
                        == nameof(FinalVirtualDispatchDerived)
                    && method.Name
                        == nameof(FinalVirtualDispatchDerived.Work))
            .IsVirtualDispatchOpen);
        Assert.False(
            Assert.Single(
                index.DeclaredMethods,
                method => method.DeclaringType.Name
                        == nameof(NonVirtualDispatchTarget)
                    && method.Name
                        == nameof(NonVirtualDispatchTarget.Work))
            .IsVirtualDispatchOpen);

        void AssertDispatch(
            string methodName,
            bool expectedUnresolved)
        {
            int token = typeof(VirtualDispatchCallers)
                .GetMethod(methodName)!
                .MetadataToken;
            CallTreeNode localChild =
                Assert.Single(
                    index.BuildCallTree(
                        token,
                        maxDepth: 2,
                        maxNodes: 10).Children);
            CallTreeNode catalogChild =
                Assert.Single(
                    catalog.BuildCallTree(
                        index,
                        token,
                        maxDepth: 2,
                        maxNodes: 10).Children);

            Assert.Equal(
                expectedUnresolved,
                localChild.HasUnresolvedDispatch);
            Assert.Equal(
                expectedUnresolved,
                catalogChild.HasUnresolvedDispatch);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CallTrees_PreserveDispatchAcrossCalleeCollapse()
    {
        var index =
            LibraryBodyIndex.Open(
                typeof(VirtualDispatchDerived).Assembly.Location);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                index.Path,
                AssemblyResolutionProvenance.Local(
                    "collapsed virtual-dispatch call-tree test"));
        using var catalog =
            new CatalogCallGraphScope(
                new AssemblyDependencyResolver(
                    new AssemblyDependencyResolutionOptions(
                        index.Path)),
                [new CatalogCallGraphParticipant(index, assembly)]);

        AssertMixedDispatch(
            nameof(
                VirtualDispatchDerived
                    .CallsBaseThenVirtual),
            expectedRepresentativeInLoop: false);
        AssertMixedDispatch(
            nameof(
                VirtualDispatchDerived
                    .CallsVirtualThenBaseInLoop),
            expectedRepresentativeInLoop: true);

        void AssertMixedDispatch(
            string methodName,
            bool expectedRepresentativeInLoop)
        {
            int token = typeof(VirtualDispatchDerived)
                .GetMethod(methodName)!
                .MetadataToken;
            CallTreeNode local =
                index.BuildCallTree(
                    token,
                    maxDepth: 2,
                    maxNodes: 10);
            CallTreeNode catalogTree =
                catalog.BuildCallTree(
                    index,
                    token,
                    maxDepth: 2,
                    maxNodes: 10);

            CallTreeNode localCollapsed =
                Assert.Single(
                    local.Children);
            CallTreeNode collapsed =
                Assert.Single(
                    catalogTree.Children);
            Assert.Equal(
                CallKind.Call,
                localCollapsed.Kind);
            Assert.Equal(
                CallKind.Call,
                collapsed.Kind);
            Assert.Equal(
                expectedRepresentativeInLoop,
                localCollapsed.Perf?.InLoop);
            Assert.Equal(
                expectedRepresentativeInLoop,
                collapsed.Perf?.InLoop);
            Assert.True(
                localCollapsed.HasUnresolvedDispatch);
            Assert.True(
                collapsed.HasUnresolvedDispatch);
            Assert.Equal(2, local.Perf?.Fanout);
            Assert.Equal(2, catalogTree.Perf?.Fanout);
            Assert.True(
                CallGraphProjection
                    .FromCallees(local)
                    .HasUnexploredTraversalBoundary);
            Assert.True(
                CallGraphProjection
                    .FromCallees(catalogTree)
                    .HasUnexploredTraversalBoundary);

            Assert.NotEqual(
                CallTreeStatus.Truncated,
                index.BuildCallTree(
                    token,
                    maxDepth: 2,
                    maxNodes: 2).Status);
            Assert.NotEqual(
                CallTreeStatus.Truncated,
                catalog.BuildCallTree(
                    index,
                    token,
                    maxDepth: 2,
                    maxNodes: 2).Status);
        }
    }

    [Fact]
    public void BuildCallTree_ClassifiesSameAssemblyBodilessCallee()
    {
        var index =
            LibraryBodyIndex.Open(
                typeof(BodilessRootFixtures).Assembly.Location);
        int rootToken = typeof(BodilessRootFixtures)
            .GetMethod(
                nameof(
                    BodilessRootFixtures
                        .InvokesThroughInterface))!
            .MetadataToken;

        CallTreeNode child =
            Assert.Single(
                index.BuildCallTree(
                    rootToken,
                    maxDepth: 2,
                    maxNodes: 10).Children);

        Assert.Equal(
            CallTreeStatus.Bodiless,
            child.Status);
        Assert.Equal(
            nameof(ICallerGraphTarget.Target),
            child.Member.Name);
    }

    [Fact]
    public void BuildCallerTree_ResolvesCallers_WhenSelectedRootIsBodilessInterfaceMethod()
    {
        var index = LibraryBodyIndex.Open(typeof(BodilessRootFixtures).Assembly.Location);
        // Interface methods have no body and so are absent from index.Methods; the caller
        // references the method by its interface-method token.
        int targetToken = typeof(ICallerGraphTarget)
            .GetMethod(nameof(ICallerGraphTarget.Target))!.MetadataToken;

        var tree = index.BuildCallerTree(targetToken, maxDepth: 2, maxNodes: 25);

        Assert.Equal(nameof(ICallerGraphTarget.Target), tree.Member.Name);
        Assert.Equal("target", tree.Perf?.RootKind);
        Assert.Contains(tree.Children, child =>
            child.Member.Name == nameof(BodilessRootFixtures.InvokesThroughInterface));
    }

    /// <summary>
    /// Derives this type's mutable cache fields by reflection and pins each one to a side of the
    /// release boundary, so the two release methods are gated on the property they exist for
    /// rather than only on staying correct. Both directions fail: a cache the doc claims to drop
    /// but doesn't, and a cache dropped that the doc says survives. A newly added cache field
    /// belongs to neither list and fails until someone decides which side it is on.
    /// <para>
    /// Known boundary: this compares populated-ness, so it would not see a future release method
    /// that empties a readonly collection in place instead of nulling a field.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void ReleaseMethods_DropExactlyTheCachesTheyDocument()
    {
        string[] resultGraphCaches =
        [
            "_directCallsByCaller",
            "_distinctCallerEdgesByCallee",
            "_distinctCallersByCallee",
            "_declaredMethodMap",
            "_methodMap",
            "_rootPathGraph",
        ];
        string[] resultRetainedCaches =
        [
            "_physicalDirectCalls",
            "_signals",
        ];
        string[] adapterGraphCaches =
        [
            "_directCallsByEvidenceMethod",
            "_overloadRelationships",
            "_projectedImplementationProfiles",
        ];
        string[] adapterRetainedCaches =
        [
            "_unsafeEvidenceByMember",
        ];

        Assert.Equal(
            resultGraphCaches.Concat(resultRetainedCaches)
                .OrderBy(name => name, StringComparer.Ordinal),
            MutableCacheFields(typeof(LibraryCallGraphAnalysisResult))
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(
            adapterGraphCaches.Concat(adapterRetainedCaches)
                .OrderBy(name => name, StringComparer.Ordinal),
            MutableCacheFields(typeof(LibraryBodyIndex))
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.Ordinal));

        string analysisPath = typeof(LibraryBodyIndex).Assembly.Location;

        var index = Exercised(analysisPath);
        LibraryCallGraphAnalysisResult callGraph =
            index.CallGraphAnalysis;
        var resultBefore = PopulatedCaches(
            callGraph,
            typeof(LibraryCallGraphAnalysisResult));
        var adapterBefore = PopulatedCaches(
            index,
            typeof(LibraryBodyIndex));

        // The gate is only meaningful if the caches under test were populated to begin with.
        // Both halves need this: an unpopulated cache is absent from `before` and from `after`,
        // so the set comparison would hold no matter what the release methods did to it.
        foreach (var name in resultGraphCaches.Concat(resultRetainedCaches))
            Assert.Contains(name, resultBefore);
        foreach (var name in adapterGraphCaches.Concat(adapterRetainedCaches))
            Assert.Contains(name, adapterBefore);

        index.ReleaseCallGraphCaches();
        Assert.Equal(
            resultBefore.Where(name => !resultGraphCaches.Contains(name)),
            PopulatedCaches(
                callGraph,
                typeof(LibraryCallGraphAnalysisResult)));
        Assert.Equal(
            adapterBefore.Where(name => !adapterGraphCaches.Contains(name)),
            PopulatedCaches(
                index,
                typeof(LibraryBodyIndex)));

        static LibraryBodyIndex Exercised(string path)
        {
            var index = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.Default
                    | LibraryBodyAnalysisFeatures
                        .ImplementationProfiles);
            int token = index.Methods.First().MetadataToken;
            index.BuildCallerTree(token, maxDepth: 2, maxNodes: 50);
            index.BuildCallTree(token, maxDepth: 2, maxNodes: 50);
            _ = LibraryBodyRootPathAnalysis.FindShortestPaths(
                index.CallGraphAnalysis,
                [
                    new(
                        index.ModuleIdentity.ModuleVersionId,
                        MetadataTokens.MethodDefinitionHandle(
                            token & 0x00FFFFFF)),
                ],
                [
                    new(
                        index.ModuleIdentity.ModuleVersionId,
                        MetadataTokens.MethodDefinitionHandle(
                            token & 0x00FFFFFF)),
                ],
                new(0, 1, 1, 1));
            _ = index.GetDirectCallsByEvidenceMethod();
            _ = index.ImplementationProfiles();
            // The retained half of the contract is only gated on caches this workload actually
            // populates, and the call-tree builders alone reach just one of the seven. Touch the
            // evidence-domain producers too; OptimizationOpportunities is what pulls in
            // _directCallerLoops and _rootReachByToken, which have no direct test access.
            _ = index.OptimizationOpportunities;
            _ = index.AllocationFanoutOpportunities;
            _ = index.GetUnsafeEvidenceByMember();
            _ = index.GeneratedFrameworkTypes;
            return index;
        }

        // Every non-readonly instance field on this type is a lazy cache, so the filter is just
        // "mutable state" rather than a type-shaped guess. An earlier version excluded value types
        // and so silently missed the two ImmutableArray caches, which is the same blind spot this
        // test exists to catch.
        static IEnumerable<FieldInfo> MutableCacheFields(Type type)
            => type
                .GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                .Where(field => field.Name.StartsWith('_') && !field.IsInitOnly);

        static List<string> PopulatedCaches(object owner, Type type)
            => MutableCacheFields(type)
                .Where(field => IsPopulated(field.GetValue(owner)))
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

        // ImmutableArray caches signal "not yet computed" with IsDefault, not with null.
        static bool IsPopulated(object? value)
        {
            if (value is null)
                return false;

            var isDefault = value.GetType().GetProperty("IsDefault");
            return isDefault is null || !(bool)isDefault.GetValue(value)!;
        }
    }

    // #3342: the same-assembly maps behind the call-tree builders are cached per index so a
    // consumer that asks many questions pays for them once. Catalog-scoped storage now belongs
    // to CatalogCallGraphScope and is gated separately by CatalogCallGraphScopeTests.
    // This walks one index through a sequence designed to poison a root-dependent cache and
    // requires every answer to match a fresh index that was asked nothing else.
    //
    // Scope note: this pins cache *independence*, not the bodiless-root contract itself. Serving
    // a bodiless root from the shared grouping is a correctness fault that a fresh index would
    // reproduce identically, so no independence test can see it;
    // BuildCallerTree_ResolvesCallers_WhenSelectedRootIsBodilessInterfaceMethod owns that, and
    // was confirmed to fail when the guard is removed.
    [Fact]
    [Trait("Speed", "Slow")]
    public void SameAssemblyCallTreeBuilders_AreUnaffectedByEarlierRequestsOnTheSameIndex()
    {
        string analysisPath = typeof(LibraryBodyIndex).Assembly.Location;
        string testPath = typeof(LibraryBodyIndexTests).Assembly.Location;

        // Pick a root with a genuinely branching caller tree. Comparing trees is only evidence
        // if the trees can differ: a root with no in-assembly callers renders one leaf line, and
        // every comparison below would hold no matter how badly a cache leaked.
        var probe = LibraryBodyIndex.Open(analysisPath);
        int richToken = PickRichest(probe, out int richest);

        Assert.True(richest >= 4, $"expected a branching caller tree to compare; richest had {richest} nodes");
        int otherToken = probe.Methods.First(method => method.MetadataToken != richToken).MetadataToken;
        // A bodiless root is the one case whose caller grouping genuinely depends on the root,
        // so it must not be served from (or poison) the shared per-index cache.
        int bodilessToken = typeof(ICallerGraphTarget).GetMethod(nameof(ICallerGraphTarget.Target))!.MetadataToken;

        // Each expectation comes from an index that has answered nothing else.
        var expectedCallers = Describe(LibraryBodyIndex.Open(analysisPath).BuildCallerTree(richToken, maxDepth: 2, maxNodes: 50));
        var expectedCallees = Describe(LibraryBodyIndex.Open(analysisPath).BuildCallTree(richToken, maxDepth: 2, maxNodes: 50));
        var expectedBodiless = Describe(LibraryBodyIndex.Open(testPath).BuildCallerTree(bodilessToken, maxDepth: 2, maxNodes: 50));

        // Now ask one index both directions in an order that would expose a leaked root.
        var reused = LibraryBodyIndex.Open(analysisPath);
        reused.BuildCallerTree(otherToken, maxDepth: 2, maxNodes: 50);
        reused.BuildCallTree(otherToken, maxDepth: 2, maxNodes: 50);

        Assert.Equal(expectedCallers, Describe(reused.BuildCallerTree(richToken, maxDepth: 2, maxNodes: 50)));
        Assert.Equal(expectedCallees, Describe(reused.BuildCallTree(richToken, maxDepth: 2, maxNodes: 50)));

        // A bodiless root asked after body-rooted requests have populated the cache.
        var reusedTests = LibraryBodyIndex.Open(testPath);
        int testAsmToken = PickRichest(reusedTests, out int testRichest);
        Assert.True(testRichest >= 2, $"expected a non-leaf caller tree in the test assembly; richest had {testRichest} nodes");
        var expectedTestAsmCallers = Describe(LibraryBodyIndex.Open(testPath).BuildCallerTree(testAsmToken, maxDepth: 2, maxNodes: 50));
        reusedTests.BuildCallerTree(testAsmToken, maxDepth: 2, maxNodes: 50);
        Assert.Equal(expectedBodiless, Describe(reusedTests.BuildCallerTree(bodilessToken, maxDepth: 2, maxNodes: 50)));

        // ...and the bodiless request must not have written its root-dependent grouping into the
        // shared cache, so a body-rooted request after it still answers like a fresh index.
        Assert.Equal(expectedTestAsmCallers, Describe(reusedTests.BuildCallerTree(testAsmToken, maxDepth: 2, maxNodes: 50)));

        // Releasing the index-owned caches is a memory/time trade only: answers after a release
        // must still match a fresh index.
        var released = LibraryBodyIndex.Open(analysisPath);
        released.BuildCallTree(richToken, maxDepth: 2, maxNodes: 50);
        released.ReleaseCallGraphCaches();
        Assert.Equal(expectedCallers, Describe(released.BuildCallerTree(richToken, maxDepth: 2, maxNodes: 50)));
        Assert.Equal(expectedCallees, Describe(released.BuildCallTree(richToken, maxDepth: 2, maxNodes: 50)));

        static int PickRichest(LibraryBodyIndex index, out int richest)
        {
            int token = 0;
            richest = 0;
            foreach (var method in index.Methods.Take(150))
            {
                int size = Describe(index.BuildCallerTree(method.MetadataToken, maxDepth: 2, maxNodes: 50)).Count;
                if (size > richest)
                {
                    richest = size;
                    token = method.MetadataToken;
                }
            }

            return token;
        }

        static List<string> Describe(CallTreeNode root)
        {
            var lines = new List<string>();
            void Walk(CallTreeNode node, int depth)
            {
                lines.Add($"{depth}|{node.Member.DeclaringType.Name}.{node.Member.Name}|{node.Status}|" +
                    $"{node.Perf?.Source ?? ""}|fanout={node.Perf?.Fanout ?? -1}|fanin={node.Perf?.Fanin ?? -1}");
                foreach (var child in node.Children)
                    Walk(child, depth + 1);
            }
            Walk(root, 0);
            return lines;
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void BuildCallerTree_WithScope_IncorporatesAndTagsExternalCallers()
    {
        var analysisIndex = LibraryBodyIndex.Open(typeof(LibraryBodyIndex).Assembly.Location);
        var testIndex = LibraryBodyIndex.Open(typeof(LibraryBodyIndexTests).Assembly.Location);
        var testAssemblyName = testIndex.Methods.First().AssemblyName;

        // LibraryBodyIndex.Open is a static method in the analysis assembly that this test
        // assembly calls; scoping the test assembly must pull those external callers into the
        // reverse graph and tag them with their source assembly.
        var open = analysisIndex.Methods.First(method =>
            method.DeclaringType.Name == nameof(LibraryBodyIndex) && method.Name == nameof(LibraryBodyIndex.Open));

        var scoped = analysisIndex.BuildCallerTree(open.MetadataToken, new[] { testIndex }, maxDepth: 2, maxNodes: 200);
        var unscoped = analysisIndex.BuildCallerTree(open.MetadataToken, maxDepth: 2, maxNodes: 200);

        Assert.Equal("target", scoped.Perf?.RootKind);
        // The target itself is not external.
        Assert.Null(scoped.Perf?.Source);
        Assert.True(HasSource(scoped, testAssemblyName), "expected an external caller tagged with the test assembly");
        // The single-assembly graph never incorporates the other assembly's callers.
        Assert.False(HasSource(unscoped, testAssemblyName), "single-assembly graph must not contain external callers");

        static bool HasSource(CallTreeNode node, string source)
            => node.Perf?.Source == source || node.Children.Any(child => HasSource(child, source));
    }

    // #3351: requesting a catalog scope that contributes no callers must not change the
    // same-assembly tree. The catalog graph remains selected for a scoped request, but its local
    // node identity, ordering, revisit, and budget behavior must agree with the cheap token graph.
    [Fact]
    [Trait("Speed", "Slow")]
    public void BuildCallerTree_NonContributingCatalogScopeMatchesSameAssemblyTree()
    {
        var index = LibraryBodyIndex.Open(typeof(LibraryBodyIndex).Assembly.Location);

        // A fixture that does not reference the analysis assembly, so it can never contribute a
        // caller. Opening it and prefiltering it away must be indistinguishable.
        var nonContributing = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphLookalikeCaller.AssemblyPath());
        using CatalogCallGraphScope scopeOpened =
            CatalogCallGraphTestExtensions.CreateScope(index, [nonContributing]);
        using CatalogCallGraphScope scopePrefilteredAway =
            CatalogCallGraphTestExtensions.CreateScope(index, []);

        int visited = 0;
        int alreadyShown = 0;
        int truncated = 0;
        int loopEdges = 0;
        var mismatches = new List<string>();
        (int MaxDepth, int MaxNodes)[] traversals =
            [(2, 50), (3, 5)];
        foreach (var method in index.DeclaredMethods)
        {
            foreach ((int maxDepth, int maxNodes) in traversals)
            {
                CallTreeNode tokenTree = index.BuildCallerTree(
                    method.MetadataToken,
                    maxDepth,
                    maxNodes);
                var noScopeRequested = FlattenCallTree(
                    tokenTree,
                    includePerf: true);
                var opened = FlattenCallTree(
                    scopeOpened.BuildCallerTree(
                        index,
                        method.MetadataToken,
                        maxDepth,
                        maxNodes),
                    includePerf: true);
                var prefilteredAway = FlattenCallTree(
                    scopePrefilteredAway.BuildCallerTree(
                        index,
                        method.MetadataToken,
                        maxDepth,
                        maxNodes),
                    includePerf: true);

                Assert.Equal(opened, prefilteredAway);
                if (!noScopeRequested.SequenceEqual(prefilteredAway))
                {
                    mismatches.Add(
                        $"{method.DeclaringType.Name}.{method.Name} "
                        + $"(depth {maxDepth}, nodes {maxNodes})"
                        + $"{Environment.NewLine}same assembly:"
                        + $"{Environment.NewLine}{string.Join(Environment.NewLine, noScopeRequested)}"
                        + $"{Environment.NewLine}catalog:"
                        + $"{Environment.NewLine}{string.Join(Environment.NewLine, prefilteredAway)}");
                }

                alreadyShown += CountStatus(
                    tokenTree,
                    CallTreeStatus.AlreadyShown);
                truncated += CountStatus(
                    tokenTree,
                    CallTreeStatus.Truncated);
                loopEdges += CountLoopEdges(tokenTree);
                visited++;
            }
        }

        Assert.True(visited > 0, "expected to visit methods");
        Assert.True(alreadyShown > 0, "expected to compare revisit placement");
        Assert.True(truncated > 0, "expected to compare budget truncation");
        Assert.True(loopEdges > 0, "expected to compare loop-edge retention");
        Assert.True(
            mismatches.Count == 0,
            $"{mismatches.Count} mismatches:{Environment.NewLine}"
                + string.Join(
                    $"{Environment.NewLine}---{Environment.NewLine}",
                    mismatches));

        static int CountStatus(
            CallTreeNode node,
            CallTreeStatus status) =>
            (node.Status == status ? 1 : 0)
                + node.Children.Sum(child => CountStatus(child, status));

        static int CountLoopEdges(CallTreeNode node) =>
            (node.Perf?.InLoop == true ? 1 : 0)
                + node.Children.Sum(CountLoopEdges);
    }

    // #3331: the same claim against the cross-assembly fixtures the matcher tests use — an assembly
    // that cannot reference the target contributes no edges, so skipping it before opening it must
    // produce an identical tree.
    [Fact]
    public void BuildCallerTree_SkippingANonContributingScopeAssemblyDoesNotChangeTheTree()
    {
        var targetIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        var caller = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        var lookalike = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphLookalikeCaller.AssemblyPath());
        var ping = targetIndex.Methods.First(method => method.Name == "Ping");

        var unfiltered = targetIndex.BuildCallerTree(ping.MetadataToken, new[] { caller, lookalike }, maxDepth: 2, maxNodes: 50);
        var prefiltered = targetIndex.BuildCallerTree(ping.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50);

        Assert.Equal(FlattenCallTree(unfiltered), FlattenCallTree(prefiltered));

        // And when the prefilter removes every scope assembly, the result must still match the walk
        // that opened them all and found nothing.
        var onlyNonContributing = targetIndex.BuildCallerTree(ping.MetadataToken, new[] { lookalike }, maxDepth: 2, maxNodes: 50);
        var allFilteredOut = targetIndex.BuildCallerTree(ping.MetadataToken, Array.Empty<LibraryBodyIndex>(), maxDepth: 2, maxNodes: 50);

        Assert.Equal(FlattenCallTree(onlyNonContributing), FlattenCallTree(allFilteredOut));
    }

    [Fact]
    public void BuildCallerTree_WithScope_OmitsCallersOfSameNameMemberInAnotherAssembly()
    {
        // Root the caller graph at the real Target.Api.Ping. One scope (CallerGraphCaller) calls
        // the real target; the other (CallerGraphLookalikeCaller) calls its own in-assembly
        // Target.Api.Ping lookalike with the same fully-qualified name. Only the real caller may
        // be reported — the cross-assembly key must carry callee assembly identity (#1579).
        var targetIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        var realCaller = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        var lookalikeCaller = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphLookalikeCaller.AssemblyPath());

        var ping = targetIndex.Methods.First(method => method.DeclaringType.Name == "Api" && method.Name == "Ping");
        var tree = targetIndex.BuildCallerTree(ping.MetadataToken, new[] { realCaller, lookalikeCaller }, maxDepth: 2, maxNodes: 50);

        var sources = tree.Children.Select(child => child.Perf?.Source).ToList();
        Assert.Contains("ILInspector.Analysis.CallerGraphCaller", sources);
        Assert.DoesNotContain("ILInspector.Analysis.CallerGraphLookalikeCaller", sources);
        Assert.Single(tree.Children);
    }

    [Fact]
    public void BuildCallerTree_WithScope_KeepsSameSignatureCallersFromDifferentAssembliesDistinct()
    {
        // Two caller assemblies declare the identical Shared.Entry.Run signature and both call the
        // real Target.Api.Ping. They must remain two distinct direct caller nodes; a key that omits
        // the caller source assembly collapses them into one (#1579).
        var targetIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        var caller = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        var twin = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCallerTwin.AssemblyPath());

        var ping = targetIndex.Methods.First(method => method.DeclaringType.Name == "Api" && method.Name == "Ping");
        var tree = targetIndex.BuildCallerTree(ping.MetadataToken, new[] { caller, twin }, maxDepth: 2, maxNodes: 50);

        var sources = tree.Children.Select(child => child.Perf?.Source).ToList();
        Assert.Equal(2, tree.Children.Length);
        Assert.Contains("ILInspector.Analysis.CallerGraphCaller", sources);
        Assert.Contains("ILInspector.Analysis.CallerGraphCallerTwin", sources);
    }

    [Fact]
    public void BuildCallerTree_WithScope_KeepsTargetOverloadsDistinct()
    {
        // Root the caller graph at the int overload of Target.Api.Ping. The caller assembly
        // invokes the int and string overloads from separate methods (RunInt/RunString). Only
        // RunInt may be reported: catalog correspondence must carry parameter types, or the two
        // overloads collapse and cross-link callers
        // (#1623 rung 1; non-vacuous because same-assembly resolution is token-based).
        var targetIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        var caller = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());

        var intOverload = targetIndex.Methods.Single(method =>
            method.DeclaringType.Name == "Api" && method.Name == "Ping"
            && method.ParameterTypes.Length == 1 && method.ParameterTypes[0].Equals(TypeRef.CoreLib("System", "Int32")));

        var tree = targetIndex.BuildCallerTree(intOverload.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50);

        var callerNames = tree.Children.Select(child => child.Member.Name).ToList();
        Assert.Contains("RunInt", callerNames);
        Assert.DoesNotContain("RunString", callerNames);
        Assert.DoesNotContain("Run", callerNames);
    }

    [Fact]
    public void BuildCallerTree_WithScope_LinksConstructedGenericTypeMemberCaller()
    {
        // Root the caller graph at the open Box<T>.Store(T). Another assembly calls
        // Box<int>.Store(1) — a member reference keyed on the List<int>-style instantiation. The
        // cross-assembly reverse map must normalize that constructed declaring type to its open
        // definition so the caller is reported (#1339); before, it under-reported as zero.
        var targetIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        var caller = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());

        var store = targetIndex.Methods.First(method =>
            method.DeclaringType.Name == "Box`1" && method.Name == "Store"
            && method.ParameterTypes[0].Kind == TypeRefKind.GenericParameter);
        var tree = targetIndex.BuildCallerTree(store.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50);

        var callerNames = tree.Children.Select(child => child.Member.Name).ToList();
        Assert.Contains("UseBox", callerNames);
        Assert.Contains("ILInspector.Analysis.CallerGraphCaller", tree.Children.Select(child => child.Perf?.Source));
    }

    // #1731: Box<T>.Store(T) and Box<T>.Store(List<T>) share name and arity on the same
    // generic declaring type. Cross-assembly caller graph identity must keep them distinct
    // — rooting at one overload reports only its own constructed caller, not the other's.
    // The prior arity-only erasure (open declaring + name + parameter count) collapsed them
    // and cross-linked the callers, which de-verified #1623 rung 1.
    [Fact]
    public void BuildCallerTree_WithScope_KeepsSameArityGenericOverloadsDistinct()
    {
        var targetIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        var caller = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());

        var storeValue = targetIndex.Methods.First(method =>
            method.DeclaringType.Name == "Box`1" && method.Name == "Store"
            && method.ParameterTypes[0].Kind == TypeRefKind.GenericParameter);
        var storeList = targetIndex.Methods.First(method =>
            method.DeclaringType.Name == "Box`1" && method.Name == "Store"
            && method.ParameterTypes[0].Kind == TypeRefKind.GenericInstance);

        var valueCallers = targetIndex.BuildCallerTree(storeValue.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50)
            .Children.Select(child => child.Member.Name).ToList();
        var listCallers = targetIndex.BuildCallerTree(storeList.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50)
            .Children.Select(child => child.Member.Name).ToList();

        Assert.Contains("UseBox", valueCallers);
        Assert.DoesNotContain("UseBoxList", valueCallers);
        Assert.Contains("UseBoxList", listCallers);
        Assert.DoesNotContain("UseBox", listCallers);
    }

    // #1741 (review): Box<T> (Box`1) and Box<T1,T2> (Box`2) share a simple name but have
    // different generic arity, each with a same-name/same-arity Store. The declaring-type
    // portion of the caller-graph key must preserve arity so the two Stores stay distinct.
    [Fact]
    public void BuildCallerTree_WithScope_KeepsSameNameGenericTypesOfDifferentArityDistinct()
    {
        var targetIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        var caller = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());

        var box1Store = targetIndex.Methods.First(method =>
            method.DeclaringType.Name == "Box`1" && method.Name == "Store"
            && method.ParameterTypes[0].Kind == TypeRefKind.GenericParameter);
        var box2Store = targetIndex.Methods.First(method =>
            method.DeclaringType.Name == "Box`2" && method.Name == "Store");

        var box1Callers = targetIndex.BuildCallerTree(box1Store.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50)
            .Children.Select(child => child.Member.Name).ToList();
        var box2Callers = targetIndex.BuildCallerTree(box2Store.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50)
            .Children.Select(child => child.Member.Name).ToList();

        Assert.Contains("UseBox", box1Callers);
        Assert.DoesNotContain("UseBox2", box1Callers);
        Assert.Contains("UseBox2", box2Callers);
        Assert.DoesNotContain("UseBox", box2Callers);
    }

    // #1731 (adversarial review): the cross-assembly caller-graph shape keys on the OPEN
    // signature (VAR/MVAR markers), not the substituted concrete types. A constructed
    // Box<int>.Store(T) call carries a concrete int as its instantiated parameter, but the
    // open signature retains the type-parameter marker — so a type-parameter instantiation
    // stays distinct from a literal of the same type, and the shape matches the open target.
    [Fact]
    public void ResolvedCall_PreservesOpenGenericMarker_DistinctFromInstantiatedParameter()
    {
        var caller = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());

        var storeValueCall = caller.DirectCalls.First(call =>
            call.Callee.Name == "Store" && call.Callee.ParameterTypes[0].Kind == TypeRefKind.Definition);

        // Instantiated parameter is the concrete int; the open signature keeps the marker.
        Assert.Equal(TypeRefKind.Definition, storeValueCall.Callee.ParameterTypes[0].Kind);
        Assert.Equal(TypeRefKind.GenericParameter, storeValueCall.Callee.OpenSignatureParameters[0].Kind);
    }

    // #1731 (adversarial review, finding B): the erased generic shape qualifies a generic
    // instance by namespace, so N1.Foo<T> and N2.Foo<T> (same metadata name, different
    // namespace) do not collapse.
    [Fact]
    public void ErasedParameterShape_QualifiesGenericInstanceByNamespace()
    {
        var typeParameter = TypeRef.GenericParameter(0, "T");
        var foo1 = TypeRef.GenericInstance(TypeRef.Definition("AsmA", "N1", "Foo`1"), [typeParameter]);
        var foo2 = TypeRef.GenericInstance(TypeRef.Definition("AsmB", "N2", "Foo`1"), [typeParameter]);

        Assert.NotEqual(
            GenericMemberIdentity.ErasedParameterShape([foo1]),
            GenericMemberIdentity.ErasedParameterShape([foo2]));
    }

    [Fact]
    public void BuildCallerTree_WithScope_LinksConstructedGenericMethodCaller()
    {
        // Root the caller graph at the open Echo<T>(T). Another assembly calls Echo<int>(1) — a
        // MethodSpec keyed on the instantiation. Normalizing generic method identity to the open
        // definition + parameter arity links the caller across assemblies (#1339).
        var targetIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        var caller = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());

        var echo = targetIndex.Methods.First(method =>
            method.DeclaringType.Name == "GenericApi" && method.Name == "Echo");
        var tree = targetIndex.BuildCallerTree(echo.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50);

        var callerNames = tree.Children.Select(child => child.Member.Name).ToList();
        Assert.Contains("UseEcho", callerNames);
    }

    // #1741: two Ping overloads whose parameter types share the FQN Shared.Token but come
    // from different assemblies (DiffAsmLibA vs DiffAsmLibB). The cross-assembly caller
    // graph must keep them distinct — rooting at one overload reports only its own caller.
    // The prior key rendered parameter types with ToQualifiedDisplayString(), which omits
    // assembly, so both collapsed and cross-linked the callers (de-verified #1623 rung 1).
    [Fact]
    public void BuildCallerTree_WithScope_KeepsSameFqnParametersFromDifferentAssembliesDistinct()
    {
        var target = LibraryBodyIndex.Open(FixtureCatalog.DiffAsmTarget.AssemblyPath());
        var caller = LibraryBodyIndex.Open(FixtureCatalog.DiffAsmCaller.AssemblyPath());

        var pingA = target.Methods.First(method =>
            method.Name == "Ping" && method.ParameterTypes[0].Assembly == "DiffAsmLibA");
        var pingB = target.Methods.First(method =>
            method.Name == "Ping" && method.ParameterTypes[0].Assembly == "DiffAsmLibB");

        var aCallers = target.BuildCallerTree(pingA.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50)
            .Children.Select(child => child.Member.Name).ToList();
        var bCallers = target.BuildCallerTree(pingB.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50)
            .Children.Select(child => child.Member.Name).ToList();

        Assert.Contains("UseA", aCallers);
        Assert.DoesNotContain("UseB", aCallers);
        Assert.Contains("UseB", bCallers);
        Assert.DoesNotContain("UseA", bCallers);
    }

    // #3266: the forward mirror of BuildCallerTree(scopes). A single-assembly callee tree stops
    // at the assembly boundary (the callee is an External leaf); scoping the callee's assembly
    // must expand it and tag it with its source assembly.
    [Fact]
    public void BuildCallTree_WithScope_IncorporatesAndTagsExternalCallees()
    {
        var callerIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        var targetIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        var targetAssemblyName = targetIndex.Methods.First().AssemblyName;

        var run = callerIndex.Methods.First(method =>
            method.DeclaringType.Name == "Entry" && method.Name == "Run" && method.ParameterTypes.Length == 0);

        var scoped = callerIndex.BuildCallTree(run.MetadataToken, new[] { targetIndex }, maxDepth: 2, maxNodes: 50);
        var unscoped = callerIndex.BuildCallTree(run.MetadataToken, maxDepth: 2, maxNodes: 50);

        var scopedPing = Assert.Single(scoped.Children, child => child.Member.Name == "Ping");
        Assert.NotEqual(CallTreeStatus.External, scopedPing.Status);
        Assert.Equal(targetAssemblyName, scopedPing.Perf?.Source);

        // The single-assembly tree still lists the callee, but as an untagged external leaf.
        var unscopedPing = Assert.Single(unscoped.Children, child => child.Member.Name == "Ping");
        Assert.Equal(CallTreeStatus.External, unscopedPing.Status);
        Assert.Null(unscopedPing.Perf?.Source);
    }

    // #3266: with the callee's assembly in scope, a callee chain deepens across the package
    // boundary — RunOuter -> Run (same assembly) -> Target.Api.Ping (another assembly).
    [Fact]
    public void BuildCallTree_WithScope_ExpandsCalleeChainAcrossAssemblyBoundary()
    {
        var callerIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        var targetIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        var targetAssemblyName = targetIndex.Methods.First().AssemblyName;

        var runOuter = callerIndex.Methods.First(method => method.Name == "RunOuter");
        var tree = callerIndex.BuildCallTree(runOuter.MetadataToken, new[] { targetIndex }, maxDepth: 3, maxNodes: 50);

        var run = Assert.Single(tree.Children, child => child.Member.Name == "Run");
        Assert.Null(run.Perf?.Source); // same assembly as the root, so not tagged external
        var ping = Assert.Single(run.Children, child => child.Member.Name == "Ping");
        Assert.Equal(targetAssemblyName, ping.Perf?.Source);
    }

    // #3266: a constructed-generic callee (Echo<int>, a MethodSpec) is pulled into scope and
    // resolved against the open Echo<T> definition rather than left as an external leaf. Generic
    // open-signature normalization is shared with the reverse traversal.
    [Fact]
    public void BuildCallTree_WithScope_ResolvesConstructedGenericCallee()
    {
        var callerIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        var targetIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        var targetAssemblyName = targetIndex.Methods.First().AssemblyName;

        var useEcho = callerIndex.Methods.First(method => method.Name == "UseEcho");

        var scoped = callerIndex.BuildCallTree(useEcho.MetadataToken, new[] { targetIndex }, maxDepth: 2, maxNodes: 50);
        var unscoped = callerIndex.BuildCallTree(useEcho.MetadataToken, maxDepth: 2, maxNodes: 50);

        var scopedEcho = Assert.Single(scoped.Children, child => child.Member.Name == "Echo");
        Assert.NotEqual(CallTreeStatus.External, scopedEcho.Status);
        Assert.Equal(targetAssemblyName, scopedEcho.Perf?.Source);

        var unscopedEcho = Assert.Single(unscoped.Children, child => child.Member.Name == "Echo");
        Assert.Equal(CallTreeStatus.External, unscopedEcho.Status);
    }

    // #3266: children are keyed and ordered by structural identity, so an irrelevant scope's
    // position in the list cannot reorder or duplicate the graph.
    [Fact]
    public void BuildCallTree_WithScope_IsDeterministicAcrossScopeOrder()
    {
        var callerIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        var targetIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        var twinIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCallerTwin.AssemblyPath());

        var useBox = callerIndex.Methods.First(method => method.Name == "UseBox");

        var forward = callerIndex.BuildCallTree(useBox.MetadataToken, new[] { targetIndex, twinIndex }, maxDepth: 3, maxNodes: 50);
        var reversed = callerIndex.BuildCallTree(useBox.MetadataToken, new[] { twinIndex, targetIndex }, maxDepth: 3, maxNodes: 50);

        Assert.Equal(FlattenCallTree(forward), FlattenCallTree(reversed));
    }

    // #3266: with no scopes the multi-assembly overload falls back to the single-assembly builder.
    [Fact]
    public void BuildCallTree_WithEmptyScope_MatchesSingleAssemblyBuilder()
    {
        var callerIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        var useBox = callerIndex.Methods.First(method => method.Name == "UseBox");

        var single = callerIndex.BuildCallTree(useBox.MetadataToken, maxDepth: 3, maxNodes: 50);
        var fallback = callerIndex.BuildCallTree(useBox.MetadataToken, Array.Empty<LibraryBodyIndex>(), maxDepth: 3, maxNodes: 50);

        Assert.Equal(FlattenCallTree(single), FlattenCallTree(fallback));
    }

    // #3266 (review): a callee whose defining assembly is not in scope stays External even with a
    // non-empty scope list — Run -> Ping with only an unrelated caller assembly scoped keeps Ping
    // External (its own body was never decoded), not a false Leaf.
    [Fact]
    public void BuildCallTree_WithScope_MarksUndecodedExternalCalleeAsExternal()
    {
        var callerIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        var twinIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCallerTwin.AssemblyPath());
        var targetAssemblyName = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath())
            .Methods.First().AssemblyName;

        var run = callerIndex.Methods.First(method =>
            method.DeclaringType.Name == "Entry" && method.Name == "Run" && method.ParameterTypes.Length == 0);

        // The twin scope does not define Target.Api.Ping, so Ping's body is never decoded.
        var tree = callerIndex.BuildCallTree(run.MetadataToken, new[] { twinIndex }, maxDepth: 3, maxNodes: 50);

        var ping = Assert.Single(tree.Children, child => child.Member.Name == "Ping");
        Assert.Equal(CallTreeStatus.External, ping.Status);
        Assert.Equal(targetAssemblyName, ping.Perf?.Source);
    }

    // #3266 (review): fan-out reports the true outbound call-site count, independent of the
    // deduplication that collapses repeat call sites to one child — RunTwice calls Echo twice.
    [Fact]
    public void BuildCallTree_WithScope_ReportsCallSiteFanoutForRepeatedCallee()
    {
        var callerIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        var targetIndex = LibraryBodyIndex.Open(FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());

        var runTwice = callerIndex.Methods.First(method => method.Name == "RunTwice");
        var tree = callerIndex.BuildCallTree(runTwice.MetadataToken, new[] { targetIndex }, maxDepth: 2, maxNodes: 50);

        Assert.Single(tree.Children, child => child.Member.Name == "Echo");
        Assert.Equal(2, tree.Perf?.Fanout);
    }

    // #1741 (unit): the key fragment for a type includes its assembly, so same-FQN types
    // from different assemblies do not collapse.
    [Fact]
    public void KeyFragment_QualifiesNamedTypeByAssembly()
    {
        var tokenA = TypeRef.Definition("LibA", "Shared", "Token");
        var tokenB = TypeRef.Definition("LibB", "Shared", "Token");

        Assert.NotEqual(GenericMemberIdentity.KeyFragment(tokenA), GenericMemberIdentity.KeyFragment(tokenB));
    }

    [Fact]
    public void TopLeverage_RanksMostCalledMethodFirst()
    {
        var index = LibraryBodyIndex.Open(typeof(LeverageFixtures).Assembly.Location);

        var ranked = index.TopLeverage(count: 5, scope: InLeverageFixtures);

        var top = ranked[0];
        Assert.Equal(nameof(LeverageFixtures.Hot), top.Method.Name);
        // Called directly by A, B, C, and Fanned (the in-loop call site).
        Assert.Equal(4, top.DirectCallerCount);
    }

    [Fact]
    public void TopLeverage_CountsFanoutAndLoopCalls()
    {
        var index = LibraryBodyIndex.Open(typeof(LeverageFixtures).Assembly.Location);

        var ranked = index.TopLeverage(count: 25, scope: InLeverageFixtures);

        var fanned = Assert.Single(ranked.Where(entry => entry.Method.Name == nameof(LeverageFixtures.Fanned)));
        // Calls A, B, C, and Hot — at least four outbound call sites, one in a loop.
        Assert.True(fanned.Fanout >= 4, $"expected fanout >= 4, got {fanned.Fanout}");
        Assert.True(fanned.LoopCallCount >= 1, $"expected loop calls >= 1, got {fanned.LoopCallCount}");
        Assert.True(fanned.MaxDepth >= 2, $"expected depth >= 2, got {fanned.MaxDepth}");
    }

    [Fact]
    public void TopLeverage_ScopeRestrictsRankedMethods()
    {
        var index = LibraryBodyIndex.Open(typeof(LeverageFixtures).Assembly.Location);

        var ranked = index.TopLeverage(count: 100, scope: InLeverageFixtures);

        Assert.NotEmpty(ranked);
        Assert.All(ranked, entry => Assert.Equal(nameof(LeverageFixtures), entry.Method.DeclaringType.Name));
    }

    [Fact]
    public void TopLeverage_ReportsTrueChainDepth_StableAcrossMethodOrder()
    {
        var index = LibraryBodyIndex.Open(typeof(LeverageDepthFixtures).Assembly.Location);

        var ranked = index.TopLeverage(count: 100, scope: method => method.DeclaringType.Name == nameof(LeverageDepthFixtures));
        var byName = ranked.ToDictionary(entry => entry.Method.Name, entry => entry.MaxDepth);

        // ChainTop -> ChainMid -> ChainLeaf is a three-method chain.
        Assert.Equal(3, byName[nameof(LeverageDepthFixtures.ChainTop)]);
        Assert.Equal(2, byName[nameof(LeverageDepthFixtures.ChainMid)]);
        Assert.Equal(1, byName[nameof(LeverageDepthFixtures.ChainLeaf)]);

        // Pong -> ChainTop -> ChainMid -> ChainLeaf is the longest acyclic path from
        // Pong (the Pong <-> Ping back-edge is cut); Ping prepends one more hop.
        Assert.Equal(4, byName[nameof(LeverageDepthFixtures.Pong)]);
        Assert.Equal(5, byName[nameof(LeverageDepthFixtures.Ping)]);
    }

    [Fact]
    public void TopLeverage_CountsDistinctRootReach()
    {
        var index = LibraryBodyIndex.Open(typeof(LeverageRootReachFixtures).Assembly.Location);

        var ranked = index.TopLeverage(count: 100, scope: method => method.DeclaringType.Name == nameof(LeverageRootReachFixtures));
        var byName = ranked.ToDictionary(entry => entry.Method.Name);

        // Root1 -> Funnel -> Single and Root2 -> Funnel -> Single. Root1/Root2 have no
        // in-assembly caller, so each is a root reaching only itself.
        Assert.Equal(1, byName[nameof(LeverageRootReachFixtures.Root1)].RootReach);
        Assert.Equal(1, byName[nameof(LeverageRootReachFixtures.Root2)].RootReach);

        // Funnel is reached from both roots: RootReach 2 matches its direct fanin of 2.
        Assert.Equal(2, byName[nameof(LeverageRootReachFixtures.Funnel)].DirectCallerCount);
        Assert.Equal(2, byName[nameof(LeverageRootReachFixtures.Funnel)].RootReach);

        // Single has a single direct caller (Funnel) yet is reached from two roots, so
        // RootReach (2) exceeds direct fanin (1): the inbound-scale signal Root Reach adds.
        Assert.Equal(1, byName[nameof(LeverageRootReachFixtures.Single)].DirectCallerCount);
        Assert.Equal(2, byName[nameof(LeverageRootReachFixtures.Single)].RootReach);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void TopLeverage_TreatsSelfRecursiveEntryAsRoot()
    {
        var index = LibraryBodyIndex.Open(typeof(LeverageSelfRootFixtures).Assembly.Location);

        var ranked = index.TopLeverage(count: 100, scope: method => method.DeclaringType.Name == nameof(LeverageSelfRootFixtures));
        var byName = ranked.ToDictionary(entry => entry.Method.Name);

        // SelfRoot recurses, so it carries a self-edge in the reverse graph, but it has no
        // other in-assembly caller: it must still count as a root (ignoring the self-edge).
        Assert.Equal(1, byName[nameof(LeverageSelfRootFixtures.SelfRoot)].RootReach);
        // Helper is reached only from SelfRoot; it would score zero if SelfRoot were
        // misclassified as a non-root.
        Assert.Equal(1, byName[nameof(LeverageSelfRootFixtures.Helper)].RootReach);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void TopLeverage_TreatsMutuallyRecursiveEntryComponentAsRoot()
    {
        var index = LibraryBodyIndex.Open(typeof(LeverageDepthFixtures).Assembly.Location);

        var ranked = index.TopLeverage(count: 100, scope: method => method.DeclaringType.Name == nameof(LeverageDepthFixtures));
        var byName = ranked.ToDictionary(entry => entry.Method.Name);

        Assert.Equal(1, byName[nameof(LeverageDepthFixtures.Ping)].RootReach);
        Assert.Equal(1, byName[nameof(LeverageDepthFixtures.Pong)].RootReach);
        Assert.Equal(1, byName[nameof(LeverageDepthFixtures.ChainTop)].RootReach);
        Assert.Equal(1, byName[nameof(LeverageDepthFixtures.ChainLeaf)].RootReach);
    }

    [Fact]
    public void TopLeverage_CountsCallerOfIntraAssemblyGenericMethod()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var ranked = index.TopLeverage(count: 200,
            scope: method => method.DeclaringType.Name == nameof(CallSiteFixtures));

        // GenericEcho is invoked once, via a MethodSpec operand, by CallsGenericEcho.
        var echo = Assert.Single(ranked.Where(entry => entry.Method.Name == nameof(CallSiteFixtures.GenericEcho)));
        Assert.Equal(1, echo.DirectCallerCount);
    }

    [Fact]
    public void TopLeverage_CountsCallerOfConstructedGenericDeclaringType()
    {
        var index = LibraryBodyIndex.Open(typeof(GenericDeclaringCallers).Assembly.Location);

        var ranked = index.TopLeverage(count: 200,
            scope: method => method.DeclaringType.Name == "GenericDeclaringTarget`1");

        var target = Assert.Single(ranked.Where(entry => entry.Method.Name == nameof(GenericDeclaringTarget<int>.Target)));
        Assert.Equal(1, target.DirectCallerCount);
        Assert.Equal(1, target.RootReach);

        var targetWithParameter = Assert.Single(ranked.Where(entry => entry.Method.Name == nameof(GenericDeclaringTarget<int>.TargetWithParameter)));
        Assert.Equal(1, targetWithParameter.DirectCallerCount);
    }

    [Fact]
    public void BuildCallerTree_LinksConstructedGenericDeclaringTypeCaller()
    {
        var index = LibraryBodyIndex.Open(typeof(GenericDeclaringCallers).Assembly.Location);
        var target = Assert.Single(index.Methods.Where(method =>
            method.DeclaringType.Name == "GenericDeclaringTarget`1"
            && method.Name == nameof(GenericDeclaringTarget<int>.Target)));

        var tree = index.BuildCallerTree(target.MetadataToken, maxDepth: 2, maxNodes: 20);

        Assert.Contains(tree.Children, child =>
            child.Member.Name == nameof(GenericDeclaringCallers.CallGenericTarget));
    }

    [Fact]
    public void BuildCallTree_LinksConstructedGenericDeclaringTypeCallee()
    {
        var index = LibraryBodyIndex.Open(typeof(GenericDeclaringCallers).Assembly.Location);
        var caller = Assert.Single(index.Methods.Where(method =>
            method.Name == nameof(GenericDeclaringCallers.CallGenericTarget)));

        var tree = index.BuildCallTree(caller.MetadataToken, maxDepth: 2, maxNodes: 20);

        var child = Assert.Single(tree.Children.Where(node =>
            node.Member.Name == nameof(GenericDeclaringTarget<int>.Target)));
        Assert.Equal(CallTreeStatus.Leaf, child.Status);
        Assert.Equal("ILInspector.Analysis.Tests.GenericDeclaringTarget<int>", child.Member.DeclaringType.ToQualifiedDisplayString());
    }

    // #1623 rung 4: the FORWARD call tree must mark a callee edge invoked inside a loop.
    // The reverse caller tree already pins this (BuildCallerTree_MarksCallerNodeInLoop);
    // BuildCallTree forwards edge.InLoop but had no tree-level guard.
    [Fact]
    public void BuildCallTree_MarksCalleeNodeInLoop_WhenInvokedInLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);
        int root = index.Methods.First(m => m.Name == nameof(CallSiteFixtures.CallsConsoleWriteLineInLoop)).MetadataToken;

        var tree = index.BuildCallTree(root, maxDepth: 2, maxNodes: 50);

        var child = Assert.Single(tree.Children.Where(c => c.Member.Name == nameof(CallSiteFixtures.CallsConsoleWriteLine)));
        Assert.True(child.Perf?.InLoop, "forward call-tree must mark the in-loop callee edge");
    }

    // #1623 rung 4: ranking stability. Two methods tied on every leverage metric
    // (caller-count / root-reach / fanout / loop-calls) must order deterministically by
    // metadata token, and that order must not depend on the input method order. Guards the
    // final ThenBy(MetadataToken) tie-break, which was unguarded.
    [Fact]
    public void TopLeverage_TieBreak_IsDeterministicAndStableAcrossInputOrder()
    {
        var root = LeverageMethod("Root", 0x06000010);
        var tieA = LeverageMethod("TieA", 0x06000001);
        var tieB = LeverageMethod("TieB", 0x06000002);
        // Root calls each tied method exactly once -> equal caller-count(1)/reach(1)/fanout(0)/loop(0).
        var calls = ImmutableArray.Create(LeverageCall(root, tieA), LeverageCall(root, tieB));

        var forward = MethodLeverageRanking.Top(calls, [root, tieA, tieB], count: 10)
            .Select(e => e.Method.Name).Where(n => n is "TieA" or "TieB").ToList();
        var reversed = MethodLeverageRanking.Top(calls, [tieB, tieA, root], count: 10)
            .Select(e => e.Method.Name).Where(n => n is "TieA" or "TieB").ToList();

        Assert.Equal(["TieA", "TieB"], forward);   // deterministic: lower token first
        Assert.Equal(forward, reversed);            // stable across input order
    }

    [Fact]
    public void TopUnsafeLeverage_CountsCallerOfConstructedGenericDeclaringType()
    {
        var index = LibraryBodyIndex.Open(typeof(GenericDeclaringCallers).Assembly.Location);

        var unsafeTarget = Assert.Single(index.TopUnsafeLeverage(count: 100).Where(entry =>
            entry.Method.DeclaringType.Name == "GenericUnsafeTarget`1"
            && entry.Method.Name == nameof(GenericUnsafeTarget<int>.UnsafeTarget)));

        Assert.Equal(1, unsafeTarget.DirectCallerCount);
    }

    [Fact]
    public void MemberReferences_InstantiateGenericDeclaringTypeArguments()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var call = Assert.Single(index.DirectCalls.Where(c =>
            c.Caller.Name == nameof(CallSiteFixtures.CallsListAdd)
            && c.Callee.Name == "Add"));

        Assert.Equal("System.Collections.Generic.List<int>", call.Callee.DeclaringType.ToQualifiedDisplayString());
        Assert.Equal(TypeRef.CoreLib("System", "Int32"), Assert.Single(call.Callee.ParameterTypes));
    }

    [Fact]
    public void TypeIdentity_CanonicalizesCoreLibraryFacadeAssemblies()
    {
        Assert.Equal(
            TypeRef.CoreLib("System", "String"),
            TypeRef.Definition("System.Runtime", "System", "String"));
    }

    [Fact]
    public void DisplayStrings_RenderDecimalKeyword()
    {
        Assert.Equal("decimal", TypeRef.CoreLib("System", "Decimal").ToDisplayString());
    }

    [Fact]
    public void FindCalls_CanMatchFullParameterShape()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var calls = index.FindCalls(MemberPattern.Method(
            TypeRef.Definition("System.Console", "System", "Console"),
            "WriteLine",
            ImmutableArray.Create(TypeRef.CoreLib("System", "String"))));

        Assert.Contains(calls, c => c.Caller.Name == nameof(CallSiteFixtures.CallsConsoleWriteLine));
    }

    [Fact]
    public void BuildCallerTree_OverloadResolvesToOwnDefinition()
    {
        var index = LibraryBodyIndex.Open(typeof(OverloadTargets).Assembly.Location);

        var intOverload = Assert.Single(index.Methods.Where(method =>
            method.DeclaringType.Name == nameof(OverloadTargets) && method.Name == nameof(OverloadTargets.M)
            && method.ParameterTypes.Length == 1 && method.ParameterTypes[0].Equals(TypeRef.CoreLib("System", "Int32"))));
        var stringOverload = Assert.Single(index.Methods.Where(method =>
            method.DeclaringType.Name == nameof(OverloadTargets) && method.Name == nameof(OverloadTargets.M)
            && method.ParameterTypes.Length == 1 && method.ParameterTypes[0].Equals(TypeRef.CoreLib("System", "String"))));

        // Distinct definitions...
        Assert.NotEqual(intOverload.MetadataToken, stringOverload.MetadataToken);

        // ...and the caller graph for one overload does not pull in the other.
        var tree = index.BuildCallerTree(intOverload.MetadataToken, maxDepth: 2, maxNodes: 20);
        Assert.Contains(tree.Children, child => child.Member.Name == nameof(OverloadCallers.CallsEachOverloadOnce));
    }


    [Fact]
    public void TopUnsafeLeverage_RanksRequiresUnsafeMethodsByCallers()
    {
        var index = LibraryBodyIndex.Open(typeof(UnsafeEvidenceFixtures).Assembly.Location);

        var top = index.TopUnsafeLeverage(count: 100);

        Assert.Contains(top, e =>
            e.Method.Name == nameof(UnsafeEvidenceFixtures.UnsafePointerRead)
            && e.Mode == CallerUnsafeMode.Implicit);
        var pointerExtern = Assert.Single(top.Where(e =>
            e.Method.Name == nameof(UnsafeEvidenceFixtures.PointerExtern)));
        Assert.Equal(2, pointerExtern.DirectCallerCount);
        Assert.Equal(CallerUnsafeMode.Implicit, pointerExtern.Mode);
        Assert.DoesNotContain(top, e => e.Method.Name == nameof(UnsafeEvidenceFixtures.CallsUnsafeAs));
    }

    [Fact]
    public void CallTreeFanInCountsDistinctCallersNotCallSites()
    {
        var (path, directory) = BuildRepeatedCallSiteFixture();
        try
        {
            var index = LibraryBodyIndex.Open(path);
            var target = index.Methods.First(method => method.Name == "Target");
            var caller = index.Methods.First(method => method.Name == "CallsTargetTwice");

            var tree = index.BuildCallTree(caller.MetadataToken, maxDepth: 2, maxNodes: 50);
            var targetNode = tree.Children.First(child => child.Member.Name == "Target");

            // Three call sites reach Target, but only two distinct methods do. Fan-in is a
            // leverage cue and the reverse graph draws one edge per distinct caller, so the
            // number has to agree with the edges rather than count repeated sites.
            Assert.Equal(3, index.DirectCalls.Count(call => call.Callee.Name == "Target"));
            Assert.Equal(2, targetNode.Perf?.Fanin);
            Assert.NotNull(target);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
