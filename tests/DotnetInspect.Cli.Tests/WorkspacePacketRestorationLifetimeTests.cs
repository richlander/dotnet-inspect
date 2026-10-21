using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using ILInspector.Metadata;

using DotnetInspect.Cli.Commands;
using DotnetInspector.Queries;

namespace DotnetInspect.Cli.Tests;

public sealed class WorkspacePacketRestorationLifetimeTests
{
    [Fact]
    public async Task ExecuteAsync_PreservesPrimaryFailureAndCleanupReport()
    {
        InspectionWorkspace workspace =
            await CreateCleanupFailingWorkspaceAsync();
        var primary = new InvalidOperationException("Primary operation failure.");

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                WorkspacePacketRestorationLifetime.ExecuteAsync<int>(
                    workspace,
                    () => Task.FromException<int>(primary)));

        Assert.Same(primary, failure);
        var report = Assert.IsType<InspectionWorkspaceCloseReport>(
            failure.Data["DotnetInspect.Cli.WorkspaceCleanupReport"]);
        Assert.IsType<IOException>(
            Assert.Single(report.ArtifactSessionCleanupFailures));
    }

    [Fact]
    public async Task ExecuteAsync_SurfacesCleanupFailureAfterSuccess()
    {
        InspectionWorkspace workspace =
            await CreateCleanupFailingWorkspaceAsync();

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                WorkspacePacketRestorationLifetime.ExecuteAsync(
                    workspace,
                    () => Task.FromResult(42)));

        Assert.Equal(
            "The restored Workspace could not release every participant.",
            failure.Message);
        Assert.IsType<InspectionWorkspaceCloseReport>(
            failure.Data["DotnetInspect.Cli.WorkspaceCleanupReport"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_MultipleWorkspacesReleaseEveryParticipant(
        bool operationFails)
    {
        bool firstReleased = false;
        bool secondReleased = false;
        InspectionWorkspace[] workspaces =
        [
            await CreateCleanupFailingWorkspaceAsync(
                () => firstReleased = true),
            await CreateCleanupFailingWorkspaceAsync(
                () => secondReleased = true),
        ];
        var primary = new InvalidOperationException("Survey output failed.");

        Task<int> operation = WorkspacePacketRestorationLifetime.ExecuteAsync(
            workspaces,
            () =>
            {
                Assert.False(firstReleased);
                Assert.False(secondReleased);
                return operationFails
                    ? Task.FromException<int>(primary)
                    : Task.FromResult(42);
            });
        AggregateException cleanup;
        if (operationFails)
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => operation);
            Assert.Same(primary, failure);
            cleanup = Assert.IsType<AggregateException>(
                failure.Data["DotnetInspect.Cli.WorkspaceCleanupFailure"]);
        }
        else
        {
            cleanup = await Assert.ThrowsAsync<AggregateException>(
                () => operation);
        }
        Assert.True(firstReleased);
        Assert.True(secondReleased);
        Assert.Equal(2, cleanup.InnerExceptions.Count);
        Assert.All(cleanup.InnerExceptions, failure =>
        {
            var report = Assert.IsType<InspectionWorkspaceCloseReport>(
                failure.Data["DotnetInspect.Cli.WorkspaceCleanupReport"]);
            Assert.IsType<IOException>(
                Assert.Single(report.ArtifactSessionCleanupFailures));
        });
    }

    static async Task<InspectionWorkspace>
        CreateCleanupFailingWorkspaceAsync(Action? onDispose = null)
    {
        byte[] bytes =
            await File.ReadAllBytesAsync(
                typeof(WorkspacePacketRestorationLifetimeTests)
                    .Assembly.Location,
                TestContext.Current.CancellationToken);
        var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) =>
            {
                ArtifactContribution contribution =
                    scope.Register(
                        new TestArtifactProvenance(),
                        _ => new MemoryStream(bytes, writable: false),
                        kind: "managed-assembly");
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [contribution],
                        new FailingAcquisitionLease(onDispose)));
            },
            cancellationToken:
                TestContext.Current.CancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealAsync(
                TestContext.Current.CancellationToken));
        ArtifactQueryAuthorization authorization =
            session.CreateQueryAuthorization();
        ArtifactQueryLease queryLease =
            session.IssueLease(authorization);
        ArtifactDescriptor descriptor =
            Assert.Single(session.GetCatalog(queryLease));
        ArtifactContentReference content =
            session.GetContentReference(
                descriptor.Identity,
                queryLease);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                content.Registration,
                () => session.OpenRead(content, queryLease),
                AssemblyResolutionProvenance.Local(
                    "restored Workspace cleanup test"))
            ?? throw new InvalidOperationException(
                "The test artifact did not project as a managed assembly.");
        var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        assembly,
                        MissingBindingPolicy.Instance),
                ]);
        workspace.RegisterArtifactSession(
            session,
            queryLease,
            [group]);
        return workspace;
    }

    sealed class MissingBindingPolicy : IAssemblyBindingPolicy
    {
        internal static MissingBindingPolicy Instance { get; } =
            new();

        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable)));
    }

    sealed record TestArtifactProvenance : IArtifactProvenance;

    sealed class FailingAcquisitionLease(Action? onDispose = null)
        : IArtifactAcquisitionLease
    {
        public ValueTask DisposeAsync()
        {
            onDispose?.Invoke();
            return ValueTask.FromException(
                new IOException(
                    "Synthetic restored Workspace cleanup failure."));
        }
    }
}
