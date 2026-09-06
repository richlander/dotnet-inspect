using System.Collections.Immutable;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageDependencyCandidateQueryTests
{
    [Fact]
    public async Task CandidateResolution_ExactDeclarationUsesPinnedAuthorization()
    {
        var source = new StubCandidateSource();
        PackageDependencyEvidenceDeclaration declaration =
            Declaration("[1.0.0]");

        PackageDependencyCandidateResult result =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(declaration),
                source,
                TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult.Resolved resolved =
            Assert.IsType<PackageDependencyCandidateResult.Resolved>(result);
        Assert.Same(declaration, resolved.Declaration);
        Assert.Equal("1.0.0", resolved.Candidate.Coordinate.Version);
        Assert.Equal(
            PackageAcquisitionCandidateKind.CallerPinned,
            resolved.Candidate.Kind);
        Assert.Equal(1, source.PinnedCalls);
        Assert.Equal(0, source.DiscoveryCalls);
    }

    [Fact]
    public async Task CandidateResolution_BareVersionUsesCompleteDiscovery()
    {
        var source = new StubCandidateSource(
            discovery: Discovery(
                PackageVersionDiscoveryState.Authoritative,
                PackageVersionDiscoveryContract.DependencyRangeResolution,
                "1.0.0",
                "2.0.0"));

        PackageDependencyCandidateResult result =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("1.0.0")),
                source,
                TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult.Resolved resolved =
            Assert.IsType<PackageDependencyCandidateResult.Resolved>(result);
        Assert.Equal("1.0.0", resolved.Candidate.Coordinate.Version);
        Assert.Equal(
            PackageAcquisitionCandidateKind.Discovered,
            resolved.Candidate.Kind);
        Assert.Equal(0, source.PinnedCalls);
        Assert.Equal(1, source.DiscoveryCalls);
    }

    [Fact]
    public async Task CandidateResolution_BoundedRangeSelectsAuthorizedCandidate()
    {
        var source = new StubCandidateSource(
            discovery: Discovery(
                PackageVersionDiscoveryState.Authoritative,
                PackageVersionDiscoveryContract.DependencyRangeResolution,
                "3.0.0",
                "2.1.0",
                "2.0.0",
                "1.0.0"));

        PackageDependencyCandidateResult result =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("[2.0.0, 3.0.0)")),
                source,
                TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult.Resolved resolved =
            Assert.IsType<PackageDependencyCandidateResult.Resolved>(result);
        Assert.Equal("2.0.0", resolved.Candidate.Coordinate.Version);
        PackageAcquisitionAuthorityEvidence authority =
            Assert.Single(resolved.Candidate.Authorities);
        Assert.NotNull(authority.Observation);
        Assert.Same(
            authority.Authority.Association,
            authority.Observation.Source.Association);
    }

    [Theory]
    [InlineData(PackageVersionDiscoveryState.Partial)]
    [InlineData(PackageVersionDiscoveryState.Failed)]
    public async Task CandidateResolution_IncompleteDiscoveryDoesNotSelect(
        PackageVersionDiscoveryState state)
    {
        var source = new StubCandidateSource(
            discovery: Discovery(
                state,
                PackageVersionDiscoveryContract.DependencyRangeResolution,
                "2.0.0"));

        PackageDependencyCandidateResult result =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("[1.0.0, 3.0.0)")),
                source,
                TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult.Incomplete incomplete =
            Assert.IsType<PackageDependencyCandidateResult.Incomplete>(result);
        var evidence = Assert.IsType<
            PackageDependencyCandidateIncomplete.VersionDiscovery>(
                incomplete.Evidence);
        Assert.Equal(state, evidence.State);
        Assert.Equal(1, evidence.CandidateObservationCount);
        Assert.Single(evidence.Failures);
    }

    [Fact]
    public async Task CandidateResolution_AuthoritativeNoMatchIsFailure()
    {
        var source = new StubCandidateSource(
            discovery: Discovery(
                PackageVersionDiscoveryState.Authoritative,
                PackageVersionDiscoveryContract.DependencyRangeResolution,
                "1.0.0"));

        PackageDependencyCandidateResult result =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("[2.0.0, 3.0.0)")),
                source,
                TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult.Failed failed =
            Assert.IsType<PackageDependencyCandidateResult.Failed>(result);
        Assert.IsType<PackageDependencyCandidateFailure.NoMatchingVersion>(
            failed.Failure);
    }

    [Fact]
    public async Task CandidateResolution_RestoredCoordinateMustSatisfyDeclaration()
    {
        var source = new StubCandidateSource();
        PackageDependencyEvidenceDeclaration declaration =
            Declaration("[1.0.0, 2.0.0)");
        var resolvedPackage = new RestoredProjectPackageNodeIdentity(
            new RestoredProjectSelectionIdentity(
                "net8.0",
                new string('a', 64)),
            PackageSourceCoordinate.Create(PackageId, "3.0.0"));

        PackageDependencyCandidateResult result =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Restored(
                    declaration,
                    resolvedPackage),
                source,
                TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult.Failed failed =
            Assert.IsType<PackageDependencyCandidateResult.Failed>(result);
        Assert.IsType<
            PackageDependencyCandidateFailure.ResolvedCoordinateMismatch>(
                failed.Failure);
        Assert.Equal(0, source.PinnedCalls);
        Assert.Equal(0, source.DiscoveryCalls);
    }

    [Fact]
    public async Task CandidateResolution_CandidateCorrespondenceExcludesDeclarationIdentity()
    {
        var source = new StubCandidateSource();
        PackageDependencyEvidenceDeclaration first =
            Declaration("[1.0.0]", firstSourceOccurrence: 0);
        PackageDependencyEvidenceDeclaration second =
            Declaration("[1.0.0]", firstSourceOccurrence: 1);

        var firstResult = Assert.IsType<
            PackageDependencyCandidateResult.Resolved>(
                await PackageDependencyCandidateQuery.ExecuteAsync(
                    new PackageDependencyCandidateRequest.Declared(first),
                    source,
                    TestContext.Current.CancellationToken));
        var secondResult = Assert.IsType<
            PackageDependencyCandidateResult.Resolved>(
                await PackageDependencyCandidateQuery.ExecuteAsync(
                    new PackageDependencyCandidateRequest.Declared(second),
                    source,
                    TestContext.Current.CancellationToken));

        Assert.NotEqual(first.Identity, second.Identity);
        Assert.Equal(
            firstResult.Candidate.Correspondence,
            secondResult.Candidate.Correspondence);
    }

    [Fact]
    public async Task CandidateResolution_CallerCancellationPropagates()
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var source = new StubCandidateSource(
            discoveryException: new OperationCanceledException(
                cancellation.Token));

        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                    await PackageDependencyCandidateQuery.ExecuteAsync(
                        new PackageDependencyCandidateRequest.Declared(
                            Declaration("1.0.0")),
                        source,
                        cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task CandidateResolution_ContextCancellationPrecedesRestoredMismatch()
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        cancellation.Cancel();
        using var context = new NuGetOperationContext(cancellation.Token);
        var source = new StubCandidateSource();
        PackageDependencyEvidenceDeclaration declaration =
            Declaration("[1.0.0, 2.0.0)");
        var resolvedPackage = new RestoredProjectPackageNodeIdentity(
            new RestoredProjectSelectionIdentity(
                "net8.0",
                new string('a', 64)),
            PackageSourceCoordinate.Create(PackageId, "3.0.0"));

        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
#pragma warning disable xUnit1051 // The default invocation token is the contract under test.
                    await PackageDependencyCandidateQuery.ExecuteAsync(
                        new PackageDependencyCandidateRequest.Restored(
                            declaration,
                            resolvedPackage),
                        source,
                        operationContext: context)
#pragma warning restore xUnit1051
                );

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, source.PinnedCalls);
        Assert.Equal(0, source.DiscoveryCalls);
    }

    [Fact]
    public async Task CandidateResolution_IncompletePinnedAuthorizationIsNotDenial()
    {
        var configuredSource = new PackageSource(
            "browser",
            "https://browser.example/v3/index.json");
        var authorization = new DelayedPackageSourceAuthorization(
            configuredSource,
            TimeSpan.FromMilliseconds(100));
        var source = new AuthorizedPackageDependencyCandidateSource(
            authorization,
            _ => throw new InvalidOperationException(
                "Pinned authorization must not create a source client."));
        using var context = new NuGetOperationContext(
            requestTimeout: TimeSpan.FromSeconds(1),
            operationTimeout: TimeSpan.FromMilliseconds(30),
            TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult result =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("[1.0.0]")),
                source,
                TestContext.Current.CancellationToken,
                context);

        var incomplete = Assert.IsType<
            PackageDependencyCandidateResult.Incomplete>(result);
        var evidence = Assert.IsType<
            PackageDependencyCandidateIncomplete.PinnedAuthorization>(
                incomplete.Evidence);
        PackageAuthorityFailure failure =
            Assert.Single(evidence.Failures);
        Assert.Equal(
            PackageAuthorityFailureKind.Timeout,
            failure.Kind);
        Assert.Equal(1, authorization.Calls);
    }

    [Fact]
    public async Task CandidateResolution_HostNeutralSourceIssuesRangeCandidate()
    {
        var configuredSource = new PackageSource(
            "browser",
            "https://browser.example/v3/index.json");
        var source = new AuthorizedPackageDependencyCandidateSource(
            new UniformPackageSourceAuthorization([configuredSource]),
            authority => CreateVersionSourceClient(
                authority,
                hasAuthoritativeListingState: true,
                PackageListingState.Listed,
                "1.0.0",
                "2.0.0"));

        PackageDependencyCandidateResult result =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("[1.0.0, 2.0.0]")),
                source,
                TestContext.Current.CancellationToken);

        var resolved = Assert.IsType<
            PackageDependencyCandidateResult.Resolved>(result);
        Assert.Equal("1.0.0", resolved.Candidate.Coordinate.Version);
        Assert.NotNull(
            Assert.Single(resolved.Candidate.Authorities).Observation);
    }

    [Fact]
    public async Task CandidateResolution_GalleryUnknownListingStateIsIncomplete()
    {
        var configuredSource = new PackageSource(
            "gallery",
            "https://api.nuget.org/v3/index.json");
        var source = new AuthorizedPackageDependencyCandidateSource(
            new UniformPackageSourceAuthorization([configuredSource]),
            authority => CreateVersionSourceClient(
                authority,
                hasAuthoritativeListingState: false,
                PackageListingState.Unknown,
                "1.0.0"));

        PackageDependencyCandidateResult result =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("1.0.0")),
                source,
                TestContext.Current.CancellationToken);

        var incomplete = Assert.IsType<
            PackageDependencyCandidateResult.Incomplete>(result);
        var evidence = Assert.IsType<
            PackageDependencyCandidateIncomplete.VersionDiscovery>(
                incomplete.Evidence);
        Assert.Equal(
            PackageVersionDiscoveryState.Partial,
            evidence.State);
        Assert.Contains(
            evidence.Failures,
            failure =>
                failure.Kind
                    == PackageAuthorityFailureKind.IncompleteMetadata);
    }

    [Fact]
    public async Task CandidateResolution_QueryOwnsOneSharedOperationContextWhenOmitted()
    {
        PackageSource[] configuredSources =
        [
            new("first", "https://first.example/v3/index.json"),
            new("second", "https://second.example/v3/index.json"),
        ];
        var observedContexts = new List<NuGetOperationContext?>();
        var source = new AuthorizedPackageDependencyCandidateSource(
            new UniformPackageSourceAuthorization(configuredSources),
            authority => CreateVersionSourceClient(
                authority,
                hasAuthoritativeListingState: true,
                PackageListingState.Listed,
                PackageDiscoveryContract.CompleteVersionEnumeration,
                observedContexts.Add,
                "1.0.0"));

        PackageDependencyCandidateResult result =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("1.0.0")),
                source,
                TestContext.Current.CancellationToken);

        Assert.IsType<PackageDependencyCandidateResult.Resolved>(result);
        Assert.Equal(2, observedContexts.Count);
        Assert.NotNull(observedContexts[0]);
        Assert.Same(observedContexts[0], observedContexts[1]);
    }

    [Fact]
    public async Task CandidateResolution_OperationTimeoutStopsLaterAuthorities()
    {
        PackageSource[] configuredSources =
        [
            new("slow", "https://slow.example/v3/index.json"),
            new("later", "https://later.example/v3/index.json"),
        ];
        int clientCount = 0;
        var source = new AuthorizedPackageDependencyCandidateSource(
            new UniformPackageSourceAuthorization(configuredSources),
            authority =>
            {
                clientCount++;
                return clientCount == 1
                    ? CreateDelayedVersionSourceClient(
                        authority,
                        TimeSpan.FromMilliseconds(100),
                        "1.0.0")
                    : throw new InvalidOperationException(
                        "A terminal operation timeout must stop later authorities.");
            });
        using var context = new NuGetOperationContext(
            requestTimeout: TimeSpan.FromSeconds(1),
            operationTimeout: TimeSpan.FromMilliseconds(30),
            TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult result =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("1.0.0")),
                source,
                TestContext.Current.CancellationToken,
                context);

        var incomplete = Assert.IsType<
            PackageDependencyCandidateResult.Incomplete>(result);
        var evidence = Assert.IsType<
            PackageDependencyCandidateIncomplete.VersionDiscovery>(
                incomplete.Evidence);
        Assert.Equal(PackageVersionDiscoveryState.Failed, evidence.State);
        Assert.Equal(1, clientCount);
        Assert.All(
            evidence.Failures,
            failure =>
            {
                Assert.Equal(
                    PackageAuthorityFailureKind.Timeout,
                    failure.Kind);
                Assert.Equal(
                    PackageSourceTimeoutKind.Operation,
                    failure.Timeout?.Kind);
            });
    }

    [Fact]
    public async Task CandidateResolution_RejectsNonEnumerationObservations()
    {
        var configuredSource = new PackageSource(
            "browser",
            "https://browser.example/v3/index.json");
        var source = new AuthorizedPackageDependencyCandidateSource(
            new UniformPackageSourceAuthorization([configuredSource]),
            authority => CreateVersionSourceClient(
                authority,
                hasAuthoritativeListingState: true,
                PackageListingState.Listed,
                PackageDiscoveryContract.KeywordSearch,
                null,
                "1.0.0"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await PackageDependencyCandidateQuery.ExecuteAsync(
                    new PackageDependencyCandidateRequest.Declared(
                        Declaration("1.0.0")),
                    source,
                    TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CandidateResolution_OperationTimeoutPreventsLocalSelectionPublication()
    {
        string[] versions =
        [
            .. Enumerable.Range(1, 20_000)
                .Select(index => $"1.0.{index}"),
        ];
        var source = new StubCandidateSource(
            discovery: Discovery(
                PackageVersionDiscoveryState.Authoritative,
                PackageVersionDiscoveryContract.DependencyRangeResolution,
                versions));
        using var context = new NuGetOperationContext(
            requestTimeout: TimeSpan.FromSeconds(1),
            operationTimeout: TimeSpan.FromTicks(1),
            TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult result =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("[1.0.0, 2.0.0)")),
                source,
                TestContext.Current.CancellationToken,
                context);

        var incomplete = Assert.IsType<
            PackageDependencyCandidateResult.Incomplete>(result);
        var evidence = Assert.IsType<
            PackageDependencyCandidateIncomplete.VersionDiscovery>(
                incomplete.Evidence);
        Assert.Equal(PackageVersionDiscoveryState.Failed, evidence.State);
        Assert.Contains(
            evidence.Failures,
            failure =>
                failure.Timeout?.Kind
                    == PackageSourceTimeoutKind.Operation);
    }

    [Fact]
    public async Task CandidateResolution_RejectsInsufficientDiscoveryContract()
    {
        var source = new StubCandidateSource(
            discovery: Discovery(
                PackageVersionDiscoveryState.Authoritative,
                PackageVersionDiscoveryContract.Create(
                    includePrerelease: true,
                    includeUnlisted: false,
                    limit: 1),
                "1.0.0"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await PackageDependencyCandidateQuery.ExecuteAsync(
                    new PackageDependencyCandidateRequest.Declared(
                        Declaration("1.0.0")),
                    source,
                    TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CandidateResolution_ForeignAuthorityObservationIsRejected()
    {
        ConfiguredPackageAuthority reportingAuthority = Authority("reporter");
        ConfiguredPackageAuthority foreignAuthority = Authority("foreign");
        PackageCandidateObservation observation = Observation(
            reportingAuthority,
            "1.0.0");

        Assert.Throws<InvalidOperationException>(
            () => new ConfiguredPackageCandidateObservation(
                foreignAuthority,
                observation));
    }

    private const string PackageId = "contoso.dependency";

    private static PackageDependencyEvidenceDeclaration Declaration(
        string constraint,
        int firstSourceOccurrence = 0)
    {
        var root = new PackageDependencyEvidenceRootIdentity.Package(
            PackageSourceCoordinate.Create("contoso.root", "1.0.0"));
        var group = new PackageDependencyEvidenceGroupIdentity.Package(
            root,
            IsImplicitManifestGroup: false,
            firstSourceOccurrence);
        return new PackageDependencyEvidenceDeclaration(
            new PackageDependencyEvidenceDeclarationIdentity(
                group,
                PackageId),
            PackageId,
            constraint,
            InertString.Empty,
            InertString.Empty,
            SourceOccurrenceCount: 1);
    }

    private static PackageVersionDiscoveryResult Discovery(
        PackageVersionDiscoveryState state,
        PackageVersionDiscoveryContract contract,
        params string[] versions)
    {
        ConfiguredPackageAuthority authority = Authority("versions");
        var candidates =
            versions.Select(version =>
                new ConfiguredPackageCandidateObservation(
                    authority,
                    Observation(authority, version)))
                .ToArray();
        PackageAuthorityFailure[] failures =
            state == PackageVersionDiscoveryState.Authoritative
                ? []
                : [
                    new PackageAuthorityFailure(
                        InertString.Empty,
                        PackageAuthorityFailureKind.Transport,
                        "The configured authority did not answer."),
                ];
        return new PackageVersionDiscoveryResult(
            state,
            [
                .. versions.Select(version =>
                    new PackageVersionSourceInfo(
                        version,
                        "authority",
                        Listed: true)),
            ],
            failures,
            hasAnyCandidate: versions.Length > 0,
            candidates,
            contract,
            new object());
    }

    private static ConfiguredPackageAuthority Authority(string name) =>
        new(new PackageSource(
            name,
            $"https://{name}.example/v3/index.json"));

    private static PackageCandidateObservation Observation(
        ConfiguredPackageAuthority authority,
        string version)
    {
        PackageSourceResultFactory factory =
            CreateResultFactory(authority.Association);
        return factory.Candidate(
            PackageSourceCoordinate.Create(PackageId, version),
            PackageDiscoveryContract.CompleteVersionEnumeration,
            PackageListingState.Listed);
    }

    private static PackageSourceResultFactory CreateResultFactory(
        PackageSourceAssociation association)
    {
        PackageSourceResultFactory? captured = null;
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                association,
                factory =>
                {
                    captured = factory;
                    return new UnusedPackageSource(factory.Source);
                });
        return Assert.IsType<PackageSourceResultFactory>(captured);
    }

    private static IPackageSourceClient CreateVersionSourceClient(
        ConfiguredPackageAuthority authority,
        bool hasAuthoritativeListingState,
        PackageListingState listingState,
        params string[] versions) =>
        CreateVersionSourceClient(
            authority,
            hasAuthoritativeListingState,
            listingState,
            PackageDiscoveryContract.CompleteVersionEnumeration,
            null,
            versions);

    private static IPackageSourceClient CreateVersionSourceClient(
        ConfiguredPackageAuthority authority,
        bool hasAuthoritativeListingState,
        PackageListingState listingState,
        PackageDiscoveryContract discoveryContract,
        Action<NuGetOperationContext?>? observeOperation,
        params string[] versions) =>
        PackageSourceClientFactory.CreateCustom(
            PackageSourceDescriptor.NuGetGallery,
            authority.Association,
            factory => new VersionPackageSource(
                factory,
                hasAuthoritativeListingState,
                listingState,
                discoveryContract,
                observeOperation,
                versions));

    private static IPackageSourceClient CreateDelayedVersionSourceClient(
        ConfiguredPackageAuthority authority,
        TimeSpan delay,
        params string[] versions) =>
        PackageSourceClientFactory.CreateCustom(
            PackageSourceDescriptor.NuGetGallery,
            authority.Association,
            factory => new DelayedVersionPackageSource(
                factory,
                delay,
                versions));

    private sealed class StubCandidateSource(
        PackageVersionDiscoveryResult? discovery = null,
        Exception? discoveryException = null) :
        IPackageDependencyCandidateSource
    {
        private readonly object _issuer = new();
        private readonly ConfiguredPackageAuthority _authority =
            Authority("pinned");

        public int PinnedCalls { get; private set; }
        public int DiscoveryCalls { get; private set; }

        public ValueTask<PackageAcquisitionCandidateResult>
            ResolvePinnedCandidateAsync(
                PackageSourceCoordinate coordinate,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            PinnedCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                new PackageAcquisitionCandidateResult(
                    PackageAcquisitionCandidateResultState.Resolved,
                    PackageAcquisitionCandidate.CreatePinned(
                        _issuer,
                        coordinate,
                        [_authority]),
                    []));
        }

        public Task<PackageVersionDiscoveryResult>
            DiscoverDependencyVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            DiscoveryCalls++;
            if (discoveryException is not null)
                return Task.FromException<PackageVersionDiscoveryResult>(
                    discoveryException);
            return Task.FromResult(
                discovery
                ?? throw new InvalidOperationException(
                    "No discovery result was configured."));
        }
    }

    private sealed class VersionPackageSource(
        PackageSourceResultFactory factory,
        bool hasAuthoritativeListingState,
        PackageListingState listingState,
        PackageDiscoveryContract discoveryContract,
        Action<NuGetOperationContext?>? observeOperation,
        IReadOnlyList<string> versions) : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source => factory.Source;
        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.VersionEnumeration;

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            observeOperation?.Invoke(operationContext);
            PackageCandidateObservation[] candidates =
            [
                .. versions.Select(version => factory.Candidate(
                    PackageSourceCoordinate.Create(packageId, version),
                    discoveryContract,
                    listingState)),
            ];
            return Task.FromResult(
                factory.SucceededVersions(
                    factory.Versions(
                        candidates,
                        hasAuthoritativeListingState)));
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class DelayedVersionPackageSource(
        PackageSourceResultFactory factory,
        TimeSpan delay,
        IReadOnlyList<string> versions) : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source => factory.Source;
        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.VersionEnumeration;

        public async Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            await Task.Delay(delay, cancellationToken);
            PackageCandidateObservation[] candidates =
            [
                .. versions.Select(version => factory.Candidate(
                    PackageSourceCoordinate.Create(packageId, version),
                    PackageDiscoveryContract.CompleteVersionEnumeration,
                    PackageListingState.Listed)),
            ];
            return factory.SucceededVersions(
                factory.Versions(
                    candidates,
                    hasAuthoritativeListingState: true));
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class DelayedPackageSourceAuthorization(
        PackageSource source,
        TimeSpan delay) : IPackageSourceAuthorization
    {
        public int Calls { get; private set; }

        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId)
        {
            Calls++;
            Thread.Sleep(delay);
            return PackageSourceAuthorization.Authorize([source]);
        }
    }

    private sealed class UnusedPackageSource(
        PackageSourceResultIdentity source) : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source { get; } = source;
        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.None;

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
