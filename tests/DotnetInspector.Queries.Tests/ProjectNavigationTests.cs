using System.Text;

using DotnetInspector.Packages;
using InertText;

namespace DotnetInspector.Queries.Tests;

public sealed class ProjectNavigationTests
{
    static readonly ViewFacetRegistry Registry =
        InspectionViewFacetCatalog.Registry;
    static readonly ViewFacetAvailabilitySnapshot Available =
        NavigationSnapshotTestData.AllAvailable(Registry);

    [Fact]
    public async Task ProjectIdentityUsesTheExactWorkspaceOccurrence()
    {
        WorkspacePlan plan = Plan(Input("authored-incomplete"));
        await using var first = new InspectionWorkspace(plan);
        await using var second = new InspectionWorkspace(plan);
        WorkspaceScopeSnapshot firstScope = await Current(first);
        WorkspaceScopeSnapshot secondScope = await Current(second);
        StructuralSubjectIdentity.ProjectSubject project = Project(firstScope);
        StructuralSubjectIdentity.ProjectSubject other = Project(secondScope);

        Assert.Same(firstScope.Project, project.Occurrence);
        Assert.Same(plan.Project, project.Occurrence.Input);
        Assert.Equal(Project(firstScope), project);
        Assert.NotEqual(project, other);
        Assert.False(project.IsPortable);
        Assert.Throws<ArgumentException>(() =>
            StructuralSubjectIdentity.ForProject(project.Workspace, other.Occurrence));
        var rejected = Assert.IsType<NavigationRestorationPreparationResult.Rejected>(
            NavigationTransitions.PrepareRestoration(
                first.Identity, Facts(firstScope), Registry, new(other)));
        Assert.Equal(NavigationRestorationRejectionKind.InvalidContext, rejected.Kind);

        await using var noProject = new InspectionWorkspace();
        NavigationOperationInitialization control = Open(await Current(noProject));
        Assert.Null(control.State.CurrentSnapshot.Project);
        Assert.DoesNotContain(control.Result.Consumer.Snapshot.Hierarchy,
            row => row.Kind == StructuralSubjectKind.Project);
    }

    [Theory]
    [InlineData("authored-incomplete")]
    [InlineData("restored")]
    [InlineData("target-missing")]
    [InlineData("graph-failed")]
    public async Task ProjectSelectionRetainsOriginalPartialOrRestoredEvidence(string evidence)
    {
        WorkspaceProjectInput input = Input(evidence);
        await using var workspace = new InspectionWorkspace(Plan(input));
        WorkspaceScopeSnapshot scope = await Current(workspace);
        StructuralSubjectIdentity.ProjectSubject project = Project(scope);

        NavigationOperationInitialization opened = Open(scope, new(project));
        NavigationWorkspaceSnapshot snapshot = opened.State.CurrentSnapshot;

        Assert.Equal(project, snapshot.ActiveSubject);
        Assert.Same(input, snapshot.Project!.Occurrence.Input);
        Assert.Equal("project.overview", snapshot.LensOutcome.EffectiveLens!.Facet.Value);
        Assert.Empty(snapshot.Packages);
        Assert.Null(snapshot.ActiveOccurrence);
        Assert.Null(snapshot.RetainedContext);
        Assert.Equal(
            ["project.overview", "project.dependencies"],
            snapshot.Lenses.Select(row => row.Option.Descriptor.Id.Value));
        Assert.Equal(input.DeclaredName.ToString(),
            opened.Result.Consumer.Snapshot.ActiveSubject.Label);
    }

