using System.Reflection;

namespace NuGetFetch.Tests;

public sealed class PackageSourceCustomClientTests
{
    [Fact]
    public void CustomClientRegistrationValidationAndOwnership()
    {
        PackageSourceAssociation association =
            PackageSourceAssociation.Create();
        PackageSourceDescriptor descriptor =
            PackageSourceDescriptor.NuGetV3(
                "feed",
                "Feed",
                new Uri("https://feed.example/v3/index.json"));
        int callbacks = 0;
        RecordingClient? accepted = null;
        PackageSourceResultFactory? supplied = null;
        IPackageSourceClient adapter =
            PackageSourceClientFactory.CreateCustom(
                descriptor,
                association,
                factory =>
                {
                    callbacks++;
                    supplied = factory;
                    accepted = new RecordingClient(factory.Source);
                    return accepted;
                });

        Assert.Equal(1, callbacks);
        Assert.NotNull(supplied);
        Assert.NotNull(accepted);
        Assert.Same(supplied!.Source, adapter.Source);
        Assert.Same(association, adapter.Source.Association);
        Assert.Equal(PackageSourceKind.NuGetV3, adapter.Source.TransportKind);
        Assert.Equal(0, accepted!.DisposeCount);
        adapter.Dispose();
        adapter.Dispose();
        Assert.Equal(1, accepted.DisposeCount);

        int nullCallbacks = 0;
        Assert.Throws<InvalidOperationException>(
            () => PackageSourceClientFactory.CreateCustom(
                descriptor,
                association,
                _ =>
                {
                    nullCallbacks++;
                    return null!;
                }));
        Assert.Equal(1, nullCallbacks);

        int unsupportedCallbacks = 0;
        Assert.Throws<PackageSourceClientUnavailableException>(
            () => PackageSourceClientFactory.CreateCustom(
                LocalFolderDescriptor(),
                association,
                _ =>
                {
                    unsupportedCallbacks++;
                    return new RecordingClient(null!);
                }));
        Assert.Equal(0, unsupportedCallbacks);

        PackageSourceResultFactory foreign = CreateFactory(
            descriptor,
            association);
        var mismatched = new RecordingClient(foreign.Source);
        InvalidOperationException mismatch =
            Assert.Throws<InvalidOperationException>(
                () => PackageSourceClientFactory.CreateCustom(
                    descriptor,
                    association,
                    _ => mismatched));
        Assert.Contains("bound source identity", mismatch.Message);
        Assert.Equal(1, mismatched.DisposeCount);

        var sourceGetterFailure = new RecordingClient(foreign.Source)
        {
            SourceFailure =
                new InvalidOperationException("source getter failed"),
        };
        InvalidOperationException getterFailure =
            Assert.Throws<InvalidOperationException>(
                () => PackageSourceClientFactory.CreateCustom(
                    descriptor,
                    association,
                    _ => sourceGetterFailure));
        Assert.Equal("source getter failed", getterFailure.Message);
        Assert.Equal(1, sourceGetterFailure.DisposeCount);

        var disposalFailure = new IOException("dispose failed");
        var rejectedWithDisposalFailure =
            new RecordingClient(foreign.Source)
            {
                DisposalFailure = disposalFailure,
            };
        AggregateException aggregate =
            Assert.Throws<AggregateException>(
                () => PackageSourceClientFactory.CreateCustom(
                    descriptor,
                    association,
                    _ => rejectedWithDisposalFailure));
        Assert.IsType<InvalidOperationException>(
            aggregate.InnerExceptions[0]);
        Assert.Same(
            disposalFailure,
            aggregate.InnerExceptions[1]);
        Assert.Equal(1, rejectedWithDisposalFailure.DisposeCount);

        var unreturned = new RecordingClient(foreign.Source);
        var callbackFailure =
            new InvalidOperationException("callback failed");
        InvalidOperationException propagated =
            Assert.Throws<InvalidOperationException>(
                () => PackageSourceClientFactory.CreateCustom(
                    descriptor,
                    association,
                    factory =>
                    {
                        GC.KeepAlive(factory);
                        GC.KeepAlive(unreturned);
                        throw callbackFailure;
                    }));
        Assert.Same(callbackFailure, propagated);
        Assert.Equal(0, unreturned.DisposeCount);
    }

