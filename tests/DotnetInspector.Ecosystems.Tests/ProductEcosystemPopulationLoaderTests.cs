using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.EcosystemLoading;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.Ecosystems.Tests;

public sealed class ProductEcosystemPopulationLoaderTests
{
    [Fact]
    public void InputsRequireExactCapabilityPlanIdentity()
    {
        EcosystemPopulationCapabilityPlanIdentity capabilityPlan =
            EcosystemPopulationCapabilityPlanIdentity.Create(
                "runtime-capability");
        var capability = new TestPlatformPopulationCapability(
            capabilityPlan);

        Assert.Throws<ArgumentException>(
            () => new RuntimeEcosystemPopulationLoadInputs(
                EcosystemPopulationOperationPolicyIdentity.Create(
                    "runtime-policy"),
                EcosystemPopulationCapabilityPlanIdentity.Create(
                    "foreign-capability"),
                EcosystemPopulationWorkIdentity.Create("runtime-work"),
                capability));
    }

    [Fact]
    public async Task ExactLibraryDemandDoesNotInvokePlatformCapability()
    {
        var coordinate = new ExactLibrarySourceCoordinate.Platform(
            ProductEcosystemPacks.RuntimePlatformPopulation,
            new ManagedMetadataIdentity.Assembly(
                new AssemblyReferenceIdentity(
                    "System.Text.Json",
                    new Version(11, 0, 0, 0),
                    null,
                    null)));
        await using ProductLoaderFixture fixture =
            ProductLoaderFixture.Create(
                EcosystemPackIds.Runtime,
                new EcosystemPopulationDemand.ExactLibrary(coordinate));
        var known = Assert.IsType<
            EcosystemPopulationLoaderSelection.Known<
                RuntimeEcosystemPopulationLoadInputs>>(
                fixture.Selection);
        EcosystemPopulationCapabilityPlanIdentity capabilityPlan =
            EcosystemPopulationCapabilityPlanIdentity.Create(
                "runtime-exact-capabilities");
        var capability = new TestPlatformPopulationCapability(
            capabilityPlan);
        var inputs = new RuntimeEcosystemPopulationLoadInputs(
            EcosystemPopulationOperationPolicyIdentity.Create(
                "runtime-exact-policy"),
            capabilityPlan,
            EcosystemPopulationWorkIdentity.Create("runtime-exact-work"),
            capability);

        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Unavailable>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    known.CreateRequest(
                        inputs,
                        TestContext.Current.CancellationToken)));

        Assert.Equal(0, capability.InvocationCount);
        Assert.Empty(outcome.Receipt.Children);
        Assert.Equal(
            "ecosystem-loader.platform-demand-unavailable",
            Assert.Single(outcome.Receipt.Diagnostics).Code);
    }

    [Fact]
    public async Task RuntimeLoaderPreservesExactPlatformPopulation()
    {
        await using ProductLoaderFixture fixture =
            ProductLoaderFixture.Create(EcosystemPackIds.Runtime);
        var known = Assert.IsType<
            EcosystemPopulationLoaderSelection.Known<
                RuntimeEcosystemPopulationLoadInputs>>(
                fixture.Selection);
        EcosystemPopulationCapabilityPlanIdentity capabilityPlan =
            EcosystemPopulationCapabilityPlanIdentity.Create(
                "runtime-test-capabilities");
        var capability = new TestPlatformPopulationCapability(
            capabilityPlan);
        var inputs = new RuntimeEcosystemPopulationLoadInputs(
            EcosystemPopulationOperationPolicyIdentity.Create(
                "runtime-test-policy"),
            capabilityPlan,
            EcosystemPopulationWorkIdentity.Create("runtime-test-work"),
            capability);

        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    known.CreateRequest(
                        inputs,
                        TestContext.Current.CancellationToken)));

        Assert.Same(
            ProductEcosystemPacks.RuntimePlatformPopulation,
            inputs.PlatformDeclaration);
        Assert.Same(
            inputs.PlatformDeclaration,
            Assert.IsType<
                WorkspaceEcosystemPopulationDeclaration.Platform>(
                    fixture.Registration.Populations[0]).Population);
        Assert.Same(inputs.PlatformDeclaration, capability.Declaration);
        Assert.Same(
            capability.PlanIdentity,
            outcome.Receipt.Request.Inputs.CapabilityPlan);
        Assert.Equal(PlatformFamily.DotNetRuntime, capability.Family);
        Assert.Equal(
            EcosystemPopulationLibraryRole.Focus,
            Assert.Single(outcome.Owners.Libraries).Roles);
        AssertPlatformEvidence(
            outcome.Receipt,
            PlatformFamily.DotNetRuntime);
        await outcome.Owners.DisposeAsync();
    }

    [Fact]
    public async Task AspNetCoreLoaderPreservesFocusAndRuntimeSupport()
    {
        await using ProductLoaderFixture fixture =
            ProductLoaderFixture.Create(EcosystemPackIds.AspNetCore);
        var known = Assert.IsType<
            EcosystemPopulationLoaderSelection.Known<
                AspNetCoreEcosystemPopulationLoadInputs>>(
                fixture.Selection);
        EcosystemPopulationCapabilityPlanIdentity capabilityPlan =
            EcosystemPopulationCapabilityPlanIdentity.Create(
                "aspnetcore-test-capabilities");
        var capability = new TestPlatformPopulationCapability(
            capabilityPlan);
        var inputs = new AspNetCoreEcosystemPopulationLoadInputs(
            EcosystemPopulationOperationPolicyIdentity.Create(
                "aspnetcore-test-policy"),
            capabilityPlan,
            EcosystemPopulationWorkIdentity.Create(
                "aspnetcore-test-work"),
            capability);

        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    known.CreateRequest(
                        inputs,
                        TestContext.Current.CancellationToken)));

        Assert.Same(
            ProductEcosystemPacks.AspNetCorePlatformPopulation,
            inputs.PlatformDeclaration);
        Assert.Same(
            inputs.PlatformDeclaration,
            Assert.IsType<
                WorkspaceEcosystemPopulationDeclaration.Platform>(
                    fixture.Registration.Populations[0]).Population);
        Assert.Same(inputs.PlatformDeclaration, capability.Declaration);
        Assert.Same(
            capability.PlanIdentity,
            outcome.Receipt.Request.Inputs.CapabilityPlan);
        Assert.Equal(PlatformFamily.AspNetCore, capability.Family);
        Assert.Collection(
            outcome.Owners.Libraries,
            focus => Assert.Equal(
                EcosystemPopulationLibraryRole.Focus,
                focus.Roles),
            support => Assert.Equal(
                EcosystemPopulationLibraryRole.BindingSupport,
                support.Roles));
        AssertPlatformEvidence(
            outcome.Receipt,
            PlatformFamily.AspNetCore);
        await outcome.Owners.DisposeAsync();
    }

    [Fact]
    public async Task RuntimeLoaderPreservesIncompletePlatformEvidence()
    {
        await using ProductLoaderFixture fixture =
            ProductLoaderFixture.Create(EcosystemPackIds.Runtime);
        var known = Assert.IsType<
            EcosystemPopulationLoaderSelection.Known<
                RuntimeEcosystemPopulationLoadInputs>>(
                fixture.Selection);
        EcosystemPopulationCapabilityPlanIdentity capabilityPlan =
            EcosystemPopulationCapabilityPlanIdentity.Create(
                "runtime-incomplete-capabilities");
        var capability = new TestPlatformPopulationCapability(
            capabilityPlan,
            exceedBudget: true);
        var inputs = new RuntimeEcosystemPopulationLoadInputs(
            EcosystemPopulationOperationPolicyIdentity.Create(
                "runtime-incomplete-policy"),
            capabilityPlan,
            EcosystemPopulationWorkIdentity.Create(
                "runtime-incomplete-work"),
            capability);

        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Incomplete>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    known.CreateRequest(
                        inputs,
                        TestContext.Current.CancellationToken)));

        Assert.Equal(
            EcosystemPopulationChildSettlementKind.Incomplete,
            Assert.Single(outcome.Receipt.Children).Kind);
        Assert.Equal(
            "ecosystem-loader.platform-incomplete",
            Assert.Single(outcome.Receipt.Diagnostics).Code);
        Assert.Empty(outcome.Owners.Libraries);
        AssertPlatformEvidence(
            outcome.Receipt,
            PlatformFamily.DotNetRuntime);
        await outcome.Owners.DisposeAsync();
    }

    [Fact]
    public async Task RuntimeLoaderRejectsWrongPlatformFamily()
    {
        await using ProductLoaderFixture fixture =
            ProductLoaderFixture.Create(EcosystemPackIds.Runtime);
        var known = Assert.IsType<
            EcosystemPopulationLoaderSelection.Known<
                RuntimeEcosystemPopulationLoadInputs>>(
                fixture.Selection);
        EcosystemPopulationCapabilityPlanIdentity capabilityPlan =
            EcosystemPopulationCapabilityPlanIdentity.Create(
                "runtime-wrong-family-capabilities");
        var capability = new TestPlatformPopulationCapability(
            capabilityPlan,
            returnedFamily: PlatformFamily.AspNetCore);
        var inputs = new RuntimeEcosystemPopulationLoadInputs(
            EcosystemPopulationOperationPolicyIdentity.Create(
                "runtime-wrong-family-policy"),
            capabilityPlan,
            EcosystemPopulationWorkIdentity.Create(
                "runtime-wrong-family-work"),
            capability);

        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Rejected>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    known.CreateRequest(
                        inputs,
                        TestContext.Current.CancellationToken)));

        Assert.Equal(
            EcosystemPopulationChildSettlementKind.Rejected,
            Assert.Single(outcome.Receipt.Children).Kind);
        Assert.Equal(
            "ecosystem-loader.platform-rejected",
            Assert.Single(outcome.Receipt.Diagnostics).Code);
        AssertPlatformEvidence(
            outcome.Receipt,
            PlatformFamily.AspNetCore);
        var rejection = Assert.IsType<
            PlatformHouseTermination.Rejected>(
                Assert.Single(outcome.Receipt.Children)
                    .PlatformEvidence!
                    .Receipt
                    .HouseReceipt
                    .Termination);
        Assert.Equal(
            PlatformHouseRejectionKind.InvalidTargetCorrespondence,
            Assert.IsType<PlatformHouseRejection.OwnerEvidence>(
                    rejection.Rejection)
                .Kind);
        Assert.All(
            capability.Completed!.Population.Owners,
            static owner => Assert.Equal(
                LibraryContentOwnerState.Released,
                owner.State));
        await capability.Completed.Artifacts.DisposeAsync();
    }

    [Fact]
    public async Task
        RuntimeLoaderReportsWrongFamilyArtifactRetirementFailure()
    {
        await using ProductLoaderFixture fixture =
            ProductLoaderFixture.Create(EcosystemPackIds.Runtime);
        var known = Assert.IsType<
            EcosystemPopulationLoaderSelection.Known<
                RuntimeEcosystemPopulationLoadInputs>>(
                fixture.Selection);
        EcosystemPopulationCapabilityPlanIdentity capabilityPlan =
            EcosystemPopulationCapabilityPlanIdentity.Create(
                "runtime-wrong-family-cleanup-capabilities");
        var cleanupFailure =
            new IOException("synthetic wrong-family cleanup failure");
        var cleanupLease = new FailingArtifactLease(cleanupFailure);
        var capability = new TestPlatformPopulationCapability(
            capabilityPlan,
            returnedFamily: PlatformFamily.AspNetCore,
            cleanupLease: cleanupLease);
        var inputs = new RuntimeEcosystemPopulationLoadInputs(
            EcosystemPopulationOperationPolicyIdentity.Create(
                "runtime-wrong-family-cleanup-policy"),
            capabilityPlan,
            EcosystemPopulationWorkIdentity.Create(
                "runtime-wrong-family-cleanup-work"),
            capability);

        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Failed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    known.CreateRequest(
                        inputs,
                        TestContext.Current.CancellationToken)));

        Assert.Equal(
            EcosystemPopulationChildSettlementKind.Failed,
            Assert.Single(outcome.Receipt.Children).Kind);
        Assert.Equal(
            "ecosystem-loader.platform-failed",
            Assert.Single(outcome.Receipt.Diagnostics).Code);
        AssertPlatformEvidence(
            outcome.Receipt,
            PlatformFamily.AspNetCore);
        var failure = Assert.IsType<
            PlatformHouseTermination.Failed>(
                Assert.Single(outcome.Receipt.Children)
                    .PlatformEvidence!
                    .Receipt
                    .HouseReceipt
                    .Termination);
        Assert.Equal(
            [PlatformHouseFailureKind.ArtifactRetirement],
            failure.Failures);
        Assert.All(
            capability.Completed!.Population.Owners,
            static owner => Assert.Equal(
                LibraryContentOwnerState.Released,
                owner.State));
        Assert.Same(
            cleanupFailure,
            Assert.Single(
                capability.Completed.Artifacts.CleanupFailures));
        Assert.Equal(1, cleanupLease.Disposals);
        await capability.Completed.Artifacts.DisposeAsync();
    }

    static void AssertPlatformEvidence(
        EcosystemPopulationLoadReceipt receipt,
        PlatformFamily family)
    {
        EcosystemPopulationChildSettlement child =
            Assert.Single(receipt.Children);
        EcosystemPlatformPopulationChildEvidence evidence =
            Assert.IsType<EcosystemPlatformPopulationChildEvidence>(
                child.PlatformEvidence);
        Assert.Equal(family, evidence.Request.Target.Family);
        Assert.Same(
            evidence.Request,
            evidence.Receipt.HouseReceipt.Request);
    }

    static AssemblyReferenceIdentity ReadAssemblyIdentity(byte[] content)
    {
        using var reader =
            new PEReader(new MemoryStream(content, writable: false));
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            reader.GetMetadataReader());
    }

    sealed class ProductLoaderFixture : IAsyncDisposable
    {
        ProductLoaderFixture(
            InspectionWorkspace workspace,
            WorkspaceEcosystemRegistrationDeclaration registration,
            EcosystemPopulationLoaderSelection selection)
        {
            Workspace = workspace;
            Registration = registration;
            Selection = selection;
        }

        InspectionWorkspace Workspace { get; }
        internal WorkspaceEcosystemRegistrationDeclaration Registration
        {
            get;
        }
        internal EcosystemPopulationLoaderSelection Selection { get; }

        internal static ProductLoaderFixture Create(
            EcosystemPackId id,
            EcosystemPopulationDemand? demand = null)
        {
            var workspace = new InspectionWorkspace(
                EcosystemPackCatalog.CreateWorkspacePlan([id]));
            WorkspaceRegistrationRevision revision =
                Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                    workspace.GetRegistrationSnapshot()).Revision;
            WorkspaceEcosystemRegistrationDeclaration registration =
                Assert.IsType<WorkspaceRegistration.Ecosystem>(
                    Assert.Single(revision.Registrations)).Declaration;
            EcosystemPopulationLoaderSelection selection =
                EcosystemPackCatalog.SelectPopulationLoader(
                    revision,
                    registration,
                    demand
                        ?? EcosystemPopulationDemand
                            .WholePopulation.Instance);
            return new(workspace, registration, selection);
        }

        public ValueTask DisposeAsync() => Workspace.DisposeAsync();
    }

    sealed class TestPlatformPopulationCapability :
        IEcosystemPlatformPopulationCapability
    {
        readonly bool _exceedBudget;
        readonly PlatformFamily? _returnedFamily;
        readonly IArtifactAcquisitionLease? _cleanupLease;

        internal TestPlatformPopulationCapability(
            EcosystemPopulationCapabilityPlanIdentity planIdentity,
            bool exceedBudget = false,
            PlatformFamily? returnedFamily = null,
            IArtifactAcquisitionLease? cleanupLease = null)
        {
            ArgumentNullException.ThrowIfNull(planIdentity);
            PlanIdentity = planIdentity;
            _exceedBudget = exceedBudget;
            _returnedFamily = returnedFamily;
            _cleanupLease = cleanupLease;
        }

        public EcosystemPopulationCapabilityPlanIdentity PlanIdentity
        {
            get;
        }

        internal PlatformLibraryPopulationDeclaration? Declaration
        {
            get;
            private set;
        }

        internal PlatformFamily? Family { get; private set; }
        internal int InvocationCount { get; private set; }
        internal PlatformPopulationArtifactMaterializationOutcome.Completed?
            Completed
        {
            get;
            private set;
        }

        public async ValueTask<
            PlatformPopulationArtifactMaterializationOutcome> RealizeAsync(
                PlatformLibraryPopulationDeclaration declaration,
                CancellationToken cancellationToken)
        {
            InvocationCount++;
            Declaration = declaration;
            Family = declaration.Family;

            PlatformFamilyTarget target = new(
                _returnedFamily ?? declaration.Family,
                PlatformTargetFramework.Parse("net11.0"),
                PlatformVersion.Parse("11.0.0"));
            PlatformSourceCapabilityIdentity capability =
                PlatformSourceCapabilityIdentity.Create(
                    "ecosystem-product-test");
            var operation = new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Implementation);
            var request = new PlatformHouseRequest(
                PlatformHouseRequestIdentity.Create(
                    "ecosystem-product-test"),
                new PlatformTargetDemand.Exact(target),
                new PlatformHouseRequestOrigin.Standalone(
                    PlatformStandaloneOperationIdentity.Create(
                        "ecosystem-product-test")),
                operation,
                new PlatformSourcePlan(
                    PlatformSourcePlanIdentity.Create(
                        "ecosystem-product-test-sources"),
                    PlatformSourcePolicyGeneration.Create(
                        "ecosystem-product-test-generation"),
                    [
                        new PlatformSourceSelection(
                            PlatformSourceFacet.Implementation,
                            PlatformSourceSelectionMode.Precedence,
                            [capability]),
                    ]),
                new PlatformHouseWorkBudget(
                    maxSourceOperations: 2,
                    maxTargetCandidates: 2,
                    maxAssemblies: 4,
                    maxXmlDocuments: 2,
                    maxPortablePdbs: 2,
                    maxSourceDocuments: 2,
                    maxBytes: 32 * 1024 * 1024,
                    maxForwardingHops: 2,
                    maxDuration: TimeSpan.FromSeconds(30)),
                cancellationToken);
            var contribution = new PlatformSourceContribution.Realization(
                PlatformSourceFacet.Implementation,
                capability,
                request.Snapshot,
                PlatformSourceGeneration.Create(
                    "ecosystem-product-test-source-generation"),
                target,
                PlatformSourceCoordinateIdentity.Create(
                    "ecosystem-product-test-coordinate"),
                operation.Population,
                PlatformSourceContributionCompleteness.Authoritative);

            byte[] focusContent = await File.ReadAllBytesAsync(
                typeof(ProductEcosystemPopulationLoaderTests).Assembly
                    .Location,
                cancellationToken);
            List<PlatformPopulationLibraryArtifactMaterializationItem> items =
            [
                Item(
                    focusContent,
                    contribution,
                    target,
                    PlatformPopulationMemberRole.Focus,
                    "ecosystem-product-focus"),
            ];
            long totalBytes = focusContent.LongLength;
            if (declaration.Family == PlatformFamily.AspNetCore)
            {
                byte[] supportContent = await File.ReadAllBytesAsync(
                    typeof(JsonSerializer).Assembly.Location,
                    cancellationToken);
                totalBytes = checked(
                    totalBytes + supportContent.LongLength);
                items.Add(
                    Item(
                        supportContent,
                        contribution,
                        new(
                            PlatformFamily.DotNetRuntime,
                            target.TargetFramework,
                            target.Version),
                        PlatformPopulationMemberRole.BindingSupport,
                        "ecosystem-product-runtime-support"));
            }

            var consumed = new PlatformHouseConsumedWork(
                sourceOperations: 1,
                targetCandidates: 0,
                assemblies: _exceedBudget ? 5 : items.Count,
                xmlDocuments: 0,
                portablePdbs: 0,
                sourceDocuments: 0,
                bytes: totalBytes,
                forwardingHops: 0,
                targetComparisons: 0,
                elapsed: TimeSpan.Zero);
            PlatformPopulationArtifactMaterializationOutcome outcome =
                _cleanupLease is null
                    ? await PlatformHousePopulationArtifactMaterializer
                        .MaterializeImplementationsAsync(
                            request,
                            items,
                            consumed,
                            "ecosystem-product-test")
                    : await MaterializeWithCleanupFailureAsync(
                        request,
                        contribution,
                        target,
                        focusContent,
                        consumed,
                        _cleanupLease,
                        cancellationToken);
            Completed = outcome
                as PlatformPopulationArtifactMaterializationOutcome.Completed;
            return outcome;
        }

        static async ValueTask<
            PlatformPopulationArtifactMaterializationOutcome>
            MaterializeWithCleanupFailureAsync(
                PlatformHouseRequest request,
                PlatformSourceContribution.Realization contribution,
                PlatformFamilyTarget target,
                byte[] content,
                PlatformHouseConsumedWork consumed,
                IArtifactAcquisitionLease cleanupLease,
                CancellationToken cancellationToken)
        {
            var session = new ArtifactSetSession();
            ArtifactQueryLease? queryLease = null;
            ArtifactContentLease? contentLease = null;
            try
            {
                await session.AddRequiredAcquisitionAsync(
                    (scope, _) =>
                    {
                        ArtifactContribution artifact = scope.Register(
                            new PlatformLibraryArtifactProvenance(
                                contribution,
                                new Provenance(
                                    "ecosystem-product-cleanup")),
                            _ => new MemoryStream(
                                content,
                                writable: false));
                        return ValueTask.FromResult<
                            ArtifactAcquisitionOutcome>(
                                new ArtifactAcquisitionOutcome.Acquired(
                                    [artifact],
                                    cleanupLease));
                    },
                    cancellationToken: cancellationToken);
                ArtifactAssemblyProjection? projection = null;
                Assert.IsType<ArtifactSetPublicationOutcome.Published>(
                    await session.SealWithProjectionAsync(
                        (view, token) =>
                        {
                            projection =
                                Assert.IsType<
                                    ArtifactAssemblyProjectionOutcome.Projected>(
                                        ArtifactAssemblyInspection.Project(
                                            view,
                                            token))
                                    .Value;
                            return null;
                        },
                        cancellationToken));
                queryLease = session.IssueLease(
                    session.CreateQueryAuthorization());
                ArtifactContentReference reference =
                    session.GetCatalog(queryLease)
                        .Select(
                            descriptor => session.GetContentReference(
                                descriptor.Identity,
                                queryLease))
                        .Single();
                contentLease =
                    session.IssueContentLease(reference, queryLease);
                var selection =
                    new PlatformPopulationLibraryContentSelection(
                        reference,
                        Assert.IsType<ArtifactAssemblyProjection>(
                            projection),
                        new PlatformPopulationMemberAttribution(
                            target,
                            PlatformPopulationMemberRole.Focus));
                var population =
                    Assert.IsType<
                        PlatformPopulationRealizationResult.Completed>(
                            await PlatformHousePopulationRealizer
                                .RealizeImplementationsAsync(
                                    request,
                                    [selection],
                                    [contentLease],
                                    consumed));
                contentLease = null;
                queryLease.Dispose();
                queryLease = null;
                return PlatformPopulationArtifactMaterializationOutcome
                    .Completed
                    .Create(population, session);
            }
            catch
            {
                contentLease?.Dispose();
                queryLease?.Dispose();
                await session.DisposeAsync();
                throw;
            }
        }

        static PlatformPopulationLibraryArtifactMaterializationItem Item(
            byte[] content,
            PlatformSourceContribution.Realization contribution,
            PlatformFamilyTarget target,
            PlatformPopulationMemberRole role,
            string provenance) =>
            new(
                new PlatformLibraryArtifactMaterializationItem(
                    contribution,
                    new Provenance(provenance),
                    ReadAssemblyIdentity(content),
                    content.LongLength,
                    _ => new MemoryStream(content, writable: false)),
                new PlatformPopulationMemberAttribution(target, role));
    }

    sealed class FailingArtifactLease(Exception failure) :
        IArtifactAcquisitionLease
    {
        internal int Disposals { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.FromException(failure);
        }
    }

    sealed record Provenance(string Name) : IArtifactProvenance;
}
