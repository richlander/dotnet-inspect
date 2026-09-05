using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using InertText;

namespace ILInspector.Metadata;

/// <summary>
/// Projects an assembly's ECMA-335 metadata tables into a
/// <see cref="MetadataTableProjection"/>: a structurally-lossless view of SRM's
/// logical table/heap graph. Handles become resolvable
/// <see cref="HandleRef"/>/<see cref="HandleRange"/> edges, heaps surface as
/// bounded <see cref="MetadataValue.HeapReference"/> cells, and friendly decodes
/// are always additive.
///
/// This producer is a sibling of the typed extractors, never a dependency of
/// them (see <c>docs/design/metadata-table-projection.md</c>). It is read-only,
/// SRM-only, and applies explicit per-table row and blob-preview budgets with
/// typed rejection rather than silent truncation.
///
/// This is the public API and validation facade: it performs every argument
/// check, option default, and no-metadata short-circuit, then delegates to the
/// internal collaborators that own the substantive work —
/// <see cref="MetadataTableProjectionEngine"/> for table/row projection,
/// <see cref="MetadataRowReferenceFinder"/> for reverse-reference search, and
/// <see cref="MetadataHeapProjector"/> for heap inspection.
/// </summary>
public static class MetadataTableProjector
{
    /// <summary>
    /// The tables this projector models, in ECMA-335 table order. A table absent
    /// from this set is not projected at all, which is a different fact from a
    /// table that is projected and empty — see
    /// <see cref="MetadataTableSummary.IsProjected"/>.
    /// </summary>
    public static ImmutableArray<TableIndex> ProjectedTables { get; } =
        [.. MetadataTableProjectionEngine.SupportedTables.Select(static spec => spec.Index)];

    /// <summary>
    /// The columns <paramref name="table"/> projects, in order, or an empty array when the table
    /// is not projected. Reads the same <c>SupportedTables</c> declaration the projection itself
    /// reads, so a consumer can describe a table's shape — for discovery, schema registration, or
    /// column projection — without paying for a projection or restating the column list.
    /// </summary>
    public static ImmutableArray<MetadataColumn> ColumnsFor(TableIndex table)
    {
        return MetadataTableProjectionEngine.TryGetTableSpec(table, out var spec)
            ? spec.Columns
            : ImmutableArray<MetadataColumn>.Empty;
    }

    /// <summary>
    /// Projects the supported metadata tables of <paramref name="peReader"/>.
    /// Returns an empty projection when the image carries no metadata.
    /// </summary>
    public static MetadataTableProjection Project(PEReader peReader, MetadataProjectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(peReader);
        options ??= new MetadataProjectionOptions();

        if (!MetadataFormatAdmission.AdmitImage(peReader))
            return new MetadataTableProjection(ImmutableArray<MetadataTableView>.Empty);

        // MetadataReaderOptions.None keeps the projection raw: the default enables
        // Windows-Runtime projection, which would replace real table/heap values
        // with synthesized aliases and defeat structural losslessness.
        var reader = MetadataFormatAdmission.GetMetadataReader(
            peReader,
            MetadataReaderOptions.None);
        return Project(reader, options);
    }

    /// <summary>
    /// Projects a single row of one table on demand, independent of any row
    /// window applied to a wider projection. This is the handle click-through
    /// primitive: a <see cref="HandleRef"/> whose target lies outside the current
    /// window is still reachable without re-projecting the target table.
    ///
    /// The row is returned inside its table's <see cref="MetadataTableView"/> so
    /// the caller also gets the column schema and the table's physical
    /// <see cref="MetadataTableView.RowCount"/>, which a single row cannot carry.
    ///
    /// <paramref name="table"/> names the target directly, so
    /// <see cref="MetadataProjectionOptions.Tables"/> and
    /// <see cref="MetadataProjectionOptions.StartRowId"/> are deliberately
    /// ignored here; only the cell budgets apply. Honouring the table selection
    /// would dead-end the very edges this method exists to follow — a caller
    /// browsing TypeRef could not follow a TypeRef row into TypeDef.
    ///
    /// Returns <see langword="null"/> when the image has no metadata, when
    /// <paramref name="table"/> is not one this projector supports, or when
    /// <paramref name="rowId"/> is past the table's last row.
    /// </summary>
    public static MetadataTableView? ProjectRow(
        PEReader peReader,
        TableIndex table,
        int rowId,
        MetadataProjectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(peReader);
        ArgumentOutOfRangeException.ThrowIfLessThan(rowId, 1);
        options ??= new MetadataProjectionOptions();

        if (!MetadataFormatAdmission.AdmitImage(peReader)
            || !MetadataTableProjectionEngine.TryGetTableSpec(table, out var spec))
            return null;

        var reader = MetadataFormatAdmission.GetMetadataReader(
            peReader,
            MetadataReaderOptions.None);
        return ProjectRow(reader, spec, rowId, options);
    }

