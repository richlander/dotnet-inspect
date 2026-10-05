using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.ResearchQueries;
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

    private static string SystemTextJsonNetStandardPath =>
        Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "IntrinsicCoreLibrary",
            "System.Text.Json.dll");

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
            TraversalTargetFrameworkPolicy.ProductDefaultTargetFramework,
            available.Document.TraversalTargetPolicy.TargetFramework);
        Assert.Equal(
            TraversalTargetFrameworkPolicySource.ProductDefault,
            available.Document.TraversalTargetPolicy.Source);
        Assert.Equal(
            MemberCallGraphFocalLength.Everything,
            available.Document.FocalScope.FocalLength);
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
        Assert.Single(
            available.Document
                .IntrinsicCoreLibraryContextNonParticipation);
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
        DependencyMemberCallGraphContinuationPreservesInitialWorkspaceFailure()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior([RouteVersion]));
        PackageHouseOperation realizationOperation =
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize);
        PackageDependencyMemberCallGraphInspectionSource source =
            CreateCallGraphSource(environment);
        var continuation =
            new PackageDependencyMemberCallGraphContinuation(
                new UnexpectedCallGraphContinuationSource(),
                ContinuationBudget());

        InspectionEnvelope<
            PackageDependencyMemberCallGraphInspectionOutcome> envelope =
            await PackageDependencyMemberCallGraphInspection.ExecuteAsync(
                CallGraphInspectionRequest(
                    realizationOperation,
                    "RunAcrossBoundary",
                    workspaceDeadline:
                        DateTimeOffset.UtcNow.AddMinutes(-1)),
                source,
                continuation,
                TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<
                PackageDependencyMemberCallGraphInspectionOutcome.Unavailable>(
                envelope.Content);
        Assert.Equal(
            PackageDependencyMemberCallGraphInspectionUnavailableReason
                .RootWorkspaceNotCommitted,
            unavailable.Reason);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        DependencyMemberCallGraphRetainsIntrinsicCoreLibraryContextNonParticipation()
    {
        string systemTextJson = SystemTextJsonNetStandardPath;
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior([RouteVersion]));
        PackageRootBinding rootBinding =
            CallGraphRootBindingFromAssembly(
                "System.Text.Json",
                systemTextJson);
        RealizedPackageDependencyContext rootContext =
            await RouteRootContextAsync(rootBinding);
        PackageDependencyTraversalOutcome traversal =
            await RouteTraversalAsync(
                environment,
                new PackageDependencyTraversalRootOccurrence(
                    rootContext,
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources));
        Assert.Empty(traversal.Edges);

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
        PackageHouseOperation operation =
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize);
        var request = new PackageDependencyMemberCallGraphRequest(
            workspace,
            rooted,
            CurrentRegistrations(workspace),
            traversal,
            [rootBinding],
            [],
            new PackageDependencyMemberCallGraphFocus(
                rootOccurrenceIndex: 0,
                ModuleVersionId(systemTextJson),
                MethodToken(
                    systemTextJson,
                    "JsonDocument",
                    "Dispose")),
            new(
                maxDepth: 4,
                maxNodes: 30),
            DateTimeOffset.UtcNow.AddMinutes(1));

        PackageDependencyMemberCallGraphOutcome.Completed completed =
            Assert.IsType<
                PackageDependencyMemberCallGraphOutcome.Completed>(
                await PackageDependencyMemberCallGraphOperation.ExecuteAsync(
                    request,
                    environment.CreateHouse(
                        (_, _) => new InMemoryPackageStore()),
                    environment.Root.IssueOperationLease(
                        TestContext.Current.CancellationToken,
                        operation.RequestTimeout,
                        operation.OperationTimeout)));

        Assert.Empty(completed.Routes);
        Assert.Same(
            completed.ScopeRevision,
            completed.FocalScope.ScopeRevision);
        Assert.Equal(
            MemberCallGraphFocalLength.Everything,
            completed.FocalScope.FocalLength);
        Assert.Empty(completed.FocalScope.PlatformPopulations);
        Assert.NotEmpty(
            completed.IntrinsicCoreLibraryContextNonParticipation);
        Assert.True(
            completed.IntrinsicCoreLibraryContextNonParticipation
                .Select(receipt => receipt.Occurrence.OccurrenceId)
                .Distinct()
                .Count() > 1);
        Assert.All(
            completed.IntrinsicCoreLibraryContextNonParticipation,
            receipt =>
            {
                Assert.Same(
                    completed.ScopeRevision,
                    receipt.ScopeRevision);
                Assert.Same(
                    receipt.Occurrence.Origin.Registration,
                    receipt.Occurrence.CallSite.Identity
                        .SourceRegistration);
                Assert.Same(
                    rootBinding.Root.Identity,
                    receipt.Occurrence.Origin.Package);
                TypeResolutionOutcome.Unavailable unavailable =
                    Assert.IsType<TypeResolutionOutcome.Unavailable>(
                        receipt.Occurrence.Correspondence.Outcome);
                Assert.IsType<
                    AssemblyBindingTarget.IntrinsicCoreLibrary>(
                        unavailable.Target);
                AssemblyBindingOrigin.RequestingAssembly origin =
                    Assert.IsType<
                        AssemblyBindingOrigin.RequestingAssembly>(
                        unavailable.Origin);
                Assert.Same(
                    receipt.Occurrence.Origin.Registration,
                    origin.Registration);
                Assert.Equal(
                    AssemblyResolutionScope.Platform,
                    unavailable.Scope);
            });
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
                    PackageHouseTargetContext.Exact(
                        TraversalTargetFrameworkPolicy
                            .ProductDefaultTargetFramework)));
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
            TraversalTargetFrameworkPolicy.ProductDefaultTargetFramework,
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
                    PackageHouseTargetContext.Exact(
                        TraversalTargetFrameworkPolicy
                            .ProductDefaultTargetFramework)));
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
                    PackageHouseTargetContext.Exact(
                        TraversalTargetFrameworkPolicy
                            .ProductDefaultTargetFramework)));

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
                    PackageHouseTargetContext.Exact(
                        TraversalTargetFrameworkPolicy
                            .ProductDefaultTargetFramework)));
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
                            PackageHouseTargetContext.Exact(
                                TraversalTargetFrameworkPolicy
                                    .ProductDefaultTargetFramework)))),
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
        DependencyMemberCallGraphPreparationResolvesNonReducedPackageVersion()
    {
        const string lowerVersion = "1.0.0";
        const string higherVersion = "2.0.0";
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [lowerVersion, higherVersion],
                    PayloadContentEntries:
                    [
                        (
                            "lib/net11.0/ILInspector.Analysis.CallerGraphTarget.dll",
                            File.ReadAllBytes(CallGraphTargetPath)),
                    ]));
        PackageRootBinding rootBinding = CallGraphRootBinding(
            (CallGraphTargetPackage, lowerVersion));
        PackageRootBinding secondRootBinding = RouteRootBinding(
            "callgraph.second-root",
            (CallGraphTargetPackage, higherVersion));
        RealizedPackageDependencyContext rootContext =
            await RouteRootContextAsync(rootBinding);
        RealizedPackageDependencyContext secondRootContext =
            await RouteRootContextAsync(secondRootBinding);
        PackageDependencyTraversalOutcome traversal =
            await RouteTraversalAsync(
                environment,
                new PackageDependencyTraversalRootOccurrence(
                    rootContext,
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources),
                new PackageDependencyTraversalRootOccurrence(
                    secondRootContext,
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources));
        Assert.Equal(2, traversal.Edges.Length);
        PackageDependencyEdgeRealizationExecution[] executions =
        [
            .. Enumerable.Range(0, traversal.Edges.Length)
                .Select(
                    edgeIndex =>
                        PackageDependencyEdgeRealizationQuery.Execute(
                            new PackageDependencyEdgeRealizationRequest(
                                traversal,
                                rootOccurrenceIndex:
                                    Enumerable.Range(
                                            0,
                                            traversal.Roots.Length)
                                        .Single(rootIndex =>
                                            traversal
                                                .RootReachability[rootIndex]
                                                .IsEdgeAdmitted(
                                                    edgeIndex,
                                                    out _)),
                                edgeIndex,
                                PackageHouseOperation.Create(
                                    PackageHouseOperationProfile.Realize),
                                PackageHouseTargetContext.Exact(
                                    TraversalTargetFrameworkPolicy
                                        .ProductDefaultTargetFramework)))),
        ];

        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot empty = await CurrentScopeAsync(workspace);
        WorkspaceScopeSnapshot rooted =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    [rootBinding, secondRootBinding],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;
        var request = new PackageDependencyMemberCallGraphRequest(
            workspace,
            rooted,
            CurrentRegistrations(workspace),
            traversal,
            [rootBinding, secondRootBinding],
            [.. executions],
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

        PackageDependencyMemberCallGraphPreparation preparation =
            Assert.IsType<
                PackageDependencyMemberCallGraphPreparationOutcome.Prepared>(
                    await PackageDependencyMemberCallGraphOperation.PrepareAsync(
                        request,
                        environment.CreateHouse(
                            (_, _) => new InMemoryPackageStore()),
                        environment.IssueOperation(
                            executions[0].Request,
                            TestContext.Current.CancellationToken))).Value;
        PackageDependencyEdgeRealizationSubject lowerRoute =
            Assert.Single(
                executions,
                execution =>
                    execution.Subject.Candidate.Coordinate.Version
                        == lowerVersion).Subject;
        PackageRootBinding selected =
            preparation.ResolvePackageBinding(lowerRoute);

        Assert.Equal(lowerVersion, selected.Coordinate.Version);
        Assert.DoesNotContain(
            preparation.GraphBindings,
            binding => ReferenceEquals(binding, selected));
        Assert.Contains(
            preparation.GraphBindings,
            binding =>
                binding.Coordinate.PackageId.Equals(
                    CallGraphTargetPackage,
                    StringComparison.OrdinalIgnoreCase)
                && binding.Coordinate.Version == higherVersion);
        ImmutableArray<PackageRootBinding> successorBindings =
        [
            .. preparation.GraphBindings.Select(
                binding =>
                    binding.Coordinate.PackageId.Equals(
                        selected.Coordinate.PackageId,
                        StringComparison.OrdinalIgnoreCase)
                        ? selected
                        : binding),
        ];
        Assert.Contains(selected, successorBindings);
        Assert.DoesNotContain(
            successorBindings,
            binding =>
                binding.Coordinate.PackageId.Equals(
                    selected.Coordinate.PackageId,
                    StringComparison.OrdinalIgnoreCase)
                && !ReferenceEquals(binding, selected));
        await using var successorWorkspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot successorEmpty =
            await CurrentScopeAsync(successorWorkspace);
        WorkspaceScopeSnapshot successorScope =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await successorWorkspace.AddPackagesAsync(
                    successorEmpty.Revision,
                    successorEmpty.PublicationBase,
                    successorBindings,
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;
        PackageCompileAsset selectedAsset =
            Assert.Single(selected.Root.AssetSelection.Assets);
        await using PackageDependencyMemberCallGraphGeneration generation =
            await PackageDependencyMemberCallGraphOperation
                .ExecuteGenerationAsync(
                    successorWorkspace,
                    successorScope,
                    CurrentRegistrations(successorWorkspace),
                    successorBindings,
                    preparation.Root,
                    request.Focus,
                    request.Graph,
                    request.SupplyChainBaseline,
                    request.RealizationOptions,
                    [new(selected, selectedAsset)],
                    [],
                    TestContext.Current.CancellationToken);

        Assert.NotNull(generation.Outcome);
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
                    PackageHouseTargetContext.Exact(
                        TraversalTargetFrameworkPolicy
                            .ProductDefaultTargetFramework)));

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
                    PackageHouseTargetContext.Exact(
                        TraversalTargetFrameworkPolicy
                            .ProductDefaultTargetFramework)));

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
                    PackageHouseTargetContext.Exact(
                        TraversalTargetFrameworkPolicy
                            .ProductDefaultTargetFramework)));

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
            PlatformTargetFramework.Parse(
                TraversalTargetFrameworkPolicy
                    .ProductDefaultTargetFramework),
            PlatformVersion.Parse("11.0.0"));
        PlatformPruneInventory inventory =
            PlatformPruneInventory.FromExactFamily(
                new PlatformPruneTarget(
                    "Microsoft.NETCore.App",
                    TraversalTargetFrameworkPolicy
                        .ProductDefaultTargetFramework,
                    NuGetVersion.Parse("11.0.0")),
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
                        TraversalTargetFrameworkPolicy
                            .ProductDefaultTargetFramework,
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

    private static PackageDependencyMemberCallGraphInspectionSource
        CreateCallGraphSource(HouseEnvironment environment)
    {
        var candidateSource =
            new AuthorizedPackageDependencyCandidateSource(
                environment.Authorization,
                environment.Root);
        return new(
            new PackageDependencyTraversalCandidateAdapter(
                candidateSource),
            new UnexpectedManifestAcquirer(),
            environment.CreateHouse(
                (_, _) => new InMemoryPackageStore()),
            (operation, cancellationToken) =>
                environment.Root.IssueOperationLease(
                    cancellationToken,
                    operation.RequestTimeout,
                    operation.OperationTimeout));
    }

    private static PackageDependencyMemberCallGraphInspectionRequest
        CallGraphInspectionRequest(
        PackageHouseOperation realizationOperation,
        string member,
        int maximumDependencyDepth = 1,
        DateTimeOffset? workspaceDeadline = null,
        params (string PackageId, string Version)[] dependencies) =>
        new(
            CallGraphRootBinding(dependencies),
            new(
                ModuleVersionId(CallGraphCallerPath),
                MethodToken(
                    CallGraphCallerPath,
                    "Entry",
                    member)),
            TraversalTargetFrameworkPolicy.ProductDefault,
            new(
                maxDepth: 2,
                maxNodes: 10),
            realizationOperation,
            workspaceDeadline
                ?? DateTimeOffset.UtcNow.AddMinutes(1),
            maximumDependencyDepth,
            new(
                maxManifestProjections: 3,
                maxDeclarationResolutions: 3));

    private static AssemblyReferenceResolutionWorkBudget
        ContinuationBudget(
        int maxPackageRouteOccurrences = 16) =>
        new(
            maxPackageRouteOccurrences,
            maxPackageCandidateOperations: 16,
            maxSourceOperations: 16,
            maxAcquisitions: 16,
            maxRealizedAssemblies: 16,
            maxTransferBytes: 16 * 1024 * 1024,
            maxRetainedAssemblyBytes: 16 * 1024 * 1024,
            maxWorkspaceReplacements: 4,
            deadline: DateTimeOffset.UtcNow.AddMinutes(1));

    private sealed class UnexpectedCallGraphContinuationSource :
        PackageDependencyMemberCallGraphExternalContinuationSource
    {
        public override ValueTask<
            PackageDependencyMemberCallGraphPlatformRouteFormationOutcome>
            FormPlatformRouteAsync(
            AssemblyBindingRequest request,
            AssemblyReferenceResolutionGenerationReceipt generation,
            MemberCallGraphFocalScopeReceipt focalScope,
            PackageAssemblyReferenceRouteEligibilityReceipt packageRoutes,
            AssemblyReferenceResolutionWorkLedger work,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Initial Workspace failure must precede route formation.");

        public override ValueTask<
            ExternalAssemblyReferenceSupplierOutcome> ResolveAsync(
            PackageAssemblyReferenceExternalRoute packageRoute,
            PlatformAssemblyReferenceExternalRoute platformRoute,
            AssemblyBindingSelection referencingContextSelection,
            AssemblyReferenceResolutionWorkLedger work,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Initial Workspace failure must precede supplier resolution.");

        public override ValueTask<ImmutableArray<
            PackageAssemblyContextPlatformLibrary>>
            AdmitPlatformPopulationAsync(
            InspectionWorkspace workspace,
            WorkspaceRegistrationRevision registrations,
            PlatformFamilyTarget target,
            AssemblyReferenceResolutionWorkLedger work,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Initial Workspace failure must precede Platform population admission.");
    }

    private static PackageRootBinding CallGraphRootBindingFromAssembly(
        string packageId,
        string assemblyPath)
    {
        byte[] manifest = Encoding.UTF8.GetBytes(
            $$"""
            <package>
              <metadata>
                <id>{{packageId}}</id>
                <version>{{RouteVersion}}</version>
                <authors>dotnet-inspect</authors>
                <description>Intrinsic CoreLib call-graph fixture.</description>
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
            using Stream assembly = archive.CreateEntry(
                $"lib/netstandard2.0/{Path.GetFileName(assemblyPath)}")
                .Open();
            assembly.Write(File.ReadAllBytes(assemblyPath));
        }

        var payload = new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(packageId, RouteVersion),
            new InMemoryPackageContent(
                stream.ToArray(),
                fromCache: false,
                producerKey: "tests"),
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
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(assemblyPath);
        return session.InspectImage(reader =>
        {
            MetadataReader metadata = reader.GetMetadataReader();
            TypeDefinitionHandle type =
                metadata.TypeDefinitions.Single(handle =>
                    metadata.GetString(
                        metadata.GetTypeDefinition(handle).Name)
                        == typeName);
            MethodDefinitionHandle method =
                metadata.GetTypeDefinition(type).GetMethods().Single(handle =>
                {
                    MethodDefinition definition =
                        metadata.GetMethodDefinition(handle);
                    return definition.RelativeVirtualAddress != 0
                        && metadata.GetString(definition.Name)
                            == methodName;
                });
            return MetadataTokens.GetToken(method);
        });
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
                            characteristic.Payload.Descriptor,
                            InspectionGraphFocusCatalog.Role)
                        && characteristic.Target
                            == InspectionGraphTarget.Edge(edge.Id))
                    .Payload.Value)
                .Values);

    private static WorkspaceRegistrationRevision CurrentRegistrations(
        InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceRegistrationReadResult.Available>(
            workspace.GetRegistrationSnapshot()).Revision;
}
