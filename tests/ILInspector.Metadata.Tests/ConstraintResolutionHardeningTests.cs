using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using ILInspector.Metadata.Tests.SpellabilityConsumer;
using ILInspector.Metadata.Tests.SpellabilityReference;

namespace ILInspector.Metadata.Tests;

public class ConstraintResolutionHardeningTests
{
    const TypeAttributes Forwarder =
        (TypeAttributes)0x00200000;

    [Fact]
    public void CompilerProducedSameImageConstraintAuthenticatesExternalConstructedBase()
    {
        string consumerPath =
            typeof(CompilerProducedConstraintHost<>)
                .Assembly.Location;
        string dependencyPath =
            typeof(VisibleGeneric<>).Assembly.Location;
        ResolvedAssemblyReference source =
            ResolvedAssemblyReference.CreateFromPath(
                consumerPath,
                AssemblyResolutionProvenance.Local("test"));
        ResolvedAssemblyReference dependency =
            ResolvedAssemblyReference.CreateFromPath(
                dependencyPath,
                AssemblyResolutionProvenance.Local("test"));
        using var stream = File.OpenRead(consumerPath);
        using var pe = new PEReader(stream);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(dependency));

        ApiType consumer = Assert.Single(
            surface.Types,
            static type =>
                type.Name
                == nameof(
                    CompilerProducedConstraintHost<
                        ConstructedVisibleString>)
                    + "`1");
        Assert.Equal(
            TypeParameterTypeKind.ReferenceType,
            Assert.Single(consumer.TypeParameters).TypeKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameImageConstraintAuthenticatesExternalConstructedBase(
        bool multiHop)
    {
        byte[] dependencyImage =
            BuildGenericType("Dependency", "Base`1");
        byte[] consumerImage =
            BuildSameImageConstraintWithExternalConstructedBase(
                multiHop);
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

        ApiType consumer = Assert.Single(
            surface.Types,
            static type => type.Name == "Consumer`1");
        Assert.Equal(
            TypeParameterTypeKind.ReferenceType,
            Assert.Single(consumer.TypeParameters).TypeKind);
        Assert.Empty(surface.InspectionFailures);
    }

    [Fact]
    public void SameImageConstraintRejectsExternalConstructedInterfaceBase()
    {
        byte[] dependencyImage =
            BuildGenericInterface("Dependency", "Base`1");
        byte[] consumerImage =
            BuildSameImageConstraintWithExternalConstructedBase(
                multiHop: false);
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

        ApiType consumer = Assert.Single(
            surface.Types,
            static type => type.Name == "Consumer`1");
        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            Assert.Single(consumer.TypeParameters).TypeKind);
    }

