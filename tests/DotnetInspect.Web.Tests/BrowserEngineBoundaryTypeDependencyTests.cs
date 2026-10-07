using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Versioning;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using QuerySpace.Rows;

using BrowserMetadataJsonContext =
    DotnetInspect.Web.Interop.Metadata.BrowserMetadataJsonContext;
using BrowserMetadataJsonSerialization =
    DotnetInspect.Web.Interop.Metadata.BrowserMetadataJsonSerialization;
using BrowserTypeMetadata =
    DotnetInspect.Web.Interop.Metadata.BrowserTypeMetadata;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed partial class BrowserEngineBoundaryTests
{
    [Fact]
    public async Task QueryTypeProjection_RetainsDependencySubjectWireFacts()
    {
        const string packageId = "Browser.TypeDependencies.Json";
        const string typeName = "Browser.TypeDependencies.Json.Consumer";
        _ = await Coordinate(
            packageId,
            Package(
                BuildTypeDependencyImage(packageId, typeName, typeof(IDisposable)),
                $"lib/net11.0/{packageId}.dll"));

        string json = await DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryTypeProjection(
            packageId,
            "1.0.0",
            "net11.0",
            $"{packageId}.dll",
            typeName,
            typeName,
            $$"""[{"package":"{{packageId}}","version":"1.0.0","framework":"net11.0"}]""");
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement content = document.RootElement.GetProperty("typeDependencyInspection")
            .GetProperty("content");
        JsonElement participant = Assert.Single(
            content.GetProperty("queryResult").GetProperty("participants").EnumerateArray());
        Assert.Equal("completed", participant.GetProperty("kind").GetString());
        JsonElement subject = participant.GetProperty("subject");
        Assert.False(subject.TryGetProperty("registration", out _));
        Assert.Equal(packageId, subject.GetProperty("identity").GetProperty("name").GetString());
        JsonElement provenance = subject.GetProperty("provenance");
        Assert.Equal("package", provenance.GetProperty("kind").GetString());
        Assert.Equal(packageId, provenance.GetProperty("packageId").GetString(), ignoreCase: true);
        Assert.Equal("1.0.0", provenance.GetProperty("packageVersion").GetString());
        Assert.True(provenance.TryGetProperty("tfm", out _));
        Assert.True(provenance.TryGetProperty("rid", out _));
        JsonElement hierarchy =
            document.RootElement.GetProperty("hierarchy");
        Assert.Equal(
            "Derived Types",
            hierarchy.GetProperty("form").GetString());
        Assert.Equal(0, hierarchy.GetProperty("count").GetInt32());
        Assert.Equal(
            "available",
            hierarchy.GetProperty("status").GetString());
        Assert.Empty(hierarchy.GetProperty("rows").EnumerateArray());
        Assert.True(hierarchy.GetProperty("isComplete").GetBoolean());
        Assert.False(hierarchy.GetProperty("hasMore").GetBoolean());
        Assert.True(hierarchy.TryGetProperty("share", out _));
        Assert.True(hierarchy.TryGetProperty("diagnostics", out _));
        Assert.True(hierarchy.TryGetProperty("producers", out _));
        Assert.True(hierarchy.TryGetProperty("coverage", out _));
    }

    [Fact]
    public async Task QueryTypeProjection_ExpandsDependenciesAcrossWorkspacePackages()
    {
        const string rootPackageId =
            "Browser.TypeDependencies.Workspace.Root";
        const string dependencyPackageId =
            "Browser.TypeDependencies.Workspace.Dependency";
        const string rootAssemblyName =
            "Browser.TypeDependencies.Workspace.Root";
        const string typeName =
            "Browser.TypeDependencies.Workspace.Consumer";
        Type dependency = typeof(IPackagePayloadReservation);

        _ = await Coordinate(
            rootPackageId,
            Package(
                BuildTypeDependencyImage(
                    rootAssemblyName,
                    typeName,
                    dependency),
                $"lib/net11.0/{rootAssemblyName}.dll"));
        _ = await Coordinate(
            dependencyPackageId,
            Package(
                File.ReadAllBytes(dependency.Assembly.Location),
                $"lib/net11.0/{dependency.Assembly.GetName().Name}.dll"));

        BrowserTypeMetadata rootOnly = await QueryTypeProjection(
            rootPackageId,
            $"{rootAssemblyName}.dll",
            typeName,
            $$"""
            [
              {
                "package": "{{rootPackageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              }
            ]
            """);
        BrowserTypeMetadata workspace = await QueryTypeProjection(
            rootPackageId,
            $"{rootAssemblyName}.dll",
            typeName,
            $$"""
            [
              {
                "package": "{{dependencyPackageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              },
              {
                "package": "{{rootPackageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              }
            ]
            """);

        string dependencyName = dependency.FullName!;
        Assert.Contains(
            rootOnly.GraphEdges,
            edge => edge.FromId == typeName
                && edge.ToId == dependencyName
                && edge.Kind == "implements");
        Assert.DoesNotContain(
            rootOnly.GraphEdges,
            edge => edge.FromId == dependencyName
                && edge.ToId == typeof(IDisposable).FullName);
        Assert.Contains(
            workspace.GraphEdges,
            edge => edge.FromId == dependencyName
                && edge.ToId == typeof(IDisposable).FullName
                && edge.Kind == "implements");
        Assert.Empty(workspace.InspectionFailures);
        Assert.Equal(
            ExactTypeInspectionOutcome.Available,
            workspace.ExactTypeInspection.Content.Outcome);
        Assert.Equal(
            typeName,
            workspace.ExactTypeInspection.Content.Type?.FullName);
        Assert.IsType<InspectionShare.Available>(
            workspace.ExactTypeInspection.Share);
        InspectionEnvelope<ExactTypeInspectionResult> direct =
            await ExactTypeInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    rootPackageId,
                    "1.0.0",
                    "net11.0",
                    typeName),
                new WorkspaceContextLoadOptions
                {
                    HttpClient = BrowserPackageWorkspace.NetworkClient,
                    SourceAuthorization =
                        BrowserPackageWorkspace.PackageSourceAuthorization,
                    PackageStore =
                        BrowserPackageWorkspace.SessionPackageStore,
                    PackageTransferPolicy =
                        BrowserPackageWorkspace.PackageTransferPolicy,
                    PayloadLimits =
                        BrowserPackageWorkspace.PackageLimits,
                },
                BrowserApiSurfacePolicy.Limits,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            JsonSerializer.Serialize(
                direct,
                BrowserMetadataJsonContext.Default
                    .ExactTypeInspectionEnvelope),
            JsonSerializer.Serialize(
                workspace.ExactTypeInspection,
                BrowserMetadataJsonContext.Default
                    .ExactTypeInspectionEnvelope));
        Assert.Equal(
            typeName,
            workspace.TypeDependencyInspection.Content
                .QueryResult.Dependency.MatchedType);
        InspectionShare.Available share =
            Assert.IsType<InspectionShare.Available>(
                workspace.TypeDependencyInspection.Share);
        Assert.Contains(
            "?w=" + share.Packet,
            share.FullUrl,
            StringComparison.Ordinal);
        Assert.NotEmpty(share.Packet);
        Assert.Empty(workspace.TypeDependencyInspection.Diagnostics);
    }

    [Fact]
    public async Task ExactTypeInspection_BoundedProjectionMakesSelectionUnavailable()
    {
        const string packageId = "Browser.ExactType.Bounds";
        const string selectedType = "Browser.Bounds.Selected";
        const string omittedType = "Browser.Bounds.Omitted";
        _ = await Coordinate(
            packageId,
            PackageEntries(
                ("lib/net11.0/First.dll",
                    BuildTypeDependencyImage(
                        "Browser.Bounds.First",
                        selectedType,
                        typeof(IDisposable))),
                ("lib/net11.0/Second.dll",
                    BuildTypeDependencyImage(
                        "Browser.Bounds.Second",
                        omittedType,
                        typeof(IAsyncDisposable)))));
        var limits = new ApiSurfaceProjectionLimits(
            maxParticipants: 2,
            maxTypes: 1,
            maxMembers: 100,
            maxInspectionFailures: 100,
            maxTypeForwarders: 100,
            maxMetadataRows: 10_000);
        WorkspaceContextLoadOptions capabilities = new()
        {
            HttpClient = BrowserPackageWorkspace.NetworkClient,
            SourceAuthorization =
                BrowserPackageWorkspace.PackageSourceAuthorization,
            PackageStore =
                BrowserPackageWorkspace.SessionPackageStore,
            PackageTransferPolicy =
                BrowserPackageWorkspace.PackageTransferPolicy,
            PayloadLimits =
                BrowserPackageWorkspace.PackageLimits,
        };

        InspectionEnvelope<ExactTypeInspectionResult> selected =
            await ExactTypeInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    selectedType),
                capabilities,
                limits,
                TestContext.Current.CancellationToken);
        InspectionEnvelope<ExactTypeInspectionResult> unavailable =
            await ExactTypeInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    omittedType),
                capabilities,
                limits,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ExactTypeInspectionOutcome.Unavailable,
            selected.Content.Outcome);
        Assert.Null(selected.Content.Type);
        Assert.False(selected.Content.IsComplete);
        Assert.Contains(
            selected.Diagnostics,
            diagnostic => diagnostic.Code
                == "exact-type.projection-truncated");
        Assert.Equal(
            ExactTypeInspectionOutcome.Unavailable,
            unavailable.Content.Outcome);
        Assert.DoesNotContain(
            unavailable.Diagnostics,
            diagnostic => diagnostic.Code == "exact-type.not-found");
    }

    [Fact]
    public async Task
        TypeProjection_RetainedExactTypeUsesBrowserProjectionLimits()
    {
        const string packageId = "Browser.ExactType.RetainedBounds";
        const string assemblyName = "Browser.ExactType.RetainedBounds";
        const string typeName = "N.T0";
        _ = await Coordinate(
            packageId,
            Package(
                BuildTransportAmplificationImage(
                    assemblyName,
                    BrowserApiSurfacePolicy.MaxTypes + 1,
                    namespaceLength: 1),
                $"lib/net11.0/{assemblyName}.dll"));

        BrowserTypeMetadata metadata = await QueryTypeProjection(
            packageId,
            $"{assemblyName}.dll",
            typeName,
            $$"""
            [
              {
                "package": "{{packageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              }
            ]
            """);

        Assert.Equal(
            ExactTypeInspectionOutcome.Unavailable,
            metadata.ExactTypeInspection.Content.Outcome);
        Assert.Contains(
            metadata.ExactTypeInspection.Diagnostics,
            diagnostic => diagnostic.Code
                == "exact-type.projection-truncated");
        Assert.Null(metadata.Hierarchy);
    }

    [Fact]
    public async Task TypeProjection_RetainsTypedRelationshipRowSelection()
    {
        const string rootPackageId =
            "Browser.TypeDependencies.Rows.Root";
        const string dependencyPackageId =
            "Browser.TypeDependencies.Rows.Dependency";
        const string rootAssemblyName =
            "Browser.TypeDependencies.Rows.Root";
        const string typeName =
            "Browser.TypeDependencies.Rows.Consumer";
        Type dependency = typeof(IPackagePayloadReservation);

        _ = await Coordinate(
            rootPackageId,
            Package(
                BuildTypeDependencyImage(
                    rootAssemblyName,
                    typeName,
                    dependency,
                    typeof(ICloneable)),
                $"lib/net11.0/{rootAssemblyName}.dll"));
        _ = await Coordinate(
            dependencyPackageId,
            Package(
                File.ReadAllBytes(dependency.Assembly.Location),
                $"lib/net11.0/{dependency.Assembly.GetName().Name}.dll"));

        const string WorkspaceJson =
            """
            [
              {
                "package": "Browser.TypeDependencies.Rows.Root",
                "version": "1.0.0",
                "framework": "net11.0"
              },
              {
                "package": "Browser.TypeDependencies.Rows.Dependency",
                "version": "1.0.0",
                "framework": "net11.0"
              }
            ]
            """;
        BrowserTypeMetadata complete =
            await DotnetInspect.Web.Interop.Metadata.MetadataExports
                .TypeProjectionAsync(
                    rootPackageId,
                    "1.0.0",
                    "net11.0",
                    $"{rootAssemblyName}.dll",
                    typeName,
                    typeName,
                    WorkspaceJson,
                    Resolve(RowQueryIntent.Empty));
        BrowserTypeMetadata bounded =
            await DotnetInspect.Web.Interop.Metadata.MetadataExports
                .TypeProjectionAsync(
                    rootPackageId,
                    "1.0.0",
                    "net11.0",
                    $"{rootAssemblyName}.dll",
                    typeName,
                    typeName,
                    WorkspaceJson,
                    Resolve(
                        RowQueryIntent.Create(
                            [
                                new RowQueryPredicateIntent(
                                    "Source",
                                    RowQueryOperator.Equals,
                                    new RowQueryValueToken(typeName)),
                                new RowQueryPredicateIntent(
                                    "Kind",
                                    RowQueryOperator.Equals,
                                    new RowQueryValueToken("Interface")),
                            ],
                            RowQueryOrderIntent.Keys(
                                [
                                    new RowQueryOrderTermIntent(
                                        "Target",
                                        RowQueryOrderDirection.Descending),
                                ]),
                            RowSelectionIntent<RowQueryOrderIntent>.Create(
                                [
                                    RowSelectionIntentOperation<
                                        RowQueryOrderIntent>.Head(1),
                                ]))));
        string dependencyName = dependency.FullName!;
        string dependencyAssemblyName =
            dependency.Assembly.GetName().Name!;
        BrowserTypeMetadata implementers =
            await DotnetInspect.Web.Interop.Metadata.MetadataExports
                .TypeProjectionAsync(
                    dependencyPackageId,
                    "1.0.0",
                    "net11.0",
                    $"{dependencyAssemblyName}.dll",
                    dependencyName,
                    dependencyName,
                    WorkspaceJson,
                    Resolve(RowQueryIntent.Empty));
        BrowserTypeMetadata nested =
            await DotnetInspect.Web.Interop.Metadata.MetadataExports
                .TypeProjectionAsync(
                    rootPackageId,
                    "1.0.0",
                    "net11.0",
                    $"{rootAssemblyName}.dll",
                    typeName,
                    typeName,
                    WorkspaceJson,
                    Resolve(
                        RowQueryIntent.Create(
                            [
                                new RowQueryPredicateIntent(
                                    "Source",
                                    RowQueryOperator.Equals,
                                    new RowQueryValueToken(dependencyName)),
                            ],
                            baselineOrder: null,
                            RowSelectionIntent<RowQueryOrderIntent>.Empty)));

        Assert.Contains(
            complete.GraphEdges,
            edge => edge.FromId == dependencyName
                && edge.ToId == typeof(IDisposable).FullName);
        Assert.DoesNotContain(
            bounded.GraphEdges,
            edge => edge.FromId == dependencyName
                && edge.ToId == typeof(IDisposable).FullName);
        Assert.Equal(
            2,
            complete.GraphEdges.Count(edge =>
                edge.FromId == typeName
                && edge.Kind == "implements"));
        var selectedEdge = Assert.Single(bounded.GraphEdges);
        Assert.Equal(typeName, selectedEdge.FromId);
        Assert.Equal(typeof(ICloneable).FullName, selectedEdge.ToId);
        Assert.Equal("implements", selectedEdge.Kind);
        Assert.Empty(complete.InspectionFailures);
        Assert.Empty(bounded.InspectionFailures);
        Assert.NotNull(complete.Hierarchy);
        Assert.Equal("Derived Types", complete.Hierarchy.Form);
        Assert.Equal(0, complete.Hierarchy.Count);
        Assert.True(complete.Hierarchy.IsComplete);
        Assert.NotNull(implementers.Hierarchy);
        Assert.Equal("Implementers", implementers.Hierarchy.Form);
        Assert.Contains(
            implementers.Hierarchy.Rows,
            row => row.Type == typeName
                && row.PackageId == rootPackageId);
        Assert.True(implementers.Hierarchy.Count >= 1);
        Assert.Contains(
            implementers.Hierarchy.Coverage,
            coverage => coverage.Considered >= 1
                && coverage.Examined >= 1);
        TypeDependencyRelationship selectedRelationship =
            Assert.Single(
                bounded.TypeDependencyInspection.Content
                    .RowSelection.Relationships);
        Assert.Equal(
            typeof(ICloneable).FullName,
            selectedRelationship.TargetTypeName);
        var nestedEdge = Assert.Single(nested.GraphEdges);
        Assert.Equal(dependencyName, nestedEdge.FromId);
        Assert.Equal(
            "interface",
            Assert.Single(
                nested.GraphNodes,
                node => node.Id == dependencyName).Role);
    }

    [Fact]
    public async Task TypeProjection_HierarchyKeepsExactCountAndBoundedRows()
    {
        const string rootPackageId =
            "Browser.TypeHierarchy.Rows.Root";
        const string dependencyPackageId =
            "Browser.TypeHierarchy.Rows.Dependency";
        const string rootAssemblyName =
            "Browser.TypeHierarchy.Rows.Root";
        Type hierarchyInterface = typeof(IPackagePayloadReservation);
        string dependencyAssemblyName =
            hierarchyInterface.Assembly.GetName().Name!;
        string hierarchyName = hierarchyInterface.FullName!;

        _ = await Coordinate(
            rootPackageId,
            Package(
                BuildHierarchyImplementersImage(
                    rootAssemblyName,
                    "Browser.TypeHierarchy.Rows",
                    hierarchyInterface,
                    BrowserTypeHierarchyInspectionOperation.MaximumRows + 1),
                $"lib/net11.0/{rootAssemblyName}.dll"));
        _ = await Coordinate(
            dependencyPackageId,
            Package(
                File.ReadAllBytes(hierarchyInterface.Assembly.Location),
                $"lib/net11.0/{dependencyAssemblyName}.dll"));

        BrowserTypeMetadata metadata =
            await DotnetInspect.Web.Interop.Metadata.MetadataExports
                .TypeProjectionAsync(
                    dependencyPackageId,
                    "1.0.0",
                    "net11.0",
                    $"{dependencyAssemblyName}.dll",
                    hierarchyName,
                    hierarchyName,
                    $$"""
                    [
                      {
                        "package": "{{rootPackageId}}",
                        "version": "1.0.0",
                        "framework": "net11.0"
                      },
                      {
                        "package": "{{dependencyPackageId}}",
                        "version": "1.0.0",
                        "framework": "net11.0"
                      }
                    ]
                    """,
                    Resolve(RowQueryIntent.Empty));

        Assert.NotNull(metadata.Hierarchy);
        Assert.Equal("Implementers", metadata.Hierarchy.Form);
        Assert.True(metadata.Hierarchy.Count
            > BrowserTypeHierarchyInspectionOperation.MaximumRows);
        Assert.Equal(
            BrowserTypeHierarchyInspectionOperation.MaximumRows,
            metadata.Hierarchy.Rows.Length);
        Assert.True(metadata.Hierarchy.HasMore);
        Assert.False(metadata.Hierarchy.IsComplete);
        Assert.Equal("partial", metadata.Hierarchy.Status);
        Assert.DoesNotContain(
            metadata.Hierarchy.Rows,
            row => string.IsNullOrWhiteSpace(row.PackageId)
                || string.IsNullOrWhiteSpace(row.Asset));
    }

    [Fact]
    public async Task QueryTypeProjection_ReportsRejectedWorkspaceParticipants()
    {
        const string rootPackageId =
            "Browser.TypeDependencies.Rejection.Root";
        const string rejectedPackageId =
            "Browser.TypeDependencies.Rejection.Invalid";
        const string rootAssemblyName =
            "Browser.TypeDependencies.Rejection.Root";
        const string typeName =
            "Browser.TypeDependencies.Rejection.Consumer";

        _ = await Coordinate(
            rootPackageId,
            Package(
                BuildTypeDependencyImage(
                    rootAssemblyName,
                    typeName,
                    typeof(IPackagePayloadReservation)),
                $"lib/net11.0/{rootAssemblyName}.dll"));
        _ = await Coordinate(
            rejectedPackageId,
            Package(
                [0x01, 0x02, 0x03],
                "lib/net11.0/Rejected.dll"));

        BrowserTypeMetadata metadata = await QueryTypeProjection(
            rootPackageId,
            $"{rootAssemblyName}.dll",
            typeName,
            $$"""
            [
              {
                "package": "{{rootPackageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              },
              {
                "package": "{{rejectedPackageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              }
            ]
            """);

        Assert.Contains(
            metadata.InspectionFailures,
            failure => failure.Contains(
                rejectedPackageId,
                StringComparison.Ordinal)
                && failure.Contains(
                    "rejected",
                    StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            metadata.GraphEdges,
            edge => edge.FromId == typeName
                && edge.ToId
                    == typeof(IPackagePayloadReservation).FullName);
        var rejection = Assert.Single(
            metadata.TypeDependencyInspection.Diagnostics,
            diagnostic =>
                diagnostic.Code
                    == "type-dependency.participant-rejected");
        Assert.Equal("Warning", rejection.Severity.ToString());
        Assert.Contains(rejectedPackageId, rejection.Summary.ToString());
        Assert.Equal(
            $"{rejectedPackageId}@1.0.0/lib/net11.0/Rejected.dll",
            rejection.Correspondence?.ToString());
        Assert.NotNull(metadata.Hierarchy);
        Assert.Equal("unavailable", metadata.Hierarchy.Status);
        Assert.IsType<InspectionShare.Available>(
            metadata.ExactTypeInspection.Share);
        Assert.Equal(
            "nonProjectable",
            metadata.Hierarchy.Share.Kind);
        Assert.Equal(
            "type-hierarchy/share",
            metadata.Hierarchy.Share.Path);
        Assert.Contains(
            "cannot restore exact hierarchy authority",
            metadata.Hierarchy.Share.Reason,
            StringComparison.Ordinal);
        Assert.Contains(
            metadata.Hierarchy.Diagnostics,
            diagnostic => diagnostic.Code
                == "type-hierarchy.retained-context-unavailable");
    }

    [Fact]
    public async Task QueryTypeProjection_UsesTheSelectedSameNamedType()
    {
        const string rootPackageId =
            "Browser.TypeDependencies.Identity.Root";
        const string otherPackageId =
            "Browser.TypeDependencies.Identity.Other";
        const string rootAssemblyName =
            "Browser.TypeDependencies.Identity.Root";
        const string otherAssemblyName =
            "Browser.TypeDependencies.Identity.Other";
        const string typeName =
            "Browser.TypeDependencies.Identity.Consumer";

        _ = await Coordinate(
            otherPackageId,
            Package(
                BuildTypeDependencyImage(
                    otherAssemblyName,
                    typeName,
                    typeof(IAsyncDisposable)),
                $"lib/net11.0/{otherAssemblyName}.dll"));
        _ = await Coordinate(
            rootPackageId,
            Package(
                BuildTypeDependencyImage(
                    rootAssemblyName,
                    typeName,
                    typeof(IDisposable)),
                $"lib/net11.0/{rootAssemblyName}.dll"));

        BrowserTypeMetadata metadata = await QueryTypeProjection(
            rootPackageId,
            $"{rootAssemblyName}.dll",
            typeName,
            $$"""
            [
              {
                "package": "{{otherPackageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              },
              {
                "package": "{{rootPackageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              }
            ]
            """);

        Assert.Contains(
            metadata.GraphEdges,
            edge => edge.FromId == typeName
                && edge.ToId == typeof(IDisposable).FullName);
        Assert.DoesNotContain(
            metadata.GraphEdges,
            edge => edge.FromId == typeName
                && edge.ToId == typeof(IAsyncDisposable).FullName);
        Assert.Empty(metadata.InspectionFailures);

        const string authenticPackageId =
            "Browser.TypeDependencies.System.Text.Json";
        const string nestedType =
            "System.Collections.Generic.OrderedDictionary`2.KeyCollection";
        _ = await Coordinate(
            authenticPackageId,
            Package(
                File.ReadAllBytes(Path.Combine(
                    AppContext.BaseDirectory,
                    "RealAssets",
                    "TypeDependencies",
                    "System.Text.Json.dll")),
                "lib/net11.0/System.Text.Json.dll"));
        BrowserTypeMetadata authentic = await QueryTypeProjection(
            authenticPackageId,
            "System.Text.Json.dll",
            nestedType,
            $$"""
            [
              {
                "package": "{{authenticPackageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              }
            ]
            """);

        Assert.Contains(
            authentic.GraphEdges,
            edge => edge.FromId == nestedType
                && edge.ToId
                    == "System.Collections.Generic.IList<TKey>");
        Assert.Empty(authentic.InspectionFailures);
    }

    [Fact]
    public async Task TypeProjection_HierarchyUsesSelectedSameNamedInterface()
    {
        const string rootPackageId =
            "Browser.TypeHierarchy.Identity.Root";
        const string otherPackageId =
            "Browser.TypeHierarchy.Identity.Other";
        const string rootAssemblyName =
            "Browser.TypeHierarchy.Identity.Root";
        const string otherAssemblyName =
            "Browser.TypeHierarchy.Identity.Other";
        const string interfaceName =
            "Browser.TypeHierarchy.Identity.IContract";
        const string rootImplementer =
            "Browser.TypeHierarchy.Identity.RootImplementer";
        const string otherImplementer =
            "Browser.TypeHierarchy.Identity.OtherImplementer";

        _ = await Coordinate(
            otherPackageId,
            Package(
                BuildHierarchyIdentityImage(
                    otherAssemblyName,
                    interfaceName,
                    otherImplementer),
                $"lib/net11.0/{otherAssemblyName}.dll"));
        _ = await Coordinate(
            rootPackageId,
            Package(
                BuildHierarchyIdentityImage(
                    rootAssemblyName,
                    interfaceName,
                    rootImplementer),
                $"lib/net11.0/{rootAssemblyName}.dll"));

        BrowserTypeMetadata metadata = await QueryTypeProjection(
            rootPackageId,
            $"{rootAssemblyName}.dll",
            interfaceName,
            $$"""
            [
              {
                "package": "{{otherPackageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              },
              {
                "package": "{{rootPackageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              }
            ]
            """);

        Assert.Equal(
            ExactTypeInspectionOutcome.Available,
            metadata.ExactTypeInspection.Content.Outcome);
        Assert.NotNull(metadata.Hierarchy);
        Assert.Equal("Implementers", metadata.Hierarchy.Form);
        Assert.Equal(1, metadata.Hierarchy.Count);
        var row = Assert.Single(metadata.Hierarchy.Rows);
        Assert.Equal(rootImplementer, row.Type);
        Assert.Equal(rootPackageId, row.PackageId);
        Assert.Equal(
            $"lib/net11.0/{rootAssemblyName}.dll",
            row.Asset);
        Assert.DoesNotContain(
            metadata.Hierarchy.Rows,
            candidate => candidate.Type == otherImplementer
                || candidate.PackageId == otherPackageId);
        Assert.Empty(metadata.InspectionFailures);
    }

    [Fact]
    public async Task TypeProjection_HierarchyRetainsNestedDefinitionIdentity()
    {
        const string packageId =
            "Browser.TypeHierarchy.NestedIdentity";
        const string assemblyName =
            "Browser.TypeHierarchy.NestedIdentity";
        const string interfaceName =
            "Browser.TypeHierarchy.NestedIdentity.Contract";
        const string outerTypeName =
            "Browser.TypeHierarchy.NestedIdentity.Outer";
        const string nestedDefinitionName =
            "Browser.TypeHierarchy.NestedIdentity.Outer+Implementer";

        _ = await Coordinate(
            packageId,
            Package(
                BuildNestedHierarchyIdentityImage(
                    assemblyName,
                    interfaceName,
                    outerTypeName,
                    "Implementer"),
                $"lib/net11.0/{assemblyName}.dll"));

        BrowserTypeMetadata metadata = await QueryTypeProjection(
            packageId,
            $"{assemblyName}.dll",
            interfaceName,
            $$"""
            [
              {
                "package": "{{packageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              }
            ]
            """);

        Assert.NotNull(metadata.Hierarchy);
        var row = Assert.Single(metadata.Hierarchy.Rows);
        Assert.Equal(nestedDefinitionName, row.Type);
        Assert.Equal(packageId, row.PackageId);
        Assert.Equal(
            $"lib/net11.0/{assemblyName}.dll",
            row.Asset);
    }

    [Fact]
    public async Task
        TypeProjection_ShareRejectsAmbiguousPackageAssetReplay()
    {
        const string packageId =
            "Browser.TypeHierarchy.ShareIdentity";
        const string firstAssemblyName =
            "Browser.TypeHierarchy.ShareIdentity.First";
        const string selectedAssemblyName =
            "Browser.TypeHierarchy.ShareIdentity.Selected";
        const string typeName =
            "Browser.TypeHierarchy.ShareIdentity.Consumer";

        _ = await Coordinate(
            packageId,
            PackageEntries(
                (
                    $"lib/net11.0/{firstAssemblyName}.dll",
                    BuildTypeDependencyImage(
                        firstAssemblyName,
                        typeName,
                        typeof(IDisposable))),
                (
                    $"lib/net11.0/{selectedAssemblyName}.dll",
                    BuildTypeDependencyImage(
                        selectedAssemblyName,
                        typeName,
                        typeof(IAsyncDisposable)))));

        BrowserTypeMetadata metadata = await QueryTypeProjection(
            packageId,
            $"{selectedAssemblyName}.dll",
            typeName,
            $$"""
            [
              {
                "package": "{{packageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              }
            ]
            """);

        Assert.Equal(
            ExactTypeInspectionOutcome.Available,
            metadata.ExactTypeInspection.Content.Outcome);
        Assert.Equal(
            selectedAssemblyName,
            metadata.ExactTypeInspection.Content.RequestedAssembly?
                .Identity.Name);
        InspectionShare.NonProjectable share =
            Assert.IsType<InspectionShare.NonProjectable>(
                metadata.ExactTypeInspection.Share);
        Assert.Equal("exact-type/share", share.Path);
        Assert.Contains(
            "exact selected Type occurrence",
            share.Reason.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryTypeProjection_UsesSelectedNestedDefinitionIdentity()
    {
        const string packageId =
            "Browser.TypeDependencies.NestedIdentity";
        const string nestedAssemblyName =
            "Browser.TypeDependencies.NestedIdentity.Selected";
        const string topLevelAssemblyName =
            "Browser.TypeDependencies.NestedIdentity.Other";
        const string outerTypeName =
            "Browser.TypeDependencies.NestedIdentity.Outer";
        const string typeQueryId =
            "Browser.TypeDependencies.NestedIdentity.Outer.Inner";
        const string typeDefinitionId =
            "Browser.TypeDependencies.NestedIdentity.Outer+Inner";

        _ = await Coordinate(
            packageId,
            PackageEntries(
                (
                    $"lib/net11.0/{nestedAssemblyName}.dll",
                    BuildNestedTypeDependencyImage(
                        nestedAssemblyName,
                        outerTypeName,
                        "Inner",
                        typeof(IDisposable))),
                (
                    $"lib/net11.0/{topLevelAssemblyName}.dll",
                    BuildTypeDependencyImage(
                        topLevelAssemblyName,
                        typeQueryId,
                        typeof(IAsyncDisposable)))));

        BrowserTypeMetadata metadata = await QueryTypeProjection(
            packageId,
            $"{nestedAssemblyName}.dll",
            typeQueryId,
            $$"""
            [
              {
                "package": "{{packageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              }
            ]
            """,
            typeDefinitionId);

        ExactTypeApi exactType =
            Assert.IsType<ExactTypeApi>(
                metadata.ExactTypeInspection.Content.Type);
        Assert.Equal(typeQueryId, exactType.FullName);
        Assert.Equal(
            nestedAssemblyName,
            metadata.ExactTypeInspection.Content
                .RequestedAssembly?.Identity.Name);
        Assert.Equal(
            nestedAssemblyName,
            metadata.ExactTypeInspection.Content
                .SupplierAssembly?.Identity.Name);
        Assert.Contains(typeof(IDisposable).FullName!, exactType.Interfaces);
        Assert.DoesNotContain(
            typeof(IAsyncDisposable).FullName!,
            exactType.Interfaces);
    }

    [Fact]
    public async Task QueryTypeProjection_UsesOrdinalDefinitionIdentity()
    {
        const string packageId =
            "Browser.TypeDependencies.CaseIdentity";
        const string selectedAssemblyName =
            "Browser.TypeDependencies.CaseIdentity.Selected";
        const string otherAssemblyName =
            "Browser.TypeDependencies.CaseIdentity.Other";
        const string selectedType =
            "Browser.TypeDependencies.CaseIdentity.Widget";
        const string otherType =
            "Browser.TypeDependencies.CaseIdentity.widget";

        _ = await Coordinate(
            packageId,
            PackageEntries(
                (
                    $"lib/net11.0/{selectedAssemblyName}.dll",
                    BuildTypeDependencyImage(
                        selectedAssemblyName,
                        selectedType,
                        typeof(IDisposable))),
                (
                    $"lib/net11.0/{otherAssemblyName}.dll",
                    BuildTypeDependencyImage(
                        otherAssemblyName,
                        otherType,
                        typeof(IAsyncDisposable)))));

        BrowserTypeMetadata metadata = await QueryTypeProjection(
            packageId,
            $"{selectedAssemblyName}.dll",
            selectedType,
            $$"""
            [
              {
                "package": "{{packageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              }
            ]
            """,
            selectedType);

        ExactTypeApi exactType =
            Assert.IsType<ExactTypeApi>(
                metadata.ExactTypeInspection.Content.Type);
        Assert.Equal(
            ExactTypeInspectionOutcome.Available,
            metadata.ExactTypeInspection.Content.Outcome);
        Assert.Equal(
            "Browser.TypeDependencies.CaseIdentity",
            exactType.DefinitionIdentity.Namespace);
        Assert.Equal(["Widget"], exactType.DefinitionIdentity.Segments);
        Assert.Contains(typeof(IDisposable).FullName!, exactType.Interfaces);
        Assert.DoesNotContain(
            typeof(IAsyncDisposable).FullName!,
            exactType.Interfaces);
    }

    [Fact]
    public async Task QueryTypeProjection_SelectsExactInternalDependencyRootOverFuzzyPublicType()
    {
        const string packageId =
            "Browser.TypeDependencies.Fuzzy.Root";
        const string assemblyName =
            "Browser.TypeDependencies.Fuzzy.Root";
        const string typeName =
            "Browser.TypeDependencies.Fuzzy.Widget";

        _ = await Coordinate(
            packageId,
            Package(
                BuildFuzzyTypeDependencyCollisionImage(
                    assemblyName,
                    typeName,
                    typeof(IDisposable),
                    typeof(IAsyncDisposable)),
                $"lib/net11.0/{assemblyName}.dll"));

        BrowserTypeMetadata metadata = await QueryTypeProjection(
            packageId,
            $"{assemblyName}.dll",
            typeName,
            $$"""
            [
              {
                "package": "{{packageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              }
            ]
            """);

        ExactTypeApi exactType =
            Assert.IsType<ExactTypeApi>(
                metadata.ExactTypeInspection.Content.Type);
        Assert.Equal(typeName, exactType.FullName);
        Assert.Contains(typeof(IDisposable).FullName!, exactType.Interfaces);
        Assert.DoesNotContain(
            typeof(IAsyncDisposable).FullName!,
            exactType.Interfaces);
        Assert.Contains(
            metadata.GraphEdges,
            edge => edge.FromId == typeName
                && edge.ToId == typeof(IDisposable).FullName);
        Assert.DoesNotContain(
            metadata.GraphEdges,
            edge => edge.FromId == typeName
                && edge.ToId == typeof(IAsyncDisposable).FullName);
        Assert.Empty(metadata.InspectionFailures);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task QueryTypeProjection_InspectsAuthenticInternalDependencyRoot()
    {
        const string packageId = "System.Text.Json";
        const string version = "11.0.0-rc.1.26425.128";
        const string typeName = "System.Text.ValueStringBuilder";
        byte[] archive = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "TypeDependencies",
            "InternalRoot",
            $"system.text.json.{version}.nupkg"));
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(packageId, version, archive, fromCache: false));

        BrowserTypeMetadata metadata = await QueryTypeProjection(
            packageId,
            "System.Text.Json.dll",
            typeName,
            $$"""[{"package":"{{packageId}}","version":"{{version}}","framework":"net11.0"}]""",
            version: version);

        Assert.Equal(
            typeName,
            Assert.IsType<ExactTypeApi>(
                metadata.ExactTypeInspection.Content.Type).FullName);
        // The existing dependency convention omits Object, ValueType, and Enum.
        Assert.Empty(metadata.GraphEdges);
        Assert.True(metadata.TypeDependencyInspection.Content
            .QueryResult.Dependency.Found);
        Assert.Empty(metadata.InspectionFailures);
    }

    [Fact]
    public void TypeProjectionRequests_RequireExactlyOneActiveCoordinate()
    {
        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(
                () => DotnetInspect.Web.Interop.Metadata.MetadataExports
                    .TypeProjectionRequests(
                        "Root.Package",
                        "1.0.0",
                        "net11.0",
                        """
                        [
                          {
                            "package": "Other.Package",
                            "version": "1.0.0",
                            "framework": "net11.0"
                          }
                        ]
                        """));

        Assert.Contains(
            "active package coordinate exactly once",
            failure.Message,
            StringComparison.Ordinal);
    }

    static async Task<BrowserTypeMetadata> QueryTypeProjection(
        string packageId,
        string assemblyName,
        string typeName,
        string workspaceJson,
        string? typeDefinitionId = null,
        string version = "1.0.0")
    {
        string json =
            await DotnetInspect.Web.Interop.Metadata.MetadataExports
                .QueryTypeProjection(
                    packageId,
                    version,
                    "net11.0",
                    assemblyName,
                    typeName,
                    typeDefinitionId ?? typeName,
                    workspaceJson);
        var options = new JsonSerializerOptions(
            BrowserMetadataJsonContext.Default.Options);
        _ = BrowserMetadataJsonSerialization.BrowserTypeMetadata;
        options.Converters.Insert(
            0,
            new TypeDependencyEnvelopeJsonConverter());
        return JsonSerializer.Deserialize<BrowserTypeMetadata>(
            json,
            options)!;
    }

    private static ResolvedRowQueryPlan<TypeDependencyRelationship>
        Resolve(RowQueryIntent intent)
    {
        RowQueryResolutionResult<TypeDependencyRelationship> result =
            TypeDependencyVocabulary.Resolve(intent);
        return result.Plan
            ?? throw new Xunit.Sdk.XunitException(
                $"Expected Type Dependency query to resolve: "
                    + $"{result.Failure!.OperationKind}/"
                    + $"{result.Failure.Reason}.");
    }

    sealed class TypeDependencyEnvelopeJsonConverter
        : JsonConverter<InspectionEnvelope<TypeDependencySectionResult>>
    {
        public override InspectionEnvelope<TypeDependencySectionResult> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;
            JsonElement content = root.GetProperty("content");
            var wireOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };
            TypeDependencyResult dependency =
                JsonSerializer.Deserialize<TypeDependencyResult>(
                    content.GetProperty("queryResult")
                        .GetProperty("dependency"),
                    wireOptions)!;
            IReadOnlyList<TypeDependencyRelationship> relationships =
                JsonSerializer.Deserialize<
                    List<TypeDependencyRelationship>>(
                    content.GetProperty("rowSelection")
                        .GetProperty("relationships"),
                    wireOptions)
                ?? [];
            TypeDependencyRowSelectionResult rowSelection =
                new(relationships, failure: null);
            InspectionShare share =
                JsonSerializer.Deserialize<InspectionShare>(
                    root.GetProperty("share"),
                    wireOptions)!;
            ImmutableArray<InspectionDiagnostic> diagnostics =
                root.GetProperty("diagnostics")
                    .EnumerateArray()
                    .Select(static diagnostic =>
                    {
                        InspectionDiagnosticSeverity severity =
                            diagnostic.GetProperty("severity").ValueKind
                                is JsonValueKind.Number
                                ? (InspectionDiagnosticSeverity)(
                                    diagnostic.GetProperty("severity")
                                        .GetInt32())
                                : Enum.Parse<InspectionDiagnosticSeverity>(
                                    diagnostic.GetProperty("severity")
                                        .GetString()!,
                                    ignoreCase: true);
                        string? correspondence =
                            diagnostic.TryGetProperty(
                                "correspondence",
                                out JsonElement correspondenceElement)
                                && correspondenceElement.ValueKind
                                    != JsonValueKind.Null
                                ? correspondenceElement.GetString()
                                : null;
                        return new InspectionDiagnostic(
                            diagnostic.GetProperty("code").GetString()!,
                            severity,
                            diagnostic.GetProperty("summary").GetString()!,
                            correspondence);
                    })
                    .ToImmutableArray();

            return new(
                new TypeDependencySectionResult(
                    new AssemblyContextTypeDependencyResult(
                        dependency,
                        []),
                    rowSelection),
                share,
                diagnostics);
        }

        public override void Write(
            Utf8JsonWriter writer,
            InspectionEnvelope<TypeDependencySectionResult> value,
            JsonSerializerOptions options) =>
            throw new NotSupportedException();
    }

    static byte[] BuildTypeDependencyImage(
        string assemblyName,
        string typeName,
        params Type[] dependencies)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule(assemblyName);
        TypeBuilder type = module.DefineType(
            typeName,
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Class);
        foreach (Type dependency in dependencies)
            type.AddInterfaceImplementation(dependency);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    static byte[] BuildHierarchyImplementersImage(
        string assemblyName,
        string typeNamespace,
        Type hierarchyInterface,
        int count)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule(assemblyName);
        for (int index = 0; index < count; index++)
        {
            TypeBuilder type = module.DefineType(
                $"{typeNamespace}.Implementer{index:D3}",
                TypeAttributes.Public
                    | TypeAttributes.Abstract
                    | TypeAttributes.Class);
            type.AddInterfaceImplementation(hierarchyInterface);
            type.CreateType();
        }

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    static byte[] BuildHierarchyIdentityImage(
        string assemblyName,
        string interfaceName,
        string implementerName)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule(assemblyName);
        TypeBuilder hierarchyInterface = module.DefineType(
            interfaceName,
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Interface);
        Type interfaceType = hierarchyInterface.CreateType();
        TypeBuilder implementer = module.DefineType(
            implementerName,
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Class);
        implementer.AddInterfaceImplementation(interfaceType);
        implementer.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    static byte[] BuildNestedTypeDependencyImage(
        string assemblyName,
        string outerTypeName,
        string nestedTypeName,
        params Type[] dependencies)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule(assemblyName);
        TypeBuilder outer = module.DefineType(
            outerTypeName,
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Class);
        TypeBuilder nested = outer.DefineNestedType(
            nestedTypeName,
            TypeAttributes.NestedPublic
                | TypeAttributes.Abstract
                | TypeAttributes.Class);
        foreach (Type dependency in dependencies)
            nested.AddInterfaceImplementation(dependency);
        nested.CreateType();
        outer.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    static byte[] BuildNestedHierarchyIdentityImage(
        string assemblyName,
        string interfaceName,
        string outerTypeName,
        string nestedTypeName)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule(assemblyName);
        TypeBuilder hierarchyInterface = module.DefineType(
            interfaceName,
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Interface);
        Type interfaceType = hierarchyInterface.CreateType();
        TypeBuilder outer = module.DefineType(
            outerTypeName,
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Class);
        TypeBuilder nested = outer.DefineNestedType(
            nestedTypeName,
            TypeAttributes.NestedPublic
                | TypeAttributes.Abstract
                | TypeAttributes.Class);
        nested.AddInterfaceImplementation(interfaceType);
        nested.CreateType();
        outer.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    static byte[] BuildFuzzyTypeDependencyCollisionImage(
        string assemblyName,
        string typeName,
        Type selectedDependency,
        Type fuzzyDependency)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule(assemblyName);
        TypeBuilder selected = module.DefineType(
            typeName,
            TypeAttributes.NotPublic
                | TypeAttributes.Abstract
                | TypeAttributes.Class);
        selected.AddInterfaceImplementation(selectedDependency);
        selected.CreateType();
        TypeBuilder fuzzy = module.DefineType(
            $"{typeName}`1",
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Class);
        fuzzy.DefineGenericParameters("T");
        fuzzy.AddInterfaceImplementation(fuzzyDependency);
        fuzzy.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }
}
