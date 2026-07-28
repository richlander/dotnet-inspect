using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// Image-level facts that sit outside a <see cref="MetadataTableProjection"/>:
/// the metadata root's identity, the size and addressing of each heap, the
/// physical row count of every ECMA-335 table (including tables the projection
/// does not model), and the PE/COR header facts a metadata browser shows
/// alongside the tables.
///
/// This is a description of the container, not a projection of its values. It
/// deliberately reports what is physically present rather than what the
/// projection covers, so a consumer can tell "this table is empty" apart from
/// "this table is not projected".
/// </summary>
public sealed record MetadataImageOverview
{
    public MetadataImageOverview(
        string MetadataVersion,
        MetadataKind Kind,
        bool IsAssembly,
        int MetadataOffset,
        int MetadataSize,
        ImmutableArray<MetadataHeapSummary> Heaps,
        ImmutableArray<MetadataTableSummary> Tables,
        MetadataImageHeaders Headers)
    {
        ArgumentNullException.ThrowIfNull(MetadataVersion);
        ArgumentOutOfRangeException.ThrowIfNegative(MetadataOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(MetadataSize);
        ArgumentNullException.ThrowIfNull(Headers);
        if (Heaps.IsDefault)
            throw new ArgumentException("Heaps must be initialized.", nameof(Heaps));
        if (Tables.IsDefault)
            throw new ArgumentException("Tables must be initialized.", nameof(Tables));

        this.MetadataVersion = MetadataVersion;
        this.Kind = Kind;
        this.IsAssembly = IsAssembly;
        this.MetadataOffset = MetadataOffset;
        this.MetadataSize = MetadataSize;
        this.Heaps = Heaps;
        this.Tables = Tables;
        this.Headers = Headers;
    }

    /// <summary>
    /// The metadata root's version string (for example <c>v4.0.30319</c>). This
    /// is the metadata format stamp, not the assembly's target framework.
    /// </summary>
    public string MetadataVersion { get; }

    /// <summary>Whether the metadata is plain ECMA-335 or a Windows-Runtime flavour.</summary>
    public MetadataKind Kind { get; }

    /// <summary>True when the image carries an <c>Assembly</c> row (a manifest), false for a bare module.</summary>
    public bool IsAssembly { get; }

    /// <summary>The metadata root's offset from the start of the image, in bytes.</summary>
    public int MetadataOffset { get; }

    /// <summary>The total size of the metadata root, in bytes: all streams plus the root header.</summary>
    public int MetadataSize { get; }

    /// <summary>
    /// One entry per metadata heap, in <see cref="HeapKind"/> order. A heap that
    /// is absent from the image is reported with a zero size rather than omitted,
    /// so the set of heaps is always the same shape.
    /// </summary>
    public ImmutableArray<MetadataHeapSummary> Heaps { get; }

    /// <summary>
    /// One entry per ECMA-335 table known to this runtime's
    /// <see cref="TableIndex"/>, in table order, whether or not the table has
    /// rows and whether or not the projection models it.
    /// </summary>
    public ImmutableArray<MetadataTableSummary> Tables { get; }

    /// <summary>PE and CLI header facts for the containing image.</summary>
    public MetadataImageHeaders Headers { get; }
}

/// <summary>
/// The size and addressing of a single metadata heap.
/// </summary>
public sealed record MetadataHeapSummary
{
    public MetadataHeapSummary(HeapKind Heap, int SizeInBytes, MetadataHeapAddressing Addressing)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(SizeInBytes);
        this.Heap = Heap;
        this.SizeInBytes = SizeInBytes;
        this.Addressing = Addressing;
    }

    /// <summary>Which heap this describes.</summary>
    public HeapKind Heap { get; }

    /// <summary>
    /// The heap's size in bytes, or zero when the image carries no such heap.
    /// This is always a byte count, even for an index-addressed heap.
    /// </summary>
    public int SizeInBytes { get; }

    /// <summary>How a <see cref="MetadataValue.HeapReference.Offset"/> into this heap is interpreted.</summary>
    public MetadataHeapAddressing Addressing { get; }

    /// <summary>
    /// The largest addressable value for this heap: the last valid byte offset,
    /// or the last valid 1-based index for an index-addressed heap. Zero when the
    /// heap is empty, since zero always addresses the nil value.
    /// </summary>
    public int MaxAddress => Addressing switch
    {
        MetadataHeapAddressing.Index => SizeInBytes / MetadataHeapAddressingSizes.GuidSize,
        _ => SizeInBytes == 0 ? 0 : SizeInBytes - 1,
    };
}

/// <summary>
/// How a heap address is interpreted. ECMA-335 addresses the String, Blob, and
/// UserString heaps by byte offset, but the GUID heap by 1-based index into a
/// vector of 16-byte values — and SRM's <c>GetHeapOffset</c> follows suit,
/// returning an index for a GUID handle. Making that difference part of the
/// model keeps a caller from treating a GUID address as a byte offset.
/// </summary>
public enum MetadataHeapAddressing
{
    /// <summary>The address is a byte offset from the start of the heap.</summary>
    ByteOffset,

