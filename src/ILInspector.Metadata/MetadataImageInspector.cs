using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// Describes the container a <see cref="MetadataTableProjection"/> is drawn
/// from: metadata root identity, heap sizes and addressing, physical row counts
/// for every ECMA-335 table, and PE/CLI header facts.
///
/// This is the companion to <see cref="MetadataTableProjector"/> for the parts
/// of a metadata browser that are not tables. Like the projector it is
/// read-only, SRM-only, and presentation-free.
/// </summary>
public static class MetadataImageInspector
{
    /// <summary>
    /// Describes <paramref name="peReader"/>'s metadata container.
    ///
    /// Row counts cover every table this runtime's <see cref="TableIndex"/>
    /// knows, not just the tables the projector models, so a caller can see
    /// content the projection does not yet cover instead of mistaking it for an
    /// empty table.
    ///
    /// Returns <see langword="null"/> when the image carries no metadata, which
    /// is the same "not applicable" signal
    /// <see cref="MetadataTableProjector.ProjectRow"/> uses.
    /// </summary>
    public static MetadataImageOverview? Describe(PEReader peReader)
    {
        ArgumentNullException.ThrowIfNull(peReader);

        if (!peReader.HasMetadata)
            return null;

        // MetadataReaderOptions.None for the same reason the projector uses it:
        // the default enables Windows-Runtime projection, which would rewrite the
        // very facts this overview reports.
        var reader = peReader.GetMetadataReader(MetadataReaderOptions.None);
        var headers = peReader.PEHeaders;

        return new MetadataImageOverview(
            reader.MetadataVersion,
            reader.MetadataKind,
            reader.IsAssembly,
            headers.MetadataStartOffset,
            headers.MetadataSize,
            DescribeHeaps(reader),
            DescribeTables(reader),
            DescribeHeaders(headers));
    }

    static ImmutableArray<MetadataHeapSummary> DescribeHeaps(MetadataReader reader)
    {
        var heaps = ImmutableArray.CreateBuilder<MetadataHeapSummary>(HeapIndices.Length);
        foreach ((HeapKind kind, HeapIndex index) in HeapIndices)
        {
            heaps.Add(new MetadataHeapSummary(kind, reader.GetHeapSize(index), Addressing(kind)));
        }

        return heaps.MoveToImmutable();
    }

    static ImmutableArray<MetadataTableSummary> DescribeTables(MetadataReader reader)
    {
        var projected = MetadataTableProjector.ProjectedTables;
        var tables = ImmutableArray.CreateBuilder<MetadataTableSummary>();

        foreach (var index in Enum.GetValues<TableIndex>())
        {
            tables.Add(new MetadataTableSummary(
                index,
                index.ToString(),
                RowCount(reader, index),
                projected.Contains(index)));
        }

        return tables.ToImmutable();
    }

    static int RowCount(MetadataReader reader, TableIndex index)
    {
        try
        {
            return reader.GetTableRowCount(index);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or BadImageFormatException)
        {
            // A TableIndex this runtime declares but this metadata cannot count
            // is reported as empty rather than aborting the whole overview; the
            // table is still listed so its absence stays visible.
            return 0;
        }
    }

    static MetadataImageHeaders DescribeHeaders(PEHeaders headers)
    {
        var cor = headers.CorHeader is { } corHeader
            ? new MetadataCorHeaderSummary(
                corHeader.MajorRuntimeVersion,
                corHeader.MinorRuntimeVersion,
                corHeader.Flags,
                corHeader.EntryPointTokenOrRelativeVirtualAddress)
            : null;

        var pe = headers.PEHeader;

        return new MetadataImageHeaders(
            headers.CoffHeader.Machine,
            headers.CoffHeader.Characteristics,
            pe?.Subsystem ?? Subsystem.Unknown,
            pe?.DllCharacteristics ?? default,
            pe?.Magic == PEMagic.PE32Plus,
            cor);
    }

    static MetadataHeapAddressing Addressing(HeapKind heap)
        => heap is HeapKind.Guid ? MetadataHeapAddressing.Index : MetadataHeapAddressing.ByteOffset;

    static readonly (HeapKind Kind, HeapIndex Index)[] HeapIndices =
    [
        (HeapKind.String, HeapIndex.String),
        (HeapKind.Blob, HeapIndex.Blob),
        (HeapKind.Guid, HeapIndex.Guid),
        (HeapKind.UserString, HeapIndex.UserString),
    ];

    /// <summary>
    /// Maps a <see cref="HeapKind"/> to the SRM heap it names. Kept beside the
    /// overview because both the overview and random-access heap reads must
    /// agree on the mapping.
    /// </summary>
    internal static HeapIndex ToHeapIndex(HeapKind heap) => heap switch
    {
        HeapKind.String => HeapIndex.String,
        HeapKind.Blob => HeapIndex.Blob,
        HeapKind.Guid => HeapIndex.Guid,
        HeapKind.UserString => HeapIndex.UserString,
        _ => throw new ArgumentOutOfRangeException(nameof(heap), heap, "Unknown heap."),
    };

    internal static MetadataHeapAddressing AddressingOf(HeapKind heap) => Addressing(heap);
}
