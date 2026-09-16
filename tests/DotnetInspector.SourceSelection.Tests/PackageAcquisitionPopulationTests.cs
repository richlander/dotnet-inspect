using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.SourceSelection.Tests;

public sealed class PackageAcquisitionPopulationTests
{
    [Theory]
    [InlineData(false, "1.5.0")]
    [InlineData(true, "2.0.0-preview.1")]
    public async Task GalleryExactPopulationSelectsLatestEligibleListedVersion(
        bool includePrerelease,
        string expectedVersion)
    {
        await using var fixture = new SourceFixture
        {
            Versions =
            {
                ["contoso.exact"] =
                    ["1.0.0", "1.5.0", "2.0.0-preview.1"],
            },
        };
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);

        PackageAcquisitionPopulation population =
            await PackageAcquisitionPopulationResolver
                .ResolveGalleryExactAsync(
                    operation,
                    "Contoso.Exact",
                    fixture.Authorization,
                    includePrerelease);

        Assert.True(population.IsRequestedPopulationComplete);
        Assert.Equal(
            PackageAcquisitionPopulationCompletionKind.ExactPackageComplete,
            population.Completion);
        PackageAcquisitionCandidate candidate =
            Assert.Single(population.Candidates);
        Assert.Equal("contoso.exact", candidate.Coordinate.PackageId);
        Assert.Equal(expectedVersion, candidate.Coordinate.Version);
        Assert.Equal(
            PackageAcquisitionCandidateKind.Discovered,
            candidate.Kind);
        Assert.Empty(population.Failures);
        Assert.Equal(["Contoso.Exact"], fixture.Client.VersionRequests);
    }

    [Fact]
    public async Task GalleryExactPopulationTreatsAuthoritativeAbsenceAsComplete()
    {
        await using var fixture = new SourceFixture();
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);

        PackageAcquisitionPopulation population =
            await PackageAcquisitionPopulationResolver
                .ResolveGalleryExactAsync(
                    operation,
                    "Contoso.Missing",
                    fixture.Authorization);

        Assert.True(population.IsRequestedPopulationComplete);
        Assert.Equal(
            PackageAcquisitionPopulationCompletionKind.ExactPackageComplete,
            population.Completion);
        Assert.Empty(population.Candidates);
        Assert.Empty(population.Failures);
    }

    [Fact]
    public async Task ExactPopulationFreezesUniqueCoordinatesInCallerOrder()
    {
        await using var fixture = new SourceFixture();
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);
        PackageSourceCoordinate[] coordinates =
        [
            PackageSourceCoordinate.Create("Contoso.Second", "2.0.0"),
            PackageSourceCoordinate.Create("Contoso.First", "1.0.0"),
        ];

        Task<PackageAcquisitionPopulation> pending =
            operation.ResolvePinnedPopulationAsync(
                new FixedAuthorization(fixture.Authorization),
                coordinates);
        coordinates[0] =
            PackageSourceCoordinate.Create("Contoso.Changed", "3.0.0");
        PackageAcquisitionPopulation population = await pending;

        Assert.True(population.IsRequestedPopulationComplete);
        Assert.Equal(
            PackageAcquisitionPopulationCompletionKind.ExactCoordinates,
            population.Completion);
        Assert.Equal(2, population.RequestedCandidates);
        Assert.Equal(
            ["contoso.second@2.0.0", "contoso.first@1.0.0"],
            population.Candidates.Select(candidate =>
                $"{candidate.Coordinate.PackageId}@{candidate.Coordinate.Version}"));
        Assert.All(population.Candidates, candidate =>
            Assert.Equal(
                PackageAcquisitionCandidateKind.CallerPinned,
                candidate.Kind));

        await operation.AcquireCandidateManifestAsync(
            population.Candidates[0]);
        await using var otherFixture = new SourceFixture();
        using PackageSourceOperationLease otherOperation =
            otherFixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            otherOperation.AcquireCandidateManifestAsync(
                population.Candidates[0]));
    }

    [Fact]
    public async Task ExactPopulationRejectsDuplicateAndOutOfRangeSelections()
    {
        await using var fixture = new SourceFixture();
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);
        var authorization =
            new FixedAuthorization(fixture.Authorization);
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso", "1.0.0");

        Assert.Throws<ArgumentException>(() =>
        {
            _ = operation.ResolvePinnedPopulationAsync(
                authorization,
                [coordinate, coordinate]);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            _ = operation.ResolvePinnedPopulationAsync(
                authorization,
                []);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            _ = operation.ResolvePinnedPopulationAsync(
                authorization,
                Enumerable.Range(
                        0,
                        PackageAcquisitionPopulation.MaximumCandidates + 1)
                    .Select(index =>
                        PackageSourceCoordinate.Create(
                            $"Contoso.{index}",
                            "1.0.0"))
                    .ToArray());
        });
    }

    [Fact]
    public async Task ExactPopulationRetainsAuthorizationFailureBesideResolvedCandidates()
    {
        await using var fixture = new SourceFixture();
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);
        var authorization = new SelectiveAuthorization(
            fixture.Authorization,
            "contoso.denied",
            "contoso.denied.later");

        PackageAcquisitionPopulation population =
            await operation.ResolvePinnedPopulationAsync(
                authorization,
                [
                    PackageSourceCoordinate.Create(
                        "Contoso.Allowed.First",
                        "1.0.0"),
                    PackageSourceCoordinate.Create(
                        "Contoso.Denied",
                        "2.0.0"),
                    PackageSourceCoordinate.Create(
                        "Contoso.Allowed.Second",
                        "3.0.0"),
                    PackageSourceCoordinate.Create(
                        "Contoso.Denied.Later",
                        "4.0.0"),
                ]);

        Assert.False(population.IsRequestedPopulationComplete);
        Assert.Equal(
            PackageAcquisitionPopulationCompletionKind.ExactCoordinates,
            population.Completion);
        Assert.Equal(
            ["contoso.allowed.first", "contoso.allowed.second"],
            population.Candidates.Select(candidate =>
                candidate.Coordinate.PackageId));
        Assert.Equal(
            [2, 4],
            population.Failures.Select(failure =>
                failure.CandidateOrdinal));
        Assert.Equal(
            ["contoso.denied@2.0.0", "contoso.denied.later@4.0.0"],
            population.Failures.Select(failure =>
                $"{failure.PackageId}@{failure.Coordinate!.Version}"));
        Assert.All(population.Failures, failure =>
            Assert.Equal(
                PackageAuthorityFailureKind.Configuration,
                failure.Failure.Kind));
    }

    [Fact]
    public async Task PrefixPopulationSelectsLatestEligibleVersionsInSearchOrder()
    {
        await using var fixture = new SourceFixture
        {
            SearchResults =
            [
                new SearchResult("Contoso.Second", "1.0.0"),
                new SearchResult("Contoso.First", "1.0.0"),
            ],
            SearchTruncation =
                PackageSearchTruncationReason.RequestedLimit,
            Versions =
            {
                ["contoso.second"] =
                    ["1.0.0", "2.0.0-preview.1", "1.5.0"],
                ["contoso.first"] =
                    ["2.0.0", "3.0.0"],
            },
        };
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);

        PackageAcquisitionPopulation population =
            await PackageAcquisitionPopulationResolver
                .ResolveGalleryPrefixAsync(
                    operation,
                    new PackagePrefixDeclaration("Contoso."),
                    maximumCandidates: 2,
                    fixture.Authorization);

        Assert.True(population.IsRequestedPopulationComplete);
        Assert.Equal(
            PackageAcquisitionPopulationCompletionKind.CandidateLimitReached,
            population.Completion);
        Assert.Equal(2, fixture.Client.SearchTake);
        Assert.Equal(
            ["contoso.second", "contoso.first"],
            fixture.Client.VersionRequests);
        Assert.Equal(
            ["contoso.second@1.5.0", "contoso.first@3.0.0"],
            population.Candidates.Select(candidate =>
                $"{candidate.Coordinate.PackageId}@{candidate.Coordinate.Version}"));
        Assert.All(population.Candidates, candidate =>
            Assert.Equal(
                PackageAcquisitionCandidateKind.Discovered,
                candidate.Kind));
    }

    [Fact]
    public async Task PrefixPopulationPreservesSourceLimitAndVersionFailure()
    {
        await using var fixture = new SourceFixture
        {
            SearchResults =
            [
                new SearchResult("Contoso.First", "1.0.0"),
                new SearchResult("Contoso.Second", "2.0.0"),
            ],
            SearchTruncation =
                PackageSearchTruncationReason.SourcePageLimit,
            Versions =
            {
                ["contoso.first"] = ["1.0.0"],
            },
            VersionFailures =
            {
                ["contoso.second"] =
                    PackageSourceFailureKind.Transport,
            },
        };
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);

        PackageAcquisitionPopulation population =
            await PackageAcquisitionPopulationResolver
                .ResolveGalleryPrefixAsync(
                    operation,
                    new PackagePrefixDeclaration("Contoso."),
                    maximumCandidates: 3,
                    fixture.Authorization);

        Assert.False(population.IsRequestedPopulationComplete);
        Assert.Equal(
            PackageAcquisitionPopulationCompletionKind.SourcePageLimitReached,
            population.Completion);
        Assert.Equal(
            "contoso.first",
            Assert.Single(population.Candidates).Coordinate.PackageId);
        PackageAcquisitionPopulationFailure failure =
            Assert.Single(population.Failures);
        Assert.Equal(2, failure.CandidateOrdinal);
        Assert.Equal("contoso.second", failure.PackageId);
        Assert.Equal(
            PackageAuthorityFailureKind.Transport,
            failure.Failure.Kind);
        Assert.NotNull(failure.Failure.SourceFailure);
    }

    [Fact]
    public async Task PrefixPopulationRejectsDuplicateSourceRows()
    {
        await using var fixture = new SourceFixture
        {
            SearchResults =
            [
                new SearchResult("Contoso.Duplicate", "1.0.0"),
                new SearchResult("contoso.duplicate", "1.0.0"),
            ],
            SearchTruncation =
                PackageSearchTruncationReason.RequestedLimit,
        };
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);

        PackageAcquisitionPopulation population =
            await PackageAcquisitionPopulationResolver
                .ResolveGalleryPrefixAsync(
                    operation,
                    new PackagePrefixDeclaration("Contoso."),
                    maximumCandidates: 2,
                    fixture.Authorization);

        Assert.False(population.IsRequestedPopulationComplete);
        Assert.Empty(population.Candidates);
        Assert.Equal(
            PackageAcquisitionPopulationCompletionKind.SourceFailed,
            population.Completion);
        Assert.Equal(
            PackageAuthorityFailureKind.InvalidResponse,
            Assert.Single(population.Failures).Failure.Kind);
        Assert.Null(
            Assert.Single(population.Failures).CandidateOrdinal);
        Assert.Empty(fixture.Client.VersionRequests);
    }

    [Fact]
    public async Task PrefixPopulationRejectsNonCanonicalSourceId()
    {
        await using var fixture = new SourceFixture
        {
            SearchResults =
            [
                new SearchResult("Contoso.\u00e9", "1.0.0"),
            ],
            SearchTruncation =
                PackageSearchTruncationReason.RequestedLimit,
        };
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);

        PackageAcquisitionPopulation population =
            await PackageAcquisitionPopulationResolver
                .ResolveGalleryPrefixAsync(
                    operation,
                    new PackagePrefixDeclaration("Contoso."),
                    maximumCandidates: 1,
                    fixture.Authorization);

        Assert.False(population.IsRequestedPopulationComplete);
        Assert.Empty(population.Candidates);
        Assert.Equal(
            PackageAcquisitionPopulationCompletionKind.SourceFailed,
            population.Completion);
        Assert.Equal(
            PackageAuthorityFailureKind.InvalidResponse,
            Assert.Single(population.Failures).Failure.Kind);
        Assert.Empty(fixture.Client.VersionRequests);
    }

    [Theory]
    [InlineData(
        PackageSearchTruncationReason.RequestedLimit,
        PackageAcquisitionPopulationCompletionKind.CandidateLimitReached,
        true)]
    [InlineData(
        PackageSearchTruncationReason.SourcePageLimit,
        PackageAcquisitionPopulationCompletionKind.SourcePageLimitReached,
        false)]
    [InlineData(
        PackageSearchTruncationReason.ClientPageLimit,
        PackageAcquisitionPopulationCompletionKind.ClientPageLimitReached,
        false)]
    public async Task PrefixPopulationConsumesTerminalPageAfterFillingBound(
        PackageSearchTruncationReason terminalReason,
        PackageAcquisitionPopulationCompletionKind expectedCompletion,
        bool expectedComplete)
    {
        await using var fixture = new SourceFixture
        {
            SearchPages =
            [
                new(
                    [new SearchResult("Contoso.One", "1.0.0")],
                    PackageSearchTruncationReason.None),
                new([], terminalReason),
            ],
            Versions =
            {
                ["contoso.one"] = ["1.0.0"],
            },
        };
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);

        PackageAcquisitionPopulation population =
            await PackageAcquisitionPopulationResolver
                .ResolveGalleryPrefixAsync(
                    operation,
                    new PackagePrefixDeclaration("Contoso."),
                    maximumCandidates: 1,
                    fixture.Authorization);

        Assert.Equal(2, fixture.Client.SearchPagesRead);
        Assert.Equal(
            expectedComplete,
            population.IsRequestedPopulationComplete);
        Assert.Equal(expectedCompletion, population.Completion);
        Assert.Empty(population.Failures);
        Assert.Equal(
            "contoso.one",
            Assert.Single(population.Candidates).Coordinate.PackageId);
    }

    [Fact]
    public async Task PrefixPopulationPreservesTerminalFailureAfterFillingBound()
    {
        await using var fixture = new SourceFixture
        {
            SearchPages =
            [
                new(
                    [new SearchResult("Contoso.One", "1.0.0")],
                    PackageSearchTruncationReason.None),
                SearchPage.Failed(PackageSourceFailureKind.Transport),
            ],
            Versions =
            {
                ["contoso.one"] = ["1.0.0"],
            },
        };
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);

        PackageAcquisitionPopulation population =
            await PackageAcquisitionPopulationResolver
                .ResolveGalleryPrefixAsync(
                    operation,
                    new PackagePrefixDeclaration("Contoso."),
                    maximumCandidates: 1,
                    fixture.Authorization);

        Assert.Equal(2, fixture.Client.SearchPagesRead);
        Assert.False(population.IsRequestedPopulationComplete);
        Assert.Equal(
            PackageAcquisitionPopulationCompletionKind.SourceFailed,
            population.Completion);
        Assert.Equal(
            PackageAuthorityFailureKind.Transport,
            Assert.Single(population.Failures).Failure.Kind);
        Assert.Equal(
            "contoso.one",
            Assert.Single(population.Candidates).Coordinate.PackageId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrefixPopulationRejectsUnderfilledCandidateLimit(
        bool multiplePages)
    {
        SearchPage[] pages = multiplePages
            ? [
                new(
                    [new SearchResult("Contoso.One", "1.0.0")],
                    PackageSearchTruncationReason.None),
                new(
                    [],
                    PackageSearchTruncationReason.RequestedLimit),
            ]
            : [
                new(
                    [new SearchResult("Contoso.One", "1.0.0")],
                    PackageSearchTruncationReason.RequestedLimit),
            ];
        await using var fixture = new SourceFixture
        {
            SearchPages = pages,
        };
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);

        PackageAcquisitionPopulation population =
            await PackageAcquisitionPopulationResolver
                .ResolveGalleryPrefixAsync(
                    operation,
                    new PackagePrefixDeclaration("Contoso."),
                    maximumCandidates: 2,
                    fixture.Authorization);

        Assert.False(population.IsRequestedPopulationComplete);
        Assert.Empty(population.Candidates);
        Assert.Equal(
            PackageAcquisitionPopulationCompletionKind.SourceFailed,
            population.Completion);
        Assert.Equal(
            PackageAuthorityFailureKind.InvalidResponse,
            Assert.Single(population.Failures).Failure.Kind);
        Assert.Empty(fixture.Client.VersionRequests);
    }

    [Fact]
    public async Task PrefixPopulationContinuesAfterRequestTimeout()
    {
        await using var fixture = new SourceFixture
        {
            SearchResults =
            [
                new SearchResult("Contoso.Timeout", "1.0.0"),
                new SearchResult("Contoso.Later", "2.0.0"),
            ],
            SearchTruncation =
                PackageSearchTruncationReason.RequestedLimit,
            Versions =
            {
                ["contoso.later"] = ["2.0.0"],
            },
            VersionFailures =
            {
                ["contoso.timeout"] =
                    PackageSourceFailureKind.Timeout,
            },
        };
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);

        PackageAcquisitionPopulation population =
            await PackageAcquisitionPopulationResolver
                .ResolveGalleryPrefixAsync(
                    operation,
                    new PackagePrefixDeclaration("Contoso."),
                    maximumCandidates: 2,
                    fixture.Authorization);

        Assert.False(population.IsRequestedPopulationComplete);
        Assert.Equal(
            ["contoso.timeout", "contoso.later"],
            fixture.Client.VersionRequests);
        Assert.Equal(
            "contoso.later",
            Assert.Single(population.Candidates).Coordinate.PackageId);
        PackageAcquisitionPopulationFailure failure =
            Assert.Single(population.Failures);
        Assert.Equal(1, failure.CandidateOrdinal);
        Assert.Equal(
            PackageAuthorityFailureKind.Timeout,
            failure.Failure.Kind);
        Assert.Null(failure.Failure.Timeout);
    }

    [Fact]
    public async Task PrefixPopulationStopsAfterOperationTimeout()
    {
        await using var fixture = new SourceFixture
        {
            SearchResults =
            [
                new SearchResult("Contoso.Timeout", "1.0.0"),
                new SearchResult("Contoso.Later", "2.0.0"),
            ],
            SearchTruncation =
                PackageSearchTruncationReason.RequestedLimit,
            Versions =
            {
                ["contoso.timeout"] = ["1.0.0"],
                ["contoso.later"] = ["2.0.0"],
            },
        };
        fixture.Client.BeforeVersion = async packageId =>
        {
            if (packageId.Equals(
                    "contoso.timeout",
                    StringComparison.Ordinal))
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(100),
                    TestContext.Current.CancellationToken);
            }
        };
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                operationTimeout: TimeSpan.FromMilliseconds(50));

        PackageAcquisitionPopulation population =
            await PackageAcquisitionPopulationResolver
                .ResolveGalleryPrefixAsync(
                    operation,
                    new PackagePrefixDeclaration("Contoso."),
                    maximumCandidates: 2,
                    fixture.Authorization);

        Assert.False(population.IsRequestedPopulationComplete);
        Assert.Equal(
            ["contoso.timeout"],
            fixture.Client.VersionRequests);
        Assert.Empty(population.Candidates);
        PackageAcquisitionPopulationFailure failure =
            Assert.Single(population.Failures);
        Assert.Equal(
            PackageSourceTimeoutKind.Operation,
            failure.Failure.Timeout!.Kind);
    }

    [Fact]
    public async Task PrefixPopulationRequiresCredentialFreeGalleryAuthority()
    {
        await using var fixture = new SourceFixture();
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);

        var credentialed = new PackageSource(
            PackageSource.NuGetOrg.Name,
            PackageSource.NuGetOrg.Url,
            new PackageSourceCredential("user", "secret"));
        var other = new PackageSource(
            "other",
            "https://feed.example/v3/index.json");
        PackageSourceAuthorization[] invalid =
        [
            PackageSourceAuthorization.Deny(
                "No Gallery authority was selected."),
            PackageSourceAuthorization.Authorize([credentialed]),
            PackageSourceAuthorization.Authorize([other]),
            PackageSourceAuthorization.Authorize(
                [PackageSource.NuGetOrg, other]),
        ];

        foreach (PackageSourceAuthorization authorization in invalid)
        {
            Assert.Throws<ArgumentException>(() =>
            {
                _ = PackageAcquisitionPopulationResolver
                    .ResolveGalleryPrefixAsync(
                        operation,
                        new PackagePrefixDeclaration("Contoso."),
                        maximumCandidates: 1,
                        authorization);
            });
        }
    }

    [Fact]
    public async Task PrefixPopulationRequiresGalleryTransport()
    {
        PackageSourceDescriptor v3 =
            PackageSourceDescriptor.NuGetV3(
                "nuget-v3",
                "NuGet.org v3",
                new Uri(PackageSource.NuGetOrg.Url));
        await using var fixture = new SourceFixture(v3);
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PackageAcquisitionPopulationResolver.ResolveGalleryPrefixAsync(
                operation,
                new PackagePrefixDeclaration("Contoso."),
                maximumCandidates: 1,
                fixture.Authorization));
    }

    [Fact]
    public async Task PopulationCancellationRetainsCallerToken()
    {
        await using var fixture = new SourceFixture();
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        using PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(cancellation.Token);
        cancellation.Cancel();

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                operation.ResolvePinnedPopulationAsync(
                    new FixedAuthorization(fixture.Authorization),
                    [
                        PackageSourceCoordinate.Create(
                            "Contoso",
                            "1.0.0"),
                    ]));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
    }

    private sealed class FixedAuthorization(
        PackageSourceAuthorization authorization)
        : IPackageSourceAuthorization
    {
        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId) => authorization;
    }

    private sealed class SelectiveAuthorization
        : IPackageSourceAuthorization
    {
        private readonly PackageSourceAuthorization _authorization;
        private readonly HashSet<string> _deniedPackageIds;

        internal SelectiveAuthorization(
            PackageSourceAuthorization authorization,
            params string[] deniedPackageIds)
        {
            _authorization = authorization;
            _deniedPackageIds = new(
                deniedPackageIds,
                StringComparer.OrdinalIgnoreCase);
        }

        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId) =>
            _deniedPackageIds.Contains(packageId)
                ? PackageSourceAuthorization.Deny(
                    "The package is outside the selected source policy.")
                : _authorization;
    }

    private sealed class SourceFixture : IAsyncDisposable
    {
        internal PackageSourceAuthorization Authorization { get; } =
            PackageSourceAuthorization.Authorize(
                [PackageSource.NuGetOrg]);
        internal SourceClient Client { get; }
        internal IPackageSourceClient OwnedClient { get; }
        internal PackageSourceSettlementLease Root { get; }

        internal IReadOnlyList<SearchResult> SearchResults
        {
            init => Client.SearchResults = value;
        }

        internal PackageSearchTruncationReason SearchTruncation
        {
            init => Client.SearchTruncation = value;
        }

        internal IReadOnlyList<SearchPage> SearchPages
        {
            init => Client.SearchPages = value;
        }

        internal Dictionary<string, IReadOnlyList<string>> Versions =>
            Client.Versions;

        internal Dictionary<string, PackageSourceFailureKind>
            VersionFailures => Client.VersionFailures;

        internal SourceFixture(
            PackageSourceDescriptor? descriptor = null)
        {
            SourceClient? client = null;
            OwnedClient = PackageSourceClientFactory.CreateCustom(
                descriptor ?? PackageSourceDescriptor.NuGetGallery,
                Authorization.Authorities[0].Association,
                factory => client = new SourceClient(factory));
            Client = client!;
            Root = PackageSourceSettlementService.IssueLease(
                authority =>
                {
                    Assert.Same(
                        Authorization.Authorities[0],
                        authority);
                    return OwnedClient;
                });
        }

        public async ValueTask DisposeAsync()
        {
            await Root.DisposeAsync();
            OwnedClient.Dispose();
        }
    }

    private sealed class SourceClient(
        PackageSourceResultFactory results)
        : IPackageSourceClient
    {
        internal IReadOnlyList<SearchResult> SearchResults { get; set; } = [];
        internal IReadOnlyList<SearchPage>? SearchPages { get; set; }
        internal PackageSearchTruncationReason SearchTruncation { get; set; }
        internal Dictionary<string, IReadOnlyList<string>> Versions { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, PackageSourceFailureKind>
            VersionFailures { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        internal int SearchTake { get; private set; }
        internal int SearchPagesRead { get; private set; }
        internal List<string> VersionRequests { get; } = [];
        internal Func<string, Task>? BeforeVersion { get; set; }

        public PackageSourceResultIdentity Source => results.Source;

        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.Search
            | PackageSourceCapabilities.VersionEnumeration
            | PackageSourceCapabilities.Manifest;

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operationContext?.ThrowIfExpired();
            SearchTake = take;
            return Task.FromResult(
                results.SucceededSearch(
                    results.Search(
                        SearchResults,
                        SearchTruncation)));
        }

        public async IAsyncEnumerable<
            PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixPagesAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                [System.Runtime.CompilerServices.EnumeratorCancellation]
                    CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operationContext?.ThrowIfExpired();
            SearchTake = take;
            IReadOnlyList<SearchPage> pages =
                SearchPages
                ?? [new(SearchResults, SearchTruncation)];
            foreach (SearchPage page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                operationContext?.ThrowIfExpired();
                SearchPagesRead++;
                yield return page.Failure is { } failure
                    ? results.FailedSearch(failure)
                    : results.SucceededSearch(
                        results.Search(
                            page.Results,
                            page.Truncation));
            }
        }

        public async Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operationContext?.ThrowIfExpired();
            VersionRequests.Add(packageId);
            if (BeforeVersion is not null)
                await BeforeVersion(packageId);
            operationContext?.ThrowIfExpired();
            if (VersionFailures.TryGetValue(
                    packageId,
                    out PackageSourceFailureKind failure))
            {
                return results.FailedVersions(failure);
            }

            IReadOnlyList<string> versions =
                Versions.TryGetValue(
                    packageId,
                    out IReadOnlyList<string>? configured)
                    ? configured
                    : [];
            PackageVersionResult result = results.Versions(
                [
                    .. versions.Select(version =>
                        results.Candidate(
                            PackageSourceCoordinate.Create(
                                packageId,
                                version),
                            PackageDiscoveryContract
                                .CompleteVersionEnumeration,
                            PackageListingState.Listed)),
                ],
                hasAuthoritativeListingState: true);
            return results.SucceededVersions(result);
        }

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operationContext?.ThrowIfExpired();
            return Task.FromResult(
                results.FailedManifest(
                    PackageSourceCoordinate.Create(
                        packageId,
                        version),
                    PackageSourceFailureKind.NotFound));
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
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

    private sealed record SearchPage(
        IReadOnlyList<SearchResult> Results,
        PackageSearchTruncationReason Truncation,
        PackageSourceFailureKind? Failure = null)
    {
        internal static SearchPage Failed(PackageSourceFailureKind failure) =>
            new([], PackageSearchTruncationReason.None, failure);
    }
}
