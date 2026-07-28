using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Tests for <see cref="MetadataTableProjector.FindReferences"/> — the reverse of
/// the projection's forward handle edges, answering "which rows point at this
/// row?" (issue #3341, gap 4).
///
/// Two edge shapes must both resolve: a handle column naming a row directly, and
/// a list column whose contiguous run contains it. The second is the one that
/// matters most, because ECMA-335 encodes ownership as a run on the owner rather
/// than a back-pointer on the owned row, so it is the only way to answer "which
/// type declares this field?".
/// </summary>
public class MetadataRowReferenceSearchTests
{
    static string SelfPath => typeof(MetadataRowReferenceSearchTests).Assembly.Location;

    static PEReader OpenSelfFromBytes() => new(new MemoryStream(File.ReadAllBytes(SelfPath)));

    static MetadataTableProjection FullProjection(PEReader peReader)
        => MetadataTableProjector.Project(
            peReader,
            new MetadataProjectionOptions { MaxRowsPerTable = int.MaxValue });

    /// <summary>
    /// An independent restatement of the search: walk every row of a complete
    /// projection and collect the cells that point at the target. Deliberately
    /// re-derives the half-open run membership rather than calling the product's
    /// helper, so a change to that rule shows up as a disagreement.
    /// </summary>
    static ImmutableArray<(TableIndex Table, int RowId, string Column, MetadataRowReferenceKind Kind)> Expected(
        MetadataTableProjection projection,
        TableIndex targetTable,
        int targetRowId)
    {
        var found = ImmutableArray.CreateBuilder<(TableIndex, int, string, MetadataRowReferenceKind)>();
        foreach (var table in projection.Tables)
        {
            foreach (var row in table.Rows)
            {
                for (int column = 0; column < row.Cells.Length; column++)
                {
                    switch (row.Cells[column])
                    {
                        case MetadataValue.Handle handle
                            when handle.Reference.TargetTable == targetTable
                                && handle.Reference.TargetRowId == targetRowId:
                            found.Add((table.Index, row.RowId, table.Columns[column].Name, MetadataRowReferenceKind.Handle));
                            break;

                        case MetadataValue.Range range
                            when range.Reference.TargetTable == targetTable
                                && targetRowId >= range.Reference.StartRowId
                                && targetRowId < range.Reference.EndRowId:
                            found.Add((table.Index, row.RowId, table.Columns[column].Name, MetadataRowReferenceKind.Range));
                            break;
                    }
                }
            }
        }

        return found.ToImmutable();
    }

    static ImmutableArray<(TableIndex Table, int RowId, string Column, MetadataRowReferenceKind Kind)> Actual(
        MetadataRowReferenceSet set)
        => [.. set.References.Select(r => (r.Source.Table, r.Source.RowId, r.ColumnName, r.Kind))];

    /// <summary>
    /// The scan itself finished and read every row it visited. This is not
    /// <see cref="MetadataRowReferenceSet.IsComplete"/>: on a real assembly that
    /// is false because the projection models a subset of ECMA-335's tables, so
    /// populated tables go unvisited. Tests that mean "the scan ran clean" must
    /// say so rather than borrowing a stronger claim.
    /// </summary>
    static void AssertScanRanClean(MetadataRowReferenceSet set)
    {
        Assert.False(set.Truncated, "Expected the scan to run to completion.");
        Assert.Empty(set.UnreadableRows);
    }

    [Theory]
    [InlineData(TableIndex.TypeDef, 1)]
    [InlineData(TableIndex.TypeDef, 2)]
    [InlineData(TableIndex.TypeRef, 1)]
    [InlineData(TableIndex.Field, 1)]
    [InlineData(TableIndex.MethodDef, 1)]
    [InlineData(TableIndex.MethodDef, 7)]
    [InlineData(TableIndex.Param, 1)]
    [InlineData(TableIndex.AssemblyRef, 1)]
    public void FindReferences_AgreesWithAFullScanOfTheProjection(TableIndex table, int rowId)
    {
        using var peReader = OpenSelfFromBytes();

        var expected = Expected(FullProjection(peReader), table, rowId);
        var set = MetadataTableProjector.FindReferences(peReader, table, rowId, int.MaxValue);

        AssertScanRanClean(set);
        Assert.Equal(expected, Actual(set));
    }

