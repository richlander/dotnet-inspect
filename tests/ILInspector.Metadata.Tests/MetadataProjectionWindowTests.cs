using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Tests for the projection's random-access surface: the per-table row window
/// (<see cref="MetadataProjectionOptions.StartRowId"/> plus
/// <see cref="MetadataProjectionOptions.MaxRowsPerTable"/>), the on-demand
/// single-row lookup <see cref="MetadataTableProjector.ProjectRow"/>, and the
/// filesystem-free <see cref="MetadataTableProjector.Project"/> entry point.
///
/// These are the seams a browser host needs (see issue #3341): it cannot afford
/// to materialize a large table whole, cannot reach a handle target that falls
/// outside the current window without re-reading the table, and has no
/// filesystem to open the image from.
/// </summary>
public class MetadataProjectionWindowTests
{
    static string SelfPath => typeof(MetadataProjectionWindowTests).Assembly.Location;

    /// <summary>
    /// Opens this assembly through an in-memory stream. The bytes are read here
    /// once; the projector itself is only ever handed a <see cref="PEReader"/>,
    /// never a path, which is what makes it usable from a browser host.
    /// </summary>
    static PEReader OpenSelfFromBytes() => new(new MemoryStream(File.ReadAllBytes(SelfPath)));

    static MetadataTableView Window(TableIndex index, int startRowId, int maxRows)
    {
        using var peReader = OpenSelfFromBytes();
        var projection = MetadataTableProjector.Project(
            peReader,
            new MetadataProjectionOptions
            {
                Tables = [index],
                StartRowId = startRowId,
                MaxRowsPerTable = maxRows,
            });

        return Assert.Single(projection.Tables, table => table.Index == index);
    }

    static MetadataTableView FullTable(TableIndex index)
    {
        using var peReader = OpenSelfFromBytes();
        var projection = MetadataTableProjector.Project(
            peReader,
            new MetadataProjectionOptions { Tables = [index], MaxRowsPerTable = int.MaxValue });

        return Assert.Single(projection.Tables, table => table.Index == index);
    }

    [Fact]
    public void Project_ReadsFromBytes_WithoutBeingGivenAPath()
    {
        using var peReader = OpenSelfFromBytes();
        var projection = MetadataTableProjector.Project(peReader);

        Assert.NotEmpty(projection.Tables);
        Assert.Contains(projection.Tables, table => table.Index == TableIndex.TypeDef);
    }

    [Fact]
    public void RowWindow_PagesCoverEveryRowExactlyOnce_InOrder()
    {
        var full = FullTable(TableIndex.TypeDef);
        Assert.True(full.RowCount > 4, "Expected the test assembly to define more than four types.");

        const int PageSize = 3;
        var paged = ImmutableArray.CreateBuilder<int>();
        for (int start = 1; start <= full.RowCount; start += PageSize)
        {
            foreach (var row in Window(TableIndex.TypeDef, start, PageSize).Rows)
                paged.Add(row.RowId);
        }

        Assert.Equal(full.Rows.Select(row => row.RowId), paged);
    }

    [Fact]
    public void RowWindow_ReportsAbsoluteRowIds_AndThePhysicalRowCount()
    {
        var full = FullTable(TableIndex.TypeDef);
        var second = Window(TableIndex.TypeDef, startRowId: 3, maxRows: 2);

        Assert.Equal([3, 4], second.Rows.Select(row => row.RowId));
        Assert.Equal(full.RowCount, second.RowCount);
    }

    [Fact]
    public void RowWindow_ProjectsIdenticalCells_ToTheUnwindowedProjection()
    {
        var full = FullTable(TableIndex.TypeDef);
        var windowed = Window(TableIndex.TypeDef, startRowId: 3, maxRows: 2);

        // A window changes coverage, never content.
        Assert.Equal(full.Columns, windowed.Columns);
        foreach (var row in windowed.Rows)
        {
            var expected = full.Rows.Single(candidate => candidate.RowId == row.RowId);
            Assert.Equal(expected.Token, row.Token);
            Assert.Equal(expected.Cells, row.Cells);
        }
    }

    [Fact]
    public void RowWindow_MarksPartialCoverage_Explicitly()
    {
        var full = FullTable(TableIndex.TypeDef);
        var windowed = Window(TableIndex.TypeDef, startRowId: 2, maxRows: 2);

        Assert.NotNull(windowed.Truncation);
        Assert.Equal(2, windowed.Truncation!.ProjectedRows);
        Assert.Equal(full.RowCount, windowed.Truncation.RowCount);
    }

    [Fact]
    public void RowWindow_CoveringTheWholeTable_ReportsNoTruncation()
    {
        var full = FullTable(TableIndex.TypeDef);
        var windowed = Window(TableIndex.TypeDef, startRowId: 1, maxRows: full.RowCount);

        Assert.Null(windowed.Truncation);
        Assert.Equal(full.RowCount, windowed.Rows.Length);
    }

