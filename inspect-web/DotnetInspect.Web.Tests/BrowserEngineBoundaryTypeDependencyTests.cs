using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Sections;

using BrowserMetadataJsonContext =
    DotnetInspect.Web.Interop.Metadata.BrowserMetadataJsonContext;
using BrowserTypeMetadata =
    DotnetInspect.Web.Interop.Metadata.BrowserTypeMetadata;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed partial class BrowserEngineBoundaryTests
{
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
                    dependency,
                    typeof(ICloneable)),
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
                    dependency),
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
                    WorkspaceJson,
                    RowSelectionIntent<
                        TypeDependencyRowOrder>.Empty);
        BrowserTypeMetadata bounded =
            await DotnetInspect.Web.Interop.Metadata.MetadataExports
                .TypeProjectionAsync(
                    rootPackageId,
                    "1.0.0",
                    "net11.0",
                    $"{rootAssemblyName}.dll",
                    typeName,
                    WorkspaceJson,
                    RowSelectionIntent<
                        TypeDependencyRowOrder>.Create(
                            [
                                RowSelectionIntentOperation<
                                    TypeDependencyRowOrder>.Head(1),
                            ]));

        string dependencyName = dependency.FullName!;
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
        Assert.Single(
            bounded.GraphEdges.Where(edge =>
                edge.FromId == typeName
                && edge.Kind == "implements"));
        Assert.Empty(bounded.InspectionFailures);
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
    }

    [Fact]
    public async Task QueryTypeProjection_DoesNotUseFuzzyDependencyRoot()
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

        Assert.Contains(
            metadata.GraphEdges,
            edge => edge.FromId == typeName
                && edge.ToId == typeof(IDisposable).FullName);
        Assert.DoesNotContain(
            metadata.GraphEdges,
            edge => edge.FromId == typeName
                && edge.ToId == typeof(IAsyncDisposable).FullName);
        Assert.Contains(
            metadata.InspectionFailures,
            failure => failure.Contains(
                "could not certify the selected type",
                StringComparison.Ordinal));
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
        string workspaceJson)
    {
        string json =
            await DotnetInspect.Web.Interop.Metadata.MetadataExports
                .QueryTypeProjection(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    assemblyName,
                    typeName,
                    workspaceJson);
        return JsonSerializer.Deserialize(
            json,
            BrowserMetadataJsonContext.Default.BrowserTypeMetadata)!;
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
