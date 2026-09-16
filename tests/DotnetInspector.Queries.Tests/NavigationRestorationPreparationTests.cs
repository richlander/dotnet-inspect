using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

using Fixture = DotnetInspector.Queries.Tests.NavigationSessionTests.Fixture;

namespace DotnetInspector.Queries.Tests;

public sealed class NavigationRestorationPreparationTests
{
    [Fact]
    public async Task CanonicalRestoration_PreparedPairEqualsExactRequest()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.InstalledSnapshot;
        StructuralSubjectIdentity.TypeSubject type =
            installed.Types[0].Row.Subject;
        var context = new NavigationRetainedSubjectContext(
            installed.Inventory!.Package,
            type.Library,
            type);
        var lens = new NavigationLensIdentity(
            type,
            new ViewFacetId("type.compare"));
        var request = new NavigationInitialization(
            type,
            context,
            lens);

        NavigationRestorationPreparationResult result =
            NavigationTransitions.PrepareRestoration(
                fixture.Workspace.Identity,
                Facts(fixture, fixture.Packages[0]),
                fixture.Registry,
                request);

        NavigationRestorationPreparationResult.Prepared prepared =
            Assert.IsType<
                NavigationRestorationPreparationResult.Prepared>(result);
        NavigationOperationInitialization initialization =
            prepared.Initialization;
        Assert.Equal(
            type,
            initialization.State.InstalledSnapshot.ActiveSubject);
        Assert.Equal(
            lens,
            initialization.State.InstalledSnapshot
                .LensOutcome.EffectiveLens);
        Assert.Equal(
            NavigationOutcomeKind.Applied,
            initialization.Result.Consumer.Outcome.Kind);
        NavigationLensActivationResult.Applied activation =
            Assert.IsType<NavigationLensActivationResult.Applied>(
                initialization.Result.LensResolution!.Activation);
        Assert.Equal(lens, activation.Request);
        Assert.Equal(
            initialization.Result.Consumer.Authority,
            initialization.Result.LensResolution.Authority);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        CanonicalRestoration_ExactRegistryStatusRemainsPrepared(
            bool failed)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.InstalledSnapshot;
        StructuralSubjectIdentity.TypeSubject type =
            installed.Types[0].Row.Subject;
        var context = new NavigationRetainedSubjectContext(
            installed.Inventory!.Package,
            type.Library,
            type);
        var lens = new NavigationLensIdentity(
            type,
            new ViewFacetId("type.compare"));
        fixture.Override = id =>
            id == lens.Facet
                ? failed
                    ? new ViewFacetAvailability.Failed(
                        "Compare failed.",
                        new Diagnostic())
                    : new ViewFacetAvailability.Unavailable(
                        ViewFacetUnavailableReason.CapabilityAbsent(
                            "Compare is unavailable."))
                : null;

        NavigationRestorationPreparationResult result =
            NavigationTransitions.PrepareRestoration(
                fixture.Workspace.Identity,
                Facts(fixture, fixture.Packages[0]),
                fixture.Registry,
                new(type, context, lens));

