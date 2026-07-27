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

        Assert.True(set.IsComplete, "Expected a complete scan over a well-formed image.");
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

    [Fact]
    public void FindReferences_ForARowNothingPointsAt_ReportsACompleteEmptyResult()
    {
        using var peReader = OpenSelfFromBytes();
        var typeDef = FullProjection(peReader).Tables.Single(t => t.Index == TableIndex.TypeDef);

        // A row id past the end of the table cannot be referenced: a handle that
        // far out already projects as Malformed rather than a resolvable edge.
        var set = MetadataTableProjector.FindReferences(peReader, TableIndex.TypeDef, typeDef.RowCount + 1_000, int.MaxValue);

        Assert.Empty(set.References);
        Assert.True(set.IsComplete, "An empty result must be distinguishable from a stopped or blind scan.");
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
        Assert.True(viaSession.IsComplete);
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
    public void FindReferences_WellFormedImage_ReportsNoBlindSpots()
    {
        // The negative case for the check above: a real assembly must not start
        // reporting blind spots just because malformed edge cells are now tracked.
        using var peReader = OpenSelfFromBytes();

        foreach (var table in new[] { TableIndex.TypeDef, TableIndex.MethodDef, TableIndex.Field, TableIndex.TypeRef })
        {
            var set = MetadataTableProjector.FindReferences(peReader, table, 1);
            Assert.Empty(set.UnreadableRows);
            Assert.True(set.IsComplete);
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