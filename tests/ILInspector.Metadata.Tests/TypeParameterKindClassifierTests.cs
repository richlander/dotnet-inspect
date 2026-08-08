using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public class TypeParameterKindClassifierTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Classify_IllegalConstraintWrapperStaysUndetermined(
        bool pinned)
    {
        GenericParameterHandle parameter = default;
        using MetadataImage image = BuildMetadata(metadata =>
        {
            TypeDefinitionHandle modifier = AddTypeDefinition(
                metadata,
                "Modifier");
            TypeDefinitionHandle baseType = AddTypeDefinition(
                metadata,
                "Base");
            TypeDefinitionHandle consumer = AddTypeDefinition(
                metadata,
                "Consumer`1");
            parameter = metadata.AddGenericParameter(
                consumer,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);

            var signature = new BlobBuilder();
            if (pinned)
            {
                signature.WriteByte(0x45); // PINNED
            }
            else
            {
                signature.WriteByte(0x1f); // CMOD_REQD
                signature.WriteCompressedInteger(
                    MetadataTokens.GetRowNumber(modifier) << 2);
            }

            signature.WriteByte(0x12); // CLASS
            signature.WriteCompressedInteger(
                MetadataTokens.GetRowNumber(baseType) << 2);
            TypeSpecificationHandle wrapped =
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(signature));
            metadata.AddGenericParameterConstraint(
                parameter,
                wrapped);
        });

        TypeParameterTypeKind kind =
            TypeParameterKindClassifier.Classify(
                image.Reader,
                parameter,
                hasValueTypeConstraint: false,
                hasReferenceTypeConstraint: false,
                new TypeParameterKindClassifier.ChainState());

        Assert.Equal(TypeParameterTypeKind.Undetermined, kind);
    }

    [Fact]
    public void ResolutionPlan_CachesSharedLargePublicKeyAcrossDuplicateRows()
    {
        const int ReferenceCount = 32;
        var references = new List<TypeReferenceHandle>();
        using MetadataImage image = BuildMetadata(metadata =>
        {
            var key = new BlobBuilder();
            key.WriteBytes(new byte[1024 * 1024]);
            BlobHandle keyHandle = metadata.GetOrAddBlob(key);
            StringHandle assemblyName =
                metadata.GetOrAddString("LargeKeyDependency");
            StringHandle typeNamespace = metadata.GetOrAddString("N");
            StringHandle typeName = metadata.GetOrAddString("Base");

            for (int i = 0; i < ReferenceCount; i++)
            {
                AssemblyReferenceHandle assembly =
                    metadata.AddAssemblyReference(
                        assemblyName,
                        new Version(1, 0, 0, 0),
                        culture: default,
                        publicKeyOrToken: keyHandle,
                        flags: AssemblyFlags.PublicKey,
                        hashValue: default);
                references.Add(metadata.AddTypeReference(
                    assembly,
                    typeNamespace,
                    typeName));
            }
        });
        ResolvedAssemblyReference source =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(TypeParameterKindClassifierTests)
                    .Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    nameof(
                        ResolutionPlan_CachesSharedLargePublicKeyAcrossDuplicateRows)));
        var plan =
            new TypeParameterKindClassifier.ResolutionPlan(source);

        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (TypeReferenceHandle reference in references)
        {
            Assert.Equal(
                TypeParameterKindClassifier.ConstraintClass.Unreadable,
                plan.Classify(image.Reader, reference));
        }
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Single(plan.Requests);
        Assert.InRange(allocated, 0, 8 * 1024 * 1024);
    }

    [Fact]
    public void Extract_RejectedTypeDoesNotConsumeResolutionCandidate()
    {
        string rejectedPath = WriteAssembly(
            $"RejectedDependency{Guid.NewGuid():N}",
            "RejectedBase");
        string validPath = WriteAssembly(
            $"ValidDependency{Guid.NewGuid():N}",
            "ValidBase");
        string consumerPath = Path.Combine(
            Path.GetTempPath(),
            $"constraint-rollback-{Guid.NewGuid():N}.dll");

        try
        {
            ResolvedAssemblyReference rejected =
                ResolvedAssemblyReference.CreateFromPath(
                    rejectedPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_RejectedTypeDoesNotConsumeResolutionCandidate)));
            ResolvedAssemblyReference valid =
                ResolvedAssemblyReference.CreateFromPath(
                    validPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_RejectedTypeDoesNotConsumeResolutionCandidate)));
            File.WriteAllBytes(
                consumerPath,
                BuildConsumer(
                    rejected.Identity.Name,
                    valid.Identity.Name));
            ResolvedAssemblyReference source =
                ResolvedAssemblyReference.CreateFromPath(
                    consumerPath,
                    AssemblyResolutionProvenance.Local(
                        nameof(
                            Extract_RejectedTypeDoesNotConsumeResolutionCandidate)));

            using var pe =
                new PEReader(File.OpenRead(consumerPath));
            using var catalog = new TypeResolutionCatalog(
                new TypeResolutionContextOptions
                {
                    MaxCandidates = 2,
                });
            ApiSurface surface = ApiSurfaceExtractor.Extract(
                pe,
                source,
                catalog,
                new MappingPolicy(rejected, valid));

            Assert.DoesNotContain(
                surface.Types,
                type => type.Name == "Rejected`1");
            ApiType accepted = Assert.Single(
                surface.Types,
                type => type.Name == "Accepted`1");
            Assert.Equal(
                TypeParameterTypeKind.ReferenceType,
                Assert.Single(accepted.TypeParameters).TypeKind);
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(rejectedPath);
            File.Delete(validPath);
        }
    }

    static byte[] BuildConsumer(
        string rejectedAssemblyName,
        string validAssemblyName)
    {
        var metadata = NewMetadata(
            $"ConstraintConsumer{Guid.NewGuid():N}");
        AssemblyReferenceHandle rejectedAssembly =
            AddAssemblyReference(
                metadata,
                rejectedAssemblyName);
        AssemblyReferenceHandle validAssembly =
            AddAssemblyReference(
                metadata,
                validAssemblyName);
        TypeReferenceHandle rejectedBase =
            metadata.AddTypeReference(
                rejectedAssembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("RejectedBase"));
        TypeReferenceHandle validBase =
            metadata.AddTypeReference(
                validAssembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("ValidBase"));

        AddTypeDefinition(metadata, "<Module>", TypeAttributes.NotPublic);
        TypeDefinitionHandle rejectedType =
            AddTypeDefinition(metadata, "Rejected`1");
        TypeDefinitionHandle acceptedType =
            AddTypeDefinition(metadata, "Accepted`1");
        GenericParameterHandle rejectedParameter =
            metadata.AddGenericParameter(
                rejectedType,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        metadata.AddGenericParameterConstraint(
            rejectedParameter,
            rejectedBase);
        GenericParameterHandle acceptedParameter =
            metadata.AddGenericParameter(
                acceptedType,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        metadata.AddGenericParameterConstraint(
            acceptedParameter,
            validBase);

        var malformedInterface = new BlobBuilder();
        malformedInterface.WriteByte(0xff);
        TypeSpecificationHandle malformed =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(malformedInterface));
        metadata.AddInterfaceImplementation(
            rejectedType,
            malformed);
        return SerializePe(metadata);
    }

    static string WriteAssembly(
        string assemblyName,
        string typeName)
    {
        var metadata = NewMetadata(assemblyName);
        AddTypeDefinition(metadata, "<Module>", TypeAttributes.NotPublic);
        AddTypeDefinition(metadata, typeName);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"{assemblyName}.dll");
        File.WriteAllBytes(path, SerializePe(metadata));
        return path;
    }

    static MetadataImage BuildMetadata(
        Action<MetadataBuilder> addRows)
    {
        MetadataBuilder metadata = NewMetadata("Synthetic");
        AddTypeDefinition(
            metadata,
            "<Module>",
            TypeAttributes.NotPublic);
        addRows(metadata);

        var root =
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true);
        var image = new BlobBuilder();
        root.Serialize(
            image,
            methodBodyStreamRva: 0,
            mappedFieldDataStreamRva: 0);
        return new MetadataImage(image.ToImmutableArray());
    }

    static MetadataBuilder NewMetadata(string assemblyName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName:
                metadata.GetOrAddString($"{assemblyName}.dll"),
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
        return metadata;
    }

    static TypeDefinitionHandle AddTypeDefinition(
        MetadataBuilder metadata,
        string name,
        TypeAttributes attributes = TypeAttributes.Public) =>
        metadata.AddTypeDefinition(
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

    static AssemblyReferenceHandle AddAssemblyReference(
        MetadataBuilder metadata,
        string name) =>
        metadata.AddAssemblyReference(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);

    static byte[] SerializePe(MetadataBuilder metadata)
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

    sealed class MetadataImage(
        ImmutableArray<byte> image) : IDisposable
    {
        readonly MetadataReaderProvider _provider =
            MetadataReaderProvider.FromMetadataImage(image);

        internal MetadataReader Reader =>
            _provider.GetMetadataReader();

        public void Dispose() => _provider.Dispose();
    }

    sealed class MappingPolicy(
        params ResolvedAssemblyReference[] assemblies)
        : IAssemblyBindingPolicy
    {
        readonly Dictionary<string, ResolvedAssemblyReference>
            _assemblies = assemblies.ToDictionary(
                assembly => assembly.Identity.Name,
                StringComparer.Ordinal);

        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request) =>
            request.Target
                is AssemblyBindingTarget.AssemblyReference reference
                && _assemblies.TryGetValue(
                    reference.Identity.Name,
                    out ResolvedAssemblyReference? assembly)
                ? AssemblyBindingSelection.Found(assembly)
                : AssemblyBindingSelection.NotFound();
    }
}
