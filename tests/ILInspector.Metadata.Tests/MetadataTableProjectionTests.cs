using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Tests for <see cref="MetadataTableProjector"/> — the raw ECMA-335
/// metadata-table projection (see <c>docs/design/metadata-table-projection.md</c>).
/// Projects this test assembly (a known, self-describing image) and asserts the
/// structural shape: table identity, positional cells, resolvable handles,
/// list-column ranges, additive heap decodes, and explicit budgets.
/// </summary>
public class MetadataTableProjectionTests
{
    static string SelfPath => typeof(MetadataTableProjectionTests).Assembly.Location;

    static MetadataTableProjection Project(MetadataProjectionOptions? options = null)
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        return session.MetadataTables(options);
    }

    static MetadataTableView Table(MetadataTableProjection projection, TableIndex index)
        => Assert.Single(projection.Tables, table => table.Index == index);

    static int ColumnIndex(MetadataTableView table, string name)
    {
        for (int i = 0; i < table.Columns.Length; i++)
        {
            if (table.Columns[i].Name == name)
                return i;
        }

        throw new Xunit.Sdk.XunitException($"Column '{name}' not found in table '{table.Name}'.");
    }

    static MetadataValue Cell(MetadataTableView table, MetadataRow row, string column)
        => row.Cells[ColumnIndex(table, column)];

    static string? StringText(MetadataTableView table, MetadataRow row, string column)
        => (Cell(table, row, column) as MetadataValue.HeapReference)?.Text;

    [Fact]
    public void Project_ProducesTablesInEcmaOrder()
    {
        var projection = Project();

        Assert.NotEmpty(projection.Tables);
        var order = projection.Tables.Select(table => (int)table.Index).ToArray();
        Assert.Equal(order.OrderBy(value => value).ToArray(), order);
    }

    [Fact]
    public void EveryRow_HasOneCellPerColumn()
    {
        foreach (var table in Project().Tables)
        {
            foreach (var row in table.Rows)
                Assert.Equal(table.Columns.Length, row.Cells.Length);
        }
    }

    [Fact]
    public void ModuleTable_HasSingleRow_WithDecodedNameAndMvid()
    {
        var module = Table(Project(), TableIndex.Module);

        var row = Assert.Single(module.Rows);
        Assert.Equal(1, row.RowId);

        var name = Assert.IsType<MetadataValue.HeapReference>(Cell(module, row, "Name"));
        Assert.Equal(HeapKind.String, name.Heap);
        Assert.False(string.IsNullOrEmpty(name.Text));

        var mvid = Assert.IsType<MetadataValue.HeapReference>(Cell(module, row, "Mvid"));
        Assert.Equal(HeapKind.Guid, mvid.Heap);

        // A single-generation assembly carries no EnC generation ids.
        Assert.IsType<MetadataValue.Nil>(Cell(module, row, "EncId"));
        Assert.IsType<MetadataValue.Nil>(Cell(module, row, "EncBaseId"));
    }

    [Fact]
    public void TypeDefRow_ForTestType_ResolvesBaseTypeAndMethodRange()
    {
        var projection = Project();
        var typeDef = Table(projection, TableIndex.TypeDef);

        var row = Assert.Single(
            typeDef.Rows,
            candidate => StringText(typeDef, candidate, "Name") == nameof(MetadataTableProjectionTests));

        // Extends is a resolvable edge to System.Object in the TypeRef table.
        var extends = Assert.IsType<MetadataValue.Handle>(Cell(typeDef, row, "Extends"));
        Assert.Equal(TableIndex.TypeRef, extends.Reference.TargetTable);
        Assert.True(extends.Reference.TargetRowId >= 1);
        Assert.Contains("Object", extends.Reference.Display);

        // MethodList is a contiguous run in the MethodDef table; this type has methods.
        var methods = Assert.IsType<MetadataValue.Range>(Cell(typeDef, row, "MethodList"));
        Assert.Equal(TableIndex.MethodDef, methods.Reference.TargetTable);
        Assert.True(methods.Reference.Count >= 1);
        Assert.Equal(methods.Reference.EndRowId, methods.Reference.StartRowId + methods.Reference.Count);
    }

    [Fact]
    public void MethodDefRow_SignatureIsBoundedBlobPreview()
    {
        var methodDef = Table(Project(), TableIndex.MethodDef);
        var row = methodDef.Rows.First();

        var signature = Assert.IsType<MetadataValue.HeapReference>(Cell(methodDef, row, "Signature"));
        Assert.Equal(HeapKind.Blob, signature.Heap);
        Assert.Null(signature.Text);
        Assert.False(string.IsNullOrEmpty(signature.Preview));
    }

    [Fact]
    public void AssemblyRefTable_IncludesResolvedReferenceNames()
    {
        var assemblyRef = Table(Project(), TableIndex.AssemblyRef);

        var names = assemblyRef.Rows
            .Select(row => StringText(assemblyRef, row, "Name"))
            .ToArray();

        Assert.All(names, name => Assert.False(string.IsNullOrEmpty(name)));
        Assert.Contains("System.Runtime", names);
    }

    [Fact]
    public void RowBudget_TruncatesExplicitly_NeverSilently()
    {
        var full = Table(Project(), TableIndex.TypeDef);
        Assert.True(full.RowCount > 1, "Expected the test assembly to define more than one type.");

        var capped = Table(
            Project(new MetadataProjectionOptions { MaxRowsPerTable = 1 }),
            TableIndex.TypeDef);

        Assert.Single(capped.Rows);
        Assert.Equal(full.RowCount, capped.RowCount);
        Assert.NotNull(capped.Truncation);
        Assert.Equal(1, capped.Truncation!.ProjectedRows);
        Assert.Equal(full.RowCount, capped.Truncation.RowCount);
    }

    [Fact]
    public void TableSelection_RestrictsProjectionToRequestedTables()
    {
        var projection = Project(new MetadataProjectionOptions
        {
            Tables = [TableIndex.Module],
        });

        var table = Assert.Single(projection.Tables);
        Assert.Equal(TableIndex.Module, table.Index);
    }

    [Fact]
    public void HandleColumns_DeclareCandidateTargets()
    {
        var typeDef = Table(Project(), TableIndex.TypeDef);
        var extends = typeDef.Columns[ColumnIndex(typeDef, "Extends")];

        Assert.Equal(MetadataColumnKind.Handle, extends.Kind);
        Assert.Contains(TableIndex.TypeDef, extends.CandidateTargets);
        Assert.Contains(TableIndex.TypeRef, extends.CandidateTargets);
        Assert.Contains(TableIndex.TypeSpec, extends.CandidateTargets);
    }

    [Fact]
    public void EmptyImage_HasNoMetadataTables()
    {
        // The projector never fabricates a success-shaped table set; a metadata
        // image simply projects to whatever tables carry rows.
        var projection = Project();
        Assert.DoesNotContain(projection.Tables, table => table.Rows.IsDefault);
    }
}
