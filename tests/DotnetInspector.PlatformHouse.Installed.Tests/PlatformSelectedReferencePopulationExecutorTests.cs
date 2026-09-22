using System.Reflection.Metadata;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using ILInspector.Metadata;
using Inspector.Artifacts;

namespace DotnetInspector.PlatformHouse.Installed.Tests;

public sealed class PlatformSelectedReferencePopulationExecutorTests
{
    [Fact]
    public async Task InstalledPopulationCompletesAndSuppressesPackageWork()
    {
        Harness context = await CreateContextAsync(
            referenceCapabilities: null);
        int packageDiscoveries = 0;
        int packageRealizations = 0;

        PlatformPopulationArtifactMaterializationOutcome outcome =
            await PlatformHouseSelectedReferencePopulationExecutor
                .ExecuteAsync(
                    context.Request,
                    [
                        Discovery(
                            context,
                            context.InstalledDiscovery,
                            context.InstalledTarget,
                            new TestAssociation("installed")),
                        Discovery(
                            context,
                            context.PackageDiscovery,
                            context.PackageTarget,
                            new TestAssociation("package"),
                            () => packageDiscoveries++),
                    ],
                    [
                        Success(
                            context,
                            context.InstalledReference),
                        Success(
                            context,
                            context.PackageReference,
                            () => packageRealizations++),
                    ],
                    "selected-reference-population-test");

        if (outcome
            is PlatformPopulationArtifactMaterializationOutcome.Terminal
                terminal)
        {
            Assert.Fail(TerminalMessage(terminal));
        }
        var completed = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Completed>(
                outcome);
        Assert.Equal(0, packageDiscoveries);
        Assert.Equal(0, packageRealizations);
        Assert.Equal(
            context.Contents.Count,
            completed.Population.Owners.Count);
        Assert.Equal(
            context.Contents.Select(static content => content.Identity),
            completed.Population.Value.Libraries.Select(
                static library =>
                    library.ApiAssembly.AssemblyIdentity!.Identity),
            AssemblyReferenceIdentity.EquivalentComparer);
        Assert.All(
            completed.Population.Value.Members,
            member => Assert.Equal(
                context.InstalledTarget,
                member.Target));

        PlatformHouseReceipt receipt =
            completed.Population.Outcome.Receipt;
        Assert.Same(context.Request.Snapshot, receipt.Request);
        var targetSettlement =
            Assert.IsType<PlatformTargetSettlement.Selected>(
                receipt.TargetSettlement);
        Assert.Same(context.Request.Target, targetSettlement.Demand);
        Assert.Equal(
            context.InstalledTarget,
            targetSettlement.SettledTarget);
        Assert.Collection(
            receipt.SourceSettlements,
            discovery =>
            {
                Assert.Same(
                    context.InstalledDiscovery,
                    discovery.Contribution.Capability);
                Assert.Equal(
                    PlatformSourceSettlementDisposition.Selected,
                    discovery.Disposition);
            },
            realization =>
            {
                Assert.Same(
                    context.InstalledReference,
                    realization.Contribution.Capability);
                Assert.Equal(
                    PlatformSourceSettlementDisposition.Selected,
                    realization.Disposition);
                Assert.Equal(
                    context.InstalledReference.Name + "-generation",
                    realization.Contribution.Generation.Name);
            });
        Assert.Equal(2, receipt.ConsumedWork.SourceOperations);
        Assert.Equal(1, receipt.ConsumedWork.TargetCandidates);
        Assert.Equal(context.Contents.Count, receipt.ConsumedWork.Assemblies);
        Assert.Equal(
            context.Contents.Sum(static content => content.Bytes.LongLength),
            receipt.ConsumedWork.Bytes);

        MetadataTypeDefinitionName executorName = Name(
            "DotnetInspector.PlatformHouse",
            "PlatformHouseSelectedReferencePopulationExecutor");
        var catalogBounds = new PlatformTypeCatalogDerivationBounds(
            new LibraryTypeDeclarationInventoryInspectionBounds(
                maximumAssemblyBytes: 16 * 1024 * 1024,
                maximumRetainedDeclarations: 100_000),
            maximumAssemblies: 8,
            maximumAggregateAssemblyBytes: 64 * 1024 * 1024,
            maximumRetainedEntries: 200_000,
            maximumDuration: TimeSpan.FromSeconds(30));
        PlatformTypeCatalog catalog = Assert.IsType<
                PlatformTypeCatalogDerivationOutcome.Completed>(
                PlatformTypeCatalogDerivation.Execute(
                    completed.Population,
                    catalogBounds,
                    TestContext.Current.CancellationToken))
            .Catalog;
        Assert.Same(
            completed.Population.Value,
            catalog.Population);
        Assert.Same(
            completed.Population.Receipt,
            catalog.PopulationReceipt);
        Assert.Same(context.Request.Snapshot, catalog.PopulationReceipt
            .HouseReceipt.Request);
        Assert.Equal(context.InstalledTarget, catalog.Target);
        Assert.Equal(PlatformViewDemand.Reference, catalog.View);
        Assert.True(
            catalog.Work.Elapsed < catalogBounds.MaximumDuration);
        Assert.Equal(context.Contents.Count, catalog.Work.ObservedAssemblies);
        Assert.Equal(
            context.Contents.Sum(static content => content.Bytes.LongLength),
            catalog.Work.ObservedAssemblyBytes);
        PlatformTypeCatalogEntry executor = Assert.Single(
            Assert.IsType<PlatformTypeCatalogLookupOutcome.Found>(
                    catalog.Lookup(executorName))
                .Candidates);
        Assert.Same(
            completed.Population.Value.Members[1],
            executor.Member);
        Assert.Equal(
            AssemblyTypeDeclarationKind.Definition,
            executor.Kind);

        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        Assert.False(artifactRetirement.IsCompleted);
        for (int index = 0;
            index < completed.Population.Owners.Count;
            index++)
        {
            await completed.Population.Owners[index].DisposeAsync();
            if (index != completed.Population.Owners.Count - 1)
                Assert.False(artifactRetirement.IsCompleted);
        }
        await artifactRetirement.WaitAsync(
            TestContext.Current.CancellationToken);
        Assert.Same(
            executor,
            Assert.Single(
                Assert.IsType<
                        PlatformTypeCatalogLookupOutcome.Found>(
                        catalog.Lookup(executorName))
                    .Candidates));
    }

