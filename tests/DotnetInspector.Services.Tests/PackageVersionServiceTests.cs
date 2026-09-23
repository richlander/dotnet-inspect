using DotnetInspector.Cache;
using DotnetInspector.Packages;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Public-consumer contract suite for <see cref="PackageVersionService"/>:
/// the decision table, jitter determinism, the refresh cap, label honesty,
/// authorization and key isolation, eviction, offline policy, and the
/// receipt and PackageHouse rules the service relies on. Timing cases use a
/// fake source and clock because the CLI feed harness cannot inject delays.
/// </summary>
public sealed class PackageVersionServiceTests
{
    private const string SourceUrl = "https://priors.example/v3/index.json";

    public PackageVersionServiceTests()
    {
        PersistentCache.Initialize("dotnet-inspect-test");
    }

    [Fact]
    public async Task NoPrior_DiscoversAndWritesTheEntry()
    {
        var world = new World();
        var request = new PackageVersionSelectionRequest.LatestStable(world.PackageId);

        PackageVersionServiceSettlement settlement = await world.SettleAsync(
            request,
            world.Authorization,
            World.Contract,
            world.Operation(discoverVersions: ["1.0.0", "2.0.0"]));

        Assert.Equal(PackageVersionServicePath.Discovered, settlement.Path);
        Assert.True(settlement.EntryWritten);
        var resolved = Assert.IsType<PackageVersionResolutionReceipt.Resolved>(settlement.Receipt);
        Assert.Equal("2.0.0", resolved.Coordinate.Version);
        Assert.Equal(PackageVersionDiscoveryFreshness.RefreshedForRequest, resolved.Freshness);
        Assert.Equal(1, world.DiscoveryCalls);
    }

    [Fact]
    public async Task PriorWithinWindow_IsServedAsCurrentWithoutDiscovery()
    {
        var world = new World();
        var request = new PackageVersionSelectionRequest.LatestStable(world.PackageId);
        await world.SeedAsync(request, "2.0.0");

        PackageVersionServiceSettlement settlement = await world.SettleAsync(
            request,
            world.Authorization,
            World.Contract,
            world.Operation(discoverVersions: null));

        Assert.Equal(PackageVersionServicePath.PriorWithinWindow, settlement.Path);
        var prior = Assert.IsType<PackageVersionResolutionReceipt.Prior>(settlement.Receipt);
        Assert.Equal("2.0.0", prior.Coordinate.Version);
        Assert.Equal(PackageVersionDiscoveryFreshness.Current, prior.Freshness);
        Assert.Null(prior.Age);
        Assert.Same(request, prior.Request);
        Assert.Equal(PackageAcquisitionCandidateKind.CallerPinned, prior.Candidate.Kind);
        Assert.Equal(0, world.DiscoveryCalls);
    }

    [Fact]
    public async Task PastWindow_FastSource_RefreshesAndRewritesTheEntry()
    {
        var world = new World();
        var request = new PackageVersionSelectionRequest.LatestStable(world.PackageId);
        await world.SeedAsync(request, "2.0.0");
        world.Advance(TimeSpan.FromHours(2));

        PackageVersionServiceSettlement refreshed = await world.SettleAsync(
            request,
            world.Authorization,
            World.Contract,
            world.Operation(discoverVersions: ["2.0.0", "3.0.0"]));

        Assert.Equal(PackageVersionServicePath.Discovered, refreshed.Path);
        Assert.True(refreshed.EntryWritten);
        Assert.Equal(
            "3.0.0",
            Assert.IsType<PackageVersionResolutionReceipt.Resolved>(refreshed.Receipt)
                .Coordinate.Version);

        PackageVersionServiceSettlement served = await world.SettleAsync(
            request,
            world.Authorization,
            World.Contract,
            world.Operation(discoverVersions: null));
        Assert.Equal(PackageVersionServicePath.PriorWithinWindow, served.Path);
        Assert.Equal(
            "3.0.0",
            Assert.IsType<PackageVersionResolutionReceipt.Prior>(served.Receipt)
                .Coordinate.Version);
    }

