using System.Collections.Concurrent;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

public sealed partial class ConfiguredPayloadAcquisitionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DesktopSettlementWaitsForPayloadBeforeReleasingClients(bool externalContext)
    {
        const string Id = "Payload.Settlement";
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new ConcurrentQueue<string>();
        var handler = new SettlementTrackingHandler(new PayloadFeedHandler(
            FirstFeed, Id, () => PackageContent(Id, "retained after settlement"), requests,
            async token =>
            {
                started.TrySetResult();
                await finish.Task.WaitAsync(token);
            }));
        await using var composition = CreateComposition((_, _) => handler);
        using NuGetOperationContext? context = externalContext
            ? new(TestContext.Current.CancellationToken)
            : null;
        Task<ConfiguredPackagePayloadResult> pending = composition.AcquirePinnedAsync(
            Id, Version, (_, _) => new InMemoryPackageStore(),
            new NuGetSourceOptions { Sources = [FirstFeed] },
            cancellationToken: TestContext.Current.CancellationToken,
            operationContext: context);
        try
        {
            await started.Task.WaitAsync(TestContext.Current.CancellationToken);
            Task settlement = composition.DisposeAsync().AsTask();
            Task secondSettlement = composition.DisposeAsync().AsTask();
            Assert.False(settlement.IsCompleted);
            Assert.False(secondSettlement.IsCompleted);
            Assert.False(handler.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() =>
            {
                _ = composition.AcquirePinnedAsync(
                    Id, Version, (_, _) => new InMemoryPackageStore(),
                    cancellationToken: TestContext.Current.CancellationToken);
            });
            finish.SetResult();
            ConfiguredPackagePayloadResult result = await pending;
            await settlement.WaitAsync(TestContext.Current.CancellationToken);
            await secondSettlement.WaitAsync(TestContext.Current.CancellationToken);

            Assert.True(handler.IsDisposed);
            Assert.Equal("retained after settlement", ReadReadme(AssertPayload(result, Id).Content));
            context?.ThrowIfExpired();
        }
        finally
        {
            finish.TrySetResult();
            await pending;
        }
    }

    private sealed class SettlementTrackingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        internal bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
