using System.Collections.Immutable;
using DotnetInspector.Platforms;
using DotnetInspector.QueriesConsumer;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultConstructionIsEmptyAndWorkspaceExact(bool asynchronous)
    {
        await using InspectionWorkspace first = asynchronous
            ? InspectionWorkspace.CreateAsynchronous() : new();
        await using InspectionWorkspace second = asynchronous
            ? InspectionWorkspace.CreateAsynchronous() : new();
        WorkspaceRegistrationRevision initial = Current(first);

        Assert.Empty(initial.Registrations);
        Assert.Same(first.Identity, initial.Workspace);
        Assert.Same(initial, Current(first));
        Assert.NotSame(initial.Workspace, Current(second).Workspace);
        Assert.NotSame(initial.Identity, Current(second).Identity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicConsumerPreservesCompleteInitialValuesAndReplacement(bool asynchronous)
    {
        ExactLibrarySourceCoordinate library = WorkspaceRegistrationTestData.RealPackageSystemTextJson();
        var prefix = new PackagePrefixDeclaration("Microsoft.Extensions.");
        WorkspaceEcosystemRegistrationDeclaration ecosystem = Platform();
        ImmutableArray<WorkspaceRegistration> initial =
        [
            new WorkspaceRegistration.ExactLibrary(library),
            new WorkspaceRegistration.PackagePrefix(prefix),
            new WorkspaceRegistration.Ecosystem(ecosystem),
        ];
        await using InspectionWorkspace workspace = WorkspaceRegistrationConsumer.Create(initial, asynchronous);
        WorkspaceRegistrationObservation observed = WorkspaceRegistrationConsumer.Observe(workspace);

        Assert.Equal(initial, observed.Revision.Registrations);
        Assert.Same(library, Assert.Single(observed.ExactLibraries));
        Assert.Same(prefix, Assert.Single(observed.PackagePrefixes));
        Assert.Same(ecosystem, Assert.Single(observed.Ecosystems));
        Assert.Equal("System.Text.Json", library.LibraryIdentity.Identity.Name);
        var replacement = Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            WorkspaceRegistrationConsumer.Replace(workspace, observed.Revision, [initial[2], initial[0]]));
        Assert.Equal([initial[2], initial[0]], replacement.Revision.Registrations);
        Assert.NotSame(observed.Revision.Identity, replacement.Revision.Identity);
        Assert.Equal(initial, observed.Revision.Registrations);
        Assert.Same(replacement.Revision, Current(workspace));
    }

    [Fact]
    public void ConstructionRejectsIncompleteAndDuplicateIdentitySets()
    {
        var library = new WorkspaceRegistration.ExactLibrary(
            WorkspaceRegistrationTestData.RealPackageSystemTextJson());
        var equalLibrary = new WorkspaceRegistration.ExactLibrary(
            WorkspaceRegistrationTestData.RealPackageSystemTextJson());
        WorkspaceRegistration prefix = Prefix("Microsoft.Extensions.");
        WorkspaceRegistration ecosystem = new WorkspaceRegistration.Ecosystem(Platform());
        ImmutableArray<WorkspaceRegistration>[] invalid =
        [
            default,
            [null!],
            [prefix, Prefix("Microsoft.Extensions.")],
            [library, equalLibrary],
            [ecosystem, new WorkspaceRegistration.Ecosystem(Platform())],
        ];
        foreach (ImmutableArray<WorkspaceRegistration> registrations in invalid)
        {
            Assert.Throws<ArgumentException>(() => new InspectionWorkspace(registrations));
            Assert.Throws<ArgumentException>(() => InspectionWorkspace.CreateAsynchronous(registrations));
        }
    }

    [Fact]
    public void RegistrationArmsRequireTheirOwnerValues()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkspaceRegistration.ExactLibrary(null!));
        Assert.Throws<ArgumentNullException>(() => new WorkspaceRegistration.PackagePrefix(null!));
        Assert.Throws<ArgumentNullException>(() => new WorkspaceRegistration.Ecosystem(null!));
    }

    [Fact]
    public void OrderedEqualityKeepsRevisionWhileReorderAndReadditionAreFresh()
    {
        var library = new WorkspaceRegistration.ExactLibrary(
            WorkspaceRegistrationTestData.RealPackageSystemTextJson());
        WorkspaceRegistration prefix = Prefix("Microsoft.Extensions.");
        WorkspaceEcosystemRegistrationDeclaration declaration = Platform();
        var ecosystem = new WorkspaceRegistration.Ecosystem(declaration);
        using var workspace = new InspectionWorkspace([library, prefix, ecosystem]);
        WorkspaceRegistrationRevision initial = Current(workspace);
        var noEffect = Assert.IsType<WorkspaceRegistrationOperationResult.NoEffect>(
            workspace.ReplaceRegistrations(initial,
            [
                new WorkspaceRegistration.ExactLibrary(
                    WorkspaceRegistrationTestData.RealPackageSystemTextJson()),
                Prefix("Microsoft.Extensions."),
                new WorkspaceRegistration.Ecosystem(declaration),
            ]));
        Assert.Same(initial, noEffect.Revision);

        var reordered = Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            workspace.ReplaceRegistrations(initial, [ecosystem, prefix, library]));
        Assert.NotSame(initial.Identity, reordered.Revision.Identity);
        Assert.Equal([library, prefix, ecosystem], initial.Registrations);
        var cleared = Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            workspace.ReplaceRegistrations(reordered.Revision, []));
        Assert.Empty(cleared.Revision.Registrations);
        Assert.Same(cleared.Revision,
            Assert.IsType<WorkspaceRegistrationOperationResult.NoEffect>(
                workspace.ReplaceRegistrations(cleared.Revision, [])).Revision);
        var readded = Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            workspace.ReplaceRegistrations(cleared.Revision, initial.Registrations));
        Assert.NotSame(initial.Identity, readded.Revision.Identity);
        Assert.NotSame(cleared.Revision.Identity, readded.Revision.Identity);
    }

    [Fact]
    public void SameEcosystemIdWithNewDeclarationReplacesIssuedCorrespondence()
    {
        WorkspaceEcosystemRegistrationDeclaration first = Platform();
        WorkspaceEcosystemRegistrationDeclaration second = Platform();
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.NamespaceRoots, second.NamespaceRoots);
        using var workspace = new InspectionWorkspace([new WorkspaceRegistration.Ecosystem(first)]);
        WorkspaceRegistrationRevision initial = Current(workspace);
        var changed = Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            workspace.ReplaceRegistrations(initial, [new WorkspaceRegistration.Ecosystem(second)]));

        Assert.Same(second,
            Assert.IsType<WorkspaceRegistration.Ecosystem>(
                Assert.Single(changed.Revision.Registrations)).Declaration);
        Assert.Same(first,
            Assert.IsType<WorkspaceRegistration.Ecosystem>(
                Assert.Single(initial.Registrations)).Declaration);
        var rejected = Assert.IsType<WorkspaceRegistrationOperationResult.Rejected>(
            workspace.ReplaceRegistrations(changed.Revision,
                [new WorkspaceRegistration.Ecosystem(first), new WorkspaceRegistration.Ecosystem(second)]));
        Assert.Equal(WorkspaceRegistrationRejection.DuplicateIdentity, rejected.Reason);
        Assert.Same(changed.Revision, rejected.Revision);
    }

    [Fact]
    public void ExpectedRevisionAndCandidateValidationPrecedeNoop()
    {
        WorkspaceRegistration prefix = Prefix("Microsoft.Extensions.");
        using var workspace = new InspectionWorkspace();
        using var foreign = new InspectionWorkspace();
        WorkspaceRegistrationRevision initial = Current(workspace);
        WorkspaceRegistrationRevision current =
            Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
                workspace.ReplaceRegistrations(initial, [prefix])).Revision;

        Reject(null!, [], WorkspaceRegistrationRejection.Malformed);
        Reject(Current(foreign), default, WorkspaceRegistrationRejection.ForeignWorkspace);
        Reject(initial, current.Registrations, WorkspaceRegistrationRejection.RevisionMismatch);
        Reject(current, default, WorkspaceRegistrationRejection.Malformed);
        Reject(current, [null!], WorkspaceRegistrationRejection.Malformed);
        Reject(current, [prefix, prefix], WorkspaceRegistrationRejection.DuplicateIdentity);
        Assert.Same(current, Current(workspace));

        void Reject(
            WorkspaceRegistrationRevision expected,
            ImmutableArray<WorkspaceRegistration> registrations,
            WorkspaceRegistrationRejection reason)
        {
            var rejected = Assert.IsType<WorkspaceRegistrationOperationResult.Rejected>(
                workspace.ReplaceRegistrations(expected, registrations));
            Assert.Equal(reason, rejected.Reason);
            Assert.Same(current, rejected.Revision);
        }
    }

    [Fact]
    public void DifferentArmsAndDistinctLiteralPrefixesKeepTheirOwnedMeaning()
    {
        WorkspaceRegistration first = Prefix("Microsoft.Extensions.");
        WorkspaceRegistration second = Prefix("microsoft.extensions.");
        var ecosystem = new WorkspaceRegistration.Ecosystem(new(
            WorkspaceEcosystemRegistrationId.Create("ecosystem.microsoft-extensions"),
            ["Microsoft.Extensions"], [],
            [new WorkspaceEcosystemPopulationDeclaration.PackagePrefix(
                new("Microsoft.Extensions."))]));
        using var workspace = new InspectionWorkspace([first, ecosystem, second]);

        Assert.Equal([first, ecosystem, second], Current(workspace).Registrations);
    }

    [Fact]
    public async Task CompetingReplacementsCannotBothPublishFromOneRevision()
    {
        using var workspace = new InspectionWorkspace();
        WorkspaceRegistrationRevision initial = Current(workspace);
        WorkspaceRegistrationOperationResult[] results = await Task.WhenAll(
            Task.Run(() => workspace.ReplaceRegistrations(initial, [Prefix("Microsoft.Extensions.")]),
                TestContext.Current.CancellationToken),
            Task.Run(() => workspace.ReplaceRegistrations(initial, [Prefix("Aspire.")]),
                TestContext.Current.CancellationToken));
        var committed = Assert.Single(results.OfType<WorkspaceRegistrationOperationResult.Committed>());
        var rejected = Assert.Single(results.OfType<WorkspaceRegistrationOperationResult.Rejected>());

        Assert.Equal(WorkspaceRegistrationRejection.RevisionMismatch, rejected.Reason);
        Assert.Same(committed.Revision, rejected.Revision);
        Assert.Same(committed.Revision, Current(workspace));
        Assert.Empty(initial.Registrations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedWorkspaceReturnsHistoricalRevisionBeforeInputValidation(bool asynchronous)
    {
        InspectionWorkspace workspace = WorkspaceRegistrationConsumer.Create(
            [Prefix("Microsoft.Extensions.")], asynchronous);
        WorkspaceRegistrationRevision initial = Current(workspace);
        await workspace.DisposeAsync();

        var read = Assert.IsType<WorkspaceRegistrationReadResult.Unavailable>(
            workspace.GetRegistrationSnapshot());
        var mutation = Assert.IsType<WorkspaceRegistrationOperationResult.Unavailable>(
            workspace.ReplaceRegistrations(null!, default));
        Assert.Equal(ArtifactRootFailure.WorkspaceClosed, read.RuntimeFailure);
        Assert.Equal(read.RuntimeFailure, mutation.RuntimeFailure);
        Assert.Same(initial, read.LastRevision);
        Assert.Same(initial, mutation.LastRevision);
    }

    static WorkspaceRegistrationRevision Current(InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceRegistrationReadResult.Available>(
            workspace.GetRegistrationSnapshot()).Revision;

    static WorkspaceRegistration Prefix(string prefix) =>
        new WorkspaceRegistration.PackagePrefix(new(prefix));

    static WorkspaceEcosystemRegistrationDeclaration Platform() =>
        new(WorkspaceEcosystemRegistrationId.Create("ecosystem.platform"),
            ["System"], [],
            [new WorkspaceEcosystemPopulationDeclaration.Platform(
                new(PlatformFamily.DotNetRuntime))]);
}
