using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

using Fixture = DotnetInspector.Queries.Tests.NavigationSessionTests.Fixture;

namespace DotnetInspector.Queries.Tests;

public sealed class NavigationRestorationPreparationTests
{
    [Fact]
    public async Task
        CanonicalRestoration_ExactEcosystemPairIsPrepared()
    {
        await using EcosystemFixture fixture =
            await EcosystemFixture.CreateAsync("ecosystem.aspire");
        NavigationEcosystemEvaluation ecosystem =
            fixture.Evaluation("ecosystem.aspire");
        StructuralSubjectIdentity.EcosystemSubject subject =
            fixture.Subject(ecosystem);
        var lens = new NavigationLensIdentity(
            subject,
            new ViewFacetId("ecosystem.overview"));

        NavigationRestorationPreparationResult.Prepared prepared =
            Assert.IsType<
                NavigationRestorationPreparationResult.Prepared>(
                NavigationTransitions.PrepareRestoration(
                    fixture.Workspace.Identity,
                    fixture.Facts(ecosystem),
                    fixture.Registry,
                    new(subject, Lens: lens)));

        NavigationWorkspaceSnapshot snapshot =
            prepared.Initialization.State.CurrentSnapshot;
        Assert.Same(subject.Occurrence, snapshot.Ecosystem!.Occurrence);
        Assert.Equal(subject, snapshot.ActiveSubject);
        Assert.Null(snapshot.RetainedContext);
        Assert.Empty(snapshot.Packages);
        Assert.Equal(lens, snapshot.LensOutcome.EffectiveLens);
    }

    [Fact]
    public async Task
        CanonicalRestoration_EcosystemFactsDefaultToExactSubject()
    {
        await using EcosystemFixture fixture =
            await EcosystemFixture.CreateAsync("ecosystem.aspire");
        NavigationEcosystemEvaluation ecosystem =
            fixture.Evaluation("ecosystem.aspire");

        NavigationRestorationPreparationResult.Prepared prepared =
            Assert.IsType<
                NavigationRestorationPreparationResult.Prepared>(
                NavigationTransitions.PrepareRestoration(
                    fixture.Workspace.Identity,
                    fixture.Facts(ecosystem),
                    fixture.Registry,
                    new()));

        StructuralSubjectIdentity.EcosystemSubject subject =
            Assert.IsType<StructuralSubjectIdentity.EcosystemSubject>(
                prepared.Initialization.State.CurrentSnapshot.ActiveSubject);
        Assert.Same(ecosystem.Occurrence, subject.Occurrence);
        Assert.Equal(
            "ecosystem.overview",
            prepared.Initialization.State.CurrentSnapshot
                .LensOutcome.EffectiveLens!.Facet.Value);
    }

    [Fact]
    public async Task
        CanonicalRestoration_EcosystemFactsCanSelectWorkspace()
    {
        await using EcosystemFixture fixture =
            await EcosystemFixture.CreateAsync("ecosystem.aspire");
        NavigationEcosystemEvaluation ecosystem =
            fixture.Evaluation("ecosystem.aspire");
        StructuralSubjectIdentity.WorkspaceSubject workspace =
            StructuralSubjectIdentity.ForWorkspace(
                fixture.Workspace.Identity);

        NavigationRestorationPreparationResult.Prepared prepared =
            Assert.IsType<
                NavigationRestorationPreparationResult.Prepared>(
                NavigationTransitions.PrepareRestoration(
                    fixture.Workspace.Identity,
                    fixture.Facts(ecosystem),
                    fixture.Registry,
                    new(workspace)));

        NavigationWorkspaceSnapshot snapshot =
            prepared.Initialization.State.CurrentSnapshot;
        Assert.Equal(workspace, snapshot.ActiveSubject);
        Assert.Same(ecosystem.Occurrence, snapshot.Ecosystem!.Occurrence);
        Assert.Null(snapshot.RetainedContext);
    }

