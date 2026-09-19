using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using Inspector.Findings;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed partial class ApiSurfaceExtractorBoundsTests
{
    [Fact]
    public void RepeatedLongMemberName_StopsBeforeLargeAllocationAmplification()
    {
        byte[] image = BuildRepeatedLongMethodNameImage(
            methodCount: 10_000,
            nameLength: 4_000);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                maxTypes: 10,
                maxMembers: 10_000,
                maxInspectionFailures: 10,
                maxTypeForwarders: 10,
                maxMetadataRows: 50_000,
                maxRetainedTextCharacters: 8_000_000));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(result);
        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharacters,
            exceeded.Bound);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void RepeatedLongSkippedAccessorName_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildRepeatedLongMethodNameImage(
                methodCount: 10_000,
                nameLength: 4_000,
                prefix: "get_"));
    }

    [Fact]
    public void RepeatedLongSkippedFieldName_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildRepeatedLongFieldNameImage(
                fieldCount: 10_000,
                nameLength: 4_000));
    }

    [Fact]
    public void RepeatedLongVisibilityAttributeTypeName_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildRepeatedLongVisibilityAttributeTypeNameImage(
                methodCount: 10_000,
                nameLength: 4_000));
    }

    [Fact]
    public void RepeatedLongGenericConstraintName_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildRepeatedLongGenericConstraintNameImage(
                parameterCount: 10_000,
                nameLength: 4_000));
    }

    [Fact]
    public void SameModuleTypeReferenceIndex_ChargesDefinitionNamesBeforeCopying()
    {
        AssertTextAmplificationIsBounded(
            BuildSameModuleTypeReferenceDefinitionNameWorkImage(
                definitionCount: 256,
                nameLength: 9_000));
    }

    [Fact]
    public void OneWideSignature_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildWideSignatureImage(parameterCount: 10_000, nameLength: 4_000));
    }

    [Fact]
    public void OneInterfaceHeavyType_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildInterfaceFloodImage(interfaceCount: 10_000, nameLength: 4_000));
    }

    [Fact]
    public void OneWideFieldSignature_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildWideTypeSpecImage(WideTypeSpecUse.Field, argumentCount: 1_000, nameLength: 10_000));
    }

    [Theory]
    [InlineData(WideTypeSpecUse.BaseType)]
    [InlineData(WideTypeSpecUse.Event)]
    [InlineData(WideTypeSpecUse.Interface)]
    [InlineData(WideTypeSpecUse.GenericConstraint)]
    public void OneWideTypeSpec_StopsBeforeLargeAllocationAmplification(
        WideTypeSpecUse use)
    {
        AssertTextAmplificationIsBounded(
            BuildWideTypeSpecImage(use, argumentCount: 1_000, nameLength: 10_000));
    }

    [Fact]
    public void OneLargeCustomAttribute_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildLargeAttributeImage(valueLength: 4_000_000));
    }

    [Theory]
    [InlineData(1, 4_096)]
    [InlineData(512, 1)]
    [InlineData(512, 4_096)]
    public void SharedCustomAttributeBlob_DecodeWorkIsAdditive(
        int attributeCount,
        int elementCount)
    {
        AssertSharedCustomAttributeBlobExtracts(attributeCount, elementCount);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(1, 1)]
    [InlineData(1, 64)]
    [InlineData(1, 512)]
    [InlineData(1, 4_096)]
    [InlineData(8, 1)]
    [InlineData(8, 64)]
    [InlineData(8, 512)]
    [InlineData(8, 4_096)]
    [InlineData(64, 1)]
    [InlineData(64, 64)]
    [InlineData(64, 512)]
    [InlineData(64, 4_096)]
    [InlineData(512, 1)]
    [InlineData(512, 64)]
    [InlineData(512, 512)]
    [InlineData(512, 4_096)]
    public void SharedCustomAttributeBlob_JointCostMatrix(
        int attributeCount,
        int elementCount)
    {
        AssertSharedCustomAttributeBlobExtracts(attributeCount, elementCount);
    }

    [Fact]
    public void DecodeLocalBudgetRefusal_DoesNotBecomeSharedBlobRefusal()
    {
        ApiSurface cold = ExtractSharedBudgetLifecycleImage(warmIndex: false);
        Assert.Empty(
            Assert.Single(cold.Types, type => type.Name == "Target1")
                .Attributes);
        Assert.Single(
            Assert.Single(cold.Types, type => type.Name == "Target2")
                .Attributes);

        ApiSurface warm = ExtractSharedBudgetLifecycleImage(warmIndex: true);
        Assert.Single(
            Assert.Single(warm.Types, type => type.Name == "Target1")
                .Attributes);
        Assert.Single(
            Assert.Single(warm.Types, type => type.Name == "Target2")
                .Attributes);
    }

    static ApiSurface ExtractSharedBudgetLifecycleImage(bool warmIndex)
    {
        byte[] image = BuildSharedBudgetLifecycleImage(warmIndex);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(peReader);
    }

    static void AssertSharedCustomAttributeBlobExtracts(
        int attributeCount,
        int elementCount)
    {
        byte[] image = BuildSharedCustomAttributeArrayImage(
            attributeCount,
            elementCount);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);

        var extracted = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            ApiSurfaceExtractor.ExtractBounded(
                peReader,
                ApiSurfaceExtractionScope.Public,
                new ApiSurfaceExtractionBounds(
                    maxTypes: attributeCount + 1,
                    maxMembers: 0,
                    maxInspectionFailures: 0,
                    maxTypeForwarders: 0,
                    maxMetadataRows: attributeCount * 2 + 16,
                    maxRetainedTextCharacters: 8_000_000)));

        Assert.Equal(attributeCount, extracted.Surface.Types.Count);
        Assert.All(extracted.Surface.Types, type => Assert.Empty(type.Attributes));
    }

    [Fact]
    public void GenericAttributeTypeSpec_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildWideGenericAttributeImage(
                argumentCount: 1_000,
                nameLength: 150_000));
    }

    [Fact]
    public void PropertyAccessorReturnAttribute_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildLargeAccessorReturnAttributeImage(
                AccessorOwner.Property,
                valueLength: 4_000_000));
    }

    [Fact]
    public void EventAccessorReturnAttribute_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildLargeAccessorReturnAttributeImage(
                AccessorOwner.Event,
                valueLength: 4_000_000));
    }

    [Fact]
    public void OneDeeplyNestedTypeSpec_StopsBeforeLargeAllocationAmplification()
    {
        // Stacked GENERICINST prefixes are not ECMA-335 II.23.2.12 (the first
        // slot must be CLASS|VALUETYPE). SignatureBlobGuard rejects the field
        // before SRM can expand the long argument name, so the retained-text
        // bound is not the tripwire. The allocation bound still is.
        AssertRejectedSignatureDoesNotAmplify(
            BuildNestedTypeSpecFieldImage(depth: 500, nameLength: 3_900));
    }

    [Fact]
    public void OneArgumentNestedTypeSpec_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildArgumentNestedTypeSpecFieldImage(depth: 511, nameLength: 1_700));
    }

    [Fact]
    public void OneNestedArrayType_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildNestedArrayFieldImage(depth: 500, rank: 3_800));
    }

    [Fact]
    public void EnclosingTypeNameChain_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildNestedTypeNameChainImage(
                depth: 256,
                nameLength: 4_000));
    }

    [Fact]
    public void EnclosingTypeReferenceChain_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildNestedTypeReferenceFieldImage(
                depth: 256,
                nameLength: 100_000));
    }

    [Fact]
    public void RejectedTypes_SpendDecodeWorkAcrossTheExtraction()
    {
        AssertTextAmplificationIsBounded(
            BuildRepeatedNestedGenericTypesImage(
                typeCount: 64,
                depth: 20,
                nameLength: 7_000,
                poison: true));
    }

    [Theory]
    [InlineData(TransformArrayKind.TupleElementNames)]
    [InlineData(TransformArrayKind.Nullable)]
    [InlineData(TransformArrayKind.Dynamic)]
    public void LargeTransformArray_StopsBeforeLargeAllocationAmplification(
        TransformArrayKind kind)
    {
        AssertTextAmplificationIsBounded(
            BuildLargeTransformArrayImage(kind, elementCount: 5_000_000));
    }

    [Fact]
    public void RepeatedMethodGenericContext_ReusesTypeParameterNames()
    {
        byte[] image = BuildRepeatedMethodGenericContextImage(
            genericParameterCount: 1_000,
            nameLength: 1_000,
            methodCount: 300);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result =
            ApiSurfaceExtractor.ExtractBounded(
                peReader,
                ApiSurfaceExtractionScope.Public,
                new ApiSurfaceExtractionBounds(
                    maxTypes: 100_000,
                    maxMembers: 1_000_000,
                    maxInspectionFailures: 1_024,
                    maxTypeForwarders: 100_000,
                    maxMetadataRows: 250_000,
                    maxRetainedTextCharacters: 32_000_000));
        Assert.True(
            result is ApiSurfaceExtractionResult.Extracted,
            $"Extraction rejected the reusable generic context: {result}");
        var extracted =
            (ApiSurfaceExtractionResult.Extracted)result;

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Single(extracted.Surface.Types);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void OneHugeArrayRank_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildHugeArrayRankFieldImage(rank: 40_000_000));
    }

    [Fact]
    public void RepeatedHiddenAttributeProbe_DoesNotCopyTheValueBlob()
    {
        byte[] image = BuildRepeatedHiddenAttributeImage(
            typeCount: 1_000,
            blobLength: 1_000_000);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        var extracted = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            ApiSurfaceExtractor.ExtractBounded(
                peReader,
                ApiSurfaceExtractionScope.PublicWithNonPublicTypes,
                new ApiSurfaceExtractionBounds(
                    maxTypes: 100_000,
                    maxMembers: 1_000_000,
                    maxInspectionFailures: 1_024,
                    maxTypeForwarders: 100_000,
                    maxMetadataRows: 250_000,
                    maxRetainedTextCharacters: 32_000_000)));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Empty(extracted.Surface.Types);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void HiddenAutoPropertySignature_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildHiddenAutoPropertyImage(
                argumentCount: 10_000,
                nameLength: 4_000));
    }

    [Fact]
    public void HugeParameterDefault_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildHugeParameterDefaultImage(characterCount: 10_000_000));
    }

    [Fact]
    public void EnumDefaultScan_ChargesSkippedEnclosingTypeNames()
    {
        AssertTextAmplificationIsBounded(
            BuildNestedEnumDefaultImage(
                depth: 256,
                nameLength: 4_000));
    }

    [Fact]
    public void EnumStorageSlotProvidesUnderlyingTypeWithoutBecomingMember()
    {
        byte[] image = BuildNestedEnumDefaultImage(
            depth: 0,
            nameLength: 1);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);

        ApiType type = Assert.Single(
            ApiSurfaceExtractor.Extract(peReader).Types,
            candidate => candidate.Name == "TargetEnum");

        Assert.NotNull(type.EnumUnderlyingType);
        Assert.DoesNotContain(type.Members, member => member.Name == "value__");
        ApiMember value = Assert.Single(
            type.Members,
            member => member.Name == "One");
        Assert.Equal("1", value.EnumValueLiteral);

        using var summaryStream = new MemoryStream(image, writable: false);
        using var summaryReader = new PEReader(summaryStream);
        ApiType summaryType = Assert.Single(
            ApiSurfaceExtractor.ExtractSummary(summaryReader).Types,
            candidate => candidate.Name == "TargetEnum");
        Assert.DoesNotContain(
            summaryType.Members,
            member => member.Name == "value__");
        Assert.Single(
            summaryType.Members,
            member => member.Name == "One");
    }

    [Fact]
    public void EnumStorageSlotIgnoresMemberPresentationFilters()
    {
        byte[] image = BuildNestedEnumDefaultImage(
            depth: 0,
            nameLength: 1,
            enumElementType: 0x05,
            hideStorageSlot: true);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);

        ApiType type = Assert.Single(
            ApiSurfaceExtractor.Extract(peReader).Types,
            candidate => candidate.Name == "TargetEnum");

        Assert.Equal("byte", type.EnumUnderlyingType);
        Assert.DoesNotContain(type.Members, member => member.Name == "value__");
        Assert.Single(type.Members, member => member.Name == "One");
    }

    [Fact]
    public void EnumDefaultScan_ChargesRejectedBaseTypeNames()
    {
        AssertTextAmplificationIsBounded(
            BuildEnumDefaultDecoyImage(
                decoyTypeCount: 512,
                defaultMethodCount: 32,
                baseNameLength: 8_000));
    }

    [Fact]
    public void EnumDefaultScan_ChargesTypeSpecArrayRank()
    {
        AssertTextAmplificationIsBounded(
            BuildEnumDefaultTypeSpecDecoyImage(
                decoyTypeCount: 64,
                defaultMethodCount: 16,
                rank: 40_000_000));
    }

    [Fact]
    public void AttributeTypeSpec_ChargesArrayRankBeforeRendering()
    {
        AssertTextAmplificationIsBounded(
            BuildTypeSpecArrayAttributeImage(
                rank: 40_000_000,
                typeCount: 1));
    }

    [Fact]
    public void OneHugeCustomAttributeArrayCount_StopsBeforeLargeAllocationAmplification()
    {
        AssertRefusedAttributeDoesNotAmplify(
            BuildCustomAttributeArrayCountImage(
                attributeCount: 1,
                elementCount: 100_000_000));
    }

    [Fact]
    public void RepeatedNamedArgumentCount_StopsBeforeLargeAllocationAmplification()
    {
        AssertRefusedAttributeDoesNotAmplify(
            BuildCustomAttributeNamedArgumentCountImage(
                attributeCount: 64,
                namedArgumentCount: 65_535));
    }

    [Fact]
    public void PropertyAccessorNullableContextTypeSpec_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildAccessorTypeSpecArrayAttributeImage(rank: 40_000_000));
    }

    [Fact]
    public void DeepBoxedCustomAttribute_StopsBeforeStackOverflow()
    {
        byte[] image = BuildDeepBoxedAttributeImage(depth: 80_000);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                maxTypes: 100_000,
                maxMembers: 1_000_000,
                maxInspectionFailures: 1_024,
                maxTypeForwarders: 100_000,
                maxMetadataRows: 250_000,
                maxRetainedTextCharacters: 32_000_000));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsType<ApiSurfaceExtractionResult.Extracted>(result);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void OneHugeNamedArgumentArrayCount_StopsBeforeLargeAllocationAmplification()
    {
        AssertRefusedAttributeDoesNotAmplify(
            BuildNamedArgumentArrayCountImage(elementCount: 100_000_000));
    }

    [Fact]
    public void DeepNamedNestedArrayCustomAttribute_StopsBeforeStackOverflow()
    {
        byte[] image = BuildNamedNestedArrayAttributeImage(depth: 80_000);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                maxTypes: 100_000,
                maxMembers: 1_000_000,
                maxInspectionFailures: 1_024,
                maxTypeForwarders: 100_000,
                maxMetadataRows: 250_000,
                maxRetainedTextCharacters: 32_000_000));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsType<ApiSurfaceExtractionResult.Extracted>(result);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void TypeRefEnumWidthDesync_StopsBeforeLargeAllocationAmplification()
    {
        AssertRefusedAttributeDoesNotAmplify(
            BuildTypeRefEnumDesyncImage(elementCount: 100_000_000));
    }

    [Fact]
    public void OverDeepEnumFieldModifiers_StopsBeforeLargeAllocationAmplification()
    {
        AssertRefusedAttributeDoesNotAmplify(
            BuildEnumCmodDesyncImage(
                modifierCount: SignatureBlobGuard.DefaultMaxDepth + 1,
                elementCount: 100_000_000));
    }

    [Fact]
    public void AssemblyQualifiedNamedEnum_StopsBeforeLargeAllocationAmplification()
    {
        AssertRefusedAttributeDoesNotAmplify(
            BuildAssemblyQualifiedNamedEnumImage(elementCount: 100_000_000));
    }

    [Fact]
    public void ClassSystemStringFixedArgument_StopsBeforeLargeAllocationAmplification()
    {
        AssertRefusedAttributeDoesNotAmplify(
            BuildClassSystemStringImage(elementCount: 100_000_000));
    }

    [Fact]
    public void DottedSystemTypeTypeRef_StopsBeforeLargeAllocationAmplification()
    {
        AssertRefusedAttributeDoesNotAmplify(
            BuildDottedSystemTypeImage(elementCount: 100_000_000));
    }

    [Fact]
    public void StringTypedEnumValue_StopsBeforeLargeAllocationAmplification()
    {
        AssertRefusedAttributeDoesNotAmplify(
            BuildStringTypedEnumImage(elementCount: 100_000_000));
    }

    [Fact]
    public void BoxedEnumArrayEmptyName_StopsBeforeLargeAllocationAmplification()
    {
        AssertRefusedAttributeDoesNotAmplify(
            BuildBoxedEnumArrayEmptyNameImage(elementCount: 100_000_000));
    }

    [Fact]
    public void FnPtrEarlierGenericArgumentThenArray_StopsBeforeLargeAllocationAmplification()
    {
        AssertRefusedAttributeDoesNotAmplify(
            BuildGenericEarlierThenArrayImage(pointerToFnPtr: false, elementCount: 100_000_000));
    }

    [Fact]
    public void PtrFnPtrEarlierGenericArgumentThenArray_StopsBeforeLargeAllocationAmplification()
    {
        AssertRefusedAttributeDoesNotAmplify(
            BuildGenericEarlierThenArrayImage(pointerToFnPtr: true, elementCount: 100_000_000));
    }

    [Fact]
    public void ClassTypeDefRow4EarlierArgument_StopsBeforeLargeAllocationAmplification()
    {
        AssertRefusedAttributeDoesNotAmplify(
            BuildClassTypeDefRow4DesyncImage(elementCount: 100_000_000));
    }

    [Fact]
    public void ValueTypeTypeRefRow4EarlierArgument_StopsBeforeLargeAllocationAmplification()
    {
        AssertRefusedAttributeDoesNotAmplify(
            BuildValueTypeTypeRefRow4DesyncImage(elementCount: 100_000_000));
    }

    [Fact]
    public void SelfReferentialGenericVar_StopsBeforeStackOverflow()
    {
        byte[] image = BuildSelfReferentialGenericVarImage();
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                maxTypes: 100_000,
                maxMembers: 1_000_000,
                maxInspectionFailures: 1_024,
                maxTypeForwarders: 100_000,
                maxMetadataRows: 250_000,
                maxRetainedTextCharacters: 32_000_000));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsType<ApiSurfaceExtractionResult.Extracted>(result);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void LegalNestedLongEnumNamedArgument_HasBoundedUnboundedParity()
    {
        AssertCompilerAttributeParity(
            nameof(LegalNestedEnumFixture),
            "LegalNestedEnum",
            "Choice");
    }

    [Fact]
    public void LegalGenericCtorAttribute_HasBoundedUnboundedParity()
    {
        AssertCompilerAttributeParity(
            nameof(LegalGenericCtorFixture),
            "LegalGenericCtor",
            "5");
    }

    [Fact]
    public void PropertyRefReturnDuplicateSeq0Attributes_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildRefPropertyDuplicateSeq0TypeSpecImage(
                returnParameterCount: 8,
                rank: 1_000_000));
    }
}
