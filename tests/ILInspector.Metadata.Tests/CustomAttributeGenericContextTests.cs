using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using Samples = ILInspector.Metadata.Tests.CustomAttributeGenericContextSamples;

namespace ILInspector.Metadata.Tests;

public sealed class CustomAttributeGenericContextTests
{
    [Theory]
    [InlineData(typeof(Samples.PairFour), 4, 1)]
    [InlineData(typeof(Samples.PairEight), 8, 1)]
    [InlineData(typeof(Samples.RepeatedFour), 4, 3)]
    [InlineData(typeof(Samples.RepeatedEight), 8, 3)]
    [InlineData(typeof(Samples.AlternatingEight), 8, 3)]
    [InlineData(typeof(Samples.AscendingFour), 4, 3)]
    [InlineData(typeof(Samples.DescendingFour), 4, 3)]
    [InlineData(typeof(Samples.UnusedTail), 1, 0)]
    [InlineData(typeof(Samples.NoGenericParameters), 1, 0)]
    public void GenericArgumentLookup_ReusesVisitedPrefix(
        Type sample,
        int parameterCount,
        long expectedBytesSkipped)
    {
        using var pe = new PEReader(File.OpenRead(sample.Assembly.Location));
        MetadataReader reader = pe.GetMetadataReader();
        CustomAttribute attribute = SampleAttribute(reader, sample);
        var work = new CustomAttributeValueDecoder.GenericContextWork();

        Assert.True(CustomAttributeValueDecoder.TryDecode(
            reader,
            attribute,
            preserveSerializedTypeNames: false,
            captureDefaultedWidths: false,
            beforeMaterialize: null,
            enumUnderlyingType: null,
            out var measured,
            out _,
            out _,
            genericContextWork: work));
        var ordinary = AttributeDecoder.TryDecode(reader, attribute);

        Assert.NotNull(ordinary);
        Assert.Equal(parameterCount, measured.FixedArguments.Length);
        Assert.Equal(parameterCount, ordinary.Value.FixedArguments.Length);
        Assert.Empty(measured.NamedArguments);
        Assert.Empty(ordinary.Value.NamedArguments);
        for (int i = 0; i < parameterCount; i++)
        {
            Assert.Equal("int", measured.FixedArguments[i].Type);
            Assert.Equal(i + 1, measured.FixedArguments[i].Value);
            Assert.Equal(measured.FixedArguments[i], ordinary.Value.FixedArguments[i]);
        }
        Assert.Equal(expectedBytesSkipped, work.BytesSkipped);
    }

    [Fact]
    public void GenericArgumentLookup_DoesNotCacheResolvedEnumWidths()
    {
        Type sample = typeof(Samples.RepeatedEnum);
        using var pe = new PEReader(File.OpenRead(sample.Assembly.Location));
        MetadataReader reader = pe.GetMetadataReader();
        int resolutions = 0;

        var decoded = AttributeDecoder.TryDecodeDetailed(
            reader,
            SampleAttribute(reader, sample),
            enumUnderlyingType: (string name, out PrimitiveTypeCode width) =>
            {
                Assert.Equal("System.DayOfWeek", name);
                width = PrimitiveTypeCode.Int32;
                return ++resolutions % 2 == 0;
            });

        Assert.NotNull(decoded);
        Assert.Equal(4, resolutions);
        Assert.Equal([true, false, true, false],
            decoded.Value.FixedArgumentEnumWidthDefaulted);
        Assert.Equal([0, 1, 0, 1], decoded.Value.Value.FixedArguments
            .Select(argument => Assert.IsType<int>(argument.Value)));
    }

    [Fact]
    public void GenericArgumentLookup_PreservesMixedValuesAndCharges()
    {
        Type sample = typeof(Samples.MixedValues);
        using var pe = new PEReader(File.OpenRead(sample.Assembly.Location));
        MetadataReader reader = pe.GetMetadataReader();
        CustomAttribute attribute = SampleAttribute(reader, sample);
        var work = new CustomAttributeValueDecoder.GenericContextWork();
        var measuredCharges = new List<int>();
        var ordinaryCharges = new List<int>();

        Assert.True(CustomAttributeValueDecoder.TryDecode(
            reader,
            attribute,
            preserveSerializedTypeNames: false,
            captureDefaultedWidths: true,
            beforeMaterialize: measuredCharges.Add,
            enumUnderlyingType: null,
            out var measured,
            out var fixedFlags,
            out var namedFlags,
            genericContextWork: work));
        var ordinary = AttributeDecoder.TryDecode(reader, attribute, ordinaryCharges.Add);

        Assert.NotNull(ordinary);
        AssertValues(measured);
        AssertValues(ordinary.Value);
        Assert.Equal([false, false, false, false], fixedFlags);
        Assert.Empty(namedFlags);
        Assert.Equal([64, 5, 32, 4], measuredCharges);
        Assert.Equal(measuredCharges, ordinaryCharges);
        Assert.Equal(3, work.BytesSkipped);
    }

    static void AssertValues(CustomAttributeValue<string> value)
    {
        Assert.Equal(["string", "long[]", "int", "string"],
            value.FixedArguments.Select(argument => argument.Type));
        Assert.Equal("first", value.FixedArguments[0].Value);
        var array = Assert.IsType<ImmutableArray<CustomAttributeTypedArgument<string>>>(
            value.FixedArguments[1].Value);
        Assert.Equal(["long", "long"], array.Select(argument => argument.Type));
        Assert.Equal([2L, 3L], array.Select(argument => Assert.IsType<long>(argument.Value)));
        Assert.Equal(4, value.FixedArguments[2].Value);
        Assert.Equal("last", value.FixedArguments[3].Value);
        Assert.Empty(value.NamedArguments);
    }

    static CustomAttribute SampleAttribute(MetadataReader reader, Type sample)
    {
        var handle = (TypeDefinitionHandle)MetadataTokens.EntityHandle(sample.MetadataToken);
        return Assert.Single(reader.GetTypeDefinition(handle).GetCustomAttributes()
            .Select(reader.GetCustomAttribute),
            attribute =>
                attribute.Constructor.Kind == HandleKind.MemberReference
                && reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor)
                    .Parent.Kind == HandleKind.TypeSpecification);
    }
}
