using System.Text.Json;
using DotnetInspector.Output;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Tests;

/// <summary>
/// Covers the lowered JSON view (#3494): JSON rendered through the Markout formatter seam rather
/// than from a separate typed graph, so that Shape and Section decisions reach it.
/// </summary>
public class JsonSectionFormatterTests
{
    private static FindResultView BuildView() => new()
    {
        Title = "Find: Cache",
        Results =
        [
            new FindRow("Cache", "MemoryCache", "System.Runtime.Caching", "class", "System.Runtime.Caching", "runtime", "Exact", "1.00"),
            new FindRow("Cache", "HybridCache", "Microsoft.Extensions.Caching.Hybrid", "class", "Microsoft.Extensions.Caching.Abstractions", "nuget", "Partial", "0.80"),
        ],
    };

    private static string Render(MarkoutWriterOptions options)
    {
        // Mirror the production configuration: OutputFormatter.RenderProjectedJson asks Markout for
        // its JSONL vocabulary so the formatter is handed machine key names, not display headings.
        OutputFormatter.ConfigureTableWriterOptions(options, tsv: false, jsonl: true);
        var formatter = new JsonSectionFormatter();
        formatter.BeginDocument(options);
        MarkoutSerializer.Serialize(BuildView(), TextWriter.Null, formatter, SearchViewContext.Default, options);
        return formatter.Finish();
    }

    [Fact]
    public void TableSection_RendersAsArrayOfRowObjects()
    {
        var json = Render(new MarkoutWriterOptions());

        using var document = JsonDocument.Parse(json);
        var rows = document.RootElement.GetProperty("results");

        Assert.Equal(JsonValueKind.Array, rows.ValueKind);
        Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal("MemoryCache", rows[0].GetProperty("type").GetString());
        Assert.Equal("HybridCache", rows[1].GetProperty("type").GetString());
    }

    [Fact]
    public void Projection_ChangesTheJson()
    {
        // The Format-invariance gate from #3494: toggle a Shape option and diff the JSON. If the
        // JSON is unchanged, the option is being honored by one format and dropped by JSON, which
        // is the mislayering this view exists to remove.
        var unprojected = Render(new MarkoutWriterOptions());
        var projected = Render(new MarkoutWriterOptions
        {
            Projection = new MarkoutProjection { IncludeColumns = ["Type", "Library"] },
        });

        Assert.NotEqual(unprojected, projected);

        using var document = JsonDocument.Parse(projected);
        foreach (var row in document.RootElement.GetProperty("results").EnumerateArray())
            Assert.Equal(["type", "library"], row.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Fact]
    public void SectionSelection_ChangesTheJson()
    {
        var all = Render(new MarkoutWriterOptions());
        var none = Render(new MarkoutWriterOptions { IncludeSections = new HashSet<string>(["Nothing Matches"]) });

        Assert.Contains("results", all, StringComparison.Ordinal);
        Assert.NotEqual(all, none);
        Assert.DoesNotContain("MemoryCache", none);
    }

    [Fact]
    public void UnmatchedColumn_Throws()
    {
        // A bad column name must not yield a success-shaped empty document. Markout owns the
        // projection, so the lowered JSON path inherits the same failure the table formats raise.
        var exception = Assert.Throws<InvalidOperationException>(() => Render(new MarkoutWriterOptions
        {
            Projection = new MarkoutProjection { IncludeColumns = ["NotAColumn"] },
        }));

        Assert.Contains("No columns matched projection", exception.Message);
    }

    [Fact]
    public void RenderProjectedJson_HonorsColumns()
    {
        var json = OutputFormatter.RenderProjectedJson(
            columns: ["Type"],
            fields: null,
            (writer, formatter, writerOptions) =>
                MarkoutSerializer.Serialize(BuildView(), writer, formatter, SearchViewContext.Default, writerOptions));

        using var document = JsonDocument.Parse(json);
        foreach (var row in document.RootElement.GetProperty("results").EnumerateArray())
            Assert.Equal(["type"], row.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Fact]
    public void MixedContentInOneSection_FailsRatherThanDroppingIt()
    {
        // A section serializes as exactly one JSON value, so a section that receives two kinds of
        // content cannot keep both. The gate for "this projection never silently loses data": if
        // this throw is ever removed or downgraded, the fields written here vanish from the
        // document and nothing else in the suite notices. Reported by adversarial review of #3494.
        var formatter = new JsonSectionFormatter();
        formatter.BeginDocument(new MarkoutWriterOptions());

        formatter.FormatHeading(TextWriter.Null, 2, "Overview", null);
        formatter.FormatFields(TextWriter.Null, [new MarkoutField("Package", "Contoso")], false);

        // A subheading does not open a new section, so this table lands in "Overview".
        formatter.FormatHeading(TextWriter.Null, 3, "Detail", null);

        var error = Assert.Throws<NotSupportedException>(() =>
            formatter.FormatTable(TextWriter.Null, ["Header"], [["Value"]], 0, new MarkoutWriterOptions()));

        Assert.Contains("Overview", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SameKindContentInOneSection_Accumulates()
    {
        // The negative case that keeps the guard honest: appending content of the kind a section
        // already holds is lossless, so it must NOT throw. A guard that rejected this would be
        // rejecting normal multi-call field emission.
        var formatter = new JsonSectionFormatter();
        formatter.BeginDocument(new MarkoutWriterOptions());

        formatter.FormatHeading(TextWriter.Null, 2, "Overview", null);
        formatter.FormatFields(TextWriter.Null, [new MarkoutField("Package", "Contoso")], false);
        formatter.FormatFields(TextWriter.Null, [new MarkoutField("Version", "1.0.0")], false);

        using var document = JsonDocument.Parse(formatter.Finish());
        var overview = document.RootElement.GetProperty("overview");

        Assert.Equal("Contoso", overview.GetProperty("package").GetString());
        Assert.Equal("1.0.0", overview.GetProperty("version").GetString());
    }

    [Fact]
    public void SecondTableWithDifferentColumns_FailsRatherThanRelabellingRows()
    {
        // Rows append but headers replace, so a second table in one section would re-label the
        // first table's already-buffered rows with the second table's column names.
        var formatter = new JsonSectionFormatter();
        formatter.BeginDocument(new MarkoutWriterOptions());

        formatter.FormatHeading(TextWriter.Null, 2, "Results", null);
        formatter.FormatTable(TextWriter.Null, ["Type"], [["MemoryCache"]], 0, new MarkoutWriterOptions());

        var error = Assert.Throws<NotSupportedException>(() =>
            formatter.FormatTable(TextWriter.Null, ["Member"], [["Dispose"]], 0, new MarkoutWriterOptions()));

        Assert.Contains("Results", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RowWindow_IsAppliedToDataRatherThanRenderedLines()
    {
        // --rows must survive the change of Format. Applying it to the buffered rows (rather than
        // with the line-oriented limiter the table formats use) is also what keeps the document
        // parsable: a line limiter would cut a pretty-printed document mid-object.
        var options = new MarkoutWriterOptions();
        OutputFormatter.ConfigureTableWriterOptions(options, tsv: false, jsonl: true);

        var json = OutputFormatter.RenderProjectedJson(
            columns: ["Type"],
            fields: null,
            serialize: (writer, formatter, writerOptions) =>
                MarkoutSerializer.Serialize(BuildView(), writer, formatter, SearchViewContext.Default, writerOptions),
            indented: true,
            maxRows: RowWindow.Head(1));

        using var document = JsonDocument.Parse(json);
        var rows = document.RootElement.GetProperty("results");

        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal("MemoryCache", rows[0].GetProperty("type").GetString());
    }
}
