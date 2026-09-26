using System.IO.Compression;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Text;
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

[SupportedOSPlatform("browser")]
public sealed partial class BrowserEngineBoundaryTests
{
    const int MiB = 1024 * 1024;

    public static object PerformanceBoxingProbe(int value) => value;

    public static int PerformanceNoAllocationProbe(int value) => value;

    public static int InvocationDestinationProbe(int value) =>
        InvocationDestinationTarget(value);

    static int InvocationDestinationTarget(int value) => value;

    public static int CalleeEvidenceProbe(int value) =>
        PerformanceStackAllocProbe(value);

    public static int CostCalleeEvidenceProbe(int count) =>
        PerformanceAllocationInLoopProbe(count);

    public static Guid PerformanceValueTypeConstructionProbe(byte[] bytes) =>
        new(bytes);

    public static int PerformanceStackAllocProbe(int value)
    {
        Span<int> values = stackalloc int[1];
        values[0] = value;
        return values[0];
    }

    public static int PerformanceAllocationInLoopProbe(int count)
    {
        int total = 0;
        for (int i = 0; i < count; i++)
            total += new object().GetHashCode();
        return total;
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

    static async Task AssertRootOnlyAggregateStatus(
        string packageId,
        string framework,
        BrowserCompileLibraryStatus expectedStatus)
    {
        BrowserPackageMetadata metadata =
            Assert.IsType<BrowserPackageMetadata>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPackageMetadata(
                        packageId,
                        "1.0.0",
                        framework,
                        packageId),
                    BrowserMetadataJsonContext.Default.BrowserPackageMetadata));
        Assert.Empty(metadata.Assemblies);
        Assert.Null(metadata.InspectionError);
        // Each export assembly declares its own compile-library enum, and the wire
        // value is the member name, so the aggregate is compared by that name.
        Assert.Equal(expectedStatus.ToString(), metadata.CompileLibrary.Status.ToString());

