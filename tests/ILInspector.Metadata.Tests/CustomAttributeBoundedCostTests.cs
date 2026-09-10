using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class CustomAttributeBoundedCostTests
{
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