    [Fact]
    public async Task PastWindow_RefusingSource_ServesThePriorWithItsAge()
    {
        var world = new World();
        var request = new PackageVersionSelectionRequest.LatestStable(world.PackageId);
        await world.SeedAsync(request, "2.0.0");
        world.Advance(TimeSpan.FromHours(3));

        PackageVersionServiceSettlement settlement = await world.SettleAsync(
            request,
            world.Authorization,
            World.Contract,
            world.Operation(discoverFails: true));

        Assert.Equal(PackageVersionServicePath.PriorAfterRefreshFailure, settlement.Path);
        var prior = Assert.IsType<PackageVersionResolutionReceipt.Prior>(settlement.Receipt);
        Assert.Equal(PackageVersionDiscoveryFreshness.ServedPrior, prior.Freshness);
        Assert.Equal(TimeSpan.FromHours(3), prior.Age);
        Assert.Equal("2.0.0", prior.Coordinate.Version);
        Assert.Equal(1, world.DiscoveryCalls);
    }

    [Fact]
    public async Task PastWindow_SourceSlowerThanTheRefreshBound_ServesThePrior()
    {
        var world = new World(new PackageVersionServiceOptions
        {
            RefreshBound = TimeSpan.FromMilliseconds(50),
        });
        var request = new PackageVersionSelectionRequest.LatestStable(world.PackageId);
        await world.SeedAsync(request, "2.0.0");
        world.Advance(TimeSpan.FromHours(2));

        PackageVersionServiceSettlement settlement = await world.SettleAsync(
            request,
            world.Authorization,
            World.Contract,
            world.Operation(discoverDelay: TimeSpan.FromSeconds(30), discoverVersions: ["9.0.0"]));

        Assert.Equal(PackageVersionServicePath.PriorAfterRefreshFailure, settlement.Path);
        var prior = Assert.IsType<PackageVersionResolutionReceipt.Prior>(settlement.Receipt);
        Assert.Equal(PackageVersionDiscoveryFreshness.ServedPrior, prior.Freshness);
        Assert.Equal("2.0.0", prior.Coordinate.Version);
    }

    [Fact]
    public async Task AlwaysLatest_DiscoversPastAFreshPrior_AndRewritesTheLatestEntry()
    {
        var world = new World();
        var latest = new PackageVersionSelectionRequest.LatestStable(world.PackageId);
        await world.SeedAsync(latest, "2.0.0");

        PackageVersionServiceSettlement always = await world.SettleAsync(
            new PackageVersionSelectionRequest.AlwaysLatest(world.PackageId),
            world.Authorization,
            World.Contract,
            world.Operation(discoverVersions: ["2.0.0", "2.1.0"]));

        Assert.Equal(PackageVersionServicePath.Discovered, always.Path);
        Assert.Equal(1, world.DiscoveryCalls);

        PackageVersionServiceSettlement served = await world.SettleAsync(
            latest,
            world.Authorization,
            World.Contract,
            world.Operation(discoverVersions: null));
        Assert.Equal(PackageVersionServicePath.PriorWithinWindow, served.Path);
        Assert.Equal(
            "2.1.0",
            Assert.IsType<PackageVersionResolutionReceipt.Prior>(served.Receipt)
                .Coordinate.Version);
    }

    [Fact]
    public async Task AlwaysLatest_WithARefusingSource_FailsVisibly()
    {
        var world = new World();
        await world.SeedAsync(
            new PackageVersionSelectionRequest.LatestStable(world.PackageId),
            "2.0.0");

        PackageVersionServiceSettlement settlement = await world.SettleAsync(
            new PackageVersionSelectionRequest.AlwaysLatest(world.PackageId),
            world.Authorization,
            World.Contract,
            world.Operation(discoverFails: true));

        Assert.Equal(PackageVersionServicePath.Discovered, settlement.Path);
        Assert.IsNotType<PackageVersionResolutionReceipt.Prior>(settlement.Receipt);
        Assert.IsNotType<PackageVersionResolutionReceipt.Resolved>(settlement.Receipt);
    }

    [Fact]
    public void Window_IsDeterministicPerKeyAndSpreadAcrossKeys()
    {
        var first = new World();
        var second = new World(sharePackageId: first.PackageId);
        var request = new PackageVersionSelectionRequest.LatestStable(first.PackageId);

        TimeSpan window = first.Service.WindowFor(request, first.Authorization, World.Contract);
        Assert.Equal(
            window,
            second.Service.WindowFor(request, second.Authorization, World.Contract));

        TimeSpan baseWindow = TimeSpan.FromHours(1);
        var windows = Enumerable.Range(0, 16)
            .Select(_ => new World())
            .Select(world => world.Service.WindowFor(
                new PackageVersionSelectionRequest.LatestStable(world.PackageId),
                world.Authorization,
                World.Contract))
            .ToList();
        Assert.All(windows, value =>
        {
            Assert.InRange(value, baseWindow * 0.8, baseWindow * 1.2);
        });
        Assert.True(windows.Distinct().Count() > 1);
    }

