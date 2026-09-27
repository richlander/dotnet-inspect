using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageRoleMemberCallGraphQueryTests
{
    static string CallerPath =>
        FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();

    static string TargetPath =>
        FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();

    static string TargetV2Path =>
        FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath();

    static string RepeatedCallPath =>
        FixtureCatalog.QueriesIntrinsicCoreLibraryCalls.AssemblyPath();

    static string CallerBindingCallerPath =>
        FixtureCatalog.CallerBindingCaller.AssemblyPath();

    static string CallerBindingFacadePath =>
        FixtureCatalog.CallerBindingFacade.AssemblyPath();

    static string RouteLearningMiddlePath =>
        FixtureCatalog.ServicesRouteLearningMiddle.AssemblyPath();

    static string RouteLearningBasePath =>
        FixtureCatalog.ServicesRouteLearningBase.AssemblyPath();

    static string SystemTextJsonNetStandardPath =>
        Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "IntrinsicCoreLibrary",
            "System.Text.Json.dll");

    static string ExternalFocusRole(
        InspectionGraphDocument document,
        InspectionGraphEdge edge) =>
        Assert.Single(
            Assert.IsType<InspectionGraphValue.TokenSet>(
                Assert.Single(
                    document.Characteristics,
                    characteristic =>
                        ReferenceEquals(
                            characteristic.Descriptor,
                            InspectionGraphFocusCatalog.Role)
                        && characteristic.Target
                            == InspectionGraphTarget.Edge(edge.Id))
                    .Value)
                .Values);

    [Fact]
    public async Task
        ExecuteUsesExactRootImplementationAndReturnsDetachedExternalGraph()
    {
        PackageRootBinding caller = PackageBinding(
            "callgraph.caller",
            CallerPath);
        PackageRootBinding target = PackageBinding(
            "callgraph.target",
            TargetPath);
        PackageRootBinding versionSkewedTarget = PackageBinding(
            "callgraph.target.v2",
            TargetV2Path);
        (InspectionGraphDocument document,
            ImmutableArray<PackageRoleMemberCallGraphNodePackage>
                nodePackages,
            PackageIntrinsicCoreLibraryIneligibilityReceipt
                coreLibraryIneligibility,
            ImmutableArray<
                PackageIntrinsicCoreLibraryCallOccurrenceEvidence>
                intrinsicCoreLibraryOccurrences) =
            await ExecuteAsync(caller, target, versionSkewedTarget);
        InspectionGraphEdge edge = Assert.Single(document.Edges);
        Assert.Equal(
            ("RunAcrossBoundary", "Forward"),
            (
                Member(document.Nodes[edge.FromNodeId]).Name,
                Member(document.Nodes[edge.ToNodeId]).Name));
        Assert.Equal("exit", ExternalFocusRole(document, edge));
        Assert.Equal(
            [
                (edge.FromNodeId, "callgraph.caller"),
                (edge.ToNodeId, "callgraph.target"),
            ],
            nodePackages
                .OrderBy(item => item.NodeId)
                .Select(item => (item.NodeId, item.Package.PackageId)));
        Assert.Equal(
            [
                "callgraph.caller",
                "callgraph.target",
                "callgraph.target.v2",
            ],
            coreLibraryIneligibility.Participants.Select(
                participant => participant.Package.PackageId));
        Assert.Equal(
            coreLibraryIneligibility.Participants.Length,
            coreLibraryIneligibility.Participants
                .Select(participant => participant.Registration)
                .Distinct(ReferenceEqualityComparer.Instance)
                .Count());
        PackageIntrinsicCoreLibraryCallOccurrenceEvidence occurrence =
            Assert.Single(intrinsicCoreLibraryOccurrences);
        Assert.Equal(
            "Forward",
            occurrence.CallSite.Target.Name);
        Assert.Equal(
            "System",
            occurrence.Correspondence.Type.Namespace);
        Assert.Equal(
            ["Void"],
            occurrence.Correspondence.Type.Segments);
    }

    [Fact]
    public async Task
        RepeatedPhysicalCallsEachRetainIntrinsicCoreLibraryEvidence()
    {
        PackageRootBinding caller = PackageBinding(
            "callgraph.caller",
            RepeatedCallPath);
        PackageRootBinding target = PackageBinding(
            "callgraph.target",
            TargetPath);

        (InspectionGraphDocument document,
            _,
            _,
            ImmutableArray<
                PackageIntrinsicCoreLibraryCallOccurrenceEvidence>
                occurrences) =
            await ExecuteAsync(
                caller,
                RepeatedCallPath,
                "RepeatedCall",
                "CallTargetTwice",
                target);

        int physicalOccurrences =
            document.Occurrences.Count(
                occurrence =>
                    occurrence.Evidence
                        is CallGraphCallSiteEvidence);
        Assert.Equal(2, physicalOccurrences);
        Assert.Equal(
            physicalOccurrences,
            occurrences
                .Select(occurrence => occurrence.OccurrenceId)
                .Distinct()
                .Count());
        Assert.All(
            occurrences,
            occurrence =>
                Assert.NotNull(
                    occurrence.CallSite.TargetEvidence));
    }

    [Fact]
    public async Task
        IntrinsicCoreLibraryOccurrencesRetainExactPackageContextEvidence()
    {
        string systemTextJson = SystemTextJsonNetStandardPath;
        PackageRootBinding root = PackageBindingForTarget(
            "System.Text.Json",
            "netstandard2.0",
            systemTextJson);

        (InspectionGraphDocument document,
            _,
            PackageIntrinsicCoreLibraryIneligibilityReceipt context,
            ImmutableArray<
                PackageIntrinsicCoreLibraryCallOccurrenceEvidence>
                occurrences) =
            await ExecuteCoreAsync(
                root,
                systemTextJson,
                "JsonDocument",
                "Dispose",
                new(
                    maxDepth: 4,
                    maxNodes: 30),
                PackageSupplyChainBaseline.Nothing,
                WorkspacePlan.Empty);

        Assert.NotEmpty(occurrences);
        Assert.True(
            occurrences
                .Select(occurrence => occurrence.OccurrenceId)
                .Distinct()
                .Count() > 1);
        Assert.All(
            occurrences,
            occurrence =>
            {
                Assert.Same(context, occurrence.Context);
                Assert.Same(
                    occurrence.Origin.Registration,
                    occurrence.CallSite.Identity.SourceRegistration);
                Assert.Same(
                    root.Root.Identity,
                    occurrence.Origin.Package);
                TypeResolutionOutcome.Unavailable unavailable =
                    Assert.IsType<TypeResolutionOutcome.Unavailable>(
                        occurrence.Correspondence.Outcome);
                Assert.IsType<
                    AssemblyBindingTarget.IntrinsicCoreLibrary>(
                        unavailable.Target);
                AssemblyBindingOrigin.RequestingAssembly origin =
                    Assert.IsType<
                        AssemblyBindingOrigin.RequestingAssembly>(
                        unavailable.Origin);
                Assert.Same(
                    occurrence.Origin.Registration,
                    origin.Registration);
                Assert.Equal(
                    AssemblyResolutionScope.Platform,
                    unavailable.Scope);
                InspectionGraphOccurrence graphOccurrence =
                    Assert.Single(
                        document.Occurrences,
                        candidate =>
                            candidate.Id == occurrence.OccurrenceId);
                Assert.Same(
                    occurrence.CallSite,
                    graphOccurrence.Evidence);
            });
    }

    [Fact]
    public async Task SamePackageImplementationAssembliesRemainHubLocal()
    {
        PackageRootBinding root = PackageBinding(
            "callgraph.root",
            CallerPath,
            TargetPath);

        (InspectionGraphDocument document,
            ImmutableArray<PackageRoleMemberCallGraphNodePackage>
                nodePackages,
            _,
            _) =
            await ExecuteAsync(root);

        Assert.Empty(document.Edges);
        InspectionGraphNode seed = Assert.Single(document.Nodes);
        Assert.Equal("RunAcrossBoundary", Member(seed).Name);
        Assert.Equal(
            [(seed.Id, "callgraph.root")],
            nodePackages.Select(item =>
                (item.NodeId, item.Package.PackageId)));
    }

    [Fact]
    public async Task FirstPartyPrefixKeepsKnownDependencyInBaseline()
    {
        PackageRootBinding root = PackageBinding(
            "callgraph.root",
            CallerPath);
        PackageRootBinding target = PackageBinding(
            "callgraph.target",
            TargetPath);
        var plan = new WorkspacePlan(
            [
                new WorkspaceRegistration.PackagePrefix(
                    new PackagePrefixDeclaration("callgraph.target")),
            ]);

        (InspectionGraphDocument document,
            ImmutableArray<PackageRoleMemberCallGraphNodePackage>
                nodePackages,
            _,
            _) =
            await ExecuteWithPolicyAsync(
                root,
                PackageSupplyChainBaseline.Self,
                plan,
                target);

        Assert.Empty(document.Edges);
        InspectionGraphNode seed = Assert.Single(document.Nodes);
        Assert.Equal("RunAcrossBoundary", Member(seed).Name);
        Assert.Equal(
            [(seed.Id, "callgraph.root")],
            nodePackages.Select(item =>
                (item.NodeId, item.Package.PackageId)));
    }

    [Fact]
    public async Task RegisteredEcosystemKeepsKnownDependencyInBaseline()
    {
        PackageRootBinding root = PackageBinding(
            "callgraph.root",
            CallerPath);
        PackageRootBinding target = PackageBinding(
            "callgraph.target",
            TargetPath);
        var ecosystem =
            new WorkspaceEcosystemRegistrationDeclaration(
                WorkspaceEcosystemRegistrationId.Create(
                    "ecosystem.callgraph"),
                [],
                [],
                [
                    new WorkspaceEcosystemPopulationDeclaration
                        .PackagePrefix(
                            new PackagePrefixDeclaration(
                                "callgraph.target")),
                ]);
        var plan = new WorkspacePlan(
            [
                new WorkspaceRegistration.Ecosystem(ecosystem),
            ]);

        (InspectionGraphDocument document, _, _, _) =
            await ExecuteWithPolicyAsync(
                root,
                PackageSupplyChainBaseline
                    .SelfAndRegisteredEcosystems,
                plan,
                target);

        Assert.Empty(document.Edges);
        Assert.Single(document.Nodes);
    }

    [Fact]
    public async Task ForwardedReferenceKeepsUnclassifiedBoundary()
    {
        PackageRootBinding root = PackageBinding(
            "callgraph.forwarding.root",
            CallerBindingCallerPath,
            CallerBindingFacadePath);
        PackageRootBinding dependency = PackageBinding(
            "callgraph.forwarding.dependency",
            RouteLearningMiddlePath,
            RouteLearningBasePath);
        var plan = new WorkspacePlan(
            [
                new WorkspaceRegistration.PackagePrefix(
                    new PackagePrefixDeclaration(
                        "callgraph.forwarding.dependency")),
            ]);

        (InspectionGraphDocument document,
            ImmutableArray<PackageRoleMemberCallGraphNodePackage>
                nodePackages,
            _,
            _) =
            await ExecuteCoreAsync(
                root,
                CallerBindingCallerPath,
                "Caller",
                "Create",
                new(
                    maxDepth: 2,
                    maxNodes: 10),
                PackageSupplyChainBaseline.Self,
                plan,
                dependency);

        InspectionGraphEdge edge = Assert.Single(document.Edges);
        Assert.Equal(
            ("Create", ".ctor"),
            (
                Member(document.Nodes[edge.FromNodeId]).Name,
                Member(document.Nodes[edge.ToNodeId]).Name));
        Assert.Equal(
            "unclassified-boundary",
            ExternalFocusRole(document, edge));
        Assert.Contains(
            document.Limits,
            limit => ReferenceEquals(
                limit.Descriptor,
                InspectionGraphFocusCatalog
                    .ScopeClassificationIncomplete));
        Assert.Equal(
            [(edge.FromNodeId, "callgraph.forwarding.root")],
            nodePackages.Select(item =>
                (item.NodeId, item.Package.PackageId)));
    }

    [Fact]
    public void AmbiguousDefinitionOwnershipReturnsNoPackage()
    {
        PackageRootBinding firstTarget = PackageBinding(
            "callgraph.target.one",
            TargetPath);
        PackageRootBinding secondTarget = PackageBinding(
            "callgraph.target.two",
            TargetPath);
        var firstParticipant = new PackageAssemblyRoleParticipant(
            firstTarget.Root.Identity,
            Assert.Single(firstTarget.Root.AssetSelection.Assets),
            new AssemblyContextParticipant(
                ResolvedAssemblyReference.CreateFromPath(
                    TargetPath,
                    AssemblyResolutionProvenance.Local("query test")),
                NoResolverAssemblyBindingPolicy.Instance));
        var secondParticipant = new PackageAssemblyRoleParticipant(
            secondTarget.Root.Identity,
            Assert.Single(secondTarget.Root.AssetSelection.Assets),
            new AssemblyContextParticipant(
                ResolvedAssemblyReference.CreateFromPath(
                    TargetPath,
                    AssemblyResolutionProvenance.Local("query test")),
                NoResolverAssemblyBindingPolicy.Instance));

        (PackageRootIdentity? package, bool ambiguous) =
            PackageRoleMemberCallGraphQuery.MatchPackage(
                [firstParticipant, secondParticipant],
                firstParticipant.Participant.Assembly.Identity);

        Assert.Null(package);
        Assert.True(ambiguous);
    }

    private static async Task<(
        InspectionGraphDocument Document,
        ImmutableArray<PackageRoleMemberCallGraphNodePackage> NodePackages,
        PackageIntrinsicCoreLibraryIneligibilityReceipt
            CoreLibraryIneligibility,
        ImmutableArray<PackageIntrinsicCoreLibraryCallOccurrenceEvidence>
            IntrinsicCoreLibraryOccurrences)>
        ExecuteAsync(
            PackageRootBinding caller,
            params PackageRootBinding[] packages)
        => await ExecuteAsync(
            caller,
            CallerPath,
            "Entry",
            "RunAcrossBoundary",
            packages);

    private static async Task<(
        InspectionGraphDocument Document,
        ImmutableArray<PackageRoleMemberCallGraphNodePackage> NodePackages,
        PackageIntrinsicCoreLibraryIneligibilityReceipt
            CoreLibraryIneligibility,
        ImmutableArray<PackageIntrinsicCoreLibraryCallOccurrenceEvidence>
            IntrinsicCoreLibraryOccurrences)>
        ExecuteWithPolicyAsync(
            PackageRootBinding caller,
            PackageSupplyChainBaseline baseline,
            WorkspacePlan plan,
            params PackageRootBinding[] packages) =>
        await ExecuteCoreAsync(
            caller,
            CallerPath,
            "Entry",
            "RunAcrossBoundary",
            new(
                maxDepth: 2,
                maxNodes: 10),
            baseline,
            plan,
            packages);

    private static async Task<(
        InspectionGraphDocument Document,
        ImmutableArray<PackageRoleMemberCallGraphNodePackage> NodePackages,
        PackageIntrinsicCoreLibraryIneligibilityReceipt
            CoreLibraryIneligibility,
        ImmutableArray<PackageIntrinsicCoreLibraryCallOccurrenceEvidence>
            IntrinsicCoreLibraryOccurrences)>
        ExecuteAsync(
            PackageRootBinding caller,
            string focusAssemblyPath,
            string focusTypeName,
            string focusMethodName,
            params PackageRootBinding[] packages)
            => await ExecuteCoreAsync(
                caller,
                focusAssemblyPath,
                focusTypeName,
                focusMethodName,
                new(
                    maxDepth: 2,
                    maxNodes: 10),
                PackageSupplyChainBaseline.Nothing,
                WorkspacePlan.Empty,
                packages);

    private static async Task<(
        InspectionGraphDocument Document,
        ImmutableArray<PackageRoleMemberCallGraphNodePackage> NodePackages,
        PackageIntrinsicCoreLibraryIneligibilityReceipt
            CoreLibraryIneligibility,
        ImmutableArray<PackageIntrinsicCoreLibraryCallOccurrenceEvidence>
            IntrinsicCoreLibraryOccurrences)>
        ExecuteCoreAsync(
            PackageRootBinding caller,
            string focusAssemblyPath,
            string focusTypeName,
            string focusMethodName,
            MemberCallGraphCalleeNeighborhoodRequest graph,
            PackageSupplyChainBaseline baseline,
            WorkspacePlan plan,
            params PackageRootBinding[] packages)
    {
        ImmutableArray<PackageRootBinding> bindings =
            [caller, .. packages];
        await using var workspace = new InspectionWorkspace(plan);
        WorkspaceScopeSnapshot empty =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync()).Snapshot;
        WorkspaceScopeSnapshot scope =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    bindings,
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;
        Assert.Equal(bindings.Length, scope.Packages.Length);

        PackageAssemblyContextCompletionOperation operation =
            workspace.PreparePackageAssemblyContextCompletion(bindings);
        PackageAssemblyContextCompletion completion =
            await operation.ExecuteAsync(operation.Identity);
        PackageAssemblyContextProjection projection =
            completion.CreateProjection(bindings);
        try
        {
            WorkspaceRegistrationRevision registrations =
                Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                    workspace.GetRegistrationSnapshot()).Revision;
            PackageRoleMemberCallGraphOutcome outcome =
                PackageRoleMemberCallGraphQuery.Execute(
                    projection,
                    new PackageRoleMemberCallGraphFocus(
                        caller.Root.Identity,
                        ModuleVersionId(focusAssemblyPath),
                        MethodToken(
                            focusAssemblyPath,
                            focusTypeName,
                            focusMethodName)),
                    graph,
                    PackageSupplyChainBaselinePolicy.Create(
                        [caller.Root.Identity.PackageId],
                        baseline,
                        registrations));
            PackageRoleMemberCallGraphOutcome.Unavailable? unavailable =
                outcome as PackageRoleMemberCallGraphOutcome.Unavailable;
            Assert.True(
                outcome is PackageRoleMemberCallGraphOutcome.Available,
                unavailable?.Failure.ToString());
            var available =
                (PackageRoleMemberCallGraphOutcome.Available)outcome;
            PackageAssemblyContextRoleProjection role =
                projection.ImplementationRole
                ?? projection.SurfaceRole;
            Assert.Same(
                role.GroupIdentity,
                available.IntrinsicCoreLibraryIneligibility.Group);
            Assert.Same(
                role.Use(group => group.BindingPolicyVersion),
                available.IntrinsicCoreLibraryIneligibility
                    .BindingPolicyVersion);
            Assert.Equal(
                role.Participants.Length,
                available.IntrinsicCoreLibraryIneligibility
                    .Participants.Length);
            Assert.All(
                role.Participants.Zip(
                    available.IntrinsicCoreLibraryIneligibility
                        .Participants),
                pair =>
                {
                    Assert.Same(
                        pair.First.Package,
                        pair.Second.Package);
                    Assert.Same(
                        pair.First.Asset,
                        pair.Second.Asset);
                    Assert.Same(
                        pair.First.Participant.Assembly.Registration,
                        pair.Second.Registration);
                    Assert.IsType<
                        AssemblyResolutionProvenance.PackageAsset>(
                            pair.First.Participant.Assembly.Provenance);
                });
            return (
                available.Document,
                available.NodePackages,
                available.IntrinsicCoreLibraryIneligibility,
                available.IntrinsicCoreLibraryOccurrences);
        }
        finally
        {
            await projection.ReturnAsync();
            PackageRoleCleanupReport cleanup =
                await completion.CloseAsync();
            Assert.DoesNotContain(
                cleanup.Groups,
                static group =>
                    group is PackageRoleGroupCleanupRecord.Failed);
        }
    }

    private static PackageRootBinding PackageBinding(
        string packageId,
        params string[] assemblyPaths) =>
        PackageBindingForTarget(
            packageId,
            "net11.0",
            assemblyPaths);

    private static PackageRootBinding PackageBindingForTarget(
        string packageId,
        string targetFramework,
        params string[] assemblyPaths)
    {
        byte[] manifest = Encoding.UTF8.GetBytes(
            $$"""
            <package>
              <metadata>
                <id>{{packageId}}</id>
                <version>1.0.0</version>
                <authors>dotnet-inspect</authors>
                <description>Package-role call-graph fixture.</description>
              </metadata>
            </package>
            """);
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using (Stream nuspec = archive.CreateEntry(
                $"{packageId}.nuspec").Open())
            {
                nuspec.Write(manifest);
            }
            foreach (string assemblyPath in assemblyPaths)
            {
                using Stream assembly = archive.CreateEntry(
                    $"lib/{targetFramework}/{Path.GetFileName(assemblyPath)}")
                    .Open();
                assembly.Write(File.ReadAllBytes(assemblyPath));
            }
        }

        var payload = new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(packageId, "1.0.0"),
            new InMemoryPackageContent(
                stream.ToArray(),
                fromCache: false,
                producerKey: "tests"),
            "tests",
            PackagePayloadOrigin.Download);
        return PackageRootBinding.CreateFromSource(
            payload,
            targetFramework);
    }

    private static Guid ModuleVersionId(string assemblyPath)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(assemblyPath);
        return session.ModuleVersionId();
    }

    private static int MethodToken(
        string assemblyPath,
        string typeName,
        string methodName)
    {
        Analysis.LibraryBodyIndex index =
            Analysis.LibraryBodyIndex.Open(assemblyPath);
        return index.Methods.Single(
            method => method.DeclaringType.Name == typeName
                && method.Name == methodName).MetadataToken;
    }

    private static Analysis.MemberRef Member(
        InspectionGraphNode node) =>
        Assert.IsType<InspectionGraphMemberIdentity.CallGraph>(
            Assert.IsType<InspectionGraphSubject.MemberSubject>(
                node.Subject)
                .Identity)
            .Member;

}