    [Fact]
    public void FindReferences_ResolvesOwnership_ThroughAListColumnRun()
    {
        using var peReader = OpenSelfFromBytes();

        // ECMA-335 gives a Field no back-pointer to its type; the only evidence is
        // that some TypeDef's FieldList run covers it. Without range edges this
        // question is unanswerable.
        var set = MetadataTableProjector.FindReferences(peReader, TableIndex.Field, 1, int.MaxValue);

        var owner = Assert.Single(
            set.References.Where(r => r.Source.Table == TableIndex.TypeDef && r.Kind == MetadataRowReferenceKind.Range));
        Assert.Equal("FieldList", owner.ColumnName);

        // The claimed owner must actually cover the field.
        var ownerRow = MetadataTableProjector.ProjectRow(peReader, TableIndex.TypeDef, owner.Source.RowId);
        var cell = Assert.IsType<MetadataValue.Range>(Assert.Single(ownerRow!.Rows).Cells[owner.ColumnIndex]);
        Assert.Equal(TableIndex.Field, cell.Reference.TargetTable);
        Assert.InRange(1, cell.Reference.StartRowId, cell.Reference.EndRowId - 1);
    }

    [Fact]
    public void FindReferences_ResolvesTheDeclaringMethodOfAParam()
    {
        using var peReader = OpenSelfFromBytes();

        var set = MetadataTableProjector.FindReferences(peReader, TableIndex.Param, 1, int.MaxValue);

        var owner = Assert.Single(
            set.References.Where(r => r.Source.Table == TableIndex.MethodDef && r.Kind == MetadataRowReferenceKind.Range));
        Assert.Equal("ParamList", owner.ColumnName);
    }

    [Fact]
    public void FindReferences_ResolvesHandleEdges()
    {
        using var peReader = OpenSelfFromBytes();
        var projection = FullProjection(peReader);

        // Pick a real handle edge out of the image, then look for it backwards.
        var typeDef = projection.Tables.Single(t => t.Index == TableIndex.TypeDef);
        int extends = typeDef.Columns.Select((c, i) => (c, i)).First(x => x.c.Name == "Extends").i;
        var edge = typeDef.Rows
            .Select(row => (row, cell: row.Cells[extends] as MetadataValue.Handle))
            .First(x => x.cell is { Reference.TargetTable: TableIndex.TypeRef });

        var set = MetadataTableProjector.FindReferences(
            peReader,
            TableIndex.TypeRef,
            edge.cell!.Reference.TargetRowId,
            int.MaxValue);

        Assert.Contains(set.References, r =>
            r.Source.Table == TableIndex.TypeDef
            && r.Source.RowId == edge.row.RowId
            && r.ColumnName == "Extends"
            && r.Kind == MetadataRowReferenceKind.Handle);
    }

    [Fact]
    public void FindReferences_CarriesAColumnIndexThatAlignsWithTheSourceRow()
    {
        using var peReader = OpenSelfFromBytes();

        var set = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeRef, 1, int.MaxValue);
        Assert.NotEmpty(set.References);

