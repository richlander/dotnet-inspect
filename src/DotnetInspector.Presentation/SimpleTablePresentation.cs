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
        var writer = CreateWriter(output, showHeader, format);
        writer.WriteTableStart(displayColumns, stableColumns);
        foreach (var row in rows)
            writer.WriteTableRow(row);
        writer.WriteTableEnd();
        writer.Flush();
    }

    public static void WriteList(
        TextWriter output,
        SimpleTableFormat format,
        string displayName,
        string stableName,
        IEnumerable<string> values)
    {
        var writer = CreateWriter(output, showHeader: false, format);
        if (format == SimpleTableFormat.Jsonl)
        {
            writer.WriteTableStart([displayName], [stableName]);
            foreach (var value in values)
                writer.WriteTableRow(value);
            writer.WriteTableEnd();
        }
        else
            writer.WriteList(values.ToArray());
        writer.Flush();
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
