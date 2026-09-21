using System.Reflection;
using System.Reflection.Metadata;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspector.Libraries;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using Inspector.Resources;

namespace DotnetInspector.PlatformHouse.Tests;

public partial class PlatformLibraryRealizationTests
{
    [Fact]
    public async Task
        ExactLibraryRealizer_TransfersReferenceAndImplementationOwner()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PlatformLibraryIdentity library =
            PlatformLibraryIdentityAuthority.Create("runtime-catalog")
                .Issue("System.Text.Json");
        var request = Request(
            library,
            PlatformViewDemand.ReferenceAndImplementation,
            cancellationToken);
        PlatformSourceContribution.Realization referenceContribution =
            Contribution(
                request.Request,
                request.Reference,
                PlatformSourceFacet.Reference);
        PlatformSourceContribution.Realization implementationContribution =
            Contribution(
                request.Request,
                request.Implementation,
                PlatformSourceFacet.Implementation);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(
                referenceContribution,
                implementationContribution);
        PlatformLibraryContentSelection reference = artifacts.Selection(0);
        PlatformLibraryContentSelection implementation =
            artifacts.Selection(1);
        var correspondence = new PlatformLibraryViewCorrespondence(
            reference,
            implementation,
            "system-text-json-views");
        ArtifactContentLease referenceLease =
            artifacts.IssueContentLease(0);
        ArtifactContentLease implementationLease =
            artifacts.IssueContentLease(1);

        PlatformLibraryRealizationResult.Completed completed =
            Assert.IsType<PlatformLibraryRealizationResult.Completed>(
                PlatformHouseLibraryRealizer
                    .RealizeReferenceAndImplementation(
                        request.Request,
                        reference,
                        referenceLease,
                        implementation,
                        implementationLease,
                        correspondence,
                        Consumed(assemblies: 2)));

        LibraryReference realized = completed.Value.Reference;
        Assert.Same(realized, completed.Owner.Reference);
        Assert.Same(
            realized,
            completed.Receipt.RealizedLibrary);
        Assert.Same(artifacts[0], realized.ApiAssembly.ArtifactReference);
        Assert.Same(
            artifacts[1],
            realized.ImplementationAssembly!.ArtifactReference);
        Assert.True(
            realized.ApiAssembly.HasRole(
                LibraryContentRole.ApiAssembly));
        Assert.True(
            realized.ImplementationAssembly.HasRole(
                LibraryContentRole.ImplementationAssembly));
        Assert.Equal(
            PlatformFamily.DotNetRuntime,
            Assert.IsType<ExactLibrarySourceCoordinate.Platform>(
                    realized.SourceCoordinate)
                .Population.Family);
        Assert.Same(
            correspondence.Identity,
            Assert.IsType<PlatformHouseCompletion.Realization>(
                    completed.Receipt.HouseReceipt.Completion)
                .ViewCorrespondence);

        Task artifactRetirement = artifacts.BeginRetirement();
        Assert.False(artifactRetirement.IsCompleted);
        LibraryOperationLease operation = Issued(
            completed.Owner,
            realized);
        Assert.Equal(
            (0x4d, 0x4d),
            operation.SnapshotPair(
                realized.ApiAssembly,
                realized.ImplementationAssembly,
                static (view, _) =>
                    ((int)view.First.Content[0],
                        (int)view.Second.Content[0]),
                cancellationToken));
        operation.Dispose();
        await completed.Owner.DisposeAsync();
        await artifactRetirement.WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task ExactLibraryRealizer_ClosesReferenceOnlyRole()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PlatformLibraryIdentity library =
            PlatformLibraryIdentityAuthority.Create("reference-catalog")
                .Issue("System.Collections");
        var request = Request(
            library,
            PlatformViewDemand.Reference,
            cancellationToken);
        PlatformSourceContribution.Realization contribution =
            Contribution(
                request.Request,
                request.Reference,
                PlatformSourceFacet.Reference);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(contribution);
        PlatformLibraryContentSelection reference = artifacts.Selection(0);

        PlatformLibraryRealizationResult.Completed completed =
            Assert.IsType<PlatformLibraryRealizationResult.Completed>(
                PlatformHouseLibraryRealizer.RealizeReference(
                    request.Request,
                    reference,
                    artifacts.IssueContentLease(0),
                    Consumed(assemblies: 1)));