        foreach (var reference in set.References)
        {
            // ColumnIndex must index the source table's own schema, not the
            // target's, or a consumer highlighting the cell would point at the
            // wrong column.
            var view = MetadataTableProjector.ProjectRow(peReader, reference.Source.Table, reference.Source.RowId);
            Assert.NotNull(view);
            Assert.Equal(reference.ColumnName, view!.Columns[reference.ColumnIndex].Name);
            Assert.InRange(reference.ColumnIndex, 0, Assert.Single(view.Rows).Cells.Length - 1);
        }
    }

    /// <summary>
    /// A row that exists and that genuinely nothing points at. The row id is
    /// derived rather than hardcoded, because which rows are unreferenced is an
    /// artifact of how this assembly happens to be compiled.
    ///
    /// Deliberately not a row id past the end of the table: that is a different
    /// answer wearing the same clothes, and conflating the two is what
    /// <see cref="MetadataRowReferenceSet.TargetExists"/> exists to stop.
    /// </summary>
    [Fact]
    public void FindReferences_ForARowNothingPointsAt_ReportsAnEmptyResultFromACleanScan()
    {
        using var peReader = OpenSelfFromBytes();
        var typeDef = FullProjection(peReader).Tables.Single(t => t.Index == TableIndex.TypeDef);

        int unreferenced = Enumerable
            .Range(1, typeDef.RowCount)
            .First(rid => MetadataTableProjector
                .FindReferences(peReader, TableIndex.TypeDef, rid, int.MaxValue)
                .References.IsEmpty);

        var set = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeDef, unreferenced, int.MaxValue);

        Assert.Empty(set.References);
        Assert.True(set.TargetExists, "The row must exist, or this is not the case under test.");
        AssertScanRanClean(set);
    }

    [Fact]
    public void FindReferences_ForARowPastTheEndOfItsTable_SaysTheRowIsNotThere()
    {
        using var peReader = OpenSelfFromBytes();
        var typeDef = FullProjection(peReader).Tables.Single(t => t.Index == TableIndex.TypeDef);

        var set = MetadataTableProjector.FindReferences(
            peReader, TableIndex.TypeDef, typeDef.RowCount + 1_000, int.MaxValue);

        // Empty for a reason the caller must be able to tell apart from "this
        // row exists and nothing points at it". Without TargetExists the two
        // render identically, so a typo'd row id reads as a real answer.
        Assert.Empty(set.References);
        Assert.False(set.TargetExists);
        Assert.False(set.IsComplete);
    }

    [Fact]
    public void FindReferences_ForTheLastRowOfATable_SaysTheRowIsThere()
    {
        using var peReader = OpenSelfFromBytes();
        var typeDef = FullProjection(peReader).Tables.Single(t => t.Index == TableIndex.TypeDef);

        // The boundary the off-by-one would land on: row ids are 1-based, so the
        // last valid row is RowCount itself, not RowCount - 1.
        var set = MetadataTableProjector.FindReferences(
            peReader, TableIndex.TypeDef, typeDef.RowCount, int.MaxValue);

        Assert.True(set.TargetExists);
    }

    /// <summary>
    /// The <c>TypeRef</c> row the image points at most (in practice
    /// <c>System.Object</c>). Derived rather than hardcoded, because which row id
    /// is popular is an artifact of how this test assembly happens to be
    /// compiled, not something a test should assume.
    /// </summary>
    static int MostReferencedTypeRef(MetadataTableProjection projection)
    {
        var counts = new Dictionary<int, int>();
        foreach (var table in projection.Tables)
        {
            foreach (var row in table.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    if (cell is MetadataValue.Handle { Reference.TargetTable: TableIndex.TypeRef } handle)
                        counts[handle.Reference.TargetRowId] = counts.GetValueOrDefault(handle.Reference.TargetRowId) + 1;
                }
            }
        }

        Assert.NotEmpty(counts);
        return counts.MaxBy(pair => pair.Value).Key;
    }

    [Fact]
    public void FindReferences_MarksTruncation_WhenTheBudgetStopsTheScan()
    {
        using var peReader = OpenSelfFromBytes();
        int popular = MostReferencedTypeRef(FullProjection(peReader));

        var full = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeRef, popular, int.MaxValue);
        Assert.True(full.References.Length > 1, $"Expected TypeRef[{popular}] to be referenced more than once.");

        var capped = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeRef, popular, 1);

        Assert.Single(capped.References);
        Assert.True(capped.Truncated);
        Assert.False(capped.IsComplete, "A stopped scan must never claim completeness.");
        Assert.Equal(full.References[0], capped.References[0]);
    }

    [Fact]
    public void FindReferences_WithAZeroBudget_StillReportsThatItStopped()
    {
        using var peReader = OpenSelfFromBytes();
        int popular = MostReferencedTypeRef(FullProjection(peReader));

        var set = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeRef, popular, 0);

        Assert.Empty(set.References);
        // Zero results plus Truncated is the honest encoding of "there are
        // references, you just asked for none of them".
        Assert.True(set.Truncated);
        Assert.False(set.IsComplete);
    }

    [Fact]
    public void FindReferences_OrdersResultsByTableThenRow()
    {
        using var peReader = OpenSelfFromBytes();
        int popular = MostReferencedTypeRef(FullProjection(peReader));

        var set = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeRef, popular, int.MaxValue);
        Assert.True(set.References.Length > 1, "Expected more than one reference to order.");

        var keys = set.References.Select(r => ((int)r.Source.Table, r.Source.RowId)).ToArray();
        Assert.Equal(keys.OrderBy(k => k.Item1).ThenBy(k => k.Item2).ToArray(), keys);
    }

    [Fact]
    public void FindReferences_ReportsNoBlindSpots_ForAWellFormedImage()
    {
        using var peReader = OpenSelfFromBytes();

        var set = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeDef, 1, int.MaxValue);

        Assert.Empty(set.UnreadableRows);
    }

    /// <summary>
    /// The projection models a subset of ECMA-335's tables, and a real assembly
    /// populates tables outside that subset. The scan never visits them, so an
    /// edge living in one is invisible — and that must be reported rather than
    /// folded into an empty or "complete" answer.
    /// </summary>
    [Fact]
    public void FindReferences_ReportsPopulatedUnmodelledTables_AsABlindSpot()
    {
        using var peReader = OpenSelfFromBytes();

        var set = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeDef, 1, int.MaxValue);

        AssertScanRanClean(set);
        Assert.NotEmpty(set.UnscannedTables);
        Assert.False(
            set.IsComplete,
            "A scan that left a populated table unread, in whole or in part, has not covered the whole image.");
    }

    /// <summary>
    /// An independent restatement of the tables the projection models, spelled
    /// out rather than read back from <c>SupportedTables</c>. Deriving the
    /// expectation from the product would make the exactness check below
    /// tautological: it would agree with any table list, including a wrong one.
    /// Growing the projection is expected to fail this list — that is the point,
    /// because the blind-spot report must move in lockstep with coverage.
    /// </summary>
    static readonly ImmutableArray<TableIndex> ModelledTables =
    [
        TableIndex.Module,
        TableIndex.TypeRef,
        TableIndex.TypeDef,
        TableIndex.Field,
        TableIndex.MethodDef,
        TableIndex.Param,
        TableIndex.MemberRef,
        TableIndex.Constant,
        TableIndex.CustomAttribute,
        TableIndex.StandAloneSig,
        TableIndex.MethodImpl,
        TableIndex.TypeSpec,
        TableIndex.Assembly,
        TableIndex.AssemblyRef,
        TableIndex.ExportedType,
        TableIndex.GenericParam,
        TableIndex.MethodSpec,
    ];

    /// <summary>
    /// The blind spot must be exactly the populated tables the projection does
    /// not model — no modelled table, and no empty table. Over-reporting would
    /// scare a caller off a result the search actually did cover;
    /// under-reporting is the original bug.
    /// </summary>
    [Fact]
    public void FindReferences_UnscannedTables_AreExactlyThePopulatedUnmodelledOnes()
    {
        using var peReader = OpenSelfFromBytes();
        var reader = peReader.GetMetadataReader();

        // Guard the restatement itself: if the projection's coverage moves, this
        // fires first and names the drift, rather than the exactness assertion
        // failing for a reason the reader has to reverse-engineer.
        Assert.Equal(
            ModelledTables.Order().ToArray(),
            FullProjection(peReader).Tables.Select(t => t.Index).Order().ToArray());

        var modelled = ModelledTables.ToHashSet();
        var expected = Enum.GetValues<TableIndex>()
            .Where(t => !modelled.Contains(t) && reader.GetTableRowCount(t) > 0)
            .ToArray();

        var set = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeDef, 1, int.MaxValue);

        Assert.NotEmpty(expected);
        Assert.Equal(expected, set.UnscannedTables);
        Assert.All(set.UnscannedTables, t => Assert.DoesNotContain(t, modelled));
        Assert.All(set.UnscannedTables, t => Assert.True(reader.GetTableRowCount(t) > 0));
    }

    /// <summary>
    /// A budget that stops the scan leaves modelled tables unreached, and those
    /// are unscanned in exactly the sense the report means: the search did not
    /// look. Deriving the blind spot from a static list of what the scan
    /// *intends* to visit would miss them and under-report — the same failure
    /// as the unmodelled-table gap, at a smaller scale.
    /// </summary>
    /// <summary>
    /// Entering a table is not searching it. When the budget stops the scan
    /// part-way through a table, the rows after the stop were never read and an
    /// edge could sit in any of them — so that table must stay a blind spot
    /// rather than count as covered on the strength of having been started.
    /// </summary>
    [Fact]
    public void FindReferences_TableStoppedPartWayThrough_IsNotCountedAsSearched()
    {
        using var peReader = OpenSelfFromBytes();
        var reader = peReader.GetMetadataReader();
        int popular = MostReferencedTypeRef(FullProjection(peReader));

        var capped = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeRef, popular, 1);
        Assert.True(capped.Truncated, "Expected the budget to stop this scan.");

        // The table holding the one reference we kept is where the scan stopped.
        var stoppedIn = Assert.Single(capped.References).Source.Table;

        // It is modelled, so the only reason it can appear as unscanned is that
        // the scan did not finish it.
        Assert.Contains(stoppedIn, ModelledTables);
        Assert.True(
            reader.GetTableRowCount(stoppedIn) > 1,
            "Need a multi-row table for 'stopped part-way' to be meaningful.");
        Assert.Contains(stoppedIn, capped.UnscannedTables);

        // The same table is fully searched when nothing stops the scan, so the
        // blind spot is evidence of the stop and not a permanent gap.
        var full = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeRef, popular, int.MaxValue);
        Assert.DoesNotContain(stoppedIn, full.UnscannedTables);
    }

    [Fact]
    public void FindReferences_StoppedOnATableSLastRow_IsStillNotCountedAsSearched()
    {
        // The budget is checked inside the column loop, so a scan can enter the
        // final row of a table and abandon it part way through its columns. The
        // row counter still reaches RowCount + 1 on the way out, which looks
        // exactly like an ordinary completed table. Truncation is the only thing
        // that separates the two.
        using var peReader = OpenSelfFromBytes();
        var reader = peReader.GetMetadataReader();
        int popular = MostReferencedTypeRef(FullProjection(peReader));

        var full = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeRef, popular, int.MaxValue);
        Assert.False(full.Truncated);

        // Find a match that sits on the last row of a multi-row table. Budgeting
        // the scan to the matches before it makes that row the one the budget
        // trips on, so the scan stops inside the table's final row.
        int budget = -1;
        TableIndex stoppedIn = default;
        for (int i = 0; i < full.References.Length; i++)
        {
            var source = full.References[i].Source;
            int rowCount = reader.GetTableRowCount(source.Table);
            if (source.RowId == rowCount && rowCount > 1)
            {
                budget = i;
                stoppedIn = source.Table;
                break;
            }
        }

        Assert.True(budget >= 0, "No match sat on the last row of a multi-row table.");

        var capped = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeRef, popular, budget);
        Assert.True(capped.Truncated, "Expected the budget to stop this scan.");
        Assert.Equal(budget, capped.References.Length);

        // The last row was entered but never finished, so its columns after the
        // stop were never compared against the target.
        Assert.Contains(stoppedIn, ModelledTables);
        Assert.Contains(stoppedIn, capped.UnscannedTables);

        // Nothing stops the full scan, so the same table is searched there. That
        // is what makes the blind spot evidence of the stop rather than a gap in
        // what the projection models.
        Assert.DoesNotContain(stoppedIn, full.UnscannedTables);
    }

    [Fact]
    public void FindReferences_StoppedOnATableSVeryLastCell_IsStillCountedAsSearched()
    {
        // The counterpart to the test above, and the boundary between them. If
        // the budget trips on the *last column of the last row*, every cell the
        // table has was examined, so the table really was searched in full even
        // though the scan ended inside it. Reporting it unscanned would be a
        // false blind spot: it would claim an unread row could hide an edge when
        // no row went unread.
        using var peReader = OpenSelfFromBytes();
        var reader = peReader.GetMetadataReader();

        int typeDefRows = reader.GetTableRowCount(TableIndex.TypeDef);
        int lastColumn = FullProjection(peReader)
            .Tables.Single(t => t.Index == TableIndex.TypeDef)
            .Columns.Length - 1;

        // TypeDef's last column is its MethodList run, so a method owned by the
        // last TypeDef row is pointed at from exactly that cell.
        var lastType = reader.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle(typeDefRows));
        int methodRid = MetadataTokens.GetRowNumber(lastType.GetMethods().First());

        var full = MetadataTableProjector.FindReferences(peReader, TableIndex.MethodDef, methodRid, int.MaxValue);
        int budget = -1;
        for (int i = 0; i < full.References.Length; i++)
        {
            var reference = full.References[i];
            if (reference.Source.Table == TableIndex.TypeDef
                && reference.Source.RowId == typeDefRows
                && reference.ColumnIndex == lastColumn)
            {
                budget = i;
                break;
            }
        }

        Assert.True(budget >= 0, "Expected the last TypeDef row's final column to point at its own method.");

        var capped = MetadataTableProjector.FindReferences(peReader, TableIndex.MethodDef, methodRid, budget);
        Assert.True(capped.Truncated, "Expected the budget to stop this scan.");

        // Stopped inside TypeDef, yet TypeDef is fully searched.
        Assert.DoesNotContain(TableIndex.TypeDef, capped.UnscannedTables);
    }

    [Fact]
    public void FindReferences_TruncatedScan_ReportsTheTablesItNeverReached()
    {
        using var peReader = OpenSelfFromBytes();
        var reader = peReader.GetMetadataReader();
        int popular = MostReferencedTypeRef(FullProjection(peReader));

        // One result is enough to stop the scan almost immediately, leaving most
        // of the modelled tables unvisited.
        var capped = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeRef, popular, 1);
        Assert.True(capped.Truncated, "Expected the budget to stop this scan.");

        var full = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeRef, popular, int.MaxValue);
        Assert.False(full.Truncated);

        // Stopping early can only widen the blind spot, never narrow it.
        var cappedBlind = capped.UnscannedTables.ToHashSet();
        var fullBlind = full.UnscannedTables.ToHashSet();
        Assert.True(
            cappedBlind.IsSupersetOf(fullBlind),
            $"Stopped scan lost blind spots the full scan reported: {string.Join(", ", fullBlind.Except(cappedBlind))}");
        Assert.True(
            cappedBlind.Count > fullBlind.Count,
            "A stopped scan left modelled tables unreached, so it must report more of them.");

        // Every extra entry must be a modelled table the full scan did reach —
        // that is what makes it evidence of truncation rather than noise.
        foreach (var table in cappedBlind.Except(fullBlind))
        {
            Assert.Contains(table, ModelledTables);
            Assert.True(reader.GetTableRowCount(table) > 0);
        }
    }

    /// <summary>
    /// The blind spot is not hypothetical. <c>NestedClass</c> is the only place a
    /// nested type's declaring type is recorded, and the projection does not
    /// model it, so the search silently misses that edge today. This test pins
    /// the miss *and* the disclosure together: if the projection later covers
    /// <c>NestedClass</c>, the edge must appear and the table must drop off the
    /// blind-spot list at the same time.
    /// </summary>
    [Fact]
    public void FindReferences_MissesTheDeclaringTypeOfANestedType_AndDisclosesWhy()
    {
        using var peReader = OpenSelfFromBytes();
        var reader = peReader.GetMetadataReader();

        // Independent oracle: ask SRM which TypeDef is nested, rather than
        // hardcoding a row id that recompiling this assembly would move.
        int nestedRowId = reader.TypeDefinitions
            .Where(h => reader.GetTypeDefinition(h).IsNested)
            .Select(h => MetadataTokens.GetRowNumber((EntityHandle)h))
            .First();

        var set = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeDef, nestedRowId, int.MaxValue);

        AssertScanRanClean(set);
        Assert.DoesNotContain(set.References, r => r.Source.Table == TableIndex.NestedClass);

        if (set.UnscannedTables.Contains(TableIndex.NestedClass))
            Assert.False(set.IsComplete, "The missed edge must not be hidden behind a complete result.");
        else
            Assert.Contains(set.References, r => r.Source.Table == TableIndex.NestedClass);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FindReferences_RejectsANonPositiveTargetRowId(int rowId)
    {
        using var peReader = OpenSelfFromBytes();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => MetadataTableProjector.FindReferences(peReader, TableIndex.TypeDef, rowId));
    }

    [Fact]
    public void FindReferences_RejectsANegativeBudget()
    {
        using var peReader = OpenSelfFromBytes();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => MetadataTableProjector.FindReferences(peReader, TableIndex.TypeDef, 1, -1));
    }

    [Fact]
    public void RowLocation_ComposesTheSameTokenTheProjectionAssignsToARow()
    {
        using var peReader = OpenSelfFromBytes();
        var row = Assert.Single(MetadataTableProjector.ProjectRow(peReader, TableIndex.TypeDef, 3)!.Rows);

        Assert.Equal(row.Token, new MetadataRowLocation(TableIndex.TypeDef, 3).Token);
    }

    [Fact]
    public void RowLocation_RejectsANonPositiveRowId()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new MetadataRowLocation(TableIndex.TypeDef, 0));

    [Fact]
    public void Session_MetadataReferencesFacet_MatchesTheProjectorDirectly()
    {
        // The session's contract is that callers never touch a PEReader, so the
        // facet must be a real alternative to the projector, not a thinner view.
        using var peReader = OpenSelfFromBytes();
        var direct = MetadataTableProjector.FindReferences(peReader, TableIndex.Field, 1);

        using var session = AssemblyInspectionSession.Open(SelfPath);
        var viaSession = session.MetadataReferences(TableIndex.Field, 1);

        Assert.Equal(direct.References.Length, viaSession.References.Length);
        Assert.Equal(
            direct.References.Select(r => (r.Source.Table, r.Source.RowId, r.ColumnName, r.Kind)),
            viaSession.References.Select(r => (r.Source.Table, r.Source.RowId, r.ColumnName, r.Kind)));
        Assert.Equal(direct.Target.Token, viaSession.Target.Token);
        // Comparing IsComplete alone would be vacuous: both are false on a real
        // assembly. Compare the blind spots themselves, so the facet cannot
        // silently report a different search than the projector ran.
        Assert.Equal(direct.UnscannedTables, viaSession.UnscannedTables);
        Assert.Equal(direct.UnreadableRows, viaSession.UnreadableRows);
        Assert.Equal(direct.Truncated, viaSession.Truncated);
        Assert.Equal(direct.IsComplete, viaSession.IsComplete);
        AssertScanRanClean(viaSession);
    }

    [Fact]
    public void Session_MetadataReferencesFacet_HonorsTheBudget()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        var bounded = session.MetadataReferences(TableIndex.Field, 1, maxReferences: 1);

        Assert.Single(bounded.References);
        Assert.True(bounded.Truncated);
        Assert.False(bounded.IsComplete);
    }

    [Fact]
    public void Session_MetadataTableRowFacet_MatchesTheProjectorDirectly()
    {
        using var peReader = OpenSelfFromBytes();
        var direct = MetadataTableProjector.ProjectRow(peReader, TableIndex.TypeDef, 3);

        using var session = AssemblyInspectionSession.Open(SelfPath);
        var viaSession = session.MetadataTableRow(TableIndex.TypeDef, 3);

        Assert.NotNull(direct);
        Assert.NotNull(viaSession);
        Assert.Equal(direct!.RowCount, viaSession!.RowCount);
        Assert.Equal(Assert.Single(direct.Rows).Token, Assert.Single(viaSession.Rows).Token);
    }

    [Fact]
    public void Session_MetadataTableRowFacet_ReturnsNullPastTheEndOfATable()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);

        Assert.Null(session.MetadataTableRow(TableIndex.TypeDef, int.MaxValue));
    }

    /// <summary>
    /// A synthetic image every one of whose populated tables the projection
    /// models, with no malformed cell anywhere.
    ///
    /// This is what makes it useful: on a real assembly a scan is incomplete for
    /// several reasons at once, so an <c>IsComplete == false</c> assertion there
    /// passes no matter which reason produced it. Here a clean scan is genuinely
    /// available, so target existence can be isolated as the only thing left
    /// that can take it away.
    /// </summary>
    static byte[] BuildMinimalCleanImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Clean.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Clean"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);

        // The <Module> pseudo-type must be row 1.
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Solo"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var rootBuilder = new MetadataRootBuilder(metadata, suppressValidation: true);
        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            rootBuilder,
            ilStream: new BlobBuilder());
        var image = new BlobBuilder();
        peBuilder.Serialize(image);
        return image.ToArray();
    }

    [Fact]
    public void FindReferences_MissingTargetRow_IsTheOnlyThingWrongWithAnOtherwiseCleanScan()
    {
        using var peReader = new PEReader(new MemoryStream(BuildMinimalCleanImage()));
        var reader = peReader.GetMetadataReader();
        int rowCount = reader.GetTableRowCount(TableIndex.TypeDef);

        // A row that is there. Every other completeness condition holds on this
        // image, so the scan is complete — which is what licenses the comparison
        // below to attribute the difference to target existence alone.
        var present = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeDef, rowCount);
        Assert.True(
            present.IsComplete,
            "This fixture exists to offer a genuinely complete scan; without one the test below proves nothing.");

        // The same image, the same scan, one row further on.
        var absent = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeDef, rowCount + 1);
        Assert.False(absent.Truncated);
        Assert.Empty(absent.UnreadableRows);
        Assert.Empty(absent.UnscannedTables);
        Assert.False(absent.TargetExists);

        // So an empty result here cannot be reported as a trustworthy "nothing
        // points at this row": there is no row for anything to point at.
        Assert.Empty(absent.References);
        Assert.False(absent.IsComplete);
    }

    /// <summary>
    /// A synthetic image whose <c>TypeDef[2].Extends</c> names a <c>TypeRef</c>
    /// row that does not exist, and whose <c>Module[1].Mvid</c> points past the
    /// end of the GUID heap.
    ///
    /// Real compilers do not emit either, which is exactly why a blind spot here
    /// has to be constructed: the cell readers contain both failures as
    /// <see cref="MetadataValue.Malformed"/> rather than throwing, so the row
    /// reads successfully and would otherwise pass as fully searched.
    /// </summary>
    static byte[] BuildImageWithMalformedCells()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Synthetic.dll"),
            // A broken cell in a Heap column, which was never an edge: the GUID
            // heap holds no such index.
            mvid: MetadataTokens.GuidHandle(9999),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);

        // The <Module> pseudo-type must be row 1.
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        // Row 2: a dangling edge in a Handle column.
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("DanglingExtends"),
            baseType: MetadataTokens.TypeReferenceHandle(9999),
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));


        // One method, so TypeDef[2].MethodList is a real run rather than an
        // empty one. TypeDef[1] and TypeDef[2] both start at MethodDef row 1, so
        // TypeDef[1]'s run is empty and TypeDef[2]'s is [1, 2).
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(0, r => r.Void(), _ => { });
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Abstract,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: -1,
            parameterList: MetadataTokens.ParameterHandle(1));

        var rootBuilder = new MetadataRootBuilder(metadata, suppressValidation: true);
        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            rootBuilder,
            ilStream: new BlobBuilder());
        var image = new BlobBuilder();
        peBuilder.Serialize(image);
        return image.ToArray();
    }

    [Fact]
    public void MalformedCells_AreOnlyProducedWhereThisFixtureIntends()
    {
        // Guards the two tests below: if SRM ever stops producing a malformed
        // cell here, they would silently become vacuous.
        using var peReader = new PEReader(new MemoryStream(BuildImageWithMalformedCells()));
        var projection = FullProjection(peReader);

        var typeDefs = Assert.Single(projection.Tables, t => t.Index == TableIndex.TypeDef);
        Assert.Equal(2, typeDefs.Rows.Length);
        int extends = Array.FindIndex(typeDefs.Columns.ToArray(), c => c.Kind == MetadataColumnKind.Handle);
        Assert.IsType<MetadataValue.Malformed>(typeDefs.Rows[1].Cells[extends]);

        // The Heap-column counterpart, which must not be treated as a lost edge.
        var modules = Assert.Single(projection.Tables, t => t.Index == TableIndex.Module);
        int mvid = Array.FindIndex(modules.Columns.ToArray(), c => c.Name == "Mvid");
        Assert.Equal(MetadataColumnKind.Heap, modules.Columns[mvid].Kind);
        Assert.IsType<MetadataValue.Malformed>(modules.Rows[0].Cells[mvid]);
    }

    [Fact]
    public void FindReferences_MalformedEdgeColumn_IsReportedAsABlindSpot()
    {
        using var peReader = new PEReader(new MemoryStream(BuildImageWithMalformedCells()));
        var set = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeRef, 1);

        // The Extends cell may or may not have been an edge onto TypeRef[1]; the
        // search cannot tell, so it must not answer "nothing points here".
        Assert.Empty(set.References);
        Assert.False(set.IsComplete);

        var blind = Assert.Single(set.UnreadableRows);
        Assert.Equal(TableIndex.TypeDef, blind.Table);
        Assert.Equal(2, blind.RowId);
    }

    [Fact]
    public void FindReferences_MalformedHeapColumn_IsNotABlindSpot()
    {
        using var peReader = new PEReader(new MemoryStream(BuildImageWithMalformedCells()));
        var set = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeRef, 1);

        // Module[1].Mvid is a Heap column. It was never an edge, so it cannot
        // hide a reference and must not be reported as one lost.
        Assert.DoesNotContain(set.UnreadableRows, r => r.Table == TableIndex.Module);
    }

    [Fact]
    public void FindReferences_MalformedEdge_StillReportsTheGoodEdgesOnTheSameRow()
    {
        using var peReader = new PEReader(new MemoryStream(BuildImageWithMalformedCells()));

        // TypeDef[2] has a broken Extends *and* a valid MethodList run. The row
        // being a blind spot must not cost the caller the edges it does have.
        var set = MetadataTableProjector.FindReferences(peReader, TableIndex.MethodDef, 1);

        Assert.Contains(
            set.References,
            r => r.Source.Table == TableIndex.TypeDef && r.Source.RowId == 2 && r.Kind == MetadataRowReferenceKind.Range);
        Assert.Single(set.UnreadableRows);
    }

    [Fact]
    public void FindReferences_WellFormedImage_ReportsNoUnreadableRows()
    {
        // The negative case for the check above: a real assembly must not start
        // reporting unreadable rows just because malformed edge cells are now
        // tracked.
        using var peReader = OpenSelfFromBytes();

        foreach (var table in new[] { TableIndex.TypeDef, TableIndex.MethodDef, TableIndex.Field, TableIndex.TypeRef })
        {
            var set = MetadataTableProjector.FindReferences(peReader, table, 1);
            AssertScanRanClean(set);
        }
    }

    /// <summary>
    /// The invariant the malformed-edge gate rests on: an edge cell only ever
    /// appears in a column declared as an edge column. If a column that can
    /// produce a <see cref="MetadataValue.Handle"/> or
    /// <see cref="MetadataValue.Range"/> were declared <c>Heap</c>, <c>Scalar</c>,
    /// or <c>Flags</c>, a malformed cell there would be a lost edge the search
    /// silently ignores — the exact bug the gate exists to prevent.
    /// </summary>
    [Fact]
    public void EdgeCells_OnlyEverAppearInEdgeColumns()
    {
        using var peReader = OpenSelfFromBytes();
        var projection = FullProjection(peReader);

        int inspected = 0;
        foreach (var table in projection.Tables)
        {
            foreach (var row in table.Rows)
            {
                for (int column = 0; column < row.Cells.Length; column++)
                {
                    var kind = table.Columns[column].Kind;
                    inspected++;

                    if (row.Cells[column] is MetadataValue.Handle)
                        Assert.Equal(MetadataColumnKind.Handle, kind);

                    if (row.Cells[column] is MetadataValue.Range)
                        Assert.Equal(MetadataColumnKind.HandleRange, kind);
                }
            }
        }

        // Guards against the loop above passing because it saw nothing.
        Assert.True(inspected > 10_000, $"only {inspected} cells inspected");
    }

    /// <summary>
    /// The converse: every column kind the gate treats as an edge column is
    /// actually reachable in real metadata, so the gate is not written against
    /// kinds that never occur.
    /// </summary>
    [Fact]
    public void BothEdgeColumnKinds_OccurInRealMetadata()
    {
        using var peReader = OpenSelfFromBytes();
        var projection = FullProjection(peReader);

        var kinds = projection.Tables
            .SelectMany(t => t.Columns)
            .Select(c => c.Kind)
            .ToHashSet();

        Assert.Contains(MetadataColumnKind.Handle, kinds);
        Assert.Contains(MetadataColumnKind.HandleRange, kinds);
    }
}