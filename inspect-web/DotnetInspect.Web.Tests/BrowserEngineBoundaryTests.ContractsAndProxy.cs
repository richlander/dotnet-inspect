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
using QuerySpace;
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
                    "Example.*",
                    [
                        new PortableQueryTerm(
                            PackageQuery.ToolTermKey,
                            PortableQueryOperator.Equal,
                            "true"),
                        new PortableQueryTerm(
                            PackageQuery.DependenciesTermKey,
                            PortableQueryOperator.Equal,
                            "none"),
                    ],
                    MaximumCandidates: 20))).Plan;

        Assert.All(
            plan.BoundTerms,
            term => Assert.Equal(
                PackageQueryAcquisitionTier.Nuspec,
                term.Descriptor.Tier));
        Assert.Equal(
            [
                PackageQuery.DependenciesTermKey,
                PackageQuery.ToolTermKey,
            ],
            plan.Terms.Select(term => term.Key));
    }

    [Fact]
    public void TransitivePackageQueryPlanner_IsReachableFromBrowserConsumer()
    {
        PackageQueryPlan plan = Assert.IsType<PackageQueryPlanResult.Accepted>(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Microsoft.Extensions.Http",
                    [
                        new PortableQueryTerm(
                            PackageQuery.DependsTransitiveTermKey,
                            PortableQueryOperator.Equal,
                            "Microsoft.Extensions.Primitives"),
                        new PortableQueryTerm(
                            PackageQuery.DependencyTargetTermKey,
                            PortableQueryOperator.Equal,
                            "net10.0"),
                        new PortableQueryTerm(
                            PackageQuery.DependencyDepthTermKey,
                            PortableQueryOperator.Equal,
                            "2"),
                    ],
                    MaximumCandidates: 1))).Plan;

        Assert.True(plan.RequiresDependencyTraversal);
        Assert.Equal(2, plan.DependencyDepth);
        Assert.Contains(
            PackageQuery.Terms,
            term => term.Key == PackageQuery.DependsTransitiveTermKey
                && term.ExecutionClass
                    == PackageQueryExecutionClass.NuspecExpensive);
    }

    [Fact]
    public void DependencyStartsWithPlanner_IsReachableFromBrowserConsumer()
    {
        PackageQueryPlan plan = Assert.IsType<PackageQueryPlanResult.Accepted>(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Microsoft.Extensions.Http",
                    [
                        new PortableQueryTerm(
                            PackageQuery.DependsTermKey,
                            PortableQueryOperator.StartsWith,
                            "Microsoft.Extensions."),
                    ],
                    MaximumCandidates: 1))).Plan;

        BoundPackageQueryTerm term = Assert.Single(plan.BoundTerms);
        Assert.Equal(
            "Microsoft.Extensions.",
            term.Predicate.PackagePrefix!.Prefix);
        Assert.Equal(
            PackageQueryExecutionClass.Nuspec,
            term.Descriptor.ExecutionClass);
    }

    [Fact]
    public async Task QueryFailureAdapters_DoNotEmitArtifactAuthoredText()
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
        await using var workspace = new InspectionWorkspace();
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
            DotnetInspect.Web.Interop.Analysis.AnalysisExports.CreateIntegrations(
                "Package.Sample",
                "1.0.0",
                "net11.0",
                integrations.Assemblies).InspectionError!,
            DotnetInspect.Web.Interop.Analysis.AnalysisExports.CreateOpportunities(
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

    [Theory]
    [InlineData(
        "https://api.nuget.org/v3/index.json",
        "https://dotnet-inspect.ca/api/package-changes/nuget"
        + "?path=%2Fv3%2Findex.json")]
    [InlineData(
        "https://api.nuget.org/v3/catalog0/page20764.json",
        "https://dotnet-inspect.ca/api/package-changes/nuget"
        + "?path=%2Fv3%2Fcatalog0%2Fpage20764.json")]
    [InlineData(
        "https://api.github.com/advisories?ecosystem=nuget"
        + "&type=reviewed&is_withdrawn=false&per_page=100"
        + "&affects=Microsoft.Extensions.AI",
        "https://dotnet-inspect.ca/api/package-changes/advisories"
        + "?ecosystem=nuget&type=reviewed&is_withdrawn=false"
        + "&per_page=100&affects=Microsoft.Extensions.AI")]
    public async Task PublicEvidenceProxy_RewritesFixedProviderRequests(
        string providerRequest,
        string expectedProxyRequest)
    {
        var inner = new RequestRecordingHandler();
        using var handler =
            new BrowserPublicEvidenceProxyHandler(inner);
        handler.Configure("https://dotnet-inspect.ca");
        using var client = new HttpClient(handler);

        using HttpResponseMessage response =
            await client.GetAsync(
                providerRequest,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            expectedProxyRequest,
            inner.RequestUri?.AbsoluteUri);
        Assert.Equal(
            providerRequest,
            response.RequestMessage?.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task PublicEvidenceProxy_ForwardsNoProviderRequestHeaders()
    {
        var inner = new RequestRecordingHandler();
        using var handler =
            new BrowserPublicEvidenceProxyHandler(inner);
        handler.Configure("https://dotnet-inspect.ca");
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.github.com/advisories?ecosystem=nuget");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                "secret");
        request.Headers.TryAddWithoutValidation("X-Caller-Header", "value");

        using HttpResponseMessage response =
            await client.SendAsync(
                request,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            "https://dotnet-inspect.ca/api/package-changes/advisories"
            + "?ecosystem=nuget",
            inner.RequestUri?.AbsoluteUri);
        Assert.False(inner.HadAuthorization);
        Assert.False(inner.HadCallerHeader);
        Assert.Equal(
            "https://api.github.com/advisories?ecosystem=nuget",
            response.RequestMessage?.RequestUri?.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://api.nuget.org/v3-flatcontainer/example/index.json")]
    [InlineData("https://api.nuget.org/v3/catalog0/index.json?other=true")]
    [InlineData("https://api.github.com/repos/example/project")]
    [InlineData("https://example.com/advisories?ecosystem=nuget")]
    public async Task PublicEvidenceProxy_LeavesOtherDestinationsUnchanged(
        string destination)
    {
        var inner = new RequestRecordingHandler();
        using var handler =
            new BrowserPublicEvidenceProxyHandler(inner);
        handler.Configure("https://dotnet-inspect.ca");
        using var client = new HttpClient(handler);

        using HttpResponseMessage response =
            await client.GetAsync(
                destination,
                TestContext.Current.CancellationToken);

        Assert.Equal(destination, inner.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task PublicEvidenceProxy_RequiresConfiguredOrigin()
    {
        using var handler =
            new BrowserPublicEvidenceProxyHandler(
                new RequestRecordingHandler());
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync(
                "https://api.nuget.org/v3/index.json",
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://dotnet-inspect.ca/path")]
    [InlineData("https://user@example.com")]
    public void PublicEvidenceProxy_RejectsValuesThatAreNotHttpOrigins(
        string origin)
    {
        using var handler =
            new BrowserPublicEvidenceProxyHandler(
                new RequestRecordingHandler());
        Assert.Throws<ArgumentException>(() => handler.Configure(origin));
    }

    [Fact]
    public void SourceContexts_UseFreshMemoryOnlyPdbStores()
    {
        AssemblyContextSourceQueryContext first =
            DotnetInspect.Web.Interop.Source.SourceExports.CreateSourceContext();
        AssemblyContextSourceQueryContext second =
            DotnetInspect.Web.Interop.Source.SourceExports.CreateSourceContext();

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
    public async Task CancelledWait_WithoutEpochSettlesBeforeObservedPhysicalFailure()
    {
        var completion =
            new TaskCompletionSource<AcquiredPackageSourcePayload>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var acquisition = new BrowserSharedPackageAcquisition(() => completion.Task, epochWork: null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Task<AcquiredPackageSourcePayload> waiting = acquisition.WaitAsync(cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.False(completion.Task.IsCompleted);
        var lateFailure = new InvalidOperationException("late package failure");
        completion.SetException(lateFailure);
        Assert.Same(lateFailure,
            await Assert.ThrowsAsync<InvalidOperationException>(() => acquisition.Completion));
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
                "DotnetInspect.Web.Tests.dll",
                "aspnetcore.app",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        BrowserPackageSurface surface = Assert.IsType<BrowserPackageSurface>(
            JsonSerializer.Deserialize(
                DotnetInspect.Web.Interop.Package.PackageExports.ProjectPlatformSurface(
                    resolution,
                    "Misleading.dll"),
                BrowserPackageJsonContext.Default.BrowserPackageSurface));

        Assert.Equal(
            BrowserPlatformIdentity.PackageName,
            surface.Package);
        BrowserAssemblySurface selectedAssembly =
            Assert.Single(surface.Assemblies);
        Assert.Equal("Misleading.dll", selectedAssembly.Asset);
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
                await DotnetInspect.Web.Interop.CallGraph.CallGraphExports.ExpandPlatformCallGraph(
                    "net11.0-ios",
                    "DotnetInspect.Web.Tests",
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
                target.Assembly == "DotnetInspect.Web.Tests"),
        ];
        Assert.NotEmpty(ownTargets);
        Assert.All(
            ownTargets,
            target => Assert.Equal(
                "aspnetcore.app",
                target.PlatformPack));
    }
}
