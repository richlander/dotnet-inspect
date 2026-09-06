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
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Decompiler;
using ILInspector.Findings;
using ILInspector.Metadata;
using NuGetFetch;

using InspectWeb.Engine.PackageFacade;
using BrowserMetadataJsonContext = InspectWeb.Engine.MetadataFacade.BrowserMetadataJsonContext;
using BrowserAnalysisJsonContext = InspectWeb.Engine.AnalysisFacade.BrowserAnalysisJsonContext;
using BrowserSourceJsonContext = InspectWeb.Engine.SourceFacade.BrowserSourceJsonContext;
using BrowserCallGraphJsonContext = InspectWeb.Engine.CallGraphFacade.BrowserCallGraphJsonContext;
using BrowserCatalogJsonContext = InspectWeb.Engine.CatalogFacade.BrowserCatalogJsonContext;
using BrowserPackageMetadata = InspectWeb.Engine.MetadataFacade.BrowserPackageMetadata;
using BrowserMetadataCompileLibraryStatus = InspectWeb.Engine.MetadataFacade.BrowserCompileLibraryStatus;
using BrowserAnalysisCompileLibraryStatus = InspectWeb.Engine.AnalysisFacade.BrowserCompileLibraryStatus;
using BrowserPackageIntegrations = InspectWeb.Engine.AnalysisFacade.BrowserPackageIntegrations;
using BrowserPackageOpportunities = InspectWeb.Engine.AnalysisFacade.BrowserPackageOpportunities;
using BrowserPackagePerformance = InspectWeb.Engine.AnalysisFacade.BrowserPackagePerformance;
using BrowserPerformanceMember = InspectWeb.Engine.AnalysisFacade.BrowserPerformanceMember;
using BrowserOpportunityItem = InspectWeb.Engine.AnalysisFacade.BrowserOpportunityItem;
using BrowserSource = InspectWeb.Engine.SourceFacade.BrowserSource;
using BrowserCallGraph = InspectWeb.Engine.CallGraphFacade.BrowserCallGraph;
using BrowserCallGraphTarget = InspectWeb.Engine.CallGraphFacade.BrowserCallGraphTarget;
using BrowserCallGraphWireProjection = InspectWeb.Engine.CallGraphFacade.BrowserCallGraphWireProjection;
using BrowserCallGraphDiagnostics = InspectWeb.Engine.CallGraphFacade.BrowserCallGraphDiagnostics;
using BrowserHomeDemoRunResult = InspectWeb.Engine.CatalogFacade.BrowserHomeDemoRunResult;
using BrowserHomeDemoRunActivation = InspectWeb.Engine.CatalogFacade.BrowserHomeDemoRunActivation;
using BrowserHomeDemoRunPlan = InspectWeb.Engine.CatalogFacade.BrowserHomeDemoRunPlan;
using BrowserHomeDemoRunMember = InspectWeb.Engine.CatalogFacade.BrowserHomeDemoRunMember;

namespace InspectWeb.Engine.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserEngineBoundaryTests
{
    const int MiB = 1024 * 1024;

    [Fact]
    public void PackageManifestFacts_FromInMemoryBytesRemainBrowserCompatible()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(
                "Example.Package",
                "1.0.0");
        byte[] manifestBytes = Encoding.UTF8.GetBytes(
            """
            <package>
              <metadata>
                <id>Example.Package</id>
                <version>1.0.0</version>
                <dependencies>
                  <dependency id="Example.Dependency" version="[2.0.0]" />
                </dependencies>
              </metadata>
            </package>
            """);

        PackageManifestFacts facts = Assert.IsType<
            PackageManifestFactsResult.Available>(
                PackageManifestFactsQuery.Execute(
                    manifestBytes,
                    coordinate)).Value;

