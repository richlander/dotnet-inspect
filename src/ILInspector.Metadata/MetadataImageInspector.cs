using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using InertText;

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
    ///
    /// The metadata root's version stamp is a counted string read straight out of
    /// the image, so it is attacker-controlled in exactly the way heap strings
    /// are — and unlike them it reaches presentation without passing through the
    /// row projection, so it inherits none of that path's containment. It is
    /// neutralized here, with the projector's own escaper rather than a parallel
    /// one, so that every consumer inherits the containment instead of each
    /// renderer having to remember. A value that skips the projection must still
    /// not skip its containment.
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

        InertString version = MetadataTableProjector.ContainCellText(
            reader.MetadataVersion, MetadataVersionBudget);

        return new MetadataImageOverview(
            version,
            reader.MetadataKind,
            reader.IsAssembly,
            headers.MetadataStartOffset,
            headers.MetadataSize,
            DescribeHeaps(reader),
            DescribeTables(reader),
            DescribeHeaders(headers));
    }

    /// <summary>
    /// Bounds the neutralized version stamp. ECMA-335 II.24.2.1 bounds the field
    /// at 255 bytes and neutralization expands a control character to six
    /// characters, so 255 * 6 is the widest a conforming stamp can become and the
    /// budget never binds on one. It exists for the non-conforming case, because
    /// the stamp is a counted string read straight out of the image and this code
    /// does not get to assume the image conforms.
    ///
    /// When the budget does bind, the clipped stamp says so itself: the value is
    /// an <see cref="InertString"/>, and the character budget is the only way it
    /// can become partial, so <c>IsTruncated</c> carries the fact rather than a
    /// separate flag a caller could read without. A prefix is never mistaken for
    /// a whole stamp — the same rule every other contained value in this layer
    /// follows.
    /// </summary>
    const int MetadataVersionBudget = 255 * 6;

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
            // Deliberately undefended. GetTableRowCount validates the index and
            // then indexes a row-count array that MetadataReader parsed during
            // construction, so it cannot report a malformed image here: a corrupt
            // table stream already threw out of GetMetadataReader. Every declared
            // TableIndex is in range for the same assembly's TableCount. Catching
            // and returning 0 would turn any future failure into a table that
            // reads as legitimately empty.
            tables.Add(new MetadataTableSummary(
                index,
                index.ToString(),
                reader.GetTableRowCount(index),
                projected.Contains(index)));
        }

        return tables.ToImmutable();
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
