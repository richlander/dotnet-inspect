using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;
using InertText;
using InertText.Encoding;
using Markout;

namespace DotnetInspector.MetadataRendering;

/// <summary>The table serialization used when rendering a projection.</summary>
public enum MetadataTableFormat
{
    /// <summary>GitHub-flavored Markdown pipe tables (default, human-readable).</summary>
    Markdown,

    /// <summary>Tab-separated values.</summary>
    Tsv,

    /// <summary>JSON Lines: one self-describing object per row.</summary>
    Jsonl,
}

/// <summary>
/// Renders a <see cref="MetadataTableProjection"/> as a document of per-table
/// Markout tables, one section per table.
///
/// This is the product-side presentation of the raw metadata projection. The
/// Metadata layer stays presentation-free (see
/// <c>docs/design/metadata-table-projection.md</c>), so this renderer is a
/// sibling of that layer — reused by the <c>mdi</c> tool now and, later, a
/// <c>dotnet-inspect metadata</c> lens.
///
/// Rendering is deliberately lossy: it is a human/inspection view, and the
/// projection model remains the lossless source of truth (for example for an
/// oracle diff). Even so, three invariants hold: every cell is rendered from
/// exactly one <see cref="MetadataValue"/> case; a
/// <see cref="MetadataValue.Malformed"/> cell stays visibly marked and is never
/// rendered as a success-shaped empty value; and a bounded preview is suffixed
/// with an ellipsis so it is never mistaken for a whole value.
///
/// The two format families identify a row's table differently. Markdown
/// introduces each table with a <c>## &lt;Name&gt; (rows)</c> heading; TSV and
/// JSONL carry a leading <c>Table</c> column so every row self-identifies,
/// keeping those outputs pure machine-readable streams.
/// </summary>
public static class MetadataProjectionRenderer
{
    const string Ellipsis = "\u2026";

    /// <summary>Renders <paramref name="projection"/> to <paramref name="output"/>.</summary>
    public static void Render(
        MetadataTableProjection projection,
        TextWriter output,
        MetadataTableFormat format = MetadataTableFormat.Markdown,
        UntrustedTextMode mode = UntrustedTextMode.Contain)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(output);

