using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

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
/// </summary>
public static class MetadataTableProjector
{
    static readonly ImmutableArray<TableIndex> ResolutionScopeTargets =
        [TableIndex.Module, TableIndex.ModuleRef, TableIndex.AssemblyRef, TableIndex.TypeRef];

    static readonly ImmutableArray<TableIndex> TypeDefOrRefTargets =
        [TableIndex.TypeDef, TableIndex.TypeRef, TableIndex.TypeSpec];

    static readonly ImmutableArray<TableIndex> MemberRefParentTargets =
        [TableIndex.TypeDef, TableIndex.TypeRef, TableIndex.ModuleRef, TableIndex.MethodDef, TableIndex.TypeSpec];

    static readonly ImmutableArray<TableIndex> CustomAttributeTypeTargets =
        [TableIndex.MethodDef, TableIndex.MemberRef];

    static readonly ImmutableArray<TableIndex> HasCustomAttributeTargets =
    [
        TableIndex.MethodDef, TableIndex.Field, TableIndex.TypeRef, TableIndex.TypeDef,
        TableIndex.Param, TableIndex.InterfaceImpl, TableIndex.MemberRef, TableIndex.Module,
        TableIndex.DeclSecurity, TableIndex.Property, TableIndex.Event, TableIndex.StandAloneSig,
        TableIndex.ModuleRef, TableIndex.TypeSpec, TableIndex.Assembly, TableIndex.AssemblyRef,
        TableIndex.File, TableIndex.ExportedType, TableIndex.ManifestResource, TableIndex.GenericParam,
        TableIndex.GenericParamConstraint, TableIndex.MethodSpec,
    ];

    static readonly ImmutableArray<TableIndex> HasConstantTargets =
        [TableIndex.Field, TableIndex.Param, TableIndex.Property];

    static readonly ImmutableArray<TableIndex> MethodDefOrRefTargets =
        [TableIndex.MethodDef, TableIndex.MemberRef];

    static readonly ImmutableArray<TableIndex> ImplementationTargets =
        [TableIndex.File, TableIndex.ExportedType, TableIndex.AssemblyRef];

    static readonly ImmutableArray<TableIndex> TypeOrMethodDefTargets =
        [TableIndex.TypeDef, TableIndex.MethodDef];

    static readonly ImmutableArray<TableSpec> SupportedTables =
    [
        new(TableIndex.Module, "Module", ModuleColumns, ReadModuleRow),
        new(TableIndex.TypeRef, "TypeRef", TypeRefColumns, ReadTypeRefRow),
        new(TableIndex.TypeDef, "TypeDef", TypeDefColumns, ReadTypeDefRow),
        new(TableIndex.Field, "Field", FieldColumns, ReadFieldRow),
        new(TableIndex.MethodDef, "MethodDef", MethodDefColumns, ReadMethodDefRow),
        new(TableIndex.Param, "Param", ParamColumns, ReadParamRow),
        new(TableIndex.MemberRef, "MemberRef", MemberRefColumns, ReadMemberRefRow),
        new(TableIndex.Constant, "Constant", ConstantColumns, ReadConstantRow),
        new(TableIndex.CustomAttribute, "CustomAttribute", CustomAttributeColumns, ReadCustomAttributeRow),
        new(TableIndex.StandAloneSig, "StandAloneSig", StandAloneSigColumns, ReadStandAloneSigRow),
        new(TableIndex.MethodImpl, "MethodImpl", MethodImplColumns, ReadMethodImplRow),
        new(TableIndex.TypeSpec, "TypeSpec", TypeSpecColumns, ReadTypeSpecRow),
        new(TableIndex.Assembly, "Assembly", AssemblyColumns, ReadAssemblyRow),
        new(TableIndex.AssemblyRef, "AssemblyRef", AssemblyRefColumns, ReadAssemblyRefRow),
        new(TableIndex.ExportedType, "ExportedType", ExportedTypeColumns, ReadExportedTypeRow),
        new(TableIndex.GenericParam, "GenericParam", GenericParamColumns, ReadGenericParamRow),
        new(TableIndex.MethodSpec, "MethodSpec", MethodSpecColumns, ReadMethodSpecRow),
    ];

    /// <summary>
    /// The tables this projector models, in ECMA-335 table order. A table absent
    /// from this set is not projected at all, which is a different fact from a
    /// table that is projected and empty — see
    /// <see cref="MetadataTableSummary.IsProjected"/>.
    /// </summary>
    public static ImmutableArray<TableIndex> ProjectedTables { get; } =
        [.. SupportedTables.Select(static spec => spec.Index)];

    /// <summary>
    /// The columns <paramref name="table"/> projects, in order, or an empty array when the table
    /// is not projected. Reads the same <c>SupportedTables</c> declaration the projection itself
    /// reads, so a consumer can describe a table's shape — for discovery, schema registration, or
    /// column projection — without paying for a projection or restating the column list.
    /// </summary>
    public static ImmutableArray<MetadataColumn> ColumnsFor(TableIndex table)
    {
        foreach (var spec in SupportedTables)
            if (spec.Index == table)
                return spec.Columns;

        return ImmutableArray<MetadataColumn>.Empty;
    }

