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
        var source = new PackageSource(
            "pinned",
            "https://pinned.example/v3/index.json");
        var authorization = new ObservingPackageSourceAuthorization(
            new UniformPackageSourceAuthorization([source]));
        int clientCount = 0;
        PackageDependencyEvidenceDeclaration declaration =
            Declaration("[1.0.0]");

        PackageDependencyCandidateResult result =
            await ExecuteWithIssuedOperationAsync(
                new PackageDependencyCandidateRequest.Declared(declaration),
                authorization,
                authority =>
                {
                    clientCount++;
                    return CreateVersionSourceClient(
                        authority,
                        hasAuthoritativeListingState: true,
                        PackageListingState.Listed,
                        "1.0.0");
                },
                TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult.Resolved resolved =
            Assert.IsType<PackageDependencyCandidateResult.Resolved>(result);
        Assert.Same(declaration, resolved.Declaration);
        Assert.Equal("1.0.0", resolved.Candidate.Coordinate.Version);
        Assert.Equal(
            PackageAcquisitionCandidateKind.CallerPinned,
            resolved.Candidate.Kind);
        Assert.Equal(1, authorization.Calls);
        Assert.Equal(0, clientCount);
    }

    [Fact]
    public async Task CandidateResolution_BareVersionUsesCompleteDiscovery()
    {
        var source = new PackageSource(
            "versions",
            "https://versions.example/v3/index.json");
        var authorization = new ObservingPackageSourceAuthorization(
            new UniformPackageSourceAuthorization([source]));
        int clientCount = 0;

        PackageDependencyCandidateResult result =
            await ExecuteWithIssuedOperationAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("1.0.0")),
                authorization,
                authority =>
                {
                    clientCount++;
                    return CreateVersionSourceClient(
                        authority,
                        hasAuthoritativeListingState: true,
                        PackageListingState.Listed,
                        "1.0.0",
                        "2.0.0");
                },
                TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult.Resolved resolved =
            Assert.IsType<PackageDependencyCandidateResult.Resolved>(result);
        Assert.Equal("1.0.0", resolved.Candidate.Coordinate.Version);
        Assert.Equal(
            PackageAcquisitionCandidateKind.Discovered,
            resolved.Candidate.Kind);
        Assert.Equal(1, authorization.Calls);
        Assert.Equal(1, clientCount);
    }

    [Fact]
    public async Task CandidateResolution_BoundedRangeSelectsAuthorizedCandidate()
    {
        var source = new PackageSource(
            "versions",
            "https://versions.example/v3/index.json");

        PackageDependencyCandidateResult result =
            await ExecuteWithIssuedOperationAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("[2.0.0, 3.0.0)")),
                new UniformPackageSourceAuthorization([source]),
                authority => CreateVersionSourceClient(
                    authority,
                    hasAuthoritativeListingState: true,
                    PackageListingState.Listed,
                    "3.0.0",
                    "2.1.0",
                    "2.0.0",
                    "1.0.0"),
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

    [Fact]
    public async Task CandidateResolution_PartialDiscoveryDoesNotSelect()
    {
        PackageSource[] sources =
        [
            new("versions", "https://versions.example/v3/index.json"),
            new("failed", "https://failed.example/v3/index.json"),
        ];

        PackageDependencyCandidateResult result =
            await ExecuteWithIssuedOperationAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("[1.0.0, 3.0.0)")),
                new UniformPackageSourceAuthorization(sources),
                authority => authority.Source.Name == "versions"
                    ? CreateVersionSourceClient(
                        authority,
                        hasAuthoritativeListingState: true,
                        PackageListingState.Listed,
                        "2.0.0")
                    : CreateFailedVersionSourceClient(authority),
                TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult.Incomplete incomplete =
            Assert.IsType<PackageDependencyCandidateResult.Incomplete>(result);
        var evidence = Assert.IsType<
            PackageDependencyCandidateIncomplete.VersionDiscovery>(
                incomplete.Evidence);
        Assert.Equal(PackageVersionDiscoveryState.Partial, evidence.State);
        Assert.Equal(1, evidence.CandidateObservationCount);
        Assert.Single(evidence.Failures);
    }

    [Fact]
    public async Task CandidateResolution_FailedDiscoveryDoesNotSelect()
    {
        var source = new PackageSource(
            "failed",
            "https://failed.example/v3/index.json");

        PackageDependencyCandidateResult result =
            await ExecuteWithIssuedOperationAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("[1.0.0, 3.0.0)")),
                new UniformPackageSourceAuthorization([source]),
                CreateFailedVersionSourceClient,
                TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult.Incomplete incomplete =
            Assert.IsType<PackageDependencyCandidateResult.Incomplete>(result);
        var evidence = Assert.IsType<
            PackageDependencyCandidateIncomplete.VersionDiscovery>(
                incomplete.Evidence);
        Assert.Equal(PackageVersionDiscoveryState.Failed, evidence.State);
        Assert.Equal(0, evidence.CandidateObservationCount);
        Assert.Single(evidence.Failures);
    }

    [Fact]
    public async Task CandidateResolution_AuthoritativeNoMatchIsFailure()
    {
        var source = new PackageSource(
            "versions",
            "https://versions.example/v3/index.json");

        PackageDependencyCandidateResult result =
            await ExecuteWithIssuedOperationAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("[2.0.0, 3.0.0)")),
                new UniformPackageSourceAuthorization([source]),
                authority => CreateVersionSourceClient(
                    authority,
                    hasAuthoritativeListingState: true,
                    PackageListingState.Listed,
                    "1.0.0"),
                TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult.Failed failed =
            Assert.IsType<PackageDependencyCandidateResult.Failed>(result);
        Assert.IsType<PackageDependencyCandidateFailure.NoMatchingVersion>(
            failed.Failure);
    }

    [Fact]
    public async Task CandidateResolution_RestoredCoordinateMustSatisfyDeclaration()
    {
        var authorization = new ObservingPackageSourceAuthorization(
            new UniformPackageSourceAuthorization(
                [new("pinned", "https://pinned.example/v3/index.json")]));
        PackageDependencyEvidenceDeclaration declaration =
            Declaration("[1.0.0, 2.0.0)");
        var resolvedPackage = new RestoredProjectPackageNodeIdentity(
            new RestoredProjectSelectionIdentity(
                "net8.0",
                new string('a', 64)),
            PackageSourceCoordinate.Create(PackageId, "3.0.0"));

        PackageDependencyCandidateResult result =
            await ExecuteWithIssuedOperationAsync(
                new PackageDependencyCandidateRequest.Restored(
                    declaration,
                    resolvedPackage),
                authorization,
                static _ => throw new InvalidOperationException(
                    "A restored-coordinate mismatch must not create a source client."),
                TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult.Failed failed =
            Assert.IsType<PackageDependencyCandidateResult.Failed>(result);
        Assert.IsType<
            PackageDependencyCandidateFailure.ResolvedCoordinateMismatch>(
                failed.Failure);
        Assert.Equal(0, authorization.Calls);
    }

    [Fact]
    public async Task CandidateResolution_CandidateCorrespondenceExcludesDeclarationIdentity()
    {
        var source = new PackageSource(
            "pinned",
            "https://pinned.example/v3/index.json");
        await using PackageSourceSettlementLease lease =
            PackageSourceSettlementService.IssueLease(
                static _ => throw new InvalidOperationException(
                    "Pinned authorization must not create a source client."));
        var authorization =
            new UniformPackageSourceAuthorization([source]);
        PackageDependencyEvidenceDeclaration first =
            Declaration("[1.0.0]", firstSourceOccurrence: 0);
        PackageDependencyEvidenceDeclaration second =
            Declaration("[1.0.0]", firstSourceOccurrence: 1);

        PackageDependencyCandidateResult.Resolved firstResult;
        using (PackageSourceOperationLease operation =
            lease.IssueOperationLease(
                TestContext.Current.CancellationToken))
        {
            firstResult = Assert.IsType<
                PackageDependencyCandidateResult.Resolved>(
                    await PackageDependencyCandidateQuery.ExecuteAsync(
                        new PackageDependencyCandidateRequest.Declared(first),
                        authorization,
                        operation));
        }
        PackageDependencyCandidateResult.Resolved secondResult;
        using (PackageSourceOperationLease operation =
            lease.IssueOperationLease(
                TestContext.Current.CancellationToken))
        {
            secondResult = Assert.IsType<
                PackageDependencyCandidateResult.Resolved>(
                    await PackageDependencyCandidateQuery.ExecuteAsync(
                        new PackageDependencyCandidateRequest.Declared(second),
                        authorization,
                        operation));
        }

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
        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                    await ExecuteWithIssuedOperationAsync(
                        new PackageDependencyCandidateRequest.Declared(
                            Declaration("1.0.0")),
                        new UniformPackageSourceAuthorization(
                            [new("versions", "https://versions.example/v3/index.json")]),
                        static authority => CreateVersionSourceClient(
                            authority,
                            hasAuthoritativeListingState: true,
                            PackageListingState.Listed,
                            "1.0.0"),
                        cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task CandidateResolution_OperationCancellationPrecedesRestoredMismatch()
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var authorization = new ObservingPackageSourceAuthorization(
            new UniformPackageSourceAuthorization(
                [new("pinned", "https://pinned.example/v3/index.json")]));
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
                    await ExecuteWithIssuedOperationAsync(
                        new PackageDependencyCandidateRequest.Restored(
                            declaration,
                            resolvedPackage),
                        authorization,
                        static _ => throw new InvalidOperationException(
                            "A cancelled operation must not create a source client."),
                        cancellation.Token)
                );

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, authorization.Calls);
    }

    [Theory]
    [InlineData("[1.0.0]")]
    [InlineData("1.0.0")]
    public async Task CandidateResolution_OperationCancellationReachesWork(
        string constraint)
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new PackageSource(
            "versions",
            "https://versions.example/v3/index.json");
        IPackageSourceAuthorization authorization =
            constraint[0] == '['
                ? new CancellationObservingAuthorization(
                    cancellation.Token,
                    entered)
                : new UniformPackageSourceAuthorization([source]);
        Func<ConfiguredPackageAuthority, IPackageSourceClient> createClient =
            constraint[0] == '['
                ? static _ => throw new InvalidOperationException(
                    "Pinned authorization must not create a source client.")
                : authority => CreateCancellationObservingVersionSourceClient(
                    authority,
                    cancellation.Token,
                    entered);
        Task<PackageDependencyCandidateResult> pending = Task.Run(
            async () => await ExecuteWithIssuedOperationAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration(constraint)),
                authorization,
                createClient,
                cancellation.Token),
            TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await pending);

        Assert.Equal(cancellation.Token, exception.CancellationToken);
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
        await using PackageSourceSettlementLease lease =
            PackageSourceSettlementService.IssueLease(
                _ => throw new InvalidOperationException(
                    "Pinned authorization must not create a source client."));
        using PackageSourceOperationLease operation =
            lease.IssueOperationLease(
                TestContext.Current.CancellationToken,
                requestTimeout: TimeSpan.FromSeconds(1),
                operationTimeout: TimeSpan.FromMilliseconds(30));

        PackageDependencyCandidateResult result =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("[1.0.0]")),
                authorization,
                operation);

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
        await using PackageSourceSettlementLease lease =
            PackageSourceSettlementService.IssueLease(
                authority => CreateVersionSourceClient(
                    authority,
                    hasAuthoritativeListingState: true,
                    PackageListingState.Listed,
                    "1.0.0",
                    "2.0.0"));
        using PackageSourceOperationLease operation =
            lease.IssueOperationLease(
                TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult result =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("[1.0.0, 2.0.0]")),
                new UniformPackageSourceAuthorization([configuredSource]),
                operation);

        var resolved = Assert.IsType<
            PackageDependencyCandidateResult.Resolved>(result);
        Assert.Equal("1.0.0", resolved.Candidate.Coordinate.Version);
        Assert.NotNull(
            Assert.Single(resolved.Candidate.Authorities).Observation);
    }

    [Fact]
    public async Task CandidateResolution_SharedDependencyDiscoveryStillSelectsEachRange()
    {
        var authorization = new UniformPackageSourceAuthorization(
            [new PackageSource("gallery", "https://api.nuget.org/v3/index.json")]);
        ConfiguredPackageAuthority authority =
            Assert.Single(authorization.AuthorizeSourcesFor(PackageId).Authorities);
        var observedContexts = new List<NuGetOperationContext?>();
        using IPackageSourceClient client = CreateVersionSourceClient(
            authority,
            hasAuthoritativeListingState: true,
            PackageListingState.Listed,
            PackageDiscoveryContract.CompleteVersionEnumeration,
            observedContexts.Add,
            "1.0.0", "2.0.0", "3.0.0");
        await using PackageSourceSettlementLease lease =
            PackageSourceSettlementService.IssueLease(_ => client);
        using PackageSourceOperationLease operation =
            lease.IssueOperationLease(TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult first =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("[1.0.0, 2.0.0)")),
                authorization,
                operation);
        PackageDependencyCandidateResult second =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("[2.0.0, 3.0.0)")),
                authorization,
                operation);

        Assert.Equal(
            "1.0.0",
            Assert.IsType<PackageDependencyCandidateResult.Resolved>(first)
                .Candidate.Coordinate.Version);
        Assert.Equal(
            "2.0.0",
            Assert.IsType<PackageDependencyCandidateResult.Resolved>(second)
                .Candidate.Coordinate.Version);
        Assert.Single(observedContexts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CandidateResolution_GalleryUnknownListingStateIsIncomplete(
        bool hasAuthoritativeListingState)
    {
        var configuredSource = new PackageSource(
            "gallery",
            "https://api.nuget.org/v3/index.json");
        await using PackageSourceSettlementLease lease =
            PackageSourceSettlementService.IssueLease(
                authority => CreateVersionSourceClient(
                    authority,
                    hasAuthoritativeListingState,
                    PackageListingState.Unknown,
                    "1.0.0"));
        using PackageSourceOperationLease operation =
            lease.IssueOperationLease(
                TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult result =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("1.0.0")),
                new UniformPackageSourceAuthorization([configuredSource]),
                operation);

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
    public async Task CandidateResolution_TransferredOperationSharesOneContextAcrossAuthorities()
    {
        PackageSource[] configuredSources =
        [
            new("first", "https://first.example/v3/index.json"),
            new("second", "https://second.example/v3/index.json"),
        ];
        var observedContexts = new List<NuGetOperationContext?>();
        await using PackageSourceSettlementLease lease =
            PackageSourceSettlementService.IssueLease(
                authority => CreateVersionSourceClient(
                    authority,
                    hasAuthoritativeListingState: true,
                    PackageListingState.Listed,
                    PackageDiscoveryContract.CompleteVersionEnumeration,
                    observedContexts.Add,
                    "1.0.0"));
        using PackageSourceOperationLease operation =
            lease.IssueOperationLease(
                TestContext.Current.CancellationToken);

        PackageDependencyCandidateResult result =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("1.0.0")),
                new UniformPackageSourceAuthorization(configuredSources),
                operation);

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
        CancellationToken operationToken = default;
        await using PackageSourceSettlementLease lease =
            PackageSourceSettlementService.IssueLease(
                authority =>
                {
                    clientCount++;
                    return clientCount == 1
                        ? CreateDeadlineObservingVersionSourceClient(
                            authority,
                            operationToken,
                            "1.0.0")
                        : throw new InvalidOperationException(
                            "A terminal operation timeout must stop later authorities.");
                });
        using PackageSourceOperationLease operation =
            lease.IssueOperationLease(
                TestContext.Current.CancellationToken,
                requestTimeout: TimeSpan.FromSeconds(1),
                operationTimeout: TimeSpan.FromSeconds(1));
        operationToken = operation.OperationCancellationToken;

        PackageDependencyCandidateResult result =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("1.0.0")),
                new UniformPackageSourceAuthorization(configuredSources),
                operation);

        var incomplete = Assert.IsType<
            PackageDependencyCandidateResult.Incomplete>(result);
        var evidence = Assert.IsType<
            PackageDependencyCandidateIncomplete.VersionDiscovery>(
                incomplete.Evidence);
        Assert.Equal(PackageVersionDiscoveryState.Failed, evidence.State);
        Assert.Equal(1, clientCount);
        Assert.Equal(
            ["", "slow", "later"],
            evidence.Failures.Select(
                failure => failure.Authority.ToString()));
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
        await using PackageSourceSettlementLease lease =
            PackageSourceSettlementService.IssueLease(
                authority => CreateVersionSourceClient(
                    authority,
                    hasAuthoritativeListingState: true,
                    PackageListingState.Listed,
                    PackageDiscoveryContract.KeywordSearch,
                    null,
                    "1.0.0"));
        using PackageSourceOperationLease operation =
            lease.IssueOperationLease(
                TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await PackageDependencyCandidateQuery.ExecuteAsync(
                    new PackageDependencyCandidateRequest.Declared(
                        Declaration("1.0.0")),
                    new UniformPackageSourceAuthorization([configuredSource]),
                    operation));
    }

    [Fact]
    public async Task CandidateResolution_OperationTimeoutPreventsLocalSelectionPublication()
    {
        string[] versions =
        [
            .. Enumerable.Range(1, 20_000)
                .Select(index => $"1.0.{index}"),
        ];
        var configuredSource = new PackageSource(
            "versions",
            "https://versions.example/v3/index.json");

        PackageDependencyCandidateResult result =
            await ExecuteWithIssuedOperationAsync(
                new PackageDependencyCandidateRequest.Declared(
                    Declaration("[1.0.0, 2.0.0)")),
                new UniformPackageSourceAuthorization([configuredSource]),
                authority => CreateVersionSourceClient(
                    authority,
                    hasAuthoritativeListingState: true,
                    PackageListingState.Listed,
                    versions),
                TestContext.Current.CancellationToken,
                requestTimeout: TimeSpan.FromSeconds(1),
                operationTimeout: TimeSpan.FromTicks(1));

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
            SourceOccurrenceCount: 1,
            PackageDependencyEvidenceAuthorship.LibraryDeclared);
    }

    private static async ValueTask<PackageDependencyCandidateResult>
        ExecuteWithIssuedOperationAsync(
            PackageDependencyCandidateRequest request,
            IPackageSourceAuthorization authorization,
            Func<ConfiguredPackageAuthority, IPackageSourceClient> createClient,
            CancellationToken cancellationToken,
            TimeSpan? requestTimeout = null,
            TimeSpan? operationTimeout = null)
    {
        await using PackageSourceSettlementLease lease =
            PackageSourceSettlementService.IssueLease(createClient);
        using PackageSourceOperationLease operation =
            lease.IssueOperationLease(
                cancellationToken,
                requestTimeout,
                operationTimeout);
        return await PackageDependencyCandidateQuery.ExecuteAsync(
            request,
            authorization,
            operation);
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

    private static IPackageSourceClient CreateDeadlineObservingVersionSourceClient(
        ConfiguredPackageAuthority authority,
        CancellationToken operationToken,
        params string[] versions) =>
        PackageSourceClientFactory.CreateCustom(
            PackageSourceDescriptor.NuGetGallery,
            authority.Association,
            factory => new DeadlineObservingVersionPackageSource(
                factory,
                operationToken,
                versions));

    private static IPackageSourceClient CreateFailedVersionSourceClient(
        ConfiguredPackageAuthority authority) =>
        PackageSourceClientFactory.CreateCustom(
            PackageSourceDescriptor.NuGetGallery,
            authority.Association,
            factory => new VersionPackageSource(
                factory,
                hasAuthoritativeListingState: false,
                PackageListingState.Unknown,
                PackageDiscoveryContract.CompleteVersionEnumeration,
                observeOperation: null,
                versions: [],
                failure: PackageSourceFailureKind.Transport));

    private static IPackageSourceClient
        CreateCancellationObservingVersionSourceClient(
            ConfiguredPackageAuthority authority,
            CancellationToken expectedToken,
            TaskCompletionSource entered) =>
        PackageSourceClientFactory.CreateCustom(
            PackageSourceDescriptor.NuGetGallery,
            authority.Association,
            factory => new VersionPackageSource(
                factory,
                hasAuthoritativeListingState: false,
                PackageListingState.Unknown,
                PackageDiscoveryContract.CompleteVersionEnumeration,
                observeOperation: null,
                versions: [],
                expectedToken: expectedToken,
                entered: entered));

    private sealed class ObservingPackageSourceAuthorization(
        IPackageSourceAuthorization inner) : IPackageSourceAuthorization
    {
        public int Calls { get; private set; }

        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId)
        {
            Calls++;
            return inner.AuthorizeSourcesFor(packageId);
        }
    }

    private sealed class CancellationObservingAuthorization(
        CancellationToken expectedToken,
        TaskCompletionSource entered) : IPackageSourceAuthorization
    {
        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId)
        {
            entered.TrySetResult();
            expectedToken.WaitHandle.WaitOne();
            expectedToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException(
                "Cancellation did not stop pinned authorization.");
        }
    }

    private sealed class VersionPackageSource(
        PackageSourceResultFactory factory,
        bool hasAuthoritativeListingState,
        PackageListingState listingState,
        PackageDiscoveryContract discoveryContract,
        Action<NuGetOperationContext?>? observeOperation,
        IReadOnlyList<string> versions,
        PackageSourceFailureKind? failure = null,
        CancellationToken? expectedToken = null,
        TaskCompletionSource? entered = null) : IPackageSourceClient
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
            cancellationToken.ThrowIfCancellationRequested();
            observeOperation?.Invoke(operationContext);
            if (expectedToken is { } expected)
            {
                entered!.TrySetResult();
                if (cancellationToken != expected)
                {
                    throw new InvalidOperationException(
                        "The source operation did not receive the operation token.");
                }

                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
                throw new InvalidOperationException(
                    "Cancellation did not stop dependency version discovery.");
            }
            if (failure is { } sourceFailure)
                return factory.FailedVersions(sourceFailure);
            PackageCandidateObservation[] candidates =
            [
                .. versions.Select(version => factory.Candidate(
                    PackageSourceCoordinate.Create(packageId, version),
                    discoveryContract,
                    listingState)),
            ];
            return factory.SucceededVersions(
                factory.Versions(
                    candidates,
                    hasAuthoritativeListingState));
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

    private sealed class DeadlineObservingVersionPackageSource(
        PackageSourceResultFactory factory,
        CancellationToken operationToken,
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
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    operationToken));
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
