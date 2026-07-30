using System.Buffers;
using System.Text.Json;
using Markout;
using Markout.Formatting;

namespace DotnetInspector.Output;

/// <summary>
/// Renders a Markout view as JSON by implementing the same formatter seam Markdown, plain text,
/// TSV, and JSONL use, rather than by serializing a separate typed graph.
/// </summary>
/// <remarks>
/// <para>
/// This is the <em>lowered</em> JSON view (dotnet-inspect#3494). A caller reaches it by naming
/// <c>--fields</c>/<c>--columns</c>, which select from the post-lowering vocabulary: computed
/// table columns such as <c>Return Type</c> have no counterpart in the typed object model, so
/// naming one is an opt-in to the display view. Plain <c>--json</c> keeps the pre-lowered typed
/// shape and does not come through here.
/// </para>
/// <para>
/// Because Markout's formatter seam carries display strings — <see cref="MarkoutField"/> is
/// <c>(string Key, string Value)</c> and table rows are <c>string[]</c> — the JSON this emits has
/// title-cased keys and stringized values (<c>"Is Static": "yes"</c>, not <c>"is_static": true</c>).
/// That is the display view the caller asked for, not a fidelity regression in the machine
/// contract; richlander/markout#173 and #175 would let this carry typed values and machine keys.
/// </para>
/// <para>
/// Section and projection decisions arrive already applied: Markout honors
/// <see cref="MarkoutWriterOptions.IncludeSections"/> and <see cref="MarkoutWriterOptions.Projection"/>
/// before invoking the formatter, so an excluded section never announces a heading and a projected
/// column never reaches <see cref="FormatTable"/>. That is what makes this Format-invariant with
/// the table formats instead of a second projection implementation.
/// </para>
/// <para>
/// Emission is deferred to <see cref="Finish"/>: JSON nests, so a section's shape is not known
/// until its content arrives. The seam has no end-of-document callback, so the caller flushes the
/// <see cref="MarkoutWriter"/> and then calls <see cref="Finish"/>. Leaves are written as JSON
/// strings through <see cref="Utf8JsonWriter"/>, which keeps the path NativeAOT-safe.
/// </para>
/// </remarks>
internal sealed class JsonSectionFormatter :
    IMarkoutFormatter,
    IHeadingFormatter,
    IFieldFormatter,
    ITableFormatter,
    IStreamingTableFormatter,
    IListFormatter,
    ITreeFormatter
{
    private enum SectionKind
    {
        Fields,
        Table,
        List,
        Tree,
    }

    private sealed class Section(string name, SectionKind kind)
    {
        public string Name { get; } = name;
        public SectionKind Kind { get; private set; } = kind;
        public string[] Headers { get; private set; } = [];
        public List<string[]> Rows { get; } = [];
        public List<MarkoutField> Fields { get; } = [];
        public List<string> Items { get; } = [];
        public List<TreeNode> Tree { get; } = [];

        /// <summary>
        /// Records that <paramref name="incoming"/> content is being added to this section.
        /// </summary>
        /// <remarks>
        /// Appending content of the kind a section already holds is lossless: fields merge into one
        /// object, list items and tree nodes into one array. Changing the kind is not, because a
        /// section serializes as exactly one JSON value, so whichever collection did not match the
        /// final kind would vanish from the document without a word.
        ///
        /// That case is a gap in this projection, not a caller error, and the repository rule is to
        /// keep failure visible rather than emit success-shaped output that quietly lost data. It is
        /// unreachable from the single-table views wired today (dotnet-inspect#3494); throwing is
        /// what forces the mixed-content shape to be designed when a multi-section view first needs
        /// it, instead of shipping silent truncation.
        /// </remarks>
        public void Adopt(SectionKind incoming)
        {
            var occupied = Rows.Count > 0 || Fields.Count > 0 || Items.Count > 0 || Tree.Count > 0;
            if (occupied && Kind != incoming)
            {
                throw new NotSupportedException(
                    $"Section '{(Name.Length == 0 ? "<unnamed>" : Name)}' mixes {Kind} and {incoming} content, " +
                    "which the JSON view cannot represent as one value. Split the section or extend JsonSectionFormatter.");
            }

            Kind = incoming;
        }

        /// <summary>
        /// Adopts the header set for this section's table. A second table in the same section would
        /// otherwise replace the headers while its rows append to the first table's, silently
        /// re-labelling data that was already buffered under different columns.
        /// </summary>
        public void SetHeaders(ReadOnlySpan<string> headers)
        {
            if (Rows.Count > 0 && !headers.SequenceEqual(Headers))
            {
                throw new NotSupportedException(
                    $"Section '{(Name.Length == 0 ? "<unnamed>" : Name)}' holds two tables with different columns " +
                    $"([{string.Join(", ", Headers)}] then [{string.Join(", ", headers.ToArray())}]), " +
                    "which the JSON view cannot represent as one array.");
            }

            Headers = headers.ToArray();
        }
    }

    private readonly List<MarkoutField> _rootFields = [];
    private readonly List<Section> _sections = [];
    private Section? _current;
    private Section? _streamingTable;
    private int _sectionLevel = 2;
    private RowWindow? _rows;

    /// <summary>
    /// Resets heading tracking for a new document. <paramref name="options"/> supplies the same
    /// heading-level offset Markout applies, so the section level is recognized even when a caller
    /// nests the view under another document.
    /// </summary>
    /// <param name="rows">
    /// The <c>--rows</c> window to apply to table sections, or null for every row. The window is
    /// applied to buffered data rows rather than to rendered text: a row window is a Shape
    /// decision, and a line-oriented limiter would cut a pretty-printed document mid-object.
    /// </param>
    internal void BeginDocument(MarkoutWriterOptions options, RowWindow? rows = null)
    {
        _rootFields.Clear();
        _sections.Clear();
        _current = null;
        _streamingTable = null;
        _sectionLevel = Math.Clamp(2 + options.HeadingLevelOffset, 1, 6);
        _rows = rows;
    }

    /// <summary>
    /// Names the sections that produced content, in emission order. Callers use this to report
    /// which sections a projection actually reached.
    /// </summary>
    internal IReadOnlyList<string> SectionNames => _sections.Select(section => section.Name).ToArray();

    public void FormatHeading(TextWriter writer, int level, string text, string? context)
    {
        // Only the section level starts a new JSON property. A deeper heading is content within
        // the current section, and a shallower one is the document title, which the caller owns.
        if (level == _sectionLevel)
            _current = GetOrAddSection(text, SectionKind.Fields);
    }

    public void FormatFieldName(TextWriter writer, string key, bool bold)
    {
        // A bare field name carries no value; it is a label Markdown renders before a value block.
        // The value arrives through FormatFields, so nothing is recorded here.
    }

    public void FormatFields(TextWriter writer, MarkoutField[] fields, bool bold)
        => FormatFields(writer, (ReadOnlySpan<MarkoutField>)fields, bold);

    public void FormatFields(TextWriter writer, ReadOnlySpan<MarkoutField> fields, bool bold)
    {
        // Fields emitted before any section heading are the document's own top-level fields.
        if (_current is null)
        {
            foreach (var field in fields)
                _rootFields.Add(field);
            return;
        }

        _current.Adopt(SectionKind.Fields);
        foreach (var field in fields)
            _current.Fields.Add(field);
    }

    public void FormatTable(
        TextWriter writer,
        ReadOnlySpan<string> headers,
        IList<string[]> rows,
        int skippedRows,
        MarkoutWriterOptions options)
    {
        var section = RequireSection(SectionKind.Table);
        section.SetHeaders(headers);
        foreach (var row in rows)
            section.Rows.Add(row);
    }

    public void BeginTable(TextWriter writer, ReadOnlySpan<string> headers, MarkoutWriterOptions options)
    {
        var section = RequireSection(SectionKind.Table);
        section.SetHeaders(headers);
        _streamingTable = section;
    }

    public void WriteRow(TextWriter writer, ReadOnlySpan<string> values)
        => _streamingTable?.Rows.Add(values.ToArray());

    public void EndTable(TextWriter writer, int skippedRows)
        => _streamingTable = null;

    public void FormatArray(TextWriter writer, string name, ReadOnlySpan<string> values, bool bold)
    {
        var section = RequireSection(SectionKind.List);
        foreach (var value in values)
            section.Items.Add(value);
    }

    public void FormatListItem(TextWriter writer, string item)
    {
        var section = RequireSection(SectionKind.List);
        section.Items.Add(item);
    }

    public void FormatTree(TextWriter writer, ReadOnlySpan<TreeNode> nodes, MarkoutWriterOptions options)
    {
        var section = RequireSection(SectionKind.Tree);
        foreach (var node in nodes)
            section.Tree.Add(node);
    }

    public void FormatTreeNode(TextWriter writer, string text, string badge)
    {
        var section = RequireSection(SectionKind.Tree);
        section.Tree.Add(new TreeNode(text) { Badge = badge });
    }

    /// <summary>
    /// Serializes the buffered document. Call after flushing the <see cref="MarkoutWriter"/>.
    /// </summary>
    internal string Finish(bool indented = true)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented }))
        {
            json.WriteStartObject();

            foreach (var field in _rootFields)
                json.WriteString(MachineKey(field.Key), field.Value);

            foreach (var section in _sections)
                WriteSection(json, section, _rows);

            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Converts a display heading or field label into the machine key a JSON consumer expects
    /// ("Call Graph" to "call_graph").
    /// </summary>
    /// <remarks>
    /// This is the same policy the pre-lowered serializers declare
    /// (<c>JsonKnownNamingPolicy.SnakeCaseLower</c>), so <c>--json</c> does not change key casing
    /// depending on whether a projection was requested. Table headers deliberately do not come
    /// through here: Markout is asked for its JSONL vocabulary, which already supplies machine
    /// names, and that is what keeps a projected row byte-identical to the same row under
    /// <c>--jsonl</c>. It is a pure string transform, so it stays NativeAOT-safe.
    /// </remarks>
    private static string MachineKey(string display) =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(display);

    private static void WriteSection(Utf8JsonWriter json, Section section, RowWindow? rows)
    {
        switch (section.Kind)
        {
            case SectionKind.Table:
                json.WriteStartArray(MachineKey(section.Name));
                // Resolve the --rows window over the buffered rows. RowWindow.Resolve is the single
                // place head/tail/range semantics are interpreted, so JSON keeps the same window the
                // table formats get instead of reinterpreting the flag.
                var (start, end) = rows is { IsUnlimited: false } window
                    ? window.Resolve(section.Rows.Count)
                    : (0, section.Rows.Count);
                for (var r = start; r < end; r++)
                {
                    var row = section.Rows[r];
                    json.WriteStartObject();
                    // A row shorter than the header set is a Markout padding artifact, not data;
                    // stopping at the shorter of the two avoids inventing keys with null values.
                    var count = Math.Min(section.Headers.Length, row.Length);
                    for (var i = 0; i < count; i++)
                        json.WriteString(section.Headers[i], row[i]);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                break;

            case SectionKind.List:
                json.WriteStartArray(MachineKey(section.Name));
                foreach (var item in section.Items)
                    json.WriteStringValue(item);
                json.WriteEndArray();
                break;

            case SectionKind.Tree:
                json.WriteStartArray(MachineKey(section.Name));
                foreach (var node in section.Tree)
                    WriteTreeNode(json, node);
                json.WriteEndArray();
                break;

            default:
                json.WriteStartObject(MachineKey(section.Name));
                foreach (var field in section.Fields)
                    json.WriteString(MachineKey(field.Key), field.Value);
                json.WriteEndObject();
                break;
        }
    }

    private static void WriteTreeNode(Utf8JsonWriter json, TreeNode node)
    {
        json.WriteStartObject();
        json.WriteString("text", node.Text);
        if (!string.IsNullOrEmpty(node.Badge))
            json.WriteString("badge", node.Badge);
        if (node.Children is { Count: > 0 } children)
        {
            json.WriteStartArray("children");
            foreach (var child in children)
                WriteTreeNode(json, child);
            json.WriteEndArray();
        }
        json.WriteEndObject();
    }

    private Section RequireSection(SectionKind kind)
    {
        // Content can precede any heading when a view renders a single unnamed section. Give it a
        // stable home rather than dropping it, so no content disappears from the JSON.
        var section = _current ??= GetOrAddSection(string.Empty, kind);
        section.Adopt(kind);
        return section;
    }

    private Section GetOrAddSection(string name, SectionKind kind)
    {
        foreach (var existing in _sections)
        {
            if (string.Equals(existing.Name, name, StringComparison.Ordinal))
                return existing;
        }

        var section = new Section(name, kind);
        _sections.Add(section);
        return section;
    }
}
