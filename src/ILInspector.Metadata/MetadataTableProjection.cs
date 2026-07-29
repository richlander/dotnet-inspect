using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

/// <summary>
/// A raw ECMA-335 metadata-table projection over a single assembly: one
/// <see cref="MetadataTableView"/> per supported table, each carrying its
/// columns and per-row cells with resolvable handles.
///
/// This is the structurally-lossless sibling of the typed extractors (see
/// <c>docs/design/metadata-table-projection.md</c>): it mirrors SRM's logical
/// table/heap graph without curating it into a semantic view, and it is never
/// a dependency of those typed extractors.
/// </summary>
public sealed record MetadataTableProjection
{
    public MetadataTableProjection(ImmutableArray<MetadataTableView> Tables)
    {
        if (Tables.IsDefault)
            throw new ArgumentException("Tables must be initialized.", nameof(Tables));

        this.Tables = Tables;
    }

    /// <summary>The projected tables, in ECMA-335 table order.</summary>
    public ImmutableArray<MetadataTableView> Tables { get; }
}

/// <summary>A single metadata table: its identity, schema, and projected rows.</summary>
public sealed record MetadataTableView
{
    public MetadataTableView(
        TableIndex Index,
        string Name,
        int RowCount,
        ImmutableArray<MetadataColumn> Columns,
        ImmutableArray<MetadataRow> Rows,
        MetadataTableTruncation? Truncation = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(Name);
        ArgumentOutOfRangeException.ThrowIfNegative(RowCount);
        if (Columns.IsDefault)
            throw new ArgumentException("Columns must be initialized.", nameof(Columns));
        if (Rows.IsDefault)
            throw new ArgumentException("Rows must be initialized.", nameof(Rows));

        this.Index = Index;
        this.Name = Name;
        this.RowCount = RowCount;
        this.Columns = Columns;
        this.Rows = Rows;
        this.Truncation = Truncation;
    }

    /// <summary>The ECMA-335 table this view projects.</summary>
    public TableIndex Index { get; }

    /// <summary>The table's canonical name (for example <c>TypeDef</c>).</summary>
    public string Name { get; }

    /// <summary>The physical row count reported by the metadata, before any row budget.</summary>
    public int RowCount { get; }

    /// <summary>The column schema, aligned positionally to each row's cells.</summary>
    public ImmutableArray<MetadataColumn> Columns { get; }

    /// <summary>The projected rows, ordered by row id.</summary>
    public ImmutableArray<MetadataRow> Rows { get; }

    /// <summary>
    /// Non-null when the row window covered fewer than all of the table's rows.
    /// Enumeration never silently truncates without this marker.
    /// </summary>
    public MetadataTableTruncation? Truncation { get; }
}

/// <summary>A single column in a table's schema.</summary>
public sealed record MetadataColumn
{
    public MetadataColumn(
        string Name,
        MetadataColumnKind Kind,
        ImmutableArray<TableIndex> CandidateTargets = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(Name);
        this.Name = Name;
        this.Kind = Kind;
        this.CandidateTargets = CandidateTargets.IsDefault
            ? ImmutableArray<TableIndex>.Empty
            : CandidateTargets;
    }

    /// <summary>The column's ECMA-335 name (for example <c>Extends</c>).</summary>
    public string Name { get; }

    /// <summary>What kind of value this column carries.</summary>
    public MetadataColumnKind Kind { get; }

    /// <summary>
    /// For <see cref="MetadataColumnKind.Handle"/> and
    /// <see cref="MetadataColumnKind.HandleRange"/> columns, the schema-declared
    /// set of tables a value may point at (a coded index resolves to exactly one
    /// of these). Empty for other column kinds.
    /// </summary>
    public ImmutableArray<TableIndex> CandidateTargets { get; }
}

/// <summary>What kind of value a <see cref="MetadataColumn"/> carries.</summary>
public enum MetadataColumnKind
{
    /// <summary>A raw scalar (row id detail, sequence, RVA, version part).</summary>
    Scalar,

    /// <summary>A flag enumeration (attributes), kept as raw bits plus decoded names.</summary>
    Flags,

    /// <summary>A reference into a heap (String, Blob, Guid, or UserString).</summary>
    Heap,