    [Fact]
    public async Task CustomClientOutcomesRemainFactoryBound()
    {
        PackageSourceAssociation association =
            PackageSourceAssociation.Create();
        PackageSourceDescriptor descriptor =
            PackageSourceDescriptor.NuGetV3(
                "feed",
                "Feed",
                new Uri("https://feed.example/v3/index.json"));
        PackageSourceResultFactory foreign = CreateFactory(
            descriptor,
            association);
        RecordingClient? inner = null;
        PackageSourceResultFactory? bound = null;
        using IPackageSourceClient adapter =
            PackageSourceClientFactory.CreateCustom(
                descriptor,
                association,
                factory =>
                {
                    bound = factory;
                    inner = new RecordingClient(factory.Source);
                    return inner;
                });
        Assert.NotNull(bound);
        Assert.NotNull(inner);
        Assert.Equal(bound!.Source, foreign.Source);
        Assert.NotSame(bound.Source, foreign.Source);
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso", "1.0.0");

        inner!.SearchResult = foreign.SucceededSearch(
            foreign.Search([new SearchResult("Contoso", "1.0.0")]));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        inner.PrefixResult = foreign.FailedSearch(
            PackageSourceFailureKind.Transport);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.SearchByPrefixAsync(
                "Contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        PackageCandidateObservation foreignCandidate = foreign.Candidate(
            coordinate,
            PackageDiscoveryContract.CompleteVersionEnumeration,
            PackageListingState.Listed);
        inner.VersionsResult = foreign.SucceededVersions(
            foreign.Versions(
                [foreignCandidate],
                hasAuthoritativeListingState: true));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.GetVersionsAsync(
                "Contoso",
                TestContext.Current.CancellationToken));

