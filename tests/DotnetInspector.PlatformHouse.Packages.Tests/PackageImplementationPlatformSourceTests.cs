using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;

namespace DotnetInspector.PlatformHouse.Packages.Tests;

public sealed class PackageImplementationPlatformSourceTests
{
    [Fact]
    public async Task DotNetRuntimeUsesManifestMembershipAndDetachedSnapshots()
    {
        byte[] runtime = PackagePlatformTestData.Assembly("System.Runtime");
        byte[] unlisted = PackagePlatformTestData.Assembly("Unlisted");
        IReadOnlyList<KeyValuePair<string, byte[]>> entries =
            PackagePlatformTestData.RuntimePackEntries(
                "Microsoft.NETCore.App",
                PackagePlatformTestData.RuntimeConfiguration(),
                PackagePlatformTestData.DependencyManifest(
                    "System.Runtime.dll"),
                ("System.Runtime.dll", runtime),
                ("Unlisted.dll", unlisted));
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment
                        .RuntimeImplementationPackageId,
                    entries: entries),
            ]);

        var succeeded = Assert.IsType<
            PackagePlatformSourceOutcome<
                PackageImplementationRealization>.Succeeded>(
                    await environment.CreateSource()
                        .RealizeImplementationAsync(
                            Coordinate(PlatformFamily.DotNetRuntime),
                            Work(),
                            environment.IssueOperation(
                                TestContext.Current.CancellationToken)));

        PackageImplementationFramework framework =
            Assert.Single(succeeded.Value.Frameworks);
        Assert.Equal("Microsoft.NETCore.App", framework.Name.Value);
        Assert.Equal(
            PackagePlatformTestEnvironment.RuntimeImplementationPackageId,
            framework.PackageId);
        Assert.Equal("linux-x64", framework.RuntimeIdentifier);
        Assert.Equal(64, framework.RuntimeConfigurationDigest.Value.Length);
        Assert.Equal(64, framework.DependencyManifestDigest.Value.Length);
        PackageImplementationLibrary library =
            Assert.Single(succeeded.Value.Libraries);
        Assert.Equal("System.Runtime", library.Identity.Name);
        Assert.Equal(64, library.ContentDigest.Value.Length);

        await environment.AssertSettledAsync();
        Assert.Equal(
            runtime,
            await PackagePlatformTestData.ReadAllAsync(library));
    }

    [Fact]
    public async Task AspNetCoreResolvesExactRuntimeSupportPack()
    {
        byte[] core = PackagePlatformTestData.Assembly("System.Runtime");
        byte[] aspnet =
            PackagePlatformTestData.Assembly("Microsoft.AspNetCore.Hosting");
        IReadOnlyList<KeyValuePair<string, byte[]>> coreEntries =
            PackagePlatformTestData.RuntimePackEntries(
                "Microsoft.NETCore.App",
                PackagePlatformTestData.RuntimeConfiguration(),
                PackagePlatformTestData.DependencyManifest(
                    "System.Runtime.dll"),
                ("System.Runtime.dll", core));
        IReadOnlyList<KeyValuePair<string, byte[]>> aspnetEntries =
            PackagePlatformTestData.RuntimePackEntries(
                "Microsoft.AspNetCore.App",
                PackagePlatformTestData.RuntimeConfiguration(
                    (
                        "Microsoft.NETCore.App",
                        PackagePlatformTestEnvironment.Version,
                        "Disable")),
                PackagePlatformTestData.DependencyManifest(
                    "Microsoft.AspNetCore.Hosting.dll"),
                ("Microsoft.AspNetCore.Hosting.dll", aspnet));
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.CreatePackages(
                    (
                        PackagePlatformTestEnvironment
                            .AspNetImplementationPackageId,
                        PackagePlatformTestEnvironment.Version,
                        aspnetEntries),
                    (
                        PackagePlatformTestEnvironment
                            .RuntimeImplementationPackageId,
                        PackagePlatformTestEnvironment.Version,
                        coreEntries)),
            ]);

        var succeeded = Assert.IsType<
            PackagePlatformSourceOutcome<
                PackageImplementationRealization>.Succeeded>(
                    await environment.CreateSource()
                        .RealizeImplementationAsync(
                            Coordinate(PlatformFamily.AspNetCore),
                            Work(),
                            environment.IssueOperation(
                                TestContext.Current.CancellationToken)));

        Assert.Equal(
            ["Microsoft.NETCore.App", "Microsoft.AspNetCore.App"],
            succeeded.Value.Frameworks.Select(
                static framework => framework.Name.Value));
        Assert.Equal(
            [
                PlatformFamily.DotNetRuntime,
                PlatformFamily.AspNetCore,
            ],
            succeeded.Value.Frameworks.Select(
                static framework => framework.Family));
        Assert.Equal(
            ["Microsoft.AspNetCore.Hosting", "System.Runtime"],
            succeeded.Value.Libraries.Select(
                static library => library.Identity.Name));
        Assert.All(
            succeeded.Value.Frameworks,
            framework => Assert.Equal(
                PackagePlatformTestEnvironment.Version,
                framework.Version.Value));
        Assert.Equal(1, environment.Clients[0].VersionRequests);
        Assert.Equal(2, environment.Clients[0].PayloadRequests);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task AspNetCoreAppliesManifestRollForwardToSupportInventory()
    {
        const string rootVersion = "11.0.0";
        const string selectedSupportVersion = "11.0.1";
        byte[] core = PackagePlatformTestData.Assembly("System.Runtime");
        byte[] aspnet =
            PackagePlatformTestData.Assembly("Microsoft.AspNetCore.Hosting");
        IReadOnlyList<KeyValuePair<string, byte[]>> coreEntries =
            PackagePlatformTestData.RuntimePackEntries(
                "Microsoft.NETCore.App",
                PackagePlatformTestData.RuntimeConfiguration(),
                PackagePlatformTestData.DependencyManifest(
                    "System.Runtime.dll"),
                ("System.Runtime.dll", core));
        IReadOnlyList<KeyValuePair<string, byte[]>> aspnetEntries =
            PackagePlatformTestData.RuntimePackEntries(
                "Microsoft.AspNetCore.App",
                PackagePlatformTestData.RuntimeConfiguration(
                    (
                        "Microsoft.NETCore.App",
                        rootVersion,
                        "LatestPatch")),
                PackagePlatformTestData.DependencyManifest(
                    "Microsoft.AspNetCore.Hosting.dll"),
                ("Microsoft.AspNetCore.Hosting.dll", aspnet));
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.CreatePackages(
                    (
                        PackagePlatformTestEnvironment
                            .AspNetImplementationPackageId,
                        rootVersion,
                        aspnetEntries),
                    (
                        PackagePlatformTestEnvironment
                            .RuntimeImplementationPackageId,
                        rootVersion,
                        coreEntries),
                    (
                        PackagePlatformTestEnvironment
                            .RuntimeImplementationPackageId,
                        selectedSupportVersion,
                        coreEntries)),
            ]);
        var coordinate = new PackageImplementationPlatformCoordinate(
            new PlatformFamilyTarget(
                PlatformFamily.AspNetCore,
                PlatformTargetFramework.Parse("net11.0"),
                PlatformVersion.Parse(rootVersion)),
            "linux-x64");

        var succeeded = Assert.IsType<
            PackagePlatformSourceOutcome<
                PackageImplementationRealization>.Succeeded>(
                    await environment.CreateSource()
                        .RealizeImplementationAsync(
                            coordinate,
                            Work(),
                            environment.IssueOperation(
                                TestContext.Current.CancellationToken)));

        Assert.Equal(
            selectedSupportVersion,
            succeeded.Value.Frameworks[0].Version.Value);
        Assert.Equal(
            PackageAcquisitionCandidateKind.Discovered,
            succeeded.Value.Frameworks[0].Candidate.Kind);
        Assert.Equal(
            PackageAcquisitionCandidateKind.CallerPinned,
            succeeded.Value.Frameworks[1].Candidate.Kind);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task SupportInventoryRespectsCandidateBound()
    {
        const string rootVersion = "11.0.0";
        byte[] core = PackagePlatformTestData.Assembly("System.Runtime");
        byte[] aspnet =
            PackagePlatformTestData.Assembly("Microsoft.AspNetCore.Hosting");
        IReadOnlyList<KeyValuePair<string, byte[]>> coreEntries =
            PackagePlatformTestData.RuntimePackEntries(
                "Microsoft.NETCore.App",
                PackagePlatformTestData.RuntimeConfiguration(),
                PackagePlatformTestData.DependencyManifest(
                    "System.Runtime.dll"),
                ("System.Runtime.dll", core));
        IReadOnlyList<KeyValuePair<string, byte[]>> aspnetEntries =
            PackagePlatformTestData.RuntimePackEntries(
                "Microsoft.AspNetCore.App",
                PackagePlatformTestData.RuntimeConfiguration(
                    (
                        "Microsoft.NETCore.App",
                        rootVersion,
                        "LatestPatch")),
                PackagePlatformTestData.DependencyManifest(
                    "Microsoft.AspNetCore.Hosting.dll"),
                ("Microsoft.AspNetCore.Hosting.dll", aspnet));
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.CreatePackages(
                    (
                        PackagePlatformTestEnvironment
                            .AspNetImplementationPackageId,
                        rootVersion,
                        aspnetEntries),
                    (
                        PackagePlatformTestEnvironment
                            .RuntimeImplementationPackageId,
                        rootVersion,
                        coreEntries),
                    (
                        PackagePlatformTestEnvironment
                            .RuntimeImplementationPackageId,
                        "11.0.1",
                        coreEntries)),
            ]);
        var coordinate = new PackageImplementationPlatformCoordinate(
            new PlatformFamilyTarget(
                PlatformFamily.AspNetCore,
                PlatformTargetFramework.Parse("net11.0"),
                PlatformVersion.Parse(rootVersion)),
            "linux-x64");

        var incomplete = Assert.IsType<
            PackagePlatformSourceOutcome<
                PackageImplementationRealization>.Incomplete>(
                    await environment.CreateSource(
                            new PackagePlatformSourceLimits(
                                maxCandidates: 1))
                        .RealizeImplementationAsync(
                            coordinate,
                            Work(),
                            environment.IssueOperation(
                                TestContext.Current.CancellationToken)));

        Assert.Equal(
            PackagePlatformSourceDiagnosticKind.WorkLimitExceeded,
            incomplete.Diagnostic.Kind);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task ManifestMembershipDoesNotReadUnlistedBodies()
    {
        byte[] runtime = PackagePlatformTestData.Assembly("System.Runtime");
        IReadOnlyList<KeyValuePair<string, byte[]>> entries =
            PackagePlatformTestData.RuntimePackEntries(
                "Microsoft.NETCore.App",
                PackagePlatformTestData.RuntimeConfiguration(),
                PackagePlatformTestData.DependencyManifest(
                    "System.Runtime.dll"),
                ("System.Runtime.dll", runtime),
                ("Unlisted.dll",
                    PackagePlatformTestData.Assembly("Unlisted")));
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment
                        .RuntimeImplementationPackageId),
            ]);
        var content = new TrackingPackageContent(
            environment.Clients[0].Source.Producer.Key,
            entries,
            throwOnOpen:
            [
                "runtimes/linux-x64/lib/net11.0/Unlisted.dll",
            ]);
        environment.Store = new StaticPackageStore(content);

        var succeeded = Assert.IsType<
            PackagePlatformSourceOutcome<
                PackageImplementationRealization>.Succeeded>(
                    await environment.CreateSource()
                        .RealizeImplementationAsync(
                            Coordinate(PlatformFamily.DotNetRuntime),
                            Work(),
                            environment.IssueOperation(
                                TestContext.Current.CancellationToken)));

        Assert.DoesNotContain(
            "runtimes/linux-x64/lib/net11.0/Unlisted.dll",
            content.OpenedEntries);
        PackageImplementationLibrary library =
            Assert.Single(succeeded.Value.Libraries);
        await environment.AssertSettledAsync();
        content.Retire();
        Assert.Equal(
            runtime,
            await PackagePlatformTestData.ReadAllAsync(library));
    }

    [Theory]
    [InlineData("runtimeconfig", PackagePlatformSourceDiagnosticKind.InvalidManifest)]
    [InlineData("missing-member", PackagePlatformSourceDiagnosticKind.InvalidMember)]
    [InlineData("identity", PackagePlatformSourceDiagnosticKind.AssemblyIdentityMismatch)]
    [InlineData("malformed", PackagePlatformSourceDiagnosticKind.MalformedAssembly)]
    [InlineData("runtime-target", PackagePlatformSourceDiagnosticKind.InvalidManifest)]
    [InlineData("logical-collision", PackagePlatformSourceDiagnosticKind.DuplicateLogicalCoordinate)]
    public async Task InvalidManifestMemberAndIdentityRejectAtomically(
        string scenario,
        PackagePlatformSourceDiagnosticKind expected)
    {
        byte[] image = scenario == "malformed"
            ? [0x4d, 0x5a, 0, 1]
            : PackagePlatformTestData.Assembly(
                scenario == "identity" ? "Other" : "System.Runtime");
        byte[] runtimeConfiguration =
            scenario == "runtimeconfig"
                ? "{]"u8.ToArray()
                : PackagePlatformTestData.RuntimeConfiguration();
        string[] manifestAssets =
            scenario == "logical-collision"
                ? ["a/System.Runtime.dll", "b/System.Runtime.dll"]
                : ["System.Runtime.dll"];
        (string FileName, byte[] Content)[] members =
            scenario == "missing-member"
                ? []
                : [("System.Runtime.dll", image)];
        IReadOnlyList<KeyValuePair<string, byte[]>> entries =
            PackagePlatformTestData.RuntimePackEntries(
                "Microsoft.NETCore.App",
                runtimeConfiguration,
                scenario == "runtime-target"
                    ? PackagePlatformTestData.DependencyManifestForTarget(
                        ".NETCoreApp,Version=v11.0/win-x64",
                        manifestAssets)
                    : PackagePlatformTestData.DependencyManifest(
                        manifestAssets),
                members);
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment
                        .RuntimeImplementationPackageId,
                    entries: entries),
            ]);

        var rejected = Assert.IsType<
            PackagePlatformSourceOutcome<
                PackageImplementationRealization>.Rejected>(
                    await environment.CreateSource()
                        .RealizeImplementationAsync(
                            Coordinate(PlatformFamily.DotNetRuntime),
                            Work(),
                            environment.IssueOperation(
                                TestContext.Current.CancellationToken)));

        Assert.Equal(expected, rejected.Diagnostic.Kind);
        Assert.Empty(rejected.Diagnostic.PackageFailures);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task UnsupportedFrameworkClosureRejectsAtomically()
    {
        IReadOnlyList<KeyValuePair<string, byte[]>> entries =
            PackagePlatformTestData.RuntimePackEntries(
                "Microsoft.AspNetCore.App",
                PackagePlatformTestData.RuntimeConfiguration(
                    (
                        "Contoso.Unsupported",
                        PackagePlatformTestEnvironment.Version,
                        "LatestPatch")),
                PackagePlatformTestData.DependencyManifest(
                    "Microsoft.AspNetCore.Hosting.dll"),
                ("Microsoft.AspNetCore.Hosting.dll",
                    PackagePlatformTestData.Assembly(
                        "Microsoft.AspNetCore.Hosting")));
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment
                        .AspNetImplementationPackageId,
                    entries: entries),
            ]);

        var rejected = Assert.IsType<
            PackagePlatformSourceOutcome<
                PackageImplementationRealization>.Rejected>(
                    await environment.CreateSource()
                        .RealizeImplementationAsync(
                            Coordinate(PlatformFamily.AspNetCore),
                            Work(),
                            environment.IssueOperation(
                                TestContext.Current.CancellationToken)));

        Assert.Equal(
            PackagePlatformSourceDiagnosticKind.InvalidFrameworkGraph,
            rejected.Diagnostic.Kind);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task CancellationAndTimeoutRemainDistinct()
    {
        IReadOnlyList<KeyValuePair<string, byte[]>> entries =
            PackagePlatformTestData.RuntimePackEntries(
                "Microsoft.NETCore.App",
                PackagePlatformTestData.RuntimeConfiguration(),
                PackagePlatformTestData.DependencyManifest(
                    "System.Runtime.dll"),
                ("System.Runtime.dll",
                    PackagePlatformTestData.Assembly("System.Runtime")));

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        await using PackagePlatformTestEnvironment cancelled =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment
                        .RuntimeImplementationPackageId,
                    entries: entries,
                    beforePayload: token => Task.Delay(
                        TimeSpan.FromMilliseconds(60),
                        token)),
            ]);
        Task<PackagePlatformSourceOutcome<
            PackageImplementationRealization>> pending =
                cancelled.CreateSource()
                    .RealizeImplementationAsync(
                        Coordinate(PlatformFamily.DotNetRuntime),
                        Work(),
                        cancelled.IssueOperation(cancellation.Token));
        cancellation.Cancel();
        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => pending);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        await cancelled.AssertSettledAsync();

        await using PackagePlatformTestEnvironment timedOut =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment
                        .RuntimeImplementationPackageId,
                    entries: entries,
                    beforePayload: token => Task.Delay(
                        TimeSpan.FromMilliseconds(300),
                        token)),
            ]);
        var timeout = Assert.IsType<
            PackagePlatformSourceOutcome<
                PackageImplementationRealization>.Incomplete>(
                    await timedOut.CreateSource()
                        .RealizeImplementationAsync(
                            Coordinate(PlatformFamily.DotNetRuntime),
                            Work(),
                            timedOut.IssueOperation(
                                TestContext.Current.CancellationToken,
                                operationTimeout:
                                    TimeSpan.FromMilliseconds(150))));

        Assert.Equal(
            PackagePlatformSourceDiagnosticKind.Timeout,
            timeout.Diagnostic.Kind);
        await timedOut.AssertSettledAsync();
    }

    [Fact]
    public async Task SupportPackAuthorizationIsIndependent()
    {
        byte[] aspnet =
            PackagePlatformTestData.Assembly("Microsoft.AspNetCore.Hosting");
        IReadOnlyList<KeyValuePair<string, byte[]>> aspnetEntries =
            PackagePlatformTestData.RuntimePackEntries(
                "Microsoft.AspNetCore.App",
                PackagePlatformTestData.RuntimeConfiguration(
                    (
                        "Microsoft.NETCore.App",
                        PackagePlatformTestEnvironment.Version,
                        "Disable")),
                PackagePlatformTestData.DependencyManifest(
                    "Microsoft.AspNetCore.Hosting.dll"),
                ("Microsoft.AspNetCore.Hosting.dll", aspnet));
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.CreatePackages(
                    (
                        PackagePlatformTestEnvironment
                            .AspNetImplementationPackageId,
                        PackagePlatformTestEnvironment.Version,
                        aspnetEntries),
                    (
                        PackagePlatformTestEnvironment
                            .RuntimeImplementationPackageId,
                        PackagePlatformTestEnvironment.Version,
                        [])),
            ],
            deniedPackageIds:
            [
                PackagePlatformTestEnvironment
                    .RuntimeImplementationPackageId,
            ]);

        var unavailable = Assert.IsType<
            PackagePlatformSourceOutcome<
                PackageImplementationRealization>.Unavailable>(
                    await environment.CreateSource()
                        .RealizeImplementationAsync(
                            Coordinate(PlatformFamily.AspNetCore),
                            Work(),
                            environment.IssueOperation(
                                TestContext.Current.CancellationToken)));

        Assert.Equal(
            PackagePlatformSourceDiagnosticKind.AuthorizationDenied,
            unavailable.Diagnostic.Kind);
        Assert.Equal(
            [
                PackagePlatformTestEnvironment
                    .AspNetImplementationPackageId,
                PackagePlatformTestEnvironment
                    .RuntimeImplementationPackageId,
            ],
            environment.Authorization.Requests);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task FrameworkAssemblyAndByteBoundsDoNotReturnPartialSuccess()
    {
        byte[] image = PackagePlatformTestData.Assembly("System.Runtime");
        IReadOnlyList<KeyValuePair<string, byte[]>> entries =
            PackagePlatformTestData.RuntimePackEntries(
                "Microsoft.NETCore.App",
                PackagePlatformTestData.RuntimeConfiguration(),
                PackagePlatformTestData.DependencyManifest(
                    "System.Runtime.dll"),
                ("System.Runtime.dll", image));
        foreach (PackageImplementationWorkBudget work in new[]
        {
            new PackageImplementationWorkBudget(0, 8, 8, 8, 8, long.MaxValue),
            new PackageImplementationWorkBudget(8, 0, 8, 8, 8, long.MaxValue),
            new PackageImplementationWorkBudget(8, 8, 0, 8, 8, long.MaxValue),
            new PackageImplementationWorkBudget(8, 8, 8, 0, 8, long.MaxValue),
            new PackageImplementationWorkBudget(8, 8, 8, 8, 0, long.MaxValue),
            new PackageImplementationWorkBudget(8, 8, 8, 8, 8, 1),
        })
        {
            await using PackagePlatformTestEnvironment environment =
                PackagePlatformTestEnvironment.Create(
                [
                    TestSourceBehavior.Create(
                        PackagePlatformTestEnvironment
                            .RuntimeImplementationPackageId,
                        entries: entries),
                ]);

            var incomplete = Assert.IsType<
                PackagePlatformSourceOutcome<
                    PackageImplementationRealization>.Incomplete>(
                        await environment.CreateSource()
                            .RealizeImplementationAsync(
                                Coordinate(PlatformFamily.DotNetRuntime),
                                work,
                                environment.IssueOperation(
                                    TestContext.Current.CancellationToken)));

            Assert.Equal(
                PackagePlatformSourceDiagnosticKind.WorkLimitExceeded,
                incomplete.Diagnostic.Kind);
            await environment.AssertSettledAsync();
        }
    }

    [Fact]
    public async Task ManifestBoundsApplyAcrossTheCompleteFrameworkClosure()
    {
        IReadOnlyList<KeyValuePair<string, byte[]>> coreEntries =
            PackagePlatformTestData.RuntimePackEntries(
                "Microsoft.NETCore.App",
                PackagePlatformTestData.RuntimeConfiguration(),
                PackagePlatformTestData.DependencyManifest(
                    "System.Runtime.dll"),
                ("System.Runtime.dll",
                    PackagePlatformTestData.Assembly("System.Runtime")));
        IReadOnlyList<KeyValuePair<string, byte[]>> aspnetEntries =
            PackagePlatformTestData.RuntimePackEntries(
                "Microsoft.AspNetCore.App",
                PackagePlatformTestData.RuntimeConfiguration(
                    (
                        "Microsoft.NETCore.App",
                        PackagePlatformTestEnvironment.Version,
                        "LatestPatch")),
                PackagePlatformTestData.DependencyManifest(
                    "Microsoft.AspNetCore.Hosting.dll"),
                ("Microsoft.AspNetCore.Hosting.dll",
                    PackagePlatformTestData.Assembly(
                        "Microsoft.AspNetCore.Hosting")));
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.CreatePackages(
                    (
                        PackagePlatformTestEnvironment
                            .AspNetImplementationPackageId,
                        PackagePlatformTestEnvironment.Version,
                        aspnetEntries),
                    (
                        PackagePlatformTestEnvironment
                            .RuntimeImplementationPackageId,
                        PackagePlatformTestEnvironment.Version,
                        coreEntries)),
            ]);
        var work = new PackageImplementationWorkBudget(
            maxFrameworks: 2,
            maxResolutionSteps: 2,
            maxManifestLibraries: 1,
            maxManifestAssets: 8,
            maxAssemblies: 8,
            maxBytes: 1024 * 1024);

        var incomplete = Assert.IsType<
            PackagePlatformSourceOutcome<
                PackageImplementationRealization>.Incomplete>(
                    await environment.CreateSource()
                        .RealizeImplementationAsync(
                            Coordinate(PlatformFamily.AspNetCore),
                            work,
                            environment.IssueOperation(
                                TestContext.Current.CancellationToken)));

        Assert.Equal(
            PackagePlatformSourceDiagnosticKind.WorkLimitExceeded,
            incomplete.Diagnostic.Kind);
        await environment.AssertSettledAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("linux/x64")]
    [InlineData("Linux-X64")]
    [InlineData(".linux")]
    [InlineData("linux.")]
    public void CoordinateRejectsInvalidRuntimeIdentifier(string value)
    {
        Assert.Throws<ArgumentException>(
            () => new PackageImplementationPlatformCoordinate(
                Target(PlatformFamily.DotNetRuntime),
                value));
    }

    private static PackageImplementationPlatformCoordinate Coordinate(
        PlatformFamily family) =>
        new(Target(family), "linux-x64");

    private static PlatformFamilyTarget Target(PlatformFamily family) =>
        new(
            family,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse(
                PackagePlatformTestEnvironment.Version));

    private static PackageImplementationWorkBudget Work() =>
        new(
            maxFrameworks: 4,
            maxResolutionSteps: 8,
            maxManifestLibraries: 32,
            maxManifestAssets: 512,
            maxAssemblies: 512,
            maxBytes: 64 * 1024 * 1024);
}
