using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using InertText;

namespace ILInspector.Metadata;

/// <summary>
/// One metadata Name range whose UTF-8 meaning is established by the Metadata grammar.
/// </summary>
public readonly struct MetadataUtf8Name
{
    private readonly OpaqueByteRange _range;

    internal MetadataUtf8Name(OpaqueByteRange range) =>
        _range = range;

    /// <summary>Tests an exact caller-supplied UTF-8 sequence without decoding this Name.</summary>
    public bool ValueTextEquals(ReadOnlySpan<byte> utf8Text) =>
        new Utf8RangeReader(_range).ValueTextEquals(utf8Text);

    /// <summary>Constructs the policy-contained UTF-8 presentation value.</summary>
    public InertUtf8String Contain(TextPolicy policy) =>
        new(policy, new Utf8RangeReader(_range));

    /// <summary>The number of exact metadata Name bytes.</summary>
    public int Length => _range.Length;
}

/// <summary>
/// A detached exact metadata Type name over one compact immutable byte backing.
/// </summary>
public sealed class MetadataUtf8TypeDefinitionName
{
    internal MetadataUtf8TypeDefinitionName(
        MetadataUtf8Name @namespace,
        ImmutableArray<MetadataUtf8Name> segments)
    {
        Namespace = @namespace;
        Segments = segments;
    }

    /// <summary>The root Type namespace.</summary>
    public MetadataUtf8Name Namespace { get; }

    /// <summary>Root-to-leaf metadata-name segments, including generic arity.</summary>
    public ImmutableArray<MetadataUtf8Name> Segments { get; }

    /// <summary>
    /// Projects this byte-native identity into contained presentation values.
    /// </summary>
    public MetadataContainedTypeDefinitionName Contain(
        TextPolicy policy) =>
        new(
            Namespace.Contain(policy),
            [
                .. Segments.Select(segment =>
                    segment.Contain(policy)),
            ]);
}

/// <summary>
/// The contained presentation of one metadata Type name.
/// </summary>
public sealed class MetadataContainedTypeDefinitionName
{
    internal MetadataContainedTypeDefinitionName(
        InertUtf8String @namespace,
        ImmutableArray<InertUtf8String> segments)
    {
        Namespace = @namespace;
        Segments = segments;
    }

    /// <summary>The contained root Type namespace.</summary>
    public InertUtf8String Namespace { get; }

    /// <summary>The contained root-to-leaf metadata-name segments.</summary>
    public ImmutableArray<InertUtf8String> Segments { get; }
}

internal static class MetadataUtf8TypeDefinitionNameReader
{
    internal static MetadataUtf8TypeDefinitionName Detach(
        PEReader peReader,
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        Span<TypeDefinitionHandle> rootToLeaf =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal
                .TryWalkTypeDefinitionDeclaringChain(
                    reader,
                    handle,
                    rootToLeaf,
                    out int consumedNodes,
                    out _,
                    out _))
        {
            throw new BadImageFormatException(
                "The matched TypeDef declaring chain is malformed.");
        }

        int stringHeapOffset =
            reader.GetHeapMetadataOffset(HeapIndex.String);
        int stringHeapSize =
            reader.GetHeapSize(HeapIndex.String);
        BlobReader heap = peReader.GetMetadata()
            .GetReader(stringHeapOffset, stringHeapSize);

        Span<(int Offset, int Length)> sourceRanges =
            stackalloc (int Offset, int Length)[consumedNodes + 1];
        TypeDefinition root =
            reader.GetTypeDefinition(rootToLeaf[0]);
        sourceRanges[0] = GetRange(heap, root.Namespace);
        int totalLength = sourceRanges[0].Length;
        for (int i = 0; i < consumedNodes; i++)
        {
            TypeDefinition definition =
                reader.GetTypeDefinition(rootToLeaf[i]);
            sourceRanges[i + 1] =
                GetRange(heap, definition.Name);
            totalLength = checked(
                totalLength + sourceRanges[i + 1].Length);
        }

        byte[] bytes =
            GC.AllocateUninitializedArray<byte>(totalLength);
        var backing = new OpaqueByteBacking(bytes);
        int destinationOffset = 0;

        var @namespace =
            new MetadataUtf8Name(
                CopyRange(
                    heap,
                    bytes,
                    backing,
                    sourceRanges[0],
                    ref destinationOffset));
        var segments =
            ImmutableArray.CreateBuilder<MetadataUtf8Name>(
                consumedNodes);
        for (int i = 0; i < consumedNodes; i++)
        {
            segments.Add(
                new MetadataUtf8Name(
                    CopyRange(
                        heap,
                        bytes,
                        backing,
                        sourceRanges[i + 1],
                        ref destinationOffset)));
        }

        return new(@namespace, segments.MoveToImmutable());
    }

    private static OpaqueByteRange CopyRange(
        BlobReader heap,
        byte[] destination,
        OpaqueByteBacking backing,
        (int Offset, int Length) source,
        ref int destinationOffset)
    {
        heap.Offset = source.Offset;
        heap.ReadBytes(
            source.Length,
            destination,
            destinationOffset);
        var range = new OpaqueByteRange(
            backing,
            destinationOffset,
            source.Length);
        destinationOffset = checked(
            destinationOffset + source.Length);
        return range;
    }

    private static (int Offset, int Length) GetRange(
        BlobReader heap,
        StringHandle handle)
    {
        int offset = MetadataTokens.GetHeapOffset(handle);
        if ((uint)offset >= (uint)heap.Length)
        {
            throw new BadImageFormatException(
                "A metadata String handle is outside the #Strings heap.");
        }

        heap.Offset = offset;
        int length = 0;
        bool terminated = false;
        while (heap.RemainingBytes > 0)
        {
            if (heap.ReadByte() == 0)
            {
                terminated = true;
                break;
            }
            length++;
        }
        if (!terminated)
        {
            throw new BadImageFormatException(
                "A metadata String is not null terminated.");
        }
        return (offset, length);
    }
}
