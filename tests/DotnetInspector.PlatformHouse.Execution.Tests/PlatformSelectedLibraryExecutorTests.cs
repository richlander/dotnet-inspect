using System.Reflection.Metadata;
using DotnetInspector.Platforms;
using ILInspector.Metadata;
using Inspector.Artifacts;

namespace DotnetInspector.PlatformHouse.Tests;

public sealed class PlatformSelectedLibraryExecutorTests
{
    [Fact]
    public async Task InstalledSelectionSuppressesPackageWork()
    {
        Harness context = await CreateContextAsync(
            PlatformViewDemand.Reference);
        int packageDiscoveries = 0;
        int packageRealizations = 0;

        PlatformLibraryArtifactMaterializationOutcome outcome =
            await PlatformHouseSelectedLibraryExecutor.ExecuteAsync(
                context.Request,
                [
                    Discovery(
                        context,
                        context.InstalledDiscovery,
                        context.Target,
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
                        context.InstalledReference,
                        PlatformSourceFacet.Reference),
                    Success(
                        context,
                        context.PackageReference,
                        PlatformSourceFacet.Reference,
                        () => packageRealizations++),
                ],
                "selected-library-test");

        if (outcome
            is PlatformLibraryArtifactMaterializationOutcome.Terminal
                terminal)
        {
            Assert.Fail(
                TerminalMessage(terminal));
        }
        var completed = Assert.IsType<
            PlatformLibraryArtifactMaterializationOutcome.Completed>(
                outcome);
        Assert.Equal(0, packageDiscoveries);
        Assert.Equal(0, packageRealizations);
        Assert.Equal(
            context.Target,
            completed.Library.Outcome.Receipt.TargetSettlement
                .SettledTarget);
        Assert.Equal(
            PlatformHouseSettlementKind.Completed,
            completed.Library.Outcome.Receipt.SettlementKind);
        Assert.Collection(
            completed.Library.Outcome.Receipt.SourceSettlements,
            discovery => Assert.Equal(
                PlatformSourceSettlementDisposition.Selected,
                discovery.Disposition),
            realization => Assert.Equal(
                PlatformSourceSettlementDisposition.Selected,
                realization.Disposition));

        await RetireAsync(completed);
    }

