using System.Collections.Immutable;

namespace ILInspector.Metadata;

/// <summary>
/// How completely a <see cref="MetadataHeapEntrySet"/> covers its heap.
///
/// ECMA-335 heaps are not tables: only the GUID heap is a sequence of fixed-size records, so only
/// it can be enumerated. The string, blob, and user-string heaps are sequences of length-prefixed
/// items with no index, and <c>System.Reflection.Metadata</c> exposes no public walker for them.
/// The coverage of a set is therefore part of the answer, not a detail — a caller that cannot tell
/// "every entry" from "the entries something pointed at" would read a partial list as a whole heap.
/// </summary>
public enum MetadataHeapCoverage
{
    /// <summary>
    /// Every entry in the heap is listed. Reached only for the GUID heap, whose entries are
    /// 16-byte records at consecutive 1-based indices, so the entry count follows from the heap
    /// size by arithmetic rather than by scanning bytes.
    /// </summary>
    Complete,

    /// <summary>
    /// Only entries referenced by a projected table row are listed. The heap may hold entries no
    /// row points at, and those are invisible here; they remain readable by address.
    /// </summary>
    ReferencedOnly,

    /// <summary>
    /// No entry can be listed at all. This is the user-string heap: no ECMA-335 table column
    /// points into it — its references are <c>ldstr</c> operands inside method bodies — and it
    /// cannot be walked, so neither enumeration nor a referenced-value scan yields anything.
    /// An empty entry list under this coverage is a blind spot, not an empty heap.
    /// </summary>
    NotEnumerable,
}

/// <summary>
/// One heap entry: its address, its value, and how many projected table cells referenced it.
/// </summary>
/// <param name="Offset">
/// The entry's heap address — a byte offset, or a 1-based index for the GUID heap. This is the
/// same address a projected cell's <see cref="MetadataValue.HeapReference.Offset"/> carries, so a
/// listed entry round-trips through a by-address read.
/// </param>
/// <param name="Value">
/// The entry's value. Normally a <see cref="MetadataValue.HeapReference"/> carrying the decoded
/// length, bounded preview, and truncation flag — the same shape a projected cell carries, so a
/// listed entry and a cell pointing at it render identically. Typed as the open
/// <see cref="MetadataValue"/> so a value the reader rejected stays a
/// <see cref="MetadataValue.Malformed"/> rather than being dropped from the listing.
/// </param>
/// <param name="ReferenceCount">
/// How many projected table cells referenced this entry. Zero is meaningful: under
/// <see cref="MetadataHeapCoverage.Complete"/> it marks an entry that exists but that no projected
/// row points at.
/// </param>
public sealed record MetadataHeapEntry(int Offset, MetadataValue Value, int ReferenceCount);

/// <summary>
/// The listable entries of one metadata heap, with the limits of that listing attached.
///
/// Every field that bounds the answer travels with it: <see cref="Coverage"/> says what kind of
/// listing this is, <see cref="EntriesTruncated"/> says the entry budget cut it short, and
/// <see cref="RowsTruncated"/> says the reference scan did not see every row of every table. A
/// consumer that ignores them reports a bounded sample as a complete heap.
/// </summary>
public sealed record MetadataHeapEntrySet
{
    public MetadataHeapEntrySet(
        HeapKind Heap,
        int SizeInBytes,
        MetadataHeapCoverage Coverage,
        ImmutableArray<MetadataHeapEntry> Entries,
        bool EntriesTruncated = false,
        bool RowsTruncated = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(SizeInBytes);
        if (Entries.IsDefault)
            throw new ArgumentException("Entries must be initialized.", nameof(Entries));

        this.Heap = Heap;
        this.SizeInBytes = SizeInBytes;
        this.Coverage = Coverage;
        this.Entries = Entries;
        this.EntriesTruncated = EntriesTruncated;
        this.RowsTruncated = RowsTruncated;
    }

    /// <summary>The heap these entries come from.</summary>
    public HeapKind Heap { get; }

    /// <summary>The heap's physical size, independent of how many entries were listed.</summary>
    public int SizeInBytes { get; }

    /// <summary>What the entry list covers.</summary>
    public MetadataHeapCoverage Coverage { get; }

    /// <summary>The listed entries, ordered by heap address.</summary>
    public ImmutableArray<MetadataHeapEntry> Entries { get; }

    /// <summary>
    /// True when the entry budget stopped the listing before every listable entry was added.
    /// </summary>
    public bool EntriesTruncated { get; }

    /// <summary>
    /// True when at least one table's row window covered fewer than all its rows, so a reference
    /// from an unscanned row was not counted and an entry only such rows point at is missing.
    /// </summary>
    public bool RowsTruncated { get; }
}