    [Fact]
    public async Task RefreshCap_BoundsSynchronousRefreshesPerInvocation()
    {
        var budget = new PackageVersionRefreshBudget(cap: 2);
        var worlds = Enumerable.Range(0, 3).Select(_ => new World()).ToList();
        foreach (World world in worlds)
        {
            await world.SeedAsync(
                new PackageVersionSelectionRequest.LatestStable(world.PackageId),
                "1.0.0");
            world.Advance(TimeSpan.FromHours(2));
        }

        var paths = new List<PackageVersionServicePath>();
        foreach (World world in worlds)
        {
            PackageVersionServiceSettlement settlement = await world.SettleAsync(
                new PackageVersionSelectionRequest.LatestStable(world.PackageId),
                world.Authorization,
                World.Contract,
                world.Operation(discoverVersions: ["1.0.0", "1.1.0"], budget: budget));
            paths.Add(settlement.Path);
        }

        Assert.Equal(2, worlds.Sum(world => world.DiscoveryCalls));
        Assert.Equal(
            [
                PackageVersionServicePath.Discovered,
                PackageVersionServicePath.Discovered,
                PackageVersionServicePath.PriorBudgetExhausted,
            ],
            paths);
        Assert.Equal(2, budget.Used);
    }

    [Fact]
    public async Task PastWindowEntry_IsNeverLabeledCurrent()
    {
        var world = new World();
        var request = new PackageVersionSelectionRequest.LatestStable(world.PackageId);
        await world.SeedAsync(request, "2.0.0");
        world.Advance(TimeSpan.FromHours(2));

        PackageVersionServiceSettlement exhausted = await world.SettleAsync(
            request,
            world.Authorization,
            World.Contract,
            world.Operation(discoverVersions: ["2.0.0"], budget: new PackageVersionRefreshBudget(cap: 0)));

        Assert.Equal(PackageVersionServicePath.PriorBudgetExhausted, exhausted.Path);
        Assert.Equal(
            PackageVersionDiscoveryFreshness.ServedPrior,
            exhausted.Receipt.Freshness);
        Assert.Equal(0, world.DiscoveryCalls);
    }

    [Fact]
    public async Task AuthorizationChange_IsAMiss()
    {
        var world = new World();
        var request = new PackageVersionSelectionRequest.LatestStable(world.PackageId);
        await world.SeedAsync(request, "2.0.0");

        var other = new World(sharePackageId: world.PackageId, sourceUrl: "https://other.example/v3/index.json");
        PackageVersionServiceSettlement settlement = await other.SettleAsync(
            request,
            other.Authorization,
            World.Contract,
            other.Operation(discoverVersions: ["2.5.0"]));

        Assert.Equal(PackageVersionServicePath.Discovered, settlement.Path);
        Assert.Equal(1, other.DiscoveryCalls);
    }

    [Fact]
    public async Task LatestPrior_IsNeverServedToWildcardOrRangeRequests()
    {
        var world = new World();
        await world.SeedAsync(
            new PackageVersionSelectionRequest.LatestPrerelease(world.PackageId),
            "3.0.0-preview.1");

        PackageVersionServiceSettlement wildcard = await world.SettleAsync(
            new PackageVersionSelectionRequest.Wildcard(world.PackageId, "3.0."),
            world.Authorization,
            PackageVersionDiscoveryContract.Create(includePrerelease: true, includeUnlisted: false, limit: null),
            world.Operation(discoverVersions: ["3.0.0-preview.1", "3.0.1"]));
        Assert.Equal(PackageVersionServicePath.Discovered, wildcard.Path);

        Assert.True(PackageVersionRange.TryParse(
            $"{world.PackageId}@1.0.0..4.0.0", out PackageVersionRange? range, out _));
        PackageVersionServiceSettlement ranged = await world.SettleAsync(
            new PackageVersionSelectionRequest.Range(range!, new PackageVersionRangeSelection.Last()),
            world.Authorization,
            World.Contract,
            world.Operation(discoverVersions: ["1.0.0", "3.0.1"]));
        Assert.Equal(PackageVersionServicePath.Discovered, ranged.Path);
        Assert.Equal(2, world.DiscoveryCalls);
    }

