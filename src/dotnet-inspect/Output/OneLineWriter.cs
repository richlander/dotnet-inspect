using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// A MarkoutWriter subclass that produces docker-style columnar output.
/// Tables use space-padded columns with uppercase headers.
/// Suppresses all shapes except Tables and Lists via <see cref="SupportedShapes"/>.
/// </summary>
public class OneLineWriter : MarkoutWriter
{
    private const int ColumnGap = 2;
    private readonly bool _showHeader;

    public OneLineWriter(TextWriter writer, bool showHeader = true) : base(writer)
    {
        _showHeader = showHeader;
    }

    public OneLineWriter(TextWriter writer, MarkoutWriterOptions options, bool showHeader = true) : base(writer, options)
    {
        _showHeader = showHeader;
    }

    /// <inheritdoc/>
    public override MarkoutShape SupportedShapes => MarkoutShape.Tables | MarkoutShape.Lists;

    // Silent no-ops for shapes OneLineWriter doesn't render.
    // Overriding avoids the base class "unsupported shape" warning.
    public override void WriteParagraph(string? text) { }
    public override void WriteField(string key, string? value) { }
    public override void WriteField(string key, bool value) { }
    public override void WriteFieldList(IReadOnlyList<MarkoutField> fields) { }

    public override void WriteHeading(int level, string text, string? context)
    {
        UpdateSectionState(level, text);
    }

    /// <inheritdoc/>
    protected override void FlushStreamingTable(string[] headers, IList<string[]> rows, int skippedRows)
    {
        WriteTable(headers, rows);
        if (skippedRows > 0)
            Writer.WriteLine($"\n... and {skippedRows} more");
    }

    public override void WriteTable(IEnumerable<string> headers, IEnumerable<string[]> rows)
    {
        if (SectionExcluded)
            return;

        var headerArray = headers as string[] ?? headers.ToArray();
        var rowList = rows as IList<string[]> ?? rows.ToList();

        // Apply MaxItems
        var maxItems = Options.MaxItems;
        var visibleRows = maxItems.HasValue && rowList.Count > maxItems.Value
            ? rowList.Take(maxItems.Value).ToList()
            : rowList;
        var skipped = rowList.Count - visibleRows.Count;

        // Calculate column widths from headers and visible data
        var widths = new int[headerArray.Length];
        for (int i = 0; i < headerArray.Length; i++)
            widths[i] = headerArray[i].Length;
        foreach (var row in visibleRows)
        {
            for (int i = 0; i < Math.Min(row.Length, widths.Length); i++)
                widths[i] = Math.Max(widths[i], row[i].Length);
        }

        if (_showHeader)
        {
            for (int i = 0; i < headerArray.Length; i++)
            {
                var text = headerArray[i].ToUpperInvariant();
                if (i < headerArray.Length - 1)
                    Writer.Write(text.PadRight(widths[i] + ColumnGap));
                else
                    Writer.Write(text);
            }
            Writer.WriteLine();
        }

        foreach (var row in visibleRows)
        {
            for (int i = 0; i < row.Length; i++)
            {
                if (i < row.Length - 1)
                    Writer.Write(row[i].PadRight(widths[i] + ColumnGap));
                else
                    Writer.Write(row[i]);
            }
            Writer.WriteLine();
        }

        if (skipped > 0)
            Writer.WriteLine($"\n... and {skipped} more");
    }

    public override void WriteListItem(string text)
    {
        if (SectionExcluded)
            return;

        Writer.WriteLine(text);
    }

    protected override void EnsureBlankLineIfNeeded() { }
}
