using System.Reflection;
using System.Runtime.CompilerServices;
using DotnetInspector.Packages;
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
        Assert.Equal(7, wrappers.Length);
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
        Assert.Equal(6, machines.Length);
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
            return factory.SucceededVersions(factory.Versions(
                [factory.Candidate(
                    PackageSourceCoordinate.Create(packageId, Coordinate.Version),
                    PackageDiscoveryContract.CompleteVersionEnumeration, PackageListingState.Listed)],
                hasAuthoritativeListingState: true));
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