        Assert.Equal(coordinate, facts.Coordinate);
        Assert.Equal(
            "Example.Dependency",
            Assert.Single(
                Assert.Single(facts.DependencyGroups).Dependencies).Id);
    }

    [Fact]
    public void PackageQueryPlanner_IsReachableFromBrowserConsumer()
    {
        PackageQueryPlan plan = Assert.IsType<PackageQueryPlanResult.Accepted>(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Example.",
                    [
                        PackageQuery.ToolFacetId,
                        PackageQuery.NoDependenciesFacetId,
                    ]))).Plan;

        Assert.Equal(PackageQueryFacetTier.Nuspec, plan.Facets[0].Tier);
        Assert.Equal(
            [
                PackageQuery.ToolFacetId,
                PackageQuery.NoDependenciesFacetId,
            ],
            plan.Facets.Select(facet => facet.Id));
    }

    public static object PerformanceBoxingProbe(int value) => value;

    public static int PerformanceNoAllocationProbe(int value) => value;

    public static int InvocationDestinationProbe(int value) =>
        InvocationDestinationTarget(value);

    static int InvocationDestinationTarget(int value) => value;

    public static Guid PerformanceValueTypeConstructionProbe(byte[] bytes) =>
        new(bytes);

    public static int PerformanceStackAllocProbe(int value)
    {
        Span<int> values = stackalloc int[1];
        values[0] = value;
        return values[0];
    }

    public static int PerformanceGenericCallProbe()
    {
        PerformanceGenericCallTarget<int>();
        PerformanceGenericCallTarget<string>();
        PerformanceGenericCallTarget<System.Threading.Timer>();
        PerformanceGenericCallTarget<System.Timers.Timer>();
        return 0;
    }

    static void PerformanceGenericCallTarget<T>()
    {
    }

    public static object PerformanceBoxingProperty => 42;

    public static class PerformanceNestedProbe
    {
        public static object Box(int value) => value;
    }

    [Fact]
    public void QueryFailureAdapters_DoNotEmitArtifactAuthoredText()
    {
        const string artifactText = "Artifact\u202e";
        var identity = new AssemblyReferenceIdentity(
            artifactText,
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                identity,
                "test",
                () => new MemoryStream([0x01, 0x02, 0x03]),
                AssemblyResolutionProvenance.Package(
                    "Package.Sample",
                    "1.0.0",
                    "net11.0",
                    rid: null));
        var participant = new AssemblyContextParticipant(
            assembly,
            new RejectingBindingPolicy());
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        AssemblyContextApiSurfaceResult surface =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                BrowserApiSurfacePolicy.Limits);
        AssemblyContextIntegrationsResult integrations =
            AssemblyContextIntegrationsQuery.Execute(group);
        AssemblyIntegrationOpportunitiesEntry opportunity =
            AssemblyContextIntegrationOpportunitiesQuery.ExecuteParticipant(
                group,
                participant);
        string[] failures =
        [
            Assert.Single(
                BrowserSurfaceProjection.ApiSurfaceFailureEntries(
                    surface.Assemblies.Assemblies)),
            AnalysisExports.CreateIntegrations(
                "Package.Sample",
                "1.0.0",
                "net11.0",
                integrations.Assemblies).InspectionError!,
            AnalysisExports.CreateOpportunities(
                "Package.Sample",
                "1.0.0",
                "net11.0",
                [opportunity]).InspectionError!,
            BrowserSurfaceProjection.RejectedAssembly(
                new CandidateOpenFailure(
                    CandidateOpenFailureKind.InvalidImage,
                    artifactText)),
            BrowserSurfaceProjection.FailedAssembly(
                new InvalidDataException(artifactText)),
            BrowserSurfaceProjection.PartialApiSurface(1),
        ];

        Assert.All(
            failures,
            failure =>
            {
                Assert.DoesNotContain(
                    artifactText,
                    failure,
                    StringComparison.Ordinal);
                Assert.DoesNotContain('\u202e', failure);
            });
        Assert.Equal("Assembly unavailable: InvalidImage.", failures[0]);
        Assert.Equal(
            "Assembly inspection failed (InvalidDataException).",
            failures[4]);
        Assert.Equal(
            "An assembly API surface omitted 1 metadata row(s).",
            failures[5]);
    }

    [Fact]
    public void MemberProjection_CarriesFilterFactsWithoutSignatureParsing()
    {
        var type = new ApiType
        {
            Namespace = "Example",
            Name = "Widget",
            Kind = "class",
        };
        var member = new ApiMember
        {
            Name = "BuildAsync",
            Kind = "method",
            Signature = "protected static async Task BuildAsync()",
            Accessibility = "protected",
            IsStatic = true,
            IsUnsafe = true,
            IsVirtual = true,
            IsAbstract = true,
            IsOverride = true,
            IsExtension = true,
            IsObsolete = true,
        };

        BrowserMemberSurfaceInfo projected = BrowserSurfaceProjection.Member(type, member);

        Assert.Equal("protected", projected.Accessibility);
        Assert.True(projected.IsStatic);
        Assert.True(projected.IsUnsafe);
        Assert.True(projected.IsVirtual);
        Assert.True(projected.IsAbstract);
        Assert.True(projected.IsOverride);
        Assert.True(projected.IsExtension);
        Assert.True(projected.IsObsolete);

        BrowserMemberSurfaceInfo ordinary = BrowserSurfaceProjection.Member(
            type,
            new ApiMember
            {
                Name = "Name",
                Kind = "property",
                Signature = "string Name { get; }",
            });

        Assert.Equal("public", ordinary.Accessibility);
        Assert.False(ordinary.IsStatic);
        Assert.False(ordinary.IsObsolete);

        BrowserMemberSurfaceInfo explicitImplementation = BrowserSurfaceProjection.Member(
            type,
            new ApiMember
            {
                Name = "IDisposable.Dispose",
                Kind = "explicit-interface-implementation",
                Signature = "void IDisposable.Dispose()",
            });

        Assert.Equal("private", explicitImplementation.Accessibility);

        BrowserMemberSurfaceInfo finalizer = BrowserSurfaceProjection.Member(
            type,
            new ApiMember
            {
                Name = "Finalize",
                Kind = "finalizer",
                Signature = "~Widget()",
            });

        Assert.Equal("protected", finalizer.Accessibility);
    }

    [Fact]
    public async Task MsdlProxy_RewritesExactSymbolRequestToCurrentSwaApi()
    {
        var inner = new RequestRecordingHandler();
        using var handler = new BrowserMsdlProxyHandler(inner);
        handler.Configure("https://dotnet-inspect.ca");
        using var client = new HttpClient(handler);

        using HttpResponseMessage response =
            await client.GetAsync(
                "https://msdl.microsoft.com/download/symbols/"
                + "System.Text.Json.pdb/"
                + "00112233445566778899AABBCCDDEEFF1/"
                + "System.Text.Json.pdb",
                TestContext.Current.CancellationToken);

        Assert.Equal(
            "https://dotnet-inspect.ca/api/msdl/"
            + "System.Text.Json.pdb/"
            + "00112233445566778899AABBCCDDEEFF1",
            inner.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task MsdlProxy_LeavesEveryOtherDestinationUnchanged()
    {
        var inner = new RequestRecordingHandler();
        using var handler = new BrowserMsdlProxyHandler(inner);
        handler.Configure("https://dotnet-inspect.ca");
        using var client = new HttpClient(handler);

        using HttpResponseMessage response =
            await client.GetAsync(
                "https://api.nuget.org/v3/index.json",
                TestContext.Current.CancellationToken);

        Assert.Equal(
            "https://api.nuget.org/v3/index.json",
            inner.RequestUri?.AbsoluteUri);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://dotnet-inspect.ca/path")]
    [InlineData("https://user@example.com")]
    public void MsdlProxy_RejectsValuesThatAreNotHttpOrigins(string origin)
    {
        using var handler =
            new BrowserMsdlProxyHandler(
                new RequestRecordingHandler());
        Assert.Throws<ArgumentException>(() => handler.Configure(origin));
    }

    [Fact]
    public void SourceContexts_UseFreshMemoryOnlyPdbStores()
    {
        AssemblyContextSourceQueryContext first =
            SourceExports.CreateSourceContext();
        AssemblyContextSourceQueryContext second =
            SourceExports.CreateSourceContext();

        var firstStore =
            Assert.IsType<InMemoryPdbStore>(first.PdbStore);
        Assert.IsType<InMemoryPdbStore>(second.PdbStore);
        Assert.NotSame(first.PdbStore, second.PdbStore);
        Assert.Equal(24L * MiB, firstStore.MaxRetainedBytes);
        Assert.False(first.AllowLocalSourceReads);
        Assert.Null(first.RepositoryPaths);
        Assert.NotNull(first.SymbolAcquisitionLimits);
        Assert.InRange(
            first.SymbolAcquisitionLimits.MaxSymbolPackageBytes,
            1,
            24L * MiB);
        Assert.InRange(
            first.SymbolAcquisitionLimits.MaxPortablePdbBytes,
            1,
            8L * MiB);
        Assert.InRange(
            first.SymbolAcquisitionLimits.MaxExpandedPdbBytes,
            1,
            24L * MiB);
    }

    [Fact]
    public async Task SourceOperations_AreExclusiveAndSuperseding()
    {
        using BrowserSourceOperationLease first =
            await BrowserSourceOperationCoordinator.BeginAsync();
        Task<BrowserSourceOperationLease> secondTask =
            BrowserSourceOperationCoordinator.BeginAsync().AsTask();

        Assert.True(first.CancellationToken.IsCancellationRequested);
        Assert.False(secondTask.IsCompletedSuccessfully);

        first.Dispose();
        using BrowserSourceOperationLease second = await secondTask;
        Assert.False(second.CancellationToken.IsCancellationRequested);

        BrowserSourceOperationCoordinator.CancelCurrent();
        Assert.True(second.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task CancelledWait_ReleasesSharedPackageAcquisition()
    {
        var completion =
            new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        Task<int> waiting = BrowserPackageWorkspace.WaitForSharedAcquisitionAsync(
            completion.Task,
            cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.False(completion.Task.IsCompleted);
        completion.SetResult(42);
        Assert.Equal(42, await completion.Task);
    }

    [Fact]
    public async Task CancelledPackageAcquisition_StopsBeforeNetworkAccess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => BrowserPackageWorkspace.AcquireAsync(
                "Cancelled.Source",
                "1.0.0",
                cancellation.Token));
    }

    [Fact]
    public async Task ActiveScopeLease_PreventsWorkspaceAndPackageEviction()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate activeCoordinate = await Coordinate(
            "Active.Source",
            Package(image, "lib/net11.0/Active.Source.dll"));
        await using BrowserScopeLease<BrowserInspectionScope> activeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
            [activeCoordinate],
            TestContext.Current.CancellationToken);
        BrowserInspectionScope active = activeLease.Scope;
        await using BrowserScopeLease<BrowserInspectionScope> lease =
            BrowserPackageWorkspace.LeaseScope(active);

        foreach (string id in new[] { "Lease.B", "Lease.C", "Lease.D", "Lease.E" })
        {
            await (await BrowserPackageWorkspace.OpenScopeAsync(
                [await Coordinate(id, Package(image, $"lib/net11.0/{id}.dll"))],
                TestContext.Current.CancellationToken))
                .DisposeAsync();
        }

        await using BrowserScopeLease<BrowserInspectionScope> reopenedLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
            [activeCoordinate],
            TestContext.Current.CancellationToken);
        BrowserInspectionScope reopened = reopenedLease.Scope;
        Assert.Same(active, reopened);
        Assert.InRange(BrowserPackageWorkspace.Stats().Workspaces, 1, 4);
    }

    [Fact]
    public async Task PlatformWorkspace_UsesMetadataIdentityForPackMembership()
    {
        const string packageId =
            "microsoft.aspnetcore.app.runtime.linux-x64";
        const string version = "11.0.0";
        byte[] nupkg = PlatformPackage(
            ("Misleading.dll",
                File.ReadAllBytes(
                    typeof(BrowserEngineBoundaryTests).Assembly.Location)));
        var handler = new PlatformVersionHandler(
            packageId,
            version,
            nupkg);
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);

        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0-ios",
                "InspectWeb.Engine.Tests.dll",
                "aspnetcore.app",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        BrowserPackageSurface surface = Assert.IsType<BrowserPackageSurface>(
            JsonSerializer.Deserialize(
                PackageExports.ProjectPlatformSurface(resolution),
                BrowserPackageJsonContext.Default.BrowserPackageSurface));

        BrowserAssemblySurface selectedAssembly =
            Assert.Single(surface.Assemblies);
        Assert.Equal(
            "aspnetcore.app",
            selectedAssembly.PlatformPack);
        Assert.All(
            surface.Types,
            type => Assert.Equal("aspnetcore.app", type.PlatformPack));

        var selected = surface.Types
            .SelectMany(type =>
                type.Api.Select(member => (Type: type, Member: member)))
            .First(candidate =>
                candidate.Member.MetadataToken is > 0
                && candidate.Member.BodySelectors.Length > 0);
        BrowserCallGraph graph = Assert.IsType<BrowserCallGraph>(
            JsonSerializer.Deserialize(
                await CallGraphExports.ExpandPlatformCallGraph(
                    "net11.0-ios",
                    "InspectWeb.Engine.Tests",
                    "aspnetcore.app",
                    selectedAssembly.Version,
                    selectedAssembly.Culture,
                    selectedAssembly.PublicKeyToken,
                    selected.Type.MetadataId,
                    selected.Member.Name,
                    selected.Member.GraphSelectorKey,
                    selected.Member.MetadataToken!.Value),
                BrowserCallGraphJsonContext.Default.BrowserCallGraph));
        BrowserCallGraphTarget[] ownTargets =
        [
            .. graph.Targets.Where(target =>
                target.Assembly == "InspectWeb.Engine.Tests"),
        ];
        Assert.NotEmpty(ownTargets);
        Assert.All(
            ownTargets,
            target => Assert.Equal(
                "aspnetcore.app",
                target.PlatformPack));
    }

    [Fact]
    public async Task PlatformWorkspace_ResolvesUnknownFamilyFromProductPacks()
    {
        const string version = "11.0.101";
        byte[] runtimeNupkg = PlatformPackage(
            ("System.Private.CoreLib.dll",
                File.ReadAllBytes(typeof(object).Assembly.Location)));
        byte[] aspNetNupkg = PlatformPackage(
            ("InspectWeb.Engine.Tests.dll",
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
                "InspectWeb.Engine.Tests.dll",
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
                "InspectWeb.Engine.Tests"));
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
            ("InspectWeb.Engine.Tests.dll",
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
                "InspectWeb.Engine.Tests.dll",
                "",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                cancellation.Token));

        handler.BeforeDownload = null;
        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0-auto-family-cancellation",
                "InspectWeb.Engine.Tests.dll",
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
                    Package(image, "lib/net11.0/InspectWeb.Engine.Tests.dll"),
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
                        ("InspectWeb.Engine.Tests.dll", image)),
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
            ("InspectWeb.Engine.Tests.dll", sharedImage));
        byte[] aspNetNupkg = PlatformPackage(
            ("InspectWeb.Engine.Tests.dll", sharedImage));
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
                    "InspectWeb.Engine.Tests.dll",
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
            ("InspectWeb.Engine.Tests.dll",
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
                "InspectWeb.Engine.Tests.dll",
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
                "InspectWeb.Engine.Tests.dll",
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
                "InspectWeb.Engine.Tests"));
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
            ("InspectWeb.Engine.Tests.dll",
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
                "InspectWeb.Engine.Tests.dll",
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
            ("InspectWeb.Engine.Tests.dll",
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
                        "InspectWeb.Engine.Tests.dll",
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
    public async Task PlatformWorkspace_RejectsOneNameAcrossPackFamilies()
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
                "InspectWeb.Engine.Tests.dll",
                "netcore.app",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPlatformWorkspace.OpenAssemblyAsync(
                    "net11.0-platform-family-collision",
                    "InspectWeb.Engine.Tests.dll",
                    "aspnetcore.app",
                    client,
                    authorization,
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken));

        Assert.Contains("already selected", failure.Message);
        Assert.Single(runtime.Scope.Members);

        bool downloaded = false;
        handler.BeforeDownload = _ => downloaded = true;
        InvalidOperationException batchFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPlatformWorkspace.OpenAssembliesAsync(
                    "net11.0-platform-family-batch-collision",
                    [
                        new(
                            "InspectWeb.Engine.Tests.dll",
                            "netcore.app"),
                        new(
                            "InspectWeb.Engine.Tests.dll",
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
            ("InspectWeb.Engine.Tests.dll",
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
                "InspectWeb.Engine.Tests.dll",
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
                "InspectWeb.Engine.Tests"));
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
            ("InspectWeb.Engine.Tests.dll",
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
                "InspectWeb.Engine.Tests.dll",
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
                "InspectWeb.Engine.Tests"));
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
    public void SourceFetchPolicy_OmitsCredentialsAndRefusesRedirects()
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                "https://raw.githubusercontent.com/org/repo/commit/A.cs");

        BrowserSourceFetchPolicy.Instance.ConfigureRequest(request);

        Assert.True(request.Options.TryGetValue(
            new HttpRequestOptionsKey<IDictionary<string, object>>(
                "WebAssemblyFetchOptions"),
            out IDictionary<string, object>? options));
        Assert.Equal("omit", options["credentials"]);
        Assert.Equal("error", options["redirect"]);
    }

    [Fact]
    public async Task TypeSourceParticipant_RefusesReferenceOnlyAssembly()
    {
        byte[] image =
            File.ReadAllBytes(
                typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await Coordinate(
            "Reference.Source",
            Package(
                image,
                "ref/net11.0/InspectWeb.Engine.Tests.dll"));

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(
                () => coordinate.ImplementationAsset(
                    Assert.IsType<PackageCompileAsset>(
                        coordinate.DefaultAsset).AssemblyName));

        Assert.Contains("reference assembly only", error.Message);
    }

    [Fact]
    public async Task RidSpecificPackage_SeparatesCompileAndImplementationAssets()
    {
        const string packageId = "Rid.Specific";
        byte[] image =
            File.ReadAllBytes(
                typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] unrelatedImage =
            File.ReadAllBytes(
                typeof(PackageAssemblyContextRealization).Assembly.Location);
        var package = new BrowserPackage(
            packageId,
            "1.0.0",
            PackageEntries(
                ("lib/net11.0/Rid.Specific.dll", image),
                ("lib/net11.0/shadow/Rid.Specific.dll", unrelatedImage),
                ("runtimes/linux-x64/lib/net11.0/Rid.Specific.dll", image)),
            fromCache: false);
        var coordinate = new BrowserPackageCoordinate(
            package,
            new PackageRootRealization(
                package.Content,
                packageId,
                package.Version,
                "net11.0",
                "linux-x64"));

        PackageCompileAsset compile =
            coordinate.CompileAsset("Rid.Specific.dll");
        Assert.Equal("lib/net11.0/Rid.Specific.dll", compile.Path);
        Assert.Equal(
            "runtimes/linux-x64/lib/net11.0/Rid.Specific.dll",
            coordinate.ImplementationAsset("Rid.Specific.dll").Path);
        await using BrowserInspectionScope scope = await BrowserInspectionScope.CreateAsync([coordinate], TestContext.Current.CancellationToken);
        BrowserWorkspaceParticipant surface =
            Assert.Single(scope.SurfaceParticipants);
        BrowserWorkspaceParticipant implementation =
            scope.ImplementationParticipants.Single(candidate =>
                candidate.Asset.Path
                    == "runtimes/linux-x64/lib/net11.0/Rid.Specific.dll");
        Assert.Equal(compile.Path, surface.Asset.Path);
        Assert.Equal(
            "runtimes/linux-x64/lib/net11.0/Rid.Specific.dll",
            implementation.Asset.Path);
        Assert.Contains(
            scope.ImplementationParticipants,
            candidate =>
                candidate.Asset.Path
                    == "lib/net11.0/shadow/Rid.Specific.dll");
        Assert.Same(
            implementation,
            scope.ImplementationParticipant(surface));
    }

    [Fact]
    public async Task PackageFrameworkUnavailability_DoesNotEmitArtifactFramework()
    {
        const char bidi = '\u202E';
        const string packageId = "Bidi.Framework.Failure";
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                PackageEntries(
                    ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                        $"""
                         <?xml version="1.0" encoding="utf-8"?>
                         <package>
                           <metadata>
                             <id>{packageId}</id>
                             <version>1.0.0</version>
                             <dependencies>
                               <group targetFramework="net8.0{bidi}" />
                             </dependencies>
                           </metadata>
                         </package>
                         """)),
                    ($"lib/net8.0{bidi}/{packageId}.dll", [0x01])),
                fromCache: false));

        BrowserPackageSurface surface = Assert.IsType<BrowserPackageSurface>(
            JsonSerializer.Deserialize(
                await PackageExports.QueryPackage(
                    packageId,
                    "1.0.0",
                    "net11.0"),
                BrowserPackageJsonContext.Default.BrowserPackageSurface));

        Assert.DoesNotContain(
            surface.Frameworks,
            framework => framework.Contains(bidi, StringComparison.Ordinal));
        Assert.Equal(
            BrowserCompileLibraryStatus.NoMatchingTargetFramework,
            surface.CompileLibrary.Status);
        Assert.Empty(surface.Frameworks);
        Assert.DoesNotContain(bidi, surface.ActiveFramework);
        Assert.DoesNotContain(
            bidi,
            surface.CompileLibrary.TargetFramework ?? "");
        InvalidOperationException dependencyFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => PackageExports.QueryPackageDependencies(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    assemblyId: ""));
        Assert.Contains(
            "cannot be represented safely",
            dependencyFailure.Message);
        Assert.DoesNotContain(bidi, dependencyFailure.Message);

        var selectedPackage = new BrowserPackage(
            "Bidi.Selected.Framework",
            "1.0.0",
            Package(
                [0x01],
                $"lib/net8.0{bidi}/Selected.dll"),
            fromCache: false);
        var selectedContext = new PackageRootRealization(
            selectedPackage.Content,
            selectedPackage.PackageId,
            selectedPackage.Version);
        var coordinate =
            new BrowserPackageCoordinate(selectedPackage, selectedContext);

        InvalidOperationException compileFailure =
            Assert.Throws<InvalidOperationException>(
                () => coordinate.CompileAsset("Missing.dll"));

        Assert.DoesNotContain(bidi, compileFailure.Message);
    }

    [Fact]
    public void PackageCoordinate_RejectsDifferentContentWithSameIdentity()
    {
        const string packageId = "Exact.Content";
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        var package = new BrowserPackage(
            packageId,
            "1.0.0",
            Package(image, $"lib/net11.0/{packageId}.dll"),
            fromCache: false);
        var differentContent = new BrowserPackage(
            packageId,
            "1.0.0",
            Package(
                image,
                $"lib/net11.0/{packageId}.dll",
                paddingBytes: 1),
            fromCache: false);
        var root = new PackageRootRealization(
            differentContent.Content,
            packageId,
            "1.0.0",
            "net11.0");

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new BrowserPackageCoordinate(package, root));

        Assert.Contains("exact content", error.Message);
    }

    [Fact]
    public async Task PackageScope_DoesNotCollapseDifferentContentAtSameCoordinate()
    {
        string packageId = $"Exact.Scope.{Guid.NewGuid():N}";
        byte[] firstImage =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] secondImage =
            File.ReadAllBytes(typeof(PackageAssemblyContextRealization).Assembly.Location);
        var firstPackage = new BrowserPackage(
            packageId,
            "1.0.0",
            Package(firstImage, $"lib/net11.0/{packageId}.dll"),
            fromCache: false);
        var secondPackage = new BrowserPackage(
            packageId,
            "1.0.0",
            Package(secondImage, $"lib/net11.0/{packageId}.dll"),
            fromCache: false);
        var firstCoordinate = new BrowserPackageCoordinate(
            firstPackage,
            new PackageRootRealization(
                firstPackage.Content,
                packageId,
                "1.0.0",
                "net11.0"));
        var secondCoordinate = new BrowserPackageCoordinate(
            secondPackage,
            new PackageRootRealization(
                secondPackage.Content,
                packageId,
                "1.0.0",
                "net11.0"));

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await BrowserInspectionScope.CreateAsync(
                [firstCoordinate, secondCoordinate],
                TestContext.Current.CancellationToken));
        await using BrowserInspectionScope directScope =
            await BrowserInspectionScope.CreateAsync([firstCoordinate], TestContext.Current.CancellationToken);
        Assert.False(
            directScope.ContainsExactCoordinates([secondCoordinate]));
        Assert.Throws<InvalidOperationException>(
            () => directScope.Coordinate(secondCoordinate));

        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(firstPackage);
        await using BrowserScopeLease<BrowserInspectionScope> retainedLease =
            await BrowserPackageWorkspace.OpenScopeAsync([firstCoordinate], TestContext.Current.CancellationToken);
        BrowserInspectionScope retained = retainedLease.Scope;
        try
        {
            await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(secondPackage);
            InvalidOperationException cacheFailure =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await BrowserPackageWorkspace.OpenScopeAsync(
                        [secondCoordinate],
                        TestContext.Current.CancellationToken));
            Assert.Contains(
                "exact requested package content",
                cacheFailure.Message);
        }
        finally
        {
            await BrowserPackageWorkspace.RemoveScopeAsync(retained);
        }
    }

    [Fact]
    public async Task PackageScope_ValidatesEveryCoordinateAgainstCacheProvenance()
    {
        string provenanceId = $"Exact.Provenance.{Guid.NewGuid():N}";
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] archive =
            Package(image, $"lib/net11.0/{provenanceId}.dll");
        var registered = new BrowserPackage(
            provenanceId,
            "1.0.0",
            archive,
            fromCache: false,
            producerKey: "producer-a");
        var unregisteredProducer = new BrowserPackage(
            provenanceId,
            "1.0.0",
            archive,
            fromCache: false,
            producerKey: "producer-b");
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(registered);
        var provenanceCoordinate = new BrowserPackageCoordinate(
            unregisteredProducer,
            new PackageRootRealization(
                unregisteredProducer.Content,
                provenanceId,
                "1.0.0",
                "net11.0"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await BrowserPackageWorkspace.OpenScopeAsync(
                [provenanceCoordinate],
                TestContext.Current.CancellationToken));

        string frameworksId = $"Exact.Frameworks.{Guid.NewGuid():N}";
        var cachedPackage = new BrowserPackage(
            frameworksId,
            "1.0.0",
            Package(
                image,
                $"lib/net10.0/{frameworksId}.dll"),
            fromCache: false,
            producerKey: "producer-a");
        var unregisteredFramework = new BrowserPackage(
            frameworksId,
            "1.0.0",
            Package(
                image,
                $"lib/net11.0/{frameworksId}.dll"),
            fromCache: false,
            producerKey: "producer-a");
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(cachedPackage);
        var cachedCoordinate = new BrowserPackageCoordinate(
            cachedPackage,
            new PackageRootRealization(
                cachedPackage.Content,
                frameworksId,
                "1.0.0",
                "net10.0"));
        var unregisteredCoordinate = new BrowserPackageCoordinate(
            unregisteredFramework,
            new PackageRootRealization(
                unregisteredFramework.Content,
                frameworksId,
                "1.0.0",
                "net11.0"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await BrowserPackageWorkspace.OpenScopeAsync(
                [cachedCoordinate, unregisteredCoordinate],
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PackageScope_RequestedFrameworkCannotForgeCompositeRegistryKey()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string firstId = $"Scope.Collision.First.{suffix}";
        string secondId = $"Scope.Collision.Second.{suffix}";
        var firstPackage = new BrowserPackage(
            firstId,
            "1.0.0",
            PackageEntries(($"tools/net11.0/any/{firstId}.dll", [0x01])),
            fromCache: false);
        var secondPackage = new BrowserPackage(
            secondId,
            "1.0.0",
            PackageEntries(($"tools/net11.0/any/{secondId}.dll", [0x02])),
            fromCache: false);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(firstPackage);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(secondPackage);

        BrowserPackageCoordinate crafted = new(
            firstPackage,
            new PackageRootRealization(
                firstPackage.Content,
                firstId,
                "1.0.0",
                $"net8.0|{secondId.ToLowerInvariant()}@1.0.0/net9.0"));
        BrowserPackageCoordinate first = new(
            firstPackage,
            new PackageRootRealization(
                firstPackage.Content,
                firstId,
                "1.0.0",
                "net8.0"));
        BrowserPackageCoordinate second = new(
            secondPackage,
            new PackageRootRealization(
                secondPackage.Content,
                secondId,
                "1.0.0",
                "net9.0"));

        await using BrowserScopeLease<BrowserInspectionScope> craftedScopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync([crafted], TestContext.Current.CancellationToken);
        BrowserInspectionScope craftedScope = craftedScopeLease.Scope;
        await using BrowserScopeLease<BrowserInspectionScope> legitimateScopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync([first, second], TestContext.Current.CancellationToken);
        BrowserInspectionScope legitimateScope = legitimateScopeLease.Scope;
        try
        {
            Assert.NotSame(craftedScope, legitimateScope);
            Assert.Single(craftedScope.Coordinates);
            Assert.Equal(2, legitimateScope.Coordinates.Length);
        }
        finally
        {
            await BrowserPackageWorkspace.RemoveScopeAsync(craftedScope);
            await BrowserPackageWorkspace.RemoveScopeAsync(legitimateScope);
        }
    }

    [Fact]
    public async Task MixedPackageScope_RealizesOnlySelectedCoordinates()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        var selectedPackage = new BrowserPackage(
            "Selected.Library",
            "1.0.0",
            Package(image, "lib/net11.0/Selected.Library.dll"),
            fromCache: false);
        var rootOnlyPackage = new BrowserPackage(
            "Tool.Pointer",
            "1.0.0",
            PackageEntries(
                ("tools/net11.0/any/Tool.Pointer.dll", [0x01])),
            fromCache: false);
        var selectedCoordinate = new BrowserPackageCoordinate(
            selectedPackage,
            new PackageRootRealization(
                selectedPackage.Content,
                selectedPackage.PackageId,
                selectedPackage.Version,
                "net11.0"));
        var rootOnlyCoordinate = new BrowserPackageCoordinate(
            rootOnlyPackage,
            new PackageRootRealization(
                rootOnlyPackage.Content,
                rootOnlyPackage.PackageId,
                rootOnlyPackage.Version,
                "net11.0"));

        await using BrowserInspectionScope scope = await BrowserInspectionScope.CreateAsync(
            [selectedCoordinate, rootOnlyCoordinate],
            TestContext.Current.CancellationToken);

        Assert.Equal(2, scope.Coordinates.Length);
        BrowserWorkspaceParticipant participant =
            Assert.Single(scope.SurfaceParticipants);
        Assert.Same(selectedCoordinate, participant.Coordinate);
        Assert.Same(
            selectedCoordinate,
            Assert.Single(scope.ImplementationParticipants).Coordinate);
        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(
                () => rootOnlyCoordinate.CompileAsset("Tool.Pointer"));
        Assert.Contains(
            nameof(PackageCompileAssetSelectionStatus.NoCompileAssets),
            error.Message);
    }

    [Fact]
    public async Task ReferenceOnlyFailures_DoNotEmitArtifactAssemblyNames()
    {
        const char bidi = '\u202E';
        string assemblyName = $"Bidi.Reference{bidi}.dll";
        byte[] image =
            File.ReadAllBytes(
                typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await Coordinate(
            "Bidi.ReferenceOnly",
            Package(
                image,
                $"ref/net11.0/{assemblyName}"));

        InvalidOperationException coordinateFailure =
            Assert.Throws<InvalidOperationException>(
                () => coordinate.ImplementationAsset(assemblyName));

        Assert.Contains("reference assembly only", coordinateFailure.Message);
        Assert.DoesNotContain(bidi, coordinateFailure.Message);

        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync([coordinate], TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;
        InvalidOperationException scopeFailure =
            Assert.Throws<InvalidOperationException>(
                () => scope.ImplementationParticipant(
                    Assert.Single(scope.SurfaceParticipants)));

        Assert.Contains("reference assembly only", scopeFailure.Message);
        Assert.DoesNotContain(bidi, scopeFailure.Message);
    }

    [Fact]
    public void MissingPackageEntryFailure_DoesNotEmitArtifactPath()
    {
        const char bidi = '\u202E';
        var package = new BrowserPackage(
            "Bidi.Missing.Entry",
            "1.0.0",
            Package([0x01], "lib/net11.0/Present.dll"),
            fromCache: false);

        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(
                () => package.OpenEntry(
                    $"lib/net11.0/Missing{bidi}.dll",
                    1_024));

        Assert.DoesNotContain(bidi, failure.Message);
    }

    [Fact]
    public void SourceFailures_PreserveTypedDetailAndCause()
    {
        var cause = new IOException("symbol service failed");
        var failure = new AssemblySourceFailure(
            AssemblySourceFailureKind.InspectionFailed,
            "Source inspection failed.",
            cause);

        InvalidOperationException adapted =
            SourceExports.SourceUnavailable(failure);

        Assert.Contains(
            nameof(AssemblySourceFailureKind.InspectionFailed),
            adapted.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            failure.Detail,
            adapted.Message,
            StringComparison.Ordinal);
        Assert.Same(cause, adapted.InnerException);

        InvalidOperationException withPdbSourceFailure =
            SourceExports.SourceUnavailable(
                failure,
                "The host does not authorize this SourceLink destination.");
        Assert.Contains(
            "PDB source unavailable",
            withPdbSourceFailure.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "does not authorize",
            withPdbSourceFailure.Message,
            StringComparison.Ordinal);
        Assert.Same(cause, withPdbSourceFailure.InnerException);
    }

    [Fact]
    public async Task DecompiledSources_CarryPdbAttemptLimitation()
    {
        byte[] image =
            File.ReadAllBytes(
                typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await Coordinate(
            "Source.Limitation",
            Package(
                image,
                "lib/net11.0/InspectWeb.Engine.Tests.dll"));
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync([coordinate], TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserWorkspaceParticipant participant =
            Assert.Single(scope.ImplementationParticipants);
        AssemblyContextApiSurfaceResult result =
            scope.UseImplementation(
                group => AssemblyContextApiSurfaceQuery.Execute(group));
        var available =
            Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
                Assert.Single(result.Assemblies.Assemblies));
        ApiType type = available.Value.Surface.Types.First(
            candidate => candidate.Members.Any(
                member => member.MetadataToken is not null));
        ApiMember member = type.Members.First(
            candidate => candidate.MetadataToken is not null);

        const string MemberLimitation = "member PDB source unavailable";
        var memberAttempt = new PdbMemberSourceInspection(
            new FindingInspection<string>.Absent(
                FindingInspectionAbsenceKind.NoApplicableInput,
                MemberLimitation),
            Text: null,
            Mapping: null,
            Document: null,
            ChecksumVerification: null);
        var memberEntry = new AssemblyMemberSourceEntry.Available(
            available.Subject,
            AssemblyMemberSourceRequest.From(type, member),
            new AssemblyMemberSource.Decompiled(
                "void M() {}",
                new MemberRenderResult(
                    MemberBodyProductionStatus.Complete,
                    "void M() {}",
                    []),
                memberAttempt));

        BrowserSource memberSource =
            SourceExports.Adapt(memberEntry, participant);
        Assert.Equal(MemberLimitation, memberSource.PdbSourceLimitation);

        const string TypeLimitation = "type PDB source unavailable";
        var typeAttempt = new PdbTypeSourceInspection(
            new FindingInspection<string>.Absent(
                FindingInspectionAbsenceKind.NoApplicableInput,
                TypeLimitation),
            Text: null,
            Mapping: null,
            Document: null,
            ChecksumVerification: null);
        var typeEntry = new AssemblyTypeSourceEntry.Available(
            available.Subject,
            AssemblyTypeSourceRequest.From(type),
            new AssemblyTypeSource.Decompiled(
                "class C {}",
                new DecompilerResult(
                    "class C {}",
                    DecompilationFidelity.Full,
                    []),
                typeAttempt));

        BrowserSource typeSource =
            SourceExports.Adapt(typeEntry, participant);
        Assert.Equal(TypeLimitation, typeSource.PdbSourceLimitation);
    }

    [Fact]
    public async Task WorkspaceOwnership_AccountsArchivesAndCarriesSelectedFailures()
    {
        byte[] image = File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);

        await (await BrowserPackageWorkspace.OpenScopeAsync(
            [await Coordinate("Large.A", Package(image, "lib/net11.0/Large.A.dll", 60 * MiB))],
            TestContext.Current.CancellationToken))
            .DisposeAsync();
        long expectedResidentBytes = 0;
        foreach (string id in new[] { "Small.B", "Small.C", "Small.D" })
        {
            byte[] package = Package(
                image,
                $"lib/net11.0/{id}.dll",
                25 * MiB);
            expectedResidentBytes += package.LongLength;
            await (await BrowserPackageWorkspace.OpenScopeAsync(
                [await Coordinate(id, package)],
                TestContext.Current.CancellationToken))
                .DisposeAsync();
        }

        BrowserPackageCacheSnapshot stats = BrowserPackageWorkspace.Stats();
        Assert.Equal(3, stats.Workspaces);
        Assert.Equal(3, stats.Resident);
        Assert.Equal(expectedResidentBytes, stats.ResidentBytes);

        using (await BrowserPackageWorkspace.ReservePackageDownloadAsync(
            "pending.package@1.0.0",
            80L * MiB))
        {
            BrowserPackageCacheSnapshot reserved = BrowserPackageWorkspace.Stats();
            Assert.InRange(reserved.ResidentBytes, 80L * MiB, 128L * MiB);
            Assert.Equal(1, reserved.Workspaces);
        }

        await using BrowserScopeLease<BrowserInspectionScope> malformedLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
            [await Coordinate(
                "Malformed",
                Package([0x01, 0x02, 0x03], "lib/net11.0/Malformed.dll"))],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope malformed = malformedLease.Scope;
        AssemblyContextApiSurfaceResult malformedResult = malformed.UseSurface(
            group => AssemblyContextApiSurfaceQuery.Execute(group));
        Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Rejected>(
            Assert.Single(malformedResult.Assemblies.Assemblies));

        byte[] largeReferenceImage = new byte[40 * MiB];
        image.CopyTo(largeReferenceImage, 0);
        await using BrowserScopeLease<BrowserInspectionScope> referenceOnlyLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
            [await Coordinate(
                "Reference.Only",
                Package(
                    largeReferenceImage,
                    "ref/net11.0/Reference.Only.dll"))],
                    TestContext.Current.CancellationToken);
        BrowserInspectionScope referenceOnly = referenceOnlyLease.Scope;
        AssemblyContextApiSurfaceResult referenceResult = referenceOnly.UseSurface(
            group => AssemblyContextApiSurfaceQuery.Execute(group));
        Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
            Assert.Single(referenceResult.Assemblies.Assemblies));

        BrowserPackageCoordinate oversized = await Coordinate(
            "Oversized.Role",
            PackageRole(
                image,
                "Oversized.Role",
                assemblyCount: 4,
                expandedAssemblyBytes: 20 * MiB));
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await BrowserPackageWorkspace.OpenScopeAsync([oversized], TestContext.Current.CancellationToken));
        Assert.Contains(
            "before assembly identity decoding",
            failure.Message,
            StringComparison.Ordinal);

        BrowserPackageCoordinate tooManyAssemblies = await Coordinate(
            "Too.Many.Assemblies",
            PackageRole(
                [0x01],
                "Too.Many.Assemblies",
                BrowserInspectionScope.MaxAssembliesPerRole + 1,
                expandedAssemblyBytes: 1));
        InvalidOperationException countFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await BrowserPackageWorkspace.OpenScopeAsync([tooManyAssemblies], TestContext.Current.CancellationToken));
        Assert.Contains(
            "assembly-count limit",
            countFailure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryPackage_AllSelectedFailuresPreserveKindWithoutArtifactDetail()
    {
        const string packageId = "Malformed.Surface";
        const string version = "1.0.0";
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                version,
                Package(
                    [0x01, 0x02, 0x03],
                    $"lib/net11.0/{packageId}.dll"),
                fromCache: false));

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => PackageExports.QueryPackage(
                    packageId,
                    version,
                    "net11.0"));

        Assert.Contains(
            "Assembly unavailable: InvalidImage.",
            failure.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "invalid metadata",
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformWorkspace_PinsAndAccumulatesSelectedAssemblies()
    {
        const string packageId =
            "microsoft.netcore.app.runtime.linux-x64";
        const string version = "11.0.0";
        byte[] coreLibrary = File.ReadAllBytes(typeof(object).Assembly.Location);
        byte[] sibling =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] nupkg = PlatformPackage(
            ("System.Private.CoreLib.dll", coreLibrary),
            ("InspectWeb.Engine.Tests.dll", sibling));
        var handler = new PlatformVersionHandler(
            packageId,
            version,
            nupkg);
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);

        await using BrowserPlatformScopeResolution initial =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                "net11.0",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        BrowserPackageSurface surface = Assert.IsType<BrowserPackageSurface>(
            JsonSerializer.Deserialize(
                PackageExports.ProjectPlatformSurface(initial),
                BrowserPackageJsonContext.Default.BrowserPackageSurface));

        Assert.Equal("Microsoft.NETCore.App", surface.Package);
        Assert.Equal(version, surface.Version);
        Assert.Equal("System.Private.CoreLib", surface.DefaultAssemblyId);
        Assert.Single(surface.Assemblies);
        Assert.NotEmpty(surface.Types);
        Assert.Equal(
            "netcore.app",
            Assert.Single(surface.Assemblies).PlatformPack);
        Assert.All(
            surface.Types,
            type => Assert.Equal("netcore.app", type.PlatformPack));
        int requestsAfterInitialLoad = handler.Requests;
        await using BrowserPlatformScopeResolution reused =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                "net11.0",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        Assert.Same(initial.Scope, reused.Scope);
        Assert.Equal(requestsAfterInitialLoad, handler.Requests);

        await using BrowserPlatformScopeResolution expanded =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0",
                "InspectWeb.Engine.Tests.dll",
                "netcore.app",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        Assert.Equal(2, expanded.Scope.Members.Length);
        Assert.All(
            expanded.Scope.Coordinates,
            coordinate =>
            {
                Assert.Equal(version, coordinate.Version);
                Assert.Equal(
                    NuGetCache.GetSourceKey(PackageSource.NuGetOrg.Url),
                    coordinate.Producer);
            });
        Assert.True(
            BrowserPackageWorkspace.IsScopeRetained(initial.Scope));
        Assert.Single(initial.Scope.Members);
        Assert.True(
            BrowserPackageWorkspace.IsScopeRetained(expanded.Scope));
        Assert.Equal(requestsAfterInitialLoad, handler.Requests);
        await reused.DisposeAsync();
        Assert.True(
            BrowserPackageWorkspace.IsScopeRetained(initial.Scope));
        await initial.DisposeAsync();
        Assert.False(
            BrowserPackageWorkspace.IsScopeRetained(initial.Scope));

        BrowserPackageSurface siblingSurface =
            Assert.IsType<BrowserPackageSurface>(
                JsonSerializer.Deserialize(
                    await PackageExports.LoadRuntimePackAssembly(
                        "net11.0",
                        "InspectWeb.Engine.Tests.dll",
                        "netcore.app"),
                    BrowserPackageJsonContext.Default.BrowserPackageSurface));
        Assert.Equal(
            "InspectWeb.Engine.Tests",
            siblingSurface.DefaultAssemblyId);
        BrowserPackageIntegrations integrations =
            Assert.IsType<BrowserPackageIntegrations>(
                JsonSerializer.Deserialize(
                    await AnalysisExports.QueryPlatformIntegrations(
                        "net11.0",
                        "InspectWeb.Engine.Tests.dll",
                        "netcore.app"),
                    BrowserAnalysisJsonContext.Default.BrowserPackageIntegrations));
        Assert.True(integrations.IsComplete);
        Assert.Equal(
            BrowserAnalysisCompileLibraryStatus.Selected,
            integrations.CompileLibrary.Status);
        BrowserPackageOpportunities opportunities =
            Assert.IsType<BrowserPackageOpportunities>(
                JsonSerializer.Deserialize(
                    await AnalysisExports.QueryPlatformOpportunities(
                        "net11.0",
                        "InspectWeb.Engine.Tests.dll",
                        "netcore.app"),
                    BrowserAnalysisJsonContext.Default.BrowserPackageOpportunities));
        Assert.True(opportunities.IsComplete);
        Assert.Equal(
            BrowserAnalysisCompileLibraryStatus.Selected,
            opportunities.CompileLibrary.Status);
        BrowserPackageMetadata metadata =
            Assert.IsType<BrowserPackageMetadata>(
                JsonSerializer.Deserialize(
                    await MetadataExports.QueryPlatformMetadata(
                        "net11.0",
                        version,
                        "InspectWeb.Engine.Tests.dll",
                        "netcore.app"),
                    BrowserMetadataJsonContext.Default.BrowserPackageMetadata));
        Assert.Equal(
            BrowserMetadataCompileLibraryStatus.Selected,
            metadata.CompileLibrary.Status);

        var selected = siblingSurface.Types
            .SelectMany(type => type.Api.Select(member => (Type: type, Member: member)))
            .First(candidate =>
                candidate.Member.MetadataToken is > 0
                && candidate.Member.BodySelectors.Length > 0);
        BrowserAssemblySurface selectedAssembly =
            Assert.Single(
                siblingSurface.Assemblies,
                assembly => assembly.Id == selected.Type.AssemblyId);
        BrowserCallGraph graph = Assert.IsType<BrowserCallGraph>(
            JsonSerializer.Deserialize(
                await CallGraphExports.ExpandPlatformCallGraph(
                    "net11.0",
                    "InspectWeb.Engine.Tests",
                    "netcore.app",
                    selectedAssembly.Version,
                    selectedAssembly.Culture,
                    selectedAssembly.PublicKeyToken,
                    selected.Type.MetadataId,
                    selected.Member.Name,
                    selected.Member.GraphSelectorKey,
                    selected.Member.MetadataToken!.Value),
                BrowserCallGraphJsonContext.Default.BrowserCallGraph));
        Assert.Equal(0, graph.Scope.Packages);
        Assert.Equal(2, graph.Scope.Assemblies);
        BrowserCallGraphTarget[] attributedTargets =
        [
            .. graph.Targets.Where(target =>
                target.Assembly is "System.Private.CoreLib"
                    or "InspectWeb.Engine.Tests"),
        ];
        Assert.NotEmpty(attributedTargets);
        Assert.All(
            attributedTargets,
            target => Assert.Equal("netcore.app", target.PlatformPack));
        InvalidOperationException identityMismatch =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CallGraphExports.ExpandPlatformCallGraph(
                    "net11.0",
                    "InspectWeb.Engine.Tests",
                    "netcore.app",
                    "0.0.0.0",
                    selectedAssembly.Culture,
                    selectedAssembly.PublicKeyToken,
                    selected.Type.MetadataId,
                    selected.Member.Name,
                    selected.Member.GraphSelectorKey,
                    selected.Member.MetadataToken!.Value));
        Assert.Contains(
            "does not match the acquired assembly identity",
            identityMismatch.Message,
            StringComparison.Ordinal);

        await using BrowserPlatformScopeResolution qualifiedRuntime =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                "net11.0-browser",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        Assert.Single(qualifiedRuntime.Scope.Members);
        BrowserCallGraph lazySelectorGraph =
            Assert.IsType<BrowserCallGraph>(
                JsonSerializer.Deserialize(
                    await CallGraphExports.ExpandPlatformCallGraph(
                        "net11.0-browser",
                        "InspectWeb.Engine.Tests",
                        "netcore.app",
                        selectedAssembly.Version,
                        selectedAssembly.Culture,
                        selectedAssembly.PublicKeyToken,
                        selected.Type.MetadataId,
                        selected.Member.Name,
                        selected.Member.GraphSelectorKey,
                        metadataToken: 0),
                    BrowserCallGraphJsonContext.Default.BrowserCallGraph));
        Assert.Equal(2, lazySelectorGraph.Scope.Assemblies);

        await qualifiedRuntime.DisposeAsync();
        await expanded.DisposeAsync();
        using (await BrowserPackageWorkspace.ReservePackageDownloadAsync(
            "platform.eviction@1.0.0",
            128L * MiB))
        {
            Assert.False(
                BrowserPackageWorkspace.IsScopeRetained(expanded.Scope));
        }
        Assert.Throws<ObjectDisposedException>(
            () => expanded.Scope.Members);
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
                    PackageExports.ProjectPlatformSurface(
                        resolution),
                    BrowserPackageJsonContext.Default.BrowserPackageSurface));
        BrowserPackageOpportunities opportunities =
            Assert.IsType<BrowserPackageOpportunities>(
                JsonSerializer.Deserialize(
                    await AnalysisExports
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

    [Fact]
    public void MemberCallGraphRequests_PreserveContextOrderAndLocateNonFirstRoot()
    {
        (BrowserPackageRequest[] requests, int rootIndex) =
            CallGraphExports.MemberCallGraphRequests(
                "Root.Package",
                "1.0.0",
                "net11.0",
                """
                [
                  {
                    "package": "Binding.First",
                    "version": "2.0.0",
                    "framework": "net11.0"
                  },
                  {
                    "package": "Root.Package",
                    "version": "1.0.0",
                    "framework": "net11.0"
                  }
                ]
                """);

        Assert.Equal(1, rootIndex);
        Assert.Collection(
            requests,
            first => Assert.Equal("Binding.First", first.PackageId),
            root => Assert.Equal("Root.Package", root.PackageId));
    }

    [Fact]
    public void MemberCallGraphRequests_RequireOneRootInExpandedContext()
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => CallGraphExports.MemberCallGraphRequests(
                "Root.Package",
                "1.0.0",
                "net11.0",
                """
                [
                  {
                    "package": "Other.Package",
                    "version": "2.0.0",
                    "framework": "net11.0"
                  }
                ]
                """));

        Assert.Contains(
            "active package coordinate exactly once",
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryMemberCallGraph_RejectsCollapsedContextCoordinates()
    {
        byte[] rootImage =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] duplicateImage =
            File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location);
        _ = await Coordinate(
            "CallGraph.Root",
            Package(rootImage, "lib/net11.0/CallGraph.Root.dll"));
        _ = await Coordinate(
            "CallGraph.Duplicate",
            Package(
                duplicateImage,
                "lib/net11.0/CallGraph.Duplicate.dll"));

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CallGraphExports.QueryMemberCallGraph(
                    "CallGraph.Root",
                    "1.0.0",
                    "net11.0",
                    "CallGraph.Root.dll",
                    "T:Example.Root",
                    "T:Example.Root",
                    "Run",
                    "void Run()",
                    "Run|",
                    0,
                    """
                    [
                      {
                        "package": "CallGraph.Root",
                        "version": "1.0.0",
                        "framework": "net11.0"
                      },
                      {
                        "package": "CallGraph.Duplicate",
                        "version": "1.0.0",
                        "framework": "net11.0"
                      },
                      {
                        "package": "CallGraph.Duplicate",
                        "version": "1.0.0",
                        "framework": "net11.0"
                      }
                    ]
                    """));

        Assert.Contains(
            "distinct package coordinates",
            failure.Message,
            StringComparison.Ordinal);

        using var pressure =
            await BrowserPackageWorkspace.ReservePackageDownloadAsync(
                $"call-graph.after-failure.{Guid.NewGuid():N}@1.0.0",
                128L * MiB);
        Assert.Equal(128L * MiB, BrowserPackageWorkspace.Stats().ResidentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HomeDemo_ReleasesScopeAfterQuery(bool missingType)
    {
        byte[] image = File.ReadAllBytes(
            missingType
                ? typeof(BrowserEngineBoundaryTests).Assembly.Location
                : typeof(JsonSerializer).Assembly.Location);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                "System.Text.Json",
                "10.0.0",
                Package(image, "lib/net10.0/System.Text.Json.dll"),
                fromCache: false));

        if (missingType)
        {
            InvalidOperationException failure =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => CatalogExports.RunHomeDemo(ProductDemoIds.StjSerializer));
            Assert.Contains(
                "resolved to 0 browser surface rows",
                failure.Message,
                StringComparison.Ordinal);
        }
        else
        {
            BrowserHomeDemoRunResult result =
                Assert.IsType<BrowserHomeDemoRunResult>(
                    JsonSerializer.Deserialize(
                        await CatalogExports.RunHomeDemo(ProductDemoIds.StjSerializer),
                        BrowserCatalogJsonContext.Default.BrowserHomeDemoRunResult));
            Assert.True(result.Found);
            Assert.Equal("System.Text.Json", Assert.Single(result.Packages).Package);
        }

        using var pressure =
            await BrowserPackageWorkspace.ReservePackageDownloadAsync(
                $"home-demo.after-query.{Guid.NewGuid():N}@1.0.0",
                128L * MiB);
        Assert.Equal(128L * MiB, BrowserPackageWorkspace.Stats().ResidentBytes);
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

    [Fact]
    public void PackageDocumentDiscovery_UsesOneCachedEntryManifestAtTheLimit()
    {
        const int maxEntries = 4_096;
        var package = new BrowserPackage(
            "Document.Limit",
            "1.0.0",
            PackageDocuments(maxEntries),
            fromCache: false);

        IReadOnlyList<BrowserPackageDocumentEntry> documents = package.Documents();

        Assert.Equal(maxEntries, documents.Count);
        Assert.Same(
            package.Content.EnumerateEntriesWithLengths(),
            package.Content.EnumerateEntriesWithLengths());
    }

    [Fact]
    public void PackageIcon_ProjectsOnlyTheBoundedEmbeddedAsset()
    {
        const string packageId = "Icon.Package";
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        var package = new BrowserPackage(
            packageId,
            "1.0.0",
            PackageEntries(
                ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                    $"""
                    <package>
                      <metadata>
                        <id>{packageId}</id>
                        <version>1.0.0</version>
                        <authors>Example</authors>
                        <description>Example</description>
                        <icon>images\icon.png</icon>
                        <iconUrl>https://example.test/legacy.png</iconUrl>
                      </metadata>
                    </package>
                    """)),
                ("images/icon.png", png)),
            fromCache: false);

        Assert.NotNull(package.Icon);
        BrowserPackageIconPayload icon = package.Icon;

        Assert.Equal("image/png", icon.MediaType);
        Assert.Equal(png, Convert.FromBase64String(icon.Base64));
        Assert.DoesNotContain("example.test", icon.Base64, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageIcon_UsesNoRemoteManifestFallback()
    {
        const string packageId = "Legacy.Icon.Package";
        var package = new BrowserPackage(
            packageId,
            "1.0.0",
            PackageEntries(
                ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                    $"""
                    <package>
                      <metadata>
                        <id>{packageId}</id>
                        <version>1.0.0</version>
                        <authors>Example</authors>
                        <description>Example</description>
                        <iconUrl>https://example.test/legacy.png</iconUrl>
                      </metadata>
                    </package>
                    """))),
            fromCache: false);

        Assert.Null(package.Icon);
    }

    [Fact]
    public void PackageWireProjection_PreservesCoreValues()
    {
        var stats = new BrowserPackageCacheSnapshot(1, 2, 3, 4);
        var entry = new BrowserPackageDocumentEntry(
            "skill",
            "Inspect",
            "skills/inspect/SKILL.md",
            5);
        var payload = new BrowserPackageDocumentPayload(
            entry.Kind,
            entry.Name,
            entry.Path,
            "# Inspect");
        var icon = new BrowserPackageIconPayload(
            "image/png",
            "cG5n");

        Assert.Equal(
            new BrowserPackageCacheStats(1, 2, 3, 4),
            BrowserPackageWireProjection.Project(stats));
        Assert.Equal(
            [
                new BrowserPackageDocument(
                    entry.Kind,
                    entry.Name,
                    entry.Path,
                    entry.Size),
            ],
            BrowserPackageWireProjection.Project([entry]));
        Assert.Equal(
            new BrowserPackageDocumentContent(
                payload.Kind,
                payload.Name,
                payload.Path,
                payload.Text),
            BrowserPackageWireProjection.Project(payload));
        Assert.Equal(
            new BrowserPackageIcon(
                icon.MediaType,
                icon.Base64),
            BrowserPackageWireProjection.Project(icon));
        Assert.Null(BrowserPackageWireProjection.Project(
            (BrowserPackageIconPayload?)null));
    }

    [Fact]
    public void XmlDocumentation_DuplicateParametersUseTheLastCompilerEntry()
    {
        const string xml = """
            <doc>
              <members>
                <member name="M:Example.M(System.Int32)">
                  <summary>Summary</summary>
                  <param name="value">first</param>
                  <param name="value">second</param>
                </member>
              </members>
            </doc>
            """;

        BrowserMemberDocumentation documentation = BrowserXmlDocumentation.Read(
            System.Text.Encoding.UTF8.GetBytes(xml),
            "M:Example.M(System.Int32)");

        Assert.Equal("Summary", documentation.Summary);
        Assert.Equal("second", Assert.Single(documentation.Parameters).Value);
    }

    [Fact]
    public void XmlDocumentation_AcceptsTheDepthLimitAndRejectsTheNextElement()
    {
        BrowserMemberDocumentation accepted = BrowserXmlDocumentation.Read(
            System.Text.Encoding.UTF8.GetBytes(
                NestedDocumentation(CSharpText.XmlDocText.MaxElementDepth)),
            "M:Example.M");

        Assert.Equal("x", accepted.Summary);

        XmlException failure = Assert.Throws<XmlException>(
            () => BrowserXmlDocumentation.Read(
                System.Text.Encoding.UTF8.GetBytes(
                    NestedDocumentation(CSharpText.XmlDocText.MaxElementDepth + 1)),
                "M:Example.M"));

        Assert.Contains("supported element depth", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnconstrainedDependencyNavigation_SelectsLatestStableVersion()
    {
        Assert.Equal(
            "3.0.0",
            BrowserPackageWorkspace.SelectDependencyVersion(
                ["1.0.0", "3.1.0-preview.1", "3.0.0"],
                declaredRange: ""));
        Assert.Equal(
            "2.1.0",
            BrowserPackageWorkspace.SelectDependencyVersion(
                ["1.0.0", "2.0.0", "2.1.0", "3.0.0"],
                declaredRange: "2.*"));
    }

    [Fact]
    public void DependencyCoordinateMatch_PreservesProductOwnedProvenanceAndCardinality()
    {
        var platform = new BrowserDependencyCoordinateCandidate(
            "platform",
            BrowserDependencyCoordinateProvenance.PlatformRuntime,
            "Microsoft.NETCore.App",
            "10.0.10",
            "net10.0");
        var package = new BrowserDependencyCoordinateCandidate(
            "package",
            BrowserDependencyCoordinateProvenance.NuGetPackage,
            "Microsoft.NETCore.App",
            "2.2.8",
            "netcoreapp1.0");

        BrowserDependencyCoordinateMatch noMatch = MatchDependencyCoordinate(
            [platform],
            "Microsoft.NETCore.App",
            "1.0.5");
        BrowserDependencyCoordinateMatch unique = MatchDependencyCoordinate(
            [platform, package],
            "Microsoft.NETCore.App",
            "1.0.5");
        BrowserDependencyCoordinateMatch ambiguous = MatchDependencyCoordinate(
            [
                platform,
                package,
                package with { Key = "package-other-framework", TargetFramework = "net8.0" },
            ],
            "Microsoft.NETCore.App",
            "1.0.5");

        Assert.Equal(BrowserDependencyCoordinateMatchOutcome.NoMatch, noMatch.Outcome);
        Assert.Null(noMatch.CandidateKey);
        Assert.Equal(BrowserDependencyCoordinateMatchOutcome.Unique, unique.Outcome);
        Assert.Equal("package", unique.CandidateKey);
        Assert.Equal(BrowserDependencyCoordinateMatchOutcome.Ambiguous, ambiguous.Outcome);
        Assert.Null(ambiguous.CandidateKey);
    }

    [Fact]
    public void BuildIdentity_ReadsHostAssemblyAttributes()
    {
        Assembly assembly = typeof(InspectionEngine).Assembly;
        AssemblyInformationalVersionAttribute? informationalVersion =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        BrowserBuildIdentity identity =
            BrowserBuildIdentityReader.Read(assembly);

        Assert.NotNull(informationalVersion);
        Assert.Equal(
            informationalVersion.InformationalVersion.Split('+', 2)[0],
            identity.Version);
    }

    [Fact]
    public void BuildIdentity_UsesFileVersionWithoutInformationalVersion()
    {
        const string fileVersion = "2.3.4.5";
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("BrowserBuildIdentityFallback"),
            AssemblyBuilderAccess.Run);
        ConstructorInfo constructor =
            typeof(AssemblyFileVersionAttribute).GetConstructor([typeof(string)])!;
        assembly.SetCustomAttribute(
            new CustomAttributeBuilder(constructor, [fileVersion]));

        BrowserBuildIdentity identity = BrowserBuildIdentityReader.Read(assembly);

        Assert.Equal(fileVersion, identity.Version);
    }

    [Fact]
    public void BuildIdentity_UsesVersionedRepositoryProvenance()
    {
        const string commit = "0123456789abcdef0123456789abcdef01234567";

        BrowserBuildIdentity identity = BrowserBuildIdentityReader.Create(
            "0.18.0",
            commit,
            "https://github.com/richlander/dotnet-inspect",
            "2026-08-14T23:30:22Z");

        Assert.Equal("0.18.0", identity.Version);
        Assert.Equal(commit, identity.Commit);
        Assert.Equal("2026-08-14T23:30:22.0000000+00:00", identity.BuiltAtUtc);
        Assert.Equal(
            $"https://github.com/richlander/dotnet-inspect/commit/{commit}",
            identity.CommitUrl);
    }

    [Fact]
    public void BuildIdentity_DropsInvalidOptionalProvenance()
    {
        BrowserBuildIdentity identity = BrowserBuildIdentityReader.Create(
            "0.18.0",
            "not-a-commit",
            "javascript:alert(1)",
            "not-a-time");

        Assert.Null(identity.Commit);
        Assert.Null(identity.BuiltAtUtc);
        Assert.Null(identity.CommitUrl);
    }

    [Fact]
    public async Task WorkspaceBinding_RejectsPackageParticipantsForPlatformScope()
    {
        byte[] image = File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await Coordinate(
            "Platform.Confusable",
            Package(image, "lib/net11.0/Platform.Confusable.dll"));
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync([coordinate], TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserWorkspaceParticipant participant =
            Assert.Single(scope.SurfaceParticipants);
        AssemblyBindingSelection any =
            participant.Participant.BindingPolicy.Select(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(
                        participant.Assembly.Identity),
                    AssemblyBindingOrigin.FromAssembly(
                        participant.Assembly),
                    AssemblyResolutionScope.Any)).Selection;
        AssemblyBindingSelection platform =
            participant.Participant.BindingPolicy.Select(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(
                        participant.Assembly.Identity),
                    AssemblyBindingOrigin.FromAssembly(
                        participant.Assembly),
                    AssemblyResolutionScope.Platform)).Selection;

        Assert.Same(
            participant.Assembly,
            Assert.IsType<AssemblyBindingSelection.Selected>(any).Assembly);
        Assert.IsType<AssemblyBindingSelection.Missing>(platform);
    }

    [Fact]
    public async Task WorkspaceBinding_RejectsEquivalentAssemblyIdentities()
    {
        byte[] image = File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate first = await Coordinate(
            "Identity.Collision.A",
            Package(image, "lib/net11.0/Identity.Collision.A.dll"));
        BrowserPackageCoordinate second = await Coordinate(
            "Identity.Collision.B",
            Package(image, "lib/net11.0/Identity.Collision.B.dll"));

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await BrowserInspectionScope.CreateAsync(
                    [first, second],
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "same assembly identity",
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImplementationPairing_RequiresEquivalentAssemblyIdentity()
    {
        byte[] surfaceImage =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] differentImage =
            File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location);
        BrowserPackageCoordinate mismatched = await Coordinate(
            "Identity.Mismatch",
            PackagePair(surfaceImage, differentImage, "Identity.Pair.dll"));

        PackageAssemblyRoleCorrespondenceException failure =
            await Assert.ThrowsAsync<PackageAssemblyRoleCorrespondenceException>(
                async () => await BrowserPackageWorkspace.OpenScopeAsync(
                    [mismatched],
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "different assembly identities",
            failure.Message,
            StringComparison.Ordinal);

        BrowserPackageCoordinate equivalent = await Coordinate(
            "Identity.Equivalent",
            PackagePair(surfaceImage, surfaceImage, "Identity.Pair.dll"));
        await using BrowserScopeLease<BrowserInspectionScope> equivalentScopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync([equivalent], TestContext.Current.CancellationToken);
        BrowserInspectionScope equivalentScope = equivalentScopeLease.Scope;
        BrowserWorkspaceParticipant equivalentSurface =
            Assert.Single(equivalentScope.SurfaceParticipants);

        Assert.NotNull(
            equivalentScope.ImplementationParticipant(equivalentSurface));
    }

    [Fact]
    public async Task WorkspaceDisposal_ClosesWorkspaceAfterRoleFailure()
    {
        byte[] image =
            File.ReadAllBytes(
                typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await Coordinate(
            "Dispose.Roles",
            PackagePair(image, image, "Dispose.Roles.dll"));
        var scope = await BrowserInspectionScope.CreateAsync([coordinate], TestContext.Current.CancellationToken);
        AssemblyContextGroup implementation =
            scope.UseImplementation(group => group);
        MethodInfo registerOwnedResource =
            typeof(AssemblyContextGroup).GetMethod(
                "RegisterOwnedResource",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "AssemblyContextGroup.RegisterOwnedResource was not found.");
        registerOwnedResource.Invoke(
            implementation,
            [new ThrowingResource("browser role disposal failed")]);

        AggregateException failure =
            await Assert.ThrowsAsync<AggregateException>(
                async () => await scope.DisposeAsync());

        Assert.Contains(
            failure.Flatten().InnerExceptions,
            ex => ex.Message == "browser role disposal failed");
        FieldInfo field =
            typeof(BrowserInspectionScope).GetField(
                "_workspace",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "BrowserInspectionScope._workspace was not found.");
        var workspace =
            Assert.IsType<InspectionWorkspace>(field.GetValue(scope));
        Assert.Throws<ObjectDisposedException>(
            () => workspace.CreateAssemblyContextGroup(
                [scope.SurfaceParticipants[0].Participant]));
    }

    [Fact]
    public void CallGraphDiagnostics_PreserveIncompleteProductEvidence()
    {
        BrowserCallGraphDiagnostics diagnostics =
            BrowserCallGraphWireProjection.Project(
                BrowserCallGraphProjection.Diagnostics(
                    new CatalogCallGraphDiagnostics(2, 3, 4),
                    hasUnexploredTraversalBoundary: true,
                    hasAnalysisFailureBoundary: true));

        Assert.True(diagnostics.IsIncomplete);
        Assert.Equal(2, diagnostics.IncompleteNodes);
        Assert.Equal(3, diagnostics.IncompleteEdges);
        Assert.Equal(4, diagnostics.BindingIdentityConflicts);
        Assert.True(diagnostics.HasUnexploredTraversalBoundary);
        Assert.True(diagnostics.HasAnalysisFailureBoundary);
    }

    [Fact]
    public void SurfaceProjection_UsesExactMetadataTypeIdentityForBrowserKeys()
    {
        MetadataTypeDefinitionName nestedName = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Outer", "Inner"]))
            .Name;
        var type = new ApiType
        {
            Namespace = "Sample",
            Name = "Outer.Inner",
            MetadataName = "Outer+Inner",
            DefinitionName = nestedName,
            Kind = "class",
        };

        BrowserTypeSurfaceInfo projected = BrowserSurfaceProjection.Type(
            type,
            "Physical.dll",
            "asset:physical",
            "Sample");

        Assert.Equal("Sample.Outer+Inner", projected.Id);
        Assert.Equal("Physical.dll", projected.Assembly);
        Assert.Equal("asset:physical", projected.AssemblyId);
        Assert.Equal("Sample", projected.AssemblyName);
        Assert.Equal(projected.Id, projected.DefinitionId);
        Assert.Equal("Sample.Outer.Inner", projected.QueryId);
        Assert.Equal(projected.Id, projected.MetadataId);

        var literalPlus = new ApiType
        {
            Namespace = "Sample",
            Name = "Outer+Inner",
            MetadataName = "Outer+Inner",
            DefinitionName = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Outer+Inner"]))
                .Name,
            Kind = "class",
        };
        BrowserTypeSurfaceInfo projectedLiteral =
            BrowserSurfaceProjection.Type(
                literalPlus,
                "Physical.dll",
                "asset:physical",
                "Sample");

        Assert.Equal(@"Sample.Outer\+Inner", projectedLiteral.Id);
        Assert.Equal(projectedLiteral.Id, projectedLiteral.DefinitionId);
        Assert.NotEqual(projected.Id, projectedLiteral.Id);
        Assert.Equal("Sample.Outer+Inner", projectedLiteral.QueryId);
        Assert.Equal(projected.MetadataId, projectedLiteral.MetadataId);

        BrowserTypeSurfaceInfo qualified = projected with { Id = $"Sample.dll:{projected.Id}" };
        Assert.NotEqual(qualified.Id, qualified.DefinitionId);
        Assert.Equal(projected.DefinitionId, qualified.DefinitionId);
    }

    [Fact]
    public void SurfaceProjection_LongDeclaringTypeStopsIncrementally()
    {
        var type = new ApiType
        {
            Namespace = new string('N', 4_000),
            Name = "Amplifier",
            Kind = "class",
            Members =
            [
                .. Enumerable.Range(0, 10_000).Select(index => new ApiMember
                {
                    Name = $"M{index}",
                    Kind = "method",
                    Signature = $"void M{index}()",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "void",
                        MemberName = $"M{index}",
                    },
                }),
            ],
        };
        var budget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(8_000_000);
        budget.BeginParticipant();
        long before = GC.GetAllocatedBytesForCurrentThread();

        Assert.Throws<BrowserSurfaceProjection.BrowserSurfaceTextBoundExceededException>(
            () => BrowserSurfaceProjection.Type(
                type,
                "Amplifier.dll",
                "asset:amplifier",
                "Amplifier",
                budget));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(
            allocated < 64L * MiB,
            $"bounded Browser projection allocated {allocated:N0} bytes");
    }

    [Fact]
    public void SurfaceProjection_OneHugeTypeStopsBeforeDerivedIdentities()
    {
        var type = new ApiType
        {
            Namespace = new string('N', 4_000_000),
            Name = "Amplifier",
            MetadataName = "Amplifier",
            Kind = "class",
        };
        var budget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(32_000_000);
        budget.BeginParticipant();
        long before = GC.GetAllocatedBytesForCurrentThread();

        Assert.Throws<BrowserSurfaceProjection.BrowserSurfaceTextBoundExceededException>(
            () => BrowserSurfaceProjection.Type(
                type,
                "Amplifier.dll",
                "asset:amplifier",
                "Amplifier",
                budget));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(
            allocated < 4L * MiB,
            $"Browser projection preflight allocated {allocated:N0} bytes");
    }

    [Fact]
    public void SurfaceProjection_OneHugeMemberStopsBeforeDerivedIdentities()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Amplifier",
            MetadataName = "Amplifier",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "M",
                    Kind = "method",
                    Signature = new string('S', 4_000_000),
                },
            ],
        };
        var budget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(32_000_000);
        budget.BeginParticipant();
        long before = GC.GetAllocatedBytesForCurrentThread();

        Assert.Throws<BrowserSurfaceProjection.BrowserSurfaceTextBoundExceededException>(
            () => BrowserSurfaceProjection.Type(
                type,
                "Amplifier.dll",
                "asset:amplifier",
                "Amplifier",
                budget));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(
            allocated < 4L * MiB,
            $"Browser projection preflight allocated {allocated:N0} bytes");
    }

    [Fact]
    public void SurfaceProjection_OneHugeExactMemberStopsBeforeDerivedIdentities()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Amplifier",
            MetadataName = "Amplifier",
            Kind = "class",
        };
        var member = new ApiMember
        {
            Name = "M",
            Kind = "method",
            Signature = new string('S', 4_000_000),
        };
        var budget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(32_000_000);
        budget.BeginParticipant();
        long before = GC.GetAllocatedBytesForCurrentThread();

        Assert.Throws<BrowserSurfaceProjection.BrowserSurfaceTextBoundExceededException>(
            () => BrowserSurfaceProjection.Member(type, member, budget));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(
            allocated < 4L * MiB,
            $"Browser exact-member projection preflight allocated {allocated:N0} bytes");
    }

    [Fact]
    public void SurfaceProjection_PreflightUsesTheRemainingSharedBudget()
    {
        var budget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(1_000_000);
        budget.BeginParticipant();
        _ = BrowserSurfaceProjection.Type(
            new ApiType
            {
                Namespace = new string('C', 10_000),
                Name = "Committed",
                MetadataName = "Committed",
                Kind = "class",
            },
            "Committed.dll",
            "asset:committed",
            "Committed",
            budget);
        budget.CommitParticipant();
        Assert.True(budget.CommittedCharacters > 40_000);

        budget.BeginParticipant();
        Assert.Throws<BrowserSurfaceProjection.BrowserSurfaceTextBoundExceededException>(
            () => BrowserSurfaceProjection.Type(
                new ApiType
                {
                    Namespace = new string('P', 80_000),
                    Name = "Pending",
                    MetadataName = "Pending",
                    Kind = "class",
                },
                "Pending.dll",
                "asset:pending",
                "Pending",
                budget));
    }

    [Fact]
    public async Task QueryPackage_ToolsPointerRetainsRootAndManifestDependencies()
    {
        const string packageId = "Tool.Pointer";
        byte[] package = PackageEntries(
            ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>Tool.Pointer</id>
                    <version>1.0.0</version>
                    <dependencies>
                      <group targetFramework="net11.0">
                        <dependency id="Tool.Payload" version="[1.0.0]" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """)),
            ("README.md", Encoding.UTF8.GetBytes("# Tool Pointer")),
            ("tools/net11.0/any/Tool.Pointer.dll", [0x01]));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(packageId, "1.0.0", package, fromCache: false));

        await using BrowserScopeLease<BrowserInspectionScope> rootScopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                "1.0.0",
                "net11.0",
                TestContext.Current.CancellationToken);
        BrowserInspectionScope rootScope = rootScopeLease.Scope;
        BrowserPackageCoordinate coordinate = Assert.Single(rootScope.Coordinates);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoCompileAssets,
            coordinate.Root.AssetSelection.Status);
        Assert.Equal(
            coordinate.Package.Content.FromCache,
            coordinate.Root.FromCache);
        Assert.Equal(
            coordinate.Package.Content.ProducerKey,
            coordinate.Root.ProducerKey);
        Assert.True(
            coordinate.Root.ReferencesContent(coordinate.Package.Content));
        Assert.Empty(rootScope.SurfaceParticipants);
        Assert.Empty(rootScope.ImplementationParticipants);

        BrowserPackageSurface surface = Assert.IsType<BrowserPackageSurface>(
            JsonSerializer.Deserialize(
                await PackageExports.QueryPackage(
                    packageId,
                    "1.0.0",
                    "net11.0"),
                BrowserPackageJsonContext.Default.BrowserPackageSurface));

        Assert.Equal(
            BrowserCompileLibraryStatus.NoCompileAssets,
            surface.CompileLibrary.Status);
        Assert.Null(surface.CompileLibrary.TargetFramework);
        Assert.Null(surface.DefaultAssemblyId);
        Assert.Empty(surface.Assemblies);
        Assert.Empty(surface.Types);
        Assert.Empty(surface.Accessibility);
        Assert.Equal("README.md", Assert.Single(surface.Documents).Path);
        Assert.Empty(surface.InspectionErrors);
        Assert.Null(surface.InspectionError);

        BrowserPackageDependencies dependencies =
            Assert.IsType<BrowserPackageDependencies>(
                JsonSerializer.Deserialize(
                    await PackageExports.QueryPackageDependencies(
                        packageId,
                        "1.0.0",
                        "net11.0",
                        assemblyId: ""),
                    BrowserPackageJsonContext.Default.BrowserPackageDependencies));
        Assert.Null(dependencies.Assembly);
        Assert.Empty(dependencies.AssemblyReferences);
        Assert.Equal(
            BrowserCompileLibraryStatus.NoCompileAssets,
            dependencies.CompileLibrary.Status);
        Assert.Equal(
            dependencies.CompileLibrary.Message,
            dependencies.AssemblyReferenceError);
        BrowserPackageDependency dependency = Assert.Single(
            Assert.Single(dependencies.DependencyGroups).Dependencies);
        Assert.Equal("Tool.Payload", dependency.Id);
        InvalidOperationException metadataTableFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => MetadataExports.QueryPackageMetadataTable(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    assemblyFileName: "",
                    tableIndex: 0,
                    startRowId: 1,
                    maxRows: 1));
        Assert.Contains(
            nameof(PackageCompileAssetSelectionStatus.NoCompileAssets),
            metadataTableFailure.Message);
        await AssertRootOnlyAggregateStatus(
            packageId,
            "net11.0",
            BrowserCompileLibraryStatus.NoCompileAssets);
    }

    [Fact]
    public async Task QueryPackage_ExplicitEmptyCompileGroupRetainsTypedAbsence()
    {
        const string packageId = "Empty.Compile.Group";
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                PackageEntries(
                    ("ref/net11.0/_._", []),
                    ("lib/net11.0/Empty.Compile.Group.dll", [0x01])),
                fromCache: false));

        BrowserPackageSurface surface = Assert.IsType<BrowserPackageSurface>(
            JsonSerializer.Deserialize(
                await PackageExports.QueryPackage(
                    packageId,
                    "1.0.0",
                    "net11.0"),
                BrowserPackageJsonContext.Default.BrowserPackageSurface));

        Assert.Equal(
            BrowserCompileLibraryStatus.EmptyCompileGroup,
            surface.CompileLibrary.Status);
        Assert.Equal("net11.0", surface.CompileLibrary.TargetFramework);
        Assert.Null(surface.DefaultAssemblyId);
        Assert.Empty(surface.Assemblies);
        Assert.Empty(surface.Types);
        await AssertRootOnlyAggregateStatus(
            packageId,
            "net11.0",
            BrowserCompileLibraryStatus.EmptyCompileGroup);
    }

    [Fact]
    public async Task QueryPackage_NoMatchingFrameworkRetainsRequestedRoot()
    {
        const string packageId = "Future.Library";
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                PackageEntries(
                    ("lib/net11.0/Future.Library.dll",
                        File.ReadAllBytes(
                            typeof(BrowserEngineBoundaryTests).Assembly.Location))),
                fromCache: false));

        BrowserPackageSurface surface = Assert.IsType<BrowserPackageSurface>(
            JsonSerializer.Deserialize(
                await PackageExports.QueryPackage(
                    packageId,
                    "1.0.0",
                    "net10.0"),
                BrowserPackageJsonContext.Default.BrowserPackageSurface));

        Assert.Equal(
            BrowserCompileLibraryStatus.NoMatchingTargetFramework,
            surface.CompileLibrary.Status);
        Assert.Equal("net10.0", surface.ActiveFramework);
        Assert.Equal("net10.0", surface.CompileLibrary.TargetFramework);
        Assert.Null(surface.DefaultAssemblyId);
        Assert.Empty(surface.Assemblies);
        await AssertRootOnlyAggregateStatus(
            packageId,
            "net10.0",
            BrowserCompileLibraryStatus.NoMatchingTargetFramework);
    }

    static async Task AssertRootOnlyAggregateStatus(
        string packageId,
        string framework,
        BrowserCompileLibraryStatus expectedStatus)
    {
        BrowserPackageMetadata metadata =
            Assert.IsType<BrowserPackageMetadata>(
                JsonSerializer.Deserialize(
                    await MetadataExports.QueryPackageMetadata(
                        packageId,
                        "1.0.0",
                        framework),
                    BrowserMetadataJsonContext.Default.BrowserPackageMetadata));
        Assert.Empty(metadata.Assemblies);
        Assert.Null(metadata.InspectionError);
        // Each export assembly declares its own compile-library enum, and the wire
        // value is the member name, so the aggregate is compared by that name.
        Assert.Equal(expectedStatus.ToString(), metadata.CompileLibrary.Status.ToString());

        BrowserPackageIntegrations integrations =
            Assert.IsType<BrowserPackageIntegrations>(
                JsonSerializer.Deserialize(
                    await AnalysisExports.QueryPackageIntegrations(
                        packageId,
                        "1.0.0",
                        framework),
                    BrowserAnalysisJsonContext.Default.BrowserPackageIntegrations));
        Assert.Empty(integrations.Categories);
        Assert.Equal(0, integrations.TotalSignals);
        Assert.False(integrations.IsComplete);
        Assert.Null(integrations.InspectionError);
        Assert.Equal(expectedStatus.ToString(), integrations.CompileLibrary.Status.ToString());

        BrowserPackageOpportunities opportunities =
            Assert.IsType<BrowserPackageOpportunities>(
                JsonSerializer.Deserialize(
                    await AnalysisExports.QueryPackageOpportunities(
                        packageId,
                        "1.0.0",
                        framework),
                    BrowserAnalysisJsonContext.Default.BrowserPackageOpportunities));
        Assert.Empty(opportunities.Categories);
        Assert.Equal(0, opportunities.TotalOpportunities);
        Assert.False(opportunities.IsComplete);
        Assert.Null(opportunities.InspectionError);
        Assert.Equal(expectedStatus.ToString(), opportunities.CompileLibrary.Status.ToString());

        BrowserPackagePerformance performance =
            Assert.IsType<BrowserPackagePerformance>(
                JsonSerializer.Deserialize(
                    await AnalysisExports.QueryPackagePerformance(
                        packageId,
                        "1.0.0",
                        framework),
                    BrowserAnalysisJsonContext.Default.BrowserPackagePerformance));
        Assert.Empty(performance.Members);
        Assert.Equal(0, performance.TotalOpportunities);
        Assert.Null(performance.InspectionError);
        Assert.Equal(expectedStatus.ToString(), performance.CompileLibrary.Status.ToString());
    }

    [Fact]
    public async Task QueryPackage_FirstTransportTruncationReturnsTypedNotice()
    {
        const string packageId = "First.Transport.Truncation";
        byte[] image = BuildTransportAmplificationImage(
            packageId,
            typeCount: 10_000,
            namespaceLength: 1_000);
        _ = await Coordinate(
            packageId,
            Package(image, $"lib/net11.0/{packageId}.dll"));

        string json = await PackageExports.QueryPackage(
            packageId,
            "1.0.0",
            "net11.0");
        BrowserPackageSurface surface = Assert.IsType<BrowserPackageSurface>(
            JsonSerializer.Deserialize(
                json,
                BrowserPackageJsonContext.Default.BrowserPackageSurface));

        Assert.Empty(surface.Assemblies);
        Assert.NotEmpty(surface.Accessibility);
        string inspectionError = Assert.Single(surface.InspectionErrors);
        Assert.Contains(
            "truncated",
            inspectionError,
            StringComparison.Ordinal);
        Assert.Equal(surface.InspectionError, inspectionError);
    }

    [Fact]
    public void SurfaceProjection_QualifiedCollisionIdIsAccountedBeforeCommit()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Value",
            MetadataName = "Value",
            Kind = "class",
        };
        const string assembly = "Collision.Assembly.dll";
        var unqualifiedBudget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(10_000);
        unqualifiedBudget.BeginParticipant();
        _ = BrowserSurfaceProjection.Type(
            type,
            assembly,
            "asset:collision",
            "Collision.Assembly",
            unqualifiedBudget);
        unqualifiedBudget.CommitParticipant();

        var qualifiedBudget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(10_000);
        qualifiedBudget.BeginParticipant();
        BrowserTypeSurfaceInfo qualified = BrowserSurfaceProjection.Type(
            type,
            assembly,
            "asset:collision",
            "Collision.Assembly",
            qualifiedBudget,
            qualifyId: true);
        qualifiedBudget.CommitParticipant();

        Assert.Equal($"{assembly}:{qualified.DefinitionId}", qualified.Id);
        Assert.Equal(
            unqualifiedBudget.CommittedCharacters + assembly.Length + 1,
            qualifiedBudget.CommittedCharacters);
    }

    [Fact]
    public void ApiSurfacePolicy_AcceptsCoreLibraryAtEveryBrowserScope()
    {
        using var stream = File.OpenRead(typeof(object).Assembly.Location);
        using var reader = new PEReader(stream);

        foreach (ApiSurfaceExtractionScope scope in
            new[]
            {
                ApiSurfaceExtractionScope.PublicWithNonPublicTypes,
                ApiSurfaceExtractionScope.IncludeAll,
            })
        {
            stream.Position = 0;
            var extracted = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
                ApiSurfaceExtractor.ExtractBounded(
                    reader,
                    scope,
                    new ApiSurfaceExtractionBounds(
                        BrowserApiSurfacePolicy.MaxTypes,
                        BrowserApiSurfacePolicy.MaxMembers,
                        BrowserApiSurfacePolicy.MaxInspectionFailures,
                        BrowserApiSurfacePolicy.MaxTypeForwarders,
                        BrowserApiSurfacePolicy.MaxMetadataRows,
                        BrowserApiSurfacePolicy.MaxRetainedTextCharacters)));
            if (scope == ApiSurfaceExtractionScope.PublicWithNonPublicTypes)
            {
                var transportBudget =
                    new BrowserSurfaceProjection.BrowserSurfaceTextBudget(
                        BrowserApiSurfacePolicy.MaxRetainedTextCharacters);
                transportBudget.BeginParticipant();
                foreach (ApiType type in extracted.Surface.Types)
                {
                    BrowserSurfaceProjection.Type(
                        type,
                        "System.Private.CoreLib.dll",
                        "runtime:corelib",
                        "System.Private.CoreLib",
                        transportBudget);
                }
                transportBudget.CommitParticipant();
            }
        }
    }

    static string NestedDocumentation(int depth)
    {
        string nested = string.Concat(Enumerable.Repeat("<b>", depth));
        string close = string.Concat(Enumerable.Repeat("</b>", depth));
        return $"<doc><members><member name=\"M:Example.M\"><summary>{nested}x{close}</summary>"
            + "</member></members></doc>";
    }

    static byte[] BuildTransportAmplificationImage(
        string assemblyName,
        int typeCount,
        int namespaceLength)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        StringHandle @namespace =
            metadata.GetOrAddString(new string('N', namespaceLength));
        for (int index = 0; index < typeCount; index++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                @namespace,
                metadata.GetOrAddString($"T{index}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildEmptySurfaceImage(AssemblyName identity)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{identity.Name}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(identity.Name!),
            identity.Version ?? new Version(0, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    [Fact]
    public async Task PackageDependencies_UsesProductQueriesForManifestAndReferences()
    {
        const string packageId = "Browser.Dependency.Root";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] nupkg = PackageWithManifest(
            image,
            $"lib/net11.0/{packageId}.dll",
            $"""
             <package>
               <metadata>
                 <id>{packageId}</id>
                 <version>1.0.0</version>
                 <dependencies>
                   <group targetFramework=".NETCoreApp,Version=v11.0">
                     <dependency id="Browser.Dependency.Child" version="[2.0.0]" />
                   </group>
                 </dependencies>
               </metadata>
             </package>
             """);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                nupkg,
                fromCache: false));

        string json = await PackageExports.QueryPackageDependencies(
            packageId,
            "1.0.0",
            "net11.0",
            $"{packageId}.dll");

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(packageId, root.GetProperty("package").GetString());
        Assert.Equal("net11.0", root.GetProperty("activeFramework").GetString());
        JsonElement group = Assert.Single(
            root.GetProperty("dependencyGroups").EnumerateArray());
        Assert.Equal(0, group.GetProperty("index").GetInt32());
        Assert.True(group.GetProperty("isActive").GetBoolean());
        JsonElement dependency = Assert.Single(
            group.GetProperty("dependencies").EnumerateArray());
        Assert.Equal(
            "Browser.Dependency.Child",
            dependency.GetProperty("id").GetString());
        JsonElement reference = Assert.Single(
            root.GetProperty("assemblyReferences").EnumerateArray(),
            reference =>
                reference.GetProperty("name").GetString() == "System.Runtime");
        Assert.Equal("11.0.0.0", reference.GetProperty("version").GetString());
        Assert.True(reference.TryGetProperty("culture", out JsonElement culture));
        Assert.True(
            culture.ValueKind is JsonValueKind.Null or JsonValueKind.String);
        Assert.False(string.IsNullOrWhiteSpace(
            reference.GetProperty("publicKeyToken").GetString()));
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("dependencyGroupError").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("assemblyReferenceError").ValueKind);
    }

    [Fact]
    public async Task PackageDependencies_BlankDeclaredFrameworkDoesNotAbortProjection()
    {
        string packageId = $"Blank.Dependency.Framework.{Guid.NewGuid():N}";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] nupkg = PackageWithManifest(
            image,
            $"lib/net11.0/{packageId}.dll",
            $"""
             <package>
               <metadata>
                 <id>{packageId}</id>
                 <version>1.0.0</version>
                 <dependencies>
                   <group targetFramework="net11.0">
                     <dependency id="Valid.Dependency" version="[1.0.0]" />
                   </group>
                   <group targetFramework="" />
                 </dependencies>
               </metadata>
             </package>
             """);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                nupkg,
                fromCache: false));

        BrowserPackageDependencies dependencies =
            Assert.IsType<BrowserPackageDependencies>(
                JsonSerializer.Deserialize(
                    await PackageExports.QueryPackageDependencies(
                        packageId,
                        "1.0.0",
                        "net11.0",
                        $"{packageId}.dll"),
                    BrowserPackageJsonContext.Default.BrowserPackageDependencies));

        Assert.Equal(2, dependencies.DependencyGroups.Length);
        Assert.Equal("net11.0", dependencies.DependencyGroups[0].Framework);
        Assert.Equal("any", dependencies.DependencyGroups[1].Framework);
        Assert.Equal(
            "Valid.Dependency",
            Assert.Single(dependencies.DependencyGroups[0].Dependencies).Id);
        Assert.Equal(
            BrowserCompileLibraryStatus.Selected,
            dependencies.CompileLibrary.Status);
    }

    [Fact]
    public async Task PackagePerformance_UsesProductRankedWorkspaceAnalysis()
    {
        const string PackageId = "Browser.Performance.Root";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePair(
                    image,
                    image,
                    $"{PackageId}.DLL",
                    $"{PackageId}.dll"),
                fromCache: false));

        string surfaceJson = await PackageExports.QueryPackage(
            PackageId,
            "1.0.0",
            "net11.0");
        string json = await AnalysisExports.QueryPackagePerformance(
            PackageId,
            "1.0.0",
            "net11.0");

        using JsonDocument surfaceDocument =
            JsonDocument.Parse(surfaceJson);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.True(
            root.GetProperty("totalOpportunities").GetInt32() > 0);
        JsonElement member = Assert.Single(
            root.GetProperty("members").EnumerateArray(),
            candidate =>
                candidate.GetProperty("memberName").GetString()
                == nameof(PerformanceBoxingProbe));
        Assert.Equal(
            typeof(BrowserEngineBoundaryTests).FullName,
            member.GetProperty("typeId").GetString());
        Assert.Equal(
            [
                typeof(BrowserEngineBoundaryTests)
                    .GetMethod(nameof(PerformanceBoxingProbe))!
                    .MetadataToken,
            ],
            member.GetProperty("bodyTokens")
                .EnumerateArray()
                .Select(token => token.GetInt32()));
        Assert.StartsWith(
            $"{nameof(PerformanceBoxingProbe)}~",
            member.GetProperty("stableSelector").GetString());
        JsonElement surfaceType = Assert.Single(
            surfaceDocument.RootElement
                .GetProperty("types")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("definitionId").GetString()
                == member.GetProperty("typeId").GetString()
                && candidate.GetProperty("assembly").GetString()
                == member.GetProperty("assembly").GetString());
        Assert.Contains(
            surfaceType.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("stableSelector").GetString()
                == member.GetProperty("stableSelector").GetString());
        Assert.Contains(
            member.GetProperty("shapes").EnumerateArray(),
            shape => shape.GetString() == "box-value-type");
        Assert.True(
            member.GetProperty("opportunityCount").GetInt32()
            > 0);
        Assert.True(
            !root.TryGetProperty(
                "inspectionError",
                out JsonElement inspectionError)
            || inspectionError.ValueKind == JsonValueKind.Null);

        JsonElement property = Assert.Single(
            root.GetProperty("members").EnumerateArray(),
            candidate =>
                candidate.GetProperty("memberName").GetString()
                == nameof(PerformanceBoxingProperty));
        Assert.StartsWith(
            $"{nameof(PerformanceBoxingProperty)}~",
            property.GetProperty("stableSelector").GetString());
        Assert.Equal(
            [
                typeof(BrowserEngineBoundaryTests)
                    .GetProperty(nameof(PerformanceBoxingProperty))!
                    .GetMethod!
                    .MetadataToken,
            ],
            property.GetProperty("bodyTokens")
                .EnumerateArray()
                .Select(token => token.GetInt32()));

        JsonElement nested = Assert.Single(
            root.GetProperty("members").EnumerateArray(),
            candidate =>
                candidate.GetProperty("memberName").GetString()
                == nameof(PerformanceNestedProbe.Box));
        Assert.Equal(
            $"{typeof(BrowserEngineBoundaryTests).FullName}+"
                + nameof(PerformanceNestedProbe),
            nested.GetProperty("typeId").GetString());
    }

    [Fact]
    public async Task MemberFacts_DistinguishesSurfaceAndBodyTokenResolution()
    {
        const string PackageId = "Browser.Member.Facts";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePair(
                    image,
                    image,
                    $"{PackageId}.dll"),
                fromCache: false));

        string surfaceJson = await PackageExports.QueryPackage(
            PackageId,
            "1.0.0",
            "net11.0");
        using JsonDocument surfaceDocument =
            JsonDocument.Parse(surfaceJson);
        JsonElement type = Assert.Single(
            surfaceDocument.RootElement
                .GetProperty("types")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("definitionId").GetString()
                == typeof(BrowserEngineBoundaryTests).FullName);
        JsonElement member = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                == nameof(PerformanceBoxingProbe));
        JsonElement property = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                == nameof(PerformanceBoxingProperty));
        JsonElement getter = Assert.Single(
            property.GetProperty("bodySelectors").EnumerateArray());

        string graphMemberJson =
            await MetadataExports.QueryGraphMemberSurface(
                PackageId,
                "1.0.0",
                "net11.0",
                type.GetProperty("assembly").GetString()!,
                type.GetProperty("definitionId").GetString()!,
                getter.GetProperty("memberName").GetString()!,
                "stale-selector",
                getter.GetProperty("token").GetInt32());
        using JsonDocument graphMemberDocument =
            JsonDocument.Parse(graphMemberJson);
        JsonElement graphMember = graphMemberDocument.RootElement;
        JsonElement graphMemberType = graphMember.GetProperty("type");
        Assert.Equal(
            type.GetProperty("definitionId").GetString(),
            graphMemberType.GetProperty("definitionId").GetString());
        Assert.Equal(
            type.GetProperty("assemblyId").GetString(),
            graphMemberType.GetProperty("assemblyId").GetString());
        JsonElement graphMemberApi =
            Assert.Single(graphMemberType.GetProperty("api").EnumerateArray());
        Assert.Equal(
            JsonValueKind.Null,
            graphMemberApi
                .GetProperty("metadataToken").ValueKind);
        Assert.Equal(
            getter.GetProperty("token").GetInt32(),
            graphMember.GetProperty("selectedBody")
                .GetProperty("token").GetInt32());
        Assert.Equal(
            getter.GetProperty("memberName").GetString(),
            graphMember.GetProperty("selectedBody")
                .GetProperty("memberName").GetString());
        Assert.Equal(
            getter.GetProperty("selectorKey").GetString(),
            graphMember.GetProperty("selectedBody")
                .GetProperty("selectorKey").GetString());

        string accessorFactsJson =
            await AnalysisExports.QueryMemberFacts(
                PackageId,
                "1.0.0",
                "net11.0",
                type.GetProperty("assembly").GetString()!,
                type.GetProperty("definitionId").GetString()!,
                graphMember.GetProperty("selectedBody")
                    .GetProperty("memberName").GetString()!,
                property.GetProperty("signature").GetString()!,
                graphMember.GetProperty("selectedBody")
                    .GetProperty("selectorKey").GetString()!,
                graphMember.GetProperty("selectedBody")
                    .GetProperty("token").GetInt32(),
                implementationBodySelected: true);
        using JsonDocument accessorFactsDocument =
            JsonDocument.Parse(accessorFactsJson);
        Assert.Equal(
            getter.GetProperty("token").GetInt32(),
            accessorFactsDocument.RootElement
                .GetProperty("metadataToken").GetInt32());

        string json = await AnalysisExports.QueryMemberFacts(
            PackageId,
            "1.0.0",
            "net11.0",
            type.GetProperty("assembly").GetString()!,
            type.GetProperty("definitionId").GetString()!,
            member.GetProperty("name").GetString()!,
            member.GetProperty("signature").GetString()!,
            member.GetProperty("graphSelectorKey").GetString()!,
            typeof(BrowserEngineBoundaryTests)
                .GetMethod(nameof(PerformanceNoAllocationProbe))!
                .MetadataToken,
            implementationBodySelected: false);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(
            typeof(BrowserEngineBoundaryTests)
                .GetMethod(nameof(PerformanceBoxingProbe))!
                .MetadataToken,
            root.GetProperty("metadataToken").GetInt32());
        Assert.NotEqual(
            typeof(BrowserEngineBoundaryTests)
                .GetMethod(nameof(PerformanceNoAllocationProbe))!
                .MetadataToken,
            root.GetProperty("metadataToken").GetInt32());
        Assert.True(
            root.GetProperty("signals")
                .GetProperty("allocations")
                .GetInt32() > 0);
        Assert.Contains(
            root.GetProperty("allocations").EnumerateArray(),
            allocation =>
                allocation.GetProperty("kind").GetString()
                    == nameof(AllocationKind.Box)
                && allocation.GetProperty("countedAsHeap").GetBoolean());
        Assert.Contains(
            root.GetProperty("performanceOpportunities")
                .EnumerateArray(),
            opportunity =>
                opportunity.GetProperty("shape").GetString()
                == "box-value-type");
        Assert.Empty(
            root.GetProperty("diagnostics").EnumerateArray());

        JsonElement genericCallMember = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                == nameof(PerformanceGenericCallProbe));
        string genericCallJson = await AnalysisExports.QueryMemberFacts(
            PackageId,
            "1.0.0",
            "net11.0",
            type.GetProperty("assembly").GetString()!,
            type.GetProperty("definitionId").GetString()!,
            genericCallMember.GetProperty("name").GetString()!,
            genericCallMember.GetProperty("signature").GetString()!,
            genericCallMember.GetProperty("graphSelectorKey").GetString()!,
            metadataToken: 0,
            implementationBodySelected: false);
        using JsonDocument genericCallDocument =
            JsonDocument.Parse(genericCallJson);
        string[] genericCallees =
        [
            .. genericCallDocument.RootElement
                .GetProperty("calls")
                .EnumerateArray()
                .Select(call =>
                    call.GetProperty("callee").GetString()!)
                .Where(callee =>
                    callee.Contains(
                        nameof(PerformanceGenericCallTarget),
                        StringComparison.Ordinal))
                .Distinct(),
        ];
        Assert.Equal(4, genericCallees.Length);
        Assert.All(
            genericCallees,
            callee => Assert.Contains("<", callee));
        Assert.Contains(
            genericCallees,
            callee => callee.Contains(
                "<System.Threading.Timer>",
                StringComparison.Ordinal));
        Assert.Contains(
            genericCallees,
            callee => callee.Contains(
                "<System.Timers.Timer>",
                StringComparison.Ordinal));

        int implementationToken = typeof(BrowserEngineBoundaryTests)
            .GetMethod(nameof(PerformanceBoxingProbe))!
            .MetadataToken;
        string implementationBodyJson =
            await AnalysisExports.QueryMemberFacts(
                PackageId,
                "1.0.0",
                "net11.0",
                type.GetProperty("assembly").GetString()!,
                type.GetProperty("definitionId").GetString()!,
                member.GetProperty("name").GetString()!,
                member.GetProperty("signature").GetString()!,
                "missing-structural-selector",
                implementationToken,
                implementationBodySelected: true);
        using JsonDocument implementationBodyDocument =
            JsonDocument.Parse(implementationBodyJson);
        Assert.Equal(
            implementationToken,
            implementationBodyDocument.RootElement
                .GetProperty("metadataToken")
                .GetInt32());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AnalysisExports.QueryMemberFacts(
                PackageId,
                "1.0.0",
                "net11.0",
                type.GetProperty("assembly").GetString()!,
                type.GetProperty("definitionId").GetString()!,
                member.GetProperty("name").GetString()!,
                member.GetProperty("signature").GetString()!,
                "missing-structural-selector",
                implementationToken,
                implementationBodySelected: false));

        JsonElement valueTypeMember = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                == nameof(PerformanceValueTypeConstructionProbe));
        string valueTypeJson = await AnalysisExports.QueryMemberFacts(
            PackageId,
            "1.0.0",
            "net11.0",
            type.GetProperty("assembly").GetString()!,
            type.GetProperty("definitionId").GetString()!,
            valueTypeMember.GetProperty("name").GetString()!,
            valueTypeMember.GetProperty("signature").GetString()!,
            valueTypeMember.GetProperty("graphSelectorKey").GetString()!,
            metadataToken: 0,
            implementationBodySelected: false);
        using JsonDocument valueTypeDocument =
            JsonDocument.Parse(valueTypeJson);
        Assert.Contains(
            valueTypeDocument.RootElement
                .GetProperty("allocations")
                .EnumerateArray(),
            allocation =>
                !allocation.GetProperty("countedAsHeap").GetBoolean());

        JsonElement stackAllocMember = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                == nameof(PerformanceStackAllocProbe));
        string stackAllocJson = await AnalysisExports.QueryMemberFacts(
            PackageId,
            "1.0.0",
            "net11.0",
            type.GetProperty("assembly").GetString()!,
            type.GetProperty("definitionId").GetString()!,
            stackAllocMember.GetProperty("name").GetString()!,
            stackAllocMember.GetProperty("signature").GetString()!,
            stackAllocMember.GetProperty("graphSelectorKey").GetString()!,
            metadataToken: 0,
            implementationBodySelected: false);
        using JsonDocument stackAllocDocument =
            JsonDocument.Parse(stackAllocJson);
        JsonElement[] safety =
            [.. stackAllocDocument.RootElement
                .GetProperty("safety")
                .EnumerateArray()];
        Assert.Contains(
            safety,
            fact => fact.GetProperty("kind").GetString() == "stackalloc");
        Assert.DoesNotContain(
            safety
                .Where(fact => fact.GetProperty("offset").ValueKind
                    == JsonValueKind.String)
                .GroupBy(fact => fact.GetProperty("offset").GetString()),
            group => group.Count() > 1);
    }

    [Fact]
    public async Task AnnotatedSourceDestinations_RetainLoadedAssemblyIdentity()
    {
        const string PackageId = "Browser.Annotated.Destinations";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePair(
                    image,
                    image,
                    $"{PackageId}.dll"),
                fromCache: false));

        string surfaceJson = await PackageExports.QueryPackage(
            PackageId,
            "1.0.0",
            "net11.0");
        using JsonDocument surfaceDocument =
            JsonDocument.Parse(surfaceJson);
        JsonElement type = Assert.Single(
            surfaceDocument.RootElement
                .GetProperty("types")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("definitionId").GetString()
                == typeof(BrowserEngineBoundaryTests).FullName);
        JsonElement member = Assert.Single(
            type.GetProperty("api").EnumerateArray(),
            candidate =>
                candidate.GetProperty("name").GetString()
                == nameof(InvocationDestinationProbe));

        string annotatedJson =
            await SourceExports.QueryMemberAnnotatedSource(
                PackageId,
                "1.0.0",
                "net11.0",
                type.GetProperty("assembly").GetString()!,
                type.GetProperty("definitionId").GetString()!,
                type.GetProperty("queryId").GetString()!,
                member.GetProperty("name").GetString()!,
                member.GetProperty("signature").GetString()!,
                member.GetProperty("graphSelectorKey").GetString()!,
                member.GetProperty("metadataToken").GetInt32(),
                "[]");
        using JsonDocument annotatedDocument =
            JsonDocument.Parse(annotatedJson);
        JsonElement destination = Assert.Single(
            annotatedDocument.RootElement
                .GetProperty("viewerCatalog")
                .GetProperty("invocationDestinations")
                .EnumerateArray(),
            candidate =>
                candidate.GetProperty("target")
                    .GetProperty("memberName").GetString()
                == nameof(InvocationDestinationTarget));
        JsonElement target = destination.GetProperty("target");

        Assert.Equal(
            typeof(BrowserEngineBoundaryTests).Assembly.GetName().Version?.ToString(),
            target.GetProperty("assemblyVersion").GetString());
        Assert.Equal(
            type.GetProperty("assemblyId").GetString(),
            target.GetProperty("surfaceAssemblyId").GetString());
    }

    [Fact]
    public async Task GraphMemberSurface_UsesSurfaceAssetForImplementationOnlyType()
    {
        const string PackageId = "Browser.Graph.Internal.Pair";
        const string AssemblyName = "InspectWeb.Engine.Tests";
        byte[] implementation = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] surface = BuildEmptySurfaceImage(
            typeof(BrowserEngineBoundaryTests).Assembly.GetName());
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePair(
                    surface,
                    implementation,
                    $"{AssemblyName}.dll"),
                fromCache: false));
        BrowserPackageCoordinate coordinate =
            await BrowserPackageWorkspace.ResolveAsync(
                PackageId,
                "1.0.0",
                "net11.0",
                TestContext.Current.CancellationToken);
        PackageCompileAsset surfaceAsset =
            Assert.IsType<PackageCompileAsset>(coordinate.DefaultAsset);
        MethodInfo method = typeof(BrowserEngineBoundaryTests).GetMethod(
            nameof(InvocationDestinationTarget),
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"Missing {nameof(InvocationDestinationTarget)}.");

        for (int attempt = 0; attempt < 2; attempt++)
        {
            string json = await MetadataExports.QueryGraphMemberSurface(
                PackageId,
                "1.0.0",
                "net11.0",
                surfaceAsset.Id,
                typeof(BrowserEngineBoundaryTests).FullName!,
                method.Name,
                "stale-selector",
                method.MetadataToken);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement type = document.RootElement.GetProperty("type");

            Assert.Equal(
                surfaceAsset.Id,
                type.GetProperty("assemblyId").GetString());
            Assert.StartsWith(
                "compile:ref/net11.0/",
                surfaceAsset.Id,
                StringComparison.Ordinal);
            Assert.Equal(
                typeof(BrowserEngineBoundaryTests).FullName,
                type.GetProperty("definitionId").GetString());
            Assert.Equal(
                $"{surfaceAsset.AssemblyName}:{typeof(BrowserEngineBoundaryTests).FullName}",
                type.GetProperty("id").GetString());
            Assert.Single(type.GetProperty("api").EnumerateArray());
        }
    }

    [Fact]
    public async Task PackagePerformance_ExcludesMembersWithoutANavigableSurface()
    {
        const string PackageId = "Browser.Performance.Reference";
        byte[] extraImplementation = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] pairedImplementation = File.ReadAllBytes(
            typeof(BrowserPackage).Assembly.Location);
        byte[] surface = BuildEmptySurfaceImage(
            typeof(BrowserPackage).Assembly.GetName());
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                PackagePairWithExtraImplementation(
                    surface,
                    pairedImplementation,
                    "InspectWeb.Engine.dll",
                    extraImplementation,
                    "InspectWeb.Engine.Tests.dll"),
                fromCache: false));

        string json = await AnalysisExports.QueryPackagePerformance(
            PackageId,
            "1.0.0",
            "net11.0");

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.True(
            root.GetProperty("totalOpportunities").GetInt32() > 0);
        Assert.Empty(
            root.GetProperty("members").EnumerateArray());
    }

    [Fact]
    public async Task PackagePerformance_ReportsSurfaceTruncation()
    {
        const string PackageId = "Browser.Performance.Truncated";
        byte[] image = BuildTransportAmplificationImage(
            PackageId,
            typeCount: 10_000,
            namespaceLength: 1_000);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                "1.0.0",
                Package(
                    image,
                    $"lib/net11.0/{PackageId}.dll"),
                fromCache: false));

        string json = await AnalysisExports.QueryPackagePerformance(
            PackageId,
            "1.0.0",
            "net11.0");

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Contains(
            "truncated",
            document.RootElement
                .GetProperty("inspectionError")
                .GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void PerformanceMemberLimit_ReportsOnlyActualTruncation()
    {
        static BrowserPerformanceMember Member(int index) =>
            new(
                "Example.dll",
                $"Example.Type{index}",
                "Run",
                $"Run~{index}",
                [0x06000001 + index],
                1,
                0,
                ["box-value-type"],
                "high");

        var exactFailures = new List<string>();
        BrowserPerformanceMember[] exact =
            AnalysisExports.ApplyPerformanceMemberLimit(
                Enumerable.Range(0, 200).Select(Member),
                exactFailures);
        var truncatedFailures = new List<string>();
        BrowserPerformanceMember[] truncated =
            AnalysisExports.ApplyPerformanceMemberLimit(
                Enumerable.Range(0, 201).Select(Member),
                truncatedFailures);

        Assert.Equal(200, exact.Length);
        Assert.Empty(exactFailures);
        Assert.Equal(200, truncated.Length);
        Assert.Single(truncatedFailures);
        Assert.Contains(
            "truncated",
            truncatedFailures[0],
            StringComparison.Ordinal);
    }

    [Fact]
    public void MermaidLabel_ContainsGrammarSignificantArtifactText()
    {
        string encoded = BrowserCallGraphProjection.MermaidLabel(
            "A\"B\n<x>&\\\u2028\u202E\u200D\uD800X\uDC00\U000E0001-Caf\u00E9\U0001F600");

        Assert.Equal(
            "A&quot;B&#92;u000A&lt;x&gt;&amp;&#92;&#92;u2028"
                + "&#92;u202E&#92;u200D&#92;uD800X&#92;uDC00"
                + "&#92;uDB40&#92;uDC01-Caf\u00E9\U0001F600",
            encoded);
        Assert.DoesNotContain('"', encoded);
        Assert.DoesNotContain('\n', encoded);
        Assert.DoesNotContain('<', encoded);
        Assert.DoesNotContain('>', encoded);
        Assert.DoesNotContain('\\', encoded);
        Assert.DoesNotContain('\u2028', encoded);
        Assert.DoesNotContain('\u202E', encoded);
        Assert.DoesNotContain('\u200D', encoded);
        Assert.DoesNotContain('\uD800', encoded);
        Assert.DoesNotContain('\uDC00', encoded);
        Assert.EndsWith("-Caf\u00E9\U0001F600", encoded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomeDemoRunCore_ProjectsTypeOnlyMethodsSurface()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string packageId = $"Home.Demo.Methods.{suffix}";
        string peerPackageId = $"Home.Demo.Methods.Peer.{suffix}";
        string assemblyPath =
            typeof(BrowserEngineBoundaryTests).Assembly.Location;
        string peerAssemblyPath = typeof(BrowserPackage).Assembly.Location;
        BrowserPackageCoordinate coordinate = await Coordinate(
            packageId,
            Package(
                File.ReadAllBytes(assemblyPath),
                $"lib/net11.0/{Path.GetFileName(assemblyPath)}"));
        BrowserPackageCoordinate peerCoordinate = await Coordinate(
            peerPackageId,
            Package(
                File.ReadAllBytes(peerAssemblyPath),
                $"lib/net11.0/{Path.GetFileName(peerAssemblyPath)}"));
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [peerCoordinate, coordinate],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;
        try
        {
            var plan = new BrowserHomeDemoRunPlan(
                [
                    new BrowserPackageRequest(
                        peerPackageId,
                        "1.0.0",
                        "net11.0"),
                    new BrowserPackageRequest(
                        packageId,
                        "1.0.0",
                        "net11.0"),
                ],
                FocusRequestIndex: 1,
                typeof(BrowserEngineBoundaryTests).FullName!,
                ProductDemoSections.Methods,
                Member: null);
            var resolution = new BrowserScopeResolution(
                scopeLease,
                [peerCoordinate, coordinate]);

            BrowserHomeDemoRunResult result =
                CatalogExports.RunHomeDemoCore(plan, resolution);

            Assert.True(result.Found);
            Assert.Equal(2, result.Packages.Length);
            BrowserHomeDemoRunActivation activation =
                Assert.IsType<BrowserHomeDemoRunActivation>(result.Activation);
            Assert.Equal(packageId, activation.FocusPackage);
            Assert.Equal("1.0.0", activation.FocusVersion);
            Assert.Equal("net11.0", activation.FocusFramework);
            Assert.Equal(
                typeof(BrowserEngineBoundaryTests).FullName,
                activation.TypeId);
            Assert.Equal(ProductDemoSections.Methods, activation.Section);
            Assert.Null(activation.MemberName);
            Assert.Null(activation.MemberSection);
            Assert.Null(result.CallGraph);
            InspectWeb.Engine.CatalogFacade.BrowserTypeSurface type = Assert.Single(
                result.Packages[1].Types,
                candidate => candidate.Id
                    == typeof(BrowserEngineBoundaryTests).FullName);
            Assert.NotEmpty(type.Api);
            Assert.Equal(
                2,
                type.Api.Count(member =>
                    member.Name == nameof(HomeDemoRunFixture)));
        }
        finally
        {
            await BrowserPackageWorkspace.RemoveScopeAsync(scope);
        }
    }

    [Fact]
    public async Task HomeDemoRunCore_ProjectsTheAnchoredMemberAndItsGraph()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string packageId = $"Home.Demo.Run.{suffix}";
        string peerPackageId = $"Home.Demo.Peer.{suffix}";
        string assemblyPath =
            typeof(BrowserEngineBoundaryTests).Assembly.Location;
        string peerAssemblyPath = typeof(BrowserPackage).Assembly.Location;
        BrowserPackageCoordinate coordinate = await Coordinate(
            packageId,
            Package(
                File.ReadAllBytes(assemblyPath),
                $"lib/net11.0/{Path.GetFileName(assemblyPath)}"));
        BrowserPackageCoordinate peerCoordinate = await Coordinate(
            peerPackageId,
            Package(
                File.ReadAllBytes(peerAssemblyPath),
                $"lib/net11.0/{Path.GetFileName(peerAssemblyPath)}"));
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [peerCoordinate, coordinate],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;
        try
        {
            BrowserPackageSurfaceInfo surface =
                BrowserPackageSurfaceProjection.ProjectSurface(scope, coordinate);
            BrowserTypeSurfaceInfo type = Assert.Single(
                surface.Types,
                candidate => candidate.Id
                    == typeof(BrowserEngineBoundaryTests).FullName);
            BrowserMemberSurfaceInfo[] members =
            [
                .. type.Api.Where(candidate => candidate.Name
                    == nameof(HomeDemoRunFixture)),
            ];
            Assert.Equal(2, members.Length);
            BrowserMemberSurfaceInfo member = members[1];
            var plan = new BrowserHomeDemoRunPlan(
                [
                    new BrowserPackageRequest(
                        peerPackageId,
                        "1.0.0",
                        "net11.0"),
                    new BrowserPackageRequest(
                        packageId,
                        "1.0.0",
                        "net11.0"),
                ],
                FocusRequestIndex: 1,
                type.Id,
                ProductDemoSections.CallGraph,
                new BrowserHomeDemoRunMember(
                    member.Name,
                    member.Kind,
                    member.AnchorDigest[..6],
                    MemberSection: "call-graph"));
            var resolution = new BrowserScopeResolution(
                scopeLease,
                [peerCoordinate, coordinate]);

            BrowserHomeDemoRunResult result =
                CatalogExports.RunHomeDemoCore(plan, resolution);

            Assert.True(result.Found);
            Assert.Equal(2, result.Packages.Length);
            BrowserHomeDemoRunActivation activation =
                Assert.IsType<BrowserHomeDemoRunActivation>(result.Activation);
            Assert.Equal(packageId, activation.FocusPackage);
            Assert.Equal("1.0.0", activation.FocusVersion);
            Assert.Equal("net11.0", activation.FocusFramework);
            Assert.Equal(type.Id, activation.TypeId);
            Assert.Equal(ProductDemoSections.CallGraph, activation.Section);
            Assert.Equal(member.AnchorDigest, activation.MemberAnchorDigest);
            Assert.Equal("call-graph", activation.MemberSection);
            Assert.NotNull(result.CallGraph);
            Assert.False(result.CallGraph.NoBody);
            Assert.Equal(2, result.CallGraph.Scope.Packages);
            Assert.Contains(
                nameof(HomeDemoRunFixture),
                result.CallGraph.Mermaid,
                StringComparison.Ordinal);
        }
        finally
        {
            await BrowserPackageWorkspace.RemoveScopeAsync(scope);
        }
    }

    public static int HomeDemoRunFixture(int value) =>
        Math.Abs(value);

    public static string HomeDemoRunFixture(string value) =>
        value.Trim();

    [Fact]
    public void CallGraphMermaid_ContainsArtifactLabels()
    {
        TypeRef declaringType = TypeRef.Definition(
            "Sample",
            "Example",
            "A\u202E\uD800-Caf\u00E9\U0001F600");
        var member = new MemberRef(
            declaringType,
            "Run",
            [],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method);
        var tree = new CallTreeNode(
            member,
            Kind: null,
            CallTreeStatus.Leaf,
            Children: []);
        CallGraphProjection projection = CallGraphProjection.FromCallees(tree);

        string mermaid = BrowserCallGraphProjection.Mermaid(projection);

        Assert.Contains(
            "&#92;u202E&#92;uD800-Caf\u00E9\U0001F600",
            mermaid,
            StringComparison.Ordinal);
        Assert.DoesNotContain('\u202E', mermaid);
        Assert.DoesNotContain('\uD800', mermaid);
    }

    [Fact]
    public void CallGraphMermaid_DerivesLoopEdgesFromTypedProjectionState()
    {
        TypeRef type = TypeRef.Definition(
            "Example",
            "Example",
            "Worker");
        TypeRef returnType = TypeRef.CoreLib("System", "Void");
        var caller = new MemberRef(
            type,
            "Run",
            [],
            returnType,
            MemberKind.Method);
        var callee = new MemberRef(
            type,
            "Tick",
            [],
            returnType,
            MemberKind.Method);
        var calleeNode = new CallTreeNode(
            callee,
            null,
            CallTreeStatus.Leaf,
            [],
            new CallTreePerf(0, 0, 1, true, "loop"));
        var nonLoopNode = new CallTreeNode(
            callee with { Name = "Wait" },
            null,
            CallTreeStatus.Leaf,
            [],
            new CallTreePerf(0, 0, 1, false));
        var root = new CallTreeNode(
            caller,
            null,
            CallTreeStatus.Expanded,
            [calleeNode, nonLoopNode],
            new CallTreePerf(0, 0, 1, false));

        string mermaid = BrowserCallGraphProjection.Mermaid(
            CallGraphProjection.FromCallees(root));

        Assert.Contains("n0 -- loop --> n1", mermaid);
        Assert.Contains("n0 --> n2", mermaid);
    }

    [Fact]
    public void CallGraphTargets_CarryEveryNavigableNodeWithNormalizedKinds()
    {
        TypeRef declaringTypeDefinition = TypeRef.Definition(
            "Example",
            "Example",
            "Outer`1+Widget`1");
        TypeRef declaringType = TypeRef.GenericInstance(
            declaringTypeDefinition,
            [TypeRef.CoreLib("System", "String"), TypeRef.CoreLib("System", "Int32")]);
        TypeRef returnType = TypeRef.Definition(TypeRef.CoreLibrary, "System", "Void");
        var member = new MemberRef(
            declaringType,
            "Run",
            ImmutableArray<TypeRef>.Empty,
            returnType,
            MemberKind.Method);
        var arrayMember = new MemberRef(
            TypeRef.MdArray(declaringTypeDefinition, rank: 2),
            "Get",
            ImmutableArray<TypeRef>.Empty,
            returnType,
            MemberKind.Method);
        CallGraphNode[] nodes =
        [
            new(
                0,
                GraphNodeIdentity.FromMember(member),
                member,
                "focus",
                CallGraphNodeKind.Focus),
            new(
                1,
                GraphNodeIdentity.FromMember(member),
                member,
                "normal",
                CallGraphNodeKind.Normal),
            new(
                2,
                GraphNodeIdentity.FromMember(member),
                member,
                "external",
                CallGraphNodeKind.External),
            new(
                3,
                GraphNodeIdentity.FromMember(arrayMember),
                arrayMember,
                "array",
                CallGraphNodeKind.Normal),
        ];

        BrowserCallGraphTargetInfo[] targets = BrowserCallGraphProjection.Targets(
            nodes,
            [new AssemblyReferenceIdentity(
                "Example",
                new Version(1, 2, 3, 4),
                "neutral",
                "0011223344556677")]);

        Assert.Equal(["n0", "n1", "n2", "n3"], targets.Select(target => target.Id));
        Assert.Equal(
            ["focus", "normal", "external", "normal"],
            targets.Select(target => target.Kind));
        Assert.All(
            targets[..3],
            target =>
            {
                Assert.Equal("Example", target.Assembly);
                Assert.Equal("1.2.3.4", target.AssemblyVersion);
                Assert.Equal("neutral", target.AssemblyCulture);
                Assert.Equal("0011223344556677", target.AssemblyPublicKeyToken);
                Assert.Equal("Example.Outer.Widget<int>", target.TypeFullName);
                Assert.Equal("Example.Outer`1+Widget`1", target.TypeMetadataId);
            });
        Assert.Null(targets[3].TypeMetadataId);
    }

    [Fact]
    public void CallGraphTargets_PreferResolvedDefinitionAssemblyIdentity()
    {
        var facade = new AssemblyReferenceIdentity(
            "System.Runtime",
            new Version(11, 0, 0, 0),
            "neutral",
            "b03f5f7f11d50a3a");
        var definition = new AssemblyReferenceIdentity(
            "System.Private.CoreLib",
            new Version(11, 0, 0, 0),
            "neutral",
            "7cec85d7bea7798e");
        TypeRef declaringType = TypeRef.Definition(
            TypeRef.CoreLibrary,
            "System.IO",
            "TextWriter",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(facade),
                DefinitionName("System.IO", ["TextWriter"])));
        var member = new MemberRef(
            declaringType,
            "WriteLine",
            [TypeRef.CoreLib("System", "String")],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method);
        var node = new CallGraphNode(
            0,
            GraphNodeIdentity.FromMember(member),
            member,
            "TextWriter.WriteLine",
            CallGraphNodeKind.Normal,
            DefinitionAssemblyIdentity: definition);

        BrowserCallGraphTargetInfo target = Assert.Single(
            BrowserCallGraphProjection.Targets(
                [node],
                [facade, definition],
                assembly => assembly == definition.Name
                    ? "netcore.app"
                    : null));

        Assert.Equal(definition.Name, target.Assembly);
        Assert.Equal("11.0.0.0", target.AssemblyVersion);
        Assert.Equal(definition.PublicKeyToken, target.AssemblyPublicKeyToken);
        Assert.Equal("netcore.app", target.PlatformPack);
    }

    [Fact]
    public async Task PlatformCallGraph_ResolvesDefinitionsBehindFacadesWithoutHostProbing()
    {
        const string packageId =
            "microsoft.netcore.app.runtime.linux-x64";
        const string version = "11.0.97";
        const string framework = "net11.0-facade-resolution";
        string runtimeDirectory =
            Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        byte[] nupkg = PlatformPackage(
            ("System.Private.CoreLib.dll",
                File.ReadAllBytes(typeof(object).Assembly.Location)),
            ("System.Console.dll",
                File.ReadAllBytes(typeof(Console).Assembly.Location)),
            ("System.Runtime.dll",
                File.ReadAllBytes(
                    Path.Combine(
                        runtimeDirectory,
                        "System.Runtime.dll"))));
        var handler = new PlatformVersionHandler(
            packageId,
            version,
            nupkg);
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization(
                [PackageSource.NuGetOrg]);

        BrowserPlatformScopeResolution runtime =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                framework,
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        await runtime.DisposeAsync();
        await using BrowserPlatformScopeResolution console =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                framework,
                "System.Console.dll",
                "netcore.app",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        Assert.Equal(2, console.Scope.Members.Length);
        BrowserPackageSurface surface =
            Assert.IsType<BrowserPackageSurface>(
                JsonSerializer.Deserialize(
                    PackageExports.ProjectPlatformSurface(
                        console),
                    BrowserPackageJsonContext.Default.BrowserPackageSurface));
        BrowserTypeSurface consoleType = Assert.Single(
            surface.Types,
            type => type.Namespace == "System"
                && type.Name == "Console");
        BrowserMemberSurface writeLine = Assert.Single(
            consoleType.Api,
            member => member.Name == "WriteLine"
                && member.DocumentationId
                    == "M:System.Console.WriteLine(System.String)");
        BrowserAssemblySurface consoleAssembly =
            Assert.Single(
                surface.Assemblies,
                assembly => assembly.Id == consoleType.AssemblyId);
        int requestsBeforeGraph = handler.Requests;

        BrowserCallGraph graph =
            Assert.IsType<BrowserCallGraph>(
                JsonSerializer.Deserialize(
                    await CallGraphExports
                        .ExpandPlatformCallGraph(
                            framework,
                            "System.Console",
                            "netcore.app",
                            consoleAssembly.Version,
                            consoleAssembly.Culture,
                            consoleAssembly.PublicKeyToken,
                            consoleType.DefinitionId,
                            writeLine.Name,
                            writeLine.GraphSelectorKey,
                            writeLine.MetadataToken!.Value),
                    BrowserCallGraphJsonContext.Default.BrowserCallGraph));

        Assert.Equal(requestsBeforeGraph, handler.Requests);
        Assert.Equal(2, console.Scope.Members.Length);
        Assert.True(
            BrowserPackageWorkspace.IsScopeRetained(console.Scope));
        Assert.Equal(3, graph.Scope.Assemblies);
        BrowserCallGraphTarget[] forwarded =
        [
            .. graph.Targets.Where(target =>
                target.TypeDefinitionId == "System.IO.TextWriter"
                && target.MemberName == "WriteLine"),
        ];
        Assert.NotEmpty(forwarded);
        Assert.All(
            forwarded,
            target =>
            {
                Assert.Equal(
                    typeof(object).Assembly.GetName().Name,
                    target.Assembly);
                Assert.Equal("netcore.app", target.PlatformPack);
            });
        Assert.DoesNotContain(
            forwarded,
            target => target.Assembly == "System.Runtime");

        BrowserCallGraphTarget destination = forwarded[0];
        BrowserPackageSurface terminalSurface =
            Assert.IsType<BrowserPackageSurface>(
                JsonSerializer.Deserialize(
                    await PackageExports.LoadRuntimePackAssembly(
                        framework,
                        $"{destination.Assembly}.dll",
                        Assert.IsType<string>(destination.PlatformPack)),
                    BrowserPackageJsonContext.Default.BrowserPackageSurface));
        BrowserTypeSurface terminalType = Assert.Single(
            terminalSurface.Types,
            type => type.DefinitionId == destination.TypeDefinitionId);
        BrowserMemberSurface terminalMember = Assert.Single(
            terminalType.Api,
            member => member.GraphSelectorKey == destination.SelectorKey);
        Assert.Equal(destination.MemberName, terminalMember.Name);
        BrowserAssemblySurface terminalAssembly = Assert.Single(
            terminalSurface.Assemblies,
            assembly => assembly.Id == terminalType.AssemblyId);
        Assert.Equal(destination.Assembly, terminalAssembly.Id);
        Assert.Equal(destination.AssemblyVersion, terminalAssembly.Version);

        BrowserPackageSurface facadeSurface =
            Assert.IsType<BrowserPackageSurface>(
                JsonSerializer.Deserialize(
                    await PackageExports.LoadRuntimePackAssembly(
                        framework,
                        "System.Runtime.dll",
                        "netcore.app"),
                    BrowserPackageJsonContext.Default.BrowserPackageSurface));
        BrowserAssemblySurface facadeAssembly =
            Assert.Single(facadeSurface.Assemblies);
        Assert.Equal("System.Runtime", facadeAssembly.Id);

        foreach (BrowserAssemblySurface origin
            in new[] { terminalAssembly, facadeAssembly })
        {
            BrowserCallGraph continued =
                Assert.IsType<BrowserCallGraph>(
                    JsonSerializer.Deserialize(
                        await CallGraphExports.ExpandPlatformCallGraph(
                            framework,
                            origin.Id,
                            "netcore.app",
                            origin.Version,
                            origin.Culture,
                            origin.PublicKeyToken,
                            Assert.IsType<string>(destination.TypeDefinitionId),
                            destination.MemberName,
                            destination.SelectorKey,
                            metadataToken: 0),
                        BrowserCallGraphJsonContext.Default.BrowserCallGraph));

            Assert.False(continued.NoBody);
            BrowserCallGraphTarget focus = Assert.Single(
                continued.Targets,
                target => target.Kind == "focus");
            Assert.Equal(destination.Assembly, focus.Assembly);
            Assert.Equal(destination.AssemblyVersion, focus.AssemblyVersion);
            Assert.Equal(destination.TypeDefinitionId, focus.TypeDefinitionId);
            Assert.Equal(destination.SelectorKey, focus.SelectorKey);
            Assert.Equal(destination.TypeFullName, continued.Callees.TypeFullName);
            Assert.Equal(destination.MemberName, continued.Callees.MemberName);
            Assert.NotEmpty(continued.Callees.Children);
        }

        Assert.Equal(2, console.Scope.Members.Length);
        Assert.True(
            BrowserPackageWorkspace.IsScopeRetained(console.Scope));
    }

    // A package coordinate becomes a flat-container path segment and a cache key. Both halves are
    // validated before either use, so a segment-breaking coordinate never reaches the cache or
    // the network — the failing handler below proves no request was attempted.
    [Theory]
    [InlineData("evil/../other", "1.0.0")]
    [InlineData("..", "1.0.0")]
    [InlineData("Example", "1.0.0/../9.9.9")]
    [InlineData("Example", "1.0.0?x=1")]
    [InlineData("Example", "1.0.0 ")]
    [InlineData("Example", "notaversion")]
    public async Task PackageCoordinates_AreRejectedBeforeAnyCacheOrNetworkAccess(
        string packageId,
        string version)
    {
        BrowserPackageCacheSnapshot before = BrowserPackageWorkspace.Stats();

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPackageWorkspace.AcquireAsync(
                    packageId,
                    version,
                    TestContext.Current.CancellationToken));

        Assert.Contains("package coordinate", failure.Message, StringComparison.OrdinalIgnoreCase);
        BrowserPackageCacheSnapshot after = BrowserPackageWorkspace.Stats();
        Assert.Equal(before.Packages, after.Packages);
        Assert.Equal(before.Resident, after.Resident);
        Assert.Equal(before.ResidentBytes, after.ResidentBytes);
    }

    [Fact]
    public async Task PackageAcquisition_StallBecomesVisibleOperationTimeout()
    {
        var handler = new StallingPackageHandler();
        using IPackageSourceClient source = Gallery(handler);
        string packageId =
            $"timeout.package.{Guid.NewGuid():N}";

        Task<BrowserPackage> acquisition = BrowserPackageWorkspace.AcquireAsync(
            packageId,
            "1.0.0",
            source,
            PackageSourceIdentity.NuGetOrg,
            TimeSpan.FromMilliseconds(200));
        await handler.RequestStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        TimeoutException failure =
            await Assert.ThrowsAsync<TimeoutException>(() => acquisition);

        Assert.Contains(
            "Browser package operation",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task PackageAcquisition_ExactPinUsesGalleryCdnWithoutServiceIndex()
    {
        string packageId = $"gallery.exact.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        byte[] archive = PackageDocuments(1);
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            archive);
        using IPackageSourceClient source = Gallery(handler);

        BrowserPackage package = await BrowserPackageWorkspace.AcquireAsync(
            packageId,
            version,
            source,
            PackageSourceIdentity.NuGetOrg,
            TimeSpan.FromSeconds(5));

        Assert.Equal(version, package.Version);
        Assert.Equal(archive, package.RetainedBytes);
        Assert.False(package.Content.FromCache);
        Assert.Equal(
            NuGetCache.GetSourceKey(PackageSourceIdentity.NuGetOrg.Value),
            package.Content.ProducerKey);
        Assert.Equal(
            [$"https://globalcdn.nuget.org/packages/{packageId}.{version}.nupkg"],
            handler.Requested);
        Assert.DoesNotContain(
            handler.Requested,
            request => request.Contains(
                "api.nuget.org",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PackageQueryContent_AcquiresThroughBrowserPackagePolicy()
    {
        string packageId = $"gallery.query.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        byte[] archive = PackageWithSkill(packageId, version);
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            archive);
        using IPackageSourceClient source = Gallery(handler);
        PackageManifestFacts manifest = Assert.IsType<
            PackageManifestFactsResult.Available>(
                PackageManifestFactsQuery.Execute(
                    Encoding.UTF8.GetBytes(
                        Nuspec(packageId, version)),
                    PackageSourceCoordinate.Create(packageId, version))).Value;
        var package = new PackageQueryPackage(
            packageId,
            version,
            [],
            TotalDownloads: 0,
            Verified: false,
            source.Source,
            manifest);
        using var deadline =
            new BrowserPackageWorkspace.BrowserPackageOperationDeadline(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        PackageQueryContentResult result =
            await BrowserPackageWorkspace.AcquirePackageQueryContentAsync(
                package,
                source,
                PackageSourceIdentity.NuGetOrg,
                deadline);

        IPackageContent content = Assert.IsType<
            PackageQueryContentResult.Available>(result).Content;
        Assert.Contains(
            "skills/SKILL.md",
            content.EnumerateEntries(),
            StringComparer.Ordinal);
        Assert.Equal(
            [$"https://globalcdn.nuget.org/packages/{packageId}.{version}.nupkg"],
            handler.Requested);
    }

    [Fact]
    public async Task PackageQueryContent_PolicyRejectionRemainsVisible()
    {
        string packageId = $"gallery.query.no-length.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            PackageWithSkill(packageId, version),
            omitContentLength: true);
        using IPackageSourceClient source = Gallery(handler);
        PackageManifestFacts manifest = Assert.IsType<
            PackageManifestFactsResult.Available>(
                PackageManifestFactsQuery.Execute(
                    Encoding.UTF8.GetBytes(
                        Nuspec(packageId, version)),
                    PackageSourceCoordinate.Create(packageId, version))).Value;
        var package = new PackageQueryPackage(
            packageId,
            version,
            [],
            TotalDownloads: 0,
            Verified: false,
            source.Source,
            manifest);
        using var deadline =
            new BrowserPackageWorkspace.BrowserPackageOperationDeadline(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        PackageQueryContentResult result =
            await BrowserPackageWorkspace.AcquirePackageQueryContentAsync(
                package,
                source,
                PackageSourceIdentity.NuGetOrg,
                deadline);

        string message = Assert.IsType<
            PackageQueryContentResult.Unavailable>(result).Message;
        Assert.Contains(
            "did not declare its byte length",
            message,
            StringComparison.Ordinal);
        Assert.True(handler.PayloadDisposed);
    }

    [Fact]
    public async Task BrowserPackageRealization_ReceivesAcquisitionIssuedCoordinate()
    {
        string packageId = $"Gallery.Binding.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        byte[] archive = Package(
            [0x01],
            $"lib/net11.0/{packageId}.dll");
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            archive);
        using IPackageSourceClient source = Gallery(handler);

        BrowserPackageCoordinate coordinate =
            await BrowserPackageWorkspace.ResolveAsync(
                packageId,
                version,
                "net11.0",
                source,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(5));

        PackageRootBinding binding = Assert.IsType<PackageRootBinding>(
            coordinate.Binding);
        Assert.Same(binding.Root, coordinate.Root);
        Assert.Equal(packageId, binding.Root.PackageId);
        Assert.Equal(packageId.ToLowerInvariant(), binding.Coordinate.PackageId);
        Assert.Equal(version, binding.Coordinate.Version);
        Assert.Equal("net11.0", binding.Coordinate.Framework);
        Assert.Null(binding.Coordinate.RuntimeIdentifier);
        Assert.Equal(
            NuGetCache.GetSourceKey(PackageSourceIdentity.NuGetOrg.Value),
            binding.Coordinate.Producer);
        Assert.True(binding.Root.ReferencesContent(coordinate.Package.Content));
    }

    [Fact]
    public async Task WorkspaceOccurrences_PreserveOrderAndSupersedeOldActions()
    {
        string packageId = $"Gallery.Workspace.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        byte[] archive = Package(
            [0x01],
            $"lib/net11.0/{packageId}.dll");
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            archive);
        using IPackageSourceClient source = Gallery(handler);
        BrowserPackageCoordinate coordinate =
            await BrowserPackageWorkspace.ResolveAsync(
                packageId,
                version,
                "net11.0",
                source,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(5));

        BrowserWorkspacePackageOccurrenceView view =
            BrowserWorkspaceOccurrenceOperations.ReplaceCurrent(
                [coordinate, coordinate]);

        Assert.Equal(2, view.Occurrences.Length);
        Assert.All(
            view.Occurrences,
            occurrence =>
            {
                Assert.Equal(packageId, occurrence.Package);
                Assert.Equal(version, occurrence.Version);
                Assert.Equal("net11.0", occurrence.Framework);
                Assert.DoesNotContain(
                    packageId,
                    occurrence.Action,
                    StringComparison.OrdinalIgnoreCase);
            });
        Assert.NotEqual(
            view.Occurrences[0].Action,
            view.Occurrences[1].Action);
        BrowserWorkspaceOccurrenceSelection selection = Assert.IsType<
            BrowserWorkspaceOccurrenceSelection>(
                BrowserWorkspaceOccurrenceOperations.Activate(
                    view.Occurrences[1].Action));
        Assert.Same(coordinate, selection.Coordinate);

        BrowserWorkspaceOccurrenceOperations.ReplaceCurrent([]);

        Assert.Null(
            BrowserWorkspaceOccurrenceOperations.Activate(
                view.Occurrences[0].Action));

        BrowserWorkspacePackageOccurrenceView replacement =
            BrowserWorkspaceOccurrenceOperations.ReplaceCurrent(
                [coordinate]);
        BrowserWorkspaceOccurrenceOperations.ClearCurrent();

        Assert.Null(
            BrowserWorkspaceOccurrenceOperations.Activate(
                replacement.Occurrences[0].Action));
    }

    [Fact]
    public async Task WorkspaceOccurrences_ReplacedInflightQueryCannotRetireReplacement()
    {
        string packageId = $"Gallery.Workspace.Race.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            Package(
                [0x01],
                $"lib/net11.0/{packageId}.dll"));
        using IPackageSourceClient source = Gallery(handler);
        BrowserPackageCoordinate coordinate =
            await BrowserPackageWorkspace.ResolveAsync(
                packageId,
                version,
                "net11.0",
                source,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(5));
        var resolutionStarted =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var continueResolution =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        Task<BrowserWorkspacePackageOccurrenceView> query =
            BrowserWorkspaceOccurrenceOperations.QueryAsync(
                [
                    new BrowserPackageRequest(
                        packageId,
                        version,
                        "net11.0"),
                ],
                async (_, _) =>
                {
                    resolutionStarted.SetResult();
                    await continueResolution.Task;
                    return coordinate;
                });
        await resolutionStarted.Task;
        BrowserWorkspacePackageOccurrenceView replacement =
            BrowserWorkspaceOccurrenceOperations.ReplaceCurrent(
                [coordinate]);
        continueResolution.SetResult();

        BrowserWorkspacePackageOccurrenceView view = await query;

        Assert.True(view.Superseded);
        Assert.Empty(view.Occurrences);
        Assert.NotNull(
            BrowserWorkspaceOccurrenceOperations.Activate(
                replacement.Occurrences[0].Action));
    }

    [Fact]
    public async Task WorkspaceOccurrences_RevocationReleasesLeasesBeforeAStalledResolutionCompletes()
    {
        string packageId = $"Gallery.Workspace.LeaseRace.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            Package(
                [0x01],
                $"lib/net11.0/{packageId}.dll"));
        using IPackageSourceClient source = Gallery(handler);
        BrowserPackageCoordinate coordinate =
            await BrowserPackageWorkspace.ResolveAsync(
                packageId,
                version,
                "net11.0",
                source,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(5));
        var secondResolutionStarted =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var continueResolution =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        int resolution = 0;

        Task<BrowserWorkspacePackageOccurrenceView> query =
            BrowserWorkspaceOccurrenceOperations.QueryAsync(
                [
                    new BrowserPackageRequest(
                        packageId,
                        version,
                        "net11.0"),
                    new BrowserPackageRequest(
                        packageId,
                        version,
                        "net11.0"),
                ],
                async (_, cancellationToken) =>
                {
                    if (Interlocked.Increment(ref resolution) == 1)
                        return coordinate;

                    secondResolutionStarted.SetResult();
                    await continueResolution.Task;
                    return coordinate;
                });
        await secondResolutionStarted.Task;

        BrowserWorkspaceOccurrenceOperations.ClearCurrent();
        using (
            await BrowserPackageWorkspace.ReservePackageDownloadAsync(
                $"workspace.lease.pressure.{Guid.NewGuid():N}@1.0.0",
                128L * MiB))
        {
            Assert.Equal(
                0,
                BrowserPackageWorkspace.Stats().Resident);
        }
        continueResolution.SetResult();

        BrowserWorkspacePackageOccurrenceView view = await query;
        Assert.True(view.Superseded);
        Assert.Empty(view.Occurrences);
    }

    [Fact]
    public async Task BrowserPackageRealization_WithoutFrameworkKeepsHostProjectionSemantics()
    {
        string selectedId = $"gallery.binding.selected.{Guid.NewGuid():N}";
        var selectedHandler = new GalleryPackageHandler(
            selectedId,
            "1.0.0",
            Package(
                [0x01],
                $"lib/net11.0/{selectedId}.dll"));
        using IPackageSourceClient selectedSource = Gallery(selectedHandler);
        BrowserPackageCoordinate selected =
            await BrowserPackageWorkspace.ResolveAsync(
                selectedId,
                "1.0.0",
                targetFramework: null,
                selectedSource,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(5));

        Assert.Null(selected.RealizedCoordinate.Framework);
        Assert.Equal("net11.0", selected.Framework);
        Assert.True(selected.Selection.IsSelected);

        string rootOnlyId = $"gallery.binding.root.{Guid.NewGuid():N}";
        var rootOnlyHandler = new GalleryPackageHandler(
            rootOnlyId,
            "1.0.0",
            PackageDocuments(1));
        using IPackageSourceClient rootOnlySource = Gallery(rootOnlyHandler);
        BrowserPackageCoordinate rootOnly =
            await BrowserPackageWorkspace.ResolveAsync(
                rootOnlyId,
                "1.0.0",
                targetFramework: null,
                rootOnlySource,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(5));

        Assert.Null(rootOnly.RealizedCoordinate.Framework);
        Assert.Equal("", rootOnly.Framework);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoCompileAssets,
            rootOnly.Selection.Status);
    }

    [Fact]
    public async Task PackageAcquisition_GalleryFailureRemainsVisible()
    {
        string packageId = $"gallery.failure.{Guid.NewGuid():N}";
        var handler = new GalleryPackageHandler(
            packageId,
            "1.0.0",
            PackageDocuments(1),
            packageStatus: System.Net.HttpStatusCode.BadGateway);
        using IPackageSourceClient source = Gallery(handler);

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPackageWorkspace.AcquireAsync(
                    packageId,
                    "1.0.0",
                    source,
                    PackageSourceIdentity.NuGetOrg,
                    TimeSpan.FromSeconds(5)));

        Assert.Contains(
            "transport failed",
            failure.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "globalcdn.nuget.org",
            failure.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PackageAcquisition_RejectedReservationDisposesGalleryPayload()
    {
        string packageId = $"gallery.no-length.{Guid.NewGuid():N}";
        var handler = new GalleryPackageHandler(
            packageId,
            "1.0.0",
            PackageDocuments(1),
            omitContentLength: true);
        using IPackageSourceClient source = Gallery(handler);

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPackageWorkspace.AcquireAsync(
                    packageId,
                    "1.0.0",
                    source,
                    PackageSourceIdentity.NuGetOrg,
                    TimeSpan.FromSeconds(5)));

        Assert.Contains(
            "did not declare its byte length",
            failure.Message,
            StringComparison.Ordinal);
        Assert.True(handler.PayloadDisposed);
    }

    [Fact]
    public async Task PackageAcquisition_FloatingRootUsesGallerySearchAndCdn()
    {
        string packageId = $"gallery.floating.{Guid.NewGuid():N}";
        const string version = "4.5.6";
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            PackageDocuments(1),
            provideSearchResult: true);
        using IPackageSourceClient source = Gallery(handler);

        BrowserPackage package = await BrowserPackageWorkspace.AcquireAsync(
            packageId,
            version: null,
            source,
            PackageSourceIdentity.NuGetOrg,
            TimeSpan.FromSeconds(5));

        Assert.Equal(version, package.Version);
        Assert.Equal(2, handler.Requested.Count);
        Assert.StartsWith(
            "https://azuresearch-usnc.nuget.org/query?",
            handler.Requested[0],
            StringComparison.Ordinal);
        Assert.Contains(
            $"q=packageid%3A{packageId}",
            handler.Requested[0],
            StringComparison.Ordinal);
        Assert.Equal(
            $"https://globalcdn.nuget.org/packages/{packageId}.{version}.nupkg",
            handler.Requested[1]);
        Assert.DoesNotContain(
            handler.Requested,
            request => request.Contains(
                "api.nuget.org",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PackageResolution_StallBecomesVisibleOperationTimeout()
    {
        var handler = new StallingPackageHandler();
        using IPackageSourceClient source = Gallery(handler);
        string packageId =
            $"resolution.timeout.package.{Guid.NewGuid():N}";

        Task<BrowserPackage> acquisition = BrowserPackageWorkspace.AcquireAsync(
            packageId,
            version: null,
            source,
            PackageSourceIdentity.NuGetOrg,
            TimeSpan.FromSeconds(5));
        await handler.RequestStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        TimeoutException failure =
            await Assert.ThrowsAsync<TimeoutException>(() => acquisition);

        Assert.Contains(
            "Browser package operation",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task PackageAcquisition_SharedStallIsAVisibleTimeoutForEveryCaller()
    {
        var handler = new StallingPackageHandler();
        using IPackageSourceClient source = Gallery(handler);
        string packageId =
            $"shared.timeout.package.{Guid.NewGuid():N}";

        Task<BrowserPackage> first = BrowserPackageWorkspace.AcquireAsync(
            packageId,
            "1.0.0",
            source,
            PackageSourceIdentity.NuGetOrg,
            TimeSpan.FromMilliseconds(500));
        await handler.RequestStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        Task<BrowserPackage> second = BrowserPackageWorkspace.AcquireAsync(
            packageId,
            "1.0.0",
            source,
            PackageSourceIdentity.NuGetOrg,
            TimeSpan.FromMilliseconds(100));

        TimeoutException secondFailure =
            await Assert.ThrowsAsync<TimeoutException>(() => second);
        Assert.Contains(
            "0.1-second deadline",
            secondFailure.Message,
            StringComparison.Ordinal);
        Assert.False(first.IsCompleted);
        await Assert.ThrowsAsync<TimeoutException>(() => first);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public void PendingAcquisitionAssociation_UsesCoordinateAndExactClientReference()
    {
        using IPackageSourceClient gallery =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        using IPackageSourceClient v3 =
            PackageSourceClientFactory.Create(
                PackageSourceDescriptor.NuGetV3(
                    "nuget-v3",
                    "NuGet.org v3",
                    new Uri("https://api.nuget.org/v3/index.json")),
                PackageSourceAssociation.Create());
        const string coordinate = "example@1.0.0";

        Assert.Equal(gallery.Source.Producer, v3.Source.Producer);
        Assert.NotEqual(
            gallery.Source.TransportKind,
            v3.Source.TransportKind);

        var galleryKey =
            new BrowserPackageWorkspace.PendingAcquisitionKey(
                coordinate,
                gallery);
        var equivalentGalleryKey =
            new BrowserPackageWorkspace.PendingAcquisitionKey(
                coordinate,
                gallery);
        var v3Key =
            new BrowserPackageWorkspace.PendingAcquisitionKey(
                coordinate,
                v3);

        Assert.Equal(galleryKey, equivalentGalleryKey);
        Assert.Equal(
            galleryKey.GetHashCode(),
            equivalentGalleryKey.GetHashCode());
        Assert.NotEqual(galleryKey, v3Key);

        FieldInfo[] fields = typeof(
                BrowserPackageWorkspace.PendingAcquisitionKey)
            .GetFields(
                BindingFlags.Instance
                | BindingFlags.NonPublic);
        Assert.Equal(2, fields.Length);
        Assert.Contains(fields, field => field.FieldType == typeof(string));
        Assert.Contains(
            fields,
            field => field.FieldType == typeof(IPackageSourceClient));
    }

    [Fact]
    public async Task PackageAcquisition_DistinctSameProducerClientsDoNotSharePendingTransfer()
    {
        string packageId =
            $"distinct.pending.package.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        var stalledHandler = new StallingPackageHandler();
        var servingHandler = new GalleryPackageHandler(
            packageId,
            version,
            PackageDocuments(1));
        using IPackageSourceClient stalledSource =
            Gallery(stalledHandler);
        using IPackageSourceClient servingSource =
            Gallery(servingHandler);

        Assert.Equal(
            stalledSource.Source.Producer,
            servingSource.Source.Producer);
        Assert.NotSame(stalledSource, servingSource);

        Task<BrowserPackage> stalled =
            BrowserPackageWorkspace.AcquireAsync(
                packageId,
                version,
                stalledSource,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromMilliseconds(500));
        await stalledHandler.RequestStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        BrowserPackage served =
            await BrowserPackageWorkspace.AcquireAsync(
                packageId,
                version,
                servingSource,
                PackageSourceIdentity.NuGetOrg,
                TimeSpan.FromSeconds(5));

        Assert.Equal(packageId, served.PackageId);
        Assert.Equal(version, served.Version);
        await Assert.ThrowsAsync<TimeoutException>(() => stalled);
        Assert.Equal(1, stalledHandler.Requests);
        Assert.Single(servingHandler.Requested);
    }

    [Fact]
    public void PackageAcquisition_ExpiredDeadlineCannotPublishReservedContent()
    {
        using var deadline =
            new BrowserPackageWorkspace.BrowserPackageOperationDeadline(
                TimeSpan.FromMilliseconds(10));
        var inner = new RecordingTransferPolicy();
        var policy =
            new BrowserPackageWorkspace.BrowserPackageOperationTransferPolicy(
                inner,
                deadline);
        using IPackagePayloadReservation reservation =
            policy.ApplyDeadline(inner.Reservation);
        while (!deadline.HasExpired)
            Thread.SpinWait(100);

        Assert.Throws<TimeoutException>(() => reservation.Complete());
        Assert.False(inner.Reservation.Completed);
    }

    [Fact]
    public async Task PackageOperation_LateFailureBecomesVisibleTimeout()
    {
        TimeoutException failure =
            await Assert.ThrowsAsync<TimeoutException>(
                () => BrowserPackageWorkspace.RunPackageOperationAsync<int>(
                    deadline =>
                    {
                        while (!deadline.HasExpired)
                            Thread.SpinWait(100);
                        return Task.FromException<int>(
                            new InvalidOperationException(
                                "Synchronous work failed after the deadline."));
                    },
                    TimeSpan.FromMilliseconds(10),
                    TestContext.Current.CancellationToken));

        Assert.IsType<InvalidOperationException>(failure.InnerException);
    }

    [Fact]
    public async Task PackageOperation_LateSuccessDisposesOwnedResult()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await Coordinate(
            "Late.Success",
            Package(image, "lib/net11.0/Late.Success.dll"));
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync([coordinate], TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;

        await Assert.ThrowsAsync<TimeoutException>(
            () => BrowserPackageWorkspace.RunPackageOperationAsync<
                BrowserScopeLease<BrowserInspectionScope>>(
                deadline =>
                {
                    BrowserScopeLease<BrowserInspectionScope> lease =
                        BrowserPackageWorkspace.LeaseScope(scope);
                    while (!deadline.HasExpired)
                        Thread.SpinWait(100);
                    return Task.FromResult(lease);
                },
                TimeSpan.FromMilliseconds(10),
                TestContext.Current.CancellationToken));

        // Removal waits for the caller's own protected use, and then retires: the abandoned
        // late result released the lease it took rather than stranding it here.
        await BrowserPackageWorkspace.RemoveScopeAsync(scope);
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(scope));
        await scopeLease.DisposeAsync();
        Assert.False(BrowserPackageWorkspace.IsScopeRetained(scope));
    }

    [Fact]
    public async Task PackageOperation_LateCancellationPreservesCleanupFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var owned = new FailingScope();

        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(
            () => BrowserPackageWorkspace.RunPackageOperationAsync(
                _ =>
                {
                    cancellation.Cancel();
                    return Task.FromResult(owned);
                },
                TimeSpan.FromSeconds(5),
                cancellation.Token));

        Assert.Collection(
            failure.InnerExceptions,
            primary => Assert.IsAssignableFrom<OperationCanceledException>(primary),
            cleanup => Assert.IsType<InvalidOperationException>(cleanup));
        Assert.Equal(1, owned.DisposalCount);
    }

    [Fact]
    public async Task PackageOperation_LateCallerCancellationRemainsCancellation()
    {
        using var callerCancellation = new CancellationTokenSource();
        Task<int> operation =
            BrowserPackageWorkspace.RunPackageOperationAsync<int>(
                async _ =>
                {
                    callerCancellation.Cancel();
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(50),
                        TestContext.Current.CancellationToken);
                    throw new OperationCanceledException(
                        callerCancellation.Token);
                },
                TimeSpan.FromMilliseconds(10),
                callerCancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operation);
    }

    [Fact]
    public async Task PackageVersionIndex_ValidatesTheIdBeforeRequestingIt()
    {
        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPackageWorkspace.GetVersionsAsync("evil/../other"));

        Assert.Contains("package coordinate", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExactDependencyNavigation_DoesNotRequireAListedVersion()
    {
        string resolved =
            await BrowserPackageWorkspace.ResolveDependencyVersionAsync(
                "Example.Package",
                "[999999.0]");

        Assert.Equal("999999.0.0", resolved);
    }

    [Fact]
    public async Task DependencyRangeUsesAuthoritativeGalleryListingState()
    {
        var handler = new GalleryVersionHandler();
        using IPackageSourceClient source = Gallery(handler);

        string resolved =
            await BrowserPackageWorkspace.ResolveDependencyVersionAsync(
                "Contoso",
                "[1.0.0,2.0.0)",
                source,
                TimeSpan.FromSeconds(5));

        Assert.Equal("1.1.0", resolved);
        Assert.Equal(
            [
                "https://globalcdn.nuget.org/v3-flatcontainer/contoso/index.json",
                "https://globalcdn.nuget.org/v3/registration5-gz-semver2/contoso/index.json",
            ],
            handler.Requested);
    }

    [Fact]
    public async Task DependencyRangePreservesGalleryRegistrationTimeout()
    {
        var handler = new StallingGalleryRegistrationHandler();
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create(),
                handler,
                new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromMilliseconds(100),
                    OperationTimeout = TimeSpan.FromSeconds(5),
                });

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPackageWorkspace.ResolveDependencyVersionAsync(
                    "contoso",
                    "[1.0.0,2.0.0)",
                    source,
                    TimeSpan.FromSeconds(10)));

        Assert.Equal(
            "The package source operation exceeded its configured deadline.",
            failure.Message);
        Assert.Equal(1, handler.FlatContainerRequests);
        Assert.True(handler.RegistrationRequests >= 1);
    }

    [Fact]
    public void BrowserGalleryDeadlineLeavesTimeForSourceTimeout()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(5),
            BrowserPackageWorkspace.PackageOperationTimeout
            - BrowserPackageWorkspace.GalleryOperationTimeout);
        NuGetGalleryPackageSourceClient gallery =
            Assert.IsType<NuGetGalleryPackageSourceClient>(
                BrowserPackageWorkspace.Gallery);
        Assert.Equal(
            BrowserPackageWorkspace.GalleryOperationTimeout,
            gallery.RequestTimeout);
        Assert.Equal(
            BrowserPackageWorkspace.GalleryOperationTimeout,
            gallery.OperationTimeout);
    }

    [Fact]
    public async Task VersionPickerPreservesGalleryRegistrationTimeout()
    {
        var handler = new StallingGalleryRegistrationHandler();
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create(),
                handler,
                new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromMilliseconds(100),
                    OperationTimeout = TimeSpan.FromSeconds(5),
                });

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPackageWorkspace.GetVersionsAsync(
                    "contoso",
                    source,
                    TimeSpan.FromSeconds(10)));

        Assert.Equal(
            "The package source operation exceeded its configured deadline.",
            failure.Message);
        Assert.Equal(1, handler.FlatContainerRequests);
        Assert.True(handler.RegistrationRequests >= 1);
    }

    private static BrowserDependencyCoordinateMatch MatchDependencyCoordinate(
        BrowserDependencyCoordinateCandidate[] candidates,
        string packageId,
        string declaredRange)
    {
        string candidatesJson = JsonSerializer.Serialize(
            candidates,
            BrowserPackageJsonContext.Default.BrowserDependencyCoordinateCandidateArray);
        string resultJson = PackageExports.MatchPackageDependencyCoordinate(
            packageId,
            declaredRange,
            candidatesJson);
        return JsonSerializer.Deserialize(
            resultJson,
            BrowserPackageJsonContext.Default.BrowserDependencyCoordinateMatch)
            ?? throw new InvalidOperationException("The dependency-coordinate result is absent.");
    }

    // The default package load runs under explicit bounds and says so when it stops early. Both
    // halves matter: an ordinary projection must be untouched, and the bound must be reachable.
    [Fact]
    public async Task ApiSurfaceProjection_IsBoundedAndReportsTruncation()
    {
        byte[] image = File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
            [await Coordinate("Bounded.Surface", Package(image, "lib/net11.0/Bounded.Surface.dll"))],
            TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;

        AssemblyContextApiSurfaceResult complete = scope.UseSurface(group =>
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                BrowserApiSurfacePolicy.Limits));

        Assert.Null(complete.Truncation);
        Assert.True(complete.IsComplete);
        Assert.Null(BrowserApiSurfacePolicy.TruncationNotice(complete.Truncation));
        int projectedTypes = complete.Assemblies.Assemblies
            .OfType<AssemblyContextEntry<AssemblyApiSurface>.Available>()
            .Sum(entry => entry.Value.Surface.Types.Count);
        Assert.True(projectedTypes > 0);
        Assert.True(projectedTypes < BrowserApiSurfacePolicy.MaxTypes);

        AssemblyContextApiSurfaceResult truncated = scope.UseSurface(group =>
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                new ApiSurfaceProjectionLimits(1, 1, 1, 1, 1, int.MaxValue)));

        Assert.NotNull(truncated.Truncation);
        Assert.False(truncated.IsComplete);
        string notice = Assert.IsType<string>(
            BrowserApiSurfacePolicy.TruncationNotice(truncated.Truncation));
        Assert.Contains("API surface truncated", notice, StringComparison.Ordinal);

        // A truncation is carried beside participant failures, never instead of them.
        Assert.Equal(
            notice,
            BrowserSurfaceProjection.Notice(truncated.Assemblies.Assemblies, notice));

        AssemblyContextApiSurfaceResult textTruncated = scope.UseSurface(group =>
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                new ApiSurfaceProjectionLimits(
                    1,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    1)));
        Assert.Equal(
            ApiSurfaceProjectionLimit.RetainedTextCharacters,
            textTruncated.Truncation!.Limit);
        Assert.Contains(
            "retained text character",
            BrowserApiSurfacePolicy.TruncationNotice(textTruncated.Truncation),
            StringComparison.Ordinal);
    }

    // A nested Outer+Inner and a type whose own metadata name is literally "Outer+Inner" share a
    // flattened spelling. The browser must carry an identity that tells them apart, and must not
    // publish the flattened one where it names both.
    [Fact]
    public void CallGraphTargets_DistinguishNestedFromLiteralPlusDeclaringTypes()
    {
        TypeRef nested = ResolvedDefinition("Example", ["Outer", "Inner"]);
        TypeRef literalPlus = ResolvedDefinition("Example", ["Outer+Inner"]);
        TypeRef returnType = TypeRef.Definition(TypeRef.CoreLibrary, "System", "Void");
        var nestedMember = new MemberRef(
            nested,
            "Run",
            ImmutableArray<TypeRef>.Empty,
            returnType,
            MemberKind.Method);
        var literalPlusMember = new MemberRef(
            literalPlus,
            "Run",
            ImmutableArray<TypeRef>.Empty,
            returnType,
            MemberKind.Method);
        CallGraphNode[] nodes =
        [
            new(
                0,
                GraphNodeIdentity.FromMember(nestedMember),
                nestedMember,
                "nested",
                CallGraphNodeKind.Normal),
            new(
                1,
                GraphNodeIdentity.FromMember(literalPlusMember),
                literalPlusMember,
                "literal",
                CallGraphNodeKind.Normal),
        ];

        BrowserCallGraphTargetInfo[] targets = BrowserCallGraphProjection.Targets(nodes);

        // Both declaring types flatten to the same metadata spelling. That spelling genuinely
        // names the nested type, so it is still published for it; for the literal-plus type it
        // names the other one, so it is withheld rather than published as if it named this one.
        Assert.Equal("Outer+Inner", nested.Name);
        Assert.Equal("Outer+Inner", literalPlus.Name);
        Assert.Equal("Example.Outer+Inner", targets[0].TypeMetadataId);
        Assert.Null(targets[1].TypeMetadataId);

        // The escaped structured identity resolves each target uniquely, and is the same
        // projection a browsable type row carries as its id.
        Assert.Equal("Example.Outer+Inner", targets[0].TypeDefinitionId);
        Assert.Equal(@"Example.Outer\+Inner", targets[1].TypeDefinitionId);
        Assert.NotEqual(targets[0].TypeDefinitionId, targets[1].TypeDefinitionId);
        Assert.Equal(
            targets[0].TypeDefinitionId,
            BrowserSurfaceProjection.Type(
                new ApiType
                {
                    Namespace = "Example",
                    Name = "Outer.Inner",
                    MetadataName = "Outer+Inner",
                    DefinitionName = DefinitionName("Example", ["Outer", "Inner"]),
                    Kind = "class",
                },
                "Example.dll",
                "asset:example",
                "Example").DefinitionId);
        Assert.Equal(
            targets[1].TypeDefinitionId,
            BrowserSurfaceProjection.Type(
                new ApiType
                {
                    Namespace = "Example",
                    Name = "Outer+Inner",
                    MetadataName = "Outer+Inner",
                    DefinitionName = DefinitionName("Example", ["Outer+Inner"]),
                    Kind = "class",
                },
                "Example.dll",
                "asset:example",
                "Example").DefinitionId);
    }

    [Fact]
    public void CallGraphTargets_KeepTheLegacyIdentityWhereItIsUnambiguous()
    {
        TypeRef declaring = ResolvedDefinition("Example", ["Outer`1", "Widget`1"]);
        var member = new MemberRef(
            declaring,
            "Run",
            ImmutableArray<TypeRef>.Empty,
            TypeRef.Definition(TypeRef.CoreLibrary, "System", "Void"),
            MemberKind.Method);
        CallGraphNode[] nodes =
        [
            new(
                0,
                GraphNodeIdentity.FromMember(member),
                member,
                "nested",
                CallGraphNodeKind.Normal),
        ];

        BrowserCallGraphTargetInfo[] targets = BrowserCallGraphProjection.Targets(nodes);

        Assert.Equal("Example.Outer`1+Widget`1", targets[0].TypeMetadataId);
        Assert.Equal("Example.Outer`1+Widget`1", targets[0].TypeDefinitionId);
    }

    static TypeRef ResolvedDefinition(string @namespace, string[] segments)
        => TypeRef.Definition(
            "Example",
            @namespace,
            string.Join('+', segments),
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(),
                DefinitionName(@namespace, segments)));

    static MetadataTypeDefinitionName DefinitionName(string @namespace, string[] segments)
        => Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(@namespace, [.. segments]))
            .Name;

    [Fact]
    public async Task BrowserWorkspace_SingleCoordinateScopeIsArtifactBacked()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            $"Artifact.Single.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/Artifact.Single.dll"),
            TestContext.Current.CancellationToken);

        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [coordinate],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;

        Assert.True(scope.ArtifactBacked);
        BrowserWorkspaceParticipant participant =
            Assert.Single(scope.SurfaceParticipants);
        Assert.Same(
            participant,
            scope.SurfaceParticipant(
                coordinate,
                coordinate.DefaultAsset
                    ?? throw new InvalidOperationException(
                        "The artifact coordinate selected no default asset.")));
        AssemblyContextApiSurfaceResult surface = scope.UseSurface(
            group => AssemblyContextApiSurfaceQuery.Execute(group));
        Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
            Assert.Single(surface.Assemblies.Assemblies));
    }

    [Fact]
    public async Task BrowserWorkspace_CompositeScopeKeepsBindingConsistentRoles()
    {
        byte[] surfaceImage =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] otherImage =
            File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location);
        string firstId = $"Artifact.Composite.A.{Guid.NewGuid():N}";
        string secondId = $"Artifact.Composite.B.{Guid.NewGuid():N}";
        BrowserPackageCoordinate first = await ArtifactCoordinate(
            firstId,
            Package(surfaceImage, $"lib/net11.0/{firstId}.dll"),
            TestContext.Current.CancellationToken);
        BrowserPackageCoordinate second = await ArtifactCoordinate(
            secondId,
            Package(otherImage, $"lib/net11.0/{secondId}.dll"),
            TestContext.Current.CancellationToken);

        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [first, second],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;

        Assert.False(scope.ArtifactBacked);
        Assert.Equal(2, scope.SurfaceParticipants.Length);
        Assert.Equal(firstId, scope.Coordinate(first).PackageId);
        Assert.Equal(secondId, scope.Coordinate(second).PackageId);
        AssemblyContextApiSurfaceResult surface = scope.UseSurface(
            group => AssemblyContextApiSurfaceQuery.Execute(group));
        Assert.Equal(2, surface.Assemblies.Assemblies.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrowserWorkspace_ConcurrentScopeOpensShareOneRealization(bool unbound)
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            $"Artifact.SingleFlight.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/Artifact.SingleFlight.dll"),
            TestContext.Current.CancellationToken);

        // Fill the registry so neither open can be admitted immediately: three workspaces whose
        // queries are still running, and one whose retirement is suspended inside disposal.
        var held = new List<BrowserScopeLease<GatedScope>>();
        var settled = new TaskCompletionSource();
        settled.SetResult();
        for (int index = 0; index < BrowserPackageWorkspace.MaxOpenScopes - 1; index++)
        {
            ScopeReservation reservation =
                await BrowserPackageWorkspace.ReserveScopeAsync(
                    TestContext.Current.CancellationToken);
            held.Add(await BrowserPackageWorkspace.RegisterScopeAsync(
                reservation,
                $"single-flight-holder-{index}-{Guid.NewGuid():N}",
                new GatedScope(settled),
                ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal)));
        }

        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new GatedScope(release);
        await BrowserPackageWorkspace.RegisterScopeAsync(
            $"single-flight-closing-{Guid.NewGuid():N}",
            closing);
        Task removal = BrowserPackageWorkspace.RemoveScopeAsync(closing).AsTask();
        await closing.DisposeStarted.Task;
        Assert.False(removal.IsCompleted);

        Task<BrowserScopeLease<BrowserInspectionScope>> firstOpen =
            OpenAsync();
        Task<BrowserScopeLease<BrowserInspectionScope>> secondOpen =
            OpenAsync();

        // Both callers are genuinely suspended: the single freed slot is not available until the
        // gated retirement settles.
        Assert.False(firstOpen.IsCompleted);
        Assert.False(secondOpen.IsCompleted);

        release.SetResult();
        await removal;
        BrowserScopeLease<BrowserInspectionScope> firstLease = await firstOpen;
        BrowserScopeLease<BrowserInspectionScope> secondLease = await secondOpen;
        BrowserInspectionScope first = firstLease.Scope;
        BrowserInspectionScope second = secondLease.Scope;

        // Only one slot was freed, and both callers hold the one realization it admitted: the
        // caller that resumed second joined the entry rather than demanding a second slot.
        Assert.Same(first, second);
        Assert.NotSame(firstLease, secondLease);
        Assert.True(first.ArtifactBacked);
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(first));
        Assert.True(closing.Disposed);

        // Each caller's use is independent: releasing one leaves the other's workspace usable.
        await firstLease.DisposeAsync();
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(second));
        Assert.NotEmpty(
            second.UseSurface(group => AssemblyContextApiSurfaceQuery.Execute(group))
                .Assemblies
                .Assemblies);
        await secondLease.DisposeAsync();

        foreach (BrowserScopeLease<GatedScope> lease in held)
            await lease.DisposeAsync();

        Task<BrowserScopeLease<BrowserInspectionScope>> OpenAsync() =>
            unbound
                ? BrowserPackageWorkspace.OpenScopeAsync(
                    coordinate.PackageId,
                    coordinate.Version,
                    coordinate.Framework,
                    TestContext.Current.CancellationToken)
                : BrowserPackageWorkspace.OpenScopeAsync(
                    [coordinate],
                    TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task BrowserWorkspace_ClosingScopeKeepsItsRegistrySlotUntilDisposalSettles()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        string packageId = $"Artifact.Closing.{Guid.NewGuid():N}";
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            packageId,
            Package(image, "lib/net11.0/Artifact.Closing.dll"),
            TestContext.Current.CancellationToken);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new GatedScope(release);
        await BrowserPackageWorkspace.RegisterScopeAsync(
            $"closing-{Guid.NewGuid():N}",
            closing,
            [BrowserPackageWorkspace.PackageKey(packageId, coordinate.Version)]);
        int occupied = BrowserPackageWorkspace.Stats().Workspaces;

        Task removal = BrowserPackageWorkspace.RemoveScopeAsync(closing).AsTask();
        await closing.DisposeStarted.Task;

        Assert.False(removal.IsCompleted);
        Assert.Equal(occupied, BrowserPackageWorkspace.Stats().Workspaces);
        Assert.False(BrowserPackageWorkspace.IsScopeRetained(closing));
        Assert.Throws<InvalidOperationException>(
            () => BrowserPackageWorkspace.LeaseScope(closing));

        release.SetResult();
        await removal;

        Assert.Equal(occupied - 1, BrowserPackageWorkspace.Stats().Workspaces);
        Assert.True(closing.Disposed);
    }

    [Fact]
    public async Task BrowserWorkspace_PackageEvictionAwaitsScopeClosedByAnotherPath()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        string packageId = $"Artifact.Racing.{Guid.NewGuid():N}";
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            packageId,
            Package(image, "lib/net11.0/Artifact.Racing.dll", 60 * MiB),
            TestContext.Current.CancellationToken);
        string packageKey =
            BrowserPackageWorkspace.PackageKey(packageId, coordinate.Version);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new GatedScope(release);
        await BrowserPackageWorkspace.RegisterScopeAsync(
            $"racing-{Guid.NewGuid():N}",
            closing,
            [packageKey]);

        Task removal = BrowserPackageWorkspace.RemoveScopeAsync(closing).AsTask();
        await closing.DisposeStarted.Task;
        Assert.False(removal.IsCompleted);

        Task<BrowserPackageWorkspace.PackageDownloadReservation> pressure =
            BrowserPackageWorkspace.ReservePackageDownloadAsync(
                $"artifact.racing.pressure.{Guid.NewGuid():N}@1.0.0",
                120L * MiB).AsTask();

        Assert.False(pressure.IsCompleted);
        Assert.Contains(
            packageKey,
            BrowserPackageWorkspace.ResidentPackageKeys());

        release.SetResult();
        using (await pressure)
        {
            await removal;
            Assert.True(closing.Disposed);
            Assert.DoesNotContain(
                packageKey,
                BrowserPackageWorkspace.ResidentPackageKeys());
        }
    }

    [Fact]
    public async Task WorkspaceOccurrences_LeaseAcquiredDuringRetirementKeepsArchiveResident()
    {
        BrowserWorkspaceOccurrenceOperations.ClearCurrent();
        (await BrowserPackageWorkspace.ReservePackageDownloadAsync(
            $"artifact.occurrence.drain.{Guid.NewGuid():N}@1.0.0",
            128L * MiB)).Dispose();

        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            $"Artifact.OccurrenceLease.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/InspectWeb.Engine.Tests.dll"),
            TestContext.Current.CancellationToken);
        string packageKey =
            BrowserPackageWorkspace.PackageKey(coordinate.PackageId, coordinate.Version);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new GatedScope(release);
        await BrowserPackageWorkspace.RegisterScopeAsync(
            $"occurrence-retirement-{Guid.NewGuid():N}",
            closing,
            [packageKey]);
        Task<BrowserPackageWorkspace.PackageDownloadReservation> pressure =
            BrowserPackageWorkspace.ReservePackageDownloadAsync(
                $"artifact.occurrence.pressure.{Guid.NewGuid():N}@1.0.0",
                128L * MiB).AsTask();
        try
        {
            await closing.DisposeStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            BrowserWorkspacePackageOccurrenceView view =
                await BrowserWorkspaceOccurrenceOperations.QueryAsync(
                    [new BrowserPackageRequest(
                        coordinate.PackageId,
                        coordinate.Version,
                        coordinate.Framework)]);

            release.TrySetResult();
            InvalidOperationException error =
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    using var unexpected = await pressure;
                });
            Assert.Contains("package-cache limit", error.Message);
            Assert.Contains(packageKey, BrowserPackageWorkspace.ResidentPackageKeys());

            BrowserWorkspacePackageOccurrenceActivation activation =
                JsonSerializer.Deserialize(
                    await PackageExports.ActivateWorkspacePackageOccurrence(
                        Assert.Single(view.Occurrences).Action),
                    BrowserPackageJsonContext.Default
                        .BrowserWorkspacePackageOccurrenceActivation)!;
            Assert.True(activation.Activated);
            Assert.False(activation.Superseded);
            Assert.NotNull(activation.Package);
        }
        finally
        {
            release.TrySetResult();
            BrowserWorkspaceOccurrenceOperations.ClearCurrent();
            await BrowserPackageWorkspace.RemoveScopeAsync(closing);
        }
    }

    [Fact]
    public async Task BrowserWorkspace_ConcurrentReservationsStayWithinTheByteBudget()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        string packageId = $"Artifact.Budget.{Guid.NewGuid():N}";

        // Drain every unleased archive first: this gate measures the budget its own reservations
        // and its own dependent workspace occupy, not whatever an earlier test left resident.
        (await BrowserPackageWorkspace.ReservePackageDownloadAsync(
            $"artifact.budget.drain.{Guid.NewGuid():N}@1.0.0",
            128L * MiB)).Dispose();

        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            packageId,
            Package(image, "lib/net11.0/Artifact.Budget.dll", 60 * MiB),
            TestContext.Current.CancellationToken);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new GatedScope(release);
        await BrowserPackageWorkspace.RegisterScopeAsync(
            $"budget-{Guid.NewGuid():N}",
            closing,
            [BrowserPackageWorkspace.PackageKey(packageId, coordinate.Version)]);
        Task removal = BrowserPackageWorkspace.RemoveScopeAsync(closing).AsTask();
        await closing.DisposeStarted.Task;

        Task<BrowserPackageWorkspace.PackageDownloadReservation> first =
            BrowserPackageWorkspace.ReservePackageDownloadAsync(
                $"artifact.budget.a.{Guid.NewGuid():N}@1.0.0",
                70L * MiB).AsTask();
        Task<BrowserPackageWorkspace.PackageDownloadReservation> second =
            BrowserPackageWorkspace.ReservePackageDownloadAsync(
                $"artifact.budget.b.{Guid.NewGuid():N}@1.0.0",
                70L * MiB).AsTask();

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        release.SetResult();
        await removal;

        BrowserPackageWorkspace.PackageDownloadReservation? admittedFirst =
            await TryReserve(first);
        BrowserPackageWorkspace.PackageDownloadReservation? admittedSecond =
            await TryReserve(second);
        try
        {
            Assert.True(
                BrowserPackageWorkspace.Stats().ResidentBytes <= 128L * MiB,
                "Concurrent reservations overshot the browser package-cache byte budget.");
            Assert.True(admittedFirst is not null || admittedSecond is not null);
            Assert.True(admittedFirst is null || admittedSecond is null);
        }
        finally
        {
            admittedFirst?.Dispose();
            admittedSecond?.Dispose();
        }

        static async Task<BrowserPackageWorkspace.PackageDownloadReservation?> TryReserve(
            Task<BrowserPackageWorkspace.PackageDownloadReservation> reservation)
        {
            try
            {
                return await reservation;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// A terminal cleanup failure leaves the entry charged and unavailable with an observable
    /// failure record, and the archive it depended on stays charged with it: the registry never
    /// hands that capacity to a later workspace, and a runtime restart is the only recovery.
    /// </summary>
    [Fact]
    public async Task BrowserWorkspace_FailedScopeCloseStaysChargedAndUnavailable()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        string packageId = $"Artifact.CloseFailure.{Guid.NewGuid():N}";
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            packageId,
            Package(image, "lib/net11.0/Artifact.CloseFailure.dll", 60 * MiB),
            TestContext.Current.CancellationToken);
        string packageKey =
            BrowserPackageWorkspace.PackageKey(packageId, coordinate.Version);
        var failing = new FailingScope();
        await BrowserPackageWorkspace.RegisterScopeAsync(
            $"close-failure-{Guid.NewGuid():N}",
            failing,
            [packageKey]);
        BrowserScopeLease<FailingScope> lease =
            BrowserPackageWorkspace.LeaseScope(failing);
        await BrowserPackageWorkspace.RemoveScopeAsync(failing);
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(failing));

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await lease.DisposeAsync());

        Assert.Contains(
            "The gated browser scope failed to close.",
            failure.Message,
            StringComparison.Ordinal);
        Assert.False(BrowserPackageWorkspace.IsScopeRetained(failing));
        Assert.Equal(1, BrowserPackageWorkspace.QuarantinedWorkspaces);

        // The entry stays charged: the archive it depended on is not handed back to the cache,
        // and an admission that needs its capacity is rejected with the recorded failure.
        Assert.Contains(packageKey, BrowserPackageWorkspace.ResidentPackageKeys());
        var pressure = new List<BrowserScopeLease<BrowserInspectionScope>>();
        InvalidOperationException? rejection = null;
        try
        {
            for (int index = 0; index < BrowserPackageWorkspace.MaxOpenScopes; index++)
            {
                string pressureId = $"Artifact.CloseFailure.Pressure.{index}.{Guid.NewGuid():N}";
                pressure.Add(await BrowserPackageWorkspace.OpenScopeAsync(
                    [
                        await ArtifactCoordinate(
                            pressureId,
                            Package(image, $"lib/net11.0/{pressureId}.dll"),
                            TestContext.Current.CancellationToken),
                    ],
                    TestContext.Current.CancellationToken));
            }
        }
        catch (InvalidOperationException capacityFailure)
        {
            rejection = capacityFailure;
        }
        finally
        {
            foreach (BrowserScopeLease<BrowserInspectionScope> held in pressure)
                await held.DisposeAsync();
        }

        Assert.NotNull(rejection);
        Assert.Contains(
            "stay charged after a terminal cleanup failure",
            rejection!.Message,
            StringComparison.Ordinal);

        try
        {
            InvalidOperationException archiveRejection =
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await BrowserPackageWorkspace.ReservePackageDownloadAsync(
                        $"artifact.close-failure.archive-pressure.{Guid.NewGuid():N}@1.0.0",
                        120L * MiB));
            Assert.Contains(
                "failed to release its retained content",
                archiveRejection.Message,
                StringComparison.Ordinal);
            Assert.Contains(packageKey, BrowserPackageWorkspace.ResidentPackageKeys());
        }
        finally
        {
            BrowserPackageWorkspace.SimulateRuntimeRestart();
        }
        Assert.Equal(0, BrowserPackageWorkspace.QuarantinedWorkspaces);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrowserWorkspace_DuplicateCandidateRetiresItsOwnReservation(bool cleanupFails)
    {
        var settled = new TaskCompletionSource();
        settled.SetResult();
        var retained = new GatedScope(settled);
        string key = $"duplicate-candidate-{Guid.NewGuid():N}";
        await using ScopeReservation firstReservation =
            await BrowserPackageWorkspace.ReserveScopeAsync(
                TestContext.Current.CancellationToken);
        await using BrowserScopeLease<GatedScope> first =
            await BrowserPackageWorkspace.RegisterScopeAsync(
                firstReservation,
                key,
                retained,
                ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal));
        await using ScopeReservation duplicateReservation =
            await BrowserPackageWorkspace.ReserveScopeAsync(
                TestContext.Current.CancellationToken);
        int occupied = BrowserPackageWorkspace.Stats().Workspaces;
        var failing = new FailingScope();
        var healthy = new GatedScope(settled);
        IAsyncDisposable candidate = cleanupFails ? failing : healthy;

        try
        {
            if (cleanupFails)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await BrowserPackageWorkspace.RegisterScopeAsync(
                        duplicateReservation,
                        key,
                        candidate,
                        ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal)));
                Assert.Equal(1, failing.DisposalCount);
                Assert.Equal(occupied, BrowserPackageWorkspace.Stats().Workspaces);
                Assert.Equal(1, BrowserPackageWorkspace.QuarantinedWorkspaces);
            }
            else
            {
                await using BrowserScopeLease<IAsyncDisposable> joined =
                    await BrowserPackageWorkspace.RegisterScopeAsync(
                        duplicateReservation,
                        key,
                        candidate,
                        ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal));
                Assert.Same(retained, joined.Scope);
                Assert.True(healthy.Disposed);
                Assert.Equal(occupied - 1, BrowserPackageWorkspace.Stats().Workspaces);
            }

            await BrowserPackageWorkspace.RemoveScopeAsync(retained);
            await first.DisposeAsync();
            Assert.True(retained.Disposed);
        }
        finally
        {
            BrowserPackageWorkspace.SimulateRuntimeRestart();
        }
    }

    [Fact]
    public async Task BrowserWorkspace_CancelledScopeOpenYieldsNoScopeAndKeepsRegistryUsable()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            $"Artifact.Waiter.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/Artifact.Waiter.dll"),
            TestContext.Current.CancellationToken);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await BrowserPackageWorkspace.OpenScopeAsync(
                [coordinate],
                cancelled.Token));

        await using BrowserScopeLease<BrowserInspectionScope> openedLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [coordinate],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope opened = openedLease.Scope;

        Assert.True(opened.ArtifactBacked);
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(opened));
    }

    [Fact]
    public async Task BrowserWorkspace_ArtifactScopeDisposalClosesItsSession()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            $"Artifact.Disposal.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/Artifact.Disposal.dll"),
            TestContext.Current.CancellationToken);
        BrowserInspectionScope scope =
            await BrowserInspectionScope.CreateAsync(
                [coordinate],
                TestContext.Current.CancellationToken);
        Assert.True(scope.ArtifactBacked);
        Assert.Single(scope.SurfaceParticipants);

        await scope.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(
            () => scope.UseSurface(
                group => AssemblyContextApiSurfaceQuery.Execute(group)));
        await scope.DisposeAsync();
    }

    [Fact]
    public async Task BrowserWorkspace_ArtifactScopeKeepsRejectedParticipantVisible()
    {
        BrowserPackageCoordinate malformed = await ArtifactCoordinate(
            $"Artifact.Malformed.{Guid.NewGuid():N}",
            Package([0x01, 0x02, 0x03], "lib/net11.0/Artifact.Malformed.dll"),
            TestContext.Current.CancellationToken);

        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [malformed],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;

        Assert.True(scope.ArtifactBacked);
        Assert.Single(scope.SurfaceParticipants);
        AssemblyContextApiSurfaceResult surface = scope.UseSurface(
            group => AssemblyContextApiSurfaceQuery.Execute(group));
        Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Rejected>(
            Assert.Single(surface.Assemblies.Assemblies));
    }

    [Fact]
    public async Task BrowserWorkspace_ReplacedArchiveRejectsStaleArtifactCoordinate()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        string packageId = $"Artifact.Stale.{Guid.NewGuid():N}";
        BrowserPackageCoordinate stale = await ArtifactCoordinate(
            packageId,
            Package(image, "lib/net11.0/Artifact.Stale.dll"),
            TestContext.Current.CancellationToken);

        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                Package(image, "lib/net11.0/Artifact.Stale.Replacement.dll"),
                fromCache: false));

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await BrowserPackageWorkspace.OpenScopeAsync(
                    [stale],
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "escaped aggregate cache accounting",
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BrowserWorkspace_CacheRoomAwaitsDependentScopeDisposal()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            $"Artifact.Evicted.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/Artifact.Evicted.dll", 60 * MiB),
            TestContext.Current.CancellationToken);
        BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [coordinate],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(scope));

        // The lease is the caller's protection: archive pressure that could only be satisfied by
        // dropping the archive this query is reading is rejected instead of silently evicting it.
        InvalidOperationException rejected =
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await BrowserPackageWorkspace.ReservePackageDownloadAsync(
                    $"artifact.pressure.{Guid.NewGuid():N}@1.0.0",
                    120L * MiB));
        Assert.Contains(
            "cannot accommodate the requested workspace",
            rejected.Message,
            StringComparison.Ordinal);
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(scope));
        Assert.NotEmpty(
            scope.UseSurface(group => AssemblyContextApiSurfaceQuery.Execute(group))
                .Assemblies
                .Assemblies);

        // Once the protected use has been released the same pressure is satisfied by awaiting the
        // dependent workspace's disposal, and only then are the retained bytes counted as free.
        await scopeLease.DisposeAsync();
        using (await BrowserPackageWorkspace.ReservePackageDownloadAsync(
            $"artifact.pressure.{Guid.NewGuid():N}@1.0.0",
            120L * MiB))
        {
            Assert.False(BrowserPackageWorkspace.IsScopeRetained(scope));
            Assert.Throws<ObjectDisposedException>(
                () => scope.UseSurface(
                    group => AssemblyContextApiSurfaceQuery.Execute(group)));
        }
    }

    [Fact]
    public async Task BrowserWorkspace_RepeatedUnboundRequestsJoinOneRetainedBinding()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        string packageId = $"Artifact.Unbound.{Guid.NewGuid():N}";
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                Package(image, $"lib/net11.0/{packageId}.dll"),
                fromCache: false));

        await using BrowserScopeLease<BrowserInspectionScope> firstLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                "1.0.0",
                targetFramework: null,
                TestContext.Current.CancellationToken);
        await using BrowserScopeLease<BrowserInspectionScope> secondLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                "1.0.0",
                targetFramework: null,
                TestContext.Current.CancellationToken);

        // The second unbound request joined the retained workspace before a second selection
        // token could be issued, so both callers read the one artifact-backed realization.
        Assert.Same(firstLease.Scope, secondLease.Scope);
        Assert.True(firstLease.Scope.ArtifactBacked);
        Assert.Single(firstLease.Scope.SurfaceParticipants);
    }

    [Fact]
    public async Task BrowserWorkspace_IndependentlyIssuedBindingsDoNotJoinOnLabelMatch()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        string packageId = $"Artifact.Issued.{Guid.NewGuid():N}";
        BrowserPackageCoordinate first = await ArtifactCoordinate(
            packageId,
            Package(image, $"lib/net11.0/{packageId}.dll"),
            TestContext.Current.CancellationToken);
        BrowserPackageCoordinate second = await BrowserPackageWorkspace.ResolveAsync(
            packageId,
            "1.0.0",
            "net11.0",
            TestContext.Current.CancellationToken);
        Assert.NotNull(second.Binding);
        Assert.NotSame(first.Binding!.SelectionIdentity, second.Binding!.SelectionIdentity);

        await using BrowserScopeLease<BrowserInspectionScope> firstLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [first],
                TestContext.Current.CancellationToken);
        await using BrowserScopeLease<BrowserInspectionScope> secondLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [second],
                TestContext.Current.CancellationToken);

        // Two independently issued selection tokens name the same labels but are not
        // interchangeable: each keeps its own workspace.
        Assert.NotSame(firstLease.Scope, secondLease.Scope);
        Assert.True(firstLease.Scope.ArtifactBacked);
        Assert.True(secondLease.Scope.ArtifactBacked);
    }

    [Fact]
    public async Task BrowserWorkspace_DefaultAndExplicitSelectionRequestsDoNotJoin()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        string packageId = $"Artifact.Selection.{Guid.NewGuid():N}";
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                Package(image, $"lib/net11.0/{packageId}.dll"),
                fromCache: false));

        await using BrowserScopeLease<BrowserInspectionScope> defaultLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                "1.0.0",
                targetFramework: null,
                TestContext.Current.CancellationToken);
        await using BrowserScopeLease<BrowserInspectionScope> explicitLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                "1.0.0",
                "net11.0",
                TestContext.Current.CancellationToken);

        // A default selection is not an explicit one, even when it resolves to the same
        // framework: the two requests keep separate workspaces.
        Assert.NotSame(defaultLease.Scope, explicitLease.Scope);
    }

    [Fact]
    public async Task BrowserWorkspace_ProtectedUseSurvivesWorkspacePressureAcrossAnAsyncReturn()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate protectedCoordinate = await ArtifactCoordinate(
            $"Artifact.Protected.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/Artifact.Protected.dll"),
            TestContext.Current.CancellationToken);
        await using BrowserScopeLease<BrowserInspectionScope> protectedLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [protectedCoordinate],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope protectedScope = protectedLease.Scope;

        // Every later workspace competes for the remaining slots and forces eviction. The
        // protected use is what keeps this caller's workspace out of every candidate set.
        for (int index = 0; index < BrowserPackageWorkspace.MaxOpenScopes + 1; index++)
        {
            BrowserPackageCoordinate pressure = await ArtifactCoordinate(
                $"Artifact.Pressure.{index}.{Guid.NewGuid():N}",
                Package(image, $"lib/net11.0/Artifact.Pressure.{index}.dll"),
                TestContext.Current.CancellationToken);
            await using BrowserScopeLease<BrowserInspectionScope> transient =
                await BrowserPackageWorkspace.OpenScopeAsync(
                    [pressure],
                    TestContext.Current.CancellationToken);
            Assert.True(BrowserPackageWorkspace.IsScopeRetained(protectedScope));
        }

        Assert.True(BrowserPackageWorkspace.IsScopeRetained(protectedScope));
        Assert.NotEmpty(
            protectedScope.UseSurface(group => AssemblyContextApiSurfaceQuery.Execute(group))
                .Assemblies
                .Assemblies);
    }

    [Fact]
    public async Task BrowserWorkspace_CancelledWaiterLeavesTheOtherWaiterUnaffected()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            $"Artifact.Waiters.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/Artifact.Waiters.dll"),
            TestContext.Current.CancellationToken);

        await using ScopeAdmissionGate admission =
            await ScopeAdmissionGate.CreateAsync();

        using var abandoning = new CancellationTokenSource();
        Task<BrowserScopeLease<BrowserInspectionScope>> abandoned =
            BrowserPackageWorkspace.OpenScopeAsync([coordinate], abandoning.Token);
        Task<BrowserScopeLease<BrowserInspectionScope>> waiting =
            BrowserPackageWorkspace.OpenScopeAsync(
                [coordinate],
                TestContext.Current.CancellationToken);
        Assert.False(abandoned.IsCompleted);
        Assert.False(waiting.IsCompleted);

        BrowserScopeLease<BrowserInspectionScope>? lease = null;
        try
        {
            await abandoning.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await abandoned.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken));
            Assert.False(waiting.IsCompleted);
            Assert.False(admission.Retirement.IsCompleted);

            admission.Release();
            lease = await waiting;
            Assert.True(lease.Scope.ArtifactBacked);
            Assert.True(BrowserPackageWorkspace.IsScopeRetained(lease.Scope));
        }
        finally
        {
            admission.Release();
            lease ??= await waiting;
            await lease.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkspaceOccurrences_ActivationCannotOutliveItsView(bool replace)
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            $"Artifact.Activation.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/Artifact.Activation.dll"),
            TestContext.Current.CancellationToken);
        BrowserWorkspacePackageOccurrenceView view =
            BrowserWorkspaceOccurrenceOperations.ReplaceCurrent([coordinate]);
        await using ScopeAdmissionGate admission =
            await ScopeAdmissionGate.CreateAsync();

        Task<string> activation = PackageExports.ActivateWorkspacePackageOccurrence(
            Assert.Single(view.Occurrences).Action);
        try
        {
            Assert.False(activation.IsCompleted);
            if (replace)
                BrowserWorkspaceOccurrenceOperations.ReplaceCurrent([coordinate]);
            else
                BrowserWorkspaceOccurrenceOperations.ClearCurrent();

            admission.Release();
            BrowserWorkspacePackageOccurrenceActivation result =
                JsonSerializer.Deserialize(
                    await activation,
                    BrowserPackageJsonContext.Default
                        .BrowserWorkspacePackageOccurrenceActivation)!;
            Assert.False(result.Activated);
            Assert.True(result.Superseded);
            Assert.Null(result.Package);
        }
        finally
        {
            admission.Release();
            await activation;
            BrowserWorkspaceOccurrenceOperations.ClearCurrent();
        }
    }

    /// <summary>
    /// Registers one archive and resolves it the way production does, so the
    /// returned coordinate carries the acquisition-issued
    /// <c>PackageRootBinding</c> the artifact-backed realization requires.
    /// </summary>
    static async Task<BrowserPackageCoordinate> ArtifactCoordinate(
        string id,
        byte[] nupkg,
        CancellationToken cancellationToken)
    {
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(id, "1.0.0", nupkg, fromCache: false));
        BrowserPackageCoordinate coordinate =
            await BrowserPackageWorkspace.ResolveAsync(
                id,
                "1.0.0",
                "net11.0",
                cancellationToken);
        Assert.NotNull(coordinate.Binding);
        return coordinate;
    }

    static async Task<BrowserPackageCoordinate> Coordinate(string id, byte[] nupkg)
    {
        var package = new BrowserPackage(id, "1.0.0", nupkg, fromCache: false);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(package);
        var assemblyContext = new PackageRootRealization(
            package.Content,
            id,
            package.Version,
            "net11.0");
        Assert.True(assemblyContext.AssetSelection.IsSelected);
        return new BrowserPackageCoordinate(package, assemblyContext);
    }

    static byte[] Package(
        byte[] assembly,
        string assemblyPath,
        int paddingBytes = 0)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (Stream entry = archive
                .CreateEntry(assemblyPath, CompressionLevel.NoCompression)
                .Open())
            {
                entry.Write(assembly);
            }

            if (paddingBytes > 0)
            {
                using Stream padding = archive
                    .CreateEntry("content/padding.bin", CompressionLevel.NoCompression)
                    .Open();
                byte[] block = new byte[64 * 1024];
                int remaining = paddingBytes;
                while (remaining > 0)
                {
                    int count = Math.Min(remaining, block.Length);
                    padding.Write(block, 0, count);
                    remaining -= count;
                }
            }
        }

        return content.ToArray();
    }

    static byte[] PackageEntries(
        params (string Path, byte[] Content)[] entries)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, byte[] bytes) in entries)
            {
                using Stream entry = archive
                    .CreateEntry(path, CompressionLevel.NoCompression)
                    .Open();
                entry.Write(bytes);
            }
        }

        return content.ToArray();
    }

    static byte[] PackagePair(
        byte[] surfaceAssembly,
        byte[] implementationAssembly,
        string assemblyFileName) =>
        PackagePair(
            surfaceAssembly,
            implementationAssembly,
            assemblyFileName,
            assemblyFileName);

    static byte[] PackagePair(
        byte[] surfaceAssembly,
        byte[] implementationAssembly,
        string surfaceAssemblyFileName,
        string implementationAssemblyFileName)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (Stream entry = archive
                .CreateEntry(
                    $"ref/net11.0/{surfaceAssemblyFileName}",
                    CompressionLevel.NoCompression)
                .Open())
            {
                entry.Write(surfaceAssembly);
            }

            using (Stream entry = archive
                .CreateEntry(
                    $"lib/net11.0/{implementationAssemblyFileName}",
                    CompressionLevel.NoCompression)
                .Open())
            {
                entry.Write(implementationAssembly);
            }
        }

        return content.ToArray();
    }

    static byte[] PackagePairWithExtraImplementation(
        byte[] surfaceAssembly,
        byte[] implementationAssembly,
        string assemblyFileName,
        byte[] extraImplementationAssembly,
        string extraAssemblyFileName)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(
            content,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using (Stream entry = archive
                .CreateEntry(
                    $"ref/net11.0/{assemblyFileName}",
                    CompressionLevel.NoCompression)
                .Open())
            {
                entry.Write(surfaceAssembly);
            }

            using (Stream entry = archive
                .CreateEntry(
                    $"lib/net11.0/{assemblyFileName}",
                    CompressionLevel.NoCompression)
                .Open())
            {
                entry.Write(implementationAssembly);
            }

            using (Stream entry = archive
                .CreateEntry(
                    $"lib/net11.0/{extraAssemblyFileName}",
                    CompressionLevel.NoCompression)
                .Open())
            {
                entry.Write(extraImplementationAssembly);
            }
        }

        return content.ToArray();
    }

    static byte[] PlatformPackage(
        params (string Name, byte[] Content)[] assemblies)
    {
        using var content = new MemoryStream();
        using (var archive =
            new ZipArchive(
                content,
                ZipArchiveMode.Create,
                leaveOpen: true))
        {
            foreach ((string name, byte[] bytes) in assemblies)
            {
                using Stream entry = archive
                    .CreateEntry(
                        $"runtimes/linux-x64/lib/net11.0/{name}",
                        CompressionLevel.NoCompression)
                    .Open();
                entry.Write(bytes);
            }
        }

        return content.ToArray();
    }

    static byte[] PackageWithManifest(
        byte[] assembly,
        string assemblyPath,
        string manifest)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(
            content,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using (Stream entry = archive
                .CreateEntry(assemblyPath, CompressionLevel.NoCompression)
                .Open())
            {
                entry.Write(assembly);
            }

            using Stream nuspec = archive
                .CreateEntry(
                    "Browser.Dependency.Root.nuspec",
                    CompressionLevel.NoCompression)
                .Open();
            using var writer = new StreamWriter(
                nuspec,
                System.Text.Encoding.UTF8,
                leaveOpen: true);
            writer.Write(manifest);
        }

        return content.ToArray();
    }

    static byte[] PackageRole(
        byte[] assembly,
        string assemblyName,
        int assemblyCount,
        int expandedAssemblyBytes)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
        {
            byte[] expanded = new byte[expandedAssemblyBytes];
            assembly.CopyTo(expanded, 0);
            for (int index = 0; index < assemblyCount; index++)
            {
                using Stream entry = archive
                    .CreateEntry(
                        $"lib/net11.0/{assemblyName}.{index}.dll",
                        CompressionLevel.SmallestSize)
                    .Open();
                entry.Write(expanded);
            }
        }

        return content.ToArray();
    }

    static byte[] PackageEntries(int entryCount)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int index = 0; index < entryCount; index++)
                archive.CreateEntry($"content/{index:D5}.txt", CompressionLevel.NoCompression);
        }

        return content.ToArray();
    }

    static byte[] PackageDocuments(int entryCount)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int index = 0; index < entryCount; index++)
            {
                archive.CreateEntry(
                    $"skills/skill-{index:D5}.md",
                    CompressionLevel.NoCompression);
            }
        }

        return content.ToArray();
    }

    static byte[] PackageWithSkill(string packageId, string version)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(
            content,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using (StreamWriter manifest = new(
                archive.CreateEntry(
                    $"{packageId}.nuspec",
                    CompressionLevel.NoCompression).Open(),
                Encoding.UTF8,
                leaveOpen: false))
            {
                manifest.Write(Nuspec(packageId, version));
            }
            archive.CreateEntry(
                "skills/SKILL.md",
                CompressionLevel.NoCompression);
        }

        return content.ToArray();
    }

    static string Nuspec(string packageId, string version) =>
        $"""
         <package>
           <metadata>
             <id>{packageId}</id>
             <version>{version}</version>
           </metadata>
         </package>
         """;

    static IPackageSourceClient Gallery(HttpMessageHandler handler) =>
        PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(),
            handler,
            new NuGetFetchOptions
            {
                RequestTimeout = TimeSpan.FromMinutes(1),
                OperationTimeout = TimeSpan.FromMinutes(1),
            });

    sealed class PlatformVersionHandler(
        string packageId,
        string version,
        byte[]? nupkg = null) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            string url = request.RequestUri!.AbsoluteUri;
            string package = packageId.ToLowerInvariant();
            if (url.Equals(
                    $"https://api.nuget.org/v3-flatcontainer/{package}/index.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Json($$"""{"versions":["{{version}}"]}""");
            }

            if (url.Equals(
                    $"https://api.nuget.org/v3/registration5-gz-semver2/{package}/index.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Json(
                    "{\"items\":[{\"items\":[{\"catalogEntry\":{\"version\":\""
                    + version
                    + "\",\"listed\":true}}]}]}");
            }

            if (nupkg is not null
                && url.Equals(
                    $"https://api.nuget.org/v3-flatcontainer/{package}/{version}/{package}.{version}.nupkg",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(
                    new HttpResponseMessage(
                        System.Net.HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(nupkg),
                    });
            }

            return Task.FromResult(
                new HttpResponseMessage(
                    System.Net.HttpStatusCode.NotFound));
        }

        static Task<HttpResponseMessage> Json(string json) =>
            Task.FromResult(
                new HttpResponseMessage(
                    System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(json),
                });
    }

    sealed class MultiplePlatformVersionHandler(
        string version,
        IReadOnlyDictionary<string, byte[]> packages) : HttpMessageHandler
    {
        public Action<string>? BeforeDownload { get; set; }
        public Func<string, Task>? BeforeDownloadAsync { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string url = request.RequestUri!.AbsoluteUri;
            foreach ((string packageId, byte[] nupkg) in packages)
            {
                string package = packageId.ToLowerInvariant();
                if (url.Equals(
                        $"https://api.nuget.org/v3-flatcontainer/{package}/index.json",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return await Json(
                        $$"""{"versions":["{{version}}"]}""").ConfigureAwait(false);
                }

                if (url.Equals(
                        $"https://api.nuget.org/v3/registration5-gz-semver2/{package}/index.json",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return await Json(
                        "{\"items\":[{\"items\":[{\"catalogEntry\":{\"version\":\""
                        + version
                        + "\",\"listed\":true}}]}]}")
                        .ConfigureAwait(false);
                }

                if (url.Equals(
                        $"https://api.nuget.org/v3-flatcontainer/{package}/{version}/{package}.{version}.nupkg",
                        StringComparison.OrdinalIgnoreCase))
                {
                    BeforeDownload?.Invoke(packageId);
                    if (BeforeDownloadAsync is { } beforeDownload)
                        await beforeDownload(packageId).ConfigureAwait(false);
                    return new HttpResponseMessage(
                        System.Net.HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(nupkg),
                    };
                }
            }

            return new HttpResponseMessage(
                System.Net.HttpStatusCode.NotFound);
        }

        static Task<HttpResponseMessage> Json(string json) =>
            Task.FromResult(
                new HttpResponseMessage(
                    System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(json),
                });
    }

    sealed class StallingPackageHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public TaskCompletionSource RequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            RequestStarted.TrySetResult();
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            throw new InvalidOperationException(
                "The stalling handler completed without cancellation.");
        }
    }

    sealed class GalleryPackageHandler(
        string packageId,
        string version,
        byte[] archive,
        bool provideSearchResult = false,
        System.Net.HttpStatusCode packageStatus =
            System.Net.HttpStatusCode.OK,
        bool omitContentLength = false)
        : HttpMessageHandler
    {
        readonly string _packageUrl =
            $"https://globalcdn.nuget.org/packages/{packageId.ToLowerInvariant()}.{version}.nupkg";

        public List<string> Requested { get; } = [];
        public bool PayloadDisposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string url = request.RequestUri!.AbsoluteUri;
            Requested.Add(url);
            if (provideSearchResult
                && url.StartsWith(
                    "https://azuresearch-usnc.nuget.org/query?",
                    StringComparison.Ordinal))
            {
                return Task.FromResult(
                    new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            $$"""{"data":[{"id":"{{packageId}}","version":"{{version}}"}]}"""),
                    });
            }

            if (!url.Equals(_packageUrl, StringComparison.Ordinal))
            {
                return Task.FromResult(
                    new HttpResponseMessage(
                        System.Net.HttpStatusCode.NotFound));
            }

            var response = new HttpResponseMessage(packageStatus);
            if (packageStatus == System.Net.HttpStatusCode.OK)
            {
                response.Content = omitContentLength
                    ? new StreamContent(
                        new TrackingPayloadStream(
                            archive,
                            () => PayloadDisposed = true))
                    : new ByteArrayContent(archive);
            }

            return Task.FromResult(response);
        }
    }

    sealed class GalleryVersionHandler : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string url = request.RequestUri!.AbsoluteUri;
            Requested.Add(url);
            string? json = url switch
            {
                "https://globalcdn.nuget.org/v3-flatcontainer/contoso/index.json" =>
                    """{"versions":["1.0.0","1.1.0","1.2.0"]}""",
                "https://globalcdn.nuget.org/v3/registration5-gz-semver2/contoso/index.json" =>
                    """
                    {
                      "items": [
                        {
                          "items": [
                            {
                              "catalogEntry": {
                                "version": "1.0.0",
                                "listed": false
                              }
                            },
                            {
                              "catalogEntry": {
                                "version": "1.1.0"
                              }
                            },
                            {
                              "catalogEntry": {
                                "version": "1.2.0"
                              }
                            }
                          ]
                        }
                      ]
                    }
                    """,
                _ => null,
            };
            return Task.FromResult(
                new HttpResponseMessage(
                    json is null
                        ? System.Net.HttpStatusCode.NotFound
                        : System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(json ?? ""),
                });
        }
    }

    /// <summary>
    /// A registry-owned scope whose disposal suspends until the test releases it, so a competing
    /// registry operation observes the interval in which the scope has been withdrawn but its
    /// retained bytes have not been released.
    /// </summary>
    sealed class ScopeAdmissionGate : IAsyncDisposable
    {
        readonly List<BrowserScopeLease<GatedScope>> _held = [];
        readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Retirement { get; private set; } = Task.CompletedTask;

        internal static async Task<ScopeAdmissionGate> CreateAsync()
        {
            var gate = new ScopeAdmissionGate();
            var settled = new TaskCompletionSource();
            settled.SetResult();
            try
            {
                for (int index = 0; index < BrowserPackageWorkspace.MaxOpenScopes - 1; index++)
                {
                    await using ScopeReservation reservation =
                        await BrowserPackageWorkspace.ReserveScopeAsync(
                            TestContext.Current.CancellationToken);
                    gate._held.Add(await BrowserPackageWorkspace.RegisterScopeAsync(
                        reservation,
                        $"admission-holder-{index}-{Guid.NewGuid():N}",
                        new GatedScope(settled),
                        ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal)));
                }

                var closing = new GatedScope(gate._release);
                await BrowserPackageWorkspace.RegisterScopeAsync(
                    $"admission-closing-{Guid.NewGuid():N}",
                    closing);
                gate.Retirement = BrowserPackageWorkspace.RemoveScopeAsync(closing).AsTask();
                await closing.DisposeStarted.Task;
                return gate;
            }
            catch
            {
                await gate.DisposeAsync();
                throw;
            }
        }

        internal void Release() => _release.TrySetResult();

        public async ValueTask DisposeAsync()
        {
            Release();
            await Retirement;
            foreach (BrowserScopeLease<GatedScope> lease in _held)
                await lease.DisposeAsync();
        }
    }

    sealed class GatedScope(TaskCompletionSource release) : IAsyncDisposable
    {
        internal TaskCompletionSource DisposeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool Disposed { get; private set; }

        public async ValueTask DisposeAsync()
        {
            DisposeStarted.TrySetResult();
            await release.Task;
            Disposed = true;
        }
    }

    sealed class FailingScope : IAsyncDisposable
    {
        internal int DisposalCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposalCount++;
            return ValueTask.FromException(
                new InvalidOperationException(
                    "The gated browser scope failed to close."));
        }
    }

    sealed class StallingGalleryRegistrationHandler : HttpMessageHandler
    {
        public int FlatContainerRequests { get; private set; }
        public int RegistrationRequests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri.Contains(
                    "v3-flatcontainer",
                    StringComparison.Ordinal))
            {
                FlatContainerRequests++;
                return new HttpResponseMessage(
                    System.Net.HttpStatusCode.OK)
                {
                    Content =
                        new StringContent("""{"versions":["1.0.0"]}"""),
                };
            }

            RegistrationRequests++;
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            throw new InvalidOperationException(
                "The registration stall completed without cancellation.");
        }
    }

    sealed class TrackingPayloadStream(byte[] bytes, Action onDispose)
        : MemoryStream(bytes, writable: false)
    {
        public override bool CanSeek => false;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                onDispose();
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            onDispose();
            return base.DisposeAsync();
        }
    }

    sealed class RecordingTransferPolicy : IPackagePayloadTransferPolicy
    {
        internal RecordingReservation Reservation { get; } = new();

        public ValueTask<IPackagePayloadReservation> ReserveAsync(
            PackagePayloadTransfer transfer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IPackagePayloadReservation>(Reservation);
    }

    sealed class RecordingReservation : IPackagePayloadReservation
    {
        internal bool Completed { get; private set; }

        public void Complete() => Completed = true;

        public void Dispose()
        {
        }
    }

    sealed class ThrowingResource(string message) : IDisposable
    {
        public void Dispose() =>
            throw new InvalidOperationException(message);
    }

    sealed class RequestRecordingHandler : HttpMessageHandler
    {
        internal Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(
                new HttpResponseMessage(
                    System.Net.HttpStatusCode.NotFound));
        }
    }

    sealed class RejectingBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                AssemblyBindingFailureKind.CandidateUnavailable));
        }
    }

}