    [Fact]
    public void RowWindow_PastTheEnd_KeepsTheTableVisible_WithZeroRows()
    {
        var full = FullTable(TableIndex.TypeDef);

        // Dropping the table here would report a populated table as absent.
        var beyond = Window(TableIndex.TypeDef, startRowId: full.RowCount + 1, maxRows: 16);

        Assert.Empty(beyond.Rows);
        Assert.Equal(full.RowCount, beyond.RowCount);
        Assert.NotNull(beyond.Truncation);
        Assert.Equal(0, beyond.Truncation!.ProjectedRows);
        Assert.Equal(full.RowCount, beyond.Truncation.RowCount);
        Assert.Equal(full.Columns, beyond.Columns);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void RowWindow_ClampsAStartRowIdBelowOne(int startRowId)
    {
        var fromOne = Window(TableIndex.TypeDef, startRowId: 1, maxRows: 2);
        var clamped = Window(TableIndex.TypeDef, startRowId, maxRows: 2);

        Assert.Equal(fromOne.Rows.Select(row => row.RowId), clamped.Rows.Select(row => row.RowId));
    }

    [Fact]
    public void RowWindow_WithAStartBeyondIntRange_DoesNotWrapIntoTheTable()
    {
        // start + budget - 1 must be widened before the comparison, or the window
        // overflows to a negative end and silently overlaps real rows.
        var overflowing = Window(TableIndex.TypeDef, startRowId: int.MaxValue, maxRows: int.MaxValue);

        Assert.Empty(overflowing.Rows);
        Assert.True(overflowing.RowCount > 0);
    }

    [Fact]
    public void RowWindow_WithAZeroBudget_ProjectsNothing()
    {
        var empty = Window(TableIndex.TypeDef, startRowId: 1, maxRows: 0);

        Assert.Empty(empty.Rows);
        Assert.NotNull(empty.Truncation);
        Assert.Equal(0, empty.Truncation!.ProjectedRows);
    }

    [Fact]
    public void ProjectRow_ReadsARow_OutsideTheCurrentWindow()
    {
        var full = FullTable(TableIndex.TypeDef);
        int lastRowId = full.RowCount;
        Assert.True(lastRowId > 1, "Expected more than one TypeDef row.");

        // The caller's window covers only row 1; the lookup must not be bounded by it.
        var window = Window(TableIndex.TypeDef, startRowId: 1, maxRows: 1);
        Assert.DoesNotContain(window.Rows, row => row.RowId == lastRowId);

        using var peReader = OpenSelfFromBytes();
        var view = MetadataTableProjector.ProjectRow(peReader, TableIndex.TypeDef, lastRowId);

        Assert.NotNull(view);
        var row = Assert.Single(view!.Rows);
        Assert.Equal(lastRowId, row.RowId);

        // The schema and the table's real size travel with the row.
        Assert.Equal(full.Columns, view.Columns);
        Assert.Equal(full.RowCount, view.RowCount);
    }

    [Fact]
    public void ProjectRow_MatchesTheSameRow_FromAFullProjection()
    {
        var full = FullTable(TableIndex.MemberRef);
        var expected = full.Rows[^1];

        using var peReader = OpenSelfFromBytes();
        var view = MetadataTableProjector.ProjectRow(peReader, TableIndex.MemberRef, expected.RowId);

        var actual = Assert.Single(view!.Rows);
        Assert.Equal(expected.Token, actual.Token);
        Assert.Equal(expected.Cells, actual.Cells);
    }

    [Fact]
    public void ProjectRow_FollowsAHandleRef_ToItsTargetRow()
    {
        using var peReader = OpenSelfFromBytes();
        var projection = MetadataTableProjector.Project(peReader);

        // The click-through path: pick any resolvable handle edge and land on its row.
        var edge = projection.Tables
            .SelectMany(table => table.Rows)
            .SelectMany(row => row.Cells)
            .OfType<MetadataValue.Handle>()
            .Select(handle => handle.Reference)
            .First(reference => reference.TargetRowId >= 1
                && reference.TargetTable is TableIndex.TypeDef or TableIndex.TypeRef or TableIndex.AssemblyRef);

        var view = MetadataTableProjector.ProjectRow(peReader, edge.TargetTable, edge.TargetRowId);

        Assert.NotNull(view);
        Assert.Equal(edge.TargetTable, view!.Index);
        var row = Assert.Single(view.Rows);
        Assert.Equal(edge.TargetRowId, row.RowId);
        Assert.Equal(edge.Token, row.Token);
    }

    [Fact]
    public void ProjectRow_ReturnsNull_ForARowIdPastTheEnd()
    {
        var full = FullTable(TableIndex.TypeDef);

        using var peReader = OpenSelfFromBytes();

        Assert.Null(MetadataTableProjector.ProjectRow(peReader, TableIndex.TypeDef, full.RowCount + 1));
    }

    [Fact]
    public void ProjectRow_ReturnsNull_ForATableTheProjectionDoesNotCover()
    {
        using var peReader = OpenSelfFromBytes();

        Assert.Null(MetadataTableProjector.ProjectRow(peReader, TableIndex.InterfaceImpl, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ProjectRow_RejectsANonPositiveRowId(int rowId)
    {
        using var peReader = OpenSelfFromBytes();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => MetadataTableProjector.ProjectRow(peReader, TableIndex.TypeDef, rowId));
    }
}
