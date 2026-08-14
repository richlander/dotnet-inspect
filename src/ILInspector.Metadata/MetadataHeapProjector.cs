using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using InertText;

namespace ILInspector.Metadata;

/// <summary>
/// Owns the heap-inspection behavior behind <see cref="MetadataTableProjector.ReadHeapValue"/>
/// and <see cref="MetadataTableProjector.ReadHeapEntries"/>: random access into a single heap
/// value, and the strongest honest listing of a heap's entries given what
/// <c>System.Reflection.Metadata</c> exposes for it.
///
/// Reuses <see cref="MetadataTableProjectionEngine"/> for both the cell builders (so a heap value
/// read by address renders exactly like the same value read through a table cell) and the table
/// projection itself (so the reference scan behind a heap listing walks the same rows and cells
/// the table projection would produce), rather than forking a second row/cell decoder.
/// </summary>
internal static class MetadataHeapProjector
{
    /// <summary>
    /// Reads one heap value by address, independent of any table row that references it. Assumes
    /// the caller has already validated the source image has metadata.
    /// </summary>
    internal static MetadataValue ReadHeapValue(
        MetadataReader reader,
        HeapKind heap,
        int address,
        MetadataProjectionOptions options)
    {
        // Address zero is nil in every heap, including an absent one, so it is
        // answered before the bounds check rather than reported out of range.
        if (address == 0)
            return new MetadataValue.Nil();

        int size = reader.GetHeapSize(MetadataImageInspector.ToHeapIndex(heap));
        bool addressable = MetadataImageInspector.AddressingOf(heap) is MetadataHeapAddressing.Index
            ? address <= size / MetadataHeapAddressingSizes.GuidSize
            : address < size;

        if (!addressable)
            return new MetadataValue.Malformed(
                InertString.Format(TextPolicy.Field, $"{heap} heap address {address} is past the end of a {size}-byte heap."));

        try
        {
            return heap switch
            {
                HeapKind.String => MetadataTableProjectionEngine.StringCell(reader, MetadataTokens.StringHandle(address), options),
                HeapKind.Blob => MetadataTableProjectionEngine.BlobCell(reader, MetadataTokens.BlobHandle(address), options),
                HeapKind.Guid => MetadataTableProjectionEngine.GuidCell(reader, MetadataTokens.GuidHandle(address)),
                HeapKind.UserString => MetadataTableProjectionEngine.UserStringCell(reader, MetadataTokens.UserStringHandle(address), options),
                _ => throw new System.Diagnostics.UnreachableException($"Unhandled heap {heap}."),
            };
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            // Handle construction happens outside the cell readers' own guards,
            // so a rejected address is contained here rather than escaping as a
            // throw from a read-only query. An unknown HeapKind is not caught
            // here: ToHeapIndex above already rejected it.
            return new MetadataValue.Malformed(InertString.Format(TextPolicy.Field, $"{heap} heap read failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// The listable entries of one heap, with the limits of that listing attached. Assumes the
    /// caller has already validated the source image has metadata.
    /// </summary>
    internal static MetadataHeapEntrySet ReadHeapEntries(
        MetadataReader reader,
        HeapKind heap,
        MetadataProjectionOptions options)
    {
        int size = reader.GetHeapSize(MetadataImageInspector.ToHeapIndex(heap));
        int budget = Math.Max(1, options.MaxHeapEntries);

        if (heap == HeapKind.UserString)
            return new MetadataHeapEntrySet(heap, size, MetadataHeapCoverage.NotEnumerable, []);

        var references = ScanHeapReferences(reader, heap, options, out bool rowsTruncated);

        return heap == HeapKind.Guid
            ? EnumerateGuidHeap(reader, size, references, budget, rowsTruncated)
            : ReferencedHeapEntries(heap, size, references, budget, rowsTruncated);
    }

    /// <summary>
    /// Every projected cell that points into <paramref name="heap"/>, keyed by heap address: the
    /// first value seen at that address plus how many cells referenced it. Values are compared by
    /// address rather than by content, so two cells naming the same address are one entry and two
    /// equal strings stored twice remain two entries — which is what the heap actually holds.
    /// </summary>
    static Dictionary<int, (MetadataValue Value, int Count)> ScanHeapReferences(
        MetadataReader reader,
        HeapKind heap,
        MetadataProjectionOptions options,
        out bool rowsTruncated)
    {
        var found = new Dictionary<int, (MetadataValue Value, int Count)>();
        rowsTruncated = false;

        var projection = MetadataTableProjectionEngine.Project(reader, options with { Tables = default });
        foreach (var table in projection.Tables)
        {
            if (table.Truncation is not null)
                rowsTruncated = true;

            foreach (var row in table.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    if (cell is not MetadataValue.HeapReference reference || reference.Heap != heap)
                        continue;

                    found[reference.Offset] = found.TryGetValue(reference.Offset, out var existing)
                        ? (existing.Value, existing.Count + 1)
                        : (reference, 1);
                }
            }
        }

        return found;
    }

    static MetadataHeapEntrySet EnumerateGuidHeap(
        MetadataReader reader,
        int size,
        Dictionary<int, (MetadataValue Value, int Count)> references,
        int budget,
        bool rowsTruncated)
    {
        int count = size / MetadataHeapAddressingSizes.GuidSize;
        var entries = ImmutableArray.CreateBuilder<MetadataHeapEntry>(Math.Min(count, budget));

        // 1-based: index 0 is the nil GUID in every image, not a stored record.
        for (int index = 1; index <= count && entries.Count < budget; index++)
        {
            MetadataValue value;
            try
            {
                value = MetadataTableProjectionEngine.GuidCell(reader, MetadataTokens.GuidHandle(index));
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
            {
                value = new MetadataValue.Malformed(InertString.Format(TextPolicy.Field, $"Guid heap read failed at index {index}: {ex.Message}"));
            }

            entries.Add(new MetadataHeapEntry(
                index, value, references.TryGetValue(index, out var hit) ? hit.Count : 0));
        }

        return new MetadataHeapEntrySet(
            HeapKind.Guid,
            size,
            MetadataHeapCoverage.Complete,
            entries.ToImmutable(),
            EntriesTruncated: entries.Count < count,
            RowsTruncated: rowsTruncated);
    }

    static MetadataHeapEntrySet ReferencedHeapEntries(
        HeapKind heap,
        int size,
        Dictionary<int, (MetadataValue Value, int Count)> references,
        int budget,
        bool rowsTruncated)
    {
        var ordered = references
            .OrderBy(static entry => entry.Key)
            .Take(budget)
            .Select(static entry => new MetadataHeapEntry(entry.Key, entry.Value.Value, entry.Value.Count));

        return new MetadataHeapEntrySet(
            heap,
            size,
            MetadataHeapCoverage.ReferencedOnly,
            [.. ordered],
            EntriesTruncated: references.Count > budget,
            RowsTruncated: rowsTruncated);
    }
}
