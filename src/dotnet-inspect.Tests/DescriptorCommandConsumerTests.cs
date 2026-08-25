using System.Reflection.Metadata;
using DotnetInspector.Commands;
using DotnetInspector.Fixtures;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Queries.EmbeddedFixtures;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Decompiler;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class DescriptorCommandConsumerTests
{
    [Fact]
    public void TypeAnalysis_UsesDescriptorInsteadOfDisplayPath()
    {
        string path = typeof(DescriptorCommandConsumerTests).Assembly.Location;
        var options = new ApiOptions
        {
            AssemblyReference = TestAssemblyReferences.Designated(path),
        };

        var index = ApiAnalysisInspection.OpenTypeAnalysisIndex(
            "/path-that-must-not-be-opened.dll",
            options: options);

        Assert.NotEmpty(index.Methods);
    }

    [Fact]
    public void MemberAnalysisAndExceptionRegions_UseDescriptorInsteadOfDisplayPath()
    {
        string path = typeof(DescriptorCommandConsumerTests).Assembly.Location;
        ResolvedAssemblyReference assembly =
            TestAssemblyReferences.Designated(path);
        ApiSurface api = Assert.IsType<ApiSurface>(
            AssemblyReader.ExtractApiSurface(
                assembly,
                includeAll: true));
        ApiType type = Assert.Single(
            api.Types,
            type => type.FullName
                == typeof(DescriptorCommandConsumerTests).FullName);
        ApiMember method = Assert.Single(
            type.Members,
            member => member.Name == nameof(ExceptionRegionFixture));
        var options = new ApiOptions
        {
            AssemblyReference = assembly,
        };
        var inspection = new ApiMemberAnalysisInspection(
            "/path-that-must-not-be-opened.dll",
            [method],
            new HashSet<string> { SectionNames.ExceptionRegions },
            callerScopeAssemblies: null,
            options);

        Assert.NotEmpty(inspection.BodyIndex.Methods);
        Assert.NotEmpty(
            inspection.ResolveExceptionRegions(
                method.MetadataToken!.Value,
                out string? memberError));
        Assert.Null(memberError);
        Assert.NotEmpty(
            ApiAnalysisInspection.ResolveExceptionRegions(
                "/path-that-must-not-be-opened.dll",
                assembly,
                [method]));
    }

    [Fact]
    public void PathlessApiOwnership_SelectsTypedAcquisitionRoles()
    {
        string path = typeof(DescriptorCommandConsumerTests).Assembly.Location;
        ResolvedAssemblyReference assembly =
            TestAssemblyReferences.Designated(path).WithoutLocalPath();
        ResolvedAssemblyReference runtime =
            TestAssemblyReferences.Designated(path);
        ApiSurface api = Assert.IsType<ApiSurface>(
            AssemblyReader.ExtractApiSurface(assembly));
        ApiType type = Assert.Single(
            api.Types,
            type => type.FullName
                == typeof(DescriptorCommandConsumerTests).FullName);
        var loaded = new ApiServices.LoadedApiSurface(
            api,
            "/display-only.dll",
            "/display-only.dll",
            assembly,
            runtime);

        Assert.Null(type.SourceAssemblyPath);
        Assert.Same(
            assembly,
            ApiServices.AssemblyReferenceForRole(
                loaded,
                type,
                ApiServices.AssemblyReferenceRole.TokenOrigin));
        Assert.Same(
            assembly,
            ApiServices.AssemblyReferenceForRole(
                loaded,
                type,
                ApiServices.AssemblyReferenceRole.Surface));
        Assert.Same(
            runtime,
            ApiServices.AssemblyReferenceForRole(
                loaded,
                type,
                ApiServices.AssemblyReferenceRole.RuntimeOrPdb));
        Assert.NotSame(
            assembly.Registration,
            runtime.Registration);
    }

    [Fact]
    public void ForwardedApiOwnership_PreservesTargetForRuntimeRole()
    {
        string path =
            typeof(DescriptorCommandConsumerTests)
                .Assembly.Location;
        ResolvedAssemblyReference surface =
            TestAssemblyReferences.Designated(path);
        ResolvedAssemblyReference target =
            TestAssemblyReferences.Designated(path);
        ResolvedAssemblyReference runtime =
            TestAssemblyReferences.Designated(path);
        ApiSurface api = Assert.IsType<ApiSurface>(
            AssemblyReader.ExtractApiSurface(target));
        ApiType type = Assert.Single(
            api.Types,
            type => type.FullName
                == typeof(DescriptorCommandConsumerTests).FullName);
        var loaded = new ApiServices.LoadedApiSurface(
            api,
            path,
            path,
            surface,
            runtime);

        Assert.Same(
            target,
            ApiServices.AssemblyReferenceForRole(
                loaded,
                type,
                ApiServices.AssemblyReferenceRole.RuntimeOrPdb));
    }

    [Fact]
    public void FullApiLoading_AcceptsPathlessAssembly()
    {
        string path = typeof(DescriptorCommandConsumerTests).Assembly.Location;
        ResolvedAssemblyReference assembly =
            TestAssemblyReferences.Designated(path).WithoutLocalPath();

        ApiServices.LoadedApiSurface loaded = Assert.IsType<
            ApiServices.LoadedApiSurface>(
            ApiServices.LoadFullApi(
                "/display-only.dll",
                assembly,
                runtimeAssemblyReference: null,
                runtimeAssemblyPath: null,
                packagePath: null,
                packageName: null,
                apiSource: SourceKind.Library,
                apiVersion: null,
                selectedTfm: null,
                new VerboseLogger(false),
                new ApiOptions()));

        Assert.Contains(
            loaded.Api.Types,
            type => type.FullName
                == typeof(DescriptorCommandConsumerTests).FullName);
    }

    [Fact]
    public void FullApiLoading_RejectsPathlessForwardingAssembly()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"pathless-forwarder-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, BuildForwarderAssembly());
        try
        {
            ResolvedAssemblyReference assembly =
                TestAssemblyReferences.Designated(path)
                    .WithoutLocalPath();

            Assert.Null(
                ApiServices.LoadFullApi(
                    "/display-only.dll",
                    assembly,
                    runtimeAssemblyReference: null,
                    runtimeAssemblyPath: null,
                    packagePath: null,
                    packageName: null,
                    apiSource: SourceKind.Library,
                    apiVersion: null,
                    selectedTfm: null,
                    new VerboseLogger(false),
                    new ApiOptions()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WholeTypeDecompiledSource_NoCompositionIsNotAnError()
    {
        var result = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(
                new TypeOptions
                {
                    TypeName =
                        typeof(DescriptorCommandConsumerDelegate)
                            .FullName,
                    AssemblyPath =
                        typeof(DescriptorCommandConsumerDelegate)
                            .Assembly.Location,
                    IncludeSections = [SectionNames.DecompiledSource],
                    TipLevel = TipLevel.Quiet,
                    Verbosity = Verbosity.Minimal,
                    MarkdownExplicitlySet = true,
                    FormatExplicitlySet = true,
                }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.DoesNotContain(
            "DI_TYPESOURCE_NONE",
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "## Decompiled Source",
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WholeTypeDecompiledSource_AcquisitionFailureIsTypedFailure()
    {
        string path =
            typeof(DescriptorCommandConsumerTests).Assembly.Location;
        ResolvedAssemblyReference original =
            TestAssemblyReferences.Designated(path);
        ResolvedAssemblyReference rejected =
            ResolvedAssemblyReference.Create(
                original.Identity,
                path: null,
                () => throw new IOException(
                    "descriptor acquisition rejected"),
                original.Provenance);
        ApiSurface api = Assert.IsType<ApiSurface>(
            AssemblyReader.ExtractApiSurface(original));
        ApiType type = Assert.Single(
            api.Types,
            candidate => candidate.FullName
                == typeof(DescriptorCommandConsumerTests).FullName);
        var policy = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path));

        DecompilerResult result =
            MemberBodyProducer.Project(
                type,
                rejected,
                policy);

        Assert.False(result.Succeeded);
        Assert.Equal(
            DecompilationFidelity.Failed,
            result.Fidelity);
        DecompilerDiagnostic diagnostic =
            Assert.Single(result.Diagnostics);
        Assert.Equal(
            DiagnosticIds.InternalError,
            diagnostic.Id);
    }

    [Fact]
    public void WholeTypeDecompiledSource_AddressIdentityDriftIsTypedFailure()
    {
        byte[] firstImage =
            BuildTypeAssembly(Guid.NewGuid());
        byte[] secondImage =
            BuildTypeAssembly(Guid.NewGuid());
        AssemblyReferenceIdentity identity =
            ReadIdentity(firstImage);
        ResolvedAssemblyReference stable =
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(
                    firstImage,
                    writable: false),
                AssemblyResolutionProvenance.Local(
                    "whole-type identity test"));
        ApiType type = Assert.Single(
            Assert.IsType<ApiSurface>(
                AssemblyReader.ExtractApiSurface(stable))
                .Types,
            static candidate =>
                candidate.FullName == "Sample.Widget");
        int opens = 0;
        ResolvedAssemblyReference unstable =
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(
                    opens++ == 0
                        ? firstImage
                        : secondImage,
                    writable: false),
                AssemblyResolutionProvenance.Local(
                    "whole-type identity test"));
        string path = Path.Combine(
            Path.GetTempPath(),
            $"whole-type-identity-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, firstImage);
        try
        {
            var policy = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(
                    path));

            DecompilerResult result =
                MemberBodyProducer.Project(
                    type,
                    unstable,
                    policy);

            Assert.False(result.Succeeded);
            Assert.Equal(
                DiagnosticIds.InternalError,
                Assert.Single(result.Diagnostics).Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SourceFileCollection_UsesAlreadyOpenApiSurface()
    {
        string path =
            FixtureCatalog.SourceLinkNormalized.AssemblyPath();
        ResolvedAssemblyReference assembly =
            TestAssemblyReferences.Designated(path);
        using var service = SourceLinkService.Open(assembly);

        List<SourceFileInfo> files =
            await SourceFileCollector.CollectAsync(service);

        Assert.True(service.HasSourceLink);
        Assert.NotEmpty(files);
    }

    [Fact]
    public async Task MethodSource_UsesDescriptorInsteadOfDisplayPath()
    {
        string path = typeof(DescriptorCommandConsumerTests).Assembly.Location;
        ResolvedAssemblyReference assembly =
            TestAssemblyReferences.Designated(path);
        using var httpClient = new HttpClient();

        ApiCommand.ResolvedMethodSource result =
            await ApiCommand.ResolveMethodSourceAsync(
                "/path-that-must-not-be-opened.dll",
                assembly,
                typeof(DescriptorCommandConsumerTests).FullName!,
                nameof(TypeAnalysis_UsesDescriptorInsteadOfDisplayPath),
                overloadIndex: 0,
                new ApiOptions(),
                httpClient,
                new VerboseLogger(false),
                fetchSource: false);

        Assert.False(result.MemberHasNoBody);
        Assert.Null(result.PdbSourceUnavailableReason);
    }

    [Fact]
    public async Task MemberSourceLocations_ResolveFromPathlessEmbeddedPdb()
    {
        string path = typeof(EmbeddedSourceFixture)
            .Assembly.Location;
        ResolvedAssemblyReference rooted =
            TestAssemblyReferences.Designated(path);
        ResolvedAssemblyReference pathless =
            rooted.WithoutLocalPath();
        ApiType rootedType = EmbeddedSourceType(rooted);
        ApiType pathlessType = EmbeddedSourceType(pathless);
        using var httpClient = new HttpClient();
        var options = new MemberOptions();
        var logger = new VerboseLogger(false);

        await MemberSourceLocationCollector.EnrichAsync(
            rootedType,
            rooted,
            packageName: null,
            packageVersion: null,
            options,
            httpClient,
            logger);
        await MemberSourceLocationCollector.EnrichAsync(
            pathlessType,
            pathless,
            packageName: null,
            packageVersion: null,
            options,
            httpClient,
            logger);

        ApiMember rootedMember = Assert.Single(
            rootedType.Members,
            static member =>
                member.SourceUrl is not null);
        ApiMember pathlessMember = Assert.Single(
            pathlessType.Members,
            member => member.Name == rootedMember.Name);
        Assert.NotNull(rootedMember.SourceLineNumber);
        Assert.Equal(
            rootedMember.SourceUrl,
            pathlessMember.SourceUrl);
        Assert.Equal(
            rootedMember.SourceLineNumber,
            pathlessMember.SourceLineNumber);

        static ApiType EmbeddedSourceType(
            ResolvedAssemblyReference assembly)
        {
            ApiSurface api = Assert.IsType<ApiSurface>(
                AssemblyReader.ExtractApiSurface(
                    assembly,
                    includeAll: true));
            return Assert.Single(
                api.Types,
                static type =>
                    type.FullName
                    == typeof(EmbeddedSourceFixture).FullName);
        }
    }

    [Fact]
    public async Task MemberSourceLocations_ReportPathlessAcquisitionFailure()
    {
        string path =
            typeof(DescriptorCommandConsumerTests).Assembly.Location;
        ResolvedAssemblyReference original =
            TestAssemblyReferences.Designated(path);
        ResolvedAssemblyReference failing =
            ResolvedAssemblyReference.Create(
                original.Identity,
                path: null,
                () => throw new IOException(
                    "descriptor acquisition rejected"),
                original.Provenance);
        ApiSurface api = Assert.IsType<ApiSurface>(
            AssemblyReader.ExtractApiSurface(
                original,
                includeAll: true));
        ApiType type = Assert.Single(
            api.Types,
            candidate => candidate.FullName
                == typeof(DescriptorCommandConsumerTests).FullName);
        using var httpClient = new HttpClient();

        MemberSourceLocationEnrichment result =
            await MemberSourceLocationCollector.EnrichAsync(
                type,
                failing,
                packageName: null,
                packageVersion: null,
                new MemberOptions(),
                httpClient,
                new VerboseLogger(false));

        MemberSourceLocationFailure failure =
            Assert.Single(result.Failures);
        Assert.Equal(type.FullName, failure.Subject);
        Assert.Contains(
            "descriptor acquisition rejected",
            failure.Reason,
            StringComparison.Ordinal);
    }

    static int ExceptionRegionFixture()
    {
        try
        {
            return 1;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    static byte[] BuildForwarderAssembly()
    {
        var metadata = new System.Reflection.Metadata.Ecma335.MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Forwarder.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Forwarder"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            System.Reflection.TypeAttributes.NotPublic,
            @namespace: default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList:
                System.Reflection.Metadata.Ecma335.MetadataTokens
                    .FieldDefinitionHandle(1),
            methodList:
                System.Reflection.Metadata.Ecma335.MetadataTokens
                    .MethodDefinitionHandle(1));
        var target = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Target"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        metadata.AddExportedType(
            System.Reflection.TypeAttributes.Public
                | (System.Reflection.TypeAttributes)0x00200000,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Forwarded"),
            target,
            typeDefinitionId: 0);
        var pe = new System.Reflection.PortableExecutable.ManagedPEBuilder(
            System.Reflection.PortableExecutable.PEHeaderBuilder
                .CreateLibraryHeader(),
            new System.Reflection.Metadata.Ecma335.MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new System.Reflection.Metadata.BlobBuilder(),
            flags:
                System.Reflection.PortableExecutable.CorFlags.ILOnly);
        var image = new System.Reflection.Metadata.BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildTypeAssembly(Guid mvid)
    {
        var metadata =
            new System.Reflection.Metadata.Ecma335.MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("TypeAssembly.dll"),
            metadata.GetOrAddGuid(mvid),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("TypeAssembly"),
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
            System.Reflection.Metadata.Ecma335.MetadataTokens
                .FieldDefinitionHandle(1),
            System.Reflection.Metadata.Ecma335.MetadataTokens
                .MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            System.Reflection.TypeAttributes.Public,
            metadata.GetOrAddString("Sample"),
            metadata.GetOrAddString("Widget"),
            default,
            System.Reflection.Metadata.Ecma335.MetadataTokens
                .FieldDefinitionHandle(1),
            System.Reflection.Metadata.Ecma335.MetadataTokens
                .MethodDefinitionHandle(1));
        var pe =
            new System.Reflection.PortableExecutable.ManagedPEBuilder(
                System.Reflection.PortableExecutable.PEHeaderBuilder
                    .CreateLibraryHeader(),
                new System.Reflection.Metadata.Ecma335.MetadataRootBuilder(
                    metadata),
                new System.Reflection.Metadata.BlobBuilder(),
                flags:
                    System.Reflection.PortableExecutable.CorFlags.ILOnly);
        var image = new System.Reflection.Metadata.BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static AssemblyReferenceIdentity ReadIdentity(byte[] image)
    {
        using var stream =
            new MemoryStream(image, writable: false);
        using var pe =
            new System.Reflection.PortableExecutable.PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            pe.GetMetadataReader());
    }
}

public delegate void DescriptorCommandConsumerDelegate();
