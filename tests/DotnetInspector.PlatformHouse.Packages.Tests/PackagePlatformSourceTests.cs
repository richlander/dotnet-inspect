using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.PlatformHouse.Packages.Tests;

public sealed class PackagePlatformSourceTests
{
    [Theory]
    [InlineData(PlatformFamily.DotNetRuntime, PackagePlatformTestEnvironment.RuntimePackageId)]
    [InlineData(PlatformFamily.AspNetCore, PackagePlatformTestEnvironment.AspNetPackageId)]
    public async Task DiscoveryMapsFamilyFiltersTargetBandAndSortsPrereleases(
        PlatformFamily family,
        string packageId)
    {
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    packageId,
                    versions:
                    [
                        "12.0.0",
                        "11.1.0",
                        "11.0.0",
                        "11.0.0-rc.2",
                        "10.0.0",
                        "11.0.0-rc.1",
                    ]),
            ]);
        PackagePlatformSource source = environment.CreateSource();

        PackagePlatformSourceOutcome<PackagePlatformTargetInventory> outcome =
            await source.DiscoverAsync(
                new(
                    family,
                    PlatformTargetFramework.Parse("net11.0"),
                    maxCandidates: 8),
                environment.IssueOperation(TestContext.Current.CancellationToken));

        var succeeded = Assert.IsType<
            PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.Succeeded>(outcome);
        Assert.Equal(
            ["11.0.0-rc.1", "11.0.0-rc.2", "11.0.0"],
            succeeded.Value.Targets.Select(static candidate => candidate.Target.Version.Value));
        Assert.All(
            succeeded.Value.Targets,
            candidate =>
            {
                Assert.Equal(family, candidate.Target.Family);
                Assert.Equal("net11.0", candidate.Target.TargetFramework.ToString());
                Assert.Equal(packageId, candidate.Coordinate.PackageId);
                Assert.Equal(PackageAcquisitionCandidateKind.Discovered, candidate.Candidate.Kind);
                Assert.Single(candidate.Candidate.Authorities);
            });
        Assert.Equal([packageId], environment.Authorization.Requests);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task DiscoveryPreservesAuthoritativeEmptyAndRejectsPartialOrFailedInventories()
    {
        await using PackagePlatformTestEnvironment empty =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    versions: []),
            ]);
        PackagePlatformSourceOutcome<PackagePlatformTargetInventory> emptyOutcome =
            await empty.CreateSource().DiscoverAsync(
                DiscoveryRequest(),
                empty.IssueOperation(TestContext.Current.CancellationToken));
        Assert.Empty(Assert.IsType<
            PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.Succeeded>(
                emptyOutcome).Value.Targets);
        await empty.AssertSettledAsync();

        await using PackagePlatformTestEnvironment partial =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    versions: [PackagePlatformTestEnvironment.Version]),
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    versions: [],
                    versionFailure: PackageSourceFailureKind.Transport),
            ]);
        var partialOutcome = Assert.IsType<
            PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.Incomplete>(
                await partial.CreateSource().DiscoverAsync(
                    DiscoveryRequest(),
                    partial.IssueOperation(TestContext.Current.CancellationToken)));
        Assert.Equal(PackagePlatformSourceDiagnosticKind.PackageFailure, partialOutcome.Diagnostic.Kind);
        Assert.Single(partialOutcome.Diagnostic.PackageFailures);
        await partial.AssertSettledAsync();

        await using PackagePlatformTestEnvironment failed =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    versions: [],
                    versionFailure: PackageSourceFailureKind.Transport),
            ]);
        var failedOutcome = Assert.IsType<
            PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.Failed>(
                await failed.CreateSource().DiscoverAsync(
                    DiscoveryRequest(),
                    failed.IssueOperation(TestContext.Current.CancellationToken)));
        Assert.Equal(PackagePlatformSourceDiagnosticKind.PackageFailure, failedOutcome.Diagnostic.Kind);
        Assert.Equal(
            PackageAuthorityFailureKind.Transport,
            Assert.Single(failedOutcome.Diagnostic.PackageFailures).Kind);
        await failed.AssertSettledAsync();
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(8, 0)]
    public async Task DiscoveryCandidateMaximumIsIntersectionOfRequestAndSource(
        int requestMaximum,
        int sourceMaximum)
    {
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    versions: ["11.0.0", "11.0.1"]),
            ]);
        PackagePlatformSource source = environment.CreateSource(
            new PackagePlatformSourceLimits(maxCandidates: sourceMaximum));

        var outcome = Assert.IsType<
            PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.Incomplete>(
                await source.DiscoverAsync(
                    new(
                        PlatformFamily.DotNetRuntime,
                        PlatformTargetFramework.Parse("net11.0"),
                        requestMaximum),
                    environment.IssueOperation(TestContext.Current.CancellationToken)));

        Assert.Equal(PackagePlatformSourceDiagnosticKind.WorkLimitExceeded, outcome.Diagnostic.Kind);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task UnsupportedTargetSkipsAuthorizationAndDeniedPackageIdIsUnavailable()
    {
        await using PackagePlatformTestEnvironment unsupported =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    versions: [PackagePlatformTestEnvironment.Version]),
            ]);
        var oldOutcome = Assert.IsType<
            PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.Succeeded>(
                await unsupported.CreateSource().DiscoverAsync(
                    new(
                        PlatformFamily.DotNetRuntime,
                        PlatformTargetFramework.Parse("netcoreapp2.1"),
                        maxCandidates: 8),
                    unsupported.IssueOperation(TestContext.Current.CancellationToken)));
        Assert.Empty(oldOutcome.Value.Targets);
        Assert.Empty(unsupported.Authorization.Requests);
        Assert.Equal(0, unsupported.Clients[0].VersionRequests);
        await unsupported.AssertSettledAsync();

        await using PackagePlatformTestEnvironment denied =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.AspNetPackageId,
                    versions: [PackagePlatformTestEnvironment.Version]),
            ],
            deniedPackageIds: [PackagePlatformTestEnvironment.AspNetPackageId]);
        var deniedOutcome = Assert.IsType<
            PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.Unavailable>(
                await denied.CreateSource().DiscoverAsync(
                    new(
                        PlatformFamily.AspNetCore,
                        PlatformTargetFramework.Parse("net11.0"),
                        maxCandidates: 8),
                    denied.IssueOperation(TestContext.Current.CancellationToken)));
        Assert.Equal(PackagePlatformSourceDiagnosticKind.AuthorizationDenied, deniedOutcome.Diagnostic.Kind);
        var deniedRealization = Assert.IsType<
            PackagePlatformSourceOutcome<PackageReferenceRealization>.Unavailable>(
                await denied.CreateSource().RealizeAsync(
                    new PackageReferencePackCoordinate(
                        new PlatformFamilyTarget(
                            PlatformFamily.AspNetCore,
                            PlatformTargetFramework.Parse("net11.0"),
                            PlatformVersion.Parse(PackagePlatformTestEnvironment.Version))),
                    new PackageReferencePopulationDemand.CompletePopulation(),
                    Work(),
                    denied.IssueOperation(TestContext.Current.CancellationToken)));
        Assert.Equal(
            PackagePlatformSourceDiagnosticKind.AuthorizationDenied,
            deniedRealization.Diagnostic.Kind);
        Assert.Equal(
            [
                PackagePlatformTestEnvironment.AspNetPackageId,
                PackagePlatformTestEnvironment.AspNetPackageId,
            ],
            denied.Authorization.Requests);
        Assert.Equal(0, denied.Clients[0].VersionRequests);
        await denied.AssertSettledAsync();
    }

    [Fact]
    public async Task DiscoveredSelectionPreservesCandidateAndReportingAuthoritiesIncludingCache()
    {
        byte[] assembly = PackagePlatformTestData.Assembly("System.Runtime");
        var store = new InMemoryPackageStore();
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    versions: [PackagePlatformTestEnvironment.Version],
                    entries:
                    [
                        PackagePlatformTestData.Entry(
                            "ref/net11.0/System.Runtime.dll",
                            assembly),
                    ]),
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    versions: ["11.0.0-preview.1"]),
            ],
            store);
        byte[] nonReportingArchive = PackagePlatformTestData.Archive(
        [
            PackagePlatformTestData.Entry(
                "ref/net11.0/Poison.dll",
                PackagePlatformTestData.Assembly("Poison")),
        ]);
        await store.CommitAsync(
            PackagePlatformTestEnvironment.RuntimePackageId,
            PackagePlatformTestEnvironment.Version,
            environment.Clients[1].Source.Producer.Key,
            new MemoryStream(nonReportingArchive, writable: false),
            TestContext.Current.CancellationToken);
        PackagePlatformSource source = environment.CreateSource();
        var inventory = Assert.IsType<
            PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.Succeeded>(
                await source.DiscoverAsync(
                    DiscoveryRequest(),
                    environment.IssueOperation(TestContext.Current.CancellationToken))).Value;
        PackagePlatformTargetSelection selection = inventory.SelectTarget(Coordinate().Target);

        var outcome = Assert.IsType<
            PackagePlatformSourceOutcome<PackageReferenceRealization>.Succeeded>(
                await source.RealizeAsync(
                    selection,
                    new PackageReferencePopulationDemand.CompletePopulation(),
                    Work(),
                    environment.IssueOperation(TestContext.Current.CancellationToken)));

        Assert.Same(selection.Candidate, outcome.Value.Candidate);
        Assert.Same(environment.Clients[0].Source, outcome.Value.Source);
        Assert.Equal("System.Runtime", Assert.Single(outcome.Value.Libraries).Identity.Name);
        Assert.Equal(1, environment.Clients[0].PayloadRequests);
        Assert.Equal(0, environment.Clients[1].PayloadRequests);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task ExternalExactTargetUsesPinnedAuthorizationAcrossAuthorizedAuthorities()
    {
        byte[] assembly = PackagePlatformTestData.Assembly("System.Runtime");
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    payloadFailure: PackageSourceFailureKind.Transport),
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    entries:
                    [
                        PackagePlatformTestData.Entry(
                            "ref/net11.0/System.Runtime.dll",
                            assembly),
                    ]),
            ]);

        var outcome = Assert.IsType<
            PackagePlatformSourceOutcome<PackageReferenceRealization>.Succeeded>(
                await environment.CreateSource().RealizeAsync(
                    Coordinate(),
                    new PackageReferencePopulationDemand.CompletePopulation(),
                    Work(),
                    environment.IssueOperation(TestContext.Current.CancellationToken)));

        Assert.Equal(PackageAcquisitionCandidateKind.CallerPinned, outcome.Value.Candidate.Kind);
        Assert.Equal(2, outcome.Value.Candidate.Authorities.Count);
        Assert.Same(environment.Clients[1].Source, outcome.Value.Source);
        Assert.Equal(1, environment.Clients[0].PayloadRequests);
        Assert.Equal(1, environment.Clients[1].PayloadRequests);
        Assert.Equal(
            PackageAuthorityFailureKind.Transport,
            Assert.Single(outcome.Value.PackageFailures).Kind);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task PostAcquisitionRejectionRetainsEarlierAuthorityFailure()
    {
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    payloadFailure: PackageSourceFailureKind.Transport),
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    entries:
                    [
                        PackagePlatformTestData.Entry(
                            "ref/net11.0/Invalid.dll",
                            [0x4d, 0x5a, 0, 1]),
                    ]),
            ]);

        var rejected = Assert.IsType<
            PackagePlatformSourceOutcome<PackageReferenceRealization>.Rejected>(
                await environment.CreateSource().RealizeAsync(
                    Coordinate(),
                    new PackageReferencePopulationDemand.CompletePopulation(),
                    Work(),
                    environment.IssueOperation(TestContext.Current.CancellationToken)));

        Assert.Equal(PackagePlatformSourceDiagnosticKind.MalformedAssembly, rejected.Diagnostic.Kind);
        Assert.Equal(
            PackageAuthorityFailureKind.Transport,
            Assert.Single(rejected.Diagnostic.PackageFailures).Kind);
        Assert.Equal(1, environment.Clients[0].PayloadRequests);
        Assert.Equal(1, environment.Clients[1].PayloadRequests);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task SelectionFromAnotherSourceIsRejectedWithoutAcquisition()
    {
        TestSourceBehavior behavior = TestSourceBehavior.Create(
            PackagePlatformTestEnvironment.RuntimePackageId,
            versions: [PackagePlatformTestEnvironment.Version]);
        await using PackagePlatformTestEnvironment first =
            PackagePlatformTestEnvironment.Create([behavior]);
        await using PackagePlatformTestEnvironment second =
            PackagePlatformTestEnvironment.Create([behavior]);
        var selection = Assert.Single(Assert.IsType<
            PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.Succeeded>(
                await first.CreateSource().DiscoverAsync(
                    DiscoveryRequest(),
                    first.IssueOperation(TestContext.Current.CancellationToken))).Value.Targets);

        var rejected = Assert.IsType<
            PackagePlatformSourceOutcome<PackageReferenceRealization>.Rejected>(
                await second.CreateSource().RealizeAsync(
                    selection,
                    new PackageReferencePopulationDemand.CompletePopulation(),
                    Work(),
                    second.IssueOperation(TestContext.Current.CancellationToken)));

        Assert.Equal(PackagePlatformSourceDiagnosticKind.InvalidSelection, rejected.Diagnostic.Kind);
        Assert.Equal(0, second.Clients[0].PayloadRequests);
        await first.AssertSettledAsync();
        await second.AssertSettledAsync();
    }

    [Theory]
    [InlineData("11.0.0-RC.1")]
    [InlineData("11.0.0+build.1")]
    [InlineData("11.0.999999999999999999999")]
    public async Task ExternalExactTargetRejectsUnrepresentableOrIdentityChangingPackageVersion(
        string version)
    {
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(PackagePlatformTestEnvironment.RuntimePackageId),
            ]);
        var coordinate = new PackageReferencePackCoordinate(
            new PlatformFamilyTarget(
                PlatformFamily.DotNetRuntime,
                PlatformTargetFramework.Parse("net11.0"),
                PlatformVersion.Parse(version)));

        var rejected = Assert.IsType<
            PackagePlatformSourceOutcome<PackageReferenceRealization>.Rejected>(
                await environment.CreateSource().RealizeAsync(
                    coordinate,
                    new PackageReferencePopulationDemand.CompletePopulation(),
                    Work(),
                    environment.IssueOperation(TestContext.Current.CancellationToken)));

        Assert.Equal(PackagePlatformSourceDiagnosticKind.InvalidCoordinate, rejected.Diagnostic.Kind);
        Assert.Empty(environment.Authorization.Requests);
        Assert.Equal(0, environment.Clients[0].PayloadRequests);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task CompletePopulationSelectsOnlyExactTopLevelDllsInDeterministicOrder()
    {
        byte[] alpha = PackagePlatformTestData.Assembly("Alpha");
        byte[] zeta = PackagePlatformTestData.Assembly("Zeta");
        IReadOnlyList<KeyValuePair<string, byte[]>> entries =
        [
            PackagePlatformTestData.Entry("ref/net11.0/Zeta.DLL", zeta),
            PackagePlatformTestData.Entry("ref/net10.0/Other.dll", PackagePlatformTestData.Assembly("Other")),
            PackagePlatformTestData.Entry("ref/net11.0/nested/Nested.dll", PackagePlatformTestData.Assembly("Nested")),
            PackagePlatformTestData.Entry("ref/net11.0/Zeta.xml", "<doc />"u8.ToArray()),
            PackagePlatformTestData.Entry("analyzers/Analyzer.dll", PackagePlatformTestData.Assembly("Analyzer")),
            PackagePlatformTestData.Entry("ref/net11.0/Alpha.dll", alpha),
        ];
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    entries: entries),
            ]);

        var realization = Assert.IsType<
            PackagePlatformSourceOutcome<PackageReferenceRealization>.Succeeded>(
                await environment.CreateSource().RealizeAsync(
                    Coordinate(),
                    new PackageReferencePopulationDemand.CompletePopulation(),
                    Work(),
                    environment.IssueOperation(TestContext.Current.CancellationToken))).Value;

        Assert.Equal(
            ["ref/net11.0/Alpha.dll", "ref/net11.0/Zeta.DLL"],
            realization.Libraries.Select(static library => library.Path));
        Assert.Equal(["Alpha", "Zeta"], realization.Libraries.Select(static library => library.Identity.Name));
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task CaseCollisionsRejectPopulationAtomically()
    {
        byte[] assembly = PackagePlatformTestData.Assembly("System.Runtime");
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    entries:
                    [
                        PackagePlatformTestData.Entry("ref/net11.0/System.Runtime.dll", assembly),
                        PackagePlatformTestData.Entry("ref/net11.0/system.runtime.DLL", assembly),
                    ]),
            ]);
        PackagePlatformSourceOutcome<PackageReferenceRealization> outcome =
            await environment.CreateSource().RealizeAsync(
                Coordinate(),
                new PackageReferencePopulationDemand.CompletePopulation(),
                Work(),
                environment.IssueOperation(TestContext.Current.CancellationToken));

        Assert.True(
            outcome is PackagePlatformSourceOutcome<PackageReferenceRealization>.Rejected,
            $"{outcome.GetType().FullName}: "
            + (outcome as PackagePlatformSourceOutcome<PackageReferenceRealization>.NotSucceeded)
                ?.Diagnostic.Kind);
        var rejected =
            (PackagePlatformSourceOutcome<PackageReferenceRealization>.Rejected)outcome;

        Assert.Equal(PackagePlatformSourceDiagnosticKind.PackageFailure, rejected.Diagnostic.Kind);
        PackageAuthorityFailure failure =
            Assert.Single(rejected.Diagnostic.PackageFailures);
        Assert.Equal(PackageAuthorityFailureKind.ResponseRejected, failure.Kind);
        Assert.Same(environment.Clients[0].Source, failure.ResultSource);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task ExactAssemblyReadsOnlyProjectedMemberAndValidatesCompleteIdentity()
    {
        byte[] requested = PackagePlatformTestData.Assembly("System.Runtime", new Version(11, 0, 0, 0));
        byte[] unrelated = PackagePlatformTestData.Assembly("System.Console");
        AssemblyReferenceIdentity identity = PackagePlatformTestData.Identity(requested);
        var result = await CachedContentAsync(
        [
            PackagePlatformTestData.Entry("ref/net11.0/System.Console.dll", unrelated),
            PackagePlatformTestData.Entry("ref/net11.0/System.Runtime.dll", requested),
        ],
        new PackageReferencePopulationDemand.Assembly(identity),
        throwOnOpen: ["ref/net11.0/System.Console.dll"]);

        PackageReferenceLibrary library = Assert.Single(Assert.IsType<
            PackagePlatformSourceOutcome<PackageReferenceRealization>.Succeeded>(
                result.Outcome).Value.Libraries);
        Assert.Equal(["ref/net11.0/System.Runtime.dll"], result.Content.OpenedEntries);
        Assert.Equal(requested, await PackagePlatformTestData.ReadAllAsync(library));
        await result.Environment.AssertSettledAsync();
        await result.Environment.DisposeAsync();

        var mismatch = await CachedContentAsync(
        [
            PackagePlatformTestData.Entry("ref/net11.0/System.Runtime.dll", requested),
        ],
        new PackageReferencePopulationDemand.Assembly(
            new AssemblyReferenceIdentity(
                "System.Runtime",
                new Version(11, 0, 1, 0),
                null,
                null)));
        var rejected = Assert.IsType<
            PackagePlatformSourceOutcome<PackageReferenceRealization>.Rejected>(
                mismatch.Outcome);
        Assert.Equal(PackagePlatformSourceDiagnosticKind.AssemblyIdentityMismatch, rejected.Diagnostic.Kind);
        await mismatch.Environment.AssertSettledAsync();
        await mismatch.Environment.DisposeAsync();
    }

    [Fact]
    public async Task MalformedNetmoduleWinMdAndDuplicateIdentityRejectAtomically()
    {
        (byte[] Image, PackagePlatformSourceDiagnosticKind Kind)[] invalid =
        [
            ([0x4d, 0x5a, 0, 1], PackagePlatformSourceDiagnosticKind.MalformedAssembly),
            (PackagePlatformTestData.Netmodule("Module"), PackagePlatformSourceDiagnosticKind.MalformedAssembly),
            (PackagePlatformTestData.Assembly(
                "Windows",
                metadataVersion: "WindowsRuntime 1.4"),
                PackagePlatformSourceDiagnosticKind.MalformedAssembly),
        ];
        foreach ((byte[] image, PackagePlatformSourceDiagnosticKind kind) in invalid)
        {
            var result = await CachedContentAsync(
            [
                PackagePlatformTestData.Entry("ref/net11.0/Invalid.dll", image),
            ]);
            var rejected = Assert.IsType<
                PackagePlatformSourceOutcome<PackageReferenceRealization>.Rejected>(
                    result.Outcome);
            Assert.Equal(kind, rejected.Diagnostic.Kind);
            await result.Environment.AssertSettledAsync();
            await result.Environment.DisposeAsync();
        }

        byte[] duplicate = PackagePlatformTestData.Assembly("Duplicate");
        var duplicates = await CachedContentAsync(
        [
            PackagePlatformTestData.Entry("ref/net11.0/First.dll", duplicate),
            PackagePlatformTestData.Entry("ref/net11.0/Second.dll", duplicate),
        ]);
        var duplicateRejected = Assert.IsType<
            PackagePlatformSourceOutcome<PackageReferenceRealization>.Rejected>(
                duplicates.Outcome);
        Assert.Equal(
            PackagePlatformSourceDiagnosticKind.DuplicateAssemblyIdentity,
            duplicateRejected.Diagnostic.Kind);
        await duplicates.Environment.AssertSettledAsync();
        await duplicates.Environment.DisposeAsync();
    }

    [Fact]
    public async Task EntryAssemblyAndByteBudgetsHonorZeroAndExactThresholds()
    {
        byte[] first = PackagePlatformTestData.Assembly("First");
        byte[] second = PackagePlatformTestData.Assembly("Second");
        IReadOnlyList<KeyValuePair<string, byte[]>> entries =
        [
            PackagePlatformTestData.Entry("ref/net11.0/First.dll", first),
            PackagePlatformTestData.Entry("ref/net11.0/Second.dll", second),
        ];

        await AssertIncompleteAsync(
            entries,
            new PackagePlatformSourceLimits(maxObservedEntries: 0));
        await AssertSucceededAsync(
            entries,
            new PackagePlatformSourceLimits(maxObservedEntries: entries.Count));
        await AssertIncompleteAsync(
            entries,
            new PackagePlatformSourceLimits(maxAssemblies: 1));
        await AssertSucceededAsync(
            entries,
            new PackagePlatformSourceLimits(maxAssemblies: entries.Count));
        await AssertIncompleteAsync(
            entries,
            new PackagePlatformSourceLimits(maxEntryBytes: first.LongLength - 1));
        await AssertSucceededAsync(
            entries,
            new PackagePlatformSourceLimits(
                maxEntryBytes: Math.Max(first.LongLength, second.LongLength)));
        await AssertIncompleteAsync(
            entries,
            limits: new PackagePlatformSourceLimits(maxBytes: first.LongLength + second.LongLength - 1));
        await AssertSucceededAsync(
            entries,
            limits: new PackagePlatformSourceLimits(maxBytes: first.LongLength + second.LongLength));
        await AssertIncompleteAsync(
            entries,
            work: new PackageReferenceWorkBudget(maxAssemblies: 0, maxBytes: long.MaxValue));
        await AssertIncompleteAsync(
            entries,
            work: new PackageReferenceWorkBudget(
                maxAssemblies: entries.Count,
                maxBytes: first.LongLength + second.LongLength - 1));
        await AssertSucceededAsync(
            entries,
            work: new PackageReferenceWorkBudget(
                maxAssemblies: entries.Count,
                maxBytes: first.LongLength + second.LongLength));
    }

    [Fact]
    public async Task CallerCancellationAndOperationDeadlineRemainDistinctAndReleaseOperation()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        await using PackagePlatformTestEnvironment cancelled =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    beforeVersions: token => Task.Delay(TimeSpan.FromMilliseconds(60), token)),
            ]);
        Task<PackagePlatformSourceOutcome<PackagePlatformTargetInventory>> pending =
            cancelled.CreateSource().DiscoverAsync(
                DiscoveryRequest(),
                cancelled.IssueOperation(cancellation.Token));
        cancellation.Cancel();
        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        await cancelled.AssertSettledAsync();

        await using PackagePlatformTestEnvironment timedOut =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    versionFailure: PackageSourceFailureKind.Transport),
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    beforeVersions: token => Task.Delay(TimeSpan.FromMilliseconds(300), token)),
            ]);
        var timeout = Assert.IsType<
            PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.Incomplete>(
                await timedOut.CreateSource().DiscoverAsync(
                    DiscoveryRequest(),
                    timedOut.IssueOperation(
                        TestContext.Current.CancellationToken,
                        requestTimeout: TimeSpan.FromSeconds(1),
                        operationTimeout: TimeSpan.FromMilliseconds(150))));
        Assert.Equal(PackagePlatformSourceDiagnosticKind.Timeout, timeout.Diagnostic.Kind);
        PackageAuthorityFailure transport = Assert.Single(
            timeout.Diagnostic.PackageFailures,
            failure => failure.Kind == PackageAuthorityFailureKind.Transport);
        Assert.Same(timedOut.Clients[0].Source, transport.ResultSource);
        PackageAuthorityFailure deadline = Assert.Single(
            timeout.Diagnostic.PackageFailures,
            failure => failure.Kind == PackageAuthorityFailureKind.Timeout);
        Assert.Equal(PackageSourceTimeoutKind.Operation, deadline.Timeout?.Kind);
        Assert.Equal(
            PackageSourceDisplay.ForDiagnostics(
                timedOut.Authorization.AuthorizeSourcesFor(
                    PackagePlatformTestEnvironment.RuntimePackageId).Authorities[1].Source),
            deadline.Authority);
        await timedOut.AssertSettledAsync();
    }

    [Fact]
    public async Task RealizedBytesSurviveRootRetirementContentRetirementAndStoreReplacement()
    {
        byte[] image = PackagePlatformTestData.Assembly("System.Runtime");
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(PackagePlatformTestEnvironment.RuntimePackageId),
            ]);
        var content = new TrackingPackageContent(
            environment.Clients[0].Source.Producer.Key,
            [
                PackagePlatformTestData.Entry("ref/net11.0/System.Runtime.dll", image),
            ]);
        environment.Store = new StaticPackageStore(content);
        PackagePlatformSource source = environment.CreateSource();
        var realization = Assert.IsType<
            PackagePlatformSourceOutcome<PackageReferenceRealization>.Succeeded>(
                await source.RealizeAsync(
                    Coordinate(),
                    new PackageReferencePopulationDemand.CompletePopulation(),
                    Work(),
                    environment.IssueOperation(TestContext.Current.CancellationToken))).Value;

        PackagePlatformSourceOutcome<PackagePlatformTargetInventory> stillUsable =
            await source.DiscoverAsync(
                DiscoveryRequest(),
                environment.IssueOperation(TestContext.Current.CancellationToken));
        Assert.IsType<
            PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.Succeeded>(stillUsable);
        await environment.AssertSettledAsync();
        content.Retire();
        environment.Store = new InMemoryPackageStore();

        Assert.Equal(
            image,
            await PackagePlatformTestData.ReadAllAsync(Assert.Single(realization.Libraries)));
        Assert.Same(
            ((IPackageContent)content).GenerationIdentity,
            realization.ContentGeneration);
    }

    [Fact]
    public async Task MemoryAndFilesystemStoresProduceEquivalentReferenceEvidence()
    {
        byte[] first = PackagePlatformTestData.Assembly("First");
        byte[] second = PackagePlatformTestData.Assembly("Second");
        IReadOnlyList<KeyValuePair<string, byte[]>> entries =
        [
            PackagePlatformTestData.Entry(
                PackagePlatformTestEnvironment.RuntimePackageId + ".nuspec",
                System.Text.Encoding.UTF8.GetBytes(
                    "<package><metadata><id>microsoft.netcore.app.ref</id><version>"
                    + PackagePlatformTestEnvironment.Version
                    + "</version></metadata></package>")),
            PackagePlatformTestData.Entry("ref/net11.0/Second.dll", second),
            PackagePlatformTestData.Entry("ref/net11.0/First.dll", first),
        ];
        string root = Path.Combine(
            Environment.CurrentDirectory,
            "artifacts",
            "package-platform-filesystem-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? priorTemp = Environment.GetEnvironmentVariable("TMPDIR");
        Environment.SetEnvironmentVariable("TMPDIR", root);
        NuGetCache.Initialize(
            "dotnet-inspect-package-platform-tests",
            root,
            skipNuGetCache: true);
        try
        {
            PackageReferenceRealization memory =
                await RealizeWithStoreAsync(new InMemoryPackageStore(), entries);
            PackageReferenceRealization filesystem =
                await RealizeWithStoreAsync(new FileSystemPackageStore(), entries);

            Assert.Equal(
                memory.Libraries.Select(static library => (library.Path, library.Identity)),
                filesystem.Libraries.Select(static library => (library.Path, library.Identity)));
            for (int index = 0; index < memory.Libraries.Length; index++)
            {
                Assert.Equal(
                    await PackagePlatformTestData.ReadAllAsync(memory.Libraries[index]),
                    await PackagePlatformTestData.ReadAllAsync(filesystem.Libraries[index]));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMPDIR", priorTemp);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static PackageReferenceDiscoveryRequest DiscoveryRequest(int maxCandidates = 8) =>
        new(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            maxCandidates);

    private static PackageReferencePackCoordinate Coordinate() =>
        new(
            new PlatformFamilyTarget(
                PlatformFamily.DotNetRuntime,
                PlatformTargetFramework.Parse("net11.0"),
                PlatformVersion.Parse(PackagePlatformTestEnvironment.Version)));

    private static PackageReferenceWorkBudget Work() =>
        new(maxAssemblies: 16, maxBytes: 16 * 1024 * 1024);

    private static async Task<(
        PackagePlatformTestEnvironment Environment,
        TrackingPackageContent Content,
        PackagePlatformSourceOutcome<PackageReferenceRealization> Outcome)> CachedContentAsync(
        IReadOnlyList<KeyValuePair<string, byte[]>> entries,
        PackageReferencePopulationDemand? population = null,
        PackagePlatformSourceLimits? limits = null,
        PackageReferenceWorkBudget? work = null,
        IEnumerable<string>? throwOnOpen = null)
    {
        PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(PackagePlatformTestEnvironment.RuntimePackageId),
            ]);
        var content = new TrackingPackageContent(
            environment.Clients[0].Source.Producer.Key,
            entries,
            throwOnOpen);
        environment.Store = new StaticPackageStore(content);
        PackagePlatformSourceOutcome<PackageReferenceRealization> outcome =
            await environment.CreateSource(limits).RealizeAsync(
                Coordinate(),
                population ?? new PackageReferencePopulationDemand.CompletePopulation(),
                work ?? Work(),
                environment.IssueOperation(TestContext.Current.CancellationToken));
        return (environment, content, outcome);
    }

    private static async Task AssertIncompleteAsync(
        IReadOnlyList<KeyValuePair<string, byte[]>> entries,
        PackagePlatformSourceLimits? limits = null,
        PackageReferenceWorkBudget? work = null)
    {
        var result = await CachedContentAsync(entries, limits: limits, work: work);
        var incomplete = Assert.IsType<
            PackagePlatformSourceOutcome<PackageReferenceRealization>.Incomplete>(
                result.Outcome);
        Assert.Equal(PackagePlatformSourceDiagnosticKind.WorkLimitExceeded, incomplete.Diagnostic.Kind);
        await result.Environment.AssertSettledAsync();
        await result.Environment.DisposeAsync();
    }

    private static async Task AssertSucceededAsync(
        IReadOnlyList<KeyValuePair<string, byte[]>> entries,
        PackagePlatformSourceLimits? limits = null,
        PackageReferenceWorkBudget? work = null)
    {
        var result = await CachedContentAsync(entries, limits: limits, work: work);
        Assert.IsType<
            PackagePlatformSourceOutcome<PackageReferenceRealization>.Succeeded>(
                result.Outcome);
        await result.Environment.AssertSettledAsync();
        await result.Environment.DisposeAsync();
    }

    private static async Task<PackageReferenceRealization> RealizeWithStoreAsync(
        IPackageStore store,
        IReadOnlyList<KeyValuePair<string, byte[]>> entries)
    {
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    entries: entries),
            ],
            store);
        PackagePlatformSourceOutcome<PackageReferenceRealization> outcome =
            await environment.CreateSource().RealizeAsync(
                Coordinate(),
                new PackageReferencePopulationDemand.CompletePopulation(),
                Work(),
                environment.IssueOperation(TestContext.Current.CancellationToken));
        Assert.True(
            outcome is PackagePlatformSourceOutcome<PackageReferenceRealization>.Succeeded,
            $"{outcome.GetType().FullName}: "
            + (outcome as PackagePlatformSourceOutcome<PackageReferenceRealization>.NotSucceeded)
                ?.Diagnostic.Kind);
        var realization =
            ((PackagePlatformSourceOutcome<PackageReferenceRealization>.Succeeded)outcome).Value;
        await environment.AssertSettledAsync();
        return realization;
    }
}
