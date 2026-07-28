using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

/// <summary>A single row, identified absolutely by its table and 1-based row id.</summary>
public sealed record MetadataRowLocation
{
    public MetadataRowLocation(TableIndex Table, int RowId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(RowId, 1);
        this.Table = Table;
        this.RowId = RowId;
    }

    /// <summary>The table the row belongs to.</summary>
    public TableIndex Table { get; }

    /// <summary>The 1-based row number within <see cref="Table"/>.</summary>
    public int RowId { get; }

    /// <summary>The row's metadata token: the table index in the high byte, the row id below it.</summary>
    public int Token => ((int)Table << 24) | RowId;
}

/// <summary>How a source row points at a target row.</summary>
public enum MetadataRowReferenceKind
{
    /// <summary>
    /// A handle column naming the target row directly — the reverse of a
    /// <see cref="MetadataValue.Handle"/> cell.
    /// </summary>
    Handle,

    /// <summary>
    /// A list column whose contiguous run of rows contains the target — the
    /// reverse of a <see cref="MetadataValue.Range"/> cell. This is the edge
    /// that answers ownership questions such as which <c>TypeDef</c> declares a
    /// given <c>Field</c>, since ECMA-335 encodes that as a run rather than a
    /// back-pointer on the owned row.
    /// </summary>
    Range,
}

/// <summary>One row that points at the row a reverse search was run for.</summary>
public sealed record MetadataRowReference
{
    public MetadataRowReference(
        MetadataRowLocation Source,
        int ColumnIndex,
        string ColumnName,
        MetadataRowReferenceKind Kind)
    {
        ArgumentNullException.ThrowIfNull(Source);
        ArgumentOutOfRangeException.ThrowIfNegative(ColumnIndex);
        ArgumentException.ThrowIfNullOrEmpty(ColumnName);

        this.Source = Source;
        this.ColumnIndex = ColumnIndex;
        this.ColumnName = ColumnName;
        this.Kind = Kind;
    }

    /// <summary>The row holding the pointing cell.</summary>
    public MetadataRowLocation Source { get; }

    /// <summary>
    /// The pointing cell's position in the source table's column schema, aligned
    /// to <see cref="MetadataTableView.Columns"/> and <see cref="MetadataRow.Cells"/>.
    /// </summary>
    public int ColumnIndex { get; }

    /// <summary>The pointing column's name, carried so a caller need not re-project the source table.</summary>
    public string ColumnName { get; }

    /// <summary>Whether the edge is a direct handle or membership in a list-column run.</summary>
    public MetadataRowReferenceKind Kind { get; }
}

/// <summary>
/// The result of a reverse-reference search: every row found pointing at
/// <see cref="Target"/>, plus the honest limits of that search.
///
/// A reverse search must never answer "nothing points here" when it simply
/// stopped early, could not read part of the metadata, or never looked at part
/// of it, so all three blind spots are reported rather than folded into an empty
/// result: <see cref="Truncated"/> when the result budget stopped the scan,
/// <see cref="UnreadableRows"/> for rows the scan could not decode and therefore
/// could not inspect, and <see cref="UnscannedTables"/> for populated tables the
/// projection does not model and so never visited.
/// </summary>
public sealed record MetadataRowReferenceSet
{
    /// <summary>The default ceiling on how many references a search collects.</summary>
    public const int DefaultMaxReferences = 4096;

    public MetadataRowReferenceSet(
        MetadataRowLocation Target,
        ImmutableArray<MetadataRowReference> References,
        ImmutableArray<MetadataRowLocation> UnreadableRows,
        ImmutableArray<TableIndex> UnscannedTables,
        bool Truncated)
    {
        ArgumentNullException.ThrowIfNull(Target);
        if (References.IsDefault)
            throw new ArgumentException("References must be initialized.", nameof(References));
        if (UnreadableRows.IsDefault)
            throw new ArgumentException("UnreadableRows must be initialized.", nameof(UnreadableRows));
        if (UnscannedTables.IsDefault)
            throw new ArgumentException("UnscannedTables must be initialized.", nameof(UnscannedTables));

        this.Target = Target;
        this.References = References;
        this.UnreadableRows = UnreadableRows;
        this.UnscannedTables = UnscannedTables;
        this.Truncated = Truncated;
    }

    /// <summary>The row the search was run for.</summary>
    public MetadataRowLocation Target { get; }

    /// <summary>
    /// The rows found pointing at <see cref="Target"/>, in table order and then
    /// row order, matching the order the tables are projected in.
    /// </summary>
    public ImmutableArray<MetadataRowReference> References { get; }

    /// <summary>
    /// Rows whose edges could not be fully determined, so a reference from one
    /// of them would have been missed. This covers a row that failed to decode
    /// outright and a row with an edge column the projection could only report
    /// as <see cref="MetadataValue.Malformed"/> — the cell readers contain such
    /// failures rather than throwing, so the row would otherwise pass as fully
    /// searched. A malformed heap, scalar, or flags cell is not counted: it was
    /// never an edge and cannot hide a reference. Empty for well-formed
    /// metadata.
    /// </summary>
    public ImmutableArray<MetadataRowLocation> UnreadableRows { get; }

    /// <summary>
    /// Populated tables no row of which was searched: either the projection does
    /// not model the table, or the result budget stopped the scan before it
    /// covered every row of it. A table is only counted as searched once every
    /// one of its rows was examined, because an edge onto the target could sit
    /// in any row left unread.
    ///
    /// This is the largest blind spot of the three and the only one that fires
    /// on well-formed metadata, because the search covers the projected tables
    /// rather than all of ECMA-335 — so an edge in an unmodelled table
    /// (<c>NestedClass</c>, <c>MethodSemantics</c>, <c>InterfaceImpl</c> and
    /// friends on a typical assembly) is invisible to it. A nested type's
    /// declaring type is exactly such an edge. Empty tables are excluded: they
    /// cannot hide a reference.
    /// </summary>
    public ImmutableArray<TableIndex> UnscannedTables { get; }

    /// <summary>
    /// Whether the result budget stopped the scan before it finished. Unlike
    /// <see cref="MetadataTableTruncation"/> this carries no total, because a
    /// stopped scan never learns how many references it did not reach; raise the
    /// budget to see more.
    /// </summary>
    public bool Truncated { get; }

    /// <summary>
    /// Whether the search covered the whole image: it ran to completion, every
    /// row was readable, and no populated table went unscanned, so an empty
    /// <see cref="References"/> genuinely means nothing points at
    /// <see cref="Target"/>.
    /// </summary>
    /// <remarks>
    /// This is <see langword="false"/> for essentially every real assembly,
    /// because the projection models a subset of ECMA-335's tables and typical
    /// metadata populates tables outside it. That is the honest answer rather
    /// than a defect in the caller's input: until the projection covers every
    /// table, the search cannot claim to have covered the whole image. Callers
    /// that only want to know whether the scan itself finished should read
    /// <see cref="Truncated"/>.
    /// </remarks>
    public bool IsComplete => !Truncated && UnreadableRows.IsEmpty && UnscannedTables.IsEmpty;
}
