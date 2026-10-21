using System.Reflection;
using System.Runtime.CompilerServices;
using DotnetInspector.Packages;
using InertText;
using Inspector.Resources;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed class PackageSourceOperationOwnershipTests
{
    private static readonly PackageSourceCoordinate Coordinate =
        PackageSourceCoordinate.Create("contoso.json", "1.0.0");

    [Fact]
    public async Task PackageSourceSettlementLeaseRetirementRejectsNewButAllowsIssuedOperation()
    {
        await using var fixture = new SourceFixture();
        using PackageSourceOperationLease operation = Issue(fixture.Root);
        PackageAcquisitionCandidate before = Candidate(operation, fixture.Authorization);
        Task settlement = fixture.Root.DisposeAsync().AsTask();

        Assert.False(settlement.IsCompleted);
        Assert.Throws<ObjectDisposedException>(() => Issue(fixture.Root));
        Assert.Throws<ObjectDisposedException>(() =>
            fixture.Root.ResolvePinnedCandidate(fixture.Authorization, Coordinate));
        PackageAcquisitionCandidate after = Candidate(operation, fixture.Authorization);
        await operation.AcquireCandidateManifestAsync(before);
        await operation.AcquireCandidateManifestAsync(after);
        PackageVersionDiscoveryResult discovery =
            await operation.DiscoverDependencyVersionsAsync(Coordinate.PackageId, fixture.Authorization);
        await operation.AcquireCandidateManifestAsync(discovery.SelectCandidate(Coordinate.Version));

        operation.Dispose();
        await settlement.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Same(Coordinate, before.Coordinate);
        Assert.Throws<ObjectDisposedException>(() => operation.ThrowIfExpired());
        Assert.Throws<ObjectDisposedException>(() => Candidate(operation, fixture.Authorization));
    }

    [Fact]
    public async Task PackageSourceSettlementLeaseIssuanceCannotRacePastSettlement()
    {
        for (int attempt = 0; attempt < 64; attempt++)
        {
            await using var fixture = new SourceFixture();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<PackageSourceOperationLease?> issuing = Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    return Issue(fixture.Root);
                }
                catch (ObjectDisposedException)
                {
                    return null;
                }
            }, TestContext.Current.CancellationToken);
            Task<Task> retiring = Task.Run(async () =>
            {
                await start.Task;
                return fixture.Root.DisposeAsync().AsTask();
            }, TestContext.Current.CancellationToken);
            start.SetResult();
            using PackageSourceOperationLease? operation = await issuing;
            Task settlement = await retiring;
            if (operation is not null)
            {
                Assert.False(settlement.IsCompleted);
                operation.ThrowIfExpired();
                operation.Dispose();
            }
            await settlement.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Throws<ObjectDisposedException>(() => Issue(fixture.Root));
        }

        await using var invalid = new SourceFixture();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            invalid.Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                requestTimeout: TimeSpan.Zero));
        Assert.True(invalid.Root.DisposeAsync().IsCompletedSuccessfully);
    }

    [Fact]
    public async Task PackageSourceSettlementLeaseSettlementWaitsForIssuedOperations()
    {
        await using var fixture = new SourceFixture();
        using PackageSourceOperationLease first = Issue(fixture.Root);
        using PackageSourceOperationLease second = Issue(fixture.Root);
        Task settlement = fixture.Root.DisposeAsync().AsTask();
        Assert.False(settlement.IsCompleted);
        first.Dispose();
        first.Dispose();
        Assert.False(settlement.IsCompleted);
        second.Dispose();
        await settlement.WaitAsync(TestContext.Current.CancellationToken);
        await fixture.Root.DisposeAsync();
    }

    [Fact]
    public async Task PackageSourceSettlementLeaseSettlementLeavesClientsCallerOwnedAfterQuiescence()
    {
        var fixture = new SourceFixture();
        await using (fixture)
        {
            using PackageSourceOperationLease operation = Issue(fixture.Root);
            await operation.AcquireCandidateManifestAsync(Candidate(operation, fixture.Authorization));
            operation.Dispose();
            await fixture.Root.DisposeAsync();
            Assert.False(fixture.Client.IsDisposed);
            Assert.All(fixture.Client.Contexts,
                context => Assert.Throws<ObjectDisposedException>(context.ThrowIfExpired));
            using var callerContext = new NuGetOperationContext(TestContext.Current.CancellationToken);
            await fixture.OwnedClient.GetManifestAsync(
                Coordinate.PackageId, Coordinate.Version,
                callerContext.CancellationToken, callerContext);
        }
        Assert.True(fixture.Client.IsDisposed);
    }

    [Fact]
    public async Task PackageSourceOperationLeaseOwnsOneContextAcrossSequentialSteps()
    {
        await using var fixture = new SourceFixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                cancellation.Token,
                TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(31));
        PackageAcquisitionCandidateResult pinned = await operation.ResolvePinnedCandidateAsync(
            new FixedAuthorization(fixture.Authorization), Coordinate);
        await operation.AcquireCandidateManifestAsync(pinned.Candidate!);
        PackageVersionDiscoveryResult discovery = await operation.DiscoverDependencyVersionsAsync(
            Coordinate.PackageId, fixture.Authorization);
        PackageAcquisitionCandidate candidate = discovery.SelectCandidate(Coordinate.Version);
        await operation.AcquireCandidateManifestAsync(candidate);
        var store = new InMemoryPackageStore();
        byte[] archive = TestPackageArchive.Create(
            ("contoso.json.nuspec",
             "<package><metadata><id>contoso.json</id><version>1.0.0</version></metadata></package>"u8.ToArray()),
            ("lib/net10.0/contoso.json.dll", [1, 2, 3]));
        IPackageContent content = await store.CommitAsync(
            Coordinate.PackageId, Coordinate.Version, fixture.OwnedClient.Source.Producer.Key,
            new MemoryStream(archive, writable: false), cancellation.Token);
        ConfiguredPackagePayloadResult payload = await operation.AcquireCandidatePayloadAsync(
            candidate, (_, _) => store);

        NuGetOperationContext context = fixture.Client.Contexts[0];
        Assert.Equal(3, fixture.Client.Contexts.Count);
        Assert.All(fixture.Client.Contexts, observed => Assert.Same(context, observed));
        Assert.Equal(cancellation.Token, context.CancellationToken);
        Assert.Equal(TimeSpan.FromSeconds(7), context.RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(31), context.OperationTimeout);
        Assert.Same(content, payload.Payload!.Content);
        Assert.Same(fixture.OwnedClient.Source, payload.Source);
        operation.Dispose();
        Assert.Throws<ObjectDisposedException>(context.ThrowIfExpired);
        Assert.Same(content.GenerationIdentity, payload.Payload.Content.GenerationIdentity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidStepCapacityDoesNotRegisterAnOperation(int capacity)
    {
        await using var fixture = new SourceFixture();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken, null, null, capacity));
        Assert.True(fixture.Root.DisposeAsync().IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ConcurrentStepsShareContextAndRetainOperationUntilEveryStepSettles()
    {
        await using var fixture = new SourceFixture();
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken, null, null,
                maximumConcurrentSteps: 2);
        PackageAcquisitionCandidate candidate = Candidate(operation, fixture.Authorization);
        var firstGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int starts = 0;
        fixture.Client.BeforeStep = _ => (++starts) switch
        {
            1 => firstGate.Task,
            2 => secondGate.Task,
            _ => Task.CompletedTask,
        };
        Task first = operation.AcquireCandidateManifestAsync(candidate);
        Task second = operation.DiscoverDependencyVersionsAsync(
            Coordinate.PackageId, fixture.Authorization);
        try
        {
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.Equal(2, fixture.Client.Contexts.Count);
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = operation.AcquireCandidateManifestAsync(candidate);
            });
            Assert.Throws<InvalidOperationException>(operation.ThrowIfExpired);
            Assert.Throws<InvalidOperationException>(operation.Dispose);
            Task settlement = fixture.Root.DisposeAsync().AsTask();
            Assert.False(settlement.IsCompleted);

            firstGate.SetResult();
            await first;
            Assert.False(second.IsCompleted);
            Assert.Throws<InvalidOperationException>(operation.Dispose);
            await operation.AcquireCandidatePayloadAsync(
                candidate, (_, _) => new InMemoryPackageStore());
            Assert.Equal(3, fixture.Client.Contexts.Count);
            NuGetOperationContext context = fixture.Client.Contexts[0];
            Assert.All(fixture.Client.Contexts, observed => Assert.Same(context, observed));
            Assert.Equal(operation.CancellationToken, context.CancellationToken);
            Assert.False(settlement.IsCompleted);

            secondGate.SetResult();
            await second;
            Assert.False(settlement.IsCompleted);
            operation.Dispose();
            await settlement.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Throws<ObjectDisposedException>(context.ThrowIfExpired);
        }
        finally
        {
            firstGate.TrySetResult();
            secondGate.TrySetResult();
            await Task.WhenAll(first, second);
        }
    }

    [Fact]
    public async Task ConcurrentDependencyDiscoverySharesPendingWorkButAuthorizesEveryCall()
    {
        await using var fixture = new SourceFixture();
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken, null, null,
                maximumConcurrentSteps: 2);
        var authorization = new CountingAuthorization(fixture.Authorization);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.BeforeStep = _ => finish.Task;
        Task<PackageVersionDiscoveryResult> first = operation.DiscoverDependencyVersionsAsync(
            Coordinate.PackageId, authorization);
        Task<PackageVersionDiscoveryResult> second = operation.DiscoverDependencyVersionsAsync(
            Coordinate.PackageId.ToUpperInvariant(), authorization);
        try
        {
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.Equal(2, authorization.Calls);
            Assert.Single(fixture.Client.Contexts);
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = operation.DiscoverDependencyVersionsAsync("contoso.logging", authorization);
            });

            finish.SetResult();
            PackageVersionDiscoveryResult[] results = await Task.WhenAll(first, second);
            Assert.All(results, result =>
                Assert.Equal(PackageVersionDiscoveryState.Authoritative, result.State));
            Assert.NotSame(results[0], results[1]);
            await operation.DiscoverDependencyVersionsAsync(
                Coordinate.PackageId, fixture.Authorization);
            Assert.Single(fixture.Client.Contexts);
            await operation.DiscoverVersionsAsync(
                Coordinate.PackageId, fixture.Authorization,
                PackageVersionDiscoveryContract.CompleteVersionEnumeration);
            Assert.Equal(2, fixture.Client.Contexts.Count);
        }
        finally
        {
            finish.TrySetResult();
            await Task.WhenAll(first, second);
        }
    }

    [Fact]
    public async Task ConcurrentDiscoverySharesCompletedSourcesUntilAggregationAndRetriesPartialEvidence()
    {
        await using var first = new SourceFixture();
        await using var second = new SourceFixture();
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(authority =>
                ReferenceEquals(authority, first.Authorization.Authorities[0])
                    ? first.OwnedClient : second.OwnedClient);
        PackageSourceAuthorization authorization = PackageSourceAuthorization.ObserveAuthorities(
            [first.Authorization.Authorities[0], second.Authorization.Authorities[0]], []);
        using PackageSourceOperationLease operation =
            root.IssueOperationLease(
                TestContext.Current.CancellationToken, null, null,
                maximumConcurrentSteps: 2);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        second.Client.HasAuthoritativeListingState = false;
        second.Client.BeforeStep = _ =>
        {
            entered.TrySetResult();
            return finish.Task;
        };
        Task<PackageVersionDiscoveryResult> pending =
            operation.DiscoverDependencyVersionsAsync(Coordinate.PackageId, authorization);
        Task<PackageVersionDiscoveryResult>? repeated = null;
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            repeated = operation.DiscoverDependencyVersionsAsync(
                Coordinate.PackageId, authorization);
            Assert.False(pending.IsCompleted);
            Assert.False(repeated.IsCompleted);
            Assert.Single(first.Client.Contexts);
            Assert.Single(second.Client.Contexts);
            finish.SetResult();
            PackageVersionDiscoveryResult[] partial = await Task.WhenAll(pending, repeated);
            Assert.All(partial, result =>
                Assert.Equal(PackageVersionDiscoveryState.Partial, result.State));

            second.Client.HasAuthoritativeListingState = true;
            PackageVersionDiscoveryResult retried =
                await operation.DiscoverDependencyVersionsAsync(Coordinate.PackageId, authorization);
            Assert.Equal(PackageVersionDiscoveryState.Authoritative, retried.State);
            Assert.Equal(2, first.Client.Contexts.Count);
            Assert.Equal(2, second.Client.Contexts.Count);
        }
        finally
        {
            finish.TrySetResult();
            await pending;
            if (repeated is not null)
                await repeated;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentDiscoveryFailureFansOutAndRemainsRetryable(bool throws)
    {
        await using var fixture = new SourceFixture();
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken, null, null,
                maximumConcurrentSteps: 2);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new InvalidDataException("Shared source failed.");
        fixture.Client.BeforeStep = _ => finish.Task;
        fixture.Client.VersionFailure = PackageSourceFailureKind.InvalidResponse;
        Task<PackageVersionDiscoveryResult> first =
            operation.DiscoverDependencyVersionsAsync(Coordinate.PackageId, fixture.Authorization);
        Task<PackageVersionDiscoveryResult> second =
            operation.DiscoverDependencyVersionsAsync(Coordinate.PackageId, fixture.Authorization);
        try
        {
            Assert.Single(fixture.Client.Contexts);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            if (throws)
            {
                finish.SetException(expected);
                Assert.Same(expected, await Assert.ThrowsAsync<InvalidDataException>(() => first));
                Assert.Same(expected, await Assert.ThrowsAsync<InvalidDataException>(() => second));
            }
            else
            {
                finish.SetResult();
                PackageVersionDiscoveryResult[] failed = await Task.WhenAll(first, second);
                Assert.All(failed, result =>
                    Assert.NotEqual(PackageVersionDiscoveryState.Authoritative, result.State));
            }

            fixture.Client.BeforeStep = null;
            fixture.Client.VersionFailure = null;
            PackageVersionDiscoveryResult retried =
                await operation.DiscoverDependencyVersionsAsync(
                    Coordinate.PackageId, fixture.Authorization);
            Assert.Equal(PackageVersionDiscoveryState.Authoritative, retried.State);
            Assert.Equal(2, fixture.Client.Contexts.Count);
        }
        finally
        {
            finish.TrySetResult();
            Task settlement = Task.WhenAll(first, second);
            await settlement.ConfigureAwait(
                ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    [Fact]
    public async Task ConcurrentDiscoveryRuntimeReplacementCannotOverwriteAnotherSourcesObservation()
    {
        await using var fixture = new SourceFixture();
        SourceClient? replacement = null;
        using IPackageSourceClient replacementClient = PackageSourceClientFactory.CreateCustom(
            PackageSourceDescriptor.NuGetGallery,
            fixture.Authorization.Authorities[0].Association,
            factory => replacement = new SourceClient(factory));
        IPackageSourceClient current = fixture.OwnedClient;
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => current);
        using PackageSourceOperationLease operation =
            root.IssueOperationLease(
                TestContext.Current.CancellationToken, null, null,
                maximumConcurrentSteps: 3);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.BeforeStep = _ => finish.Task;
        Task<PackageVersionDiscoveryResult> old =
            operation.DiscoverDependencyVersionsAsync(Coordinate.PackageId, fixture.Authorization);
        Task<PackageVersionDiscoveryResult>? oldAgain = null;
        try
        {
            current = replacementClient;
            await operation.DiscoverDependencyVersionsAsync(Coordinate.PackageId, fixture.Authorization);
            current = fixture.OwnedClient;
            oldAgain = operation.DiscoverDependencyVersionsAsync(
                Coordinate.PackageId, fixture.Authorization);
            Assert.Single(fixture.Client.Contexts);
            Assert.Single(replacement!.Contexts);
            Assert.False(oldAgain.IsCompleted);
            finish.SetResult();
            await Task.WhenAll(old, oldAgain);
            current = replacementClient;
            PackageVersionDiscoveryResult result =
                await operation.DiscoverDependencyVersionsAsync(
                    Coordinate.PackageId, fixture.Authorization);
            Assert.Single(replacement.Contexts);
            Assert.Same(replacementClient.Source,
                Assert.Single(result.SelectCandidate(Coordinate.Version).Authorities).Observation!.Source);
        }
        finally
        {
            finish.TrySetResult();
            await old;
            if (oldAgain is not null)
                await oldAgain;
        }
    }

    [Fact]
    public async Task ConcurrentDiscoveryCancellationSettlesEverySharedWaiterWithCallerIdentity()
    {
        await using var fixture = new SourceFixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                cancellation.Token, null, null, maximumConcurrentSteps: 2);
        fixture.Client.BeforeStep = token => Task.Delay(Timeout.InfiniteTimeSpan, token);
        Task<PackageVersionDiscoveryResult>[] pending =
        [
            operation.DiscoverDependencyVersionsAsync(Coordinate.PackageId, fixture.Authorization),
            operation.DiscoverDependencyVersionsAsync(Coordinate.PackageId, fixture.Authorization),
        ];
        try
        {
            Assert.All(pending, task => Assert.False(task.IsCompleted));
            Assert.Single(fixture.Client.Contexts);
            cancellation.Cancel();
            foreach (Task task in pending)
            {
                OperationCanceledException failure =
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
                Assert.Equal(cancellation.Token, failure.CancellationToken);
            }
            operation.Dispose();
            await fixture.Root.DisposeAsync();
        }
        finally
        {
            cancellation.Cancel();
            Task settlement = Task.WhenAll(pending);
            await settlement.ConfigureAwait(
                ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    [Fact]
    public async Task ConcurrentDiscoveryExpiryDoesNotRestartCeilingOrReleasePendingWork()
    {
        await using var fixture = new SourceFixture();
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1),
                maximumConcurrentSteps: 2);
        var firstGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int starts = 0;
        fixture.Client.BeforeStep = _ => ++starts == 1 ? firstGate.Task : secondGate.Task;
        var authorization = new CountingAuthorization(fixture.Authorization);
        Task<PackageVersionDiscoveryResult> first =
            operation.DiscoverDependencyVersionsAsync(Coordinate.PackageId, authorization);
        Task<PackageVersionDiscoveryResult> second =
            operation.DiscoverDependencyVersionsAsync("contoso.logging", authorization);
        try
        {
            Assert.Equal(2, fixture.Client.Contexts.Count);
            Assert.All(fixture.Client.Contexts, context =>
                Assert.Same(fixture.Client.Contexts[0], context));
            Task settlement = fixture.Root.DisposeAsync().AsTask();
            Assert.True(operation.OperationCancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)));
            firstGate.SetResult();
            PackageVersionDiscoveryResult firstExpired = await first;
            Assert.Equal(PackageVersionDiscoveryState.Failed, firstExpired.State);
            Assert.False(second.IsCompleted);
            Assert.Throws<InvalidOperationException>(operation.Dispose);
            PackageVersionDiscoveryResult late =
                await operation.DiscoverDependencyVersionsAsync("contoso.late", authorization);
            Assert.Equal(PackageVersionDiscoveryState.Failed, late.State);
            Assert.Equal(2, authorization.Calls);
            Assert.Equal(2, fixture.Client.Contexts.Count);
            Assert.False(settlement.IsCompleted);
            secondGate.SetResult();
            PackageVersionDiscoveryResult secondExpired = await second;
            Assert.Equal(PackageVersionDiscoveryState.Failed, secondExpired.State);
            Assert.All(new[] { firstExpired, secondExpired, late }, result =>
                Assert.Contains(result.Failures,
                    failure => failure.Timeout?.Kind == PackageSourceTimeoutKind.Operation));
            operation.Dispose();
            await settlement.WaitAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            firstGate.TrySetResult();
            secondGate.TrySetResult();
            await Task.WhenAll(first, second);
        }
    }

    [Fact]
    public async Task DependencyVersionDiscoveryReusesAuthoritativeSourceObservations()
    {
        await using var fixture = new SourceFixture();
        using PackageSourceOperationLease operation = Issue(fixture.Root);
        var authorization = new CountingAuthorization(fixture.Authorization);

        PackageVersionDiscoveryResult first =
            await operation.DiscoverDependencyVersionsAsync(
                Coordinate.PackageId, authorization);
        PackageVersionDiscoveryResult repeated =
            await operation.DiscoverDependencyVersionsAsync(
                Coordinate.PackageId.ToUpperInvariant(), authorization);
        PackageVersionDiscoveryResult explicitAuthorization =
            await operation.DiscoverDependencyVersionsAsync(
                Coordinate.PackageId, fixture.Authorization);

        Assert.Equal(2, authorization.Calls);
        Assert.Single(fixture.Client.Contexts);
        Assert.Equal(PackageVersionDiscoveryState.Authoritative, repeated.State);
        Assert.Equal(
            first.SelectCandidate(Coordinate.Version).Correspondence,
            repeated.SelectCandidate(Coordinate.Version).Correspondence);
        Assert.Equal(
            first.SelectCandidate(Coordinate.Version).Correspondence,
            explicitAuthorization.SelectCandidate(Coordinate.Version).Correspondence);
    }

    [Fact]
    public async Task DependencyVersionDiscoveryDoesNotReuseAnotherOperationOrGeneralDiscovery()
    {
        await using var fixture = new SourceFixture();
        using (PackageSourceOperationLease first = Issue(fixture.Root))
        {
            await first.DiscoverDependencyVersionsAsync(
                Coordinate.PackageId, fixture.Authorization);
            await first.DiscoverVersionsAsync(
                Coordinate.PackageId,
                fixture.Authorization,
                PackageVersionDiscoveryContract.CompleteVersionEnumeration);
        }
        using (PackageSourceOperationLease second = Issue(fixture.Root))
        {
            await second.DiscoverDependencyVersionsAsync(
                Coordinate.PackageId, fixture.Authorization);
            await second.DiscoverDependencyVersionsAsync(
                "contoso.logging", fixture.Authorization);
        }

        Assert.Equal(4, fixture.Client.Contexts.Count);
    }

    [Fact]
    public async Task DependencyVersionDiscoveryKeepsConfiguredAuthoritiesSeparate()
    {
        await using var first = new SourceFixture();
        await using var second = new SourceFixture();
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(authority =>
                ReferenceEquals(
                    authority,
                    first.Authorization.Authorities[0])
                    ? first.OwnedClient
                    : second.OwnedClient);
        using PackageSourceOperationLease operation = Issue(root);

        PackageVersionDiscoveryResult firstResult =
            await operation.DiscoverDependencyVersionsAsync(
                Coordinate.PackageId, first.Authorization);
        PackageVersionDiscoveryResult secondResult =
            await operation.DiscoverDependencyVersionsAsync(
                Coordinate.PackageId, second.Authorization);

        Assert.Single(first.Client.Contexts);
        Assert.Single(second.Client.Contexts);
        Assert.NotEqual(
            firstResult.SelectCandidate(Coordinate.Version).Correspondence,
            secondResult.SelectCandidate(Coordinate.Version).Correspondence);
    }

    [Fact]
    public async Task DependencyVersionDiscoveryReadsAReplacementRuntimeClient()
    {
        await using var fixture = new SourceFixture();
        SourceClient? replacement = null;
        using IPackageSourceClient replacementClient =
            PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                fixture.Authorization.Authorities[0].Association,
                factory => replacement = new SourceClient(factory));
        IPackageSourceClient current = fixture.OwnedClient;
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => current);
        using PackageSourceOperationLease operation = Issue(root);
        await operation.DiscoverDependencyVersionsAsync(
            Coordinate.PackageId, fixture.Authorization);
        current = replacementClient;

        PackageVersionDiscoveryResult result =
            await operation.DiscoverDependencyVersionsAsync(
                Coordinate.PackageId, fixture.Authorization);

        Assert.Single(fixture.Client.Contexts);
        Assert.Single(replacement!.Contexts);
        Assert.Same(
            replacementClient.Source,
            Assert.Single(
                result.SelectCandidate(Coordinate.Version).Authorities)
                .Observation!.Source);
    }

    [Fact]
    public async Task DependencyVersionDiscoveryReusePreservesCurrentAuthorizationFailures()
    {
        await using var fixture = new SourceFixture();
        using PackageSourceOperationLease operation = Issue(fixture.Root);
        await operation.DiscoverDependencyVersionsAsync(
            Coordinate.PackageId, fixture.Authorization);
        var failure = new PackageAuthorityFailure(
            new InertString(TextPolicy.Field, "unavailable-peer"),
            PackageAuthorityFailureKind.Configuration,
            "A configured peer could not be used.");
        PackageSourceAuthorization partial =
            PackageSourceAuthorization.ObserveAuthorities(
                fixture.Authorization.Authorities, [failure]);

        PackageVersionDiscoveryResult result =
            await operation.DiscoverDependencyVersionsAsync(
                Coordinate.PackageId, new FixedAuthorization(partial));
        PackageVersionDiscoveryResult denied =
            await operation.DiscoverDependencyVersionsAsync(
                Coordinate.PackageId,
                new FixedAuthorization(
                    PackageSourceAuthorization.Deny("No mapped source.")));

        Assert.Equal(PackageVersionDiscoveryState.Partial, result.State);
        Assert.Same(failure, Assert.Single(result.Failures));
        Assert.Equal(PackageVersionDiscoveryState.Failed, denied.State);
        Assert.Empty(denied.SourceListings);
        Assert.Single(fixture.Client.Contexts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DependencyVersionDiscoveryRetriesIncompleteSourceObservations(bool failed)
    {
        await using var fixture = new SourceFixture();
        fixture.Client.HasAuthoritativeListingState = false;
        fixture.Client.VersionFailure =
            failed ? PackageSourceFailureKind.InvalidResponse : null;
        using PackageSourceOperationLease operation = Issue(fixture.Root);
        PackageVersionDiscoveryResult incomplete =
            await operation.DiscoverDependencyVersionsAsync(
                Coordinate.PackageId, fixture.Authorization);
        fixture.Client.HasAuthoritativeListingState = true;
        fixture.Client.VersionFailure = null;

        PackageVersionDiscoveryResult retried =
            await operation.DiscoverDependencyVersionsAsync(
                Coordinate.PackageId, fixture.Authorization);
        await operation.DiscoverDependencyVersionsAsync(
            Coordinate.PackageId, fixture.Authorization);

        Assert.NotEqual(
            PackageVersionDiscoveryState.Authoritative, incomplete.State);
        Assert.Equal(PackageVersionDiscoveryState.Authoritative, retried.State);
        Assert.Equal(2, fixture.Client.Contexts.Count);
    }

    [Fact]
    public async Task DependencyVersionDiscoveryReuseObservesCallerCancellation()
    {
        await using var fixture = new SourceFixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(cancellation.Token);
        await operation.DiscoverDependencyVersionsAsync(
            Coordinate.PackageId, fixture.Authorization);
        cancellation.Cancel();

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                operation.DiscoverDependencyVersionsAsync(
                    Coordinate.PackageId, fixture.Authorization));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Single(fixture.Client.Contexts);
    }

    [Fact]
    public async Task DependencyVersionDiscoveryReuseDoesNotOutliveOperationDeadline()
    {
        await using var fixture = new SourceFixture();
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                requestTimeout: TimeSpan.FromSeconds(1),
                operationTimeout: TimeSpan.FromMilliseconds(250));
        var authorization = new CountingAuthorization(fixture.Authorization);
        await operation.DiscoverDependencyVersionsAsync(
            Coordinate.PackageId, authorization);
        Assert.True(operation.OperationCancellationToken.WaitHandle.WaitOne(
            TimeSpan.FromSeconds(10)));

        PackageVersionDiscoveryResult expired =
            await operation.DiscoverDependencyVersionsAsync(
                Coordinate.PackageId, authorization);

        Assert.Equal(PackageVersionDiscoveryState.Failed, expired.State);
        Assert.Contains(
            expired.Failures,
            failure => failure.Timeout?.Kind == PackageSourceTimeoutKind.Operation);
        Assert.Equal(1, authorization.Calls);
        Assert.Single(fixture.Client.Contexts);
    }

    [Fact]
    public async Task PackageSourceOperationLeaseSuccessfulReleaseDoesNotPublishCancellation()
    {
        await using var fixture = new SourceFixture();
        PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);
        CancellationToken operationToken =
            operation.OperationCancellationToken;

        operation.Dispose();

        Assert.False(operationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task AuthorizationFailuresFlowThroughPinnedAndVersionDiscovery()
    {
        await using var fixture = new SourceFixture();
        var failure = new PackageAuthorityFailure(
            new InertString(TextPolicy.Field, "legacy"),
            PackageAuthorityFailureKind.Configuration,
            "A configured peer could not be used.");
        PackageSourceAuthorization authorization =
            PackageSourceAuthorization.ObserveAuthorities(
                fixture.Authorization.Authorities,
                [failure]);
        using PackageSourceOperationLease operation = Issue(fixture.Root);

        PackageAcquisitionCandidateResult pinned =
            operation.ResolvePinnedCandidate(
                authorization,
                Coordinate);
        Assert.NotNull(pinned.Candidate);
        Assert.Same(failure, Assert.Single(pinned.Failures));

        PackageVersionDiscoveryResult discovery =
            await operation.DiscoverDependencyVersionsAsync(
                Coordinate.PackageId,
                authorization);
        Assert.Equal(
            PackageVersionDiscoveryState.Partial,
            discovery.State);
        Assert.Same(failure, Assert.Single(discovery.Failures));
        Assert.NotEmpty(discovery.SourceListings);
    }

    [Fact]
    public async Task ExpiredVersionDiscoveryDoesNotAuthorizeSources()
    {
        await using var fixture = new SourceFixture();
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                requestTimeout: TimeSpan.FromSeconds(1),
                operationTimeout: TimeSpan.FromMilliseconds(25));
        var authorization =
            new CountingAuthorization(fixture.Authorization);
        Thread.Sleep(TimeSpan.FromMilliseconds(100));

        PackageVersionDiscoveryResult discovery =
            await operation.DiscoverDependencyVersionsAsync(
                Coordinate.PackageId,
                authorization);

        Assert.Equal(0, authorization.Calls);
        Assert.Equal(PackageVersionDiscoveryState.Failed, discovery.State);
        Assert.True(
            discovery.Contract.SupportsDependencyRangeResolution);
        Assert.Equal(0, discovery.CandidateObservationCount);
        Assert.Empty(discovery.SourceListings);
        Assert.Contains(
            discovery.Failures,
            failure => failure.Timeout?.Kind
                == PackageSourceTimeoutKind.Operation);
        Assert.Empty(fixture.Client.Contexts);
    }

    [Fact]
    public async Task PackageSourceOperationLeaseRejectsForeignGenerationEvidence()
    {
        await using var fixture = new SourceFixture();
        await using PackageSourceSettlementLease otherRoot =
            PackageSourceSettlementService.IssueLease(_ => fixture.OwnedClient);
        using PackageSourceOperationLease operation = Issue(fixture.Root);
        using PackageSourceOperationLease other = Issue(otherRoot);
        PackageAcquisitionCandidate foreign = Candidate(other, fixture.Authorization);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operation.AcquireCandidateManifestAsync(foreign));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operation.AcquireCandidatePayloadAsync(foreign, (_, _) => new InMemoryPackageStore()));
        PackageVersionDiscoveryResult discovery = await other.DiscoverDependencyVersionsAsync(
            Coordinate.PackageId, fixture.Authorization);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operation.AcquireCandidateManifestAsync(discovery.SelectCandidate(Coordinate.Version)));
        using PackageSourceOperationLease sameGeneration = Issue(fixture.Root);
        await sameGeneration.AcquireCandidateManifestAsync(Candidate(operation, fixture.Authorization));
    }

    [Fact]
    public void PackageSourceOperationAsyncStateOwnsAuthorityWithoutBorrowEscape()
    {
        Type root = typeof(PackageSourceSettlementLease);
        Type operation = typeof(PackageSourceOperationLease);
        Assert.NotNull(root.GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.NotNull(operation.GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(root));
        Assert.False(typeof(IDisposable).IsAssignableFrom(root));
        Assert.True(typeof(IDisposable).IsAssignableFrom(operation));
        Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(operation));

        Type[] resourceTypes = operation.Assembly.GetTypes()
            .Where(type =>
                type.Name.StartsWith("PackageSource", StringComparison.Ordinal) &&
                type.GetCustomAttribute<ResourceOwnershipAttribute>() is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal([operation, root], resourceTypes);

        MethodInfo[] wrappers = operation.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name.EndsWith("Async", StringComparison.Ordinal)).ToArray();
        // Inventory: the bounded DiscoverVersionsAsync overload (Package
        // Version Service refresh) is the ninth wrapper and tenth machine.
        Assert.Equal(9, wrappers.Length);
        Assert.All(wrappers, method =>
        {
            Assert.Null(method.GetCustomAttribute<AsyncStateMachineAttribute>());
            Assert.DoesNotContain(method.GetParameters(),
                parameter => parameter.ParameterType == typeof(NuGetOperationContext));
        });
        AsyncStateMachineAttribute[] machines = operation
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Select(method => method.GetCustomAttribute<AsyncStateMachineAttribute>())
            .OfType<AsyncStateMachineAttribute>().ToArray();
        Assert.Equal(10, machines.Length);
        Assert.All(machines, machine =>
        {
            FieldInfo[] fields = machine.StateMachineType.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.DoesNotContain(fields,
                field => field.FieldType == operation || field.FieldType == root);
        });
    }

    [Fact]
    public async Task PackageSourceOperationLeaseRejectsReleaseWhileAsyncWorkIsActive()
    {
        await using var fixture = new SourceFixture();
        using PackageSourceOperationLease operation = Issue(fixture.Root);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.BeforeStep = _ => finish.Task;
        PackageAcquisitionCandidate candidate = Candidate(operation, fixture.Authorization);
        Task<ConfiguredPackageManifestResult> pending = operation.AcquireCandidateManifestAsync(candidate);
        try
        {
            Assert.False(pending.IsCompleted);
            Assert.Throws<InvalidOperationException>(operation.Dispose);
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = operation.AcquireCandidateManifestAsync(candidate);
            });
            Assert.Throws<InvalidOperationException>(() => Candidate(operation, fixture.Authorization));
            Task settlement = fixture.Root.DisposeAsync().AsTask();
            Assert.False(settlement.IsCompleted);
            finish.SetResult();
            await pending;
            Assert.False(settlement.IsCompleted);
            operation.Dispose();
            operation.Dispose();
            await settlement.WaitAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            finish.TrySetResult();
            await pending;
        }
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("versions")]
    [InlineData("payload")]
    public async Task PackageSourceOperationLeaseCancellationSettlesWorkBeforeRelease(string step)
    {
        await using var fixture = new SourceFixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(cancellation.Token);
        fixture.Client.BeforeStep = token => Task.Delay(Timeout.InfiniteTimeSpan, token);
        Task pending = StartStep(operation, fixture.Authorization, step);
        cancellation.Cancel();
        OperationCanceledException failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pending);
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        operation.Dispose();
        await fixture.Root.DisposeAsync();
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("versions")]
    [InlineData("payload")]
    public async Task PackageSourceOperationLeaseFailureSettlesWorkBeforeRelease(string step)
    {
        await using var fixture = new SourceFixture();
        using PackageSourceOperationLease operation = Issue(fixture.Root);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.BeforeStep = _ => finish.Task;
        var expected = new InvalidDataException("Source failed after suspension.");
        PackageAcquisitionCandidate candidate = Candidate(operation, fixture.Authorization);
        Task pending = StartStep(operation, fixture.Authorization, step);
        finish.SetException(expected);
        Assert.Same(expected, await Assert.ThrowsAsync<InvalidDataException>(() => pending));
        fixture.Client.BeforeStep = _ => throw new InvalidDataException("Synchronous source failure.");
        await Assert.ThrowsAsync<InvalidDataException>(() => StartStep(operation, fixture.Authorization, step));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await operation.ResolvePinnedCandidateAsync(new ThrowingAuthorization(), Coordinate));
        fixture.Client.BeforeStep = null;
        await operation.AcquireCandidateManifestAsync(candidate);
        operation.Dispose();
        await fixture.Root.DisposeAsync();
    }

    private static PackageSourceOperationLease Issue(
        PackageSourceSettlementLease root) =>
        root.IssueOperationLease(TestContext.Current.CancellationToken);

    private static PackageAcquisitionCandidate Candidate(
        PackageSourceOperationLease operation, PackageSourceAuthorization authorization) =>
        Assert.IsType<PackageAcquisitionCandidate>(
            operation.ResolvePinnedCandidate(authorization, Coordinate).Candidate);

    private static Task StartStep(
        PackageSourceOperationLease operation,
        PackageSourceAuthorization authorization,
        string step) => step switch
        {
            "manifest" => operation.AcquireCandidateManifestAsync(Candidate(operation, authorization)),
            "versions" => operation.DiscoverDependencyVersionsAsync(Coordinate.PackageId, authorization),
            "payload" => operation.AcquireCandidatePayloadAsync(
                Candidate(operation, authorization), (_, _) => new InMemoryPackageStore()),
            _ => throw new ArgumentOutOfRangeException(nameof(step)),
        };

    private sealed class FixedAuthorization(PackageSourceAuthorization authorization)
        : IPackageSourceAuthorization
    {
        public PackageSourceAuthorization AuthorizeSourcesFor(string packageId) => authorization;
    }

    private sealed class CountingAuthorization(
        PackageSourceAuthorization authorization)
        : IPackageSourceAuthorization
    {
        public int Calls { get; private set; }

        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId)
        {
            Calls++;
            return authorization;
        }
    }

    private sealed class ThrowingAuthorization : IPackageSourceAuthorization
    {
        public PackageSourceAuthorization AuthorizeSourcesFor(string packageId) =>
            throw new InvalidDataException("Authorization failed.");
    }

    private sealed class SourceFixture : IAsyncDisposable
    {
        internal PackageSourceAuthorization Authorization { get; } =
            PackageSourceAuthorization.Authorize([PackageSource.NuGetOrg]);
        internal SourceClient Client { get; }
        internal IPackageSourceClient OwnedClient { get; }
        internal PackageSourceSettlementLease Root { get; }

        internal SourceFixture()
        {
            SourceClient? client = null;
            OwnedClient = PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                Authorization.Authorities[0].Association,
                factory => client = new SourceClient(factory));
            Client = client!;
            Root = PackageSourceSettlementService.IssueLease(_ => OwnedClient);
        }

        public async ValueTask DisposeAsync()
        {
            await Root.DisposeAsync();
            OwnedClient.Dispose();
        }
    }

    private sealed class SourceClient(PackageSourceResultFactory factory) : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source => factory.Source;
        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.Manifest | PackageSourceCapabilities.VersionEnumeration
            | PackageSourceCapabilities.PackagePayload;
        internal bool IsDisposed { get; private set; }
        internal List<NuGetOperationContext> Contexts { get; } = [];
        internal Func<CancellationToken, Task>? BeforeStep { get; set; }
        internal bool HasAuthoritativeListingState { get; set; } = true;
        internal PackageSourceFailureKind? VersionFailure { get; set; }

        public async Task<PackageSourceOperationResult<PackageSourceManifest>> GetManifestAsync(
            string packageId, string version, CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            Assert.False(IsDisposed);
            Contexts.Add(Assert.IsType<NuGetOperationContext>(operationContext));
            if (BeforeStep is not null)
                await BeforeStep(cancellationToken);
            return factory.FailedManifest(
                PackageSourceCoordinate.Create(packageId, version), PackageSourceFailureKind.NotFound);
        }

        public async Task<PackageSourceOperationResult<PackageVersionResult>> GetVersionsAsync(
            string packageId, CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            Assert.False(IsDisposed);
            Contexts.Add(Assert.IsType<NuGetOperationContext>(operationContext));
            if (BeforeStep is not null)
                await BeforeStep(cancellationToken);
            if (VersionFailure is { } failure)
                return factory.FailedVersions(failure);
            return factory.SucceededVersions(factory.Versions(
                [factory.Candidate(
                    PackageSourceCoordinate.Create(packageId, Coordinate.Version),
                    PackageDiscoveryContract.CompleteVersionEnumeration, PackageListingState.Listed)],
                hasAuthoritativeListingState: HasAuthoritativeListingState));
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
            string query, int take = 20, bool prerelease = false,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) => throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>> SearchByPrefixAsync(
            string prefix, int take = 100, bool prerelease = false,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) => throw new NotSupportedException();

        public async Task<PackageSourceOperationResult<PackageSourcePayload>> GetPackageAsync(
            string packageId, string version, CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            Assert.False(IsDisposed);
            Contexts.Add(Assert.IsType<NuGetOperationContext>(operationContext));
            if (BeforeStep is not null)
                await BeforeStep(cancellationToken);
            return factory.FailedPackage(
                PackageSourceCoordinate.Create(packageId, version), PackageSourceFailureKind.NotFound);
        }

        public Task<PackageSourceOperationResult<PackageSourcePayload>> TryGetSymbolsAsync(
            string packageId, string version, CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) => throw new NotSupportedException();

        public void Dispose() => IsDisposed = true;
    }
}