    [Fact]
    public async Task
        CanonicalRestoration_EcosystemMaintenanceRetainsSubjectAndLens()
    {
        await using EcosystemFixture fixture =
            await EcosystemFixture.CreateAsync("ecosystem.aspire");
        NavigationEcosystemEvaluation ecosystem =
            fixture.Evaluation("ecosystem.aspire");
        NavigationEvaluationFacts facts = fixture.Facts(ecosystem);
        StructuralSubjectIdentity.EcosystemSubject subject =
            fixture.Subject(ecosystem);
        var lens = new NavigationLensIdentity(
            subject,
            new ViewFacetId("ecosystem.overview"));
        NavigationRestorationPreparationResult.Prepared prepared =
            Assert.IsType<
                NavigationRestorationPreparationResult.Prepared>(
                NavigationTransitions.PrepareRestoration(
                    fixture.Workspace.Identity,
                    facts,
                    fixture.Registry,
                    new(subject, Lens: lens)));
        NavigationState acknowledged =
            Acknowledge(prepared.Initialization);

        NavigationTransition queued =
            NavigationTransitions.QueueMaintenance(acknowledged);
        NavigationTransition begun =
            NavigationTransitions.Advance(queued.State);
        NavigationEvaluationRequest work = begun.Work!;
        NavigationEvaluationResult evaluation =
            NavigationTransitions.Evaluate(
                work,
                new NavigationPreparation.Ready(facts),
                fixture.Registry);
        NavigationTransition completed =
            NavigationTransitions.Complete(
                begun.State,
                work,
                evaluation);

        Assert.Equal(
            NavigationOutcomeKind.Applied,
            completed.Result!.Consumer.Outcome.Kind);
        NavigationWorkspaceSnapshot snapshot =
            completed.State.CurrentSnapshot;
        Assert.Equal(subject, snapshot.ActiveSubject);
        Assert.Same(subject.Occurrence, snapshot.Ecosystem!.Occurrence);
        Assert.Equal(lens, snapshot.LensOutcome.EffectiveLens);
    }

    [Fact]
    public async Task
        CanonicalRestoration_WorkspaceCanActivatePreparedEcosystem()
    {
        await using EcosystemFixture fixture =
            await EcosystemFixture.CreateAsync("ecosystem.aspire");
        NavigationEcosystemEvaluation ecosystem =
            fixture.Evaluation("ecosystem.aspire");
        NavigationEvaluationFacts facts = fixture.Facts(ecosystem);
        StructuralSubjectIdentity.WorkspaceSubject workspace =
            StructuralSubjectIdentity.ForWorkspace(
                fixture.Workspace.Identity);
        NavigationRestorationPreparationResult.Prepared prepared =
            Assert.IsType<
                NavigationRestorationPreparationResult.Prepared>(
                NavigationTransitions.PrepareRestoration(
                    fixture.Workspace.Identity,
                    facts,
                    fixture.Registry,
                    new(workspace)));
        NavigationAction action =
            prepared.Initialization.Result.Consumer.Snapshot.Hierarchy
                .Single(row =>
                    row.Kind == StructuralSubjectKind.Ecosystem)
                .Action!;

        NavigationTransition begun =
            NavigationTransitions.Begin(
                prepared.Initialization.State,
                action);
        NavigationEvaluationRequest work = begun.Work!;
        NavigationEvaluationResult evaluation =
            NavigationTransitions.Evaluate(
                work,
                new NavigationPreparation.Ready(facts),
                fixture.Registry);
        NavigationTransition completed =
            NavigationTransitions.Complete(
                begun.State,
                work,
                evaluation);

        Assert.Equal(
            NavigationOutcomeKind.Applied,
            completed.Result!.Consumer.Outcome.Kind);
        StructuralSubjectIdentity.EcosystemSubject subject =
            Assert.IsType<StructuralSubjectIdentity.EcosystemSubject>(
                completed.State.CurrentSnapshot.ActiveSubject);
        Assert.Same(ecosystem.Occurrence, subject.Occurrence);
    }

