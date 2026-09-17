using Markout;
using Markout.Formatting;

namespace DotnetInspect.Cli.Output;

internal sealed class RenderedSectionManifest
{
    private readonly Dictionary<string, HashSet<string>> _tableColumns =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _tableDataColumns =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _fields =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _rootTableColumns =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _rootTableDataColumns =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _rootFields =
        new(StringComparer.OrdinalIgnoreCase);

    internal void RecordTable(string? section, ReadOnlySpan<string> columns)
    {
        HashSet<string> names;
        if (section is null)
        {
            names = _rootTableColumns;
        }
        else
        {
            names = GetOrCreateNames(_tableColumns, section);
        }

        foreach (string column in columns)
            names.Add(column);
    }

    internal void RecordTableData(
        string? section,
        ReadOnlySpan<string> columns,
        IList<string[]> rows)
    {
        if (rows.Count == 0)
            return;

        HashSet<string> names;
        if (section is null)
        {
            names = _rootTableDataColumns;
        }
        else
        {
            names = GetOrCreateNames(_tableDataColumns, section);
        }

        for (int column = 0; column < columns.Length; column++)
        {
            if (rows.Any(row =>
                    row.Length > column
                    && !string.IsNullOrWhiteSpace(row[column])))
            {
                names.Add(columns[column]);
            }
        }
    }

    internal void RecordTableDataColumn(string? section, string column)
    {
        HashSet<string> names;
        if (section is null)
        {
            names = _rootTableDataColumns;
        }
        else
        {
            names = GetOrCreateNames(_tableDataColumns, section);
        }

        names.Add(column);
    }

    internal bool HasTable(string section)
        => _tableColumns.ContainsKey(section);

    internal IReadOnlySet<string>? GetTableColumns(string section)
        => _tableColumns.TryGetValue(section, out var columns) ? columns : null;

    internal void RecordField(string? section, string field)
    {
        GetOrCreateFields(section).Add(field);
    }

    internal void RecordFields(string? section, IEnumerable<string> fields)
    {
        foreach (string field in fields)
            RecordField(section, field);
    }

    internal IReadOnlySet<string>? GetFields(string section)
        => _fields.TryGetValue(section, out var fields) ? fields : null;

