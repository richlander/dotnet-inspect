using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.Metadata;

namespace DotnetInspector.Services.Tests;

public class AssemblySetResolutionSessionTests
{
    const TypeAttributes Forwarder =
        (TypeAttributes)0x00200000;

    [Fact]
    public void BuildApiSurface_AcquisitionFailureIsVisible()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"unreadable-surface-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, [0x4d, 0x5a]);
        try
        {
            ApiSurface surface =
                Assert.IsType<ApiSurface>(
                    AssemblySetSurfaceBuilder.Build([path]));

            ApiSurfaceInspectionFailure failure =
                Assert.Single(surface.InspectionFailures);
            Assert.Equal(
                "acquire API surface",
                failure.Operation);
            Assert.Equal(path, failure.SourceAssemblyPath);
            Assert.Equal(
                nameof(MalformedMetadataRootException),
                failure.Kind);
            Assert.Equal(
                "The assembly metadata root is malformed "
                    + "(UnmappableMetadataDirectory).",
                failure.Detail);
            Assert.Empty(surface.Types);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BuildApiSurface_UnsupportedMetadataFormatNamesTheMechanism()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"unsupported-surface-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, BuildWindowsMetadataImage());
        try
        {
            ApiSurface surface =
                Assert.IsType<ApiSurface>(
                    AssemblySetSurfaceBuilder.Build([path]));

            ApiSurfaceInspectionFailure failure =
                Assert.Single(surface.InspectionFailures);
            Assert.Equal("acquire API surface", failure.Operation);
            Assert.Equal(path, failure.SourceAssemblyPath);
            Assert.Equal(
                nameof(UnsupportedMetadataFormatException),
                failure.Kind);
            Assert.Equal(
                "Windows Metadata is not a supported metadata format.",
                failure.Detail);
            Assert.Empty(surface.Types);
        }
        finally
        {
            File.Delete(path);
        }
    }

    static byte[] BuildWindowsMetadataImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Unsupported.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Unsupported"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                "WindowsRuntime 1.4;CLR v4.0.30319",
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    [Fact]
    public void BuildApiSurface_MetadataStreamCountOverflowPreservesHealthyNeighbor()
    {
        string directory = Directory.CreateTempSubdirectory(
            "assembly-set-overflow-").FullName;
        string overflowPath = Path.Combine(directory, "Overflow.dll");
        string healthyPath =
            typeof(AssemblySetResolutionSessionTests).Assembly.Location;
        try
        {
            File.WriteAllBytes(
                overflowPath,
                OverflowMetadataStreamCount(
                    File.ReadAllBytes(healthyPath)));

            ApiSurface surface =
                Assert.IsType<ApiSurface>(
                    AssemblySetSurfaceBuilder.Build(
                        [overflowPath, healthyPath]));

            Assert.NotEmpty(surface.Types);
            ApiSurfaceInspectionFailure failure =
                Assert.Single(surface.InspectionFailures);
            Assert.Equal("InvalidImage", failure.Kind);
            Assert.Equal(overflowPath, failure.SourceAssemblyPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BuildApiSurface_ClassifiesConstraintAcrossAssemblySet()
    {
        string directory =
            Directory.CreateTempSubdirectory(
                "assembly-set-resolution-test")
                .FullName;
        string dependencyPath =
            Path.Combine(directory, "ConstraintDependency.dll");
        string consumerPath =
            Path.Combine(directory, "ConstraintConsumer.dll");
        try
        {
            File.WriteAllBytes(
                dependencyPath,
                BuildDependency());
            File.WriteAllBytes(
                consumerPath,
                BuildConsumer());

            ApiSurface surface =
                Assert.IsType<ApiSurface>(
                    AssemblySetSurfaceBuilder.Build(
                        [consumerPath, dependencyPath]));

            ApiType consumer = Assert.Single(
                surface.Types,
                static type =>
                    type.Name == "Consumer`1");
            Assert.Equal(
                TypeParameterTypeKind.ReferenceType,
                Assert.Single(consumer.TypeParameters)
                    .TypeKind);
            Assert.Empty(surface.InspectionFailures);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BuildApiSurface_PreservesResolverLineageAcrossForwardedConstraint()
    {
        string unrelated =
            FixtureCatalog.ServicesRouteLearningUnrelated.AssemblyPath();
        string consumer =
            FixtureCatalog.ServicesRouteLearningConsumer.AssemblyPath();
        _ = FixtureCatalog.ServicesRouteLearningConsumer.AssetPath("middle");
        _ = FixtureCatalog.ServicesRouteLearningConsumer.AssetPath("base");

        ApiSurface surface =
            Assert.IsType<ApiSurface>(
                AssemblySetSurfaceBuilder.Build(
                    // The first root is the fallback and cannot resolve the
                    // forwarding chain owned by the second root.
                    [unrelated, consumer]));

        ApiType consumerType = Assert.Single(
            surface.Types,
            static type =>
                type.FullName
                    == "DotnetInspector.Services.RouteLearning.Consumer`1");
        Assert.Equal(
            TypeParameterTypeKind.ReferenceType,
            Assert.Single(consumerType.TypeParameters).TypeKind);
        Assert.Empty(surface.InspectionFailures);
    }

    [Fact]
    public void BuildApiSurface_RollsForwardPlatformConstraintReferences()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"platform-constraint-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(
            path,
            BuildPlatformConstraintConsumer());
        try
        {
            ApiSurface surface =
                Assert.IsType<ApiSurface>(
                    AssemblySetSurfaceBuilder.Build([path]));

            ApiType consumer =
                Assert.Single(
                    surface.Types,
                    static type =>
                        type.Name == "Consumer`1");
            Assert.Equal(
                TypeParameterTypeKind.ReferenceType,
                Assert.Single(consumer.TypeParameters)
                    .TypeKind);
            Assert.Empty(surface.InspectionFailures);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BuildApiSurface_ForwarderOnlyAssemblyIsRetained()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"forwarder-surface-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, BuildForwarder());
        try
        {
            ApiSurface surface =
                Assert.IsType<ApiSurface>(
                    AssemblySetSurfaceBuilder.Build([path]));

            Assert.Empty(surface.Types);
            TypeForwarder forwarder =
                Assert.Single(surface.TypeForwarders);
            Assert.Equal("System.String", forwarder.TypeName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BuildApiSurface_ValidEmptyAssemblyIsRetained()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"empty-surface-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, BuildInternalOnlyAssembly());
        try
        {
            ApiSurface surface =
                Assert.IsType<ApiSurface>(
                    AssemblySetSurfaceBuilder.Build([path]));

            Assert.Empty(surface.Types);
            Assert.Empty(surface.TypeForwarders);
            Assert.Empty(surface.InspectionFailures);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BuildApiSurface_NetmoduleUsesModuleExtraction()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"module-surface-{Guid.NewGuid():N}.netmodule");
        File.WriteAllBytes(path, BuildNetmodule());
        try
        {
            var messages = new List<string>();
            ApiSurface surface =
                Assert.IsType<ApiSurface>(
                    AssemblySetSurfaceBuilder.Build(
                        [path],
                        log: messages.Add));

            ApiType type = Assert.Single(surface.Types);
            Assert.Equal("N.Widget", type.FullName);
            Assert.Empty(surface.InspectionFailures);
            Assert.DoesNotContain(
                messages,
                message => message.StartsWith(
                    "  ! ",
                    StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    static byte[] OverflowMetadataStreamCount(byte[] image)
    {
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        int metadataStart = peReader.PEHeaders.MetadataStartOffset;
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(metadataStart + 12, sizeof(int)));
        int streamCountOffset =
            metadataStart
            + 16
            + versionLength
            + sizeof(ushort);
        BinaryPrimitives.WriteUInt16LittleEndian(
            image.AsSpan(streamCountOffset, sizeof(ushort)),
            ushort.MaxValue);
        return image;
    }

    static byte[] BuildDependency()
    {
        MetadataBuilder metadata =
            NewMetadata("ConstraintDependency");
        AddType(
            metadata,
            "<Module>",
            TypeAttributes.NotPublic);
        AddType(metadata, "Base");
        return Serialize(metadata);
    }

    static byte[] BuildConsumer()
    {
        MetadataBuilder metadata =
            NewMetadata("ConstraintConsumer");
        AssemblyReferenceHandle dependency =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(
                    "ConstraintDependency"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        TypeReferenceHandle constraint =
            metadata.AddTypeReference(
                dependency,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Base"));
        AddType(
            metadata,
            "<Module>",
            TypeAttributes.NotPublic);
        TypeDefinitionHandle consumer =
            AddType(metadata, "Consumer`1");
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        metadata.AddGenericParameterConstraint(
            parameter,
            constraint);
        return Serialize(metadata);
    }

    static byte[] BuildPlatformConstraintConsumer()
    {
        MetadataBuilder metadata =
            NewMetadata("PlatformConstraintConsumer");
        AssemblyReferenceHandle runtime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(10, 0, 0, 0),
                culture: default,
                publicKeyOrToken:
                    metadata.GetOrAddBlob(
                        Convert.FromHexString(
                            "B03F5F7F11D50A3A")),
                flags: default,
                hashValue: default);
        TypeReferenceHandle constraint =
            metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Exception"));
        AddType(
            metadata,
            "<Module>",
            TypeAttributes.NotPublic);
        TypeDefinitionHandle consumer =
            AddType(metadata, "Consumer`1");
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        metadata.AddGenericParameterConstraint(
            parameter,
            constraint);
        return Serialize(metadata);
    }

    static byte[] BuildForwarder()
    {
        MetadataBuilder metadata =
            NewMetadata("Forwarder");
        AddType(
            metadata,
            "<Module>",
            TypeAttributes.NotPublic);
        AssemblyReferenceHandle coreLibrary =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(
                    "System.Private.CoreLib"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        metadata.AddExportedType(
            TypeAttributes.Public
                | Forwarder,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("String"),
            coreLibrary,
            typeDefinitionId: 0);
        return Serialize(metadata);
    }

    static byte[] BuildInternalOnlyAssembly()
    {
        MetadataBuilder metadata =
            NewMetadata("InternalOnly");
        AddType(
            metadata,
            "<Module>",
            TypeAttributes.NotPublic);
        AddType(
            metadata,
            "Hidden",
            TypeAttributes.NotPublic
                | TypeAttributes.Class);
        return Serialize(metadata);
    }

    static byte[] BuildNetmodule()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName:
                metadata.GetOrAddString(
                    "Widget.netmodule"),
            mvid:
                metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        AddType(
            metadata,
            "<Module>",
            TypeAttributes.NotPublic);
        AddType(metadata, "Widget");
        return Serialize(metadata);
    }

    static MetadataBuilder NewMetadata(string assemblyName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName:
                metadata.GetOrAddString(
                    $"{assemblyName}.dll"),
            mvid:
                metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        return metadata;
    }

    static TypeDefinitionHandle AddType(
        MetadataBuilder metadata,
        string name,
        TypeAttributes attributes =
            TypeAttributes.Public | TypeAttributes.Class)
        => metadata.AddTypeDefinition(
            attributes,
            name == "<Module>"
                ? default
                : metadata.GetOrAddString("N"),
            metadata.GetOrAddString(name),
            baseType: default,
            fieldList:
                MetadataTokens.FieldDefinitionHandle(1),
            methodList:
                MetadataTokens.MethodDefinitionHandle(1));

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
