using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

using Descriptor = DotnetInspect.Web.BrowserSpotlightDestinationDescriptor<
    DotnetInspect.Web.TestPackageAction,
    DotnetInspect.Web.TestNavigationAction,
    DotnetInspect.Web.TestPlatformAction,
    DotnetInspect.Web.TestLibraryIntent>;
using Destination = DotnetInspect.Web.BrowserSpotlightDestination<
    DotnetInspect.Web.TestPackageAction,
    DotnetInspect.Web.TestNavigationAction,
    DotnetInspect.Web.TestPlatformAction,
    DotnetInspect.Web.TestLibraryIntent>;
using Plan = DotnetInspect.Web.BrowserSpotlightDestinationActivationPlan<
    DotnetInspect.Web.TestPackageAction,
    DotnetInspect.Web.TestNavigationAction,
    DotnetInspect.Web.TestPlatformAction,
    DotnetInspect.Web.TestLibraryIntent>;
using Result = DotnetInspect.Web.BrowserSpotlightDestinationProjectionResult<
    DotnetInspect.Web.TestPackageAction,
    DotnetInspect.Web.TestNavigationAction,
    DotnetInspect.Web.TestPlatformAction,
    DotnetInspect.Web.TestLibraryIntent>;

namespace DotnetInspect.Web;

public sealed class BrowserSpotlightDestinationProjectionTests
{
    private const string PackageVersion = "11.0.0-preview.7.26381.103";

    [Fact]
    public async Task CurrentSubjectRetainsTheExactNavigationAction()
    {
        await using var workspace = new InspectionWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        StructuralSubjectIdentity.WorkspaceSubject subject =
            StructuralSubjectIdentity.ForWorkspace(
                basis.Scope.Revision.Workspace);
        var action = new TestNavigationAction("workspace");

        Descriptor descriptor = Projected(
            basis,
            new Destination.Current(subject, action));

        var plan = Assert.IsType<Plan.NavigateCurrent>(descriptor.Plan);
        Assert.Same(subject, plan.Subject);
        Assert.Same(action, plan.Action);
        Assert.Empty(descriptor.Coverage);
        Assert.Equal(1, descriptor.Basis.ResultGeneration);
        Assert.Same(basis.Scope, descriptor.Basis.Scope);
        Assert.Same(basis.Registrations, descriptor.Basis.Registrations);
        Assert.Equal(
            BrowserSpotlightWorkspaceRelationship.Admitted,
            descriptor.WorkspaceRelationship);
        Assert.Equal(
            BrowserSpotlightWorkspaceDisposition.PreserveCurrent,
            descriptor.WorkspaceDisposition);
        Assert.Equal(
            BrowserSpotlightDestinationHome.Workspace,
            descriptor.SourceHome);
        Assert.IsType<BrowserSpotlightDestinationAvailability.Available>(
            descriptor.Availability);
    }

