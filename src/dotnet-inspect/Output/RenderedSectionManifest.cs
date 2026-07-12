using Markout;
using Markout.Formatting;

namespace DotnetInspector.Output;

internal sealed class RenderedSectionManifest
{
    private readonly Dictionary<string, HashSet<string>> _tableColumns =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _fields =
        new(StringComparer.OrdinalIgnoreCase);

    internal void RecordTable(string? section, ReadOnlySpan<string> columns)
    {
        if (section is null || _tableColumns.ContainsKey(section))
            return;

        _tableColumns[section] = new HashSet<string>(
            columns.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    internal bool HasTable(string section)
        => _tableColumns.ContainsKey(section);

    internal IReadOnlySet<string>? GetTableColumns(string section)
        => _tableColumns.TryGetValue(section, out var columns) ? columns : null;

    internal void RecordFields(string? section, ReadOnlySpan<MarkoutField> fields)
    {
        if (section is null)
            return;

        if (!_fields.TryGetValue(section, out var names))
        {
            names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _fields[section] = names;
        }

        foreach (var field in fields)
            names.Add(field.Key);
    }

    internal void RecordField(string? section, string field)
    {
        if (section is null)
            return;

        if (!_fields.TryGetValue(section, out var names))
        {
            names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _fields[section] = names;
        }

        names.Add(field);
    }

    internal IReadOnlySet<string>? GetFields(string section)
        => _fields.TryGetValue(section, out var fields) ? fields : null;
}

internal sealed class RenderManifestFormatter :
    IMarkoutFormatter,
    IHeadingFormatter,
    IFieldFormatter,
    ITableFormatter,
    IStreamingTableFormatter
{
    private string? _currentHeading;
    private string? _streamingTableSection;
    private bool _streamingFieldTable;

    internal RenderedSectionManifest Manifest { get; } = new();

    internal static RenderedSectionManifest Capture<T>(
        T value,
        MarkoutSerializerContext context,
        MarkoutWriterOptions options)
    {
        var formatter = new RenderManifestFormatter();
        var writer = new MarkoutWriter(TextWriter.Null, formatter, options);
        context.Serialize(value, writer);
        writer.Flush();
        return formatter.Manifest;
    }

    public void FormatHeading(TextWriter writer, int level, string text, string? context)
        => _currentHeading = text;

    public void FormatFieldName(TextWriter writer, string key, bool bold)
        => Manifest.RecordField(_currentHeading, key);

    public void FormatFields(TextWriter writer, MarkoutField[] fields, bool bold)
        => Manifest.RecordFields(_currentHeading, fields);

    public void FormatFields(TextWriter writer, ReadOnlySpan<MarkoutField> fields, bool bold)
        => Manifest.RecordFields(_currentHeading, fields);

    public void FormatTable(
        TextWriter writer,
        ReadOnlySpan<string> headers,
        IList<string[]> rows,
        int skippedRows,
        MarkoutWriterOptions options)
    {
        Manifest.RecordTable(_currentHeading, headers);
        if (!IsFieldTable(headers))
            return;

        foreach (var row in rows)
        {
            if (row.Length > 0)
                Manifest.RecordField(_currentHeading, row[0]);
        }
    }

    public void BeginTable(
        TextWriter writer,
        ReadOnlySpan<string> headers,
        MarkoutWriterOptions options)
    {
        Manifest.RecordTable(_currentHeading, headers);
        _streamingTableSection = _currentHeading;
        _streamingFieldTable = IsFieldTable(headers);
    }

    public void WriteRow(TextWriter writer, ReadOnlySpan<string> values)
    {
        if (_streamingFieldTable && values.Length > 0)
            Manifest.RecordField(_streamingTableSection, values[0]);
    }

    public void EndTable(TextWriter writer, int skippedRows)
    {
        _streamingTableSection = null;
        _streamingFieldTable = false;
    }

    private static bool IsFieldTable(ReadOnlySpan<string> headers)
        => headers.Length > 0
            && headers[0].Equals("Field", StringComparison.OrdinalIgnoreCase);
}