    /// <summary>The address is a 1-based index into a vector of fixed-size entries.</summary>
    Index,
}

/// <summary>Fixed sizes used by index-addressed heaps.</summary>
public static class MetadataHeapAddressingSizes
{
    /// <summary>The size of a single GUID-heap entry, in bytes.</summary>
    public const int GuidSize = 16;
}

/// <summary>
/// The physical size of a single ECMA-335 table, and whether the projection
/// models it.
/// </summary>
public sealed record MetadataTableSummary
{
    public MetadataTableSummary(TableIndex Index, string Name, int RowCount, bool IsProjected)
    {
        ArgumentException.ThrowIfNullOrEmpty(Name);
        ArgumentOutOfRangeException.ThrowIfNegative(RowCount);
        this.Index = Index;
        this.Name = Name;
        this.RowCount = RowCount;
        this.IsProjected = IsProjected;
    }

    /// <summary>The ECMA-335 table.</summary>
    public TableIndex Index { get; }

    /// <summary>The table's canonical name (for example <c>TypeDef</c>).</summary>
    public string Name { get; }

    /// <summary>The physical row count reported by the metadata, before any row budget.</summary>
    public int RowCount { get; }

    /// <summary>
    /// True when <see cref="MetadataTableProjector"/> models this table. A table
    /// with rows but <see langword="false"/> here is real content the projection
    /// does not yet cover — a visible gap rather than an empty table.
    /// </summary>
    public bool IsProjected { get; }
}

/// <summary>PE and CLI header facts for a managed image.</summary>
public sealed record MetadataImageHeaders
{
    public MetadataImageHeaders(
        Machine Machine,
        Characteristics ImageCharacteristics,
        Subsystem Subsystem,
        DllCharacteristics DllCharacteristics,
        bool IsPE32Plus,
        MetadataCorHeaderSummary? Cor)
    {
        this.Machine = Machine;
        this.ImageCharacteristics = ImageCharacteristics;
        this.Subsystem = Subsystem;
        this.DllCharacteristics = DllCharacteristics;
        this.IsPE32Plus = IsPE32Plus;
        this.Cor = Cor;
    }

    /// <summary>The COFF machine type.</summary>
    public Machine Machine { get; }

    /// <summary>The COFF image characteristics.</summary>
    public Characteristics ImageCharacteristics { get; }

    /// <summary>The optional header's subsystem.</summary>
    public Subsystem Subsystem { get; }

    /// <summary>The optional header's DLL characteristics.</summary>
    public DllCharacteristics DllCharacteristics { get; }

    /// <summary>True for a PE32+ (64-bit) optional header, false for PE32.</summary>
    public bool IsPE32Plus { get; }

    /// <summary>
    /// The CLI header, or <see langword="null"/> when the image has none. An
    /// image with metadata always has one; the nullability keeps a native or
    /// malformed image describable rather than throwing.
    /// </summary>
    public MetadataCorHeaderSummary? Cor { get; }
}

/// <summary>The CLI (COR) header facts a metadata browser shows.</summary>
public sealed record MetadataCorHeaderSummary
{
    public MetadataCorHeaderSummary(
        ushort MajorRuntimeVersion,
        ushort MinorRuntimeVersion,
        CorFlags Flags,
        int EntryPointTokenOrRelativeVirtualAddress)
    {
        this.MajorRuntimeVersion = MajorRuntimeVersion;
        this.MinorRuntimeVersion = MinorRuntimeVersion;
        this.Flags = Flags;
        this.EntryPointTokenOrRelativeVirtualAddress = EntryPointTokenOrRelativeVirtualAddress;
    }

    /// <summary>The CLI header's major runtime version.</summary>
    public ushort MajorRuntimeVersion { get; }

    /// <summary>The CLI header's minor runtime version.</summary>
    public ushort MinorRuntimeVersion { get; }

    /// <summary>The CLI header flags (IL-only, 32-bit preference, strong-name signed, …).</summary>
    public CorFlags Flags { get; }

    /// <summary>
    /// The managed entry-point token, or a native entry-point RVA when
    /// <see cref="Flags"/> has <see cref="CorFlags.NativeEntryPoint"/>. Zero when
    /// the image has no entry point.
    /// </summary>
    public int EntryPointTokenOrRelativeVirtualAddress { get; }

    /// <summary>
    /// The entry-point token when the header carries a managed one, otherwise
    /// <see langword="null"/>. Reading
    /// <see cref="EntryPointTokenOrRelativeVirtualAddress"/> as a token without
    /// this check would misreport a native entry-point RVA as a row id.
    /// </summary>
    public int? EntryPointToken
        => EntryPointTokenOrRelativeVirtualAddress != 0 && !Flags.HasFlag(CorFlags.NativeEntryPoint)
            ? EntryPointTokenOrRelativeVirtualAddress
            : null;
}