    [Fact]
    public async Task CurrentPackageAndLibraryRetainOrderedCoverageWitnesses()
    {
        ExactLibrarySourceCoordinate.Package library = PackageLibrary();
        var ecosystem = new WorkspaceEcosystemRegistrationDeclaration(
            WorkspaceEcosystemRegistrationId.Create("ecosystem.spotlight"),
            namespaceRoots: [],
            corePackages: [],
            populations:
            [
                new WorkspaceEcosystemPopulationDeclaration.PackagePrefix(
                    new PackagePrefixDeclaration("System.Text.")),
            ]);
        await using var workspace = new InspectionWorkspace(
        [
            new WorkspaceRegistration.PackagePrefix(
                new PackagePrefixDeclaration("System.")),
            new WorkspaceRegistration.Ecosystem(ecosystem),
            new WorkspaceRegistration.ExactLibrary(library),
        ]);
        WorkspaceScopeSnapshot scope = await ReplaceScope(
            workspace,
            library.PackageCoordinate,
            PackageAssemblyPath());
        BrowserSpotlightActivationBasis basis = Basis(workspace, scope);
        WorkspacePackageOccurrence occurrence =
            Assert.Single(scope.Revision.Packages);
        StructuralSubjectIdentity.WorkspaceSubject workspaceSubject =
            StructuralSubjectIdentity.ForWorkspace(
                basis.Scope.Revision.Workspace);
        StructuralSubjectIdentity.PackageSubject packageSubject =
            StructuralSubjectIdentity.ForPackage(
                workspaceSubject,
                occurrence);
        StructuralSubjectIdentity.LibrarySubject librarySubject =
            StructuralSubjectIdentity.ForLibrary(
                packageSubject,
                new WorkspaceContextMember(
                    WorkspaceMemberCoordinate.Package(
                        occurrence.Package.Coordinate.PackageId,
                        occurrence.Package.Coordinate.Version,
                        occurrence.Package.Coordinate.Framework,
                        occurrence.Package.Coordinate.RuntimeIdentifier),
                    occurrence.Package.Coordinate,
                    Participant(library)));

        Descriptor package = Projected(
            basis,
            new Destination.Current(
                packageSubject,
                new TestNavigationAction("package")));
        Descriptor packageLibrary = Projected(
            basis,
            new Destination.Current(
                librarySubject,
                new TestNavigationAction("library")));

        Assert.IsType<Plan.NavigateCurrent>(package.Plan);
        Assert.Collection(
            package.Coverage,
            witness => Assert.Equal(0, witness.RegistrationIndex),
            witness =>
            {
                Assert.Equal(1, witness.RegistrationIndex);
                Assert.Equal(0, witness.PopulationIndex);
            });
        Assert.All(
            package.Coverage,
            witness => Assert.Same(
                packageSubject,
                Assert.IsType<BrowserSpotlightCoverageTarget<
                    TestPackageAction>.Admitted>(
                        witness.Target).Subject));

        Assert.IsType<Plan.NavigateCurrent>(packageLibrary.Plan);
        Assert.Collection(
            packageLibrary.Coverage,
            witness => Assert.Equal(0, witness.RegistrationIndex),
            witness =>
            {
                Assert.Equal(1, witness.RegistrationIndex);
                Assert.Equal(0, witness.PopulationIndex);
            },
            witness => Assert.Equal(2, witness.RegistrationIndex));
        Assert.All(
            packageLibrary.Coverage,
            witness => Assert.Same(
                librarySubject,
                Assert.IsType<BrowserSpotlightCoverageTarget<
                    TestPackageAction>.Admitted>(
                        witness.Target).Subject));
    }

    [Fact]
    public async Task CurrentPackageOccurrenceActivatesAnUnrealizedLibraryInPlace()
    {
        ExactLibrarySourceCoordinate.Package library = PackageLibrary();
        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot scope = await ReplaceScope(
            workspace,
            library.PackageCoordinate,
            PackageAssemblyPath());
        BrowserSpotlightActivationBasis basis = Basis(workspace, scope);
        WorkspacePackageOccurrence occurrence =
            Assert.Single(scope.Revision.Packages);
        var intent = new TestLibraryIntent("System.Text.Json");

        Descriptor descriptor = Projected(
            basis,
            new Destination.CurrentPackageLibrary(
                library,
                PackageRequest(
                    library.PackageCoordinate,
                    occurrence.Package.Coordinate),
                intent,
                occurrence));

        var plan = Assert.IsType<Plan.ActivateCurrentPackageLibrary>(
            descriptor.Plan);
        Assert.Same(occurrence, plan.Package);
        Assert.Same(intent, plan.Intent);
        Assert.Equal(
            BrowserSpotlightWorkspaceDisposition.PreserveCurrent,
            descriptor.WorkspaceDisposition);
    }

