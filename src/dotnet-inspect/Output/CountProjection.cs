using Markout;
using Markout.Formatting;

namespace DotnetInspector.Output;

/// <summary>
/// The table-row cardinality observed after section, field, column, and row-window projection.
/// </summary>
internal sealed class CountProjection
{
    private readonly Dictionary<string, int> _sectionCounts =
        new(StringComparer.OrdinalIgnoreCase);

    internal int Total { get; private set; }

    internal bool WroteAnyContent { get; private set; }

    internal IReadOnlyDictionary<string, int> SectionCounts => _sectionCounts;

    internal void RecordContent() => WroteAnyContent = true;

    internal void RecordRows(string? section, int rowCount)
    {
        WroteAnyContent = true;
        Total += rowCount;
        if (section is not null)
            _sectionCounts[section] = _sectionCounts.GetValueOrDefault(section) + rowCount;
    }

    internal void RecordTable(string? section, int rowCount)
        => RecordRows(section, rowCount);

    internal void SetRows(string section, int rowCount)
    {
        WroteAnyContent = true;
        Total += rowCount - _sectionCounts.GetValueOrDefault(section);
        _sectionCounts[section] = rowCount;
    }

    internal void Merge(CountProjection other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Total += other.Total;
        WroteAnyContent |= other.WroteAnyContent;
        foreach (var (section, count) in other.SectionCounts)
            _sectionCounts[section] = _sectionCounts.GetValueOrDefault(section) + count;
    }
}

/// <summary>
/// A non-rendering Markout formatter that observes the same selected and windowed table rows as
/// the Markdown document formatter.
/// </summary>
internal sealed class CountProjectionFormatter :
    IMarkoutFormatter,
    IDocumentFormatter,
    IMetricsFormatter,
    IStreamingTableFormatter,
    IGlyphFormatter,
    IEmphasisFormatter,
    IGraphFormatter
{
    private string? _currentSection;
    private string? _streamingTableSection;
    private int _sectionHeadingLevel;

    internal CountProjection Projection { get; } = new();

    internal static CountProjection Capture<T>(
        T value,
        MarkoutSerializerContext context,
        MarkoutWriterOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Capture(writer => context.Serialize(value, writer), options);
    }

    internal static CountProjection Capture(
        Action<MarkoutWriter> serialize,
        MarkoutWriterOptions options)
    {
        ArgumentNullException.ThrowIfNull(serialize);
        ArgumentNullException.ThrowIfNull(options);

        var formatter = new CountProjectionFormatter();
        formatter.BeginDocument(options);
        var writer = new MarkoutWriter(TextWriter.Null, formatter, options);
        serialize(writer);
        writer.Flush();
        return formatter.Projection;
    }

    private void BeginDocument(MarkoutWriterOptions options)
    {
        _currentSection = null;
        _streamingTableSection = null;
        _sectionHeadingLevel = Math.Clamp(2 + options.HeadingLevelOffset, 1, 6);
    }

    public void FormatHeading(TextWriter writer, int level, string text, string? context)
    {
        Projection.RecordContent();
        if (level == _sectionHeadingLevel)
            _currentSection = text;
    }

    public void FormatFieldName(TextWriter writer, string key, bool bold)
        => Projection.RecordContent();

    public void FormatFields(TextWriter writer, MarkoutField[] fields, bool bold)
        => Projection.RecordContent();

    public void FormatFields(TextWriter writer, ReadOnlySpan<MarkoutField> fields, bool bold)
        => Projection.RecordContent();

    public void FormatTable(
        TextWriter writer,
        ReadOnlySpan<string> headers,
        IList<string[]> rows,
        int skippedRows,
        MarkoutWriterOptions options)
        => Projection.RecordTable(_currentSection, rows.Count);

    public void BeginTable(
        TextWriter writer,
        ReadOnlySpan<string> headers,
        MarkoutWriterOptions options)
    {
        Projection.RecordTable(_currentSection, 0);
        _streamingTableSection = _currentSection;
    }

    public void WriteRow(TextWriter writer, ReadOnlySpan<string> values)
        => Projection.RecordTable(_streamingTableSection, 1);

    public void EndTable(TextWriter writer, int skippedRows)
        => _streamingTableSection = null;

    public void FormatListItem(TextWriter writer, string item)
        => Projection.RecordContent();

    public void FormatArray(
        TextWriter writer,
        string key,
        ReadOnlySpan<string> items,
        bool bold)
        => Projection.RecordContent();

    public void FormatCodeStart(TextWriter writer, string? language)
        => Projection.RecordContent();

    public void FormatCodeEnd(TextWriter writer)
    {
    }

    public void FormatParagraph(TextWriter writer, string text)
        => Projection.RecordContent();

    public void FormatCallout(TextWriter writer, CalloutSeverity severity, string text)
        => Projection.RecordContent();

    public void FormatQuotation(TextWriter writer, string text)
        => Projection.RecordContent();

    public void FormatRule(TextWriter writer)
        => Projection.RecordContent();

    public void FormatDescription(TextWriter writer, Description description)
        => Projection.RecordContent();

    public void FormatTree(
        TextWriter writer,
        ReadOnlySpan<TreeNode> nodes,
        MarkoutWriterOptions options)
        => Projection.RecordContent();

    public void FormatTreeNode(TextWriter writer, string indent, string text)
        => Projection.RecordContent();

    public void FormatBreakdown(
        TextWriter writer,
        IReadOnlyList<Breakdown> breakdowns,
        int? width,
        bool showValues,
        MarkoutWriterOptions options)
        => Projection.RecordContent();

    public void FormatMetrics(
        TextWriter writer,
        IReadOnlyList<Metric> metrics,
        int width,
        MarkoutWriterOptions options)
        => Projection.RecordContent();

    public void FormatVerticalMetrics(
        TextWriter writer,
        IReadOnlyList<Metric> metrics,
        int height,
        int? width,
        MarkoutWriterOptions options)
        => Projection.RecordContent();

    public void FormatGraph(TextWriter writer, Graph graph, MarkoutWriterOptions options)
        => Projection.RecordContent();

    public string Emphasize(string text) => text;
}
