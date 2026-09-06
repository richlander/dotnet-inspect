using System.Buffers.Binary;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Gates the owned <see cref="CustomAttributeValueDecoder"/> through
/// <see cref="AttributeDecoder"/> independently of surface extraction:
/// declared counts are bounded before allocation, and legal constructor
/// and named-argument layouts remain decodable.
/// </summary>
public sealed class CustomAttributeValueDecoderTests
{
    [Fact]
    public void HugeArrayCount_IsRefused()
    {
        using var image = Open(
            BuildArrayCountImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.Null(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void HugeNamedArgumentCount_IsRefused()
    {
        using var image = Open(
            BuildNamedArgumentCountImage(namedArgumentCount: 65_535));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.Null(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void LegalStringArgument_Decodes()
    {
        using var image = Open(BuildStringArgumentImage("ok"));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.NotNull(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void LegalInt32Array_Decodes()
    {
        using var image = Open(BuildInt32ArrayImage([1, 2, 3]));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var decoded = AttributeDecoder.TryDecode(image.Reader, attribute);
        Assert.NotNull(decoded);
        Assert.Single(decoded.Value.FixedArguments);
        var values = Assert.IsAssignableFrom<ImmutableArray<CustomAttributeTypedArgument<string>>>(
            decoded.Value.FixedArguments[0].Value);
        Assert.Equal(3, values.Length);
    }

    [Fact]
    public void WideInt32Array_Decodes()
    {
        int[] elements = new int[4096];
        for (int index = 0; index < elements.Length; index++)
            elements[index] = index;
        using var image = Open(BuildInt32ArrayImage(elements));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var decoded = AttributeDecoder.TryDecode(image.Reader, attribute);
        Assert.NotNull(decoded);
        var values = Assert.IsAssignableFrom<ImmutableArray<CustomAttributeTypedArgument<string>>>(
            decoded.Value.FixedArguments[0].Value);
        Assert.Equal(4096, values.Length);
        Assert.Equal(4095, values[4095].Value);
    }

    [Fact]
    public void NestedBoxing_IsRefused()
    {
        // A boxed object whose boxed type is again a boxed object (0x51 0x51)
        // is refused at the first nest, regardless of how deep the chain claims
        // to go.
        using var image = Open(
            BuildBoxedNestingImage(CustomAttributeValueDecoder.MaxSerializedDepth - 1));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.Null(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    /// <summary>
    /// Non-vacuity gate that the value walk is iterative. A recursive walk of a
    /// deeply nested boxed object[] (near <see
    /// cref="CustomAttributeValueDecoder.MaxSerializedDepth"/>) overflowed a
    /// 128 KiB native stack; the heap work-stack must still complete at
    /// 128 KiB.
    /// </summary>
    [Fact]
    public void DeeplyNestedObjectArray_OnSmallNativeStack_Decodes()
    {
        byte[] bytes = BuildNamedNestedArrayImage(NamedArrayNestingAtLimit);
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    using var isolated = Open(bytes);
                    CustomAttribute attribute = FirstAttribute(isolated.Reader);
                    Assert.NotNull(
                        AttributeDecoder.TryDecode(isolated.Reader, attribute));
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            },
            maxStackSize: 128 * 1024);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        Assert.Null(failure);
    }

    [Fact]
    public void NestedEmptySzArray_IsRefused()
    {
        // A jagged fixed argument (int[][]) is refused at the inner SZArray
        // element type.
        using var image = Open(BuildNestedEmptySzArrayImage(20_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.Null(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void DeclaredArrayCount_IsRefusedBeforeCharge()
    {
        using var image = Open(
            BuildArrayCountImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        Assert.Equal(CustomAttributeValueDecoder.DeclaredSlotCharge, charged);
    }

    [Fact]
    public void HugeNamedArgumentArrayCount_IsRefused()
    {
        using var image = Open(
            BuildNamedArrayCountImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        Assert.Equal(
            CustomAttributeValueDecoder.DeclaredSlotCharge + "V".Length,
            charged);
    }

    [Fact]
    public void LegalNamedInt32Array_Decodes()
    {
        using var image = Open(BuildNamedInt32ArrayImage([1, 2, 3]));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.NotNull(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void NamedArrayNestingAtLimit_Decodes()
    {
        using var image = Open(
            BuildNamedNestedArrayImage(NamedArrayNestingAtLimit));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.NotNull(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void NamedArrayNestingJustOverLimit_IsRefused()
    {
        using var image = Open(
            BuildNamedNestedArrayImage(NamedArrayNestingAtLimit + 1));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.Null(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void TypeRefEnumMatchingLocalInt64_SeesFollowingArrayCount()
    {
        using var image = Open(
            BuildTypeRefEnumDesyncImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void DuplicateTypeDefEnumName_ResolvesTheDeclaredDefinition()
    {
        // Two definitions share the name Samples.E; the argument is declared as
        // the second, whose underlying type is Int64. Resolving by name would
        // take the first definition indexed and read four bytes, turning the
        // trailing half of the value into a 100M array count. Resolving from
        // the declared definition reads all eight, so the 100M is payload and
        // the following count is genuinely zero. The supplied name resolver
        // must not override a definition the signature already named.
        using var image = Open(
            BuildDuplicateTypeDefEnumImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.NotNull(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count),
                name => FirstWinsEnumWidth(image.Reader, name)));
        Assert.True(
            charged < 1_000,
            $"Expected the 100M to be enum payload, charged {charged}.");
    }

    [Fact]
    public void ExhaustedJaggedSzArray_IsRefused()
    {
        // A jagged fixed-argument type (an SZArray whose element is an SZArray)
        // is refused.
        using var image = Open(
            BuildDeepJaggedSzArrayImage(depth: 64, count: 2_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.Null(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void OverDeepEnumFieldModifiers_UseInt32WidthAndSeeFollowingArrayCount()
    {
        using var image = Open(
            BuildEnumCmodDesyncImage(
                modifierCount: SignatureBlobGuard.DefaultMaxDepth + 1,
                elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void TypeRefInt32EnumWithoutLocalMatch_Decodes()
    {
        using var image = Open(BuildTypeRefInt32EnumImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.NotNull(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void SystemTypeArgument_FromShippedAttribute_DecodesAndStaysBounded()
    {
        using var image = Open(BuildSystemTypeThenStringArrayImage(declareSystemType: true));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        CustomAttributeValue<string>? decoded =
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count));
        Assert.NotNull(decoded);
        Assert.True(
            charged < 1_000,
            $"A legal 80-byte blob must stay bounded, charged {charged}.");

        // The decoder's classifier recognizes System.Type, and the same
        // decode both stays bounded and returns the shipped argument values.
        TypeReferenceHandle systemType =
            FindTypeReference(image.Reader, "System", "Type");
        Assert.False(systemType.IsNil);
        var provider = new CustomAttributeValueDecoder.Classifier(
            image.Reader,
            preserveSerializedTypeNames: false,
            beforeMaterialize: null,
            enumUnderlyingType: null);
        Assert.True(
            provider.IsSystemType(
                provider.GetTypeFromReference(image.Reader, systemType, 0)),
            "The decoder must classify this argument as System.Type.");

        Assert.Equal(
            "Kentico.Content.Web.Mvc.Builder.Localization.Resx.Kentico.Builder",
            decoded.Value.FixedArguments[0].Value);
        var elements = Assert.IsType<ImmutableArray<CustomAttributeTypedArgument<string>>>(
            decoded.Value.FixedArguments[1].Value);
        Assert.Equal("en-us", Assert.Single(elements).Value);
    }

    [Fact]
    public void SystemTypeArgumentReadAsEnum_IsRefusedBeforeAmplifiedCharge()
    {
        // Gates refusal after a System.Type misclassification, using the
        // byte sequence from dotnet/runtime#57531 rather than an invented one.
        // Only the first parameter's declared type changes: reading it as an
        // Int32-width enum consumes the SerString length byte and "Ken", so the
        // following string[] count is read from "tico" (0x6F636974) instead of
        // the real 1. That is 1,868,786,036 declared slots -- 28,515 MiB at
        // DeclaredSlotCharge, matching the 28,517 MiB the issue reported.
        //
        // Pin the offset arithmetic against the fixture independently of the
        // observer, which must not receive the refused array's declared charge.
        Assert.Equal(
            1_868_786_036,
            BinaryPrimitives.ReadInt32LittleEndian(
                ShippedSystemTypeBlob.Slice(6)));

        // The owned decoder refuses the count before charging or allocating
        // the array.
        using var image = Open(BuildSystemTypeThenStringArrayImage(declareSystemType: false));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void LocalInt64EnumFixedArgument_Decodes()
    {
        using var image = Open(BuildLocalInt64EnumImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.NotNull(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void AssemblyQualifiedNamedEnum_SeesFollowingArrayCount()
    {
        using var image = Open(
            BuildAssemblyQualifiedNamedEnumImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void CrossAssemblyInt64NamedEnum_WithoutDefiningImage_DoesNotDecode()
    {
        using var image = Open(BuildCrossAssemblyInt64NamedEnumImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.Null(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void CrossAssemblyInt64NamedEnum_WithDefiningImage_Decodes()
    {
        using var defining = Open(BuildDefiningInt64EnumImage());
        using var image = Open(BuildCrossAssemblyInt64NamedEnumImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        PrimitiveTypeCode Width(string name) =>
            EnumUnderlyingPrimitive.TryFromSerializedName(
                defining.Reader,
                name,
                out PrimitiveTypeCode code)
                ? code
                : PrimitiveTypeCode.Int32;

        var decoded = AttributeDecoder.TryDecode(
            image.Reader,
            attribute,
            beforeMaterialize: null,
            Width);
        Assert.NotNull(decoded);
        Assert.Equal(2, decoded.Value.NamedArguments.Length);
        Assert.Equal("Kind", decoded.Value.NamedArguments[0].Name);
        Assert.Equal(7L, decoded.Value.NamedArguments[0].Value);
        Assert.Equal("Name", decoded.Value.NamedArguments[1].Name);
        Assert.Equal("ok", decoded.Value.NamedArguments[1].Value);
    }

    [Fact]
    public void CrossAssemblyInt64NamedEnum_WithDefiningImage_StillRefusesHostileCount()
    {
        using var defining = Open(BuildDefiningInt64EnumImage());
        using var image = Open(
            BuildCrossAssemblyInt64NamedEnumImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        PrimitiveTypeCode Width(string name) =>
            EnumUnderlyingPrimitive.TryFromSerializedName(
                defining.Reader,
                name,
                out PrimitiveTypeCode code)
                ? code
                : PrimitiveTypeCode.Int32;

        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count),
                Width));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void CrossAssemblyInt64NamedEnum_ExactSimpleNameResolver_Decodes()
    {
        using var image = Open(BuildCrossAssemblyInt64NamedEnumImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var decoded = AttributeDecoder.TryDecode(
            image.Reader,
            attribute,
            beforeMaterialize: null,
            ExactSimpleNameInt64);
        Assert.NotNull(decoded);
        Assert.Equal(7L, decoded.Value.NamedArguments[0].Value);
        Assert.Equal("ok", decoded.Value.NamedArguments[1].Value);
    }

    [Fact]
    public void CrossAssemblyInt64NamedEnum_ExactSimpleNameResolver_SeesOverlappingHostileCount()
    {
        using var image = Open(BuildOverlappingInt64NamedEnumHostileImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count),
                ExactSimpleNameInt64));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void LocalInt64EnumFixedArgument_IgnoresConflictingExternalResolver()
    {
        using var image = Open(BuildLocalInt64EnumImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var decoded = AttributeDecoder.TryDecode(
            image.Reader,
            attribute,
            beforeMaterialize: null,
            _ => PrimitiveTypeCode.Int32);
        Assert.NotNull(decoded);
        Assert.Equal(7L, decoded.Value.FixedArguments[0].Value);
    }

    [Fact]
    public void Decode_LocalInt64NamedEnum_IgnoresInt32Resolver_SeesOverlappingHostileCount()
    {
        using var image = Open(
            BuildOverlappingInt64NamedEnumHostileImage(localInt64Enum: true));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count),
                _ => PrimitiveTypeCode.Int32));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void Decode_NormalizesNonFixedWidthResolver_SeesHostileCount()
    {
        using var image = Open(BuildNamedEnumInt32ThenHostileArrayImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count),
                _ => PrimitiveTypeCode.Double));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void Decode_MalformedTypeDefIndex_DoesNotBypassHostileCount()
    {
        using var image = Open(
            BuildOverlappingInt64NamedEnumHostileImage(cyclicTypeDef: true));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                beforeMaterialize: null,
                ExactSimpleNameInt64));
    }

    [Theory]
    [InlineData(@"Samples.E\+Kind, =")]
    [InlineData(@"Samples.E\+Kind, Other, Version=")]
    public void EscapedNamedEnum_MalformedAssemblySuffix_SeesOverlappingHostileCount(
        string enumName)
        => AssertEscapedNamedEnumSeesHostileCount(enumName);

    [Fact]
    public void EscapedNamedEnum_OverBudgetAssemblySuffix_SeesOverlappingHostileCount()
        => AssertEscapedNamedEnumSeesHostileCount(
            @"Samples.E\\Kind, "
                + new string('x', MetadataSafetyPolicy.MaxTypeNameCharacters + 1));

    /// <summary>
    /// A serialized enum name whose assembly suffix is malformed or over the
    /// character budget still has to select one width. The decoder projects
    /// the blob name before resolving its underlying type, so the selected
    /// width consumes the enum value and reaches the following declared count.
    /// </summary>
    static void AssertEscapedNamedEnumSeesHostileCount(string enumName)
    {
        using var image = Open(
            BuildOverlappingInt64NamedEnumHostileImage(enumName: enumName));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count),
                EscapedMetadataNameInt64));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void ClassSystemStringFixedArgument_SeesFollowingArrayCount()
    {
        using var image = Open(
            BuildClassSystemStringImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void DottedSystemTypeTypeRef_SeesFollowingArrayCount()
    {
        using var image = Open(
            BuildDottedSystemTypeImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void NestedSystemTypeTypeRef_SeesFollowingArrayCount()
    {
        using var image = Open(
            BuildNestedSystemTypeImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void LegalSystemTypeArgument_Decodes()
    {
        using var image = Open(BuildLegalSystemTypeImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var decoded = AttributeDecoder.TryDecode(image.Reader, attribute);
        Assert.NotNull(decoded);
        Assert.Single(decoded.Value.FixedArguments);
        Assert.Equal("System.Int32", decoded.Value.FixedArguments[0].Value);
    }

    [Fact]
    public void NullSystemTypeValues_DecodeInEveryPosition()
    {
        using var image = Open(BuildNullSystemTypeValuesImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);

        var decoded = AttributeDecoder.TryDecode(image.Reader, attribute);

        Assert.NotNull(decoded);
        Assert.Null(decoded.Value.FixedArguments[0].Value);
        Assert.Null(decoded.Value.FixedArguments[1].Value);
        var array = Assert.IsType<
            ImmutableArray<CustomAttributeTypedArgument<string>>>(
                decoded.Value.FixedArguments[2].Value);
        Assert.Null(Assert.Single(array).Value);
        Assert.Null(Assert.Single(decoded.Value.NamedArguments).Value);
    }

    [Fact]
    public void StringTypedEnumValue_SeesFollowingArrayCount()
    {
        using var image = Open(
            BuildStringTypedEnumImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void TruncatedInt32ArrayThenHugeNamedCount_IsRefused()
    {
        // The int[] array count is truncated (only two of four bytes remain),
        // so the decoder refuses before reading the count and before charging.
        // The trailing bytes that would read as a huge named-argument count are
        // never reached.
        using var image = Open(BuildTruncatedArrayThenNamedImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        Assert.Equal(CustomAttributeValueDecoder.DeclaredSlotCharge, charged);
    }

    [Fact]
    public void LegalBoxedEnumArray_Decodes()
    {
        using var image = Open(BuildLegalBoxedEnumArrayImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var decoded = AttributeDecoder.TryDecode(image.Reader, attribute);
        Assert.NotNull(decoded);
        var values = Assert.IsAssignableFrom<ImmutableArray<CustomAttributeTypedArgument<string>>>(
            decoded.Value.FixedArguments[0].Value);
        Assert.Equal(2, values.Length);
        Assert.Equal(7, values[0].Value);
        Assert.Equal(9, values[1].Value);
    }

    [Fact]
    public void LegalBoxedInt32Array_Decodes()
    {
        using var image = Open(BuildLegalBoxedInt32ArrayImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var decoded = AttributeDecoder.TryDecode(image.Reader, attribute);
        Assert.NotNull(decoded);
        var values = Assert.IsAssignableFrom<ImmutableArray<CustomAttributeTypedArgument<string>>>(
            decoded.Value.FixedArguments[0].Value);
        Assert.Equal(3, values.Length);
        Assert.Equal(1, values[0].Value);
        Assert.Equal(3, values[2].Value);
    }

    [Fact]
    public void BoxedEnumArrayEmptyName_SeesFollowingArrayCount()
    {
        using var image = Open(
            BuildBoxedEnumArrayEmptyNameImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void NamedBoxedEnumArrayEmptyName_SeesFollowingArrayCount()
    {
        using var image = Open(
            BuildNamedBoxedEnumArrayEmptyNameImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void GenericAttributeTypeParameterInt32_Decodes()
    {
        using var image = Open(BuildGenericAttributeInt32Image());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var decoded = AttributeDecoder.TryDecode(image.Reader, attribute);
        Assert.NotNull(decoded);
        Assert.Single(decoded.Value.FixedArguments);
        Assert.Equal(5, decoded.Value.FixedArguments[0].Value);
    }

    [Fact]
    public void FnPtrEarlierGenericArgumentThenArray_SeesFollowingArrayCount()
    {
        using var image = Open(
            BuildGenericEarlierThenArrayImage(
                earlier: EarlierGenericArg.FnPtr,
                elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void PtrFnPtrEarlierGenericArgumentThenArray_SeesFollowingArrayCount()
    {
        using var image = Open(
            BuildGenericEarlierThenArrayImage(
                earlier: EarlierGenericArg.PtrFnPtr,
                elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void ClassTypeDefRow4EarlierArgument_SeesFollowingArrayCount()
    {
        using var image = Open(
            BuildClassTypeDefRow4DesyncImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void ValueTypeTypeRefRow4EarlierArgument_SeesFollowingArrayCount()
    {
        using var image = Open(
            BuildValueTypeTypeRefRow4DesyncImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        AssertHostileCountWasNotCharged(charged);
    }

    [Fact]
    public void SelfReferentialGenericVar_IsRefused()
    {
        using var image = Open(BuildSelfReferentialGenericVarImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.Null(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void ObserverFailureDuringNamedEnumLookup_EscapesTryDecode()
    {
        using var image = Open(BuildNamedEnumInt32Image());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var thrown = Assert.Throws<InvalidOperationException>(
            () => AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                _ => throw new InvalidOperationException("budget")));
        Assert.Equal("budget", thrown.Message);
    }

    [Fact]
    public void ObserverThrowing_MalformedInputExceptions_PropagateUnchanged()
    {
        // A caller observer raising BadImageFormatException or
        // ArgumentOutOfRangeException is a fact about the caller, not the blob,
        // so it must not be absorbed as malformed metadata (#5085). The
        // provenance sentinel carries it past the decoder's malformed-input
        // catch and the public edge rethrows the original.
        using var image = Open(BuildNamedEnumInt32Image());
        CustomAttribute attribute = FirstAttribute(image.Reader);

        var bif = Assert.Throws<BadImageFormatException>(
            () => AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                _ => throw new BadImageFormatException("caller-observer")));
        Assert.Equal("caller-observer", bif.Message);

        var range = Assert.Throws<ArgumentOutOfRangeException>(
            () => AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                _ => throw new ArgumentOutOfRangeException("caller-observer")));
        Assert.Equal("caller-observer", range.ParamName);
    }

    [Fact]
    public void ResolverThrowing_MalformedInputExceptions_PropagateUnchanged()
    {
        // A caller enum-width resolver raising BadImageFormatException likewise
        // propagates rather than being reported as a malformed blob (#5759).
        using var image = Open(BuildCrossAssemblyInt64NamedEnumImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var bif = Assert.Throws<BadImageFormatException>(
            () => AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                beforeMaterialize: null,
                (Func<string, PrimitiveTypeCode>)(
                    _ => throw new BadImageFormatException("caller-resolver"))));
        Assert.Equal("caller-resolver", bif.Message);

        var range = Assert.Throws<ArgumentOutOfRangeException>(
            () => AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                beforeMaterialize: null,
                (Func<string, PrimitiveTypeCode>)(
                    _ => throw new ArgumentOutOfRangeException("caller-resolver"))));
        Assert.Equal("caller-resolver", range.ParamName);
    }

    [Fact]
    public void MalformedBlob_IsAbsorbedAsNull_WithNonThrowingObserver()
    {
        // The control for the provenance tests above: an exception that IS
        // about the blob -- here a truncated array count -- becomes null rather
        // than propagating, even while a non-throwing observer runs.
        using var image = Open(BuildTruncatedArrayThenNamedImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.Null(
            AttributeDecoder.TryDecode(image.Reader, attribute, _ => { }));
    }

    [Fact]
    public void DetailedDecode_ReportsDefaultedAndResolvedWidths()
    {
        // One decode path, two named enum arguments: the first resolves to a
        // local Int64 TypeDef (flag clear), the second names no local
        // definition and no resolver answers, so it defaults to Int32 (flag
        // set). The no-resolver production path must report the default.
        using var image = Open(BuildResolvedAndDefaultedNamedEnumImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var detailed = AttributeDecoder.TryDecodeDetailed(image.Reader, attribute);
        Assert.NotNull(detailed);
        Assert.Equal(7L, detailed.Value.Value.NamedArguments[0].Value);
        Assert.Equal(0, detailed.Value.Value.NamedArguments[1].Value);
        Assert.Equal(
            new[] { false, true },
            detailed.Value.NamedArgumentEnumWidthDefaulted);
        Assert.Empty(detailed.Value.FixedArgumentEnumWidthDefaulted);
    }

    [Fact]
    public void DetailedDecode_LegacyFuncIsAuthoritative_ButUnresolvedDefaults()
    {
        // A legacy Func answer is authoritative, so even an Int32 it returns is
        // reported resolved; a detailed resolver may instead report the name
        // unresolved, which defaults and is reported set.
        using var image = Open(BuildNamedEnumInt32Image());
        CustomAttribute attribute = FirstAttribute(image.Reader);

        var resolved = AttributeDecoder.TryDecodeDetailed(
            image.Reader,
            attribute,
            enumUnderlyingType:
                (string name, out PrimitiveTypeCode width) =>
                {
                    width = PrimitiveTypeCode.Int32;
                    return true;
                });
        Assert.NotNull(resolved);
        Assert.False(resolved.Value.NamedArgumentEnumWidthDefaulted[0]);

        var unresolved = AttributeDecoder.TryDecodeDetailed(
            image.Reader,
            attribute,
            enumUnderlyingType:
                (string name, out PrimitiveTypeCode width) =>
                {
                    width = default;
                    return false;
                });
        Assert.NotNull(unresolved);
        Assert.True(unresolved.Value.NamedArgumentEnumWidthDefaulted[0]);

        // No resolver at all also defaults and reports set.
        var defaulted = AttributeDecoder.TryDecodeDetailed(image.Reader, attribute);
        Assert.NotNull(defaulted);
        Assert.True(defaulted.Value.NamedArgumentEnumWidthDefaulted[0]);
    }

    // A named SZARRAY-of-object nests one boxed object[] per 0x1d/0x51 pair.
    // The owned decoder charges depth for the boxing and the array separately,
    // so each pair advances depth by 2, and the terminal boxed scalar consumes
    // one more level; the deepest pair that still decodes is therefore
    // (MaxSerializedDepth - 4) / 2.
    const int NamedArrayNestingAtLimit =
        (CustomAttributeValueDecoder.MaxSerializedDepth - 4) / 2;

    static LoadedImage Open(byte[] image) => new(image);

    static void AssertHostileCountWasNotCharged(int charged) =>
        Assert.InRange(charged, 0, 100_000_000 - 1);

    static CustomAttribute FirstAttribute(MetadataReader reader)
    {
        foreach (var handle in reader.CustomAttributes)
            return reader.GetCustomAttribute(handle);
        throw new InvalidOperationException("The image has no custom attributes.");
    }

    static byte[] BuildArrayCountImage(int elementCount)
    {
        var metadata = CreateMetadata("ArrayCount");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters => parameters.AddParameter().Type().SZArray().Int32(),
            parameterCount: 1);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildNamedArgumentCountImage(int namedArgumentCount)
    {
        var metadata = CreateMetadata("NamedCount");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            _ => { },
            parameterCount: 0);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16((ushort)namedArgumentCount);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildStringArgumentImage(string text)
    {
        var metadata = CreateMetadata("StringArg");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters => parameters.AddParameter().Type().String(),
            parameterCount: 1);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString(text);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildBoxedNestingImage(int depth)
    {
        var metadata = CreateMetadata("BoxedNest");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters => parameters.AddParameter().Type().Object(),
            parameterCount: 1);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        for (int index = 0; index < depth; index++)
            value.WriteByte(0x51);
        value.WriteByte(0x08);
        value.WriteInt32(1);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildNestedEmptySzArrayImage(int outerCount)
    {
        var metadata = CreateMetadata("NestedEmpty");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters => parameters.AddParameter().Type().SZArray().SZArray().Int32(),
            parameterCount: 1);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(outerCount);
        for (int index = 0; index < outerCount; index++)
            value.WriteInt32(0);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildInt32ArrayImage(int[] elements)
    {
        var metadata = CreateMetadata("IntArray");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters => parameters.AddParameter().Type().SZArray().Int32(),
            parameterCount: 1);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elements.Length);
        foreach (int element in elements)
            value.WriteInt32(element);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildNamedArrayCountImage(int elementCount)
    {
        var metadata = CreateMetadata("NamedArrayCount");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            _ => { },
            parameterCount: 0);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(1);
        value.WriteByte(0x53);
        value.WriteByte(0x1d);
        value.WriteByte(0x08);
        value.WriteSerializedString("V");
        value.WriteInt32(elementCount);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildNamedInt32ArrayImage(int[] elements)
    {
        var metadata = CreateMetadata("NamedIntArray");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            _ => { },
            parameterCount: 0);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(1);
        value.WriteByte(0x54);
        value.WriteByte(0x1d);
        value.WriteByte(0x08);
        value.WriteSerializedString("Values");
        value.WriteInt32(elements.Length);
        foreach (int element in elements)
            value.WriteInt32(element);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildNamedNestedArrayImage(int depth)
    {
        var metadata = CreateMetadata("NamedNest");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            _ => { },
            parameterCount: 0);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(1);
        value.WriteByte(0x53);
        value.WriteByte(0x1d);
        value.WriteByte(0x51);
        value.WriteSerializedString("V");
        value.WriteInt32(1);
        for (int index = 0; index < depth; index++)
        {
            value.WriteByte(0x1d);
            value.WriteByte(0x51);
            value.WriteInt32(1);
        }

        value.WriteByte(0x08);
        value.WriteInt32(7);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildTypeRefEnumDesyncImage(int elementCount)
    {
        var metadata = CreateMetadata("EnumDesync");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle enumRef = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"));
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(enumRef, isValueType: true);
                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt64(0);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildEnumCmodDesyncImage(int modifierCount, int elementCount)
    {
        var metadata = CreateMetadata("EnumCmod");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        TypeDefinitionHandle enumDef = MetadataTokens.TypeDefinitionHandle(2);
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(enumDef, isValueType: true);
                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06);
        int coded = (MetadataTokens.GetRowNumber(systemEnum) << 2) | 0x01;
        for (int index = 0; index < modifierCount; index++)
        {
            fieldSignature.WriteByte(0x20);
            fieldSignature.WriteCompressedInteger(coded);
        }

        fieldSignature.WriteByte(0x0a);
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(0);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    /// <summary>
    /// The verbatim 80-byte value blob of
    /// <c>RegisterPageBuilderLocalizationResourceAttribute</c> as shipped in
    /// <c>Kentico.Content.Web.Mvc.dll</c>
    /// (kentico.xperience.aspnet.mvc5.libraries 13.0.18), the assembly that
    /// produced the 28,517 MiB allocation in dotnet/runtime#57531. Captured
    /// rather than reconstructed, so the fixture is the artifact and not a
    /// re-derivation of it; <see cref="SystemTypeArgument_FromShippedAttribute_DecodesAndStaysBounded"/>
    /// asserts the decoded content, which is what keeps these bytes honest.
    /// Layout: prolog, SerString(65) type name, Int32 array count 1,
    /// SerString(5) "en-us", named-argument count 0.
    /// </summary>
    static ReadOnlySpan<byte> ShippedSystemTypeBlob =>
    [
        0x01, 0x00, 0x41, 0x4B, 0x65, 0x6E, 0x74, 0x69, 0x63, 0x6F, 0x2E, 0x43, 0x6F, 0x6E, 0x74, 0x65,
        0x6E, 0x74, 0x2E, 0x57, 0x65, 0x62, 0x2E, 0x4D, 0x76, 0x63, 0x2E, 0x42, 0x75, 0x69, 0x6C, 0x64,
        0x65, 0x72, 0x2E, 0x4C, 0x6F, 0x63, 0x61, 0x6C, 0x69, 0x7A, 0x61, 0x74, 0x69, 0x6F, 0x6E, 0x2E,
        0x52, 0x65, 0x73, 0x78, 0x2E, 0x4B, 0x65, 0x6E, 0x74, 0x69, 0x63, 0x6F, 0x2E, 0x42, 0x75, 0x69,
        0x6C, 0x64, 0x65, 0x72, 0x01, 0x00, 0x00, 0x00, 0x05, 0x65, 0x6E, 0x2D, 0x75, 0x73, 0x00, 0x00,
    ];

    /// <summary>
    /// Builds an image carrying <see cref="ShippedSystemTypeBlob"/> against a
    /// two-parameter constructor. <paramref name="declareSystemType"/> selects
    /// the real shape, <c>(System.Type, string[])</c>, or the misclassified one
    /// that reads the first argument as an enum. Only the first parameter's
    /// declared type differs, which is what makes the pair a controlled
    /// comparison over one variable.
    /// </summary>
    static TypeReferenceHandle FindTypeReference(
        MetadataReader reader,
        string ns,
        string name)
    {
        foreach (TypeReferenceHandle handle in reader.TypeReferences)
        {
            TypeReference reference = reader.GetTypeReference(handle);
            if (reader.GetString(reference.Namespace) == ns
                && reader.GetString(reference.Name) == name)
            {
                return handle;
            }
        }

        return default;
    }

    static byte[] BuildSystemTypeThenStringArrayImage(bool declareSystemType)
    {
        var metadata = CreateMetadata("ShippedSystemType");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle firstParameter = declareSystemType
            ? metadata.AddTypeReference(
                other,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Type"))
            : metadata.AddTypeReference(
                other,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString("E"));
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters =>
            {
                parameters.AddParameter().Type().Type(
                    firstParameter,
                    isValueType: !declareSystemType);
                parameters.AddParameter().Type().SZArray().String();
            },
            parameterCount: 2);
        var value = new BlobBuilder();
        value.WriteBytes(ShippedSystemTypeBlob.ToArray());
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildTypeRefInt32EnumImage()
    {
        var metadata = CreateMetadata("TypeRefEnum");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle enumRef = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("AttributeTargets"));
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters => parameters.AddParameter().Type().Type(enumRef, isValueType: true),
            parameterCount: 1);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(1);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildAssemblyQualifiedNamedEnumImage(int elementCount)
    {
        var metadata = CreateMetadata("EnumSuffix");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            _ => { },
            parameterCount: 0);
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(2);
        value.WriteByte(0x53);
        value.WriteByte(0x55);
        value.WriteSerializedString("Samples.E, Other");
        value.WriteSerializedString("F");
        value.WriteInt64(0);
        value.WriteByte(0x53);
        value.WriteByte(0x1d);
        value.WriteByte(0x08);
        value.WriteSerializedString("V");
        value.WriteInt32(elementCount);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    // Two named enum arguments: the first names the local Int64 "Samples.E"
    // (resolves, Int64), the second names a type no image defines (defaults to
    // Int32). Used to gate the per-argument defaulted-width signal.
    static byte[] BuildResolvedAndDefaultedNamedEnumImage()
    {
        var metadata = CreateMetadata("ResolvedDefaulted");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            _ => { },
            parameterCount: 0);
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(2);
        // Named arg 0: enum resolving to local Int64 Samples.E.
        value.WriteByte(0x53);
        value.WriteByte(0x55);
        value.WriteSerializedString("Samples.E, Other");
        value.WriteSerializedString("Resolved");
        value.WriteInt64(7);
        // Named arg 1: enum no image defines, so it defaults to Int32.
        value.WriteByte(0x53);
        value.WriteByte(0x55);
        value.WriteSerializedString("Samples.Missing, Other");
        value.WriteSerializedString("Defaulted");
        value.WriteInt32(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildDefiningInt64EnumImage()
    {
        var metadata = CreateMetadata("Other");
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildCrossAssemblyInt64NamedEnumImage(int? elementCount = null)
    {
        var metadata = CreateMetadata("User");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            _ => { },
            parameterCount: 0);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(2);
        value.WriteByte(0x53);
        value.WriteByte(0x55);
        value.WriteSerializedString("Samples.E, Other");
        value.WriteSerializedString("Kind");
        value.WriteInt64(7);
        if (elementCount is int count)
        {
            value.WriteByte(0x53);
            value.WriteByte(0x1d);
            value.WriteByte(0x08);
            value.WriteSerializedString("V");
            value.WriteInt32(count);
        }
        else
        {
            value.WriteByte(0x53);
            value.WriteByte(0x0e);
            value.WriteSerializedString("Name");
            value.WriteSerializedString("ok");
        }

        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static PrimitiveTypeCode ExactSimpleNameInt64(string name)
        => name == "Samples.E" ? PrimitiveTypeCode.Int64 : PrimitiveTypeCode.Int32;

    /// <summary>
    /// An external width for the exact metadata names the escaped serialized
    /// spellings below denote, so decoding exercises the projected lookup name.
    /// </summary>
    static PrimitiveTypeCode EscapedMetadataNameInt64(string name)
        => name is "Samples.E+Kind" or @"Samples.E\Kind"
            ? PrimitiveTypeCode.Int64
            : PrimitiveTypeCode.Int32;

    static byte[] BuildOverlappingInt64NamedEnumHostileImage(
        bool localInt64Enum = false,
        bool cyclicTypeDef = false,
        string enumName = "Samples.E, Other")
    {
        var metadata = CreateMetadata("User");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            _ => { },
            parameterCount: 0);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(2);
        value.WriteByte(0x53);
        value.WriteByte(0x55);
        value.WriteSerializedString(enumName);
        value.WriteSerializedString("Kind");
        value.WriteByte(0x07);
        value.WriteByte(0x00);
        value.WriteByte(0x00);
        value.WriteByte(0x00);
        value.WriteByte(0x53);
        value.WriteByte(0x05);
        value.WriteByte(0x00);
        value.WriteByte(0x00);
        value.WriteByte(0x53);
        value.WriteByte(0x1d);
        value.WriteByte(0x08);
        value.WriteSerializedString("V");
        value.WriteInt32(100_000_000);
        if (localInt64Enum)
            AddAttributedTypeWithLocalInt64Enum(metadata, constructor, value);
        else
            AddAttributedType(metadata, constructor, value);
        if (cyclicTypeDef)
        {
            TypeDefinitionHandle poisoned = metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString("Poison"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddNestedType(poisoned, poisoned);
        }

        return Serialize(metadata);
    }

    static byte[] BuildNamedEnumInt32ThenHostileArrayImage()
    {
        var metadata = CreateMetadata("User");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            _ => { },
            parameterCount: 0);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(2);
        value.WriteByte(0x53);
        value.WriteByte(0x55);
        value.WriteSerializedString("Samples.E, Other");
        value.WriteSerializedString("Kind");
        value.WriteInt32(7);
        value.WriteByte(0x53);
        value.WriteByte(0x1d);
        value.WriteByte(0x08);
        value.WriteSerializedString("V");
        value.WriteInt32(100_000_000);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static void AddAttributedTypeWithLocalInt64Enum(
        MetadataBuilder metadata,
        MemberReferenceHandle constructor,
        BlobBuilder value)
    {
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
    }

    static byte[] BuildClassSystemStringImage(int elementCount)
    {
        var metadata = CreateMetadata("ClassString");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemString = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("String"));
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters =>
            {
                parameters.AddParameter().Type().Type(systemString, isValueType: false);
                parameters.AddParameter().Type().SZArray().Int32();
            },
            parameterCount: 2);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(0);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildDottedSystemTypeImage(int elementCount)
    {
        var metadata = CreateMetadata("DottedType");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemType = metadata.AddTypeReference(
            other,
            default,
            metadata.GetOrAddString("System.Type"));
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters =>
            {
                parameters.AddParameter().Type().Type(systemType, isValueType: false);
                parameters.AddParameter().Type().SZArray().Int32();
            },
            parameterCount: 2);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString(string.Empty);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildNestedSystemTypeImage(int elementCount)
    {
        var metadata = CreateMetadata("NestedType");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle system = metadata.AddTypeReference(
            other,
            default,
            metadata.GetOrAddString("System"));
        TypeReferenceHandle type = metadata.AddTypeReference(
            system,
            default,
            metadata.GetOrAddString("Type"));
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters =>
            {
                parameters.AddParameter().Type().Type(type, isValueType: false);
                parameters.AddParameter().Type().SZArray().Int32();
            },
            parameterCount: 2);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString(string.Empty);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildLegalSystemTypeImage()
    {
        var metadata = CreateMetadata("LegalType");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Type"));
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters => parameters.AddParameter().Type().Type(systemType, isValueType: false),
            parameterCount: 1);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString("System.Int32");
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildNullSystemTypeValuesImage()
    {
        var metadata = CreateMetadata("NullTypes");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Type"));
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters =>
            {
                parameters.AddParameter().Type().Type(
                    systemType,
                    isValueType: false);
                parameters.AddParameter().Type().Object();
                parameters.AddParameter().Type().SZArray().Type(
                    systemType,
                    isValueType: false);
            },
            parameterCount: 3);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteByte(0xFF);
        value.WriteByte(0x50);
        value.WriteByte(0xFF);
        value.WriteInt32(1);
        value.WriteByte(0xFF);
        value.WriteUInt16(1);
        value.WriteByte(0x54);
        value.WriteByte(0x50);
        value.WriteSerializedString("Named");
        value.WriteByte(0xFF);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildStringTypedEnumImage(int elementCount)
    {
        var metadata = CreateMetadata("StringEnum");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        TypeDefinitionHandle enumDef = MetadataTokens.TypeDefinitionHandle(2);
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(enumDef, isValueType: true);
                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().String();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(0);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildTruncatedArrayThenNamedImage()
    {
        var metadata = CreateMetadata("TruncNamed");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters => parameters.AddParameter().Type().SZArray().Int32(),
            parameterCount: 1);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(0xFFFF);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildLegalBoxedEnumArrayImage()
    {
        var metadata = CreateMetadata("BoxedEnum");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters => parameters.AddParameter().Type().Object(),
            parameterCount: 1);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteByte(0x1d);
        value.WriteByte(0x55);
        value.WriteSerializedString("N");
        value.WriteInt32(2);
        value.WriteInt32(7);
        value.WriteInt32(9);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildLegalBoxedInt32ArrayImage()
    {
        var metadata = CreateMetadata("BoxedI4");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters => parameters.AddParameter().Type().Object(),
            parameterCount: 1);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteByte(0x1d);
        value.WriteByte(0x08);
        value.WriteInt32(3);
        value.WriteInt32(1);
        value.WriteInt32(2);
        value.WriteInt32(3);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildBoxedEnumArrayEmptyNameImage(int elementCount)
    {
        var metadata = CreateMetadata("BoxedEnumAmp");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters => parameters.AddParameter().Type().Object(),
            parameterCount: 1);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteByte(0x1d);
        value.WriteByte(0x55);
        value.WriteByte(0x00);
        value.WriteInt32(elementCount);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildNamedBoxedEnumArrayEmptyNameImage(int elementCount)
    {
        var metadata = CreateMetadata("NamedBoxedEnum");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            _ => { },
            parameterCount: 0);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(1);
        value.WriteByte(0x53);
        value.WriteByte(0x51);
        value.WriteSerializedString("F");
        value.WriteByte(0x1d);
        value.WriteByte(0x55);
        value.WriteByte(0x00);
        value.WriteInt32(elementCount);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    enum EarlierGenericArg
    {
        FnPtr,
        PtrFnPtr,
    }

    static byte[] BuildGenericEarlierThenArrayImage(
        EarlierGenericArg earlier,
        int elementCount)
    {
        var metadata = CreateMetadata("FnPtrDesync");
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("MyAttr`2"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, attributeType);
        typeSpecSignature.WriteCompressedInteger(2);
        WriteEarlierGenericArg(typeSpecSignature, earlier);
        typeSpecSignature.WriteByte(0x1d);
        typeSpecSignature.WriteByte(0x08);
        TypeSpecificationHandle typeSpec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        constructorSignature.WriteByte(0x13);
        constructorSignature.WriteCompressedInteger(1);
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static void WriteEarlierGenericArg(
        BlobBuilder signature,
        EarlierGenericArg earlier)
    {
        if (earlier == EarlierGenericArg.PtrFnPtr)
            signature.WriteByte(0x0f);
        signature.WriteByte(0x1b);
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x01);
    }

    static byte[] BuildClassTypeDefRow4DesyncImage(int elementCount)
    {
        var metadata = CreateMetadata("ClassDesync");
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("MyAttr`2"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Pad"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle dummy = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Dummy"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, attributeType);
        typeSpecSignature.WriteCompressedInteger(3);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, dummy);
        typeSpecSignature.WriteByte(0x08);
        typeSpecSignature.WriteByte(0x1d);
        typeSpecSignature.WriteByte(0x08);
        TypeSpecificationHandle typeSpec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        constructorSignature.WriteByte(0x13);
        constructorSignature.WriteCompressedInteger(1);
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildValueTypeTypeRefRow4DesyncImage(int elementCount)
    {
        var metadata = CreateMetadata("VtDesync");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("A"),
            metadata.GetOrAddString("T1"));
        metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("A"),
            metadata.GetOrAddString("T2"));
        metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("A"),
            metadata.GetOrAddString("T3"));
        TypeReferenceHandle typeRef4 = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("A"),
            metadata.GetOrAddString("T4"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("MyAttr`2"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, attributeType);
        typeSpecSignature.WriteCompressedInteger(3);
        typeSpecSignature.WriteByte(0x11);
        WriteTypeDefOrRef(typeSpecSignature, typeRef4);
        typeSpecSignature.WriteByte(0x08);
        typeSpecSignature.WriteByte(0x1d);
        typeSpecSignature.WriteByte(0x08);
        TypeSpecificationHandle typeSpec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        constructorSignature.WriteByte(0x13);
        constructorSignature.WriteCompressedInteger(1);
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildSelfReferentialGenericVarImage()
    {
        var metadata = CreateMetadata("VarSo");
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("MyAttr`1"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, attributeType);
        typeSpecSignature.WriteCompressedInteger(1);
        typeSpecSignature.WriteByte(0x13);
        typeSpecSignature.WriteCompressedInteger(0);
        TypeSpecificationHandle typeSpec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        constructorSignature.WriteByte(0x13);
        constructorSignature.WriteCompressedInteger(0);
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(0);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildGenericAttributeInt32Image()
    {
        var metadata = CreateMetadata("GenericAttr");
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("MyAttr`1"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, attributeType);
        typeSpecSignature.WriteCompressedInteger(1);
        typeSpecSignature.WriteByte(0x08);
        TypeSpecificationHandle typeSpec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        constructorSignature.WriteByte(0x13);
        constructorSignature.WriteCompressedInteger(0);
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(5);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static void WriteTypeDefOrRef(BlobBuilder signature, EntityHandle handle)
    {
        int tag = handle.Kind switch
        {
            HandleKind.TypeDefinition => 0,
            HandleKind.TypeReference => 1,
            HandleKind.TypeSpecification => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(handle)),
        };
        signature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(handle) << 2 | tag);
    }

    static byte[] BuildNamedEnumInt32Image()
    {
        var metadata = CreateMetadata("NamedEnum");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            _ => { },
            parameterCount: 0);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(1);
        value.WriteByte(0x53);
        value.WriteByte(0x55);
        value.WriteSerializedString("Samples.Missing");
        value.WriteSerializedString("F");
        value.WriteInt32(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildLocalInt64EnumImage()
    {
        var metadata = CreateMetadata("LocalEnum");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        TypeDefinitionHandle enumDef = MetadataTokens.TypeDefinitionHandle(2);
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Type(enumDef, isValueType: true));
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt64(7);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    [Fact]
    public void EscapedTypeDefEnumName_DecodesTheDefinitionWidth()
    {
        // A metadata type name may contain a backslash, which reflection type
        // names use as an escape. The provider must find such a TypeDef by its
        // exact spelling, or an Int64 enum silently decodes as Int32.
        using var image = Open(BuildEscapedTypeDefEnumImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var decoded = AttributeDecoder.TryDecode(image.Reader, attribute);
        Assert.NotNull(decoded);
        Assert.Equal(7L, decoded.Value.FixedArguments[0].Value);
    }

    [Fact]
    public void EscapedTypeDefEnumName_DecodesAndStaysBounded()
    {
        // A handle-derived enum resolves from its definition without unescaping
        // the metadata name. Its full value is consumed before the following
        // array count is decoded.
        using var image = Open(BuildEscapedTypeDefEnumDesyncImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int maxCharge = 0;
        var decoded = AttributeDecoder.TryDecode(
            image.Reader,
            attribute,
            count => maxCharge = Math.Max(maxCharge, count),
            (Func<string, PrimitiveTypeCode>?)null);

        Assert.NotNull(decoded);
        Assert.True(
            maxCharge < 1_000,
            $"Decoding charged {maxCharge}; the enum should resolve to its "
                + "Int64 width, not read its payload as an array count.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedTypeNameCollision_DecodesAndStaysBounded(bool viaTypeReference)
    {
        // A nested type joins its declaring type with '.', the same separator
        // used between a namespace and a type name, so a nested Kind inside
        // Samples.E and a top-level Kind in namespace Samples.E render to one
        // string. Resolving the width by that string takes the first definition
        // indexed, so the decoder must resolve the argument structurally from
        // its own handle to consume its full Int64 value.
        //
        // The argument is declared both ways because a reference carries a
        // resolution scope that its flattened spelling discards, so it collides
        // on the same string for the same reason a definition does.
        using var image = Open(BuildNestedNameCollisionDesyncImage(viaTypeReference));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int maxCharge = 0;
        var decoded = AttributeDecoder.TryDecode(
            image.Reader,
            attribute,
            count => maxCharge = Math.Max(maxCharge, count),
            (Func<string, PrimitiveTypeCode>?)null);

        Assert.NotNull(decoded);
        Assert.True(
            maxCharge < 1_000,
            $"Decoding charged {maxCharge}; the enum should resolve to its "
                + "Int64 width, not read its payload as an array count.");
    }

    [Fact]
    public void ExternalReferenceCollidingWithNestedName_IsRefusedNotDecoded()
    {
        // The reference names Kind in namespace Samples.E of another assembly,
        // so it matches no definition here: the only local candidate is nested,
        // and a nested definition cannot answer a top-level reference. The
        // reference still renders to "Samples.E.Kind", which is exactly how the
        // nested Int64 enum is spelled once assembly qualification is stripped,
        // so the decoder reaches that definition through the name index and
        // consumes eight bytes.
        //
        // The decoder reaches the following count at that width and refuses
        // it before charging or allocating the array.
        using var image = Open(
            BuildExternalReferenceNestedCollisionImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);

        int maxCharge = 0;
        var decoded = AttributeDecoder.TryDecode(
            image.Reader,
            attribute,
            count => maxCharge = Math.Max(maxCharge, count),
            (Func<string, PrimitiveTypeCode>?)null);

        Assert.Null(decoded);

        // An explicit Int64 resolver reaches the same refusal and charge.
        int charged = 0;
        Assert.Null(
            AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = Math.Max(charged, count),
                _ => PrimitiveTypeCode.Int64));
        AssertHostileCountWasNotCharged(charged);
        Assert.Equal(charged, maxCharge);
    }

    [Fact]
    public void CyclicNestingAndResolutionScope_TerminatesInsteadOfOverflowing()
    {
        // Structural matching walks two chains outward at once: a reference's
        // resolution scope and a definition's declaring type. Neither is
        // trustworthy. A NestedClass table naming two types as each other's
        // declaring type, paired with two references naming each other as
        // resolution scope, advances both chains forever.
        //
        // The decoder resolves handle-typed enums through this path, so the
        // structural walk must stay bounded.
        using var image = Open(BuildCyclicNestingImage());
        MetadataReader reader = image.Reader;

        TypeDefinition a = reader.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle(2));
        TypeDefinition b = reader.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle(3));
        Assert.Equal(3, MetadataTokens.GetRowNumber(a.GetDeclaringType()));
        Assert.Equal(2, MetadataTokens.GetRowNumber(b.GetDeclaringType()));

        TypeReference refA = reader.GetTypeReference(MetadataTokens.TypeReferenceHandle(2));
        TypeReference refB = reader.GetTypeReference(MetadataTokens.TypeReferenceHandle(3));
        Assert.Equal(HandleKind.TypeReference, refA.ResolutionScope.Kind);
        Assert.Equal(HandleKind.TypeReference, refB.ResolutionScope.Kind);

        Assert.False(
            EnumUnderlyingPrimitive.TryResolveDefinition(
                reader,
                MetadataTokens.TypeReferenceHandle(2),
                out _));
    }

    static byte[] BuildCyclicNestingImage()
    {
        var metadata = CreateMetadata("CyclicNesting");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));

        // Rows 2 and 3 name each other as resolution scope.
        TypeReferenceHandle referenceA = metadata.AddTypeReference(
            MetadataTokens.TypeReferenceHandle(3),
            default,
            metadata.GetOrAddString("A"));
        TypeReferenceHandle referenceB = metadata.AddTypeReference(
            MetadataTokens.TypeReferenceHandle(2),
            default,
            metadata.GetOrAddString("B"));
        Assert.Equal(2, MetadataTokens.GetRowNumber(referenceA));
        Assert.Equal(3, MetadataTokens.GetRowNumber(referenceB));

        var int32Field = new BlobBuilder();
        new BlobEncoder(int32Field).FieldSignature().Int32();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(int32Field));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle definitionA = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("A"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle definitionB = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("B"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        // And each is declared inside the other.
        metadata.AddNestedType(definitionA, definitionB);
        metadata.AddNestedType(definitionB, definitionA);

        return Serialize(metadata);
    }

    [Fact]
    public void NestingDeeperThanTheMatchBound_ResolvesTheDefinitionWidth()
    {
        // Past MaxNestingDepth the structural match gives up, so the handle
        // yields no answer and the decoder falls back to the rendered name. The
        // name index reaches the same definition, so the deep enum resolves to
        // its real Int64 width, the 100M is consumed as the enum's own eight
        // bytes, and the following array count is the genuine 1.
        using var image = Open(BuildDeeplyNestedImage(depth: 200, elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);

        int decodeCharge = 0;
        var decoded = AttributeDecoder.TryDecode(
            image.Reader,
            attribute,
            count => decodeCharge = Math.Max(decodeCharge, count),
            (Func<string, PrimitiveTypeCode>?)null);

        Assert.NotNull(decoded);
        Assert.True(
            decodeCharge < 1_000,
            $"Decoding charged {decodeCharge}; the deep enum should resolve to "
                + "its Int64 width.");
    }

    static byte[] BuildDeeplyNestedImage(int depth, int elementCount)
    {
        var metadata = CreateMetadata("DeepNesting");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));

        // A reference chain of the requested depth, each scoped to the one
        // outside it. The innermost is the enum the argument is declared as.
        TypeReferenceHandle deepest = metadata.AddTypeReference(
            default,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("N0"));
        for (int i = 1; i < depth; i++)
        {
            deepest = metadata.AddTypeReference(
                deepest,
                default,
                metadata.GetOrAddString("N" + i));
        }

        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(deepest, isValueType: true);
                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));

        var int64Field = new BlobBuilder();
        new BlobEncoder(int64Field).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(int64Field));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        // The matching definition chain, nested to the same depth.
        var definitions = new TypeDefinitionHandle[depth];
        definitions[0] = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("N0"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int i = 1; i < depth; i++)
        {
            definitions[i] = metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic | TypeAttributes.Sealed,
                default,
                metadata.GetOrAddString("N" + i),
                systemEnum,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }

        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));

        for (int i = 1; i < depth; i++)
            metadata.AddNestedType(definitions[i], definitions[i - 1]);

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(0);
        value.WriteInt32(elementCount);
        value.WriteInt32(1);
        value.WriteInt32(42);
        value.WriteUInt16(0);

        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));

        return Serialize(metadata);
    }

    static byte[] BuildSignatureTypedEnumArrayImage(int elementCount)
    {
        var metadata = CreateMetadata("ProbeSigEnumAmp");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default, default, default, default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        TypeDefinitionHandle enumDef = MetadataTokens.TypeDefinitionHandle(2);

        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type()
                    .SZArray().Type(enumDef, isValueType: true));
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));

        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().Int32();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName
                | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default, default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Colors"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        for (int i = 0; i < elementCount; i++)
            value.WriteInt32(i);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildGenericParameterArrayImage(int elementCount, int argCount)
    {
        var metadata = CreateMetadata("ProbeGenericAmp");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default, default, default, default);
        TypeReferenceHandle genericType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("G`1"));

        var specBlob = new BlobBuilder();
        var args = new BlobEncoder(specBlob)
            .TypeSpecificationSignature()
            .GenericInstantiation(genericType, argCount, isValueType: true);
        for (int i = 0; i < argCount; i++)
            args.AddArgument().Int32();
        TypeSpecificationHandle spec =
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(specBlob));

        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type()
                    .SZArray().GenericTypeParameter(0));
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            spec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        for (int i = 0; i < elementCount; i++)
            value.WriteInt32(i);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildCmodArrayImage(int elementCount, int cmodCount)
    {
        var metadata = CreateMetadata("ProbeCmodAmp");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default, default, default, default);
        TypeReferenceHandle modifier = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("IsConst"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));

        var sig = new BlobBuilder();
        sig.WriteByte(0x20);
        sig.WriteCompressedInteger(1);
        sig.WriteByte(0x01);
        sig.WriteByte(0x1d);
        for (int i = 0; i < cmodCount; i++)
        {
            sig.WriteByte(0x20);
            sig.WriteCompressedInteger(
                CodedIndex.TypeDefOrRefOrSpec(modifier));
        }

        sig.WriteByte(0x08);
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(sig));

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        for (int i = 0; i < elementCount; i++)
            value.WriteInt32(i);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    [Fact]
    public void ArrayElementCustomModifiers_AreRefused()
    {
        // A custom modifier prefixing an array element type is refused. The
        // same array with no modifiers decodes, so the refusal is the
        // modifier's and not the array's.
        using var plain = Open(BuildCmodArrayImage(elementCount: 3, cmodCount: 0));
        CustomAttribute plainAttribute = FirstAttribute(plain.Reader);
        Assert.NotNull(AttributeDecoder.TryDecode(plain.Reader, plainAttribute));

        using var modified = Open(BuildCmodArrayImage(elementCount: 3, cmodCount: 1));
        CustomAttribute modifiedAttribute = FirstAttribute(modified.Reader);
        Assert.Null(
            AttributeDecoder.TryDecode(modified.Reader, modifiedAttribute));
    }

    [Fact]
    public void GenericParameterArrayElements_ResolveTheTypeSpecOnce()
    {
        // Resolve a VAR element through the constructor's TypeSpec once,
        // rather than re-validating that blob for every materialized value.
        static long Measure(int elementCount)
        {
            using var image = Open(
                BuildGenericParameterArrayImage(elementCount, 20_000));
            CustomAttribute attribute = FirstAttribute(image.Reader);
            Assert.NotNull(
                AttributeDecoder.TryDecode(
                    image.Reader,
                    attribute));
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.NotNull(
                AttributeDecoder.TryDecode(
                    image.Reader,
                    attribute));
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        long one = Measure(1);
        long many = Measure(100);
        Assert.True(
            many < one + 100_000,
            $"Decoding one element allocated {one} bytes and 100 allocated "
                + $"{many}; the TypeSpec is being re-validated per element.");
    }

    [Fact]
    public void SignatureTypedArrayElements_TypeNameChargeIsIndependentOfElementCount()
    {
        // Materialized array slots scale with the element count. After their
        // declared charge is removed, the name-rendering charge stays constant.
        static int MeasureNameCharge(int elementCount)
        {
            using var image = Open(
                BuildSignatureTypedEnumArrayImage(elementCount));
            CustomAttribute attribute = FirstAttribute(image.Reader);
            int charged = 0;
            var decoded = AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count));
            Assert.NotNull(decoded);
            var values = Assert.IsType<ImmutableArray<CustomAttributeTypedArgument<string>>>(
                decoded.Value.FixedArguments[0].Value);
            Assert.Equal(elementCount, values.Length);
            return charged
                - (elementCount + 1) * CustomAttributeValueDecoder.DeclaredSlotCharge;
        }

        int one = MeasureNameCharge(1);
        int many = MeasureNameCharge(100_000);
        Assert.True(one > 0);
        Assert.Equal(one, many);
    }

    [Fact]
    public void EnumArrayElements_ResolveTheWidthOncePerName()
    {
        // Every element of a typed enum array carries the same enum name. The
        // decoder resolves that name once before materializing the values.
        static void AssertDecoded(int elementCount)
        {
            using var image = Open(BuildNamedEnumArrayImage(elementCount));
            CustomAttribute attribute = FirstAttribute(image.Reader);
            var callbackNames = new List<string>();
            var decoded = AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                beforeMaterialize: null,
                name =>
                {
                    callbackNames.Add(name);
                    return PrimitiveTypeCode.Int32;
                });
            Assert.NotNull(decoded);
            Assert.Equal("Samples.Colors", Assert.Single(callbackNames));
            var values = Assert.IsType<ImmutableArray<CustomAttributeTypedArgument<string>>>(
                decoded.Value.FixedArguments[0].Value);
            Assert.Equal(elementCount, values.Length);
        }

        AssertDecoded(1);
        AssertDecoded(10_000);
    }

    static byte[] BuildEscapedTypeDefEnumImage()
    {
        var metadata = CreateMetadata("BackslashEnum");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        TypeDefinitionHandle enumDef = MetadataTokens.TypeDefinitionHandle(2);
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Type(enumDef, isValueType: true));
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E\\\\F"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt64(7);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }


    static byte[] BuildEscapedTypeDefEnumDesyncImage()
    {
        var metadata = CreateMetadata("BackslashDesync");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        TypeDefinitionHandle enumDef = MetadataTokens.TypeDefinitionHandle(2);
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(enumDef, isValueType: true);
                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E\\\\F"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        // 8-byte enum slot. Reading only 4 would take the trailing four
        // bytes as the following array's declared count.
        value.WriteInt32(0);
        value.WriteInt32(100_000_000);
        // The count decoded after consuming the full 8 bytes.
        value.WriteInt32(1);
        value.WriteInt32(42);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    [Fact]
    public void CollidingTypeDefNames_EachResolveTheirOwnWidth()
    {
        // Gates the premise of the decode case above: the two definitions
        // really do render to one string, so a name-keyed index cannot tell
        // them apart and the width must come from the definition instead.
        using var image = Open(BuildNestedNameCollisionDesyncImage());
        var provider = new CustomAttributeValueDecoder.Classifier(
            image.Reader,
            preserveSerializedTypeNames: false,
            beforeMaterialize: null,
            enumUnderlyingType: null);
        var decoy = MetadataTokens.TypeDefinitionHandle(2);
        var nested = MetadataTokens.TypeDefinitionHandle(4);

        string decoyName = provider.GetTypeFromDefinition(image.Reader, decoy, 0);
        PrimitiveTypeCode decoyWidth = provider.GetUnderlyingEnumType(decoyName);
        string nestedName = provider.GetTypeFromDefinition(image.Reader, nested, 0);
        PrimitiveTypeCode nestedWidth = provider.GetUnderlyingEnumType(nestedName);

        Assert.Equal(nestedName, decoyName);
        Assert.Equal(
            EnumUnderlyingPrimitive.FromDefinition(image.Reader, decoy),
            decoyWidth);
        Assert.Equal(
            EnumUnderlyingPrimitive.FromDefinition(image.Reader, nested),
            nestedWidth);
        Assert.NotEqual(decoyWidth, nestedWidth);
    }

    static byte[] BuildExternalReferenceNestedCollisionImage(int elementCount)
    {
        var metadata = CreateMetadata("ExternalNestedCollision");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));

        // Top-level, and scoped to another assembly. There is no local
        // top-level Kind in namespace Samples.E, so this resolves to nothing
        // here -- yet it renders to the same string as the local nested Kind.
        TypeReferenceHandle externalKind = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("Samples.E"),
            metadata.GetOrAddString("Kind"));

        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type()
                        .Type(externalKind, isValueType: true);
                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));

        var int64Field = new BlobBuilder();
        new BlobEncoder(int64Field).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(int64Field));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle declaring = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle nested = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("Kind"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(nested, declaring);

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        // Four bytes of enum for an unresolved width, then a benign
        // count of one. A decode that reaches the nested Int64 through the
        // flattened name swallows both as the enum and takes the element below
        // as the count instead.
        value.WriteInt32(0);
        value.WriteInt32(1);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);

        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));

        return Serialize(metadata);
    }

    static byte[] BuildNestedNameCollisionDesyncImage(bool viaTypeReference = false)
    {
        var metadata = CreateMetadata("NestedCollision");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));

        // Row 4 is the nested Kind, the type the argument is actually declared
        // as. Row 2 is the top-level decoy that renders to the same string and
        // is indexed first. The reference form names row 4 through its
        // resolution scope, which the rendered spelling discards.
        TypeDefinitionHandle nestedEnum = MetadataTokens.TypeDefinitionHandle(4);
        TypeReferenceHandle declaringReference = metadata.AddTypeReference(
            default,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"));
        TypeReferenceHandle nestedReference = metadata.AddTypeReference(
            declaringReference,
            default,
            metadata.GetOrAddString("Kind"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    if (viaTypeReference)
                    {
                        parameters.AddParameter().Type()
                            .Type(nestedReference, isValueType: true);
                    }
                    else
                    {
                        parameters.AddParameter().Type()
                            .Type(nestedEnum, isValueType: true);
                    }

                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));

        var int32Field = new BlobBuilder();
        new BlobEncoder(int32Field).FieldSignature().Int32();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(int32Field));
        var int64Field = new BlobBuilder();
        new BlobEncoder(int64Field).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(int64Field));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples.E"),
            metadata.GetOrAddString("Kind"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle declaring = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle nested = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("Kind"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(3),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(nested, declaring);

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        // 8-byte enum slot. A decoder that reads only 4 takes the trailing four
        // bytes as the following array's declared count.
        value.WriteInt32(0);
        value.WriteInt32(100_000_000);
        // The count decoded after consuming the full 8 bytes.
        value.WriteInt32(1);
        value.WriteInt32(42);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildDuplicateTypeDefEnumImage(int elementCount)
    {
        var metadata = CreateMetadata("DupEnum");
        var int32Field = new BlobBuilder();
        new BlobEncoder(int32Field).FieldSignature().Int32();
        var int64Field = new BlobBuilder();
        new BlobEncoder(int64Field).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(int32Field));
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(int64Field));
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        TypeDefinitionHandle int64Enum = MetadataTokens.TypeDefinitionHandle(3);
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(int64Enum, isValueType: true);
                    parameters.AddParameter().Type().SZArray().Int32();
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
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(3),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(0);
        value.WriteInt32(elementCount);
        value.WriteInt32(0);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildDeepJaggedSzArrayImage(int depth, int count)
    {
        var metadata = CreateMetadata("Jagged");
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        for (int index = 0; index < depth; index++)
            constructorSignature.WriteByte(0x1d);
        constructorSignature.WriteByte(0x08);
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        for (int index = 0; index < depth; index++)
            value.WriteInt32(count);
        for (int index = 0; index < count; index++)
            value.WriteByte(0);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static PrimitiveTypeCode FirstWinsEnumWidth(MetadataReader reader, string name)
    {
        string normalized = EnumUnderlyingPrimitive.NormalizeSerializedName(name);
        foreach (var handle in reader.TypeDefinitions)
        {
            if (TypeResolver.GetTypeNameFromDefinition(reader, handle) == normalized)
                return EnumUnderlyingPrimitive.FromDefinition(reader, handle);
        }

        return PrimitiveTypeCode.Int32;
    }

    static MemberReferenceHandle AddConstructor(
        MetadataBuilder metadata,
        Action<ParametersEncoder> parameters,
        int parameterCount)
    {
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                parameterCount,
                returnType => returnType.Void(),
                parameters);
        return metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
    }

    static void AddAttributedType(
        MetadataBuilder metadata,
        MemberReferenceHandle constructor,
        BlobBuilder value)
    {
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddCustomAttribute(
            type,
            constructor,
            metadata.GetOrAddBlob(value));
    }

    sealed class LoadedImage(byte[] image) : IDisposable
    {
        readonly PEReader _peReader = new(
            new MemoryStream(image, writable: false));

        public MetadataReader Reader => _peReader.GetMetadataReader();

        public void Dispose() => _peReader.Dispose();
    }

    static MetadataBuilder CreateMetadata(string assemblyName)
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


    static byte[] BuildNamedEnumArrayImage(int elementCount)
    {
        var metadata = CreateMetadata("ProbeEnumAmp");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters => parameters.AddParameter().Type().Object(),
            parameterCount: 1);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteByte(0x1d);
        value.WriteByte(0x55);
        value.WriteSerializedString("Samples.Colors");
        value.WriteInt32(elementCount);
        for (int i = 0; i < elementCount; i++)
            value.WriteInt32(i);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    [Fact]
    public void EscapedBlobEnumName_ResolvesTheEscapedSpelling()
    {
        // A blob-authored name is reflection syntax, so `E\\+Kind` names the
        // metadata type `E+Kind` and not one spelled with a backslash. Matching
        // the blob spelling verbatim would pick the wrong local enum, so only
        // handle-derived names are matched exactly.
        using var image = Open(BuildEscapeCollisionImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var decoded = AttributeDecoder.TryDecode(image.Reader, attribute);
        Assert.NotNull(decoded);
        Assert.Equal(7L, decoded.Value.FixedArguments[0].Value);
    }

    [Fact]
    public void BlobAuthoredNameDoesNotChangeALaterHandleDerivedLookup()
    {
        // Provenance belongs to one pending lookup, not to a spelling. If a
        // blob names a spelling that a handle-derived name also uses, the
        // handle-derived occurrence must still resolve to its exact metadata
        // type. Remembering spellings instead made the second occurrence
        // resolve as reflection syntax, so the width a decode consumed
        // depended on where the name appeared in the blob.
        using var image = Open(BuildEscapeCollisionImage());
        var provider = new CustomAttributeValueDecoder.Classifier(
            image.Reader,
            preserveSerializedTypeNames: false,
            beforeMaterialize: null,
            enumUnderlyingType: null);
        const string Spelling = @"Samples.E\+Kind";

        PrimitiveTypeCode before = provider.GetUnderlyingEnumType(Spelling);

        provider.GetTypeFromSerializedName(Spelling);
        PrimitiveTypeCode blob = provider.GetUnderlyingEnumType(Spelling);

        PrimitiveTypeCode after = provider.GetUnderlyingEnumType(Spelling);

        Assert.Equal(PrimitiveTypeCode.Int32, before);
        Assert.Equal(PrimitiveTypeCode.Int64, blob);
        Assert.Equal(before, after);
    }

    static byte[] BuildEscapeCollisionImage()
    {
        var metadata = CreateMetadata("EscapeCollision");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default, default, default, default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));

        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Object());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));

        // Field 1: value__ for the backslash-named enum (Int32).
        var i4 = new BlobBuilder();
        new BlobEncoder(i4).FieldSignature().Int32();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(i4));
        // Field 2: value__ for the plus-named enum (Int64).
        var i8 = new BlobBuilder();
        new BlobEncoder(i8).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(i8));

        metadata.AddTypeDefinition(
            default, default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        // Metadata name literally contains a backslash then a plus.
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E\\+Kind"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        // Metadata name literally contains a plus.
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E+Kind"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(3),
            MetadataTokens.MethodDefinitionHandle(1));

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteByte(0x55);
        // Reflection spelling: the escaped plus denotes the metadata name "E+Kind".
        value.WriteSerializedString("Samples.E\\+Kind");
        value.WriteInt64(7);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

}