    [Fact]
    public async Task ExactLibraryRegistrationCoversOnlyItsExactLibrary()
    {
        ExactLibrarySourceCoordinate.Package library = PackageLibrary();
        await using var workspace = new InspectionWorkspace(
        [
            new WorkspaceRegistration.ExactLibrary(library),
        ]);
        BrowserSpotlightActivationBasis basis = await Basis(workspace);

        Descriptor package = Projected(
            basis,
            new Destination.Package(
                PackageRequest(library.PackageCoordinate)));
        Descriptor exactLibrary = Projected(
            basis,
            new Destination.PackageLibrary(
                library,
                PackageRequest(library.PackageCoordinate),
                new TestLibraryIntent("exact")));

        Assert.IsType<Plan.RestoreExternalPackageWorkspace>(package.Plan);
        Assert.Empty(package.Coverage);
        Assert.IsType<Plan.AddCurrentPackage>(exactLibrary.Plan);
        Assert.Single(exactLibrary.Coverage);
        Assert.IsType<WorkspaceRegistration.ExactLibrary>(
            exactLibrary.Coverage[0].Registration);
        var target = Assert.IsType<
            BrowserSpotlightCoverageTarget<
                TestPackageAction>.PackageLibrary>(
                exactLibrary.Coverage[0].Target);
        Assert.Equal(
            library,
            target.Coordinate);
        Assert.Equal(
            library.PackageCoordinate,
            target.Request.Coordinate);
        Assert.Equal(
            BrowserSpotlightWorkspaceRelationship.RegistrationCovered,
            exactLibrary.WorkspaceRelationship);
    }

    [Fact]
    public async Task PrefixAndEcosystemCoverageRetainsEveryOrderedWitness()
    {
        ExactLibrarySourceCoordinate.Package library = PackageLibrary();
        var ecosystem = new WorkspaceEcosystemRegistrationDeclaration(
            WorkspaceEcosystemRegistrationId.Create("ecosystem.spotlight"),
            namespaceRoots: [],
            corePackages: [],
            populations:
            [
                new WorkspaceEcosystemPopulationDeclaration.PackagePrefix(
                    new PackagePrefixDeclaration("System.Text.")),
                new WorkspaceEcosystemPopulationDeclaration.ExactLibrary(
                    library),
            ]);
        await using var workspace = new InspectionWorkspace(
        [
            new WorkspaceRegistration.PackagePrefix(
                new PackagePrefixDeclaration("System.")),
            new WorkspaceRegistration.Ecosystem(ecosystem),
            new WorkspaceRegistration.ExactLibrary(library),
        ]);
        BrowserSpotlightActivationBasis basis = await Basis(workspace);

        Descriptor descriptor = Projected(
            basis,
            new Destination.PackageLibrary(
                library,
                PackageRequest(library.PackageCoordinate),
                new TestLibraryIntent("all-witnesses")));

        Assert.IsType<Plan.AddCurrentPackage>(descriptor.Plan);
        Assert.Collection(
            descriptor.Coverage,
            witness =>
            {
                Assert.Equal(0, witness.RegistrationIndex);
                Assert.Null(witness.PopulationIndex);
                Assert.IsType<WorkspaceRegistration.PackagePrefix>(
                    witness.Registration);
            },
            witness =>
            {
                Assert.Equal(1, witness.RegistrationIndex);
                Assert.Equal(0, witness.PopulationIndex);
                Assert.IsType<
                    WorkspaceEcosystemPopulationDeclaration.PackagePrefix>(
                        witness.Population);
            },
            witness =>
            {
                Assert.Equal(1, witness.RegistrationIndex);
                Assert.Equal(1, witness.PopulationIndex);
                Assert.IsType<
                    WorkspaceEcosystemPopulationDeclaration.ExactLibrary>(
                        witness.Population);
            },
            witness =>
            {
                Assert.Equal(2, witness.RegistrationIndex);
                Assert.Null(witness.PopulationIndex);
                Assert.IsType<WorkspaceRegistration.ExactLibrary>(
                    witness.Registration);
            });
        Assert.All(
            descriptor.Coverage,
            witness => Assert.Same(basis, witness.Basis));
    }

    [Fact]
    public async Task PackagePrefixCoversPackageWithoutChoosingAnExactLibrary()
    {
        PackageSourceCoordinate package =
            PackageSourceCoordinate.Create("System.Text.Json", PackageVersion);
        await using var workspace = new InspectionWorkspace(
        [
            new WorkspaceRegistration.PackagePrefix(
                new PackagePrefixDeclaration("System.Text.")),
        ]);
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        BrowserSpotlightPackageRequest<TestPackageAction> request =
            PackageRequest(package);

        Descriptor descriptor = Projected(
            basis,
            new Destination.Package(request));

        var plan = Assert.IsType<Plan.AddCurrentPackage>(descriptor.Plan);
        Assert.Same(request, plan.Package);
        Assert.Null(plan.LibraryIntent);
        Assert.Single(descriptor.Coverage);
    }