        Assert.Null(completed.Value.Reference.ImplementationAssembly);
        Assert.Single(completed.Value.Reference.Contents);
        await completed.Owner.DisposeAsync();
    }

    [Fact]
    public async Task
        ExactLibraryRealizer_RequiresImplementationDeclarationSurface()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PlatformLibraryIdentity library =
            PlatformLibraryIdentityAuthority.Create("runtime-catalog")
                .Issue("System.Memory");
        var request = Request(
            library,
            PlatformViewDemand.Implementation,
            cancellationToken);
        PlatformSourceContribution.Realization contribution =
            Contribution(
                request.Request,
                request.Implementation,
                PlatformSourceFacet.Implementation);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(contribution);
        PlatformLibraryContentSelection implementation =
            artifacts.Selection(0);
        ArtifactContentLease lease = artifacts.IssueContentLease(0);

        PlatformLibraryRealizationResult.Terminal result =
            Assert.IsType<PlatformLibraryRealizationResult.Terminal>(
                PlatformHouseLibraryRealizer.RealizeImplementation(
                    request.Request,
                    implementation,
                    lease,
                    declarationSurface: null,
                    Consumed(assemblies: 1)));
        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Unavailable>(
                    result.Outcome);

        Assert.Equal(
            PlatformHouseSettlementKind.Unavailable,
            result.Receipt.HouseReceipt.SettlementKind);
        Assert.Equal(0x4d, ReadByte(lease, cancellationToken));
        lease.Dispose();
    }

    [Fact]
    public async Task
        ExactLibraryRealizer_AssignsBothRolesWithDeclarationEvidence()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PlatformLibraryIdentity library =
            PlatformLibraryIdentityAuthority.Create("runtime-catalog")
                .Issue("System.Memory");
        var request = Request(
            library,
            PlatformViewDemand.Implementation,
            cancellationToken);
        PlatformSourceContribution.Realization contribution =
            Contribution(
                request.Request,
                request.Implementation,
                PlatformSourceFacet.Implementation);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(contribution);
        PlatformLibraryContentSelection implementation =
            artifacts.Selection(0);
        PlatformLibraryViewCorrespondence declaration =
            PlatformLibraryViewCorrespondence
                .CreateImplementationDeclarationSurface(
                    implementation,
                    "system-memory-declaration");

        PlatformLibraryRealizationResult.Completed completed =
            Assert.IsType<PlatformLibraryRealizationResult.Completed>(
                PlatformHouseLibraryRealizer.RealizeImplementation(
                    request.Request,
                    implementation,
                    artifacts.IssueContentLease(0),
                    declaration,
                    Consumed(assemblies: 1)));

        Assert.Same(
            completed.Value.Reference.ApiAssembly,
            completed.Value.Reference.ImplementationAssembly);
        Assert.Equal(
            [
                LibraryContentRole.ApiAssembly,
                LibraryContentRole.ImplementationAssembly,
            ],
            completed.Value.Reference.ApiAssembly.Roles);
        await completed.Owner.DisposeAsync();
    }

    [Fact]
    public async Task
        ExactLibraryRealizer_RejectsForeignContentWithoutAcceptingLease()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PlatformLibraryIdentity library =
            PlatformLibraryIdentityAuthority.Create("reference-catalog")
                .Issue("System.Runtime");
        var request = Request(
            library,
            PlatformViewDemand.Reference,
            cancellationToken);
        PlatformSourceContribution.Realization contribution =
            Contribution(
                request.Request,
                request.Reference,
                PlatformSourceFacet.Reference);
        await using ArtifactFixture selectedArtifacts =
            await ArtifactFixture.CreateAsync(contribution);
        await using ArtifactFixture foreignArtifacts =
            await ArtifactFixture.CreateAsync(contribution);
        PlatformLibraryContentSelection reference =
            selectedArtifacts.Selection(0);
        ArtifactContentLease foreignLease =
            foreignArtifacts.IssueContentLease(0);

        PlatformLibraryRealizationResult.Terminal result =
            Assert.IsType<PlatformLibraryRealizationResult.Terminal>(
                PlatformHouseLibraryRealizer.RealizeReference(
                    request.Request,
                    reference,
                    foreignLease,
                    Consumed(assemblies: 1)));
        PlatformHouseOutcome<
            PlatformLibraryRealizationValue>.Rejected rejected =
                Assert.IsType<
                    PlatformHouseOutcome<
                        PlatformLibraryRealizationValue>.Rejected>(
                            result.Outcome);

        Assert.Equal(
            PlatformHouseRejectionKind.InvalidOwnerResult,
            Assert.IsType<PlatformHouseRejection.OwnerEvidence>(
                    rejected.Evidence.Rejection)
                .Kind);
        Assert.Equal(0x4d, ReadByte(foreignLease, cancellationToken));
        foreignLease.Dispose();
    }

    [Fact]
    public async Task
        ContentSelection_RejectsForeignMetadataProjection()
    {
        PlatformLibraryIdentity library =
            PlatformLibraryIdentityAuthority.Create("reference-catalog")
                .Issue("System.Runtime");
        var request = Request(
            library,
            PlatformViewDemand.Reference,
            TestContext.Current.CancellationToken);
        PlatformSourceContribution.Realization contribution =
            Contribution(
                request.Request,
                request.Reference,
                PlatformSourceFacet.Reference);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(
                contribution,
                contribution);

        Assert.Throws<ArgumentException>(
            () => new PlatformLibraryContentSelection(
                artifacts[0],
                artifacts.Projection(1)));

        PlatformLibraryContentSelection selected =
            artifacts.Selection(0);
        Assert.Same(contribution, selected.Contribution);
        Assert.Equal(
            typeof(JsonSerializer).Assembly.GetName().Name,
            selected.AssemblyIdentity.Identity.Name);
    }

    [Fact]
    public async Task
        ExactLibraryRealizer_RejectsUnauthorizedSourceWithoutAcceptingLease()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PlatformLibraryIdentity library =
            PlatformLibraryIdentityAuthority.Create("reference-catalog")
                .Issue("System.Runtime");
        var request = Request(
            library,
            PlatformViewDemand.Reference,
            cancellationToken);
        PlatformSourceContribution.Realization contribution =
            Contribution(
                request.Request,
                PlatformSourceCapabilityIdentity.Create(
                    "unauthorized-reference"),
                PlatformSourceFacet.Reference);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(contribution);
        PlatformLibraryContentSelection reference = artifacts.Selection(0);
        ArtifactContentLease lease = artifacts.IssueContentLease(0);

        PlatformLibraryRealizationResult.Terminal result =
            Assert.IsType<PlatformLibraryRealizationResult.Terminal>(
                PlatformHouseLibraryRealizer.RealizeReference(
                    request.Request,
                    reference,
                    lease,
                    Consumed(assemblies: 1)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Rejected>(
                    result.Outcome);
        Assert.Equal(0x4d, ReadByte(lease, cancellationToken));
        lease.Dispose();
    }

    [Fact]
    public async Task
        ExactLibraryRealizer_CancellationPrecedesOwnershipAcceptance()
    {
        PlatformLibraryIdentity library =
            PlatformLibraryIdentityAuthority.Create("reference-catalog")
                .Issue("System.Runtime");
        var baseline = Request(
            library,
            PlatformViewDemand.Reference,
            TestContext.Current.CancellationToken);
        var request = (
            Request: new PlatformHouseRequest(
                baseline.Request.Identity,
                baseline.Request.Target,
                baseline.Request.Origin,
                baseline.Request.Operation,
                baseline.Request.Sources,
                baseline.Request.Work,
                new CancellationToken(canceled: true)),
            baseline.Reference,
            baseline.Implementation);
        PlatformSourceContribution.Realization contribution =
            Contribution(
                request.Request,
                request.Reference,
                PlatformSourceFacet.Reference);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(contribution);
        PlatformLibraryContentSelection reference = artifacts.Selection(0);
        ArtifactContentLease lease = artifacts.IssueContentLease(0);

        Assert.Throws<OperationCanceledException>(
            () => PlatformHouseLibraryRealizer.RealizeReference(
                request.Request,
                reference,
                lease,
                Consumed(assemblies: 1)));
        Assert.Equal(0x4d, ReadByte(lease, CancellationToken.None));
        lease.Dispose();
    }

    [Fact]
    public void PlatformHouseFailedOutcome_RetainsTypedResourceFreeEvidence()
    {
        PlatformLibraryIdentity library =
            PlatformLibraryIdentityAuthority.Create("reference-catalog")
                .Issue("System.Runtime");
        var request = Request(
            library,
            PlatformViewDemand.Reference,
            TestContext.Current.CancellationToken);
        var failure = new PlatformHouseTermination.Failed(
            PlatformHouseTerminalEvidenceIdentity.Create(
                "library-release-failed"),
            [
                PlatformHouseFailureKind.LibraryRetirement,
                PlatformHouseFailureKind.LibraryChildRelease,
            ],
            cancellationObserved: true);
        var receipt = new PlatformHouseReceipt(
            request.Request.Snapshot,
            new PlatformTargetSettlement.Exact(
                (PlatformTargetDemand.Exact)
                    request.Request.Target),
            [],
            Consumed(elapsed: TimeSpan.FromSeconds(31)),
            termination: failure);

        var outcome = new PlatformHouseOutcome<string>.Failed(
            failure,
            receipt);

        Assert.Same(failure, outcome.Evidence);
        Assert.True(failure.CancellationObserved);
        Assert.Equal(2, failure.Failures.Count);
        Assert.DoesNotContain(
            failure.GetType().GetProperties(),
            property =>
                typeof(Exception).IsAssignableFrom(
                    property.PropertyType));
    }

    [Fact]
    public async Task
        ArtifactMaterializer_RejectsInvalidViewShapeBeforePublication()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PlatformLibraryIdentity library =
            PlatformLibraryIdentityAuthority.Create("runtime-catalog")
                .Issue("System.Text.Json");
        var request = Request(
            library,
            PlatformViewDemand.ReferenceAndImplementation,
            cancellationToken);
        PlatformSourceContribution.Realization contribution =
            Contribution(
                request.Request,
                request.Reference,
                PlatformSourceFacet.Reference);
        byte[] content = await File.ReadAllBytesAsync(
            typeof(JsonSerializer).Assembly.Location,
            cancellationToken);
        using var reader = new System.Reflection.PortableExecutable.PEReader(
            new MemoryStream(content, writable: false));
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                reader.GetMetadataReader());
        int opens = 0;
        var item = new PlatformLibraryArtifactMaterializationItem(
            contribution,
            new Provenance("reference"),
            identity,
            content.LongLength,
            _ =>
            {
                opens++;
                return new MemoryStream(content, writable: false);
            });
        var consumed = new PlatformHouseConsumedWork(
            sourceOperations: 1,
            targetCandidates: 0,
            assemblies: 1,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: 0,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);

        var terminal = Assert.IsType<
            PlatformLibraryArtifactMaterializationOutcome.Terminal>(
                await PlatformHouseArtifactMaterializer.MaterializeAsync(
                    request.Request,
                    PlatformViewDemand.ReferenceAndImplementation,
                    [item],
                    consumed,
                    "test-platform-library"));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
        Assert.Equal(0, opens);
    }

    [Fact]
    public async Task
        PopulationArtifactMaterializer_RejectsDuplicateIdentityBeforePublication()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var request = PopulationRequest(cancellationToken);
        PlatformSourceContribution.Realization contribution =
            PopulationContribution(
                request.Request,
                request.Reference);
        byte[] content = await File.ReadAllBytesAsync(
            typeof(PlatformLibraryRealizationTests).Assembly.Location,
            cancellationToken);
        using var reader = new System.Reflection.PortableExecutable.PEReader(
            new MemoryStream(content, writable: false));
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                reader.GetMetadataReader());
        int opens = 0;
        PlatformPopulationLibraryArtifactMaterializationItem Item() =>
            new(
                new PlatformLibraryArtifactMaterializationItem(
                    contribution,
                    new Provenance("reference"),
                    identity,
                    content.LongLength,
                    _ =>
                    {
                        opens++;
                        return new MemoryStream(content, writable: false);
                    }),
                new PlatformPopulationMemberAttribution(
                    Target(),
                    PlatformPopulationMemberRole.Focus));
        var consumed = new PlatformHouseConsumedWork(
            sourceOperations: 1,
            targetCandidates: 0,
            assemblies: 2,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: checked(content.LongLength * 2),
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);

        var terminal = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Terminal>(
                await PlatformHousePopulationArtifactMaterializer
                    .MaterializeReferencesAsync(
                        request.Request,
                        [Item(), Item()],
                        consumed,
                        "test-platform-population"));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
        Assert.Equal(0, opens);
    }

    [Fact]
    public async Task
        ImplementationPopulationArtifactMaterializer_RejectsForeignContributionBeforePublication()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var request = PopulationRequest(
            cancellationToken,
            PlatformViewDemand.Implementation);
        var foreign = PopulationRequest(
            cancellationToken,
            PlatformViewDemand.Implementation);
        PlatformSourceContribution.Realization contribution =
            PopulationContribution(
                foreign.Request,
                foreign.Implementation,
                PlatformSourceFacet.Implementation);
        byte[] content = await File.ReadAllBytesAsync(
            typeof(PlatformLibraryRealizationTests).Assembly.Location,
            cancellationToken);
        using var reader = new System.Reflection.PortableExecutable.PEReader(
            new MemoryStream(content, writable: false));
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                reader.GetMetadataReader());
        int opens = 0;
        var item = new PlatformLibraryArtifactMaterializationItem(
            contribution,
            new Provenance("implementation"),
            identity,
            content.LongLength,
            _ =>
            {
                opens++;
                return new MemoryStream(content, writable: false);
            });
        var consumed = new PlatformHouseConsumedWork(
            sourceOperations: 1,
            targetCandidates: 0,
            assemblies: 1,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: content.LongLength,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);

        var terminal = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Terminal>(
                await PlatformHousePopulationArtifactMaterializer
                    .MaterializeImplementationsAsync(
                        request.Request,
                        [
                            new PlatformPopulationLibraryArtifactMaterializationItem(
                                item,
                                new PlatformPopulationMemberAttribution(
                                    Target(),
                                    PlatformPopulationMemberRole.Focus)),
                        ],
                        consumed,
                        "test-platform-population"));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
        Assert.Equal(0, opens);
    }

    [Fact]
    public async Task
        PairedPopulationArtifactMaterializer_RejectsForeignContributionBeforePublication()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var request = PopulationRequest(
            cancellationToken,
            PlatformViewDemand.ReferenceAndImplementation);
        var foreign = PopulationRequest(
            cancellationToken,
            PlatformViewDemand.ReferenceAndImplementation);
        byte[] content = await File.ReadAllBytesAsync(
            typeof(PlatformLibraryRealizationTests).Assembly.Location,
            cancellationToken);
        using var reader =
            new System.Reflection.PortableExecutable.PEReader(
                new MemoryStream(content, writable: false));
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                reader.GetMetadataReader());
        int opens = 0;
        PlatformPopulationLibraryArtifactMaterializationItem Item(
            PlatformSourceContribution.Realization contribution,
            string provenance) =>
            new(
                new PlatformLibraryArtifactMaterializationItem(
                    contribution,
                    new Provenance(provenance),
                    identity,
                    content.LongLength,
                    _ =>
                    {
                        opens++;
                        return new MemoryStream(content, writable: false);
                    }),
                new PlatformPopulationMemberAttribution(
                    Target(),
                    PlatformPopulationMemberRole.Focus));
        var consumed = new PlatformHouseConsumedWork(
            sourceOperations: 2,
            targetCandidates: 0,
            assemblies: 2,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: checked(content.LongLength * 2),
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);

        var terminal = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Terminal>(
                await PlatformHousePopulationArtifactMaterializer
                    .MaterializeReferenceAndImplementationAsync(
                        request.Request,
                        [
                            Item(
                                PopulationContribution(
                                    request.Request,
                                    request.Reference),
                                "reference"),
                        ],
                        [
                            Item(
                                PopulationContribution(
                                    foreign.Request,
                                    foreign.Implementation,
                                    PlatformSourceFacet.Implementation),
                                "foreign-implementation"),
                        ],
                        consumed,
                        "test-platform-population"));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
        Assert.Equal(0, opens);
    }

    [Fact]
    public async Task
        ReferencePopulationRealizer_TransfersOrderedOwnersAtomically()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var request = PopulationRequest(cancellationToken);
        PlatformSourceContribution.Realization contribution =
            PopulationContribution(
                request.Request,
                request.Reference);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationAsync(
                (
                    contribution,
                    typeof(JsonSerializer).Assembly.Location),
                (
                    contribution,
                    typeof(PlatformHousePopulationRealizer)
                        .Assembly.Location));
        PlatformPopulationLibraryContentSelection[] selections =
        [
            artifacts.PopulationSelection(0),
            artifacts.PopulationSelection(1),
        ];
        ArtifactContentLease[] leases =
        [
            artifacts.IssueContentLease(0),
            artifacts.IssueContentLease(1),
        ];

        var completed = Assert.IsType<
            PlatformPopulationRealizationResult.Completed>(
                await PlatformHousePopulationRealizer
                    .RealizeReferencesAsync(
                        request.Request,
                        selections,
                        leases,
                        Consumed(
                            sourceOperations: 1,
                            assemblies: 2)));

        Assert.Equal(2, completed.Value.Libraries.Count);
        Assert.Equal(2, completed.Owners.Count);
        Assert.Same(
            contribution,
            Assert.Single(
                    completed.Receipt.HouseReceipt.SourceSettlements)
                .Contribution);
        for (int index = 0; index < completed.Owners.Count; index++)
        {
            LibraryReference library =
                completed.Value.Libraries[index];
            Assert.Same(library, completed.Owners[index].Reference);
            Assert.True(
                AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    selections[index].AssemblyIdentity.Identity,
                    Assert.IsType<ManagedMetadataIdentity.Assembly>(
                            library.ApiAssembly.AssemblyIdentity)
                        .Identity));
            using LibraryOperationLease operation =
                Issued(completed.Owners[index], library);
            Assert.Equal(
                (byte)'M',
                operation.Snapshot(
                    library.ApiAssembly,
                    static (view, _) => view.Content[0],
                    cancellationToken));
        }

        Task artifactRetirement = artifacts.BeginRetirement();
        Assert.False(artifactRetirement.IsCompleted);
        await completed.Owners[0].DisposeAsync();
        Assert.False(artifactRetirement.IsCompleted);
        await completed.Owners[1].DisposeAsync();
        await artifactRetirement.WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task
        ImplementationPopulationRealizer_AssignsBothRolesAtomically()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var request = PopulationRequest(
            cancellationToken,
            PlatformViewDemand.Implementation);
        PlatformSourceContribution.Realization contribution =
            PopulationContribution(
                request.Request,
                request.Implementation,
                PlatformSourceFacet.Implementation);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationAsync(
                (
                    contribution,
                    typeof(JsonSerializer).Assembly.Location),
                (
                    contribution,
                    typeof(System.Net.Http.HttpClient).Assembly.Location));
        PlatformPopulationLibraryContentSelection[] selections =
        [
            artifacts.PopulationSelection(0),
            artifacts.PopulationSelection(1),
        ];

        var completed = Assert.IsType<
            PlatformPopulationRealizationResult.Completed>(
                await PlatformHousePopulationRealizer
                    .RealizeImplementationsAsync(
                        request.Request,
                        selections,
                        [
                            artifacts.IssueContentLease(0),
                            artifacts.IssueContentLease(1),
                        ],
                        Consumed(
                            sourceOperations: 1,
                            assemblies: 2)));

        Assert.NotNull(
            Assert.IsType<PlatformHouseCompletion.Realization>(
                    completed.Receipt.HouseReceipt.Completion)
                .ViewCorrespondence);
        Assert.Same(
            contribution,
            Assert.Single(
                    completed.Receipt.HouseReceipt.SourceSettlements)
                .Contribution);
        for (int index = 0; index < completed.Owners.Count; index++)
        {
            LibraryReference library =
                completed.Value.Libraries[index];
            Assert.Same(library, completed.Owners[index].Reference);
            Assert.Same(
                library.ApiAssembly,
                library.ImplementationAssembly);
            Assert.Equal(2, library.ApiAssembly.Roles.Count);
            using LibraryOperationLease operation =
                Issued(completed.Owners[index], library);
            Assert.Equal(
                (byte)'M',
                operation.Snapshot(
                    library.ApiAssembly,
                    static (view, _) => view.Content[0],
                    cancellationToken));
        }

        Task artifactRetirement = artifacts.BeginRetirement();
        Assert.False(artifactRetirement.IsCompleted);
        await completed.Owners[0].DisposeAsync();
        Assert.False(artifactRetirement.IsCompleted);
        await completed.Owners[1].DisposeAsync();
        await artifactRetirement.WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task
        ImplementationPopulationRealizer_PreservesFocusAndBindingSupport()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PlatformFamilyTarget aspNetTarget = new(
            PlatformFamily.AspNetCore,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0"));
        var request = PopulationRequest(
            cancellationToken,
            PlatformViewDemand.Implementation,
            aspNetTarget);
        PlatformSourceContribution.Realization contribution =
            PopulationContribution(
                request.Request,
                request.Implementation,
                PlatformSourceFacet.Implementation);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationAsync(
                (
                    contribution,
                    typeof(JsonSerializer).Assembly.Location),
                (
                    contribution,
                    typeof(System.Net.Http.HttpClient).Assembly.Location));
        PlatformPopulationMemberAttribution focus = new(
            aspNetTarget,
            PlatformPopulationMemberRole.Focus);
        PlatformPopulationMemberAttribution support = new(
            new PlatformFamilyTarget(
                PlatformFamily.DotNetRuntime,
                aspNetTarget.TargetFramework,
                PlatformVersion.Parse("11.0.1")),
            PlatformPopulationMemberRole.BindingSupport);

        var completed = Assert.IsType<
            PlatformPopulationRealizationResult.Completed>(
                await PlatformHousePopulationRealizer
                    .RealizeImplementationsAsync(
                        request.Request,
                        [
                            artifacts.PopulationSelection(0, focus),
                            artifacts.PopulationSelection(1, support),
                        ],
                        [
                            artifacts.IssueContentLease(0),
                            artifacts.IssueContentLease(1),
                        ],
                        Consumed(
                            sourceOperations: 1,
                            assemblies: 2)));

        Assert.Equal(
            [
                PlatformPopulationMemberRole.Focus,
                PlatformPopulationMemberRole.BindingSupport,
            ],
            completed.Value.Members.Select(
                static member => member.Role));
        Assert.Equal(
            [aspNetTarget, support.Target],
            completed.Value.Members.Select(
                static member => member.Target));
        Assert.Equal(
            [
                PlatformFamily.AspNetCore,
                PlatformFamily.DotNetRuntime,
            ],
            completed.Value.Libraries.Select(
                static library =>
                    Assert.IsType<
                            ExactLibrarySourceCoordinate.Platform>(
                            library.SourceCoordinate)
                        .Population.Family));
        Assert.Same(
            completed.Value.Members[0],
            completed.Receipt.RealizedMembers![0]);
        Assert.Same(
            completed.Value.Members[1],
            completed.Receipt.RealizedMembers![1]);

        Task artifactRetirement = artifacts.BeginRetirement();
        await completed.Owners[0].DisposeAsync();
        await completed.Owners[1].DisposeAsync();
        await artifactRetirement.WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task
        PairedPopulationRealizer_PreservesLosslessUnionAndCorrespondence()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var request = PopulationRequest(
            cancellationToken,
            PlatformViewDemand.ReferenceAndImplementation);
        PlatformSourceContribution.Realization referenceContribution =
            PopulationContribution(
                request.Request,
                request.Reference);
        PlatformSourceContribution.Realization implementationContribution =
            PopulationContribution(
                request.Request,
                request.Implementation,
                PlatformSourceFacet.Implementation);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationAsync(
                (
                    referenceContribution,
                    typeof(JsonSerializer).Assembly.Location),
                (
                    referenceContribution,
                    typeof(PlatformHousePopulationRealizer)
                        .Assembly.Location),
                (
                    implementationContribution,
                    typeof(JsonSerializer).Assembly.Location),
                (
                    implementationContribution,
                    typeof(System.Net.Http.HttpClient).Assembly.Location));

        var completed = Assert.IsType<
            PlatformPopulationRealizationResult.Completed>(
                await PlatformHousePopulationRealizer
                    .RealizeReferenceAndImplementationAsync(
                        request.Request,
                        [
                            artifacts.PopulationSelection(0),
                            artifacts.PopulationSelection(1),
                        ],
                        [
                            artifacts.IssueContentLease(0),
                            artifacts.IssueContentLease(1),
                        ],
                        [
                            artifacts.PopulationSelection(2),
                            artifacts.PopulationSelection(3),
                        ],
                        [
                            artifacts.IssueContentLease(2),
                            artifacts.IssueContentLease(3),
                        ],
                        Consumed(
                            sourceOperations: 2,
                            assemblies: 4)));

        Assert.Equal(3, completed.Value.Libraries.Count);
        Assert.Equal(3, completed.Owners.Count);
        Assert.Equal(
            [referenceContribution, implementationContribution],
            completed.Receipt.HouseReceipt.SourceSettlements
                .Select(static settlement => settlement.Contribution));
        Assert.NotNull(
            Assert.IsType<PlatformHouseCompletion.Realization>(
                    completed.Receipt.HouseReceipt.Completion)
                .ViewCorrespondence);

        LibraryReference paired = completed.Value.Libraries[0];
        Assert.Same(artifacts[0], paired.ApiAssembly.ArtifactReference);
        Assert.Same(
            artifacts[2],
            paired.ImplementationAssembly!.ArtifactReference);
        Assert.Equal(2, paired.Contents.Count);

        LibraryReference referenceOnly = completed.Value.Libraries[1];
        Assert.Same(
            artifacts[1],
            referenceOnly.ApiAssembly.ArtifactReference);
        Assert.Null(referenceOnly.ImplementationAssembly);
        Assert.Single(referenceOnly.Contents);

        LibraryReference implementationOnly =
            completed.Value.Libraries[2];
        Assert.Same(
            artifacts[3],
            implementationOnly.ApiAssembly.ArtifactReference);
        Assert.Same(
            implementationOnly.ApiAssembly,
            implementationOnly.ImplementationAssembly);
        Assert.Equal(2, implementationOnly.ApiAssembly.Roles.Count);

        Task artifactRetirement = artifacts.BeginRetirement();
        Assert.False(artifactRetirement.IsCompleted);
        for (int index = 0; index < completed.Owners.Count; index++)
        {
            Assert.Same(
                completed.Value.Libraries[index],
                completed.Owners[index].Reference);
            await completed.Owners[index].DisposeAsync();
        }
        await artifactRetirement.WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task
        PairedPopulationRealizer_RejectsMismatchedMemberAttribution()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PlatformFamilyTarget aspNetTarget = new(
            PlatformFamily.AspNetCore,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0"));
        var request = PopulationRequest(
            cancellationToken,
            PlatformViewDemand.ReferenceAndImplementation,
            aspNetTarget);
        PlatformSourceContribution.Realization referenceContribution =
            PopulationContribution(
                request.Request,
                request.Reference);
        PlatformSourceContribution.Realization implementationContribution =
            PopulationContribution(
                request.Request,
                request.Implementation,
                PlatformSourceFacet.Implementation);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationAsync(
                (
                    referenceContribution,
                    typeof(JsonSerializer).Assembly.Location),
                (
                    implementationContribution,
                    typeof(JsonSerializer).Assembly.Location));
        PlatformPopulationMemberAttribution focus = new(
            aspNetTarget,
            PlatformPopulationMemberRole.Focus);
        PlatformPopulationMemberAttribution support = new(
            new PlatformFamilyTarget(
                PlatformFamily.DotNetRuntime,
                aspNetTarget.TargetFramework,
                PlatformVersion.Parse("11.0.0")),
            PlatformPopulationMemberRole.BindingSupport);

        var terminal = Assert.IsType<
            PlatformPopulationRealizationResult.Terminal>(
                await PlatformHousePopulationRealizer
                    .RealizeReferenceAndImplementationAsync(
                        request.Request,
                        [artifacts.PopulationSelection(0, focus)],
                        [artifacts.IssueContentLease(0)],
                        [artifacts.PopulationSelection(1, support)],
                        [artifacts.IssueContentLease(1)],
                        Consumed(
                            sourceOperations: 2,
                            assemblies: 2)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Rejected>(
                    terminal.Outcome);
        await artifacts.BeginRetirement().WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task
        PairedPopulationRealizer_DoesNotPairSameNameDifferentIdentities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var request = PopulationRequest(
            cancellationToken,
            PlatformViewDemand.ReferenceAndImplementation);
        PlatformSourceContribution.Realization referenceContribution =
            PopulationContribution(
                request.Request,
                request.Reference,
                PlatformSourceFacet.Reference);
        PlatformSourceContribution.Realization implementationContribution =
            PopulationContribution(
                request.Request,
                request.Implementation,
                PlatformSourceFacet.Implementation);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationAsync(
                (
                    referenceContribution,
                    FixtureCatalog.SourceDiffPair.OldAssemblyPath()),
                (
                    implementationContribution,
                    FixtureCatalog.SourceDiffPair.NewAssemblyPath()));

        var completed = Assert.IsType<
            PlatformPopulationRealizationResult.Completed>(
                await PlatformHousePopulationRealizer
                    .RealizeReferenceAndImplementationAsync(
                        request.Request,
                        [artifacts.PopulationSelection(0)],
                        [artifacts.IssueContentLease(0)],
                        [artifacts.PopulationSelection(1)],
                        [artifacts.IssueContentLease(1)],
                        Consumed(
                            sourceOperations: 2,
                            assemblies: 2)));

        Assert.Equal(2, completed.Value.Libraries.Count);
        LibraryReference referenceOnly =
            completed.Value.Libraries[0];
        LibraryReference implementationOnly =
            completed.Value.Libraries[1];
        Assert.Equal(
            referenceOnly.ApiAssembly.AssemblyIdentity!.Name,
            implementationOnly.ApiAssembly.AssemblyIdentity!.Name);
        Assert.NotEqual(
            referenceOnly.ApiAssembly.AssemblyIdentity,
            implementationOnly.ApiAssembly.AssemblyIdentity);
        Assert.Null(referenceOnly.ImplementationAssembly);
        Assert.Same(
            implementationOnly.ApiAssembly,
            implementationOnly.ImplementationAssembly);

        Task artifactRetirement = artifacts.BeginRetirement();
        foreach (LibraryContentOwner owner in completed.Owners)
            await owner.DisposeAsync();
        await artifactRetirement.WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task
        PairedPopulationRealizer_IncompleteWorkCleansBothFacets()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var request = PopulationRequest(
            cancellationToken,
            PlatformViewDemand.ReferenceAndImplementation);
        PlatformSourceContribution.Realization referenceContribution =
            PopulationContribution(
                request.Request,
                request.Reference,
                PlatformSourceFacet.Reference);
        PlatformSourceContribution.Realization implementationContribution =
            PopulationContribution(
                request.Request,
                request.Implementation,
                PlatformSourceFacet.Implementation);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationAsync(
                (
                    referenceContribution,
                    typeof(JsonSerializer).Assembly.Location),
                (
                    implementationContribution,
                    typeof(JsonSerializer).Assembly.Location));

        var terminal = Assert.IsType<
            PlatformPopulationRealizationResult.Terminal>(
                await PlatformHousePopulationRealizer
                    .RealizeReferenceAndImplementationAsync(
                        request.Request,
                        [artifacts.PopulationSelection(0)],
                        [artifacts.IssueContentLease(0)],
                        [artifacts.PopulationSelection(1)],
                        [artifacts.IssueContentLease(1)],
                        Consumed(
                            sourceOperations: 2,
                            assemblies: 65)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Incomplete>(
                    terminal.Outcome);
        await artifacts.BeginRetirement().WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task
        PairedPopulationRealizer_CancellationCleansBothFacets()
    {
        using var cancellation = new CancellationTokenSource();
        var request = PopulationRequest(
            cancellation.Token,
            PlatformViewDemand.ReferenceAndImplementation);
        PlatformSourceContribution.Realization referenceContribution =
            PopulationContribution(
                request.Request,
                request.Reference,
                PlatformSourceFacet.Reference);
        PlatformSourceContribution.Realization implementationContribution =
            PopulationContribution(
                request.Request,
                request.Implementation,
                PlatformSourceFacet.Implementation);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationAsync(
                (
                    referenceContribution,
                    typeof(JsonSerializer).Assembly.Location),
                (
                    implementationContribution,
                    typeof(JsonSerializer).Assembly.Location));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () =>
                await PlatformHousePopulationRealizer
                    .RealizeReferenceAndImplementationAsync(
                        request.Request,
                        [artifacts.PopulationSelection(0)],
                        [artifacts.IssueContentLease(0)],
                        [artifacts.PopulationSelection(1)],
                        [artifacts.IssueContentLease(1)],
                        Consumed(
                            sourceOperations: 2,
                            assemblies: 2)));

        await artifacts.BeginRetirement().WaitAsync(
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task
        ReferencePopulationRealizer_RetiresPartialOwnerOnInvalidAuthority()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var request = PopulationRequest(cancellationToken);
        PlatformSourceContribution.Realization contribution =
            PopulationContribution(
                request.Request,
                request.Reference);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationAsync(
                (
                    contribution,
                    typeof(JsonSerializer).Assembly.Location),
                (
                    contribution,
                    typeof(PlatformHousePopulationRealizer)
                        .Assembly.Location));
        ArtifactContentLease first =
            artifacts.IssueContentLease(0);
        ArtifactContentLease released =
            artifacts.IssueContentLease(1);
        released.Dispose();

        var terminal = Assert.IsType<
            PlatformPopulationRealizationResult.Terminal>(
                await PlatformHousePopulationRealizer
                    .RealizeReferencesAsync(
                        request.Request,
                        [
                            artifacts.PopulationSelection(0),
                            artifacts.PopulationSelection(1),
                        ],
                        [first, released],
                        Consumed(
                            sourceOperations: 1,
                            assemblies: 2)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Rejected>(
                    terminal.Outcome);
        await artifacts.BeginRetirement().WaitAsync(
            cancellationToken);
    }

    [Fact]
    public async Task
        ReferencePopulationRealizer_RejectsDuplicateIdentityAndCleansLeases()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var request = PopulationRequest(cancellationToken);
        PlatformSourceContribution.Realization contribution =
            PopulationContribution(
                request.Request,
                request.Reference);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationAsync(
                (
                    contribution,
                    typeof(JsonSerializer).Assembly.Location),
                (
                    contribution,
                    typeof(JsonSerializer).Assembly.Location));

        var terminal = Assert.IsType<
            PlatformPopulationRealizationResult.Terminal>(
                await PlatformHousePopulationRealizer
                    .RealizeReferencesAsync(
                        request.Request,
                        [
                            artifacts.PopulationSelection(0),
                            artifacts.PopulationSelection(1),
                        ],
                        [
                            artifacts.IssueContentLease(0),
                            artifacts.IssueContentLease(1),
                        ],
                        Consumed(
                            sourceOperations: 1,
                            assemblies: 2)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Rejected>(
                    terminal.Outcome);
        await artifacts.BeginRetirement().WaitAsync(
            cancellationToken);
    }

    [Fact]
    public async Task
        ReferencePopulationRealizer_IncompleteWorkCleansTransferredLease()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var request = PopulationRequest(cancellationToken);
        PlatformSourceContribution.Realization contribution =
            PopulationContribution(
                request.Request,
                request.Reference);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationAsync(
                (
                    contribution,
                    typeof(JsonSerializer).Assembly.Location));

        var terminal = Assert.IsType<
            PlatformPopulationRealizationResult.Terminal>(
                await PlatformHousePopulationRealizer
                    .RealizeReferencesAsync(
                        request.Request,
                        [artifacts.PopulationSelection(0)],
                        [artifacts.IssueContentLease(0)],
                        Consumed(assemblies: 65)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Incomplete>(
                    terminal.Outcome);
        await artifacts.BeginRetirement().WaitAsync(
            cancellationToken);
    }

    [Fact]
    public async Task
        ReferencePopulationRealizer_CancellationCleansTransferredLease()
    {
        using var cancellation = new CancellationTokenSource();
        var request = PopulationRequest(cancellation.Token);
        PlatformSourceContribution.Realization contribution =
            PopulationContribution(
                request.Request,
                request.Reference);
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreatePopulationAsync(
                (
                    contribution,
                    typeof(JsonSerializer).Assembly.Location));
        ArtifactContentLease lease =
            artifacts.IssueContentLease(0);
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () =>
                await PlatformHousePopulationRealizer
                    .RealizeReferencesAsync(
                        request.Request,
                        [artifacts.PopulationSelection(0)],
                        [lease],
                        Consumed(
                            sourceOperations: 1,
                            assemblies: 1)));

        await artifacts.BeginRetirement().WaitAsync(
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public void PlatformLibraryCompletedEvidence_IsResourceFree()
    {
        Type[] resourceFree =
        [
            typeof(PlatformLibraryArtifactProvenance),
            typeof(PlatformLibraryContentSelection),
            typeof(PlatformLibraryViewCorrespondence),
            typeof(PlatformLibraryRealizationValue),
            typeof(PlatformLibraryRealizationReceipt),
            typeof(PlatformPopulationLibraryContentSelection),
            typeof(PlatformPopulationMemberAttribution),
            typeof(PlatformPopulationMember),
            typeof(PlatformPopulationRealizationValue),
            typeof(PlatformPopulationRealizationReceipt),
            typeof(ArtifactAssemblyProjection),
            typeof(PlatformHouseCompletion.Realization),
            typeof(PlatformHouseReceipt),
        ];

        foreach (Type type in resourceFree)
        {
            Assert.Null(
                type.GetCustomAttribute<ResourceOwnershipAttribute>());
            Assert.All(
                type.GetFields(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic),
                field =>
                {
                    Assert.False(
                        typeof(IDisposable).IsAssignableFrom(
                            field.FieldType));
                    Assert.False(
                        typeof(IAsyncDisposable).IsAssignableFrom(
                            field.FieldType));
                    Assert.False(
                        typeof(Delegate).IsAssignableFrom(
                            field.FieldType));
                    Assert.False(
                        typeof(Stream).IsAssignableFrom(
                            field.FieldType));
                });
        }
    }

    static (
        PlatformHouseRequest Request,
        PlatformSourceCapabilityIdentity Reference,
        PlatformSourceCapabilityIdentity Implementation) Request(
            PlatformLibraryIdentity library,
            PlatformViewDemand view,
            CancellationToken cancellationToken = default)
    {
        PlatformSourceCapabilityIdentity reference =
            PlatformSourceCapabilityIdentity.Create("reference-pack");
        PlatformSourceCapabilityIdentity implementation =
            PlatformSourceCapabilityIdentity.Create("runtime-pack");
        var selections = new List<PlatformSourceSelection>();
        if (view is PlatformViewDemand.Reference
            or PlatformViewDemand.ReferenceAndImplementation)
        {
            selections.Add(
                new PlatformSourceSelection(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    [reference]));
        }
        if (view is PlatformViewDemand.Implementation
            or PlatformViewDemand.ReferenceAndImplementation)
        {
            selections.Add(
                new PlatformSourceSelection(
                    PlatformSourceFacet.Implementation,
                    PlatformSourceSelectionMode.Precedence,
                    [implementation]));
        }
        return (
            new PlatformHouseRequest(
                PlatformHouseRequestIdentity.Create("request"),
                new PlatformTargetDemand.Exact(Target()),
                new PlatformHouseRequestOrigin.Standalone(
                    PlatformStandaloneOperationIdentity.Create(
                        "standalone")),
                new PlatformHouseOperation.Realize(
                    new PlatformPopulationDemand.Library(
                        new PlatformLibraryDemand.PlatformLibrary(
                            library)),
                    view),
                new PlatformSourcePlan(
                    PlatformSourcePlanIdentity.Create("sources"),
                    PlatformSourcePolicyGeneration.Create(
                        "generation"),
                    selections),
                Work(),
                cancellationToken),
            reference,
            implementation);
    }

    static (
        PlatformHouseRequest Request,
        PlatformSourceCapabilityIdentity Reference,
        PlatformSourceCapabilityIdentity Implementation) PopulationRequest(
            CancellationToken cancellationToken = default,
            PlatformViewDemand view = PlatformViewDemand.Reference,
            PlatformFamilyTarget? target = null)
    {
        PlatformSourceCapabilityIdentity reference =
            PlatformSourceCapabilityIdentity.Create("reference-pack");
        PlatformSourceCapabilityIdentity implementation =
            PlatformSourceCapabilityIdentity.Create("runtime-pack");
        var selections = new List<PlatformSourceSelection>();
        if (view is PlatformViewDemand.Reference
            or PlatformViewDemand.ReferenceAndImplementation)
        {
            selections.Add(
                new PlatformSourceSelection(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    [reference]));
        }
        if (view is PlatformViewDemand.Implementation
            or PlatformViewDemand.ReferenceAndImplementation)
        {
            selections.Add(
                new PlatformSourceSelection(
                    PlatformSourceFacet.Implementation,
                    PlatformSourceSelectionMode.Precedence,
                    [implementation]));
        }
        return (
            new PlatformHouseRequest(
                PlatformHouseRequestIdentity.Create(
                    "population-request"),
                new PlatformTargetDemand.Exact(target ?? Target()),
                new PlatformHouseRequestOrigin.Standalone(
                    PlatformStandaloneOperationIdentity.Create(
                        "standalone")),
                new PlatformHouseOperation.Realize(
                    new PlatformPopulationDemand.CompletePopulation(),
                    view),
                new PlatformSourcePlan(
                    PlatformSourcePlanIdentity.Create(
                        "population-sources"),
                    PlatformSourcePolicyGeneration.Create(
                        "population-generation"),
                    selections),
                Work(),
                cancellationToken),
            reference,
            implementation);
    }

    static PlatformSourceContribution.Realization Contribution(
        PlatformHouseRequest request,
        PlatformSourceCapabilityIdentity capability,
        PlatformSourceFacet facet) =>
        new(
            facet,
            capability,
            request.Snapshot,
            PlatformSourceGeneration.Create(
                $"{facet}-generation"),
            ((PlatformTargetDemand.Exact)request.Target).Target,
            PlatformSourceCoordinateIdentity.Create(
                $"{facet}-coordinate"),
            ((PlatformHouseOperation.Realize)request.Operation)
                .Population,
            PlatformSourceContributionCompleteness.Authoritative);

    static PlatformSourceContribution.Realization PopulationContribution(
        PlatformHouseRequest request,
        PlatformSourceCapabilityIdentity capability,
        PlatformSourceFacet facet = PlatformSourceFacet.Reference) =>
        new(
            facet,
            capability,
            request.Snapshot,
            PlatformSourceGeneration.Create(
                "reference-population-generation"),
            ((PlatformTargetDemand.Exact)request.Target).Target,
            PlatformSourceCoordinateIdentity.Create(
                "reference-population-coordinate"),
            ((PlatformHouseOperation.Realize)request.Operation)
                .Population,
            PlatformSourceContributionCompleteness.Authoritative);

    static PlatformFamilyTarget Target() =>
        new(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0"));

    static PlatformHouseWorkBudget Work() =>
        new(
            maxSourceOperations: 8,
            maxTargetCandidates: 8,
            maxAssemblies: 64,
            maxXmlDocuments: 8,
            maxPortablePdbs: 8,
            maxSourceDocuments: 16,
            maxBytes: 1024 * 1024,
            maxForwardingHops: 16,
            maxDuration: TimeSpan.FromSeconds(30));

    static PlatformHouseConsumedWork Consumed(
        int sourceOperations = 0,
        int assemblies = 0,
        TimeSpan? elapsed = null) =>
        new(
            sourceOperations,
            targetCandidates: 0,
            assemblies,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: 0,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: elapsed ?? TimeSpan.Zero);

    static LibraryOperationLease Issued(
        LibraryContentOwner owner,
        LibraryReference reference) =>
        Assert.IsType<LibraryOperationLeaseIssueOutcome.Issued>(
                owner.IssueOperationLease(reference))
            .Lease;

    static int ReadByte(
        ArtifactContentLease lease,
        CancellationToken cancellationToken) =>
        Assert.IsType<ArtifactContentAccessOutcome<int>.Accessed>(
                lease.WithContent(
                    static (view, _) => (int)view.Content[0],
                    cancellationToken))
            .Value;

    sealed class ArtifactFixture : IAsyncDisposable
    {
        readonly ArtifactSetSession session;
        readonly IReadOnlyList<ArtifactContentReference> references;
        readonly IReadOnlyList<ArtifactAssemblyProjection> projections;
        readonly List<ArtifactContentLease> contentLeases = [];
        ArtifactQueryLease? queryLease;
        Task? retirement;

        ArtifactFixture(
            ArtifactSetSession session,
            ArtifactQueryLease queryLease,
            IReadOnlyList<ArtifactContentReference> references,
            IReadOnlyList<ArtifactAssemblyProjection> projections)
        {
            this.session = session;
            this.queryLease = queryLease;
            this.references = references;
            this.projections = projections;
        }

        public ArtifactContentReference this[int index] =>
            references[index];

        public ArtifactAssemblyProjection Projection(int index) =>
            projections[index];

        public PlatformLibraryContentSelection Selection(int index) =>
            new(references[index], projections[index]);

        public PlatformPopulationLibraryContentSelection
            PopulationSelection(
                int index,
                PlatformPopulationMemberAttribution? attribution = null) =>
            new(
                references[index],
                projections[index],
                attribution
                    ?? new PlatformPopulationMemberAttribution(
                        Target(),
                        PlatformPopulationMemberRole.Focus));

        public ArtifactContentLease IssueContentLease(int index)
        {
            ArtifactContentLease lease = session.IssueContentLease(
                references[index],
                queryLease
                ?? throw new ObjectDisposedException(
                    nameof(ArtifactFixture)));
            contentLeases.Add(lease);
            return lease;
        }

        public Task BeginRetirement()
        {
            if (retirement is not null)
                return retirement;
            queryLease!.Dispose();
            queryLease = null;
            retirement = session.DisposeAsync().AsTask();
            return retirement;
        }

        public static async Task<ArtifactFixture> CreateAsync(
            params PlatformSourceContribution.Realization[] contributions)
            =>
            await CreatePopulationAsync(
                    contributions.Select(
                            contribution => (
                                contribution,
                                typeof(JsonSerializer).Assembly.Location))
                        .ToArray())
                .ConfigureAwait(false);

        public static async Task<ArtifactFixture> CreatePopulationAsync(
            params (
                PlatformSourceContribution.Realization Contribution,
                string Path)[] inputs)
        {
            var contents = new (
                PlatformSourceContribution.Realization Contribution,
                byte[] Content)[inputs.Length];
            for (int index = 0; index < inputs.Length; index++)
            {
                contents[index] = (
                    inputs[index].Contribution,
                    await File.ReadAllBytesAsync(
                        inputs[index].Path,
                        TestContext.Current.CancellationToken));
            }
            return await CreateCoreAsync(contents).ConfigureAwait(false);
        }

        public static async Task<ArtifactFixture>
            CreatePopulationImagesAsync(
                params (
                    PlatformSourceContribution.Realization Contribution,
                    byte[] Content)[] inputs) =>
            await CreateCoreAsync(inputs).ConfigureAwait(false);

        static async Task<ArtifactFixture> CreateCoreAsync(
            IReadOnlyList<(
                PlatformSourceContribution.Realization Contribution,
                byte[] Content)> inputs)
        {
            CancellationToken cancellationToken =
                TestContext.Current.CancellationToken;
            var session = new ArtifactSetSession();
            try
            {
                for (int index = 0; index < inputs.Count; index++)
                {
                    int ordinal = index;
                    byte[] content = inputs[index].Content;
                    await session.AddRequiredAcquisitionAsync(
                        (scope, _) =>
                        {
                            ArtifactContribution contribution =
                                scope.Register(
                                    new PlatformLibraryArtifactProvenance(
                                        inputs[ordinal].Contribution,
                                        new Provenance(
                                            $"artifact-{ordinal}")),
                                    _ => new MemoryStream(
                                        content,
                                        writable: false));
                            return ValueTask.FromResult<
                                ArtifactAcquisitionOutcome>(
                                    new ArtifactAcquisitionOutcome
                                        .Acquired(
                                            [contribution],
                                            ArtifactAcquisitionLeases
                                                .None));
                        },
                        cancellationToken: cancellationToken);
                }

                var projections =
                    new Dictionary<
                        ArtifactIdentity,
                        ArtifactAssemblyProjection>();
                Assert.IsType<ArtifactSetPublicationOutcome.Published>(
                    await session.SealWithProjectionAsync(
                        (view, token) =>
                        {
                            ArtifactAssemblyProjectionOutcome outcome =
                                ArtifactAssemblyInspection.Project(
                                    view,
                                    token);
                            projections.Add(
                                view.Artifact,
                                Assert.IsType<
                                    ArtifactAssemblyProjectionOutcome
                                        .Projected>(outcome)
                                    .Value);
                            return null;
                        },
                        cancellationToken));
                ArtifactQueryAuthorization authorization =
                    session.CreateQueryAuthorization();
                ArtifactQueryLease queryLease =
                    session.IssueLease(authorization);
                IReadOnlyList<ArtifactContentReference> references =
                    session.GetCatalog(queryLease)
                        .Select(
                            descriptor => session.GetContentReference(
                                descriptor.Identity,
                                queryLease))
                        .ToArray();
                return new ArtifactFixture(
                    session,
                    queryLease,
                    references,
                    references.Select(
                            reference =>
                                projections[reference.Artifact])
                        .ToArray());
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (ArtifactContentLease lease in contentLeases)
                lease.Dispose();
            await BeginRetirement();
        }
    }

    sealed record Provenance(string Name) : IArtifactProvenance;
}
