using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

enum AttributePreflightByteEnum : byte
{
    Value = 1,
}

[AttributeUsage(AttributeTargets.Class)]
sealed class AttributePreflightSampleAttribute(
    string text,
    object boxed,
    AttributePreflightByteEnum kind,
    Type type,
    string[]? items) : Attribute
{
    public string Text { get; } = text;
    public object Boxed { get; } = boxed;
    public AttributePreflightByteEnum Kind { get; } = kind;
    public Type Type { get; } = type;
    public string[]? Items { get; } = items;
    public string? Named { get; set; }
    public int Number { get; set; }
    public bool Enabled { get; set; }
}

[AttributePreflightSample(
    "fixed \u03bb\n",
    "boxed",
    AttributePreflightByteEnum.Value,
    typeof(string),
    null,
    Named = "named \ud83d\ude00",
    Number = 42,
    Enabled = true)]
sealed class AttributePreflightSample;

/// <summary>
/// The scope-uniform attribute API: assembly, module, type, and member
/// attributes all render through the same core, exercised here against CoreLib.
/// </summary>
public class AttributeReaderTests
{
    static PEReader OpenCoreLib() => new(File.OpenRead(typeof(object).Assembly.Location));

    [Fact]
    public void RenderAssemblyAttributes_IncludesClsCompliant()
    {
        using var pe = OpenCoreLib();
        var rendered = AttributeReader.RenderAssemblyAttributes(pe.GetMetadataReader());

        // CoreLib is marked [assembly: CLSCompliant(true)].
        Assert.Contains("CLSCompliant(true)", rendered);
    }

    [Fact]
    public void RenderAttributes_ByTypeHandle_FindsFlagsEnum()
    {
        using var pe = OpenCoreLib();
        var reader = pe.GetMetadataReader();
        var handle = reader.TypeDefinitions.Single(h =>
            TypeResolver.GetFullName(reader, reader.GetTypeDefinition(h)) == "System.AttributeTargets");

        Assert.Contains("Flags", AttributeReader.RenderAttributes(reader, handle));
    }

    [Fact]
    public void RenderAttributes_NoNamespaceSet_DoesNotThrow()
    {
        // The namespace accumulator is optional — callers that only want the
        // rendered text omit it.
        using var pe = OpenCoreLib();
        var rendered = AttributeReader.RenderModuleAttributes(pe.GetMetadataReader());

        Assert.NotNull(rendered);
    }

