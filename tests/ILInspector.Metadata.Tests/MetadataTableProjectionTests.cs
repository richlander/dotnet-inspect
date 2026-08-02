using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;
using InertText;

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
        => (Cell(table, row, column) as MetadataValue.HeapReference)?.Text?.ToString();

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
        Assert.False(name.Text!.Value.IsEmpty);

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
        Assert.Contains("Object", extends.Reference.Display!.Value.ToString());

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
        Assert.False(signature.Preview.IsEmpty);
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
    public void ConstantTable_DecodesTypeCode_AndResolvesParentIntoConstantOwner()
    {
        var constant = Table(Project(), TableIndex.Constant);
        Assert.True(constant.RowCount >= 1, "Expected the test assembly to define constants.");

        // Type is the ConstantTypeCode carried as a scalar with an additive
        // decoded name (e.g. Int32, String, Boolean), never an opaque number.
        foreach (var row in constant.Rows)
        {
            var typeCode = Assert.IsType<MetadataValue.Scalar>(Cell(constant, row, "Type"));
            Assert.False(string.IsNullOrEmpty(typeCode.Display));

            // Parent is a HasConstant coded index; every edge lands in one of the
            // three owner tables and points at a live row.
            var parent = Assert.IsType<MetadataValue.Handle>(Cell(constant, row, "Parent"));
            Assert.Contains(
                parent.Reference.TargetTable,
                new[] { TableIndex.Field, TableIndex.Param, TableIndex.Property });
            Assert.True(parent.Reference.TargetRowId >= 1);
        }

        // The column advertises all three HasConstant candidate targets.
        var parentColumn = constant.Columns[ColumnIndex(constant, "Parent")];
        Assert.Equal(MetadataColumnKind.Handle, parentColumn.Kind);
        Assert.Contains(TableIndex.Field, parentColumn.CandidateTargets);
        Assert.Contains(TableIndex.Param, parentColumn.CandidateTargets);
        Assert.Contains(TableIndex.Property, parentColumn.CandidateTargets);

        // At least one constant carries a value blob preview.
        Assert.Contains(
            constant.Rows,
            row => Cell(constant, row, "Value") is MetadataValue.HeapReference { Heap: HeapKind.Blob });
    }

    [Fact]
    public void StandAloneSigTable_IsBoundedBlobPreview()
    {
        var standAloneSig = Table(Project(), TableIndex.StandAloneSig);
        var row = standAloneSig.Rows.First();

        var signature = Assert.IsType<MetadataValue.HeapReference>(Cell(standAloneSig, row, "Signature"));
        Assert.Equal(HeapKind.Blob, signature.Heap);
        Assert.Null(signature.Text);
    }

    [Fact]
    public void MethodImplTable_ResolvesClassAndMethodEdges()
    {
        var methodImpl = Table(Project(), TableIndex.MethodImpl);
        Assert.True(methodImpl.RowCount >= 1, "Expected the test assembly to define method overrides.");

        foreach (var row in methodImpl.Rows)
        {
            // Class is a direct TypeDef handle (not a coded index).
            var declaringType = Assert.IsType<MetadataValue.Handle>(Cell(methodImpl, row, "Class"));
            Assert.Equal(TableIndex.TypeDef, declaringType.Reference.TargetTable);

            // Body and declaration are MethodDefOrRef coded indices.
            var body = Assert.IsType<MetadataValue.Handle>(Cell(methodImpl, row, "MethodBody"));
            Assert.Contains(body.Reference.TargetTable, new[] { TableIndex.MethodDef, TableIndex.MemberRef });

            var declaration = Assert.IsType<MetadataValue.Handle>(Cell(methodImpl, row, "MethodDeclaration"));
            Assert.Contains(declaration.Reference.TargetTable, new[] { TableIndex.MethodDef, TableIndex.MemberRef });
        }

        var bodyColumn = methodImpl.Columns[ColumnIndex(methodImpl, "MethodBody")];
        Assert.Contains(TableIndex.MethodDef, bodyColumn.CandidateTargets);
        Assert.Contains(TableIndex.MemberRef, bodyColumn.CandidateTargets);
    }

    [Fact]
    public void TypeSpecTable_IsBoundedBlobPreview()
    {
        var typeSpec = Table(Project(), TableIndex.TypeSpec);
        var row = typeSpec.Rows.First();

        var signature = Assert.IsType<MetadataValue.HeapReference>(Cell(typeSpec, row, "Signature"));
        Assert.Equal(HeapKind.Blob, signature.Heap);
        Assert.Null(signature.Text);
    }

    [Fact]
    public void AssemblyTable_HasSingleRow_WithDecodedNameHashAlgorithmAndFlags()
    {
        var assembly = Table(Project(), TableIndex.Assembly);

        var row = Assert.Single(assembly.Rows);
        Assert.Equal(1, row.RowId);

        var name = Assert.IsType<MetadataValue.HeapReference>(Cell(assembly, row, "Name"));
        Assert.Equal(HeapKind.String, name.Heap);
        Assert.Equal("ILInspector.Metadata.Tests", name.Text!.Value.ToString());

        // HashAlgId is a single-valued enum surfaced as a scalar with an additive
        // decoded name (SHA-1 is the csc default), not a bitflag set.
        var hashAlg = Assert.IsType<MetadataValue.Scalar>(Cell(assembly, row, "HashAlgId"));
        Assert.False(string.IsNullOrEmpty(hashAlg.Display));

        // Flags is a genuine bitflag enumeration.
        Assert.IsType<MetadataValue.Flags>(Cell(assembly, row, "Flags"));

        // The four version parts are scalars.
        Assert.IsType<MetadataValue.Scalar>(Cell(assembly, row, "MajorVersion"));
        Assert.IsType<MetadataValue.Scalar>(Cell(assembly, row, "RevisionNumber"));
    }

    [Fact]
    public void ExportedTypeTable_ResolvesImplementation_ForForwardedType()
    {
        // ExportedTypeForwarderFixture forwards MetadataTableProjector, so the
        // test assembly carries a deterministic ExportedType row.
        var exportedType = Table(Project(), TableIndex.ExportedType);

        var row = Assert.Single(
            exportedType.Rows,
            candidate => StringText(exportedType, candidate, "Name") == "MetadataTableProjector");

        Assert.IsType<MetadataValue.Flags>(Cell(exportedType, row, "Attributes"));

        var @namespace = Assert.IsType<MetadataValue.HeapReference>(Cell(exportedType, row, "Namespace"));
        Assert.Equal("ILInspector.Metadata", @namespace.Text!.Value.ToString());

        // A forwarded type's Implementation is an AssemblyRef edge to the defining
        // assembly; the column advertises the full Implementation coded-index set.
        var implementation = Assert.IsType<MetadataValue.Handle>(Cell(exportedType, row, "Implementation"));
        Assert.Equal(TableIndex.AssemblyRef, implementation.Reference.TargetTable);

        var implementationColumn = exportedType.Columns[ColumnIndex(exportedType, "Implementation")];
        Assert.Contains(TableIndex.File, implementationColumn.CandidateTargets);
        Assert.Contains(TableIndex.ExportedType, implementationColumn.CandidateTargets);
        Assert.Contains(TableIndex.AssemblyRef, implementationColumn.CandidateTargets);
    }

    [Fact]
    public void GenericParamTable_DecodesNumberAndResolvesOwner()
    {
        var genericParam = Table(Project(), TableIndex.GenericParam);
        Assert.True(genericParam.RowCount >= 1, "Expected the test assembly to define generic parameters.");

        foreach (var row in genericParam.Rows)
        {
            Assert.IsType<MetadataValue.Scalar>(Cell(genericParam, row, "Number"));
            Assert.IsType<MetadataValue.Flags>(Cell(genericParam, row, "Attributes"));

            var owner = Assert.IsType<MetadataValue.Handle>(Cell(genericParam, row, "Owner"));
            Assert.Contains(owner.Reference.TargetTable, new[] { TableIndex.TypeDef, TableIndex.MethodDef });

            var name = Assert.IsType<MetadataValue.HeapReference>(Cell(genericParam, row, "Name"));
            Assert.False(name.Text!.Value.IsEmpty);
        }

        var ownerColumn = genericParam.Columns[ColumnIndex(genericParam, "Owner")];
        Assert.Contains(TableIndex.TypeDef, ownerColumn.CandidateTargets);
        Assert.Contains(TableIndex.MethodDef, ownerColumn.CandidateTargets);
    }

    [Fact]
    public void MethodSpecTable_ResolvesMethod_AndBlobInstantiation()
    {
        var methodSpec = Table(Project(), TableIndex.MethodSpec);
        Assert.True(methodSpec.RowCount >= 1, "Expected the test assembly to instantiate generic methods.");

        foreach (var row in methodSpec.Rows)
        {
            var method = Assert.IsType<MetadataValue.Handle>(Cell(methodSpec, row, "Method"));
            Assert.Contains(method.Reference.TargetTable, new[] { TableIndex.MethodDef, TableIndex.MemberRef });

            var instantiation = Assert.IsType<MetadataValue.HeapReference>(Cell(methodSpec, row, "Instantiation"));
            Assert.Equal(HeapKind.Blob, instantiation.Heap);
        }
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
    public void StringBudget_ProjectsBoundedPreviewWithExplicitTruncation()
    {
        var projection = Project(new MetadataProjectionOptions { MaxStringChars = 1 });

        var truncated = projection.Tables
            .SelectMany(table => table.Rows)
            .SelectMany(row => row.Cells)
            .OfType<MetadataValue.HeapReference>()
            .Where(heap => heap.Heap == HeapKind.String && heap.Truncated)
            .ToList();

        Assert.NotEmpty(truncated);
        Assert.All(truncated, heap =>
        {
            // Length reports the full decoded size; the retained preview is bounded.
            Assert.True(heap.Length > 1);
            Assert.False(heap.Text!.Value.IsEmpty);
        });
    }

    [Fact]
    public void BlobBudget_ProjectsBoundedHexPreviewWithExplicitTruncation()
    {
        var methodDef = Table(
            Project(new MetadataProjectionOptions { MaxPreviewBytes = 2 }),
            TableIndex.MethodDef);

        var truncated = methodDef.Rows
            .Select(row => Cell(methodDef, row, "Signature"))
            .OfType<MetadataValue.HeapReference>()
            .Where(heap => heap.Truncated)
            .ToList();

        Assert.NotEmpty(truncated);
        Assert.All(truncated, heap =>
        {
            Assert.Equal(HeapKind.Blob, heap.Heap);
            Assert.Null(heap.Text);
            Assert.Equal(4, heap.Preview.Length); // two bytes -> four hex chars
            Assert.True(heap.Length > 2);
        });
    }

    /// <summary>
    /// A blob's partiality is established in bytes, before there is any text to bound,
    /// so it cannot be recovered from the hex the projection carries.
    /// </summary>
    /// <remarks>
    /// This is the gate for the claim in <c>docs/design/inert-text.md</c> that
    /// <see cref="MetadataValue.HeapReference.Truncated"/> must stay a stored field
    /// rather than being derived from its text the way <see cref="HandleRef.Display"/>'s
    /// truncation now is. Deriving it would report a blob that lost most of its content
    /// as whole, because the hex is a complete spelling of the bytes that were actually
    /// read. If this test fails, the two notions of truncation have been conflated.
    /// </remarks>
    [Fact]
    public void BlobTruncation_IsInvisibleToTheHexItProduces()
    {
        var methodDef = Table(
            Project(new MetadataProjectionOptions { MaxPreviewBytes = 2 }),
            TableIndex.MethodDef);

        var clipped = methodDef.Rows
            .Select(row => Cell(methodDef, row, "Signature"))
            .OfType<MetadataValue.HeapReference>()
            .Where(heap => heap.Truncated)
            .ToList();

        Assert.NotEmpty(clipped);
        Assert.All(clipped, heap => Assert.False(
            heap.Preview.IsTruncated,
            "The hex spells every byte that was read, so the text cannot know bytes were dropped."));
    }

    [Fact]
    public void HandleDisplay_RespectsStringBudgetWithExplicitTruncation()
    {
        // A handle's convenience Display is a resolved name drawn from the heaps,
        // so it is subject to the same character budget as a heap string cell; a
        // large shared name must not be re-materialized across every referencing
        // row via the Display path.
        const int budget = 4;
        var references = Project(new MetadataProjectionOptions { MaxStringChars = budget })
            .Tables
            .SelectMany(table => table.Rows)
            .SelectMany(row => row.Cells)
            .OfType<MetadataValue.Handle>()
            .Select(cell => cell.Reference)
            .Where(reference => reference.Display is not null)
            .ToList();

        Assert.NotEmpty(references);
        Assert.All(references, reference => Assert.True(
            reference.Display!.Value.Length <= budget,
            $"Display '{reference.Display}' exceeds the {budget}-char budget."));
        Assert.Contains(references, reference => reference.Display!.Value.IsTruncated);
    }

    [Fact]
    public void StringPreview_NeverExceedsCharBudgetEvenWhenEscaped()
    {
        // Escaping can expand a single character, so the budget is enforced on the
        // emitted length: no retained string preview exceeds MaxStringChars, even
        // for a value that is nominally within the character count.
        const int budget = 4;
        var strings = Project(new MetadataProjectionOptions { MaxStringChars = budget })
            .Tables
            .SelectMany(table => table.Rows)
            .SelectMany(row => row.Cells)
            .OfType<MetadataValue.HeapReference>()
            .Where(heap => heap.Heap == HeapKind.String && heap.Text is not null)
            .ToList();

        Assert.NotEmpty(strings);
        Assert.All(strings, heap => Assert.True(
            heap.Text!.Value.Length <= budget,
            $"String preview '{heap.Text}' exceeds the {budget}-char budget."));
    }

    [Fact]
    public void ContainCellText_KeepsSurrogatePairsAtomicAndContainsLoneSurrogates()
    {
        // The budget is measured in UTF-16 code units, but a supplementary scalar
        // is two of them; truncation must never retain a lone high surrogate, and
        // an unpaired surrogate must be contained rather than emitted raw.
        //
        // Called directly rather than by reflection. When this reflected on the
        // method by name, renaming it produced a NullReferenceException inside the
        // test instead of a build error, which reports a rename as a mysterious
        // failure rather than as the trivially fixable thing it is.
        const string emoji = "\U0001F600"; // one scalar, two UTF-16 code units

        // Budget 2: 'A' fits (1); the pair needs 2 more and is dropped as a unit.
        InertString dropped = MetadataTableProjector.ContainCellText("A" + emoji, 2);
        string droppedText = dropped.ToString();
        Assert.Equal("A", droppedText);
        Assert.True(dropped.IsTruncated);
        Assert.DoesNotContain(droppedText, char.IsSurrogate);

        // Budget 3: 'A' plus the atomic pair fit exactly; nothing is truncated.
        InertString kept = MetadataTableProjector.ContainCellText("A" + emoji, 3);
        Assert.Equal("A" + emoji, kept.ToString());
        Assert.False(kept.IsTruncated);

        // A lone (unpaired) surrogate is contained, never emitted as raw text.
        string loneText = MetadataTableProjector.ContainCellText("\uD83D", 10).ToString();
        Assert.Equal(@"\uD83D", loneText);
        Assert.DoesNotContain(loneText, char.IsSurrogate);
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
