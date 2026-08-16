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
                observedLowerBound = lowerBound;
                throw new StopPreflightException();
            }));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(characterCount, observedLowerBound);
        Assert.True(allocated < 8_000_000, $"Preflight allocated {allocated:N0} bytes.");
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
        bool callbackInvoked = false;

        long before = GC.GetAllocatedBytesForCurrentThread();
        var rendered = AttributeReader.RenderAttributes(
            reader,
            SyntheticTargetAttributes(reader),
            preflight: _ => callbackInvoked = true);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Empty(rendered);
        Assert.False(callbackInvoked);
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

        Assert.Equal(rendered.Count, lowerBounds.Count);
        Assert.NotEmpty(rendered);
        Assert.All(
            lowerBounds.Zip(rendered),
            pair => Assert.InRange(pair.First, 0, pair.Second.Length));
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

    static void WriteRepeatedString(BlobBuilder value, int characterCount)
    {
        value.WriteCompressedInteger(characterCount);
        value.WriteBytes((byte)'a', characterCount);
    }

    sealed class StopPreflightException : Exception;
}
