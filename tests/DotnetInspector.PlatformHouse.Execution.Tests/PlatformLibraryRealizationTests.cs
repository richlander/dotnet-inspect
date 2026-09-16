using System.Reflection;
using DotnetInspector.Libraries;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using Inspector.Resources;

namespace DotnetInspector.PlatformHouse.Tests;

public class PlatformLibraryRealizationTests
{
    [Fact]
    public async Task
        ExactLibraryRealizer_TransfersReferenceAndImplementationOwner()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([0x11], [0x22]);
        PlatformLibraryIdentity library =
            PlatformLibraryIdentityAuthority.Create("runtime-catalog")
                .Issue("System.Text.Json");
        var request = Request(
            library,
            PlatformViewDemand.ReferenceAndImplementation,
            cancellationToken);
        ManagedMetadataIdentity.Assembly identity =
            Identity("System.Text.Json");
        PlatformLibraryContentSelection reference = Selection(
            request.Request,
            request.Reference,
            PlatformSourceFacet.Reference,
            artifacts[0],
            identity);
        PlatformLibraryContentSelection implementation = Selection(
            request.Request,
            request.Implementation,
            PlatformSourceFacet.Implementation,
            artifacts[1],
            identity);
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
            (0x11, 0x22),
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
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([0x31]);
        PlatformLibraryIdentity library =
            PlatformLibraryIdentityAuthority.Create("reference-catalog")
                .Issue("System.Collections");
        var request = Request(
            library,
            PlatformViewDemand.Reference,
            cancellationToken);
        PlatformLibraryContentSelection reference = Selection(
            request.Request,
            request.Reference,
            PlatformSourceFacet.Reference,
            artifacts[0],
            Identity("System.Collections"));

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
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([0x41]);
        PlatformLibraryIdentity library =
            PlatformLibraryIdentityAuthority.Create("runtime-catalog")
                .Issue("System.Memory");
        var request = Request(
            library,
            PlatformViewDemand.Implementation,
            cancellationToken);
        PlatformLibraryContentSelection implementation = Selection(
            request.Request,
            request.Implementation,
            PlatformSourceFacet.Implementation,
            artifacts[0],
            Identity("System.Memory"));
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
        Assert.Equal(0x41, ReadByte(lease, cancellationToken));
        lease.Dispose();
    }

    [Fact]
    public async Task
        ExactLibraryRealizer_AssignsBothRolesWithDeclarationEvidence()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([0x51]);
        PlatformLibraryIdentity library =
            PlatformLibraryIdentityAuthority.Create("runtime-catalog")
                .Issue("System.Memory");
        var request = Request(
            library,
            PlatformViewDemand.Implementation,
            cancellationToken);
        PlatformLibraryContentSelection implementation = Selection(
            request.Request,
            request.Implementation,
            PlatformSourceFacet.Implementation,
            artifacts[0],
            Identity("System.Memory"));
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
        await using ArtifactFixture selectedArtifacts =
            await ArtifactFixture.CreateAsync([0x61]);
        await using ArtifactFixture foreignArtifacts =
            await ArtifactFixture.CreateAsync([0x62]);
        PlatformLibraryIdentity library =
            PlatformLibraryIdentityAuthority.Create("reference-catalog")
                .Issue("System.Runtime");
        var request = Request(
            library,
            PlatformViewDemand.Reference,
            cancellationToken);
        PlatformLibraryContentSelection reference = Selection(
            request.Request,
            request.Reference,
            PlatformSourceFacet.Reference,
            selectedArtifacts[0],
            Identity("System.Runtime"));
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
        Assert.Equal(0x62, ReadByte(foreignLease, cancellationToken));
        foreignLease.Dispose();
    }

    [Fact]
    public async Task
        ExactLibraryRealizer_RejectsUnauthorizedSourceWithoutAcceptingLease()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([0x63]);
        PlatformLibraryIdentity library =
            PlatformLibraryIdentityAuthority.Create("reference-catalog")
                .Issue("System.Runtime");
        var request = Request(
            library,
            PlatformViewDemand.Reference,
            cancellationToken);
        PlatformLibraryContentSelection reference = Selection(
            request.Request,
            PlatformSourceCapabilityIdentity.Create(
                "unauthorized-reference"),
            PlatformSourceFacet.Reference,
            artifacts[0],
            Identity("System.Runtime"));
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
        Assert.Equal(0x63, ReadByte(lease, cancellationToken));
        lease.Dispose();
    }

    [Fact]
    public async Task
        ExactLibraryRealizer_CancellationPrecedesOwnershipAcceptance()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync([0x71]);
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
        PlatformLibraryContentSelection reference = Selection(
            request.Request,
            request.Reference,
            PlatformSourceFacet.Reference,
            artifacts[0],
            Identity("System.Runtime"));
        ArtifactContentLease lease = artifacts.IssueContentLease(0);

        Assert.Throws<OperationCanceledException>(
            () => PlatformHouseLibraryRealizer.RealizeReference(
                request.Request,
                reference,
                lease,
                Consumed(assemblies: 1)));
        Assert.Equal(0x71, ReadByte(lease, CancellationToken.None));
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
    public void PlatformLibraryCompletedEvidence_IsResourceFree()
    {
        Type[] resourceFree =
        [
            typeof(PlatformLibraryContentSelection),
            typeof(PlatformLibraryViewCorrespondence),
            typeof(PlatformLibraryRealizationValue),
            typeof(PlatformLibraryRealizationReceipt),
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

    static PlatformLibraryContentSelection Selection(
        PlatformHouseRequest request,
        PlatformSourceCapabilityIdentity capability,
        PlatformSourceFacet facet,
        ArtifactContentReference content,
        ManagedMetadataIdentity.Assembly identity) =>
        new(
            new PlatformSourceContribution.Realization(
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
                PlatformSourceContributionCompleteness.Authoritative),
            content,
            identity);

    static ManagedMetadataIdentity.Assembly Identity(string name) =>
        new(
            new AssemblyReferenceIdentity(
                name,
                new Version(11, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null));

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
        int assemblies = 0,
        TimeSpan? elapsed = null) =>
        new(
            sourceOperations: 0,
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
        readonly List<ArtifactContentLease> contentLeases = [];
        ArtifactQueryLease? queryLease;
        Task? retirement;

        ArtifactFixture(
            ArtifactSetSession session,
            ArtifactQueryLease queryLease,
            IReadOnlyList<ArtifactContentReference> references)
        {
            this.session = session;
            this.queryLease = queryLease;
            this.references = references;
        }

        public ArtifactContentReference this[int index] =>
            references[index];

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
            params byte[][] contents)
        {
            CancellationToken cancellationToken =
                TestContext.Current.CancellationToken;
            var session = new ArtifactSetSession();
            try
            {
                for (int index = 0; index < contents.Length; index++)
                {
                    int ordinal = index;
                    byte[] content = contents[index];
                    await session.AddRequiredAcquisitionAsync(
                        (scope, _) =>
                        {
                            ArtifactContribution contribution =
                                scope.Register(
                                    new Provenance(
                                        $"artifact-{ordinal}"),
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

                Assert.IsType<ArtifactSetPublicationOutcome.Published>(
                    await session.SealAsync(cancellationToken));
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
                    references);
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
