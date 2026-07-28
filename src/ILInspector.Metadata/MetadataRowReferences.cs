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
/// stopped early, could not read part of the metadata, never looked at part of
/// it, or was handed a row that is not there, so those four blind spots are
/// reported rather than folded into an empty result: <see cref="Truncated"/>
/// when the result budget stopped the scan, <see cref="UnreadableRows"/> for
/// rows the scan could not decode and therefore could not inspect,
/// <see cref="UnscannedTables"/> for populated tables the scan did not read in
/// full, and <see cref="TargetExists"/> when the target row is past the end of
/// its table.
///
/// A fifth limit cannot be reported per query, because nothing about a given
/// query reveals it: the search matches edges spelled as handle columns, so an
/// edge carried inside a signature blob is never found and never disclosed.
/// See <see cref="IsComplete"/>.
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
        bool Truncated,
        bool TargetExists)
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
        this.TargetExists = TargetExists;
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
    /// Populated tables the scan did not read in full: either the projection
    /// does not model the table, so no row of it was read, or the result budget
    /// stopped the scan part-way through the table or before reaching it. A
    /// table is only counted as searched once every one of its rows was
    /// examined, because an edge onto the target could sit in any row left
    /// unread — a table read in part is as unable to rule out an edge as one
    /// never opened.
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
    /// Whether <see cref="Target"/> names a row the image actually has.
    ///
    /// Reported rather than rejected. A row id past the end of its table is
    /// usually a typo, and answering one with a clean empty result would make
    /// that typo indistinguishable from a real row nothing points at. But it is
    /// not merely invalid input: a dangling edge points at exactly the rows that
    /// do not exist, so asking what points at an absent row is a legitimate
    /// question, and one this search can answer with
    /// <see cref="UnreadableRows"/>.
    /// </summary>
    public bool TargetExists { get; }

    /// <summary>
    /// Whether the search hit none of the blind spots it can detect: the target
    /// row exists, the scan ran to completion, every row it read was readable,
    /// and every populated table was read in full.
    ///
    /// This describes the scan, not the image. It does not mean nothing points
    /// at <see cref="Target"/>: the search sees only edges that metadata spells
    /// as handle columns, so an edge carried inside a signature blob is
    /// invisible to it whether this is <see langword="true"/> or
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Four limits are disclosed per query, and this property is exactly the
    /// four of them being clear: <see cref="TargetExists"/>,
    /// <see cref="Truncated"/>, <see cref="UnreadableRows"/> and
    /// <see cref="UnscannedTables"/>.
    ///
    /// A fifth is <b>not</b> disclosed, because no per-query signal can reveal
    /// it. Signature blobs carry TypeDefOrRef coded tokens, and they sit in
    /// heap-kind columns of tables the scan reads in full —
    /// <c>Field.Signature</c>, <c>MethodDef.Signature</c>,
    /// <c>MemberRef.Signature</c>, <c>TypeSpec.Signature</c>,
    /// <c>StandAloneSig.Signature</c>, <c>MethodSpec.Instantiation</c>. The
    /// column is correctly not an edge column, the row is not blind, and the
    /// table is genuinely searched, so nothing fires and the edge is simply not
    /// reported. Decoding those blobs is a separate feature, not a bug fix.
    ///
    /// That inverts the risk, which is why this property must not be read as
    /// "nothing points here". It is <see langword="false"/> for essentially
    /// every real assembly, because typical metadata populates tables the
    /// projection does not model — so it is <see langword="true"/> mainly on
    /// small, simple images, which is exactly where a blob edge is the only
    /// limit left to hide one. Callers that only want to know whether the scan
    /// itself finished should read <see cref="Truncated"/>.
    /// </remarks>
    public bool IsComplete =>
        TargetExists && !Truncated && UnreadableRows.IsEmpty && UnscannedTables.IsEmpty;
}
