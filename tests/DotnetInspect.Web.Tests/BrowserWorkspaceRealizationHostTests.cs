using System.IO.Compression;
using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserWorkspaceRealizationHostTests
{
    [Fact]
    public async Task CapacityWait_LatestAttemptStartsAfterPredecessorSettlement()
    {
        await using var host = new BrowserWorkspaceRealizationHost();
        List<WorkspaceRealizationOperationLease> predecessors =
            await FillWithDrainingPredecessorsAsync(host);

        Task<BrowserWorkspaceRealizationCandidateStartResult> superseded =
            host.BeginCandidateAsync(
                WorkspacePlan.Empty,
                TestContext.Current.CancellationToken).AsTask();
        await Task.Yield();
        Assert.False(superseded.IsCompleted);

        Task<BrowserWorkspaceRealizationCandidateStartResult> latest =
            host.BeginCandidateAsync(
                WorkspacePlan.Empty,
                TestContext.Current.CancellationToken).AsTask();
        Assert.IsType<
            BrowserWorkspaceRealizationCandidateStartResult.Superseded>(
                await superseded);
        Assert.False(latest.IsCompleted);

        predecessors[0].Dispose();
        var prepared = Assert.IsType<
            BrowserWorkspaceRealizationCandidateStartResult.Prepared>(
                await latest);

        Assert.Equal(
            BrowserWorkspaceRealizationHost.MaxChargedRealizations,
            host.Capacity.Charged);
        Assert.IsType<
            BrowserWorkspaceRealizationCandidateRetirementResult.Retiring>(
                host.AbandonCandidate(prepared.Candidate));
        _ = await prepared.Candidate.Settlement;
        foreach (WorkspaceRealizationOperationLease predecessor
            in predecessors.Skip(1))
        {
            predecessor.Dispose();
        }
    }

    [Fact]
    public async Task FullCapacity_NewAttemptSupersedesCandidateBeforeReplacement()
    {
        await using var host = new BrowserWorkspaceRealizationHost();
        _ = await ActivateAsync(host);
        WorkspaceRealizationOperationLease first =
            await EnterAsync(host);
        _ = await ActivateAsync(host);
        WorkspaceRealizationOperationLease second =
            await EnterAsync(host);
        _ = await ActivateAsync(host);
        BrowserWorkspaceRealizationCandidateStartResult.Prepared prior =
            await BeginAsync(host);

        Assert.Equal(
            BrowserWorkspaceRealizationHost.MaxChargedRealizations,
            host.Capacity.Charged);

        BrowserWorkspaceRealizationCandidateStartResult.Prepared replacement =
            await BeginAsync(host);
        WorkspaceRealizationSettlement superseded =
            await prior.Candidate.Settlement;

        Assert.Equal(
            WorkspaceRealizationRetirementReason.CandidateSuperseded,
            superseded.Reason);
        Assert.True(superseded.Succeeded);
        Assert.Equal(
            BrowserWorkspaceRealizationHost.MaxChargedRealizations,
            host.Capacity.Charged);
        Assert.IsType<
            BrowserWorkspaceRealizationCandidateRetirementResult.Retiring>(
                host.AbandonCandidate(replacement.Candidate));
        _ = await replacement.Candidate.Settlement;
        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public async Task Close_SettlesCapacityWaitBeforePredecessorsDrain()
    {
        var host = new BrowserWorkspaceRealizationHost();
        List<WorkspaceRealizationOperationLease> predecessors =
            await FillWithDrainingPredecessorsAsync(host);
        Task<BrowserWorkspaceRealizationCandidateStartResult> waiting =
            host.BeginCandidateAsync(
                WorkspacePlan.Empty,
                TestContext.Current.CancellationToken).AsTask();
        await Task.Yield();
        Assert.False(waiting.IsCompleted);

        Task<BrowserWorkspaceRealizationHostCloseReport> close =
            host.CloseAsync();

        Assert.IsType<
            BrowserWorkspaceRealizationCandidateStartResult.Closed>(
                await waiting);
        Assert.False(close.IsCompleted);

        foreach (WorkspaceRealizationOperationLease predecessor
            in predecessors)
        {
            predecessor.Dispose();
        }
        BrowserWorkspaceRealizationHostCloseReport report = await close;
        Assert.Empty(report.Capacity.FailedSettlements);
        Assert.Equal(0, report.Capacity.Charged);
        await host.DisposeAsync();
    }

    [Fact]
    public async Task CleanupFailuresStayChargedAndRejectLaterCandidate()
    {
        var host = new BrowserWorkspaceRealizationHost();
        for (int index = 0;
            index < BrowserWorkspaceRealizationHost.MaxChargedRealizations;
            index++)
        {
            BrowserWorkspaceRealizationCutoverResult.Activated activated =
                await ActivateWithCleanupFailureAsync(host);
            if (activated.Predecessor is not null)
            {
                WorkspaceRealizationSettlement settlement =
                    await activated.Predecessor.Completion;
                Assert.False(settlement.Succeeded);
            }
        }

        BrowserWorkspaceRealizationCapacitySnapshot capacity = host.Capacity;
        Assert.Equal(
            BrowserWorkspaceRealizationHost.MaxChargedRealizations,
            capacity.Charged);
        Assert.Equal(3, capacity.FailedSettlements.Length);

        var unavailable = Assert.IsType<
            BrowserWorkspaceRealizationCandidateStartResult
                .CapacityUnavailable>(
                    await host.BeginCandidateAsync(
                        WorkspacePlan.Empty,
                        TestContext.Current.CancellationToken));

        Assert.Equal(capacity.Limit, unavailable.Capacity.Limit);
        Assert.Equal(capacity.Charged, unavailable.Capacity.Charged);
        Assert.Equal(
            capacity.FailedSettlements,
            unavailable.Capacity.FailedSettlements);
        BrowserWorkspaceRealizationHostCloseReport report =
            await host.CloseAsync();
        Assert.Equal(
            BrowserWorkspaceRealizationHost.MaxChargedRealizations,
            report.Capacity.FailedSettlements.Length);
        Assert.All(
            report.Capacity.FailedSettlements,
            settlement => Assert.False(settlement.Succeeded));
    }

    [Fact]
    public async Task CandidateRuntimeFailurePreservesActiveAdmissionAndCharge()
    {
        await using var host = new BrowserWorkspaceRealizationHost();
        WorkspaceRealization active = await ActivateAsync(host);
        BrowserWorkspaceRealizationCandidateStartResult.Prepared prepared =
            await BeginAsync(host);
        InspectionWorkspace failedWorkspace;
        using (WorkspaceRealizationConstructionLease construction =
            prepared.Candidate.EnterConstruction())
        {
            failedWorkspace = construction.Workspace;
        }
        _ = await failedWorkspace.CloseAsync();

        var rejected = Assert.IsType<
            WorkspaceRealizationCandidateCompletionResult.Rejected>(
                await host.CompleteCandidateAsync(
                    prepared.Candidate,
                    TestContext.Current.CancellationToken));
        WorkspaceRealizationSettlement settlement =
            await prepared.Candidate.Settlement;

        Assert.Equal(
            WorkspaceRealizationCandidateRejection.RuntimeUnavailable,
            rejected.Reason);
        Assert.Equal(
            WorkspaceRealizationRetirementReason.CandidateFailed,
            settlement.Reason);
        Assert.True(settlement.Succeeded);
        Assert.Equal(1, host.Capacity.Charged);
        using WorkspaceRealizationOperationLease operation =
            await EnterAsync(host);
        Assert.Same(active.Identity, operation.Realization);
    }

    [Fact]
    public async Task CandidateBarrierWait_RemainsCancellableAndSupersedable()
    {
        await using var host = new BrowserWorkspaceRealizationHost();
        BrowserWorkspaceRealizationCandidateStartResult.Prepared prior =
            await BeginAsync(host);
        WorkspaceRealizationConstructionLease construction =
            prior.Candidate.EnterConstruction();
        using var cancellation = new CancellationTokenSource();

        Task<BrowserWorkspaceRealizationCandidateStartResult> cancelled =
            host.BeginCandidateAsync(
                WorkspacePlan.Empty,
                cancellation.Token).AsTask();
        await Task.Yield();
        Assert.False(cancelled.IsCompleted);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await cancelled);

        Task<BrowserWorkspaceRealizationCandidateStartResult> superseded =
            host.BeginCandidateAsync(
                WorkspacePlan.Empty,
                TestContext.Current.CancellationToken).AsTask();
        await Task.Yield();
        Assert.False(superseded.IsCompleted);
        Task<BrowserWorkspaceRealizationCandidateStartResult> latest =
            host.BeginCandidateAsync(
                WorkspacePlan.Empty,
                TestContext.Current.CancellationToken).AsTask();
        Assert.IsType<
            BrowserWorkspaceRealizationCandidateStartResult.Superseded>(
                await superseded);
        Assert.False(latest.IsCompleted);

        construction.Dispose();
        BrowserWorkspaceRealizationCandidateStartResult.Prepared prepared =
            Assert.IsType<
                BrowserWorkspaceRealizationCandidateStartResult.Prepared>(
                    await latest);
        Assert.IsType<
            BrowserWorkspaceRealizationCandidateRetirementResult.Retiring>(
                host.AbandonCandidate(prepared.Candidate));
        _ = await prepared.Candidate.Settlement;
    }

    [Fact]
    public async Task PreCancelledStart_DoesNotSupersedeCurrentCandidate()
    {
        await using var host = new BrowserWorkspaceRealizationHost();
        BrowserWorkspaceRealizationCandidateStartResult.Prepared current =
            await BeginAsync(host);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await host.BeginCandidateAsync(
                WorkspacePlan.Empty,
                cancellation.Token));
        Assert.IsType<WorkspaceRealizationCandidateCompletionResult.Ready>(
            await host.CompleteCandidateAsync(
                current.Candidate,
                TestContext.Current.CancellationToken));
        Assert.IsType<
            BrowserWorkspaceRealizationCutoverResult.Activated>(
                host.CutOver(current.Candidate));
    }

    [Fact]
    public async Task StaleCandidateHandle_CannotCutOverCurrentCandidate()
    {
        await using var host = new BrowserWorkspaceRealizationHost();
        BrowserWorkspaceRealizationCandidateStartResult.Prepared stale =
            await BeginAsync(host);
        Assert.IsType<WorkspaceRealizationCandidateCompletionResult.Ready>(
            await host.CompleteCandidateAsync(
                stale.Candidate,
                TestContext.Current.CancellationToken));
        BrowserWorkspaceRealizationCandidateStartResult.Prepared current =
            await BeginAsync(host);
        Assert.IsType<WorkspaceRealizationCandidateCompletionResult.Ready>(
            await host.CompleteCandidateAsync(
                current.Candidate,
                TestContext.Current.CancellationToken));

        var rejected = Assert.IsType<
            BrowserWorkspaceRealizationCutoverResult.Rejected>(
                host.CutOver(stale.Candidate));

        Assert.Equal(
            WorkspaceRealizationCandidateRejection.StaleCandidate,
            rejected.Reason);
        Assert.IsType<
            BrowserWorkspaceRealizationCutoverResult.Activated>(
                host.CutOver(current.Candidate));
    }

    [Fact]
    public async Task FailedCandidateSupersession_DoesNotOverAdmitReplacement()
    {
        var host = new BrowserWorkspaceRealizationHost();
        for (int index = 0; index < 3; index++)
        {
            BrowserWorkspaceRealizationCutoverResult.Activated activated =
                await ActivateWithCleanupFailureAsync(host);
            if (activated.Predecessor is not null)
                _ = await activated.Predecessor.Completion;
        }
        BrowserWorkspaceRealizationCandidate failedCandidate =
            await PrepareCleanupFailureAsync(host);

        Assert.Equal(
            BrowserWorkspaceRealizationHost.MaxChargedRealizations,
            host.Capacity.Charged);
        var unavailable = Assert.IsType<
            BrowserWorkspaceRealizationCandidateStartResult
                .CapacityUnavailable>(
                    await host.BeginCandidateAsync(
                        WorkspacePlan.Empty,
                        TestContext.Current.CancellationToken));
        WorkspaceRealizationSettlement failed =
            await failedCandidate.Settlement;

        Assert.Equal(
            WorkspaceRealizationRetirementReason.CandidateSuperseded,
            failed.Reason);
        Assert.False(failed.Succeeded);
        Assert.Equal(
            BrowserWorkspaceRealizationHost.MaxChargedRealizations,
            unavailable.Capacity.Charged);
        Assert.Equal(3, unavailable.Capacity.FailedSettlements.Length);
        WorkspaceRealizationOperationLease active =
            await EnterAsync(host);
        active.Dispose();
        _ = await host.CloseAsync();
    }

    [Fact]
    public async Task Close_SettlesCandidateBarrierWaitBeforeConstructionDrain()
    {
        var host = new BrowserWorkspaceRealizationHost();
        BrowserWorkspaceRealizationCandidateStartResult.Prepared prior =
            await BeginAsync(host);
        WorkspaceRealizationConstructionLease construction =
            prior.Candidate.EnterConstruction();
        Assert.IsType<
            BrowserWorkspaceRealizationCandidateRetirementResult.Retiring>(
                host.CancelCandidate(prior.Candidate));
        Task<BrowserWorkspaceRealizationCandidateStartResult> waiting =
            host.BeginCandidateAsync(
                WorkspacePlan.Empty,
                TestContext.Current.CancellationToken).AsTask();
        await Task.Yield();
        Assert.False(waiting.IsCompleted);

        Task<BrowserWorkspaceRealizationHostCloseReport> close =
            host.CloseAsync();

        Assert.IsType<
            BrowserWorkspaceRealizationCandidateStartResult.Closed>(
                await waiting);
        Assert.False(close.IsCompleted);
        construction.Dispose();
        _ = await prior.Candidate.Settlement;
        BrowserWorkspaceRealizationHostCloseReport report = await close;
        Assert.Empty(report.Capacity.FailedSettlements);
        await host.DisposeAsync();
    }

    static async Task<List<WorkspaceRealizationOperationLease>>
        FillWithDrainingPredecessorsAsync(
            BrowserWorkspaceRealizationHost host)
    {
        var predecessors =
            new List<WorkspaceRealizationOperationLease>();
        _ = await ActivateAsync(host);
        predecessors.Add(await EnterAsync(host));
        _ = await ActivateAsync(host);
        predecessors.Add(await EnterAsync(host));
        _ = await ActivateAsync(host);
        predecessors.Add(await EnterAsync(host));
        _ = await ActivateAsync(host);

        Assert.Equal(
            BrowserWorkspaceRealizationHost.MaxChargedRealizations,
            host.Capacity.Charged);
        return predecessors;
    }

    static async Task<
        BrowserWorkspaceRealizationCandidateStartResult.Prepared> BeginAsync(
            BrowserWorkspaceRealizationHost host) =>
        Assert.IsType<
            BrowserWorkspaceRealizationCandidateStartResult.Prepared>(
                await host.BeginCandidateAsync(
                    WorkspacePlan.Empty,
                    TestContext.Current.CancellationToken));

    static async Task<WorkspaceRealization> ActivateAsync(
        BrowserWorkspaceRealizationHost host)
    {
        BrowserWorkspaceRealizationCandidateStartResult.Prepared prepared =
            await BeginAsync(host);
        Assert.IsType<WorkspaceRealizationCandidateCompletionResult.Ready>(
            await host.CompleteCandidateAsync(
                prepared.Candidate,
                TestContext.Current.CancellationToken));
        return Assert.IsType<
            BrowserWorkspaceRealizationCutoverResult.Activated>(
                host.CutOver(prepared.Candidate)).Realization;
    }

    static async Task<BrowserWorkspaceRealizationCutoverResult.Activated>
        ActivateWithCleanupFailureAsync(
            BrowserWorkspaceRealizationHost host)
    {
        BrowserWorkspaceRealizationCandidate candidate =
            await PrepareCleanupFailureAsync(host);
        return Assert.IsType<
            BrowserWorkspaceRealizationCutoverResult.Activated>(
                host.CutOver(candidate));
    }

    static async Task<BrowserWorkspaceRealizationCandidate>
        PrepareCleanupFailureAsync(
            BrowserWorkspaceRealizationHost host)
    {
        BrowserWorkspaceRealizationCandidateStartResult.Prepared prepared =
            await BeginAsync(host);
        using (WorkspaceRealizationConstructionLease construction =
            prepared.Candidate.EnterConstruction())
        {
            PackageRootBinding binding = SystemTextJsonBinding();
            PackageAssemblyContextCompletionOperation operation =
                construction.Workspace
                    .PreparePackageAssemblyContextCompletion([binding]);
            PackageAssemblyContextCompletion completion =
                await operation.ExecuteAsync(operation.Identity);
            completion.SurfaceAssemblyContextGroup.RegisterOwnedResource(
                new ThrowingResource());
        }
        Assert.IsType<WorkspaceRealizationCandidateCompletionResult.Ready>(
            await host.CompleteCandidateAsync(
                prepared.Candidate,
                TestContext.Current.CancellationToken));
        return prepared.Candidate;
    }

    static async ValueTask<WorkspaceRealizationOperationLease> EnterAsync(
        BrowserWorkspaceRealizationHost host)
    {
        WorkspaceRealizationOperationAdmission admission =
            await host.EnterOperationAsync(
                TestContext.Current.CancellationToken);
        return Assert.IsType<
            WorkspaceRealizationOperationAdmission.Admitted>(
                admission).Lease;
    }

    static PackageRootBinding SystemTextJsonBinding()
    {
        byte[] assembly = File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "Spotlight",
                "package",
                "System.Text.Json.dll"));
        var content = new InMemoryPackageContent(
            Archive(
                ("lib/net10.0/System.Text.Json.dll", assembly)),
            fromCache: false,
            producerKey: "inspect-web-real-asset-test");
        var payload = new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(
                "System.Text.Json",
                "11.0.0-preview.7.26381.103"),
            content,
            "inspect-web-real-asset-test",
            PackagePayloadOrigin.Download);
        return PackageRootBinding.CreateFromSource(
            payload,
            "net10.0");
    }

    static byte[] Archive(
        params (string Path, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream destination =
                    archive.CreateEntry(
                        path,
                        CompressionLevel.NoCompression)
                    .Open();
                destination.Write(content);
            }
        }
        return buffer.ToArray();
    }

    sealed class ThrowingResource : IDisposable
    {
        public void Dispose() =>
            throw new InvalidOperationException(
                "Browser realization cleanup failure.");
    }
}
