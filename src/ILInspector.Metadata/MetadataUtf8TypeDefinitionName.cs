using System.Buffers;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

using InertText;
using InertText.Encoding;

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
        Utf8RangeClassification.Read(_range).ValueTextEquals(utf8Text);

    internal OpaqueByteRange Range => _range;

    /// <summary>Constructs the policy-contained UTF-8 presentation value.</summary>
    public InertUtf8String Contain(TextPolicy policy) =>
        new(policy, Utf8RangeClassification.Read(_range));

    /// <summary>The number of exact metadata Name bytes.</summary>
    public int Length => _range.Length;
}

/// <summary>
/// A host-owned exact Type query with one UTF-8 encoding of its structured
/// metadata name.
/// </summary>
public sealed class MetadataExactTypeInput
{
    public MetadataExactTypeInput(MetadataTypeDefinitionName type)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Utf8 = MetadataUtf8TypeDefinitionNameFactory.Create(type);
    }

    /// <summary>The raw structured identity used for metadata resolution.</summary>
    public MetadataTypeDefinitionName Type { get; }

    /// <summary>Contains the host-owned UTF-8 query under a presentation policy.</summary>
    public MetadataContainedTypeDefinitionName Contain(TextPolicy policy) =>
        Utf8.Contain(policy);

    internal MetadataUtf8TypeDefinitionName Utf8 { get; }
}

internal readonly struct MetadataExactTypeMatch
{
    internal MetadataExactTypeMatch(MetadataUtf8TypeDefinitionName name) =>
        Name = name;

    internal MetadataUtf8TypeDefinitionName Name { get; }
}

/// <summary>
/// An exact metadata Type name over one compact immutable byte backing.
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
    internal static MetadataExactTypeMatch? Match(
        PEReader peReader,
        MetadataReader reader,
        TypeDefinitionHandle handle,
        MetadataExactTypeInput input)
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
        if (consumedNodes != input.Utf8.Segments.Length)
            return null;

        BlobReader heap = GetStringHeap(peReader, reader);
        TypeDefinition root =
            reader.GetTypeDefinition(rootToLeaf[0]);
        if (!RangeEquals(
                heap,
                root.Namespace,
                input.Utf8.Namespace.Range))
        {
            return null;
        }

        for (int i = 0; i < consumedNodes; i++)
        {
            TypeDefinition definition =
                reader.GetTypeDefinition(rootToLeaf[i]);
            if (!RangeEquals(
                    heap,
                    definition.Name,
                    input.Utf8.Segments[i].Range))
            {
                return null;
            }
        }

        return new(input.Utf8);
    }

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

        BlobReader heap = GetStringHeap(peReader, reader);

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

        var builder = new OpaqueByteRangeBuilder(totalLength);
        Span<OpaqueByteRangeSlot> slots =
            stackalloc OpaqueByteRangeSlot[consumedNodes + 1];
        for (int i = 0; i < sourceRanges.Length; i++)
            slots[i] = Append(builder, heap, sourceRanges[i]);

        OpaqueByteRange population = builder.Freeze();
        var segments =
            ImmutableArray.CreateBuilder<MetadataUtf8Name>(
                consumedNodes);
        for (int i = 0; i < consumedNodes; i++)
            segments.Add(new(population.Slice(slots[i + 1])));

        return new(
            new(population.Slice(slots[0])),
            segments.MoveToImmutable());
    }

    private static BlobReader GetStringHeap(
        PEReader peReader,
        MetadataReader reader) =>
        peReader.GetMetadata().GetReader(
            reader.GetHeapMetadataOffset(HeapIndex.String),
            reader.GetHeapSize(HeapIndex.String));

    private static unsafe bool RangeEquals(
        BlobReader heap,
        StringHandle handle,
        OpaqueByteRange expected)
    {
        (int offset, int length) = GetRange(heap, handle);
        if (length != expected.Length)
            return false;

        heap.Offset = offset;
        return expected.SequenceEqual(
            new ReadOnlySpan<byte>(
                heap.CurrentPointer,
                length));
    }

    private static unsafe OpaqueByteRangeSlot Append(
        OpaqueByteRangeBuilder builder,
        BlobReader heap,
        (int Offset, int Length) source)
    {
        heap.Offset = source.Offset;
        return builder.Append(
            new ReadOnlySpan<byte>(
                heap.CurrentPointer,
                source.Length));
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

internal static class MetadataUtf8TypeDefinitionNameFactory
{
    internal static MetadataUtf8TypeDefinitionName Create(
        MetadataTypeDefinitionName type)
    {
        int totalLength = Encoding.UTF8.GetByteCount(type.Namespace);
        foreach (string segment in type.Segments)
        {
            totalLength = checked(
                totalLength + Encoding.UTF8.GetByteCount(segment));
        }

        var builder = new OpaqueByteRangeBuilder(totalLength);
        OpaqueByteRangeSlot @namespace =
            Append(builder, type.Namespace);
        Span<OpaqueByteRangeSlot> slots =
            type.Segments.Length <= 32
                ? stackalloc OpaqueByteRangeSlot[type.Segments.Length]
                : new OpaqueByteRangeSlot[type.Segments.Length];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = Append(builder, type.Segments[i]);

        OpaqueByteRange population = builder.Freeze();
        var segments =
            ImmutableArray.CreateBuilder<MetadataUtf8Name>(
                slots.Length);
        foreach (OpaqueByteRangeSlot slot in slots)
            segments.Add(new(population.Slice(slot)));

        return new(
            new(population.Slice(@namespace)),
            segments.MoveToImmutable());
    }

    private static OpaqueByteRangeSlot Append(
        OpaqueByteRangeBuilder builder,
        string value)
    {
        int length = Encoding.UTF8.GetByteCount(value);
        byte[]? rented = null;
        Span<byte> utf8 =
            length <= 256
                ? stackalloc byte[length]
                : rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            int written = Encoding.UTF8.GetBytes(value, utf8);
            return builder.Append(utf8[..written]);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