    [Fact]
    public async Task PackageAndPlatformLibrariesWithTheSameAssemblyNameStayDistinct()
    {
        ExactLibrarySourceCoordinate.Package packageLibrary = PackageLibrary();
        ExactLibrarySourceCoordinate.Platform platformLibrary =
            PlatformLibrary();
        Assert.Equal(
            packageLibrary.LibraryIdentity.Identity.Name,
            platformLibrary.LibraryIdentity.Identity.Name);
        var platformRegistration = new WorkspaceRegistration.Ecosystem(
            new WorkspaceEcosystemRegistrationDeclaration(
                WorkspaceEcosystemRegistrationId.Create(
                    "ecosystem.runtime"),
                namespaceRoots: [],
                corePackages: [],
                populations:
                [
                    new WorkspaceEcosystemPopulationDeclaration.Platform(
                        DotNetRuntimePopulation()),
                ]));
        await using var workspace = new InspectionWorkspace(
        [
            platformRegistration,
        ]);
        BrowserSpotlightActivationBasis basis = await Basis(workspace);

        Descriptor package = Projected(
            basis,
            new Destination.PackageLibrary(
                packageLibrary,
                PackageRequest(packageLibrary.PackageCoordinate),
                new TestLibraryIntent("package")));
        BrowserSpotlightPlatformAction<TestPlatformAction> platformAction =
            PlatformAction(basis, "platform");
        Descriptor platform = Projected(
            basis,
            new Destination.PlatformLibrary(
                platformLibrary,
                platformAction));

        Assert.IsType<Plan.Unavailable>(package.Plan);
        Assert.Empty(package.Coverage);
        Assert.Equal(
            BrowserSpotlightDestinationHome.Package,
            package.SourceHome);
        var platformPlan =
            Assert.IsType<Plan.ActivateCurrentPlatformDestination>(
                platform.Plan);
        Assert.Same(platformAction, platformPlan.Action);
        Assert.Single(platform.Coverage);
        Assert.Equal(
            BrowserSpotlightDestinationHome.Platform,
            platform.SourceHome);
        Assert.Equal(
            BrowserSpotlightWorkspaceRelationship.RegistrationCovered,
            platform.WorkspaceRelationship);
        Assert.IsType<BrowserSpotlightDestinationAvailability.Available>(
            platform.Availability);
        Assert.IsType<WorkspaceEcosystemPopulationDeclaration.Platform>(
            platform.Coverage[0].Population);

        WorkspaceRegistrationOperationResult replaced =
            workspace.ReplaceRegistrations(
                basis.Registrations,
                [
                    new WorkspaceRegistration.PackagePrefix(
                        new PackagePrefixDeclaration("System.Text.")),
                    platformRegistration,
                ]);
        Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            replaced);
        BrowserSpotlightActivationBasis coveredBasis =
            await Basis(workspace);
        Descriptor coveredPackage = Projected(
            coveredBasis,
            new Destination.PackageLibrary(
                packageLibrary,
                PackageRequest(packageLibrary.PackageCoordinate),
                new TestLibraryIntent("package-covered")));
        BrowserSpotlightPlatformAction<TestPlatformAction>
            coveredPlatformAction =
                PlatformAction(coveredBasis, "platform-covered");
        Descriptor coveredPlatform = Projected(
            coveredBasis,
            new Destination.PlatformLibrary(
                platformLibrary,
                coveredPlatformAction));