    /// <summary>
    /// Finds every row that points at <paramref name="targetTable"/> row
    /// <paramref name="targetRowId"/> — the reverse of the forward
    /// <see cref="HandleRef"/>/<see cref="HandleRange"/> edges a projection
    /// exposes, and the "who references this?" gesture an explorer needs.
    ///
    /// Both edge shapes are searched. A handle column matches when it names the
    /// row directly; a list column matches when the target falls inside its run,
    /// which is how ECMA-335 encodes ownership — so this answers which
    /// <c>TypeDef</c> declares a given <c>Field</c>, <c>MethodDef</c>, or which
    /// <c>MethodDef</c> owns a given <c>Param</c>.
    ///
    /// The scan covers every supported table, up to the point
    /// <paramref name="maxReferences"/> stops it. It takes no
    /// <see cref="MetadataProjectionOptions"/> at all, and in particular offers
    /// no equivalent of <see cref="MetadataProjectionOptions.Tables"/>, because
    /// a reverse search narrowed to part of the image could report "nothing
    /// points here" while a pointer sat in an unsearched table. Four blind
    /// spots are reported instead of hidden:
    /// <see cref="MetadataRowReferenceSet.Truncated"/> when
    /// <paramref name="maxReferences"/> stopped the scan,
    /// <see cref="MetadataRowReferenceSet.UnreadableRows"/> for rows whose edges
    /// could not be fully determined,
    /// <see cref="MetadataRowReferenceSet.UnscannedTables"/> for populated
    /// tables the scan did not read in full — because the projection does not
    /// model the table, or because the scan stopped part-way through it or
    /// before reaching it — and
    /// <see cref="MetadataRowReferenceSet.TargetExists"/> when the target row id
    /// is past the end of its table.
    ///
    /// A fifth limit is real and cannot be reported per query: only edges
    /// spelled as handle columns are matched, so a reference carried inside a
    /// blob is never found. It is disclosed unconditionally by the renderer
    /// rather than through this result. See
    /// <see cref="MetadataRowReferenceSet.IsComplete"/>, which does not mean
    /// nothing points at the target.
    ///
    /// A dangling edge is not a reference: a handle whose row lies outside its
    /// target table projects as <see cref="MetadataValue.Malformed"/>, so it
    /// cannot match. It does, however, make its row an
    /// <see cref="MetadataRowReferenceSet.UnreadableRows"/> entry, because an
    /// edge column the projection could not resolve is an edge this search
    /// cannot account for.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="targetRowId"/> is less than 1. A row id past the *end* of
    /// the table is reported through
    /// <see cref="MetadataRowReferenceSet.TargetExists"/> rather than rejected,
    /// because a dangling edge points at exactly such rows.
    /// </exception>
    public static MetadataRowReferenceSet FindReferences(
        PEReader peReader,
        TableIndex targetTable,
        int targetRowId,
        int maxReferences = MetadataRowReferenceSet.DefaultMaxReferences)
    {
        ArgumentNullException.ThrowIfNull(peReader);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetRowId, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(maxReferences);

        var target = new MetadataRowLocation(targetTable, targetRowId);

        if (!MetadataFormatAdmission.AdmitImage(peReader))
            return new MetadataRowReferenceSet(
                target, [], [], [], Truncated: false, TargetExists: false);

        var reader = MetadataFormatAdmission.GetMetadataReader(
            peReader,
            MetadataReaderOptions.None);
        return FindReferences(reader, target, maxReferences);
    }

