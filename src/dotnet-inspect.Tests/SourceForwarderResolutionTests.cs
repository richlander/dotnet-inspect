using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Fixtures;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries.EmbeddedFixtures;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class SourceForwarderResolutionTests
{
    const TypeAttributes Forwarder = (TypeAttributes)0x00200000;

    public SourceForwarderResolutionTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
    }

    [Fact]
    public void ResolutionSession_FollowsLegitimateAcquiredSibling()
    {
        string directory = CreateDirectory();
        try
        {
            string facadePath = Path.Combine(directory, "Facade.dll");
            string targetPath = Path.Combine(directory, "Target.dll");
            File.WriteAllBytes(
                facadePath,
                BuildAssembly("Facade", new AssemblyReferenceIdentity(
                    "Target",
                    new Version(1, 0, 0, 0),
                    null,
                    null)));
            File.WriteAllBytes(targetPath, BuildAssembly("Target"));

            using var resolution = new TypeDefinitionResolutionSession(
                facadePath,
                isPlatformAssembly: false);
            TypeResolutionOutcome outcome = resolution.Resolve(TypeName());

            var resolved = Assert.IsType<TypeResolutionOutcome.Resolved>(outcome);
            Assert.Single(resolved.Hops);
            Assert.Equal(
                Path.GetFullPath(targetPath),
                resolved.Definition.Assembly.Assembly.Path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ResolutionSession_DoesNotTurnHostileAssemblyNameIntoPath()
    {
        string parent = CreateDirectory();
        string directory = Path.Combine(parent, "input");
        Directory.CreateDirectory(directory);
        try
        {
            string facadePath = Path.Combine(directory, "Facade.dll");
            File.WriteAllBytes(
                facadePath,
                BuildAssembly("Facade", new AssemblyReferenceIdentity(
                    "../payload",
                    new Version(1, 0, 0, 0),
                    null,
                    null)));
            File.WriteAllBytes(
                Path.Combine(parent, "payload.dll"),
                BuildAssembly("../payload", definesType: true));

            using var resolution = new TypeDefinitionResolutionSession(
                facadePath,
                isPlatformAssembly: false);
            TypeResolutionOutcome outcome = resolution.Resolve(TypeName());

            Assert.IsType<TypeResolutionOutcome.UnboundBinding>(outcome);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void ResolutionSession_PreservesUnavailableConstraintDependency()
    {
        string directory = CreateDirectory();
        try
        {
            string targetPath = Path.Combine(directory, "Target.dll");
            File.WriteAllBytes(
                targetPath,
                BuildConstrainedAssembly(
                    "Target",
                    "Dependency",
                    "Type`1"));
            File.WriteAllText(
                Path.Combine(directory, "Dependency.dll"),
                "not a managed assembly");

            using var resolution =
                new TypeDefinitionResolutionSession(
                    targetPath,
                    isPlatformAssembly: false);
            ApiSurface surface = Assert.IsType<ApiSurface>(
                resolution.ExtractApiSurface());

            ApiSurfaceInspectionFailure failure =
                Assert.Single(surface.InspectionFailures);
            Assert.Contains(
                "was unavailable: 'CandidateUnavailable'",
                failure.Detail,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "could not be bound",
                failure.Detail,
                StringComparison.Ordinal);
            Assert.Equal(
                "Dependency",
                Assert.IsType<AssemblyReferenceIdentity>(
                    failure.DependencyAssembly)
                    .Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ResolutionSession_RejectsSourceChangedAfterInventory()
    {
        string directory = CreateDirectory();
        try
        {
            string rootPath = Path.Combine(
                directory,
                "Root.dll");
            File.WriteAllBytes(
                rootPath,
                BuildAssembly("Root"));
            byte[] inventoried =
                BuildAssembly(
                    "Changing",
                    definesType: true,
                    typeName: "First");
            byte[] changed =
                BuildAssembly(
                    "Changing",
                    definesType: true,
                    typeName: "Second");
            int opens = 0;
            var source = ResolvedAssemblyReference.Create(
                ReadIdentity(inventoried),
                path: null,
                openRead: () => new MemoryStream(
                    Interlocked.Increment(ref opens) == 1
                        ? inventoried
                        : changed,
                    writable: false),
                AssemblyResolutionProvenance.Local(
                    nameof(
                        ResolutionSession_RejectsSourceChangedAfterInventory)));

            using var resolution =
                new TypeDefinitionResolutionSession(
                    rootPath,
                    isPlatformAssembly: false);
            ApiSurface? surface =
                resolution.ExtractApiSurface(source);

            Assert.Null(surface);
            Assert.Equal(2, opens);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ApiServices_OpensResolvedDescriptorForForwardedType()
    {
        string directory = CreateDirectory();
        try
        {
            string facadePath = Path.Combine(directory, "Facade.dll");
            string targetPath = Path.Combine(directory, "Target.dll");
            File.WriteAllBytes(
                facadePath,
                BuildAssembly("Facade", new AssemblyReferenceIdentity(
                    "Target",
                    new Version(1, 0, 0, 0),
                    null,
                    null)));
            File.WriteAllBytes(targetPath, BuildAssembly("Target"));
            ApiSurface api =
                AssemblyReader.ExtractApiSurface(facadePath)!;

            ApiServices.ResolveForwardedTypes(
                api,
                facadePath,
                new VerboseLogger(enabled: false),
                includeAll: false);

            ApiType type = Assert.Single(api.Types);
            Assert.True(type.IsForwarded);
            Assert.Equal(Path.GetFullPath(targetPath), type.SourceAssemblyPath);
            Assert.NotNull(type.DefinitionName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ApiServices_RetainsSelectedForwarderDescriptor()
    {
        string directory = CreateDirectory();
        try
        {
            string facadePath = Path.Combine(directory, "Facade.dll");
            string sourceAssemblyPath =
                typeof(SourceForwarderResolutionTests).Assembly.Location;
            string targetPath = Path.Combine(
                directory,
                Path.GetFileName(sourceAssemblyPath));
            File.Copy(sourceAssemblyPath, targetPath);
            AssemblyName targetName =
                AssemblyName.GetAssemblyName(targetPath);
            File.WriteAllBytes(
                facadePath,
                BuildAssembly(
                    "Facade",
                    new AssemblyReferenceIdentity(
                        targetName.Name!,
                        targetName.Version,
                        targetName.CultureName,
                        null),
                    typeNamespace:
                        typeof(SourceForwarderResolutionTests)
                            .Namespace!,
                    typeName:
                        nameof(SourceForwarderResolutionTests)));
            ApiSurface api =
                AssemblyReader.ExtractApiSurface(facadePath)!;
            ApiSurface targetApi =
                AssemblyReader.ExtractApiSurface(targetPath)!;
            ApiType targetType = Assert.Single(
                targetApi.Types,
                static type =>
                    type.FullName
                    == "DotnetInspector.Tests.SourceForwarderResolutionTests");
            ResolvedAssemblyReference targetAssembly =
                ResolvedAssemblyReference.CreateFromPath(
                    targetPath,
                    AssemblyResolutionProvenance.Package(
                        "Supplier.Symbols",
                        "2.0.0",
                        "net10.0",
                        rid: null));
            var sourceAssemblies =
                new Dictionary<
                    ApiType,
                    ResolvedAssemblyReference>(
                    ReferenceEqualityComparer.Instance);

            int copied = ApiServices.MergeForwardedTypes(
                api,
                targetApi,
                new HashSet<MetadataTypeDefinitionName>
                {
                    targetType.DefinitionName!,
                },
                targetAssembly,
                sourceAssemblies: sourceAssemblies);

            Assert.Equal(1, copied);
            ApiType selectedType = Assert.Single(api.Types);
            var loaded = new ApiServices.LoadedApiSurface(
                api,
                facadePath,
                targetPath,
                sourceAssemblies);
            Assert.Same(
                targetAssembly,
                loaded.GetSourceAssembly(selectedType));
            var package =
                Assert.IsType<
                    AssemblyResolutionProvenance.PackageAsset>(
                        loaded.GetSourceAssembly(selectedType)
                            .Provenance);
            Assert.Equal("Supplier.Symbols", package.PackageId);
            Assert.Equal("2.0.0", package.PackageVersion);

            using var source = SourceLinkService.Open(
                loaded.GetSourceAssembly(selectedType));
            Assert.True(source.Context.NeedsPdb);
            string pdbPath =
                Path.ChangeExtension(sourceAssemblyPath, ".pdb");
            Assert.True(
                File.Exists(pdbPath),
                $"Expected test PDB at {pdbPath}");
            var handler = new SymbolPackageHandler(
                BuildSnupkg(
                    source.Context.PdbId!.PdbFileName,
                    File.ReadAllBytes(pdbPath)));
            using var client = new HttpClient(handler);

            await PdbAcquisitionService.AcquireAsync(
                source.Context,
                loaded.GetSourceAssembly(selectedType),
                client,
                new InMemoryPdbStore(),
                new UniformPackageSourceAuthorization(
                    [NuGetFetch.PackageSource.NuGetOrg]),
                log: null,
                cancellationToken:
                    TestContext.Current.CancellationToken,
                fallbackPackageName: "Root.Symbols",
                fallbackPackageVersion: "1.0.0");

            Assert.True(source.HasPdb);
            Uri request = Assert.Single(
                handler.RequestUris,
                static uri => uri.AbsolutePath.EndsWith(
                    ".snupkg",
                    StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                "supplier.symbols.2.0.0.snupkg",
                request.AbsolutePath,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "root.symbols",
                request.AbsolutePath,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ApiServices_RetainsRootPackageDescriptor()
    {
        string directory = CreateDirectory();
        try
        {
            string rootPath = Path.Combine(directory, "Root.dll");
            File.WriteAllBytes(
                rootPath,
                BuildAssembly("Root"));

            ApiServices.LoadedApiSurface loaded =
                Assert.IsType<ApiServices.LoadedApiSurface>(
                    ApiServices.LoadFullApi(
                        rootPath,
                        runtimeAssemblyPath: null,
                        packagePath: null,
                        packageName: "Root.Symbols",
                        apiSource: SourceKind.NuGet,
                        apiVersion: "1.0.0",
                        selectedTfm: "net10.0",
                        new VerboseLogger(enabled: false),
                        new ApiOptions()));

            ApiType selectedType = Assert.Single(
                loaded.Api.Types);
            ResolvedAssemblyReference sourceAssembly =
                loaded.GetSourceAssembly(selectedType);
            Assert.Equal(
                Path.GetFullPath(rootPath),
                sourceAssembly.Path);
            var package =
                Assert.IsType<
                    AssemblyResolutionProvenance.PackageAsset>(
                        sourceAssembly.Provenance);
            Assert.Equal("Root.Symbols", package.PackageId);
            Assert.Equal("1.0.0", package.PackageVersion);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TypeSourceFiles_ForwardedPlatformDescriptorSelectsPlatformPolicy()
    {
        var fixture = CreateTypeSourceFixture(
            AssemblyResolutionProvenance.Platform(
                "netstandard",
                "10.0.0",
                "test"),
            isForwarded: true);
        try
        {
            var handler = new RecordingNotFoundHandler();
            using var client = new HttpClient(handler);
            var options = new TypeOptions
            {
                TypeName = fixture.Type.FullName,
                IncludeSections = [SectionNames.SourceFiles],
                Verbose = true,
            };
            var source = new ApiSourceResult(
                fixture.AssemblyPath,
                RuntimeAssemblyPath: null,
                PackageName: "Microsoft.Root.Symbols",
                PackageVersion: "1.0.0",
                ResolvedPackagePath:
                    "Microsoft.Root.Symbols@1.0.0",
                PackageExtractPath: null,
                ApiSource: SourceKind.NuGet,
                ApiVersion: "1.0.0",
                PlatformFramework: null,
                SelectedTfm: "net10.0",
                ProjectAssetsPath: null,
                TempDir: null,
                TypeName: fixture.Type.FullName,
                PackageReplaySourceUrls: null,
                PackageReplayUsesOriginalSources: false,
                Context: new CommandContext(
                    verbose: true,
                    client));

            var (exit, _, error) =
                await ConsoleCapture.RunAsync(
                    () => TypeCommand.ExecuteResolvedAsync(
                        options,
                        source,
                        fixture.Loaded));

            Assert.Equal(0, exit);
            Assert.Contains(
                "Platform library, trying MSDL symbol server",
                error,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Microsoft package detected",
                error,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                handler.RequestUris,
                static uri => uri.AbsolutePath.EndsWith(
                    ".snupkg",
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(
                fixture.Directory,
                recursive: true);
        }
    }

    [Fact]
    public async Task TypeSourceFiles_ProjectDescriptorUsesPackageFallback()
    {
        var fixture = CreateTypeSourceFixture(
            AssemblyResolutionProvenance.Project(
                "test.csproj",
                "net10.0",
                rid: null),
            isForwarded: false);
        try
        {
            string packageName =
                $"Supplier.Symbols.{Guid.NewGuid():N}";
            var handler = new RecordingNotFoundHandler();
            using var client = new HttpClient(handler);
            var options = new TypeOptions
            {
                TypeName = fixture.Type.FullName,
                IncludeSections = [SectionNames.SourceFiles],
                ProjectPath = "test.csproj",
                Verbose = true,
            };
            var source = new ApiSourceResult(
                fixture.AssemblyPath,
                RuntimeAssemblyPath: null,
                PackageName: packageName,
                PackageVersion: "2.0.0",
                ResolvedPackagePath: null,
                PackageExtractPath: null,
                ApiSource: SourceKind.Project,
                ApiVersion: "2.0.0",
                PlatformFramework: null,
                SelectedTfm: "net10.0",
                ProjectAssetsPath: null,
                TempDir: null,
                TypeName: fixture.Type.FullName,
                PackageReplaySourceUrls: null,
                PackageReplayUsesOriginalSources: false,
                Context: new CommandContext(
                    verbose: true,
                    client));

            var (exit, _, _) =
                await ConsoleCapture.RunAsync(
                    () => TypeCommand.ExecuteResolvedAsync(
                        options,
                        source,
                        fixture.Loaded));

            Assert.Equal(0, exit);
            Uri[] packageRequests =
            [
                .. handler.RequestUris.Where(
                    uri => uri.AbsolutePath.Contains(
                    packageName,
                    StringComparison.OrdinalIgnoreCase)
                    && uri.AbsolutePath.EndsWith(
                        ".snupkg",
                        StringComparison.OrdinalIgnoreCase)),
            ];
            Assert.NotEmpty(packageRequests);
            Assert.All(
                packageRequests,
                request => Assert.Contains(
                    $"{packageName}.2.0.0.snupkg",
                    request.AbsolutePath,
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(
                fixture.Directory,
                recursive: true);
        }
    }

    [Fact]
    public void ApiServices_PropagatesForwardedTargetInspectionFailures()
    {
        string directory = CreateDirectory();
        try
        {
            string facadePath = Path.Combine(directory, "Facade.dll");
            string targetPath = Path.Combine(directory, "Target.dll");
            File.WriteAllBytes(
                facadePath,
                BuildAssembly("Facade", new AssemblyReferenceIdentity(
                    "Target",
                    new Version(1, 0, 0, 0),
                    null,
                    null),
                    typeName: "Type`1"));
            File.WriteAllBytes(
                targetPath,
                BuildTargetWithMissingConstraint());
            ApiSurface api =
                AssemblyReader.ExtractApiSurface(facadePath)!;

            ApiServices.ResolveForwardedTypes(
                api,
                facadePath,
                new VerboseLogger(enabled: false),
                includeAll: false);

            Assert.True(Assert.Single(api.Types).IsForwarded);
            ApiSurfaceInspectionFailure failure =
                Assert.Single(api.InspectionFailures);
            Assert.Contains(
                "N.ForwardedBase",
                failure.Detail,
                StringComparison.Ordinal);
            Assert.Single(
                api.ConstraintResolutionFailuresBySubject);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ApiServices_ClassifiesConstraintsOnForwardedType()
    {
        string directory = CreateDirectory();
        try
        {
            string targetSource =
                typeof(CrossAssemblyConstraintRestatementFixture)
                    .Assembly.Location;
            string dependencySource =
                typeof(DotnetInspector.Fixtures.ExternalConstraintClass)
                    .Assembly.Location;
            string targetPath = Path.Combine(
                directory,
                Path.GetFileName(targetSource));
            string dependencyPath = Path.Combine(
                directory,
                Path.GetFileName(dependencySource));
            File.Copy(targetSource, targetPath);
            File.Copy(dependencySource, dependencyPath);

            AssemblyName targetName =
                typeof(CrossAssemblyConstraintRestatementFixture)
                    .Assembly.GetName();
            string facadePath = Path.Combine(directory, "Facade.dll");
            File.WriteAllBytes(
                facadePath,
                BuildAssembly(
                    "Facade",
                    new AssemblyReferenceIdentity(
                        targetName.Name!,
                        targetName.Version,
                        targetName.CultureName,
                        null),
                    typeNamespace:
                        typeof(CrossAssemblyConstraintRestatementFixture)
                            .Namespace!,
                    typeName:
                        nameof(
                            CrossAssemblyConstraintRestatementFixture)));
            ApiSurface api =
                AssemblyReader.ExtractApiSurface(facadePath)!;

            ApiServices.ResolveForwardedTypes(
                api,
                facadePath,
                new VerboseLogger(enabled: false),
                includeAll: false);

            ApiType type = Assert.Single(api.Types);
            Assert.True(type.IsForwarded);
            AssertKind(
                nameof(
                    CrossAssemblyConstraintRestatementFixture
                        .ClassConstraint),
                TypeParameterTypeKind.ReferenceType);
            AssertKind(
                nameof(
                    CrossAssemblyConstraintRestatementFixture
                        .InterfaceConstraint),
                TypeParameterTypeKind.NeitherReferenceNorValue);

            void AssertKind(
                string methodName,
                TypeParameterTypeKind expected)
            {
                ApiMember member = Assert.Single(
                    type.Members,
                    candidate => candidate.Name == methodName);
                Assert.Equal(
                    expected,
                    Assert.Single(
                        member.SignatureModel!.TypeParameters)
                        .TypeKind);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ApiServices_PreservesForwardedConstraintFailures()
    {
        string directory = CreateDirectory();
        try
        {
            string facadePath = Path.Combine(directory, "Facade.dll");
            string targetPath = Path.Combine(directory, "Target.dll");
            File.WriteAllBytes(
                facadePath,
                BuildAssembly(
                    "Facade",
                    new AssemblyReferenceIdentity(
                        "Target",
                        new Version(1, 0, 0, 0),
                        null,
                        null),
                    typeName: "Type`1"));
            File.WriteAllBytes(
                targetPath,
                BuildConstrainedAssembly(
                    "Target",
                    "Dependency",
                    "Type`1"));
            ApiSurface api =
                AssemblyReader.ExtractApiSurface(facadePath)!;
            ResolvedAssemblyReference target =
                ResolvedAssemblyReference.CreateFromPath(
                    targetPath,
                    AssemblyResolutionProvenance.Local("test"));
            byte[] dependencyImage =
                BuildAssembly(
                    "Dependency",
                    definesType: true,
                    typeName: "Constraint");
            ResolvedAssemblyReference dependency =
                ResolvedAssemblyReference.Create(
                    ReadIdentity(dependencyImage),
                    path: null,
                    openRead: static () =>
                        throw new IOException("test open failure"),
                    AssemblyResolutionProvenance.Local("test"));
            using var catalog = new TypeResolutionCatalog();
            ResolutionAwareApiSurfaceOutcome outcome =
                catalog.ExtractApiSurface(
                    target,
                    new MappingPolicy(dependency));
            ApiSurface targetApi =
                Assert.IsType<
                    ResolutionAwareApiSurfaceOutcome.Read>(
                        outcome)
                    .Surface;
            ApiSurfaceInspectionFailure targetFailure =
                Assert.Single(targetApi.InspectionFailures);
            targetApi.InspectionFailures.Add(targetFailure);
            MetadataTypeDefinitionName forwardedType =
                Assert.IsType<MetadataTypeDefinitionName>(
                    Assert.Single(targetApi.Types)
                        .DefinitionName);

            int copied = ApiServices.MergeForwardedTypes(
                api,
                targetApi,
                new HashSet<MetadataTypeDefinitionName>
                {
                    forwardedType,
                },
                target);

            Assert.Equal(1, copied);
            Assert.True(Assert.Single(api.Types).IsForwarded);
            ApiSurfaceInspectionFailure failure =
                Assert.Single(api.InspectionFailures);
            Assert.Equal(
                ApiSurfaceInspectionFailure
                    .GenericParameterConstraintResolutionOperation,
                failure.Operation);
            using JsonDocument json = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    api,
                    ApiJsonContext.Default.ApiSurface));
            Assert.Equal(
                "Target",
                json.RootElement
                    .GetProperty("inspection_failures")[0]
                    .GetProperty("subject_assembly")
                    .GetProperty("name")
                    .GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ApiServices_PreservesForwardedExtractionFailuresOnlyForCopiedTypes()
    {
        string directory = CreateDirectory();
        try
        {
            string targetPath = Path.Combine(directory, "Target.dll");
            File.WriteAllBytes(
                targetPath,
                BuildAssembly(
                    "Target",
                    definesType: true));
            ApiSurface targetApi =
                AssemblyReader.ExtractApiSurface(targetPath)!;
            ApiType targetType = Assert.Single(targetApi.Types);
            int targetTypeToken =
                Assert.IsType<int>(targetType.MetadataToken);
            targetApi.InspectionFailures.Add(
                new ApiSurfaceInspectionFailure(
                    "type row",
                    targetTypeToken,
                    MetadataTypeNameFailureMechanism.Metadata,
                    "Malformed",
                    "The copied type is incomplete."));
            targetApi.InspectionFailures.Add(
                new ApiSurfaceInspectionFailure(
                    "type row",
                    0x02000003,
                    MetadataTypeNameFailureMechanism.Metadata,
                    "Malformed",
                    "An unrelated type is incomplete."));
            var api = new ApiSurface();
            ResolvedAssemblyReference target =
                ResolvedAssemblyReference.CreateFromPath(
                    targetPath,
                    AssemblyResolutionProvenance.Local("test"));

            int copied = ApiServices.MergeForwardedTypes(
                api,
                targetApi,
                new HashSet<MetadataTypeDefinitionName>
                {
                    Assert.IsType<MetadataTypeDefinitionName>(
                        targetType.DefinitionName),
                },
                target);

            Assert.Equal(1, copied);
            ApiSurfaceInspectionFailure failure =
                Assert.Single(api.InspectionFailures);
            Assert.Equal(targetTypeToken, failure.SubjectToken);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ApiServices_PreservesFailureWhenForwardedTypeWasRejected()
    {
        byte[] targetImage =
            BuildAssembly(
                "Target",
                definesType: true);
        ResolvedAssemblyReference target =
            ResolvedAssemblyReference.Create(
                ReadIdentity(targetImage),
                path: null,
                openRead: () => new MemoryStream(targetImage),
                AssemblyResolutionProvenance.Local("test"));
        var targetApi = new ApiSurface();
        targetApi.InspectionFailures.Add(
            new ApiSurfaceInspectionFailure(
                "type row",
                0x1B000001,
                MetadataTypeNameFailureMechanism.Metadata,
                "Malformed",
                "The requested forwarded type was rejected.")
            {
                OwningTypeToken = 0x02000002,
            });
        var api = new ApiSurface();

        int copied = ApiServices.MergeForwardedTypes(
            api,
            targetApi,
            new HashSet<MetadataTypeDefinitionName>
            {
                TypeName(),
            },
            target,
            new HashSet<int>
            {
                0x02000002,
            });

        Assert.Equal(0, copied);
        Assert.Empty(api.Types);
        Assert.Single(api.InspectionFailures);
    }

    [Fact]
    public void ApiServices_ScopesTargetWideFailureToRequestedForwardedType()
    {
        string directory = CreateDirectory();
        try
        {
            string targetPath =
                Path.Combine(directory, "Target.dll");
            File.WriteAllBytes(
                targetPath,
                BuildAssembly(
                    "Target",
                    definesType: true));
            ApiSurface targetApi =
                AssemblyReader.ExtractApiSurface(targetPath)!;
            MetadataTypeDefinitionName forwardedType =
                Assert.IsType<MetadataTypeDefinitionName>(
                    Assert.Single(targetApi.Types)
                        .DefinitionName);
            targetApi.InspectionFailures.Add(
                new ApiSurfaceInspectionFailure(
                    "inventory assembly adjacency",
                    0,
                    MetadataTypeNameFailureMechanism.Metadata,
                    "InvalidImage",
                    "The target-wide inventory is incomplete."));
            var api = new ApiSurface();
            ResolvedAssemblyReference target =
                ResolvedAssemblyReference.CreateFromPath(
                    targetPath,
                    AssemblyResolutionProvenance.Local(
                        "test"));

            int copied = ApiServices.MergeForwardedTypes(
                api,
                targetApi,
                new HashSet<MetadataTypeDefinitionName>
                {
                    forwardedType,
                },
                target);

            Assert.Equal(1, copied);
            ApiSurfaceInspectionFailure failure =
                Assert.Single(api.InspectionFailures);
            Assert.Equal(
                [forwardedType],
                failure.AffectedTypeDefinitions);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ResolutionSession_PreservesTargetSurfaceRejectionCause()
    {
        string directory = CreateDirectory();
        try
        {
            string facadePath =
                Path.Combine(directory, "Facade.dll");
            File.WriteAllBytes(
                facadePath,
                BuildAssembly("Facade"));
            byte[] targetImage =
                BuildAssembly(
                    "Target",
                    definesType: true);
            ResolvedAssemblyReference target =
                ResolvedAssemblyReference.Create(
                    ReadIdentity(targetImage),
                    path: null,
                    openRead: static () =>
                        throw new IOException(
                            "test target open failure"),
                    AssemblyResolutionProvenance.Local(
                        "test"));
            using var resolution =
                new TypeDefinitionResolutionSession(
                    facadePath,
                    isPlatformAssembly: false);

            ApiSurface? surface =
                resolution.ExtractApiSurface(
                    target,
                    includeAll: false,
                    typesOnly: false,
                    out TypeDefinitionApiSurfaceFailure?
                        failure);

            Assert.Null(surface);
            Assert.NotNull(failure);
            Assert.Equal("Unreadable", failure.Kind);
            Assert.Equal(
                "The selected image could not be read.",
                failure.Detail);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ApiServices_PreservesMalformedForwardedTypeFailureEndToEnd()
    {
        string directory = CreateDirectory();
        try
        {
            string facadePath =
                Path.Combine(directory, "Facade.dll");
            string targetPath =
                Path.Combine(directory, "Target.dll");
            File.WriteAllBytes(
                facadePath,
                BuildAssembly(
                    "Facade",
                    new AssemblyReferenceIdentity(
                        "Target",
                        new Version(1, 0, 0, 0),
                        null,
                        null)));
            File.WriteAllBytes(
                targetPath,
                BuildTargetWithMalformedType(
                    requestedTypeIsMalformed: true));
            ApiSurface api =
                AssemblyReader.ExtractApiSurface(facadePath)!;

            ApiServices.ResolveForwardedTypes(
                api,
                facadePath,
                new VerboseLogger(enabled: false),
                includeAll: false);

            Assert.Empty(api.Types);
            ApiSurfaceInspectionFailure failure =
                Assert.Single(api.InspectionFailures);
            Assert.Equal(0x1B000001, failure.SubjectToken);
            Assert.Equal(0x02000002, failure.OwningTypeToken);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ApiServices_ExcludesMalformedUnrelatedForwardedTargetType()
    {
        string directory = CreateDirectory();
        try
        {
            string facadePath =
                Path.Combine(directory, "Facade.dll");
            string targetPath =
                Path.Combine(directory, "Target.dll");
            File.WriteAllBytes(
                facadePath,
                BuildAssembly(
                    "Facade",
                    new AssemblyReferenceIdentity(
                        "Target",
                        new Version(1, 0, 0, 0),
                        null,
                        null)));
            File.WriteAllBytes(
                targetPath,
                BuildTargetWithMalformedType(
                    requestedTypeIsMalformed: false));
            ApiSurface api =
                AssemblyReader.ExtractApiSurface(facadePath)!;

            ApiServices.ResolveForwardedTypes(
                api,
                facadePath,
                new VerboseLogger(enabled: false),
                includeAll: false);

            Assert.Single(api.Types);
            Assert.Empty(api.InspectionFailures);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TypeCommand_PreservesOnlyFailuresForSelectedForwardedTypes()
    {
        const string TargetPath = "/platform/Target.dll";
        var source = new ApiSurface();
        source.InspectionFailures.Add(
            Failure(
                "selected",
                0x1B000001,
                TargetPath,
                owningTypeToken: 0x02000002));
        source.InspectionFailures.Add(
            Failure(
                "unrelated",
                0x1B000002,
                TargetPath,
                owningTypeToken: 0x02000003));
        source.InspectionFailures.Add(
            Failure(
                "target-wide",
                0,
                TargetPath));
        source.InspectionFailures.Add(
            Failure(
                "other-target-wide",
                0,
                "/platform/Other.dll"));
        source.InspectionFailures.Add(
            Failure(
                "failed-target",
                0,
                "/platform/Failed.dll",
                affectedTypeDefinitions:
                    [TypeName()]));
        source.InspectionFailures.Add(
            Failure(
                "unrelated-failed-target",
                0,
                "/platform/Unrelated.dll",
                affectedTypeDefinitions:
                    [
                        Assert.IsType<
                            MetadataTypeDefinitionNameResult.Valid>(
                                MetadataTypeDefinitionName.Create(
                                    "N",
                                    ["Other"]))
                            .Name,
                    ]));
        var selectedType = new ApiType
        {
            Namespace = "N",
            Name = "Type",
            MetadataToken = 0x02000002,
            SourceAssemblyPath = TargetPath,
        };
        var destination = new ApiSurface();

        TypeCommand.MergeSelectedInspectionFailures(
            destination,
            source,
            [selectedType],
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                "N.Type",
            },
            "/platform/Facade.dll");

        Assert.Equal(
            ["selected", "target-wide", "failed-target"],
            destination.InspectionFailures
                .Select(failure => failure.Detail)
                .ToArray());
    }

    [Fact]
    public void TypeCommand_PreservesFailureForRejectedSelectedForwardedType()
    {
        var source = new ApiSurface();
        source.InspectionFailures.Add(
            Failure(
                "selected",
                0x1B000001,
                "/platform/Target.dll",
                owningTypeToken: 0x02000002,
                owningTypeDefinition: TypeName()));
        source.InspectionFailures.Add(
            Failure(
                "unrelated",
                0x1B000002,
                "/platform/Target.dll",
                owningTypeToken: 0x02000003,
                owningTypeDefinition:
                    Assert.IsType<
                        MetadataTypeDefinitionNameResult.Valid>(
                            MetadataTypeDefinitionName.Create(
                                "N",
                                ["Other"]))
                        .Name));
        var destination = new ApiSurface();

        TypeCommand.MergeSelectedInspectionFailures(
            destination,
            source,
            [],
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                "N.Type",
            },
            "/platform/Facade.dll");

        Assert.Equal(
            "selected",
            Assert.Single(
                destination.InspectionFailures)
                .Detail);
        Assert.True(
            TypeCommand.HasPlatformPrefixBrowseResult(
                destination));
    }

    [Fact]
    public void ApiCommand_TypeFilterScopesOwnedForwardedFailures()
    {
        MetadataTypeDefinitionName selectedName =
            TypeName();
        MetadataTypeDefinitionName unrelatedName =
            OtherTypeName();
        var api = new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Namespace = "N",
                    Name = "Type",
                    DefinitionName = selectedName,
                    MetadataToken = 0x02000002,
                    SourceAssemblyPath =
                        "/platform/Target.dll",
                },
                new ApiType
                {
                    Namespace = "N",
                    Name = "Other",
                    DefinitionName = unrelatedName,
                    MetadataToken = 0x02000003,
                    SourceAssemblyPath =
                        "/platform/Target.dll",
                },
            ],
        };
        api.InspectionFailures.Add(
            Failure(
                "selected",
                0x1B000001,
                "/platform/Target.dll",
                owningTypeToken: 0x02000002,
                owningTypeDefinition: selectedName));
        api.InspectionFailures.Add(
            Failure(
                "unrelated",
                0x1B000002,
                "/platform/Target.dll",
                owningTypeToken: 0x02000003,
                owningTypeDefinition: unrelatedName));
        api.InspectionFailures.Add(
            Failure(
                "unrelated-target-wide",
                0,
                "/platform/Target.dll",
                affectedTypeDefinitions:
                    [unrelatedName]));
        api.InspectionFailures.Add(
            Failure(
                "unowned-target-wide",
                0,
                "/platform/Target.dll"));

        ApiCommand.ApplySurfaceFilters(
            api,
            new ApiOptions(),
            "N.Type");

        Assert.Equal(
            ["selected", "unowned-target-wide"],
            api.InspectionFailures
                .Select(failure => failure.Detail)
                .ToArray());
    }

    [Fact]
    public void ApiServices_UnresolvedForwarderIsVisibleWithoutOpeningTraversalTarget()
    {
        string parent = CreateDirectory();
        string directory = Path.Combine(parent, "input");
        Directory.CreateDirectory(directory);
        try
        {
            string facadePath = Path.Combine(directory, "Facade.dll");
            File.WriteAllBytes(
                facadePath,
                BuildAssembly("Facade", new AssemblyReferenceIdentity(
                    "../payload",
                    new Version(1, 0, 0, 0),
                    null,
                    null)));
            File.WriteAllBytes(
                Path.Combine(parent, "payload.dll"),
                BuildAssembly("../payload", definesType: true));
            ApiSurface api =
                AssemblyReader.ExtractApiSurface(facadePath)!;

            ApiServices.ResolveForwardedTypes(
                api,
                facadePath,
                new VerboseLogger(enabled: false),
                includeAll: false);

            Assert.Empty(api.Types);
            ApiSurfaceInspectionFailure failure =
                Assert.Single(api.InspectionFailures);
            Assert.Equal(
                "resolve forwarded type",
                failure.Operation);
            Assert.Equal(
                "Facade",
                Assert.IsType<AssemblyReferenceIdentity>(
                    failure.SubjectAssembly).Name);
            Assert.Equal(
                "../payload",
                Assert.IsType<AssemblyReferenceIdentity>(
                    failure.DependencyAssembly).Name);
            Assert.Equal(
                [TypeName()],
                failure.AffectedTypeDefinitions);
            Assert.Equal(
                Path.GetFullPath(facadePath),
                failure.SourceAssemblyPath);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void ApiServices_SummaryDoesNotOpenTraversalTarget()
    {
        string parent = CreateDirectory();
        string directory = Path.Combine(parent, "input");
        Directory.CreateDirectory(directory);
        try
        {
            string facadePath = Path.Combine(directory, "Facade.dll");
            File.WriteAllBytes(
                facadePath,
                BuildAssembly("Facade", new AssemblyReferenceIdentity(
                    "../payload",
                    new Version(1, 0, 0, 0),
                    null,
                    null)));
            File.WriteAllBytes(
                Path.Combine(parent, "payload.dll"),
                BuildAssembly("../payload", definesType: true));
            ApiSurface api =
                AssemblyReader.ExtractApiSurface(facadePath)!;

            ApiServices.ResolveForwardedTypes(
                api,
                facadePath,
                new VerboseLogger(enabled: false),
                includeAll: false,
                summaryOnly: true);

            Assert.Empty(api.Types);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void PlatformSummary_RejectsFilenameMatchWithIncompatibleManifestIdentity()
    {
        string directory = CreateDirectory();
        try
        {
            string facadePath = Path.Combine(directory, "Facade.dll");
            string targetPath = Path.Combine(directory, "Target.dll");
            File.WriteAllBytes(
                facadePath,
                BuildAssembly(
                    "Facade",
                    new AssemblyReferenceIdentity(
                        "Target",
                        new Version(1, 0, 0, 0),
                        null,
                        null)));
            File.WriteAllBytes(
                targetPath,
                BuildAssembly(
                    "DifferentTarget",
                    definesType: true));

            ApiServices.LoadedApiSurface loaded =
                Assert.IsType<ApiServices.LoadedApiSurface>(
                    ApiServices.LoadPlatformApiSummary(
                        facadePath,
                        facadePath,
                        SourceKind.Platform,
                        "1.0.0",
                        "net11.0",
                        new VerboseLogger(enabled: false)));

            Assert.Empty(loaded.Api.Types);
            Assert.Empty(loaded.SourceAssemblies);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PlatformSummary_RetainsValidatedForwardedSupplierDescriptor()
    {
        string directory = CreateDirectory();
        try
        {
            string facadePath = Path.Combine(directory, "Facade.dll");
            string targetPath = Path.Combine(directory, "Target.dll");
            File.WriteAllBytes(
                facadePath,
                BuildAssembly(
                    "Facade",
                    new AssemblyReferenceIdentity(
                        "Target",
                        new Version(1, 0, 0, 0),
                        null,
                        null)));
            File.WriteAllBytes(
                targetPath,
                BuildAssembly(
                    "Target",
                    definesType: true));

            ApiServices.LoadedApiSurface loaded =
                Assert.IsType<ApiServices.LoadedApiSurface>(
                    ApiServices.LoadPlatformApiSummary(
                        facadePath,
                        facadePath,
                        SourceKind.Platform,
                        "1.0.0",
                        "net11.0",
                        new VerboseLogger(enabled: false)));

            ApiType type = Assert.Single(loaded.Api.Types);
            Assert.True(type.IsForwarded);
            ResolvedAssemblyReference supplier =
                loaded.GetSourceAssembly(type);
            Assert.Equal(
                Path.GetFullPath(targetPath),
                supplier.Path);
            Assert.Equal("Target", supplier.Identity.Name);
            Assert.IsType<
                AssemblyResolutionProvenance.PlatformAsset>(
                    supplier.Provenance);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("N.Type", false)]
    [InlineData("N.Type", true)]
    public async Task TypeApiSelection_RejectsUnusableAssemblyIdentity(
        string? typeName,
        bool deferred)
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "BlankIdentity.dll");
            File.WriteAllBytes(path, BuildAssembly(" "));

            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => deferred
                    ? MemberCommand.ExecuteAsync(new MemberOptions
                    {
                        AssemblyPath = path,
                        TypeName = typeName,
                        RouterDeferredTypeOrMember = true,
                        JsonOutput = true,
                    })
                    : TypeCommand.ExecuteAsync(new TypeOptions
                    {
                        AssemblyPath = path,
                        TypeName = typeName,
                        JsonOutput = true,
                    }));

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(path, error);
            Assert.Contains("Could not select API assembly", error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("N.Type")]
    public async Task TypeApiSelection_RejectsUnusablePackageAssembly(string? typeName)
    {
        string directory = CreateDirectory();
        try
        {
            string content = Path.Combine(directory, "content");
            string library = Path.Combine(content, "lib", "net11.0");
            Directory.CreateDirectory(library);
            File.WriteAllBytes(
                Path.Combine(library, "BlankIdentity.dll"),
                BuildAssembly(" "));
            string package = Path.Combine(directory, "Selected.Package.2.3.4.nupkg");
            ZipFile.CreateFromDirectory(content, package);

            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(new TypeOptions
                {
                    PackagePath = package,
                    TypeName = typeName,
                    JsonOutput = true,
                }));

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("BlankIdentity.dll", error);
            Assert.Contains("Could not select API assembly", error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "N.Type")]
    [InlineData(true, null)]
    [InlineData(true, "N.Type")]
    public async Task TypeApiSelection_PreservesAssemblyAndModuleInspection(
        bool isModule,
        string? typeName)
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "Healthy.dll");
            File.WriteAllBytes(path, BuildAssembly("Healthy", isModule: isModule));
            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(new TypeOptions
                {
                    AssemblyPath = path,
                    TypeName = typeName,
                    JsonOutput = true,
                }));

            Assert.Equal(0, exit);
            Assert.Contains("N.Type", output);
            Assert.DoesNotContain("Could not select API assembly", error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(SourceKind.Library)]
    [InlineData(SourceKind.NuGet)]
    [InlineData(SourceKind.Project)]
    [InlineData(SourceKind.Platform)]
    public void TypeApiSelection_RetainsResolvedProvenance(string sourceKind)
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "Root.dll");
            File.WriteAllBytes(path, BuildAssembly("Root"));
            var source = CreateApiSource(path, sourceKind);
            var options = new TypeOptions { ProjectPath = "selected-project" };
            var loaded = Assert.IsType<ApiServices.LoadedApiSurface>(
                ApiServices.LoadTypeApi(source, options));
            var assembly = loaded.GetSourceAssembly(Assert.Single(loaded.Api.Types));

            Assert.Equal(Path.GetFullPath(path), assembly.Path);
            Assert.Equal("Root", assembly.Identity.Name);
            var expected = sourceKind switch
            {
                SourceKind.Library => AssemblyResolutionProvenance.Designated("ApiServices"),
                SourceKind.NuGet => AssemblyResolutionProvenance.Package(
                    "Selected.Package", "2.3.4", "net11.0", rid: null),
                SourceKind.Project => AssemblyResolutionProvenance.Project(
                    "selected-project", "net11.0", rid: null),
                SourceKind.Platform => AssemblyResolutionProvenance.Platform(
                    "aspnetcore", "2.3.4", "ApiServices"),
                _ => AssemblyResolutionProvenance.Local("ApiServices"),
            };
            Assert.Equal(expected, assembly.Provenance);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, SourceKind.Library)]
    [InlineData(false, SourceKind.Platform)]
    [InlineData(true, SourceKind.Platform)]
    public void TypeApiSelection_RetainsForwardedSupplier(bool summaryOnly, string sourceKind)
    {
        string directory = CreateDirectory();
        try
        {
            string facadePath = Path.Combine(directory, "Facade.dll");
            string targetPath = Path.Combine(directory, "Target.dll");
            File.WriteAllBytes(facadePath, BuildAssembly(
                "Facade", new AssemblyReferenceIdentity(
                    "Target", new Version(1, 0, 0, 0), null, null)));
            File.WriteAllBytes(targetPath, BuildAssembly("Target"));
            var loaded = Assert.IsType<ApiServices.LoadedApiSurface>(
                ApiServices.LoadTypeApi(
                    CreateApiSource(facadePath, sourceKind),
                    new TypeOptions(),
                    summaryOnly));
            var type = Assert.Single(loaded.Api.Types);
            var supplier = loaded.GetSourceAssembly(type);

            Assert.Equal(summaryOnly, loaded.IsSummary);
            Assert.True(type.IsForwarded);
            Assert.Equal(Path.GetFullPath(targetPath), supplier.Path);
            Assert.Equal("Target", supplier.Identity.Name);
            if (sourceKind == SourceKind.Library)
                Assert.IsType<AssemblyResolutionProvenance.LocalAsset>(supplier.Provenance);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("Healthy")]
    public void TypeApiSelection_CompactSummaryUsesTypedSelection(string name)
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "Input.dll");
            File.WriteAllBytes(path, BuildAssembly(name));
            var source = CreateApiSource(path, SourceKind.Platform);
            if (string.IsNullOrWhiteSpace(name))
            {
                var error = Assert.Throws<BadImageFormatException>(
                    () => ApiServices.LoadTypeApi(source, new TypeOptions(), summaryOnly: true));
                Assert.Contains(path, error.Message);
                return;
            }

            var loaded = Assert.IsType<ApiServices.LoadedApiSurface>(
                ApiServices.LoadTypeApi(source, new TypeOptions(), summaryOnly: true));
            Assert.True(loaded.IsSummary);
            var root = loaded.GetSourceAssembly(Assert.Single(loaded.Api.Types));
            Assert.Equal(
                AssemblyResolutionProvenance.Platform("aspnetcore", "2.3.4", "ApiServices"),
                root.Provenance);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TypeBodyShapesAcquisition_PreservesDesignatedCoreLibraryRows()
    {
        string path = typeof(System.Text.StringBuilder).Assembly.Location;
        var options = new TypeOptions
        {
            AssemblyPath = path,
            BodyKindQuery = new() { Kind = "ObjectCreationExpression" },
        };
        var loaded = Assert.IsType<ApiServices.LoadedApiSurface>(
            ApiServices.LoadTypeApi(CreateApiSource(path, SourceKind.Library), options));
        var type = Assert.Single(loaded.Api.Types, type => type.FullName == "System.Text.StringBuilder");
        var tokens = ApiOutputFormatter.ResolveTypeBodyShapeMethodTokens(type);
        var expected = new TypeView();
        var actual = new TypeView();

        ApiOutputFormatter.PopulateBodyShapes(expected, path, null, tokens, options);
        ApiOutputFormatter.PopulateBodyShapes(
            actual, path, null, tokens, options, loaded.GetSourceAssembly(type));

        Assert.NotNull(expected.BodyShapeRows);
        Assert.NotEmpty(expected.BodyShapeRows);
        Assert.Equal(expected.BodyShapeRows, actual.BodyShapeRows);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    public async Task TypeBodyShapesAcquisition_UsesSelectedSupplier(
        bool isForwarded, bool discover, bool project)
    {
        int opens = 0;
        byte[] image = File.ReadAllBytes(typeof(BodyShapeFixture).Assembly.Location);
        var fixture = CreateTypeSourceFixture(
            AssemblyResolutionProvenance.Local("type-body-shapes"),
            isForwarded,
            () =>
            {
                opens++;
                return new MemoryStream(image, writable: false);
            },
            typeof(BodyShapeFixture),
            includePdb: true);
        try
        {
            var handler = new RecordingNotFoundHandler();
            using var client = new HttpClient(handler);
            var source = CreateApiSource(fixture.AssemblyPath, SourceKind.Library) with
            {
                TypeName = fixture.Type.FullName,
                Context = new CommandContext(verbose: false, client),
            };
            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteResolvedAsync(
                    new TypeOptions
                    {
                        TypeName = fixture.Type.FullName,
                        MemberFilter = [nameof(BodyShapeFixture.PublicCreation)],
                        BodyKindQuery = new() { Kind = "ObjectCreationExpression" },
                        Select = [SectionNames.BodyShapes],
                        Discover = discover ? [SectionNames.BodyShapes] : null,
                        Columns = project ? ["Kind"] : null,
                        DocsExplicitlySet = true,
                        TipLevel = TipLevel.Quiet,
                        Verbosity = Verbosity.Minimal,
                    },
                    source,
                    fixture.Loaded));

            Assert.Equal(0, exit);
            Assert.DoesNotContain("Error:", error);
            Assert.Contains(discover ? "Kind" : "ObjectCreationExpression", output);
            Assert.True(opens > 1, "Body search must open the supplier after PDB acquisition.");
            Assert.Empty(handler.RequestUris);
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task TypeBodyShapesAcquisition_ReportsBodyOpenFailureAfterPdbAcquisition(
        bool discover, bool invalidImage)
    {
        int opens = 0;
        byte[] image = File.ReadAllBytes(typeof(BodyShapeFixture).Assembly.Location);
        var fixture = CreateTypeSourceFixture(
            AssemblyResolutionProvenance.Local("type-body-shapes"),
            isForwarded: true,
            () =>
            {
                if (++opens == 1)
                    return new MemoryStream(image, writable: false);
                return invalidImage
                    ? new MemoryStream([1, 2, 3], writable: false)
                    : throw new IOException("Selected body-shape image could not be opened.");
            },
            typeof(BodyShapeFixture),
            includePdb: true);
        try
        {
            var handler = new RecordingNotFoundHandler();
            using var client = new HttpClient(handler);
            var source = CreateApiSource(fixture.AssemblyPath, SourceKind.Library) with
            {
                TypeName = fixture.Type.FullName,
                Context = new CommandContext(verbose: false, client),
            };
            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteResolvedAsync(
                    new TypeOptions
                    {
                        TypeName = fixture.Type.FullName,
                        BodyKindQuery = new() { Kind = "ObjectCreationExpression" },
                        Select = [SectionNames.BodyShapes],
                        Discover = discover ? [SectionNames.BodyShapes] : null,
                        DocsExplicitlySet = true,
                        TipLevel = TipLevel.Quiet,
                        Verbosity = Verbosity.Minimal,
                    },
                    source,
                    fixture.Loaded));

            Assert.Equal(1, exit);
            Assert.Contains("Error:", error);
            if (!invalidImage)
                Assert.Contains("Selected body-shape image could not be opened.", error);
            Assert.Empty(output);
            Assert.Equal(2, opens);
            Assert.Empty(handler.RequestUris);
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    public async Task TypeExceptionRegionsAcquisition_UsesSelectedSupplier(
        bool isForwarded, bool discover, bool project)
    {
        int opens = 0;
        byte[] image = File.ReadAllBytes(typeof(MemberExceptionRegionsFixture).Assembly.Location);
        var fixture = CreateTypeSourceFixture(
            AssemblyResolutionProvenance.Local("type-exception-regions"),
            isForwarded,
            () =>
            {
                opens++;
                return new MemoryStream(image, writable: false);
            },
            typeof(MemberExceptionRegionsFixture));
        try
        {
            var handler = new RecordingNotFoundHandler();
            using var client = new HttpClient(handler);
            var source = CreateApiSource(fixture.AssemblyPath, SourceKind.Library) with
            {
                TypeName = fixture.Type.FullName,
                RuntimeAssemblyPath = typeof(object).Assembly.Location,
                Context = new CommandContext(verbose: false, client),
            };
            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteResolvedAsync(
                    new TypeOptions
                    {
                        TypeName = fixture.Type.FullName,
                        MemberFilter = [nameof(MemberExceptionRegionsFixture.TryCatch)],
                        Select = [SectionNames.ExceptionRegions],
                        Discover = discover ? [SectionNames.ExceptionRegions] : null,
                        Columns = project ? ["Clause"] : null,
                        DocsExplicitlySet = true,
                        TipLevel = TipLevel.Quiet,
                        Verbosity = Verbosity.Minimal,
                    },
                    source,
                    fixture.Loaded));

            Assert.Equal(0, exit);
            Assert.DoesNotContain("Error:", error);
            Assert.Contains(discover ? "Clause" : "catch", output);
            Assert.Equal(1, opens);
            Assert.Empty(handler.RequestUris);
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task TypeExceptionRegionsAcquisition_ReportsSelectedOpenFailure(
        bool discover, bool invalidImage)
    {
        int opens = 0;
        var fixture = CreateTypeSourceFixture(
            AssemblyResolutionProvenance.Local("type-exception-regions"),
            isForwarded: true,
            () =>
            {
                opens++;
                return invalidImage
                    ? new MemoryStream([1, 2, 3], writable: false)
                    : throw new IOException("Selected exception-region image could not be opened.");
            },
            typeof(MemberExceptionRegionsFixture));
        try
        {
            var handler = new RecordingNotFoundHandler();
            using var client = new HttpClient(handler);
            var source = CreateApiSource(fixture.AssemblyPath, SourceKind.Library) with
            {
                TypeName = fixture.Type.FullName,
                Context = new CommandContext(verbose: false, client),
            };
            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteResolvedAsync(
                    new TypeOptions
                    {
                        TypeName = fixture.Type.FullName,
                        Select = [SectionNames.ExceptionRegions],
                        Discover = discover ? [SectionNames.ExceptionRegions] : null,
                        DocsExplicitlySet = true,
                        TipLevel = TipLevel.Quiet,
                        Verbosity = Verbosity.Minimal,
                    },
                    source,
                    fixture.Loaded));

            Assert.Equal(1, exit);
            Assert.Contains("Error:", error);
            if (!invalidImage)
                Assert.Contains("Selected exception-region image could not be opened.", error);
            Assert.DoesNotContain("No exception regions", output);
            Assert.Equal(1, opens);
            Assert.Empty(handler.RequestUris);
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    public async Task TypeAnalysisAcquisition_UsesSelectedSupplier(
        bool isForwarded, bool discover, bool project)
    {
        int opens = 0;
        byte[] image = File.ReadAllBytes(typeof(BodyShapeFixture).Assembly.Location);
        var fixture = CreateTypeSourceFixture(
            AssemblyResolutionProvenance.Local("type-analysis"),
            isForwarded,
            () =>
            {
                opens++;
                return new MemoryStream(image, writable: false);
            },
            typeof(BodyShapeFixture));
        try
        {
            var handler = new RecordingNotFoundHandler();
            using var client = new HttpClient(handler);
            var source = CreateApiSource(fixture.AssemblyPath, SourceKind.Library) with
            {
                TypeName = fixture.Type.FullName,
                RuntimeAssemblyPath = typeof(object).Assembly.Location,
                Context = new CommandContext(verbose: false, client),
            };
            MethodBodyInspectionSession.OpenCountForTests = 0;

            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteResolvedAsync(
                    new TypeOptions
                    {
                        TypeName = fixture.Type.FullName,
                        MemberFilter = [nameof(BodyShapeFixture.PublicSmallArray)],
                        Select = [SectionNames.AllocationFacts, SectionNames.CostFacts],
                        Discover = discover ? [SectionNames.AllocationFacts] : null,
                        Columns = project ? ["Member"] : null,
                        DocsExplicitlySet = true,
                        TipLevel = TipLevel.Quiet,
                        Verbosity = Verbosity.Minimal,
                    },
                    source,
                    fixture.Loaded));

            Assert.Equal(0, exit);
            Assert.DoesNotContain("Error:", error);
            Assert.Contains(discover ? "Allocation Kind" : "PublicSmallArray", output);
            Assert.Equal(1, opens);
            Assert.Equal(1, MethodBodyInspectionSession.OpenCountForTests);
            Assert.Empty(handler.RequestUris);
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task TypeAnalysisAcquisition_ReportsSelectedOpenFailure(
        bool discover, bool invalidImage)
    {
        int opens = 0;
        var fixture = CreateTypeSourceFixture(
            AssemblyResolutionProvenance.Local("failed-type-analysis"),
            isForwarded: true,
            () =>
            {
                opens++;
                return invalidImage
                    ? new MemoryStream([1, 2, 3], writable: false)
                    : throw new IOException("Selected Analysis image is unavailable.");
            },
            typeof(BodyShapeFixture));
        try
        {
            var source = CreateApiSource(fixture.AssemblyPath, SourceKind.Library) with
            {
                TypeName = fixture.Type.FullName,
            };
            MethodBodyInspectionSession.OpenCountForTests = 0;
            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteResolvedAsync(
                    new TypeOptions
                    {
                        TypeName = fixture.Type.FullName,
                        Select = [SectionNames.AllocationFacts],
                        Discover = discover ? [SectionNames.AllocationFacts] : null,
                        DocsExplicitlySet = true,
                        TipLevel = TipLevel.Quiet,
                    },
                    source,
                    fixture.Loaded));

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                invalidImage ? "InvalidImage" : "Unreadable",
                error);
            Assert.Contains(fixture.AssemblyPath, error);
            Assert.Equal(1, opens);
            Assert.Equal(0, MethodBodyInspectionSession.OpenCountForTests);
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public async Task TypeAnalysisAcquisition_SkipsOrdinaryApiOutput()
    {
        int opens = 0;
        var fixture = CreateTypeSourceFixture(
            AssemblyResolutionProvenance.Local("ordinary-api"),
            isForwarded: false,
            () =>
            {
                opens++;
                throw new IOException("Analysis was not requested.");
            },
            typeof(BodyShapeFixture));
        try
        {
            var source = CreateApiSource(fixture.AssemblyPath, SourceKind.Library) with
            {
                TypeName = fixture.Type.FullName,
            };
            MethodBodyInspectionSession.OpenCountForTests = 0;
            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteResolvedAsync(
                    new TypeOptions
                    {
                        TypeName = fixture.Type.FullName,
                        DocsExplicitlySet = true,
                        TipLevel = TipLevel.Quiet,
                        Verbosity = Verbosity.Minimal,
                    },
                    source,
                    fixture.Loaded));

            Assert.Equal(0, exit);
            Assert.Contains(nameof(BodyShapeFixture), output);
            Assert.DoesNotContain("Error:", error);
            Assert.Equal(0, opens);
            Assert.Equal(0, MethodBodyInspectionSession.OpenCountForTests);
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(SectionNames.CalledTypes, false, false, false)]
    [InlineData(SectionNames.AllocationFacts, true, false, false)]
    [InlineData(SectionNames.TopLeverage, false, false, true)]
    [InlineData(SectionNames.PerformanceTriage, true, true, true)]
    public void TypeAnalysisAcquisition_PreservesFeaturesAndScope(
        string section, bool allocations, bool opportunities, bool wholeAssembly)
    {
        int opens = 0;
        byte[] image = File.ReadAllBytes(typeof(BodyShapeFixture).Assembly.Location);
        var fixture = CreateTypeSourceFixture(
            AssemblyResolutionProvenance.Local("analysis-scope"),
            isForwarded: false,
            () =>
            {
                opens++;
                return new MemoryStream(image, writable: false);
            },
            typeof(BodyShapeFixture));
        try
        {
            Analysis.LibraryBodyIndex index = ApiAnalysisInspection.OpenTypeAnalysisIndex(
                fixture.AssemblyPath,
                [section],
                fixture.Type,
                sourceAssembly: fixture.Loaded.GetSourceAssembly(fixture.Type));

            Assert.Equal(1, opens);
            Assert.Equal(allocations, index.Features.HasFlag(Analysis.LibraryBodyAnalysisFeatures.Allocations));
            Assert.Equal(opportunities, index.Features.HasFlag(Analysis.LibraryBodyAnalysisFeatures.OptimizationOpportunities));
            Assert.NotEmpty(index.DirectCalls);
            Assert.Equal(
                wholeAssembly,
                index.DirectCalls.Any(call =>
                    !ApiAnalysisInspection.SameType(call.Caller.DeclaringType, fixture.Type)));
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypeAnalysisAcquisition_IgnoresUnrequestedDebugData(bool deferred)
    {
        string directory = CreateDirectory();
        try
        {
            byte[] image = File.ReadAllBytes(typeof(EmbeddedSourceFixture).Assembly.Location);
            using (var reader = new PEReader(new MemoryStream(image, writable: false)))
            {
                DebugDirectoryEntry embedded = Assert.Single(
                    reader.ReadDebugDirectory(),
                    entry => entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
                image[embedded.DataPointer] = 0;
            }
            string path = Path.Combine(directory, "MalformedDebug.dll");
            File.WriteAllBytes(path, image);
            MethodBodyInspectionSession.OpenCountForTests = 0;
            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => deferred
                    ? MemberCommand.ExecuteAsync(new MemberOptions
                    {
                        AssemblyPath = path,
                        TypeName = typeof(EmbeddedSourceFixture).FullName,
                        Select = [SectionNames.CostFacts],
                        DocsExplicitlySet = true,
                        TipLevel = TipLevel.Quiet,
                        RouterDeferredTypeOrMember = true,
                    })
                    : TypeCommand.ExecuteAsync(new TypeOptions
                    {
                        AssemblyPath = path,
                        TypeName = typeof(EmbeddedSourceFixture).FullName,
                        Select = [SectionNames.CostFacts],
                        DocsExplicitlySet = true,
                        TipLevel = TipLevel.Quiet,
                    }));

            Assert.Equal(0, exit);
            Assert.Contains("Cost Facts", output);
            Assert.DoesNotContain("Error:", error);
            Assert.Equal(1, MethodBodyInspectionSession.OpenCountForTests);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypeSourceAcquisition_SourceFilesUsesSelectedOpener(bool isForwarded)
    {
        int opens = 0;
        string original = typeof(EmbeddedSourceFixture).Assembly.Location;
        byte[] image = File.ReadAllBytes(original);
        var fixture = CreateTypeSourceFixture(
            AssemblyResolutionProvenance.Local("source-opening"),
            isForwarded,
            () =>
            {
                opens++;
                return new MemoryStream(image, writable: false);
            },
            typeof(EmbeddedSourceFixture));
        try
        {
            var handler = new RecordingNotFoundHandler();
            using var client = new HttpClient(handler);
            var source = CreateApiSource(fixture.AssemblyPath, SourceKind.Library) with
            {
                TypeName = fixture.Type.FullName,
                RuntimeAssemblyPath = typeof(object).Assembly.Location,
                Context = new CommandContext(verbose: false, client),
            };

            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteResolvedAsync(
                    new TypeOptions
                    {
                        TypeName = fixture.Type.FullName,
                        Select = [SectionNames.SourceFiles],
                        DocsExplicitlySet = true,
                        ShowDocs = false,
                    },
                    source,
                    fixture.Loaded));

            Assert.Equal(0, exit);
            Assert.DoesNotContain("Error:", error);
            Assert.Contains("EmbeddedSourceFixture.cs", output);
            Assert.Equal(1, opens);
            Assert.Empty(handler.RequestUris);
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public async Task TypeSourceAcquisition_PdbPathUsesSelectedOpener()
    {
        int opens = 0;
        string original = typeof(SourceForwarderResolutionTests).Assembly.Location;
        byte[] image = File.ReadAllBytes(original);
        var fixture = CreateTypeSourceFixture(
            AssemblyResolutionProvenance.Local("pdb-opening"),
            isForwarded: false,
            () =>
            {
                opens++;
                return new MemoryStream(image, writable: false);
            });
        try
        {
            string expected = Path.ChangeExtension(fixture.AssemblyPath, ".pdb");
            File.Copy(Path.ChangeExtension(original, ".pdb"), expected);
            var handler = new RecordingNotFoundHandler();
            using var client = new HttpClient(handler);

            string? path = await ApiCommand.TryAcquirePdbPathAsync(
                Path.Combine(fixture.Directory, "unused-path-projection.dll"),
                fixture.Loaded.GetSourceAssembly(fixture.Type),
                new TypeOptions(),
                new VerboseLogger(enabled: false),
                client,
                TestContext.Current.CancellationToken);

            Assert.Equal(expected, path);
            Assert.Equal(1, opens);
            Assert.Empty(handler.RequestUris);
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(SectionNames.SourceFiles)]
    [InlineData(SectionNames.DecompiledSource)]
    public async Task TypeSourceAcquisition_ReportsSelectedOpenFailure(string section)
    {
        int opens = 0;
        var fixture = CreateTypeSourceFixture(
            AssemblyResolutionProvenance.Local("failed-opening"),
            isForwarded: false,
            () =>
            {
                opens++;
                throw new IOException("Selected source image could not be opened.");
            });
        try
        {
            var handler = new RecordingNotFoundHandler();
            using var client = new HttpClient(handler);
            var source = CreateApiSource(fixture.AssemblyPath, SourceKind.Library) with
            {
                TypeName = fixture.Type.FullName,
                Context = new CommandContext(verbose: false, client),
            };

            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteResolvedAsync(
                    new TypeOptions
                    {
                        TypeName = fixture.Type.FullName,
                        Select = [section],
                        DocsExplicitlySet = true,
                    },
                    source,
                    fixture.Loaded));

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("Selected source image could not be opened.", error);
            Assert.Equal(1, opens);
            Assert.Empty(handler.RequestUris);
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypeSourceAcquisition_PreservesMissingSymbols(bool isModule)
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "NoSymbols.dll");
            File.WriteAllBytes(path, BuildAssembly("NoSymbols", isModule: isModule));
            var handler = new RecordingNotFoundHandler();
            using var client = new HttpClient(handler);
            var source = CreateApiSource(path, SourceKind.Library) with
            {
                TypeName = "N.Type",
                Context = new CommandContext(verbose: false, client),
            };
            var options = new TypeOptions
            {
                TypeName = "N.Type",
                Select = [SectionNames.SourceFiles],
                DocsExplicitlySet = true,
            };
            var loaded = Assert.IsType<ApiServices.LoadedApiSurface>(
                ApiServices.LoadTypeApi(source, options));

            var (exit, _, error) = await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteResolvedAsync(options, source, loaded));
            Assert.Equal(0, exit);
            Assert.DoesNotContain("Error:", error);

            var descriptor = loaded.TryGetSourceAssembly(Assert.Single(loaded.Api.Types));
            string? pdb = descriptor is null
                ? await ApiCommand.TryAcquirePdbPathAsync(
                    path, options, new VerboseLogger(enabled: false), client,
                    TestContext.Current.CancellationToken)
                : await ApiCommand.TryAcquirePdbPathAsync(
                    path, descriptor, options, new VerboseLogger(enabled: false), client,
                    TestContext.Current.CancellationToken);
            Assert.Null(pdb);
            Assert.Empty(handler.RequestUris);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypeSourceAcquisition_ReportsMalformedDebugData(bool deferred)
    {
        string directory = CreateDirectory();
        try
        {
            byte[] image = File.ReadAllBytes(typeof(EmbeddedSourceFixture).Assembly.Location);
            using (var reader = new PEReader(new MemoryStream(image, writable: false)))
            {
                DebugDirectoryEntry embedded = Assert.Single(
                    reader.ReadDebugDirectory(),
                    entry => entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
                image[embedded.DataPointer] = 0;
            }
            string path = Path.Combine(directory, "MalformedDebug.dll");
            File.WriteAllBytes(path, image);
            var options = new TypeOptions
            {
                AssemblyPath = path,
                TypeName = typeof(EmbeddedSourceFixture).FullName,
                Select = [SectionNames.SourceFiles],
                DocsExplicitlySet = true,
            };

            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => deferred
                    ? MemberCommand.ExecuteAsync(new MemberOptions
                    {
                        AssemblyPath = options.AssemblyPath,
                        TypeName = options.TypeName,
                        Select = options.Select,
                        DocsExplicitlySet = true,
                        RouterDeferredTypeOrMember = true,
                    })
                    : TypeCommand.ExecuteAsync(options));

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("embedded portable PDB signature is invalid", error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static ApiSourceResult CreateApiSource(string path, string sourceKind) =>
        new(
            SearchPath: path,
            RuntimeAssemblyPath: sourceKind == SourceKind.Platform ? path : null,
            PackageName: "Selected.Package",
            PackageVersion: "2.3.4",
            ResolvedPackagePath: null,
            PackageExtractPath: null,
            ApiSource: sourceKind,
            ApiVersion: "2.3.4",
            PlatformFramework: "aspnetcore",
            SelectedTfm: "net11.0",
            ProjectAssetsPath: null,
            TempDir: null,
            TypeName: null,
            PackageReplaySourceUrls: null,
            PackageReplayUsesOriginalSources: false,
            Context: new CommandContext(verbose: false));

    static MetadataTypeDefinitionName TypeName() =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", ["Type"])).Name;

    static MetadataTypeDefinitionName OtherTypeName() =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "N",
                ["Other"])).Name;

    static ApiSurfaceInspectionFailure Failure(
        string detail,
        int subjectToken,
        string sourcePath,
        int? owningTypeToken = null,
        MetadataTypeDefinitionName?
            owningTypeDefinition = null,
        MetadataTypeDefinitionName[]?
            affectedTypeDefinitions = null) =>
        new(
            "type row",
            subjectToken,
            MetadataTypeNameFailureMechanism.Metadata,
            "Malformed",
            detail)
        {
            SourceAssemblyPath = sourcePath,
            OwningTypeToken = owningTypeToken,
            OwningTypeDefinition = owningTypeDefinition,
            AffectedTypeDefinitions =
                affectedTypeDefinitions is null
                    ? []
                    : [.. affectedTypeDefinitions],
        };

    static string CreateDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-source-forwarder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    static AssemblyReferenceIdentity ReadIdentity(
        byte[] image)
    {
        using var pe =
            new PEReader(new MemoryStream(image));
        return AssemblyReferenceIdentity
            .FromAssemblyDefinition(
                pe.GetMetadataReader());
    }

    static byte[] BuildAssembly(
        string assemblyName,
        AssemblyReferenceIdentity? forwardTarget = null,
        bool definesType = false,
        string typeNamespace = "N",
        string typeName = "Type",
        bool isModule = false)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString($"{assemblyName}.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        if (!isModule)
        {
            metadata.AddAssembly(
                metadata.GetOrAddString(assemblyName),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKey: default,
                flags: default,
                hashAlgorithm: default);
        }
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        if (definesType || forwardTarget is null)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString(typeNamespace),
                metadata.GetOrAddString(typeName),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        }

        if (forwardTarget is not null)
        {
            AssemblyReferenceHandle target = metadata.AddAssemblyReference(
                metadata.GetOrAddString(forwardTarget.Name),
                forwardTarget.Version!,
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
            metadata.AddExportedType(
                TypeAttributes.Public | Forwarder,
                metadata.GetOrAddString(typeNamespace),
                metadata.GetOrAddString(typeName),
                target,
                typeDefinitionId: 0);
        }

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildTargetWithMalformedType(
        bool requestedTypeIsMalformed)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName:
                metadata.GetOrAddString("Target.dll"),
            mvid:
                metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Target"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        TypeSpecificationHandle malformedBase =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0x15,
                    }));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList:
                MetadataTokens.FieldDefinitionHandle(1),
            methodList:
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Type"),
            baseType:
                requestedTypeIsMalformed
                    ? malformedBase
                    : default(EntityHandle),
            fieldList:
                MetadataTokens.FieldDefinitionHandle(1),
            methodList:
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Other"),
            baseType:
                requestedTypeIsMalformed
                    ? default(EntityHandle)
                    : malformedBase,
            fieldList:
                MetadataTokens.FieldDefinitionHandle(1),
            methodList:
                MetadataTokens.MethodDefinitionHandle(1));

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildTargetWithMissingConstraint()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Target.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Target"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        AssemblyReferenceHandle missingAssembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Missing"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        TypeReferenceHandle forwardedBase =
            metadata.AddTypeReference(
                missingAssembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("ForwardedBase"));
        TypeReferenceHandle unrelatedBase =
            metadata.AddTypeReference(
                missingAssembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("UnrelatedBase"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle forwarded =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Type`1"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle unrelated =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Unrelated`1"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        AddMissingConstraint(
            forwarded,
            "T",
            forwardedBase);
        AddMissingConstraint(
            unrelated,
            "U",
            unrelatedBase);

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();

        void AddMissingConstraint(
            TypeDefinitionHandle type,
            string name,
            TypeReferenceHandle missingBase)
        {
            GenericParameterHandle parameter =
                metadata.AddGenericParameter(
                    type,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString(name),
                    index: 0);
            metadata.AddGenericParameterConstraint(
                parameter,
                missingBase);
        }
    }

    static byte[] BuildConstrainedAssembly(
        string assemblyName,
        string dependencyName,
        string typeName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString($"{assemblyName}.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        AssemblyReferenceHandle dependency =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(dependencyName),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        TypeReferenceHandle constraint =
            metadata.AddTypeReference(
                dependency,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Constraint"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle definition =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(typeName),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                definition,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                0);
        metadata.AddGenericParameterConstraint(
            parameter,
            constraint);

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildSnupkg(
        string pdbFileName,
        byte[] pdbBytes)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry(
                $"lib/net10.0/{pdbFileName}");
            using Stream stream = entry.Open();
            stream.Write(pdbBytes);
        }

        return buffer.ToArray();
    }

    static (
        string Directory,
        string AssemblyPath,
        ApiType Type,
        ApiServices.LoadedApiSurface Loaded)
        CreateTypeSourceFixture(
            AssemblyResolutionProvenance provenance,
            bool isForwarded,
            Func<Stream>? openRead = null,
            Type? fixtureType = null,
            bool includePdb = false)
    {
        fixtureType ??= typeof(SourceForwarderResolutionTests);
        string directory = CreateDirectory();
        string sourceAssemblyPath =
            fixtureType.Assembly.Location;
        string assemblyPath = Path.Combine(
            directory,
            Path.GetFileName(sourceAssemblyPath));
        File.Copy(sourceAssemblyPath, assemblyPath);
        if (includePdb)
        {
            File.Copy(
                Path.ChangeExtension(sourceAssemblyPath, ".pdb"),
                Path.ChangeExtension(assemblyPath, ".pdb"));
        }
        ApiSurface api =
            AssemblyReader.ExtractApiSurface(assemblyPath)!;
        ApiType type = Assert.Single(
            api.Types,
            candidate =>
                candidate.FullName
                == fixtureType.FullName);
        api.Types = [type];
        type.IsForwarded = isForwarded;
        type.SourceAssemblyPath = assemblyPath;
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                assemblyPath,
                provenance);
        if (openRead is not null)
        {
            assembly = ResolvedAssemblyReference.Create(
                assembly.Identity,
                assembly.Path,
                openRead,
                provenance);
        }
        var sourceAssemblies =
            new Dictionary<
                ApiType,
                ResolvedAssemblyReference>(
                ReferenceEqualityComparer.Instance)
            {
                [type] = assembly,
            };
        var loaded = new ApiServices.LoadedApiSurface(
            api,
            assemblyPath,
            assemblyPath,
            sourceAssemblies);
        return (
            directory,
            assemblyPath,
            type,
            loaded);
    }

    sealed class RecordingNotFoundHandler
        : HttpMessageHandler
    {
        internal List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(
                new HttpResponseMessage(
                    HttpStatusCode.NotFound));
        }
    }

    sealed class SymbolPackageHandler(
        byte[] snupkg) : HttpMessageHandler
    {
        internal List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(
                request.RequestUri!.AbsolutePath.EndsWith(
                    ".snupkg",
                    StringComparison.OrdinalIgnoreCase)
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(snupkg),
                    }
                    : new HttpResponseMessage(
                        HttpStatusCode.NotFound));
        }
    }

    sealed class MappingPolicy(
        ResolvedAssemblyReference dependency)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                request.Target
                is AssemblyBindingTarget.AssemblyReference reference
                && reference.Identity == dependency.Identity
                ? AssemblyBindingSelection.Found(dependency)
                : AssemblyBindingSelection.NotFound();
        }
    }
}
