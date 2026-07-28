using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;

namespace DotnetInspector.MetadataRendering.Tests;

/// <summary>
/// Tests for rendering a <see cref="MetadataRowReferenceSet"/>. The renderer's
/// obligation is that a reverse search's blind spots — a budget that stopped the
/// scan, rows that could not be decoded, and populated tables the projection
/// never modelled — survive into every format, so an empty answer is never
/// mistaken for a confident one.
/// </summary>
public class MetadataRowReferenceRendererTests
{
    static string Render(MetadataRowReferenceSet references, MetadataTableFormat format = MetadataTableFormat.Markdown)
    {
        var writer = new StringWriter();
        MetadataProjectionRenderer.Render(references, writer, format);
        return writer.ToString();
    }

    static MetadataRowReferenceSet Set(
        ImmutableArray<MetadataRowReference>? references = null,
        ImmutableArray<MetadataRowLocation>? unreadable = null,
        ImmutableArray<TableIndex>? unscanned = null,
        bool truncated = false)
        => new(
            new MetadataRowLocation(TableIndex.TypeDef, 5),
            references ?? ImmutableArray.Create(
                new MetadataRowReference(
                    new MetadataRowLocation(TableIndex.NestedClass, 3),
                    ColumnIndex: 1,
                    ColumnName: "EnclosingClass",
                    MetadataRowReferenceKind.Handle)),
            unreadable ?? [],
            unscanned ?? [],
            truncated);

    [Fact]
    public void Markdown_NamesTargetAndCountInHeading()
    {
        var markdown = Render(Set());

        Assert.Contains("## References to TypeDef[5] (1)", markdown);
        Assert.Contains("NestedClass", markdown);
        Assert.Contains("EnclosingClass", markdown);
        Assert.Contains("Handle", markdown);
    }

    [Fact]
    public void Markdown_EmptyAndComplete_SaysNothingPointsHere()
    {
        var markdown = Render(Set(references: []));

        Assert.Contains("No row points at TypeDef[5].", markdown);
        // Nothing was hidden, so no caveat may appear.
        Assert.DoesNotContain("budget", markdown);
        Assert.DoesNotContain("could not be read", markdown);
        Assert.DoesNotContain("not modelled by the projection", markdown);
    }

    [Fact]
    public void Markdown_Truncated_SaysMoreMayExist()
    {
        var markdown = Render(Set(truncated: true));

        Assert.Contains("budget stopped this scan", markdown);
    }

    [Fact]
    public void Markdown_UnreadableRows_AreVisibleAndCounted()
    {
        var unreadable = ImmutableArray.Create(
            new MetadataRowLocation(TableIndex.MethodDef, 7),
            new MetadataRowLocation(TableIndex.MethodDef, 8));

        var markdown = Render(Set(unreadable: unreadable));

        Assert.Contains("2 rows had edges that could not be read", markdown);
    }

    [Fact]
    public void Markdown_SingleUnreadableRow_ReadsAsSingular()
    {
        var unreadable = ImmutableArray.Create(new MetadataRowLocation(TableIndex.MethodDef, 7));

        var markdown = Render(Set(unreadable: unreadable));

        Assert.Contains("1 row had an edge that could not be read", markdown);
        Assert.DoesNotContain("1 rows", markdown);
    }

    [Fact]
    public void MachineFormats_CarryTargetColumn_SoRowsSelfIdentify()
    {
        // Markdown names the target once in a heading; TSV and JSONL have no
        // heading, so the target must travel on every row.
        var tsv = Render(Set(), MetadataTableFormat.Tsv);
        var jsonl = Render(Set(), MetadataTableFormat.Jsonl);

        Assert.Contains("TypeDef[5]", tsv);
        Assert.Contains("TypeDef[5]", jsonl);
        Assert.Contains("target", jsonl);
    }

    [Fact]
    public void Jsonl_EmitsOneObjectPerReference()
    {
        var references = ImmutableArray.Create(
            new MetadataRowReference(
                new MetadataRowLocation(TableIndex.TypeDef, 4),
                0,
                "FieldList",
                MetadataRowReferenceKind.Range),
            new MetadataRowReference(
                new MetadataRowLocation(TableIndex.NestedClass, 3),
                1,
                "EnclosingClass",
                MetadataRowReferenceKind.Handle));

        var jsonl = Render(Set(references), MetadataTableFormat.Jsonl);

        var lines = jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.StartsWith("{", line.Trim()));
        Assert.Contains("Range", jsonl);
        Assert.Contains("Handle", jsonl);
    }

    [Fact]
    public void ReferenceOrder_IsPreserved()
    {
        var references = ImmutableArray.Create(
            new MetadataRowReference(new MetadataRowLocation(TableIndex.TypeDef, 4), 0, "FieldList", MetadataRowReferenceKind.Range),
            new MetadataRowReference(new MetadataRowLocation(TableIndex.MethodDef, 9), 0, "Signature", MetadataRowReferenceKind.Handle),
            new MetadataRowReference(new MetadataRowLocation(TableIndex.NestedClass, 3), 1, "EnclosingClass", MetadataRowReferenceKind.Handle));

        var tsv = Render(Set(references), MetadataTableFormat.Tsv);

        int first = tsv.IndexOf("FieldList", StringComparison.Ordinal);
        int second = tsv.IndexOf("Signature", StringComparison.Ordinal);
        int third = tsv.IndexOf("EnclosingClass", StringComparison.Ordinal);

        Assert.True(first >= 0 && first < second && second < third);
    }

    [Fact]
    public void Caveats_AreEmptyForACompleteSearch()
        => Assert.Empty(MetadataProjectionRenderer.Caveats(Set()));

    [Fact]
    public void Caveats_ReportEachBlindSpotIndependently()
    {
        var all = Set(
            unreadable: ImmutableArray.Create(new MetadataRowLocation(TableIndex.MethodDef, 7)),
            unscanned: ImmutableArray.Create(TableIndex.NestedClass),
            truncated: true);

        Assert.Equal(3, MetadataProjectionRenderer.Caveats(all).Count());
    }

    [Fact]
    public void Markdown_UnscannedTables_AreNamedNotJustCounted()
    {
        // A reader cannot judge what the search missed from a bare count, so the
        // caveat must name the tables that went unvisited.
        var markdown = Render(Set(
            unscanned: ImmutableArray.Create(TableIndex.NestedClass, TableIndex.MethodSemantics)));

        Assert.Contains("2 populated tables are not modelled by the projection", markdown);
        Assert.Contains("NestedClass", markdown);
        Assert.Contains("MethodSemantics", markdown);
    }

    [Fact]
    public void Markdown_SingleUnscannedTable_ReadsAsSingular()
    {
        var markdown = Render(Set(unscanned: ImmutableArray.Create(TableIndex.NestedClass)));

        Assert.Contains("1 populated table is not modelled by the projection", markdown);
        Assert.DoesNotContain("1 populated tables", markdown);
    }

    [Fact]
    public void Render_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(
            () => MetadataProjectionRenderer.Render((MetadataRowReferenceSet)null!, new StringWriter()));
        Assert.Throws<ArgumentNullException>(
            () => MetadataProjectionRenderer.Render(Set(), null!));
    }
}