    /// <summary>
    /// Reads one heap value by address, independent of any table row that
    /// references it. This is the heap counterpart of
    /// <see cref="ProjectRow"/>: a <see cref="MetadataValue.HeapReference"/> cell
    /// carries a bounded preview plus an <see cref="MetadataValue.HeapReference.Offset"/>,
    /// and this resolves that address back to a value so a browser can show the
    /// whole entry, or browse a heap the tables never point into at all (the
    /// UserString heap is referenced only from IL).
    ///
    /// <paramref name="address"/> uses the same convention as
    /// <see cref="MetadataValue.HeapReference.Offset"/>, so a value read from a
    /// projected cell round-trips: a byte offset for the String, Blob, and
    /// UserString heaps, but a 1-based index for the GUID heap. See
    /// <see cref="MetadataHeapAddressing"/>. Address zero is the nil value of
    /// every heap and reads as <see cref="MetadataValue.Nil"/>.
    ///
    /// The result is shaped exactly like the projected cell for the same value,
    /// so one renderer serves both. An address past the end of the heap yields
    /// <see cref="MetadataValue.Malformed"/> rather than an empty value.
    ///
    /// Returns <see langword="null"/> when the image carries no metadata.
    /// </summary>
    public static MetadataValue? ReadHeapValue(
        PEReader peReader,
        HeapKind heap,
        int address,
        MetadataProjectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(peReader);
        ArgumentOutOfRangeException.ThrowIfNegative(address);
        options ??= new MetadataProjectionOptions();

        if (!MetadataFormatAdmission.AdmitImage(peReader))
            return null;

        var reader = MetadataFormatAdmission.GetMetadataReader(
            peReader,
            MetadataReaderOptions.None);
        return ReadHeapValue(reader, heap, address, options);
    }

    /// <summary>
    /// The listable entries of one heap, with the limits of that listing attached. Null when the
    /// image carries no metadata.
    ///
    /// A heap is not a table, and this method does not pretend otherwise. ECMA-335 stores the
    /// string, blob, and user-string heaps as sequences of length-prefixed items with no index,
    /// and <c>System.Reflection.Metadata</c> exposes no walker over them; the only way to produce
    /// a full listing would be to re-parse the heap bytes ourselves, which would fork a second
    /// metadata decoder and hand back items SRM never validated. So each heap is listed by the
    /// strongest honest means available, and the result says which was used:
    ///
    /// <list type="bullet">
    /// <item><b>GUID</b> — <see cref="MetadataHeapCoverage.Complete"/>. Its entries are 16-byte
    /// records at consecutive 1-based indices, so the entry count follows from the heap size by
    /// arithmetic and every entry is read through SRM by index.</item>
    /// <item><b>String, Blob</b> — <see cref="MetadataHeapCoverage.ReferencedOnly"/>. The distinct
    /// values the projected table rows point at, in address order. Entries no row references are
    /// absent and stay readable only by address.</item>
    /// <item><b>UserString</b> — <see cref="MetadataHeapCoverage.NotEnumerable"/>, with no
    /// entries. No table column points into it: its references are <c>ldstr</c> operands in method
    /// bodies, which this projection does not read. An empty list here is a blind spot the
    /// coverage names, not an empty heap — <see cref="MetadataHeapEntrySet.SizeInBytes"/> still
    /// reports the real size.</item>
    /// </list>
    ///
    /// The reference scan covers every projected table, not the caller's
    /// <see cref="MetadataProjectionOptions.Tables"/> selection: an entry is referenced by the
    /// image, not by a subset of it, so honoring a table filter here would silently drop entries
    /// and undercount references. The row window is honored, and a table whose window fell short
    /// sets <see cref="MetadataHeapEntrySet.RowsTruncated"/>.
    /// </summary>
    public static MetadataHeapEntrySet? ReadHeapEntries(
        PEReader peReader,
        HeapKind heap,
        MetadataProjectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(peReader);
        options ??= new MetadataProjectionOptions();

        if (!MetadataFormatAdmission.AdmitImage(peReader))
            return null;

        var reader = MetadataFormatAdmission.GetMetadataReader(
            peReader,
            MetadataReaderOptions.None);
        return ReadHeapEntries(reader, heap, options);
    }

    internal static MetadataTableProjection Project(
        MetadataReader reader,
        MetadataProjectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(options);
        return MetadataTableProjectionEngine.Project(reader, options);
    }

