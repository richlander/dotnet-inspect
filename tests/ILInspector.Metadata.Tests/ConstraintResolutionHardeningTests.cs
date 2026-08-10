using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace ILInspector.Metadata.Tests;

public class ConstraintResolutionHardeningTests
{
    [Fact]
    public void DeepConstructedBaseAuthenticationUsesBoundedStack()
    {
        const int Count = 1_000;
        byte[] firstImage =
            BuildChain("ChainA", "T", "ChainB", "U", Count, 0);
        byte[] secondImage =
            BuildChain("ChainB", "U", "ChainA", "T", Count, 1);
        byte[] consumerImage =
            BuildConsumer("Consumer", "ChainA", "T0", constructed: true);
        ResolvedAssemblyReference first = Descriptor(firstImage);
        ResolvedAssemblyReference second = Descriptor(secondImage);
        ResolvedAssemblyReference consumer = Descriptor(consumerImage);
        TypeParameterTypeKind kind = default;
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    using var pe = Reader(consumerImage);
                    using var catalog = new TypeResolutionCatalog();
                    ApiSurface surface = ApiSurfaceExtractor.Extract(
                        pe,
                        consumer,
                        catalog,
                        new MappingPolicy(first, second));
                    kind = Assert.Single(
                        Assert.Single(surface.Types).TypeParameters)
                        .TypeKind;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            },
            maxStackSize: 1024 * 1024);

        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.Equal(TypeParameterTypeKind.Undetermined, kind);
    }

    [Fact]
    public void BudgetExhaustionIsNotPromotedAcrossGenerations()
    {
        byte[] firstImage = BuildBudgetFirst();
        byte[] secondImage = BuildBudgetSecond();
        ResolvedAssemblyReference first = Descriptor(firstImage);
        ResolvedAssemblyReference second = Descriptor(secondImage);
        var policy = new MappingPolicy(first, second);
        TypeResolutionRequest target = Request(first, "T0");
        TypeResolutionRequest filler1 = Request(first, "Filler1");
        TypeResolutionRequest filler2 = Request(first, "Filler2");
        using var catalog = new TypeResolutionCatalog(
            new TypeResolutionContextOptions
            {
                MaxTypeResolutionRequests = 4,
            });

        using (TypeResolutionContext exhausted =
            catalog.CreateContext(
                policy,
                [first],
                [filler1, filler2, target]))
        {
            Assert.IsType<TypeResolutionFailure.RequestBudgetExceeded>(
                Assert.IsType<TypeResolutionOutcome.Rejected>(
                    exhausted.Resolve(target)).Failure);
        }

        using TypeResolutionContext fresh =
            catalog.CreateContext(
                policy,
                [first],
                [target]);
        Assert.Equal(
            MetadataTypeDefinitionKind.Class,
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                fresh.Resolve(target)).Definition.Kind);
    }

    [Fact]
    public void MissingCoreBindingDoesNotProveConstraintKind()
    {
        byte[] image = BuildCoreEnumConsumer();
        ResolvedAssemblyReference source = Descriptor(image);
        using var pe = Reader(image);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MissingPolicy());

        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            Assert.Single(Assert.Single(surface.Types).TypeParameters)
                .TypeKind);
    }

    [Fact]
    public void ConstructedAuthenticCoreValueTypeDoesNotAuthenticateAsClass()
    {
        AssemblyName coreIdentity = typeof(object).Assembly.GetName();
        byte[] derivedImage =
            BuildMarkedDerived(
                "Derived",
                coreIdentity,
                "System",
                "ValueType",
                constructed: true);
        byte[] consumerImage =
            BuildConsumer(
                "Consumer",
                "Derived",
                "Derived",
                constructed: false);
        ResolvedAssemblyReference source = Descriptor(consumerImage);
        ResolvedAssemblyReference derived = Descriptor(derivedImage);
        ResolvedAssemblyReference core =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(object).Assembly.Location,
                AssemblyResolutionProvenance.Local("test"));
        using var pe = Reader(consumerImage);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(derived, core));

        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            Assert.Single(Assert.Single(surface.Types).TypeParameters)
                .TypeKind);
    }

    [Theory]
    [InlineData("ValueType")]
    [InlineData("Enum")]
    public void AuthenticCoreValueTypeRootsDoNotAuthenticateAsClass(
        string baseName)
    {
        AssemblyName coreIdentity = typeof(object).Assembly.GetName();
        byte[] derivedImage =
            BuildMarkedDerived(
                "Derived",
                coreIdentity,
                "System",
                baseName,
                constructed: false);
        byte[] consumerImage =
            BuildConsumer(
                "Consumer",
                "Derived",
                "Derived",
                constructed: false);
        ResolvedAssemblyReference source = Descriptor(consumerImage);
        ResolvedAssemblyReference derived = Descriptor(derivedImage);
        ResolvedAssemblyReference core =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(object).Assembly.Location,
                AssemblyResolutionProvenance.Local("test"));
        using var pe = Reader(consumerImage);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(derived, core));

        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            Assert.Single(Assert.Single(surface.Types).TypeParameters)
                .TypeKind);
    }

    [Theory]
    [InlineData("ValueType", false)]
    [InlineData("Enum", false)]
    [InlineData("ValueType", true)]
    public void SameImageCoreRootsDoNotAuthenticateForgedClassMarkers(
        string baseName,
        bool constructed)
    {
        byte[] coreImage =
            BuildCoreWithMarkedDerived(baseName, constructed);
        byte[] consumerImage =
            BuildConsumer(
                "Consumer",
                "HostileCore",
                "Derived",
                constructed: false);
        ResolvedAssemblyReference source = Descriptor(consumerImage);
        ResolvedAssemblyReference core = Descriptor(coreImage);
        using var pe = Reader(consumerImage);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(core));

        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            Assert.Single(Assert.Single(surface.Types).TypeParameters)
                .TypeKind);
    }

    [Fact]
    public void ConstructedConstraintRequiresMatchingExternalArity()
    {
        byte[] dependencyImage = BuildSimpleType("Dependency", "Base");
        byte[] consumerImage =
            BuildConsumer(
                "Consumer",
                "Dependency",
                "Base",
                constructed: true);
        ResolvedAssemblyReference source = Descriptor(consumerImage);
        ResolvedAssemblyReference dependency =
            Descriptor(dependencyImage);
        using var pe = Reader(consumerImage);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(dependency));

        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            Assert.Single(Assert.Single(surface.Types).TypeParameters)
                .TypeKind);
    }

    [Fact]
    public void ConstructedConstraintRequiresMatchingSameImageArity()
    {
        byte[] image = BuildSameImageConstructedConstraint();
        using var pe = Reader(image);

        ApiSurface surface = ApiSurfaceExtractor.Extract(pe);

        ApiType consumer =
            Assert.Single(
                surface.Types,
                static type => type.Name == "Consumer`1");
        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            Assert.Single(consumer.TypeParameters).TypeKind);
    }

    [Fact]
    public void ConstructedConstraintRejectsTrailingSignatureBytes()
    {
        byte[] dependencyImage =
            BuildGenericType("Dependency", "Base`1");
        byte[] consumerImage = BuildTrailingConstraintConsumer();
        ResolvedAssemblyReference source = Descriptor(consumerImage);
        ResolvedAssemblyReference dependency =
            Descriptor(dependencyImage);
        using var pe = Reader(consumerImage);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(dependency));

        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            Assert.Single(Assert.Single(surface.Types).TypeParameters)
                .TypeKind);
    }

    [Fact]
    public void ConstructedCoreConstraintWithoutResolutionStaysUndetermined()
    {
        byte[] image = BuildConstructedCoreEnumConsumer();
        using var pe = Reader(image);

        ApiSurface surface = ApiSurfaceExtractor.Extract(pe);

        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            Assert.Single(Assert.Single(surface.Types).TypeParameters)
                .TypeKind);
    }

    [Fact]
    public void AuthenticationBudgetExhaustionIsVisibleOnApiSurface()
    {
        byte[] firstImage = BuildBudgetFirst();
        byte[] secondImage = BuildBudgetSecond();
        byte[] consumerImage =
            BuildConsumer(
                "Consumer",
                "BudgetA",
                "T0",
                constructed: false);
        ResolvedAssemblyReference source = Descriptor(consumerImage);
        ResolvedAssemblyReference first = Descriptor(firstImage);
        ResolvedAssemblyReference second = Descriptor(secondImage);
        using var pe = Reader(consumerImage);
        using var catalog = new TypeResolutionCatalog(
            new TypeResolutionContextOptions
            {
                MaxTypeResolutionRequests = 1,
            });

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(first, second));

        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            Assert.Single(Assert.Single(surface.Types).TypeParameters)
                .TypeKind);
        ApiSurfaceInspectionFailure failure =
            Assert.Single(surface.InspectionFailures);
        Assert.Contains(
            "dependency authentication",
            failure.Detail,
            StringComparison.Ordinal);
        Assert.Contains(
            "configured budget of 1",
            failure.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveryBudgetExhaustionIsVisibleOnApiSurface()
    {
        byte[] image = BuildTwoConstraintConsumer();
        ResolvedAssemblyReference source = Descriptor(image);
        using var pe = Reader(image);
        using var catalog = new TypeResolutionCatalog(
            new TypeResolutionContextOptions
            {
                MaxTypeResolutionRequests = 1,
            });

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MissingPolicy());

        ApiSurfaceInspectionFailure failure =
            Assert.Single(surface.InspectionFailures);
        Assert.Equal(
            "resolve generic parameter constraints",
            failure.Operation);
        Assert.Equal(
            "MalformedMetadata",
            failure.Kind);
        Assert.Contains(
            "configured budget of 1",
            failure.Detail,
            StringComparison.Ordinal);
    }

    static byte[] BuildChain(
        string assemblyName,
        string prefix,
        string otherAssembly,
        string otherPrefix,
        int count,
        int offset)
    {
        MetadataBuilder metadata = NewMetadata(assemblyName);
        AssemblyReferenceHandle other =
            AddReference(metadata, otherAssembly);
        AddModule(metadata);
        StringHandle typeNamespace = metadata.GetOrAddString("N");
        for (int i = 0; i < count; i++)
        {
            TypeReferenceHandle baseType =
                metadata.AddTypeReference(
                    other,
                    typeNamespace,
                    metadata.GetOrAddString(
                        $"{otherPrefix}{i + offset}"));
            TypeDefinitionHandle definition =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public | TypeAttributes.Class,
                    typeNamespace,
                    metadata.GetOrAddString($"{prefix}{i}"),
                    AddConstructedClass(metadata, baseType),
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddGenericParameter(
                definition,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                0);
        }

        return Serialize(metadata);
    }

    static byte[] BuildBudgetFirst()
    {
        MetadataBuilder metadata = NewMetadata("BudgetA");
        AssemblyReferenceHandle other =
            AddReference(metadata, "BudgetB");
        StringHandle typeNamespace = metadata.GetOrAddString("N");
        TypeReferenceHandle baseType =
            metadata.AddTypeReference(
                other,
                typeNamespace,
                metadata.GetOrAddString("U0"));
        AddModule(metadata);
        TypeDefinitionHandle first =
            AddType(
                metadata,
                "T0",
                AddConstructedClass(metadata, baseType));
        AddGenericParameter(metadata, first);
        TypeDefinitionHandle terminal =
            AddType(metadata, "T1");
        AddGenericParameter(metadata, terminal);
        AddType(metadata, "Filler1");
        AddType(metadata, "Filler2");
        return Serialize(metadata);
    }

    static byte[] BuildBudgetSecond()
    {
        MetadataBuilder metadata = NewMetadata("BudgetB");
        AssemblyReferenceHandle other =
            AddReference(metadata, "BudgetA");
        TypeReferenceHandle baseType =
            metadata.AddTypeReference(
                other,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("T1"));
        AddModule(metadata);
        TypeDefinitionHandle definition =
            AddType(
                metadata,
                "U0",
                AddConstructedClass(metadata, baseType));
        AddGenericParameter(metadata, definition);
        return Serialize(metadata);
    }

    static byte[] BuildCoreEnumConsumer()
    {
        MetadataBuilder metadata = NewMetadata("CoreConsumer");
        var token =
            new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a };
        AssemblyReferenceHandle core =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(10, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(token),
                default,
                default);
        TypeReferenceHandle enumType =
            metadata.AddTypeReference(
                core,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Enum"));
        AddModule(metadata);
        TypeDefinitionHandle consumer =
            AddType(metadata, "Consumer`1");
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                0);
        metadata.AddGenericParameterConstraint(
            parameter,
            enumType);
        return Serialize(metadata);
    }

    static byte[] BuildConstructedCoreEnumConsumer()
    {
        MetadataBuilder metadata = NewMetadata("ConstructedCoreConsumer");
        var token =
            new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a };
        AssemblyReferenceHandle core =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(10, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(token),
                default,
                default);
        TypeReferenceHandle enumType =
            metadata.AddTypeReference(
                core,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Enum"));
        AddModule(metadata);
        TypeDefinitionHandle consumer =
            AddType(metadata, "Consumer`1");
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                0);
        metadata.AddGenericParameterConstraint(
            parameter,
            AddConstructedClass(metadata, enumType));
        return Serialize(metadata);
    }

    static byte[] BuildCoreWithMarkedDerived(
        string baseName,
        bool constructed)
    {
        MetadataBuilder metadata = NewMetadata("HostileCore");
        AddModule(metadata);
        TypeDefinitionHandle objectType =
            AddType(metadata, "Object", typeNamespace: "System");
        TypeDefinitionHandle valueType =
            AddType(
                metadata,
                "ValueType",
                objectType,
                "System");
        TypeDefinitionHandle enumType =
            AddType(
                metadata,
                "Enum",
                valueType,
                "System");
        TypeDefinitionHandle root =
            baseName == "Enum" ? enumType : valueType;
        AddType(
            metadata,
            "Derived",
            constructed
                ? AddConstructedClass(metadata, root)
                : AddClassSpecification(metadata, root));
        return Serialize(metadata);
    }

    static byte[] BuildSimpleType(
        string assemblyName,
        string typeName)
    {
        MetadataBuilder metadata = NewMetadata(assemblyName);
        AddModule(metadata);
        AddType(metadata, typeName);
        return Serialize(metadata);
    }

    static byte[] BuildGenericType(
        string assemblyName,
        string typeName)
    {
        MetadataBuilder metadata = NewMetadata(assemblyName);
        AddModule(metadata);
        TypeDefinitionHandle definition =
            AddType(metadata, typeName);
        AddGenericParameter(metadata, definition);
        return Serialize(metadata);
    }

    static byte[] BuildSameImageConstructedConstraint()
    {
        MetadataBuilder metadata = NewMetadata("SameImage");
        AddModule(metadata);
        TypeDefinitionHandle root = AddType(metadata, "Base");
        TypeDefinitionHandle consumer =
            AddType(metadata, "Consumer`1");
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                0);
        metadata.AddGenericParameterConstraint(
            parameter,
            AddConstructedClass(metadata, root));
        return Serialize(metadata);
    }

    static byte[] BuildTrailingConstraintConsumer()
    {
        MetadataBuilder metadata = NewMetadata("Trailing");
        AssemblyReferenceHandle dependency =
            AddReference(metadata, "Dependency");
        TypeReferenceHandle root =
            metadata.AddTypeReference(
                dependency,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Base`1"));
        AddModule(metadata);
        TypeDefinitionHandle consumer =
            AddType(metadata, "Consumer`1");
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                0);
        var signature = new BlobBuilder();
        signature.WriteByte(0x15);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(
            (MetadataTokens.GetRowNumber(root) << 2) | 1);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x08);
        signature.WriteByte(0x08);
        metadata.AddGenericParameterConstraint(
            parameter,
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(signature)));
        return Serialize(metadata);
    }

    static byte[] BuildMarkedDerived(
        string assemblyName,
        AssemblyName baseAssembly,
        string baseNamespace,
        string baseName,
        bool constructed)
    {
        MetadataBuilder metadata = NewMetadata(assemblyName);
        AssemblyReferenceHandle reference =
            AddReference(metadata, baseAssembly);
        TypeReferenceHandle baseType =
            metadata.AddTypeReference(
                reference,
                metadata.GetOrAddString(baseNamespace),
                metadata.GetOrAddString(baseName));
        AddModule(metadata);
        AddType(
            metadata,
            "Derived",
            constructed
                ? AddConstructedClass(metadata, baseType)
                : AddClassSpecification(metadata, baseType));
        return Serialize(metadata);
    }

    static byte[] BuildConsumer(
        string assemblyName,
        string targetAssembly,
        string targetType,
        bool constructed)
    {
        MetadataBuilder metadata = NewMetadata(assemblyName);
        AssemblyReferenceHandle reference =
            AddReference(metadata, targetAssembly);
        TypeReferenceHandle target =
            metadata.AddTypeReference(
                reference,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(targetType));
        EntityHandle constraint = constructed
            ? AddConstructedClass(metadata, target)
            : target;
        AddModule(metadata);
        TypeDefinitionHandle consumer =
            AddType(metadata, "Consumer`1");
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                0);
        metadata.AddGenericParameterConstraint(
            parameter,
            constraint);
        return Serialize(metadata);
    }

    static byte[] BuildTwoConstraintConsumer()
    {
        MetadataBuilder metadata = NewMetadata("TwoConstraints");
        AssemblyReferenceHandle reference =
            AddReference(metadata, "Dependency");
        AddModule(metadata);
        for (int i = 0; i < 2; i++)
        {
            TypeReferenceHandle constraint =
                metadata.AddTypeReference(
                    reference,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString($"Base{i}"));
            TypeDefinitionHandle consumer =
                AddType(metadata, $"Consumer{i}`1");
            GenericParameterHandle parameter =
                metadata.AddGenericParameter(
                    consumer,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("T"),
                    0);
            metadata.AddGenericParameterConstraint(
                parameter,
                constraint);
        }

        return Serialize(metadata);
    }

    static MetadataBuilder NewMetadata(string assemblyName)
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
        return metadata;
    }

    static void AddModule(MetadataBuilder metadata) =>
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

    static TypeDefinitionHandle AddType(
        MetadataBuilder metadata,
        string name,
        EntityHandle baseType = default,
        string typeNamespace = "N") =>
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString(typeNamespace),
            metadata.GetOrAddString(name),
            baseType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

    static void AddGenericParameter(
        MetadataBuilder metadata,
        TypeDefinitionHandle definition) =>
        metadata.AddGenericParameter(
            definition,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            0);

    static AssemblyReferenceHandle AddReference(
        MetadataBuilder metadata,
        string name) =>
        metadata.AddAssemblyReference(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

    static AssemblyReferenceHandle AddReference(
        MetadataBuilder metadata,
        AssemblyName identity) =>
        metadata.AddAssemblyReference(
            metadata.GetOrAddString(identity.Name!),
            identity.Version ?? new Version(0, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(
                identity.GetPublicKeyToken() ?? []),
            default,
            default);

    static TypeSpecificationHandle AddConstructedClass(
        MetadataBuilder metadata,
        EntityHandle type)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x15);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(
            EncodeTypeDefOrRef(type));
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x08);
        return metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(signature));
    }

    static TypeSpecificationHandle AddClassSpecification(
        MetadataBuilder metadata,
        EntityHandle type)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(
            EncodeTypeDefOrRef(type));
        return metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(signature));
    }

    static int EncodeTypeDefOrRef(EntityHandle type) =>
        (MetadataTokens.GetRowNumber(type) << 2)
        | type.Kind switch
        {
            HandleKind.TypeDefinition => 0,
            HandleKind.TypeReference => 1,
            HandleKind.TypeSpecification => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

    static MetadataTypeDefinitionName Name(string leaf) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "N",
                ImmutableArray.Create(leaf))).Name;

    static TypeResolutionRequest Request(
        ResolvedAssemblyReference assembly,
        string leaf) =>
        TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            Name(leaf));

    static ResolvedAssemblyReference Descriptor(byte[] image)
    {
        using var pe = Reader(image);
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                pe.GetMetadataReader());
        return ResolvedAssemblyReference.Create(
            identity,
            path: null,
            () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local("test"));
    }

    static PEReader Reader(byte[] image) =>
        new(ImmutableCollectionsMarshal.AsImmutableArray(image));

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

    sealed class MappingPolicy(
        params ResolvedAssemblyReference[] assemblies)
        : IAssemblyBindingPolicy
    {
        readonly Dictionary<string, ResolvedAssemblyReference> map =
            assemblies.ToDictionary(
                static assembly => assembly.Identity.Name,
                StringComparer.Ordinal);

        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request) =>
            request.Target
                    is AssemblyBindingTarget.AssemblyReference reference
                && map.TryGetValue(
                    reference.Identity.Name,
                    out ResolvedAssemblyReference? assembly)
                    ? AssemblyBindingSelection.Found(assembly)
                    : AssemblyBindingSelection.NotFound();
    }

    sealed class MissingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request) =>
            AssemblyBindingSelection.NotFound();
    }
}
