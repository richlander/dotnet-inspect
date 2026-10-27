using System.Text;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserRetainedWorkspaceActivationTests
{
    [Theory]
    [InlineData("authored")]
    [InlineData("central")]
    [InlineData("restored")]
    [InlineData("target-unavailable")]
    [InlineData("partial-graph")]
    [InlineData("failed-graph")]
    public async Task ProjectActivationRetainsSharedEvidenceWithoutAcquiringPackages(string scenario)
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner = new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspaceInput.Project input = ProjectInput(scenario);
        BrowserRetainedWorkspaceActivationSession session = owner.BeginActivation(
            ProjectRequest("project", input), TestContext.Current.CancellationToken);
        var prepared = Assert.IsType<BrowserRetainedWorkspacePreparationResult.Prepared>(
            await session.Preparation);
        Assert.True(session.Commit());
        BrowserRetainedWorkspacePosting posting =
            Assert.IsType<BrowserRetainedWorkspaceActivationResult.Activated>(await session.Activation).Posting;
        Assert.IsType<BrowserRetainedWorkspaceConsumerCompletionResult.Completed>(
            session.Complete(succeeded: true, failure: null));

        Assert.Same(input,
            Assert.IsType<BrowserRetainedWorkspaceOrigin.Project>(posting.Origin).Input);
        Assert.IsType<InspectionShare.NonProjectable>(input.Inspection.Share);
        Assert.Null(posting.CanonicalPacket);
        Assert.Null(posting.Definition);
        Assert.Empty(posting.Packages);
        Assert.Empty(posting.Platforms);
        Assert.Equal(StructuralSubjectKind.Project, posting.Navigation.Snapshot.ActiveSubject.Kind);
        Assert.Equal("project.overview",
            posting.Navigation.Snapshot.LensOutcome.EffectiveLens!.Facet);
        Assert.Equal(["project.overview", "project.dependencies"],
            posting.Navigation.Snapshot.Lenses.Select(lens => lens.Facet.Id));

        using WorkspaceRealizationOperationLease operation = await EnterAsync(owner, "project");
        Assert.Empty(operation.Scope.Packages);
        WorkspaceProjectOccurrence occurrence = Assert.IsType<WorkspaceProjectOccurrence>(operation.Scope.Project);
        if (input.Inspection.Content.Kind == ProjectInputKind.AuthoredProject)
        {
            Assert.Same(input.Inspection.Content.AuthoredProject,
                Assert.IsType<WorkspaceProjectInput.Authored>(occurrence.Input).Result);
        }
        else
        {
            Assert.Same(input.Inspection.Content.RestoredTraversal,
                Assert.IsType<WorkspaceProjectInput.Restored>(occurrence.Input).Result);
        }
        Assert.Equal(
            StructuralSubjectIdentity.ForProject(
                StructuralSubjectIdentity.ForWorkspace(posting.Realization), occurrence),
            prepared.Posting.Navigation.State.CurrentSnapshot.ActiveSubject);
    }

    [Fact]
    public async Task Project_Packet_ProjectConstructsFreshOccurrencesFromTheSameLocalEvidence()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner = new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspaceInput.Project input = ProjectInput("central");
        BrowserRetainedWorkspacePosting first = await ActivateProjectAsync(owner, "project", input);
        WorkspaceProjectOccurrence original;
        using (WorkspaceRealizationOperationLease operation = await EnterAsync(owner, "project"))
            original = operation.Scope.Project!;

        BrowserRetainedWorkspacePosting packet = await ActivateAsync(owner, "packet", Packet());
        using (WorkspaceRealizationOperationLease operation = await EnterAsync(owner, "packet"))
            AssertWorkspace(packet, operation, ["System.Text.Json"], selectedIndex: 0);

        BrowserRetainedWorkspacePosting restored = await ActivateProjectAsync(owner, "project", input);
        using WorkspaceRealizationOperationLease current = await EnterAsync(owner, "project");
        Assert.NotSame(first.Realization, restored.Realization);
        Assert.NotSame(original, current.Scope.Project);
        Assert.Same(
            Assert.IsType<WorkspaceProjectInput.Authored>(original.Input).Result,
            Assert.IsType<WorkspaceProjectInput.Authored>(current.Scope.Project!.Input).Result);
        Assert.Same(input,
            Assert.IsType<BrowserRetainedWorkspaceOrigin.Project>(restored.Origin).Input);
        Assert.True(restored.PublicationOrdinal > packet.PublicationOrdinal);
    }

    [Theory]
    [InlineData(ProjectInputKind.AuthoredProject)]
    [InlineData(ProjectInputKind.RestoredProject)]
    public async Task RejectedProjectDocumentPreservesTheIncumbentWithoutCandidateCharge(ProjectInputKind kind)
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner = new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting incumbent = await ActivateAsync(owner, "packet", Packet());
        var input = new BrowserRetainedWorkspaceInput.Project(
            ProjectInputInspection.Execute(kind, "invalid", "not a Project"u8.ToArray()),
            TraversalTargetFrameworkPolicy.ProductDefault);
        var failed = Assert.IsType<BrowserRetainedWorkspaceActivationResult.Failed>(
            await owner.ActivateAsync(ProjectRequest("invalid", input), TestContext.Current.CancellationToken));

        Assert.IsType<CompleteRestorationFailure.HostConstructionFailed>(failed.Failure);
        Assert.Same(incumbent, owner.Active);
        Assert.Equal(1, owner.Capacity.Charged);
        using WorkspaceRealizationOperationLease operation = await EnterAsync(owner, "packet");
        AssertWorkspace(incumbent, operation, ["System.Text.Json"], selectedIndex: 0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelledOrSupersededProjectSettlesWithoutReplacingTheIncumbent(bool cancel)
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner = new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspaceInput.Project input = ProjectInput("central");
        BrowserRetainedWorkspacePosting incumbent = await ActivateProjectAsync(owner, "incumbent", input);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        BrowserRetainedWorkspaceActivationSession pending =
            owner.BeginActivation(ProjectRequest("candidate", ProjectInput("restored")), cancellation.Token);
        Assert.IsType<BrowserRetainedWorkspacePreparationResult.Prepared>(await pending.Preparation);
        Assert.Same(incumbent, owner.Active);
        using (WorkspaceRealizationOperationLease operation = await EnterAsync(owner, "incumbent"))
            Assert.NotNull(operation.Scope.Project);

        if (cancel)
            cancellation.Cancel();
        else
            Assert.IsType<BrowserRetainedWorkspaceActivationResult.NoEffect>(
                await owner.ActivateAsync(ProjectRequest("incumbent", input), TestContext.Current.CancellationToken));

        Assert.IsType<BrowserRetainedWorkspaceActivationResult.Superseded>(await pending.Activation);
        Assert.Same(incumbent, owner.Active);
        Assert.Equal(1, owner.Capacity.Charged);
    }

    [Fact]
    public async Task ProjectCutoverWaitsForConsumerAcceptanceAndRetainsVisibleCompletionFailure()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner = new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting incumbent =
            await ActivateProjectAsync(owner, "incumbent", ProjectInput("authored"));
        BrowserRetainedWorkspaceActivationSession pending =
            owner.BeginActivation(ProjectRequest("candidate", ProjectInput("central")),
                TestContext.Current.CancellationToken);
        var prepared = Assert.IsType<BrowserRetainedWorkspacePreparationResult.Prepared>(await pending.Preparation);
        Assert.Same(incumbent, owner.Active);
        Assert.False(pending.Activation.IsCompleted);
        Assert.True(pending.Commit());
        var activated = Assert.IsType<BrowserRetainedWorkspaceActivationResult.Activated>(await pending.Activation);
        Assert.Same(prepared.Posting.Origin, activated.Posting.Origin);
        Assert.Same(activated.Posting, owner.Active);

        var blocked = Assert.IsType<BrowserRetainedWorkspaceActivationResult.Failed>(
            await owner.ActivateAsync(ProjectRequest("later", ProjectInput("restored")),
                TestContext.Current.CancellationToken));
        Assert.Contains("awaiting consumer completion", blocked.Failure.Message, StringComparison.Ordinal);
        var completion = Assert.IsType<BrowserRetainedWorkspaceConsumerCompletionResult.Completed>(
            pending.Complete(succeeded: false, failure: "Project presentation failed."));
        Assert.False(completion.Succeeded);
        Assert.Equal("Project presentation failed.", completion.Failure);
        Assert.Same(activated.Posting, owner.Active);
        using WorkspaceRealizationOperationLease operation = await EnterAsync(owner, "candidate");
        Assert.NotNull(operation.Scope.Project);
    }

    static BrowserRetainedWorkspaceActivationRequest ProjectRequest(
        string id, BrowserRetainedWorkspaceInput.Project input) =>
        new(id, $"Project {id}", "/", input);

    static async Task<BrowserRetainedWorkspacePosting> ActivateProjectAsync(
        BrowserRetainedWorkspaceActivationOwner owner,
        string id,
        BrowserRetainedWorkspaceInput.Project input) =>
        Assert.IsType<BrowserRetainedWorkspaceActivationResult.Activated>(
            await owner.ActivateAsync(ProjectRequest(id, input), TestContext.Current.CancellationToken)).Posting;

    static BrowserRetainedWorkspaceInput.Project ProjectInput(string scenario)
    {
        ProjectInputKind kind = scenario is "authored" or "central"
            ? ProjectInputKind.AuthoredProject : ProjectInputKind.RestoredProject;
        string content = scenario switch
        {
            "authored" => """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup><PackageReference Include="Newtonsoft.Json" Version="13.0.3" /></ItemGroup>
                </Project>
                """,
            "central" => """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>$(DefaultTargetFramework)</TargetFramework></PropertyGroup>
                  <ItemGroup><PackageReference Include="Markout" /></ItemGroup>
                </Project>
                """,
            "partial-graph" => ProjectAssets.Replace(
                """{ "type": "package" }""",
                """{ "type": "package", "dependencies": { "Missing.Package": "1.0.0" } }""",
                StringComparison.Ordinal),
            "failed-graph" => ProjectAssets.Replace(
                "\"targets\": {", "\"targets\": { \"NET11.0\": {},", StringComparison.Ordinal),
            "restored" or "target-unavailable" => ProjectAssets,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        InspectionEnvelope<ProjectInputDocument> inspection = ProjectInputInspection.Execute(
            kind, kind == ProjectInputKind.AuthoredProject ? "Example.csproj" : "project.assets.json",
            Encoding.UTF8.GetBytes(content),
            scenario == "target-unavailable" ? new RestoredProjectTargetRequest("net12.0") : null);
        return new(inspection, TraversalTargetFrameworkPolicy.ProductDefault);
    }

    const string ProjectAssets = """
        {
          "version": 3,
          "targets": {
            "net11.0": {
              "Newtonsoft.Json/13.0.3": { "type": "package" }
            }
          },
          "projectFileDependencyGroups": { "net11.0": [ "Newtonsoft.Json >= 13.0.3" ] },
          "project": {
            "frameworks": {
              "net11.0": {
                "dependencies": {
                  "Newtonsoft.Json": { "target": "Package", "version": "[13.0.3, )" }
                }
              }
            }
          }
        }
        """;
}