    /// <summary>A single coded/simple index into another table.</summary>
    Handle,

    /// <summary>A contiguous run of rows in another table (a list column).</summary>
    HandleRange,
}

/// <summary>A single projected row: its coordinates and positional cells.</summary>
public sealed record MetadataRow
{
    public MetadataRow(int RowId, int Token, ImmutableArray<MetadataValue> Cells)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(RowId, 1);
        if (Cells.IsDefault)
            throw new ArgumentException("Cells must be initialized.", nameof(Cells));

        this.RowId = RowId;
        this.Token = Token;
        this.Cells = Cells;
    }

    /// <summary>The 1-based row number within the table.</summary>
    public int RowId { get; }

    /// <summary>The full metadata token (table tag | row id).</summary>
    public int Token { get; }

    /// <summary>The row's cells, aligned positionally to the table's columns.</summary>
    public ImmutableArray<MetadataValue> Cells { get; }
}

/// <summary>
/// A projected cell value. This is a closed discriminated union: every cell is
/// exactly one of the nested variants. Friendly decodes are always additive —
/// they sit beside the raw value, never replace it.
/// </summary>
public abstract record MetadataValue
{
    private protected MetadataValue()
    {
    }

    /// <summary>An empty column: a nil handle or an absent value.</summary>
    public sealed record Nil : MetadataValue;

    /// <summary>A raw scalar value plus its display text.</summary>
    public sealed record Scalar(long Raw, string Display) : MetadataValue;

    /// <summary>A flag enumeration: the raw bits plus the decoded flag names.</summary>
    public sealed record Flags(long Raw, string Decoded) : MetadataValue;

    /// <summary>
    /// A reference into a metadata heap. <see cref="Text"/> is the decoded,
    /// control-escaped string for the String/Guid heaps (safe to render as data:
    /// terminal control sequences are neutralized); it is null for the Blob heap,
    /// whose <see cref="Preview"/> carries a bounded hex dump. <see cref="Length"/>
    /// is the full decoded size; <see cref="Truncated"/> is true when the bounded
    /// preview stopped short of it, so a preview is never mistaken for the whole
    /// value. The complete heap value remains addressable via <see cref="Offset"/>.
    /// </summary>
    public sealed record HeapReference(
        HeapKind Heap,
        int Offset,
        int Length,
        string? Text,
        string Preview,
        bool Truncated) : MetadataValue;

    /// <summary>A single resolvable index into another table.</summary>
    public sealed record Handle(HandleRef Reference) : MetadataValue;

    /// <summary>A contiguous run of rows in another table (a list column).</summary>
    public sealed record Range(HandleRange Reference) : MetadataValue;

    /// <summary>
    /// A cell that could not be trusted: SRM rejected the value or a handle was
    /// malformed. Never a success-shaped empty value.
    /// </summary>
    public sealed record Malformed(string Detail) : MetadataValue;
}

/// <summary>The metadata heaps a <see cref="MetadataValue.HeapReference"/> can point at.</summary>
public enum HeapKind
{
    String,
    Blob,
    Guid,
    UserString,
}

/// <summary>
/// A resolvable edge into a single row of another table. The
/// <see cref="TargetTable"/>/<see cref="TargetRowId"/> pair is authoritative;
/// <see cref="Display"/> is best-effort convenience text.
/// </summary>
public sealed record HandleRef
{
    public HandleRef(TableIndex TargetTable, int TargetRowId, int Token, string? Display = null, bool DisplayTruncated = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(TargetRowId);
        this.TargetTable = TargetTable;
        this.TargetRowId = TargetRowId;
        this.Token = Token;
        this.Display = Display;
        this.DisplayTruncated = DisplayTruncated;
    }

    /// <summary>The table the edge points into.</summary>
    public TableIndex TargetTable { get; }

    /// <summary>The 1-based row number in <see cref="TargetTable"/>, or 0 for a nil target.</summary>
    public int TargetRowId { get; }

    /// <summary>The full metadata token of the target.</summary>
    public int Token { get; }

    /// <summary>Best-effort display text for the target, or null when unavailable.</summary>
    public string? Display { get; }