    /// <summary>
    /// Projects the supported metadata tables of <paramref name="peReader"/>.
    /// Returns an empty projection when the image carries no metadata.
    /// </summary>
    public static MetadataTableProjection Project(PEReader peReader, MetadataProjectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(peReader);
        options ??= new MetadataProjectionOptions();

        if (!peReader.HasMetadata)
            return new MetadataTableProjection(ImmutableArray<MetadataTableView>.Empty);

        // MetadataReaderOptions.None keeps the projection raw: the default enables
        // Windows-Runtime projection, which would replace real table/heap values
        // with synthesized aliases and defeat structural losslessness.
        var reader = peReader.GetMetadataReader(MetadataReaderOptions.None);

        var selected = options.Tables.IsDefaultOrEmpty
            ? (IReadOnlyCollection<TableIndex>?)null
            : options.Tables.ToImmutableHashSet();

        var views = ImmutableArray.CreateBuilder<MetadataTableView>();
        foreach (var spec in SupportedTables)
        {
            if (selected is not null && !selected.Contains(spec.Index))
                continue;

            int rowCount = reader.GetTableRowCount(spec.Index);
            if (rowCount == 0)
                continue;

            views.Add(BuildView(reader, spec, rowCount, options));
        }

        return new MetadataTableProjection(views.ToImmutable());
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

        if (!peReader.HasMetadata)
            return null;

        TableSpec? match = null;
        foreach (var candidate in SupportedTables)
        {
            if (candidate.Index == table)
            {
                match = candidate;
                break;
            }
        }

        if (match is not { } spec)
            return null;

        var reader = peReader.GetMetadataReader(MetadataReaderOptions.None);
        int rowCount = reader.GetTableRowCount(spec.Index);
        if (rowId > rowCount)
            return null;

        // Reuse the windowed reader rather than a second row path: a one-row
        // window at rowId is exactly this lookup, including malformed-row containment.
        return BuildView(reader, spec, rowCount, options with { StartRowId = rowId, MaxRowsPerTable = 1 });
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
        var references = ImmutableArray.CreateBuilder<MetadataRowReference>();
        var unreadable = ImmutableArray.CreateBuilder<MetadataRowLocation>();

        if (!peReader.HasMetadata)
            return new MetadataRowReferenceSet(
                target, references.ToImmutable(), unreadable.ToImmutable(), [],
                Truncated: false, TargetExists: false);

        var reader = peReader.GetMetadataReader(MetadataReaderOptions.None);

        // Reported rather than rejected. A row id past the end of the table is
        // usually a typo, and answering it with a clean empty result makes that
        // typo indistinguishable from a real row nothing points at. But it is
        // not simply invalid input either: a dangling edge points exactly at
        // rows that do not exist, so "what points at TypeRef[1]?" stays a
        // legitimate question in an image whose TypeRef table is empty.
        bool targetExists = targetRowId <= reader.GetTableRowCount(targetTable);

        // The scan needs edges, not text. Handle and range cells carry their
        // target table and row id independently of these budgets, so trimming the
        // heap previews cannot change which rows match — it only avoids decoding
        // strings and blobs the result never shows.
        var scan = new MetadataProjectionOptions { MaxStringChars = 1, MaxPreviewBytes = 0 };

        // What the scan actually reached, recorded as it goes. The blind-spot
        // report is derived from this rather than from SupportedTables, so a
        // table the loop skips — because the projection does not model it, or
        // because the budget stopped the scan before reaching it — cannot be
        // declared searched by a list that disagrees with the loop.
        var visited = new HashSet<TableIndex>();

        bool truncated = false;
        foreach (var spec in SupportedTables)
        {
            if (truncated)
                break;

            int rowCount = reader.GetTableRowCount(spec.Index);
            int rid = 1;
            bool abandonedMidTable = false;
            for (; rid <= rowCount && !truncated; rid++)
            {
                ImmutableArray<MetadataValue> cells;
                try
                {
                    cells = spec.ReadRow(reader, rid, scan);
                }
                catch (Exception ex) when (ex is BadImageFormatException or ArgumentException or InvalidOperationException)
                {
                    // The row's edges are unknowable, so record the blind spot
                    // rather than letting a missed reference look like an absent one.
                    unreadable.Add(new MetadataRowLocation(spec.Index, rid));
                    continue;
                }

                bool blind = false;
                for (int column = 0; column < cells.Length; column++)
                {
                    // A cell that failed to decode in an *edge* column may have
                    // been an edge onto the target. The cell-level readers
                    // contain such failures as Malformed rather than throwing, so
                    // ReadRow succeeds and the row would otherwise pass as fully
                    // searched. Record the blind spot instead, or a missed
                    // reference reads as an absent one.
                    //
                    // The column's declared kind decides, not the cell: a
                    // Malformed heap, scalar, or flags cell was never an edge and
                    // cannot hide a reference.
                    if (cells[column] is MetadataValue.Malformed
                        && spec.Columns[column].Kind is MetadataColumnKind.Handle or MetadataColumnKind.HandleRange)
                    {
                        blind = true;
                        continue;
                    }

                    if (!PointsAt(cells[column], targetTable, targetRowId, out var kind))
                        continue;

                    if (references.Count >= maxReferences)
                    {
                        truncated = true;

                        // Whether this table still holds anything unlooked-at:
                        // the columns after this one on this row, or any row
                        // after it. Stopping on the very last column of the very
                        // last row leaves nothing unexamined, so the table was
                        // genuinely searched in full even though the scan ended
                        // inside it.
                        //
                        // This also covers `blind` going under-determined. A
                        // break before the last column leaves the remaining
                        // columns unchecked for Malformed edges, so this row
                        // might belong in unreadable without our knowing — but
                        // that break is exactly the case where the flag is true,
                        // so the table is disclosed as unscanned instead. When
                        // the break is on the last column, every column was
                        // checked and `blind` is fully determined. Either way
                        // the gap is reported.
                        abandonedMidTable = column + 1 < cells.Length || rid < rowCount;
                        break;
                    }

                    references.Add(new MetadataRowReference(
                        new MetadataRowLocation(spec.Index, rid),
                        column,
                        spec.Columns[column].Name,
                        kind));
                }

                // Recorded once per row, and after the column loop, so a row with
                // one broken edge still reports the good edges it does have.
                if (blind)
                    unreadable.Add(new MetadataRowLocation(spec.Index, rid));
            }

            // Recorded after the row loop and only when every row the image says
            // exists was examined in full. "Entered" is not the same as
            // "searched", at either granularity:
            //
            //  - A row loop that stopped short leaves whole rows unread, so rid
            //    never passes the count and the table is not recorded.
            //  - The budget check sits inside the *column* loop, so truncation
            //    on a table's final row leaves that row entered but abandoned
            //    part-way through its columns. rid still passes the count, so
            //    the row loop alone cannot tell.
            //
            // abandonedMidTable carries that column-level fact. Note it is not
            // the same as truncated: a scan that stops on the last column of the
            // last row examined every cell this table has, so reporting it
            // unscanned would be a false blind spot — claiming an unread row
            // could hide an edge when no row went unread.
            //
            // The count is re-read from the reader rather than trusting the
            // local, so the claim "we covered this table" is anchored to the
            // metadata instead of to a variable the loop could have narrowed.
            if (!abandonedMidTable && rid > reader.GetTableRowCount(spec.Index))
                visited.Add(spec.Index);
        }

        return new MetadataRowReferenceSet(
            target,
            references.ToImmutable(),
            unreadable.ToImmutable(),
            CollectUnscannedTables(reader, visited),
            truncated,
            targetExists);
    }

