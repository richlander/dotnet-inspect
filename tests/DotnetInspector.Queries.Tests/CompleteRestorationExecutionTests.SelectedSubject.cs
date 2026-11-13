using DotnetInspector.Queries.Definitions;
using DotnetInspector.QueriesConsumer;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using QuerySpace;

namespace DotnetInspector.Queries.Tests;

public sealed partial class CompleteRestorationExecutionTests
{
    // PR-fast: pinned archive, offline restoration, at most two bounded contexts.
    [Theory]
    [InlineData("package")]
    [InlineData("all-libraries")]
    [InlineData("library")]
    [InlineData("type")]
    [InlineData("member")]
    public async Task SelectedSubject_BorrowsTheExactSavedTarget(string kind)
    {
        var library = new PortableLibraryIdentity(
            "System.Text.Json", "9.0.0.0", null, "cc7b13ffcd2ddd51");
        MetadataTypeDefinitionName type = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("System.Text.Json", ["JsonSerializer"])).Name;
        PortableRetainedSubjectContext context = kind switch
        {
            "package" => new PortableRetainedSubjectContext.Package(),
            "all-libraries" => new PortableRetainedSubjectContext.AllLibraries(),
            "library" => new PortableRetainedSubjectContext.Library(library),
            "type" => new PortableRetainedSubjectContext.Type(library, type),
            "member" => new PortableRetainedSubjectContext.Member(library, type,
                memberAnchor: "1dc14dd1fb"),
            _ => throw new InvalidOperationException(),
        };
        PortableSubjectRequest subject = kind switch
        {
            "package" => new PortableSubjectRequest.Package(),
            "all-libraries" or "library" => new PortableSubjectRequest.Library(),
            "type" => new PortableSubjectRequest.Type(),
            _ => new PortableSubjectRequest.Member(),
        };
        string facet = kind switch
        {
            "package" => "package.libraries",
            "all-libraries" or "library" => "library.types",
            "type" => "type.overview",
            _ => "member.overview",
        };
        PackageFixture fixture = await SystemTextJsonPackageAsync();
        var intent = new TestIntentAuthority();
        var preparation = WorkspaceDefinitionConsumer.PrepareRestoration(
            PackageRegistry(InspectionDefinitionSchema.Version4,
                new("package", subject, context, facet)),
            "scenario", intent);
        using var client = new HttpClient(new RejectingHandler());
        var host = new TestHost();
        var restored = Assert.IsType<CompleteRestorationResult<InspectionWorkspace>.Activated>(
            await WorkspaceDefinitionConsumer.RestoreAsync(preparation, intent, host,
                Options(client, fixture.Store) with { CaptureSelectedSubject = true },
                TestContext.Current.CancellationToken));
        try
        {
            bool called = false;
            CompleteRestorationSubjectResult<string> result =
                await CompleteRestorationSelectedSubjectOperation.ExecuteAsync(
                    restored.Activation, restored.Workspace, (target, roles, _) =>
                    {
                        called = true;
                        Assert.Equal(facet, target.Lens.Facet.Value);
                        Assert.Same(target.Subject, target.Lens.Subject);
                        Assert.Same(Assert.Single(restored.Workspace.Snapshot.Scope.Packages),
                            target.Occurrence);
                        Assert.Equal(target.Root.Coordinate,
                            target.Occurrence.Occurrence.Package.Coordinate);
                        Assert.Empty(target.Queries);
                        switch (target)
                        {
                            case CompleteRestorationSelectedSubject.Package package:
                                Assert.Equal("package", kind);
                                Assert.Same(package.Subject.Occurrence, target.Occurrence.Occurrence);
                                break;
                            case CompleteRestorationSelectedSubject.AllLibraries aggregate:
                                Assert.Equal("all-libraries", kind);
                                Assert.NotEmpty(aggregate.Libraries);
                                break;
                            case CompleteRestorationSelectedSubject.Library one:
                                Assert.Equal("library", kind);
                                Assert.Same(one.Subject.Identity.Registration,
                                    NavigationRegistrationIdentity.From(
                                        one.DefiningLibrary.Library.Participant.Assembly.Registration));
                                break;
                            case CompleteRestorationSelectedSubject.Type one:
                                Assert.Equal("type", kind);
                                Assert.Contains(one.DefiningLibrary.Library.Participant,
                                    roles.SurfaceGroup.Participants);
                                Assert.Same(one.Subject.Identity.Registration,
                                    NavigationRegistrationIdentity.From(
                                        one.DefiningLibrary.Library.Participant.Assembly.Registration));
                                Assert.DoesNotContain(
                                    restored.Workspace.SelectedContext!.Group!.Participants,
                                    candidate => ReferenceEquals(candidate.Assembly.Registration,
                                        one.DefiningLibrary.Library.Participant.Assembly.Registration));
                                break;
                            case CompleteRestorationSelectedSubject.Member one:
                                Assert.Equal("member", kind);
                                Assert.Contains(one.DefiningLibrary.Library.Participant,
                                    roles.SurfaceGroup.Participants);
                                Assert.DoesNotContain(restored.Workspace.SelectedContext!.Group!.Participants,
                                    candidate => ReferenceEquals(candidate.Assembly.Registration,
                                        one.DefiningLibrary.Library.Participant.Assembly.Registration));
                                Assert.Equal("1dc14dd1fb", one.Subject.Identity.Member.Fingerprint);
                                Assert.Same(one.Subject.Identity.Registration,
                                    NavigationRegistrationIdentity.From(
                                        one.DefiningLibrary.Library.Participant.Assembly.Registration));
                                break;
                            default:
                                Assert.Fail("Unexpected selected target.");
                                break;
                        }
                        return ValueTask.FromResult(target.Root.Root.PackageId);
                    }, TestContext.Current.CancellationToken);
            Assert.True(called);
            Assert.Equal("System.Text.Json",
                Assert.IsType<CompleteRestorationSubjectResult<string>.Completed>(result).Content,
                ignoreCase: true);
        }
        finally
        {
            Assert.True((await restored.Activation.CloseAsync()).Succeeded);
        }
        var closed = Assert.IsType<CompleteRestorationSubjectResult<bool>.Unavailable>(
            await CompleteRestorationSelectedSubjectOperation.ExecuteAsync<bool>(
                restored.Activation, restored.Workspace,
                (_, _) => throw new InvalidOperationException("A closed target was used."),
                TestContext.Current.CancellationToken));
        Assert.Equal(CompleteRestorationSubjectUnavailableReason.ArtifactUnavailable,
            closed.Failure.Reason);
    }

    [Theory]
    [InlineData(false, "package")]
    [InlineData(true, null)]
    public async Task SelectedSubject_OmissionAndWorkspaceLandingAreExplicit(
        bool capture, string? focus)
    {
        PackageFixture fixture = await SystemTextJsonPackageAsync();
        var intent = new TestIntentAuthority();
        var preparation = WorkspaceDefinitionConsumer.PrepareRestoration(
            Version2PackageRegistry(focus: focus), "scenario", intent);
        using var client = new HttpClient(new RejectingHandler());
        var restored = Assert.IsType<CompleteRestorationResult<InspectionWorkspace>.Activated>(
            await WorkspaceDefinitionConsumer.RestoreAsync(preparation, intent, new TestHost(),
                Options(client, fixture.Store) with { CaptureSelectedSubject = capture },
                TestContext.Current.CancellationToken));
        try
        {
            var result = Assert.IsType<CompleteRestorationSubjectResult<bool>.Unavailable>(
                await CompleteRestorationSelectedSubjectOperation.ExecuteAsync<bool>(
                    restored.Activation, restored.Workspace,
                    (_, _) => throw new InvalidOperationException("Unexpected callback."),
                    TestContext.Current.CancellationToken));
            Assert.Equal(capture
                ? CompleteRestorationSubjectUnavailableReason.UnsupportedSubject
                : CompleteRestorationSubjectUnavailableReason.NotCaptured,
                result.Failure.Reason);
        }
        finally { await restored.Activation.CloseAsync(); }
    }
    [Fact]
    public async Task SelectedSubject_UsesFocusedAssetAndItsOwnQueryBinding()
    {
        const int schema = InspectionDefinitionSchema.Version4;
        var library = new PortableLibraryIdentity("System.Text.Json", "9.0.0.0",
            null, "cc7b13ffcd2ddd51");
        MetadataTypeDefinitionName type = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("System.Text.Json", ["JsonSerializer"])).Name;
        var first = new DefinitionMemberCoordinate.PackageCoordinate(
            "System.Text.Json", "9.0.4", "net9.0");
        var selected = new DefinitionMemberCoordinate.PackageCoordinate(
            "System.Text.Json", "9.0.4", "net8.0");
        var descriptor = new PortableQueryDefinitionDescriptor<string>(
            "selection-test/v1", "selection-test",
            PortableQueryDefinitionInputs.StateBound(
                [PortableSubjectRequestKind.Type], ["type.api"],
                PortableQueryInputRequirement.Required,
                PortableQueryInputRequirement.Required,
                PortableQueryInputRequirement.Required),
            (_, attachment, _) => new PortableQueryDefinitionResolution<string>.Accepted(
                Assert.IsType<DefinitionMemberCoordinate.PackageCoordinate>(
                    attachment.StateCoordinate).Framework!));
        var registry = new InspectionDefinitionRegistry();
        registry.AddQueryDescriptor(descriptor);
        registry.Add(new WorkspaceDefinition(schema, "workspace",
            [new WorkspaceContextDefinition("first", "net9.0", members: [first]),
             new WorkspaceContextDefinition("selected", "net8.0", members: [selected])]));
        registry.Add(new CommittedNavigationDefinition(schema, "navigation",
            [new NavigationTabDefinition("first", first),
             new NavigationTabDefinition("selected", selected)], "selected"));
        registry.Add(new CommittedQueryDefinition(schema, "same-query",
            PortableQueryIdentity.Create(descriptor.QueryId,
                PortableQueryIntent.Create([], [], [], []), TestContext.Current.CancellationToken)));
        registry.Add(new CommittedViewDefinition(schema, "view",
            [new CommittedViewStateDefinition(null, new PortableSubjectRequest.Workspace()),
             State("first"), State("selected")]));
        registry.Add(new ScenarioDefinition(schema, "scenario", workspace: "workspace",
            context: "selected", view: "view", navigation: "navigation"));
        PackageFixture fixture = await SystemTextJsonPackageAsync();
        var intent = new TestIntentAuthority();
        using var client = new HttpClient(new RejectingHandler());
        var restored = Assert.IsType<CompleteRestorationResult<InspectionWorkspace>.Activated>(
            await WorkspaceDefinitionConsumer.RestoreAsync(
                WorkspaceDefinitionConsumer.PrepareRestoration(registry, "scenario", intent),
                intent, new TestHost(),
                Options(client, fixture.Store) with { CaptureSelectedSubject = true },
                TestContext.Current.CancellationToken));
        try
        {
            var result = await CompleteRestorationSelectedSubjectOperation.ExecuteAsync(
                restored.Activation, restored.Workspace, (target, _) =>
                {
                    var exact = Assert.IsType<CompleteRestorationSelectedSubject.Type>(target);
                    Assert.Equal("net8.0", exact.Root.Coordinate.Framework);
                    Assert.Equal("net8.0", Assert.IsType<BoundCommittedQuery<string>>(
                        Assert.Single(exact.Queries)).Plan);
                    Assert.Same(exact.DefiningLibrary, Assert.Single(exact.QueryLibraries));
                    Assert.Equal(2, restored.Workspace.Contexts.Length);
                    Assert.Equal(2, restored.Workspace.Snapshot.Scope.Packages.Length);
                    WorkspacePackageOccurrenceDescriptor neighbor =
                        Assert.Single(restored.Workspace.Snapshot.Scope.Packages,
                            item => item.Occurrence.Package.Coordinate.Framework == "net9.0");
                    Assert.NotSame(neighbor.Occurrence, exact.Occurrence.Occurrence);
                    Assert.Same(exact.Subject.Identity.Registration,
                        NavigationRegistrationIdentity.From(
                            exact.DefiningLibrary.Library.Participant.Assembly.Registration));
                    return ValueTask.FromResult(exact.Subject.Identity.Type);
                }, TestContext.Current.CancellationToken);
            Assert.Equal(type,
                Assert.IsType<CompleteRestorationSubjectResult<MetadataTypeDefinitionName>.Completed>(
                    result).Content);

            await using var foreign = new InspectionWorkspace();
            var rejected = Assert.IsType<CompleteRestorationSubjectResult<bool>.Unavailable>(
                await CompleteRestorationSelectedSubjectOperation.ExecuteAsync<bool>(
                    foreign, restored.Workspace, (_, _) => throw new InvalidOperationException(),
                    TestContext.Current.CancellationToken));
            Assert.Equal(CompleteRestorationSubjectUnavailableReason.ForeignWorkspace,
                rejected.Failure.Reason);
        }
        finally { await restored.Activation.CloseAsync(); }

        CommittedViewStateDefinition State(string tab) => new(tab,
            new PortableSubjectRequest.Type(),
            new PortableRetainedSubjectContext.Type(library, type), "type.api",
            queries: ["same-query"], libraries: [library]);
    }
}