    [Fact]
    public async Task WorkspaceActionSelectsProjectAndExactDependenciesLens()
    {
        await using var workspace = new InspectionWorkspace(Plan(Input("authored-incomplete")));
        WorkspaceScopeSnapshot scope = await Current(workspace);
        NavigationOperationInitialization initial = Open(scope);
        Assert.Equal(StructuralSubjectKind.Workspace, initial.State.CurrentSnapshot.ActiveSubject.Kind);
        NavigationConsumerSubjectDescriptor descriptor = initial.Result.Consumer.Snapshot.Hierarchy
            .Single(row => row.Kind == StructuralSubjectKind.Project);
        Assert.Equal(initial.Result.Consumer.Snapshot.Workspace.Id, descriptor.Subject!.Parent);

        NavigationTransition selected = Execute(initial.State, descriptor.Action!, Facts(scope));
        NavigationTransition dependencies = Execute(
            selected.State,
            selected.Result!.Consumer.Snapshot.Lenses
                .Single(row => row.Facet.Id == "project.dependencies").Action!,
            Facts(scope));

        Assert.Equal(Project(scope), dependencies.State.CurrentSnapshot.ActiveSubject);
        Assert.Equal("project.dependencies",
            dependencies.State.CurrentSnapshot.LensOutcome.EffectiveLens!.Facet.Value);
        Assert.Equal(NavigationLensBasisKind.ExactRequest,
            dependencies.Result!.Consumer.Snapshot.LensOutcome.Basis);
    }

    [Fact]
    public async Task ProjectSelectionPreservesAPreparedEcosystemPeer()
    {
        var declaration = new WorkspaceEcosystemRegistrationDeclaration(
            WorkspaceEcosystemRegistrationId.Create("ecosystem.aspire"), ["Aspire"], [], []);
        await using var workspace = new InspectionWorkspace(new WorkspacePlan(
            TraversalTargetFrameworkPolicy.ProductDefault,
            [new WorkspaceRegistration.Ecosystem(declaration)], [],
            Input("authored-incomplete")));
        WorkspaceScopeSnapshot scope = await Current(workspace);
        var registrations = Assert.IsType<WorkspaceRegistrationReadResult.Available>(
            workspace.GetRegistrationSnapshot());
        var ecosystem = new NavigationEcosystemEvaluation(
            registrations.Revision,
            Assert.Single(registrations.Revision.EcosystemContributions).Ecosystem);
        var facts = new NavigationEvaluationFacts(scope, null, (_, _) => Available)
        {
            Ecosystem = ecosystem,
        };
        NavigationOperationInitialization initial = NavigationTransitions.Initialize(
            workspace.Identity, facts, Registry,
            new(StructuralSubjectIdentity.ForWorkspace(workspace.Identity)));
        NavigationConsumerSubjectDescriptor originalEcosystem =
            initial.Result.Consumer.Snapshot.Hierarchy
                .Single(row => row.Kind == StructuralSubjectKind.Ecosystem);
        NavigationConsumerSubjectDescriptor project =
            initial.Result.Consumer.Snapshot.Hierarchy
                .Single(row => row.Kind == StructuralSubjectKind.Project);
        Assert.Equal(NavigationDescriptorState.Available, originalEcosystem.State);
        Assert.NotNull(originalEcosystem.Action);
        Assert.Equal(NavigationDescriptorState.Available, project.State);
        Assert.NotNull(project.Action);

        NavigationTransition selected = Execute(initial.State, project.Action, facts);

        Assert.Equal(Project(scope), selected.State.CurrentSnapshot.ActiveSubject);
        Assert.Equal(initial.State.CurrentSnapshot.Ecosystem, selected.State.CurrentSnapshot.Ecosystem);
        Assert.Null(selected.State.CurrentSnapshot.ActiveOccurrence);
        Assert.Null(selected.State.CurrentSnapshot.RetainedContext);
        Assert.Empty(selected.State.CurrentSnapshot.Libraries);
        Assert.Empty(selected.State.CurrentSnapshot.Types);
        NavigationConsumerSubjectDescriptor retainedEcosystem =
            selected.Result!.Consumer.Snapshot.Hierarchy
                .Single(row => row.Kind == StructuralSubjectKind.Ecosystem);
        Assert.Equal(NavigationDescriptorState.Available, retainedEcosystem.State);
        Assert.Equal(originalEcosystem.Subject!.Id, retainedEcosystem.Subject!.Id);
        Assert.NotNull(retainedEcosystem.Action);

        NavigationTransition returned = Execute(selected.State, retainedEcosystem.Action, facts);

        Assert.Equal(initial.State.CurrentSnapshot.Ecosystem, returned.State.CurrentSnapshot.ActiveSubject);
        NavigationConsumerSubjectDescriptor retainedProject =
            returned.Result!.Consumer.Snapshot.Hierarchy
                .Single(row => row.Kind == StructuralSubjectKind.Project);
        Assert.Equal(NavigationDescriptorState.Available, retainedProject.State);
        Assert.Equal(project.Subject!.Id, retainedProject.Subject!.Id);
        Assert.NotNull(retainedProject.Action);
    }