    [Fact]
    public async Task Evict_RemovesTheEntrySoTheNextRequestDiscovers()
    {
        var world = new World();
        var request = new PackageVersionSelectionRequest.LatestStable(world.PackageId);
        await world.SeedAsync(request, "2.0.0");

        Assert.True(world.Service.Evict(request, world.Authorization, World.Contract));
        Assert.False(world.Service.Evict(request, world.Authorization, World.Contract));

        PackageVersionServiceSettlement settlement = await world.SettleAsync(
            request,
            world.Authorization,
            World.Contract,
            world.Operation(discoverVersions: ["2.0.1"]));
        Assert.Equal(PackageVersionServicePath.Discovered, settlement.Path);
        Assert.Equal(1, world.DiscoveryCalls);
    }

    [Fact]
    public async Task NonResolvedPin_DiscoversUnboundedAndRetainsThePinFailures()
    {
        var world = new World();
        var request = new PackageVersionSelectionRequest.LatestStable(world.PackageId);
        await world.SeedAsync(request, "2.0.0");

        PackageVersionServiceSettlement settlement = await world.SettleAsync(
            request,
            world.Authorization,
            World.Contract,
            world.Operation(
                discoverVersions: ["2.0.0", "2.1.0"],
                pinState: PackageAcquisitionCandidateResultState.Incomplete));

        // The prior was not served; the current generation refused to pin it.
        Assert.Equal(PackageVersionServicePath.Discovered, settlement.Path);
        Assert.Equal(
            "2.1.0",
            Assert.IsType<PackageVersionResolutionReceipt.Resolved>(settlement.Receipt)
                .Coordinate.Version);
        PackageAuthorityFailure pinFailure = Assert.Single(settlement.PinFailures);
        Assert.Equal(PackageAuthorityFailureKind.Configuration, pinFailure.Kind);
        // A pin refusal is not a refresh: the discovery ran under the ordinary
        // deadline, not the per-refresh bound (the seed's discovery did too).
        Assert.All(world.DiscoveryBounds, bound => Assert.Null(bound));
    }

    [Fact]
    public async Task Refresh_CarriesThePerRefreshBound_AndTerminalDiscoveryDoesNot()
    {
        var options = new PackageVersionServiceOptions { RefreshBound = TimeSpan.FromSeconds(3) };
        var world = new World(options);
        var request = new PackageVersionSelectionRequest.LatestStable(world.PackageId);

        await world.SettleAsync(
            request,
            world.Authorization,
            World.Contract,
            world.Operation(discoverVersions: ["1.0.0"]));
        world.Advance(TimeSpan.FromHours(2));
        await world.SettleAsync(
            request,
            world.Authorization,
            World.Contract,
            world.Operation(discoverVersions: ["1.0.0", "1.1.0"]));

        Assert.Equal([null, TimeSpan.FromSeconds(3)], world.DiscoveryBounds);
    }

    [Fact]
    public async Task Entry_RecordsTheSourceIdentityTheKeyUses()
    {
        var world = new World();
        var request = new PackageVersionSelectionRequest.LatestStable(world.PackageId);

        await world.SettleAsync(
            request,
            world.Authorization,
            World.Contract,
            world.Operation(discoverVersions: ["1.0.0"]));

        string key = PackageVersionService.StoreKey(request, world.Authorization, World.Contract)!;
        string entry = PersistentCache.TryGet(
            PackageVersionService.StoreCategory, key, extension: "txt")!;
        string[] lines = entry.Split('\n');
        Assert.Equal("1.0.0", lines[0]);
        // An HTTP authority has no persistent cache key; the entry still names it.
        Assert.Equal("http:" + new Uri(SourceUrl).AbsoluteUri.ToLowerInvariant(), lines[1]);
    }

    [Fact]
    public void StoreKey_IgnoresPackageIdCase()
    {
        var world = new World();
        string lower = PackageVersionService.StoreKey(
            new PackageVersionSelectionRequest.LatestStable(world.PackageId.ToLowerInvariant()),
            world.Authorization,
            World.Contract)!;
        string upper = PackageVersionService.StoreKey(
            new PackageVersionSelectionRequest.LatestStable(world.PackageId.ToUpperInvariant()),
            world.Authorization,
            World.Contract)!;
        Assert.Equal(lower, upper);
    }

