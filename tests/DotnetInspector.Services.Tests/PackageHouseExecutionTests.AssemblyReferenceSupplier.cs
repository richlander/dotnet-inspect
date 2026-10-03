using System.Collections.Immutable;

using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using NuGet.Versioning;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageHouseExecutionTests
{
    [Fact]
    public async Task
        AssemblyReferenceSupplierSelectsExactPackageBeforeLowerTiers()
    {
        AssemblyReferenceIdentity identity = CallGraphTargetIdentity();
        string exactPackage = identity.Name;
        const string LowerPackage = "unrelated.supplier";
        byte[] image = File.ReadAllBytes(CallGraphTargetPath);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntriesByPackageId:
                        new Dictionary<
                            string,
                            IReadOnlyList<(
                                string EntryPath,
                                byte[] Content)>>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            [exactPackage] =
                            [
                                (
                                    $"lib/net11.0/{identity.Name}.dll",
                                    image),
                            ],
                            [LowerPackage] =
                            [
                                (
                                    $"lib/net11.0/{identity.Name}.dll",
                                    image),
                            ],
                        },
                    IncludeManifest: true));
        (PackageAssemblyReferenceSupplierAssociation association,
            ImmutableArray<PackageDependencyEdgeRealizationExecution>
                executions) =
            await SupplierAssociationAsync(
                environment,
                (exactPackage, RouteVersion),
                (LowerPackage, RouteVersion));
        var store = new InMemoryPackageStore();
        PackageHouse house = environment.CreateContentHouse(
            (_, _) => store);
        using PackageSourceOperationLease operation =
            environment.IssueOperation(
                executions[0].Request,
                TestContext.Current.CancellationToken);

        var selected = Assert.IsType<
            PackageAssemblyReferenceSupplierOutcome.Selected>(
                await association.ResolveAsync(
                    BindingRequest(identity),
                    AssemblyBindingSelection.NameNotOwned(),
                    house,
                    operation));

        Assert.Equal(
            PackageAssemblyReferenceSupplierTier.ExactPackageId,
            selected.Selection.Evidence.Tier);
        Assert.Equal(
            exactPackage,
            selected.Selection.Evidence.Route.Candidate.Coordinate
                .PackageId,
            ignoreCase: true);
        Assert.Equal(identity, selected.Selection.Assembly.Identity);
        Assert.Equal(
            [exactPackage.ToLowerInvariant()],
            environment.Clients[0].PayloadPackageIds
                .Select(static package => package.ToLowerInvariant())
                .Distinct());
        operation.Dispose();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        AssemblyReferenceSupplierUsesCompleteInventoryForNoAffinityPackage()
    {
        AssemblyReferenceIdentity identity = CallGraphTargetIdentity();
        const string EmptyPackage = "unrelated.empty";
        const string SupplierPackage = "xunit.extensibility.core";
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntriesByPackageId:
                        new Dictionary<
                            string,
                            IReadOnlyList<(
                                string EntryPath,
                                byte[] Content)>>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            [EmptyPackage] =
                            [
                                (
                                    "lib/net11.0/Unrelated.Empty.dll",
                                    File.ReadAllBytes(
                                        CallGraphCallerPath)),
                            ],
                            [SupplierPackage] =
                            [
                                (
                                    $"lib/net11.0/{identity.Name}.dll",
                                    File.ReadAllBytes(
                                        CallGraphTargetPath)),
                            ],
                        },
                    IncludeManifest: true));
        (PackageAssemblyReferenceSupplierAssociation association,
            ImmutableArray<PackageDependencyEdgeRealizationExecution>
                executions) =
            await SupplierAssociationAsync(
                environment,
                (EmptyPackage, RouteVersion),
                (SupplierPackage, RouteVersion));
        var store = new InMemoryPackageStore();
        PackageHouse house = environment.CreateContentHouse(
            (_, _) => store);
        using PackageSourceOperationLease operation =
            environment.IssueOperation(
                executions[0].Request,
                TestContext.Current.CancellationToken);

        var selected = Assert.IsType<
            PackageAssemblyReferenceSupplierOutcome.Selected>(
                await association.ResolveAsync(
                    BindingRequest(identity),
                    AssemblyBindingSelection.NameNotOwned(),
                    house,
                    operation));

        Assert.Equal(
            PackageAssemblyReferenceSupplierTier.SelectedFileName,
            selected.Selection.Evidence.Tier);
        Assert.Equal(
            SupplierPackage,
            selected.Selection.Evidence.Route.Candidate.Coordinate
                .PackageId,
            ignoreCase: true);
        Assert.Equal(
            [EmptyPackage, SupplierPackage],
            environment.Clients[0].PayloadPackageIds.Select(
                static package => package.ToLowerInvariant()));
        operation.Dispose();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        AssemblyReferenceSupplierSelectsPackageFamilyPrefixBeforeNoAffinity()
    {
        AssemblyReferenceIdentity identity = CallGraphTargetIdentity();
        const string PrefixPackage = "ILInspector.Analysis";
        const string NoAffinityPackage = "unrelated.supplier";
        byte[] image = File.ReadAllBytes(CallGraphTargetPath);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntriesByPackageId:
                        new Dictionary<
                            string,
                            IReadOnlyList<(
                                string EntryPath,
                                byte[] Content)>>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            [PrefixPackage] =
                            [
                                (
                                    $"lib/net11.0/{identity.Name}.dll",
                                    image),
                            ],
                            [NoAffinityPackage] =
                            [
                                (
                                    $"lib/net11.0/{identity.Name}.dll",
                                    image),
                            ],
                        },
                    IncludeManifest: true));
        (PackageAssemblyReferenceSupplierAssociation association,
            ImmutableArray<PackageDependencyEdgeRealizationExecution>
                executions) =
            await SupplierAssociationAsync(
                environment,
                (PrefixPackage, RouteVersion),
                (NoAffinityPackage, RouteVersion));
        var store = new InMemoryPackageStore();
        using PackageSourceOperationLease operation =
            environment.IssueOperation(
                executions[0].Request,
                TestContext.Current.CancellationToken);

        var selected = Assert.IsType<
            PackageAssemblyReferenceSupplierOutcome.Selected>(
                await association.ResolveAsync(
                    BindingRequest(identity),
                    AssemblyBindingSelection.NameNotOwned(),
                    environment.CreateContentHouse((_, _) => store),
                    operation));

        Assert.Equal(
            PackageAssemblyReferenceSupplierTier.PackageFamilyPrefix,
            selected.Selection.Evidence.Tier);
        Assert.Equal(
            PrefixPackage,
            selected.Selection.Evidence.Route.Candidate.Coordinate
                .PackageId,
            ignoreCase: true);
        operation.Dispose();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        AssemblyReferenceSupplierContinuesAfterExactIdentityMiss()
    {
        AssemblyReferenceIdentity identity = CallGraphTargetIdentity();
        const string LowerPackage = "unrelated.supplier";
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntriesByPackageId:
                        new Dictionary<
                            string,
                            IReadOnlyList<(
                                string EntryPath,
                                byte[] Content)>>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            [identity.Name] =
                            [
                                (
                                    $"lib/net11.0/{identity.Name}.dll",
                                    File.ReadAllBytes(
                                        CallGraphTargetV2Path)),
                            ],
                            [LowerPackage] =
                            [
                                (
                                    $"lib/net11.0/{identity.Name}.dll",
                                    File.ReadAllBytes(
                                        CallGraphTargetPath)),
                            ],
                        },
                    IncludeManifest: true));
        (PackageAssemblyReferenceSupplierAssociation association,
            ImmutableArray<PackageDependencyEdgeRealizationExecution>
                executions) =
            await SupplierAssociationAsync(
                environment,
                (identity.Name, RouteVersion),
                (LowerPackage, RouteVersion));
        var store = new InMemoryPackageStore();
        using PackageSourceOperationLease operation =
            environment.IssueOperation(
                executions[0].Request,
                TestContext.Current.CancellationToken);

        var selected = Assert.IsType<
            PackageAssemblyReferenceSupplierOutcome.Selected>(
                await association.ResolveAsync(
                    BindingRequest(identity),
                    AssemblyBindingSelection.NameNotOwned(),
                    environment.CreateContentHouse((_, _) => store),
                    operation));

        Assert.True(selected.ObservedNameOwnedMiss);
        Assert.Equal(
            PackageAssemblyReferenceSupplierTier.SelectedFileName,
            selected.Selection.Evidence.Tier);
        Assert.Equal(
            LowerPackage,
            selected.Selection.Evidence.Route.Candidate.Coordinate
                .PackageId,
            ignoreCase: true);
        Assert.Contains(
            selected.EvaluatedCandidates,
            evidence =>
                evidence.Tier
                    == PackageAssemblyReferenceSupplierTier.ExactPackageId
                && evidence.OwnsSimpleName
                && !evidence.MatchesRequest);
        operation.Dispose();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        AssemblyReferenceSupplierReportsSameTierAmbiguity()
    {
        AssemblyReferenceIdentity identity = CallGraphTargetIdentity();
        const string FirstPackage = "first.supplier";
        const string SecondPackage = "second.supplier";
        byte[] image = File.ReadAllBytes(CallGraphTargetPath);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntriesByPackageId:
                        new Dictionary<
                            string,
                            IReadOnlyList<(
                                string EntryPath,
                                byte[] Content)>>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            [FirstPackage] =
                            [
                                (
                                    $"lib/net11.0/{identity.Name}.dll",
                                    image),
                            ],
                            [SecondPackage] =
                            [
                                (
                                    $"lib/net11.0/{identity.Name}.dll",
                                    image),
                            ],
                        },
                    IncludeManifest: true));
        (PackageAssemblyReferenceSupplierAssociation association,
            ImmutableArray<PackageDependencyEdgeRealizationExecution>
                executions) =
            await SupplierAssociationAsync(
                environment,
                (FirstPackage, RouteVersion),
                (SecondPackage, RouteVersion));
        var store = new InMemoryPackageStore();
        using PackageSourceOperationLease operation =
            environment.IssueOperation(
                executions[0].Request,
                TestContext.Current.CancellationToken);

        var ambiguous = Assert.IsType<
            PackageAssemblyReferenceSupplierOutcome.Ambiguous>(
                await association.ResolveAsync(
                    BindingRequest(identity),
                    AssemblyBindingSelection.NameNotOwned(),
                    environment.CreateContentHouse((_, _) => store),
                    operation));

        Assert.Equal(
            PackageAssemblyReferenceSupplierTier.SelectedFileName,
            ambiguous.Tier);
        Assert.Equal(2, ambiguous.Candidates.Length);
        operation.Dispose();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        AssemblyReferenceSupplierReportsAssemblyByteLimit()
    {
        AssemblyReferenceIdentity identity = CallGraphTargetIdentity();
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        (
                            $"lib/net11.0/{identity.Name}.dll",
                            File.ReadAllBytes(CallGraphTargetPath)),
                    ],
                    IncludeManifest: true));
        (PackageAssemblyReferenceSupplierAssociation association,
            ImmutableArray<PackageDependencyEdgeRealizationExecution>
                executions) =
            await SupplierAssociationAsync(
                environment,
                new PackageAssemblyReferenceSupplierLimits
                {
                    MaxAssemblyBytes = 1,
                },
                (identity.Name, RouteVersion));
        var store = new InMemoryPackageStore();
        using PackageSourceOperationLease operation =
            environment.IssueOperation(
                executions[0].Request,
                TestContext.Current.CancellationToken);

        var incomplete = Assert.IsType<
            PackageAssemblyReferenceSupplierOutcome.Incomplete>(
                await association.ResolveAsync(
                    BindingRequest(identity),
                    AssemblyBindingSelection.NameNotOwned(),
                    environment.CreateContentHouse((_, _) => store),
                    operation));

        Assert.Contains(
            "assembly-byte limit",
            incomplete.Reason,
            StringComparison.Ordinal);
        operation.Dispose();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        AssemblyReferenceSupplierReusesFileListAndDecodedMember()
    {
        AssemblyReferenceIdentity identity = CallGraphTargetIdentity();
        string packageId = identity.Name;
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        (
                            $"lib/net11.0/{identity.Name}.dll",
                            File.ReadAllBytes(CallGraphTargetPath)),
                    ],
                    IncludeManifest: true));
        (PackageAssemblyReferenceSupplierAssociation association,
            ImmutableArray<PackageDependencyEdgeRealizationExecution>
                executions) =
            await SupplierAssociationAsync(
                environment,
                (packageId, RouteVersion));
        var store = new InMemoryPackageStore();
        PackageHouse house = environment.CreateContentHouse(
            (_, _) => store);
        using PackageSourceOperationLease operation =
            environment.IssueOperation(
                executions[0].Request,
                TestContext.Current.CancellationToken);
        AssemblyBindingRequest request = BindingRequest(identity);

        var first = Assert.IsType<
            PackageAssemblyReferenceSupplierOutcome.Selected>(
                await association.ResolveAsync(
                    request,
                    AssemblyBindingSelection.NameNotOwned(),
                    house,
                    operation));
        var second = Assert.IsType<
            PackageAssemblyReferenceSupplierOutcome.Selected>(
                await association.ResolveAsync(
                    request,
                    AssemblyBindingSelection.NameNotOwned(),
                    house,
                    operation));

        Assert.Same(
            first.Selection.Assembly,
            second.Selection.Assembly);
        Assert.Equal(1, environment.Clients[0].PayloadRequests);
        operation.Dispose();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        AssemblyReferenceSupplierExcludesPrunedPackageWithoutAcquisition()
    {
        const string PackageId = "system.text.json";
        const string PackageVersion = "10.0.0";
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [PackageVersion],
                    IncludeManifest: true));
        PackageRootBinding rootBinding =
            CallGraphRootBinding((PackageId, PackageVersion));
        PackageDependencyTraversalOutcome traversal =
            await CompleteSupplierTraversalAsync(
                environment,
                rootBinding);
        var platformTarget = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0"));
        PlatformPruneInventory inventory =
            PlatformPruneInventory.FromExactFamily(
                new PlatformPruneTarget(
                    "Microsoft.NETCore.App",
                    "net11.0",
                    NuGetVersion.Parse("11.0.0")),
                [$"{PackageId}|11.0.0"]);
        PackageDependencyEdgeRealizationExecution execution =
            PackageDependencyEdgeRealizationQuery.Execute(
                new PackageDependencyEdgeRealizationRequest(
                    traversal,
                    rootOccurrenceIndex: 0,
                    edgeIndex: 0,
                    PackageHouseOperation.Create(
                        PackageHouseOperationProfile.Realize),
                    PackageHouseTargetContext.Exact(
                        "net11.0",
                        platformTarget: platformTarget),
                    inventory));
        PackageAssemblyReferenceSupplierAssociation association =
            PackageAssemblyReferenceSupplierAssociation.Create(
                new PackageAssemblyReferenceSupplierAssociationRequest(
                    traversal,
                    rootOccurrenceIndex: 0,
                    [execution]));
        using PackageSourceOperationLease operation =
            environment.IssueOperation(
                execution.Request,
                TestContext.Current.CancellationToken);

        var missing = Assert.IsType<
            PackageAssemblyReferenceSupplierOutcome.Missing>(
                await association.ResolveAsync(
                    BindingRequest(CallGraphTargetIdentity()),
                    AssemblyBindingSelection.NameNotOwned(),
                    environment.CreateContentHouse(
                        (_, _) => new InMemoryPackageStore()),
                    operation));

        Assert.Equal(
            AssemblyBindingMissDisposition.NoNameOwner,
            missing.Disposition);
        Assert.Empty(environment.Clients[0].PayloadPackageIds);
        operation.Dispose();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        ExternalAssemblyReferenceSupplierDoesNotInvokePlatformAfterPackageSelection()
    {
        AssemblyReferenceIdentity identity = CallGraphTargetIdentity();
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        (
                            $"lib/net11.0/{identity.Name}.dll",
                            File.ReadAllBytes(CallGraphTargetPath)),
                    ],
                    IncludeManifest: true));
        (PackageAssemblyReferenceSupplierAssociation association,
            ImmutableArray<PackageDependencyEdgeRealizationExecution>
                executions) =
            await SupplierAssociationAsync(
                environment,
                (identity.Name, RouteVersion));
        var store = new InMemoryPackageStore();
        PackageHouse house = environment.CreateContentHouse(
            (_, _) => store);
        using PackageSourceOperationLease operation =
            environment.IssueOperation(
                executions[0].Request,
                TestContext.Current.CancellationToken);
        int platformCalls = 0;

        var packageOwned = Assert.IsType<
            ExternalAssemblyReferenceSupplierOutcome.PackageOwned>(
                await ExternalAssemblyReferenceSupplierAssociation
                    .ExecuteAsync(
                        association,
                        BindingRequest(identity),
                        AssemblyBindingSelection.NameNotOwned(),
                        house,
                        operation,
                        (_, _) =>
                        {
                            platformCalls++;
                            throw new InvalidOperationException(
                                "Platform must not run after Package selection.");
                        },
                        TestContext.Current.CancellationToken));

        Assert.Equal(identity, packageOwned.Package.Selection.Assembly.Identity);
        Assert.Equal(0, platformCalls);
        operation.Dispose();
        await environment.AssertRootSettledAsync();
    }

    private static async Task<(
        PackageAssemblyReferenceSupplierAssociation Association,
        ImmutableArray<PackageDependencyEdgeRealizationExecution>
            Executions)> SupplierAssociationAsync(
        HouseEnvironment environment,
        params (string PackageId, string Version)[] dependencies)
        => await SupplierAssociationAsync(
            environment,
            limits: null,
            dependencies);

    private static async Task<(
        PackageAssemblyReferenceSupplierAssociation Association,
        ImmutableArray<PackageDependencyEdgeRealizationExecution>
            Executions)> SupplierAssociationAsync(
        HouseEnvironment environment,
        PackageAssemblyReferenceSupplierLimits? limits,
        params (string PackageId, string Version)[] dependencies)
    {
        PackageRootBinding rootBinding =
            CallGraphRootBinding(dependencies);
        PackageDependencyTraversalOutcome traversal =
            await CompleteSupplierTraversalAsync(
                environment,
                rootBinding);
        var executions =
            ImmutableArray.CreateBuilder<
                PackageDependencyEdgeRealizationExecution>();
        for (int edgeIndex = 0;
            edgeIndex < traversal.Edges.Length;
            edgeIndex++)
        {
            if (traversal.RootReachability[0].IsEdgeAdmitted(
                    edgeIndex,
                    out _)
                && traversal.Edges[edgeIndex].Authority
                    == PackageDependencyTraversalEdgeEmissionAuthority
                        .ResolvedCandidate)
            {
                executions.Add(
                    PackageDependencyEdgeRealizationQuery.Execute(
                        new PackageDependencyEdgeRealizationRequest(
                            traversal,
                            rootOccurrenceIndex: 0,
                            edgeIndex,
                            PackageHouseOperation.Create(
                                PackageHouseOperationProfile.Realize),
                            PackageHouseTargetContext.Exact(
                                traversal.TraversalTargetPolicy
                                    .TargetFramework))));
            }
        }

        ImmutableArray<PackageDependencyEdgeRealizationExecution>
            snapshot = executions.ToImmutable();
        return (
            PackageAssemblyReferenceSupplierAssociation.Create(
                new PackageAssemblyReferenceSupplierAssociationRequest(
                    traversal,
                    rootOccurrenceIndex: 0,
                    snapshot,
                    limits)),
            snapshot);
    }

    private static async Task<PackageDependencyTraversalOutcome>
        CompleteSupplierTraversalAsync(
            HouseEnvironment environment,
            PackageRootBinding rootBinding)
    {
        var candidateSource =
            new AuthorizedPackageDependencyCandidateSource(
                environment.Authorization,
                environment.Root);
        PackageDependencyTraversalOutcome traversal =
            await PackageDependencyTraversalQuery.ExecuteAsync(
                new PackageDependencyTraversalRequest(
                    [
                        new PackageDependencyTraversalRootOccurrence(
                            await RouteRootContextAsync(rootBinding),
                            PackageDependencyTraversalExpansionAuthority
                                .RecursiveSources),
                    ],
                    TraversalTargetFrameworkPolicy.ProductDefault,
                    new PackageDependencyTraversalCandidateAdapter(
                        candidateSource),
                    new AuthorizedPackageDependencyManifestSource(
                        candidateSource),
                    new PackageDependencyTraversalWorkBudget(
                        maxManifestProjections: 8,
                        maxDeclarationResolutions: 8),
                    maxDepth: 2),
                TestContext.Current.CancellationToken);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.Complete,
            traversal.Roots[0].Completion);
        foreach (HouseSourceClient client in environment.Clients)
            client.ResetPayloadTracking();
        return traversal;
    }

    private static AssemblyReferenceIdentity CallGraphTargetIdentity()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(CallGraphTargetPath);
        return session.AssemblyIdentity();
    }

    private static string CallGraphTargetV2Path =>
        FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath();

    private static AssemblyBindingRequest BindingRequest(
        AssemblyReferenceIdentity identity) =>
        new(
            AssemblyBindingTarget.Reference(identity),
            AssemblyBindingOrigin.FromAssembly(
                ResolvedAssemblyReference.CreateFromPath(
                    CallGraphCallerPath,
                    AssemblyResolutionProvenance.Local(
                        "Package supplier association test"))),
            AssemblyResolutionScope.Any);
}
