using System.IO.Compression;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGet.Versioning;
using NuGetFetch;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageHouseExecutionTests
{
    private const string CallGraphRootPackage = "callgraph.root";
    private const string CallGraphTargetPackage = "callgraph.target";

    private static string CallGraphCallerPath =>
        FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();

    private static string CallGraphTargetPath =>
        FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();

    [Fact]
    public async Task
        DependencyMemberCallGraphInspectionPreparesTraversalAndReturnsEnvelope()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        (
                            "lib/net11.0/ILInspector.Analysis.CallerGraphTarget.dll",
                            File.ReadAllBytes(CallGraphTargetPath)),
                    ]));
        PackageRootBinding rootBinding = CallGraphRootBinding(
            (CallGraphTargetPackage, RouteVersion));
        var candidateSource =
            new AuthorizedPackageDependencyCandidateSource(
                environment.Authorization,
                environment.Root);
        PackageHouseOperation realizationOperation =
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize);

        InspectionEnvelope<
            PackageDependencyMemberCallGraphInspectionOutcome> envelope =
            await PackageDependencyMemberCallGraphInspection.ExecuteAsync(
                new PackageDependencyMemberCallGraphInspectionRequest(
                    rootBinding,
                    new PackageDependencyMemberCallGraphInspectionFocus(
                        ModuleVersionId(CallGraphCallerPath),
                        MethodToken(
                            CallGraphCallerPath,
                            "Entry",
                            "RunAcrossBoundary")),
                    TraversalTargetFrameworkPolicy.ProductDefault,
                    new MemberCallGraphCalleeNeighborhoodRequest(
                        maxDepth: 2,
                        maxNodes: 10),
                    realizationOperation,
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    maximumDependencyDepth: 1,
                    traversalWorkBudget:
                        new PackageDependencyTraversalWorkBudget(
                            maxManifestProjections: 3,
                            maxDeclarationResolutions: 3)),
                new PackageDependencyMemberCallGraphInspectionSource(
                    new PackageDependencyTraversalCandidateAdapter(
                        candidateSource),
                    new UnexpectedManifestAcquirer(),
                    environment.CreateHouse(
                        (_, _) => new InMemoryPackageStore()),
                    (operation, cancellationToken) =>
                        environment.Root.IssueOperationLease(
                            cancellationToken,
                            operation.RequestTimeout,
                            operation.OperationTimeout)),
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<
                PackageDependencyMemberCallGraphInspectionOutcome.Available>(
                envelope.Content);
        Assert.Equal(
            "net12.0",
            available.Document.TraversalTargetPolicy.TargetFramework);
        Assert.Equal(
            TraversalTargetFrameworkPolicySource.ProductDefault,
            available.Document.TraversalTargetPolicy.Source);
        PackageDependencyMemberCallGraphInspectionDestination.Package
            destination =
            Assert.IsType<
                PackageDependencyMemberCallGraphInspectionDestination.Package>(
                Assert.Single(available.Document.Routes).Destination);
        Assert.Equal(
            CallGraphTargetPackage,
            destination.Descriptor.PackageId);
        Assert.Equal(
            "net11.0",
            destination.Descriptor.SelectedTargetFramework);
        InspectionGraphEdge edge =
            Assert.Single(available.Document.Graph.Edges);
        Assert.Equal(
            ("RunAcrossBoundary", "Forward"),
            (
                GraphMember(
                    available.Document.Graph.Nodes[edge.FromNodeId]).Name,
                GraphMember(
                    available.Document.Graph.Nodes[edge.ToNodeId]).Name));
        Assert.Equal(
            "exit",
            ExternalFocusRole(available.Document.Graph, edge));
        Assert.Equal(
            [
                (
                    edge.FromNodeId,
                    CallGraphRootPackage,
                    RouteVersion,
                    "netstandard2.0"),
                (
                    edge.ToNodeId,
                    CallGraphTargetPackage,
                    RouteVersion,
                    "net11.0"),
            ],
            available.Document.PackageSubjects
                .OrderBy(subject => subject.NodeId)
                .Select(subject =>
                    (
                        subject.NodeId,
                        subject.PackageId,
                        subject.PackageVersion,
                        subject.TargetFramework)));
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        Assert.Equal(
            [
                "call.traversal-incomplete",
                "call.correspondence-incomplete",
            ],
            envelope.Diagnostics.Select(
                diagnostic => diagnostic.Correspondence!.ToString()));
        Assert.Equal(
            [CallGraphTargetPackage],
            environment.Clients[0].PayloadPackageIds);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        DependencyMemberCallGraphRealizationBudgetFailureReleasesOperationWorkspace()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        (
                            "lib/net11.0/ILInspector.Analysis.CallerGraphTarget.dll",
                            File.ReadAllBytes(CallGraphTargetPath)),
                    ]));
        PackageRootBinding rootBinding = CallGraphRootBinding(
            (CallGraphTargetPackage, RouteVersion));
        var candidateSource =
            new AuthorizedPackageDependencyCandidateSource(
                environment.Authorization,
                environment.Root);
        PackageHouseOperation realizationOperation =
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize);

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await PackageDependencyMemberCallGraphInspection
                        .ExecuteAsync(
                            new PackageDependencyMemberCallGraphInspectionRequest(
                                rootBinding,
                                new PackageDependencyMemberCallGraphInspectionFocus(
                                    ModuleVersionId(CallGraphCallerPath),
                                    MethodToken(
                                        CallGraphCallerPath,
                                        "Entry",
                                        "RunAcrossBoundary")),
                                TraversalTargetFrameworkPolicy.ProductDefault,
                                new MemberCallGraphCalleeNeighborhoodRequest(
                                    maxDepth: 2,
                                    maxNodes: 10),
                                realizationOperation,
                                DateTimeOffset.UtcNow.AddMinutes(1),
                                maximumDependencyDepth: 1,
                                traversalWorkBudget:
                                    new PackageDependencyTraversalWorkBudget(
                                        maxManifestProjections: 3,
                                        maxDeclarationResolutions: 3),
                                realizationOptions:
                                    new PackageAssemblyContextRealizationOptions
                                    {
                                        MaxAssembliesPerRole = 0,
                                        MaxAggregateRetainedImageBytes =
                                            long.MaxValue,
                                        MaxAssemblyEntryBytes =
                                            long.MaxValue,
                                        RequireDeclaredEntryLengths = true,
                                    }),
                            new PackageDependencyMemberCallGraphInspectionSource(
                                new PackageDependencyTraversalCandidateAdapter(
                                    candidateSource),
                                new UnexpectedManifestAcquirer(),
                                environment.CreateHouse(
                                    (_, _) => new InMemoryPackageStore()),
                                (operation, cancellationToken) =>
                                    environment.Root.IssueOperationLease(
                                        cancellationToken,
                                        operation.RequestTimeout,
                                        operation.OperationTimeout)),
                            TestContext.Current.CancellationToken));

        Assert.Contains(
            "assembly-count limit",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Equal(
            [CallGraphTargetPackage],
            environment.Clients[0].PayloadPackageIds);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        DependencyMemberCallGraphExecutesRouteAndReturnsDetachedGraph()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        (
                            "lib/net11.0/ILInspector.Analysis.CallerGraphTarget.dll",
                            File.ReadAllBytes(CallGraphTargetPath)),
                    ]));
        PackageRootBinding rootBinding = CallGraphRootBinding(
            (CallGraphTargetPackage, RouteVersion));
        RealizedPackageDependencyContext rootContext =
            await RouteRootContextAsync(rootBinding);
        PackageDependencyTraversalOutcome traversal =
            await RouteTraversalAsync(
                environment,
                new PackageDependencyTraversalRootOccurrence(
                    rootContext,
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources));
        PackageDependencyEdgeRealizationExecution execution =
            PackageDependencyEdgeRealizationQuery.Execute(
                new PackageDependencyEdgeRealizationRequest(
                    traversal,
                    rootOccurrenceIndex: 0,
                    edgeIndex: 0,
                    PackageHouseOperation.Create(
                        PackageHouseOperationProfile.Realize),
                    PackageHouseTargetContext.Exact("net12.0")));
        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot empty = await CurrentScopeAsync(workspace);
        WorkspaceScopeSnapshot rooted =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    [rootBinding],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;
        WorkspaceRegistrationRevision graphRegistrations =
            CurrentRegistrations(workspace);
        var request = new PackageDependencyMemberCallGraphRequest(
            workspace,
            rooted,
            graphRegistrations,
            traversal,
            [rootBinding],
            [execution],
            new PackageDependencyMemberCallGraphFocus(
                rootOccurrenceIndex: 0,
                ModuleVersionId(CallGraphCallerPath),
                MethodToken(
                    CallGraphCallerPath,
                    "Entry",
                    "RunAcrossBoundary")),
            new(
                maxDepth: 2,
                maxNodes: 10),
            DateTimeOffset.UtcNow.AddMinutes(1),
            supplyChainBaseline:
                PackageSupplyChainBaseline
                    .SelfAndRegisteredEcosystems);
        var laterEcosystem =
            new WorkspaceEcosystemRegistrationDeclaration(
                WorkspaceEcosystemRegistrationId.Create(
                    "ecosystem.later"),
                [],
                [],
                [
                    new WorkspaceEcosystemPopulationDeclaration
                        .PackagePrefix(
                            new PackagePrefixDeclaration(
                                CallGraphTargetPackage)),
                ]);
        Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            workspace.ReplaceRegistrations(
                graphRegistrations,
                [
                    new WorkspaceRegistration.Ecosystem(
                        laterEcosystem),
                ]));

        PackageDependencyMemberCallGraphOutcome.Completed completed =
            Assert.IsType<
                PackageDependencyMemberCallGraphOutcome.Completed>(
                await PackageDependencyMemberCallGraphOperation.ExecuteAsync(
                    request,
                    environment.CreateHouse(
                        (_, _) => new InMemoryPackageStore()),
                    environment.IssueOperation(
                        execution.Request,
                        TestContext.Current.CancellationToken)));

        PackageDependencyMemberCallGraphDestination.Package destination =
            Assert.IsType<
                PackageDependencyMemberCallGraphDestination.Package>(
                Assert.Single(completed.Routes).Destination);
        Assert.Equal(
            CallGraphTargetPackage,
            destination.Descriptor.PackageId);
        Assert.Equal(
            "net11.0",
            destination.Descriptor.SelectedTargetFramework);
        Assert.Equal(
            "net12.0",
            completed.TraversalTargetPolicy.TargetFramework);
        Assert.Empty(completed.Baseline.RegisteredEcosystems);
        Assert.Equal(
            [CallGraphRootPackage.ToLowerInvariant()],
            completed.Baseline.RootPackageIds);
        WorkspaceScopeSnapshot finalScope =
            await CurrentScopeAsync(workspace);
        Assert.Equal(
            "netstandard2.0",
            finalScope.Packages[0].Occurrence.Package
                .RequestedTargetFramework);
        InspectionGraphEdge edge = Assert.Single(completed.Graph.Edges);
        Assert.Equal(
            ("RunAcrossBoundary", "Forward"),
            (
                GraphMember(
                    completed.Graph.Nodes[edge.FromNodeId]).Name,
                GraphMember(
                    completed.Graph.Nodes[edge.ToNodeId]).Name));
        Assert.Equal(
            [CallGraphTargetPackage],
            environment.Clients[0].PayloadPackageIds);
        Assert.Equal(2, finalScope.Packages.Length);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        DependencyMemberCallGraphValidatesAllExecutionsBeforeSourceWork()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        (
                            "lib/net11.0/ILInspector.Analysis.CallerGraphTarget.dll",
                            File.ReadAllBytes(CallGraphTargetPath)),
                    ]));
        PackageRootBinding rootBinding = CallGraphRootBinding(
            (CallGraphTargetPackage, RouteVersion));
        RealizedPackageDependencyContext rootContext =
            await RouteRootContextAsync(rootBinding);
        PackageDependencyTraversalOutcome traversal =
            await RouteTraversalAsync(
                environment,
                new PackageDependencyTraversalRootOccurrence(
                    rootContext,
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources));
        PackageDependencyEdgeRealizationExecution execution =
            PackageDependencyEdgeRealizationQuery.Execute(
                new PackageDependencyEdgeRealizationRequest(
                    traversal,
                    rootOccurrenceIndex: 0,
                    edgeIndex: 0,
                    PackageHouseOperation.Create(
                        PackageHouseOperationProfile.Realize),
                    PackageHouseTargetContext.Exact("net12.0")));
        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot empty = await CurrentScopeAsync(workspace);
        WorkspaceScopeSnapshot rooted =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    [rootBinding],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;
        var request = new PackageDependencyMemberCallGraphRequest(
            workspace,
            rooted,
            CurrentRegistrations(workspace),
            traversal,
            [rootBinding],
            [],
            new PackageDependencyMemberCallGraphFocus(
                rootOccurrenceIndex: 0,
                ModuleVersionId(CallGraphCallerPath),
                MethodToken(
                    CallGraphCallerPath,
                    "Entry",
                    "RunAcrossBoundary")),
            new(
                maxDepth: 2,
                maxNodes: 10),
            DateTimeOffset.UtcNow.AddMinutes(1));

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                PackageDependencyMemberCallGraphOperation.ExecuteAsync(
                    request,
                    environment.CreateHouse(
                        (_, _) => new InMemoryPackageStore()),
                    environment.IssueOperation(
                        execution.Request,
                        TestContext.Current.CancellationToken)));

        Assert.Empty(environment.Clients[0].PayloadPackageIds);
        Assert.Single((await CurrentScopeAsync(workspace)).Packages);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        DependencyMemberCallGraphRejectsDifferentRootGenerationBeforeSourceWork()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        (
                            "lib/net11.0/ILInspector.Analysis.CallerGraphTarget.dll",
                            File.ReadAllBytes(CallGraphTargetPath)),
                    ]));
        PackageRootBinding scopeBinding = CallGraphRootBinding(
            (CallGraphTargetPackage, RouteVersion));
        PackageRootBinding requestBinding = CallGraphRootBinding(
            (CallGraphTargetPackage, RouteVersion));
        Assert.Equal(
            scopeBinding.CreateReacquisitionRequest(),
            requestBinding.CreateReacquisitionRequest());
        Assert.NotSame(
            scopeBinding.ContentGenerationIdentity,
            requestBinding.ContentGenerationIdentity);
        Assert.NotSame(
            scopeBinding.SelectionIdentity,
            requestBinding.SelectionIdentity);

        RealizedPackageDependencyContext rootContext =
            await RouteRootContextAsync(requestBinding);
        PackageDependencyTraversalOutcome traversal =
            await RouteTraversalAsync(
                environment,
                new PackageDependencyTraversalRootOccurrence(
                    rootContext,
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources));
        PackageDependencyEdgeRealizationExecution execution =
            PackageDependencyEdgeRealizationQuery.Execute(
                new PackageDependencyEdgeRealizationRequest(
                    traversal,
                    rootOccurrenceIndex: 0,
                    edgeIndex: 0,
                    PackageHouseOperation.Create(
                        PackageHouseOperationProfile.Realize),
                    PackageHouseTargetContext.Exact("net12.0")));

        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot empty = await CurrentScopeAsync(workspace);
        WorkspaceScopeSnapshot rooted =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    [scopeBinding],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;
        var request = new PackageDependencyMemberCallGraphRequest(
            workspace,
            rooted,
            CurrentRegistrations(workspace),
            traversal,
            [requestBinding],
            [execution],
            new PackageDependencyMemberCallGraphFocus(
                rootOccurrenceIndex: 0,
                ModuleVersionId(CallGraphCallerPath),
                MethodToken(
                    CallGraphCallerPath,
                    "Entry",
                    "RunAcrossBoundary")),
            new(
                maxDepth: 2,
                maxNodes: 10),
            DateTimeOffset.UtcNow.AddMinutes(1));

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                PackageDependencyMemberCallGraphOperation.ExecuteAsync(
                    request,
                    environment.CreateHouse(
                        (_, _) => new InMemoryPackageStore()),
                    environment.IssueOperation(
                        execution.Request,
                        TestContext.Current.CancellationToken)));

        Assert.Empty(environment.Clients[0].PayloadPackageIds);
        WorkspaceScopeSnapshot current = await CurrentScopeAsync(workspace);
        Assert.NotNull(
            current.FindExactPackageOccurrence(scopeBinding));
        Assert.Null(
            current.FindExactPackageOccurrence(requestBinding));
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        DependencyMemberCallGraphRejectsReusedDependencyGeneration()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        (
                            "lib/net11.0/ILInspector.Analysis.CallerGraphTarget.dll",
                            File.ReadAllBytes(CallGraphTargetPath)),
                    ]));
        PackageRootBinding rootBinding = CallGraphRootBinding(
            (CallGraphTargetPackage, RouteVersion));
        RealizedPackageDependencyContext rootContext =
            await RouteRootContextAsync(rootBinding);
        PackageDependencyTraversalOutcome traversal =
            await RouteTraversalAsync(
                environment,
                new PackageDependencyTraversalRootOccurrence(
                    rootContext,
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources));
        PackageDependencyEdgeRealizationExecution execution =
            PackageDependencyEdgeRealizationQuery.Execute(
                new PackageDependencyEdgeRealizationRequest(
                    traversal,
                    rootOccurrenceIndex: 0,
                    edgeIndex: 0,
                    PackageHouseOperation.Create(
                        PackageHouseOperationProfile.Realize),
                    PackageHouseTargetContext.Exact("net12.0")));
        PackageDependencyEdgeRealizationEvidence retainedRealization =
            await execution.ExecuteAsync(
                environment.CreateHouse(
                    (_, _) => new InMemoryPackageStore()),
                environment.IssueOperation(
                    execution.Request,
                    TestContext.Current.CancellationToken));
        PackageRootBinding retainedDependency =
            Assert.IsType<
                PackageHouseRootContributionOutcome.Contributed>(
                retainedRealization.RootContribution)
            .Contribution.Binding;

        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot empty = await CurrentScopeAsync(workspace);
        WorkspaceScopeSnapshot rooted =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    [rootBinding, retainedDependency],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;
        var request = new PackageDependencyMemberCallGraphRequest(
            workspace,
            rooted,
            CurrentRegistrations(workspace),
            traversal,
            [rootBinding],
            [execution],
            new PackageDependencyMemberCallGraphFocus(
                rootOccurrenceIndex: 0,
                ModuleVersionId(CallGraphCallerPath),
                MethodToken(
                    CallGraphCallerPath,
                    "Entry",
                    "RunAcrossBoundary")),
            new(
                maxDepth: 2,
                maxNodes: 10),
            DateTimeOffset.UtcNow.AddMinutes(1));

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    PackageDependencyMemberCallGraphOperation.ExecuteAsync(
                        request,
                        environment.CreateHouse(
                            (_, _) => new InMemoryPackageStore()),
                        environment.IssueOperation(
                            execution.Request,
                            TestContext.Current.CancellationToken)));

        Assert.Contains(
            "exact admitted dependency Package",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            environment.Clients.Sum(client =>
                client.PayloadPackageIds.Count(package =>
                    package == CallGraphTargetPackage)));
        WorkspaceScopeSnapshot current = await CurrentScopeAsync(workspace);
        Assert.Equal(2, current.Packages.Length);
        Assert.NotNull(
            current.FindExactPackageOccurrence(retainedDependency));
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        DependencyMemberCallGraphExecutesEdgesInTraversalOrder()
    {
        const string firstPackage = "callgraph.first";
        const string secondPackage = "callgraph.second";
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        (
                            "lib/net11.0/ILInspector.Analysis.CallerGraphTarget.dll",
                            File.ReadAllBytes(CallGraphTargetPath)),
                    ]));
        PackageRootBinding rootBinding = CallGraphRootBinding(
            (firstPackage, RouteVersion),
            (secondPackage, RouteVersion));
        RealizedPackageDependencyContext rootContext =
            await RouteRootContextAsync(rootBinding);
        PackageDependencyTraversalOutcome traversal =
            await RouteTraversalAsync(
                environment,
                new PackageDependencyTraversalRootOccurrence(
                    rootContext,
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources));
        PackageDependencyEdgeRealizationExecution[] executions =
        [
            .. traversal.Edges.Select(
                (_, edgeIndex) =>
                    PackageDependencyEdgeRealizationQuery.Execute(
                        new PackageDependencyEdgeRealizationRequest(
                            traversal,
                            rootOccurrenceIndex: 0,
                            edgeIndex,
                            PackageHouseOperation.Create(
                                PackageHouseOperationProfile.Realize),
                            PackageHouseTargetContext.Exact("net12.0")))),
        ];

        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot empty = await CurrentScopeAsync(workspace);
        WorkspaceScopeSnapshot rooted =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    [rootBinding],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;
        var request = new PackageDependencyMemberCallGraphRequest(
            workspace,
            rooted,
            CurrentRegistrations(workspace),
            traversal,
            [rootBinding],
            [.. executions.Reverse()],
            new PackageDependencyMemberCallGraphFocus(
                rootOccurrenceIndex: 0,
                ModuleVersionId(CallGraphCallerPath),
                MethodToken(
                    CallGraphCallerPath,
                    "Entry",
                    "RunAcrossBoundary")),
            new(
                maxDepth: 2,
                maxNodes: 10),
            DateTimeOffset.UtcNow.AddMinutes(-1));

        PackageDependencyMemberCallGraphOutcome.WorkspaceNotCommitted
            notCommitted =
                Assert.IsType<
                    PackageDependencyMemberCallGraphOutcome
                        .WorkspaceNotCommitted>(
                    await PackageDependencyMemberCallGraphOperation
                        .ExecuteAsync(
                            request,
                            environment.CreateHouse(
                                (_, _) => new InMemoryPackageStore()),
                            environment.IssueOperation(
                                executions[0].Request,
                                TestContext.Current.CancellationToken)));

        Assert.IsType<WorkspaceScopeOperationResult.Rejected>(
            notCommitted.ScopeOperation);
        Assert.Equal(
            [firstPackage, secondPackage],
            environment.Clients[0].PayloadPackageIds);
        Assert.Single((await CurrentScopeAsync(workspace)).Packages);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        DependencyMemberCallGraphCancellationPreventsSourceAndWorkspaceWork()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        (
                            "lib/net11.0/ILInspector.Analysis.CallerGraphTarget.dll",
                            File.ReadAllBytes(CallGraphTargetPath)),
                    ]));
        PackageRootBinding rootBinding = CallGraphRootBinding(
            (CallGraphTargetPackage, RouteVersion));
        RealizedPackageDependencyContext rootContext =
            await RouteRootContextAsync(rootBinding);
        PackageDependencyTraversalOutcome traversal =
            await RouteTraversalAsync(
                environment,
                new PackageDependencyTraversalRootOccurrence(
                    rootContext,
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources));
        PackageDependencyEdgeRealizationExecution execution =
            PackageDependencyEdgeRealizationQuery.Execute(
                new PackageDependencyEdgeRealizationRequest(
                    traversal,
                    rootOccurrenceIndex: 0,
                    edgeIndex: 0,
                    PackageHouseOperation.Create(
                        PackageHouseOperationProfile.Realize),
                    PackageHouseTargetContext.Exact("net12.0")));

        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot empty = await CurrentScopeAsync(workspace);
        WorkspaceScopeSnapshot rooted =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    [rootBinding],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;
        var request = new PackageDependencyMemberCallGraphRequest(
            workspace,
            rooted,
            CurrentRegistrations(workspace),
            traversal,
            [rootBinding],
            [execution],
            new PackageDependencyMemberCallGraphFocus(
                rootOccurrenceIndex: 0,
                ModuleVersionId(CallGraphCallerPath),
                MethodToken(
                    CallGraphCallerPath,
                    "Entry",
                    "RunAcrossBoundary")),
            new(
                maxDepth: 2,
                maxNodes: 10),
            DateTimeOffset.UtcNow.AddMinutes(1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () =>
                PackageDependencyMemberCallGraphOperation.ExecuteAsync(
                    request,
                    environment.CreateHouse(
                        (_, _) => new InMemoryPackageStore()),
                    environment.IssueOperation(
                        execution.Request,
                        cancellation.Token)));

        Assert.Empty(environment.Clients[0].PayloadPackageIds);
        Assert.Single((await CurrentScopeAsync(workspace)).Packages);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        DependencyMemberCallGraphCancellationAfterPublicationPreventsGraph()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        (
                            "lib/net11.0/ILInspector.Analysis.CallerGraphTarget.dll",
                            File.ReadAllBytes(CallGraphTargetPath)),
                    ]));
        using var cancellation = new CancellationTokenSource();
        CancelAfterOpenPackageContent? cancelingContent = null;
        bool cancellationArmed = false;
        PackageRootBinding rootBinding = CallGraphRootBinding(
            content =>
                cancelingContent = new(
                    content,
                    cancellation,
                    relativePath =>
                        cancellationArmed
                        && relativePath.EndsWith(
                            ".dll",
                            StringComparison.OrdinalIgnoreCase)),
            (CallGraphTargetPackage, RouteVersion));
        RealizedPackageDependencyContext rootContext =
            await RouteRootContextAsync(rootBinding);
        PackageDependencyTraversalOutcome traversal =
            await RouteTraversalAsync(
                environment,
                new PackageDependencyTraversalRootOccurrence(
                    rootContext,
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources));
        PackageDependencyEdgeRealizationExecution execution =
            PackageDependencyEdgeRealizationQuery.Execute(
                new PackageDependencyEdgeRealizationRequest(
                    traversal,
                    rootOccurrenceIndex: 0,
                    edgeIndex: 0,
                    PackageHouseOperation.Create(
                        PackageHouseOperationProfile.Realize),
                    PackageHouseTargetContext.Exact("net12.0")));

        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot empty = await CurrentScopeAsync(workspace);
        WorkspaceScopeSnapshot rooted =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    [rootBinding],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;
        var request = new PackageDependencyMemberCallGraphRequest(
            workspace,
            rooted,
            CurrentRegistrations(workspace),
            traversal,
            [rootBinding],
            [execution],
            new PackageDependencyMemberCallGraphFocus(
                rootOccurrenceIndex: 0,
                ModuleVersionId(CallGraphCallerPath),
                MethodToken(
                    CallGraphCallerPath,
                    "Entry",
                    "RunAcrossBoundary")),
            new(
                maxDepth: 2,
                maxNodes: 10),
            DateTimeOffset.UtcNow.AddMinutes(1));
        cancellationArmed = true;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () =>
                PackageDependencyMemberCallGraphOperation.ExecuteAsync(
                    request,
                    environment.CreateHouse(
                        (_, _) => new InMemoryPackageStore()),
                    environment.IssueOperation(
                        execution.Request,
                        cancellation.Token)));

        Assert.NotNull(cancelingContent);
        Assert.True(cancelingContent.OpenedStreamCount > 0);
        Assert.Equal(
            cancelingContent.OpenedStreamCount,
            cancelingContent.DisposedStreamCount);
        Assert.Equal(
            [CallGraphTargetPackage],
            environment.Clients[0].PayloadPackageIds);
        Assert.Equal(
            2,
            (await CurrentScopeAsync(workspace)).Packages.Length);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        DependencyMemberCallGraphRejectsForeignCandidatesBeforeSourceWork()
    {
        SourceBehavior behavior =
            new(
                [RouteVersion],
                PayloadContentEntries:
                [
                    (
                        "lib/net11.0/ILInspector.Analysis.CallerGraphTarget.dll",
                        File.ReadAllBytes(CallGraphTargetPath)),
                ]);
        await using HouseEnvironment traversalEnvironment =
            HouseEnvironment.CreateForAnyPackage(behavior);
        await using HouseEnvironment executionEnvironment =
            HouseEnvironment.CreateForAnyPackage(behavior);
        PackageRootBinding rootBinding = CallGraphRootBinding(
            (CallGraphTargetPackage, RouteVersion));
        RealizedPackageDependencyContext rootContext =
            await RouteRootContextAsync(rootBinding);
        PackageDependencyTraversalOutcome traversal =
            await RouteTraversalAsync(
                traversalEnvironment,
                new PackageDependencyTraversalRootOccurrence(
                    rootContext,
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources));
        PackageDependencyEdgeRealizationExecution execution =
            PackageDependencyEdgeRealizationQuery.Execute(
                new PackageDependencyEdgeRealizationRequest(
                    traversal,
                    rootOccurrenceIndex: 0,
                    edgeIndex: 0,
                    PackageHouseOperation.Create(
                        PackageHouseOperationProfile.Realize),
                    PackageHouseTargetContext.Exact("net12.0")));

        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot empty = await CurrentScopeAsync(workspace);
        WorkspaceScopeSnapshot rooted =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    [rootBinding],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;
        var request = new PackageDependencyMemberCallGraphRequest(
            workspace,
            rooted,
            CurrentRegistrations(workspace),
            traversal,
            [rootBinding],
            [execution],
            new PackageDependencyMemberCallGraphFocus(
                rootOccurrenceIndex: 0,
                ModuleVersionId(CallGraphCallerPath),
                MethodToken(
                    CallGraphCallerPath,
                    "Entry",
                    "RunAcrossBoundary")),
            new(
                maxDepth: 2,
                maxNodes: 10),
            DateTimeOffset.UtcNow.AddMinutes(1));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                PackageDependencyMemberCallGraphOperation.ExecuteAsync(
                    request,
                    executionEnvironment.CreateHouse(
                        (_, _) => new InMemoryPackageStore()),
                    executionEnvironment.IssueOperation(
                        execution.Request,
                        TestContext.Current.CancellationToken)));

        Assert.Empty(
            traversalEnvironment.Clients[0].PayloadPackageIds);
        Assert.Empty(
            executionEnvironment.Clients[0].PayloadPackageIds);
        Assert.Single((await CurrentScopeAsync(workspace)).Packages);
        await traversalEnvironment.AssertRootSettledAsync();
        await executionEnvironment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        DependencyMemberCallGraphRetainsPlatformRouteWithoutPackagePayload()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        (
                            "lib/net11.0/ILInspector.Analysis.CallerGraphTarget.dll",
                            File.ReadAllBytes(CallGraphTargetPath)),
                    ]));
        PackageRootBinding rootBinding = CallGraphRootBinding(
            (CallGraphTargetPackage, RouteVersion));
        RealizedPackageDependencyContext rootContext =
            await RouteRootContextAsync(rootBinding);
        PackageDependencyTraversalOutcome traversal =
            await RouteTraversalAsync(
                environment,
                new PackageDependencyTraversalRootOccurrence(
                    rootContext,
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources));
        var platformTarget = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net12.0"),
            PlatformVersion.Parse("12.0.0"));
        PlatformPruneInventory inventory =
            PlatformPruneInventory.FromExactFamily(
                new PlatformPruneTarget(
                    "Microsoft.NETCore.App",
                    "net12.0",
                    NuGetVersion.Parse("12.0.0")),
                [$"{CallGraphTargetPackage}|{RouteVersion}"]);
        PackageDependencyEdgeRealizationExecution execution =
            PackageDependencyEdgeRealizationQuery.Execute(
                new PackageDependencyEdgeRealizationRequest(
                    traversal,
                    rootOccurrenceIndex: 0,
                    edgeIndex: 0,
                    PackageHouseOperation.Create(
                        PackageHouseOperationProfile.Realize),
                    PackageHouseTargetContext.Exact(
                        "net12.0",
                        platformTarget: platformTarget),
                    inventory));

        var platformEcosystem =
            new WorkspaceEcosystemRegistrationDeclaration(
                WorkspaceEcosystemRegistrationId.Create(
                    "ecosystem.runtime"),
                [],
                [],
                [
                    new WorkspaceEcosystemPopulationDeclaration
                        .PackagePrefix(
                            new PackagePrefixDeclaration(
                                CallGraphTargetPackage)),
                ]);
        await using var workspace = new InspectionWorkspace(
            new WorkspacePlan(
                [
                    new WorkspaceRegistration.Ecosystem(
                        platformEcosystem),
                ]));
        WorkspaceScopeSnapshot empty = await CurrentScopeAsync(workspace);
        WorkspaceScopeSnapshot rooted =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    [rootBinding],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;
        var request = new PackageDependencyMemberCallGraphRequest(
            workspace,
            rooted,
            CurrentRegistrations(workspace),
            traversal,
            [rootBinding],
            [execution],
            new PackageDependencyMemberCallGraphFocus(
                rootOccurrenceIndex: 0,
                ModuleVersionId(CallGraphCallerPath),
                MethodToken(
                    CallGraphCallerPath,
                    "Entry",
                    "RunAcrossBoundary")),
            new(
                maxDepth: 2,
                maxNodes: 10),
            DateTimeOffset.UtcNow.AddMinutes(1),
            supplyChainBaseline:
                PackageSupplyChainBaseline
                    .SelfAndRegisteredEcosystems);

        PackageDependencyMemberCallGraphOutcome.Completed completed =
            Assert.IsType<
                PackageDependencyMemberCallGraphOutcome.Completed>(
                await PackageDependencyMemberCallGraphOperation.ExecuteAsync(
                    request,
                    environment.CreateHouse(
                        (_, _) => new InMemoryPackageStore()),
                    environment.IssueOperation(
                        execution.Request,
                        TestContext.Current.CancellationToken)));

        PackageDependencyMemberCallGraphDestination.Platform destination =
            Assert.IsType<
                PackageDependencyMemberCallGraphDestination.Platform>(
                Assert.Single(completed.Routes).Destination);
        Assert.Equal(platformTarget, destination.Target);
        Assert.True(destination.Supply.DelegatesToPlatform);
        Assert.Empty(environment.Clients[0].PayloadPackageIds);
        Assert.Single((await CurrentScopeAsync(workspace)).Packages);
        Assert.Equal(
            PackageSupplyChainBaseline
                .SelfAndRegisteredEcosystems,
            completed.Baseline.Kind);
        Assert.Equal(
            ["ecosystem.runtime"],
            completed.Baseline.RegisteredEcosystems);
        Assert.Equal(
            [CallGraphRootPackage.ToLowerInvariant()],
            completed.Baseline.RootPackageIds);
        Assert.DoesNotContain(
            completed.NodePackages,
            subject => string.Equals(
                subject.Descriptor.PackageId,
                CallGraphTargetPackage,
                StringComparison.OrdinalIgnoreCase));
        InspectionGraphEdge edge = Assert.Single(completed.Graph.Edges);
        Assert.Equal(
            ("RunAcrossBoundary", "Forward"),
            (
                GraphMember(
                    completed.Graph.Nodes[edge.FromNodeId]).Name,
                GraphMember(
                    completed.Graph.Nodes[edge.ToNodeId]).Name));
        Assert.Equal(
            "unclassified-boundary",
            ExternalFocusRole(completed.Graph, edge));
        await environment.AssertRootSettledAsync();
    }

    private static PackageRootBinding CallGraphRootBinding(
        params (string PackageId, string Version)[] dependencies) =>
        CallGraphRootBinding(
            static content => content,
            dependencies);

    private static PackageRootBinding CallGraphRootBinding(
        Func<InMemoryPackageContent, IPackageContent> contentFactory,
        params (string PackageId, string Version)[] dependencies)
    {
        ArgumentNullException.ThrowIfNull(contentFactory);
        string dependencyXml = string.Join(
            Environment.NewLine,
            dependencies.Select(
                dependency =>
                    $"""<dependency id="{dependency.PackageId}" version="[{dependency.Version}]" />"""));
        byte[] manifest = Encoding.UTF8.GetBytes(
            $$"""
            <package>
              <metadata>
                <id>{{CallGraphRootPackage}}</id>
                <version>{{RouteVersion}}</version>
                <authors>dotnet-inspect</authors>
                <description>Dependency call-graph fixture.</description>
                <dependencies>
                  <group targetFramework="netstandard2.0">
                    {{dependencyXml}}
                  </group>
                </dependencies>
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
                $"{CallGraphRootPackage}.nuspec").Open())
            {
                nuspec.Write(manifest);
            }
            using Stream assembly = archive.CreateEntry(
                "lib/netstandard2.0/ILInspector.Analysis.CallerGraphCaller.dll")
                .Open();
            assembly.Write(File.ReadAllBytes(CallGraphCallerPath));
        }

        var content = new InMemoryPackageContent(
            stream.ToArray(),
            fromCache: false,
            producerKey: "tests");
        var payload = new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(
                CallGraphRootPackage,
                RouteVersion),
            contentFactory(content),
            "tests",
            PackagePayloadOrigin.Download);
        return PackageRootBinding.CreateFromSource(
            payload,
            "netstandard2.0");
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

    private static Analysis.MemberRef GraphMember(
        InspectionGraphNode node) =>
        Assert.IsType<InspectionGraphMemberIdentity.CallGraph>(
            Assert.IsType<InspectionGraphSubject.MemberSubject>(
                node.Subject)
                .Identity)
            .Member;

    private static string ExternalFocusRole(
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

    private static WorkspaceRegistrationRevision CurrentRegistrations(
        InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceRegistrationReadResult.Available>(
            workspace.GetRegistrationSnapshot()).Revision;
}