    [Fact]
    public async Task PackageSelectionRoutesAssociationOnlyToReference()
    {
        Harness context = await CreateContextAsync(
            PlatformViewDemand.ReferenceAndImplementation);
        var association = new TestAssociation("package-selection");
        PlatformTargetDiscoveryCandidate? referenceAssociation = null;
        PlatformTargetDiscoveryCandidate? implementationAssociation = null;

        PlatformLibraryArtifactMaterializationOutcome outcome =
            await PlatformHouseSelectedLibraryExecutor.ExecuteAsync(
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
                        PlatformSourceFacet.Reference,
                        observedAssociation:
                            candidate =>
                                referenceAssociation = candidate,
                        associationCapability:
                            context.PackageDiscovery),
                    Success(
                        context,
                        context.PackageImplementation,
                        PlatformSourceFacet.Implementation,
                        observedAssociation:
                            candidate =>
                                implementationAssociation = candidate),
                ],
                "selected-library-test");

        if (outcome
            is PlatformLibraryArtifactMaterializationOutcome.Terminal
                terminal)
        {
            Assert.Fail(
                TerminalMessage(terminal));
        }
        var completed = Assert.IsType<
            PlatformLibraryArtifactMaterializationOutcome.Completed>(
                outcome);
        var paired = Assert.IsType<
            PlatformTargetDiscoveryCandidate<TestAssociation>>(
                referenceAssociation);
        Assert.Same(association, paired.Association);
        Assert.Null(implementationAssociation);
        Assert.Equal(
            context.PackageTarget,
            completed.Library.Outcome.Receipt.TargetSettlement
                .SettledTarget);
        Assert.Equal(
            4,
            completed.Library.Outcome.Receipt.SourceSettlements.Count);
        Assert.Equal(
            2,
            completed.Library.Outcome.Receipt.SourceSettlements.Count(
                settlement =>
                    settlement.Disposition
                        == PlatformSourceSettlementDisposition.Selected
                    && settlement.Contribution.Kind
                        == PlatformSourceContributionKind.Realization));

        await RetireAsync(completed);
    }

    [Fact]
    public async Task SourceInternalWorkSuppressesLaterFacet()
    {
        Harness context = await CreateContextAsync(
            PlatformViewDemand.ReferenceAndImplementation,
            maxAssemblies: 2);
        int implementationInvocations = 0;
        PlatformHouseConsumedWork referenceWork = Work(
            assemblies: 2,
            bytes: context.Content.LongLength * 2);

        PlatformLibraryArtifactMaterializationOutcome outcome =
            await PlatformHouseSelectedLibraryExecutor.ExecuteAsync(
                context.Request,
                [
                    Discovery(
                        context,
                        context.PackageDiscovery,
                        context.PackageTarget,
                        new TestAssociation("package")),
                    TerminalDiscovery(
                        context,
                        context.InstalledDiscovery),
                ],
                [
                    Success(
                        context,
                        context.PackageReference,
                        PlatformSourceFacet.Reference,
                        associationCapability:
                            context.PackageDiscovery,
                        reportedWork: referenceWork),
                    Success(
                        context,
                        context.PackageImplementation,
                        PlatformSourceFacet.Implementation,
                        () => implementationInvocations++),
                ],
                "selected-library-test");

        var terminal = Assert.IsType<
            PlatformLibraryArtifactMaterializationOutcome.Terminal>(
                outcome);
        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Incomplete>(
                    terminal.TerminalRealization.Outcome);
        Assert.Equal(0, implementationInvocations);
        Assert.Equal(
            2,
            terminal.TerminalRealization.Outcome.Receipt
                .ConsumedWork.Assemblies);
    }

    [Fact]
    public async Task ExhaustedSourceAllowanceRetainsCurrentFacetEvidence()
    {
        Harness context = await CreateContextAsync(
            PlatformViewDemand.Reference,
            maxSourceOperations: 2);
        int fallbackInvocations = 0;

        PlatformLibraryArtifactMaterializationOutcome outcome =
            await PlatformHouseSelectedLibraryExecutor.ExecuteAsync(
                context.Request,
                [
                    Discovery(
                        context,
                        context.InstalledDiscovery,
                        context.Target,
                        new TestAssociation("installed")),
                    Discovery(
                        context,
                        context.PackageDiscovery,
                        context.PackageTarget,
                        new TestAssociation("package")),
                ],
                [
                    Failed(
                        context,
                        context.InstalledReference,
                        PlatformSourceFacet.Reference),
                    Success(
                        context,
                        context.PackageReference,
                        PlatformSourceFacet.Reference,
                        () => fallbackInvocations++),
                ],
                "selected-library-test");

        var terminal = Assert.IsType<
            PlatformLibraryArtifactMaterializationOutcome.Terminal>(
                outcome);
        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Incomplete>(
                    terminal.TerminalRealization.Outcome);
        Assert.Equal(0, fallbackInvocations);
        Assert.Contains(
            terminal.TerminalRealization.Outcome.Receipt.SourceSettlements,
            settlement =>
                ReferenceEquals(
                    settlement.Contribution.Capability,
                    context.InstalledReference)
                && settlement.Contribution
                    is PlatformSourceContribution.Failed
                && settlement.Disposition
                    == PlatformSourceSettlementDisposition
                        .OutcomeRelevant);
    }

    [Fact]
    public async Task AggregationFailureRemainsFailedWhenAllowancePreventsPeer()
    {
        Harness context = await CreateContextAsync(
            PlatformViewDemand.Reference,
            maxSourceOperations: 2,
            referenceMode: PlatformSourceSelectionMode.Aggregation);
        int peerInvocations = 0;

        PlatformLibraryArtifactMaterializationOutcome outcome =
            await PlatformHouseSelectedLibraryExecutor.ExecuteAsync(
                context.Request,
                [
                    Discovery(
                        context,
                        context.InstalledDiscovery,
                        context.Target,
                        new TestAssociation("installed")),
                    Discovery(
                        context,
                        context.PackageDiscovery,
                        context.PackageTarget,
                        new TestAssociation("package")),
                ],
                [
                    Failed(
                        context,
                        context.InstalledReference,
                        PlatformSourceFacet.Reference),
                    Success(
                        context,
                        context.PackageReference,
                        PlatformSourceFacet.Reference,
                        () => peerInvocations++),
                ],
                "selected-library-test");

        var terminal = Assert.IsType<
            PlatformLibraryArtifactMaterializationOutcome.Terminal>(
                outcome);
        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Failed>(
                    terminal.TerminalRealization.Outcome);
        Assert.Equal(0, peerInvocations);
        Assert.Contains(
            terminal.TerminalRealization.Outcome.Receipt.SourceSettlements,
            settlement =>
                ReferenceEquals(
                    settlement.Contribution.Capability,
                    context.InstalledReference)
                && settlement.Contribution
                    is PlatformSourceContribution.Failed
                && settlement.Disposition
                    == PlatformSourceSettlementDisposition
                        .OutcomeRelevant);
    }

    [Fact]
    public async Task AggregationFailureRemainsFailedWhenAttemptExhaustsWork()
    {
        Harness context = await CreateContextAsync(
            PlatformViewDemand.Reference,
            maxAssemblies: 1,
            referenceMode: PlatformSourceSelectionMode.Aggregation);
        int peerInvocations = 0;

        PlatformLibraryArtifactMaterializationOutcome outcome =
            await PlatformHouseSelectedLibraryExecutor.ExecuteAsync(
                context.Request,
                [
                    Discovery(
                        context,
                        context.InstalledDiscovery,
                        context.Target,
                        new TestAssociation("installed")),
                    Discovery(
                        context,
                        context.PackageDiscovery,
                        context.PackageTarget,
                        new TestAssociation("package")),
                ],
                [
                    Failed(
                        context,
                        context.InstalledReference,
                        PlatformSourceFacet.Reference,
                        Work(assemblies: 2, bytes: 0)),
                    Success(
                        context,
                        context.PackageReference,
                        PlatformSourceFacet.Reference,
                        () => peerInvocations++),
                ],
                "selected-library-test");

        var terminal = Assert.IsType<
            PlatformLibraryArtifactMaterializationOutcome.Terminal>(
                outcome);
        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Failed>(
                    terminal.TerminalRealization.Outcome);
        Assert.Equal(0, peerInvocations);
        Assert.Equal(
            2,
            terminal.TerminalRealization.Outcome.Receipt
                .ConsumedWork.Assemblies);
    }

    [Fact]
    public async Task MalformedAttemptWorkIsChargedBeforeRejection()
    {
        Harness context = await CreateContextAsync(
            PlatformViewDemand.Reference,
            maxAssemblies: 1);
        var malformedSource =
            new PlatformLibraryRealizationSource(
                context.InstalledReference,
                PlatformSourceFacet.Reference,
                (request, target, _, _) =>
                {
                    var contribution =
                        new PlatformSourceContribution.Realization(
                            PlatformSourceFacet.Reference,
                            context.PackageReference,
                            request.Snapshot,
                            Generation(context.PackageReference),
                            target,
                            PlatformSourceCoordinateIdentity.Create(
                                "malformed-source"),
                            ((PlatformHouseOperationSnapshot.Realize)
                                request.Snapshot.Operation).Population,
                            PlatformSourceContributionCompleteness
                                .Authoritative);
                    var item =
                        new PlatformLibraryArtifactMaterializationItem(
                            contribution,
                            new Provenance("malformed-source"),
                            context.Identity,
                            context.Content.LongLength,
                            _ => new MemoryStream(
                                context.Content,
                                writable: false));
                    PlatformLibraryRealizationSourceAttempt attempt =
                        new PlatformLibraryRealizationSourceAttempt
                            .Succeeded(
                                contribution,
                                PlatformHouseCandidateIdentity.Create(
                                    "malformed-candidate"),
                                item,
                                Work(
                                    assemblies: 2,
                                    bytes:
                                        context.Content.LongLength * 2));
                    return ValueTask.FromResult(attempt);
                });

        PlatformLibraryArtifactMaterializationOutcome outcome =
            await PlatformHouseSelectedLibraryExecutor.ExecuteAsync(
                context.Request,
                [
                    Discovery(
                        context,
                        context.InstalledDiscovery,
                        context.Target,
                        new TestAssociation("installed")),
                    Discovery(
                        context,
                        context.PackageDiscovery,
                        context.PackageTarget,
                        new TestAssociation("package")),
                ],
                [
                    malformedSource,
                    Success(
                        context,
                        context.PackageReference,
                        PlatformSourceFacet.Reference),
                ],
                "selected-library-test");

        var terminal = Assert.IsType<
            PlatformLibraryArtifactMaterializationOutcome.Terminal>(
                outcome);
        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Incomplete>(
                    terminal.TerminalRealization.Outcome);
        Assert.Equal(
            2,
            terminal.TerminalRealization.Outcome.Receipt
                .ConsumedWork.Assemblies);
        Assert.DoesNotContain(
            terminal.TerminalRealization.Outcome.Receipt.SourceSettlements,
            settlement => ReferenceEquals(
                settlement.Contribution.Capability,
                context.PackageReference));
    }

    [Fact]
    public async Task PublicationDurationCannotCompleteOverBudget()
    {
        Harness context = await CreateContextAsync(
            PlatformViewDemand.Reference,
            maxDuration: TimeSpan.FromMilliseconds(150));

        PlatformLibraryArtifactMaterializationOutcome outcome =
            await PlatformHouseSelectedLibraryExecutor.ExecuteAsync(
                context.Request,
                [
                    Discovery(
                        context,
                        context.InstalledDiscovery,
                        context.Target,
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
                        PlatformSourceFacet.Reference,
                        openRead: token =>
                        {
                            Thread.Sleep(250);
                            token.ThrowIfCancellationRequested();
                            return new MemoryStream(
                                context.Content,
                                writable: false);
                        }),
                    Success(
                        context,
                        context.PackageReference,
                        PlatformSourceFacet.Reference),
                ],
                "selected-library-test");

        var terminal = Assert.IsType<
            PlatformLibraryArtifactMaterializationOutcome.Terminal>(
                outcome);
        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Incomplete>(
                    terminal.TerminalRealization.Outcome);
        Assert.True(
            terminal.TerminalRealization.Outcome.Receipt
                .ConsumedWork.Elapsed
            >= context.Request.Work.MaxDuration);
    }

    static async ValueTask<Harness> CreateContextAsync(
        PlatformViewDemand view,
        int maxSourceOperations = 16,
        int maxAssemblies = 8,
        TimeSpan? maxDuration = null,
        PlatformSourceSelectionMode referenceMode =
            PlatformSourceSelectionMode.Fallback)
    {
        CancellationToken cancellation =
            TestContext.Current.CancellationToken;
        byte[] content = await File.ReadAllBytesAsync(
            typeof(PlatformSelectedLibraryExecutorTests).Assembly.Location,
            cancellation);
        using var reader =
            new System.Reflection.PortableExecutable.PEReader(
                new MemoryStream(content, writable: false));
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                reader.GetMetadataReader());

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
        PlatformSourceCapabilityIdentity packageImplementation =
            PlatformSourceCapabilityIdentity.Create(
                "package-implementation");
        PlatformSourceAssociationRouteIdentity packageRoute =
            PlatformSourceAssociationRouteIdentity.Create(
                "package-association-route");
        PlatformFamilyTarget target = Target("net11.0", "11.0.0-rc.1");
        PlatformFamilyTarget packageTarget =
            Target("net10.0", "10.0.12");
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
                    [installedDiscovery]),
                new PlatformTargetDiscoveryStage(
                    new PlatformTargetDiscoveryScope.ExactFramework(
                        PlatformTargetFramework.Parse("net10.0")),
                    [packageDiscovery])),
            new PlatformTargetDiscoveryBudget(32, 128));
        var population = new PlatformPopulationDemand.Library(
            new PlatformLibraryDemand.Assembly(identity));
        var selections = new List<PlatformSourceSelection>
        {
            new(
                PlatformSourceFacet.TargetDiscovery,
                PlatformSourceSelectionMode.Fallback,
                [installedDiscovery, packageDiscovery]),
            new(
                PlatformSourceFacet.Reference,
                referenceMode,
                view == PlatformViewDemand.Reference
                    ? [installedReference, packageReference]
                    : [packageReference]),
        };
        if (view == PlatformViewDemand.ReferenceAndImplementation)
        {
            selections.Add(
                new PlatformSourceSelection(
                    PlatformSourceFacet.Implementation,
                    PlatformSourceSelectionMode.Fallback,
                    [packageImplementation]));
        }
        var request = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create("selected-library-request"),
            targetDemand,
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create("standalone")),
            new PlatformHouseOperation.Realize(population, view),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("selected-library-sources"),
                PlatformSourcePolicyGeneration.Create("generation-1"),
                selections),
            new PlatformHouseWorkBudget(
                maxSourceOperations,
                maxTargetCandidates: 32,
                maxAssemblies,
                maxXmlDocuments: 2,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: content.LongLength * 8,
                maxForwardingHops: 0,
                maxDuration ?? TimeSpan.FromSeconds(30)),
            cancellation);
        return new(
            request,
            content,
            identity,
            target,
            packageTarget,
            installedDiscovery,
            packageDiscovery,
            installedReference,
            packageReference,
            packageImplementation,
            packageRoute);
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

    static PlatformLibraryRealizationSource Success(
        Harness context,
        PlatformSourceCapabilityIdentity capability,
        PlatformSourceFacet facet,
        Action? invoked = null,
        Action<PlatformTargetDiscoveryCandidate?>?
            observedAssociation = null,
        PlatformSourceCapabilityIdentity?
            associationCapability = null,
        PlatformHouseConsumedWork? reportedWork = null,
        Func<CancellationToken, Stream>? openRead = null) =>
        new(
            capability,
            facet,
            (request, target, remainingWork, association) =>
            {
                _ = remainingWork;
                invoked?.Invoke();
                observedAssociation?.Invoke(association);
                var contribution =
                    new PlatformSourceContribution.Realization(
                        facet,
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
                var item =
                    new PlatformLibraryArtifactMaterializationItem(
                        contribution,
                        new Provenance(capability.Name),
                        context.Identity,
                        context.Content.LongLength,
                        openRead
                            ?? (_ => new MemoryStream(
                                context.Content,
                                writable: false)));
                PlatformLibraryRealizationSourceAttempt attempt =
                    new PlatformLibraryRealizationSourceAttempt.Succeeded(
                        contribution,
                        PlatformHouseCandidateIdentity.Create(
                            capability.Name + "-candidate"),
                        item,
                        reportedWork);
                return ValueTask.FromResult(attempt);
            },
            associationCapability,
            associationCapability is null
                ? null
                : context.PackageRoute);

    static PlatformLibraryRealizationSource Failed(
        Harness context,
        PlatformSourceCapabilityIdentity capability,
        PlatformSourceFacet facet,
        PlatformHouseConsumedWork? reportedWork = null) =>
        new(
            capability,
            facet,
            (request, target, _, _) =>
            {
                PlatformLibraryRealizationSourceAttempt attempt =
                    new PlatformLibraryRealizationSourceAttempt
                        .NotSucceeded(
                            new PlatformSourceContribution.Failed(
                                facet,
                                capability,
                                request.Snapshot,
                                Generation(capability),
                                target),
                            consumedWork: reportedWork);
                return ValueTask.FromResult(attempt);
            });

    static async ValueTask RetireAsync(
        PlatformLibraryArtifactMaterializationOutcome.Completed completed)
    {
        Task retirement = completed.Artifacts.DisposeAsync().AsTask();
        await completed.Library.Owner.DisposeAsync();
        await retirement.WaitAsync(
            TestContext.Current.CancellationToken);
    }

    static string TerminalMessage(
        PlatformLibraryArtifactMaterializationOutcome.Terminal terminal)
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

    static PlatformHouseConsumedWork Work(
        int assemblies,
        long bytes) =>
        new(
            sourceOperations: 0,
            targetCandidates: 0,
            assemblies,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);

    static PlatformFamilyTarget Target(
        string framework,
        string version) =>
        new(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse(framework),
            PlatformVersion.Parse(version));

    sealed record TestAssociation(string Name);
    sealed record Provenance(string Name) : IArtifactProvenance;

    sealed record Harness(
        PlatformHouseRequest Request,
        byte[] Content,
        AssemblyReferenceIdentity Identity,
        PlatformFamilyTarget Target,
        PlatformFamilyTarget PackageTarget,
        PlatformSourceCapabilityIdentity InstalledDiscovery,
        PlatformSourceCapabilityIdentity PackageDiscovery,
        PlatformSourceCapabilityIdentity InstalledReference,
        PlatformSourceCapabilityIdentity PackageReference,
        PlatformSourceCapabilityIdentity PackageImplementation,
        PlatformSourceAssociationRouteIdentity PackageRoute);
}
