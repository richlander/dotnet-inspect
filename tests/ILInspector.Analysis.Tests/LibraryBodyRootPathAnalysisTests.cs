using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;

using DotnetInspector.Fixtures;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Analysis.Tests;

public class LibraryBodyRootPathAnalysisTests
{
    static readonly LibraryBodyRootPathLimits s_generousLimits =
        new(
            MaximumDepth: 16,
            MaximumNodes: 10_000,
            MaximumEdges: 100_000,
            MaximumPaths: 1_000);

    [Fact]
    public void FindShortestPaths_UsesShortestStableLocalWitnesses()
    {
        LibraryCallGraphAnalysisResult callGraph = RootPathCallGraph();
        MethodIdentity create = RootPathMethod(callGraph, "Create");
        MethodIdentity createOuter =
            RootPathMethod(callGraph, "CreateOuter");
        MethodIdentity mapped =
            RootPathMethod(callGraph, "CreateMappedTextDiff");
        MethodIdentity addChange =
            RootPathMethod(callGraph, "AddChange");
        MethodIdentity cycleRoot =
            RootPathMethod(callGraph, "CycleRoot");
        MethodIdentity cycleUse =
            RootPathMethod(callGraph, "CycleUse");

        LibraryBodyRootPathResult result =
            LibraryBodyRootPathAnalysis.FindShortestPaths(
                callGraph,
                [
                    Address(createOuter),
                    Address(cycleRoot),
                    Address(create),
                ],
                [
                    Address(cycleUse),
                    Address(addChange),
                    Address(mapped),
                ],
                s_generousLimits);

        Assert.True(result.IsComplete);
        Assert.Equal(5, result.Witnesses.Length);
        Assert.Equal(
            ["Create", "CreateMappedTextDiff"],
            MethodNames(Witness(result, create, mapped)));
        Assert.Equal(
            ["CreateOuter", "Create", "CreateMappedTextDiff"],
            MethodNames(Witness(result, createOuter, mapped)));
        Assert.Equal(
            ["Create", "CreateMappedTextDiff", "AddChange"],
            MethodNames(Witness(result, create, addChange)));
        Assert.Equal(
            [
                "CreateOuter",
                "Create",
                "CreateMappedTextDiff",
                "AddChange",
            ],
            MethodNames(Witness(result, createOuter, addChange)));
        Assert.Equal(
            ["CycleRoot", "CycleA", "CycleB", "CycleUse"],
            MethodNames(Witness(result, cycleRoot, cycleUse)));

        Guid mvid = Guid.NewGuid();
        MethodIdentity root = Method(mvid, 1, "Root");
        MethodIdentity first = Method(mvid, 2, "First");
        MethodIdentity second = Method(mvid, 3, "Second");
        MethodIdentity destination = Method(mvid, 4, "Destination");
        LibraryCallGraphAnalysisResult equalPaths = CallGraph(
            [root, first, second, destination],
            [
                Call(root, second, 4),
                Call(root, first, 8),
                Call(second, destination, 12),
                Call(first, destination, 16),
            ]);

        LibraryBodyRootPathWitness stable =
            Assert.Single(
                LibraryBodyRootPathAnalysis.FindShortestPaths(
                    equalPaths,
                    [Address(root)],
                    [Address(destination)],
                    s_generousLimits)
                .Witnesses);

        Assert.Equal(
            ["Root", "First", "Destination"],
            MethodNames(stable));
    }

