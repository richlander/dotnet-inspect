using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// A MarkoutWriter subclass that produces docker-style columnar output.
/// Batch tables (WriteTable) use space-padded columns with uppercase headers.
/// Streaming tables (WriteTableStart/Row/End) fall back to tab-separated values.
/// Suppresses headings, fields, paragraphs, and code blocks.
/// </summary>
public class OneLineWriter : MarkoutWriter
{
    private const int ColumnGap = 2;
    private readonly bool _showHeader;

    public OneLineWriter(TextWriter writer, bool showHeader = true) : base(writer)
    {
        _showHeader = showHeader;
    }

    public override void WriteHeading(int level, string text, string? context)
    {
        UpdateSectionState(level, text);
    }

    public override void WriteParagraph(string? text) { }

    public override void WriteField(string key, string? value) { }

    public override void WriteField(string key, bool value) { }

    public override void WriteField<T>(string key, T value) { }

    public override void WriteFieldNoBreak(string key, string? value) { }

    public override void WriteFieldNoBreak(string key, bool value) { }

    public override void WriteFieldNoBreak<T>(string key, T value) { }

    public override void WriteCompactFields(params MarkoutField[] fields) { }

    public override void WriteCompactFields(IReadOnlyList<MarkoutField> fields) { }

    public override void WriteCodeBlockStart(string? language = null) { }

    public override void WriteCodeBlockEnd() { }

    public override void WriteTableStart(params string[] headers)
    {
        if (SectionExcluded)
            return;

        if (_showHeader)
            Writer.WriteLine(string.Join('\t', headers.Select(h => h.ToUpperInvariant())));
    }

    public override void WriteTableRow(params string[] values)
    {
        if (SectionExcluded)
            return;

        Writer.WriteLine(string.Join('\t', values));
    }

    public override void WriteTableEnd() { }

    public override void WriteTable(IEnumerable<string> headers, IEnumerable<string[]> rows)
    {
        if (SectionExcluded)
            return;

        var headerArray = headers as string[] ?? headers.ToArray();
        var rowList = rows as IList<string[]> ?? rows.ToList();

        // Calculate column widths from headers and data
        var widths = new int[headerArray.Length];
        for (int i = 0; i < headerArray.Length; i++)
            widths[i] = headerArray[i].Length;
        foreach (var row in rowList)
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

        foreach (var row in rowList)
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
    }

    public override void WriteListItem(string text)
    {
        if (SectionExcluded)
            return;

        Writer.WriteLine(text);
    }

    protected override void EnsureBlankLineIfNeeded() { }
}
