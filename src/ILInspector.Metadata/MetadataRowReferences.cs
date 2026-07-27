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
/// stopped early or could not read part of the metadata, so both blind spots are
/// reported rather than folded into an empty result: <see cref="Truncated"/>
/// when the result budget stopped the scan, and <see cref="UnreadableRows"/> for
/// rows the scan could not decode and therefore could not inspect.
/// </summary>
public sealed record MetadataRowReferenceSet
{
    /// <summary>The default ceiling on how many references a search collects.</summary>
    public const int DefaultMaxReferences = 4096;

    public MetadataRowReferenceSet(
        MetadataRowLocation Target,
        ImmutableArray<MetadataRowReference> References,
        ImmutableArray<MetadataRowLocation> UnreadableRows,
        bool Truncated)
    {
        ArgumentNullException.ThrowIfNull(Target);
        if (References.IsDefault)
            throw new ArgumentException("References must be initialized.", nameof(References));
        if (UnreadableRows.IsDefault)
            throw new ArgumentException("UnreadableRows must be initialized.", nameof(UnreadableRows));

        this.Target = Target;
        this.References = References;
        this.UnreadableRows = UnreadableRows;
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
    /// Rows the scan could not decode. Each is a row whose edges could not be
    /// inspected, so a reference from it would have been missed. Empty for
    /// well-formed metadata.
    /// </summary>
    public ImmutableArray<MetadataRowLocation> UnreadableRows { get; }

    /// <summary>
    /// Whether the result budget stopped the scan before it finished. Unlike
    /// <see cref="MetadataTableTruncation"/> this carries no total, because a
    /// stopped scan never learns how many references it did not reach; raise the
    /// budget to see more.
    /// </summary>
    public bool Truncated { get; }

    /// <summary>
    /// Whether the search covered the whole image: it ran to completion and
    /// every row was readable, so an empty <see cref="References"/> genuinely
    /// means nothing points at <see cref="Target"/>.
    /// </summary>
    public bool IsComplete => !Truncated && UnreadableRows.IsEmpty;
}
