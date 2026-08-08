using Markout;
using Markout.Formatting;

namespace DotnetInspector.Output;

internal sealed class ProjectionValidationException(string message) : Exception(message);

internal static class ProjectionHeaderValidation
{
    internal const string DuplicateResolvedColumnMessage =
        "Column projection resolves the same column more than once. Use non-overlapping --columns patterns.";

    internal static void RejectDuplicateResolvedColumns(ReadOnlySpan<string> headers)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var header in headers)
        {
            if (!seen.Add(header))
                throw new ProjectionValidationException(DuplicateResolvedColumnMessage);
        }
    }
}

/// <summary>
/// Validates the projected headers Markout resolved before delegating to its table formatter.
/// </summary>
internal sealed class ProjectionValidatingTableFormatter(IMarkoutFormatter formatter) :
    IMarkoutFormatter,
    ITableFormatter,
    IFieldFormatter,
    IListFormatter,
    ICompositeCellFormatter,
    IGraphFormatter
{
    private readonly ITableFormatter _tableFormatter = (ITableFormatter)formatter;
    private readonly IFieldFormatter _fieldFormatter = (IFieldFormatter)formatter;
    private readonly IListFormatter _listFormatter = (IListFormatter)formatter;
    private readonly ICompositeCellFormatter _compositeCellFormatter = (ICompositeCellFormatter)formatter;
    private readonly IGraphFormatter _graphFormatter = (IGraphFormatter)formatter;

    public bool DecomposesCompositeCells => _compositeCellFormatter.DecomposesCompositeCells;

    public void FormatTable(
        TextWriter writer,
        ReadOnlySpan<string> headers,
        IList<string[]> rows,
        int skippedRows,
        MarkoutWriterOptions options)
    {
        if (options.Projection?.IncludeColumns is { Count: > 0 })
            ProjectionHeaderValidation.RejectDuplicateResolvedColumns(headers);

        _tableFormatter.FormatTable(writer, headers, rows, skippedRows, options);
    }

    public void FormatFieldName(TextWriter writer, string key, bool bold)
        => _fieldFormatter.FormatFieldName(writer, key, bold);

    public void FormatFields(TextWriter writer, MarkoutField[] fields, bool bold)
        => _fieldFormatter.FormatFields(writer, fields, bold);

    public void FormatFields(TextWriter writer, ReadOnlySpan<MarkoutField> fields, bool bold)
        => _fieldFormatter.FormatFields(writer, fields, bold);

    public void FormatListItem(TextWriter writer, string item)
        => _listFormatter.FormatListItem(writer, item);

    public void FormatArray(TextWriter writer, string name, ReadOnlySpan<string> values, bool bold)
        => _listFormatter.FormatArray(writer, name, values, bold);

    public void FormatGraph(TextWriter writer, Graph graph, MarkoutWriterOptions options)
        => _graphFormatter.FormatGraph(writer, graph, options);
}