    /// <summary>
    /// True when <see cref="Display"/> was bounded by the projection's string
    /// budget, so the retained text is a prefix rather than the full name.
    /// </summary>
    public bool DisplayTruncated { get; }
}

/// <summary>
/// A contiguous run of rows in a target table (an ECMA-335 list column such as
/// <c>TypeDef.FieldList</c>). The run is half-open: <c>[StartRowId, EndRowId)</c>.
/// </summary>
public sealed record HandleRange
{
    public HandleRange(TableIndex TargetTable, int StartRowId, int EndRowId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(StartRowId);
        ArgumentOutOfRangeException.ThrowIfLessThan(EndRowId, StartRowId);
        this.TargetTable = TargetTable;
        this.StartRowId = StartRowId;
        this.EndRowId = EndRowId;
    }

    /// <summary>The table the run points into.</summary>
    public TableIndex TargetTable { get; }

    /// <summary>The 1-based row number of the first row in the run.</summary>
    public int StartRowId { get; }

    /// <summary>The 1-based row number just past the last row in the run (exclusive).</summary>
    public int EndRowId { get; }

    /// <summary>The number of rows in the run.</summary>
    public int Count => EndRowId - StartRowId;
}

/// <summary>
/// Evidence that a table's row window projected fewer than all of its rows. Its
/// presence makes partial coverage explicit rather than silent, whether the
/// cause was the row budget or a window that starts past row 1.
/// </summary>
public sealed record MetadataTableTruncation
{
    public MetadataTableTruncation(int ProjectedRows, int RowCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ProjectedRows);
        ArgumentOutOfRangeException.ThrowIfLessThan(RowCount, ProjectedRows);
        this.ProjectedRows = ProjectedRows;
        this.RowCount = RowCount;
    }

    /// <summary>How many rows the window projected.</summary>
    public int ProjectedRows { get; }

    /// <summary>The physical row count of the table.</summary>
    public int RowCount { get; }
}

/// <summary>Bounds and selection for a <see cref="MetadataTableProjection"/>.</summary>
public sealed record MetadataProjectionOptions
{
    /// <summary>The default per-table row ceiling.</summary>
    public const int DefaultMaxRowsPerTable = 4096;

    /// <summary>The default first row projected from each table.</summary>
    public const int DefaultStartRowId = 1;

    /// <summary>The default bounded blob preview length, in bytes.</summary>
    public const int DefaultMaxPreviewBytes = 32;

    /// <summary>The default bounded string/name preview length, in characters.</summary>
    public const int DefaultMaxStringChars = 1024;

    /// <summary>The maximum number of rows projected per table before truncation.</summary>
    public int MaxRowsPerTable { get; init; } = DefaultMaxRowsPerTable;

    /// <summary>
    /// The 1-based row id at which each table's projection begins. Together with
    /// <see cref="MaxRowsPerTable"/> this forms a row window, letting a consumer
    /// page through a large table without materializing it whole. Values below 1
    /// are clamped to 1, matching how <see cref="MaxRowsPerTable"/> clamps.
    ///
    /// A window never hides the table's size: <see cref="MetadataTableView.RowCount"/>
    /// stays the physical count, each <see cref="MetadataRow.RowId"/> locates the
    /// row absolutely, and a partial window is marked by
    /// <see cref="MetadataTableView.Truncation"/>. A window past the end of a
    /// non-empty table yields that table's view with zero rows rather than
    /// dropping the table.
    /// </summary>
    public int StartRowId { get; init; } = DefaultStartRowId;

    /// <summary>The maximum number of blob bytes captured in a bounded preview.</summary>
    public int MaxPreviewBytes { get; init; } = DefaultMaxPreviewBytes;

    /// <summary>
    /// The maximum number of characters retained from a decoded string/name
    /// value. A longer value is projected as a bounded preview with
    /// <see cref="HeapReference.Truncated"/> set, bounding output amplification.
    /// </summary>
    public int MaxStringChars { get; init; } = DefaultMaxStringChars;

    /// <summary>
    /// When non-default, restricts projection to these tables. Default projects
    /// every supported table.
    /// </summary>
    public ImmutableArray<TableIndex> Tables { get; init; } = default;
}