    internal static MetadataTableView? ProjectRow(
        MetadataReader reader,
        TableIndex table,
        int rowId,
        MetadataProjectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfLessThan(rowId, 1);
        ArgumentNullException.ThrowIfNull(options);

        return MetadataTableProjectionEngine.TryGetTableSpec(table, out var spec)
            ? ProjectRow(reader, spec, rowId, options)
            : null;
    }

    static MetadataTableView? ProjectRow(
        MetadataReader reader,
        MetadataTableProjectionEngine.TableSpec spec,
        int rowId,
        MetadataProjectionOptions options) =>
        MetadataTableProjectionEngine.ProjectRow(reader, spec, rowId, options);

    internal static MetadataRowReferenceSet FindReferences(
        MetadataReader reader,
        TableIndex targetTable,
        int targetRowId,
        int maxReferences)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetRowId, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(maxReferences);
        return FindReferences(
            reader,
            new MetadataRowLocation(targetTable, targetRowId),
            maxReferences);
    }

    static MetadataRowReferenceSet FindReferences(
        MetadataReader reader,
        MetadataRowLocation target,
        int maxReferences) =>
        MetadataRowReferenceFinder.FindReferences(reader, target, maxReferences);

    internal static MetadataValue ReadHeapValue(
        MetadataReader reader,
        HeapKind heap,
        int address,
        MetadataProjectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegative(address);
        ArgumentNullException.ThrowIfNull(options);
        return MetadataHeapProjector.ReadHeapValue(reader, heap, address, options);
    }

    internal static MetadataHeapEntrySet ReadHeapEntries(
        MetadataReader reader,
        HeapKind heap,
        MetadataProjectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(options);
        return MetadataHeapProjector.ReadHeapEntries(reader, heap, options);
    }

    /// <summary>
    /// Applies <paramref name="mode"/> to a decoded heap string and bounds the EMITTED text to
    /// <paramref name="maxChars"/> characters.
    /// <para>
    /// Under <see cref="UntrustedTextMode.Contain"/> and <see cref="UntrustedTextMode.Refuse"/>,
    /// every non-graphic scalar is spelled rather than emitted, by Unicode general category
    /// (<c>Cc</c>, <c>Cf</c>, <c>Cs</c>, <c>Zl</c>, <c>Zp</c>) rather than by a hand-written
    /// range. Category is what makes this correct where the range this replaced was not:
    /// <c>U+202E</c> is <c>Cf</c> and <c>U+2028</c>/<c>U+2029</c> are <c>Zl</c>/<c>Zp</c>, so
    /// none of them is below <c>U+0020</c> and all three reached the terminal raw (issue #3628).
    /// </para>
    /// <para>
    /// Refusal is checked against the raw text, before encoding, because the question it asks is
    /// about the artifact rather than about the rendering. When it passes, the value renders
    /// exactly as <see cref="UntrustedTextMode.Contain"/> would, so the two modes differ only in
    /// whether they can fail.
    /// </para>
    /// <para>
    /// Internal rather than private because the image overview
    /// (<see cref="MetadataImageInspector"/>) reports an artifact-derived string of its own —
    /// the metadata root's version stamp — and must apply this same treatment rather than a
    /// parallel one.
    /// </para>
    /// </summary>
    /// <returns>
    /// The contained value, still paired with whether it was bounded. Returning
    /// <see cref="InertString"/> rather than a <see cref="string"/> and an <c>out</c> flag
    /// is the point: the pair travels together into the projection, and only a sink that
    /// has decided how to mark a partial value takes it apart.
    /// </returns>
    /// <exception cref="UntrustedTextException">
    /// <paramref name="mode"/> is <see cref="UntrustedTextMode.Refuse"/> and the text carries a
    /// scalar the policy does not permit.
    /// </exception>
    // The budget goes in as configured: a negative one is read as zero rather than
    // refused, so clamping here would be a line that never changes an answer.
    internal static InertString ContainCellText(
        string value,
        int maxChars,
        UntrustedTextMode mode = UntrustedTextMode.Contain,
        TextOrigin origin = default)
        => MetadataTableProjectionEngine.ContainCellText(value, maxChars, mode, origin);
}