    [Fact]
    public async Task Offline_ServesAnyPriorRegardlessOfAge_AndDiscoversWithoutOne()
    {
        var world = new World();
        var request = new PackageVersionSelectionRequest.LatestStable(world.PackageId);
        await world.SeedAsync(request, "2.0.0");
        world.Advance(TimeSpan.FromDays(30));

        PackageVersionServiceSettlement served = await world.SettleAsync(
            request,
            world.Authorization,
            World.Contract,
            world.Operation(discoverVersions: null, offline: true));
        Assert.Equal(PackageVersionServicePath.PriorOffline, served.Path);
        Assert.Equal(PackageVersionDiscoveryFreshness.ServedPrior, served.Receipt.Freshness);
        Assert.Equal(0, world.DiscoveryCalls);

        var fresh = new World();
        PackageVersionServiceSettlement missing = await fresh.SettleAsync(
            new PackageVersionSelectionRequest.LatestStable(fresh.PackageId),
            fresh.Authorization,
            World.Contract,
            fresh.Operation(discoverFails: true, offline: true));
        Assert.Equal(PackageVersionServicePath.Discovered, missing.Path);
        Assert.IsNotType<PackageVersionResolutionReceipt.Prior>(missing.Receipt);
        Assert.Equal(1, fresh.DiscoveryCalls);
    }

    [Fact]
    public async Task AuthoritativeAbsenceOnRefresh_EvictsRatherThanServingThePrior()
    {
        var world = new World();
        var request = new PackageVersionSelectionRequest.LatestStable(world.PackageId);
        await world.SeedAsync(request, "2.0.0");
        world.Advance(TimeSpan.FromHours(2));

        PackageVersionServiceSettlement settlement = await world.SettleAsync(
            request,
            world.Authorization,
            World.Contract,
            world.Operation(discoverVersions: []));

        Assert.Equal(PackageVersionServicePath.Discovered, settlement.Path);
        Assert.True(settlement.EntryEvicted);
        Assert.IsType<PackageVersionResolutionReceipt.NotFound>(settlement.Receipt);

        PackageVersionServiceSettlement next = await world.SettleAsync(
            request,
            world.Authorization,
            World.Contract,
            world.Operation(discoverVersions: ["2.0.0"]));
        Assert.Equal(PackageVersionServicePath.Discovered, next.Path);
    }

