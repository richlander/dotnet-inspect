using System.Collections.Immutable;

using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class NavigationDescendantLensEvaluationTests
{
    [Fact]
    public async Task LibraryToType_AppliesExactDestinationPair()
    {
        await using SnapshotFixture fixture = await CreateFixtureAsync();
        NavigationTypeDescriptor type = fixture.Snapshot.Types[0];
        NavigationWorkspaceSnapshot source =
            fixture.WithActive(type.Row.Subject.Library);
        var request = new DescendantSubjectLensRequest(
            source.ActiveSubject,
            new NavigationLensIdentity(
                type.Row.Subject,
                new ViewFacetId("type.compare")));

        NavigationDescendantLensResult.Applied applied =
            Assert.IsType<NavigationDescendantLensResult.Applied>(
                NavigationDescendantLensEvaluation.Evaluate(
                    source,
                    request,
                    fixture.Registry,
                    fixture.AllAvailable));

        Assert.Same(request, applied.Request);
        Assert.Same(type.Row.Subject, applied.Snapshot.ActiveSubject);
        Assert.Equal(
            request.Destination,
            applied.Snapshot.LensOutcome.EffectiveLens);
        Assert.Same(
            type.Row.Subject.Library,
            applied.Snapshot.TypeInventoryLibraryContext);
        Assert.Null(
            typeof(NavigationWorkspaceSnapshot).GetProperty("Action"));
    }

    [Fact]
    public async Task AllLibrariesType_RetainsExactDefiningLibrary()
    {
        await using SnapshotFixture fixture = await CreateFixtureAsync();
        NavigationLibraryDescriptor aggregate =
            Assert.Single(
                fixture.Snapshot.Libraries,
                library => library.Subject
                    is StructuralSubjectIdentity.AllLibrariesSubject);
        NavigationTypeDescriptor selected = fixture.Snapshot.Types[1];
        Assert.Equal(
            fixture.Snapshot.Types[0].Row.Subject.Identity.Type,
            selected.Row.Subject.Identity.Type);
        Assert.NotEqual(
            fixture.Snapshot.Types[0].Row.Subject.Library,
            selected.Row.Subject.Library);
        NavigationWorkspaceSnapshot source =
            fixture.WithActive(aggregate.Subject);
        var request = new DescendantSubjectLensRequest(
            source.ActiveSubject,
            new NavigationLensIdentity(
                selected.Row.Subject,
                new ViewFacetId("type.compare")));

        NavigationDescendantLensResult.Applied applied =
            Assert.IsType<NavigationDescendantLensResult.Applied>(
                NavigationDescendantLensEvaluation.Evaluate(
                    source,
                    request,
                    fixture.Registry,
                    fixture.AllAvailable));

        Assert.Same(selected.Row.Subject, applied.Snapshot.ActiveSubject);
        Assert.Same(
            selected.Row.Subject.Library,
            applied.Snapshot.RetainedContext!.Library);
        Assert.Same(
            selected.Row.Subject.Library,
            applied.Snapshot.TypeInventoryLibraryContext);
    }

    [Fact]
    public async Task TypeToMember_AppliesExactDestinationPair()
    {
        await using SnapshotFixture fixture = await CreateFixtureAsync();
        NavigationTypeDescriptor type = fixture.Snapshot.Types[0];
        NavigationMemberDescriptor member = Assert.Single(
            fixture.Snapshot.Members,
            candidate => candidate.Row.Subject.DeclaringType
                == type.Row.Subject);
        NavigationWorkspaceSnapshot source =
            fixture.WithActive(type.Row.Subject);
        var request = new DescendantSubjectLensRequest(
            source.ActiveSubject,
            new NavigationLensIdentity(
                member.Row.Subject,
                new ViewFacetId("member.compare")));

        NavigationDescendantLensResult.Applied applied =
            Assert.IsType<NavigationDescendantLensResult.Applied>(
                NavigationDescendantLensEvaluation.Evaluate(
                    source,
                    request,
                    fixture.Registry,
                    fixture.AllAvailable));

        Assert.Same(member.Row.Subject, applied.Snapshot.ActiveSubject);
        Assert.Equal(
            request.Destination,
            applied.Snapshot.LensOutcome.EffectiveLens);
        Assert.Same(
            type.Row.Subject,
            applied.Snapshot.RetainedContext!.Type);
    }

    [Fact]
    public async Task InvalidAncestry_RejectsBeforeRegistryResolution()
    {
        await using SnapshotFixture fixture = await CreateFixtureAsync();
        NavigationTypeDescriptor type = fixture.Snapshot.Types[0];
        NavigationTypeDescriptor sibling = fixture.Snapshot.Types[1];
        NavigationWorkspaceSnapshot source =
            fixture.WithActive(type.Row.Subject.Library);
        ViewFacetRegistry throwingRegistry =
            Registry(
                Descriptor("type.compare", StructuralSubjectKind.Type),
                _ => throw new InvalidOperationException(
                    "Registry applicability must not run."));

        var sourceMismatch = new DescendantSubjectLensRequest(
            sibling.Row.Subject.Library,
            new NavigationLensIdentity(
                type.Row.Subject,
                new ViewFacetId("type.compare")));
        Assert.Equal(
            NavigationDescendantLensRejectionKind.SourceMismatch,
            Reject(source, sourceMismatch).Validation);

        await using SnapshotFixture foreign = await CreateFixtureAsync(
            "Navigation.Foreign");
        var foreignOccurrence = new DescendantSubjectLensRequest(
            source.ActiveSubject,
            new NavigationLensIdentity(
                foreign.Snapshot.Types[0].Row.Subject,
                new ViewFacetId("type.compare")));
        Assert.Equal(
            NavigationDescendantLensRejectionKind.ForeignWorkspace,
            Reject(source, foreignOccurrence).Validation);

        StructuralSubjectTestData.PackageContext stalePackage =
            StructuralSubjectTestData.Package(
                fixture.Snapshot.Inventory!.Package.Coordinate,
                fixture.Snapshot.Workspace.Identity);
        StructuralSubjectIdentity.LibrarySubject staleLibrary =
            StructuralSubjectIdentity.ForLibrary(
                stalePackage.Subject,
                fixture.Package.Libraries[0].Library);
        StructuralSubjectIdentity.TypeSubject staleType =
            StructuralSubjectIdentity.ForType(
                staleLibrary,
                type.Row.Subject.Identity.Type);
        var staleOccurrence = new DescendantSubjectLensRequest(
            source.ActiveSubject,
            new NavigationLensIdentity(
                staleType,
                new ViewFacetId("type.compare")));
        Assert.Equal(
            NavigationDescendantLensRejectionKind.ForeignOccurrence,
            Reject(source, staleOccurrence).Validation);

        var nonDescendant = new DescendantSubjectLensRequest(
            source.ActiveSubject,
            new NavigationLensIdentity(
                sibling.Row.Subject,
                new ViewFacetId("type.compare")));
        Assert.Equal(
            NavigationDescendantLensRejectionKind.NonDescendant,
            Reject(source, nonDescendant).Validation);

        NavigationDescendantLensResult.Rejected Reject(
            NavigationWorkspaceSnapshot snapshot,
            DescendantSubjectLensRequest request) =>
            Assert.IsType<NavigationDescendantLensResult.Rejected>(
                NavigationDescendantLensEvaluation.Evaluate(
                    snapshot,
                    request,
                    throwingRegistry,
                    ThrowingFacts.Instance));
    }

    [Fact]
    public async Task NonAvailableDestinationLenses_InstallNeitherRequestedHalf()
    {
        await using SnapshotFixture fixture = await CreateFixtureAsync();
        NavigationTypeDescriptor type = fixture.Snapshot.Types[0];
        NavigationWorkspaceSnapshot source =
            fixture.WithActive(type.Row.Subject.Library);
        ViewFacetUnavailableReason unavailableReason =
            ViewFacetUnavailableReason.CapabilityAbsent(
                "Compare is unavailable.");
        var diagnostic = new TestDiagnosticEvidence("failed");
        ViewFacetDescriptor available =
            Descriptor("type.available", StructuralSubjectKind.Type, 100);
        ViewFacetDescriptor unavailable =
            Descriptor("type.unavailable", StructuralSubjectKind.Type, 200);
        ViewFacetDescriptor failed =
            Descriptor("type.failed", StructuralSubjectKind.Type, 300);
        ViewFacetDescriptor inapplicable =
            Descriptor(
                "workspace.inapplicable",
                StructuralSubjectKind.Workspace,
                100);
        ViewFacetRegistry registry = Registry(
            Registration(available),
            Registration(unavailable),
            Registration(failed),
            Registration(inapplicable));
        var facts = new ViewFacetAvailabilitySnapshot(
        [
            new(
                available.Id,
                ViewFacetAvailability.Available.Instance),
            new(
                unavailable.Id,
                new ViewFacetAvailability.Unavailable(
                    unavailableReason)),
            new(
                failed.Id,
                new ViewFacetAvailability.Failed(
                    "Compare failed.",
                    diagnostic)),
        ]);

        foreach ((string Facet, Type ResultType) item in new[]
        {
            ("type.unavailable", typeof(NavigationDescendantLensResult.Unavailable)),
            ("type.failed", typeof(NavigationDescendantLensResult.Failed)),
            ("workspace.inapplicable", typeof(NavigationDescendantLensResult.Rejected)),
            ("type.unknown", typeof(NavigationDescendantLensResult.Rejected)),
        })
        {
            var request = new DescendantSubjectLensRequest(
                source.ActiveSubject,
                new NavigationLensIdentity(
                    type.Row.Subject,
                    new ViewFacetId(item.Facet)));
            NavigationDescendantLensResult result =
                NavigationDescendantLensEvaluation.Evaluate(
                    source,
                    request,
                    registry,
                    facts);

            Assert.IsType(item.ResultType, result);
            Assert.Same(source, result.Snapshot);
            Assert.Same(source.ActiveSubject, result.Snapshot.ActiveSubject);
            Assert.NotEqual(
                request.Destination,
                result.Snapshot.LensOutcome.EffectiveLens);
        }
    }

    static async Task<SnapshotFixture> CreateFixtureAsync(
        string packageId = "Navigation.Descendant")
    {
        var workspace = InspectionWorkspace.CreateAsynchronous();
        PackageRootBinding binding =
            NavigationSnapshotTestData.BindingWithAssemblyImages(
                packageId,
                "net11.0",
                (
                    "DotnetInspector.Queries.Tests",
                    await File.ReadAllBytesAsync(
                        typeof(NavigationDescendantLensEvaluationTests)
                            .Assembly.Location,
                        TestContext.Current.CancellationToken)),
                (
                    "ILInspector.Metadata",
                    await File.ReadAllBytesAsync(
                        typeof(ApiType).Assembly.Location,
                        TestContext.Current.CancellationToken)));
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(
                workspace,
                binding);
        PackageCompileAsset firstAsset =
            binding.Root.AssetSelection.Assets[0];
        PackageCompileAsset secondAsset =
            binding.Root.AssetSelection.Assets[1];
        WorkspaceContextMember firstLibrary =
            Library(
                binding.Coordinate,
                Path.GetFileNameWithoutExtension(
                    firstAsset.AssemblyName));
        WorkspaceContextMember secondLibrary =
            Library(
                binding.Coordinate,
                Path.GetFileNameWithoutExtension(
                    secondAsset.AssemblyName));
        ApiType firstType = NavigationSnapshotTestData.Type(
            "Widget",
            NavigationSnapshotTestData.Member("Run"));
        ApiType secondType = NavigationSnapshotTestData.Type(
            "Widget",
            NavigationSnapshotTestData.Member("Stop"));
        var package = new NavigationPackageEvaluation(
            scope.Packages[0],
            binding,
            [
                new(
                    binding.Coordinate,
                    new PackageAssemblyRoleParticipant(
                        binding.Root.Identity,
                        firstAsset,
                        firstLibrary.Participant)),
                new(
                    binding.Coordinate,
                    new PackageAssemblyRoleParticipant(
                        binding.Root.Identity,
                        secondAsset,
                        secondLibrary.Participant)),
            ],
            Surface(
                Available(firstLibrary, firstType),
                Available(secondLibrary, secondType)));
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        ViewFacetAvailabilitySnapshot allAvailable =
            NavigationSnapshotTestData.AllAvailable(registry);
        NavigationWorkspaceSnapshot snapshot =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = scope,
                    Package = package,
                },
                registry,
                allAvailable);
        return new(
            workspace,
            scope,
            package,
            snapshot,
            registry,
            allAvailable);
    }

    static AssemblyContextApiSurfaceResult Surface(
        params AssemblyContextEntry<AssemblyApiSurface>[] entries) =>
        new(
            new AssemblyContextResult<AssemblyApiSurface>([.. entries]),
            [],
            Truncation: null);

    static AssemblyContextEntry<AssemblyApiSurface> Available(
        WorkspaceContextMember library,
        params ApiType[] types)
    {
        var surface = new ApiSurface
        {
            Types = [.. types],
            InspectionFailures = [],
        };
        return new AssemblyContextEntry<AssemblyApiSurface>.Available(
            new AssemblyContextSubject(library.Participant.Assembly),
            new AssemblyApiSurface(surface, []));
    }

    static WorkspaceContextMember Library(
        RealizedMemberCoordinate.Package coordinate,
        string name)
    {
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    name,
                    new Version(1, 0, 0, 0),
                    Culture: null,
                    PublicKeyToken: null),
                path: null,
                () => new MemoryStream([0], writable: false),
                AssemblyResolutionProvenance.Package(
                    coordinate.PackageId,
                    coordinate.Version,
                    coordinate.Framework,
                    coordinate.RuntimeIdentifier));
        return new WorkspaceContextMember(
            WorkspaceMemberCoordinate.Package(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                coordinate.RuntimeIdentifier),
            coordinate,
            new AssemblyContextParticipant(
                assembly,
                NoResolverAssemblyBindingPolicy.Instance));
    }

    static ViewFacetRegistry Registry(
        ViewFacetDescriptor descriptor,
        Func<ViewFacetTarget, bool> applies) =>
        Registry(Registration(descriptor, applies));

    static ViewFacetRegistry Registry(
        params ViewFacetRegistration[] registrations)
    {
        ViewFacetRegistration[] values = [.. registrations];
        return new(
            values,
            values.Select(registration =>
                Assert.IsType<ViewFacetRegistration.Active>(
                    registration).Binding));
    }

    static ViewFacetRegistration.Active Registration(
        ViewFacetDescriptor descriptor,
        Func<ViewFacetTarget, bool>? applies = null) =>
        new(
            descriptor,
            descriptor.Summary,
            applies ?? (target => target.Subject.Kind == descriptor.Kind),
            new ViewFacetExecutionBinding(descriptor.Id, descriptor),
            (_, facts) => facts.Get(descriptor.Id));

    static ViewFacetDescriptor Descriptor(
        string id,
        StructuralSubjectKind kind,
        int order = 100) =>
        new(
            new ViewFacetId(id),
            kind,
            id,
            $"Summary for {id}.",
            order);

    sealed record SnapshotFixture(
        InspectionWorkspace Workspace,
        WorkspaceScopeSnapshot Scope,
        NavigationPackageEvaluation Package,
        NavigationWorkspaceSnapshot Snapshot,
        ViewFacetRegistry Registry,
        ViewFacetAvailabilitySnapshot AllAvailable)
        : IAsyncDisposable
    {
        public NavigationWorkspaceSnapshot WithActive(
            StructuralSubjectIdentity subject) =>
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = Scope,
                    Package = Package,
                    ActiveSubject = subject,
                },
                Registry,
                AllAvailable);

        public ValueTask DisposeAsync() => Workspace.DisposeAsync();
    }

    sealed record TestDiagnosticEvidence(string Value)
        : IViewFacetDiagnosticEvidence;

    sealed class ThrowingFacts : IViewFacetAvailabilityFacts
    {
        public static ThrowingFacts Instance { get; } = new();

        public ViewFacetAvailability Get(ViewFacetId id) =>
            throw new InvalidOperationException(
                $"Facts must not be read for '{id.Value}'.");
    }
}