    [Fact]
    public void AttributeDecoder_ResolvesNameAndDecodesArgument()
    {
        // The shared decode primitive: CoreLib is [assembly: CLSCompliant(true)].
        using var pe = OpenCoreLib();
        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attr = reader.GetCustomAttribute(handle);
            if (AttributeDecoder.GetAttributeTypeName(reader, attr.Constructor) != "System.CLSCompliantAttribute")
                continue;
            var value = AttributeDecoder.TryDecode(reader, attr);
            Assert.NotNull(value);
            Assert.Equal(true, value!.Value.FixedArguments[0].Value);
            return;
        }
        Assert.Fail("CLSCompliant attribute not found on CoreLib");
    }

    [Theory]
    [InlineData("Attribute", "Attribute")]
    [InlineData("Samples.Attribute", "Samples.Attribute")]
    [InlineData("Samples.WidgetAttribute", "Samples.Widget")]
    [InlineData("Samples.AttributeAttribute", "Samples.Attribute")]
    public void QualifiedAttributeName_DoesNotStripToInvalidName(string fullName, string expected)
    {
        var method = typeof(AttributeReader).GetMethod(
            "GetQualifiedAttributeName",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expected, method!.Invoke(null, [fullName]));
    }

    [Fact]
    public void RenderAttributes_GiantScalar_PreflightsWithoutMaterializingString()
    {
        const int characterCount = 30_000_000;
        using var provider = BuildSyntheticAttribute(
            array: false,
            value => WriteRepeatedString(value, characterCount));
        var reader = provider.GetMetadataReader();
        var attributes = SyntheticTargetAttributes(reader);
        long observedLowerBound = -1;

        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<StopPreflightException>(() => AttributeReader.RenderAttributes(
            reader,
            attributes,
            preflight: lowerBound =>
            {
                // Type-name materialization is gated first; the value lower bound follows.
                if (lowerBound < characterCount)
                    return;
                observedLowerBound = lowerBound;
                throw new StopPreflightException();
            }));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(characterCount, observedLowerBound);
        Assert.True(allocated < 8_000_000, $"Preflight allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void RenderAttributes_GiantNamedArgumentName_PreflightsWithoutMaterializingString()
    {
        const int characterCount = 20_000_000;
        using var provider = BuildSyntheticNamedStringAttribute(
            value => WriteRepeatedString(value, characterCount));
        var reader = provider.GetMetadataReader();
        var attributes = reader.GetTypeDefinition(
            reader.TypeDefinitions.Single(handle =>
                reader.GetString(reader.GetTypeDefinition(handle).Name) == "Target"))
            .GetCustomAttributes();
        long observedLowerBound = -1;

        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<StopPreflightException>(() => AttributeReader.RenderAttributes(
            reader,
            attributes,
            preflight: lowerBound =>
            {
                if (lowerBound < characterCount)
                    return;
                observedLowerBound = lowerBound;
                throw new StopPreflightException();
            }));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Name characters plus the short retained field value ("x").
        Assert.Equal(characterCount + 1, observedLowerBound);
        Assert.True(allocated < 8_000_000, $"Preflight allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void RenderAttributes_AssemblyQualifiedType_SkipsWithoutMaterializingSuffix()
    {
        const int suffixCharacters = 20_000_000;
        using var provider = BuildSyntheticTypeAttributeWithAssemblySuffix(suffixCharacters);
        var reader = provider.GetMetadataReader();
        TypeDefinitionHandle target = reader.TypeDefinitions.Single(
            handle => reader.GetString(
                reader.GetTypeDefinition(handle).Name) == "Target");
        CustomAttributeHandleCollection attributes =
            reader.GetTypeDefinition(target).GetCustomAttributes();
        long maxPreflight = 0;

        long before = GC.GetAllocatedBytesForCurrentThread();
        var rendered = AttributeReader.RenderAttributes(
            reader,
            attributes,
            preflight: lowerBound => maxPreflight = Math.Max(maxPreflight, lowerBound));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Empty(rendered);
        // Type-name gate may fire; the hostile AQ suffix must not appear as a bound.
        Assert.True(maxPreflight < 1_000, $"Preflight saw {maxPreflight:N0} characters.");
        Assert.True(allocated < 8_000_000, $"Preflight allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void RenderAttributes_StringBackedEnum_PreflightsWithoutMaterializingString()
    {
        // Hostile metadata can declare value__ : string. The decoder then treats
        // the enum argument as a length-prefixed string. Preflight must use the
        // same layout and charge the string before DecodeValue materializes it.
        const int characterCount = 20_000_000;
        using var provider = BuildSyntheticStringBackedEnumAttribute(
            value => WriteRepeatedString(value, characterCount));
        var reader = provider.GetMetadataReader();
        var attributes = reader.GetTypeDefinition(
            reader.TypeDefinitions.Single(handle =>
                reader.GetString(reader.GetTypeDefinition(handle).Name) == "Target"))
            .GetCustomAttributes();
        long observedLowerBound = -1;

        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<StopPreflightException>(() => AttributeReader.RenderAttributes(
            reader,
            attributes,
            preflight: lowerBound =>
            {
                if (lowerBound < characterCount)
                    return;
                observedLowerBound = lowerBound;
                throw new StopPreflightException();
            }));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(characterCount, observedLowerBound);
        Assert.True(allocated < 8_000_000, $"Preflight allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void RenderAttributes_StringBackedEnum_OrdinaryValueMatchesUnbounded()
    {
        const string text = "fixed \u03bb\n";
        using var provider = BuildSyntheticStringBackedEnumAttribute(value =>
        {
            byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(text);
            value.WriteCompressedInteger(utf8.Length);
            value.WriteBytes(utf8);
        });
        var reader = provider.GetMetadataReader();
        var attributes = reader.GetTypeDefinition(
            reader.TypeDefinitions.Single(handle =>
                reader.GetString(reader.GetTypeDefinition(handle).Name) == "Target"))
            .GetCustomAttributes();

        var expected = AttributeReader.RenderAttributes(reader, attributes, qualifyNames: true);
        var actual = AttributeReader.RenderAttributes(
            reader,
            attributes,
            qualifyNames: true,
            preflight: _ => { });

        Assert.Equal(expected, actual);
        Assert.Single(actual);
        Assert.Contains("fixed", actual[0], StringComparison.Ordinal);
        Assert.Contains("\\n", actual[0], StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAttributes_GiantNonNullArray_SkipsWithoutMaterializingArray()
    {
        const int elementCount = 5_000_000;
        using var provider = BuildSyntheticAttribute(
            array: true,
            value =>
            {
                value.WriteUInt32(elementCount);
                value.WriteBytes(0xff, elementCount);
            });
        var reader = provider.GetMetadataReader();
        long maxPreflight = 0;

        long before = GC.GetAllocatedBytesForCurrentThread();
        var rendered = AttributeReader.RenderAttributes(
            reader,
            SyntheticTargetAttributes(reader),
            preflight: lowerBound => maxPreflight = Math.Max(maxPreflight, lowerBound));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Empty(rendered);
        // Type-name gate may fire; the array itself must not be charged or allocated.
        Assert.True(maxPreflight < 1_000, $"Preflight saw {maxPreflight:N0} characters.");
        Assert.True(allocated < 8_000_000, $"Preflight allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void RenderAttributes_OrdinaryAttribute_IsIdenticalWithPreflight()
    {
        using var pe = new PEReader(File.OpenRead(typeof(AttributePreflightSample).Assembly.Location));
        var reader = pe.GetMetadataReader();
        var attributes = SampleAttributes(reader);

        var expected = AttributeReader.RenderAttributes(reader, attributes, qualifyNames: true);
        var actual = AttributeReader.RenderAttributes(
            reader,
            attributes,
            qualifyNames: true,
            preflight: _ => { });

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RenderAttributes_PreflightLowerBound_DoesNotExceedRenderedLength()
    {
        using var pe = new PEReader(File.OpenRead(typeof(AttributePreflightSample).Assembly.Location));
        var reader = pe.GetMetadataReader();
        List<long> lowerBounds = [];
        List<string> rendered = [];

        AttributeReader.RenderAttributes(
            reader,
            SampleAttributes(reader),
            qualifyNames: true,
            beforeRetain: rendered.Add,
            preflight: lowerBounds.Add);

        // Value lower bounds only (type-name hard-cap gate does not fire for ordinary
        // sample attributes). Each bound must not exceed the retained spelling.
        Assert.Equal(rendered.Count, lowerBounds.Count);
        Assert.NotEmpty(rendered);
        Assert.All(
            lowerBounds.Zip(rendered),
            pair => Assert.InRange(pair.First, 0, pair.Second.Length));
    }

    [Fact]
    public void RenderAttributes_TypeArgumentSplitAcrossUtf8Chunks_DoesNotThrow()
    {
        using var provider = BuildSyntheticTypeAttribute();
        MetadataReader reader = provider.GetMetadataReader();
        TypeDefinitionHandle target = reader.TypeDefinitions.Single(
            handle => reader.GetString(
                reader.GetTypeDefinition(handle).Name) == "Target");
        CustomAttributeHandleCollection attributes =
            reader.GetTypeDefinition(target).GetCustomAttributes();
        List<long> lowerBounds = [];

        List<string> expected = AttributeReader.RenderAttributes(
            reader,
            attributes);
        List<string> actual = AttributeReader.RenderAttributes(
            reader,
            attributes,
            preflight: lowerBounds.Add);

        Assert.Equal(expected, actual);
        Assert.Single(actual);
        Assert.Single(lowerBounds);
        Assert.InRange(lowerBounds[0], 0, actual[0].Length);
    }

    static CustomAttributeHandleCollection SampleAttributes(MetadataReader reader)
    {
        var handle = reader.TypeDefinitions.Single(h =>
            TypeResolver.GetFullName(reader, reader.GetTypeDefinition(h))
                == typeof(AttributePreflightSample).FullName);
        return reader.GetTypeDefinition(handle).GetCustomAttributes();
    }

    static CustomAttributeHandleCollection SyntheticTargetAttributes(MetadataReader reader)
        => reader.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle(3)).GetCustomAttributes();

    static MetadataReaderProvider BuildSyntheticAttribute(
        bool array,
        Action<BlobBuilder> writeFixedValue)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Synthetic.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
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
        var attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Synthetic"),
            metadata.GetOrAddString("LargeAttribute"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var targetType = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Synthetic"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));

        var signature = new BlobBuilder();
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(1);
        signature.WriteByte((byte)SignatureTypeCode.Void);
        if (array)
            signature.WriteByte((byte)SignatureTypeCode.SZArray);
        signature.WriteByte((byte)SignatureTypeCode.String);
        var constructor = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(signature),
            -1,
            MetadataTokens.ParameterHandle(1));

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        writeFixedValue(value);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(targetType, constructor, metadata.GetOrAddBlob(value));

        var image = new BlobBuilder();
        new MetadataRootBuilder(metadata, suppressValidation: true).Serialize(image, 0, 0);
        return MetadataReaderProvider.FromMetadataImage(ImmutableArray.Create(image.ToArray()));
    }

    static MetadataReaderProvider BuildSyntheticNamedStringAttribute(
        Action<BlobBuilder> writeNamedName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("SyntheticNamed.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("SyntheticNamed"),
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
        var attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Synthetic"),
            metadata.GetOrAddString("LargeAttribute"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var targetType = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Synthetic"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));

        // Parameterless ctor; one named field argument carries the giant name.
        var ctorSignature = new BlobBuilder();
        ctorSignature.WriteByte(0x20);
        ctorSignature.WriteCompressedInteger(0);
        ctorSignature.WriteByte((byte)SignatureTypeCode.Void);
        var constructor = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(ctorSignature),
            -1,
            MetadataTokens.ParameterHandle(1));

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(1); // one named arg
        value.WriteByte(0x53); // field
        value.WriteByte((byte)SerializationTypeCode.String);
        writeNamedName(value);
        value.WriteCompressedInteger(1);
        value.WriteByte((byte)'x'); // short string value
        metadata.AddCustomAttribute(targetType, constructor, metadata.GetOrAddBlob(value));

        var image = new BlobBuilder();
        new MetadataRootBuilder(metadata, suppressValidation: true).Serialize(image, 0, 0);
        return MetadataReaderProvider.FromMetadataImage(ImmutableArray.Create(image.ToArray()));
    }

    static MetadataReaderProvider BuildSyntheticTypeAttributeWithAssemblySuffix(
        int suffixCharacters)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("SyntheticTypeSuffix.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("SyntheticTypeSuffix"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle coreLib = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Private.CoreLib"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemType = metadata.AddTypeReference(
            coreLib,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Type"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            coreLib,
            metadata.GetOrAddString("Synthetic"),
            metadata.GetOrAddString("TypeAttribute"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle target = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Synthetic"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var signature = new BlobBuilder();
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(1);
        signature.WriteByte((byte)SignatureTypeCode.Void);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(systemType));
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(signature));

        // "T," + giant assembly suffix — renderer cannot retain it; preflight must
        // fail closed before DecodeValue materializes the suffix.
        int total = 2 + suffixCharacters;
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteCompressedInteger(total);
        value.WriteByte((byte)'T');
        value.WriteByte((byte)',');
        value.WriteBytes((byte)'a', suffixCharacters);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            target,
            constructor,
            metadata.GetOrAddBlob(value));

        var image = new BlobBuilder();
        new MetadataRootBuilder(
            metadata,
            suppressValidation: true).Serialize(image, 0, 0);
        return MetadataReaderProvider.FromMetadataImage(
            ImmutableArray.Create(image.ToArray()));
    }

    static MetadataReaderProvider BuildSyntheticStringBackedEnumAttribute(
        Action<BlobBuilder> writeFixedValue)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("SyntheticEnum.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("SyntheticEnum"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var coreLib = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Private.CoreLib"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        var enumBase = metadata.AddTypeReference(
            coreLib,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var enumType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Synthetic"),
            metadata.GetOrAddString("StringBackedEnum"),
            enumBase,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Synthetic"),
            metadata.GetOrAddString("LargeAttribute"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var targetType = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Synthetic"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(2));

        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte((byte)SignatureKind.Field);
        fieldSignature.WriteByte((byte)SignatureTypeCode.String);
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));

        // hasthis, 1 arg, void, valuetype StringBackedEnum
        var ctorSignature = new BlobBuilder();
        ctorSignature.WriteByte(0x20);
        ctorSignature.WriteCompressedInteger(1);
        ctorSignature.WriteByte((byte)SignatureTypeCode.Void);
        ctorSignature.WriteByte(0x11); // ELEMENT_TYPE_VALUETYPE
        ctorSignature.WriteCompressedInteger(CodedIndex.TypeDefOrRef(enumType));
        var constructor = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(ctorSignature),
            -1,
            MetadataTokens.ParameterHandle(1));

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        writeFixedValue(value);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(targetType, constructor, metadata.GetOrAddBlob(value));

        var image = new BlobBuilder();
        new MetadataRootBuilder(metadata, suppressValidation: true).Serialize(image, 0, 0);
        return MetadataReaderProvider.FromMetadataImage(ImmutableArray.Create(image.ToArray()));
    }

    static MetadataReaderProvider BuildSyntheticTypeAttribute()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("SyntheticType.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("SyntheticType"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle coreLib = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Private.CoreLib"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemType = metadata.AddTypeReference(
            coreLib,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Type"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            coreLib,
            metadata.GetOrAddString("Synthetic"),
            metadata.GetOrAddString("TypeAttribute"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle target = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Synthetic"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var signature = new BlobBuilder();
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(1);
        signature.WriteByte((byte)SignatureTypeCode.Void);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(systemType));
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(signature));

        byte[] typeName = Enumerable.Repeat((byte)'a', 8192).ToArray();
        typeName[4093] = 0xf0;
        typeName[4094] = 0x9f;
        typeName[4095] = 0x98;
        typeName[4096] = 0x80;
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteCompressedInteger(typeName.Length);
        value.WriteBytes(typeName);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            target,
            constructor,
            metadata.GetOrAddBlob(value));

        var image = new BlobBuilder();
        new MetadataRootBuilder(
            metadata,
            suppressValidation: true).Serialize(image, 0, 0);
        return MetadataReaderProvider.FromMetadataImage(
            ImmutableArray.Create(image.ToArray()));
    }

    static void WriteRepeatedString(BlobBuilder value, int characterCount)
    {
        value.WriteCompressedInteger(characterCount);
        value.WriteBytes((byte)'a', characterCount);
    }

    sealed class StopPreflightException : Exception;
}
