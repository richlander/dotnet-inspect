using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;

namespace DotnetInspector.MetadataRendering.Tests;

/// <summary>
/// Tests for <see cref="MetadataProjectionRenderer"/>. Most cases render a small
/// synthetic <see cref="MetadataTableProjection"/> so a single
/// <see cref="MetadataValue"/> case is exercised in isolation; one case renders
/// this test assembly end to end.
/// </summary>
public class MetadataProjectionRendererTests
{
    static string Render(MetadataTableProjection projection, MetadataTableFormat format = MetadataTableFormat.Markdown)
    {
        var writer = new StringWriter();
        MetadataProjectionRenderer.Render(projection, writer, format);
        return writer.ToString();
    }

    static MetadataTableProjection OneCell(
        string tableName,
        MetadataColumn column,
        MetadataValue cell,
        MetadataTableTruncation? truncation = null,
        int rowCount = 1)
        => new(ImmutableArray.Create(
            new MetadataTableView(
                TableIndex.TypeDef,
                tableName,
                rowCount,
                ImmutableArray.Create(column),
                ImmutableArray.Create(new MetadataRow(1, 0x02000001, ImmutableArray.Create(cell))),
                truncation)));

    static MetadataColumn Column(string name, MetadataColumnKind kind) => new(name, kind);

    [Fact]
    public void Markdown_WritesHeadingHeaderRowAndRidColumn()
    {
        var projection = OneCell(
            "TypeDef",
            Column("Name", MetadataColumnKind.Heap),
            new MetadataValue.HeapReference(HeapKind.String, 10, 6, "System", "System", Truncated: false));

        var markdown = Render(projection);

        Assert.Contains("## TypeDef (1 row)", markdown);
        Assert.Contains("System", markdown);
        // A leading row-id column is prepended before the table's own columns.
        Assert.Contains("#", markdown);
        Assert.Contains("Name", markdown);
        // A Markdown pipe table has at least one row line that starts with '|'.
        Assert.Contains(markdown.Split('\n'), line => line.TrimStart().StartsWith('|'));
    }

    [Fact]
    public void Malformed_RendersVisibleMarker_NeverEmpty()
    {
        var projection = OneCell(
            "TypeDef",
            Column("Extends", MetadataColumnKind.Handle),
            new MetadataValue.Malformed("Handle 0x02000099 targets TypeDef row 153, outside [1, 40]."));

        var markdown = Render(projection);

        Assert.Contains("!malformed:", markdown);
        Assert.Contains("outside [1, 40]", markdown);
    }

    [Fact]
    public void TruncatedHeap_RendersEllipsis()
    {
        var projection = OneCell(
            "TypeDef",
            Column("Name", MetadataColumnKind.Heap),
            new MetadataValue.HeapReference(HeapKind.String, 0, 100, "abc", "abc", Truncated: true));

        Assert.Contains("abc\u2026", Render(projection));
    }

    [Fact]
    public void BlobHeap_RendersHexPreview()
    {
        var projection = OneCell(
            "TypeDef",
            Column("Signature", MetadataColumnKind.Heap),
            new MetadataValue.HeapReference(HeapKind.Blob, 0, 3, Text: null, "0A1B2C", Truncated: false));

        Assert.Contains("0A1B2C", Render(projection));
    }

    [Fact]
    public void Handle_RendersTargetCoordinateAndDisplay()
    {
        var projection = OneCell(
            "TypeDef",
            Column("Extends", MetadataColumnKind.Handle),
            new MetadataValue.Handle(new HandleRef(TableIndex.TypeRef, 5, 0x01000005, "System.Object")));

        Assert.Contains("TypeRef[5] (System.Object)", Render(projection));
    }

    [Fact]
    public void Handle_NilTarget_RendersNil()
    {
        var projection = OneCell(
            "TypeDef",
            Column("Extends", MetadataColumnKind.Handle),
            new MetadataValue.Handle(new HandleRef(TableIndex.TypeRef, 0, 0)));

        Assert.Contains("nil", Render(projection));
    }

    [Fact]
    public void Range_RendersHalfOpenInterval()
    {
        var projection = OneCell(
            "TypeDef",
            Column("MethodList", MetadataColumnKind.HandleRange),
            new MetadataValue.Range(new HandleRange(TableIndex.MethodDef, 1, 5)));

        Assert.Contains("MethodDef[1..5)", Render(projection));
    }

    [Fact]
    public void Nil_RendersNilMarker()
    {
        var projection = OneCell(
            "Module",
            Column("EncId", MetadataColumnKind.Heap),
            new MetadataValue.Nil());

        Assert.Contains("nil", Render(projection));
    }

    [Fact]
    public void TableTruncation_ShownInHeading()
    {
        var projection = OneCell(
            "TypeDef",
            Column("Name", MetadataColumnKind.Heap),
            new MetadataValue.HeapReference(HeapKind.String, 0, 6, "System", "System", Truncated: false),
            truncation: new MetadataTableTruncation(1, 40),
            rowCount: 40);

        Assert.Contains("## TypeDef (showing 1 of 40 rows)", Render(projection));
    }

    [Fact]
    public void Tsv_IsTabDelimitedWithSelfIdentifyingTableColumn()
    {
        var projection = OneCell(
            "TypeDef",
            Column("Name", MetadataColumnKind.Heap),
            new MetadataValue.HeapReference(HeapKind.String, 0, 6, "System", "System", Truncated: false));

        var tsv = Render(projection, MetadataTableFormat.Tsv);

        Assert.Contains("\t", tsv);
        Assert.Contains("System", tsv);
        // The row self-identifies via a leading Table column instead of a heading.
        Assert.Contains("TypeDef", tsv);
        Assert.DoesNotContain("## TypeDef", tsv);
        // TSV rows are not Markdown pipe tables.
        Assert.DoesNotContain("| ", tsv);
    }

    [Fact]
    public void Jsonl_EmitsOneObjectPerRowWithTableAndRidKeys()
    {
        var projection = OneCell(
            "TypeDef",
            Column("Name", MetadataColumnKind.Heap),
            new MetadataValue.HeapReference(HeapKind.String, 0, 6, "System", "System", Truncated: false));

        var jsonl = Render(projection, MetadataTableFormat.Jsonl);

        Assert.Contains("\"table\"", jsonl);
        Assert.Contains("\"rid\"", jsonl);
        Assert.Contains("System", jsonl);
        Assert.DoesNotContain("## TypeDef", jsonl);
        Assert.Contains(jsonl.Split('\n'), line => line.TrimStart().StartsWith('{'));
    }

    [Fact]
    public void Render_SelfAssembly_ProducesModuleAndTypeDefTables()
    {
        using var session = AssemblyInspectionSession.Open(typeof(MetadataProjectionRendererTests).Assembly.Location);
        var projection = session.MetadataTables();

        var markdown = Render(projection);

        Assert.Contains("## Module", markdown);
        Assert.Contains("## TypeDef", markdown);
        Assert.Contains(nameof(MetadataProjectionRendererTests), markdown);
    }
}