    /// <summary>
    /// The populated tables a reverse search did not reach — because the
    /// projection does not model them, or because the result budget stopped the
    /// scan first. Derived from the tables the scan actually visited, not from a
    /// parallel declaration of what it intends to visit: the two could drift,
    /// and a blind spot that under-reports is the bug this exists to prevent.
    /// Empty tables are excluded: a table with no rows cannot hold an edge onto
    /// the target, so reporting it would overstate the blind spot.
    /// </summary>
    static ImmutableArray<TableIndex> CollectUnscannedTables(
        MetadataReader reader,
        HashSet<TableIndex> visited)
    {
        var unscanned = ImmutableArray.CreateBuilder<TableIndex>();
        foreach (var table in Enum.GetValues<TableIndex>())
        {
            if (visited.Contains(table))
                continue;

            // Every defined TableIndex is below the reader's table count, so
            // this is a range check plus an array index over counts parsed at
            // MetadataReader construction. It cannot fail here.
            if (reader.GetTableRowCount(table) > 0)
                unscanned.Add(table);
        }

        return unscanned.ToImmutable();
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

        if (!peReader.HasMetadata)
            return null;

        var reader = peReader.GetMetadataReader(MetadataReaderOptions.None);

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
                $"{heap} heap address {address} is past the end of a {size}-byte heap.");

        try
        {
            return heap switch
            {
                HeapKind.String => StringCell(reader, MetadataTokens.StringHandle(address), options),
                HeapKind.Blob => BlobCell(reader, MetadataTokens.BlobHandle(address), options),
                HeapKind.Guid => GuidCell(reader, MetadataTokens.GuidHandle(address)),
                HeapKind.UserString => UserStringCell(reader, MetadataTokens.UserStringHandle(address), options),
                _ => throw new System.Diagnostics.UnreachableException($"Unhandled heap {heap}."),
            };
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            // Handle construction happens outside the cell readers' own guards,
            // so a rejected address is contained here rather than escaping as a
            // throw from a read-only query. An unknown HeapKind is not caught
            // here: ToHeapIndex above already rejected it.
            return new MetadataValue.Malformed($"{heap} heap read failed: {ex.Message}");
        }
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

        if (!peReader.HasMetadata)
            return null;

        var reader = peReader.GetMetadataReader(MetadataReaderOptions.None);
        int size = reader.GetHeapSize(MetadataImageInspector.ToHeapIndex(heap));
        int budget = Math.Max(1, options.MaxHeapEntries);

        if (heap == HeapKind.UserString)
            return new MetadataHeapEntrySet(heap, size, MetadataHeapCoverage.NotEnumerable, []);