    [Fact]
    public void SameImageTypeSpecificationBaseAuthenticationUsesBoundedStack()
    {
        const int Count = 256;
        byte[] dependencyImage =
            BuildSameImageTypeSpecificationBaseChain(Count);
        byte[] consumerImage =
            BuildConsumer(
                "Consumer",
                "SameImageChain",
                "T0",
                constructed: false);
        ResolvedAssemblyReference source = Descriptor(consumerImage);
        ResolvedAssemblyReference dependency =
            Descriptor(dependencyImage);
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
                        source,
                        catalog,
                        new MappingPolicy(dependency));
                    kind = Assert.Single(
                        Assert.Single(surface.Types).TypeParameters)
                        .TypeKind;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            },
            maxStackSize: 128 * 1024);

        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.Equal(TypeParameterTypeKind.ReferenceType, kind);
    }

    [Fact]
    public void NestedTypeSpecificationDepthBoundaryUsesBoundedStack()
    {
        byte[] consumerImage =
            BuildConsumer(
                "Consumer",
                "NestedSignature",
                "Derived",
                constructed: false);
        ResolvedAssemblyReference source = Descriptor(consumerImage);
        TypeParameterTypeKind accepted = default;
        TypeParameterTypeKind rejected = default;
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    using var pe = Reader(consumerImage);
                    accepted = Classify(
                        TypeSpecificationRoot
                            .MaxAuthenticationSignatureDepth - 1);
                    rejected = Classify(
                        TypeSpecificationRoot
                            .MaxAuthenticationSignatureDepth);

                    TypeParameterTypeKind Classify(int depth)
                    {
                        byte[] dependencyImage =
                            BuildDeeplyNestedTypeSpecificationBase(
                                depth);
                        ResolvedAssemblyReference dependency =
                            Descriptor(dependencyImage);
                        using var catalog =
                            new TypeResolutionCatalog();
                        ApiSurface surface = ApiSurfaceExtractor.Extract(
                            pe,
                            source,
                            catalog,
                            new MappingPolicy(dependency));
                        return Assert.Single(
                            Assert.Single(surface.Types).TypeParameters)
                            .TypeKind;
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            },
            maxStackSize: 128 * 1024);

        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.Equal(TypeParameterTypeKind.ReferenceType, accepted);
        Assert.Equal(TypeParameterTypeKind.Undetermined, rejected);
    }

    [Fact]
    public void CyclicTypeSpecificationBaseFailsClosed()
    {
        byte[] image = BuildCyclicTypeSpecificationBase();
        using var pe = Reader(image);

        TypeDeclarationResult.Defined declaration =
            Assert.IsType<TypeDeclarationResult.Defined>(
                MetadataTypeDeclarationProbe.Probe(
                    pe.GetMetadataReader(),
                    Name("Derived")));

        Assert.Equal(
            MetadataTypeDefinitionKind.Unknown,
            declaration.Kind);
    }

    [Fact]
    public void SharedAcyclicTypeSpecificationDependencyIsAccepted()
    {
        byte[] image =
            BuildSharedAcyclicTypeSpecificationBase();
        using var pe = Reader(image);

        TypeDeclarationResult.Defined declaration =
            Assert.IsType<TypeDeclarationResult.Defined>(
                MetadataTypeDeclarationProbe.Probe(
                    pe.GetMetadataReader(),
                    Name("Derived")));

        Assert.Equal(
            MetadataTypeDefinitionKind.Class,
            declaration.Kind);
    }

    [Fact]
    public void InvalidGenericParameterNumberingCannotAuthenticateKind()
    {
        byte[] dependencyImage =
            BuildGenericType("Dependency", "Base`1");
        byte[] middleImage =
            BuildInvalidlyNumberedInterfaceWithExternalBase();
        byte[] consumerImage =
            BuildConsumer(
                "Consumer",
                "Middle",
                "Middle`2",
                constructed: false);
        ResolvedAssemblyReference source = Descriptor(consumerImage);
        ResolvedAssemblyReference middle = Descriptor(middleImage);
        ResolvedAssemblyReference dependency =
            Descriptor(dependencyImage);
        using var pe = Reader(consumerImage);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(middle, dependency));

        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            Assert.Single(Assert.Single(surface.Types).TypeParameters)
                .TypeKind);
    }

    [Fact]
    public void DeclarationIndexReusesAssemblyReferenceProjection()
    {
        const int Count = 64;
        byte[] image =
            BuildTypesWithSharedExternalBase(
                Count,
                [1, 2, 3, 4, 5, 6, 7, 8]);
        using var pe = Reader(image);
        MetadataTypeDeclarationProbe.Index index =
            MetadataTypeDeclarationProbe.CreateIndex(
                pe.GetMetadataReader());
        AssemblyReferenceIdentity? first = null;

        for (int i = 0; i < Count; i++)
        {
            TypeDeclarationResult.Defined declaration =
                Assert.IsType<TypeDeclarationResult.Defined>(
                    index.Probe(Name($"Derived{i}")));
            AssemblyReferenceIdentity reference =
                Assert.IsType<DefinitionKindDependency>(
                    declaration.KindDependency)
                    .Reference;
            if (first is null)
                first = reference;
            else
                Assert.Same(first, reference);
        }
    }

    [Fact]
    public void DirectCoreBaseClassification_DoesNotRehashLargePublicKeyPerType()
    {
        const int TypeCount = 32;
        var (image, types) =
            BuildDirectCoreBaseSample(
                typeCount: TypeCount,
                publicKeyBytes: 1024 * 1024);
        using var pe = Reader(image);
        MetadataReader reader = pe.GetMetadataReader();

        Assert.Equal(
            MetadataTypeDefinitionKind.Class,
            MetadataTypeDeclarationProbe.ClassifyDefinitionKind(
                reader,
                types[0],
                declaringAssemblyDefinesCoreLibraryRoot: false));

        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (TypeDefinitionHandle type in types)
        {
            Assert.Equal(
                MetadataTypeDefinitionKind.Class,
                MetadataTypeDeclarationProbe.ClassifyDefinitionKind(
                    reader,
                    type,
                    declaringAssemblyDefinesCoreLibraryRoot: false));
        }

        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(
            allocated,
            0,
            8 * 1024 * 1024);
    }

    [Fact]
    public async Task
        RetainedProjection_ConcurrentSharedReaderAccessReusesSingleIdentity()
    {
        const int WorkerCount = 16;
        var (image, types) =
            BuildDirectCoreBaseSample(
                typeCount: WorkerCount,
                publicKeyBytes: 1024 * 1024);
        using var pe = Reader(image);
        MetadataReader reader = pe.GetMetadataReader();
        AssemblyReferenceHandle reference =
            Assert.Single(reader.AssemblyReferences);
        using var start = new Barrier(WorkerCount);

        Task<(
            AssemblyReferenceIdentity Identity,
            MetadataTypeDefinitionKind Kind)>[] tasks =
        [
            .. Enumerable.Range(0, WorkerCount)
                .Select(index =>
                    Task.Run(() =>
                    {
                        start.SignalAndWait();
                        AssemblyReferenceIdentity identity =
                            AssemblyReferenceIdentity.From(
                                reference,
                                AssemblyReferenceIdentity
                                    .RetainedProjection(reader));
                        MetadataTypeDefinitionKind kind =
                            MetadataTypeDeclarationProbe
                                .ClassifyDefinitionKind(
                                    reader,
                                    types[index],
                                    declaringAssemblyDefinesCoreLibraryRoot:
                                        false);
                        return (identity, kind);
                    })),
        ];

        (
            AssemblyReferenceIdentity Identity,
            MetadataTypeDefinitionKind Kind)[] results =
                await Task.WhenAll(tasks);
        AssemblyReferenceIdentity first = results[0].Identity;
        Assert.All(
            results,
            result =>
            {
                Assert.Same(first, result.Identity);
                Assert.Equal(
                    MetadataTypeDefinitionKind.Class,
                    result.Kind);
            });
    }

    [Fact]
    public void InvalidAssemblyReferenceTokenLengthIsRejected()
    {
        byte[] image =
            BuildTypesWithSharedExternalBase(
                count: 1,
                new byte[1024]);
        using var pe = Reader(image);
        MetadataReader reader = pe.GetMetadataReader();

        Assert.Throws<BadImageFormatException>(
            () => AssemblyReferenceIdentity.From(
                reader,
                Assert.Single(reader.AssemblyReferences)));
    }

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
        ApiSurfaceInspectionFailure failure =
            Assert.Single(surface.InspectionFailures);
        Assert.Contains(
            "could not be bound",
            failure.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NestedFakeSystemObjectDoesNotAuthenticateCoreLibrary()
    {
        var (image, parameter) =
            BuildNestedFakeSystemObjectConstraintSample();
        using var pe = Reader(image);

        TypeParameterTypeKind kind =
            TypeParameterKindClassifier.Classify(
                pe.GetMetadataReader(),
                parameter,
                hasValueTypeConstraint: false,
                hasReferenceTypeConstraint: false,
                new TypeParameterKindClassifier.ChainState());

        Assert.Equal(
            TypeParameterTypeKind.ReferenceType,
            kind);
    }

    [Fact]
    public void AmbiguousSystemObjectDoesNotAuthenticateCoreLibrary()
    {
        var (image, parameter) =
            BuildAmbiguousSystemObjectConstraintSample();
        using var pe = Reader(image);

        TypeParameterTypeKind kind =
            TypeParameterKindClassifier.Classify(
                pe.GetMetadataReader(),
                parameter,
                hasValueTypeConstraint: false,
                hasReferenceTypeConstraint: false,
                new TypeParameterKindClassifier.ChainState());

        Assert.Equal(
            TypeParameterTypeKind.ReferenceType,
            kind);
    }

    [Fact]
    public void NestedFakeSystemObjectInCoreNamedAssemblyDoesNotAuthenticateCoreLibrary()
    {
        AssemblyName coreIdentity =
            typeof(object).Assembly.GetName();
        byte[] hostileCore =
            BuildCoreNamedNestedFakeSystemObject(
                coreIdentity);
        byte[] consumerImage =
            BuildCoreObjectConsumer(coreIdentity);
        ResolvedAssemblyReference source =
            Descriptor(consumerImage);
        ResolvedAssemblyReference hostile =
            Descriptor(hostileCore);
        using var pe = Reader(consumerImage);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(hostile));

        Assert.Equal(
            TypeParameterTypeKind.ReferenceType,
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
        Assert.Empty(surface.InspectionFailures);
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
        Assert.Empty(surface.InspectionFailures);
    }

    [Theory]
    [InlineData("ValueType")]
    [InlineData("Enum")]
    public void SameImageTypeDefinitionBasedOnAuthenticCoreMarkerStaysUnknown(
        string baseName)
    {
        AssemblyName coreIdentity = typeof(object).Assembly.GetName();
        byte[] image =
            BuildSameImageMarkedDerivedConsumer(
                coreIdentity,
                baseName);
        ResolvedAssemblyReference source = Descriptor(image);
        ResolvedAssemblyReference core =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(object).Assembly.Location,
                AssemblyResolutionProvenance.Local("test"));
        using var pe = Reader(image);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(core));

        ApiType consumer = Assert.Single(
            surface.Types,
            type => type.Name == "Consumer`1");
        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            Assert.Single(consumer.TypeParameters).TypeKind);
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
        ApiSurfaceInspectionFailure failure =
            Assert.Single(surface.InspectionFailures);
        Assert.Contains(
            "generic arity 0",
            failure.Detail,
            StringComparison.Ordinal);
        Assert.Contains(
            "uses arity 1",
            failure.Detail,
            StringComparison.Ordinal);
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

        Assert.Empty(surface.Types);
        ApiSurfaceInspectionFailure failure =
            Assert.Single(surface.InspectionFailures);
        Assert.Equal("type name", failure.Operation);
        Assert.Equal(
            MetadataTypeNameFailureMechanism.Signature,
            failure.Mechanism);
        Assert.Equal(
            SignatureDecodeRejectionKind.UnsafeStructure.ToString(),
            failure.Kind);
        Assert.Contains(
            "TypeSpec exceeds the structural safety limit",
            failure.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TypeSpecificationRootRejectsTrailingSignatureBytes()
    {
        byte[] image = BuildTrailingConstraintConsumer();
        using var pe = Reader(image);
        MetadataReader reader = pe.GetMetadataReader();
        Assert.Equal(
            1,
            reader.GetTableRowCount(TableIndex.TypeSpec));
        TypeSpecificationHandle handle =
            MetadataTokens.TypeSpecificationHandle(1);

        Assert.False(
            TypeSpecificationRoot.TryRead(
                reader,
                handle,
                out _));
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
    public void DependencyOpenFailureIsVisibleOnApiSurface()
    {
        byte[] dependencyImage =
            BuildSimpleType("Dependency", "Base0");
        byte[] consumerImage = BuildTwoConstraintConsumer();
        ResolvedAssemblyReference source = Descriptor(consumerImage);
        ResolvedAssemblyReference dependency =
            UnreadableDescriptor(dependencyImage);
        using var pe = Reader(consumerImage);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(dependency));

        Assert.All(
            surface.Types,
            static type => Assert.Equal(
                TypeParameterTypeKind.Undetermined,
                Assert.Single(type.TypeParameters).TypeKind));
        ApiSurfaceInspectionFailure failure =
            Assert.Single(surface.InspectionFailures);
        Assert.Equal(
            "resolve generic parameter constraints",
            failure.Operation);
        Assert.Contains(
            "dependency could not be opened",
            failure.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TransitiveDependencyOpenFailureIsVisibleOnApiSurface()
    {
        byte[] dependencyImage =
            BuildSimpleType("Dependency", "Base`1");
        byte[] derivedImage =
            BuildDerivedWithExternalConstructedBase();
        byte[] consumerImage =
            BuildConsumer(
                "Consumer",
                "Derived",
                "Derived",
                constructed: false);
        ResolvedAssemblyReference source = Descriptor(consumerImage);
        ResolvedAssemblyReference derived = Descriptor(derivedImage);
        ResolvedAssemblyReference dependency =
            UnreadableDescriptor(dependencyImage);
        using var pe = Reader(consumerImage);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(derived, dependency));

        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            Assert.Single(Assert.Single(surface.Types).TypeParameters)
                .TypeKind);
        ApiSurfaceInspectionFailure failure =
            Assert.Single(surface.InspectionFailures);
        Assert.Contains(
            "dependency could not be opened",
            failure.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TransitiveUnboundDependencyIsVisibleOnApiSurface()
    {
        byte[] derivedImage =
            BuildDerivedWithExternalConstructedBase();
        byte[] consumerImage =
            BuildConsumer(
                "Consumer",
                "Derived",
                "Derived",
                constructed: false);
        ResolvedAssemblyReference source = Descriptor(consumerImage);
        ResolvedAssemblyReference derived = Descriptor(derivedImage);
        using var pe = Reader(consumerImage);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(derived));

        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            Assert.Single(Assert.Single(surface.Types).TypeParameters)
                .TypeKind);
        ApiSurfaceInspectionFailure failure =
            Assert.Single(surface.InspectionFailures);
        Assert.Contains(
            "transitive generic-constraint dependency",
            failure.Detail,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "Dependency",
            Assert.IsType<AssemblyReferenceIdentity>(
                failure.DependencyAssembly)
            .Name);
    }

    [Fact]
    public void ForwardedUnboundDependencyPreservesTerminalAssemblyIdentity()
    {
        byte[] consumerImage =
            BuildConsumer(
                "Consumer",
                "Facade",
                "ForwardedType",
                constructed: false);
        byte[] facadeImage =
            BuildForwarder(
                "Facade",
                "Target",
                "ForwardedType");
        ResolvedAssemblyReference source =
            Descriptor(consumerImage);
        ResolvedAssemblyReference facade =
            Descriptor(facadeImage);
        using var pe = Reader(consumerImage);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(facade));

        ApiSurfaceInspectionFailure failure =
            Assert.Single(surface.InspectionFailures);
        Assert.Equal(
            "Target",
            Assert.IsType<AssemblyReferenceIdentity>(
                failure.DependencyAssembly)
            .Name);
    }

    [Fact]
    public void ForwardedModuleExportRejectionPreservesTerminalAssemblyIdentity()
    {
        byte[] consumerImage =
            BuildConsumer(
                "Consumer",
                "Facade",
                "Type",
                constructed: false);
        byte[] facadeImage =
            BuildForwarder(
                "Facade",
                "Target",
                "Type");
        byte[] targetImage =
            BuildModuleExportTarget(
                "Target",
                "Part.netmodule");
        ResolvedAssemblyReference source =
            Descriptor(consumerImage);
        ResolvedAssemblyReference facade =
            Descriptor(facadeImage);
        ResolvedAssemblyReference target =
            Descriptor(targetImage);
        using var pe = Reader(consumerImage);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(facade, target));

        ApiSurfaceInspectionFailure failure =
            Assert.Single(surface.InspectionFailures);
        Assert.Equal(
            "Target",
            Assert.IsType<AssemblyReferenceIdentity>(
                failure.DependencyAssembly)
            .Name);
    }

    [Fact]
    public void RecursiveForwardedModuleExportRejectionPreservesTerminalAssemblyIdentity()
    {
        byte[] consumerImage =
            BuildConsumer(
                "Consumer",
                "Facade",
                "Type",
                constructed: false);
        byte[] facadeImage =
            BuildForwarder(
                "Facade",
                "Middle",
                "Type");
        byte[] middleImage =
            BuildForwarder(
                "Middle",
                "Target",
                "Type");
        byte[] targetImage =
            BuildModuleExportTarget(
                "Target",
                "Part.netmodule");
        ResolvedAssemblyReference source =
            Descriptor(consumerImage);
        ResolvedAssemblyReference facade =
            Descriptor(facadeImage);
        ResolvedAssemblyReference middle =
            Descriptor(middleImage);
        ResolvedAssemblyReference target =
            Descriptor(targetImage);
        using var pe = Reader(consumerImage);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(
                facade,
                middle,
                target));

        ApiSurfaceInspectionFailure failure =
            Assert.Single(surface.InspectionFailures);
        Assert.Equal(
            "Target",
            Assert.IsType<AssemblyReferenceIdentity>(
                failure.DependencyAssembly)
            .Name);
    }

    [Fact]
    public void RecursiveForwardedModuleKindFailurePreservesTerminalAssemblyIdentity()
    {
        byte[] consumerImage =
            BuildConsumer(
                "Consumer",
                "Outer",
                "Outer",
                constructed: false);
        byte[] outerImage =
            BuildTypeWithExternalConstructedBase(
                "Outer",
                "Outer",
                genericDefinition: false,
                "Facade",
                "Type`1");
        byte[] facadeImage =
            BuildForwarder(
                "Facade",
                "Middle",
                "Type`1");
        byte[] middleImage =
            BuildForwarder(
                "Middle",
                "Target",
                "Type`1");
        byte[] targetImage =
            BuildModuleExportTarget(
                "Target",
                "Part.netmodule",
                "Type`1");
        ResolvedAssemblyReference source =
            Descriptor(consumerImage);
        ResolvedAssemblyReference outer =
            Descriptor(outerImage);
        ResolvedAssemblyReference facade =
            Descriptor(facadeImage);
        ResolvedAssemblyReference middle =
            Descriptor(middleImage);
        ResolvedAssemblyReference target =
            Descriptor(targetImage);
        using var pe = Reader(consumerImage);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(
                outer,
                facade,
                middle,
                target));

        ApiSurfaceInspectionFailure failure =
            Assert.Single(surface.InspectionFailures);
        Assert.Equal(
            "Target",
            Assert.IsType<AssemblyReferenceIdentity>(
                failure.DependencyAssembly)
            .Name);
    }

    [Fact]
    public void SameImageConstructedBaseHopPreservesTerminalKindDependency()
    {
        byte[] dependencyImage =
            BuildGenericType("Dependency", "Base`1");
        byte[] middleImage =
            BuildSameImageConstructedBaseHop();
        byte[] consumerImage =
            BuildConsumer(
                "Consumer",
                "Middle",
                "Outer`1",
                constructed: true);
        ResolvedAssemblyReference source = Descriptor(consumerImage);
        ResolvedAssemblyReference middle = Descriptor(middleImage);
        ResolvedAssemblyReference dependency =
            Descriptor(dependencyImage);
        using var pe = Reader(consumerImage);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(middle, dependency));

        Assert.Equal(
            TypeParameterTypeKind.ReferenceType,
            Assert.Single(Assert.Single(surface.Types).TypeParameters)
                .TypeKind);
        Assert.Empty(surface.InspectionFailures);
    }

    [Fact]
    public void SameImageConstructedBaseHopPreservesTerminalFailure()
    {
        byte[] dependencyImage =
            BuildGenericType("Dependency", "Base`1");
        byte[] middleImage =
            BuildSameImageConstructedBaseHop();
        byte[] consumerImage =
            BuildConsumer(
                "Consumer",
                "Middle",
                "Outer`1",
                constructed: true);
        ResolvedAssemblyReference source = Descriptor(consumerImage);
        ResolvedAssemblyReference middle = Descriptor(middleImage);
        ResolvedAssemblyReference dependency =
            UnreadableDescriptor(dependencyImage);
        using var pe = Reader(consumerImage);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(middle, dependency));

        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            Assert.Single(Assert.Single(surface.Types).TypeParameters)
                .TypeKind);
        Assert.Contains(
            "dependency could not be opened",
            Assert.Single(surface.InspectionFailures).Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SameImageConstraintPreservesTerminalKindDependency()
    {
        byte[] dependencyImage =
            BuildGenericType("Dependency", "Base`1");
        byte[] sourceImage =
            BuildSameImageConstructedBaseHop(
                includeConsumer: true);
        ResolvedAssemblyReference source = Descriptor(sourceImage);
        ResolvedAssemblyReference dependency =
            Descriptor(dependencyImage);
        using var pe = Reader(sourceImage);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(dependency));

        ApiType consumer = Assert.Single(
            surface.Types,
            static type => type.Name == "Consumer`1");
        Assert.Equal(
            TypeParameterTypeKind.ReferenceType,
            Assert.Single(consumer.TypeParameters).TypeKind);
        Assert.Empty(surface.InspectionFailures);
    }

    [Fact]
    public void SameImageConstraintPreservesTerminalFailure()
    {
        byte[] dependencyImage =
            BuildGenericType("Dependency", "Base`1");
        byte[] sourceImage =
            BuildSameImageConstructedBaseHop(
                includeConsumer: true);
        ResolvedAssemblyReference source = Descriptor(sourceImage);
        ResolvedAssemblyReference dependency =
            UnreadableDescriptor(dependencyImage);
        using var pe = Reader(sourceImage);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MappingPolicy(dependency));

        ApiType consumer = Assert.Single(
            surface.Types,
            static type => type.Name == "Consumer`1");
        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            Assert.Single(consumer.TypeParameters).TypeKind);
        Assert.Contains(
            "dependency could not be opened",
            Assert.Single(surface.InspectionFailures).Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TransitiveDependencyOpenFailurePreservesResolvedIdentity()
    {
        byte[] dependencyImage =
            BuildSimpleType("Dependency", "Base`1");
        byte[] derivedImage =
            BuildDerivedWithExternalConstructedBase();
        ResolvedAssemblyReference derived = Descriptor(derivedImage);
        ResolvedAssemblyReference dependency =
            UnreadableDescriptor(dependencyImage);
        TypeResolutionRequest request =
            Request(derived, "Derived");
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            new MappingPolicy(dependency),
            [derived],
            [request]);

        ResolvedTypeDefinition definition =
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                context.Resolve(request)).Definition;

        Assert.Equal(
            MetadataTypeDefinitionKind.Unknown,
            definition.Kind);
        Assert.IsType<TypeResolutionFailure.CandidateOpenFailed>(
            definition.KindResolutionFailure);
    }

    [Fact]
    public void MultiHopKindFailureRemainsVisibleAndPreservesResolvedIdentity()
    {
        byte[] terminalImage =
            BuildGenericType("Terminal", "Base`1");
        byte[] middleImage =
            BuildTypeWithExternalConstructedBase(
                "Middle",
                "Middle`1",
                genericDefinition: true,
                "Terminal",
                "Base`1");
        byte[] outerImage =
            BuildTypeWithExternalConstructedBase(
                "Outer",
                "Outer",
                genericDefinition: false,
                "Middle",
                "Middle`1");
        byte[] consumerImage =
            BuildConsumer(
                "Consumer",
                "Outer",
                "Outer",
                constructed: false);
        ResolvedAssemblyReference terminal =
            UnreadableDescriptor(terminalImage);
        ResolvedAssemblyReference middle = Descriptor(middleImage);
        ResolvedAssemblyReference outer = Descriptor(outerImage);
        ResolvedAssemblyReference consumer = Descriptor(consumerImage);
        var policy = new MappingPolicy(
            outer,
            middle,
            terminal);
        TypeResolutionRequest request = Request(outer, "Outer");

        using (var catalog = new TypeResolutionCatalog())
        using (TypeResolutionContext context = catalog.CreateContext(
            policy,
            [outer],
            [request]))
        {
            ResolvedTypeDefinition definition =
                Assert.IsType<TypeResolutionOutcome.Resolved>(
                    context.Resolve(request)).Definition;
            Assert.Equal(
                MetadataTypeDefinitionKind.Unknown,
                definition.Kind);
            Assert.IsType<TypeResolutionFailure.CandidateOpenFailed>(
                definition.KindResolutionFailure);
        }

        using var pe = Reader(consumerImage);
        using var extractionCatalog = new TypeResolutionCatalog();
        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            consumer,
            extractionCatalog,
            policy);

        Assert.Equal(
            TypeParameterTypeKind.Undetermined,
            Assert.Single(Assert.Single(surface.Types).TypeParameters)
                .TypeKind);
        Assert.Single(surface.InspectionFailures);
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
            Assert.Single(
                surface.InspectionFailures,
                failure => failure.Detail.Contains(
                    "configured budget of 1",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            surface.InspectionFailures,
            failure => failure.Detail.Contains(
                "absent from the frozen type-resolution plan",
                StringComparison.Ordinal));
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

    [Fact]
    public void DistinctResolutionFailuresKeepCompleteSubjectsButBoundOutput()
    {
        const int Count = 100;
        byte[] image =
            BuildManyMissingConstraintConsumers(Count);
        ResolvedAssemblyReference source = Descriptor(image);
        using var pe = Reader(image);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MissingPolicy());

        Assert.Equal(
            Count,
            surface.ConstraintResolutionFailuresBySubject.Count);
        Assert.Equal(
            ApiSurface.MaxVisibleConstraintResolutionFailures + 1,
            surface.InspectionFailures.Count);
        Assert.Contains(
            surface.InspectionFailures,
            failure => failure.Kind == "ResourceLimit"
                && failure.Detail.Contains(
                    "suppressed",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void DistinctResolutionFailuresOnOneSubjectArePreserved()
    {
        byte[] image =
            BuildConsumerWithTwoMissingConstraints();
        ResolvedAssemblyReference source = Descriptor(image);
        using var pe = Reader(image);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MissingPolicy());

        List<ApiSurfaceInspectionFailure> failures =
            Assert.Single(
                surface.ConstraintResolutionFailuresBySubject)
                .Value;
        Assert.Equal(2, failures.Count);
        Assert.Contains(
            failures,
            failure => failure.Detail.Contains(
                "MissingA",
                StringComparison.Ordinal));
        Assert.Contains(
            failures,
            failure => failure.Detail.Contains(
                "MissingB",
                StringComparison.Ordinal));
    }

    [Fact]
    public void SameNamedResolutionFailuresFromDistinctAssembliesArePreserved()
    {
        byte[] image =
            BuildConsumerWithSameNamedMissingConstraints();
        ResolvedAssemblyReference source = Descriptor(image);
        using var pe = Reader(image);
        using var catalog = new TypeResolutionCatalog();

        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            source,
            catalog,
            new MissingPolicy());

        List<ApiSurfaceInspectionFailure> failures =
            Assert.Single(
                surface.ConstraintResolutionFailuresBySubject)
            .Value;
        Assert.Equal(2, failures.Count);
        Assert.Equal(2, surface.InspectionFailures.Count);
        Assert.Equal(
            ["DependencyA", "DependencyB"],
            failures
                .Select(failure =>
                    Assert.IsType<AssemblyReferenceIdentity>(
                        failure.DependencyAssembly)
                    .Name)
                .Order()
                .ToArray());
        Assert.All(
            failures,
            failure => Assert.Contains(
                failure.DependencyAssembly!.Name,
                failure.Detail,
                StringComparison.Ordinal));
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

    static byte[] BuildManyMissingConstraintConsumers(int count)
    {
        MetadataBuilder metadata =
            NewMetadata("ManyMissingConstraints");
        AssemblyReferenceHandle missing =
            AddReference(metadata, "Missing");
        AddModule(metadata);
        for (int i = 0; i < count; i++)
        {
            TypeReferenceHandle constraint =
                metadata.AddTypeReference(
                    missing,
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

    static byte[] BuildConsumerWithTwoMissingConstraints()
    {
        MetadataBuilder metadata =
            NewMetadata("TwoMissingConstraints");
        AssemblyReferenceHandle missing =
            AddReference(metadata, "Missing");
        AddModule(metadata);
        TypeReferenceHandle first =
            metadata.AddTypeReference(
                missing,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("MissingA"));
        TypeReferenceHandle second =
            metadata.AddTypeReference(
                missing,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("MissingB"));
        TypeDefinitionHandle consumer =
            AddType(metadata, "Consumer`2");
        GenericParameterHandle firstParameter =
            metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                0);
        GenericParameterHandle secondParameter =
            metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("U"),
                1);
        metadata.AddGenericParameterConstraint(
            firstParameter,
            first);
        metadata.AddGenericParameterConstraint(
            secondParameter,
            second);
        return Serialize(metadata);
    }

    static byte[] BuildConsumerWithSameNamedMissingConstraints()
    {
        MetadataBuilder metadata =
            NewMetadata("SameNamedMissingConstraints");
        AssemblyReferenceHandle firstAssembly =
            AddReference(metadata, "DependencyA");
        AssemblyReferenceHandle secondAssembly =
            AddReference(metadata, "DependencyB");
        AddModule(metadata);
        TypeReferenceHandle first =
            metadata.AddTypeReference(
                firstAssembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Marker"));
        TypeReferenceHandle second =
            metadata.AddTypeReference(
                secondAssembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Marker"));
        TypeDefinitionHandle consumer =
            AddType(metadata, "Consumer`1");
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                0);
        metadata.AddGenericParameterConstraint(parameter, first);
        metadata.AddGenericParameterConstraint(parameter, second);
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

    static byte[] BuildSameImageTypeSpecificationBaseChain(
        int count)
    {
        MetadataBuilder metadata = NewMetadata("SameImageChain");
        AddModule(metadata);
        TypeDefinitionHandle[] definitions =
            new TypeDefinitionHandle[count];
        for (int i = count - 1; i >= 0; i--)
        {
            definitions[i] =
                AddType(
                    metadata,
                    $"T{i}",
                    i + 1 < count
                        ? AddConstructedClass(
                            metadata,
                            definitions[i + 1])
                        : default);
            AddGenericParameter(metadata, definitions[i]);
        }

        return Serialize(metadata);
    }

    static byte[]
        BuildSameImageConstraintWithExternalConstructedBase(
            bool multiHop)
    {
        MetadataBuilder metadata =
            NewMetadata("SameImageExternalBase");
        AssemblyReferenceHandle dependency =
            AddReference(metadata, "Dependency");
        TypeReferenceHandle externalBase =
            metadata.AddTypeReference(
                dependency,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Base`1"));
        AddModule(metadata);

        TypeDefinitionHandle constraint;
        if (multiHop)
        {
            TypeDefinitionHandle localBase =
                AddType(
                    metadata,
                    "LocalBase`1",
                    AddConstructedClass(
                        metadata,
                        externalBase));
            AddGenericParameter(metadata, localBase);
            constraint =
                AddType(
                    metadata,
                    "Derived",
                    AddConstructedClass(
                        metadata,
                        localBase));
        }
        else
        {
            constraint =
                AddType(
                    metadata,
                    "Derived",
                    AddConstructedClass(
                        metadata,
                        externalBase));
        }

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

    static byte[] BuildDeeplyNestedTypeSpecificationBase(int depth)
    {
        MetadataBuilder metadata =
            NewMetadata("NestedSignature");
        AddModule(metadata);
        TypeDefinitionHandle root =
            AddType(metadata, "Base`1");
        AddGenericParameter(metadata, root);
        var signature = new BlobBuilder();
        for (int i = 0; i < depth; i++)
        {
            signature.WriteByte(0x15);
            signature.WriteByte(0x12);
            signature.WriteCompressedInteger(
                EncodeTypeDefOrRef(root));
            signature.WriteCompressedInteger(1);
        }

        signature.WriteByte(0x08);
        AddType(
            metadata,
            "Derived",
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(signature)));
        return Serialize(metadata);
    }

    static byte[] BuildCyclicTypeSpecificationBase()
    {
        MetadataBuilder metadata =
            NewMetadata("CyclicTypeSpecification");
        AddModule(metadata);
        TypeDefinitionHandle root =
            AddType(metadata, "Base`1");
        AddGenericParameter(metadata, root);
        var signature = new BlobBuilder();
        signature.WriteByte(0x15);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(
            EncodeTypeDefOrRef(root));
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x1f);
        signature.WriteCompressedInteger(
            EncodeTypeDefOrRef(
                MetadataTokens.TypeSpecificationHandle(1)));
        signature.WriteByte(0x08);
        TypeSpecificationHandle specification =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(signature));
        AddType(metadata, "Derived", specification);
        return Serialize(metadata);
    }

    static byte[] BuildSharedAcyclicTypeSpecificationBase()
    {
        MetadataBuilder metadata =
            NewMetadata("SharedTypeSpecification");
        AddModule(metadata);
        TypeDefinitionHandle root =
            AddType(metadata, "Base`2");
        AddGenericParameter(metadata, root);
        metadata.AddGenericParameter(
            root,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("U"),
            1);
        var dependency = new BlobBuilder();
        dependency.WriteByte(0x08);
        metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(dependency));
        var signature = new BlobBuilder();
        signature.WriteByte(0x15);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(
            EncodeTypeDefOrRef(root));
        signature.WriteCompressedInteger(2);
        for (int i = 0; i < 2; i++)
        {
            signature.WriteByte(0x1f);
            signature.WriteCompressedInteger(
                EncodeTypeDefOrRef(
                    MetadataTokens.TypeSpecificationHandle(1)));
            signature.WriteByte(0x08);
        }

        TypeSpecificationHandle specification =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(signature));
        AddType(metadata, "Derived", specification);
        return Serialize(metadata);
    }

    static byte[] BuildInvalidlyNumberedInterfaceWithExternalBase()
    {
        MetadataBuilder metadata = NewMetadata("Middle");
        AssemblyReferenceHandle dependency =
            AddReference(metadata, "Dependency");
        TypeReferenceHandle root =
            metadata.AddTypeReference(
                dependency,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Base`1"));
        AddModule(metadata);
        TypeDefinitionHandle definition =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Middle`2"),
                AddConstructedClass(metadata, root),
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddGenericParameter(
            definition,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            1);
        metadata.AddGenericParameter(
            definition,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("U"),
            0);
        return Serialize(metadata);
    }

    static byte[] BuildTypesWithSharedExternalBase(
        int count,
        byte[] token)
    {
        MetadataBuilder metadata =
            NewMetadata("SharedAssemblyReference");
        AssemblyReferenceHandle dependency =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Dependency"),
                new Version(1, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(token),
                default,
                default);
        TypeReferenceHandle root =
            metadata.AddTypeReference(
                dependency,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Base`1"));
        AddModule(metadata);
        TypeSpecificationHandle baseType =
            AddConstructedClass(metadata, root);
        for (int i = 0; i < count; i++)
            AddType(metadata, $"Derived{i}", baseType);
        return Serialize(metadata);
    }

    static byte[] BuildDerivedWithExternalConstructedBase() =>
        BuildTypeWithExternalConstructedBase(
            "Derived",
            "Derived",
            genericDefinition: false,
            "Dependency",
            "Base`1");

    static byte[] BuildSameImageConstructedBaseHop(
        bool includeConsumer = false)
    {
        MetadataBuilder metadata = NewMetadata("Middle");
        AssemblyReferenceHandle dependency =
            AddReference(metadata, "Dependency");
        TypeReferenceHandle terminal =
            metadata.AddTypeReference(
                dependency,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Base`1"));
        AddModule(metadata);
        TypeDefinitionHandle inner =
            AddType(
                metadata,
                "Inner`1",
                AddConstructedClass(metadata, terminal));
        AddGenericParameter(metadata, inner);
        TypeDefinitionHandle outer =
            AddType(
                metadata,
                "Outer`1",
                AddConstructedClass(metadata, inner));
        AddGenericParameter(metadata, outer);
        if (includeConsumer)
        {
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
                AddConstructedClass(metadata, outer));
        }
        return Serialize(metadata);
    }

    static byte[] BuildTypeWithExternalConstructedBase(
        string assemblyName,
        string typeName,
        bool genericDefinition,
        string targetAssembly,
        string targetType)
    {
        MetadataBuilder metadata = NewMetadata(assemblyName);
        AssemblyReferenceHandle dependency =
            AddReference(metadata, targetAssembly);
        TypeReferenceHandle root =
            metadata.AddTypeReference(
                dependency,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(targetType));
        AddModule(metadata);
        TypeDefinitionHandle definition = AddType(
            metadata,
            typeName,
            AddConstructedClass(metadata, root));
        if (genericDefinition)
            AddGenericParameter(metadata, definition);
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

    static byte[] BuildGenericInterface(
        string assemblyName,
        string typeName)
    {
        MetadataBuilder metadata = NewMetadata(assemblyName);
        AddModule(metadata);
        TypeDefinitionHandle definition =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(typeName),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
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

    static (
        byte[] Image,
        ImmutableArray<TypeDefinitionHandle> Types)
        BuildDirectCoreBaseSample(
            int typeCount,
            int publicKeyBytes)
    {
        MetadataBuilder metadata =
            NewMetadata("DirectCoreBase");
        BlobHandle publicKey =
            metadata.GetOrAddBlob(new byte[publicKeyBytes]);
        AssemblyReferenceHandle core =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(10, 0, 0, 0),
                culture: default,
                publicKeyOrToken: publicKey,
                flags: AssemblyFlags.PublicKey,
                hashValue: default);
        TypeReferenceHandle valueType =
            metadata.AddTypeReference(
                core,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("ValueType"));
        AddModule(metadata);
        var types =
            ImmutableArray.CreateBuilder<TypeDefinitionHandle>(
                typeCount);
        for (int i = 0; i < typeCount; i++)
        {
            types.Add(
                AddType(
                    metadata,
                    $"Derived{i}",
                    valueType));
        }

        return (Serialize(metadata), types.MoveToImmutable());
    }

    static (
        byte[] Image,
        GenericParameterHandle Parameter)
        BuildNestedFakeSystemObjectConstraintSample()
    {
        MetadataBuilder metadata =
            NewMetadata("NestedFakeCore");
        AddModule(metadata);
        TypeDefinitionHandle fakeObject =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic
                    | TypeAttributes.Class,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
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
            fakeObject);
        return (Serialize(metadata), parameter);
    }

    static (
        byte[] Image,
        GenericParameterHandle Parameter)
        BuildAmbiguousSystemObjectConstraintSample()
    {
        MetadataBuilder metadata =
            NewMetadata("AmbiguousFakeCore");
        AddModule(metadata);
        TypeDefinitionHandle first =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
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
            first);
        return (Serialize(metadata), parameter);
    }

    static byte[] BuildCoreNamedNestedFakeSystemObject(
        AssemblyName identity)
    {
        MetadataBuilder metadata =
            NewMetadata(
                identity.Name!,
                identity.GetPublicKey());
        AddModule(metadata);
        metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic
                | TypeAttributes.Class,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildCoreObjectConsumer(
        AssemblyName targetAssembly)
    {
        MetadataBuilder metadata =
            NewMetadata("CoreObjectConsumer");
        AssemblyReferenceHandle reference =
            AddReference(metadata, targetAssembly);
        TypeReferenceHandle target =
            metadata.AddTypeReference(
                reference,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
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
            target);
        return Serialize(metadata);
    }

    static byte[] BuildForwarder(
        string assemblyName,
        string targetAssembly,
        string targetType)
    {
        MetadataBuilder metadata =
            NewMetadata(assemblyName);
        AddModule(metadata);
        AssemblyReferenceHandle target =
            AddReference(metadata, targetAssembly);
        metadata.AddExportedType(
            TypeAttributes.Public | Forwarder,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString(targetType),
            target,
            typeDefinitionId: 0);
        return Serialize(metadata);
    }

    static byte[] BuildModuleExportTarget(
        string assemblyName,
        string moduleName,
        string typeName = "Type")
    {
        MetadataBuilder metadata =
            NewMetadata(assemblyName);
        AddModule(metadata);
        AssemblyFileHandle module =
            metadata.AddAssemblyFile(
                metadata.GetOrAddString(moduleName),
                metadata.GetOrAddBlob(
                    new byte[] { 1, 2, 3 }),
                containsMetadata: true);
        metadata.AddExportedType(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString(typeName),
            module,
            typeDefinitionId: 1);
        return Serialize(metadata);
    }

    static byte[] BuildSameImageMarkedDerivedConsumer(
        AssemblyName coreAssembly,
        string coreTypeName)
    {
        MetadataBuilder metadata =
            NewMetadata("SameImageMarkedDerived");
        AssemblyReferenceHandle reference =
            AddReference(metadata, coreAssembly);
        TypeReferenceHandle coreType =
            metadata.AddTypeReference(
                reference,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString(coreTypeName));
        AddModule(metadata);
        TypeDefinitionHandle derived =
            AddType(
                metadata,
                "Derived",
                AddClassSpecification(metadata, coreType));
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
            derived);
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

    static MetadataBuilder NewMetadata(
        string assemblyName,
        byte[]? publicKey = null)
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
            publicKey is null
                ? default
                : metadata.GetOrAddBlob(publicKey),
            publicKey is null
                ? default
                : AssemblyFlags.PublicKey,
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

    static ResolvedAssemblyReference UnreadableDescriptor(byte[] image)
    {
        using var pe = Reader(image);
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                pe.GetMetadataReader());
        return ResolvedAssemblyReference.Create(
            identity,
            path: null,
            () => throw new IOException("Synthetic read failure."),
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

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                request.Target
                is AssemblyBindingTarget.AssemblyReference reference
                && map.TryGetValue(
                reference.Identity.Name,
                out ResolvedAssemblyReference? assembly)
                ? AssemblyBindingSelection.Found(assembly)
                : AssemblyBindingSelection.NotFound();
        }
    }

    sealed class MissingPolicy : IAssemblyBindingPolicy
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
                AssemblyBindingSelection.NotFound();
        }
    }
}
