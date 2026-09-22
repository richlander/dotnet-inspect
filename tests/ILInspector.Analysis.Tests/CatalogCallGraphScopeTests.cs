using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.CallGraph;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public class CatalogCallGraphScopeTests
{
    [Fact]
    public void EmptyIndexCatalogBindingUsesIssuedModuleIdentity()
    {
        string path =
            typeof(CatalogCallGraphScopeTests).Assembly.Location;
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.None);
        ResolvedAssemblyReference assembly = Descriptor(index);
        var policy = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path));

        Assert.Empty(index.DeclaredMethods);
        using (var scope = new CatalogCallGraphScope(
            policy,
            [new(index, assembly)]))
        {
            CallTreeNode root = scope.BuildCallerTree(
                index,
                0x06000001);

            Assert.Equal(
                index.ModuleIdentity.ModuleVersionId,
                root.GraphEvidence?.Storage.ModuleVersionId);
        }

        ResolvedAssemblyReference wrongAssembly =
            ResolvedAssemblyReference.Create(
                assembly.Identity with
                {
                    Version = new Version(99, 0, 0, 0),
                },
                path,
                () => File.OpenRead(path),
                AssemblyResolutionProvenance.Local(
                    "mismatched catalog module identity"));

        Assert.Throws<ArgumentException>(
            () => new CatalogCallGraphScope(
                policy,
                [new(index, wrongAssembly)]));
    }

    [Fact]
    [Trait("Speed", "Slow")]
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
    public void CalleeTreeCarriesResolvedDefinitionAssemblyIdentity()
    {
        LibraryBodyIndex caller = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        LibraryBodyIndex target = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        using CatalogCallGraphScope scope =
            CatalogCallGraphTestExtensions.CreateScope(
                caller,
                [target]);
        MethodIdentity root = caller.DeclaredMethods.Single(method =>
            method.DeclaringType.Name == "Entry"
            && method.Name == "RunTwice");

        CallTreeNode tree = scope.BuildCallTree(
            caller,
            root.MetadataToken);
        CallTreeNode callee = Assert.Single(tree.Children);

        Assert.Equal(
            GraphNodeStorageKind.CallSite,
            callee.GraphEvidence?.Storage.Kind);
        Assert.True(
            Descriptor(target).Identity.IsEquivalentTo(
                Assert.IsType<AssemblyReferenceIdentity>(
                    callee.DefinitionAssemblyIdentity)));
        Assert.True(
            Descriptor(target).Identity.IsEquivalentTo(
                Assert.IsType<AssemblyReferenceIdentity>(
                    callee.ResolutionAssemblyIdentity)));
        CallGraphNode projected = Assert.Single(
            CallGraphProjection.FromCallees(tree).Nodes,
            node => node.Member.Name == callee.Member.Name);
        Assert.True(
            Descriptor(target).Identity.IsEquivalentTo(
                Assert.IsType<AssemblyReferenceIdentity>(
                    projected.DefinitionAssemblyIdentity)));
        Assert.True(
            Descriptor(target).Identity.IsEquivalentTo(
                Assert.IsType<AssemblyReferenceIdentity>(
                    projected.ResolutionAssemblyIdentity)));
    }

    [Fact]
    public void ResolvedCallsEnumeratesExactPairWithoutTraversalBounds()
    {
        LibraryBodyIndex caller = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        LibraryBodyIndex target = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        using CatalogCallGraphScope scope =
            CatalogCallGraphTestExtensions.CreateScope(
                caller,
                [target]);

        ImmutableArray<CatalogResolvedCallSite> calls =
            scope.ResolvedCalls(caller, target);

        Assert.Contains(
            calls,
            call => call.SourceMethod.Name == "Run"
                && call.TargetMethod.Name == "Ping"
                && call.Call.Kind == CallKind.Call);
        Assert.Contains(
            calls,
            call => call.SourceMethod.Name == "CallBodiless"
                && call.TargetMethod.Name == "Invoke"
                && call.Call.Kind == CallKind.CallVirtual);
        Assert.Contains(
            calls,
            call => call.SourceMethod.Name == "UseBox"
                && call.TargetMethod.Name == ".ctor"
                && call.Call.Kind == CallKind.NewObject);
        Assert.All(
            calls,
            call =>
            {
                Assert.Same(
                    caller.CallGraphAnalysis,
                    call.Source.CallGraph);
                Assert.Same(
                    target.CallGraphAnalysis,
                    call.Target.CallGraph);
                Assert.Equal(
                    call.SourceMethod.MetadataToken,
                    call.Call.Caller.MetadataToken);
            });
        Assert.Empty(scope.ResolvedCalls(target, caller));
    }

    [Fact]
    public void CensusPublishesWholePopulationInCanonicalOrder()
    {
        LibraryBodyIndex indirect = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphIndirectCaller
                .AssemblyPath());
        LibraryBodyIndex caller = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        LibraryBodyIndex target = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        using CatalogCallGraphScope forward =
            CatalogCallGraphTestExtensions.CreateScope(
                indirect,
                [caller, target]);
        using CatalogCallGraphScope reverse =
            CatalogCallGraphTestExtensions.CreateScope(
                target,
                [caller, indirect]);

        CatalogCallCensus first = forward.Census();
        CatalogCallCensus second = reverse.Census();

        Assert.Equal(
            first.Population.Select(
                participant => participant.Assembly.Identity.Name),
            second.Population.Select(
                participant => participant.Assembly.Identity.Name));
        Assert.Equal(
            first.Members.Select(MemberFingerprint),
            second.Members.Select(MemberFingerprint));
        Assert.Equal(
            first.Occurrences.Select(OccurrenceFingerprint),
            second.Occurrences.Select(OccurrenceFingerprint));
        Assert.Equal(
            first.Members.Select(member => member.OrderingKey),
            first.Members
                .Select(member => member.OrderingKey)
                .Order());
        Assert.Equal(
            first.Occurrences.Select(
                occurrence => occurrence.OrderingKey),
            first.Occurrences
                .Select(occurrence => occurrence.OrderingKey)
                .Order());
        Assert.Equal(
            first.Members.Length,
            first.Members
                .Select(member => member.OrderingKey)
                .Distinct()
                .Count());
        Assert.Equal(
            first.Occurrences.Length,
            first.Occurrences
                .Select(occurrence => occurrence.OrderingKey)
                .Distinct()
                .Count());

        Assert.Contains(
            first.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "Run"
                && occurrence.Source.Assembly.Identity.Name
                    .Contains("IndirectCaller", StringComparison.Ordinal)
                && occurrence.TargetMethod.Name == "Run"
                && occurrence.Target.Assembly.Identity.Name
                    .Contains("Caller", StringComparison.Ordinal));
        Assert.Contains(
            first.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "RunOuter"
                && occurrence.TargetMethod.Name == "Run"
                && ReferenceEquals(
                    occurrence.Source,
                    occurrence.Target));
        Assert.Equal(
            2,
            first.Occurrences.Count(occurrence =>
                occurrence.SourceMethod.Name == "RunTwice"
                && occurrence.TargetMethod.Name == "Echo"));
        CatalogCallCensusOccurrence generated = Assert.Single(
            first.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "AsyncRoot"
                && occurrence.TargetMethod.Name == "AsyncUse");
        Assert.Equal(
            generated.SourceMethod.MetadataToken,
            generated.Call.Caller.MetadataToken);
        Assert.NotEqual(
            generated.SourceMethod.MetadataToken,
            generated.Call.EvidenceMethod.MetadataToken);
        Assert.Equal(
            generated.Call.EvidenceMethod.MetadataToken,
            generated.OrderingKey.EvidenceMethod.MetadataToken);
        CatalogMemberJoinProjection.Issued issued = Assert.IsType<
            CatalogMemberJoinProjection.Issued>(
                generated.CallSiteEvidence.Correspondence);
        Assert.Same(first.Receipt.Generation, issued.Key.Generation);
    }

    [Fact]
    public void CensusRetainsPositiveCallsBesideBoundaryDiagnostics()
    {
        LibraryBodyIndex caller = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        using CatalogCallGraphScope scope =
            CatalogCallGraphTestExtensions.CreateScope(caller, []);

        CatalogCallCensus census = scope.Census();

        Assert.Contains(
            census.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "RunOuter"
                && occurrence.TargetMethod.Name == "Run");
        Assert.DoesNotContain(
            census.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "Run"
                && occurrence.TargetMethod.Name == "Ping");
        Assert.True(census.Diagnostics.IsIncomplete);
        Assert.Contains(
            census.UnresolvedOccurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "Run"
                && occurrence.Call.Callee.Name == "Ping");
        Assert.False(census.IsComplete);
    }

    [Fact]
    public void CensusHonorsExactBindingBeforeStructuralFallback()
    {
        (string directory, string callerPath, string selectedPath,
            string shadowPath) = BuildSameIdentityCallFixture();
        try
        {
            LibraryBodyIndex caller = LibraryBodyIndex.Open(callerPath);
            LibraryBodyIndex selected =
                LibraryBodyIndex.Open(selectedPath);
            LibraryBodyIndex shadow = LibraryBodyIndex.Open(shadowPath);
            ResolvedAssemblyReference callerAssembly =
                Descriptor(caller);
            ResolvedAssemblyReference selectedAssembly =
                Descriptor(selected);
            ResolvedAssemblyReference shadowAssembly =
                Descriptor(shadow);
            var policy = new CountingGroupPolicy(
                [selectedAssembly, shadowAssembly, callerAssembly],
                UnavailablePolicy.Instance);
            using var scope = new CatalogCallGraphScope(
                policy,
                [
                    new(caller, callerAssembly),
                    new(selected, selectedAssembly),
                    new(shadow, shadowAssembly),
                ]);

            CatalogCallCensus census = scope.Census();

            CatalogCallCensusOccurrence call = Assert.Single(
                census.Occurrences,
                occurrence =>
                    occurrence.SourceMethod.Name == "Run"
                    && occurrence.TargetMethod.Name == "Ping");
            Assert.Equal(
                selected.ModuleIdentity.ModuleVersionId,
                call.TargetMethod.ModuleVersionId);
            Assert.DoesNotContain(
                census.UnresolvedOccurrences,
                occurrence =>
                    occurrence.SourceMethod.Name == "Run"
                    && occurrence.Call.Callee.Name == "Ping");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CensusKeepsAmbiguousUniqueStructuralMatchUnresolved()
    {
        (string directory, string callerPath, string selectedPath,
            string shadowPath) =
                BuildSameIdentityCallFixture(
                    shadowDeclaresPing: false);
        try
        {
            LibraryBodyIndex caller = LibraryBodyIndex.Open(callerPath);
            LibraryBodyIndex selected =
                LibraryBodyIndex.Open(selectedPath);
            LibraryBodyIndex shadow = LibraryBodyIndex.Open(shadowPath);
            ResolvedAssemblyReference callerAssembly =
                Descriptor(caller);
            ResolvedAssemblyReference selectedAssembly =
                Descriptor(selected);
            ResolvedAssemblyReference shadowAssembly =
                Descriptor(shadow);
            var policy = new SourceRelativeAssemblyGroupBindingPolicy(
                new[]
                {
                    callerAssembly,
                    selectedAssembly,
                    shadowAssembly,
                }.Select(assembly => (
                    assembly,
                    Policy: (IAssemblyBindingPolicy)
                        UnavailablePolicy.Instance)));
            using var scope = new CatalogCallGraphScope(
                policy,
                [
                    new(caller, callerAssembly),
                    new(selected, selectedAssembly),
                    new(shadow, shadowAssembly),
                ]);

            CatalogCallCensus census = scope.Census();

            Assert.DoesNotContain(
                census.Occurrences,
                occurrence =>
                    occurrence.SourceMethod.Name == "Run"
                    && occurrence.TargetMethod.Name == "Ping");
            Assert.Contains(
                census.UnresolvedOccurrences,
                occurrence =>
                    occurrence.SourceMethod.Name == "Run"
                    && occurrence.Call.Callee.Name == "Ping");
            Assert.False(census.IsComplete);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FallbackSignatureRequiresCompleteRetainedTypeIdentity()
    {
        var targetIdentity = new AssemblyReferenceIdentity(
            "Provider",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        var dependencyV1 = new AssemblyReferenceIdentity(
            "Dependency",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        var dependencyV2 = dependencyV1 with
        {
            Version = new Version(2, 0, 0, 0),
        };
        MetadataTypeDefinitionName providerTypeName =
            TypeName("Provider", "Api");
        MetadataTypeDefinitionName dependencyTypeName =
            TypeName("Dependency", "Value");
        TypeRef providerReference = TypeRef.Definition(
            targetIdentity.Name,
            providerTypeName.Namespace,
            providerTypeName.Segments[0],
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(
                    targetIdentity),
                providerTypeName));
        TypeRef providerDefinition = TypeRef.Definition(
            targetIdentity.Name,
            providerTypeName.Namespace,
            providerTypeName.Segments[0],
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(
                    targetIdentity),
                providerTypeName));
        TypeRef Dependency(AssemblyReferenceIdentity identity) =>
            TypeRef.Definition(
                identity.Name,
                dependencyTypeName.Namespace,
                dependencyTypeName.Segments[0],
                new ResolvableTypeReference(
                    new TypeReferenceOrigin.AssemblyReference(
                        identity),
                    dependencyTypeName));
        TypeRef retainedCoreLibraryVoid = TypeRef.Definition(
            TypeRef.CoreLibrary,
            "System",
            "Void",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.IntrinsicCoreLibrary(),
                TypeName("System", "Void")));
        var untrustedSystemRuntime = new AssemblyReferenceIdentity(
            "System.Runtime",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        TypeRef untrustedCoreLibraryVoid = TypeRef.Definition(
            untrustedSystemRuntime.Name,
            "System",
            "Void",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(
                    untrustedSystemRuntime),
                TypeName("System", "Void")),
            trustedFrameworkAssembly: false);
        MetadataTypeDefinitionName literalNestedName =
            TypeName("Dependency", "Outer+Inner");
        MetadataTypeDefinitionName structuredNestedName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Dependency",
                    ["Outer", "Inner"])).Name;
        TypeRef StructuredDependency(
            MetadataTypeDefinitionName typeName) =>
            TypeRef.Definition(
                dependencyV1.Name,
                typeName.Namespace,
                "Outer+Inner",
                new ResolvableTypeReference(
                    new TypeReferenceOrigin.AssemblyReference(
                        dependencyV1),
                    typeName));
        MemberRef callSite = new(
            providerReference,
            "Use",
            [Dependency(dependencyV1)],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method)
        {
            HasThis = false,
        };
        MethodIdentity Definition(
            AssemblyReferenceIdentity dependency,
            TypeRef? returnType = null) =>
            new(
                targetIdentity.Name,
                Guid.NewGuid(),
                providerDefinition,
                "Use",
                [Dependency(dependency)],
                returnType ?? TypeRef.CoreLib("System", "Void"),
                0x06000001,
                IsStatic: true);

        Assert.Equal(
            GraphNodeIdentity.FromMember(callSite),
            GraphNodeIdentity.FromMethod(Definition(dependencyV2)));
        MemberRef versionTwoCallSite = callSite with
        {
            ParameterTypes = [Dependency(dependencyV2)],
        };
        Assert.Equal(
            GraphNodeIdentity.FromMember(callSite),
            GraphNodeIdentity.FromMember(versionTwoCallSite));
        Assert.NotEqual(
            CatalogCallGraphScope.ExactPlanMemberIdentity(
                callSite),
            CatalogCallGraphScope.ExactPlanMemberIdentity(
                versionTwoCallSite));
        Assert.True(
            CatalogCallGraphScope.ExactFallbackSignaturesMatch(
                callSite,
                Definition(dependencyV1)));
        Assert.True(
            CatalogCallGraphScope.ExactFallbackSignaturesMatch(
                callSite,
                Definition(
                    dependencyV1,
                    retainedCoreLibraryVoid)));
        Assert.False(
            CatalogCallGraphScope.ExactFallbackSignaturesMatch(
                callSite,
                Definition(dependencyV2)));
        Assert.False(
            CatalogCallGraphScope.ExactFallbackSignaturesMatch(
                callSite,
                Definition(
                    dependencyV1,
                    untrustedCoreLibraryVoid)));

        MemberRef literalNestedCall = callSite with
        {
            ParameterTypes = [StructuredDependency(literalNestedName)],
        };
        MethodIdentity structuredNestedDefinition =
            Definition(dependencyV1) with
            {
                ParameterTypes =
                    [StructuredDependency(structuredNestedName)],
            };
        Assert.Equal(
            GraphNodeIdentity.FromMember(literalNestedCall),
            GraphNodeIdentity.FromMethod(
                structuredNestedDefinition));
        Assert.False(
            CatalogCallGraphScope.ExactFallbackSignaturesMatch(
                literalNestedCall,
                structuredNestedDefinition));

        MethodIdentity closedTarget = Definition(dependencyV1);
        MethodIdentity openTarget = closedTarget with
        {
            IsVirtualDispatchOpen = true,
        };
        Assert.True(
            CatalogCallGraphScope.IsResolvedExactTarget(
                CallKind.Call,
                openTarget));
        Assert.True(
            CatalogCallGraphScope.IsResolvedExactTarget(
                CallKind.NewObject,
                openTarget));
        Assert.True(
            CatalogCallGraphScope.IsResolvedExactTarget(
                CallKind.CallVirtual,
                closedTarget));
        Assert.False(
            CatalogCallGraphScope.IsResolvedExactTarget(
                CallKind.CallVirtual,
                openTarget));
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
        using CatalogCallGraphScope permuted =
            CatalogCallGraphTestExtensions.CreateScope(
                caller,
                [targetV1, targetV2]);
        MethodIdentity ping = targetV2.DeclaredMethods.Single(method =>
            method.DeclaringType.Name == "Api"
            && method.Name == "Ping"
            && method.ParameterTypes.Length == 0);

        CallTreeNode tree = scope.BuildCallerTree(
            targetV2,
            ping.MetadataToken);
        CatalogCallCensus census = scope.Census();
        CatalogCallCensus permutedCensus = permuted.Census();

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
        Assert.Contains(
            census.VersionSkewedBindings,
            evidence =>
                evidence.Selected.Version
                    == new Version(1, 0, 0, 0));
        CatalogCallCensusVersionSkewEvidence skew =
            census.VersionSkewedBindings.First(evidence =>
                evidence.Selected.Version
                    == new Version(1, 0, 0, 0));
        Assert.Equal(
            new Version(1, 0, 0, 0),
            skew.Requested.Version);
        Assert.Contains(
            skew.AdmittedAlternatives,
            identity =>
                identity.Version == new Version(2, 0, 0, 0));
        Assert.Equal(
            census.VersionSkewedBindings.Length,
            census.Diagnostics.VersionSkewedBindingCount);
        Assert.Equal(
            0,
            census.Diagnostics.Graph.BindingIdentityConflictCount);
        Assert.Equal(
            census.VersionSkewedBindings.Select(
                VersionSkewFingerprint),
            permutedCensus.VersionSkewedBindings.Select(
                VersionSkewFingerprint));
    }

    [Fact]
    public void DetachedVersionSkewedDefinitionsRemainDistinct()
    {
        LibraryBodyIndex targetV2 = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath());
        LibraryBodyIndex targetV1 = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        var scope = CatalogCallGraphTestExtensions.CreateScope(
            targetV2,
            [targetV1]);
        MethodIdentity pingV2 = targetV2.DeclaredMethods.Single(method =>
            method.DeclaringType.Name == "Api"
            && method.Name == "Ping"
            && method.ParameterTypes.Length == 0);
        MethodIdentity pingV1 = targetV1.DeclaredMethods.Single(method =>
            method.DeclaringType.Name == "Api"
            && method.Name == "Ping"
            && method.ParameterTypes.Length == 0);

        CallTreeNode root = scope.Detach(
            scope.BuildCallTree(targetV2, pingV2.MetadataToken));
        CallTreeNode versionSkewed = scope.Detach(
            scope.BuildCallTree(targetV1, pingV1.MetadataToken));
        scope.Dispose();

        Assert.NotNull(root.GraphEvidence);
        Assert.NotNull(versionSkewed.GraphEvidence);
        Assert.Null(root.GraphEvidence.Correspondence);
        Assert.Null(versionSkewed.GraphEvidence.Correspondence);
        Assert.NotEqual(
            root.GraphEvidence.Identity,
            versionSkewed.GraphEvidence.Identity);

        CallGraphProjection projection = CallGraphProjection.FromCallees(
            root with
            {
                Status = CallTreeStatus.Expanded,
                Children = [versionSkewed],
            });

        Assert.Equal(2, projection.Nodes.Length);
        Assert.NotEqual(
            projection.Nodes[0].Identity,
            projection.Nodes[1].Identity);
        CallGraphEdge edge = Assert.Single(projection.Edges);
        Assert.Equal(0, edge.From);
        Assert.Equal(1, edge.To);
    }

    [Fact]
    public void DetachedRepeatedExternalOccurrencesStayJoined()
    {
        LibraryBodyIndex caller = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        ResolvedAssemblyReference assembly = Descriptor(caller);
        using var scope = new CatalogCallGraphScope(
            new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(caller.Path)),
            [new(caller, assembly)]);
        MethodIdentity rootMethod = caller.DeclaredMethods.Single(method =>
            method.DeclaringType.Name == "Entry"
            && method.Name == "RunTwice");
        DirectCall[] calls =
        [
            .. caller.DirectCalls.Where(call =>
                call.Caller.MetadataToken == rootMethod.MetadataToken),
        ];
        Assert.Equal(2, calls.Length);
        GraphNodeIdentity externalIdentity =
            GraphNodeIdentity.FromMember(calls[0].Callee);
        CallTreeNode root = scope.BuildCallTree(
            caller,
            rootMethod.MetadataToken);
        Assert.Equal(
            2,
            Assert.Single(root.Children)
                .ParentEdgeCallSites.Length);
        CallTreeNode[] occurrences =
        [
            .. calls.Select(call =>
                new CallTreeNode(
                    call.Callee,
                    call.Kind,
                    CallTreeStatus.External,
                    [])
                {
                    GraphEvidence = new GraphNodeEvidence(
                        GraphNodeStorageKey.CallSite(
                            assembly,
                            call.Caller.ModuleVersionId,
                            call),
                        externalIdentity,
                        correspondence: null),
                    ParentEdgeCallSites = [call],
                }),
        ];

        CallTreeNode detached = scope.Detach(
            root with { Children = [.. occurrences] });
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(detached);

        CallGraphNode external = Assert.Single(
            projection.Nodes,
            node => node.Member.Name == "Echo");
        Assert.Equal(2, external.GraphEvidence.Length);
        Assert.Equal(2, projection.CallSites.Length);
        Assert.Equal(
            [0, 1],
            Assert.Single(projection.Edges).CallSiteIds);
    }

    [Fact]
    public void DetachedArtifactIdentityIgnoresAcquisitionRegistration()
    {
        LibraryBodyIndex first = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        LibraryBodyIndex second = LibraryBodyIndex.Open(first.Path);
        ResolvedAssemblyReference firstAssembly = Descriptor(first);
        ResolvedAssemblyReference secondAssembly = Descriptor(second);
        MethodIdentity firstPing = first.DeclaredMethods.Single(method =>
            method.DeclaringType.Name == "Api"
            && method.Name == "Ping"
            && method.ParameterTypes.Length == 0);
        MethodIdentity secondPing = second.DeclaredMethods.Single(method =>
            method.DeclaringType.Name == "Api"
            && method.Name == "Ping"
            && method.ParameterTypes.Length == 0);

        using var firstScope = new CatalogCallGraphScope(
            new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(first.Path)),
            [new(first, firstAssembly)]);
        using var secondScope = new CatalogCallGraphScope(
            new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(second.Path)),
            [new(second, secondAssembly)]);
        CallTreeNode callerRoot = firstScope.Detach(
            firstScope.BuildCallerTree(
                first,
                firstPing.MetadataToken));
        CallTreeNode calleeRoot = secondScope.Detach(
            secondScope.BuildCallTree(
                second,
                secondPing.MetadataToken));

        CallGraphProjection projection = CallGraphProjection.Create(
            callerRoot,
            calleeRoot);

        Assert.Single(projection.Nodes);
        Assert.Equal("Ping", projection.Focus.Member.Name);
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
        var dependencyV1 = new AssemblyReferenceIdentity(
            "Dependency",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: "0011223344556677");
        var dependencyV2 = dependencyV1 with
        {
            Version = new Version(2, 0, 0, 0),
            PublicKeyToken = "8899aabbccddeeff",
        };
        TypeRef modifier = TypeRef.Definition(
            "System.Runtime",
            "System.Runtime.CompilerServices",
            "CallConvCdecl");
        TypeRef integer = TypeRef.CoreLib("System", "Int32");
        TypeRef text = TypeRef.CoreLib("System", "String");
        TypeRef voidType = TypeRef.CoreLib("System", "Void");

        MemberRef Member(
            SignatureCallingConvention convention,
            TypeRef returnType,
            TypeRef parameter) =>
            new(
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
                MemberKind.Method);
        GraphNodeIdentity Identity(
            SignatureCallingConvention convention,
            TypeRef returnType,
            TypeRef parameter) =>
            GraphNodeIdentity.FromMember(
                Member(convention, returnType, parameter));
        TypeRef Dependency(AssemblyReferenceIdentity assembly) =>
            TypeRef.Definition(
                assembly.Name,
                "Dependency",
                "Value",
                new ResolvableTypeReference(
                    new TypeReferenceOrigin.AssemblyReference(
                        assembly),
                    TypeName("Dependency", "Value")));

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
        MemberRef dependencyOne = Member(
            SignatureCallingConvention.Default,
            voidType,
            Dependency(dependencyV1));
        MemberRef dependencyTwo = Member(
            SignatureCallingConvention.Default,
            voidType,
            Dependency(dependencyV2));
        Assert.Equal(
            GraphNodeIdentity.FromMember(dependencyOne),
            GraphNodeIdentity.FromMember(dependencyTwo));
        Assert.NotEqual(
            CatalogCallGraphScope.ExactPlanMemberIdentity(
                dependencyOne),
            CatalogCallGraphScope.ExactPlanMemberIdentity(
                dependencyTwo));
    }

    [Fact]
    public void PlanCacheIdentityPreservesArrayBoundsAndRawTypeKind()
    {
        TypeRef owner = TypeRef.Definition("Owner", "", "Api");
        TypeRef integer = TypeRef.CoreLib("System", "Int32");
        TypeRef voidType = TypeRef.CoreLib("System", "Void");

        GraphNodeIdentity Identity(TypeRef parameter) =>
            GraphNodeIdentity.FromMember(
                new MemberRef(
                    owner,
                    "Store",
                    [parameter],
                    voidType,
                    MemberKind.Method));

        Assert.NotEqual(
            Identity(
                TypeRef.MdArray(
                    integer,
                    new ArrayShape(1, [3], [0]))),
            Identity(
                TypeRef.MdArray(
                    integer,
                    new ArrayShape(1, [3], [1]))));
        Assert.NotEqual(
            Identity(
                TypeRef.Definition(
                    "Owner",
                    "",
                    "Value",
                    resolution: null,
                    rawTypeKind: 0x12)),
            Identity(
                TypeRef.Definition(
                    "Owner",
                    "",
                    "Value",
                    resolution: null,
                    rawTypeKind: 0x11)));
    }

    [Fact]
    [Trait("Speed", "Slow")]
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
    [Trait("Speed", "Slow")]
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

    static string MemberFingerprint(CatalogCallCensusMember member) =>
        string.Join(
            "|",
            member.OrderingKey.Assembly.Name,
            member.OrderingKey.ModuleVersionId,
            member.OrderingKey.MetadataToken,
            member.HasBody);

    static string OccurrenceFingerprint(
        CatalogCallCensusOccurrence occurrence) =>
        string.Join(
            "|",
            occurrence.SourceOrderingKey.Assembly.Name,
            occurrence.SourceOrderingKey.MetadataToken,
            occurrence.TargetOrderingKey.Assembly.Name,
            occurrence.TargetOrderingKey.MetadataToken,
            occurrence.OrderingKey.EvidenceMethod.MetadataToken,
            occurrence.OrderingKey.ILOffset,
            occurrence.OrderingKey.OperandToken,
            occurrence.OrderingKey.Kind);

    static string VersionSkewFingerprint(
        CatalogCallCensusVersionSkewEvidence evidence) =>
        string.Join(
            "|",
            evidence.CallSite.Storage.ModuleVersionId,
            evidence.CallSite.Storage.MethodToken,
            evidence.CallSite.Storage.ILOffset,
            evidence.Requested,
            evidence.Selected,
            string.Join(",", evidence.AdmittedAlternatives));

    static (string Directory, string CallerPath, string SelectedPath,
        string ShadowPath) BuildSameIdentityCallFixture()
        => BuildSameIdentityCallFixture(shadowDeclaresPing: true);

    static (string Directory, string CallerPath, string SelectedPath,
        string ShadowPath) BuildSameIdentityCallFixture(
            bool shadowDeclaresPing)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-call-census-"
                + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var targetName = new AssemblyName("CallCensusShadowTarget")
        {
            Version = new Version(1, 0, 0, 0),
        };
        string selectedPath =
            Path.Combine(directory, "selected.dll");
        MethodBuilder selectedMethod = BuildTarget(
            targetName,
            selectedPath);
        string shadowPath =
            Path.Combine(directory, "shadow.dll");
        _ = BuildTarget(
            targetName,
            shadowPath,
            shadowDeclaresPing ? "Ping" : "Other");

        var callerName = new AssemblyName("CallCensusShadowCaller")
        {
            Version = new Version(1, 0, 0, 0),
        };
        var callerAssembly = new PersistedAssemblyBuilder(
            callerName,
            typeof(object).Assembly);
        ModuleBuilder callerModule =
            callerAssembly.DefineDynamicModule(callerName.Name!);
        TypeBuilder callerType = callerModule.DefineType(
            "Consumer.Entry",
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);
        MethodBuilder run = callerType.DefineMethod(
            "Run",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);
        ILGenerator il = run.GetILGenerator();
        il.Emit(OpCodes.Call, selectedMethod);
        il.Emit(OpCodes.Ret);
        _ = callerType.CreateType();
        string callerPath =
            Path.Combine(directory, "caller.dll");
        callerAssembly.Save(callerPath);
        return (directory, callerPath, selectedPath, shadowPath);

        static MethodBuilder BuildTarget(
            AssemblyName assemblyName,
            string path,
            string methodName = "Ping")
        {
            var assembly = new PersistedAssemblyBuilder(
                assemblyName,
                typeof(object).Assembly);
            ModuleBuilder module =
                assembly.DefineDynamicModule(assemblyName.Name!);
            TypeBuilder type = module.DefineType(
                "Target.Api",
                TypeAttributes.Public
                    | TypeAttributes.Abstract
                    | TypeAttributes.Sealed);
            MethodBuilder method = type.DefineMethod(
                methodName,
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                Type.EmptyTypes);
            method.GetILGenerator().Emit(OpCodes.Ret);
            _ = type.CreateType();
            assembly.Save(path);
            return method;
        }
    }

    static MetadataTypeDefinitionName TypeName(
        string @namespace,
        string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [name])).Name;

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

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore()
            {
                SelectionCount++;
                return request.Target
                    is AssemblyBindingTarget.AssemblyReference reference
                    && _roots.TryGetValue(
                        reference.Identity,
                        out ResolvedAssemblyReference? root)
                            ? AssemblyBindingSelection.Found(root)
                            : inner.Select(request).Selection;

            }
        }
    }

    sealed class UnavailablePolicy : IAssemblyBindingPolicy
    {
        internal static UnavailablePolicy Instance { get; } = new();

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                AssemblyBindingFailureKind.CandidateUnavailable));
        }
    }
}