        if (format == MetadataTableFormat.Markdown)
            RenderMarkdown(projection, output, mode);
        else
            RenderTabular(projection, output, format, mode);
    }

    static void RenderMarkdown(MetadataTableProjection projection, TextWriter output, UntrustedTextMode mode)
    {
        var writer = new MarkoutWriter(output, new MarkdownFormatter(), new MarkoutWriterOptions());

        bool first = true;
        foreach (var table in projection.Tables)
        {
            if (!first)
                writer.WriteBlankLine();
            first = false;

            writer.WriteHeading(2, HeadingText(table));
            WriteTable(writer, table, identifyTable: false, mode);
        }

        writer.Flush();
    }

    static void RenderTabular(MetadataTableProjection projection, TextWriter output, MetadataTableFormat format, UntrustedTextMode mode)
    {
        var options = new MarkoutWriterOptions
        {
            TableMode = format == MetadataTableFormat.Tsv ? MarkoutTableMode.Tsv : MarkoutTableMode.Jsonl,
        };
        var writer = new MarkoutWriter(output, new TableFormatter(showHeader: true), options);

        foreach (var table in projection.Tables)
            WriteTable(writer, table, identifyTable: true, mode);

        writer.Flush();
    }

    /// <summary>
    /// Renders a reverse-reference search — the rows pointing at one row — to
    /// <paramref name="output"/>.
    ///
    /// A reverse search has blind spots the table renderer has no equivalent of,
    /// and none may be dropped: a target row that is not there, a budget that
    /// stopped the scan, rows that could not be decoded, populated tables the
    /// scan did not read in full, and — always, since no per-query signal can
    /// reveal it — references spelled only inside blobs. All are rendered
    /// as explicit caveats, so an empty result is never mistaken for a confident
    /// "nothing points here".
    /// </summary>
    public static void Render(
        MetadataRowReferenceSet references,
        TextWriter output,
        MetadataTableFormat format = MetadataTableFormat.Markdown)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(output);

        if (format == MetadataTableFormat.Markdown)
            RenderReferencesMarkdown(references, output);
        else
            RenderReferencesTabular(references, output, format);
    }

    static void RenderReferencesMarkdown(MetadataRowReferenceSet references, TextWriter output)
    {
        var writer = new MarkoutWriter(output, new MarkdownFormatter(), new MarkoutWriterOptions());

        writer.WriteHeading(2, $"References to {Describe(references.Target)} ({references.References.Length})");

        if (references.References.IsEmpty)
            writer.WriteParagraph($"No row points at {Describe(references.Target)}.");
        else
            WriteReferenceTable(writer, references, identifyTarget: false);

        foreach (string caveat in Caveats(references))
        {
            writer.WriteBlankLine();
            writer.WriteParagraph(caveat);
        }

        writer.Flush();
    }

    static void RenderReferencesTabular(
        MetadataRowReferenceSet references,
        TextWriter output,
        MetadataTableFormat format)
    {
        var options = new MarkoutWriterOptions
        {
            TableMode = format == MetadataTableFormat.Tsv ? MarkoutTableMode.Tsv : MarkoutTableMode.Jsonl,
        };
        var writer = new MarkoutWriter(output, new TableFormatter(showHeader: true), options);

        WriteReferenceTable(writer, references, identifyTarget: true);

        writer.Flush();
    }

    static void WriteReferenceTable(MarkoutWriter writer, MetadataRowReferenceSet references, bool identifyTarget)
    {
        // Markdown names the target in its heading; the machine formats have no
        // heading, so they carry a leading Target column and every row stays
        // self-describing.
        string[] headers = identifyTarget
            ? ["Target", "Table", "#", "Column", "Kind"]
            : ["Table", "#", "Column", "Kind"];
        string[] headerNames = identifyTarget
            ? ["target", "table", "rid", "column", "kind"]
            : ["table", "rid", "column", "kind"];

        string target = Describe(references.Target);
        var rows = new List<string[]>(references.References.Length);
        foreach (var reference in references.References)
        {
            rows.Add(identifyTarget
                ?
                [
                    target,
                    reference.Source.Table.ToString(),
                    reference.Source.RowId.ToString(),
                    reference.ColumnName,
                    reference.Kind.ToString(),
                ]
                :
                [
                    reference.Source.Table.ToString(),
                    reference.Source.RowId.ToString(),
                    reference.ColumnName,
                    reference.Kind.ToString(),
                ]);
        }

        writer.WriteTable(headers, headerNames, rows);
    }

    /// <summary>
    /// The limits of a reverse search, as caveats a reader must see. Never
    /// empty: four limits are reported when they fire, and the blob limit
    /// always applies, so an empty result is never presented as a bare
    /// "nothing points here".
    /// </summary>
    public static IEnumerable<string> Caveats(MetadataRowReferenceSet references)
    {
        if (!references.TargetExists)
            yield return $"{Describe(references.Target)} is past the end of its table, so no row exists to point at — a row id this large is usually a typo.";

        if (references.Truncated)
            yield return "The result budget stopped this scan before it finished, so more references may exist.";

        if (!references.UnreadableRows.IsEmpty)
        {
            int count = references.UnreadableRows.Length;
            yield return $"{count} {(count == 1 ? "row" : "rows")} had {(count == 1 ? "an edge" : "edges")} that could not be read, so a reference from {(count == 1 ? "it" : "them")} would have been missed.";
        }

        if (!references.UnscannedTables.IsEmpty)
        {
            int count = references.UnscannedTables.Length;
            var names = string.Join(", ", references.UnscannedTables);
            // Deliberately does not say *why* a table went unsearched. A table is
            // here either because the projection does not model it or because the
            // budget stopped the scan first, and naming one cause would be a
            // false claim about the other. "in full" is load-bearing for the same
            // reason: the budget can stop part-way through a table, so claiming
            // none of its rows was read would be false.
            // "Cell" rather than "row" deliberately. A table lands here for
            // three different reasons — never entered, entered and stopped
            // between rows, or entered and stopped between columns of its final
            // row — and only the first two leave a row unread. The third leaves
            // every row read but some cells unexamined, so "a row the scan never
            // read" would be false there.
            yield return $"{count} populated {(count == 1 ? "table was" : "tables were")} not searched in full, so an edge in a cell the scan never examined would have been missed: {names}.";
        }

        // Unconditional, unlike the four above, because no per-query signal can
        // reveal it. Blob payloads sit in heap-kind columns of tables the scan
        // reads in full, so the column is correctly not an edge column, the row
        // is not blind, and the table is genuinely searched — nothing fires. A
        // caveat printed only alongside the others would therefore be absent
        // exactly on the small, clean images where this is the only limit left,
        // which is where "No row points at X" reads most like a guarantee.
        //
        // Two unlike things are covered deliberately. A signature blob spells a
        // reference as a TypeDefOrRef coded *token*, which is a genuine missed
        // row-to-row edge. A custom-attribute value spells one as a serialized
        // type *name*, which is not a token edge at all and so is out of scope
        // for a search defined over tokens — but a reader asking "what
        // references this type?" is not served by that distinction, and an
        // unqualified empty answer would still mislead them.
        yield return "Blob payloads are not searched, so a reference spelled inside one — as a type token in a signature, or as a type name in a custom-attribute value — is not reported.";
    }

    static string Describe(MetadataRowLocation location) => $"{location.Table}[{location.RowId}]";

    /// <summary>
    /// Renders an image overview — metadata root identity, heap sizes, table row
    /// counts, and PE/CLI header facts — to <paramref name="output"/>.
    ///
    /// Only tables that carry rows are listed; the number omitted is reported as
    /// a caveat rather than left implicit, so a short list is never mistaken for
    /// the whole of ECMA-335. A table with rows that the projection does not
    /// model stays visible and is marked as unprojected, because that is a gap in
    /// coverage rather than an empty table.
    /// </summary>
    public static void Render(
        MetadataImageOverview overview,
        TextWriter output,
        MetadataTableFormat format = MetadataTableFormat.Markdown,
        UntrustedTextMode mode = UntrustedTextMode.Contain)
    {
        ArgumentNullException.ThrowIfNull(overview);
        ArgumentNullException.ThrowIfNull(output);

        if (format == MetadataTableFormat.Markdown)
            RenderOverviewMarkdown(overview, output, mode);
        else
            RenderOverviewTabular(overview, output, format, mode);
    }

    static void RenderOverviewMarkdown(MetadataImageOverview overview, TextWriter output, UntrustedTextMode mode)
    {
        var writer = new MarkoutWriter(output, new MarkdownFormatter(), new MarkoutWriterOptions());

        writer.WriteHeading(2, "Image");
        WriteImageTable(writer, overview, identifySection: false, mode);

        writer.WriteBlankLine();
        writer.WriteHeading(2, "Heaps");
        WriteHeapTable(writer, overview, identifySection: false);

        writer.WriteBlankLine();
        writer.WriteHeading(2, $"Tables ({NonEmptyTables(overview).Count})");
        WriteTableTable(writer, overview, identifySection: false);

        foreach (string caveat in Caveats(overview))
        {
            writer.WriteBlankLine();
            writer.WriteParagraph(caveat);
        }

        writer.Flush();
    }

    static void RenderOverviewTabular(MetadataImageOverview overview, TextWriter output, MetadataTableFormat format, UntrustedTextMode mode)
    {
        var options = new MarkoutWriterOptions
        {
            TableMode = format == MetadataTableFormat.Tsv ? MarkoutTableMode.Tsv : MarkoutTableMode.Jsonl,
        };
        var writer = new MarkoutWriter(output, new TableFormatter(showHeader: true), options);

        // The three parts have different shapes, so each keeps its own header and
        // carries a leading Section column, matching how the table renderer keeps
        // machine rows self-identifying.
        WriteImageTable(writer, overview, identifySection: true, mode);
        WriteHeapTable(writer, overview, identifySection: true);
        WriteTableTable(writer, overview, identifySection: true);

        writer.Flush();
    }

    static void WriteImageTable(MarkoutWriter writer, MetadataImageOverview overview, bool identifySection, UntrustedTextMode mode)
    {
        WriteSectionTable(
            writer, identifySection, "image",
            ["Property", "Value"], ["property", "value"], ImageFactRows(overview, mode));
    }

    /// <summary>
    /// The image-level metadata facts as <c>Property</c>/<c>Value</c> rows. Shared by the
    /// multi-section overview and by <see cref="RenderImageFacts"/> so the two cannot report
    /// different facts about the same image.
    /// </summary>
    static List<string[]> ImageFactRows(MetadataImageOverview overview, UntrustedTextMode mode)
    {
        var rows = new List<string[]>();

        // A truncated stamp must carry the ellipsis for the same reason a
        // truncated handle display does: a prefix must never be mistaken for the
        // whole value.
        Add("Metadata version", Display(overview.MetadataVersion, mode));
        Add("Metadata kind", overview.Kind.ToString());
        Add("Has assembly manifest", overview.IsAssembly ? "yes" : "no");
        Add("Metadata offset", overview.MetadataOffset.ToString());
        Add("Metadata size", $"{overview.MetadataSize} bytes");
        Add("Machine", overview.Headers.Machine.ToString());
        Add("Image characteristics", overview.Headers.ImageCharacteristics.ToString());
        Add("Subsystem", overview.Headers.Subsystem.ToString());
        Add("DLL characteristics", overview.Headers.DllCharacteristics.ToString());
        Add("Optional header", overview.Headers.IsPE32Plus ? "PE32+" : "PE32");

        if (overview.Headers.Cor is { } cor)
        {
            Add("CLI runtime version", $"{cor.MajorRuntimeVersion}.{cor.MinorRuntimeVersion}");
            Add("CLI flags", cor.Flags.ToString());
            Add("Entry point", DescribeEntryPoint(cor));
        }
        else
        {
            // Never rendered as a blank or zero: an image with no CLI header is a
            // different fact from one whose header happens to be empty.
            Add("CLI header", "absent");
        }

        return rows;

        void Add(string property, string value) => rows.Add([property, value]);
    }

    /// <summary>
    /// Renders the image-level facts as a single heading-free <c>Property</c>/<c>Value</c> table:
    /// the metadata root identity and PE/CLI header facts, followed by one row per heap giving its
    /// size and addressing.
    ///
    /// This is the shape the CLI's <c>Metadata: Image</c> section needs. That section is one
    /// section, so it must be one heading and one table, whereas
    /// <see cref="Render(MetadataImageOverview, TextWriter, MetadataTableFormat)"/> deliberately
    /// emits three headings for a standalone report. The per-table row counts that report also
    /// carries are omitted here rather than flattened in: they are rows of the table sections this
    /// lens already registers, reachable as <c>-S @Metadata --count</c>.
    ///
    /// <paramref name="columns"/> narrows the rendered table to the named columns of
    /// <c>Property</c>/<c>Value</c>; null or empty renders both. The facts themselves are rows, so
    /// selecting *which facts* is <c>--rows</c>'s job, not this parameter's.
    /// </summary>
    public static void RenderImageFacts(
        MetadataImageOverview overview,
        TextWriter output,
        IReadOnlyCollection<string>? columns = null,
        MetadataTableFormat format = MetadataTableFormat.Markdown,
        UntrustedTextMode mode = UntrustedTextMode.Contain)
    {
        ArgumentNullException.ThrowIfNull(overview);
        ArgumentNullException.ThrowIfNull(output);

        var rows = ImageFactRows(overview, mode);
        foreach (var heap in overview.Heaps)
        {
            string addressing = heap.Addressing == MetadataHeapAddressing.Index ? "index" : "byte offset";
            rows.Add([
                $"{heap.Heap} heap",
                $"{heap.SizeInBytes} bytes, addressed by {addressing}, max address {heap.MaxAddress}",
            ]);
        }

        string[] headers = ["Property", "Value"];
        string[] headerNames = ["property", "value"];
        if (columns is { Count: > 0 })
        {
            var wanted = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
            var keep = Enumerable.Range(0, headers.Length).Where(i => wanted.Contains(headers[i])).ToArray();

            // An empty selection means neither column was named — the request was aimed at a
            // sibling section — so the full table stands rather than rendering as blank rows.
            if (keep.Length > 0)
            {
                headers = [.. keep.Select(i => headers[i])];
                headerNames = [.. keep.Select(i => headerNames[i])];
                rows = [.. rows.Select(row => keep.Select(i => row[i]).ToArray())];
            }
        }

        var writer = format == MetadataTableFormat.Markdown
            ? new MarkoutWriter(output, new MarkdownFormatter(), new MarkoutWriterOptions())
            : new MarkoutWriter(
                output,
                new TableFormatter(showHeader: true),
                new MarkoutWriterOptions
                {
                    TableMode = format == MetadataTableFormat.Tsv ? MarkoutTableMode.Tsv : MarkoutTableMode.Jsonl,
                });
        writer.WriteTable(headers, headerNames, rows);
        writer.Flush();
    }

    static string DescribeEntryPoint(MetadataCorHeaderSummary cor)
    {
        if (cor.EntryPointToken is { } token)
            return $"token 0x{token:X8}";

        if (cor.EntryPointTokenOrRelativeVirtualAddress == 0)
            return "none";

        return $"native RVA 0x{cor.EntryPointTokenOrRelativeVirtualAddress:X8}";
    }

    static void WriteHeapTable(MarkoutWriter writer, MetadataImageOverview overview, bool identifySection)
    {
        var rows = new List<string[]>(overview.Heaps.Length);
        foreach (var heap in overview.Heaps)
        {
            rows.Add([
                heap.Heap.ToString(),
                heap.SizeInBytes.ToString(),
                heap.Addressing == MetadataHeapAddressing.Index ? "index" : "byte offset",
                heap.MaxAddress.ToString(),
            ]);
        }

        WriteSectionTable(
            writer,
            identifySection,
            "heap",
            ["Heap", "Bytes", "Addressing", "Max address"],
            ["heap", "bytes", "addressing", "maxAddress"],
            rows);
    }

    static void WriteTableTable(MarkoutWriter writer, MetadataImageOverview overview, bool identifySection)
    {
        var rows = new List<string[]>();
        foreach (var table in NonEmptyTables(overview))
            rows.Add([table.Name, table.RowCount.ToString(), table.IsProjected ? "yes" : "no"]);

        WriteSectionTable(
            writer,
            identifySection,
            "table",
            ["Table", "Rows", "Projected"],
            ["table", "rows", "projected"],
            rows);
    }

    static void WriteSectionTable(
        MarkoutWriter writer,
        bool identifySection,
        string section,
        string[] headers,
        string[] headerNames,
        List<string[]> rows)
    {
        if (!identifySection)
        {
            writer.WriteTable(headers, headerNames, rows);
            return;
        }

        string[] prefixedHeaders = ["Section", .. headers];
        string[] prefixedNames = ["section", .. headerNames];
        var prefixedRows = new List<string[]>(rows.Count);
        foreach (var row in rows)
            prefixedRows.Add([section, .. row]);

        writer.WriteTable(prefixedHeaders, prefixedNames, prefixedRows);
    }

    static List<MetadataTableSummary> NonEmptyTables(MetadataImageOverview overview)
    {
        var tables = new List<MetadataTableSummary>();
        foreach (var table in overview.Tables)
        {
            if (table.RowCount > 0)
                tables.Add(table);
        }

        return tables;
    }

    /// <summary>
    /// What an image overview leaves out, as caveats a reader must see: the
    /// tables omitted for being empty, and any table with rows the projection
    /// does not model.
    /// </summary>
    public static IEnumerable<string> Caveats(MetadataImageOverview overview)
    {
        ArgumentNullException.ThrowIfNull(overview);

        int empty = overview.Tables.Length - NonEmptyTables(overview).Count;
        if (empty > 0)
            yield return $"{empty} of {overview.Tables.Length} ECMA-335 tables carry no rows in this image and are not listed.";

        var unprojected = new List<string>();
        foreach (var table in NonEmptyTables(overview))
        {
            if (!table.IsProjected)
                unprojected.Add(table.Name);
        }

        if (unprojected.Count > 0)
            yield return $"{unprojected.Count} {(unprojected.Count == 1 ? "table has" : "tables have")} rows the projection does not model, so their contents cannot be dumped: {string.Join(", ", unprojected)}.";
    }

    /// <summary>
    /// Renders a single heap value read by address to
    /// <paramref name="output"/>. The address is echoed alongside the value so
    /// the row identifies what was asked for, in every format.
    /// </summary>
    public static void Render(
        MetadataValue value,
        HeapKind heap,
        int address,
        TextWriter output,
        MetadataTableFormat format = MetadataTableFormat.Markdown,
        UntrustedTextMode mode = UntrustedTextMode.Contain)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(output);

        if (format == MetadataTableFormat.Markdown)
        {
            var markdown = new MarkoutWriter(output, new MarkdownFormatter(), new MarkoutWriterOptions());
            markdown.WriteHeading(2, $"{MetadataHeapCoordinate.StreamName(heap)} heap at {address}");
            markdown.WriteTable(HeapValueHeaders, HeapValueHeaderNames, [HeapValueRow(value, heap, address, mode)]);
            markdown.Flush();
            return;
        }

        RenderHeapValue(value, heap, address, output, columns: null, format, mode);
    }

    static readonly string[] HeapValueHeaders = ["Heap", "Address", "Length", "Truncated", "Value"];
    static readonly string[] HeapValueHeaderNames = ["heap", "address", "length", "truncated", "value"];

    /// <summary>
    /// The column names <see cref="RenderHeapValue"/> emits. Exposed so a caller that must declare
    /// this shape — the CLI registers its sections' columns for discovery and <c>--columns</c>
    /// validation — reads them from the renderer instead of restating them and drifting.
    /// </summary>
    public static IReadOnlyList<string> HeapValueColumns => HeapValueHeaders;

    static string[] HeapValueRow(MetadataValue value, HeapKind heap, int address, UntrustedTextMode mode)
    {
        var heapReference = value as MetadataValue.HeapReference;
        return
        [
            MetadataHeapCoordinate.StreamName(heap),
            address.ToString(),
            heapReference is null ? string.Empty : heapReference.Length.ToString(),
            heapReference is { Truncated: true } ? "yes" : "no",
            FormatCell(value, mode),
        ];
    }

    /// <summary>
    /// Renders a single heap value as a heading-free one-row table.
    ///
    /// This is the shape the CLI's coordinate-scoped heap section needs: that section owns its
    /// heading, because the section orderer, the section filter, and <c>--count</c> all key off
    /// the heading text, whereas
    /// <see cref="Render(MetadataValue, HeapKind, int, TextWriter, MetadataTableFormat)"/> writes
    /// its own heading for a standalone report. Both build the same row from the same cell
    /// renderer, so the two cannot disagree about a value.
    ///
    /// <paramref name="columns"/> narrows the rendered columns; null, empty, or a selection that
    /// names none of them leaves the table whole rather than rendering a blank row.
    /// </summary>
    public static void RenderHeapValue(
        MetadataValue value,
        HeapKind heap,
        int address,
        TextWriter output,
        IReadOnlyCollection<string>? columns = null,
        MetadataTableFormat format = MetadataTableFormat.Markdown,
        UntrustedTextMode mode = UntrustedTextMode.Contain)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(output);

        WriteNarrowedTable(
            output, format, HeapValueHeaders, HeapValueHeaderNames, [HeapValueRow(value, heap, address, mode)], columns);
    }

    static readonly string[] HeapEntryHeaders = ["Address", "Length", "Refs", "Truncated", "Value"];
    static readonly string[] HeapEntryHeaderNames = ["address", "length", "refs", "truncated", "value"];

    /// <summary>The column names <see cref="RenderHeapEntries"/> emits.</summary>
    public static IReadOnlyList<string> HeapEntryColumns => HeapEntryHeaders;

    /// <summary>
    /// Renders a heap's listable entries as a heading-free table, one row per entry, in address
    /// order.
    ///
    /// The table is only half the answer — what the listing *covers* is the other half, and it is
    /// not rendered here. A caller must also render <see cref="Caveats(MetadataHeapEntrySet)"/>,
    /// exactly as the table sections do, or a referenced-values listing reads as a complete heap.
    /// </summary>
    public static void RenderHeapEntries(
        MetadataHeapEntrySet entries,
        TextWriter output,
        IReadOnlyCollection<string>? columns = null,
        MetadataTableFormat format = MetadataTableFormat.Markdown,
        UntrustedTextMode mode = UntrustedTextMode.Contain)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(output);

        var rows = new List<string[]>(entries.Entries.Length);
        foreach (var entry in entries.Entries)
        {
            var heapReference = entry.Value as MetadataValue.HeapReference;
            rows.Add([
                entry.Offset.ToString(),
                heapReference is null ? string.Empty : heapReference.Length.ToString(),
                entry.ReferenceCount.ToString(),
                heapReference is { Truncated: true } ? "yes" : "no",
                FormatCell(entry.Value, mode),
            ]);
        }

        WriteNarrowedTable(output, format, HeapEntryHeaders, HeapEntryHeaderNames, rows, columns);
    }

    /// <summary>
    /// What <paramref name="entries"/> does not show. Every bound the set carries becomes a
    /// sentence, so a listing is never presented as more than it is.
    /// </summary>
    public static IEnumerable<string> Caveats(MetadataHeapEntrySet entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        string name = MetadataHeapCoordinate.StreamName(entries.Heap);

        switch (entries.Coverage)
        {
            case MetadataHeapCoverage.Complete:
                yield return $"The {name} heap holds fixed-size records, so every entry in its {entries.SizeInBytes} bytes is listed. Refs counts the projected table cells pointing at each one; zero means the entry is stored but unreferenced.";
                break;

            case MetadataHeapCoverage.ReferencedOnly:
                yield return $"Listed entries are the distinct {name} values that projected table rows point at, not a walk of the heap: ECMA-335 stores it as length-prefixed items with no index and System.Reflection.Metadata exposes no walker, so entries no row references are not listed. The heap is {entries.SizeInBytes} bytes; any address in it stays readable with --heap \"{name}:<address>\".";
                break;

            case MetadataHeapCoverage.NotEnumerable:
                yield return $"No entry of the {name} heap can be listed: no metadata table column points into it — its references are ldstr operands inside method bodies, which this projection does not read — and it cannot be walked. Its {entries.SizeInBytes} bytes are not empty; read a known address with --heap \"{name}:<address>\".";
                break;
        }

        if (entries.EntriesTruncated)
            yield return $"The entry budget stopped this listing short, so it is a prefix of the {name} entries rather than all of them.";

        if (entries.RowsTruncated)
            yield return "At least one table projected fewer than all of its rows, so a reference from an unscanned row was not counted and an entry only such rows point at is missing.";
    }

    /// <summary>
    /// Writes one table, optionally narrowed to <paramref name="columns"/>, in the requested
    /// format.
    ///
    /// A selection naming none of the table's columns leaves the table whole. That is deliberate
    /// and matches <see cref="RenderImageFacts"/>: with several sections selected, a
    /// <c>--columns</c> name usually belongs to a sibling section, and narrowing to nothing would
    /// misreport a populated section as a set of blank rows.
    /// </summary>
    static void WriteNarrowedTable(
        TextWriter output,
        MetadataTableFormat format,
        string[] headers,
        string[] headerNames,
        IReadOnlyList<string[]> rows,
        IReadOnlyCollection<string>? columns)
    {
        if (columns is { Count: > 0 })
        {
            var wanted = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
            var keep = Enumerable.Range(0, headers.Length).Where(i => wanted.Contains(headers[i])).ToArray();

            if (keep.Length > 0)
            {
                headers = [.. keep.Select(i => headers[i])];
                headerNames = [.. keep.Select(i => headerNames[i])];
                rows = [.. rows.Select(row => keep.Select(i => row[i]).ToArray())];
            }
        }

        var writer = format == MetadataTableFormat.Markdown
            ? new MarkoutWriter(output, new MarkdownFormatter(), new MarkoutWriterOptions())
            : new MarkoutWriter(
                output,
                new TableFormatter(showHeader: true),
                new MarkoutWriterOptions
                {
                    TableMode = format == MetadataTableFormat.Tsv ? MarkoutTableMode.Tsv : MarkoutTableMode.Jsonl,
                });

        writer.WriteTable(headers, headerNames, [.. rows]);
        writer.Flush();
    }

    /// <summary>
    /// Renders one table's rows as a Markdown table with no heading of its own.
    ///
    /// Exists for callers that must own their section heading: the CLI's <c>@Metadata</c> lens
    /// renders each table under the exact section name the section pipeline registered
    /// (<c>## Metadata: TypeRef</c>), because the section orderer, the section filter, and
    /// <c>--count</c> all key off that heading text. Such a caller still needs this type's cell
    /// rendering — one <see cref="MetadataValue"/> case per cell, malformed cells visibly marked,
    /// bounded previews suffixed — so it reuses it here instead of forking a second decoder.
    ///
    /// The row-extent facts that <see cref="Render(MetadataTableProjection, TextWriter, MetadataTableFormat)"/>
    /// puts in its heading are not lost: they are available as caveats from
    /// <see cref="Caveats(MetadataTableView)"/>, which such a caller must render.
    /// </summary>
    public static void RenderRows(MetadataTableView table, TextWriter output, UntrustedTextMode mode = UntrustedTextMode.Contain)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(output);

        var writer = new MarkoutWriter(output, new MarkdownFormatter(), new MarkoutWriterOptions());
        WriteTable(writer, table, identifyTable: false, mode);
        writer.Flush();
    }

    /// <summary>
    /// What a projected table's rows leave out, as caveats a reader must see.
    ///
    /// A table rendered without <see cref="HeadingText"/>'s extent suffix carries no other signal
    /// that its rows are a window rather than the whole table, so a truncated projection must say
    /// so here or the reader would take a partial dump for a complete one.
    /// </summary>
    public static IEnumerable<string> Caveats(MetadataTableView table)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (table.Truncation is not { } truncation)
            yield break;

        if (table.Rows.IsEmpty)
        {
            yield return $"{table.Name}: showing 0 of {truncation.RowCount} rows; the row window starts past the end of the table.";
            yield break;
        }

        int first = table.Rows[0].RowId;
        int last = table.Rows[^1].RowId;
        yield return first == 1
            ? $"{table.Name}: showing {truncation.ProjectedRows} of {truncation.RowCount} rows."
            : $"{table.Name}: showing rows {first}\u2013{last} of {truncation.RowCount}.";
    }

    static string HeadingText(MetadataTableView table)
    {
        if (table.Truncation is not { } truncation)
            return $"{table.Name} ({table.RowCount} {(table.RowCount == 1 ? "row" : "rows")})";

        if (table.Rows.IsEmpty)
            return $"{table.Name} (showing 0 of {truncation.RowCount} rows)";

        // A row window that does not start at row 1 must say where it sits:
        // "showing 4 of 100 rows" alone would read as the first four rows.
        int first = table.Rows[0].RowId;
        int last = table.Rows[^1].RowId;
        return first == 1
            ? $"{table.Name} (showing {truncation.ProjectedRows} of {truncation.RowCount} rows)"
            : $"{table.Name} (showing rows {first}\u2013{last} of {truncation.RowCount})";
    }

    static void WriteTable(MarkoutWriter writer, MetadataTableView table, bool identifyTable, UntrustedTextMode mode)
    {
        // Markdown identifies the table with a heading; the machine formats carry
        // a leading Table column instead so every row self-identifies. Both add a
        // row-id column so a resolved handle target (for example TypeRef[5]) can be
        // cross-referenced back to that row's number in its table.
        int prefix = identifyTable ? 2 : 1;
        var headers = new string[table.Columns.Length + prefix];
        var headerNames = new string[table.Columns.Length + prefix];

        int slot = 0;
        if (identifyTable)
        {
            headers[slot] = "Table";
            headerNames[slot] = "table";
            slot++;
        }

        headers[slot] = "#";
        headerNames[slot] = "rid";
        slot++;

        for (int i = 0; i < table.Columns.Length; i++)
        {
            headers[slot + i] = table.Columns[i].Name;
            headerNames[slot + i] = table.Columns[i].Name;
        }

        var rows = new List<string[]>(table.Rows.Length);
        foreach (var row in table.Rows)
        {
            var cells = new string[row.Cells.Length + prefix];
            int cellSlot = 0;
            if (identifyTable)
                cells[cellSlot++] = table.Name;
            cells[cellSlot++] = row.RowId.ToString();
            for (int i = 0; i < row.Cells.Length; i++)
                cells[cellSlot + i] = FormatCell(row.Cells[i], mode);
            rows.Add(cells);
        }

        writer.WriteTable(headers, headerNames, rows);
    }

    static string FormatCell(MetadataValue value, UntrustedTextMode mode) => value switch
    {
        MetadataValue.Nil => "nil",
        MetadataValue.Scalar scalar => scalar.Display,
        MetadataValue.Flags flags => flags.Decoded,
        MetadataValue.HeapReference heap => FormatHeap(heap, mode),
        MetadataValue.Handle handle => FormatHandle(handle.Reference, mode),
        MetadataValue.Range range => FormatRange(range.Reference),
        MetadataValue.Malformed malformed => $"!malformed: {Display(malformed.Detail, mode)}",
        _ => throw new InvalidOperationException($"Unhandled metadata value: {value.GetType().Name}"),
    };

    /// <summary>
    /// Turns a contained value into display text, marking a partial one so it can never
    /// be read as whole.
    /// </summary>
    /// <remarks>
    /// This is the boundary the projection carries <see cref="InertString"/> up to. The
    /// model keeps the text and its truncation together precisely so that the decision
    /// made here — what a clipped value looks like — is made once, by the sink that knows
    /// its own notation, rather than by every producer guessing at it.
    /// <para>
    /// It is also the only place <see cref="UntrustedTextMode.Raw"/> can act. The projection
    /// cannot carry untreated text — no conversion admits a <see cref="string"/> into an
    /// <see cref="InertString"/> — so raw output is produced by running the encoder backwards
    /// here rather than by keeping a second, untreated copy of every value upstream. That is
    /// the <c>vis</c>/<c>unvis</c> pairing the threat model specifies: the encoding is lossless
    /// and invertible, and the decoder is what makes raw output a rendering choice instead of a
    /// property of the model.
    /// </para>
    /// <para>
    /// Because the decode happens after bounding, the budget cuts the same value in both modes:
    /// the axes differ in how text is spelled, not in how much of it is shown. Encoded and raw
    /// output of a clipped cell therefore describe the same prefix.
    /// </para>
    /// </remarks>
    static string Display(InertString text, bool truncated, UntrustedTextMode mode)
    {
        string body = mode is UntrustedTextMode.Raw ? Decode(text) : text.ToString();
        return truncated ? body + Ellipsis : body;
    }

    /// <inheritdoc cref="Display(InertString, bool, UntrustedTextMode)"/>
    static string Display(InertString text, UntrustedTextMode mode)
        => Display(text, text.IsTruncated, mode);

    /// <summary>
    /// Runs the visual encoding backwards, recovering the artifact's own spelling.
    /// </summary>
    /// <remarks>
    /// Total for anything this library produced: every spelling the encoder emits is one the
    /// decoder accepts. The fallback is unreachable rather than load-bearing, and it fails
    /// toward the encoded form — the safe direction — so a value that somehow did not round-trip
    /// is shown contained rather than raw.
    /// </remarks>
    static string Decode(InertString text)
        => VisualEncoder.TryDecode(text.ToString(), out string? decoded) ? decoded : text.ToString();

    static string FormatHeap(MetadataValue.HeapReference heap, UntrustedTextMode mode)
    {
        // The Blob heap carries no decoded text; its bounded hex preview stands in.
        // Truncation comes off the reference rather than the text because a blob's
        // preview is bounded in bytes, which the hex spelling cannot report.
        return Display(heap.Text ?? heap.Preview, heap.Truncated, mode);
    }

    static string FormatHandle(HandleRef reference, UntrustedTextMode mode)
    {
        if (reference.TargetRowId == 0)
            return "nil";

        string target = $"{reference.TargetTable}[{reference.TargetRowId}]";

        if (reference.Display is not { } display)
            return target;

        // A truncated display must always carry the ellipsis so it is never
        // mistaken for a whole value — even when the budget clipped it to empty,
        // which must still render as "(…)" rather than a bare target that looks
        // like an unavailable display.
        if (display.IsTruncated)
            return $"{target} ({Display(display, mode)})";

        if (display.IsEmpty)
            return target;

        return $"{target} ({Display(display, mode)})";
    }

    static string FormatRange(HandleRange range)
        => $"{range.TargetTable}[{range.StartRowId}..{range.EndRowId})";
}