    [Fact]
    public async Task PackageFallbackReceivesExactSelectedAssociation()
    {
        Harness context = await CreateContextAsync(
            referenceCapabilities: null);
        context = context with
        {
            Request = CreateRequest(
                context,
                [context.PackageReference]),
        };
        var association = new TestAssociation("package-selection");
        PlatformTargetDiscoveryCandidate? observedAssociation = null;

        PlatformPopulationArtifactMaterializationOutcome outcome =
            await PlatformHouseSelectedReferencePopulationExecutor
                .ExecuteAsync(
                    context.Request,
                    [
                        TerminalDiscovery(
                            context,
                            context.InstalledDiscovery),
                        Discovery(
                            context,
                            context.PackageDiscovery,
                            context.PackageTarget,
                            association),
                    ],
                    [
                        Success(
                            context,
                            context.PackageReference,
                            observedAssociation:
                                candidate =>
                                    observedAssociation = candidate,
                            associationCapability:
                                context.PackageDiscovery),
                    ],
                    "selected-reference-population-test");

        if (outcome
            is PlatformPopulationArtifactMaterializationOutcome.Terminal
                terminal)
        {
            Assert.Fail(TerminalMessage(terminal));
        }
        var completed = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Completed>(
                outcome);
        var paired = Assert.IsType<
            PlatformTargetDiscoveryCandidate<TestAssociation>>(
                observedAssociation);
        Assert.Same(association, paired.Association);
        Assert.Equal(context.PackageTarget, paired.Target);
        Assert.Equal(
            context.PackageTarget,
            completed.Population.Outcome.Receipt.TargetSettlement
                .SettledTarget);
        Assert.Collection(
            completed.Population.Outcome.Receipt.SourceSettlements,
            preferred => Assert.Equal(
                PlatformSourceSettlementDisposition.OutcomeRelevant,
                preferred.Disposition),
            fallback => Assert.Equal(
                PlatformSourceSettlementDisposition.Selected,
                fallback.Disposition),
            realization => Assert.Equal(
                PlatformSourceSettlementDisposition.Selected,
                realization.Disposition));

        await RetireAsync(completed);
    }

