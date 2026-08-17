using Markout;
using Markout.Formatting;

namespace DotnetInspector.Output;

internal sealed class RenderedSectionManifest
{
    private const string RootSection = "\0";

    private readonly Dictionary<string, HashSet<string>> _tableColumns =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _fields =
        new(StringComparer.OrdinalIgnoreCase);

    internal void RecordTable(string? section, ReadOnlySpan<string> columns)
    {
        var key = section ?? RootSection;
        if (_tableColumns.ContainsKey(key))
            return;

        _tableColumns[key] = new HashSet<string>(
            columns.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    internal bool HasTable(string section)
        => _tableColumns.ContainsKey(section);

    internal IReadOnlySet<string>? GetTableColumns(string section)
        => _tableColumns.TryGetValue(section, out var columns) ? columns : null;

    internal void RecordFields(string? section, ReadOnlySpan<MarkoutField> fields)
    {
        var key = section ?? RootSection;
        if (!_fields.TryGetValue(key, out var names))
        {
            names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _fields[key] = names;
        }

        foreach (var field in fields)
            names.Add(field.Key);
    }

    internal void RecordField(string? section, string field)
    {
        var key = section ?? RootSection;
        if (!_fields.TryGetValue(key, out var names))
        {
            names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _fields[key] = names;
        }

        names.Add(field);
    }

    internal IReadOnlySet<string>? GetFields(string section)
        => _fields.TryGetValue(section, out var fields) ? fields : null;

    internal IReadOnlySet<string> ContentKeys =>
        _tableColumns.Keys.Concat(_fields.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyList<string> TableColumns =>
        [.. _tableColumns.Values.SelectMany(columns => columns)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    internal IReadOnlyList<string> FieldsFor(IReadOnlySet<string> contentKeys) =>
        [.. _fields
            .Where(entry => contentKeys.Contains(entry.Key))
            .SelectMany(entry => entry.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
}

internal sealed class RenderManifestFormatter :
    IMarkoutFormatter,
    IHeadingFormatter,
    IFieldFormatter,
    ITableFormatter,
    IStreamingTableFormatter
{
    private readonly HashSet<string> _fieldSections;
    private readonly HashSet<string> _columnSections;
    private string? _currentHeading;
    private string? _streamingTableSection;
    private int _streamingFieldIndex = -1;
    private int _sectionHeadingLevel;

    internal RenderManifestFormatter(DocumentSchema schema)
    {
        _fieldSections = schema.SectionNames
            .Select(schema.GetSection)
            .Where(section => section is not null
                && section.ItemKind.Equals("field", StringComparison.OrdinalIgnoreCase))
            .Select(section => section!.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _columnSections = schema.SectionNames
            .Select(schema.GetSection)
            .Where(section => section is not null
                && section.ItemKind.Equals("column", StringComparison.OrdinalIgnoreCase))
            .Select(section => section!.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal RenderedSectionManifest Manifest { get; } = new();

    internal static RenderedSectionManifest Capture<T>(
        T value,
        MarkoutSerializerContext context,
        MarkoutWriterOptions options,
        DocumentSchema schema)
    {
        var formatter = new RenderManifestFormatter(schema);
        formatter.BeginDocument(options);
        var writer = new MarkoutWriter(TextWriter.Null, formatter, options);
        context.Serialize(value, writer);
        writer.Flush();
        return formatter.Manifest;
    }

    internal static RenderedSectionManifest Capture(
        Action<TextWriter, IMarkoutFormatter, MarkoutWriterOptions> serialize,
        MarkoutWriterOptions options,
        DocumentSchema schema)
    {
        var formatter = new RenderManifestFormatter(schema);
        formatter.BeginDocument(options);
        serialize(TextWriter.Null, formatter, options);
        return formatter.Manifest;
    }

    internal void BeginDocument(MarkoutWriterOptions options)
    {
        _currentHeading = null;
        _streamingTableSection = null;
        _streamingFieldIndex = -1;
        _sectionHeadingLevel = Math.Clamp(2 + options.HeadingLevelOffset, 1, 6);
    }

    public void FormatHeading(TextWriter writer, int level, string text, string? context)
    {
        if (level == _sectionHeadingLevel)
            _currentHeading = text;
    }

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
        var fieldIndex = headers.IndexOf(
            "Field",
            StringComparer.OrdinalIgnoreCase);
        if (!IsFieldSection(_currentHeading)
            && (fieldIndex < 0 || IsColumnSection(_currentHeading)))
            return;

        foreach (var row in rows)
        {
            var index = fieldIndex >= 0 ? fieldIndex : 0;
            if (index < row.Length)
                Manifest.RecordField(_currentHeading, row[index]);
        }
    }

    public void BeginTable(
        TextWriter writer,
        ReadOnlySpan<string> headers,
        MarkoutWriterOptions options)
    {
        Manifest.RecordTable(_currentHeading, headers);
        _streamingTableSection = _currentHeading;
        _streamingFieldIndex = headers.IndexOf(
            "Field",
            StringComparer.OrdinalIgnoreCase);
        if (IsFieldSection(_currentHeading))
        {
            if (_streamingFieldIndex < 0)
                _streamingFieldIndex = 0;
        }
        else if (_streamingFieldIndex < 0 || IsColumnSection(_currentHeading))
        {
            _streamingFieldIndex = -1;
        }
    }

    public void WriteRow(TextWriter writer, ReadOnlySpan<string> values)
    {
        if (_streamingFieldIndex >= 0 && _streamingFieldIndex < values.Length)
            Manifest.RecordField(_streamingTableSection, values[_streamingFieldIndex]);
    }

    public void EndTable(TextWriter writer, int skippedRows)
    {
        _streamingTableSection = null;
        _streamingFieldIndex = -1;
    }

    private bool IsFieldSection(string? section)
        => section is not null && _fieldSections.Contains(section);

    private bool IsColumnSection(string? section)
        => section is not null && _columnSections.Contains(section);
}