        Assert.IsType<Plan.AddCurrentPackage>(coveredPackage.Plan);
        Assert.Equal(
            BrowserSpotlightDestinationHome.Package,
            coveredPackage.SourceHome);
        Assert.Same(
            coveredPlatformAction,
            Assert.IsType<Plan.ActivateCurrentPlatformDestination>(
                coveredPlatform.Plan).Action);
        Assert.Equal(
            BrowserSpotlightDestinationHome.Platform,
            coveredPlatform.SourceHome);
    }

    [Fact]
    public async Task RealizedPlatformAndDescendantActionsRemainOpaque()
    {
        await using var workspace = new InspectionWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        var libraryAction = new TestPlatformAction("library");
        var descendantAction = new TestPlatformAction("member");

        Descriptor library = Projected(
            basis,
            new Destination.CurrentPlatformLibrary(
                PlatformLibrary(),
                PlatformAction(basis, libraryAction)));
        Descriptor descendant = Projected(
            basis,
            new Destination.PlatformDescendant(
                PlatformAction(basis, descendantAction)));

        Assert.Same(
            libraryAction,
            Assert.IsType<Plan.ActivateCurrentPlatformDestination>(
                library.Plan).Action.Action);
        Assert.Same(
            descendantAction,
            Assert.IsType<Plan.ActivateCurrentPlatformDestination>(
                descendant.Plan).Action.Action);
        Assert.Empty(library.Coverage);
        Assert.Empty(descendant.Coverage);
        Assert.Equal(
            BrowserSpotlightDestinationHome.Platform,
            library.SourceHome);
        Assert.Equal(
            BrowserSpotlightWorkspaceRelationship.Admitted,
            library.WorkspaceRelationship);
    }

    [Fact]
    public async Task UncoveredPackageCreatesFreshWorkspaceButLibraryIsUnavailable()
    {
        ExactLibrarySourceCoordinate.Package library = PackageLibrary();
        await using var workspace = new InspectionWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(workspace);

        Descriptor package = Projected(
            basis,
            new Destination.Package(
                PackageRequest(library.PackageCoordinate)));
        Descriptor packageLibrary = Projected(
            basis,
            new Destination.PackageLibrary(
                library,
                PackageRequest(library.PackageCoordinate),
                new TestLibraryIntent("uncovered")));
        Descriptor platformLibrary = Projected(
            basis,
            new Destination.PlatformLibrary(
                PlatformLibrary(),
                PlatformAction(basis, "uncovered")));

        Assert.IsType<Plan.RestoreExternalPackageWorkspace>(package.Plan);
        Assert.Equal(
            BrowserSpotlightWorkspaceDisposition.CreateFresh,
            package.WorkspaceDisposition);
        Assert.Equal(
            BrowserSpotlightWorkspaceRelationship.External,
            package.WorkspaceRelationship);
        Assert.Equal(
            BrowserSpotlightDestinationHome.Package,
            package.SourceHome);
        Assert.IsType<BrowserSpotlightDestinationAvailability.Available>(
            package.Availability);
        Assert.IsType<Plan.Unavailable>(packageLibrary.Plan);
        Assert.IsType<Plan.Unavailable>(platformLibrary.Plan);
        Assert.Equal(
            BrowserSpotlightWorkspaceDisposition.Unavailable,
            packageLibrary.WorkspaceDisposition);
        Assert.Equal(
            BrowserSpotlightWorkspaceRelationship.Unavailable,
            packageLibrary.WorkspaceRelationship);
        var unavailable = Assert.IsType<
            BrowserSpotlightDestinationAvailability.Unavailable>(
                packageLibrary.Availability);
        Assert.Equal(
            BrowserSpotlightDestinationUnavailableReason.UncoveredLibrary,
            unavailable.Reason);
        Assert.Equal(
            BrowserSpotlightDestinationHome.Package,
            packageLibrary.SourceHome);
        Assert.Equal(
            BrowserSpotlightDestinationHome.Platform,
            platformLibrary.SourceHome);
    }

    [Fact]
    public async Task ForeignRegistrationRevisionProducesTypedStaleBasis()
    {
        await using var scopeWorkspace = new InspectionWorkspace();
        await using var registrationWorkspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot scope = await Scope(scopeWorkspace);
        WorkspaceRegistrationRevision registrations =
            Registrations(registrationWorkspace);
        var basis = new BrowserSpotlightActivationBasis(
            7,
            scope,
            registrations);
        var destination = new Destination.Package(
            PackageRequest(
                PackageSourceCoordinate.Create(
                    "System.Text.Json",
                    PackageVersion)));

        Result result = BrowserSpotlightDestinationProjection.Project(
            basis,
            destination);

        var stale = Assert.IsType<Result.StaleBasis>(result);
        Assert.Equal(
            BrowserSpotlightProjectionStaleReason
                .RegistrationWorkspaceMismatch,
            stale.Reason);
        Assert.Same(basis, stale.Basis);
        Assert.Same(destination, stale.Destination);
    }

    [Fact]
    public async Task RemovedOccurrenceProducesTypedStaleBasis()
    {
        ExactLibrarySourceCoordinate.Package library = PackageLibrary();
        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot populated = await ReplaceScope(
            workspace,
            library.PackageCoordinate,
            PackageAssemblyPath());
        WorkspacePackageOccurrence removed =
            Assert.Single(populated.Revision.Packages);
        WorkspaceScopeSnapshot empty = Assert.IsType<
            WorkspaceScopeOperationResult.Committed>(
                await workspace.ClearScopeAsync(
                    populated.Revision,
                    DateTimeOffset.UtcNow.AddSeconds(30),
                    TestContext.Current.CancellationToken)).Snapshot;
        BrowserSpotlightActivationBasis basis = Basis(workspace, empty);

        Result result = BrowserSpotlightDestinationProjection.Project(
            basis,
            new Destination.CurrentPackageLibrary(
                library,
                PackageRequest(
                    library.PackageCoordinate,
                    removed.Package.Coordinate),
                new TestLibraryIntent("stale"),
                removed));

        Assert.Equal(
            BrowserSpotlightProjectionStaleReason
                .PackageOccurrenceNotCurrent,
            Assert.IsType<Result.StaleBasis>(result).Reason);
    }

    [Fact]
    public async Task ForeignNavigationSubjectProducesTypedStaleBasis()
    {
        await using var current = new InspectionWorkspace();
        await using var foreign = new InspectionWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(current);
        StructuralSubjectIdentity.WorkspaceSubject subject =
            StructuralSubjectIdentity.ForWorkspace(
                (await Scope(foreign)).Revision.Workspace);

        Result result = BrowserSpotlightDestinationProjection.Project(
            basis,
            new Destination.Current(
                subject,
                new TestNavigationAction("foreign")));

        Assert.Equal(
            BrowserSpotlightProjectionStaleReason
                .NavigationSubjectWorkspaceMismatch,
            Assert.IsType<Result.StaleBasis>(result).Reason);
    }

    [Fact]
    public async Task SamePackageCoordinateRetainsDistinctSourceRequests()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(
                "System.Text.Json",
                PackageVersion);
        await using var workspace = new InspectionWorkspace(
        [
            new WorkspaceRegistration.PackagePrefix(
                new PackagePrefixDeclaration("System.Text.")),
        ]);
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        BrowserSpotlightPackageRequest<TestPackageAction> first =
            PackageRequest(coordinate, source: "source-a");
        BrowserSpotlightPackageRequest<TestPackageAction> second =
            PackageRequest(coordinate, source: "source-b");

        Descriptor firstDescriptor = Projected(
            basis,
            new Destination.Package(first));
        Descriptor secondDescriptor = Projected(
            basis,
            new Destination.Package(second));

        var firstPlan =
            Assert.IsType<Plan.AddCurrentPackage>(firstDescriptor.Plan);
        var secondPlan =
            Assert.IsType<Plan.AddCurrentPackage>(secondDescriptor.Plan);
        Assert.Same(first, firstPlan.Package);
        Assert.Same(second, secondPlan.Package);
        Assert.Equal("source-a", firstPlan.Package.Request.Source);
        Assert.Equal("source-b", secondPlan.Package.Request.Source);
        Assert.Same(
            first,
            Assert.IsType<BrowserSpotlightCoverageTarget<
                TestPackageAction>.Package>(
                firstDescriptor.Coverage[0].Target).Request);
        Assert.Same(
            second,
            Assert.IsType<BrowserSpotlightCoverageTarget<
                TestPackageAction>.Package>(
                secondDescriptor.Coverage[0].Target).Request);
    }

    [Fact]
    public async Task DifferentProducerOccurrenceCannotSatisfyCurrentLibrary()
    {
        ExactLibrarySourceCoordinate.Package library = PackageLibrary();
        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot scope = await ReplaceScope(
            workspace,
            library.PackageCoordinate,
            PackageAssemblyPath());
        BrowserSpotlightActivationBasis basis = Basis(workspace, scope);
        WorkspacePackageOccurrence occurrence =
            Assert.Single(scope.Revision.Packages);
        var otherProducer = new RealizedMemberCoordinate.Package(
            occurrence.Package.Coordinate.PackageId,
            occurrence.Package.Coordinate.Version,
            "other-source",
            occurrence.Package.Coordinate.Framework,
            occurrence.Package.Coordinate.RuntimeIdentifier);

        Result result = BrowserSpotlightDestinationProjection.Project(
            basis,
            new Destination.CurrentPackageLibrary(
                library,
                PackageRequest(
                    library.PackageCoordinate,
                    otherProducer),
                new TestLibraryIntent("wrong-producer"),
                occurrence));

        Assert.Equal(
            BrowserSpotlightProjectionStaleReason
                .PackageOccurrenceCoordinateMismatch,
            Assert.IsType<Result.StaleBasis>(result).Reason);
    }

    [Fact]
    public async Task PlatformActionFromAnotherWorkspaceIsStale()
    {
        await using var current = new InspectionWorkspace();
        await using var foreign = new InspectionWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(current);
        InspectionWorkspaceIdentity foreignIdentity =
            (await Scope(foreign)).Revision.Workspace;

        Result result = BrowserSpotlightDestinationProjection.Project(
            basis,
            new Destination.CurrentPlatformLibrary(
                PlatformLibrary(),
                new BrowserSpotlightPlatformAction<TestPlatformAction>(
                    foreignIdentity,
                    new TestPlatformAction("foreign"))));

        Assert.Equal(
            BrowserSpotlightProjectionStaleReason
                .PlatformActionWorkspaceMismatch,
            Assert.IsType<Result.StaleBasis>(result).Reason);
    }

    [Fact]
    public async Task RegistrationReplacementLeavesScopeBasisIndependent()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot scope = await Scope(workspace);
        WorkspaceRegistrationRevision initial =
            Registrations(workspace);
        WorkspaceRegistrationRevision replaced = Assert.IsType<
            WorkspaceRegistrationOperationResult.Committed>(
                workspace.ReplaceRegistrations(
                    initial,
                    [
                        new WorkspaceRegistration.PackagePrefix(
                            new PackagePrefixDeclaration("System.Text.")),
                    ])).Revision;
        WorkspaceScopeSnapshot after = await Scope(workspace);

        Assert.Same(scope.Revision, after.Revision);
        Assert.Same(scope.PublicationBase, after.PublicationBase);
        Assert.NotSame(initial.Identity, replaced.Identity);
        Descriptor descriptor = Projected(
            new BrowserSpotlightActivationBasis(2, after, replaced),
            new Destination.Package(
                PackageRequest(
                    PackageSourceCoordinate.Create(
                        "System.Text.Json",
                        PackageVersion))));
        Assert.IsType<Plan.AddCurrentPackage>(descriptor.Plan);
    }

    private static Descriptor Projected(
        BrowserSpotlightActivationBasis basis,
        Destination destination) =>
        Assert.IsType<Result.Projected>(
            BrowserSpotlightDestinationProjection.Project(
                basis,
                destination)).Descriptor;

    private static async Task<BrowserSpotlightActivationBasis> Basis(
        InspectionWorkspace workspace) =>
        new(
            resultGeneration: 1,
            await Scope(workspace),
            Registrations(workspace));

    private static BrowserSpotlightActivationBasis Basis(
        InspectionWorkspace workspace,
        WorkspaceScopeSnapshot scope) =>
        new(
            resultGeneration: 1,
            scope,
            Registrations(workspace));

    private static async Task<WorkspaceScopeSnapshot> Scope(
        InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceScopeReadResult.Available>(
            await workspace.GetScopeSnapshotAsync()).Snapshot;

    private static WorkspaceRegistrationRevision Registrations(
        InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceRegistrationReadResult.Available>(
            workspace.GetRegistrationSnapshot()).Revision;

    private static async Task<WorkspaceScopeSnapshot> ReplaceScope(
        InspectionWorkspace workspace,
        PackageSourceCoordinate package,
        string assemblyPath)
    {
        WorkspaceScopeSnapshot current = await Scope(workspace);
        PackageRootBinding binding = Binding(package, assemblyPath);
        return Assert.IsType<WorkspaceScopeOperationResult.Committed>(
            await workspace.ReplaceScopeAsync(
                current.Revision,
                [binding],
                DateTimeOffset.UtcNow.AddSeconds(30),
                TestContext.Current.CancellationToken)).Snapshot;
    }

    private static PackageRootBinding Binding(
        PackageSourceCoordinate package,
        string assemblyPath)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(
            bytes,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry(
                "lib/net11.0/System.Text.Json.dll");
            using Stream destination = entry.Open();
            using Stream source = File.OpenRead(assemblyPath);
            source.CopyTo(destination);
        }

        return PackageRootBinding.CreateFromSource(
            new AcquiredPackageSourcePayload(
                package,
                new InMemoryPackageContent(
                    bytes.ToArray(),
                    fromCache: false,
                    producerKey: "nuget-org"),
                producerKey: "nuget-org",
                PackagePayloadOrigin.Download),
            selectionTargetFramework: "net11.0",
            runtimeIdentifier: null);
    }

    private static ExactLibrarySourceCoordinate.Package PackageLibrary() =>
        new(
            PackageSourceCoordinate.Create(
                "System.Text.Json",
                PackageVersion),
            Assembly(PackageAssemblyPath()));

    private static ExactLibrarySourceCoordinate.Platform PlatformLibrary() =>
        new(
            DotNetRuntimePopulation(),
            Assembly(PlatformAssemblyPath()));

    private static BrowserSpotlightPackageRequest<TestPackageAction>
        PackageRequest(
            PackageSourceCoordinate coordinate,
            RealizedMemberCoordinate.Package? workspaceCoordinate = null,
            string source = "nuget-org") =>
        new(
            coordinate,
            new TestPackageAction(source),
            workspaceCoordinate);

    private static BrowserSpotlightPlatformAction<TestPlatformAction>
        PlatformAction(
            BrowserSpotlightActivationBasis basis,
            string value) =>
        PlatformAction(basis, new TestPlatformAction(value));

    private static BrowserSpotlightPlatformAction<TestPlatformAction>
        PlatformAction(
            BrowserSpotlightActivationBasis basis,
            TestPlatformAction action) =>
        new(basis.Scope.Revision.Workspace, action);

    private static PlatformLibraryPopulationDeclaration
        DotNetRuntimePopulation() =>
        new(PlatformFamily.DotNetRuntime);

    private static ManagedMetadataIdentity.Assembly Assembly(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        return new(
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader));
    }

    private static AssemblyContextParticipant Participant(
        ExactLibrarySourceCoordinate.Package library)
    {
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                library.LibraryIdentity.Identity,
                PackageAssemblyPath(),
                () => File.OpenRead(PackageAssemblyPath()),
                AssemblyResolutionProvenance.Package(
                    library.PackageCoordinate.PackageId,
                    library.PackageCoordinate.Version,
                    "net11.0",
                    rid: null));
        return new AssemblyContextParticipant(
            assembly,
            NoResolverAssemblyBindingPolicy.Instance);
    }

    private static string PackageAssemblyPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "Spotlight",
            "package",
            "System.Text.Json.dll");

    private static string PlatformAssemblyPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "Spotlight",
            "platform",
            "System.Text.Json.dll");
}

internal sealed record TestNavigationAction(string Value);

internal sealed record TestPackageAction(string Source);

internal sealed record TestPlatformAction(string Value);

internal sealed record TestLibraryIntent(string Value);