    [Fact]
    public void PriorReceipt_EnforcesItsFreshnessAndAgeShape()
    {
        var world = new World();
        var request = new PackageVersionSelectionRequest.LatestStable(world.PackageId);
        PackageAcquisitionCandidate candidate = world.PinnedCandidate("1.0.0");

        var current = new PackageVersionResolutionReceipt.Prior(
            request, candidate, PackageVersionDiscoveryFreshness.Current, age: null);
        Assert.Null(current.Age);
        var served = new PackageVersionResolutionReceipt.Prior(
            request, candidate, PackageVersionDiscoveryFreshness.ServedPrior, TimeSpan.FromMinutes(90));
        Assert.Equal(TimeSpan.FromMinutes(90), served.Age);

        Assert.Throws<ArgumentException>(() => new PackageVersionResolutionReceipt.Prior(
            request, candidate, PackageVersionDiscoveryFreshness.RefreshedForRequest, age: null));
        Assert.Throws<ArgumentException>(() => new PackageVersionResolutionReceipt.Prior(
            request, candidate, PackageVersionDiscoveryFreshness.Current, TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentException>(() => new PackageVersionResolutionReceipt.Prior(
            request, candidate, PackageVersionDiscoveryFreshness.ServedPrior, age: null));
        Assert.Throws<ArgumentException>(() => new PackageVersionResolutionReceipt.Prior(
            new PackageVersionSelectionRequest.LatestStable("other.package"),
            candidate,
            PackageVersionDiscoveryFreshness.Current,
            age: null));
    }

    [Fact]
    public void DiscoveryBackedArms_RejectServedPrior()
    {
        var world = new World();
        var request = new PackageVersionSelectionRequest.LatestStable(world.PackageId);
        PackageVersionDiscoveryResult discovery = world.Discovery(["1.0.0"]);

        PackageVersionResolutionReceipt receipt = PackageVersionSelectionResolver.Resolve(
            request,
            discovery,
            PackageVersionDiscoveryFreshness.ServedPrior);

        var rejected = Assert.IsType<PackageVersionResolutionReceipt.Rejected>(receipt);
        Assert.Contains("ServedPrior", rejected.Reason.ToString(), StringComparison.Ordinal);
        Assert.IsAssignableFrom<PackageVersionResolutionReceipt.Discovered>(rejected);
    }

    [Fact]
    public void PackageHouse_AcceptsAPriorDecisionAndItsPostAcquisitionOutcomes()
    {
        var world = new World();
        var request = new PackageVersionSelectionRequest.LatestStable(world.PackageId);
        var prior = new PackageVersionResolutionReceipt.Prior(
            request,
            world.PinnedCandidate("1.0.0"),
            PackageVersionDiscoveryFreshness.ServedPrior,
            TimeSpan.FromHours(2));
        var houseRequest = new PackageHouseRequest(
            new PackageHouseDemand.Selecting(request),
            PackageHouseOperation.Create(PackageHouseOperationProfile.Settle));

        PackageHouseDecisionReceipt decision =
            PackageHouseDecisionReceipt.RetainPriorPackage(houseRequest, prior);
        Assert.Equal(PackageHouseDecision.RetainPackage, decision.Decision);
        Assert.Equal(prior.Coordinate, decision.Coordinate);
        Assert.Same(prior.Candidate, decision.Candidate);
        Assert.Same(prior, decision.VersionResolution);

        // Terminal-outcome preservation treats Prior as a settled arm: a typed
        // failure after a prior decision is constructible, exactly as after Resolved.
        var evidence = new PackageHouseEvidence(houseRequest, decision);
        var notFound = new PackageHouseResult.NotFound(
            evidence,
            new InertString(TextPolicy.Field, "The prior coordinate is gone from every source."));
        Assert.Same(prior, notFound.Decision!.VersionResolution);
        var failed = new PackageHouseResult.Failed(
            evidence,
            new InertString(TextPolicy.Field, "Acquisition failed after a prior decision."));
        Assert.Same(prior, failed.Decision!.VersionResolution);

        // The decision receipt still binds the prior's exact coordinate and candidate.
        Assert.Throws<ArgumentException>(() => PackageHouseDecisionReceipt.Stop(
            houseRequest,
            prior.Coordinate,
            versionResolution: prior));
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } =
            new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    /// <summary>One package, one authorization, one fake source, one clock.</summary>
    private sealed class World
    {
        private static readonly object Issuer = new();

        private readonly TestTimeProvider _clock = new();

        private readonly ConfiguredPackageAuthority _authority;

        private readonly PackageSourceResultFactory _factory;

        public World(
            PackageVersionServiceOptions? options = null,
            string? sharePackageId = null,
            string sourceUrl = SourceUrl)
        {
            PackageId = sharePackageId ?? $"prior.{Guid.NewGuid():N}";
            _authority = new ConfiguredPackageAuthority(
                new PackageSource("priors", sourceUrl));
            Authorization = PackageSourceAuthorization.ObserveAuthorities([_authority], []);
            PackageSourceResultFactory? captured = null;
            using IPackageSourceClient client = PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                _authority.Association,
                factory =>
                {
                    captured = factory;
                    return new FactoryOnlyPackageSourceClient(factory.Source);
                });
            _factory = captured!;
            Service = new PackageVersionService(_clock, options);
        }

        public static PackageVersionDiscoveryContract Contract { get; } =
            PackageVersionDiscoveryContract.Create(
                includePrerelease: false,
                includeUnlisted: false,
                limit: null);

        public string PackageId { get; }

        public PackageSourceAuthorization Authorization { get; }

        public PackageVersionService Service { get; }

        public int DiscoveryCalls { get; private set; }

        public List<TimeSpan?> DiscoveryBounds { get; } = [];

        public void Advance(TimeSpan by) => _clock.UtcNow += by;

        public Task<PackageVersionServiceSettlement> SettleAsync(
            PackageVersionSelectionRequest request,
            PackageSourceAuthorization authorization,
            PackageVersionDiscoveryContract contract,
            PackageVersionServiceOperation operation) =>
            Service.SettleAsync(
                request,
                authorization,
                contract,
                operation,
                TestContext.Current.CancellationToken);

        public async Task SeedAsync(PackageVersionSelectionRequest request, string version)
        {
            PackageVersionServiceSettlement seeded = await SettleAsync(
                request,
                Authorization,
                Contract,
                Operation(discoverVersions: [version]));
            Assert.True(seeded.EntryWritten);
            DiscoveryCalls = 0;
        }

        public PackageVersionServiceOperation Operation(
            string[]? discoverVersions = null,
            bool discoverFails = false,
            TimeSpan? discoverDelay = null,
            PackageVersionRefreshBudget? budget = null,
            bool offline = false,
            PackageAcquisitionCandidateResultState pinState =
                PackageAcquisitionCandidateResultState.Resolved) =>
            new()
            {
                Discover = async scope =>
                {
                    DiscoveryCalls++;
                    DiscoveryBounds.Add(scope.Bound);
                    if (discoverDelay is { } delay)
                        await Task.Delay(delay, scope.CancellationToken);
                    if (discoverFails)
                        return FailedDiscovery();
                    if (discoverVersions is null)
                        throw new InvalidOperationException("Discovery was not expected on this path.");
                    return Discovery(discoverVersions);
                },
                Pin = coordinate => pinState == PackageAcquisitionCandidateResultState.Resolved
                    ? new PackageAcquisitionCandidateResult(
                        PackageAcquisitionCandidateResultState.Resolved,
                        PackageAcquisitionCandidate.CreatePinned(Issuer, coordinate, [_authority]),
                        [])
                    : new PackageAcquisitionCandidateResult(
                        pinState,
                        candidate: null,
                        [
                            new PackageAuthorityFailure(
                                new InertString(TextPolicy.Field, "priors"),
                                PackageAuthorityFailureKind.Configuration,
                                "The source configuration could not be read."),
                        ]),
                Budget = budget ?? new PackageVersionRefreshBudget(),
                Offline = offline,
            };

        public PackageAcquisitionCandidate PinnedCandidate(string version) =>
            PackageAcquisitionCandidate.CreatePinned(
                Issuer,
                PackageSourceCoordinate.Create(PackageId, version),
                [_authority]);

        public PackageVersionDiscoveryResult Discovery(string[] versions)
        {
            bool prerelease = versions.Any(version =>
                NuGet.Versioning.NuGetVersion.Parse(version).IsPrerelease);
            var candidates = versions.Select(version =>
                new ConfiguredPackageCandidateObservation(
                    _authority,
                    _factory.Candidate(
                        PackageSourceCoordinate.Create(PackageId, version),
                        PackageDiscoveryContract.CompleteVersionEnumeration,
                        PackageListingState.Listed)))
                .ToArray();
            return new PackageVersionDiscoveryResult(
                PackageId,
                PackageVersionDiscoveryState.Authoritative,
                [.. versions.Select(version => new PackageVersionSourceInfo(version, "priors", Listed: true))],
                [],
                hasAnyCandidate: versions.Length > 0,
                candidates,
                PackageVersionDiscoveryContract.Create(
                    includePrerelease: prerelease,
                    includeUnlisted: false,
                    limit: null),
                candidateIssuer: Issuer);
        }

        private PackageVersionDiscoveryResult FailedDiscovery() =>
            new(
                PackageId,
                PackageVersionDiscoveryState.Failed,
                sourceListings: [],
                failures:
                [
                    new PackageAuthorityFailure(
                        new InertString(TextPolicy.Field, "priors"),
                        PackageAuthorityFailureKind.Timeout,
                        "The source refused the request."),
                ],
                hasAnyCandidate: false,
                candidates: null,
                contract: Contract,
                candidateIssuer: Issuer);
    }

    private sealed class FactoryOnlyPackageSourceClient(
        PackageSourceResultIdentity source)
        : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source { get; } = source;

        public PackageSourceCapabilities Capabilities => PackageSourceCapabilities.None;

        public Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
            string query,
            int take = 20,
            bool prerelease = false,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>> SearchByPrefixAsync(
            string prefix,
            int take = 100,
            bool prerelease = false,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>> GetVersionsAsync(
            string packageId,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourceManifest>> GetManifestAsync(
            string packageId,
            string version,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>> GetPackageAsync(
            string packageId,
            string version,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>> TryGetSymbolsAsync(
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
