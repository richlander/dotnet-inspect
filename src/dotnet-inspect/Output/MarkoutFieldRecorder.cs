using Markout;
using Markout.Formatting;

namespace DotnetInspector.Output;

/// <summary>
/// Captures the key/value fields a Markout view declares, by serializing the view through the
/// ordinary formatter seam and recording the two-column field table Markout renders for a
/// <see cref="FieldLayout.Table"/> document.
/// </summary>
/// <remarks>
/// A sink that lowers fields differently from a Markdown field table — the JSON view names them
/// as object keys — needs the same labels and values the generated serializer declares. Reading
/// them back from that serializer keeps one source of truth: a field added to, removed from, or
/// relabelled on the view cannot silently disagree with what the other sink emits.
/// </remarks>
internal sealed class MarkoutFieldRecorder : IMarkoutFormatter, ITableFormatter
{
    private readonly List<MarkoutField> _fields = [];

    /// <summary>
    /// Records the fields <paramref name="value"/> declares, in declaration order.
    /// </summary>
    /// <remarks>
    /// The caller passes a view whose section row sets are all absent, so the only table this
    /// document renders is its field table.
    /// </remarks>
    public static MarkoutField[] Record<T>(
        T value,
        MarkoutSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var recorder = new MarkoutFieldRecorder();
        MarkoutSerializer.Serialize(
            value,
            TextWriter.Null,
            recorder,
            context,
            new MarkoutWriterOptions());
        return [.. recorder._fields];
    }

    public void FormatTable(
        TextWriter writer,
        ReadOnlySpan<string> headers,
        IList<string[]> rows,
        int skippedRows,
        MarkoutWriterOptions options)
    {
        // Recording a table that is not the two-column field table would silently reinterpret
        // row data as fields. Refusing keeps the mismatch visible instead of emitting a
        // plausible-looking field set the view never declared.
        if (headers.Length != 2)
        {
            throw new NotSupportedException(
                $"A field recorder received a {headers.Length}-column table "
                + $"([{string.Join(", ", headers.ToArray())}]). Only the two-column field "
                + "table a FieldLayout.Table document renders can be read back as fields.");
        }

        foreach (string[] row in rows)
        {
            if (row.Length == 2)
                _fields.Add(new MarkoutField(row[0], row[1]));
        }
    }
}