    internal IReadOnlySet<string> GetRenderedNames(
        string itemKind,
        IReadOnlyCollection<string>? sections = null)
    {
        var names = new HashSet<string>(
            GetRootRenderedNames(itemKind),
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<string>> sectionNames =
            GetSectionNames(itemKind);
        AddSectionNames(sectionNames, sections, names);

        return names;
    }

    internal IReadOnlySet<string> GetRootRenderedNames(string itemKind)
        => itemKind.Equals("field", StringComparison.OrdinalIgnoreCase)
            ? _rootFields
            : itemKind.Equals("column", StringComparison.OrdinalIgnoreCase)
                ? _rootTableDataColumns
                : EmptyNames;

    internal IReadOnlySet<string> GetSectionRenderedNames(
        string itemKind,
        string section)
    {
        Dictionary<string, HashSet<string>> names = GetSectionNames(itemKind);
        return names.TryGetValue(section, out HashSet<string>? sectionNames)
            ? sectionNames
            : EmptyNames;
    }

    internal bool HasAnyData =>
        _rootFields.Count > 0
        || _rootTableDataColumns.Count > 0
        || _fields.Values.Any(static fields => fields.Count > 0)
        || _tableDataColumns.Values.Any(static columns => columns.Count > 0);

    internal void ReplaceSectionsFrom(
        RenderedSectionManifest source,
        IEnumerable<string> sections)
    {
        foreach (var section in sections)
        {
            _tableColumns.Remove(section);
            if (source._tableColumns.TryGetValue(section, out var columns))
            {
                _tableColumns[section] = new HashSet<string>(
                    columns,
                    StringComparer.OrdinalIgnoreCase);
            }

            _tableDataColumns.Remove(section);
            if (source._tableDataColumns.TryGetValue(section, out var dataColumns))
            {
                _tableDataColumns[section] = new HashSet<string>(
                    dataColumns,
                    StringComparer.OrdinalIgnoreCase);
            }

            _fields.Remove(section);
            if (source._fields.TryGetValue(section, out var fields))
            {
                _fields[section] = new HashSet<string>(
                    fields,
                    StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    internal void MergeRenderedFieldTablesFrom(
        RenderedSectionManifest source)
    {
        if (_rootTableColumns.Count > 0)
            _rootFields.UnionWith(source._rootFields);

        foreach ((string section, HashSet<string> fields) in source._fields)
        {
            if (_tableColumns.ContainsKey(section))
                GetOrCreateFields(section).UnionWith(fields);
        }
    }

    private HashSet<string> GetOrCreateFields(string? section)
    {
        if (section is null)
            return _rootFields;

        if (!_fields.TryGetValue(section, out var names))
        {
            names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _fields[section] = names;
        }

        return names;
    }

    private static HashSet<string> GetOrCreateNames(
        Dictionary<string, HashSet<string>> namesBySection,
        string section)
    {
        if (!namesBySection.TryGetValue(section, out var names))
        {
            names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            namesBySection[section] = names;
        }

        return names;
    }

    private static void AddSectionNames(
        Dictionary<string, HashSet<string>> source,
        IReadOnlyCollection<string>? sections,
        HashSet<string> destination)
    {
        if (sections is null)
        {
            foreach (HashSet<string> names in source.Values)
                destination.UnionWith(names);
            return;
        }

        foreach (string section in sections)
        {
            if (source.TryGetValue(section, out HashSet<string>? names))
                destination.UnionWith(names);
        }
    }

    private Dictionary<string, HashSet<string>> GetSectionNames(
        string itemKind)
        => itemKind.Equals("field", StringComparison.OrdinalIgnoreCase)
            ? _fields
            : itemKind.Equals("column", StringComparison.OrdinalIgnoreCase)
                ? _tableDataColumns
                : EmptySectionNames;

    private static IReadOnlySet<string> EmptyNames { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, HashSet<string>> EmptySectionNames { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class RenderManifestFormatter :
    IMarkoutFormatter,
    IHeadingFormatter,
    IFieldFormatter,
    ITableFormatter,
    IStreamingTableFormatter
{
    private readonly HashSet<string> _fieldSections;
    private readonly Dictionary<string, Dictionary<string, string>> _itemNamesBySection =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _globalItemNames =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ambiguousGlobalItemNames =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _currentHeading;
    private string? _streamingTableSection;
    private bool _streamingFieldTable;
    private string[] _streamingHeaders = [];
    private bool[] _streamingDataColumns = [];
    private int _streamingFieldNameColumn = -1;
    private int _streamingFieldValueColumn = -1;
    private int _sectionHeadingLevel;
    private readonly string? _rootSection;
    private readonly bool _lockRootScope;

    internal RenderManifestFormatter(
        DocumentSchema schema,
        string? rootSection = null,
        bool lockRootScope = false)
    {
        _rootSection = rootSection;
        _lockRootScope = lockRootScope || rootSection is not null;
        _fieldSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string sectionName in schema.SectionNames)
        {
            var section = schema.GetSection(sectionName);
            if (section is null)
                continue;

            if (section.ItemKind.Equals("field", StringComparison.OrdinalIgnoreCase))
                _fieldSections.Add(section.Name);

            var names = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var item in schema.Discover(sectionName) ?? [])
            {
                AddItemName(names, item.Name, item.Name);
                AddItemName(names, item.StableName, item.Name);
                AddItemName(names, item.Key, item.Name);
                AddGlobalItemName(item.Name, item.Name);
                AddGlobalItemName(item.StableName, item.Name);
                AddGlobalItemName(item.Key, item.Name);
            }

            _itemNamesBySection[section.Name] = names;
        }
    }

    internal RenderedSectionManifest Manifest { get; } = new();

    internal static RenderedSectionManifest Capture<T>(
        T value,
        MarkoutSerializerContext context,
        MarkoutWriterOptions options,
        DocumentSchema schema,
        string? rootSection = null,
        bool lockRootScope = false)
    {
        var formatter = new RenderManifestFormatter(
            schema,
            rootSection,
            lockRootScope);
        formatter.BeginDocument(options);
        var writer = new MarkoutWriter(TextWriter.Null, formatter, options);
        context.Serialize(value, writer);
        writer.Flush();
        return formatter.Manifest;
    }

    internal void BeginDocument(MarkoutWriterOptions options)
    {
        _currentHeading = _rootSection;
        _streamingTableSection = null;
        _streamingFieldTable = false;
        _streamingHeaders = [];
        _streamingDataColumns = [];
        _streamingFieldNameColumn = -1;
        _streamingFieldValueColumn = -1;
        _sectionHeadingLevel = Math.Clamp(2 + options.HeadingLevelOffset, 1, 6);
    }

    public void FormatHeading(TextWriter writer, int level, string text, string? context)
    {
        if (!_lockRootScope
            && level == _sectionHeadingLevel)
            _currentHeading = text;
    }

    public void FormatFieldName(TextWriter writer, string key, bool bold)
        => Manifest.RecordField(
            _currentHeading,
            CanonicalizeItemName(_currentHeading, key));

    public void FormatFields(TextWriter writer, MarkoutField[] fields, bool bold)
        => RecordFields(fields);

    public void FormatFields(TextWriter writer, ReadOnlySpan<MarkoutField> fields, bool bold)
        => RecordFields(fields);

    public void FormatTable(
        TextWriter writer,
        ReadOnlySpan<string> headers,
        IList<string[]> rows,
        int skippedRows,
        MarkoutWriterOptions options)
    {
        string[] canonicalHeaders = CanonicalizeHeaders(
            _currentHeading,
            headers);
        Manifest.RecordTable(_currentHeading, canonicalHeaders);
        Manifest.RecordTableData(
            _currentHeading,
            canonicalHeaders,
            rows);

        if (IsFieldSection(_currentHeading))
        {
            (int fieldColumn, int valueColumn) =
                GetFieldTableColumns(canonicalHeaders);
            foreach (var row in rows)
            {
                if (fieldColumn >= 0
                    && valueColumn >= 0
                    && row.Length > fieldColumn
                    && row.Length > valueColumn
                    && !string.IsNullOrWhiteSpace(row[valueColumn]))
                {
                    Manifest.RecordField(
                        _currentHeading,
                        CanonicalizeItemName(
                            _currentHeading,
                            row[fieldColumn]));
                }
            }
        }
    }

    public void BeginTable(
        TextWriter writer,
        ReadOnlySpan<string> headers,
        MarkoutWriterOptions options)
    {
        string[] canonicalHeaders = CanonicalizeHeaders(
            _currentHeading,
            headers);
        Manifest.RecordTable(_currentHeading, canonicalHeaders);
        _streamingTableSection = _currentHeading;
        _streamingFieldTable = IsFieldSection(_currentHeading);
        _streamingHeaders = canonicalHeaders;
        _streamingDataColumns = new bool[headers.Length];
        (_streamingFieldNameColumn, _streamingFieldValueColumn) =
            _streamingFieldTable
                ? GetFieldTableColumns(canonicalHeaders)
                : (-1, -1);
    }

    public void WriteRow(TextWriter writer, ReadOnlySpan<string> values)
    {
        if (_streamingFieldTable)
        {
            if (_streamingFieldNameColumn >= 0
                && _streamingFieldValueColumn >= 0
                && values.Length > _streamingFieldNameColumn
                && values.Length > _streamingFieldValueColumn
                && !string.IsNullOrWhiteSpace(
                    values[_streamingFieldValueColumn]))
            {
                Manifest.RecordField(
                    _streamingTableSection,
                    CanonicalizeItemName(
                        _streamingTableSection,
                        values[_streamingFieldNameColumn]));
            }
        }

        for (int column = 0;
            column < values.Length && column < _streamingDataColumns.Length;
            column++)
        {
            if (!string.IsNullOrWhiteSpace(values[column]))
                _streamingDataColumns[column] = true;
        }
    }

    public void EndTable(TextWriter writer, int skippedRows)
    {
        for (int column = 0;
            column < _streamingHeaders.Length;
            column++)
        {
            if (_streamingDataColumns[column])
                Manifest.RecordTableDataColumn(
                    _streamingTableSection,
                    _streamingHeaders[column]);
        }

        _streamingTableSection = null;
        _streamingFieldTable = false;
        _streamingHeaders = [];
        _streamingDataColumns = [];
        _streamingFieldNameColumn = -1;
        _streamingFieldValueColumn = -1;
    }

    private bool IsFieldSection(string? section)
        => section is not null && _fieldSections.Contains(section);

    private void RecordFields(ReadOnlySpan<MarkoutField> fields)
    {
        foreach (MarkoutField field in fields)
        {
            Manifest.RecordField(
                _currentHeading,
                CanonicalizeItemName(
                    _currentHeading,
                    field.Key));
        }
    }

    private string[] CanonicalizeHeaders(
        string? section,
        ReadOnlySpan<string> headers)
    {
        var canonical = new string[headers.Length];
        for (int index = 0; index < headers.Length; index++)
            canonical[index] = CanonicalizeItemName(section, headers[index]);
        return canonical;
    }

    private static (int Field, int Value) GetFieldTableColumns(
        string[] headers)
    {
        int field = Array.FindIndex(
            headers,
            static header => header.Equals(
                "Field",
                StringComparison.OrdinalIgnoreCase));
        int value = Array.FindIndex(
            headers,
            static header => header.Equals(
                "Value",
                StringComparison.OrdinalIgnoreCase));
        if (field >= 0 || value >= 0)
            return field >= 0 && value >= 0
                ? (field, value)
                : (-1, -1);

        return headers.Length >= 2
            ? (0, 1)
            : (-1, -1);
    }

    private string CanonicalizeItemName(string? section, string name)
    {
        if (section is not null
            && _itemNamesBySection.TryGetValue(section, out var sectionNames)
            && sectionNames.TryGetValue(name, out string? canonical))
        {
            return canonical;
        }

        if (!_ambiguousGlobalItemNames.Contains(name)
            && _globalItemNames.TryGetValue(name, out string? globalCanonical))
        {
            return globalCanonical;
        }

        return name;
    }

    private static void AddItemName(
        Dictionary<string, string> names,
        string? alias,
        string canonical)
    {
        if (!string.IsNullOrEmpty(alias))
            names[alias] = canonical;
    }

    private void AddGlobalItemName(string? alias, string canonical)
    {
        if (string.IsNullOrEmpty(alias)
            || _ambiguousGlobalItemNames.Contains(alias))
        {
            return;
        }

        if (_globalItemNames.TryGetValue(alias, out string? existing)
            && !existing.Equals(canonical, StringComparison.OrdinalIgnoreCase))
        {
            _globalItemNames.Remove(alias);
            _ambiguousGlobalItemNames.Add(alias);
            return;
        }

        _globalItemNames[alias] = canonical;
    }
}