        BrowserPackageIntegrations integrations =
            Assert.IsType<BrowserPackageIntegrations>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPackageIntegrations(
                        packageId,
                        "1.0.0",
                        framework,
                        packageId),
                    BrowserAnalysisJsonContext.Default.BrowserPackageIntegrations));
        Assert.Empty(integrations.Categories);
        Assert.Equal(0, integrations.TotalSignals);
        Assert.False(integrations.IsComplete);
        Assert.Null(integrations.InspectionError);
        Assert.Equal(expectedStatus.ToString(), integrations.CompileLibrary.Status.ToString());

        BrowserPackageOpportunities opportunities =
            Assert.IsType<BrowserPackageOpportunities>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPackageOpportunities(
                        packageId,
                        "1.0.0",
                        framework,
                        packageId),
                    BrowserAnalysisJsonContext.Default.BrowserPackageOpportunities));
        Assert.Empty(opportunities.Categories);
        Assert.Equal(0, opportunities.TotalOpportunities);
        Assert.False(opportunities.IsComplete);
        Assert.Null(opportunities.InspectionError);
        Assert.Equal(expectedStatus.ToString(), opportunities.CompileLibrary.Status.ToString());

        BrowserPackagePerformance performance =
            Assert.IsType<BrowserPackagePerformance>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPackagePerformance(
                        packageId,
                        "1.0.0",
                        framework,
                        packageId),
                    BrowserAnalysisJsonContext.Default.BrowserPackagePerformance));
        Assert.Empty(performance.Members);
        Assert.Equal(0, performance.TotalOpportunities);
        Assert.Null(performance.InspectionError);
        Assert.Equal(expectedStatus.ToString(), performance.CompileLibrary.Status.ToString());
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

    static WorkspacePlan CreateStjPlan(
        string framework = "net10.0",
        string? runtimeIdentifier = null) =>
        new(
            [],
            [
                new WorkspaceContextInput
                {
                    Framework = "net8.0",
                    Members = [WorkspaceMemberCoordinate.Platform(
                        "runtime",
                        "System.Text.Json",
                        "10.0.12",
                        "net10.0")],
                },
                new WorkspaceContextInput
                {
                    Framework = framework,
                    RuntimeIdentifier = runtimeIdentifier,
                    Members = [WorkspaceMemberCoordinate.Platform(
                        "runtime",
                        "System.Text.Json",
                        "10.0.12",
                        "net10.0")],
                },
            ]);

    static byte[] BuildIntegrationImage(
        string assemblyName,
        params string[] integrationTypeNames)
    {
        var assemblyBuilder = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            typeof(object).Assembly);
        ModuleBuilder module =
            assemblyBuilder.DefineDynamicModule(assemblyName);
        foreach (string integrationTypeName in integrationTypeNames)
        {
            TypeBuilder type = module.DefineType(
                integrationTypeName,
                TypeAttributes.Public | TypeAttributes.Class);
            type.DefineDefaultConstructor(MethodAttributes.Public);
            type.CreateType();
        }

        using var stream = new MemoryStream();
        assemblyBuilder.Save(stream);
        return stream.ToArray();
    }

    static async Task AssertPerformanceParticipantIsolation(
        string packageId,
        byte[] neighbor)
    {
        byte[] selected = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await Coordinate(
            packageId,
            PackageEntries(
                ("lib/net11.0/A.Other.dll", neighbor),
                ("lib/net11.0/Z.Selected.dll", selected)));
        BrowserPackageCoordinate isolated = await Coordinate(
            $"{packageId}.Isolated",
            Package(selected, "lib/net11.0/Z.Selected.dll"));
        string selectedId = coordinate.Selection.Assets.Single(
            asset => asset.AssemblyName == "Z.Selected.dll").Id;
        string expected = await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPackagePerformance(
            isolated.PackageId, "1.0.0", "net11.0", selectedId);
        string actual = await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPackagePerformance(
            packageId, "1.0.0", "net11.0", selectedId);
        BrowserPackagePerformance performance = Assert.IsType<BrowserPackagePerformance>(
            JsonSerializer.Deserialize(
                actual, BrowserAnalysisJsonContext.Default.BrowserPackagePerformance));

        Assert.Null(performance.InspectionError);
        Assert.True(performance.TotalOpportunities > 0);
        Assert.Contains(performance.Members, member =>
            member.MemberName == nameof(PerformanceBoxingProbe));
        Assert.Equal(expected, actual);

        string neighborId = coordinate.Selection.Assets.Single(
            asset => asset.AssemblyName == "A.Other.dll").Id;
        BrowserPackagePerformance neighborPerformance = Assert.IsType<BrowserPackagePerformance>(
            JsonSerializer.Deserialize(
                await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPackagePerformance(
                    packageId, "1.0.0", "net11.0", neighborId),
                BrowserAnalysisJsonContext.Default.BrowserPackagePerformance));
        Assert.NotNull(neighborPerformance.InspectionError);
        Assert.Empty(neighborPerformance.Members);
    }

    public static int HomeDemoRunFixture(int value) =>
        Math.Abs(value);

    public static string HomeDemoRunFixture(string value) =>
        value.Trim();

    public static int HomeDemoRunLocalFixture(int value) =>
        value + 1;

    private static BrowserDependencyCoordinateMatch MatchDependencyCoordinate(
        BrowserDependencyCoordinateCandidate[] candidates,
        string packageId,
        string declaredRange)
    {
        string candidatesJson = JsonSerializer.Serialize(
            candidates,
            BrowserPackageJsonContext.Default.BrowserDependencyCoordinateCandidateArray);
        string resultJson = DotnetInspect.Web.Interop.Package.PackageExports.MatchPackageDependencyCoordinate(
            packageId,
            declaredRange,
            candidatesJson);
        return JsonSerializer.Deserialize(
            resultJson,
            BrowserPackageJsonContext.Default.BrowserDependencyCoordinateMatch)
            ?? throw new InvalidOperationException("The dependency-coordinate result is absent.");
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
        => PlatformPackage("net11.0", assemblies);

    static byte[] PlatformPackage(
        string framework,
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
                        $"runtimes/linux-x64/lib/{framework}/{name}",
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

    static byte[] PackageWithDocuments(
        string packageId,
        string version,
        string readme,
        string skill)
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

            WritePackageText(
                archive,
                "README.md",
                readme);
            WritePackageText(
                archive,
                "skills/demo/SKILL.md",
                skill);
            WritePackageText(
                archive,
                "content/notes.txt",
                "Not browsable.");
        }

        return content.ToArray();
    }

    static void WritePackageText(
        ZipArchive archive,
        string path,
        string text)
    {
        using Stream entry = archive
            .CreateEntry(path, CompressionLevel.Optimal)
            .Open();
        entry.Write(Encoding.UTF8.GetBytes(text));
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
        BrowserPackageWorkspace.CreateGallerySource(
            handler,
            new NuGetFetchOptions
            {
                RequestTimeout = TimeSpan.FromMinutes(1),
                OperationTimeout = TimeSpan.FromMinutes(1),
            });

    sealed class IncompletePinnedCandidateSource :
        IPackageDependencyCandidateSource
    {
        public ValueTask<PackageAcquisitionCandidateResult>
            ResolvePinnedCandidateAsync(
            PackageSourceCoordinate coordinate,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                new PackageAcquisitionCandidateResult(
                    PackageAcquisitionCandidateResultState.Incomplete,
                    null,
                    []));
        }

        public Task<PackageVersionDiscoveryResult>
            DiscoverDependencyVersionsAsync(
            string packageId,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new InvalidOperationException(
                "Pinned candidate resolution must not discover versions.");
    }

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
        (string Version, bool Listed)[]? discoveryVersions = null,
        System.Net.HttpStatusCode packageStatus =
            System.Net.HttpStatusCode.OK,
        bool omitContentLength = false,
        Task? payloadRelease = null)
        : HttpMessageHandler
    {
        readonly string _package = packageId.ToLowerInvariant();
        readonly string _packageUrl =
            $"https://globalcdn.nuget.org/packages/{packageId.ToLowerInvariant()}.{version}.nupkg";

        public List<string> Requested { get; } = [];
        public bool PayloadDisposed { get; private set; }
        public TaskCompletionSource PayloadReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string url = request.RequestUri!.AbsoluteUri;
            Requested.Add(url);
            if (discoveryVersions is not null
                && url.Equals(
                    $"https://globalcdn.nuget.org/v3-flatcontainer/{_package}/index.json",
                    StringComparison.Ordinal))
            {
                return Json(JsonSerializer.Serialize(new
                {
                    versions = discoveryVersions.Select(candidate => candidate.Version),
                }));
            }

            if (discoveryVersions is not null
                && url.Equals(
                    $"https://globalcdn.nuget.org/v3/registration5-gz-semver2/{_package}/index.json",
                    StringComparison.Ordinal))
            {
                return Json(JsonSerializer.Serialize(new
                {
                    items = new[]
                    {
                        new
                        {
                            items = discoveryVersions.Select(candidate => new
                            {
                                catalogEntry = new
                                {
                                    version = candidate.Version,
                                    listed = candidate.Listed,
                                },
                            }),
                        },
                    },
                }));
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
                response.Content = payloadRelease is not null
                    ? new StreamContent(new GatedPayloadStream(
                        archive, PayloadReadStarted, payloadRelease))
                    : omitContentLength
                    ? new StreamContent(
                        new TrackingPayloadStream(
                            archive,
                            () => PayloadDisposed = true))
                    : new ByteArrayContent(archive);
                if (payloadRelease is not null)
                    response.Content.Headers.ContentLength = archive.LongLength;
            }

            return Task.FromResult(response);
        }

        static Task<HttpResponseMessage> Json(string json) =>
            Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(json),
                });
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

    sealed class GatedPayloadStream(
        byte[] bytes,
        TaskCompletionSource started,
        Task release) : MemoryStream(bytes, writable: false)
    {
        public override bool CanSeek => false;

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await release.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
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
        internal bool HadAuthorization { get; private set; }
        internal bool HadCallerHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            HadAuthorization = request.Headers.Authorization is not null;
            HadCallerHeader = request.Headers.Contains("X-Caller-Header");
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
