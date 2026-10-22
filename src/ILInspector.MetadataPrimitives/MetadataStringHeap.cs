using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// The <c>#Strings</c> heap of one image as stored bytes: the Symbol Key name
/// source that copies a name's exact UTF-8 into a key without decoding or
/// Windows Runtime projection. A name offset outside the heap, or an
/// unterminated entry, is malformed.
/// </summary>
public sealed class MetadataStringHeap
{
    readonly BlobReader _heap;

    public MetadataStringHeap(PEReader pe, MetadataReader md)
    {
        ArgumentNullException.ThrowIfNull(pe);
        ArgumentNullException.ThrowIfNull(md);
        _heap = pe.GetMetadata().GetReader(
            md.GetHeapMetadataOffset(HeapIndex.String),
            md.GetHeapSize(HeapIndex.String));
    }

    /// <summary>The stored byte length of a name.</summary>
    public int Length(StringHandle handle)
    {
        int offset = MetadataTokens.GetHeapOffset(handle);
        if (offset < 0 || offset >= _heap.Length)
            throw new BadImageFormatException("A name lies outside the #Strings heap.");
        BlobReader reader = _heap;
        reader.Offset = offset;
        int length = reader.IndexOf(0);
        return length >= 0
            ? length
            : throw new BadImageFormatException("A #Strings entry is not terminated.");
    }

    /// <summary>Whether the stored bytes are well-formed UTF-8.</summary>
    public bool IsWellFormed(StringHandle handle)
        => System.Text.Unicode.Utf8.IsValid(Bytes(handle, Length(handle)));

    /// <summary>Whether the stored name equals the caller's UTF-8 query, byte for byte.</summary>
    public bool ValueTextEquals(StringHandle handle, ReadOnlySpan<byte> utf8Text)
        => Bytes(handle, Length(handle)).SequenceEqual(utf8Text);

    /// <summary>
    /// The stored bytes of a name, borrowed from the image for the duration of
    /// one scoped operation; never retained.
    /// </summary>
    internal unsafe ReadOnlySpan<byte> Bytes(StringHandle handle, int length)
    {
        BlobReader reader = _heap;
        reader.Offset = MetadataTokens.GetHeapOffset(handle);
        return new ReadOnlySpan<byte>(reader.CurrentPointer, length);
    }
}