        NavigationRestorationPreparationResult.Prepared prepared =
            Assert.IsType<
                NavigationRestorationPreparationResult.Prepared>(result);
        Assert.Equal(
            failed
                ? NavigationOutcomeKind.Failed
                : NavigationOutcomeKind.Unavailable,
            prepared.Initialization.Result.Consumer.Outcome.Kind);
        Assert.Null(
            prepared.Initialization.State.InstalledSnapshot
                .LensOutcome.EffectiveLens);
        NavigationLensEvaluationBasis.ExactRequest exact =
            Assert.IsType<NavigationLensEvaluationBasis.ExactRequest>(
                prepared.Initialization.State.InstalledSnapshot
                    .LensOutcome.Basis);
        Assert.Equal(lens, exact.Request);
        if (failed)
        {
            Assert.IsType<NavigationLensActivationResult.Failed>(
                prepared.Initialization.Result.LensResolution!.Activation);
            Assert.IsType<ViewFacetResolution.Failed>(exact.Result);
        }
        else
        {
            Assert.IsType<NavigationLensActivationResult.Unavailable>(
                prepared.Initialization.Result.LensResolution!.Activation);
            Assert.IsType<ViewFacetResolution.Unavailable>(exact.Result);
        }
    }

    [Theory]
    [InlineData("type.unknown")]
    [InlineData("package.overview")]
    public async Task
        CanonicalRestoration_RejectsUnknownOrInapplicableExactLens(
            string facet)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.InstalledSnapshot;
        StructuralSubjectIdentity.TypeSubject type =
            installed.Types[0].Row.Subject;
        var context = new NavigationRetainedSubjectContext(
            installed.Inventory!.Package,
            type.Library,
            type);
        var lens = new NavigationLensIdentity(
            type,
            new ViewFacetId(facet));

        NavigationRestorationPreparationResult result =
            NavigationTransitions.PrepareRestoration(
                fixture.Workspace.Identity,
                Facts(fixture, fixture.Packages[0]),
                fixture.Registry,
                new(type, context, lens));

        NavigationRestorationPreparationResult.Rejected rejected =
            Assert.IsType<
                NavigationRestorationPreparationResult.Rejected>(result);
        Assert.Equal(
            NavigationRestorationRejectionKind.Registry,
            rejected.Kind);
        NavigationLensActivationResult.Rejected resolution =
            Assert.IsType<NavigationLensActivationResult.Rejected>(
                rejected.LensResolution);
        Assert.Equal(lens, resolution.Request);
    }

    [Fact]
    public async Task
        CanonicalRestoration_RejectsMismatchedSubjectBoundLens()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.InstalledSnapshot;
        StructuralSubjectIdentity.TypeSubject subject =
            installed.Types[0].Row.Subject;
        StructuralSubjectIdentity.TypeSubject lensSubject =
            installed.Types[1].Row.Subject;
        var context = new NavigationRetainedSubjectContext(
            installed.Inventory!.Package,
            subject.Library,
            subject);
        var request = new NavigationInitialization(
            subject,
            context,
            new NavigationLensIdentity(
                lensSubject,
                new ViewFacetId("type.compare")));
        var facts = new NavigationEvaluationFacts(
            fixture.Scope,
            fixture.Packages[0],
            (_, _) => throw new InvalidOperationException(
                "Registry availability must not be queried."));

        NavigationRestorationPreparationResult result =
            NavigationTransitions.PrepareRestoration(
                fixture.Workspace.Identity,
                facts,
                fixture.Registry,
                request);

        NavigationRestorationPreparationResult.Rejected rejected =
            Assert.IsType<
                NavigationRestorationPreparationResult.Rejected>(result);
        Assert.Equal(
            NavigationRestorationRejectionKind.LensSubjectMismatch,
            rejected.Kind);
        Assert.Null(rejected.LensResolution);
    }

    [Fact]
    public async Task
        CanonicalRestoration_RejectsExactLensWithoutSubject()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.InstalledSnapshot;
        StructuralSubjectIdentity.TypeSubject type =
            installed.Types[0].Row.Subject;
        var facts = new NavigationEvaluationFacts(
            fixture.Scope,
            fixture.Packages[0],
            (_, _) => throw new InvalidOperationException(
                "Registry availability must not be queried."));

        NavigationRestorationPreparationResult result =
            NavigationTransitions.PrepareRestoration(
                fixture.Workspace.Identity,
                facts,
                fixture.Registry,
                new(
                    Subject: null,
                    Context: new NavigationRetainedSubjectContext(
                        installed.Inventory!.Package),
                    Lens: new NavigationLensIdentity(
                        type,
                        new ViewFacetId("type.compare"))));

        NavigationRestorationPreparationResult.Rejected rejected =
            Assert.IsType<
                NavigationRestorationPreparationResult.Rejected>(result);
        Assert.Equal(
            NavigationRestorationRejectionKind.LensRequiresSubject,
            rejected.Kind);
        Assert.Null(rejected.LensResolution);
    }

    [Fact]
    public async Task
        CanonicalRestoration_SubjectlessPackageContextRecommends()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.InstalledSnapshot;
        StructuralSubjectIdentity.TypeSubject type =
            installed.Types[0].Row.Subject;
        StructuralSubjectIdentity.PackageSubject package =
            installed.Inventory!.Package;

        NavigationRestorationPreparationResult root =
            NavigationTransitions.PrepareRestoration(
                fixture.Workspace.Identity,
                Facts(fixture, fixture.Packages[0]),
                fixture.Registry,
                new(
                    Subject: null,
                    Context: new NavigationRetainedSubjectContext(package)));
        NavigationRestorationPreparationResult.Prepared prepared =
            Assert.IsType<
                NavigationRestorationPreparationResult.Prepared>(root);
        Assert.Equal(
            StructuralSubjectKind.Library,
            prepared.Initialization.State.Snapshot.ActiveSubject.Kind);
        Assert.NotNull(
            prepared.Initialization.State.InstalledSnapshot
                .LensOutcome.EffectiveLens);
    }

    [Fact]
    public async Task
        CanonicalRestoration_RejectsSubjectlessLowerRetainedPath()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.InstalledSnapshot;
        StructuralSubjectIdentity.TypeSubject type =
            installed.Types[0].Row.Subject;
        StructuralSubjectIdentity.PackageSubject package =
            installed.Inventory!.Package;
        NavigationRestorationPreparationResult lower =
            NavigationTransitions.PrepareRestoration(
                fixture.Workspace.Identity,
                Facts(fixture, fixture.Packages[0]),
                fixture.Registry,
                new(
                    Subject: null,
                    Context: new NavigationRetainedSubjectContext(
                        package,
                        type.Library,
                        type)));
        NavigationRestorationPreparationResult.Rejected rejected =
            Assert.IsType<
                NavigationRestorationPreparationResult.Rejected>(lower);
        Assert.Equal(
            NavigationRestorationRejectionKind.SubjectOutsideContext,
            rejected.Kind);
    }

    [Fact]
    public async Task
        CanonicalRestoration_RejectsSubjectFromAnotherOccurrence()
    {
        await using Fixture first = await Fixture.CreateAsync();
        await using Fixture second = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot firstInstalled =
            first.Session.InstalledSnapshot;
        NavigationWorkspaceSnapshot secondInstalled =
            second.Session.InstalledSnapshot;
        StructuralSubjectIdentity.TypeSubject type =
            secondInstalled.Types[0].Row.Subject;
        var request = new NavigationInitialization(
            type,
            new NavigationRetainedSubjectContext(
                firstInstalled.Inventory!.Package));

        NavigationRestorationPreparationResult result =
            NavigationTransitions.PrepareRestoration(
                first.Workspace.Identity,
                Facts(first, first.Packages[0]),
                first.Registry,
                request);

        NavigationRestorationPreparationResult.Rejected rejected =
            Assert.IsType<
                NavigationRestorationPreparationResult.Rejected>(result);
        Assert.Equal(
            NavigationRestorationRejectionKind.InvalidContext,
            rejected.Kind);
    }

    [Fact]
    public async Task
        CanonicalRestoration_RejectsForeignPreparedPackageFacts()
    {
        await using Fixture first = await Fixture.CreateAsync();
        await using Fixture second = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            first.Session.InstalledSnapshot;
        StructuralSubjectIdentity.TypeSubject type =
            installed.Types[0].Row.Subject;
        var request = new NavigationInitialization(
            type,
            new NavigationRetainedSubjectContext(
                installed.Inventory!.Package,
                type.Library,
                type));

        NavigationRestorationPreparationResult result =
            NavigationTransitions.PrepareRestoration(
                first.Workspace.Identity,
                Facts(first, second.Packages[0]),
                first.Registry,
                request);

        NavigationRestorationPreparationResult.Rejected rejected =
            Assert.IsType<
                NavigationRestorationPreparationResult.Rejected>(result);
        Assert.Equal(
            NavigationRestorationRejectionKind.InvalidContext,
            rejected.Kind);
    }

    [Fact]
    public async Task
        CanonicalRestoration_RejectsSameOccurrenceSubjectOutsideRetainedPath()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.InstalledSnapshot;
        StructuralSubjectIdentity.TypeSubject retained =
            installed.Types[0].Row.Subject;
        StructuralSubjectIdentity.TypeSubject requested =
            installed.Types[1].Row.Subject;
        var context = new NavigationRetainedSubjectContext(
            installed.Inventory!.Package,
            retained.Library,
            retained);
        var facts = new NavigationEvaluationFacts(
            fixture.Scope,
            fixture.Packages[0],
            (_, _) => throw new InvalidOperationException(
                "Registry availability must not be queried."));

        NavigationRestorationPreparationResult result =
            NavigationTransitions.PrepareRestoration(
                fixture.Workspace.Identity,
                facts,
                fixture.Registry,
                new(requested, context));

        NavigationRestorationPreparationResult.Rejected rejected =
            Assert.IsType<
                NavigationRestorationPreparationResult.Rejected>(result);
        Assert.Equal(
            NavigationRestorationRejectionKind.SubjectOutsideContext,
            rejected.Kind);
    }

    [Fact]
    public async Task
        CanonicalRestoration_RejectsInconsistentRetainedOccurrenceContext()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.InstalledSnapshot;
        StructuralSubjectIdentity.TypeSubject first =
            installed.Types[0].Row.Subject;
        StructuralSubjectIdentity.TypeSubject second =
            installed.Types[1].Row.Subject;

        Assert.Throws<ArgumentException>(() =>
            new NavigationRetainedSubjectContext(
                installed.Inventory!.Package,
                first.Library,
                second));
    }

    [Fact]
    public async Task
        CanonicalRestoration_DerivesTypeInventoryContextFromRetainedPathAndFacts()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.InstalledSnapshot;
        StructuralSubjectIdentity.TypeSubject type =
            installed.Types[1].Row.Subject;
        var context = new NavigationRetainedSubjectContext(
            installed.Inventory!.Package,
            type.Library,
            type);

        NavigationRestorationPreparationResult.Prepared prepared =
            Assert.IsType<
                NavigationRestorationPreparationResult.Prepared>(
                NavigationTransitions.PrepareRestoration(
                    fixture.Workspace.Identity,
                    Facts(fixture, fixture.Packages[0]),
                    fixture.Registry,
                    new(installed.Workspace, context)));

        Assert.Equal(
            type.Library,
            prepared.Initialization.State.InstalledSnapshot
                .TypeInventoryLibraryContext);
        Assert.Equal(
            type.Library,
            prepared.Initialization.State.InstalledSnapshot
                .RetainedContext!.Library);
    }

    [Fact]
    public async Task CanonicalRestoration_ExactPackageRootIsPrepared()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.InstalledSnapshot;
        StructuralSubjectIdentity.PackageSubject package =
            installed.Inventory!.Package;
        var lens = new NavigationLensIdentity(
            package,
            new ViewFacetId("package.overview"));

        NavigationRestorationPreparationResult.Prepared prepared =
            Assert.IsType<
                NavigationRestorationPreparationResult.Prepared>(
                NavigationTransitions.PrepareRestoration(
                    fixture.Workspace.Identity,
                    Facts(fixture, fixture.Packages[0]),
                    fixture.Registry,
                    new(
                        package,
                        new NavigationRetainedSubjectContext(package),
                        lens)));

        Assert.Equal(
            package,
            prepared.Initialization.State.InstalledSnapshot.ActiveSubject);
        Assert.Equal(
            lens,
            prepared.Initialization.State.InstalledSnapshot
                .LensOutcome.EffectiveLens);
    }

    [Fact]
    public async Task
        CanonicalRestoration_NonReadyPackageFailsWithoutRegistryResolution()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.InstalledSnapshot;
        StructuralSubjectIdentity.PackageSubject package =
            installed.Inventory!.Package;
        WorkspaceScopeSnapshot scope = WithStatus(
            fixture.Scope,
            new ArtifactRootRealizationStatus.Pending());
        var facts = new NavigationEvaluationFacts(
            scope,
            Package: null,
            (_, _) => throw new InvalidOperationException(
                "Registry availability must not be queried."),
            new NavigationNonReadyPackageEvaluation(scope.Packages[0]));

        NavigationRestorationPreparationResult result =
            NavigationTransitions.PrepareRestoration(
                fixture.Workspace.Identity,
                facts,
                fixture.Registry,
                new(
                    installed.Workspace,
                    new NavigationRetainedSubjectContext(package)));

        NavigationRestorationPreparationResult.Failed failed =
            Assert.IsType<
                NavigationRestorationPreparationResult.Failed>(result);
        Assert.Equal(
            NavigationRestorationFailureKind.PackageNotPrepared,
            failed.Kind);
        Assert.Equal(package, failed.Subject);
    }

    [Fact]
    public async Task
        CanonicalRestoration_WorkspaceSubjectPreservesDistinctDescendantContexts()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.InstalledSnapshot;
        StructuralSubjectIdentity.TypeSubject first =
            installed.Types[0].Row.Subject;
        StructuralSubjectIdentity.TypeSubject second =
            installed.Types[1].Row.Subject;
        StructuralSubjectIdentity.PackageSubject package =
            installed.Inventory!.Package;

        NavigationRestorationPreparationResult.Prepared firstResult =
            Prepared(new(
                installed.Workspace,
                new NavigationRetainedSubjectContext(
                    package,
                    first.Library,
                    first)));
        NavigationRestorationPreparationResult.Prepared secondResult =
            Prepared(new(
                installed.Workspace,
                new NavigationRetainedSubjectContext(
                    package,
                    second.Library,
                    second)));

        NavigationConsumerSnapshot firstSnapshot =
            firstResult.Initialization.Result.Consumer.Snapshot;
        NavigationConsumerSnapshot secondSnapshot =
            secondResult.Initialization.Result.Consumer.Snapshot;
        Assert.Equal(
            StructuralSubjectKind.Workspace,
            firstSnapshot.ActiveSubject.Kind);
        Assert.Equal(
            StructuralSubjectKind.Workspace,
            secondSnapshot.ActiveSubject.Kind);
        Assert.NotEqual(
            firstResult.Initialization.State.InstalledSnapshot.RetainedContext,
            secondResult.Initialization.State.InstalledSnapshot.RetainedContext);
        Assert.Equal(
            first.Library,
            firstResult.Initialization.State.InstalledSnapshot
                .TypeInventoryLibraryContext);
        Assert.Equal(
            second.Library,
            secondResult.Initialization.State.InstalledSnapshot
                .TypeInventoryLibraryContext);

        NavigationRestorationPreparationResult.Prepared Prepared(
            NavigationInitialization request) =>
            Assert.IsType<
                NavigationRestorationPreparationResult.Prepared>(
                NavigationTransitions.PrepareRestoration(
                    fixture.Workspace.Identity,
                    Facts(fixture, fixture.Packages[0]),
                    fixture.Registry,
                    request));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task
        CanonicalRestoration_InventoryMissDistinguishesAbsentFromIncomplete(
            bool incomplete,
            bool member)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ApiSurfaceInspectionFailure[] failures =
            incomplete
                ?
                [
                    new ApiSurfaceInspectionFailure(
                        "decode Type",
                        SubjectToken: 1,
                        MetadataTypeNameFailureMechanism.Metadata,
                        Kind: "TypeDef",
                        Detail: "invalid"),
                ]
                : [];
        NavigationPackageEvaluation package =
            NavigationSnapshotTestData.PackageEvaluation(
                fixture.Scope.Packages[0],
                fixture.Bindings[0],
                new NavigationSnapshotTestData.LibrarySurface(
                    "Primary",
                    member
                        ? [NavigationSnapshotTestData.Type("Existing")]
                        : [],
                    [.. failures],
                    Error: null),
                NavigationSnapshotTestData.Surface(
                    "Secondary",
                    NavigationSnapshotTestData.Type("Other")));
        NavigationWorkspaceSnapshot basis =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = fixture.Scope,
                    Package = package,
                    ActiveSubject = StructuralSubjectIdentity.ForWorkspace(
                        fixture.Workspace.Identity),
                },
                fixture.Registry,
                fixture.Availability);
        StructuralSubjectIdentity.LibrarySubject library =
            basis.Inventory!.Libraries[0].Subject;
        StructuralSubjectIdentity.TypeSubject type =
            member
                ? basis.Inventory.Libraries[0].Types.Rows[0].Subject
                : StructuralSubjectIdentity.ForType(
                library,
                NavigationSnapshotTestData.Type("Missing").DefinitionName!);
        StructuralSubjectIdentity missing =
            member
                ? StructuralSubjectIdentity.ForMember(
                    type,
                    new MemberAnchor(
                        "Missing()",
                        "Existing.Missing()",
                        MemberAnchor.ComputeFingerprint(
                            "Existing.Missing()"),
                        "Existing",
                        "Missing"))
                : type;
        var context = new NavigationRetainedSubjectContext(
            basis.Inventory.Package,
            library,
            type,
            member
                ? (StructuralSubjectIdentity.MemberSubject)missing
                : null);

        NavigationRestorationPreparationResult result =
            NavigationTransitions.PrepareRestoration(
                fixture.Workspace.Identity,
                Facts(fixture, package),
                fixture.Registry,
                new(missing, context));

        if (incomplete)
        {
            NavigationRestorationPreparationResult.Failed failed =
                Assert.IsType<
                    NavigationRestorationPreparationResult.Failed>(result);
            Assert.Equal(
                NavigationRestorationFailureKind.IncompleteInventory,
                failed.Kind);
            Assert.Equal(
                basis.Inventory.Libraries[0].Types,
                failed.Inventory);
        }
        else
        {
            NavigationRestorationPreparationResult.Unavailable unavailable =
                Assert.IsType<
                    NavigationRestorationPreparationResult.Unavailable>(result);
            Assert.Equal(missing, unavailable.Subject);
        }
    }

    [Fact]
    public async Task
        CanonicalRestoration_IncompleteFailureEvidenceIsDetached()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationPackageEvaluation source = fixture.Packages[0];
        var error = new InvalidOperationException(
            "restoration participant failed");
        var surface = new AssemblyContextApiSurfaceResult(
            new AssemblyContextResult<AssemblyApiSurface>(
            [
                .. source.Libraries.Select(
                    library =>
                        (AssemblyContextEntry<AssemblyApiSurface>)
                        new AssemblyContextEntry<AssemblyApiSurface>.Failed(
                            new AssemblyContextSubject(
                                library.Library.Participant.Assembly),
                            error)),
            ]),
            [],
            Truncation: null);
        var package = new NavigationPackageEvaluation(
            source.Occurrence,
            fixture.Bindings[0],
            source.Libraries,
            surface);
        NavigationWorkspaceSnapshot basis =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = fixture.Scope,
                    Package = package,
                    ActiveSubject = StructuralSubjectIdentity.ForWorkspace(
                        fixture.Workspace.Identity),
                },
                fixture.Registry,
                fixture.Availability);
        StructuralSubjectIdentity.LibrarySubject library =
            basis.Inventory!.Libraries[0].Subject;
        StructuralSubjectIdentity.TypeSubject missing =
            StructuralSubjectIdentity.ForType(
                library,
                NavigationSnapshotTestData.Type("Missing").DefinitionName!);

        NavigationRestorationPreparationResult.Failed failed =
            Assert.IsType<
                NavigationRestorationPreparationResult.Failed>(
                NavigationTransitions.PrepareRestoration(
                    fixture.Workspace.Identity,
                    Facts(fixture, package),
                    fixture.Registry,
                    new(
                        missing,
                        new NavigationRetainedSubjectContext(
                            basis.Inventory.Package,
                            library,
                            missing))));

        NavigationInventoryEvidence.DetachedParticipantFailed evidence =
            Assert.IsType<
                NavigationInventoryEvidence.DetachedParticipantFailed>(
                Assert.Single(failed.Inventory!.Evidence));
        Assert.Equal(error.Message, evidence.Error.Message);
    }

    [Fact]
    public async Task
        CanonicalRestoration_TrustworthyRequestedRowSurvivesPeerFailure()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationPackageEvaluation package =
            NavigationSnapshotTestData.PackageEvaluation(
                fixture.Scope.Packages[0],
                fixture.Bindings[0],
                new NavigationSnapshotTestData.LibrarySurface(
                    "Primary",
                    [NavigationSnapshotTestData.Type("Requested")],
                    [
                        new ApiSurfaceInspectionFailure(
                            "decode peer Type",
                            SubjectToken: 2,
                            MetadataTypeNameFailureMechanism.Metadata,
                            Kind: "TypeDef",
                            Detail: "invalid"),
                    ],
                    Error: null),
                NavigationSnapshotTestData.Surface(
                    "Secondary",
                    NavigationSnapshotTestData.Type("Other")));
        NavigationWorkspaceSnapshot basis =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = fixture.Scope,
                    Package = package,
                    ActiveSubject = StructuralSubjectIdentity.ForWorkspace(
                        fixture.Workspace.Identity),
                },
                fixture.Registry,
                fixture.Availability);
        StructuralSubjectIdentity.TypeSubject requested =
            basis.Inventory!.Libraries[0].Types.Rows[0].Subject;
        var context = new NavigationRetainedSubjectContext(
            basis.Inventory.Package,
            requested.Library,
            requested);

        NavigationRestorationPreparationResult result =
            NavigationTransitions.PrepareRestoration(
                fixture.Workspace.Identity,
                Facts(fixture, package),
                fixture.Registry,
                new(requested, context));

        NavigationRestorationPreparationResult.Prepared prepared =
            Assert.IsType<
                NavigationRestorationPreparationResult.Prepared>(result);
        Assert.Equal(
            requested,
            prepared.Initialization.State.InstalledSnapshot.ActiveSubject);
        Assert.NotEmpty(
            prepared.Initialization.State.InstalledSnapshot
                .Inventory!.Libraries[0].Types.Evidence);
    }

    [Fact]
    public async Task
        CanonicalRestoration_EqualInputsIssueIndependentStateAndAuthority()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.InstalledSnapshot;
        StructuralSubjectIdentity.TypeSubject type =
            installed.Types[0].Row.Subject;
        var request = new NavigationInitialization(
            type,
            new NavigationRetainedSubjectContext(
                installed.Inventory!.Package,
                type.Library,
                type),
            new NavigationLensIdentity(
                type,
                new ViewFacetId("type.compare")));

        NavigationRestorationPreparationResult.Prepared first =
            Prepare();
        NavigationRestorationPreparationResult.Prepared second =
            Prepare();

        Assert.NotEqual(
            first.Initialization.State.Id,
            second.Initialization.State.Id);
        Assert.NotEqual(
            first.Initialization.State.Publication,
            second.Initialization.State.Publication);
        Assert.NotEqual(
            first.Initialization.Result.Consumer.Authority,
            second.Initialization.Result.Consumer.Authority);
        Assert.NotEqual(
            first.Initialization.State.Snapshot.Types
                .Single(type => !type.Navigation.IsActive)
                .Navigation.Action!.Id,
            second.Initialization.State.Snapshot.Types
                .Single(type => !type.Navigation.IsActive)
                .Navigation.Action!.Id);
        Assert.True(
            NavigationWorkspaceSnapshotEquality.Equals(
                first.Initialization.State.InstalledSnapshot,
                second.Initialization.State.InstalledSnapshot));

        NavigationRestorationPreparationResult.Prepared Prepare() =>
            Assert.IsType<
                NavigationRestorationPreparationResult.Prepared>(
                NavigationTransitions.PrepareRestoration(
                    fixture.Workspace.Identity,
                    Facts(fixture, fixture.Packages[0]),
                    fixture.Registry,
                    request));
    }

    static NavigationEvaluationFacts Facts(
        Fixture fixture,
        NavigationPackageEvaluation? package) =>
        new(
            fixture.Scope,
            package,
            fixture.Availability);

    static WorkspaceScopeSnapshot WithStatus(
        WorkspaceScopeSnapshot scope,
        ArtifactRootRealizationStatus status) =>
        new(
            scope.Revision,
            scope.PhysicalComposition,
            [
                new(
                    scope.Packages[0].Occurrence,
                    scope.Packages[0].Realization with
                    {
                        Status = status,
                    }),
                .. scope.Packages.Skip(1),
            ],
            scope.Closure,
            scope.Preparing);

    sealed record Diagnostic : IViewFacetDiagnosticEvidence;
}
