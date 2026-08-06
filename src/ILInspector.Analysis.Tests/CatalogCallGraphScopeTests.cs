using System.Collections.Immutable;

using DotnetInspector.Services;
using ILInspector.CallGraph;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public class CatalogCallGraphScopeTests
{
    [Fact]
    public void BothDirectionsAndProjectionReuseOneFrozenGraph()
    {
        LibraryBodyIndex analysis = LibraryBodyIndex.Open(
            typeof(LibraryBodyIndex).Assembly.Location);
        LibraryBodyIndex tests = LibraryBodyIndex.Open(
            typeof(LibraryBodyIndexTests).Assembly.Location);
        ResolvedAssemblyReference analysisAssembly = Descriptor(analysis);
        ResolvedAssemblyReference testAssembly = Descriptor(tests);
        var inner = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(analysis.Path)
            {
                PreferImplementationAssemblies = true,
                AllowPlatformAssemblyVersionRollForward = true,
            });
        var policy = new CountingGroupPolicy(
            [analysisAssembly, testAssembly],
            inner);
        using var scope = new CatalogCallGraphScope(
            policy,
            [
                new(analysis, analysisAssembly),
                new(tests, testAssembly),
            ]);
        MethodIdentity open = analysis.DeclaredMethods.First(method =>
            method.DeclaringType.Name == nameof(LibraryBodyIndex)
            && method.Name == nameof(LibraryBodyIndex.Open));

        CallTreeNode callers = scope.BuildCallerTree(
            analysis,
            open.MetadataToken,
            maxDepth: 2,
            maxNodes: 200);
        int selections = policy.SelectionCount;
        AssemblyCatalogGenerationId generation =
            Assert.IsType<AssemblyCatalogGenerationId>(
                scope.Generation);
        int storageNodes = scope.StorageNodeCount;
        int storageEdges = scope.StorageEdgeCount;

        CallTreeNode callees = scope.BuildCallTree(
            analysis,
            open.MetadataToken,
            maxDepth: 2,
            maxNodes: 200);
        _ = CallGraphProjection.Create(callers, callees);
        _ = CallGraphProjection.Create(callers, callees);

        Assert.True(selections > 0);
        Assert.Equal(selections, policy.SelectionCount);
        Assert.Equal(generation, scope.Generation);
        Assert.Equal(storageNodes, scope.StorageNodeCount);
        Assert.Equal(storageEdges, scope.StorageEdgeCount);
        Assert.NotNull(callers.GraphEvidence?.Correspondence);
        Assert.NotNull(callees.GraphEvidence?.Correspondence);
    }

    [Fact]
    public void DuplicatePhysicalParticipantsAreStoredOnce()
    {
        LibraryBodyIndex first = LibraryBodyIndex.Open(
            typeof(LibraryBodyIndex).Assembly.Location);
        LibraryBodyIndex duplicate = LibraryBodyIndex.Open(first.Path);
        ResolvedAssemblyReference firstAssembly = Descriptor(first);
        ResolvedAssemblyReference duplicateAssembly = Descriptor(duplicate);
        var inner = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(first.Path)
            {
                PreferImplementationAssemblies = true,
                AllowPlatformAssemblyVersionRollForward = true,
            });
        var policy = new CountingGroupPolicy(
            [firstAssembly, duplicateAssembly],
            inner);
        MethodIdentity root = first.DeclaredMethods.First();

        using var single = new CatalogCallGraphScope(
            policy,
            [new(first, firstAssembly)]);
        using var repeated = new CatalogCallGraphScope(
            policy,
            [
                new(first, firstAssembly),
                new(duplicate, duplicateAssembly),
            ]);

        Assert.Equal(single.StorageNodeCount, repeated.StorageNodeCount);
        Assert.Equal(single.StorageEdgeCount, repeated.StorageEdgeCount);
        CallTreeNode throughDuplicate = repeated.BuildCallTree(
            duplicate,
            root.MetadataToken);
        Assert.Equal(root.Name, throughDuplicate.Member.Name);
    }

    [Fact]
    public void UnavailableCorrespondenceRemainsVisibleWithoutFabricatedJoins()
    {
        LibraryBodyIndex analysis = LibraryBodyIndex.Open(
            typeof(LibraryBodyIndex).Assembly.Location);
        LibraryBodyIndex tests = LibraryBodyIndex.Open(
            typeof(LibraryBodyIndexTests).Assembly.Location);
        ResolvedAssemblyReference analysisAssembly = Descriptor(analysis);
        ResolvedAssemblyReference testAssembly = Descriptor(tests);
        using var scope = new CatalogCallGraphScope(
            UnavailablePolicy.Instance,
            [
                new(analysis, analysisAssembly),
                new(tests, testAssembly),
            ]);
        MethodIdentity open = analysis.DeclaredMethods.First(method =>
            method.DeclaringType.Name == nameof(LibraryBodyIndex)
            && method.Name == nameof(LibraryBodyIndex.Open));

        CallTreeNode callers = scope.BuildCallerTree(
            analysis,
            open.MetadataToken,
            maxDepth: 2,
            maxNodes: 200);

        Assert.True(scope.StorageEdgeCount > 0);
        Assert.NotEmpty(scope.IncompleteNodes);
        Assert.NotEmpty(scope.IncompleteEdges);
        Assert.All(
            scope.IncompleteNodes,
            evidence => Assert.Equal(
                GraphCorrespondenceKind.Incomplete,
                evidence.Kind));
        Assert.Equal(
            GraphCorrespondenceKind.Indeterminate,
            callers.GraphEvidence?.Kind);
        Assert.DoesNotContain(
            Flatten(callers),
            node => node.Perf?.Source
                == testAssembly.Identity.Name);
    }

    [Fact]
    public void ReleaseGraphStartsANewGenerationWithoutReopeningIndexes()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            typeof(LibraryBodyIndex).Assembly.Location);
        ResolvedAssemblyReference assembly = Descriptor(index);
        var inner = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(index.Path)
            {
                PreferImplementationAssemblies = true,
                AllowPlatformAssemblyVersionRollForward = true,
            });
        var policy = new CountingGroupPolicy([assembly], inner);
        using var scope = new CatalogCallGraphScope(
            policy,
            [new(index, assembly)]);
        int token = index.DeclaredMethods.First().MetadataToken;

        _ = scope.BuildCallTree(index, token);
        AssemblyCatalogGenerationId first =
            Assert.IsType<AssemblyCatalogGenerationId>(scope.Generation);
        scope.ReleaseGraph();
        Assert.Null(scope.Generation);

        _ = scope.BuildCallerTree(index, token);
        AssemblyCatalogGenerationId second =
            Assert.IsType<AssemblyCatalogGenerationId>(scope.Generation);
        Assert.NotEqual(first, second);
    }

    static IEnumerable<CallTreeNode> Flatten(CallTreeNode root)
    {
        yield return root;
        foreach (CallTreeNode child in root.Children)
        {
            foreach (CallTreeNode descendant in Flatten(child))
                yield return descendant;
        }
    }

    static ResolvedAssemblyReference Descriptor(LibraryBodyIndex index) =>
        ResolvedAssemblyReference.CreateFromPath(
            index.Path,
            AssemblyResolutionProvenance.Local(
                "catalog call-graph test"));

    sealed class CountingGroupPolicy(
        ImmutableArray<ResolvedAssemblyReference> roots,
        IAssemblyBindingPolicy inner) : IAssemblyBindingPolicy
    {
        readonly Dictionary<AssemblyReferenceIdentity,
            ResolvedAssemblyReference> _roots =
                roots.GroupBy(root => root.Identity)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First());

        internal int SelectionCount { get; private set; }

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request)
        {
            SelectionCount++;
            return request.Target
                is AssemblyBindingTarget.AssemblyReference reference
                && _roots.TryGetValue(
                    reference.Identity,
                    out ResolvedAssemblyReference? root)
                        ? AssemblyBindingSelection.Found(root)
                        : inner.Select(request);
        }
    }

    sealed class UnavailablePolicy : IAssemblyBindingPolicy
    {
        internal static UnavailablePolicy Instance { get; } = new();

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request) =>
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable));
    }
}
