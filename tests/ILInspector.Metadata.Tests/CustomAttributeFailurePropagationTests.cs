using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class CustomAttributeFailurePropagationTests
{
    [Theory]
    [InlineData(DecodeSurface.Value)]
    [InlineData(DecodeSurface.PreservingSerializedTypeNames)]
    [InlineData(DecodeSurface.Detailed)]
    public void StringMaterializationOutOfMemory_PropagatesUnchanged(DecodeSurface surface)
    {
        Type sample = typeof(CustomAttributeFidelitySamples.Types);
        using var pe = new PEReader(File.OpenRead(sample.Assembly.Location));
        var strings = new FaultingStringDecoder();
        MetadataReader reader = pe.GetMetadataReader(
            MetadataReaderOptions.Default, strings);
        var type = (TypeDefinitionHandle)MetadataTokens.EntityHandle(sample.MetadataToken);
        CustomAttribute attribute = reader.GetCustomAttribute(
            Assert.Single(reader.GetTypeDefinition(type).GetCustomAttributes()));

        Assert.NotNull(Decode(reader, attribute, surface));
        var failure = new OutOfMemoryException("Synthetic SRM string-materialization failure.");
        strings.FailNext(failure);

        var propagated = Assert.Throws<OutOfMemoryException>(
            () => Decode(reader, attribute, surface));

        Assert.Same(failure, propagated);
        Assert.Equal(1, strings.InjectedFailures);
        Assert.NotNull(Decode(reader, attribute, surface));
        Assert.Equal(1, strings.InjectedFailures);
    }

    static CustomAttributeValue<string>? Decode(
        MetadataReader reader,
        CustomAttribute attribute,
        DecodeSurface surface)
        => surface switch
        {
            DecodeSurface.Value =>
                AttributeDecoder.TryDecode(reader, attribute),
            DecodeSurface.PreservingSerializedTypeNames =>
                AttributeDecoder.TryDecodePreservingSerializedTypeNames(reader, attribute),
            DecodeSurface.Detailed =>
                AttributeDecoder.TryDecodeDetailed(reader, attribute)?.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };

    public enum DecodeSurface
    {
        Value,
        PreservingSerializedTypeNames,
        Detailed,
    }

    sealed class FaultingStringDecoder() : MetadataStringDecoder(Encoding.UTF8)
    {
        OutOfMemoryException? _failure;

        public int InjectedFailures { get; private set; }

        public void FailNext(OutOfMemoryException failure) => _failure = failure;

        public override unsafe string GetString(byte* bytes, int byteCount)
        {
            if (_failure is { } failure)
            {
                _failure = null;
                InjectedFailures++;
                throw failure;
            }
            return base.GetString(bytes, byteCount);
        }
    }
}
