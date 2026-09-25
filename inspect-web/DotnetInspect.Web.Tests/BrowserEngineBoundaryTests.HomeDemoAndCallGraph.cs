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
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Decompiler;
using InertText;
using Inspector.Findings;
using ILInspector.Metadata;
using NuGetFetch;

using DotnetInspect.Web.Interop.Package;
using BrowserProductWorkspacePlans =
    DotnetInspect.Web.Interop.Catalog.BrowserProductWorkspacePlans;
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
                WorkspacePlan.Empty,
                new WorkspaceContextInput(),
                [
                    new BrowserHomeDemoRunRequest.Package(
                        new BrowserPackageRequest(
                            peerPackageId,
                            "1.0.0",
                            "net11.0")),
                    new BrowserHomeDemoRunRequest.Package(
                        new BrowserPackageRequest(
                            packageId,
                            "1.0.0",
                            "net11.0")),
                ],
                FocusRequestIndex: 1,
                typeof(BrowserEngineBoundaryTests).FullName!,
                ProductDemoSections.Methods,
                Member: null);
            var resolution = new BrowserScopeResolution(
                scopeLease,
                [peerCoordinate, coordinate]);

            BrowserHomeDemoRunResult result =
                DotnetInspect.Web.Interop.Catalog.CatalogExports.RunHomeDemoCore(plan, resolution);

            Assert.True(result.Found);
            Assert.Equal(2, result.Packages.Length);
            BrowserHomeDemoRunActivation activation =
                Assert.IsType<BrowserHomeDemoRunActivation>(result.Activation);
            Assert.Equal("package", activation.FocusKind);
            Assert.Equal(packageId, activation.FocusId);
            Assert.Equal("1.0.0", activation.FocusVersion);
            Assert.Equal("net11.0", activation.FocusFramework);
            Assert.Null(activation.FocusAssembly);
            Assert.Equal(
                typeof(BrowserEngineBoundaryTests).FullName,
                activation.TypeId);
            Assert.Equal(ProductDemoSections.Methods, activation.Section);
            Assert.Null(activation.MemberName);
            Assert.Null(activation.MemberSection);
            Assert.Null(result.CallGraph);
            DotnetInspect.Web.Interop.Catalog.BrowserTypeSurface type = Assert.Single(
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
                WorkspacePlan.Empty,
                new WorkspaceContextInput(),
                [
                    new BrowserHomeDemoRunRequest.Package(
                        new BrowserPackageRequest(
                            peerPackageId,
                            "1.0.0",
                            "net11.0")),
                    new BrowserHomeDemoRunRequest.Package(
                        new BrowserPackageRequest(
                            packageId,
                            "1.0.0",
                            "net11.0")),
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
                DotnetInspect.Web.Interop.Catalog.CatalogExports.RunHomeDemoCore(plan, resolution);

            Assert.True(result.Found);
            Assert.Equal(2, result.Packages.Length);
            BrowserHomeDemoRunActivation activation =
                Assert.IsType<BrowserHomeDemoRunActivation>(result.Activation);
            Assert.Equal("package", activation.FocusKind);
            Assert.Equal(packageId, activation.FocusId);
            Assert.Equal("1.0.0", activation.FocusVersion);
            Assert.Equal("net11.0", activation.FocusFramework);
            Assert.Null(activation.FocusAssembly);
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

    [Fact]
    public async Task PlatformHomeDemoRunCore_ProjectsMethodsWithSourceNativeActivation()
    {
        const string packageId =
            "microsoft.netcore.app.runtime.linux-x64";
        const string version = "11.0.304";
        const string framework = "net11.0-platform-home-demo-methods";
        byte[] nupkg = PlatformPackage(
            ("PhysicalPayload.dll",
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
        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssembliesAsync(
                framework,
                version,
                [
                    new(
                        "System.Private.CoreLib.dll",
                        "netcore.app"),
                    new(
                        "dotnetinspect.web.tests.dll",
                        "netcore.app"),
                ],
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        var plan = new BrowserHomeDemoRunPlan(
            WorkspacePlan.Empty,
            new WorkspaceContextInput(),
            [
                new BrowserHomeDemoRunRequest.Platform(
                    "runtime",
                    "System.Private.CoreLib",
                    $"{version}.0",
                    "net11.0-authored"),
                new BrowserHomeDemoRunRequest.Platform(
                    "runtime",
                    "dotnetinspect.web.tests",
                    $"{version}.0",
                    "net11.0-authored"),
            ],
            FocusRequestIndex: 1,
            typeof(BrowserEngineBoundaryTests).FullName!,
            ProductDemoSections.Methods,
            Member: null);

        BrowserHomeDemoRunResult result =
            DotnetInspect.Web.Interop.Catalog.CatalogExports.PreparePlatformHomeDemo(
                plan,
                resolution).Result;

        Assert.True(result.Found);
        Assert.Equal(2, result.Packages.Length);
        var surface = Assert.Single(
            result.Packages,
            candidate => candidate.Types.Any(type =>
                type.DefinitionId
                    == typeof(BrowserEngineBoundaryTests).FullName));
        Assert.Equal(BrowserPlatformIdentity.PackageName, surface.Package);
        Assert.Equal(version, surface.Version);
        BrowserHomeDemoRunActivation activation =
            Assert.IsType<BrowserHomeDemoRunActivation>(result.Activation);
        Assert.Equal("platform", activation.FocusKind);
        Assert.Equal("runtime", activation.FocusId);
        Assert.Equal(version, activation.FocusVersion);
        Assert.Equal(framework, activation.FocusFramework);
        Assert.Equal(
            "DotnetInspect.Web.Tests",
            activation.FocusAssembly);
        Assert.Contains(
            result.Packages,
            surface => surface.DefaultAssemblyId
                == activation.FocusAssembly);
        Assert.Equal(
            "PhysicalPayload.dll",
            Assert.Single(
                surface.Assemblies,
                assembly => assembly.Name
                    == activation.FocusAssembly).Asset);
        Assert.Equal(
            $"DotnetInspect.Web.Tests:{typeof(BrowserEngineBoundaryTests).FullName}",
            activation.TypeId);
        Assert.Null(result.CallGraph);
    }

    [Fact]
    public async Task PlatformHomeDemoRunCore_PreservesContextAcrossEquivalentVersionSpellings()
    {
        const string packageId =
            "microsoft.netcore.app.runtime.linux-x64";
        const string packageVersion = "11.0.105";
        const string requestedVersion = "11.0.105.0";
        const string framework = "net11.0-platform-home-demo-graph";
        byte[] nupkg = PlatformPackage(
            ("DotnetInspect.Web.Tests.dll",
                File.ReadAllBytes(
                    typeof(BrowserEngineBoundaryTests).Assembly.Location)),
            ("System.Private.CoreLib.dll",
                File.ReadAllBytes(typeof(object).Assembly.Location)));
        var handler = new PlatformVersionHandler(
            packageId,
            packageVersion,
            nupkg);
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);
        BrowserPlatformScope retainedOrdinaryScope;
        await using (BrowserPlatformScopeResolution ordinary =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                framework,
                requestedVersion,
                "System.Private.CoreLib.dll",
                "netcore.app",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken))
        {
            retainedOrdinaryScope = ordinary.Scope;
        }
        await using BrowserScopeLease<BrowserPlatformScope> ordinaryPin =
            BrowserPackageWorkspace.LeaseScope(retainedOrdinaryScope);
        var workspacePlan = new WorkspacePlan(
            [],
            [
                new WorkspaceContextInput
                {
                    Framework = framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            "System.Private.CoreLib",
                            requestedVersion,
                            framework),
                        WorkspaceMemberCoordinate.Platform(
                            "runtime",
                            "DotnetInspect.Web.Tests",
                            requestedVersion,
                            framework),
                    ],
                },
            ]);
        BrowserMemberSurfaceInfo member;
        BrowserHomeDemoRunResult result;
        await using (BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenContextAsync(
                workspacePlan,
                workspacePlan.Contexts[0],
                "runtime",
                "DotnetInspect.Web.Tests",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken))
        {
            BrowserPlatformProjectionInfo projection =
                BrowserPlatformSurfaceProjection.Project(
                    resolution.Scope,
                    resolution.Scope.Participant(
                        "runtime",
                        "DotnetInspect.Web.Tests"),
                    resolution.Scope.Coordinates.Single(coordinate =>
                        coordinate.Assembly == "DotnetInspect.Web.Tests"));
            BrowserTypeSurfaceInfo type = Assert.Single(
                projection.Surface.Types,
                candidate => candidate.DefinitionId
                    == typeof(BrowserEngineBoundaryTests).FullName);
            member = Assert.Single(
                type.Api,
                candidate => candidate.Name == nameof(HomeDemoRunLocalFixture));
            var plan = new BrowserHomeDemoRunPlan(
                workspacePlan,
                workspacePlan.Contexts[0],
                [
                    new BrowserHomeDemoRunRequest.Platform(
                        "runtime",
                        "System.Private.CoreLib",
                        requestedVersion,
                        framework),
                    new BrowserHomeDemoRunRequest.Platform(
                        "runtime",
                        "DotnetInspect.Web.Tests",
                        requestedVersion,
                        framework),
                ],
                FocusRequestIndex: 1,
                type.DefinitionId,
                ProductDemoSections.CallGraph,
                new BrowserHomeDemoRunMember(
                    member.Name,
                    member.Kind,
                    member.AnchorDigest,
                    MemberSection: "call-graph"));
            var preparation =
                DotnetInspect.Web.Interop.Catalog.CatalogExports
                    .PreparePlatformHomeDemo(
                        plan,
                        resolution);
            result =
                await DotnetInspect.Web.Interop.Catalog.CatalogExports
                    .CompletePlatformHomeDemoAsync(
                        preparation,
                        client,
                        authorization,
                        TimeSpan.FromSeconds(5),
                        TestContext.Current.CancellationToken,
                        resolution);
        }

        Assert.True(result.Found);
        BrowserHomeDemoRunActivation activation =
            Assert.IsType<BrowserHomeDemoRunActivation>(result.Activation);
        Assert.Equal("platform", activation.FocusKind);
        Assert.Equal("runtime", activation.FocusId);
        Assert.Equal(packageVersion, activation.FocusVersion);
        Assert.Equal(
            "DotnetInspect.Web.Tests",
            activation.FocusAssembly);
        Assert.Equal(member.AnchorDigest, activation.MemberAnchorDigest);
        var graph = Assert.IsType<
            DotnetInspect.Web.Interop.Catalog.BrowserCallGraph>(
                result.CallGraph);
        Assert.Equal(0, graph.Scope.Packages);
        Assert.Equal(2, graph.Scope.Assemblies);
        Assert.Contains(
            nameof(HomeDemoRunLocalFixture),
            graph.Mermaid,
            StringComparison.Ordinal);
        await using BrowserPlatformScopeResolution retainedOrdinary =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                framework,
                requestedVersion,
                "System.Private.CoreLib.dll",
                "netcore.app",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        Assert.Equal(
            "System.Private.CoreLib",
            retainedOrdinary.Participant.Participant.Assembly.Identity.Name);
    }

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
    public void ProductWorkspacePlan_ConfiguresSupplyChainBaseline()
    {
        BrowserProductWorkspacePlans.ConfigurePlatform();

        string[] ecosystems =
        [
            .. BrowserPackageWorkspace.ProductWorkspacePlan.Registrations
                .OfType<WorkspaceRegistration.Ecosystem>()
                .Select(registration =>
                    registration.Declaration.Id.Value),
        ];

        Assert.Contains("ecosystem.runtime", ecosystems);
        Assert.Contains("ecosystem.aspnetcore", ecosystems);
        Assert.Contains("ecosystem.microsoft-extensions", ecosystems);
    }

    [Fact]
    public void DependencyCallGraphDocument_ProjectsDetachedBrowserGraph()
    {
        var connectorIdentity = new AssemblyReferenceIdentity(
            "Microsoft.Extensions.Options",
            new Version(1, 2, 3, 4),
            Culture: null,
            PublicKeyToken: null);
        var boundaryIdentity = new AssemblyReferenceIdentity(
            "OpenTelemetry.Api",
            new Version(5, 6, 7, 8),
            Culture: null,
            PublicKeyToken: null);
        TypeRef type = TypeRef.Definition(
            "OpenTelemetry",
            "Example",
            "Worker");
        TypeRef returnType = TypeRef.CoreLib("System", "Void");
        var focus = new MemberRef(
            type,
            "Run",
            [],
            returnType,
            MemberKind.Method);
        var connector = new MemberRef(
            TypeRef.Definition(
                "Microsoft.Extensions.Options",
                "Microsoft.Extensions.DependencyInjection",
                "OptionsServiceCollectionExtensions",
                new ResolvableTypeReference(
                    new TypeReferenceOrigin.CurrentAssembly(
                        connectorIdentity),
                    DefinitionName(
                        "Microsoft.Extensions.DependencyInjection",
                        ["OptionsServiceCollectionExtensions"]))),
            "AddOptions",
            [],
            returnType,
            MemberKind.Method);
        var boundary = new MemberRef(
            TypeRef.Definition(
                "OpenTelemetry.Api",
                "OpenTelemetry.Context",
                "RuntimeContextSlot`1",
                new ResolvableTypeReference(
                    new TypeReferenceOrigin.CurrentAssembly(
                        boundaryIdentity),
                    DefinitionName(
                        "OpenTelemetry.Context",
                        ["RuntimeContextSlot`1"]))),
            "Get",
            [],
            returnType,
            MemberKind.Method);
        var disconnectedConnector = new MemberRef(
            TypeRef.Definition(
                "Microsoft.Extensions.Options",
                "Microsoft.Extensions.Options",
                "OptionsMonitor",
                new ResolvableTypeReference(
                    new TypeReferenceOrigin.CurrentAssembly(
                        connectorIdentity),
                    DefinitionName(
                        "Microsoft.Extensions.Options",
                        ["OptionsMonitor"]))),
            "Read",
            [],
            returnType,
            MemberKind.Method);
        var unclassifiedBoundary = new MemberRef(
            TypeRef.CoreLib("System", "Console"),
            "WriteLine",
            [],
            returnType,
            MemberKind.Method);
        var unclassifiedUnknown = new MemberRef(
            TypeRef.Definition("", "Example", "Unknown"),
            "Invoke",
            [],
            returnType,
            MemberKind.Method);
        var tree = new CallTreeNode(
            focus,
            null,
            CallTreeStatus.Expanded,
            [
                new CallTreeNode(
                    connector,
                    null,
                    CallTreeStatus.Expanded,
                    [
                        new CallTreeNode(
                            boundary,
                            null,
                            CallTreeStatus.External,
                            []),
                    ]),
            ]);
        InspectionGraphDocument graph =
            CallGraphInspectionGraphAdapter.Create(
                CallGraphProjection.FromCallees(tree));
        int disconnectedConnectorNodeId = graph.Nodes.Length;
        int unclassifiedBoundaryNodeId =
            disconnectedConnectorNodeId + 1;
        int unclassifiedUnknownNodeId =
            disconnectedConnectorNodeId + 2;
        int unclassifiedBoundaryEdgeId = graph.Edges.Length;
        int unclassifiedUnknownEdgeId = graph.Edges.Length + 1;
        int unclassifiedBoundaryOccurrenceId =
            graph.Occurrences.Length;
        int unclassifiedUnknownOccurrenceId =
            graph.Occurrences.Length + 1;
        InspectionGraphSubject disconnectedConnectorSubject =
            InspectionGraphSubject.ForMember(
                GraphNodeIdentity.FromMember(
                    disconnectedConnector),
                disconnectedConnector);
        InspectionGraphSubject unclassifiedBoundarySubject =
            InspectionGraphSubject.ForMember(
                GraphNodeIdentity.FromMember(
                    unclassifiedBoundary),
                unclassifiedBoundary);
        InspectionGraphSubject unclassifiedUnknownSubject =
            InspectionGraphSubject.ForMember(
                GraphNodeIdentity.FromMember(
                    unclassifiedUnknown),
                unclassifiedUnknown);
        graph = new InspectionGraphDocument(
            graph.Scope,
            graph.ModeRequest,
            [
                .. graph.Nodes,
                new InspectionGraphNode(
                    disconnectedConnectorNodeId,
                    disconnectedConnectorSubject,
                    InspectionGraphNodeRole.Ordinary,
                    []),
                new InspectionGraphNode(
                    unclassifiedBoundaryNodeId,
                    unclassifiedBoundarySubject,
                    InspectionGraphNodeRole.External,
                    []),
                new InspectionGraphNode(
                    unclassifiedUnknownNodeId,
                    unclassifiedUnknownSubject,
                    InspectionGraphNodeRole.External,
                    []),
            ],
            graph.Groups,
            [
                .. graph.Edges,
                new InspectionGraphEdge(
                    unclassifiedBoundaryEdgeId,
                    disconnectedConnectorNodeId,
                    unclassifiedBoundaryNodeId,
                    graph.Edges[0].Relationship,
                    [unclassifiedBoundaryOccurrenceId]),
                new InspectionGraphEdge(
                    unclassifiedUnknownEdgeId,
                    disconnectedConnectorNodeId,
                    unclassifiedUnknownNodeId,
                    graph.Edges[0].Relationship,
                    [unclassifiedUnknownOccurrenceId]),
            ],
            [
                .. graph.Occurrences,
                new InspectionGraphOccurrence(
                    unclassifiedBoundaryOccurrenceId,
                    graph.Edges[0].Relationship,
                    disconnectedConnectorSubject,
                    unclassifiedBoundarySubject,
                    new CallGraphLogicalEdgeEvidence(
                        unclassifiedBoundaryEdgeId),
                    []),
                new InspectionGraphOccurrence(
                    unclassifiedUnknownOccurrenceId,
                    graph.Edges[0].Relationship,
                    disconnectedConnectorSubject,
                    unclassifiedUnknownSubject,
                    new CallGraphLogicalEdgeEvidence(
                        unclassifiedUnknownEdgeId),
                    []),
            ],
            graph.Characteristics,
            graph.Seeds,
            [
                .. graph.Limits,
                new InspectionGraphLimit(
                    CallGraphInspectionGraphCatalog
                        .TraversalNodeBound,
                    InspectionGraphTarget.Node(0),
                    new CallGraphTraversalNodeBoundEvidence(50)),
                new InspectionGraphLimit(
                    InspectionGraphNeighborhoodCatalog.DepthBound,
                    InspectionGraphTarget.Node(0),
                    new InspectionGraphNeighborhoodDepthBoundEvidence(3)),
                new InspectionGraphLimit(
                    CallGraphInspectionGraphCatalog
                        .CorrespondenceIncomplete,
                    InspectionGraphTarget.Node(0),
                    new CallGraphCorrespondenceIncompleteEvidence(
                        incompleteNodeCount: 2,
                        incompleteEdgeCount: 3,
                        bindingIdentityConflictCount: 4)),
                new InspectionGraphLimit(
                    InspectionGraphFocusCatalog
                        .ScopeClassificationIncomplete,
                    InspectionGraphTarget.Edge(
                        unclassifiedBoundaryEdgeId)),
                new InspectionGraphLimit(
                    InspectionGraphFocusCatalog
                        .ScopeClassificationIncomplete,
                    InspectionGraphTarget.Edge(
                        unclassifiedUnknownEdgeId)),
            ],
            graph.Failures);
        InspectionGraphCharacteristic[] supplyChainRoles =
        [
            .. graph.Edges.Select(edge =>
            {
                var target =
                    Assert.IsType<
                        InspectionGraphSubject.MemberSubject>(
                            graph.Nodes[edge.ToNodeId].Subject);
                string role =
                    ((InspectionGraphMemberIdentity.CallGraph)
                        target.Identity).Member.Name switch
                    {
                        "AddOptions" =>
                            InspectionGraphFocusCatalog
                                .ConnectorRole,
                        "Get" =>
                            InspectionGraphFocusCatalog.ExitRole,
                        "WriteLine" =>
                            InspectionGraphFocusCatalog
                                .UnclassifiedBoundaryRole,
                        "Invoke" =>
                            InspectionGraphFocusCatalog
                                .UnclassifiedBoundaryRole,
                        _ => throw new InvalidOperationException(
                            "Unexpected call-graph member."),
                    };
                InspectionGraphTarget edgeTarget =
                    InspectionGraphTarget.Edge(edge.Id);
                return new InspectionGraphCharacteristic(
                    InspectionGraphFocusCatalog.Role,
                    edgeTarget,
                    new InspectionGraphValue.TokenSet([role]),
                    new InspectionGraphCharacteristicDerivation(
                        InspectionGraphCharacteristicDerivationKind
                            .Derived,
                        [edgeTarget]));
            }),
        ];
        graph = new InspectionGraphDocument(
            graph.Scope,
            graph.ModeRequest,
            graph.Nodes,
            graph.Groups,
            graph.Edges,
            graph.Occurrences,
            [
                .. graph.Characteristics,
                .. supplyChainRoles,
            ],
            graph.Seeds,
            graph.Limits,
            graph.Failures);
        int connectorNodeId = Assert.Single(
            graph.Nodes,
            node =>
                node.Subject
                    is InspectionGraphSubject.MemberSubject
                    {
                        Identity:
                            InspectionGraphMemberIdentity.CallGraph
                            {
                                Member.Name: "AddOptions",
                            },
                    }).Id;
        int boundaryNodeId = Assert.Single(
            graph.Nodes,
            node =>
                node.Subject
                    is InspectionGraphSubject.MemberSubject
                    {
                        Identity:
                            InspectionGraphMemberIdentity.CallGraph
                            {
                                Member.Name: "Get",
                            },
                    }).Id;
        var document =
            new PackageDependencyMemberCallGraphDocument(
                TraversalTargetFrameworkPolicy.ProductDefault,
                new PackageDependencyTraversalSummary(
                    CompleteRoots: 1,
                    DepthBoundedRoots: 0,
                    SourceBoundedRoots: 0,
                    PartialRoots: 0),
                [],
                new PackageSupplyChainBaselineEvidence(
                    PackageSupplyChainBaseline
                        .SelfAndRegisteredEcosystems,
                    ["microsoft.extensions.http.polly"],
                    [],
                    [
                        "ecosystem.runtime",
                        "ecosystem.aspnetcore",
                        "ecosystem.microsoft-extensions",
                    ]),
                [
                    new PackageDependencyMemberCallGraphPackageSubject(
                        connectorNodeId,
                        "Microsoft.Extensions.Options",
                        "11.0.0",
                        "net8.0"),
                    new PackageDependencyMemberCallGraphPackageSubject(
                        boundaryNodeId,
                        PackageId: "OpenTelemetry.Api",
                        PackageVersion: "1.2.3",
                        TargetFramework: "net8.0"),
                    new PackageDependencyMemberCallGraphPackageSubject(
                        disconnectedConnectorNodeId,
                        "Microsoft.Extensions.Options",
                        "11.0.0",
                        "net8.0"),
                ],
                graph);

        BrowserCallGraphInfo projected =
            BrowserCallGraphProjection.Project(document);

        Assert.Equal("Run", projected.Callees.MemberName);
        BrowserCallGraphNodeInfo connectorNode =
            Assert.Single(projected.Callees.Children);
        Assert.Equal("AddOptions", connectorNode.MemberName);
        Assert.Equal(
            "Get",
            Assert.Single(connectorNode.Children).MemberName);
        BrowserCallGraphTargetInfo connectorTarget = Assert.Single(
            projected.Targets,
            target =>
                target.Assembly == "Microsoft.Extensions.Options"
                && target.MemberName == "AddOptions");
        Assert.Equal("connector", connectorTarget.Kind);
        BrowserCallGraphTargetInfo boundaryTarget = Assert.Single(
            projected.Targets,
            target =>
                target.Assembly == "OpenTelemetry.Api"
                && target.MemberName == "Get");
        Assert.Equal("boundary", boundaryTarget.Kind);
        Assert.Equal("OpenTelemetry.Api", boundaryTarget.PackageId);
        Assert.Equal("1.2.3", boundaryTarget.PackageVersion);
        Assert.Equal("net8.0", boundaryTarget.PackageFramework);
        Assert.Equal("5.6.7.8", boundaryTarget.AssemblyVersion);
        Assert.Equal(2, projected.Diagnostics.IncompleteNodes);
        Assert.Equal(3, projected.Diagnostics.IncompleteEdges);
        Assert.Equal(4, projected.Diagnostics.BindingIdentityConflicts);
        Assert.True(projected.Diagnostics.HasIncompleteCorrespondence);
        Assert.True(
            projected.Diagnostics.HasUnexploredTraversalBoundary);
        Assert.Equal(
            2,
            projected.Diagnostics.UnclassifiedBoundaryEdges);
        Assert.Equal(
            1,
            projected.Diagnostics.UnclassifiedBoundaryNamedEdges);
        Assert.Equal(
            ["corelib"],
            projected.Diagnostics.UnclassifiedBoundaryAssemblies);
        BrowserCallGraphTargetInfo disconnectedConnectorTarget =
            Assert.Single(
                projected.Targets,
                target =>
                    target.Assembly == "Microsoft.Extensions.Options"
                    && target.MemberName == "Read");
        Assert.Equal(
            "connector",
            disconnectedConnectorTarget.Kind);
        BrowserCallGraphTargetInfo unclassifiedBoundaryTarget =
            Assert.Single(
                projected.Targets,
                target =>
                    target.TypeFullName == "System.Console"
                    && target.MemberName == "WriteLine");
        Assert.Equal(
            "unclassified-boundary",
            unclassifiedBoundaryTarget.Kind);
        Assert.Contains("Example.Worker.Run", projected.Mermaid);
        Assert.Equal("Supply Chain", projected.Scope.CalleeScope);

        BrowserCallGraph wire = BrowserCallGraphWireProjection.Project(
            projected,
            [
                new InspectionDiagnostic(
                    "package-dependency-member-call-graph.route-unavailable",
                    InspectionDiagnosticSeverity.Warning,
                    "A dependency route was unavailable."),
            ]);
        Assert.Equal(
            1,
            wire.Diagnostics.UnavailableDependencyRoutes);
        Assert.Equal(
            2,
            wire.Diagnostics.UnclassifiedBoundaryEdges);
        Assert.Equal(
            1,
            wire.Diagnostics.UnclassifiedBoundaryNamedEdges);
        Assert.Equal(
            ["corelib"],
            wire.Diagnostics.UnclassifiedBoundaryAssemblies);
        Assert.True(wire.Diagnostics.IsIncomplete);
        DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphTarget wireTarget =
            Assert.Single(
                wire.Targets,
                target => target.MemberName == "Get");
        Assert.Equal("boundary", wireTarget.Kind);
        Assert.Equal("OpenTelemetry.Api", wireTarget.PackageId);
        Assert.Equal("1.2.3", wireTarget.PackageVersion);
        Assert.Equal("net8.0", wireTarget.PackageFramework);
        Assert.Equal("5.6.7.8", wireTarget.AssemblyVersion);
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
                    DotnetInspect.Web.Interop.Package.PackageExports.ProjectPlatformSurface(
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
                    await DotnetInspect.Web.Interop.CallGraph.CallGraphExports
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
        var exactPlan = new WorkspacePlan(
            [],
            [
                new WorkspaceContextInput
                {
                    Framework = framework,
                    Members = [WorkspaceMemberCoordinate.Platform(
                        "runtime",
                        "System.Runtime",
                        version,
                        framework)],
                },
            ]);
        await using BrowserPlatformScopeResolution exact =
            await BrowserPlatformWorkspace.OpenContextAsync(
                exactPlan,
                exactPlan.Contexts[0],
                "runtime",
                "System.Runtime",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        AssemblyReferenceIdentity exactIdentity =
            exact.Participant.Participant.Assembly.Identity;
        string contextId = Assert.IsType<string>(exact.ContextId);
        BrowserCallGraph exactGraph = Assert.IsType<BrowserCallGraph>(
            JsonSerializer.Deserialize(
                await DotnetInspect.Web.Interop.CallGraph.CallGraphExports.ExpandPlatformCallGraph(
                    framework,
                    version,
                    "System.Runtime",
                    "netcore.app",
                    exactIdentity.Version!.ToString(),
                    exactIdentity.Culture,
                    exactIdentity.PublicKeyToken,
                    destination.TypeDefinitionId!,
                    destination.MemberName,
                    destination.SelectorKey,
                    0,
                    contextId),
                BrowserCallGraphJsonContext.Default.BrowserCallGraph));
        Assert.Equal(2, exactGraph.Scope.Assemblies);
        await using (BrowserPlatformScopeResolution expanded =
            await BrowserPlatformWorkspace.OpenRetainedContextAssemblyAsync(
                contextId,
                framework,
                version,
                "System.Private.CoreLib.dll",
                "netcore.app",
                TestContext.Current.CancellationToken))
        {
            Assert.Equal(contextId, expanded.ContextId);
            Assert.NotSame(exact.Scope, expanded.Scope);
            Assert.Equal(2, expanded.Scope.Members.Length);
        }

        BrowserCallGraph continuedExact = Assert.IsType<BrowserCallGraph>(
            JsonSerializer.Deserialize(
                await DotnetInspect.Web.Interop.CallGraph.CallGraphExports.ExpandPlatformCallGraph(
                    framework,
                    version,
                    destination.Assembly!,
                    destination.PlatformPack!,
                    destination.AssemblyVersion!,
                    destination.AssemblyCulture,
                    destination.AssemblyPublicKeyToken,
                    destination.TypeDefinitionId!,
                    destination.MemberName,
                    destination.SelectorKey,
                    destination.MetadataToken ?? 0,
                    contextId),
                BrowserCallGraphJsonContext.Default.BrowserCallGraph));
        Assert.Equal(2, continuedExact.Scope.Assemblies);

        BrowserPackageSurface terminalSurface =
            Assert.IsType<BrowserPackageSurface>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package.PackageExports.LoadRuntimePackAssembly(
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
                    await DotnetInspect.Web.Interop.Package.PackageExports.LoadRuntimePackAssembly(
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
                        await DotnetInspect.Web.Interop.CallGraph.CallGraphExports.ExpandPlatformCallGraph(
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
}
