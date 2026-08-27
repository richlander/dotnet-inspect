using System.Buffers;
using System.Text;
using System.Text.Json;
using DotnetInspector.Views;
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
/// Markout's formatter seam carries strings — <see cref="MarkoutField"/> is
/// <c>(string Key, string Value)</c> and table rows are <c>string[]</c>. Inline value slots can
/// contain Markout semantic markup, so this formatter applies the same plain-text rendering as
/// JSONL before JSON encoding. Values remain strings (<c>"yes"</c>, not <c>true</c>); that is the
/// lowered display contract, not a fidelity regression in the typed machine contract.
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
/// <para>
/// Markout shapes with no formatter callback write directly to the supplied text stream. This
/// formatter exposes <see cref="ContentWriter"/>, which ignores structural whitespace but rejects
/// content so code blocks, graphs, prose, and other unsupported shapes cannot disappear.
/// </para>
/// </remarks>
internal sealed class JsonSectionFormatter :
    IMarkoutFormatter,
    IHeadingFormatter,
    IFieldFormatter,
    IBlockFormatter,
    ICodeBlockFormatter,
    IGraphFormatter,
    ITableFormatter,
    IStreamingTableFormatter,
    IListFormatter,
    ITreeFormatter
{
    private enum SectionKind
    {
        Fields,
        Table,
        UnlabeledList,
        LabeledArrays,
        Tree,
    }

    private sealed record LabeledArray(string Name, string[] Values);
    private sealed record ProjectedTreeNode(
        string Text,
        string? Badge,
        TreeNodeState State,
        ProjectedTreeNode[] Children,
        bool IncludeBadge);

    private sealed class Section(string name, SectionKind kind)
    {
        private bool _hasContent;
        private bool _hasHeaders;

        public string Name { get; } = name;
        public SectionKind Kind { get; private set; } = kind;
        public bool HasContent => _hasContent;
        public string[] Headers { get; private set; } = [];
        public List<string[]> Rows { get; } = [];
        public List<MarkoutField> Fields { get; } = [];
        public List<string> Items { get; } = [];
        public List<LabeledArray> LabeledArrays { get; } = [];
        public List<ProjectedTreeNode> Tree { get; } = [];

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
        /// keep failure visible rather than emit success-shaped output that quietly lost data.
        /// Throwing forces each newly wired multi-section view to account for the shape instead of
        /// shipping silent truncation.
        /// </remarks>
        public void Adopt(SectionKind incoming)
        {
            if (_hasContent && Kind != incoming)
            {
                throw new NotSupportedException(
                    $"Section '{(Name.Length == 0 ? "<unnamed>" : Name)}' mixes {Kind} and {incoming} content, " +
                    "which the JSON view cannot represent as one value. Split the section or extend JsonSectionFormatter.");
            }

            Kind = incoming;
            _hasContent = true;
        }

        /// <summary>
        /// Adopts the header set for this section's table. A second table in the same section would
        /// otherwise replace the headers while its rows append to the first table's, silently
        /// re-labelling data that was already buffered under different columns.
        /// </summary>
        public void SetHeaders(ReadOnlySpan<string> headers)
        {
            if (_hasHeaders && !headers.SequenceEqual(Headers))
            {
                throw new NotSupportedException(
                    $"Section '{(Name.Length == 0 ? "<unnamed>" : Name)}' holds two tables with different columns " +
                    $"([{string.Join(", ", Headers)}] then [{string.Join(", ", headers.ToArray())}]), " +
                    "which the JSON view cannot represent as one array.");
            }

            // Each row serializes as an object keyed by these headers, so a repeated header emits a
            // repeated property -- silent loss again, reached through the table door rather than the
            // section door. BuildProjection already rejects a duplicated --columns entry, which is
            // where a user can cause this; the check is repeated here because this formatter is
            // shared infrastructure and a view could declare the collision itself.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var header in headers)
            {
                if (!seen.Add(header))
                {
                    throw new NotSupportedException(
                        $"Section '{(Name.Length == 0 ? "<unnamed>" : Name)}' has two columns named '{header}'. " +
                        "Each row would carry that JSON key twice, and a parser would keep only one.");
                }
            }

            Headers = headers.ToArray();
            _hasHeaders = true;
        }

        public void AddRows(IList<string[]> rows)
        {
            foreach (var row in rows)
                ValidateRowWidth(row.Length);

            foreach (var row in rows)
                Rows.Add(row.ToArray());
        }

        public void AddRow(ReadOnlySpan<string> values)
        {
            ValidateRowWidth(values.Length);
            Rows.Add(values.ToArray());
        }

        private void ValidateRowWidth(int width)
        {
            if (width > Headers.Length)
            {
                throw new NotSupportedException(
                    $"Section '{(Name.Length == 0 ? "<unnamed>" : Name)}' has a row with {width} cells " +
                    $"but only {Headers.Length} columns. The extra cells cannot be represented in JSON.");
            }
        }
    }

    private sealed class UnsupportedContentWriter(JsonSectionFormatter formatter) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (!char.IsWhiteSpace(value))
                ThrowUnsupportedContent(formatter.CurrentSectionName);
        }

        public override void Write(string? value)
        {
            if (value is not null)
                RejectNonWhitespace(value.AsSpan());
        }

        public override void Write(ReadOnlySpan<char> buffer) => RejectNonWhitespace(buffer);

        private void RejectNonWhitespace(ReadOnlySpan<char> value)
        {
            foreach (var character in value)
            {
                if (!char.IsWhiteSpace(character))
                    ThrowUnsupportedContent(formatter.CurrentSectionName);
            }
        }

        private static void ThrowUnsupportedContent(string section)
            => throw new NotSupportedException(
                $"Section '{section}' emitted text directly to the formatter stream, which the " +
                "lowered JSON view cannot represent. Select a table or field-set section, use " +
                "typed JSON, or extend JsonSectionFormatter for this shape.");
    }

    private readonly List<MarkoutField> _rootFields = [];
    private readonly List<Section> _sections = [];
    private readonly TextWriter _contentWriter;
    private IReadOnlyList<string>? _sectionOrder;
    private Section? _current;
    private Section? _streamingTable;
    private int _sectionLevel = 2;

    public JsonSectionFormatter() => _contentWriter = new UnsupportedContentWriter(this);

    internal TextWriter ContentWriter => _contentWriter;

    private string CurrentSectionName =>
        _current?.Name is { Length: > 0 } name ? name : "<document>";

    /// <summary>
    /// Resets heading tracking for a new document. <paramref name="options"/> supplies the same
    /// heading-level offset Markout applies, so the section level is recognized even when a caller
    /// nests the view under another document.
    /// </summary>
    internal void BeginDocument(MarkoutWriterOptions options)
    {
        _rootFields.Clear();
        _sections.Clear();
        _current = null;
        _streamingTable = null;
        _sectionOrder = options.SectionOrder?.ToArray();
        _sectionLevel = Math.Clamp(2 + options.HeadingLevelOffset, 1, 6);
    }

    /// <summary>
    /// Names the sections that produced content, in output order. Callers use this to report
    /// which sections a projection actually reached.
    /// </summary>
    internal IReadOnlyList<string> SectionNames =>
        SectionsInOutputOrder().Select(section => section.Name).ToArray();

    internal IReadOnlyList<string> EmittedSectionNames =>
        [.. SectionsInOutputOrder().Where(section => section.HasContent).Select(section => section.Name)];

    internal IReadOnlyList<string> EmittedColumns =>
        [.. SectionsInOutputOrder()
            .Where(section => section.Kind == SectionKind.Table)
            .SelectMany(section => section.Headers)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    internal IReadOnlyList<string> EmittedTableRowSections =>
        [.. SectionsInOutputOrder()
            .Where(section => section.Kind == SectionKind.Table && section.Rows.Count > 0)
            .Select(section => section.Name)];

    internal IReadOnlyList<OutputFormatter.ProjectedJsonFieldEvidence> EmittedFields
    {
        get
        {
            var fields = new List<OutputFormatter.ProjectedJsonFieldEvidence>();
            fields.AddRange(_rootFields.Select(
                item => new OutputFormatter.ProjectedJsonFieldEvidence(null, item.Key)));
            foreach (var section in SectionsInOutputOrder())
            {
                foreach (var item in section.Fields)
                {
                    fields.Add(new OutputFormatter.ProjectedJsonFieldEvidence(
                        section.Name,
                        item.Key));
                }

                if (section.Kind != SectionKind.Table)
                    continue;

                var fieldIndex = Array.FindIndex(
                    section.Headers,
                    header => string.Equals(header, "field", StringComparison.OrdinalIgnoreCase));
                if (fieldIndex < 0)
                    continue;

                foreach (var row in section.Rows)
                {
                    if (fieldIndex < row.Length)
                    {
                        fields.Add(new OutputFormatter.ProjectedJsonFieldEvidence(
                            section.Name,
                            row[fieldIndex]));
                    }
                }
            }

            return fields;
        }
    }

    internal void ReplaceSectionsFrom(
        JsonSectionFormatter source,
        IReadOnlyCollection<string> sectionNames)
    {
        foreach (var name in sectionNames)
        {
            var replacement = source._sections.FirstOrDefault(
                section => string.Equals(section.Name, name, StringComparison.OrdinalIgnoreCase));
            if (replacement is null)
                throw new InvalidOperationException(
                    $"Cannot replace section '{name}' because the unwindowed render did not produce it.");

            var index = _sections.FindIndex(
                section => string.Equals(section.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new InvalidOperationException(
                    $"Cannot replace section '{name}' because the windowed render did not produce it.");

            _sections[index] = replacement;
        }
    }

    public void FormatHeading(TextWriter writer, int level, string text, string? context)
    {
        RequireNoActiveStreamingTable("format a heading");

        // Only the section level starts a new JSON property. A deeper heading is content within
        // the current section, and a shallower one is the document title, which the caller owns.
        if (level == _sectionLevel)
            _current = GetOrAddSection(text, SectionKind.Fields);
    }

    public void FormatFieldName(TextWriter writer, string key, bool bold)
    {
        RequireNoActiveStreamingTable("format a field name");

        // Markout writes the associated value directly to the text stream after this callback.
        // Accepting the callback would lose the association between the key and that value.
        throw new NotSupportedException(
            $"Field '{key}' was emitted without its value at the formatter seam. " +
            "Use grouped field formatting or extend the seam to carry both key and value.");
    }

    public void FormatFields(TextWriter writer, MarkoutField[] fields, bool bold)
        => FormatFields(writer, (ReadOnlySpan<MarkoutField>)fields, bold);

    public void FormatFields(TextWriter writer, ReadOnlySpan<MarkoutField> fields, bool bold)
    {
        RequireNoActiveStreamingTable("format fields");

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

    public void FormatParagraph(TextWriter writer, string text)
    {
        // Type/member views emit their typed-document summary between the title and the selected
        // sections. Named field/column projection deliberately selects the lowered sections, not
        // that preamble; plain --json remains the path that carries the typed summary.
        if (_current is null)
            return;

        if (!_current.HasContent && ApiSectionEmptyText.IsDeclared(_current.Name, text))
            return;

        ThrowUnsupportedBlock("paragraph");
    }

    public void FormatCallout(TextWriter writer, CalloutSeverity severity, string message)
        => ThrowUnsupportedBlock("callout");

    public void FormatQuotation(TextWriter writer, string text) => ThrowUnsupportedBlock("quotation");

    public void FormatRule(TextWriter writer) => ThrowUnsupportedBlock("rule");

    public void FormatDescription(TextWriter writer, Description item)
        => ThrowUnsupportedBlock("description");

    public void FormatCodeStart(TextWriter writer, string? language) => ThrowUnsupportedBlock("code block");

    public void FormatCodeEnd(TextWriter writer) => ThrowUnsupportedBlock("code block");

    public void FormatGraph(TextWriter writer, Graph graph, MarkoutWriterOptions options)
        => ThrowUnsupportedBlock("graph");

    public void FormatTable(
        TextWriter writer,
        ReadOnlySpan<string> headers,
        IList<string[]> rows,
        int skippedRows,
        MarkoutWriterOptions options)
    {
        RequireNoActiveStreamingTable("format a table");

        var section = RequireSection(SectionKind.Table);
        section.SetHeaders(headers);
        section.AddRows(rows);
    }

    public void BeginTable(TextWriter writer, ReadOnlySpan<string> headers, MarkoutWriterOptions options)
    {
        if (_streamingTable is not null)
            throw new InvalidOperationException("Cannot begin a table while another streaming table is active.");

        var section = RequireSection(SectionKind.Table);
        section.SetHeaders(headers);
        _streamingTable = section;
    }

    public void WriteRow(TextWriter writer, ReadOnlySpan<string> values)
    {
        if (_streamingTable is null)
            throw new InvalidOperationException("Cannot write a row without an active streaming table.");

        _streamingTable.AddRow(values);
    }

    public void EndTable(TextWriter writer, int skippedRows)
    {
        if (_streamingTable is null)
            throw new InvalidOperationException("Cannot end a table without an active streaming table.");

        _streamingTable = null;
    }

    public void FormatArray(TextWriter writer, string name, ReadOnlySpan<string> values, bool bold)
    {
        RequireNoActiveStreamingTable("format an array");

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new NotSupportedException(
                "A labeled array reached the JSON formatter without a label. " +
                "Use list-item formatting for an unlabeled array.");
        }
        var section = RequireSection(SectionKind.LabeledArrays);
        section.LabeledArrays.Add(new LabeledArray(name, values.ToArray()));
        section.LabeledArrays.Add(new LabeledArray(name, values.ToArray()));
    }

    public void FormatListItem(TextWriter writer, string item)
    {
        RequireNoActiveStreamingTable("format a list item");

        var section = RequireSection(SectionKind.UnlabeledList);
        section.Items.Add(item);
    }

    public void FormatTree(TextWriter writer, ReadOnlySpan<TreeNode> nodes, MarkoutWriterOptions options)
    {
        RequireNoActiveStreamingTable("format a tree");

        var section = RequireSection(SectionKind.Tree);
        foreach (var node in nodes)
            section.Tree.Add(SnapshotTreeNode(node, options.IncludeBadges));
    }

    public void FormatTreeNode(TextWriter writer, string text, string prefix)
    {
        RequireNoActiveStreamingTable("format a tree node");

        // The streaming callback carries only a rendered hierarchy prefix, not the typed parent/
        // child relationship FormatTree receives. Storing that prefix as data would flatten the
        // tree and mislabel presentation glyphs as a badge.
        throw new NotSupportedException(
            $"Tree node '{text}' was emitted with a rendered hierarchy prefix that the JSON view " +
            "cannot reconstruct. Use typed tree formatting or extend the formatter seam.");
    }

    /// <summary>
    /// Serializes the buffered document. Call after flushing the <see cref="MarkoutWriter"/>.
    /// </summary>
    internal string Finish(bool indented = true)
    {
        if (_streamingTable is not null)
            throw new InvalidOperationException("Cannot finish the document while a streaming table is active.");

        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented }))
        {
            // Utf8JsonWriter does not reject a repeated property name -- it emits both, and the
            // consumer's parser silently keeps one. Two display names can normalize to the same
            // machine key ("Call Graph" and "CallGraph" both give "call_graph"), and a root field
            // can collide with a section, so the collision has to be caught here rather than
            // trusted to the writer. Reported by adversarial review of dotnet-inspect#3494.
            var emitted = new HashSet<string>(StringComparer.Ordinal);

            json.WriteStartObject();

            foreach (var field in _rootFields)
                json.WriteString(RequireUniqueKey(emitted, field.Key), RenderInlineValue(field.Value));

            // A heading whose projected body emitted nothing is not itself data. Explicit empty
            // fields, tables, lists, and trees still call Adopt and retain their shape.
            foreach (var section in SectionsInOutputOrder().Where(section => section.HasContent))
                WriteSection(json, section, RequireUniqueKey(emitted, section.Name));

            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private IEnumerable<Section> SectionsInOutputOrder()
    {
        if (_sectionOrder is not { Count: > 0 })
            return _sections;

        var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < _sectionOrder.Count; index++)
            order.TryAdd(_sectionOrder[index], index);

        return _sections.OrderBy(
            section => order.TryGetValue(section.Name, out var index)
                ? index
                : int.MaxValue);
    }

    /// <summary>
    /// Converts <paramref name="display"/> to its machine key and records it, failing when the
    /// document has already emitted that key.
    /// </summary>
    private static string RequireUniqueKey(HashSet<string> emitted, string display)
    {
        var key = MachineKey(display);
        if (!emitted.Add(key))
        {
            throw new NotSupportedException(
                $"Two parts of this view both serialize to the JSON key '{key}' " +
                $"(most recently '{display}'). Duplicate keys are not an error a JSON parser reports, " +
                "so one of them would be dropped without a word. Rename the section or field.");
        }

        return key;
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

    private static void WriteSection(Utf8JsonWriter json, Section section, string key)
    {
        switch (section.Kind)
        {
            case SectionKind.Table:
                json.WriteStartArray(key);
                // Markout applies its row window before handing visible rows to the formatter.
                foreach (var row in section.Rows)
                {
                    json.WriteStartObject();
                    for (var i = 0; i < section.Headers.Length; i++)
                    {
                        var value = i < row.Length ? RenderInlineValue(row[i]) : "";
                        json.WriteString(section.Headers[i], value);
                    }
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                break;

            case SectionKind.UnlabeledList:
                json.WriteStartArray(key);
                foreach (var item in section.Items)
                    json.WriteStringValue(RenderInlineValue(item));
                json.WriteEndArray();
                break;

            case SectionKind.LabeledArrays:
                json.WriteStartObject(key);
                var arrayKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var array in section.LabeledArrays)
                {
                    json.WriteStartArray(RequireUniqueKey(arrayKeys, array.Name));
                    foreach (var value in array.Values)
                        json.WriteStringValue(RenderInlineValue(value));
                    json.WriteEndArray();
                }
                json.WriteEndObject();
                break;

            case SectionKind.Tree:
                json.WriteStartArray(key);
                foreach (var node in section.Tree)
                    WriteTreeNode(json, node);
                json.WriteEndArray();
                break;

            default:
                json.WriteStartObject(key);
                var fieldKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in section.Fields)
                    json.WriteString(RequireUniqueKey(fieldKeys, field.Key), RenderInlineValue(field.Value));
                json.WriteEndObject();
                break;
        }
    }

    private static ProjectedTreeNode SnapshotTreeNode(TreeNode node, bool includeBadge)
    {
        var children = node.Children is { Count: > 0 }
            ? node.Children.Select(child => SnapshotTreeNode(child, includeBadge)).ToArray()
            : [];
        return new ProjectedTreeNode(node.Text, node.Badge, node.State, children, includeBadge);
    }

    private static void WriteTreeNode(Utf8JsonWriter json, ProjectedTreeNode node)
    {
        json.WriteStartObject();
        json.WriteString("text", RenderInlineValue(node.Text));
        if (node.IncludeBadge && !string.IsNullOrEmpty(node.Badge))
            json.WriteString("badge", RenderInlineValue(node.Badge));
        if (node.State != TreeNodeState.Normal)
            json.WriteString("state", TreeStateMachineName(node.State));
        if (node.Children.Length > 0)
        {
            json.WriteStartArray("children");
            foreach (var child in node.Children)
                WriteTreeNode(json, child);
            json.WriteEndArray();
        }
        json.WriteEndObject();
    }

    private static string RenderInlineValue(string? value) =>
        FormatHelper.RenderInlinePlainText(value);

    private static string TreeStateMachineName(TreeNodeState state) =>
        state switch
        {
            TreeNodeState.Normal => "normal",
            TreeNodeState.Revisit => "revisit",
            _ => throw new NotSupportedException(
                $"Tree node state '{state}' has no stable projected JSON name."),
        };

    private Section RequireSection(SectionKind kind)
    {
        // Content can precede any heading when a view renders a single unnamed section. Give it a
        // stable home rather than dropping it, so no content disappears from the JSON.
        var section = _current ??= GetOrAddSection(string.Empty, kind);
        section.Adopt(kind);
        return section;
    }

    private void RequireNoActiveStreamingTable(string operation)
    {
        if (_streamingTable is not null)
            throw new InvalidOperationException($"Cannot {operation} while a streaming table is active.");
    }

    private void ThrowUnsupportedBlock(string kind)
        => throw new NotSupportedException(
            $"Section '{CurrentSectionName}' emitted a {kind}, which the lowered JSON view cannot " +
            "represent. Select a table or field-set section, use typed JSON, or extend " +
            "JsonSectionFormatter for this shape.");

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
