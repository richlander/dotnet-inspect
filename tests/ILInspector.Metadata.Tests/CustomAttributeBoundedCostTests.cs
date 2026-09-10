using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class CustomAttributeBoundedCostTests
{
    [Theory]
    [InlineData(1, 4096)]
    [InlineData(1024, 1)]
    [InlineData(16, 16)]
    public void SharedNamespaceIndex_BelowBudgetPreservesValueAndCountsNameBytes(
        int definitionCount,
        int namespaceLength)
    {
        using var image = Open(BuildSharedNamespaceIndexImage(
            definitionCount,
            namespaceLength));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var work = new CustomAttributeValueDecoder.EnumResolutionWork();

        Assert.True(TryDecode(image.Reader, attribute, work, out var measured));
        CustomAttributeValue<string>? ordinary =
            AttributeDecoder.TryDecode(image.Reader, attribute);

        Assert.NotNull(ordinary);
        Assert.Equal(
            measured.FixedArguments[0],
            ordinary.Value.FixedArguments[0]);
        Assert.Equal(
            ExpectedTypeDefinitionIndexNameBytes(image.Reader),
            work.TypeDefinitionIndexNameBytes);
        Assert.Equal(0, work.TypeReferenceMatchNameBytes);
        Assert.Equal(
            work.TypeDefinitionIndexNameBytes,
            work.NameBytes);
    }

    [Fact]
    public void SharedNamespaceIndex_CrossProductStopsAtNameBudget()
    {
        using var image = Open(BuildSharedNamespaceIndexImage(
            definitionCount: 1024,
            namespaceLength: 4096));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var work = new CustomAttributeValueDecoder.EnumResolutionWork();

        bool measured = TryDecode(image.Reader, attribute, work, out _);
        CustomAttributeValue<string>? ordinary =
            AttributeDecoder.TryDecode(image.Reader, attribute);

        Assert.Equal(
            CustomAttributeValueDecoder.MaxEnumResolutionNameWork,
            work.NameBytes);
        Assert.Equal(ordinary is not null, measured);
        Assert.False(measured);
        Assert.Null(ordinary);
    }

    [Theory]
    [InlineData(1, 4096)]
    [InlineData(1024, 1)]
    [InlineData(16, 16)]
    public void InvariantReferenceName_BelowBudgetPreservesValueAndCountsNameBytes(
        int definitionCount,
        int nameLength)
    {
        using var image = Open(BuildInvariantReferenceNameImage(
            definitionCount,
            nameLength));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var work = new CustomAttributeValueDecoder.EnumResolutionWork();

        Assert.True(TryDecode(image.Reader, attribute, work, out var measured));
        CustomAttributeValue<string>? ordinary =
            AttributeDecoder.TryDecode(image.Reader, attribute);

        Assert.NotNull(ordinary);
        Assert.Equal(
            measured.FixedArguments[0],
            ordinary.Value.FixedArguments[0]);
        long expected =
            ((long)definitionCount + 1) * nameLength
            + (long)definitionCount * "Match".Length;
        Assert.Equal(expected, work.TypeReferenceMatchNameBytes);
        Assert.Equal(0, work.TypeDefinitionIndexNameBytes);
        Assert.Equal(expected, work.NameBytes);
    }

    [Fact]
    public void InvariantReferenceName_CrossProductStopsAtNameBudget()
    {
        using var image = Open(BuildInvariantReferenceNameImage(
            definitionCount: 1024,
            nameLength: 4096));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var work = new CustomAttributeValueDecoder.EnumResolutionWork();

        bool measured = TryDecode(image.Reader, attribute, work, out _);
        CustomAttributeValue<string>? ordinary =
            AttributeDecoder.TryDecode(image.Reader, attribute);

        Assert.Equal(
            CustomAttributeValueDecoder.MaxEnumResolutionNameWork,
            work.NameBytes);
        Assert.Equal(ordinary is not null, measured);
        Assert.False(measured);
        Assert.Null(ordinary);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void ResolutionNameWork_JointDimensionSweepsStayWithinBudget()
    {
        int[] rowCounts = [1, 4, 16, 64, 256, 512, 1024];
        int[] nameLengths = [1, 16, 64, 256, 1024, 4096, 8192];
        foreach (int rowCount in rowCounts)
        {
            foreach (int nameLength in nameLengths)
            {
                AssertNameWorkOutcome(
                    BuildSharedNamespaceIndexImage(rowCount, nameLength));
                AssertNameWorkOutcome(
                    BuildInvariantReferenceNameImage(rowCount, nameLength));
            }
        }
    }

    [Theory]
    [InlineData(1, 192)]
    [InlineData(192, 1)]
    [InlineData(8, 8)]
    public void DistinctUnresolvedEnums_BelowBudgetPreservesValuesAndCounts(
        int referenceCount,
        int decoyTypeCount)
    {
        using var image = Open(BuildImage(referenceCount, decoyTypeCount));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var work = new CustomAttributeValueDecoder.EnumResolutionWork();

        Assert.True(CustomAttributeValueDecoder.TryDecode(
            image.Reader,
            attribute,
            preserveSerializedTypeNames: false,
            captureDefaultedWidths: false,
            beforeMaterialize: null,
            enumUnderlyingType: null,
            out CustomAttributeValue<string> measured,
            out _,
            out _,
            enumResolutionWork: work));
        CustomAttributeValue<string>? ordinary =
            AttributeDecoder.TryDecode(image.Reader, attribute);

        Assert.NotNull(ordinary);
        Assert.Equal(referenceCount, measured.FixedArguments.Length);
        Assert.Equal(referenceCount, ordinary.Value.FixedArguments.Length);
        for (int i = 0; i < referenceCount; i++)
        {
            Assert.Equal(i, measured.FixedArguments[i].Value);
            Assert.Equal(
                measured.FixedArguments[i],
                ordinary.Value.FixedArguments[i]);
        }

        long expectedCandidates =
            referenceCount * image.Reader.TypeDefinitions.Count;
        Assert.Equal(
            expectedCandidates,
            work.TypeDefinitionCandidatesVisited);
        Assert.Equal(expectedCandidates, work.StructuralMatchFrames);
        Assert.Equal(expectedCandidates * 2, work.Operations);
    }

    [Fact]
    public void DistinctUnresolvedEnums_CrossProductStopsAtAggregateBudget()
    {
        using var image = Open(BuildImage(
            referenceCount: 192,
            decoyTypeCount: 192));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var work = new CustomAttributeValueDecoder.EnumResolutionWork();

        bool measured = CustomAttributeValueDecoder.TryDecode(
            image.Reader,
            attribute,
            preserveSerializedTypeNames: false,
            captureDefaultedWidths: false,
            beforeMaterialize: null,
            enumUnderlyingType: null,
            out _,
            out _,
            out _,
            enumResolutionWork: work);
        CustomAttributeValue<string>? ordinary =
            AttributeDecoder.TryDecode(image.Reader, attribute);

        Assert.Equal(ordinary is not null, measured);
        Assert.False(measured);
        Assert.Null(ordinary);
        Assert.Equal(
            CustomAttributeValueDecoder.MaxEnumResolutionWork,
            work.Operations);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void DistinctUnresolvedEnums_JointDimensionSweepStaysWithinBudget()
    {
        int[] dimensions =
        [
            1, 2, 4, 8, 16, 32, 64, 96,
            128, 160, 192, 256, 384, 512, 768, 1024,
        ];
        foreach (int referenceCount in dimensions)
        {
            foreach (int decoyTypeCount in dimensions)
            {
                using var image = Open(BuildImage(
                    referenceCount,
                    decoyTypeCount));
                CustomAttribute attribute = FirstAttribute(image.Reader);
                var work =
                    new CustomAttributeValueDecoder.EnumResolutionWork();

                bool measured = CustomAttributeValueDecoder.TryDecode(
                    image.Reader,
                    attribute,
                    preserveSerializedTypeNames: false,
                    captureDefaultedWidths: false,
                    beforeMaterialize: null,
                    enumUnderlyingType: null,
                    out _,
                    out _,
                    out _,
                    enumResolutionWork: work);
                bool ordinary =
                    AttributeDecoder.TryDecode(image.Reader, attribute)
                        is not null;
                long unboundedOperations =
                    2L
                    * referenceCount
                    * image.Reader.TypeDefinitions.Count;

                Assert.Equal(ordinary, measured);
                Assert.Equal(
                    unboundedOperations
                        <= CustomAttributeValueDecoder.MaxEnumResolutionWork,
                    measured);
                Assert.Equal(
                    Math.Min(
                        unboundedOperations,
                        CustomAttributeValueDecoder.MaxEnumResolutionWork),
                    work.Operations);
            }
        }
    }

    static byte[] BuildImage(int referenceCount, int decoyTypeCount)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("BoundedCost.dll"),
            metadata.GetOrAddGuid(
                new Guid("e9b384ad-266d-4e18-999e-c4ed08370d51")),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("BoundedCost"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle externalAssembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("External.Enums"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            externalAssembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var enumReferences = new TypeReferenceHandle[referenceCount];
        for (int i = 0; i < enumReferences.Length; i++)
        {
            enumReferences[i] = metadata.AddTypeReference(
                externalAssembly,
                metadata.GetOrAddString("External.Enums"),
                metadata.GetOrAddString($"Missing{i}"));
        }

        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                referenceCount,
                returnType => returnType.Void(),
                parameters =>
                {
                    foreach (TypeReferenceHandle enumReference in enumReferences)
                    {
                        parameters.AddParameter().Type().Type(
                            enumReference,
                            isValueType: true);
                    }
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int i = 0; i < decoyTypeCount; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Local.Types"),
                metadata.GetOrAddString($"Decoy{i}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }

        TypeDefinitionHandle attributedType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        for (int i = 0; i < referenceCount; i++)
            value.WriteInt32(i);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributedType,
            constructor,
            metadata.GetOrAddBlob(value));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildSharedNamespaceIndexImage(
        int definitionCount,
        int namespaceLength)
    {
        MetadataBuilder metadata = CreateMetadata("SharedNamespaceIndex");
        AssemblyReferenceHandle externalAssembly =
            AddExternalAssembly(metadata);
        MemberReferenceHandle constructor =
            AddObjectConstructor(metadata, externalAssembly);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        StringHandle sharedNamespace = metadata.GetOrAddString(
            new string('N', namespaceLength));
        for (int i = 0; i < definitionCount; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                sharedNamespace,
                metadata.GetOrAddString($"Decoy{i}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }

        TypeDefinitionHandle attributedType = AddAttributedType(metadata);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteByte(0x55);
        value.WriteSerializedString("Missing.Enum");
        value.WriteInt32(0);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributedType,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildInvariantReferenceNameImage(
        int definitionCount,
        int nameLength)
    {
        MetadataBuilder metadata = CreateMetadata("InvariantReferenceName");
        AssemblyReferenceHandle externalAssembly =
            AddExternalAssembly(metadata);
        StringHandle sharedName = metadata.GetOrAddString(
            new string('E', nameLength));
        TypeReferenceHandle enumReference = metadata.AddTypeReference(
            externalAssembly,
            metadata.GetOrAddString("Match"),
            sharedName);
        MemberReferenceHandle constructor = AddEnumConstructor(
            metadata,
            externalAssembly,
            enumReference);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int i = 0; i < definitionCount; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString(
                    i == definitionCount - 1 ? "Match" : $"N{i}"),
                sharedName,
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }

        TypeDefinitionHandle attributedType = AddAttributedType(metadata);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(0);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributedType,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static MetadataBuilder CreateMetadata(string assemblyName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(
                new Guid("e9b384ad-266d-4e18-999e-c4ed08370d51")),
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

    static AssemblyReferenceHandle AddExternalAssembly(
        MetadataBuilder metadata) =>
        metadata.AddAssemblyReference(
            metadata.GetOrAddString("External.Enums"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

    static MemberReferenceHandle AddObjectConstructor(
        MetadataBuilder metadata,
        AssemblyReferenceHandle externalAssembly)
    {
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            externalAssembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Object());
        return metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(signature));
    }

    static MemberReferenceHandle AddEnumConstructor(
        MetadataBuilder metadata,
        AssemblyReferenceHandle externalAssembly,
        TypeReferenceHandle enumReference)
    {
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            externalAssembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Type(
                    enumReference,
                    isValueType: true));
        return metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(signature));
    }

    static TypeDefinitionHandle AddAttributedType(
        MetadataBuilder metadata) =>
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

    static long ExpectedTypeDefinitionIndexNameBytes(
        MetadataReader reader)
    {
        long bytes = 0;
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition definition = reader.GetTypeDefinition(handle);
            bytes += reader.GetBlobReader(definition.Namespace).Length;
            bytes += reader.GetBlobReader(definition.Name).Length;
        }
        return bytes;
    }

    static void AssertNameWorkOutcome(byte[] bytes)
    {
        using var image = Open(bytes);
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var work = new CustomAttributeValueDecoder.EnumResolutionWork();

        bool measured = TryDecode(
            image.Reader,
            attribute,
            work,
            out _);
        bool ordinary =
            AttributeDecoder.TryDecode(image.Reader, attribute) is not null;

        Assert.Equal(ordinary, measured);
        Assert.InRange(
            work.NameBytes,
            0,
            CustomAttributeValueDecoder.MaxEnumResolutionNameWork);
    }

    static bool TryDecode(
        MetadataReader reader,
        CustomAttribute attribute,
        CustomAttributeValueDecoder.EnumResolutionWork work,
        out CustomAttributeValue<string> value) =>
        CustomAttributeValueDecoder.TryDecode(
            reader,
            attribute,
            preserveSerializedTypeNames: false,
            captureDefaultedWidths: false,
            beforeMaterialize: null,
            enumUnderlyingType: null,
            out value,
            out _,
            out _,
            enumResolutionWork: work);

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static CustomAttribute FirstAttribute(MetadataReader reader)
    {
        foreach (CustomAttributeHandle handle in reader.CustomAttributes)
            return reader.GetCustomAttribute(handle);
        throw new InvalidOperationException("The image has no custom attributes.");
    }

    static LoadedImage Open(byte[] image) => new(image);

    sealed class LoadedImage(byte[] image) : IDisposable
    {
        readonly PEReader _reader = new(
            new MemoryStream(image, writable: false));

        public MetadataReader Reader => _reader.GetMetadataReader();

        public void Dispose() => _reader.Dispose();
    }
}