    [Fact]
    public async Task ExplicitPackageAdmissionDrillsDownAndProjectRemainsAWorkspacePeer()
    {
        await using var workspace = new InspectionWorkspace(Plan(Input("authored-incomplete")));
        WorkspaceScopeSnapshot scope = await Current(workspace);
        StructuralSubjectIdentity.ProjectSubject project = Project(scope);
        var lens = new NavigationLensIdentity(project, new("project.dependencies"));
        NavigationOperationInitialization initial = Open(scope, new(project, Lens: lens));
        PackageRootBinding binding = NavigationSnapshotTestData.Binding("Markout");
        WorkspaceScopeRequest request = workspace.IssueAddPackagesRequest(
            scope.Revision, [binding], DateTimeOffset.UtcNow.AddMinutes(5),
            workspace.CreateScopePackageTarget(binding));
        NavigationPackageEvaluation? preparedPackage = null;

        NavigationTransition admitted = await Settle(
            workspace, Acknowledge(initial.State, initial.Result.Consumer.Authority!), request,
            current => preparedPackage = Package(current, binding));
        NavigationPackageEvaluation package = Assert.IsType<NavigationPackageEvaluation>(preparedPackage);
        NavigationWorkspaceSnapshot snapshot = admitted.State.CurrentSnapshot;
        Assert.Same(snapshot.Scope.Packages[0].Occurrence, snapshot.ActiveOccurrence);
        var library = Assert.IsType<StructuralSubjectIdentity.LibrarySubject>(snapshot.ActiveSubject);
        Assert.Same(snapshot.ActiveOccurrence, library.Package.Occurrence);
        NavigationConsumerSnapshot consumer = admitted.Result!.Consumer.Snapshot;
        Assert.Equal(consumer.Workspace.Id, Assert.Single(consumer.Packages).Subject.Parent);
        NavigationTransition type = Execute(
            Acknowledge(admitted.State, admitted.Result.Consumer.Authority!),
            Assert.Single(consumer.Types).Navigation.Action!,
            Facts(snapshot.Scope, package));
        Assert.IsType<StructuralSubjectIdentity.TypeSubject>(type.State.CurrentSnapshot.ActiveSubject);
        consumer = type.Result!.Consumer.Snapshot;
        NavigationConsumerSubjectDescriptor projectRow = consumer.Hierarchy
            .Single(row => row.Kind == StructuralSubjectKind.Project);
        Assert.Equal(consumer.Workspace.Id, projectRow.Subject!.Parent);
        Assert.NotNull(projectRow.Action);

        NavigationTransition returned = Execute(
            type.State, projectRow.Action!, Facts(snapshot.Scope));
        Assert.Equal(project, returned.State.CurrentSnapshot.ActiveSubject);
        Assert.Null(returned.State.CurrentSnapshot.ActiveOccurrence);
        Assert.Null(returned.State.CurrentSnapshot.RetainedContext);
        Assert.Empty(returned.State.CurrentSnapshot.Types);
        Assert.Single(returned.State.CurrentSnapshot.Packages);

        NavigationOperationInitialization restored = Open(snapshot.Scope, new(project, Lens: lens));
        Assert.Equal(project, restored.State.CurrentSnapshot.ActiveSubject);
        Assert.Equal(lens, restored.State.CurrentSnapshot.LensOutcome.EffectiveLens);

        NavigationTransition packageAgain = Execute(
            returned.State, Assert.Single(returned.Result!.Consumer.Snapshot.Packages).Action!,
            Facts(snapshot.Scope, package));
        Assert.Same(snapshot.ActiveOccurrence, packageAgain.State.CurrentSnapshot.ActiveOccurrence);
    }