    [Fact]
    public async Task
        CanonicalRestoration_RejectsForeignEcosystemFacts()
    {
        await using EcosystemFixture first =
            await EcosystemFixture.CreateAsync("ecosystem.aspire");
        await using EcosystemFixture second =
            await EcosystemFixture.CreateAsync("ecosystem.aspire");
        var facts = new NavigationEvaluationFacts(
            first.Scope,
            Package: null,
            first.Availability)
        {
            Ecosystem = second.Evaluation("ecosystem.aspire"),
        };

        NavigationRestorationPreparationResult.Rejected rejected =
            Assert.IsType<
                NavigationRestorationPreparationResult.Rejected>(
                NavigationTransitions.PrepareRestoration(
                    first.Workspace.Identity,
                    facts,
                    first.Registry,
                    new()));

        Assert.Equal(
            NavigationRestorationRejectionKind.InvalidContext,
            rejected.Kind);
    }

    [Fact]
    public async Task
        CanonicalRestoration_RejectsMismatchedEcosystemSubject()
    {
        await using EcosystemFixture fixture =
            await EcosystemFixture.CreateAsync(
                "ecosystem.aspire",
                "ecosystem.extensions");
        NavigationEcosystemEvaluation aspire =
            fixture.Evaluation("ecosystem.aspire");
        StructuralSubjectIdentity.EcosystemSubject extensions =
            fixture.Subject(
                fixture.Evaluation("ecosystem.extensions"));

        NavigationRestorationPreparationResult.Rejected rejected =
            Assert.IsType<
                NavigationRestorationPreparationResult.Rejected>(
                NavigationTransitions.PrepareRestoration(
                    fixture.Workspace.Identity,
                    fixture.Facts(aspire),
                    fixture.Registry,
                    new(extensions)));

        Assert.Equal(
            NavigationRestorationRejectionKind.InvalidContext,
            rejected.Kind);
    }