    [Fact]
    public void FindShortestPaths_DoesNotMaterializeCompatibilityIndex()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence));
        LibraryCallGraphAnalysisResult callGraph =
            execution.CallGraph;
        MethodIdentity create =
            RootPathMethod(callGraph, "Create");
        MethodIdentity mapped =
            RootPathMethod(callGraph, "CreateMappedTextDiff");

        Assert.False(execution.HasMaterializedCompatibilityIndex);

        LibraryBodyRootPathResult result =
            LibraryBodyRootPathAnalysis.FindShortestPaths(
                callGraph,
                [Address(create)],
                [Address(mapped)],
                s_generousLimits);

        Assert.Single(result.Witnesses);
        Assert.False(execution.HasMaterializedCompatibilityIndex);
    }

    [Fact]
    public void FindShortestPaths_PreservesPhysicalReceiptsAndSemanticCaller()
    {
        LibraryCallGraphAnalysisResult callGraph = RootPathCallGraph();
        MethodIdentity mapped =
            RootPathMethod(callGraph, "CreateMappedTextDiff");
        MethodIdentity addChange =
            RootPathMethod(callGraph, "AddChange");
        MethodIdentity asyncRoot =
            RootPathMethod(callGraph, "AsyncRoot");
        MethodIdentity asyncUse =
            RootPathMethod(callGraph, "AsyncUse");

        LibraryBodyRootPathResult result =
            LibraryBodyRootPathAnalysis.FindShortestPaths(
                callGraph,
                [
                    Address(mapped),
                    Address(asyncRoot),
                ],
                [
                    Address(addChange),
                    Address(asyncUse),
                ],
                s_generousLimits);

        LibraryBodyRootPathStep repeated = Assert.Single(
            Witness(result, mapped, addChange).Steps);
        Assert.Equal(2, repeated.CallSites.Length);
        Assert.Equal(
            repeated.CallSites
                .OrderBy(static call => call.ILOffset)
                .Select(static call => call.ILOffset),
            repeated.CallSites.Select(static call => call.ILOffset));

        LibraryBodyRootPathStep attributed = Assert.Single(
            Witness(result, asyncRoot, asyncUse).Steps);
        DirectCall physical = Assert.Single(attributed.CallSites);
        Assert.Equal(asyncRoot, attributed.Caller);
        Assert.Equal(asyncRoot, physical.Caller);
        Assert.NotEqual(
            physical.Caller.MetadataToken,
            physical.EvidenceMethod.MetadataToken);
        Assert.Equal("MoveNext", physical.EvidenceMethod.Name);

        LibraryCallGraphAnalysisResult iteratorCallGraph =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerGraphCallerTwin.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence))
            .CallGraph;
        DirectCall iteratorCall = iteratorCallGraph.DirectCalls.Single(
            call => call.Kind == CallKind.Call
                && call.Caller.Name == "MoveNext"
                && call.Caller.DeclaringType.Name.Contains(
                    "IteratorRoot",
                    StringComparison.Ordinal)
                && call.Callee.Name == "IteratorUse");
        MethodIdentity iteratorDestination =
            iteratorCallGraph.DeclaredMethods.Single(method =>
                method.MetadataToken
                == MethodDefinitionMap
                    .Create(iteratorCallGraph.DeclaredMethods)
                    .Resolve(iteratorCall));
        MethodIdentity iteratorRoot =
            iteratorCallGraph.DeclaredMethods.Single(method =>
                method.Name == "IteratorRoot");
        LibraryBodyRootPathResult iteratorResult =
            LibraryBodyRootPathAnalysis.FindShortestPaths(
                iteratorCallGraph,
                [Address(iteratorRoot)],
                [Address(iteratorDestination)],
                s_generousLimits);
        Assert.Empty(iteratorResult.Witnesses);
        Assert.True(
            Assert.Single(
                iteratorResult.Boundaries.OfType<
                    LibraryBodyRootPathBoundary
                        .UnattributedGeneratedBodies>())
            .Count > 0);
    }

    [Fact]
    public void FindShortestPaths_HonorsExactRootSetAndLocalParticipant()
    {
        LibraryCallGraphAnalysisResult callGraph = RootPathCallGraph();
        MethodIdentity getter = RootPathMethod(callGraph, "get_Value");
        MethodIdentity setter = RootPathMethod(callGraph, "set_Value");
        MethodIdentity use = RootPathMethod(callGraph, "AccessorUse");

        LibraryBodyRootPathResult publicRootsOnly =
            LibraryBodyRootPathAnalysis.FindShortestPaths(
                callGraph,
                [Address(getter)],
                [Address(use)],
                s_generousLimits);
        Assert.Equal(
            ["get_Value"],
            publicRootsOnly.Witnesses
                .Select(static witness => witness.Root.Name));

        LibraryBodyRootPathResult callerSelectedBoth =
            LibraryBodyRootPathAnalysis.FindShortestPaths(
                callGraph,
                [Address(setter), Address(getter)],
                [Address(use)],
                s_generousLimits);
        Assert.Equal(
            ["get_Value", "set_Value"],
            callerSelectedBoth.Witnesses
                .Select(static witness => witness.Root.Name));

        LibraryCallGraphAnalysisResult external =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerGraphIndirectCaller
                    .AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence))
            .CallGraph;
        MethodIdentity externalCaller = external.DeclaredMethods.Single(
            static method => method.Name == "RunRootPath");
        Assert.Contains(
            external.DirectCalls,
            static call =>
                call.Caller.Name == "RunRootPath"
                && call.Callee.Name == "Create");

        Assert.Throws<ArgumentException>(
            () => LibraryBodyRootPathAnalysis.FindShortestPaths(
                callGraph,
                [Address(externalCaller)],
                [Address(use)],
                s_generousLimits));
    }

    [Fact]
    public void FindShortestPaths_ReportsIndependentLimits()
    {
        Guid mvid = Guid.NewGuid();
        MethodIdentity root = Method(mvid, 1, "Root");
        MethodIdentity middle = Method(mvid, 2, "Middle");
        MethodIdentity destination = Method(mvid, 3, "Destination");
        LibraryCallGraphAnalysisResult line = CallGraph(
            [root, middle, destination],
            [
                Call(root, middle, 4),
                Call(middle, destination, 8),
            ]);

        LibraryBodyRootPathResult depthLimited =
            LibraryBodyRootPathAnalysis.FindShortestPaths(
                line,
                [Address(root)],
                [Address(middle), Address(destination)],
                new(1, 100, 100, 100));
        Assert.Single(
            depthLimited.Witnesses,
            witness => witness.Destination == middle);
        var depth = Assert.Single(
            depthLimited.Boundaries.OfType<
                LibraryBodyRootPathBoundary.DepthLimit>());
        Assert.Equal(Address(destination), depth.Destination);

        LibraryBodyRootPathResult nodeLimited =
            LibraryBodyRootPathAnalysis.FindShortestPaths(
                line,
                [Address(root)],
                [Address(destination)],
                new(3, 2, 100, 100));
        Assert.IsType<LibraryBodyRootPathBoundary.NodeBudget>(
            Assert.Single(nodeLimited.Boundaries));
        Assert.Equal(2, nodeLimited.Receipt.SearchNodes);
        Assert.Empty(nodeLimited.Witnesses);

        LibraryBodyRootPathResult edgeLimited =
            LibraryBodyRootPathAnalysis.FindShortestPaths(
                line,
                [Address(root)],
                [Address(destination)],
                new(3, 100, 1, 100));
        Assert.IsType<LibraryBodyRootPathBoundary.EdgeBudget>(
            Assert.Single(edgeLimited.Boundaries));
        Assert.Equal(1, edgeLimited.Receipt.SearchedEdges);
        Assert.Empty(edgeLimited.Witnesses);

        MethodIdentity secondRoot = Method(mvid, 4, "SecondRoot");
        LibraryCallGraphAnalysisResult twoRoots = CallGraph(
            [root, secondRoot, destination],
            [
                Call(root, destination, 4),
                Call(secondRoot, destination, 8),
            ]);
        LibraryBodyRootPathResult pathLimited =
            LibraryBodyRootPathAnalysis.FindShortestPaths(
                twoRoots,
                [Address(secondRoot), Address(root)],
                [Address(destination)],
                new(1, 100, 100, 1));
        Assert.Equal("Root", Assert.Single(pathLimited.Witnesses).Root.Name);
        Assert.IsType<LibraryBodyRootPathBoundary.PathBudget>(
            Assert.Single(pathLimited.Boundaries));
        Assert.Equal(2, pathLimited.Receipt.ObservedReachablePairs);

        LibraryBodyRootPathResult exactCapacity =
            LibraryBodyRootPathAnalysis.FindShortestPaths(
                line,
                [Address(middle)],
                [Address(destination)],
                new(1, 100, 100, 1));
        Assert.True(exactCapacity.IsComplete);
        Assert.Single(exactCapacity.Witnesses);
    }

    [Fact]
    public void FindShortestPaths_RetainsPositiveEvidenceAcrossAnalysisBoundary()
    {
        Guid mvid = Guid.NewGuid();
        MethodIdentity root = Method(mvid, 1, "Root");
        MethodIdentity destination = Method(mvid, 2, "Destination");
        MethodIdentity failed = Method(mvid, 3, "Failed");
        MethodIdentity unresolved = Method(mvid, 4, "Unresolved");
        var diagnostic = new AnalysisDiagnostic(
            failed.MetadataToken,
            failed.Name,
            "Synthetic body failure");
        LibraryCallGraphAnalysisResult callGraph = CallGraph(
            [root, destination, failed],
            [
                Call(root, destination, 4),
                new DirectCall(
                    failed,
                    CallTreeMember.FromDefinition(unresolved),
                    ILOffset: 8,
                    OperandToken: 0x0A000001,
                    CalleeDefinitionToken: 0x0A000001,
                    Kind: CallKind.Call),
            ],
            [diagnostic]);

        LibraryBodyRootPathResult result =
            LibraryBodyRootPathAnalysis.FindShortestPaths(
                callGraph,
                [Address(root)],
                [Address(destination)],
                s_generousLimits);

        Assert.False(result.IsComplete);
        Assert.Single(result.Witnesses);
        var boundary = Assert.IsType<
            LibraryBodyRootPathBoundary.AnalysisIncomplete>(
                Assert.Single(
                    result.Boundaries.OfType<
                        LibraryBodyRootPathBoundary.AnalysisIncomplete>()));
        Assert.Equal(1, boundary.DiagnosticCount);
        Assert.Equal(
            1,
            Assert.Single(
                result.Boundaries.OfType<
                    LibraryBodyRootPathBoundary.UnresolvedLocalCalls>())
            .Count);

        LibraryCallGraphAnalysisResult full = RootPathCallGraph();
        MethodIdentity create = RootPathMethod(full, "Create");
        MethodIdentity mapped =
            RootPathMethod(full, "CreateMappedTextDiff");
        LibraryCallGraphAnalysisResult scoped =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence,
                    bodyScope:
                        new HashSet<int>
                        {
                            create.MetadataToken,
                            mapped.MetadataToken,
                        }))
            .CallGraph;

        LibraryBodyRootPathResult scopedResult =
            LibraryBodyRootPathAnalysis.FindShortestPaths(
                scoped,
                [Address(create)],
                [Address(mapped)],
                s_generousLimits);

        Assert.True(full.HasFullMethodEvidenceScope);
        Assert.False(scoped.HasFullMethodEvidenceScope);
        Assert.Single(scopedResult.Witnesses);
        Assert.IsType<
            LibraryBodyRootPathBoundary.PartialMethodEvidenceScope>(
                Assert.Single(scopedResult.Boundaries));
    }

    [Fact]
    public void
        FindShortestPaths_UnattributedLiftedBodyMakesAbsenceIncomplete()
    {
        Guid mvid = Guid.NewGuid();
        MethodIdentity root = Method(mvid, 1, "Root");
        MethodIdentity lifted =
            Method(mvid, 2, "<MissingOwner>g__Use|0_0");
        MethodIdentity destination = Method(mvid, 3, "Destination");
        LibraryCallGraphAnalysisResult callGraph = CallGraph(
            [root, lifted, destination],
            [Call(lifted, destination, 4)]);

        LibraryBodyRootPathResult result =
            LibraryBodyRootPathAnalysis.FindShortestPaths(
                callGraph,
                [Address(root)],
                [Address(destination)],
                s_generousLimits);

        Assert.Empty(result.Witnesses);
        Assert.False(result.IsComplete);
        Assert.Equal(
            1,
            Assert.Single(
                result.Boundaries.OfType<
                    LibraryBodyRootPathBoundary
                        .UnattributedGeneratedBodies>())
            .Count);
    }

    [Fact]
    public void FindShortestPaths_RejectsInvalidRequests()
    {
        Guid mvid = Guid.NewGuid();
        MethodIdentity method = Method(mvid, 1, "Method");
        LibraryCallGraphAnalysisResult callGraph =
            CallGraph([method], []);

        Assert.Throws<ArgumentException>(
            () => LibraryBodyRootPathAnalysis.FindShortestPaths(
                callGraph,
                [],
                [Address(method)],
                s_generousLimits));
        Assert.Throws<ArgumentException>(
            () => LibraryBodyRootPathAnalysis.FindShortestPaths(
                callGraph,
                [Address(method)],
                [
                    new MetadataMethodAddress(
                        Guid.NewGuid(),
                        MetadataTokens.MethodDefinitionHandle(1)),
                ],
                s_generousLimits));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LibraryBodyRootPathAnalysis.FindShortestPaths(
                callGraph,
                [Address(method)],
                [Address(method)],
                new(1, 1, 1, 0)));

        LibraryBodyRootPathWitness self =
            Assert.Single(
                LibraryBodyRootPathAnalysis.FindShortestPaths(
                    callGraph,
                    [Address(method)],
                    [Address(method)],
                    new(0, 1, 1, 1))
                .Witnesses);
        Assert.Equal(0, self.Depth);
        Assert.Empty(self.Steps);
    }

    static LibraryCallGraphAnalysisResult RootPathCallGraph() =>
        LibraryBodyAnalysisService.ExecutePath(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            LibraryBodyAnalysisRequest.Create(
                LibraryBodyAnalysisFeatures.MethodEvidence))
        .CallGraph;

    static MethodIdentity RootPathMethod(
        LibraryCallGraphAnalysisResult callGraph,
        string name) =>
        callGraph.DeclaredMethods.Single(method =>
            method.DeclaringType.Name == "RootPathEntry"
            && method.Name == name);

    static LibraryBodyRootPathWitness Witness(
        LibraryBodyRootPathResult result,
        MethodIdentity root,
        MethodIdentity destination) =>
        Assert.Single(
            result.Witnesses,
            witness =>
                witness.Root == root
                && witness.Destination == destination);

    static string[] MethodNames(LibraryBodyRootPathWitness witness) =>
    [
        witness.Root.Name,
        .. witness.Steps.Select(static step => step.Callee.Name),
    ];

    static LibraryCallGraphAnalysisResult CallGraph(
        ImmutableArray<MethodIdentity> methods,
        ImmutableArray<DirectCall> calls,
        ImmutableArray<AnalysisDiagnostic> diagnostics = default) =>
        LibraryBodyIndex.FromEvidence(
            methods,
            [],
            diagnostics: diagnostics,
            directCalls: calls)
        .CallGraphAnalysis;

    static MethodIdentity Method(
        Guid moduleVersionId,
        int row,
        string name) =>
        new(
            "RootPathFixture",
            moduleVersionId,
            TypeRef.Definition(
                "RootPathFixture",
                "Fixtures",
                "Graph"),
            name,
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000000 | row,
            IsStatic: true);

    static DirectCall Call(
        MethodIdentity caller,
        MethodIdentity callee,
        int ilOffset) =>
        new(
            caller,
            CallTreeMember.FromDefinition(callee),
            ilOffset,
            callee.MetadataToken,
            callee.MetadataToken,
            CallKind.Call);

    static MetadataMethodAddress Address(MethodIdentity method) =>
        new(
            method.ModuleVersionId,
            MetadataTokens.MethodDefinitionHandle(
                method.MetadataToken & 0x00FFFFFF));
}
