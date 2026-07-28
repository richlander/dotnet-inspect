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
        bool truncated = false,
        bool targetExists = true)
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
            truncated,
            targetExists);

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
    public void Markdown_EmptyAndClean_QualifiesNothingPointsHere()
    {
        var markdown = Render(Set(references: []));

        Assert.Contains("No row points at TypeDef[5].", markdown);

        // No detectable blind spot fired, so none of their caveats may appear...
        Assert.DoesNotContain("budget", markdown);
        Assert.DoesNotContain("could not be read", markdown);
        Assert.DoesNotContain("not searched in full", markdown);
        Assert.DoesNotContain("past the end of its table", markdown);

        // ...but the blob limit always applies, and this is the case where the
        // bare sentence above would otherwise read as a guarantee.
        Assert.Contains("Blob payloads are not searched", markdown);
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

    /// <summary>
    /// The signature-blob limit is unconditional, so a search that hits no
    /// detectable blind spot still carries exactly one caveat. A reader acting
    /// on "No row points at X" must see it precisely in this case, because this
    /// is when that sentence reads most like a guarantee.
    /// </summary>
    [Fact]
    public void Caveats_ForACleanSearch_AreTheBlobLimitAlone()
    {
        var caveat = Assert.Single(MetadataProjectionRenderer.Caveats(Set()));

        Assert.Contains("Blob payloads are not searched", caveat);
    }

    [Fact]
    public void Caveats_ReportEachBlindSpotIndependently()
    {
        var all = Set(
            unreadable: ImmutableArray.Create(new MetadataRowLocation(TableIndex.MethodDef, 7)),
            unscanned: ImmutableArray.Create(TableIndex.NestedClass),
            truncated: true,
            targetExists: false);

        // Four detectable blind spots, plus the unconditional blob limit.
        Assert.Equal(5, MetadataProjectionRenderer.Caveats(all).Count());
    }

    [Fact]
    public void Caveats_MissingTargetRow_IsReportedNotRenderedAsAnEmptyAnswer()
    {
        // A row id past the end of its table is usually a typo. Answering it
        // with a clean "nothing points here" makes the typo indistinguishable
        // from a real row nothing points at.
        var caveats = MetadataProjectionRenderer.Caveats(Set(references: [], targetExists: false));

        Assert.Contains(caveats, c => c.Contains("past the end of its table", StringComparison.Ordinal));
    }

    [Fact]
    public void Markdown_UnscannedTables_AreNamedNotJustCounted()
    {
        // A reader cannot judge what the search missed from a bare count, so the
        // caveat must name the tables that went unsearched.
        var markdown = Render(Set(
            unscanned: ImmutableArray.Create(TableIndex.NestedClass, TableIndex.MethodSemantics)));

        Assert.Contains("2 populated tables were not searched", markdown);
        Assert.Contains("NestedClass", markdown);
        Assert.Contains("MethodSemantics", markdown);
    }

    [Fact]
    public void Markdown_SingleUnscannedTable_ReadsAsSingular()
    {
        var markdown = Render(Set(unscanned: ImmutableArray.Create(TableIndex.NestedClass)));

        Assert.Contains("1 populated table was not searched", markdown);
        Assert.DoesNotContain("1 populated tables", markdown);
    }

    /// <summary>
    /// A table goes unsearched either because the projection does not model it
    /// or because the budget stopped the scan first. The caveat cannot see
    /// which, so it must not assert a cause it does not know — a modelled table
    /// the budget never reached would make "not modelled" a false statement.
    ///
    /// It must not overstate the extent either. The budget can stop *part-way
    /// through* a table, so a caveat claiming none of the table's rows was
    /// searched would be false for the very table truncation landed in.
    /// </summary>
    [Fact]
    public void Markdown_UnscannedTables_DoNotClaimACause()
    {
        var markdown = Render(Set(
            unscanned: ImmutableArray.Create(TableIndex.Assembly),
            truncated: true));

        Assert.Contains("Assembly", markdown);
        Assert.DoesNotContain("not modelled", markdown);
        Assert.Contains("not searched in full", markdown);

        // A table lands in UnscannedTables for three reasons, and only two of
        // them leave a whole row unread — the third stops between columns of an
        // already-read final row. Claiming an unread row would be false there,
        // so the caveat is worded around an unexamined cell instead.
        Assert.Contains("cell the scan never examined", markdown);
        Assert.DoesNotContain("row the scan never read", markdown);
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
