using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.LibraryMetadata;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.ResearchQueries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageHouseExecutionTests
{
    [Fact]
    public void IntrinsicCoreLibraryPlatformApplicability_ContractsAreClosedAndResourceFree()
    {
        Type[] closedFamilies =
        [
            typeof(IntrinsicCoreLibraryRouteDecision),
            typeof(IntrinsicCoreLibraryPlatformApplicabilityPlanResult),
            typeof(IntrinsicCoreLibraryPlatformIncompleteEvidence),
            typeof(IntrinsicCoreLibraryWorkspaceContinuationOutcome),
        ];
        Assert.All(
            closedFamilies,
            family => Assert.All(
                family.GetConstructors(
                    BindingFlags.Instance | BindingFlags.NonPublic),
                constructor => Assert.True(
                    constructor.IsPrivate
                    || constructor.IsFamilyAndAssembly)));
        Assert.All(
            closedFamilies.SelectMany(
                family => family.GetNestedTypes(BindingFlags.Public)),
            arm => Assert.True(arm.IsSealed));

        Type[] resourceFreeTypes =
        [
            typeof(MemberCallGraphPlatformPopulationScope),
            typeof(MemberCallGraphFocalScopeReceipt),
            typeof(IntrinsicCoreLibraryPlatformFamilyComposition),
            typeof(IntrinsicCoreLibraryPlatformApplicabilityPlan),
            typeof(IntrinsicCoreLibraryRouteApplicabilityReceipt),
            typeof(IntrinsicCoreLibraryWorkspaceSuccessorEvidence),
            typeof(
                IntrinsicCoreLibraryWorkspaceOccurrenceCorrespondence),
            typeof(IntrinsicCoreLibraryWorkspaceContinuationReceipt),
            .. closedFamilies,
            .. closedFamilies.SelectMany(
                family => family.GetNestedTypes(BindingFlags.Public)),
        ];
        Assert.All(resourceFreeTypes, AssertResourceFree);
    }

    [Fact]
    public async Task
        IntrinsicCoreLibraryPlatformApplicability_SelectsExactPlatformDefinition()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PackageDependencyMemberCallGraphOutcome.Completed graph =
            await IntrinsicCoreLibraryGraphAsync(
                includeRuntimePlatform: true,
                cancellationToken);
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            occurrence = Assert.Single(
                graph.IntrinsicCoreLibraryContextNonParticipation,
                receipt =>
                    receipt.Occurrence.OccurrenceId == 0
                    && receipt.Occurrence.CallSite.Target.Name == "Return"
                    && receipt.Occurrence.Correspondence.Type
                        == TypeName("System", "Void"));

        PlatformPopulationArtifactMaterializationOutcome.Completed?
            completedPopulation = null;
        try
        {
            IntrinsicCoreLibraryRouteDecision decision =
                await IntrinsicCoreLibraryPlatformApplicabilityQuery
                    .ExecuteAsync(
                        occurrence,
                        graph.FocalScope,
                        PlatformFamily.DotNetRuntime,
                        async _ =>
                        {
                            completedPopulation =
                                await CreateCoreLibraryPlatformPopulationAsync(
                                    cancellationToken);
                            return completedPopulation;
                        },
                        CatalogBounds(),
                        cancellationToken);

            var applicable = Assert.IsType<
                IntrinsicCoreLibraryRouteDecision.Applicable>(decision);
            Assert.Same(occurrence, applicable.Receipt.Context);
            Assert.Same(graph.FocalScope, applicable.Receipt.FocalScope);
            Assert.Equal(
                TypeName("System", "Void"),
                applicable.Receipt.Definition.Name);
            Assert.Equal(
                "System.Runtime",
                applicable.Receipt.Definition.Member.PlatformLibrary.Library
                    .ApiAssembly.AssemblyIdentity!.Identity.Name);
            Assert.Equal(
                PlatformFamily.DotNetRuntime,
                applicable.Receipt.Target.Family);
            Assert.Contains(
                applicable.Receipt.FamilyComposition.Populations,
                population =>
                    ReferenceEquals(
                        population.Ecosystem,
                        graph.FocalScope.Ecosystems.Single()));
        }
        finally
        {
            if (completedPopulation is not null)
                await RetireAsync(completedPopulation);
        }
    }

    [Fact]
    public async Task
        IntrinsicCoreLibraryPlatformApplicability_OutsideScopePerformsNoPlatformWork()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PackageDependencyMemberCallGraphOutcome.Completed graph =
            await IntrinsicCoreLibraryGraphAsync(
                includeRuntimePlatform: false,
                cancellationToken);
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            occurrence =
                graph.IntrinsicCoreLibraryContextNonParticipation[0];
        var invocations = 0;

        IntrinsicCoreLibraryRouteDecision decision =
            await IntrinsicCoreLibraryPlatformApplicabilityQuery.ExecuteAsync(
                occurrence,
                graph.FocalScope,
                PlatformFamily.DotNetRuntime,
                _ =>
                {
                    invocations++;
                    throw new InvalidOperationException(
                        "Platform work must not run outside the focal scope.");
                },
                CatalogBounds(),
                cancellationToken);

        Assert.IsType<
            IntrinsicCoreLibraryRouteDecision.OutsideOperationScope>(
                decision);
        Assert.Equal(0, invocations);
        Assert.Empty(graph.FocalScope.PlatformPopulations);
    }

    [Fact]
    public async Task
        IntrinsicCoreLibraryPlatformApplicability_PreservesDefinitionUnavailable()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PackageDependencyMemberCallGraphOutcome.Completed graph =
            await IntrinsicCoreLibraryGraphAsync(
                includeRuntimePlatform: true,
                cancellationToken);
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            occurrence = ExactVoidOccurrence(graph);

        PlatformPopulationArtifactMaterializationOutcome.Completed?
            completedPopulation = null;
        try
        {
            IntrinsicCoreLibraryRouteDecision decision =
                await IntrinsicCoreLibraryPlatformApplicabilityQuery
                    .ExecuteAsync(
                        occurrence,
                        graph.FocalScope,
                        PlatformFamily.DotNetRuntime,
                        async _ =>
                        {
                            completedPopulation =
                                await CreateCoreLibraryPlatformPopulationAsync(
                                    cancellationToken,
                                    ["netstandard.dll"]);
                            return completedPopulation;
                        },
                        CatalogBounds(),
                        cancellationToken);

            var unavailable = Assert.IsType<
                IntrinsicCoreLibraryRouteDecision.Unavailable>(decision);
            Assert.Equal(
                IntrinsicCoreLibraryPlatformApplicabilityUnavailableReason
                    .DefinitionUnavailable,
                unavailable.Reason);
            Assert.Same(occurrence, unavailable.Plan.Context);
            Assert.NotNull(unavailable.Catalog);
            Assert.Equal(
                TypeName("System", "Void"),
                unavailable.Plan.TargetType);
        }
        finally
        {
            if (completedPopulation is not null)
                await RetireAsync(completedPopulation);
        }
    }

    [Fact]
    public async Task
        IntrinsicCoreLibraryPlatformApplicability_RejectsAmbiguousDefinition()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PackageDependencyMemberCallGraphOutcome.Completed graph =
            await IntrinsicCoreLibraryGraphAsync(
                includeRuntimePlatform: true,
                cancellationToken);
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            occurrence = ExactVoidOccurrence(graph);

        PlatformPopulationArtifactMaterializationOutcome.Completed?
            completedPopulation = null;
        try
        {
            IntrinsicCoreLibraryRouteDecision decision =
                await IntrinsicCoreLibraryPlatformApplicabilityQuery
                    .ExecuteAsync(
                        occurrence,
                        graph.FocalScope,
                        PlatformFamily.DotNetRuntime,
                        async _ =>
                        {
                            completedPopulation =
                                await CreateCoreLibraryPlatformPopulationAsync(
                                    cancellationToken,
                                    [
                                        "System.Runtime.dll",
                                        "System.Runtime.net9.dll",
                                    ]);
                            return completedPopulation;
                        },
                        CatalogBounds(),
                        cancellationToken);

            var rejected = Assert.IsType<
                IntrinsicCoreLibraryRouteDecision.Rejected>(decision);
            Assert.Equal(
                IntrinsicCoreLibraryPlatformApplicabilityRejectionReason
                    .AmbiguousDefinition,
                rejected.Reason);
            Assert.Same(occurrence, rejected.Context);
            Assert.NotNull(rejected.Catalog);
            Assert.Equal(
                2,
                Assert.IsType<PlatformTypeCatalogLookupOutcome.Found>(
                        rejected.Catalog.Lookup(
                            TypeName("System", "Void")))
                    .Candidates.Count(
                        candidate =>
                            candidate.Kind
                                == AssemblyTypeDeclarationKind.Definition));
        }
        finally
        {
            if (completedPopulation is not null)
                await RetireAsync(completedPopulation);
        }
    }

    [Fact]
    public async Task
        IntrinsicCoreLibraryPlatformApplicability_PreservesCatalogBound()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PackageDependencyMemberCallGraphOutcome.Completed graph =
            await IntrinsicCoreLibraryGraphAsync(
                includeRuntimePlatform: true,
                cancellationToken);
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            occurrence = ExactVoidOccurrence(graph);

        PlatformPopulationArtifactMaterializationOutcome.Completed?
            completedPopulation = null;
        try
        {
            IntrinsicCoreLibraryRouteDecision decision =
                await IntrinsicCoreLibraryPlatformApplicabilityQuery
                    .ExecuteAsync(
                        occurrence,
                        graph.FocalScope,
                        PlatformFamily.DotNetRuntime,
                        async _ =>
                        {
                            completedPopulation =
                                await CreateCoreLibraryPlatformPopulationAsync(
                                    cancellationToken);
                            return completedPopulation;
                        },
                        CatalogBounds(maximumAssemblies: 1),
                        cancellationToken);

            var incomplete = Assert.IsType<
                IntrinsicCoreLibraryRouteDecision.Incomplete>(decision);
            Assert.Same(occurrence, incomplete.Plan.Context);
            Assert.IsType<
                IntrinsicCoreLibraryPlatformIncompleteEvidence.Catalog>(
                    incomplete.Evidence);
        }
        finally
        {
            if (completedPopulation is not null)
                await RetireAsync(completedPopulation);
        }
    }

    [Fact]
    public async Task
        IntrinsicCoreLibraryPlatformApplicability_PreservesPopulationBound()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PackageDependencyMemberCallGraphOutcome.Completed graph =
            await IntrinsicCoreLibraryGraphAsync(
                includeRuntimePlatform: true,
                cancellationToken);
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            occurrence = ExactVoidOccurrence(graph);

        IntrinsicCoreLibraryRouteDecision decision =
            await IntrinsicCoreLibraryPlatformApplicabilityQuery.ExecuteAsync(
                occurrence,
                graph.FocalScope,
                PlatformFamily.DotNetRuntime,
                _ => CreateCoreLibraryPlatformPopulationOutcomeAsync(
                    cancellationToken,
                    maximumAssemblies: 1),
                CatalogBounds(),
                cancellationToken);

        var incomplete = Assert.IsType<
            IntrinsicCoreLibraryRouteDecision.Incomplete>(decision);
        Assert.Same(occurrence, incomplete.Plan.Context);
        var population = Assert.IsType<
            IntrinsicCoreLibraryPlatformIncompleteEvidence.Population>(
                incomplete.Evidence);
        Assert.IsType<
            PlatformHouseOutcome<PlatformPopulationRealizationValue>
                .Incomplete>(
                    population.Evidence.Outcome);
    }

    [Fact]
    public async Task
        IntrinsicCoreLibraryPlatformApplicability_PreservesPopulationRejection()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PackageDependencyMemberCallGraphOutcome.Completed graph =
            await IntrinsicCoreLibraryGraphAsync(
                includeRuntimePlatform: true,
                cancellationToken);
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            occurrence = ExactVoidOccurrence(graph);

        IntrinsicCoreLibraryRouteDecision decision =
            await IntrinsicCoreLibraryPlatformApplicabilityQuery.ExecuteAsync(
                occurrence,
                graph.FocalScope,
                PlatformFamily.DotNetRuntime,
                _ => CreateCoreLibraryPlatformPopulationOutcomeAsync(
                    cancellationToken,
                    role: PlatformPopulationMemberRole.BindingSupport),
                CatalogBounds(),
                cancellationToken);

        var rejected = Assert.IsType<
            IntrinsicCoreLibraryRouteDecision.Rejected>(decision);
        Assert.Equal(
            IntrinsicCoreLibraryPlatformApplicabilityRejectionReason
                .PlatformEvidenceMismatch,
            rejected.Reason);
        Assert.Same(occurrence, rejected.Context);
        Assert.NotNull(rejected.Population);
        Assert.IsType<
            PlatformHouseOutcome<PlatformPopulationRealizationValue>
                .Rejected>(
                    rejected.Population.Outcome);
    }

    [Fact]
    public async Task
        IntrinsicCoreLibraryPlatformApplicability_RejectsScopeMismatchBeforePlatformWork()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PackageDependencyMemberCallGraphOutcome.Completed first =
            await IntrinsicCoreLibraryGraphAsync(
                includeRuntimePlatform: true,
                cancellationToken);
        PackageDependencyMemberCallGraphOutcome.Completed second =
            await IntrinsicCoreLibraryGraphAsync(
                includeRuntimePlatform: true,
                cancellationToken);
        var invocations = 0;

        IntrinsicCoreLibraryRouteDecision decision =
            await IntrinsicCoreLibraryPlatformApplicabilityQuery.ExecuteAsync(
                ExactVoidOccurrence(first),
                second.FocalScope,
                PlatformFamily.DotNetRuntime,
                _ =>
                {
                    invocations++;
                    throw new InvalidOperationException(
                        "Platform work must not run for mismatched Scope evidence.");
                },
                CatalogBounds(),
                cancellationToken);

        var rejected = Assert.IsType<
            IntrinsicCoreLibraryRouteDecision.Rejected>(decision);
        Assert.Equal(
            IntrinsicCoreLibraryPlatformApplicabilityRejectionReason
                .ScopeMismatch,
            rejected.Reason);
        Assert.Same(ExactVoidOccurrence(first), rejected.Context);
        Assert.Same(second.FocalScope, rejected.FocalScope);
        Assert.Equal(
            PlatformFamily.DotNetRuntime,
            rejected.RequestedFamily);
        Assert.Equal(0, invocations);
    }

    [Fact]
    public async Task
        IntrinsicCoreLibraryPlatformApplicability_RejectsPlatformTargetMismatch()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PackageDependencyMemberCallGraphOutcome.Completed graph =
            await IntrinsicCoreLibraryGraphAsync(
                includeRuntimePlatform: true,
                cancellationToken);
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            occurrence = ExactVoidOccurrence(graph);
        var mismatchedTarget = new PlatformFamilyTarget(
            PlatformFamily.AspNetCore,
            PlatformTargetFramework.Parse("net10.0"),
            PlatformVersion.Parse("10.0.10"));

        PlatformPopulationArtifactMaterializationOutcome.Completed?
            completedPopulation = null;
        try
        {
            IntrinsicCoreLibraryRouteDecision decision =
                await IntrinsicCoreLibraryPlatformApplicabilityQuery
                    .ExecuteAsync(
                        occurrence,
                        graph.FocalScope,
                        PlatformFamily.DotNetRuntime,
                        async _ =>
                        {
                            completedPopulation =
                                await CreateCoreLibraryPlatformPopulationAsync(
                                    cancellationToken,
                                    target: mismatchedTarget);
                            return completedPopulation;
                        },
                        CatalogBounds(),
                        cancellationToken);

            var rejected = Assert.IsType<
                IntrinsicCoreLibraryRouteDecision.Rejected>(decision);
            Assert.Equal(
                IntrinsicCoreLibraryPlatformApplicabilityRejectionReason
                    .PlatformTargetMismatch,
                rejected.Reason);
            Assert.Same(occurrence, rejected.Context);
            Assert.Null(rejected.Catalog);
            Assert.Same(
                completedPopulation!.Population.Receipt,
                rejected.PopulationReceipt);
        }
        finally
        {
            if (completedPopulation is not null)
                await RetireAsync(completedPopulation);
        }
    }

    [Fact]
    public async Task
        IntrinsicCoreLibraryPlatformApplicability_SelectsBindingSupportDefinition()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PackageDependencyMemberCallGraphOutcome.Completed graph =
            await IntrinsicCoreLibraryGraphAsync(
                PlatformFamily.AspNetCore,
                cancellationToken);
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            occurrence = ExactVoidOccurrence(graph);
        var aspNetTarget = new PlatformFamilyTarget(
            PlatformFamily.AspNetCore,
            PlatformTargetFramework.Parse("net10.0"),
            PlatformVersion.Parse("10.0.10"));
        var runtimeTarget = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net10.0"),
            PlatformVersion.Parse("10.0.10"));

        PlatformPopulationArtifactMaterializationOutcome.Completed?
            completedPopulation = null;
        try
        {
            IntrinsicCoreLibraryRouteDecision decision =
                await IntrinsicCoreLibraryPlatformApplicabilityQuery
                    .ExecuteAsync(
                        occurrence,
                        graph.FocalScope,
                        PlatformFamily.AspNetCore,
                        async _ =>
                        {
                            completedPopulation =
                                await CreateCoreLibraryPlatformPopulationAsync(
                                    cancellationToken,
                                    ["System.Runtime.dll"],
                                    aspNetTarget,
                                    runtimeTarget,
                                    PlatformPopulationMemberRole
                                        .BindingSupport);
                            return completedPopulation;
                        },
                        CatalogBounds(),
                        cancellationToken);

            var applicable = Assert.IsType<
                IntrinsicCoreLibraryRouteDecision.Applicable>(decision);
            Assert.Equal(aspNetTarget, applicable.Receipt.Target);
            Assert.Equal(runtimeTarget, applicable.Receipt.Member.Target);
            Assert.Equal(
                PlatformPopulationMemberRole.BindingSupport,
                applicable.Receipt.Member.Role);
        }
        finally
        {
            if (completedPopulation is not null)
                await RetireAsync(completedPopulation);
        }
    }

    private static async Task<
        PackageDependencyMemberCallGraphOutcome.Completed>
        IntrinsicCoreLibraryGraphAsync(
            bool includeRuntimePlatform,
            CancellationToken cancellationToken) =>
        await IntrinsicCoreLibraryGraphAsync(
            includeRuntimePlatform
                ? PlatformFamily.DotNetRuntime
                : null,
            cancellationToken);

    private static async Task<
        PackageDependencyMemberCallGraphOutcome.Completed>
        IntrinsicCoreLibraryGraphAsync(
            PlatformFamily? platformFamily,
            CancellationToken cancellationToken)
    {
        WorkspacePlan plan =
            IntrinsicCoreLibraryWorkspacePlan(platformFamily);
        await using var workspace = new InspectionWorkspace(plan);
        return await IntrinsicCoreLibraryGraphAsync(
            workspace,
            platformFamily,
            cancellationToken);
    }

    private static async Task<
        PackageDependencyMemberCallGraphOutcome.Completed>
        IntrinsicCoreLibraryGraphAsync(
            InspectionWorkspace workspace,
            PlatformFamily? platformFamily,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
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

        WorkspaceScopeSnapshot empty = await CurrentScopeAsync(workspace);
        WorkspaceScopeSnapshot rooted =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    [rootBinding],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    cancellationToken)).Snapshot;
        PackageHouseOperation operation =
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize);
        WorkspaceRegistrationRevision registrations =
            CurrentRegistrations(workspace);
        var request = new PackageDependencyMemberCallGraphRequest(
            workspace,
            rooted,
            registrations,
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
                        cancellationToken,
                        operation.RequestTimeout,
                        operation.OperationTimeout)));
        Assert.Same(
            completed.ScopeRevision,
            completed.FocalScope.ScopeRevision);
        Assert.Same(
            registrations.Identity,
            completed.FocalScope.RegistrationRevision);
        Assert.Equal(
            MemberCallGraphFocalLength.Everything,
            completed.FocalScope.FocalLength);
        Assert.Equal(
            platformFamily is null ? 0 : 1,
            completed.FocalScope.PlatformPopulations.Length);
        return completed;
    }

    private static WorkspacePlan IntrinsicCoreLibraryWorkspacePlan(
        PlatformFamily? platformFamily) =>
        platformFamily is { } family
            ? new WorkspacePlan(
                [
                    new WorkspaceRegistration.Ecosystem(
                        new WorkspaceEcosystemRegistrationDeclaration(
                            WorkspaceEcosystemRegistrationId.Create(
                                "ecosystem.runtime"),
                            [],
                            [],
                            [
                                new WorkspaceEcosystemPopulationDeclaration
                                    .Platform(
                                        new PlatformLibraryPopulationDeclaration(
                                            family)),
                            ])),
                ])
            : WorkspacePlan.Empty;

    private static async ValueTask<
        PlatformPopulationArtifactMaterializationOutcome.Completed>
        CreateCoreLibraryPlatformPopulationAsync(
            CancellationToken cancellationToken,
            string[]? assetNames = null,
            PlatformFamilyTarget? target = null,
            PlatformFamilyTarget? memberTarget = null,
            PlatformPopulationMemberRole role =
                PlatformPopulationMemberRole.Focus)
        =>
        Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Completed>(
                await CreateCoreLibraryPlatformPopulationOutcomeAsync(
                    cancellationToken,
                    assetNames,
                    target,
                    memberTarget,
                    role: role));

    private static async ValueTask<
        PlatformPopulationArtifactMaterializationOutcome>
        CreateCoreLibraryPlatformPopulationOutcomeAsync(
            CancellationToken cancellationToken,
            string[]? assetNames = null,
            PlatformFamilyTarget? target = null,
            PlatformFamilyTarget? memberTarget = null,
            int maximumAssemblies = 16,
            PlatformPopulationMemberRole role =
                PlatformPopulationMemberRole.Focus)
    {
        target ??=
            new(
                PlatformFamily.DotNetRuntime,
                PlatformTargetFramework.Parse("net10.0"),
                PlatformVersion.Parse("10.0.10"));
        memberTarget ??= target;
        PlatformSourceCapabilityIdentity capability =
            PlatformSourceCapabilityIdentity.Create(
                "reference-pack");
        PlatformHouseRequest request =
            new(
                PlatformHouseRequestIdentity.Create(
                    "intrinsic-corelib-platform"),
                new PlatformTargetDemand.Exact(target),
                new PlatformHouseRequestOrigin.Standalone(
                    PlatformStandaloneOperationIdentity.Create(
                        "intrinsic-corelib-test")),
                new PlatformHouseOperation.Realize(
                    new PlatformPopulationDemand.CompletePopulation(),
                    PlatformViewDemand.Reference),
                new PlatformSourcePlan(
                    PlatformSourcePlanIdentity.Create(
                        "intrinsic-corelib-sources"),
                    PlatformSourcePolicyGeneration.Create(
                        "intrinsic-corelib-generation"),
                    [
                        new PlatformSourceSelection(
                            PlatformSourceFacet.Reference,
                            PlatformSourceSelectionMode.Precedence,
                            [capability]),
                    ]),
                new PlatformHouseWorkBudget(
                    maxSourceOperations: 4,
                    maxTargetCandidates: 4,
                    maxAssemblies: maximumAssemblies,
                    maxXmlDocuments: 0,
                    maxPortablePdbs: 0,
                    maxSourceDocuments: 0,
                    maxBytes: 32 * 1024 * 1024,
                    maxForwardingHops: 8,
                    maxDuration: TimeSpan.FromSeconds(30)),
                cancellationToken);
        PlatformSourceContribution.Realization contribution =
            new(
                PlatformSourceFacet.Reference,
                capability,
                request.Snapshot,
                PlatformSourceGeneration.Create(
                    "intrinsic-corelib-reference-generation"),
                target,
                PlatformSourceCoordinateIdentity.Create(
                    "intrinsic-corelib-reference-coordinate"),
                ((PlatformHouseOperation.Realize)request.Operation)
                    .Population,
                PlatformSourceContributionCompleteness.Authoritative);
        assetNames ??=
            [
                "mscorlib.dll",
                "netstandard.dll",
                "System.Runtime.dll",
            ];
        string[] paths = [.. assetNames.Select(PlatformAsset)];
        byte[][] images =
            await Task.WhenAll(
                paths.Select(
                    path => File.ReadAllBytesAsync(
                        path,
                        cancellationToken)));
        PlatformPopulationLibraryArtifactMaterializationItem[] items =
        [
            .. images.Select(
                image =>
                    new PlatformPopulationLibraryArtifactMaterializationItem(
                        new PlatformLibraryArtifactMaterializationItem(
                            contribution,
                            new PlatformTestProvenance(),
                            AssemblyIdentity(image),
                            image.LongLength,
                            _ => new MemoryStream(
                                image,
                                writable: false)),
                        new PlatformPopulationMemberAttribution(
                            memberTarget,
                            role))),
        ];
        PlatformHouseConsumedWork consumed =
            new(
                sourceOperations: 1,
                targetCandidates: 0,
                assemblies: items.Length,
                xmlDocuments: 0,
                portablePdbs: 0,
                sourceDocuments: 0,
                bytes: images.Sum(
                    image => (long)image.Length),
                forwardingHops: 0,
                targetComparisons: 0,
                elapsed: TimeSpan.Zero);

        return await PlatformHousePopulationArtifactMaterializer
            .MaterializeReferencesAsync(
                request,
                items,
                consumed,
                "intrinsic-corelib-platform");
    }

    private static
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
        ExactVoidOccurrence(
            PackageDependencyMemberCallGraphOutcome.Completed graph) =>
        Assert.Single(
            graph.IntrinsicCoreLibraryContextNonParticipation,
            receipt =>
                receipt.Occurrence.OccurrenceId == 0
                && receipt.Occurrence.CallSite.Target.Name == "Return"
                && receipt.Occurrence.Correspondence.Type
                    == TypeName("System", "Void"));

    private static PlatformTypeCatalogDerivationBounds CatalogBounds(
        int maximumAssemblies = 16) =>
        new(
            new LibraryTypeDeclarationInventoryInspectionBounds(
                maximumAssemblyBytes: 16 * 1024 * 1024,
                maximumRetainedDeclarations: 100_000,
                maximumMetadataRows: int.MaxValue,
                maximumRetainedTextCharacters: int.MaxValue),
            maximumAssemblies,
            maximumAggregateAssemblyBytes: 32 * 1024 * 1024,
            maximumRetainedEntries: 500_000,
            maximumDuration: TimeSpan.FromSeconds(30));

    private static async ValueTask RetireAsync(
        PlatformPopulationArtifactMaterializationOutcome.Completed completed)
    {
        foreach (var owner in completed.Population.Owners)
            await owner.DisposeAsync();
        await completed.Artifacts.DisposeAsync();
    }

    private static AssemblyReferenceIdentity AssemblyIdentity(byte[] image)
    {
        using var peReader =
            new PEReader(new MemoryStream(image, writable: false));
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            peReader.GetMetadataReader());
    }

    private static MetadataTypeDefinitionName TypeName(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    ImmutableArray.Create(segments)))
            .Name;

    private static void AssertResourceFree(Type type)
    {
        Assert.False(typeof(IDisposable).IsAssignableFrom(type));
        Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(type));
        Assert.DoesNotContain(
            type.GetFields(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic),
            field =>
                typeof(IDisposable).IsAssignableFrom(field.FieldType)
                || typeof(IAsyncDisposable).IsAssignableFrom(
                    field.FieldType)
                || typeof(Delegate).IsAssignableFrom(field.FieldType)
                || typeof(Stream).IsAssignableFrom(field.FieldType));
    }

    private static string PlatformAsset(string name) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "IntrinsicCoreLibraryPlatform",
            name);

    private sealed record PlatformTestProvenance : IArtifactProvenance;
}
