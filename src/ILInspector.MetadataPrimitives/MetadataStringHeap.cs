using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

/// <summary>
/// The <c>#Strings</c> heap of one image as stored bytes: the Symbol Key name
/// source that copies a name's exact UTF-8 into a key without decoding or
/// Windows Runtime projection. A name offset outside the heap, or an
/// unterminated entry, is malformed. Names are read through the metadata
/// reader's own heap view; the type never touches the metadata root, and it
/// refuses a reader that would project Windows Metadata names.
/// </summary>
public sealed class MetadataStringHeap
{
    readonly MetadataReader _md;
    readonly int _heapSize;

    public MetadataStringHeap(MetadataReader md)
    {
        ArgumentNullException.ThrowIfNull(md);
        if (md.MetadataKind != MetadataKind.Ecma335
            && (md.Options & MetadataReaderOptions.ApplyWindowsRuntimeProjections) != 0)
        {
            throw new ArgumentException(
                "Stored names of Windows Metadata require a reader without Windows Runtime projections.", nameof(md));
        }
        _md = md;
        _heapSize = md.GetHeapSize(HeapIndex.String);
    }

    /// <summary>The stored byte length of a name.</summary>
    public int Length(StringHandle handle) => Reader(handle).Length;

    /// <summary>Whether the stored bytes are well-formed UTF-8.</summary>
    public bool IsWellFormed(StringHandle handle)
        => System.Text.Unicode.Utf8.IsValid(Bytes(handle));

    /// <summary>Whether the stored name equals the caller's UTF-8 query, byte for byte.</summary>
    public bool ValueTextEquals(StringHandle handle, ReadOnlySpan<byte> utf8Text)
        => Bytes(handle).SequenceEqual(utf8Text);

    /// <summary>
    /// The stored bytes of a name, measured once and borrowed from the image
    /// for the duration of one scoped operation; never retained.
    /// </summary>
    internal unsafe ReadOnlySpan<byte> Bytes(StringHandle handle)
    {
        BlobReader reader = Reader(handle);
        return new ReadOnlySpan<byte>(reader.CurrentPointer, reader.Length);
    }

    // The reader's string view ends at the entry's terminator, or at the end
    // of the heap when there is none; the latter is a malformed heap.
    BlobReader Reader(StringHandle handle)
    {
        int offset = MetadataTokens.GetHeapOffset(handle);
        if (offset < 0 || offset >= _heapSize)
            throw new BadImageFormatException("A name lies outside the #Strings heap.");
        BlobReader reader = _md.GetBlobReader(handle);
        if (offset + reader.Length >= _heapSize)
            throw new BadImageFormatException("A #Strings entry is not terminated.");
        return reader;
    }
}
