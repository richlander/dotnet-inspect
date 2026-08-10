using System.Collections.Immutable;
using System.Reflection.Metadata;

using DotnetInspector.Fixtures;
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
        Assert.Empty(scope.BindingIdentityConflicts);
    }

    [Fact]
    public void DuplicatePhysicalParticipantsAreStoredOnce()
    {
        LibraryBodyIndex first = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
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
        MethodIdentity root = first.DeclaredMethods.Single(method =>
            method.DeclaringType.Name == "Entry"
            && method.Name == "RunTwice");

        using var single = new CatalogCallGraphScope(
            policy,
            [new(first, firstAssembly)]);
        using var repeated = new CatalogCallGraphScope(
            policy,
            [
                new(first, firstAssembly),
                new(first, firstAssembly),
                new(duplicate, duplicateAssembly),
            ]);

        Assert.Equal(single.StorageNodeCount, repeated.StorageNodeCount);
        Assert.Equal(single.StorageEdgeCount, repeated.StorageEdgeCount);
        CallTreeNode throughFirst = single.BuildCallTree(
            first,
            root.MetadataToken);
        CallTreeNode throughDuplicate = repeated.BuildCallTree(
            duplicate,
            root.MetadataToken);
        Assert.Equal(root.Name, throughDuplicate.Member.Name);
        Assert.Equal(2, throughFirst.Perf?.Fanout);
        Assert.Equal(
            throughFirst.Perf?.Fanout,
            throughDuplicate.Perf?.Fanout);
    }

    [Fact]
    public void ExactVersionSkewedParticipantRetainsTypedConflictEvidence()
    {
        LibraryBodyIndex targetV2 = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath());
        LibraryBodyIndex caller = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        LibraryBodyIndex targetV1 = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        using CatalogCallGraphScope scope =
            CatalogCallGraphTestExtensions.CreateScope(
                targetV2,
                [caller, targetV1]);
        MethodIdentity ping = targetV2.DeclaredMethods.Single(method =>
            method.DeclaringType.Name == "Api"
            && method.Name == "Ping"
            && method.ParameterTypes.Length == 0);

        CallTreeNode tree = scope.BuildCallerTree(
            targetV2,
            ping.MetadataToken);

        Assert.Empty(tree.Children);
        Assert.NotEmpty(scope.BindingIdentityConflicts);
        Assert.Equal(
            scope.BindingIdentityConflicts.Length,
            scope.Diagnostics.BindingIdentityConflictCount);
        Assert.All(
            scope.BindingIdentityConflicts,
            conflict =>
            {
                Assert.Equal(
                    new Version(1, 0, 0, 0),
                    conflict.Requested.Version);
                Assert.Equal(
                    new Version(1, 0, 0, 0),
                    conflict.Selected.Version);
                Assert.Equal(
                    new Version(2, 0, 0, 0),
                    conflict.Primary.Version);
                Assert.IsType<CatalogMemberJoinProjection.Issued>(
                    conflict.CallSite.Correspondence);
            });
    }

    [Fact]
    public void MethodGenericArityKeepsOverloadsAndTheirCallersSeparate()
    {
        LibraryBodyIndex target = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        LibraryBodyIndex caller = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        ResolvedAssemblyReference targetAssembly = Descriptor(target);
        ResolvedAssemblyReference callerAssembly = Descriptor(caller);
        var policy = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (
                    targetAssembly,
                    (IAssemblyBindingPolicy)new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(target.Path))),
                (
                    callerAssembly,
                    (IAssemblyBindingPolicy)new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(caller.Path))),
            ]);
        using var scope = new CatalogCallGraphScope(
            policy,
            [
                new(target, targetAssembly),
                new(caller, callerAssembly),
            ]);
        MethodIdentity[] overloads = target.DeclaredMethods
            .Where(method =>
                method.DeclaringType.Name == "ArityApi"
                && method.Name == "Store")
            .OrderBy(method => method.GenericArity)
            .ToArray();

        Assert.Equal(
            [0, 1],
            overloads.Select(method => method.GenericArity));

        CallTreeNode nonGeneric = scope.BuildCallerTree(
            target,
            overloads[0].MetadataToken);
        CallTreeNode generic = scope.BuildCallerTree(
            target,
            overloads[1].MetadataToken);

        Assert.Equal(1, nonGeneric.Perf?.Fanin);
        Assert.Equal(1, generic.Perf?.Fanin);
        Assert.Equal(
            "UseNonGenericStore",
            Assert.Single(nonGeneric.Children).Member.Name);
        Assert.Equal(
            "UseGenericStore",
            Assert.Single(generic.Children).Member.Name);
    }

    [Fact]
    public void FunctionPointerPayloadKeepsOverloadsAndTheirCallersSeparate()
    {
        LibraryBodyIndex target = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        LibraryBodyIndex caller = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        using CatalogCallGraphScope scope =
            CatalogCallGraphTestExtensions.CreateScope(
                target,
                [caller]);
        MethodIdentity[] overloads = target.DeclaredMethods
            .Where(method =>
                method.DeclaringType.Name == "FunctionPointerApi"
                && method.Name == "Store")
            .ToArray();

        Assert.Equal(2, overloads.Length);
        MethodIdentity cdecl = Assert.Single(overloads, method =>
            method.ParameterTypes[0]
                .FunctionPointerSignature?.Header.CallingConvention
                == SignatureCallingConvention.CDecl);
        MethodIdentity stdcall = Assert.Single(overloads, method =>
            method.ParameterTypes[0]
                .FunctionPointerSignature?.Header.CallingConvention
                == SignatureCallingConvention.StdCall);

        CallTreeNode cdeclCallers = scope.BuildCallerTree(
            target,
            cdecl.MetadataToken);
        CallTreeNode stdcallCallers = scope.BuildCallerTree(
            target,
            stdcall.MetadataToken);

        Assert.Equal(
            "UseCdeclStore",
            Assert.Single(cdeclCallers.Children).Member.Name);
        Assert.Equal(
            "UseStdcallStore",
            Assert.Single(stdcallCallers.Children).Member.Name);
        Assert.NotEqual(
            cdeclCallers.GraphEvidence?.Identity,
            stdcallCallers.GraphEvidence?.Identity);
    }

    [Fact]
    public void PlanCacheIdentityPreservesRecursiveFunctionPointerPayload()
    {
        TypeRef owner = TypeRef.Definition("Owner", "", "Api");
        TypeRef modifier = TypeRef.Definition(
            "System.Runtime",
            "System.Runtime.CompilerServices",
            "CallConvCdecl");
        TypeRef integer = TypeRef.CoreLib("System", "Int32");
        TypeRef text = TypeRef.CoreLib("System", "String");
        TypeRef voidType = TypeRef.CoreLib("System", "Void");

        GraphNodeIdentity Identity(
            SignatureCallingConvention convention,
            TypeRef returnType,
            TypeRef parameter) =>
            GraphNodeIdentity.FromMember(
                new MemberRef(
                    owner,
                    "Store",
                    [
                        TypeRef.UnsupportedFunctionPointer(
                            new MethodSignature<TypeRef>(
                                new SignatureHeader(
                                    SignatureKind.Method,
                                    convention,
                                    SignatureAttributes.None),
                                returnType,
                                requiredParameterCount: 1,
                                genericParameterCount: 0,
                                [parameter])),
                    ],
                    voidType,
                    MemberKind.Method));

        GraphNodeIdentity baseline = Identity(
            SignatureCallingConvention.CDecl,
            integer,
            integer);

        Assert.NotEqual(
            baseline,
            Identity(
                SignatureCallingConvention.StdCall,
                integer,
                integer));
        Assert.NotEqual(
            baseline,
            Identity(
                SignatureCallingConvention.CDecl,
                text,
                integer));
        Assert.NotEqual(
            baseline,
            Identity(
                SignatureCallingConvention.CDecl,
                integer,
                text));
        Assert.NotEqual(
            Identity(
                SignatureCallingConvention.Unmanaged,
                TypeRef.UnsupportedModified(
                    modifier,
                    integer,
                    isRequired: true),
                integer),
            Identity(
                SignatureCallingConvention.Unmanaged,
                TypeRef.UnsupportedModified(
                    modifier,
                    integer,
                    isRequired: false),
                integer));
        Assert.NotEqual(
            Identity(
                SignatureCallingConvention.Unmanaged,
                TypeRef.UnsupportedModified(
                    modifier,
                    integer,
                    isRequired: true),
                integer),
            Identity(
                SignatureCallingConvention.Unmanaged,
                TypeRef.UnsupportedModified(
                    modifier,
                    text,
                    isRequired: true),
                integer));
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
            GraphCorrespondenceKind.Incomplete,
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