    [Fact]
    public async Task ExhaustedPopulationWorkCannotPublishShortenedResult()
    {
        Harness context = await CreateContextAsync(
            referenceCapabilities: null,
            maxAssemblies: 1);
        int packageRealizations = 0;
        int openedAssemblies = 0;

        PlatformPopulationArtifactMaterializationOutcome outcome =
            await PlatformHouseSelectedReferencePopulationExecutor
                .ExecuteAsync(
                    context.Request,
                    [
                        Discovery(
                            context,
                            context.InstalledDiscovery,
                            context.InstalledTarget,
                            new TestAssociation("installed")),
                        Discovery(
                            context,
                            context.PackageDiscovery,
                            context.PackageTarget,
                            new TestAssociation("package")),
                    ],
                    [
                        Success(
                            context,
                            context.InstalledReference,
                            opened: () => openedAssemblies++),
                        Success(
                            context,
                            context.PackageReference,
                            () => packageRealizations++),
                    ],
                    "selected-reference-population-test");

        var terminal = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Terminal>(
                outcome);
        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Incomplete>(
                    terminal.TerminalRealization.Outcome);
        Assert.Equal(0, packageRealizations);
        Assert.Equal(0, openedAssemblies);
        Assert.Equal(
            context.Contents.Count,
            terminal.TerminalRealization.Outcome.Receipt
                .ConsumedWork.Assemblies);
        Assert.Contains(
            terminal.TerminalRealization.Outcome.Receipt.SourceSettlements,
            settlement =>
                ReferenceEquals(
                    settlement.Contribution.Capability,
                    context.InstalledReference)
                && settlement.Disposition
                    == PlatformSourceSettlementDisposition.OutcomeRelevant);
    }

    static async ValueTask<Harness> CreateContextAsync(
        IReadOnlyList<PlatformSourceCapabilityIdentity>?
            referenceCapabilities,
        int maxAssemblies = 8)
    {
        CancellationToken cancellation =
            TestContext.Current.CancellationToken;
        AssemblyContent[] contents =
        [
            await AssemblyContent.CreateAsync(
                typeof(PlatformSelectedReferencePopulationExecutorTests)
                    .Assembly.Location,
                cancellation),
            await AssemblyContent.CreateAsync(
                typeof(PlatformHouseSelectedReferencePopulationExecutor)
                    .Assembly.Location,
                cancellation),
        ];
        PlatformSourceCapabilityIdentity installedDiscovery =
            PlatformSourceCapabilityIdentity.Create(
                "installed-target-discovery");
        PlatformSourceCapabilityIdentity packageDiscovery =
            PlatformSourceCapabilityIdentity.Create(
                "package-target-discovery");
        PlatformSourceCapabilityIdentity installedReference =
            PlatformSourceCapabilityIdentity.Create(
                "installed-reference");
        PlatformSourceCapabilityIdentity packageReference =
            PlatformSourceCapabilityIdentity.Create(
                "package-reference");
        PlatformSourceAssociationRouteIdentity packageRoute =
            PlatformSourceAssociationRouteIdentity.Create(
                "package-association-route");
        PlatformFamilyTarget installedTarget =
            Target("net11.0", "11.0.0-rc.1");
        PlatformFamilyTarget packageTarget =
            Target("net10.0", "10.0.12");
        var seed = new Harness(
            Request: null!,
            contents,
            installedTarget,
            packageTarget,
            installedDiscovery,
            packageDiscovery,
            installedReference,
            packageReference,
            packageRoute,
            maxAssemblies);
        return seed with
        {
            Request = CreateRequest(
                seed,
                referenceCapabilities
                    ?? [installedReference, packageReference]),
        };
    }