    [Fact]
    public async Task
        CanonicalRestoration_RejectsEcosystemWithPackageFacts()
    {
        await using Fixture packageFixture = await Fixture.CreateAsync();
        WorkspaceRegistrationRevision registrations =
            Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                packageFixture.Workspace.GetRegistrationSnapshot()).Revision;
        var declaration = new WorkspaceEcosystemRegistrationDeclaration(
            WorkspaceEcosystemRegistrationId.Create("ecosystem.aspire"),
            ["Aspire"],
            [],
            []);
        WorkspaceRegistrationRevision updated =
            Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
                packageFixture.Workspace.ReplaceRegistrations(
                    registrations,
                    [new WorkspaceRegistration.Ecosystem(declaration)]))
                .Revision;
        WorkspaceEcosystemRegistrationOccurrence occurrence =
            Assert.Single(updated.EcosystemContributions).Ecosystem;
        var facts = new NavigationEvaluationFacts(
            packageFixture.Scope,
            packageFixture.Packages[0],
            packageFixture.Availability)
        {
            Ecosystem = new(updated, occurrence),
        };

        Assert.Throws<ArgumentException>(
            () => NavigationTransitions.PrepareRestoration(
                packageFixture.Workspace.Identity,
                facts,
                packageFixture.Registry,
                new()));
    }

    [Fact]
    public async Task
        CanonicalRestoration_RejectsEcosystemWithRetainedPackageContext()
    {
        await using Fixture packageFixture = await Fixture.CreateAsync();
        WorkspaceRegistrationRevision registrations =
            Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                packageFixture.Workspace.GetRegistrationSnapshot()).Revision;
        var declaration = new WorkspaceEcosystemRegistrationDeclaration(
            WorkspaceEcosystemRegistrationId.Create("ecosystem.aspire"),
            ["Aspire"],
            [],
            []);
        WorkspaceRegistrationRevision updated =
            Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
                packageFixture.Workspace.ReplaceRegistrations(
                    registrations,
                    [new WorkspaceRegistration.Ecosystem(declaration)]))
                .Revision;
        WorkspaceEcosystemRegistrationOccurrence occurrence =
            Assert.Single(updated.EcosystemContributions).Ecosystem;
        StructuralSubjectIdentity.PackageSubject package =
            packageFixture.Session.CurrentSnapshot.Inventory!.Package;
        var facts = new NavigationEvaluationFacts(
            packageFixture.Scope,
            Package: null,
            packageFixture.Availability)
        {
            Ecosystem = new(updated, occurrence),
        };

        NavigationRestorationPreparationResult.Rejected rejected =
            Assert.IsType<
                NavigationRestorationPreparationResult.Rejected>(
                NavigationTransitions.PrepareRestoration(
                    packageFixture.Workspace.Identity,
                    facts,
                    packageFixture.Registry,
                    new(
                        Context:
                            new NavigationRetainedSubjectContext(package))));

        Assert.Equal(
            NavigationRestorationRejectionKind.InvalidContext,
            rejected.Kind);
    }

    [Theory]
    [InlineData("ecosystem.unknown")]
    [InlineData("package.overview")]
    public async Task
        CanonicalRestoration_RejectsUnknownOrInapplicableEcosystemLens(
            string facet)
    {
        await using EcosystemFixture fixture =
            await EcosystemFixture.CreateAsync("ecosystem.aspire");
        NavigationEcosystemEvaluation ecosystem =
            fixture.Evaluation("ecosystem.aspire");
        StructuralSubjectIdentity.EcosystemSubject subject =
            fixture.Subject(ecosystem);

        NavigationRestorationPreparationResult.Rejected rejected =
            Assert.IsType<
                NavigationRestorationPreparationResult.Rejected>(
                NavigationTransitions.PrepareRestoration(
                    fixture.Workspace.Identity,
                    fixture.Facts(ecosystem),
                    fixture.Registry,
                    new(
                        subject,
                        Lens: new NavigationLensIdentity(
                            subject,
                            new ViewFacetId(facet)))));

        Assert.Equal(
            NavigationRestorationRejectionKind.Registry,
            rejected.Kind);
    }

    [Fact]
    public async Task CanonicalRestoration_PreparedPairEqualsExactRequest()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.CurrentSnapshot;
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
            initialization.State.CurrentSnapshot.ActiveSubject);
        Assert.Equal(
            lens,
            initialization.State.CurrentSnapshot
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
            fixture.Session.CurrentSnapshot;
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
            prepared.Initialization.State.CurrentSnapshot
                .LensOutcome.EffectiveLens);
        NavigationLensEvaluationBasis.ExactRequest exact =
            Assert.IsType<NavigationLensEvaluationBasis.ExactRequest>(
                prepared.Initialization.State.CurrentSnapshot
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
            fixture.Session.CurrentSnapshot;
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
            fixture.Session.CurrentSnapshot;
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
            fixture.Session.CurrentSnapshot;
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
            fixture.Session.CurrentSnapshot;
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
            prepared.Initialization.State.CurrentSnapshot
                .LensOutcome.EffectiveLens);
    }

    [Fact]
    public async Task
        CanonicalRestoration_RejectsSubjectlessLowerRetainedPath()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.CurrentSnapshot;
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
            first.Session.CurrentSnapshot;
        NavigationWorkspaceSnapshot secondInstalled =
            second.Session.CurrentSnapshot;
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
            first.Session.CurrentSnapshot;
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
            fixture.Session.CurrentSnapshot;
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
            fixture.Session.CurrentSnapshot;
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
            fixture.Session.CurrentSnapshot;
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
            prepared.Initialization.State.CurrentSnapshot
                .TypeInventoryLibraryContext);
        Assert.Equal(
            type.Library,
            prepared.Initialization.State.CurrentSnapshot
                .RetainedContext!.Library);
    }

    [Fact]
    public async Task CanonicalRestoration_ExactPackageRootIsPrepared()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.CurrentSnapshot;
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
            prepared.Initialization.State.CurrentSnapshot.ActiveSubject);
        Assert.Equal(
            lens,
            prepared.Initialization.State.CurrentSnapshot
                .LensOutcome.EffectiveLens);
    }

    [Fact]
    public async Task
        CanonicalRestoration_NonReadyPackageFailsWithoutRegistryResolution()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.CurrentSnapshot;
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
            fixture.Session.CurrentSnapshot;
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
            firstResult.Initialization.State.CurrentSnapshot.RetainedContext,
            secondResult.Initialization.State.CurrentSnapshot.RetainedContext);
        Assert.Equal(
            first.Library,
            firstResult.Initialization.State.CurrentSnapshot
                .TypeInventoryLibraryContext);
        Assert.Equal(
            second.Library,
            secondResult.Initialization.State.CurrentSnapshot
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
            prepared.Initialization.State.CurrentSnapshot.ActiveSubject);
        Assert.NotEmpty(
            prepared.Initialization.State.CurrentSnapshot
                .Inventory!.Libraries[0].Types.Evidence);
    }

    [Fact]
    public async Task
        CanonicalRestoration_EqualInputsIssueIndependentStateAndAuthority()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationWorkspaceSnapshot installed =
            fixture.Session.CurrentSnapshot;
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
                first.Initialization.State.CurrentSnapshot,
                second.Initialization.State.CurrentSnapshot));

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

    static NavigationState Acknowledge(
        NavigationOperationInitialization initialization)
    {
        NavigationEffectAuthority authority =
            initialization.Result.Consumer.Authority!;
        NavigationState posted =
            NavigationTransitions.RecordConsumerPosting(
                initialization.State,
                authority).State;
        return NavigationTransitions.Acknowledge(
            posted,
            authority).State;
    }

    sealed class EcosystemFixture : IAsyncDisposable
    {
        EcosystemFixture(
            InspectionWorkspace workspace,
            WorkspaceRegistrationRevision registrations,
            WorkspaceScopeSnapshot scope)
        {
            Workspace = workspace;
            Registrations = registrations;
            Scope = scope;
            Registry = InspectionViewFacetCatalog.Registry;
            ViewFacetAvailabilitySnapshot available =
                NavigationSnapshotTestData.AllAvailable(Registry);
            Availability = (_, _) => available;
        }

        public InspectionWorkspace Workspace { get; }

        public WorkspaceRegistrationRevision Registrations { get; }

        public WorkspaceScopeSnapshot Scope { get; }

        public ViewFacetRegistry Registry { get; }

        public NavigationFacetAvailabilityProvider Availability { get; }

        public static async ValueTask<EcosystemFixture> CreateAsync(
            params string[] ids)
        {
            var workspace = new InspectionWorkspace(
                [
                    .. ids.Select(
                        id => new WorkspaceRegistration.Ecosystem(
                            new WorkspaceEcosystemRegistrationDeclaration(
                                WorkspaceEcosystemRegistrationId.Create(id),
                                [NamespaceRoot(id)],
                                [],
                                []))),
                ]);
            WorkspaceRegistrationRevision registrations =
                Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                    workspace.GetRegistrationSnapshot()).Revision;
            WorkspaceScopeSnapshot scope =
                Assert.IsType<WorkspaceScopeReadResult.Available>(
                    await workspace.GetScopeSnapshotAsync()).Snapshot;
            return new(workspace, registrations, scope);
        }

        public NavigationEcosystemEvaluation Evaluation(string id)
        {
            WorkspaceEcosystemRegistrationOccurrence occurrence =
                Registrations.EcosystemContributions
                    .Select(static contribution =>
                        contribution.Ecosystem)
                    .Single(ecosystem =>
                        ecosystem.Declaration.Id.Value == id);
            return new(Registrations, occurrence);
        }

        public NavigationEvaluationFacts Facts(
            NavigationEcosystemEvaluation ecosystem) =>
            new(
                Scope,
                Package: null,
                Availability)
            {
                Ecosystem = ecosystem,
            };

        public StructuralSubjectIdentity.EcosystemSubject Subject(
            NavigationEcosystemEvaluation ecosystem) =>
            StructuralSubjectIdentity.ForEcosystem(
                StructuralSubjectIdentity.ForWorkspace(
                    Workspace.Identity),
                ecosystem.Occurrence,
                ecosystem.Id);

        public ValueTask DisposeAsync() => Workspace.DisposeAsync();

        static string NamespaceRoot(string id) =>
            id == "ecosystem.aspire"
                ? "Aspire"
                : "Microsoft.Extensions";
    }

    sealed record Diagnostic : IViewFacetDiagnosticEvidence;
}