        inner.ManifestResult = foreign.SucceededManifest(
            coordinate,
            foreign.Manifest(
                coordinate,
                ReadOnlyMemory<byte>.Empty));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.GetManifestAsync(
                "Contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));

        var foreignPackageStream = new DisposalCountingStream();
        PackageSourcePayload foreignPackage = foreign.Payload(
            coordinate,
            PackageSourcePayloadKind.Package,
            foreignPackageStream);
        inner.PackageResult = foreign.SucceededPackage(
            coordinate,
            foreignPackage);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.GetPackageAsync(
                "Contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        Assert.Equal(1, foreignPackageStream.AsyncDisposeCount);

        var foreignSymbolsStream = new DisposalCountingStream();
        PackageSourcePayload foreignSymbols = foreign.Payload(
            coordinate,
            PackageSourcePayloadKind.Symbols,
            foreignSymbolsStream);
        inner.SymbolsResult = foreign.SucceededSymbols(
            coordinate,
            foreignSymbols);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.TryGetSymbolsAsync(
                "Contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        Assert.Equal(1, foreignSymbolsStream.AsyncDisposeCount);

        inner.PackageResult = bound.FailedSymbols(
            coordinate,
            PackageSourceFailureKind.Transport);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.GetPackageAsync(
                "Contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));

        var wrongKindStream = new DisposalCountingStream();
        PackageSourcePayload wrongKind = bound.Payload(
            coordinate,
            PackageSourcePayloadKind.Symbols,
            wrongKindStream);
        inner.PackageResult = bound.SucceededSymbols(
            coordinate,
            wrongKind);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.GetPackageAsync(
                "Contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        Assert.Equal(1, wrongKindStream.AsyncDisposeCount);

        PackageSourceCoordinate other =
            PackageSourceCoordinate.Create("Other", "2.0.0");
        var wrongCoordinateStream = new DisposalCountingStream();
        PackageSourcePayload wrongCoordinate = bound.Payload(
            other,
            PackageSourcePayloadKind.Package,
            wrongCoordinateStream);
        inner.PackageResult = bound.SucceededPackage(
            other,
            wrongCoordinate);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.GetPackageAsync(
                "Contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        Assert.Equal(1, wrongCoordinateStream.AsyncDisposeCount);

        var throwingStream = new ThrowingAsyncDisposeStream();
        PackageSourcePayload throwingPayload = bound.Payload(
            other,
            PackageSourcePayloadKind.Package,
            throwingStream);
        inner.PackageResult = bound.SucceededPackage(
            other,
            throwingPayload);
        AggregateException disposalAggregate =
            await Assert.ThrowsAsync<AggregateException>(
                () => adapter.GetPackageAsync(
                    "Contoso",
                    "1.0.0",
                    TestContext.Current.CancellationToken));
        Assert.IsType<InvalidOperationException>(
            disposalAggregate.InnerExceptions[0]);
        Assert.Same(
            throwingStream.Failure,
            disposalAggregate.InnerExceptions[1]);
        Assert.Equal(1, throwingStream.AsyncDisposeCount);
        Assert.Equal(0, inner.DisposeCount);

        inner.PackageResult = null;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.GetPackageAsync(
                "Contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));

        var validStream = new DisposalCountingStream();
        PackageSourcePayload valid = bound.Payload(
            coordinate,
            PackageSourcePayloadKind.Package,
            validStream);
        PackageSourceOperationResult<PackageSourcePayload> validOutcome =
            bound.SucceededPackage(coordinate, valid);
        inner.PackageResult = validOutcome;
        PackageSourceOperationResult<PackageSourcePayload> returned =
            await adapter.GetPackageAsync(
                "Contoso",
                "1.0.0",
                TestContext.Current.CancellationToken);
        Assert.Same(validOutcome, returned);
        Assert.Same(validStream, returned.Value!.Content);
        Assert.Equal(0, validStream.AsyncDisposeCount);
        await returned.Value.Content.DisposeAsync();
        Assert.Equal(1, validStream.AsyncDisposeCount);
        Assert.Equal(0, inner.DisposeCount);
    }

    [Fact]
    public async Task CustomClientAdapterForwardsOperationsExactly()
    {
        PackageSourceAssociation association =
            PackageSourceAssociation.Create();
        PackageSourceDescriptor descriptor =
            PackageSourceDescriptor.NuGetV3(
                "feed",
                "Feed",
                new Uri("https://feed.example/v3/index.json"));
        RecordingClient? inner = null;
        PackageSourceResultFactory? factory = null;
        using IPackageSourceClient adapter =
            PackageSourceClientFactory.CreateCustom(
                descriptor,
                association,
                supplied =>
                {
                    factory = supplied;
                    inner = new RecordingClient(supplied.Source)
                    {
                        CapabilityValue =
                            PackageSourceCapabilities.Search
                            | PackageSourceCapabilities.Manifest,
                    };
                    return inner;
                });
        Assert.NotNull(factory);
        Assert.NotNull(inner);
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(
                "Contoso.Package",
                "1.0");
        inner!.SearchResult = factory!.SucceededSearch(
            factory.Search([]));
        inner.PrefixResult = factory.SucceededSearch(
            factory.Search([]));
        inner.VersionsResult = factory.SucceededVersions(
            factory.Versions(
                [],
                hasAuthoritativeListingState: false));
        inner.ManifestResult = factory.SucceededManifest(
            coordinate,
            factory.Manifest(
                coordinate,
                ReadOnlyMemory<byte>.Empty));
        inner.PackageResult = factory.SucceededPackage(
            coordinate,
            factory.Payload(
                coordinate,
                PackageSourcePayloadKind.Package,
                new MemoryStream()));
        inner.SymbolsResult = factory.SucceededSymbols(
            coordinate,
            factory.Payload(
                coordinate,
                PackageSourcePayloadKind.Symbols,
                new MemoryStream()));

        Assert.Equal(inner.CapabilityValue, adapter.Capabilities);
        Assert.Equal(inner.CapabilityValue, adapter.Capabilities);
        Assert.Equal(2, inner.CapabilityReads);
        Assert.Same(factory.Source, adapter.Source);
        Assert.Equal(1, inner.SourceReads);

        using var cancellation = new CancellationTokenSource();
        using var operation = new NuGetOperationContext(
            cancellation.Token);
        await adapter.SearchAsync(
            "raw search",
            take: 17,
            prerelease: true,
            cancellation.Token,
            operation);
        await adapter.SearchByPrefixAsync(
            "Raw.Prefix",
            take: 19,
            prerelease: true,
            cancellation.Token,
            operation);
        await adapter.GetVersionsAsync(
            "Raw.Package",
            cancellation.Token,
            operation);
        await adapter.GetManifestAsync(
            "Contoso.Package",
            "1.0",
            cancellation.Token,
            operation);
        PackageSourcePayload package = (await adapter.GetPackageAsync(
            "Contoso.Package",
            "1.0",
            cancellation.Token,
            operation)).Value!;
        PackageSourcePayload symbols = (await adapter.TryGetSymbolsAsync(
            "Contoso.Package",
            "1.0",
            cancellation.Token,
            operation)).Value!;

        Assert.Equal(
            new SearchCall(
                "raw search",
                17,
                true,
                cancellation.Token,
                operation),
            Assert.Single(inner.SearchCalls));
        Assert.Equal(
            new SearchCall(
                "Raw.Prefix",
                19,
                true,
                cancellation.Token,
                operation),
            Assert.Single(inner.PrefixCalls));
        Assert.Equal(
            new IdCall(
                "Raw.Package",
                cancellation.Token,
                operation),
            Assert.Single(inner.VersionCalls));
        var expectedExact = new ExactCall(
            "Contoso.Package",
            "1.0",
            cancellation.Token,
            operation);
        Assert.Equal(
            expectedExact,
            Assert.Single(inner.ManifestCalls));
        Assert.Equal(
            expectedExact,
            Assert.Single(inner.PackageCalls));
        Assert.Equal(
            expectedExact,
            Assert.Single(inner.SymbolCalls));
        await package.Content.DisposeAsync();
        await symbols.Content.DisposeAsync();
    }

    private static PackageSourceResultFactory CreateFactory(
        PackageSourceDescriptor descriptor,
        PackageSourceAssociation association)
    {
        PackageSourceResultFactory? captured = null;
        using IPackageSourceClient adapter =
            PackageSourceClientFactory.CreateCustom(
                descriptor,
                association,
                factory =>
                {
                    captured = factory;
                    return new RecordingClient(factory.Source);
                });
        return Assert.IsType<PackageSourceResultFactory>(captured);
    }

    private static PackageSourceDescriptor LocalFolderDescriptor()
    {
        ConstructorInfo constructor = Assert.Single(
            typeof(PackageSourceDescriptor).GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic),
            candidate => candidate.GetParameters().Length == 6);
        return (PackageSourceDescriptor)constructor.Invoke(
            [
                "local",
                "Local",
                PackageSourceKind.LocalFolder,
                PackageSourceIdentity.NuGetOrg,
                null,
                true,
            ]);
    }

    private sealed class RecordingClient(
        PackageSourceResultIdentity source)
        : IPackageSourceClient
    {
        private readonly PackageSourceResultIdentity _source = source;

        public Exception? SourceFailure { get; init; }
        public Exception? DisposalFailure { get; init; }
        public int SourceReads { get; private set; }
        public int CapabilityReads { get; private set; }
        public int DisposeCount { get; private set; }
        public PackageSourceCapabilities CapabilityValue { get; init; } =
            PackageSourceCapabilities.Search
            | PackageSourceCapabilities.VersionEnumeration
            | PackageSourceCapabilities.Manifest
            | PackageSourceCapabilities.PackagePayload
            | PackageSourceCapabilities.SymbolPayload;
        public PackageSourceOperationResult<PackageSearchResult>?
            SearchResult { get; set; }
        public PackageSourceOperationResult<PackageSearchResult>?
            PrefixResult { get; set; }
        public PackageSourceOperationResult<PackageVersionResult>?
            VersionsResult { get; set; }
        public PackageSourceOperationResult<PackageSourceManifest>?
            ManifestResult { get; set; }
        public PackageSourceOperationResult<PackageSourcePayload>?
            PackageResult { get; set; }
        public PackageSourceOperationResult<PackageSourcePayload>?
            SymbolsResult { get; set; }
        public List<SearchCall> SearchCalls { get; } = [];
        public List<SearchCall> PrefixCalls { get; } = [];
        public List<IdCall> VersionCalls { get; } = [];
        public List<ExactCall> ManifestCalls { get; } = [];
        public List<ExactCall> PackageCalls { get; } = [];
        public List<ExactCall> SymbolCalls { get; } = [];

        public PackageSourceResultIdentity Source
        {
            get
            {
                SourceReads++;
                if (SourceFailure is not null)
                    throw SourceFailure;
                return _source;
            }
        }

        public PackageSourceCapabilities Capabilities
        {
            get
            {
                CapabilityReads++;
                return CapabilityValue;
            }
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            SearchCalls.Add(
                new SearchCall(
                    query,
                    take,
                    prerelease,
                    cancellationToken,
                    operationContext));
            return Task.FromResult(SearchResult!);
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            PrefixCalls.Add(
                new SearchCall(
                    prefix,
                    take,
                    prerelease,
                    cancellationToken,
                    operationContext));
            return Task.FromResult(PrefixResult!);
        }

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            VersionCalls.Add(
                new IdCall(
                    packageId,
                    cancellationToken,
                    operationContext));
            return Task.FromResult(VersionsResult!);
        }

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            ManifestCalls.Add(
                new ExactCall(
                    packageId,
                    version,
                    cancellationToken,
                    operationContext));
            return Task.FromResult(ManifestResult!);
        }

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            PackageCalls.Add(
                new ExactCall(
                    packageId,
                    version,
                    cancellationToken,
                    operationContext));
            return Task.FromResult(PackageResult!);
        }

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            SymbolCalls.Add(
                new ExactCall(
                    packageId,
                    version,
                    cancellationToken,
                    operationContext));
            return Task.FromResult(SymbolsResult!);
        }

        public void Dispose()
        {
            DisposeCount++;
            if (DisposalFailure is not null)
                throw DisposalFailure;
        }
    }

    private sealed class DisposalCountingStream : MemoryStream
    {
        public int AsyncDisposeCount { get; private set; }

        public override ValueTask DisposeAsync()
        {
            AsyncDisposeCount++;
            base.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingAsyncDisposeStream : MemoryStream
    {
        public IOException Failure { get; } =
            new("payload disposal failed");
        public int AsyncDisposeCount { get; private set; }

        public override ValueTask DisposeAsync()
        {
            AsyncDisposeCount++;
            return ValueTask.FromException(Failure);
        }
    }

    private sealed record SearchCall(
        string Value,
        int Take,
        bool Prerelease,
        CancellationToken CancellationToken,
        NuGetOperationContext? OperationContext);

    private sealed record IdCall(
        string PackageId,
        CancellationToken CancellationToken,
        NuGetOperationContext? OperationContext);

    private sealed record ExactCall(
        string PackageId,
        string Version,
        CancellationToken CancellationToken,
        NuGetOperationContext? OperationContext);
}