        var references = ScanHeapReferences(peReader, heap, options, out bool rowsTruncated);

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
        PEReader peReader,
        HeapKind heap,
        MetadataProjectionOptions options,
        out bool rowsTruncated)
    {
        var found = new Dictionary<int, (MetadataValue Value, int Count)>();
        rowsTruncated = false;

        var projection = Project(peReader, options with { Tables = default });
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
                value = GuidCell(reader, MetadataTokens.GuidHandle(index));
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
            {
                value = new MetadataValue.Malformed($"Guid heap read failed at index {index}: {ex.Message}");
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

    /// <summary>
    /// Whether <paramref name="value"/> is an edge onto the target row. A handle
    /// names one row; a list column covers the half-open run
    /// <c>[StartRowId, EndRowId)</c>, so membership — not equality — decides.
    /// </summary>
    static bool PointsAt(MetadataValue value, TableIndex table, int rowId, out MetadataRowReferenceKind kind)    {
        switch (value)
        {
            case MetadataValue.Handle handle
                when handle.Reference.TargetTable == table && handle.Reference.TargetRowId == rowId:
                kind = MetadataRowReferenceKind.Handle;
                return true;

            case MetadataValue.Range range
                when range.Reference.TargetTable == table
                    && rowId >= range.Reference.StartRowId
                    && rowId < range.Reference.EndRowId:
                kind = MetadataRowReferenceKind.Range;
                return true;

            default:
                kind = default;
                return false;
        }
    }

    static MetadataTableView BuildView(
        MetadataReader reader,
        TableSpec spec,
        int rowCount,
        MetadataProjectionOptions options)
    {
        int start = Math.Max(1, options.StartRowId);
        int budget = Math.Max(0, options.MaxRowsPerTable);

        // Widen before adding: a caller-supplied start near int.MaxValue would
        // otherwise overflow into a window that wrongly overlaps the table.
        long inclusiveEnd = (long)start + budget - 1;
        int end = (int)Math.Min(rowCount, inclusiveEnd);
        int projected = end < start ? 0 : end - start + 1;

        var rows = ImmutableArray.CreateBuilder<MetadataRow>(projected);

        for (int rid = start; rid <= end; rid++)
        {
            int token = ((int)spec.Index << 24) | rid;

            // A single malformed row must not abort the whole projection: contain
            // SRM's rejection as a typed Malformed row aligned to the columns.
            try
            {
                rows.Add(new MetadataRow(rid, token, spec.ReadRow(reader, rid, options)));
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentException or InvalidOperationException)
            {
                var malformed = ImmutableArray.CreateBuilder<MetadataValue>(spec.Columns.Length);
                for (int column = 0; column < spec.Columns.Length; column++)
                    malformed.Add(new MetadataValue.Malformed($"Row read failed: {ex.Message}"));

                rows.Add(new MetadataRow(rid, token, malformed.MoveToImmutable()));
            }
        }

        var truncation = projected < rowCount ? new MetadataTableTruncation(projected, rowCount) : null;
        return new MetadataTableView(spec.Index, spec.Name, rowCount, spec.Columns, rows.ToImmutable(), truncation);
    }

    // ---- Per-table column schemas ----------------------------------------

    static ImmutableArray<MetadataColumn> ModuleColumns =>
    [
        new("Generation", MetadataColumnKind.Scalar),
        new("Name", MetadataColumnKind.Heap),
        new("Mvid", MetadataColumnKind.Heap),
        new("EncId", MetadataColumnKind.Heap),
        new("EncBaseId", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> TypeRefColumns =>
    [
        new("ResolutionScope", MetadataColumnKind.Handle, ResolutionScopeTargets),
        new("Name", MetadataColumnKind.Heap),
        new("Namespace", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> TypeDefColumns =>
    [
        new("Attributes", MetadataColumnKind.Flags),
        new("Name", MetadataColumnKind.Heap),
        new("Namespace", MetadataColumnKind.Heap),
        new("Extends", MetadataColumnKind.Handle, TypeDefOrRefTargets),
        new("FieldList", MetadataColumnKind.HandleRange, [TableIndex.Field]),
        new("MethodList", MetadataColumnKind.HandleRange, [TableIndex.MethodDef]),
    ];

    static ImmutableArray<MetadataColumn> FieldColumns =>
    [
        new("Attributes", MetadataColumnKind.Flags),
        new("Name", MetadataColumnKind.Heap),
        new("Signature", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> MethodDefColumns =>
    [
        new("Rva", MetadataColumnKind.Scalar),
        new("ImplAttributes", MetadataColumnKind.Flags),
        new("Attributes", MetadataColumnKind.Flags),
        new("Name", MetadataColumnKind.Heap),
        new("Signature", MetadataColumnKind.Heap),
        new("ParamList", MetadataColumnKind.HandleRange, [TableIndex.Param]),
    ];

    static ImmutableArray<MetadataColumn> ParamColumns =>
    [
        new("Attributes", MetadataColumnKind.Flags),
        new("Sequence", MetadataColumnKind.Scalar),
        new("Name", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> MemberRefColumns =>
    [
        new("Class", MetadataColumnKind.Handle, MemberRefParentTargets),
        new("Name", MetadataColumnKind.Heap),
        new("Signature", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> ConstantColumns =>
    [
        new("Type", MetadataColumnKind.Scalar),
        new("Parent", MetadataColumnKind.Handle, HasConstantTargets),
        new("Value", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> CustomAttributeColumns =>
    [
        new("Parent", MetadataColumnKind.Handle, HasCustomAttributeTargets),
        new("Type", MetadataColumnKind.Handle, CustomAttributeTypeTargets),
        new("Value", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> AssemblyRefColumns =>
    [
        new("MajorVersion", MetadataColumnKind.Scalar),
        new("MinorVersion", MetadataColumnKind.Scalar),
        new("BuildNumber", MetadataColumnKind.Scalar),
        new("RevisionNumber", MetadataColumnKind.Scalar),
        new("Flags", MetadataColumnKind.Flags),
        new("PublicKeyOrToken", MetadataColumnKind.Heap),
        new("Name", MetadataColumnKind.Heap),
        new("Culture", MetadataColumnKind.Heap),
        new("HashValue", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> StandAloneSigColumns =>
    [
        new("Signature", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> MethodImplColumns =>
    [
        new("Class", MetadataColumnKind.Handle, [TableIndex.TypeDef]),
        new("MethodBody", MetadataColumnKind.Handle, MethodDefOrRefTargets),
        new("MethodDeclaration", MetadataColumnKind.Handle, MethodDefOrRefTargets),
    ];

    static ImmutableArray<MetadataColumn> TypeSpecColumns =>
    [
        new("Signature", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> AssemblyColumns =>
    [
        new("HashAlgId", MetadataColumnKind.Scalar),
        new("MajorVersion", MetadataColumnKind.Scalar),
        new("MinorVersion", MetadataColumnKind.Scalar),
        new("BuildNumber", MetadataColumnKind.Scalar),
        new("RevisionNumber", MetadataColumnKind.Scalar),
        new("Flags", MetadataColumnKind.Flags),
        new("PublicKey", MetadataColumnKind.Heap),
        new("Name", MetadataColumnKind.Heap),
        new("Culture", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> ExportedTypeColumns =>
    [
        new("Attributes", MetadataColumnKind.Flags),
        new("TypeDefId", MetadataColumnKind.Scalar),
        new("Name", MetadataColumnKind.Heap),
        new("Namespace", MetadataColumnKind.Heap),
        new("Implementation", MetadataColumnKind.Handle, ImplementationTargets),
    ];

    static ImmutableArray<MetadataColumn> GenericParamColumns =>
    [
        new("Number", MetadataColumnKind.Scalar),
        new("Attributes", MetadataColumnKind.Flags),
        new("Owner", MetadataColumnKind.Handle, TypeOrMethodDefTargets),
        new("Name", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> MethodSpecColumns =>
    [
        new("Method", MetadataColumnKind.Handle, MethodDefOrRefTargets),
        new("Instantiation", MetadataColumnKind.Heap),
    ];

    // ---- Per-table row readers -------------------------------------------

    static ImmutableArray<MetadataValue> ReadModuleRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var module = reader.GetModuleDefinition();
        return
        [
            new MetadataValue.Scalar(module.Generation, module.Generation.ToString()),
            StringCell(reader, module.Name, options),
            GuidCell(reader, module.Mvid),
            GuidCell(reader, module.GenerationId),
            GuidCell(reader, module.BaseGenerationId),
        ];
    }

    static ImmutableArray<MetadataValue> ReadTypeRefRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var typeRef = reader.GetTypeReference(MetadataTokens.TypeReferenceHandle(rid));
        return
        [
            HandleCell(reader, typeRef.ResolutionScope, options),
            StringCell(reader, typeRef.Name, options),
            StringCell(reader, typeRef.Namespace, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadTypeDefRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var handle = MetadataTokens.TypeDefinitionHandle(rid);
        var typeDef = reader.GetTypeDefinition(handle);
        return
        [
            FlagsCell((long)typeDef.Attributes, typeDef.Attributes.ToString()),
            StringCell(reader, typeDef.Name, options),
            StringCell(reader, typeDef.Namespace, options),
            HandleCell(reader, typeDef.BaseType, options),
            RangeCell(TableIndex.Field, typeDef.GetFields()),
            RangeCell(TableIndex.MethodDef, typeDef.GetMethods()),
        ];
    }

    static ImmutableArray<MetadataValue> ReadFieldRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var field = reader.GetFieldDefinition(MetadataTokens.FieldDefinitionHandle(rid));
        return
        [
            FlagsCell((long)field.Attributes, field.Attributes.ToString()),
            StringCell(reader, field.Name, options),
            BlobCell(reader, field.Signature, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadMethodDefRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var method = reader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(rid));
        return
        [
            new MetadataValue.Scalar(method.RelativeVirtualAddress, $"0x{method.RelativeVirtualAddress:X8}"),
            FlagsCell((long)method.ImplAttributes, method.ImplAttributes.ToString()),
            FlagsCell((long)method.Attributes, method.Attributes.ToString()),
            StringCell(reader, method.Name, options),
            BlobCell(reader, method.Signature, options),
            RangeCell(TableIndex.Param, method.GetParameters()),
        ];
    }

    static ImmutableArray<MetadataValue> ReadParamRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var param = reader.GetParameter(MetadataTokens.ParameterHandle(rid));
        return
        [
            FlagsCell((long)param.Attributes, param.Attributes.ToString()),
            new MetadataValue.Scalar(param.SequenceNumber, param.SequenceNumber.ToString()),
            StringCell(reader, param.Name, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadMemberRefRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var memberRef = reader.GetMemberReference(MetadataTokens.MemberReferenceHandle(rid));
        return
        [
            HandleCell(reader, memberRef.Parent, options),
            StringCell(reader, memberRef.Name, options),
            BlobCell(reader, memberRef.Signature, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadConstantRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var constant = reader.GetConstant(MetadataTokens.ConstantHandle(rid));
        return
        [
            new MetadataValue.Scalar((long)constant.TypeCode, constant.TypeCode.ToString()),
            HandleCell(reader, constant.Parent, options),
            BlobCell(reader, constant.Value, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadCustomAttributeRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var attribute = reader.GetCustomAttribute(MetadataTokens.CustomAttributeHandle(rid));
        return
        [
            HandleCell(reader, attribute.Parent, options),
            HandleCell(reader, attribute.Constructor, options),
            BlobCell(reader, attribute.Value, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadAssemblyRefRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var assemblyRef = reader.GetAssemblyReference(MetadataTokens.AssemblyReferenceHandle(rid));
        var version = assemblyRef.Version;
        return
        [
            new MetadataValue.Scalar(version.Major, version.Major.ToString()),
            new MetadataValue.Scalar(version.Minor, version.Minor.ToString()),
            new MetadataValue.Scalar(version.Build, version.Build.ToString()),
            new MetadataValue.Scalar(version.Revision, version.Revision.ToString()),
            FlagsCell((long)assemblyRef.Flags, assemblyRef.Flags.ToString()),
            BlobCell(reader, assemblyRef.PublicKeyOrToken, options),
            StringCell(reader, assemblyRef.Name, options),
            StringCell(reader, assemblyRef.Culture, options),
            BlobCell(reader, assemblyRef.HashValue, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadStandAloneSigRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var signature = reader.GetStandaloneSignature(MetadataTokens.StandaloneSignatureHandle(rid));
        return
        [
            BlobCell(reader, signature.Signature, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadMethodImplRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var methodImpl = reader.GetMethodImplementation(MetadataTokens.MethodImplementationHandle(rid));
        return
        [
            HandleCell(reader, methodImpl.Type, options),
            HandleCell(reader, methodImpl.MethodBody, options),
            HandleCell(reader, methodImpl.MethodDeclaration, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadTypeSpecRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var typeSpec = reader.GetTypeSpecification(MetadataTokens.TypeSpecificationHandle(rid));
        return
        [
            BlobCell(reader, typeSpec.Signature, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadAssemblyRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var assembly = reader.GetAssemblyDefinition();
        var version = assembly.Version;
        return
        [
            new MetadataValue.Scalar((long)assembly.HashAlgorithm, assembly.HashAlgorithm.ToString()),
            new MetadataValue.Scalar(version.Major, version.Major.ToString()),
            new MetadataValue.Scalar(version.Minor, version.Minor.ToString()),
            new MetadataValue.Scalar(version.Build, version.Build.ToString()),
            new MetadataValue.Scalar(version.Revision, version.Revision.ToString()),
            FlagsCell((long)assembly.Flags, assembly.Flags.ToString()),
            BlobCell(reader, assembly.PublicKey, options),
            StringCell(reader, assembly.Name, options),
            StringCell(reader, assembly.Culture, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadExportedTypeRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var exportedType = reader.GetExportedType(MetadataTokens.ExportedTypeHandle(rid));
        int typeDefId = exportedType.GetTypeDefinitionId();
        return
        [
            FlagsCell((long)exportedType.Attributes, exportedType.Attributes.ToString()),
            new MetadataValue.Scalar(typeDefId, $"0x{typeDefId:X8}"),
            StringCell(reader, exportedType.Name, options),
            StringCell(reader, exportedType.Namespace, options),
            HandleCell(reader, exportedType.Implementation, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadGenericParamRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var genericParam = reader.GetGenericParameter(MetadataTokens.GenericParameterHandle(rid));
        return
        [
            new MetadataValue.Scalar(genericParam.Index, genericParam.Index.ToString()),
            FlagsCell((long)genericParam.Attributes, genericParam.Attributes.ToString()),
            HandleCell(reader, genericParam.Parent, options),
            StringCell(reader, genericParam.Name, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadMethodSpecRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var methodSpec = reader.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(rid));
        return
        [
            HandleCell(reader, methodSpec.Method, options),
            BlobCell(reader, methodSpec.Signature, options),
        ];
    }

    // ---- Cell builders ----------------------------------------------------

    static MetadataValue StringCell(MetadataReader reader, StringHandle handle, MetadataProjectionOptions options)
    {
        if (handle.IsNil)
            return new MetadataValue.Nil();

        try
        {
            string raw = reader.GetString(handle);
            string text = EscapeText(raw, options.MaxStringChars, out bool truncated);
            return new MetadataValue.HeapReference(
                HeapKind.String, HeapOffset(handle), raw.Length, text, text, truncated);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed($"String heap read failed: {ex.Message}");
        }
    }

    static MetadataValue UserStringCell(MetadataReader reader, UserStringHandle handle, MetadataProjectionOptions options)
    {
        if (handle.IsNil)
            return new MetadataValue.Nil();

        try
        {
            string raw = reader.GetUserString(handle);
            string text = EscapeText(raw, options.MaxStringChars, out bool truncated);
            return new MetadataValue.HeapReference(
                HeapKind.UserString, MetadataTokens.GetHeapOffset(handle), raw.Length, text, text, truncated);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed($"UserString heap read failed: {ex.Message}");
        }
    }

    static MetadataValue GuidCell(MetadataReader reader, GuidHandle handle)
    {
        if (handle.IsNil)
            return new MetadataValue.Nil();

        try
        {
            string text = reader.GetGuid(handle).ToString();
            return new MetadataValue.HeapReference(
                HeapKind.Guid, HeapOffset(handle), 16, text, text, Truncated: false);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed($"Guid heap read failed: {ex.Message}");
        }
    }

    static MetadataValue BlobCell(MetadataReader reader, BlobHandle handle, MetadataProjectionOptions options)
    {
        if (handle.IsNil)
            return new MetadataValue.Nil();

        try
        {
            var blobReader = reader.GetBlobReader(handle);
            int length = blobReader.Length;
            int take = Math.Min(length, Math.Max(0, options.MaxPreviewBytes));
            byte[] bytes = blobReader.ReadBytes(take);
            string preview = Convert.ToHexString(bytes);

            return new MetadataValue.HeapReference(
                HeapKind.Blob, HeapOffset(handle), length, Text: null, preview, Truncated: take < length);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed($"Blob heap read failed: {ex.Message}");
        }
    }

    static int HeapOffset(StringHandle handle)
    {
        try
        {
            return MetadataTokens.GetHeapOffset(handle);
        }
        catch (ArgumentException)
        {
            return -1;
        }
    }

    static int HeapOffset(GuidHandle handle)
    {
        try
        {
            return MetadataTokens.GetHeapOffset(handle);
        }
        catch (ArgumentException)
        {
            return -1;
        }
    }

    static int HeapOffset(BlobHandle handle)
    {
        try
        {
            return MetadataTokens.GetHeapOffset(handle);
        }
        catch (ArgumentException)
        {
            return -1;
        }
    }

    static MetadataValue HandleCell(MetadataReader reader, EntityHandle handle, MetadataProjectionOptions options)
    {
        if (handle.IsNil)
            return new MetadataValue.Nil();

        try
        {
            if (!MetadataTokens.TryGetTableIndex(handle.Kind, out var table))
                return new MetadataValue.Malformed($"Handle kind {handle.Kind} does not map to a table.");

            int rid = MetadataTokens.GetRowNumber(handle);
            int token = MetadataTokens.GetToken(handle);

            // A coded index can decode to a row that does not exist in the target
            // table; a dangling edge is a visible failure, not a resolvable handle.
            int targetRows = reader.GetTableRowCount(table);
            if (rid < 1 || rid > targetRows)
                return new MetadataValue.Malformed(
                    $"Handle 0x{token:X8} targets {table} row {rid}, outside [1, {targetRows}].");

            string? display = ResolveHandleDisplay(reader, handle);
            bool displayTruncated = false;
            if (display is not null)
                display = NeutralizeControls(display, options.MaxStringChars, out displayTruncated);

            return new MetadataValue.Handle(new HandleRef(table, rid, token, display, displayTruncated));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed($"Handle resolution failed: {ex.Message}");
        }
    }

    static MetadataValue RangeCell(TableIndex target, FieldDefinitionHandleCollection fields)
    {
        if (fields.Count == 0)
            return new MetadataValue.Nil();

        try
        {
            int start = 0;
            foreach (var handle in fields)
            {
                start = MetadataTokens.GetRowNumber(handle);
                break;
            }

            return new MetadataValue.Range(new HandleRange(target, start, start + fields.Count));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed($"List column read failed: {ex.Message}");
        }
    }

    static MetadataValue RangeCell(TableIndex target, MethodDefinitionHandleCollection methods)
    {
        if (methods.Count == 0)
            return new MetadataValue.Nil();

        try
        {
            int start = 0;
            foreach (var handle in methods)
            {
                start = MetadataTokens.GetRowNumber(handle);
                break;
            }

            return new MetadataValue.Range(new HandleRange(target, start, start + methods.Count));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed($"List column read failed: {ex.Message}");
        }
    }

    static MetadataValue RangeCell(TableIndex target, ParameterHandleCollection parameters)
    {
        if (parameters.Count == 0)
            return new MetadataValue.Nil();

        try
        {
            int start = 0;
            foreach (var handle in parameters)
            {
                start = MetadataTokens.GetRowNumber(handle);
                break;
            }

            return new MetadataValue.Range(new HandleRange(target, start, start + parameters.Count));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed($"List column read failed: {ex.Message}");
        }
    }

    static string? ResolveHandleDisplay(MetadataReader reader, EntityHandle handle)
    {
        try
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                case HandleKind.TypeReference:
                case HandleKind.TypeSpecification:
                case HandleKind.MethodDefinition:
                case HandleKind.MemberReference:
                case HandleKind.FieldDefinition:
                    string text = ILTokenResolver.ResolveToken(reader, MetadataTokens.GetToken(handle));
                    return text.StartsWith("0x", StringComparison.Ordinal) ? null : text;

                case HandleKind.AssemblyReference:
                    return reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)handle).Name);

                case HandleKind.ModuleReference:
                    return reader.GetString(reader.GetModuleReference((ModuleReferenceHandle)handle).Name);

                case HandleKind.ModuleDefinition:
                    return reader.GetString(reader.GetModuleDefinition().Name);

                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return null;
        }
    }

    static MetadataValue FlagsCell(long raw, string decoded)
        => new MetadataValue.Flags(raw, decoded);

    /// <summary>
    /// Escapes a decoded heap string for use as data and bounds the EMITTED
    /// preview to <paramref name="maxChars"/> characters. Backslash and quote
    /// are escaped, and every control character (including ESC) is rendered as
    /// <c>\uXXXX</c> so the value cannot inject terminal control sequences or
    /// break structured output. Because escaping can expand a single character,
    /// the budget is enforced on the output length, not the input length;
    /// <paramref name="truncated"/> reports whether any input was dropped to keep
    /// the preview within budget.
    /// </summary>
    static string EscapeText(string value, int maxChars, out bool truncated)
        => EscapeCore(value, maxChars, escapeStructural: true, out truncated);

    /// <summary>
    /// Renders every control character in a display string as <c>\uXXXX</c>,
    /// leaving all other characters (including the structural <c>::</c>, quotes,
    /// and generic-arity marks in resolved names) intact, and bounds the EMITTED
    /// text to <paramref name="maxChars"/> characters so a large resolved name
    /// cannot be re-materialized across every referencing row. The budget is
    /// enforced on the output length, not the input length;
    /// <paramref name="truncated"/> reports whether any input was dropped to keep
    /// the text within budget.
    /// </summary>
    static string NeutralizeControls(string value, int maxChars, out bool truncated)
        => EscapeCore(value, maxChars, escapeStructural: false, out truncated);

    /// <summary>
    /// Shared budget-bounded escaper. Walks the UTF-16 value one scalar at a
    /// time so a well-formed surrogate pair is kept atomic — the budget boundary
    /// can never retain a lone (malformed) surrogate — while an unpaired
    /// surrogate is rendered as <c>\uXXXX</c> so it cannot corrupt the output on
    /// UTF-8 conversion. When <paramref name="escapeStructural"/> is set,
    /// <c>\ " \n \r \t</c> are escaped for use as data; either way every control
    /// character is rendered as <c>\uXXXX</c>. The <paramref name="maxChars"/>
    /// budget is enforced on the emitted length; <paramref name="truncated"/>
    /// reports whether any input was dropped to stay within it.
    /// </summary>
    static string EscapeCore(string value, int maxChars, bool escapeStructural, out bool truncated)
    {
        int limit = Math.Max(0, maxChars);
        var builder = new System.Text.StringBuilder(Math.Min(value.Length, limit));
        truncated = false;

        int i = 0;
        while (i < value.Length)
        {
            char c = value[i];

            // A well-formed surrogate pair is one scalar; emit it atomically.
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                if (builder.Length + 2 > limit)
                {
                    truncated = true;
                    break;
                }

                builder.Append(c).Append(value[i + 1]);
                i += 2;
                continue;
            }

            // A lone surrogate is ill-formed text; escape it rather than emit it.
            if (char.IsSurrogate(c))
            {
                if (builder.Length + 6 > limit)
                {
                    truncated = true;
                    break;
                }

                builder.Append("\\u").Append(((int)c).ToString("X4"));
                i++;
                continue;
            }

            string? structural = escapeStructural
                ? c switch
                {
                    '\\' => "\\\\",
                    '"' => "\\\"",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    _ => null,
                }
                : null;

            int width = structural is not null ? structural.Length : (IsControl(c) ? 6 : 1);
            if (builder.Length + width > limit)
            {
                truncated = true;
                break;
            }

            if (structural is not null)
                builder.Append(structural);
            else if (IsControl(c))
                builder.Append("\\u").Append(((int)c).ToString("X4"));
            else
                builder.Append(c);

            i++;
        }

        return builder.ToString();
    }

    // C0 controls, DEL, and the C1 control range — none of which are safe to
    // emit verbatim into a terminal or a structured record.
    static bool IsControl(char c) => c < ' ' || c == '\x7f' || (c >= '\x80' && c <= '\x9f');

    readonly record struct TableSpec(
        TableIndex Index,
        string Name,
        ImmutableArray<MetadataColumn> Columns,
        Func<MetadataReader, int, MetadataProjectionOptions, ImmutableArray<MetadataValue>> ReadRow);
}