    static PlatformHouseRequest CreateRequest(
        Harness context,
        IReadOnlyList<PlatformSourceCapabilityIdentity>
            referenceCapabilities)
    {
        var targetDemand = new PlatformTargetDemand.FamilyDefault(
            PlatformFamily.DotNetRuntime,
            new PlatformVersionlessRuntimeTargetPolicy(
                PlatformTargetSelectionPolicyIdentity.Create(
                    "versionless-runtime-default"),
                PlatformTargetSelectionPolicyGeneration.Create(
                    "generation-1"),
                PlatformVersion.Parse("10.0.1"),
                new PlatformTargetDiscoveryStage(
                    new PlatformTargetDiscoveryScope.AllFrameworks(),
                    [context.InstalledDiscovery]),
                new PlatformTargetDiscoveryStage(
                    new PlatformTargetDiscoveryScope.ExactFramework(
                        PlatformTargetFramework.Parse("net10.0")),
                    [context.PackageDiscovery])),
            new PlatformTargetDiscoveryBudget(32, 128));
        return new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create(
                "selected-reference-population-request"),
            targetDemand,
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create("standalone")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Reference),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "selected-reference-population-sources"),
                PlatformSourcePolicyGeneration.Create("generation-1"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.TargetDiscovery,
                        PlatformSourceSelectionMode.Fallback,
                        [
                            context.InstalledDiscovery,
                            context.PackageDiscovery,
                        ]),
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Reference,
                        PlatformSourceSelectionMode.Fallback,
                        referenceCapabilities),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations: 16,
                maxTargetCandidates: 32,
                maxAssemblies: context.MaxAssemblies,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: context.Contents.Sum(
                    static content => content.Bytes.LongLength) * 4,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)),
            TestContext.Current.CancellationToken);
    }

    static PlatformTargetDiscoverySource Discovery(
        Harness context,
        PlatformSourceCapabilityIdentity capability,
        PlatformFamilyTarget target,
        TestAssociation association,
        Action? invoked = null) =>
        new(
            capability,
            (request, _) =>
            {
                invoked?.Invoke();
                var contribution =
                    new PlatformSourceContribution.TargetDiscovery(
                        capability,
                        request.Snapshot,
                        Generation(capability),
                        [target]);
                PlatformTargetDiscoveryAttempt attempt =
                    new PlatformTargetDiscoveryAttempt.Succeeded(
                        contribution,
                        [
                            new PlatformTargetDiscoveryCandidate<
                                TestAssociation>(
                                    target,
                                    association),
                        ]);
                return ValueTask.FromResult(attempt);
            },
            ReferenceEquals(
                capability,
                context.PackageDiscovery)
                ? context.PackageRoute
                : null);

    static PlatformTargetDiscoverySource TerminalDiscovery(
        Harness context,
        PlatformSourceCapabilityIdentity capability) =>
        new(
            capability,
            (request, _) =>
            {
                PlatformTargetDiscoveryAttempt attempt =
                    new PlatformTargetDiscoveryAttempt.NotSucceeded(
                        new PlatformSourceContribution.Unavailable(
                            PlatformSourceFacet.TargetDiscovery,
                            capability,
                            request.Snapshot,
                            Generation(capability),
                            exactTarget: null,
                            PlatformSourceUnavailabilityKind.Absent));
                return ValueTask.FromResult(attempt);
            });

    static PlatformReferencePopulationRealizationSource Success(
        Harness context,
        PlatformSourceCapabilityIdentity capability,
        Action? invoked = null,
        Action<PlatformTargetDiscoveryCandidate?>?
            observedAssociation = null,
        PlatformSourceCapabilityIdentity?
            associationCapability = null,
        Action? opened = null) =>
        new(
            capability,
            (request, target, _, association) =>
            {
                invoked?.Invoke();
                observedAssociation?.Invoke(association);
                var contribution =
                    new PlatformSourceContribution.Realization(
                        PlatformSourceFacet.Reference,
                        capability,
                        request.Snapshot,
                        Generation(capability),
                        target,
                        PlatformSourceCoordinateIdentity.Create(
                            capability.Name + "-coordinate"),
                        ((PlatformHouseOperation.Realize)
                            request.Operation).Population,
                        PlatformSourceContributionCompleteness
                            .Authoritative);
                PlatformPopulationLibraryArtifactMaterializationItem[] items =
                [
                    .. context.Contents.Select(
                        content =>
                            new PlatformPopulationLibraryArtifactMaterializationItem(
                                new PlatformLibraryArtifactMaterializationItem(
                                    contribution,
                                    new Provenance(
                                        capability.Name,
                                        content.Identity.Name),
                                    content.Identity,
                                    content.Bytes.LongLength,
                                    _ =>
                                    {
                                        opened?.Invoke();
                                        return new MemoryStream(
                                            content.Bytes,
                                            writable: false);
                                    }),
                                new PlatformPopulationMemberAttribution(
                                    target,
                                    PlatformPopulationMemberRole.Focus))),
                ];
                PlatformReferencePopulationRealizationSourceAttempt attempt =
                    new PlatformReferencePopulationRealizationSourceAttempt
                        .Succeeded(
                            contribution,
                            PlatformHouseCandidateIdentity.Create(
                                capability.Name + "-candidate"),
                            items);
                return ValueTask.FromResult(attempt);
            },
            associationCapability,
            associationCapability is null
                ? null
                : context.PackageRoute);

    static async ValueTask RetireAsync(
        PlatformPopulationArtifactMaterializationOutcome.Completed completed)
    {
        Task retirement = completed.Artifacts.DisposeAsync().AsTask();
        foreach (var owner in completed.Population.Owners)
            await owner.DisposeAsync();
        await retirement.WaitAsync(
            TestContext.Current.CancellationToken);
    }

    static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;

    static string TerminalMessage(
        PlatformPopulationArtifactMaterializationOutcome.Terminal terminal)
    {
        PlatformHouseReceipt receipt =
            terminal.TerminalRealization.Outcome.Receipt;
        string detail = receipt.Termination
            is PlatformHouseTermination.Rejected
            {
                Rejection:
                    PlatformHouseRejection.OwnerEvidence owner,
            }
                ? $"{owner.Kind}:{owner.Evidence.Name}"
                : receipt.Termination?.GetType().Name ?? "none";
        return $"Unexpected {receipt.SettlementKind} terminal: {detail}";
    }

    static PlatformSourceGeneration Generation(
        PlatformSourceCapabilityIdentity capability) =>
        PlatformSourceGeneration.Create(
            capability.Name + "-generation");

    static PlatformFamilyTarget Target(
        string framework,
        string version) =>
        new(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse(framework),
            PlatformVersion.Parse(version));

    sealed record TestAssociation(string Name);
    sealed record Provenance(
        string Capability,
        string Assembly) : IArtifactProvenance;

    sealed record AssemblyContent(
        byte[] Bytes,
        AssemblyReferenceIdentity Identity)
    {
        internal static async ValueTask<AssemblyContent> CreateAsync(
            string path,
            CancellationToken cancellation)
        {
            byte[] bytes = await File.ReadAllBytesAsync(
                path,
                cancellation);
            using var reader =
                new System.Reflection.PortableExecutable.PEReader(
                    new MemoryStream(bytes, writable: false));
            return new(
                bytes,
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    reader.GetMetadataReader()));
        }
    }

    sealed record Harness(
        PlatformHouseRequest Request,
        IReadOnlyList<AssemblyContent> Contents,
        PlatformFamilyTarget InstalledTarget,
        PlatformFamilyTarget PackageTarget,
        PlatformSourceCapabilityIdentity InstalledDiscovery,
        PlatformSourceCapabilityIdentity PackageDiscovery,
        PlatformSourceCapabilityIdentity InstalledReference,
        PlatformSourceCapabilityIdentity PackageReference,
        PlatformSourceAssociationRouteIdentity PackageRoute,
        int MaxAssemblies);
}