    [Fact]
    public async Task ProjectAndExactLensSurviveMaintenanceAndTargetlessPackageEdits()
    {
        await using var workspace = new InspectionWorkspace(Plan(Input("restored")));
        WorkspaceScopeSnapshot scope = await Current(workspace);
        StructuralSubjectIdentity.ProjectSubject project = Project(scope);
        var lens = new NavigationLensIdentity(project, new("project.dependencies"));
        NavigationOperationInitialization initial = Open(scope, new(project, Lens: lens));
        NavigationTransition queued = NavigationTransitions.QueueMaintenance(
            Acknowledge(initial.State, initial.Result.Consumer.Authority!));
        NavigationTransition begun = NavigationTransitions.Advance(queued.State);
        NavigationEvaluationResult evaluation = NavigationTransitions.Evaluate(
            begun.Work!, new NavigationPreparation.Ready(Facts(scope)), Registry);
        NavigationTransition maintained = NavigationTransitions.Complete(
            begun.State, begun.Work!, evaluation);
        Assert.Equal(project, maintained.State.CurrentSnapshot.ActiveSubject);
        Assert.Equal(lens, maintained.State.CurrentSnapshot.LensOutcome.EffectiveLens);

        PackageRootBinding binding = NavigationSnapshotTestData.Binding("Selected.Dependency");
        NavigationTransition added = await Settle(
            workspace, Acknowledge(maintained.State, maintained.Result!.Consumer.Authority!),
            workspace.IssueAddPackagesRequest(
                scope.Revision, [binding], DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.Equal(project, added.State.CurrentSnapshot.ActiveSubject);
        Assert.Equal(lens, added.State.CurrentSnapshot.LensOutcome.EffectiveLens);
        Assert.Single(added.State.CurrentSnapshot.Packages);
        Assert.Null(added.State.CurrentSnapshot.ActiveOccurrence);

        NavigationTransition cleared = await Settle(
            workspace, Acknowledge(added.State, added.Result!.Consumer.Authority!),
            workspace.IssueClearScopeRequest(
                added.State.CurrentSnapshot.Scope.Revision, DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.Equal(project, cleared.State.CurrentSnapshot.ActiveSubject);
        Assert.Equal(lens, cleared.State.CurrentSnapshot.LensOutcome.EffectiveLens);
        Assert.Same(scope.Project, cleared.State.CurrentSnapshot.Project!.Occurrence);
        Assert.Empty(cleared.State.CurrentSnapshot.Packages);
    }

    [Fact]
    public async Task FailedPackageRealizationDoesNotDisableProjectSelection()
    {
        await using var workspace = new InspectionWorkspace(Plan(Input("authored-incomplete")));
        PackageRootBinding binding = NavigationSnapshotTestData.Binding("Markout");
        WorkspaceScopeSnapshot scope = await NavigationSnapshotTestData.ReplaceAsync(workspace, binding);
        NavigationPackageEvaluation package = Package(scope, binding);
        NavigationOperationInitialization initial = NavigationTransitions.Initialize(
            workspace.Identity, Facts(scope, package), Registry);
        var failed = new WorkspaceScopeSnapshot(
            scope.Revision, scope.PhysicalComposition,
            [new(scope.Packages[0].Occurrence, scope.Packages[0].Realization with
            {
                Status = new ArtifactRootRealizationStatus.Failed(ArtifactRootFailure.PreparationFailed),
            })],
            scope.Closure, scope.Preparing);
        var facts = new NavigationEvaluationFacts(
            failed, Package: null, (_, _) => Available,
            new NavigationNonReadyPackageEvaluation(failed.Packages[0]));
        NavigationTransition queued = NavigationTransitions.QueueMaintenance(
            Acknowledge(initial.State, initial.Result.Consumer.Authority!));
        NavigationTransition begun = NavigationTransitions.Advance(queued.State);
        NavigationTransition refreshed = NavigationTransitions.Complete(
            begun.State, begun.Work!,
            NavigationTransitions.Evaluate(
                begun.Work!, new NavigationPreparation.Ready(facts), Registry));
        NavigationConsumerSubjectDescriptor project = refreshed.Result!.Consumer.Snapshot.Hierarchy
            .Single(row => row.Kind == StructuralSubjectKind.Project);
        Assert.Equal(NavigationDescriptorState.Available, project.State);
        NavigationAction action = refreshed.Result.Consumer.Snapshot.Hierarchy
            .Single(row => row.Kind == StructuralSubjectKind.Project).Action!;

        NavigationTransition selected = Execute(refreshed.State, action, Facts(failed));

        Assert.Equal(Project(scope), selected.State.CurrentSnapshot.ActiveSubject);
        Assert.Equal("project.overview",
            selected.State.CurrentSnapshot.LensOutcome.EffectiveLens!.Facet.Value);
        Assert.Null(selected.State.CurrentSnapshot.ActiveOccurrence);
        Assert.Equal(NavigationRealizationKind.Failed,
            Assert.Single(selected.Result!.Consumer.Snapshot.Packages).Realization);
    }

    [Fact]
    public async Task UnavailableExactProjectLensIsNotReplacedByOverview()
    {
        await using var workspace = new InspectionWorkspace(Plan(Input("restored")));
        WorkspaceScopeSnapshot scope = await Current(workspace);
        StructuralSubjectIdentity.ProjectSubject project = Project(scope);
        var lens = new NavigationLensIdentity(project, new("project.dependencies"));
        var availability = new ViewFacetAvailabilitySnapshot(Registry.Descriptors.Select(descriptor =>
            new ViewFacetAvailabilityFact(descriptor.Id,
                descriptor.Id == lens.Facet
                    ? new ViewFacetAvailability.Unavailable(
                        ViewFacetUnavailableReason.CapabilityAbsent("Dependency view is unavailable."))
                    : ViewFacetAvailability.Available.Instance)));
        var prepared = Assert.IsType<NavigationRestorationPreparationResult.Prepared>(
            NavigationTransitions.PrepareRestoration(
                workspace.Identity, new(scope, null, (_, _) => availability),
                Registry, new(project, Lens: lens)));

        Assert.Equal(NavigationOutcomeKind.Unavailable, prepared.Initialization.Result.Consumer.Outcome.Kind);
        Assert.Null(prepared.Initialization.State.CurrentSnapshot.LensOutcome.EffectiveLens);
        var exact = Assert.IsType<NavigationLensEvaluationBasis.ExactRequest>(
            prepared.Initialization.State.CurrentSnapshot.LensOutcome.Basis);
        Assert.Equal(lens, exact.Request);
    }

    static WorkspacePlan Plan(WorkspaceProjectInput input) =>
        new(TraversalTargetFrameworkPolicy.ProductDefault, [], [], input);

    static async Task<WorkspaceScopeSnapshot> Current(InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceScopeReadResult.Available>(
            await workspace.GetScopeSnapshotAsync()).Snapshot;

    static StructuralSubjectIdentity.ProjectSubject Project(WorkspaceScopeSnapshot scope) =>
        StructuralSubjectIdentity.ForProject(
            StructuralSubjectIdentity.ForWorkspace(scope.Revision.Workspace), scope.Project!);

    static NavigationEvaluationFacts Facts(
        WorkspaceScopeSnapshot scope, NavigationPackageEvaluation? package = null) =>
        new(scope, package, (_, _) => Available);

    static NavigationOperationInitialization Open(
        WorkspaceScopeSnapshot scope, NavigationInitialization? initialization = null) =>
        Assert.IsType<NavigationRestorationPreparationResult.Prepared>(
            NavigationTransitions.PrepareRestoration(
                scope.Revision.Workspace, Facts(scope), Registry, initialization ?? new())).Initialization;

    static NavigationPackageEvaluation Package(WorkspaceScopeSnapshot scope, PackageRootBinding binding) =>
        NavigationSnapshotTestData.PackageEvaluation(
            Assert.Single(scope.Packages), binding,
            NavigationSnapshotTestData.Surface("Navigation.Library",
                NavigationSnapshotTestData.Type("Widget", NavigationSnapshotTestData.Member("Run"))));

    static NavigationTransition Execute(
        NavigationState state, NavigationAction action, NavigationEvaluationFacts facts)
    {
        NavigationTransition begun = NavigationTransitions.Begin(state, action);
        if (action.Kind == NavigationOperationKind.Subject
            && begun.Work!.ConsumerRequest!.Destination.Kind == StructuralSubjectKind.Project)
        {
            Assert.Null(begun.Work.Occurrence);
        }
        NavigationEvaluationResult evaluation = NavigationTransitions.Evaluate(
            begun.Work!, new NavigationPreparation.Ready(facts), Registry);
        NavigationTransition completed = NavigationTransitions.Complete(
            begun.State, begun.Work!, evaluation);
        Assert.Equal(NavigationOutcomeKind.Applied, completed.Result!.Consumer.Outcome.Kind);
        return completed;
    }

    static NavigationState Acknowledge(NavigationState state, NavigationEffectAuthority authority)
    {
        NavigationState posted = NavigationTransitions.RecordConsumerPosting(state, authority).State;
        return NavigationTransitions.Acknowledge(posted, authority).State;
    }

    static async Task<NavigationTransition> Settle(
        InspectionWorkspace workspace, NavigationState state, WorkspaceScopeRequest request,
        Func<WorkspaceScopeSnapshot, NavigationPackageEvaluation?>? prepare = null)
    {
        NavigationTransition accepted = NavigationTransitions.AcceptScopeOperation(state, request.Association);
        var result = Assert.IsType<WorkspaceScopeOperationResult.Committed>(
            await workspace.SubmitScopeRequestAsync(request, TestContext.Current.CancellationToken));
        NavigationScopeEvaluationResult evaluation = NavigationTransitions.EvaluateScopeOperation(
            accepted.ScopeWork!, result,
            new NavigationScopePreparation.Ready(Facts(result.Snapshot, prepare?.Invoke(result.Snapshot))),
            Registry);
        NavigationTransition completed = NavigationTransitions.CompleteScopeOperation(
            accepted.State, accepted.ScopeWork!, evaluation);
        Assert.Equal(NavigationScopeSettlementKind.Committed,
            completed.Result!.Consumer.Outcome.Scope!.Kind);
        return completed;
    }

    static WorkspaceProjectInput Input(string evidence)
    {
        if (evidence == "authored-incomplete")
        {
            var result = AuthoredProjectDependencyFactsQuery.Execute(Encoding.UTF8.GetBytes("""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>$(DefaultTargetFramework)</TargetFramework></PropertyGroup>
                  <ItemGroup><PackageReference Include="Markout" /></ItemGroup>
                </Project>
                """));
            Assert.IsType<AuthoredProjectDependencyFactsResult.Incomplete>(result);
            return new WorkspaceProjectInput.Authored(new(TextPolicy.Field, "DotnetInspect.Cli.csproj"), result);
        }

        byte[] bytes = Encoding.UTF8.GetBytes(evidence == "graph-failed"
            ? """
                {
                  "version": 3,
                  "targets": { "net11.0": {}, "NET11.0": {} },
                  "projectFileDependencyGroups": { "net11.0": [] },
                  "project": { "frameworks": { "net11.0": {} } }
                }
                """
            : """
                {
                  "version": 3,
                  "targets": { "net11.0": { "Newtonsoft.Json/13.0.3": { "type": "package" } } },
                  "projectFileDependencyGroups": { "net11.0": [ "Newtonsoft.Json >= 13.0.3" ] },
                  "project": { "frameworks": { "net11.0": {
                    "dependencies": { "Newtonsoft.Json": { "target": "Package", "version": "[13.0.3, )" } }
                  } } }
                }
                """);
        RestoredProjectDependencyTraversalResult restored =
            RestoredProjectDependencyTraversalQuery.Execute(bytes,
                evidence == "target-missing"
                    ? new(new RestoredProjectTargetRequest("net10.0"))
                    : null);
        switch (evidence)
        {
            case "restored":
                Assert.IsType<RestoredProjectDependencyTraversalResult.Available>(restored);
                break;
            case "target-missing":
                Assert.IsType<RestoredProjectDependencyTraversalResult.Unavailable>(restored);
                break;
            case "graph-failed":
                Assert.IsType<RestoredProjectDependencyTraversalFailure.Graph>(
                    Assert.IsType<RestoredProjectDependencyTraversalResult.Failed>(restored).Failure);
                break;
            default:
                throw new ArgumentException("Unknown Project evidence fixture.", nameof(evidence));
        }
        return new WorkspaceProjectInput.Restored(new(TextPolicy.Field, "project.assets.json"), restored);
    }
}
