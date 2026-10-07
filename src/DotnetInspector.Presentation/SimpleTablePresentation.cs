using Markout;

namespace DotnetInspector.Presentation;

/// <summary>The rendering mode for a simple table or string list.</summary>
public enum SimpleTableFormat
{
    Table,
    Tsv,
    Jsonl,
}

/// <summary>Renders settled rows without exposing Markout to the caller.</summary>
public static class SimpleTablePresentation
{
    public static void WriteTable(
        TextWriter output,
        bool showHeader,
        SimpleTableFormat format,
        string[] displayColumns,
        string[] stableColumns,
        IEnumerable<string[]> rows)
    {
        WriteComplete(output, writer =>
        {
            var table = CreateWriter(writer, showHeader, format);
            table.WriteTable(displayColumns, stableColumns, rows);
            table.Flush();
        });
    }

    public static void WriteList(
        TextWriter output,
        SimpleTableFormat format,
        string displayName,
        string stableName,
        IEnumerable<string> values)
    {
        string[] items = values.ToArray();
        WriteComplete(output, writer =>
        {
            var table = CreateWriter(writer, showHeader: false, format);
            if (format == SimpleTableFormat.Jsonl)
                table.WriteTable([displayName], [stableName], items.Select(value => new[] { value }).ToArray());
            else
                table.WriteList(items);
            table.Flush();
        });
    }

    private static void WriteComplete(TextWriter output, Action<TextWriter> render)
    {
        using var buffer = new StringWriter { NewLine = output.NewLine };
        render(buffer);
        output.Write(buffer.ToString());
    }

    private static MarkoutWriter CreateWriter(
        TextWriter output,
        bool showHeader,
        SimpleTableFormat format)
    {
        var options = new MarkoutWriterOptions
        {
            TableMode = format switch
            {
                SimpleTableFormat.Table => MarkoutTableMode.Pretty,
                SimpleTableFormat.Tsv => MarkoutTableMode.Tsv,
                SimpleTableFormat.Jsonl => MarkoutTableMode.Jsonl,
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            },
        };
        return new MarkoutWriter(output, new TableFormatter(showHeader), options);
    }
}
