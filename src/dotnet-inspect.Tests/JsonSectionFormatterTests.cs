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
        var rows = document.RootElement.GetProperty("Results");

        Assert.Equal(JsonValueKind.Array, rows.ValueKind);
        Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal("MemoryCache", rows[0].GetProperty("Type").GetString());
        Assert.Equal("HybridCache", rows[1].GetProperty("Type").GetString());
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
        foreach (var row in document.RootElement.GetProperty("Results").EnumerateArray())
            Assert.Equal(["Type", "Library"], row.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Fact]
    public void SectionSelection_ChangesTheJson()
    {
        var all = Render(new MarkoutWriterOptions());
        var none = Render(new MarkoutWriterOptions { IncludeSections = new HashSet<string>(["Nothing Matches"]) });

        Assert.Contains("Results", all);
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
        foreach (var row in document.RootElement.GetProperty("Results").EnumerateArray())
            Assert.Equal(["Type"], row.EnumerateObject().Select(property => property.Name).ToArray());
    }
}
