using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

/// <summary>
/// Owns the reverse-reference scan behind <see cref="MetadataTableProjector.FindReferences"/>:
/// finding every row that points at a given target row, and disclosing the scan's own blind
/// spots rather than folding them into a silently-partial result.
///
/// Reads the same <see cref="MetadataTableProjectionEngine.SupportedTables"/> declarations and
/// row readers the table projection uses, so the reverse search can never see an edge shape the
/// forward projection does not also see.
/// </summary>
internal static class MetadataRowReferenceFinder
{
    /// <summary>
    /// Finds every row that points at <paramref name="target"/> — the reverse of the forward
    /// <see cref="HandleRef"/>/<see cref="HandleRange"/> edges a projection exposes. Assumes the
    /// caller has already validated the source image has metadata.
    /// </summary>
    internal static MetadataRowReferenceSet FindReferences(
        MetadataReader reader,
        MetadataRowLocation target,
        int maxReferences)
    {
        var references = ImmutableArray.CreateBuilder<MetadataRowReference>();
        var unreadable = ImmutableArray.CreateBuilder<MetadataRowLocation>();

        // Reported rather than rejected. A row id past the end of the table is
        // usually a typo, and answering it with a clean empty result makes that
        // typo indistinguishable from a real row nothing points at. But it is
        // not simply invalid input either: a dangling edge points exactly at
        // rows that do not exist, so "what points at TypeRef[1]?" stays a
        // legitimate question in an image whose TypeRef table is empty.
        bool targetExists = target.RowId <= reader.GetTableRowCount(target.Table);

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
        foreach (var spec in MetadataTableProjectionEngine.SupportedTables)
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

                    if (!PointsAt(cells[column], target.Table, target.RowId, out var kind))
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
    /// Whether <paramref name="value"/> is an edge onto the target row. A handle
    /// names one row; a list column covers the half-open run
    /// <c>[StartRowId, EndRowId)</c>, so membership — not equality — decides.
    /// </summary>
    static bool PointsAt(MetadataValue value, TableIndex table, int rowId, out MetadataRowReferenceKind kind)
    {
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
}
