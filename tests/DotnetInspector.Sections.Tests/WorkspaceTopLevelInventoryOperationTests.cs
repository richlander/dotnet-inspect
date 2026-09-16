using System.Text.Json;

using DotnetInspector.Queries;

namespace DotnetInspector.Sections.Tests;

public sealed class WorkspaceTopLevelInventoryOperationTests
{
    [Fact]
    public async Task Execute_ComposesExactPacketAndDetachedExecution()
    {
        await using RealizationFixture fixture =
            await RealizationFixture.CreateAsync();
        const string packet = "workspace-packet";

        WorkspaceTopLevelInventoryExecution execution =
            WorkspaceTopLevelInventoryOperation.Execute(
                fixture.Lease,
                WorkspaceTopLevelInventoryRequest.All,
                WorkspaceTopLevelInventoryShareBasis.CreatePacketInput(
                    fixture.Lease.Definition,
                    packet));

        var available =
            Assert.IsType<WorkspaceTopLevelInventoryOutcome.Available>(
                execution.Inspection.Content);
        Assert.Empty(available.Document.Entries);
        var share = Assert.IsType<InspectionShare.Available>(
            execution.Inspection.Share);
        Assert.Equal(packet, share.Packet);
        Assert.Equal(
            $"https://dotnet-inspect.net/?w={packet}",
            share.FullUrl);
        Assert.Empty(execution.Inspection.Diagnostics);
        Assert.True(execution.Selection.HasAuthority);

        fixture.Lease.Dispose();
        string json = JsonSerializer.Serialize(execution);
        Assert.Contains("\"Inspection\"", json);
        Assert.DoesNotContain("\"Selection\"", json);
        Assert.Empty(
            Assert.IsType<WorkspaceTopLevelInventoryOutcome.Available>(
                execution.Inspection.Content).Document.Entries);
    }

    [Fact]
    public async Task Execute_RealizedAndFilteredInputsRemainAvailableButNotShareable()
    {
        await using RealizationFixture fixture =
            await RealizationFixture.CreateAsync();

        WorkspaceTopLevelInventoryExecution unfiltered =
            WorkspaceTopLevelInventoryOperation.Execute(
                fixture.Lease,
                WorkspaceTopLevelInventoryRequest.All,
                WorkspaceTopLevelInventoryShareBasis
                    .CreateRealizedWorkspace(fixture.Lease.Definition));
        WorkspaceTopLevelInventoryExecution filtered =
            WorkspaceTopLevelInventoryOperation.Execute(
                fixture.Lease,
                new(
                    new WorkspaceTopLevelInventoryKindFilter(
                    [
                        WorkspaceTopLevelInventoryEntryKind.Package,
                    ])),
                WorkspaceTopLevelInventoryShareBasis.CreateDefinitionInput(
                    fixture.Lease.Definition,
                    "workspace-packet"));

        Assert.IsType<WorkspaceTopLevelInventoryOutcome.Available>(
            unfiltered.Inspection.Content);
        var unfilteredShare = Assert.IsType<InspectionShare.NonProjectable>(
            unfiltered.Inspection.Share);
        Assert.Contains(
            "no retained Definitions-owned projection",
            unfilteredShare.Reason.ToString());
        Assert.IsType<WorkspaceTopLevelInventoryOutcome.Available>(
            filtered.Inspection.Content);
        var filteredShare = Assert.IsType<InspectionShare.NonProjectable>(
            filtered.Inspection.Share);
        Assert.Contains(
            "do not represent inventory kind filters",
            filteredShare.Reason.ToString());
    }

    [Fact]
    public async Task Execute_InvalidShareBasisPrecedesInvalidFilter()
    {
        await using RealizationFixture first =
            await RealizationFixture.CreateAsync();
        await using RealizationFixture second =
            await RealizationFixture.CreateAsync();
        var request = new WorkspaceTopLevelInventoryRequest(
            new WorkspaceTopLevelInventoryKindFilter(
                Array.Empty<WorkspaceTopLevelInventoryEntryKind>()));

        WorkspaceTopLevelInventoryExecution execution =
            WorkspaceTopLevelInventoryOperation.Execute(
                first.Lease,
                request,
                WorkspaceTopLevelInventoryShareBasis.CreatePacketInput(
                    second.Lease.Definition,
                    "foreign-packet"));

        var unavailable =
            Assert.IsType<WorkspaceTopLevelInventoryOutcome.Unavailable>(
                execution.Inspection.Content);
        Assert.Equal(
            WorkspaceTopLevelInventoryUnavailableReason.InvalidShareBasis,
            unavailable.Reason);
        Assert.False(execution.Selection.HasAuthority);
        Assert.Contains(
            execution.Inspection.Diagnostics,
            diagnostic =>
                diagnostic.Code
                    == "workspace-top-level-inventory.invalid-share-basis");
    }

    [Fact]
    public async Task Execute_InvalidFilterHasTypedDiagnostic()
    {
        await using RealizationFixture fixture =
            await RealizationFixture.CreateAsync();

        WorkspaceTopLevelInventoryExecution execution =
            WorkspaceTopLevelInventoryOperation.Execute(
                fixture.Lease,
                new(
                    new WorkspaceTopLevelInventoryKindFilter(
                        Array.Empty<
                            WorkspaceTopLevelInventoryEntryKind>())),
                WorkspaceTopLevelInventoryShareBasis.CreatePacketInput(
                    fixture.Lease.Definition,
                    "workspace-packet"));

        var rejected =
            Assert.IsType<WorkspaceTopLevelInventoryOutcome.Rejected>(
                execution.Inspection.Content);
        Assert.Equal(
            WorkspaceTopLevelInventoryRejection.InvalidFilter,
            rejected.Reason);
        Assert.IsType<InspectionShare.NonProjectable>(
            execution.Inspection.Share);
        Assert.Contains(
            execution.Inspection.Diagnostics,
            diagnostic =>
                diagnostic.Code
                    == "workspace-top-level-inventory.invalid-filter");
        Assert.False(execution.Selection.HasAuthority);
    }

    sealed class RealizationFixture : IAsyncDisposable
    {
        RealizationFixture(
            WorkspaceRealizationCoordinator coordinator,
            WorkspaceRealizationOperationLease lease)
        {
            Coordinator = coordinator;
            Lease = lease;
        }

        WorkspaceRealizationCoordinator Coordinator { get; }

        internal WorkspaceRealizationOperationLease Lease { get; }

        internal static async Task<RealizationFixture> CreateAsync()
        {
            var coordinator = new WorkspaceRealizationCoordinator();
            try
            {
                WorkspaceRealizationCandidateStartResult start =
                    await coordinator.BeginCandidateAsync(
                        WorkspacePlan.Empty);
                var prepared = Assert.IsType<
                    WorkspaceRealizationCandidateStartResult.Prepared>(start);
                WorkspaceRealizationCandidateCompletionResult completion =
                    await coordinator.CompleteCandidateAsync(
                        prepared.Candidate);
                Assert.IsType<
                    WorkspaceRealizationCandidateCompletionResult.Ready>(
                        completion);
                Assert.IsType<WorkspaceRealizationCutoverResult.Activated>(
                    coordinator.CutOver(prepared.Candidate));
                WorkspaceRealizationOperationAdmission admission =
                    await coordinator.EnterOperationAsync();
                WorkspaceRealizationOperationLease lease =
                    Assert.IsType<
                        WorkspaceRealizationOperationAdmission.Admitted>(
                            admission).Lease;
                return new(coordinator, lease);
            }
            catch
            {
                await coordinator.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Lease.Dispose();
            await Coordinator.DisposeAsync();
        }
    }
}
