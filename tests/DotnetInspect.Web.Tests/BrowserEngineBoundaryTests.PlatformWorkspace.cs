using System.IO.Compression;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Text;
using System.Xml;
using System.Text.Json;
using DotnetInspector.Ecosystems;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Decompiler;
using InertText;
using Inspector.Findings;
using ILInspector.Metadata;
using NuGetFetch;

using DotnetInspect.Web.Interop.Package;
using BrowserMetadataJsonContext = DotnetInspect.Web.Interop.Metadata.BrowserMetadataJsonContext;
using BrowserAnalysisJsonContext = DotnetInspect.Web.Interop.Analysis.BrowserAnalysisJsonContext;
using BrowserSourceJsonContext = DotnetInspect.Web.Interop.Source.BrowserSourceJsonContext;
using BrowserCallGraphJsonContext = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphJsonContext;
using BrowserCatalogJsonContext = DotnetInspect.Web.Interop.Catalog.BrowserCatalogJsonContext;
using BrowserPackageMetadata = DotnetInspect.Web.Interop.Metadata.BrowserPackageMetadata;
using BrowserMetadataCompileLibraryStatus = DotnetInspect.Web.Interop.Metadata.BrowserCompileLibraryStatus;
using BrowserAnalysisCompileLibraryStatus = DotnetInspect.Web.Interop.Analysis.BrowserCompileLibraryStatus;
using BrowserPackageIntegrations = DotnetInspect.Web.Interop.Analysis.BrowserPackageIntegrations;
using BrowserPackageOpportunities = DotnetInspect.Web.Interop.Analysis.BrowserPackageOpportunities;
using BrowserPackagePerformance = DotnetInspect.Web.Interop.Analysis.BrowserPackagePerformance;
using BrowserPerformanceMember = DotnetInspect.Web.Interop.Analysis.BrowserPerformanceMember;
using BrowserOpportunityItem = DotnetInspect.Web.Interop.Analysis.BrowserOpportunityItem;
using BrowserSource = DotnetInspect.Web.Interop.Source.BrowserSource;
using BrowserCallGraph = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraph;
using BrowserCallGraphTarget = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphTarget;
using BrowserCallGraphWireProjection = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphWireProjection;
using BrowserCallGraphDiagnostics = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphDiagnostics;
using BrowserHomeDemoRunResult = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunResult;
using BrowserHomeDemoRunActivation = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunActivation;
using BrowserHomeDemoRunPlan = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunPlan;
using BrowserProductHomeDemos = DotnetInspect.Web.Interop.Catalog.BrowserProductHomeDemos;
using BrowserHomeDemoRunMember = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunMember;
using BrowserHomeDemoRunRequest = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunRequest;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserEngineBoundaryTests
{

    [Fact]
    public async Task PlatformWorkspace_ResolvesUnknownFamilyFromProductPacks()
    {
        const string version = "11.0.101";
        byte[] runtimeNupkg = PlatformPackage(
            ("System.Private.CoreLib.dll",
                File.ReadAllBytes(typeof(object).Assembly.Location)));
        byte[] aspNetNupkg = PlatformPackage(
            ("DotnetInspect.Web.Tests.dll",
                File.ReadAllBytes(
                    typeof(BrowserEngineBoundaryTests).Assembly.Location)));
        var handler = new MultiplePlatformVersionHandler(
            version,
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["microsoft.netcore.app.runtime.linux-x64"] = runtimeNupkg,
                ["microsoft.aspnetcore.app.runtime.linux-x64"] = aspNetNupkg,
            });
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);

        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0-auto-family-resolution",
                "DotnetInspect.Web.Tests.dll",
                "",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        Assert.Equal("aspnetcore", resolution.Coordinate.Family);
        Assert.True(
            BrowserPackageWorkspace.IsScopeRetained(resolution.Scope));
        Assert.Single(resolution.Scope.Members);
        Assert.Equal(
            "aspnetcore.app",
            resolution.Scope.PlatformPackForAssembly(
                "DotnetInspect.Web.Tests"));
    }

    [Fact]
    public async Task PlatformWorkspace_UnknownFamilyCancellationLeavesTargetStateClean()
    {
        const string version = "11.0.104";
        const string runtimePackage =
            "microsoft.netcore.app.runtime.linux-x64";
        const string aspNetPackage =
            "microsoft.aspnetcore.app.runtime.linux-x64";
        byte[] runtimeNupkg = PlatformPackage(
            ("DotnetInspect.Web.Tests.dll",
                File.ReadAllBytes(
                    typeof(BrowserEngineBoundaryTests).Assembly.Location)));
        byte[] aspNetNupkg = PlatformPackage(
            ("Microsoft.AspNetCore.Http.dll",
                File.ReadAllBytes(typeof(object).Assembly.Location)));
        var handler = new MultiplePlatformVersionHandler(
            version,
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                [runtimePackage] = runtimeNupkg,
                [aspNetPackage] = aspNetNupkg,
            });
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);
        using var cancellation = new CancellationTokenSource();
        handler.BeforeDownload = package =>
        {
            if (package.Equals(
                    aspNetPackage,
                    StringComparison.OrdinalIgnoreCase))
            {
                cancellation.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0-auto-family-cancellation",
                "DotnetInspect.Web.Tests.dll",
                "",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                cancellation.Token));

        handler.BeforeDownload = null;
        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0-auto-family-cancellation",
                "DotnetInspect.Web.Tests.dll",
                "",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        Assert.Equal("runtime", resolution.Coordinate.Family);
        Assert.True(
            BrowserPackageWorkspace.IsScopeRetained(resolution.Scope));
        Assert.Single(resolution.Scope.Members);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task PlatformWorkspace_UnknownFamilyReservesBeforeProbing(int protectedScopes)
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        var held = new List<BrowserScopeLease<BrowserInspectionScope>>();
        try
        {
            for (int index = 0; index < BrowserPackageWorkspace.MaxOpenScopes; index++)
            {
                BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
                    $"Artifact.PlatformCapacity.{Guid.NewGuid():N}",
                    Package(image, "lib/net11.0/DotnetInspect.Web.Tests.dll"),
                    TestContext.Current.CancellationToken);
                held.Add(await BrowserPackageWorkspace.OpenScopeAsync(
                    [coordinate],
                    TestContext.Current.CancellationToken));
            }
            if (protectedScopes < held.Count)
            {
                BrowserScopeLease<BrowserInspectionScope> released = held[^1];
                await BrowserPackageWorkspace.RemoveScopeAsync(released.Scope);
                await released.DisposeAsync();
                held.RemoveAt(held.Count - 1);
            }
            Assert.Equal(protectedScopes, BrowserPackageWorkspace.Stats().Workspaces);

            var observedCounts = new List<int>();
            var handler = new MultiplePlatformVersionHandler(
                $"11.0.12{protectedScopes}",
                new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["microsoft.netcore.app.runtime.linux-x64"] = PlatformPackage(
                        ("System.Private.CoreLib.dll",
                            File.ReadAllBytes(typeof(object).Assembly.Location))),
                    ["microsoft.aspnetcore.app.runtime.linux-x64"] = PlatformPackage(
                        ("DotnetInspect.Web.Tests.dll", image)),
                })
            {
                BeforeDownload = _ =>
                    observedCounts.Add(BrowserPackageWorkspace.Stats().Workspaces),
            };
            using var client = new HttpClient(handler);
            var authorization =
                new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);

            if (protectedScopes == BrowserPackageWorkspace.MaxOpenScopes)
            {
                InvalidOperationException error =
                    await Assert.ThrowsAsync<InvalidOperationException>(OpenAsync);
                Assert.Contains("cannot evict an active inspection", error.Message);
                Assert.Empty(observedCounts);
            }
            else
            {
                await using BrowserPlatformScopeResolution resolution = await OpenAsync();
                Assert.Equal("runtime", resolution.Coordinate.Family);
                Assert.Single(resolution.Scope.Members);
                Assert.Equal(2, observedCounts.Count);
                Assert.All(observedCounts, count =>
                    Assert.Equal(BrowserPackageWorkspace.MaxOpenScopes, count));
                Assert.Equal(
                    BrowserPackageWorkspace.MaxOpenScopes,
                    BrowserPackageWorkspace.Stats().Workspaces);
            }

            Task<BrowserPlatformScopeResolution> OpenAsync() =>
                BrowserPlatformWorkspace.OpenAssemblyAsync(
                    $"net11.0-auto-family-capacity-{protectedScopes}",
                    "System.Private.CoreLib.dll",
                    "",
                    client,
                    authorization,
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken);
        }
        finally
        {
            foreach (BrowserScopeLease<BrowserInspectionScope> lease in held)
                await lease.DisposeAsync();
        }
    }

    [Fact]
    public async Task PlatformWorkspace_UnknownFamilyRefusesMissingAssembly()
    {
        const string version = "11.0.102";
        byte[] runtimeNupkg = PlatformPackage(
            ("System.Private.CoreLib.dll",
                File.ReadAllBytes(typeof(object).Assembly.Location)));
        byte[] aspNetNupkg = PlatformPackage(
            ("Microsoft.AspNetCore.Http.dll",
                File.ReadAllBytes(
                    typeof(BrowserEngineBoundaryTests).Assembly.Location)));
        var handler = new MultiplePlatformVersionHandler(
            version,
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["microsoft.netcore.app.runtime.linux-x64"] = runtimeNupkg,
                ["microsoft.aspnetcore.app.runtime.linux-x64"] = aspNetNupkg,
            });
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPlatformWorkspace.OpenAssemblyAsync(
                    "net11.0-auto-family-missing",
                    "Missing.Platform.Assembly.dll",
                    "",
                    client,
                    authorization,
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "not carried by any supported platform family",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformWorkspace_UnknownFamilyRefusesAmbiguousAssembly()
    {
        const string version = "11.0.103";
        byte[] sharedImage =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] runtimeNupkg = PlatformPackage(
            ("DotnetInspect.Web.Tests.dll", sharedImage));
        byte[] aspNetNupkg = PlatformPackage(
            ("DotnetInspect.Web.Tests.dll", sharedImage));
        var handler = new MultiplePlatformVersionHandler(
            version,
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["microsoft.netcore.app.runtime.linux-x64"] = runtimeNupkg,
                ["microsoft.aspnetcore.app.runtime.linux-x64"] = aspNetNupkg,
            });
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPlatformWorkspace.OpenAssemblyAsync(
                    "net11.0-auto-family-ambiguous",
                    "DotnetInspect.Web.Tests.dll",
                    "",
                    client,
                    authorization,
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "more than one supported platform family",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformWorkspace_LatestSentinelUsesVersionDiscovery()
    {
        const string packageId =
            "microsoft.netcore.app.runtime.linux-x64";
        const string discoveredVersion = "11.0.75";
        byte[] nupkg = PlatformPackage(
            ("System.Private.CoreLib.dll",
                File.ReadAllBytes(typeof(object).Assembly.Location)));
        var handler = new PlatformVersionHandler(
            packageId,
            discoveredVersion,
            nupkg);
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);

        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                "net11.0-latest-platform-sentinel",
                "latest",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        Assert.Equal(discoveredVersion, resolution.Coordinate.Version);
    }

    [Fact]
    public async Task PlatformWorkspace_ExactVersionSkipsDiscoveryAndDoesNotReuseLatestState()
    {
        const string packageId =
            "microsoft.netcore.app.runtime.linux-x64";
        const string latestVersion = "11.0.76";
        const string exactVersion = "11.0.77";
        const string framework = "net11.0-exact-platform-version";
        byte[] nupkg = PlatformPackage(
            ("System.Private.CoreLib.dll",
                File.ReadAllBytes(typeof(object).Assembly.Location)));
        var latestHandler = new PlatformVersionHandler(
            packageId,
            latestVersion,
            nupkg);
        var exactHandler = new PlatformVersionHandler(
            packageId,
            exactVersion,
            nupkg);
        using var latestClient = new HttpClient(latestHandler);
        using var exactClient = new HttpClient(exactHandler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);

        await using BrowserPlatformScopeResolution latest =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                framework,
                latestClient,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        await using BrowserPlatformScopeResolution exact =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                framework,
                exactVersion,
                exactClient,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        Assert.Equal(latestVersion, latest.Coordinate.Version);
        Assert.Equal(exactVersion, exact.Coordinate.Version);
        Assert.NotSame(latest.Scope, exact.Scope);
        Assert.Equal(1, exactHandler.Requests);
    }

    [Fact]
    public async Task PlatformWorkspace_LeasesArchivesUntilCandidateRegistration()
    {
        const string runtimePackage =
            "microsoft.netcore.app.runtime.linux-x64";
        const string aspNetPackage =
            "microsoft.aspnetcore.app.runtime.linux-x64";
        const string version = "11.0.2";
        byte[] runtimeNupkg = PlatformPackage(
            ("System.Private.CoreLib.dll",
                File.ReadAllBytes(typeof(object).Assembly.Location)));
        byte[] aspNetNupkg = PlatformPackage(
            ("DotnetInspect.Web.Tests.dll",
                File.ReadAllBytes(
                    typeof(BrowserEngineBoundaryTests).Assembly.Location)));
        var handler = new MultiplePlatformVersionHandler(
            version,
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                [runtimePackage] = runtimeNupkg,
                [aspNetPackage] = aspNetNupkg,
            });
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);

        BrowserPlatformScopeResolution runtime =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                "net11.0-tvos",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        await runtime.DisposeAsync();
        await using BrowserPlatformScopeResolution initial =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0-tvos",
                "DotnetInspect.Web.Tests.dll",
                "aspnetcore.app",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        Assert.Equal(2, initial.Scope.Members.Length);
        await initial.DisposeAsync();

        using (await BrowserPackageWorkspace.ReservePackageDownloadAsync(
            "platform.lease.evict@1.0.0",
            128L * MiB))
        {
            Assert.False(
                BrowserPackageWorkspace.IsScopeRetained(initial.Scope));
        }

        int reacquisitionDownloads = 0;
        bool pressureBlocked = false;
        handler.BeforeDownloadAsync = async _ =>
        {
            reacquisitionDownloads++;
            if (reacquisitionDownloads != 2)
                return;

            try
            {
                using var pressure =
                    await BrowserPackageWorkspace.ReservePackageDownloadAsync(
                        "platform.lease.pressure@1.0.0",
                        128L * MiB);
            }
            catch (InvalidOperationException exception)
                when (exception.Message.Contains(
                    "cannot accommodate",
                    StringComparison.OrdinalIgnoreCase))
            {
                pressureBlocked = true;
            }
        };

        await using BrowserPlatformScopeResolution reacquired =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0-tvos",
                "DotnetInspect.Web.Tests.dll",
                "aspnetcore.app",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        Assert.Equal(2, reacquisitionDownloads);
        Assert.True(pressureBlocked);
        Assert.Equal(2, reacquired.Scope.Members.Length);
        Assert.Equal(
            "netcore.app",
            reacquired.Scope.PlatformPackForAssembly(
                "System.Private.CoreLib"));
        Assert.Equal(
            "aspnetcore.app",
            reacquired.Scope.PlatformPackForAssembly(
                "DotnetInspect.Web.Tests"));
    }

    [Fact]
    public async Task PlatformWorkspace_ReplacementDefersDisposalUntilLastLeaseEnds()
    {
        const string packageId =
            "microsoft.netcore.app.runtime.linux-x64";
        const string version = "11.0.3";
        byte[] nupkg = PlatformPackage(
            ("System.Private.CoreLib.dll",
                File.ReadAllBytes(typeof(object).Assembly.Location)),
            ("DotnetInspect.Web.Tests.dll",
                File.ReadAllBytes(
                    typeof(BrowserEngineBoundaryTests).Assembly.Location)));
        var handler = new PlatformVersionHandler(
            packageId,
            version,
            nupkg);
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);

        await using BrowserPlatformScopeResolution first =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                "net11.0-lease-replacement",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        await using BrowserPlatformScopeResolution secondLease =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                "net11.0-lease-replacement",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        await using BrowserPlatformScopeResolution replacement =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0-lease-replacement",
                "DotnetInspect.Web.Tests.dll",
                "netcore.app",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        Assert.NotSame(first.Scope, replacement.Scope);
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(first.Scope));
        Assert.Single(first.Scope.Members);

        await first.DisposeAsync();
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(secondLease.Scope));
        Assert.Single(secondLease.Scope.Members);

        await secondLease.DisposeAsync();
        Assert.False(BrowserPackageWorkspace.IsScopeRetained(first.Scope));
        Assert.Throws<ObjectDisposedException>(() => first.Scope.Members);

        await using BrowserPlatformScopeResolution reused =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                "net11.0-lease-replacement",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        Assert.Same(replacement.Scope, reused.Scope);
        Assert.Equal(2, reused.Scope.Members.Length);

        await replacement.DisposeAsync();
    }

    [Fact]
    public async Task PlatformWorkspace_BatchesCumulativeAssemblyExpansion()
    {
        const string packageId =
            "microsoft.netcore.app.runtime.linux-x64";
        const string version = "11.0.5";
        byte[] nupkg = PlatformPackage(
            ("System.Private.CoreLib.dll",
                File.ReadAllBytes(typeof(object).Assembly.Location)),
            ("DotnetInspect.Web.Tests.dll",
                File.ReadAllBytes(
                    typeof(BrowserEngineBoundaryTests).Assembly.Location)),
            ("System.Data.Common.dll",
                File.ReadAllBytes(
                    typeof(System.Data.Common.DbDataSource)
                        .Assembly.Location)));
        var handler = new PlatformVersionHandler(
            packageId,
            version,
            nupkg);
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);

        await using BrowserPlatformScopeResolution initial =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                "net11.0-platform-batch",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        await using BrowserPlatformScopeResolution expanded =
            await BrowserPlatformWorkspace.OpenAssembliesAsync(
                "net11.0-platform-batch",
                [
                    new(
                        "DotnetInspect.Web.Tests.dll",
                        "netcore.app"),
                    new(
                        "System.Data.Common.dll",
                        "netcore.app"),
                ],
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        Assert.Equal(3, expanded.Scope.Members.Length);
        Assert.Equal(
            "System.Data.Common",
            expanded.Participant.Participant.Assembly.Identity.Name);
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(initial.Scope));
        await initial.DisposeAsync();
        Assert.False(BrowserPackageWorkspace.IsScopeRetained(initial.Scope));
    }

    [Fact]
    public async Task PlatformWorkspace_ReplacesOneNameAcrossPackFamiliesButRejectsBatch()
    {
        const string version = "11.0.6";
        byte[] package = PlatformPackage(
            ("Shared.dll",
                File.ReadAllBytes(
                    typeof(BrowserEngineBoundaryTests).Assembly.Location)));
        var handler = new MultiplePlatformVersionHandler(
            version,
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["microsoft.netcore.app.runtime.linux-x64"] = package,
                ["microsoft.aspnetcore.app.runtime.linux-x64"] = package,
            });
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);

        await using BrowserPlatformScopeResolution runtime =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0-platform-family-collision",
                "DotnetInspect.Web.Tests.dll",
                "netcore.app",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        await using BrowserPlatformScopeResolution aspnet =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0-platform-family-collision",
                "DotnetInspect.Web.Tests.dll",
                "aspnetcore.app",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            "aspnetcore.app",
            BrowserPlatformWorkspace.Pack(aspnet.Coordinate.Family));
        Assert.Single(aspnet.Scope.Members);
        Assert.Single(runtime.Scope.Members);
        Assert.Same(
            runtime.Participant,
            runtime.Scope.Participant(
                runtime.Coordinate.Family,
                "DotnetInspect.Web.Tests"));
        BrowserPackageSurface runtimeSurface =
            Assert.IsType<BrowserPackageSurface>(
                JsonSerializer.Deserialize(
                    DotnetInspect.Web.Interop.Package.PackageExports.ProjectPlatformSurface(
                        runtime,
                        "Shared.dll"),
                    BrowserPackageJsonContext.Default.BrowserPackageSurface));
        BrowserPackageSurface aspnetSurface =
            Assert.IsType<BrowserPackageSurface>(
                JsonSerializer.Deserialize(
                    DotnetInspect.Web.Interop.Package.PackageExports.ProjectPlatformSurface(
                        aspnet,
                        "Shared.dll"),
                    BrowserPackageJsonContext.Default.BrowserPackageSurface));
        Assert.Equal(
            "netcore.app",
            Assert.Single(runtimeSurface.Assemblies).PlatformPack);
        Assert.Equal(
            "aspnetcore.app",
            Assert.Single(aspnetSurface.Assemblies).PlatformPack);

        bool downloaded = false;
        handler.BeforeDownload = _ => downloaded = true;
        InvalidOperationException batchFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPlatformWorkspace.OpenAssembliesAsync(
                    "net11.0-platform-family-batch-collision",
                    [
                        new(
                            "DotnetInspect.Web.Tests.dll",
                            "netcore.app"),
                        new(
                            "DotnetInspect.Web.Tests.dll",
                            "aspnetcore.app"),
                    ],
                    client,
                    authorization,
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken));

        Assert.Contains("selected from both", batchFailure.Message);
        Assert.False(downloaded);
    }

    [Fact]
    public async Task PlatformWorkspace_EvictionRemovesRetainedTargetState()
    {
        const string packageId =
            "microsoft.netcore.app.runtime.linux-x64";
        const string version = "11.0.4";
        byte[] nupkg = PlatformPackage(
            ("System.Private.CoreLib.dll",
                File.ReadAllBytes(typeof(object).Assembly.Location)));
        var handler = new PlatformVersionHandler(
            packageId,
            version,
            nupkg);
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);
        string[] frameworks =
        [
            "net11.0-platform-retention-a",
            "net11.0-platform-retention-b",
            "net11.0-platform-retention-c",
            "net11.0-platform-retention-d",
            "net11.0-platform-retention-e",
        ];

        foreach (string framework in frameworks)
        {
            await using BrowserPlatformScopeResolution resolution =
                await BrowserPlatformWorkspace.OpenRuntimeAsync(
                    framework,
                    client,
                    authorization,
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken);
        }

        var targets = Assert.IsAssignableFrom<System.Collections.IDictionary>(
            typeof(BrowserPlatformWorkspace)
                .GetField(
                    "Targets",
                    BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null));
        Assert.False(targets.Contains($"{frameworks[0]}@latest"));
        Assert.True(targets.Contains($"{frameworks[^1]}@latest"));
    }

    [Fact]
    public async Task PlatformWorkspace_UnknownFamilyProbePinsCumulativeState()
    {
        const string version = "11.0.2601";
        const string runtimePackage =
            "microsoft.netcore.app.runtime.linux-x64";
        const string aspNetPackage =
            "microsoft.aspnetcore.app.runtime.linux-x64";
        byte[] runtimeNupkg = PlatformPackage(
            ("System.Private.CoreLib.dll",
                File.ReadAllBytes(typeof(object).Assembly.Location)));
        byte[] aspNetNupkg = PlatformPackage(
            ("DotnetInspect.Web.Tests.dll",
                File.ReadAllBytes(
                    typeof(BrowserEngineBoundaryTests).Assembly.Location)));
        var handler = new MultiplePlatformVersionHandler(
            version,
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                [runtimePackage] = runtimeNupkg,
                [aspNetPackage] = aspNetNupkg,
            });
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);
        string[] frameworks =
        [
            "net11.0-r26-retention-a",
            "net11.0-r26-retention-b",
            "net11.0-r26-retention-c",
            "net11.0-r26-retention-d",
            "net11.0-r26-retention-e",
        ];

        foreach (string framework in frameworks[..4])
        {
            await using BrowserPlatformScopeResolution resolution =
                await BrowserPlatformWorkspace.OpenRuntimeAsync(
                    framework,
                    client,
                    authorization,
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken);
        }

        var aspNetDownloadStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var continueAspNetDownload = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        handler.BeforeDownloadAsync = async package =>
        {
            if (!package.Equals(
                    aspNetPackage,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            aspNetDownloadStarted.TrySetResult();
            await continueAspNetDownload.Task.WaitAsync(
                TestContext.Current.CancellationToken);
        };
        Task<BrowserPlatformScopeResolution> expansion =
            BrowserPlatformWorkspace.OpenAssemblyAsync(
                frameworks[0],
                "DotnetInspect.Web.Tests.dll",
                "",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        try
        {
            await aspNetDownloadStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            await using BrowserPlatformScopeResolution competing =
                await BrowserPlatformWorkspace.OpenRuntimeAsync(
                    frameworks[4],
                    client,
                    authorization,
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken);
            var targets =
                Assert.IsAssignableFrom<System.Collections.IDictionary>(
                    typeof(BrowserPlatformWorkspace)
                        .GetField(
                            "Targets",
                            BindingFlags.Static | BindingFlags.NonPublic)!
                        .GetValue(null));
            Assert.True(targets.Contains($"{frameworks[0]}@latest"));
            Assert.False(targets.Contains($"{frameworks[1]}@latest"));
        }
        finally
        {
            continueAspNetDownload.TrySetResult();
        }

        await using BrowserPlatformScopeResolution expanded = await expansion;
        Assert.Equal(
            "netcore.app",
            expanded.Scope.PlatformPackForAssembly(
                "System.Private.CoreLib"));
        Assert.Equal(
            "aspnetcore.app",
            expanded.Scope.PlatformPackForAssembly(
                "DotnetInspect.Web.Tests"));
        Assert.Equal(2, expanded.Scope.Members.Length);
    }

    [Fact]
    public async Task PlatformWorkspace_FailedUnknownFamilyProbePreservesCumulativeState()
    {
        const string version = "11.0.2701";
        const string runtimePackage =
            "microsoft.netcore.app.runtime.linux-x64";
        const string aspNetPackage =
            "microsoft.aspnetcore.app.runtime.linux-x64";
        byte[] runtimeNupkg = PlatformPackage(
            ("System.Private.CoreLib.dll",
                File.ReadAllBytes(typeof(object).Assembly.Location)),
            ("DotnetInspect.Web.Tests.dll",
                File.ReadAllBytes(
                    typeof(BrowserEngineBoundaryTests).Assembly.Location)));
        byte[] aspNetNupkg = PlatformPackage(
            ("Microsoft.AspNetCore.Http.dll",
                File.ReadAllBytes(typeof(object).Assembly.Location)));
        var handler = new MultiplePlatformVersionHandler(
            version,
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                [runtimePackage] = runtimeNupkg,
                [aspNetPackage] = aspNetNupkg,
            });
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);
        string[] frameworks =
        [
            "net11.0-r27-failure-a",
            "net11.0-r27-failure-b",
            "net11.0-r27-failure-c",
            "net11.0-r27-failure-d",
            "net11.0-r27-failure-e",
        ];

        await using (BrowserPlatformScopeResolution runtime =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                frameworks[0],
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken))
        {
        }
        await using (BrowserPlatformScopeResolution second =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                frameworks[0],
                "DotnetInspect.Web.Tests.dll",
                "netcore.app",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken))
        {
            Assert.Equal(2, second.Scope.Members.Length);
        }
        foreach (string framework in frameworks[1..4])
        {
            await using BrowserPlatformScopeResolution resolution =
                await BrowserPlatformWorkspace.OpenRuntimeAsync(
                    framework,
                    client,
                    authorization,
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken);
        }

        var aspNetDownloadStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var continueAspNetDownload = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        handler.BeforeDownloadAsync = async package =>
        {
            if (!package.Equals(
                    aspNetPackage,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            aspNetDownloadStarted.TrySetResult();
            await continueAspNetDownload.Task.WaitAsync(
                TestContext.Current.CancellationToken);
        };
        Task<BrowserPlatformScopeResolution> missing =
            BrowserPlatformWorkspace.OpenAssemblyAsync(
                frameworks[0],
                "Missing.Platform.Assembly.dll",
                "",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        try
        {
            await aspNetDownloadStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            await using BrowserPlatformScopeResolution competing =
                await BrowserPlatformWorkspace.OpenRuntimeAsync(
                    frameworks[4],
                    client,
                    authorization,
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken);
        }
        finally
        {
            continueAspNetDownload.TrySetResult();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await missing);
        await using BrowserPlatformScopeResolution reopened =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                frameworks[0],
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        Assert.Equal(2, reopened.Scope.Members.Length);
        Assert.Equal(
            "netcore.app",
            reopened.Scope.PlatformPackForAssembly(
                "DotnetInspect.Web.Tests"));
    }

    [Theory]
    [InlineData("https://raw.githubusercontent.com/org/repo/commit/A.cs", true)]
    [InlineData("https://dev.azure.com/org/project/_apis/git/A.cs", true)]
    [InlineData("https://org.visualstudio.com/project/_apis/git/A.cs", true)]
    [InlineData("https://api.bitbucket.org/2.0/repositories/org/repo/src/commit/A.cs", true)]
    [InlineData("https://localhost/A.cs", false)]
    [InlineData("https://127.0.0.1/A.cs", false)]
    [InlineData("https://example.com/A.cs", false)]
    [InlineData("http://raw.githubusercontent.com/org/repo/commit/A.cs", false)]
    public void SourceFetchPolicy_AuthorizesBeforeDispatch(
        string url,
        bool expected)
    {
        Assert.Equal(
            expected,
            BrowserSourceFetchPolicy.Instance.IsRequestAllowed(
                new Uri(url)));
    }

    [Fact]
    public async Task PlatformOpportunities_CarryExactSourceIdentity()
    {
        const string packageId =
            "microsoft.netcore.app.runtime.linux-x64";
        const string version = "11.0.98";
        const string framework = "net11.0-opportunity-identity";
        byte[] nupkg = PlatformPackage(
            ("System.Data.Common.dll",
                File.ReadAllBytes(
                    typeof(System.Data.Common.DbDataSource)
                        .Assembly.Location)));
        var handler = new PlatformVersionHandler(
            packageId,
            version,
            nupkg);
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);

        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                framework,
                "System.Data.Common.dll",
                "netcore.app",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        BrowserPackageSurface surface =
            Assert.IsType<BrowserPackageSurface>(
                JsonSerializer.Deserialize(
                    DotnetInspect.Web.Interop.Package.PackageExports.ProjectPlatformSurface(
                        resolution),
                    BrowserPackageJsonContext.Default.BrowserPackageSurface));
        BrowserPackageOpportunities opportunities =
            Assert.IsType<BrowserPackageOpportunities>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Analysis.AnalysisExports
                        .QueryPlatformOpportunities(
                            framework,
                            "System.Data.Common.dll",
                            "netcore.app"),
                    BrowserAnalysisJsonContext.Default
                        .BrowserPackageOpportunities));

        BrowserAssemblySurface assembly =
            Assert.Single(surface.Assemblies);
        BrowserOpportunityItem[] items =
            [.. opportunities.Categories
                .SelectMany(category => category.Items)];
        Assert.NotEmpty(items);
        Assert.All(
            items,
            item =>
            {
                BrowserTypeSurface type = Assert.Single(
                    surface.Types,
                    candidate =>
                        candidate.DefinitionId
                            == item.SourceDefinitionId);
                Assert.Equal(
                    type.DefinitionId,
                    item.SourceDefinitionId);
                Assert.Equal(assembly.Name, item.SourceAssembly);
                Assert.Equal(
                    assembly.Version,
                    item.SourceAssemblyVersion);
                Assert.Equal(
                    assembly.Culture,
                    item.SourceAssemblyCulture);
                Assert.Equal(
                    assembly.PublicKeyToken,
                    item.SourceAssemblyPublicKeyToken);
            });
    }

    [Theory]
    [InlineData("System.Runtime.dll", "unknown.app")]
    [InlineData("../System.Runtime.dll", "netcore.app")]
    [InlineData("System.Runtime", "netcore.app")]
    public async Task PlatformWorkspace_RejectsInvalidSelectionsBeforeNetwork(
        string assemblyFileName,
        string pack)
    {
        var handler = new PlatformVersionHandler(
            "microsoft.netcore.app.runtime.linux-x64",
            "11.0.0");
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0",
                assemblyFileName,
                pack,
                client,
                new UniformPackageSourceAuthorization(
                    [PackageSource.NuGetOrg]),
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public void PlatformWorkspace_RejectsAssemblyCountAboveBrowserBound()
    {
        BrowserPlatformWorkspace.EnsureAssemblyCapacity(
            BrowserInspectionScope.MaxAssembliesPerRole);

        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(
                () => BrowserPlatformWorkspace.EnsureAssemblyCapacity(
                    BrowserInspectionScope.MaxAssembliesPerRole + 1));

        Assert.Contains(
            "assembly-count limit",
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformWorkspace_ReuseTouchesTheSharedScopeLru()
    {
        const string packageId =
            "microsoft.netcore.app.runtime.linux-x64";
        const string version = "11.0.1";
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] platformNupkg = PlatformPackage(
            ("System.Private.CoreLib.dll",
                File.ReadAllBytes(typeof(object).Assembly.Location)));
        var handler = new PlatformVersionHandler(
            packageId,
            version,
            platformNupkg);
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);
        await using BrowserPlatformScopeResolution platform =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                "net11.0-android",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        BrowserScopeLease<BrowserInspectionScope> firstPackageLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
            [await Coordinate(
                "Platform.Lru.A",
                Package(image, "lib/net11.0/Platform.Lru.A.dll"))],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope firstPackage = firstPackageLease.Scope;
        BrowserScopeLease<BrowserInspectionScope> secondPackageLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
            [await Coordinate(
                "Platform.Lru.B",
                Package(image, "lib/net11.0/Platform.Lru.B.dll"))],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope secondPackage = secondPackageLease.Scope;
        BrowserScopeLease<BrowserInspectionScope> thirdPackageLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
            [await Coordinate(
                "Platform.Lru.C",
                Package(image, "lib/net11.0/Platform.Lru.C.dll"))],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope thirdPackage = thirdPackageLease.Scope;

        BrowserPlatformScopeResolution reused =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                "net11.0-android",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        Assert.Same(platform.Scope, reused.Scope);

        // Every protected use is released before the fifth workspace asks for a slot, so the
        // eviction is decided by the shared recency order alone — and reusing the platform
        // workspace moved it out of the victim position.
        await reused.DisposeAsync();
        await platform.DisposeAsync();
        await firstPackageLease.DisposeAsync();
        await secondPackageLease.DisposeAsync();
        await thirdPackageLease.DisposeAsync();

        await using BrowserScopeLease<BrowserInspectionScope> fourthPackageLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
            [await Coordinate(
                "Platform.Lru.D",
                Package(image, "lib/net11.0/Platform.Lru.D.dll"))],
                TestContext.Current.CancellationToken);

        Assert.True(
            BrowserPackageWorkspace.IsScopeRetained(platform.Scope));
        Assert.False(
            BrowserPackageWorkspace.IsScopeRetained(firstPackage));
        Assert.True(
            BrowserPackageWorkspace.IsScopeRetained(secondPackage));
        Assert.True(
            BrowserPackageWorkspace.IsScopeRetained(thirdPackage));
    }

    [Fact]
    public async Task PlatformWorkspace_CanceledQueueEntryPreservesSerialization()
    {
        string key = $"cancellation-{Guid.NewGuid():N}";
        var firstGate =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdStarted =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> first = BrowserPlatformWorkspace.EnqueueAsync(
            key,
            async () =>
            {
                firstStarted.SetResult();
                await firstGate.Task;
                return 1;
            },
            CancellationToken.None);
        await firstStarted.Task;

        using var cancellation = new CancellationTokenSource();
        Task<int> second = BrowserPlatformWorkspace.EnqueueAsync(
            key,
            () => Task.FromResult(2),
            cancellation.Token);
        Task<int> third = BrowserPlatformWorkspace.EnqueueAsync(
            key,
            () =>
            {
                thirdStarted.SetResult();
                return Task.FromResult(3);
            },
            CancellationToken.None);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => second);
        Assert.False(thirdStarted.Task.IsCompleted);

        firstGate.SetResult();
        Assert.Equal(1, await first);
        Assert.Equal(3, await third);
    }

    [Fact]
    public async Task ReusedCompositeScope_PreservesTheCurrentRequestedRoot()
    {
        byte[] firstImage =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] secondImage =
            File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location);
        _ = await Coordinate(
            "Root.Order.A",
            Package(firstImage, "lib/net11.0/Root.Order.A.dll"));
        _ = await Coordinate(
            "Root.Order.B",
            Package(secondImage, "lib/net11.0/Root.Order.B.dll"));

        await using BrowserScopeResolution first = await BrowserPackageWorkspace.ResolveAndOpenScopeAsync(
        [
            new BrowserPackageRequest("Root.Order.A", "1.0.0", "net11.0"),
            new BrowserPackageRequest("Root.Order.B", "1.0.0", "net11.0"),
        ],
        TestContext.Current.CancellationToken);
        await using BrowserScopeResolution second = await BrowserPackageWorkspace.ResolveAndOpenScopeAsync(
        [
            new BrowserPackageRequest("Root.Order.B", "1.0.0", "net11.0"),
            new BrowserPackageRequest("Root.Order.A", "1.0.0", "net11.0"),
        ],
        TestContext.Current.CancellationToken);

        Assert.Same(first.Scope, second.Scope);
        BrowserPackageCoordinate requestedRoot = second.RequestedCoordinates[0];
        Assert.Equal("Root.Order.B", requestedRoot.PackageId);
        Assert.Equal("Root.Order.B", second.Scope.Coordinate(requestedRoot).PackageId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HomeDemo_ReleasesScopeAfterQuery(bool missingType)
    {
        byte[] image = missingType
            ? BuildIntegrationImage(
                "System.Text.Json",
                "Example.Missing")
            : File.ReadAllBytes(typeof(JsonSerializer).Assembly.Location);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                "microsoft.netcore.app.runtime.linux-x64",
                "10.0.12",
                PlatformPackage(
                    "net10.0",
                    ("System.Text.Json.dll", image)),
                fromCache: false));

        if (missingType)
        {
            InvalidOperationException failure =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => DotnetInspect.Web.Interop.Catalog.CatalogExports.RunHomeDemo(ProductDemoIds.StjSerializer));
            Assert.Contains(
                "resolved to 0 Browser Platform surface rows",
                failure.Message,
                StringComparison.Ordinal);
        }
        else
        {
            BrowserHomeDemoRunResult result =
                Assert.IsType<BrowserHomeDemoRunResult>(
                    JsonSerializer.Deserialize(
                        await DotnetInspect.Web.Interop.Catalog.CatalogExports.RunHomeDemo(ProductDemoIds.StjSerializer),
                        BrowserCatalogJsonContext.Default.BrowserHomeDemoRunResult));
            Assert.True(result.Found);
            Assert.Equal(
                BrowserPlatformIdentity.PackageName,
                Assert.Single(result.Packages).Package);
            BrowserHomeDemoRunActivation activation =
                Assert.IsType<BrowserHomeDemoRunActivation>(
                    result.Activation);
            Assert.Equal("platform", activation.FocusKind);
            Assert.Equal("runtime", activation.FocusId);
        }

        using var pressure =
            await BrowserPackageWorkspace.ReservePackageDownloadAsync(
                $"home-demo.after-query.{Guid.NewGuid():N}@1.0.0",
                128L * MiB);
        Assert.Equal(
            128L * MiB,
            BrowserPackageWorkspace.Stats().ResidentBytes);
    }

    [Fact]
    public async Task PlatformHomeDemo_DefinitionAndProgrammaticPlansReturnSameMethods()
    {
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                "microsoft.netcore.app.runtime.linux-x64",
                "10.0.12",
                PlatformPackage(
                    "net10.0",
                    ("System.Text.Json.dll",
                        File.ReadAllBytes(
                            typeof(JsonSerializer).Assembly.Location))),
                fromCache: false));
        ResolvedScenario scenario =
            Assert.IsType<EcosystemDemoSelectionResult.Known>(
                EcosystemPackCatalog.SelectDemo(
                    ProductDemoIds.StjSerializer))
                .Selection.Scenario;
        BrowserHomeDemoRunPlan documentPlan =
            BrowserProductHomeDemos.ToRunPlan(scenario);
        WorkspacePlan programmaticWorkspacePlan = CreateStjPlan();
        BrowserHomeDemoRunPlan programmaticPlan = documentPlan with
        {
            WorkspacePlan = programmaticWorkspacePlan,
            ContextInput = programmaticWorkspacePlan.Contexts[1],
        };

        BrowserHomeDemoRunResult document =
            await DotnetInspect.Web.Interop.Catalog.CatalogExports
                .RunPlatformHomeDemoAsync(documentPlan);
        BrowserHomeDemoRunResult programmatic =
            await DotnetInspect.Web.Interop.Catalog.CatalogExports
                .RunPlatformHomeDemoAsync(programmaticPlan);
        Assert.NotNull(document.Activation!.PlatformContextId);
        Assert.NotNull(programmatic.Activation!.PlatformContextId);
        Assert.NotEqual(
            document.Activation.PlatformContextId,
            programmatic.Activation.PlatformContextId);

        string documentJson = JsonSerializer.Serialize(
            document with
            {
                Activation = document.Activation with { PlatformContextId = null },
            },
            BrowserCatalogJsonContext.Default.BrowserHomeDemoRunResult);
        string programmaticJson = JsonSerializer.Serialize(
            programmatic with
            {
                Activation = programmatic.Activation with { PlatformContextId = null },
            },
            BrowserCatalogJsonContext.Default.BrowserHomeDemoRunResult);
        Assert.Contains(
            "System.Text.Json.JsonSerializer",
            documentJson,
            StringComparison.Ordinal);
        Assert.Equal(documentJson, programmaticJson);
    }

    [Fact]
    public async Task PlatformHomeDemo_RequiresPlanRetainedContextInput()
    {
        WorkspacePlan plan = CreateStjPlan();
        WorkspaceContextInput detachedCopy = plan.Contexts[1] with { };

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => BrowserPlatformWorkspace.OpenContextAsync(
                plan,
                detachedCopy,
                "runtime",
                "System.Text.Json",
                TestContext.Current.CancellationToken));

        Assert.Contains(
            "exact input retained by the Workspace plan",
            error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "net9.0",
        null,
        WorkspaceContextLoadFailureKind.ConflictingAcquisitionTarget)]
    [InlineData(
        "net10.0",
        "LINUX-X64",
        WorkspaceContextLoadFailureKind.InvalidCoordinate)]
    public async Task PlatformHomeDemo_PreservesPlanTargetFailures(
        string framework,
        string? runtimeIdentifier,
        WorkspaceContextLoadFailureKind failureKind)
    {
        WorkspacePlan workspacePlan =
            CreateStjPlan(framework, runtimeIdentifier);
        var handler = new PlatformVersionHandler(
            "microsoft.netcore.app.runtime.linux-x64",
            "10.0.12");
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPlatformWorkspace.OpenContextAsync(
                    workspacePlan,
                    workspacePlan.Contexts[1],
                    "runtime",
                    "System.Text.Json",
                    client,
                    authorization,
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            failureKind.ToString(),
            error.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, handler.Requests);
    }

    [Theory]
    [InlineData("net9.0", null, "10.0.9876",
        WorkspaceContextLoadFailureKind.ConflictingAcquisitionTarget)]
    [InlineData("net10.0", "LINUX-X64", "10.0.9876",
        WorkspaceContextLoadFailureKind.InvalidCoordinate)]
    [InlineData("net10.0", null, "10.0.not-a-version",
        WorkspaceContextLoadFailureKind.InvalidCoordinate)]
    public async Task PlatformHomeDemo_ProductionValidatesBeforeAcquisition(
        string framework,
        string? runtimeIdentifier,
        string version,
        WorkspaceContextLoadFailureKind failureKind)
    {
        Assert.Null(BrowserPackageWorkspace.SessionPackageStore.TryGetCached(
            "microsoft.netcore.app.runtime.linux-x64", version, null));
        var plan = new WorkspacePlan(
            [],
            [
                new WorkspaceContextInput
                {
                    Framework = framework,
                    RuntimeIdentifier = runtimeIdentifier,
                    Members = [WorkspaceMemberCoordinate.Platform(
                        "runtime", "System.Text.Json", version, "net10.0")],
                },
            ]);

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPlatformWorkspace.OpenContextAsync(
                    plan,
                    plan.Contexts[0],
                    "runtime",
                    "System.Text.Json",
                    TestContext.Current.CancellationToken));

        Assert.Contains(failureKind.ToString(), failure.Message, StringComparison.Ordinal);
        Assert.Null(BrowserPackageWorkspace.SessionPackageStore.TryGetCached(
            "microsoft.netcore.app.runtime.linux-x64", version, null));
    }

    [Fact]
    public async Task PlatformHomeDemo_PreservesCumulativeStateAndIndependentPlanRealizations()
    {
        const string packageId =
            "microsoft.netcore.app.runtime.linux-x64";
        const string version = "11.0.107";
        const string framework = "net11.0-platform-home-demo-independent";
        byte[] nupkg = PlatformPackage(
            ("DotnetInspect.Web.Tests.dll",
                File.ReadAllBytes(
                    typeof(BrowserEngineBoundaryTests).Assembly.Location)),
            ("System.Private.CoreLib.dll",
                File.ReadAllBytes(typeof(object).Assembly.Location)));
        var handler = new PlatformVersionHandler(
            packageId,
            version,
            nupkg);
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);
        WorkspacePlan firstPlan = PlatformTestPlan();
        WorkspacePlan secondPlan = PlatformTestPlan();
        BrowserPlatformScope secondScope;
        BrowserPlatformScope cumulativeScope;
        await using (BrowserPlatformScopeResolution cumulative =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                framework,
                version,
                "System.Private.CoreLib.dll",
                "netcore.app",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken))
        {
            cumulativeScope = cumulative.Scope;
        }
        await using BrowserScopeLease<BrowserPlatformScope> cumulativePin =
            BrowserPackageWorkspace.LeaseScope(cumulativeScope);

        await using (BrowserPlatformScopeResolution first =
            await BrowserPlatformWorkspace.OpenContextAsync(
                firstPlan,
                firstPlan.Contexts[0],
                "runtime",
                "DotnetInspect.Web.Tests",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken))
        await using (BrowserPlatformScopeResolution second =
            await BrowserPlatformWorkspace.OpenContextAsync(
                secondPlan,
                secondPlan.Contexts[0],
                "runtime",
                "DotnetInspect.Web.Tests",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken))
        {
            Assert.NotSame(first.Scope, second.Scope);
            Assert.NotSame(firstPlan, secondPlan);
            Assert.Single(first.Scope.Members);
            Assert.Single(second.Scope.Members);
            secondScope = second.Scope;
        }

        await using BrowserPlatformScopeResolution retainedCumulative =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                framework,
                version,
                "System.Private.CoreLib.dll",
                "netcore.app",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        Assert.Same(cumulativeScope, retainedCumulative.Scope);
        await BrowserPackageWorkspace.RemoveScopeAsync(secondScope);

        WorkspacePlan PlatformTestPlan() =>
            new(
                [],
                [
                    new WorkspaceContextInput
                    {
                        Framework = framework,
                        Members = [WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            "DotnetInspect.Web.Tests",
                            version,
                            framework)],
                    },
                ]);
    }

    [Fact]
    public void PackageArchiveEntryFlood_IsRejectedBeforeArchiveEnumeration()
    {
        const int maxEntries = 4_096;
        _ = new BrowserPackage(
            "Entry.Limit",
            "1.0.0",
            PackageEntries(maxEntries),
            fromCache: false);
        byte[] nupkg = PackageEntries(maxEntries + 1);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => new BrowserPackage("Entry.Flood", "1.0.0", nupkg, fromCache: false));

        Assert.Contains("more than 4096 entries", failure.Message, StringComparison.Ordinal);
    }
}
