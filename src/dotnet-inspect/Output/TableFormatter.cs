using Markout;
using Markout.Formatting;

namespace DotnetInspector.Output;

/// <summary>
/// Canonical formatter for tabular projections. It emits normalized TSV:
/// one physical line per row, one tab-delimited field per column.
/// </summary>
public sealed class TableFormatter : IMarkoutFormatter, ITableFormatter, IFieldFormatter, IListFormatter
{
    private readonly bool _showHeader;

    public TableFormatter(bool showHeader = true)
    {
        _showHeader = showHeader;
    }

    void ITableFormatter.FormatTable(TextWriter w, ReadOnlySpan<string> headers, IList<string[]> rows, int skippedRows, MarkoutWriterOptions options)
    {
        if (_showHeader)
            WriteRow(w, headers);

        foreach (var row in rows)
            WriteRow(w, row);
    }

    void IFieldFormatter.FormatFieldName(TextWriter w, string key, bool bold)
    {
        w.Write(Normalize(key));
        w.Write('\t');
    }

    void IFieldFormatter.FormatFields(TextWriter w, MarkoutField[] fields, bool bold)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0)
                w.Write('\t');
            w.Write(Normalize(fields[i].Value));
        }
        w.WriteLine();
    }

    void IListFormatter.FormatListItem(TextWriter w, string text)
    {
        w.WriteLine(Normalize(text));
    }

    void IListFormatter.FormatArray(TextWriter w, string key, ReadOnlySpan<string> items, bool bold)
    {
        foreach (var item in items)
            w.WriteLine(Normalize(item));
    }

    private static void WriteRow(TextWriter w, ReadOnlySpan<string> values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
                w.Write('\t');
            w.Write(Normalize(values[i]));
        }
        w.WriteLine();
    }

    private static void WriteRow(TextWriter w, string[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
                w.Write('\t');
            w.Write(Normalize(values[i]));
        }
        w.WriteLine();
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        for (int i = 0; i < value.Length; i++)
        {
            if (NeedsReplacement(value[i]))
                return NormalizeSlow(value);
        }

        return value;
    }

    private static string NormalizeSlow(string value)
    {
        var chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (NeedsReplacement(chars[i]))
                chars[i] = ' ';
        }
        return new string(chars);
    }

    private static bool NeedsReplacement(char c) =>
        c is '\t' or '\r' or '\n' or '\u0085' or '\u2028' or '\u2029';
}

public static class PrettyTableFormatter
{
    private const int ColumnGap = 2;

    public static string Format(string tsv)
    {
        var rows = ReadRows(tsv);
        if (rows.Count == 0)
            return "";

        var widths = ComputeWidths(rows);
        var writer = new StringWriter();

        foreach (var row in rows)
            WritePrettyRow(writer, row, widths);

        return writer.ToString();
    }

    public static void Write(TextWriter writer, string tsv)
    {
        writer.Write(Format(tsv));
    }

    private static List<string[]> ReadRows(string tsv)
    {
        List<string[]> rows = [];
        using var reader = new StringReader(tsv);

        while (reader.ReadLine() is { } line)
            rows.Add(line.Split('\t'));

        return rows;
    }

    private static int[] ComputeWidths(List<string[]> rows)
    {
        var columnCount = rows.Max(row => row.Length);
        var widths = new int[columnCount];

        foreach (var row in rows)
        {
            for (int i = 0; i < row.Length; i++)
                widths[i] = Math.Max(widths[i], row[i].Length);
        }

        return widths;
    }

    private static void WritePrettyRow(TextWriter writer, string[] row, int[] widths)
    {
        for (int i = 0; i < row.Length; i++)
        {
            if (i < row.Length - 1)
                writer.Write(row[i].PadRight(widths[i] + ColumnGap));
            else
                writer.Write(row[i]);
        }
        writer.WriteLine();
    }
}
