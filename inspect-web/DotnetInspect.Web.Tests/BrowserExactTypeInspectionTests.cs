using System.Runtime.Versioning;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Research;
using DotnetInspector.SourceSelection;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Web.Interop.Metadata;

using BrowserTypeMetadata =
    DotnetInspect.Web.Interop.Metadata.BrowserTypeMetadata;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed partial class BrowserEngineBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactType_TransportUsesReferenceSurfaceWithSharedIdentity(bool referenceOnly)
    {
        string package = "Browser.ExactType.ReferenceSurface." + referenceOnly;
        string fixtureRoot = Path.Combine(AppContext.BaseDirectory, "RealAssets", "Spotlight");
        byte[] reference = File.ReadAllBytes(Path.Combine(fixtureRoot, "platform", "System.Text.Json.dll"));
        byte[] implementation = File.ReadAllBytes(Path.Combine(fixtureRoot, "package", "System.Text.Json.dll"));
        BrowserPackageCoordinate coordinate = await Coordinate(
            package,
            referenceOnly
                ? Package(reference, "ref/net11.0/System.Text.Json.dll")
                : PackageEntries(
                    ("ref/net11.0/System.Text.Json.dll", reference),
                    ("lib/net11.0/System.Text.Json.dll", implementation)));
        string asset = coordinate.CompileAsset("System.Text.Json.dll").Id;
        using var referencePe = new PEReader(new MemoryStream(reference));
        using var implementationPe = new PEReader(new MemoryStream(implementation));
        MetadataReader metadata = referencePe.GetMetadataReader();
        Guid expectedMvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
        MetadataReader other = implementationPe.GetMetadataReader();
        Assert.NotEqual(expectedMvid, other.GetGuid(other.GetModuleDefinition().Mvid));
        Assert.Equal(metadata.GetAssemblyDefinition().Version, other.GetAssemblyDefinition().Version);
        Assert.Equal(metadata.GetString(metadata.GetAssemblyDefinition().Name), other.GetString(other.GetAssemblyDefinition().Name));

        string json = await DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryTypeProjection(
            package, "1.0.0", "net11.0", "SYSTEM.TEXT.JSON.DLL", "JsonSerializer",
            $$"""[{"package":"{{package}}","version":"1.0.0","framework":"net11.0"}]""");
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement envelope = document.RootElement.GetProperty("exactTypeInspection");
        JsonElement content = envelope.GetProperty("content");
        JsonElement candidate = content.GetProperty("available").GetProperty("candidate");
        Assert.Equal("Available", content.GetProperty("kind").GetString());
        Assert.Equal(expectedMvid.ToString("D"), candidate.GetProperty("address").GetProperty("moduleVersionId").GetString());
        Assert.Equal(asset, content.GetProperty("request").GetProperty("compileAssetId").GetString());
        Assert.Equal(asset, candidate.GetProperty("declarationAssetId").GetString());
        Assert.Equal(asset, candidate.GetProperty("supplierAssetId").GetString());
        Assert.Equal("Available", envelope.GetProperty("share").GetProperty("kind").GetString());
        Assert.True(document.RootElement.TryGetProperty("typeDependencyInspection", out _));
    }

    [Fact]
    public async Task ExactType_AssemblySelectionPreservesDetachedDerivedTypes()
    {
        const string package = "Browser.ExactType.AssemblyCollision";
        const string typeName = "ExactType.Base";
        _ = await Coordinate(package, PackageEntries(
            ("lib/net11.0/First.dll", Image("First", typeof(IDisposable), "FirstChild")),
            ("lib/net11.0/Second.dll", Image("Second", typeof(IAsyncDisposable), "SecondChild"))));

        foreach (var (assembly, dependency, child) in new[]
        {
            ("First", typeof(IDisposable), "FirstChild"),
            ("Second", typeof(IAsyncDisposable), "SecondChild"),
        })
        {
            var envelope = await DotnetInspect.Web.Interop.Metadata.BrowserExactTypeInspection.ExecuteAsync(
                package, "1.0.0", "net11.0", typeName, assembly + ".dll",
                cancellationToken: TestContext.Current.CancellationToken);
            var available = Assert.IsType<ExactTypeInspectionResult.Available>(envelope.Content);
            Assert.Equal(assembly, available.Candidate.SupplierAssembly.Name);
            Assert.Equal([dependency.FullName!], available.Type.Interfaces);
            Assert.Equal(["ExactType." + child], available.Type.DerivedTypes);
            Assert.Null(available.Type.SourceAssemblyPath);
            var projection = ResearchViews.ProjectType(available.Type);
            Assert.Equal(available.Type.DerivedTypes, projection.DerivedTypes);
            Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        }

        static byte[] Image(string name, Type dependency, string childName)
        {
            var assembly = new PersistedAssemblyBuilder(new AssemblyName(name), typeof(object).Assembly);
            ModuleBuilder module = assembly.DefineDynamicModule(name);
            TypeBuilder type = module.DefineType(
                typeName, TypeAttributes.Public | TypeAttributes.Abstract);
            type.AddInterfaceImplementation(dependency);
            type.CreateType();
            module.DefineType(
                "ExactType." + childName,
                TypeAttributes.Public | TypeAttributes.Abstract,
                type).CreateType();
            using var image = new MemoryStream();
            assembly.Save(image);
            return image.ToArray();
        }
    }

    [Fact]
    public async Task ExactType_FullBrowserIdentityPrecedesNamespaceSuffix()
    {
        const string package = "Browser.ExactType.FullIdentity";
        const string assemblyName = "FullIdentity";
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            typeof(object).Assembly);
        ModuleBuilder module =
            assembly.DefineDynamicModule(assemblyName);
        module.DefineType(
            "N.Widget",
            TypeAttributes.Public | TypeAttributes.Class).CreateType();
        module.DefineType(
            "Other.N.Widget",
            TypeAttributes.Public | TypeAttributes.Class).CreateType();
        using var image = new MemoryStream();
        assembly.Save(image);
        _ = await Coordinate(
            package,
            Package(
                image.ToArray(),
                $"lib/net11.0/{assemblyName}.dll"));
        string workspaceJson =
            $$"""
            [
              {
                "package": "{{package}}",
                "version": "1.0.0",
                "framework": "net11.0"
              }
            ]
            """;

        BrowserTypeMetadata presentation =
            await DotnetInspect.Web.Interop.Metadata.MetadataExports
                .TypeProjectionAsync(
                    package,
                    "1.0.0",
                    "net11.0",
                    $"{assemblyName}.dll",
                    "N.Widget",
                    workspaceJson,
                    Resolve(RowQueryIntent.Empty));

        Assert.Equal("N.Widget", presentation.FullName);
        Assert.Equal(
            BrowserExactTypeOutcome.Available,
            presentation.ExactTypeInspection.Content.Kind);
    }

    [Fact]
    public async Task ExactType_EscapedNestedIdentityPrecedesDottedTopLevel()
    {
        const string package = "Browser.ExactType.StructuredIdentity";
        const string assemblyName = "StructuredIdentity";
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            typeof(object).Assembly);
        ModuleBuilder module =
            assembly.DefineDynamicModule(assemblyName);
        TypeBuilder outer = module.DefineType(
            "N.Outer",
            TypeAttributes.Public | TypeAttributes.Class);
        TypeBuilder nested = outer.DefineNestedType(
            "Inner",
            TypeAttributes.NestedPublic | TypeAttributes.Class);
        nested.DefineDefaultConstructor(MethodAttributes.Public);
        outer.DefineDefaultConstructor(MethodAttributes.Public);
        nested.CreateType();
        outer.CreateType();
        module.DefineType(
            "N.Outer.Inner",
            TypeAttributes.Public | TypeAttributes.Class).CreateType();
        using var image = new MemoryStream();
        assembly.Save(image);
        _ = await Coordinate(
            package,
            Package(
                image.ToArray(),
                $"lib/net11.0/{assemblyName}.dll"));
        string workspaceJson =
            $$"""
            [
              {
                "package": "{{package}}",
                "version": "1.0.0",
                "framework": "net11.0"
              }
            ]
            """;

        BrowserTypeMetadata presentation =
            await DotnetInspect.Web.Interop.Metadata.MetadataExports
                .TypeProjectionAsync(
                    package,
                    "1.0.0",
                    "net11.0",
                    $"{assemblyName}.dll",
                    "N.Outer+Inner",
                    workspaceJson,
                    Resolve(RowQueryIntent.Empty));

        Assert.Equal(
            ["Outer", "Inner"],
            presentation.ExactTypeInspection.Content.Available!
                .Candidate.Definition.Segments);
    }

    [Fact]
    public async Task TypeProjection_UsesSharedExactTypeEnvelope()
    {
        const string packageId = "Browser.ExactType.SystemTextJson";
        const string supplierAssembly = "System.Text.Json";
        const string typeName = "System.Text.Json.JsonSerializer";
        byte[] supplier = File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "Spotlight",
                "package",
                "System.Text.Json.dll"));
        BrowserPackageCoordinate coordinate = await Coordinate(
            packageId,
            Package(
                supplier,
                $"lib/net11.0/{supplierAssembly}.dll"));
        string compileAssetId =
            Assert.Single(coordinate.FrameworkAssets).Id;
        string workspaceJson =
            $$"""
            [
              {
                "package": "{{packageId}}",
                "version": "1.0.0",
                "framework": "net11.0"
              }
            ]
            """;

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await DotnetInspect.Web.Interop.Metadata
                .BrowserExactTypeInspection.ExecuteAsync(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    typeName,
                    $"{supplierAssembly}.dll",
                    cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(
            envelope.Content is ExactTypeInspectionResult.Available,
            $"Expected Available, got {envelope.Content.GetType().Name}: "
                + string.Join(
                    ", ",
                    envelope.Content.Failures.Select(
                        static failure =>
                            $"{failure.Kind}/{failure.ContextLoadFailure}")));
        var available =
            (ExactTypeInspectionResult.Available)envelope.Content;
        Assert.Equal(
            supplierAssembly,
            available.Candidate.SupplierAssembly.Name);
        Assert.Empty(available.Candidate.ForwardingHops);
        Assert.Equal(
            packageId,
            Assert.IsType<ExactLibrarySourceCoordinate.Package>(
                available.Candidate.Supplier).PackageCoordinate.PackageId,
            ignoreCase: true);
        ResearchViews.TypeProjectionResult sharedProjection =
            ResearchViews.ProjectType(available.Type);

        BrowserTypeMetadata presentation =
            await DotnetInspect.Web.Interop.Metadata.MetadataExports
                .TypeProjectionAsync(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    compileAssetId,
                    typeName,
                    workspaceJson,
                    Resolve(RowQueryIntent.Empty));

        Assert.Equal(
            sharedProjection.Identity.FullName,
            presentation.FullName);
        Assert.Equal(supplierAssembly, presentation.Assembly);
        Assert.Equal(sharedProjection.Identity.Kind, presentation.Kind);
        Assert.Equal(sharedProjection.BaseType, presentation.BaseType);
        Assert.Equal(sharedProjection.Attributes, presentation.Attributes);
        Assert.NotNull(presentation.Composition);
        Assert.Equal(
            sharedProjection.Composition?.Methods,
            presentation.Composition.Methods);
        Assert.True(sharedProjection.Composition?.Async > 0);
        Assert.Equal(
            sharedProjection.Composition?.Async,
            presentation.Composition.Async);
        Assert.Empty(presentation.InspectionFailures);
        Assert.Equal(
            BrowserExactTypeOutcome.Available,
            presentation.ExactTypeInspection.Content.Kind);
        Assert.True(
            presentation.ExactTypeInspection.Content.IsComplete);
        BrowserExactTypeAvailable transported =
            Assert.IsType<BrowserExactTypeAvailable>(
                presentation.ExactTypeInspection.Content.Available);
        Assert.Equal(
            available.Candidate.Definition.Namespace,
            transported.Candidate.Definition.Namespace);
        Assert.Equal(
            available.Candidate.Definition.Segments,
            transported.Candidate.Definition.Segments);
        Assert.Equal(
            available.Members.Members.Count,
            transported.Type.Surface.Api.Length);
        Assert.Equal(
            available.Members.Members.Select(member =>
                (member.MetadataToken, member.DeclarationMetadataToken, member.IsAsync, member.HasMethodBody)),
            transported.MemberFacts.Select(member =>
                (member.MetadataToken, member.DeclarationMetadataToken, member.IsAsync, member.HasMethodBody)));
        Assert.Equal(
            available.Members.KindFacets.Count,
            transported.MemberKindFacets.Length);
        Assert.Equal(
            available.InspectionFailures.Length,
            transported.InspectionFailures.Length);

        var cliOptions = new TypeOptions
        {
            PackagePath = $"{packageId}@1.0.0",
            Tfm = "net11.0",
            TypeName = typeName,
            IncludeAll = true,
        };
        var loadOptions = new WorkspaceContextLoadOptions
        {
            HttpClient = BrowserPackageWorkspace.NetworkClient,
            SourceAuthorization = BrowserPackageWorkspace.PackageSourceAuthorization,
            PackageStore = BrowserPackageWorkspace.SessionPackageStore,
            UseVersionCache = false,
            IncludePackageRootBindings = true,
        };
        var cli = await CliExactTypeInspection.ExecuteAsync(
            cliOptions,
            loadOptions,
            TestContext.Current.CancellationToken);
        var cliType = Assert.IsType<ExactTypeInspectionResult.Available>(cli.Content);
        Assert.NotEqual(cliType.Definition, available.Definition);
        Assert.Equal(cliType.Candidate.Definition, available.Candidate.Definition);
        Assert.Equal(cliType.Candidate.Address, available.Candidate.Address);
        Assert.Equal(cliType.Candidate.Supplier, available.Candidate.Supplier);
        Assert.Equal(cliType.Candidate.SupplierSource, available.Candidate.SupplierSource);
        Assert.Equal(cliType.Candidate.SupplierAssembly, available.Candidate.SupplierAssembly);
        Assert.Equal(cliType.Candidate.ForwardingHops, available.Candidate.ForwardingHops);
        Assert.Equal(cliType.Type.DefinitionName, available.Type.DefinitionName);
        Assert.Equal(cliType.Type.MetadataToken, available.Type.MetadataToken);
        Assert.Equal(cliType.Type.IsForwarded, available.Type.IsForwarded);
        Assert.Equal(cliType.Type.Kind, available.Type.Kind);
        Assert.Equal(cliType.Type.BaseType, available.Type.BaseType);
        Assert.Equal(cliType.Type.Interfaces, available.Type.Interfaces);
        Assert.Equal(cliType.Type.Attributes, available.Type.Attributes);
        Assert.Equal(cliType.Type.DerivedTypes, available.Type.DerivedTypes);
        Assert.Equal(
            cliType.Members.Members.Select(member => (member.Kind, member.Name, member.Signature, member.IsAsync)),
            available.Members.Members.Select(member => (member.Kind, member.Name, member.Signature, member.IsAsync)));
        Assert.Equal(cliType.InspectionFailures, available.InspectionFailures);
        Assert.Equal(cli.Content.Failures, envelope.Content.Failures);
        Assert.Equal(cli.Diagnostics, envelope.Diagnostics);
        Assert.Equal(cli.Share, envelope.Share);
        Assert.IsType<InspectionShare.Available>(envelope.Share);

        string transportJson =
            await DotnetInspect.Web.Interop.Metadata.MetadataExports
                .QueryTypeProjection(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    compileAssetId,
                    typeName,
                    workspaceJson);
        using (JsonDocument document = JsonDocument.Parse(transportJson))
        {
            JsonElement exact = document.RootElement
                .GetProperty("exactTypeInspection");
            JsonElement content = exact.GetProperty("content");
            Assert.Equal(
                "Available",
                content.GetProperty("kind").GetString());
            Assert.True(content.GetProperty("isComplete").GetBoolean());
            Assert.Equal(
                compileAssetId,
                content.GetProperty("request")
                    .GetProperty("compileAssetId").GetString());

            JsonElement value = content.GetProperty("available");
            JsonElement candidate = value.GetProperty("candidate");
            Assert.Equal(
                "System.Text.Json",
                candidate.GetProperty("definition")
                    .GetProperty("namespace").GetString());
            Assert.Equal(
                "JsonSerializer",
                Assert.Single(
                    candidate.GetProperty("definition")
                        .GetProperty("segments")
                        .EnumerateArray())
                    .GetString());
            Assert.Equal(
                "Package",
                candidate.GetProperty("declaration")
                    .GetProperty("kind").GetString());
            Assert.Equal(
                packageId.ToLowerInvariant(),
                candidate.GetProperty("supplier")
                    .GetProperty("packageId").GetString());
            Assert.Equal(
                "Package",
                candidate.GetProperty("supplierSource")
                    .GetProperty("kind").GetString());
            Assert.Equal(
                supplierAssembly,
                candidate.GetProperty("supplierAssembly")
                    .GetProperty("name").GetString());
            Assert.Equal(
                JsonValueKind.Array,
                candidate.GetProperty("forwardingHops").ValueKind);

            JsonElement selectedType = value.GetProperty("type");
            Assert.Equal(
                compileAssetId,
                selectedType.GetProperty("surface")
                    .GetProperty("assemblyId").GetString());
            Assert.NotEmpty(
                selectedType.GetProperty("surface")
                    .GetProperty("api").EnumerateArray());
            Assert.Equal(
                JsonValueKind.Array,
                value.GetProperty("memberKindFacets").ValueKind);
            Assert.Equal(
                JsonValueKind.Array,
                value.GetProperty("inspectionFailures").ValueKind);
            Assert.Equal(
                "Available",
                exact.GetProperty("share")
                    .GetProperty("kind").GetString());
            Assert.StartsWith(
                "https://dotnet-inspect.net/?w=",
                exact.GetProperty("share")
                    .GetProperty("fullUrl").GetString(),
                StringComparison.Ordinal);
            Assert.Equal(
                JsonValueKind.Array,
                exact.GetProperty("diagnostics").ValueKind);
        }
        Assert.DoesNotContain(
            AppContext.BaseDirectory,
            transportJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "sourceAssemblyPath",
            transportJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "sourceFilePath",
            transportJson,
            StringComparison.Ordinal);

        const string missingType = "System.Text.Json.Missing";
        var browserMiss = await DotnetInspect.Web.Interop.Metadata.BrowserExactTypeInspection.ExecuteAsync(
            packageId, "1.0.0", "net11.0", missingType, supplierAssembly,
            cancellationToken: TestContext.Current.CancellationToken);
        var cliMiss = await CliExactTypeInspection.ExecuteAsync(
            cliOptions with { TypeName = missingType },
            loadOptions,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            Assert.IsType<ExactTypeInspectionResult.NotFound>(cliMiss.Content).Suggestions,
            Assert.IsType<ExactTypeInspectionResult.NotFound>(browserMiss.Content).Suggestions);
        Assert.Equal(cliMiss.Diagnostics, browserMiss.Diagnostics);
        Assert.Equal(
            Assert.IsType<InspectionShare.NonProjectable>(cliMiss.Share).Path,
            Assert.IsType<InspectionShare.NonProjectable>(browserMiss.Share).Path);
        Assert.Equal(
            Assert.IsType<InspectionShare.NonProjectable>(cliMiss.Share).Reason.ToString(),
            Assert.IsType<InspectionShare.NonProjectable>(browserMiss.Share).Reason.ToString());

        BrowserExactTypeInspectionEnvelope missingTransport =
            BrowserExactTypeInspectionWireProjection.Project(
                browserMiss);
        string missingJson = JsonSerializer.Serialize(
            missingTransport,
            BrowserMetadataJsonContext.Default
                .BrowserExactTypeInspectionEnvelope);
        using (JsonDocument document = JsonDocument.Parse(missingJson))
        {
            Assert.Equal(
                "NotFound",
                document.RootElement.GetProperty("content")
                    .GetProperty("kind").GetString());
            Assert.True(
                document.RootElement.GetProperty("content")
                    .GetProperty("isComplete").GetBoolean());
            Assert.Equal(
                "NonProjectable",
                document.RootElement.GetProperty("share")
                    .GetProperty("kind").GetString());
            Assert.Equal(
                "exact-type/type",
                document.RootElement.GetProperty("share")
                    .GetProperty("path").GetString());
        }

        InspectionEnvelope<ExactTypeInspectionResult> rejectedEnvelope =
            await DotnetInspect.Web.Interop.Metadata
                .BrowserExactTypeInspection.ExecuteAsync(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    "*",
                    supplierAssembly,
                    cancellationToken:
                        TestContext.Current.CancellationToken);
        string rejectedJson = JsonSerializer.Serialize(
            BrowserExactTypeInspectionWireProjection.Project(
                rejectedEnvelope),
            BrowserMetadataJsonContext.Default
                .BrowserExactTypeInspectionEnvelope);
        using (JsonDocument document = JsonDocument.Parse(rejectedJson))
        {
            JsonElement content = document.RootElement
                .GetProperty("content");
            Assert.Equal(
                "Rejected",
                content.GetProperty("kind").GetString());
            Assert.False(content.GetProperty("isComplete").GetBoolean());
            Assert.Equal(
                "InvalidRequest",
                Assert.Single(
                    content.GetProperty("failures")
                        .EnumerateArray())
                    .GetProperty("kind").GetString());
            JsonElement diagnostic = Assert.Single(
                document.RootElement.GetProperty("diagnostics")
                    .EnumerateArray());
            Assert.Equal(
                "exact-type.invalid-request",
                diagnostic.GetProperty("code").GetString());
            Assert.Equal(
                "Warning",
                diagnostic.GetProperty("severity").GetString());
        }

        InvalidOperationException missing =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => DotnetInspect.Web.Interop.Metadata.MetadataExports
                    .TypeProjectionAsync(
                        packageId,
                        "1.0.0",
                        "net11.0",
                        $"{supplierAssembly}.dll",
                        "System.Text.Json.Missing",
                        workspaceJson,
                        Resolve(RowQueryIntent.Empty)));
        Assert.Contains(
            "did not find 'System.Text.Json.Missing'",
            missing.Message,
            StringComparison.Ordinal);
    }
}
