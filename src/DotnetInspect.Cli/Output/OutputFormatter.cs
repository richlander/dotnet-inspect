using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;
using DotnetInspector.Packages;
using DotnetInspect.Cli.Views;
using System.Globalization;
using System.Net;
using System.Text.Json;
using DotnetInspect.Cli.Options;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using ILInspector.Metadata;
using Markout;
using Markout.Formatting;

namespace DotnetInspect.Cli.Output;

/// <summary>
/// Diagnostic returned when the rendering service detects an incompatibility
/// between the requested sections and the formatter's capabilities.
/// </summary>
public record RenderDiagnostic(string Formatter, string Condition, string[] Sections);

internal sealed class ProducerLibraryTableFormatter(
    IMarkoutFormatter inner,
    string[] provenanceHeaders,
    string[] provenanceValues,
    string[]? projectedColumns) : IMarkoutFormatter, ITableFormatter
{
    public void FormatTable(
        TextWriter writer,
        ReadOnlySpan<string> headers,
        IList<string[]> rows,
        int skippedRows,
        MarkoutWriterOptions options)
    {
        if (inner is not ITableFormatter tableFormatter)
        {
            throw new InvalidOperationException(
                $"Formatter '{inner.GetType().Name}' cannot lower "
                + "producer-Library table provenance.");
        }

        if (provenanceHeaders.Length != provenanceValues.Length)
        {
            throw new InvalidOperationException(
                "Producer-Library provenance headers and values must have the same cardinality.");
        }

        string[] headersWithLibrary =
            new string[headers.Length + provenanceHeaders.Length];
        provenanceHeaders.CopyTo(headersWithLibrary, 0);
        headers.CopyTo(headersWithLibrary.AsSpan(provenanceHeaders.Length));

        var rowsWithLibrary =
            new List<string[]>(rows.Count);
        foreach (string[] row in rows)
        {
            var rowWithLibrary =
                new string[row.Length + provenanceValues.Length];
            provenanceValues.CopyTo(rowWithLibrary, 0);
            row.CopyTo(rowWithLibrary, provenanceValues.Length);
            rowsWithLibrary.Add(rowWithLibrary);
        }

        if (projectedColumns is { Length: > 0 })
        {
            var projection = MarkoutProjection.WithColumns(projectedColumns);
            if (!projection.TryResolveColumns(
                    headersWithLibrary,
                    out ColumnProjectionResolution resolution))
            {
                return;
            }

            int[] selectedColumns =
            [
                .. Enumerable.Range(0, provenanceHeaders.Length),
                .. resolution.ColumnMap
                    .Where(index => index >= provenanceHeaders.Length)
                    .Distinct(),
            ];
            bool producerSelected = resolution.ColumnMap.Any(
                index => index < provenanceHeaders.Length);
            if (!producerSelected
                && selectedColumns.Length == provenanceHeaders.Length)
                return;

            headersWithLibrary =
            [
                .. selectedColumns.Select(
                    index => headersWithLibrary[index]),
            ];
            rowsWithLibrary =
            [
                .. rowsWithLibrary.Select(row =>
                    selectedColumns.Select(index => row[index]).ToArray()),
            ];
        }

        tableFormatter.FormatTable(
            writer,
            headersWithLibrary,
            rowsWithLibrary,
            skippedRows,
            options);
    }
}

internal sealed class CapturedLibraryTableFormatter :
    IMarkoutFormatter,
    ITableFormatter
{
    private string[]? _headers;
    private readonly List<string[]> _rows = [];

    internal bool HasTable => _headers is not null;

    public void FormatTable(
        TextWriter writer,
        ReadOnlySpan<string> headers,
        IList<string[]> rows,
        int skippedRows,
        MarkoutWriterOptions options)
    {
        if (_headers is not null)
        {
            throw new InvalidOperationException(
                "An aggregate Library table can be initialized only once.");
        }

        _headers = headers.ToArray();
        _rows.AddRange(rows);
    }

    internal int WindowedRowCount(RowWindow? rows) =>
        WindowedRows(rows).Count;

    internal Dictionary<string, string>[] JsonRows(RowWindow? rows)
    {
        if (_headers is null)
            return [];

        return
        [
            .. WindowedRows(rows).Select(
                row =>
                    Enumerable.Range(0, _headers.Length)
                        .ToDictionary(
                            index => _headers[index],
                            index => LowerJsonCell(row[index]),
                            StringComparer.Ordinal)),
        ];
    }

    private static string LowerJsonCell(string value)
    {
        if (value is { Length: > 1 }
            && value[0] == '`'
            && value[^1] == '`')
        {
            return WebUtility.HtmlDecode(value[1..^1]);
        }

        const string openCode = "<code>";
        const string closeCode = "</code>";
        if (value.StartsWith(
                openCode,
                StringComparison.OrdinalIgnoreCase)
            && value.EndsWith(
                closeCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return WebUtility.HtmlDecode(
                value[openCode.Length..^closeCode.Length]);
        }

        return value;
    }

    internal void ProjectColumns(
        string[]? projectedColumns,
        int provenanceColumnCount)
    {
        if (_headers is null
            || projectedColumns is not { Length: > 0 })
        {
            return;
        }

        var projection =
            MarkoutProjection.WithColumns(
                projectedColumns);
        if (!projection.TryResolveColumns(
                _headers,
                out ColumnProjectionResolution resolution))
        {
            throw new InvalidOperationException(
                "Validated package aggregate columns were not present in the captured row shape.");
        }

        int[] selectedColumns =
        [
            .. Enumerable.Range(
                0,
                provenanceColumnCount),
            .. resolution.ColumnMap
                .Where(
                    index =>
                        index >= provenanceColumnCount)
                .Distinct(),
        ];
        bool provenanceSelected =
            resolution.ColumnMap.Any(
                index =>
                    index < provenanceColumnCount);
        if (!provenanceSelected
            && selectedColumns.Length
                == provenanceColumnCount)
        {
            throw new InvalidOperationException(
                "A package aggregate projection must retain at least one selected data column.");
        }

        string[][] projectedRows =
        [
            .. _rows.Select(
                row =>
                    selectedColumns
                        .Select(index => row[index])
                        .ToArray()),
        ];
        _headers =
        [
            .. selectedColumns.Select(
                index => _headers[index]),
        ];
        _rows.Clear();
        _rows.AddRange(projectedRows);
    }

    internal void Write(
        TextWriter writer,
        IMarkoutFormatter formatter,
        MarkoutWriterOptions options,
        RowWindow? rows = null)
    {
        if (_headers is null)
            return;

        if (formatter is not ITableFormatter tableFormatter)
        {
            throw new InvalidOperationException(
                $"Formatter '{formatter.GetType().Name}' cannot render "
                + "a captured package aggregate table.");
        }

        tableFormatter.FormatTable(
            writer,
            _headers,
            WindowedRows(rows),
            skippedRows: 0,
            options);
    }

    internal string RenderMarkdown(
        string section,
        RowWindow? rows)
    {
        if (_headers is null)
            return string.Empty;

        var output = new StringWriter { NewLine = "\n" };
        var writer = new MarkoutWriter(
            output,
            new MarkdownFormatter(),
            new MarkoutWriterOptions());
        writer.WriteHeading(2, section);
        writer.WriteTable(
            _headers,
            _headers,
            WindowedRows(rows));
        writer.Flush();
        return output.ToString().TrimEnd();
    }

    internal string RenderPlainText(
        string section,
        RowWindow? rows)
    {
        if (_headers is null)
            return string.Empty;

        var output = new StringWriter { NewLine = "\n" };
        var writer = new MarkoutWriter(
            output,
            new PlainTextFormatter(),
            new MarkoutWriterOptions());
        writer.WriteHeading(2, section);
        writer.WriteTable(
            _headers,
            _headers,
            WindowedRows(rows));
        writer.Flush();
        return output.ToString().TrimEnd();
    }

    private List<string[]> WindowedRows(RowWindow? rows)
    {
        if (rows is not { IsUnlimited: false } window)
            return [.. _rows];

        var (start, end) = window.Resolve(_rows.Count);
        return [.. _rows.Skip(start).Take(end - start)];
    }
}

/// <summary>
/// Handles output formatting for inspection results.
/// </summary>
public static class OutputFormatter
{
    public static string RenderTable(bool showHeader, Action<TextWriter, IMarkoutFormatter> serialize)
    {
        var sw = new StringWriter { NewLine = "\n" };
        serialize(sw, new TableFormatter(showHeader));
        return sw.ToString();
    }

    public static void WriteTable(TextWriter output, bool showHeader, Action<TextWriter, IMarkoutFormatter> serialize, RowWindow? maxRows = null)
    {
        // Row-limiting operates on the rendered text, so the capped path must materialize
        // the table first. Without a cap, serialize straight to the destination writer and
        // skip the StringWriter + whole-table string allocation.
        //
        // Exception: the line/tail-limiting console writers count newlines per write call,
        // so a single buffered Write(string) is not interchangeable with row-by-row writes
        // (it changes which trailing content survives the limit). Keep the buffered path for
        // those wrappers to preserve byte-identical output; their output is already small.
        if (maxRows is null or { IsUnlimited: true } && output is not (LineLimitingTextWriter or TailLineLimitingTextWriter))
        {
            serialize(output, new TableFormatter(showHeader));
            return;
        }

        output.Write(LimitRenderedTableRows(RenderTable(showHeader, serialize), maxRows, showHeader));
    }

    /// <summary>
    /// Trims a rendered single-section table to <paramref name="maxRows"/> data rows,
    /// for any table output format. <c>--tsv</c>/<c>--jsonl</c> render one section at a
    /// time, so the rendered text is a single table: jsonl is one self-describing row
    /// object per line (no header line), tsv has an optional header line, and the default
    /// table mode is a Markdown table delimited by a separator line. A null/negative limit
    /// (no <c>--rows</c>) leaves the output untouched.
    /// </summary>
    public static string LimitRenderedTableRows(string rendered, RowWindow? maxRows, bool hasHeader)
    {
        if (maxRows is not { IsUnlimited: false } limit || string.IsNullOrEmpty(rendered))
            return rendered;

        var trailingNewline = rendered.EndsWith('\n');
        var newline = MarkdownScan.DetectNewline(rendered);
        var body = rendered.ReplaceLineEndings("\n");
        if (trailingNewline)
            body = body.TrimEnd('\n');
        var lines = body.Split('\n');

        // Markdown table (header row followed by a separator line): delegate to the
        // Markdown-aware limiter, which also tolerates surrounding prose/code fences.
        if (lines.Length >= 2 && MarkdownScan.IsTableLine(lines[0]) && MarkdownScan.IsSeparatorLine(lines[1]))
            return MarkdownTableRowLimiter.Apply(rendered, maxRows);

        // jsonl rows are self-describing objects with no header line; tsv keeps its header.
        bool jsonl = lines[0].StartsWith('{');
        int headerLines = !jsonl && hasHeader ? 1 : 0;
        if (lines.Length <= headerLines)
            return rendered;

        var header = lines.Take(headerLines);
        var dataRows = lines.Skip(headerLines);
        var (keepStart, keepEnd) = limit.Resolve(lines.Length - headerLines);
        var windowed = dataRows.Skip(keepStart).Take(keepEnd - keepStart);
        var kept = string.Join(newline, header.Concat(windowed));
        // A zero-width window over a headerless format (JSONL, --no-header TSV) keeps
        // nothing; return empty rather than re-adding the trailing newline, which would
        // emit a phantom blank row / invalid empty JSONL record.
        if (kept.Length == 0)
            return string.Empty;
        return trailingNewline ? kept + newline : kept;
    }

    public static MarkoutWriterOptions ConfigureTableWriterOptions(MarkoutWriterOptions options, bool tsv, bool jsonl)
    {
        if (jsonl)
            options.TableMode = MarkoutTableMode.Jsonl;
        else if (tsv)
            options.TableMode = MarkoutTableMode.Tsv;
        return options;
    }

    public static MarkoutWriterOptions CreateTableWriterOptions(bool tsv, bool jsonl) =>
        ConfigureTableWriterOptions(new MarkoutWriterOptions(), tsv, jsonl);

    public static MarkoutWriterOptions CreateProjectedWriterOptions(
        string[]? columns = null,
        string[]? fields = null,
        RowWindow? rows = null) =>
        new()
        {
            Projection = BuildProjection(columns, fields),
            RowWindow = RowWindow.ToMarkout(rows),
        };

    public static string RenderProjectedTable(
        bool showHeader,
        bool tsv,
        bool jsonl,
        string[]? columns,
        string[]? fields,
        Action<TextWriter, IMarkoutFormatter, MarkoutWriterOptions> serialize,
        RowWindow? maxRows = null)
    {
        var writerOptions = CreateProjectedWriterOptions(columns, fields, maxRows);
        ConfigureTableWriterOptions(writerOptions, tsv, jsonl);
        return RenderTable(showHeader,
            (writer, formatter) => serialize(writer, formatter, writerOptions));
    }

    public static void WriteProjectedTable(
        TextWriter output,
        bool showHeader,
        bool tsv,
        bool jsonl,
        string[]? columns,
        string[]? fields,
        Action<TextWriter, IMarkoutFormatter, MarkoutWriterOptions> serialize,
        RowWindow? maxRows = null) =>
        output.Write(RenderProjectedTable(showHeader, tsv, jsonl, columns, fields, serialize, maxRows));

    internal static RenderedSectionManifest CaptureProjectedTableManifest(
        bool tsv,
        bool jsonl,
        string[]? columns,
        string[]? fields,
        Action<TextWriter, IMarkoutFormatter, MarkoutWriterOptions> serialize,
        DocumentSchema schema,
        RowWindow? maxRows = null,
        string? rootSection = null,
        bool lockRootScope = false)
    {
        RenderedSectionManifest manifest = Capture(columns, fields);
        if (columns is { Length: > 0 }
            && fields is { Length: > 0 })
        {
            manifest.MergeRenderedFieldTablesFrom(Capture(null, fields));
        }

        return manifest;

        RenderedSectionManifest Capture(
            string[]? captureColumns,
            string[]? captureFields)
        {
            var writerOptions = CreateProjectedWriterOptions(
                captureColumns,
                captureFields,
                maxRows);
            ConfigureTableWriterOptions(writerOptions, tsv, jsonl);
            var formatter = new RenderManifestFormatter(
                schema,
                rootSection,
                lockRootScope);
            formatter.BeginDocument(writerOptions);
            serialize(TextWriter.Null, formatter, writerOptions);
            return formatter.Manifest;
        }
    }

    /// <summary>
    /// Renders a view as the lowered JSON view: the same section and projection decisions the
    /// table formats honor, emitted as JSON instead of Markdown/TSV/JSONL (dotnet-inspect#3494).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the <c>--json</c> destination once a caller names <c>--fields</c>/<c>--columns</c>.
    /// Those flags select from the post-lowering vocabulary, so naming one opts into the display
    /// view; plain <c>--json</c> keeps the pre-lowered typed shape and does not come through here.
    /// </para>
    /// <para>
    /// The projection is applied by Markout, not re-implemented here, which is what keeps JSON
    /// content-identical to the table formats at the same shape. An unmatched column therefore
    /// fails the same way it does under <c>--tsv</c>: Markout throws and the top-level handler
    /// reports <c>No columns matched projection</c>. Letting that propagate keeps a bad column
    /// name failing closed instead of silently yielding an empty document.
    /// </para>
    /// <para>
    /// The serialize callback is handed <see cref="TextWriter.Null"/> because JSON is assembled by
    /// the formatter rather than written linearly; the rendered text stream carries no content.
    /// </para>
    /// </remarks>
    public static string RenderProjectedJson(
        string[]? columns,
        string[]? fields,
        Action<TextWriter, IMarkoutFormatter, MarkoutWriterOptions> serialize,
        bool indented = true,
        RowWindow? maxRows = null,
        IReadOnlyList<string>? sectionOrder = null)
    {
        var writerOptions = CreateProjectedWriterOptions(columns, fields, maxRows);
        writerOptions.SectionOrder = sectionOrder;
        // Ask Markout for the JSONL flavor of the header names. The formatter is ours, so this
        // does not change who renders the table -- it changes the vocabulary handed to the
        // renderer, which is how --jsonl and the pre-lowered --json both get machine keys
        // ("type") rather than the display headings Markdown shows ("Type"). Without it the same
        // --json flag would change key casing depending on whether a projection was requested.
        ConfigureTableWriterOptions(writerOptions, tsv: false, jsonl: true);
        var formatter = new JsonSectionFormatter();
        formatter.BeginDocument(writerOptions);
        serialize(TextWriter.Null, formatter, writerOptions);
        return formatter.Finish(indented);
    }

    /// <summary>
    /// Writes the lowered JSON view produced by <see cref="RenderProjectedJson"/>.
    /// </summary>
    /// <remarks>
    /// This does not post-process rendered text with <see cref="LimitRenderedTableRows"/>. A
    /// pretty-printed JSON document has no one-line-per-row correspondence and would be cut
    /// mid-object. Markout applies the window to the data before handing rows to the formatter.
    /// </remarks>
    public static void WriteProjectedJson(
        TextWriter output,
        string[]? columns,
        string[]? fields,
        Action<TextWriter, IMarkoutFormatter, MarkoutWriterOptions> serialize,
        bool indented = true,
        RowWindow? maxRows = null,
        IReadOnlyList<string>? sectionOrder = null) =>
        output.WriteLine(
            RenderProjectedJson(
                columns,
                fields,
                serialize,
                indented,
                maxRows,
                sectionOrder));

    /// <summary>
    /// Serializes a view with <c>--rows</c> applied at the writer seam and writes the result.
    /// </summary>
    /// <remarks>
    /// markout windows rows as it emits them, so the window is applied to table rows the writer
    /// knows about rather than re-derived by parsing rendered Markdown back into tables. That
    /// removes the need to tell a table row from a prose line or a fenced code line after the
    /// fact. The remaining rendered-text windowing site is the <c>@Metadata</c>
    /// lens (#3619), whose content the writer never sees.
    /// </remarks>
    public static void WriteWindowedMarkdown(
        TextWriter output,
        RowWindow? rows,
        Func<MarkoutWriterOptions, string> serialize,
        string[]? columns = null,
        string[]? fields = null) =>
        output.WriteLine(serialize(CreateWindowedOptions(rows, columns, fields)).TrimEnd());

    /// <summary>
    /// Creates writer options carrying a <c>--rows</c> window and optional projection, for callers
    /// that serialize directly rather than through <see cref="WriteWindowedMarkdown"/>.
    /// </summary>
    public static MarkoutWriterOptions CreateWindowedOptions(
        RowWindow? rows,
        string[]? columns = null,
        string[]? fields = null) =>
        new()
        {
            RowWindow = RowWindow.ToMarkout(rows),
            Projection = BuildProjection(columns, fields)
        };

    /// <summary>
    /// Writes <paramref name="payload"/> followed by a single LF, for payloads whose interior is
    /// already LF on every platform.
    /// </summary>
    /// <remarks>
    /// <see cref="TextWriter.WriteLine(string)"/> terminates with the writer's <c>NewLine</c>,
    /// which is CRLF on Windows for <see cref="Console.Out"/>. Using it on an LF-interior payload
    /// yields a document that is LF throughout except for its last line, which is the mixed-ending
    /// shape this method exists to avoid.
    ///
    /// Callers pair this terminator with payloads serialized through an LF-configured writer.
    /// Switching only the terminator for a platform-native payload would introduce the very
    /// mixed-ending shape described above; interior and terminator have to move together.
    /// </remarks>
    public static void WriteLfLine(TextWriter output, string payload)
    {
        output.Write(payload);
        output.Write('\n');
    }

    /// <summary>
    /// Writes version/feed rows in whichever format the caller selected.
    /// </summary>
    /// <remarks>
    /// A version carried by two feeds appears twice, once per feed. That is the point of the
    /// view: every other listing collapses feeds together, so this is where cross-feed
    /// duplication becomes visible.
    /// </remarks>
    /// <summary>
    /// Writes versions with the feed that served each one. A <c>Listing</c> column appears only
    /// when the set actually contains an unlisted version, so the common case stays two columns.
    /// </summary>
    public static void WriteVersionFeedTable(
        IEnumerable<PackageVersionSourceInfo> versionFeeds,
        InspectionOptions options,
        TextWriter output)
    {
        var items = versionFeeds.ToArray();

        if (options.JsonOutput
            && options.Limit is null)
        {
            var objects = items
                .Select(
                    v => new VersionFeedJson(
                        v.Version,
                        v.Feed,
                        v.Listed))
                .ToList();
            output.WriteLine(JsonSerializer.Serialize(objects, JsonContext.Default.ListVersionFeedJson));
            return;
        }

        bool showListing = items.Any(v => !v.Listed);
        string[] display = showListing ? ["Version", "Feed", "Listing"] : ["Version", "Feed"];
        string[] stable = showListing ? ["version", "feed", "listing"] : ["version", "feed"];
        var rows = items
            .Select(v => showListing
                ? new[] { v.Version, v.Feed, v.Listed ? "listed" : "unlisted" }
                : new[] { v.Version, v.Feed })
            .ToArray();

        WriteTable(output, showHeader: !options.NoHeader, (writer, formatter) =>
        {
            var markoutWriter = new MarkoutWriter(writer, formatter, CreateTableWriterOptions(options.Tsv, options.Jsonl));
            markoutWriter.WriteTable(display, stable, rows);
            markoutWriter.Flush();
        });
    }

    public static void WriteStringList(IEnumerable<string> values, string displayName, string stableName,
        bool tsv, bool jsonl, TextWriter output)
    {
        var rows = values.Select(value => new[] { value }).ToArray();
        WriteTable(output, showHeader: false, (writer, formatter) =>
        {
            var markoutWriter = new MarkoutWriter(writer, formatter, CreateTableWriterOptions(tsv, jsonl));
            if (jsonl)
                markoutWriter.WriteTable([displayName], [stableName], rows);
            else
                markoutWriter.WriteList(rows.Select(row => row[0]).ToArray());
            markoutWriter.Flush();
        });
    }

    /// <summary>
    /// Writes a version list annotated with listing status as a two-column Version/Listing table.
    /// Used by <c>--versions --include-unlisted</c> so unlisted versions are marked rather than
    /// silently included.
    /// </summary>
    public static void WriteVersionListings(IEnumerable<PackageVersionInfo> versions,
        InspectionOptions options, TextWriter output)
    {
        var items = versions.ToArray();
        if (options.JsonOutput
            && options.Limit is null)
        {
            var objects = items
                .Select(
                    v => new VersionListingJson(
                        v.Version,
                        v.Listed ? "listed" : "unlisted"))
                .ToList();
            output.WriteLine(
                JsonSerializer.Serialize(
                    objects,
                    JsonContext.Default.ListVersionListingJson));
            return;
        }

        var rows = items.Select(v => new[] { v.Version, v.Listed ? "listed" : "unlisted" }).ToArray();
        WriteTable(output, showHeader: !options.NoHeader, (writer, formatter) =>
        {
            var markoutWriter = new MarkoutWriter(
                writer,
                formatter,
                CreateTableWriterOptions(options.Tsv, options.Jsonl));
            markoutWriter.WriteTable(["Version", "Listing"], ["version", "listing"], rows);
            markoutWriter.Flush();
        });
    }

    /// <summary>
    /// The ordered sections a <c>--count</c> map should report, or <c>null</c> when the selection
    /// names at most one section and a scalar count is the answer.
    /// </summary>
    /// <remarks>
    /// Bare <c>-S</c> on a curated pipeline carries its selection as
    /// <see cref="LibraryOptions.FixedOverview"/> rather than as an include set, so a map decision
    /// that reads only <paramref name="includeSections"/> silently degrades a multi-section
    /// overview to one meaningless total across heterogeneous tables (#3547). The request - not the
    /// rendered set - is what the map describes, so a requested section with no rows reports zero
    /// rather than disappearing, matching how a category renders.
    /// </remarks>
    internal static IReadOnlyList<string>? ResolveCountMapSections<TModel>(
        SectionPipeline<TModel> pipeline, HashSet<string>? includeSections, bool fixedOverview)
    {
        var requested = includeSections is { Count: > 0 }
            ? includeSections
            : fixedOverview
                ? new HashSet<string>(pipeline.BareSelectSectionNames, StringComparer.OrdinalIgnoreCase)
                : null;

        if (requested is not { Count: > 1 })
            return null;

        return requested.OrderBy(
            section => section,
            StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string FormatResult(InspectionResult result, InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
    {
        if (options.JsonOutput && !options.Count)
        {
            return JsonSerializer.Serialize(
                PackageInspectionJson.Create(result, options.Rows),
                PackageInspectionJsonContext.Default.PackageInspectionJson);
        }

        var view = new InspectionResultView(
            result,
            includeTitleVersion: false);
        var writerOptions = BuildPackageDocumentWriterOptions(result, options, pipeline);
        if (options.Count)
        {
            var projection = CapturePackageCountProjection(
                result,
                options,
                pipeline);
            var ordered = ResolveCountMapSections(
                pipeline, options.IncludeSections, options.FixedOverview);
            return CountOutput.Render(
                projection, ordered, options.Format, options.NoHeader);
        }

        if (options.Format == OutputFormat.Markdown
            && options.Columns is not { Length: > 0 }
            && options.Fields is not { Length: > 0 }
            && result.DependencyHierarchyProjection is { } hierarchy
            && writerOptions.IncludeSections?.Contains(
                PackageSections.DependencyHierarchy) == true)
        {
            writerOptions.IncludeSections =
                writerOptions.IncludeSections
                    .Where(section => !section.Equals(
                        PackageSections.DependencyHierarchy,
                        StringComparison.OrdinalIgnoreCase))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            string package = MarkoutSerializer.Serialize(
                view,
                InspectionContext.Default,
                writerOptions).TrimEnd();
            string hierarchySection =
                DependsCommand.RenderHierarchySection(
                    hierarchy,
                    options.Rows,
                    embeddedMermaid: false);
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                new[] { package, hierarchySection }
                    .Where(static fragment =>
                        !string.IsNullOrWhiteSpace(fragment)));
        }

        return MarkoutSerializer.Serialize(
            view, InspectionContext.Default, writerOptions).TrimEnd();
    }

    internal static CountProjection CapturePackageCountProjection(
        InspectionResult result,
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
    {
        MarkoutWriterOptions writerOptions =
            BuildPackageDocumentWriterOptions(result, options, pipeline);
        CountProjection projection = CountProjectionFormatter.Capture(
            new InspectionResultView(
                result,
                includeTitleVersion: false),
            InspectionContext.Default,
            writerOptions);
        if (result.DependencyHierarchyProjection is { } hierarchy
            && writerOptions.IncludeSections?.Contains(
                PackageSections.DependencyHierarchy) == true)
        {
            int count = options.Rows is { IsUnlimited: false } window
                ? window.Apply(hierarchy.HierarchyRows).Count
                : hierarchy.HierarchyRows.Length;
            projection.SetRows(
                PackageSections.DependencyHierarchy,
                count);
        }
        return projection;
    }

    internal static MarkoutWriterOptions BuildPackageDocumentWriterOptions(
        InspectionResult result,
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
    {
        bool selectAll = SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections);
        bool selectInfo = SelectResolver.IsActiveInfoSelector(options.SelectDefault, options.IncludeSections);
        var writerOptions = BuildWriterOptions(result, options, pipeline);
        if (selectAll)
            writerOptions.SectionOrder = pipeline.GetAllSelectorSections(result);
        else if (selectInfo)
            writerOptions.SectionOrder = pipeline.InfoSectionNames;
        writerOptions.RowWindow = RowWindow.ToMarkout(options.Rows);
        return writerOptions;
    }

    /// <summary>
    /// Renders one package section as tabular output (TSV/JSONL/pretty table). The caller has
    /// already narrowed <paramref name="options"/> to a single section, so the rendered text is a
    /// single table and <c>--rows</c> windows it exactly as it windows every other tabular section.
    /// Forwarding <c>options.Rows</c> is what keeps this path agreeing with <c>--count</c>, which
    /// windows the same section through <see cref="FormatResult"/> (#3457).
    /// </summary>
    public static void WritePackageTable(InspectionResult result, InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline, bool showHeader)
    {
        var writerOpts = BuildWriterOptions(result, options, pipeline);
        ConfigureTableWriterOptions(writerOpts, options.Tsv, options.Jsonl);
        var view = new InspectionResultView(
            result);
        WriteTable(Console.Out, showHeader,
            (writer, formatter) => MarkoutSerializer.Serialize(view, writer, formatter, InspectionContext.Default, writerOpts),
            options.Rows);
    }

    /// <summary>
    /// Checks whether the computed writer options would produce multiple sections.
    /// Used by commands to decide whether to auto-promote to markdown or error.
    /// </summary>
    public static RenderDiagnostic? CheckMultiSection(InspectionResult result, InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
    {
        var writerOpts = BuildWriterOptions(result, options, pipeline);
        if (writerOpts.IncludeSections is { Count: > 1 })
            return new RenderDiagnostic("table", "multiple_sections",
                writerOpts.IncludeSections.ToArray());
        return null;
    }

    internal static MarkoutWriterOptions BuildWriterOptions(InspectionResult result, InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
    {
        var selectAll = SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections);
        var selectInfo = SelectResolver.IsActiveInfoSelector(options.SelectDefault, options.IncludeSections);
        var includeSections = pipeline.ComputeIncludeSections(
            result, options.Verbosity, options.IncludeSections, selectAll, options.FixedOverview);

        return new MarkoutWriterOptions
        {
            IncludeSections = includeSections,
            IncludeDescription = options.Verbosity != Verbosity.Quiet
                && options.IncludeSections is not { Count: > 0 }
                && !selectInfo,
            Projection = BuildProjection(options.Columns, options.Fields)
        };
    }

    public static void WriteLibraryResult(LibraryInspection inspection, LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        bool selectAll = SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections);
        bool topFieldsOnly = ShouldRenderLibraryContext(options);
        var auditView = new LibraryInspectionView(inspection, topFieldsOnly);
        var includeSections = pipeline.ComputeIncludeSections(
            inspection, options.Verbosity, options.IncludeSections, selectAll, options.FixedOverview);
        var writerOpts = new MarkoutWriterOptions
        {
            IncludeSections = includeSections,
            Projection = BuildProjection(options.Columns, options.Fields)
        };

        if (IsSingleReferenceHierarchySelection(options))
        {
            WriteLibraryReferenceHierarchyResults(
                [inspection],
                options);
            return;
        }

        if (options.Count)
        {
            var projection = CaptureLibraryCountProjection(
                auditView, inspection, writerOpts, options.Rows, options.Fields, options.Columns);
            var ordered = ResolveCountMapSections(pipeline, options.IncludeSections, options.FixedOverview);
            CountOutput.Write(
                projection, ordered, options.Format, options.NoHeader, options.OutputPath, options.Rows);
            return;
        }

        OutputDestination.Write(
            options.OutputPath,
            options.Rows,
            output =>
            {
                if (options.JsonOutput)
                {
                    output.WriteLine(JsonSerializer.Serialize(
                        inspection,
                        JsonContext.Default.LibraryInspection));
                    return;
                }

                if (options.Format == OutputFormat.PlainText)
                {
                    WriteLfLine(output, SerializeLibraryPlainText(
                        auditView, inspection, writerOpts, options.Rows));
                }
                else if (options.VerbosityEnabled)
                {
                    var markdown = SerializeLibraryMarkdown(
                        auditView, inspection, writerOpts, pipeline, options.Rows);
                    WriteLfLine(output, markdown);
                }
                else if (writerOpts.IncludeSections is { Count: > 1 }
                         && !options.TabularExplicitlySet)
                {
                    // Auto-promote to markdown when multiple sections and tabular output wasn't explicitly requested
                    var markdown = SerializeLibraryMarkdown(
                        auditView, inspection, writerOpts, pipeline, options.Rows);
                    WriteLfLine(output, markdown);
                }
                else
                {
                    ConfigureTableWriterOptions(writerOpts, options.Tsv, options.Jsonl);
                    WriteLibraryTabular(
                        auditView,
                        inspection,
                        writerOpts,
                        options,
                        output);
                }
            });
    }

    private static string SerializeLibraryPlainText(
        LibraryInspectionView auditView,
        LibraryInspection inspection,
        MarkoutWriterOptions writerOpts,
        RowWindow? rows)
    {
        var includesMetadata = MetadataLensRenderer.IsSelected(writerOpts.IncludeSections);
        if (!includesMetadata)
            writerOpts.RowWindow = RowWindow.ToMarkout(rows);

        // Serialize into an LF writer rather than straight to Console.Out, whose ambient CRLF
        // would otherwise terminate lines whose interiors this branch already emits as LF —
        // the appended metadata is LF on every platform. Buffering matches the Markdown
        // sibling below, which composes its document before writing for the same reason.
        var plain = new StringWriter { NewLine = "\n" };
        MarkoutSerializer.Serialize(
            auditView, plain, new PlainTextFormatter(), InspectionContext.Default, writerOpts);
        var plainText = plain.ToString().TrimEnd();
        if (MetadataLensRenderer.RenderMarkdown(
            inspection,
            writerOpts.IncludeSections,
            writerOpts.Projection?.IncludeColumns) is { } plainMetadata)
        {
            var trimmedMetadata = plainMetadata.TrimEnd();
            // A single separator, not a blank line: the streamed original ended its last body
            // line and wrote the metadata on the next one. The Markdown sibling below joins
            // with a blank line because Markdown sections require one; plain text does not.
            plainText = plainText.Length == 0
                ? trimmedMetadata
                : plainText + "\n" + trimmedMetadata;
        }

        return includesMetadata
            ? MarkdownTableRowLimiter.Apply(plainText, rows)
            : plainText;
    }

    /// <summary>
    /// Serializes the library view and, when the <c>@Metadata</c> lens is selected, composes its
    /// sections into the same Markdown document before ordering and row windowing.
    ///
    /// Metadata sections are rendered as Markdown text and appended, so ordering and row
    /// windowing have to run *after* the append. Without metadata, both operations run directly
    /// at the writer seam.
    ///
    /// This is the last caller of <see cref="MarkdownSectionOrderer"/> and the reason it and
    /// <see cref="MarkdownTableRowLimiter"/> still exist. Every other section producer applies
    /// ordering and row windowing at the writer seam via <see cref="MarkoutWriterOptions"/>.
    /// The stated reason for hand-writing — metadata columns differ per table, so they cannot be
    /// attributed view properties — no longer holds: markout's <c>MarkoutTable</c> models a table
    /// whose columns are runtime data. What still blocks the migration is the lens's caveat
    /// *prose*, which has no serializer-model expression. See #3619 (the migration) and #3620
    /// (the prose gap, which blocks it).
    /// </summary>
    internal static string SerializeLibraryMarkdown(
        LibraryInspectionView auditView,
        LibraryInspection inspection,
        MarkoutWriterOptions writerOpts,
        SectionPipeline<LibraryInspection> pipeline,
        RowWindow? rows)
    {
        var includesMetadata = MetadataLensRenderer.IsSelected(writerOpts.IncludeSections);
        if (!includesMetadata)
        {
            writerOpts.RowWindow = RowWindow.ToMarkout(rows);
        }

        // Serialize through an LF writer rather than the string-returning overload, which inherits
        // Environment.NewLine. The metadata half appended below is LF on every platform, and
        // MarkdownSectionOrderer rejoins on whichever ending it detects — so a CRLF shell here
        // would both mix endings and normalize the metadata sections back to CRLF.
        //
        // TrimEnd restores parity with the string overload, which returns no trailing newline
        // while the TextWriter overload writes one. Without it the callers' own terminator lands
        // on top of it and emits a stray blank line on every platform.
        var shell = new StringWriter { NewLine = "\n" };
        MarkoutSerializer.Serialize(auditView, shell, InspectionContext.Default, writerOpts);
        var markdown = shell.ToString().TrimEnd();

        if (MetadataLensRenderer.RenderMarkdown(inspection, writerOpts.IncludeSections, writerOpts.Projection?.IncludeColumns) is { } metadata)
        {
            var body = markdown.TrimEnd();
            markdown = body.Length == 0 ? metadata : body + "\n" + "\n" + metadata;
        }

        if (!includesMetadata)
            return markdown;

        markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.AlphabeticalSectionOrder);
        return MarkdownTableRowLimiter.Apply(markdown, rows);
    }

    /// <summary>
    /// Renders one library inspection as tabular output (TSV/JSONL/pretty table). When the selected
    /// sections are the kind-scoped <c>@Performance</c> group (all sharing one row view), they are
    /// flattened into a single self-describing table with a leading <c>Kind</c> column — one header,
    /// aligned columns, and correct <c>--rows</c> accounting — instead of concatenated per-kind
    /// tables. This path is shared by the single- and multi-assembly renderers so both stay
    /// consistent. <paramref name="writerOpts"/> must already have its TSV/JSONL format configured.
    /// </summary>
    private static void WriteLibraryTabular(
        LibraryInspectionView auditView, LibraryInspection inspection,
        MarkoutWriterOptions writerOpts, LibraryOptions options,
        TextWriter output,
        string[]? provenanceHeaders = null,
        string[]? provenanceValues = null,
        bool? showHeader = null)
    {
        bool includeHeader =
            showHeader ?? !options.NoHeader;
        IMarkoutFormatter AddProducerLibrary(
            IMarkoutFormatter formatter) =>
            provenanceHeaders is null
                ? formatter
                : new ProducerLibraryTableFormatter(
                    formatter,
                    provenanceHeaders,
                    provenanceValues
                        ?? throw new InvalidOperationException(
                            "Producer-Library provenance values were not supplied."),
                    options.Columns);

        // The metadata lens owns its own tabular rendering for the same reason it owns its
        // Markdown rendering: per-table column shapes have no static row type for Markout to bind.
        // Its rows already self-identify with a leading Table/Section column. It still goes through
        // WriteTable so `--rows` windows it exactly as it windows every other tabular section —
        // the limiter operates on rendered text and needs no knowledge of the lens.
        if (MetadataLensRenderer.IsSelected(writerOpts.IncludeSections))
        {
            var format = MetadataLensRenderer.FormatFor(options.Tsv, options.Jsonl);
            WriteTable(output, includeHeader,
                (writer, _) => MetadataLensRenderer.TryRenderTabular(
                    inspection, writerOpts.IncludeSections, format, writer, CommandError.Writer,
                    writerOpts.Projection?.IncludeColumns),
                options.Rows);
            return;
        }

        if (writerOpts.IncludeSections is { Count: > 1 }
            && Sections.PerformanceKinds.AllShareCommonView(writerOpts.IncludeSections))
        {
            var groupRows = auditView.PerformanceGroupRows(writerOpts.IncludeSections);
            var groupView = new PerformanceGroupView(groupRows);
            var groupOpts = ConfigureTableWriterOptions(
                new MarkoutWriterOptions { Projection = writerOpts.Projection }, options.Tsv, options.Jsonl);
            WriteTable(output, includeHeader,
                (writer, formatter) => MarkoutSerializer.Serialize(
                    groupView,
                    writer,
                    AddProducerLibrary(formatter),
                    InspectionContext.Default,
                    groupOpts),
                options.Rows);
        }
        else
        {
            WriteTable(output, includeHeader,
                (writer, formatter) => MarkoutSerializer.Serialize(
                    auditView,
                    writer,
                    AddProducerLibrary(formatter),
                    InspectionContext.Default,
                    writerOpts),
                options.Rows);
        }
    }

    public static bool WriteLibraryResults(
        List<LibraryInspection> inspections,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline) =>
        WriteLibraryResults(
            inspections,
            Path.GetFileNameWithoutExtension(inspections[0].FileName),
            options,
            pipeline);

    public static bool WriteLibraryResults(
        List<LibraryInspection> inspections,
        string documentTitle,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline) =>
        WriteLibraryResultsCore(
            inspections,
            documentTitle,
            options,
            pipeline,
            aggregatePackage: null,
            aggregateVersion: null);

    internal static bool WritePackageAggregateResults(
        List<LibraryInspection> inspections,
        string documentTitle,
        string aggregatePackage,
        string? aggregateVersion,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline) =>
        WriteLibraryResultsCore(
            inspections,
            documentTitle,
            options,
            pipeline,
            aggregatePackage,
            aggregateVersion);

    private static bool WriteLibraryResultsCore(
        List<LibraryInspection> inspections,
        string documentTitle,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline,
        string? aggregatePackage,
        string? aggregateVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentTitle);
        if (aggregatePackage is null
            && aggregateVersion is not null)
        {
            throw new InvalidOperationException(
                "Package aggregate version requires package identity.");
        }

        bool selectAll = SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections);
        bool topFieldsOnly = ShouldRenderLibraryContext(options);
        bool packageAggregate = aggregatePackage is not null;

        MarkoutWriterOptions WriterOptions(LibraryInspection inspection) => new()
        {
            IncludeSections = pipeline.ComputeIncludeSections(
                inspection, options.Verbosity, options.IncludeSections, selectAll, options.FixedOverview),
            Projection = BuildProjection(options.Columns, options.Fields)
        };

        if (IsSingleReferenceHierarchySelection(options))
        {
            WriteLibraryReferenceHierarchyResults(
                inspections,
                options);
            return true;
        }

        if (options.Count)
        {
            var ordered = ResolveCountMapSections(
                pipeline,
                options.IncludeSections,
                options.FixedOverview);
            if (packageAggregate)
            {
                var projection = CaptureLibraryCountProjection(
                    inspections,
                    topFieldsOnly,
                    WriterOptions,
                    options,
                    packageAggregate: true,
                    pipeline);
                CountOutput.Write(
                    projection,
                    ordered,
                    options.Format,
                    options.NoHeader,
                    options.OutputPath,
                    options.Rows);
                return true;
            }

            var frameworkGroups = inspections
                .GroupBy(
                    inspection => inspection.Tfm,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (frameworkGroups.Length == 1)
            {
                var projection = CaptureLibraryCountProjection(
                    frameworkGroups[0],
                    topFieldsOnly,
                    WriterOptions,
                    options,
                    packageAggregate,
                    pipeline);
                CountOutput.Write(
                    projection,
                    ordered,
                    options.Format,
                    options.NoHeader,
                    options.OutputPath,
                    options.Rows);
                return true;
            }

            if (!CountOutput.ValidateRowSetTableFormat(
                    options.Format,
                    options.Tree))
                return false;

            string? selectedSection = ordered is null
                ? options.IncludeSections is { Count: 1 } selected
                    ? selected.Single()
                    : throw new InvalidOperationException(
                        "A single-section count must retain its selected section.")
                : null;
            var rowSetCounts = new List<RowSetCount>();
            foreach (var frameworkGroup in frameworkGroups)
            {
                var projection = CaptureLibraryCountProjection(
                    frameworkGroup,
                    topFieldsOnly,
                    WriterOptions,
                    options,
                    packageAggregate,
                    pipeline);
                string framework = frameworkGroup.Key
                    ?? throw new InvalidOperationException(
                        "A multi-framework count must retain framework identity.");
                if (ordered is null)
                {
                    rowSetCounts.Add(
                        new RowSetCount(
                            $"{framework} / {selectedSection}",
                            projection.Total));
                    continue;
                }

                foreach (string section in ordered)
                {
                    rowSetCounts.Add(
                        new RowSetCount(
                            $"{framework} / {section}",
                            projection.SectionCounts.GetValueOrDefault(section)));
                }
            }

            CountOutput.WriteRowSetCounts(
                rowSetCounts,
                options.Format,
                options.NoHeader,
                options.OutputPath,
                options.Rows);
            return true;
        }

        OutputDestination.Write(
            options.OutputPath,
            options.Rows,
            output =>
            {
                if (options.JsonOutput)
                {
                    if (packageAggregate)
                    {
                        output.WriteLine(JsonSerializer.Serialize(
                            CreatePackageLibraryAggregateJson(
                                inspections,
                                aggregatePackage!,
                                aggregateVersion,
                                topFieldsOnly,
                                WriterOptions,
                                options,
                                pipeline),
                            PackageLibraryAggregateJsonContext.Default
                                .PackageLibraryAggregateJson));
                    }
                    else
                    {
                        output.WriteLine(JsonSerializer.Serialize(
                            inspections.ToArray(),
                            JsonContext.Default.LibraryInspectionArray));
                    }
                    return;
                }

                if (options.Format == OutputFormat.PlainText)
                {
                    var documents = new List<string>
                    {
                        LibraryViewText.Contain(documentTitle) ?? string.Empty,
                    };
                    if (!packageAggregate)
                        documents.Add("Libraries");
                    documents.AddRange(inspections.Select(inspection =>
                    {
                        var auditView = new LibraryInspectionView(inspection, topFieldsOnly);
                        var writerOpts =
                            packageAggregate
                                ? WithoutAggregateSections(
                                    WriterOptions(inspection),
                                    pipeline)
                                : WriterOptions(inspection);
                        var title = LibraryViewText.DocumentTitle(inspection);
                        var body = RemovePlainTextDocumentTitle(
                            SerializeLibraryPlainText(
                                auditView, inspection, writerOpts, options.Rows),
                            LibraryViewText.DocumentTitle(auditView));
                        return body.Length == 0
                            ? packageAggregate
                                ? string.Empty
                                : title
                            : title + "\n\n" + body;
                    }).Where(document => document.Length > 0));
                    if (packageAggregate)
                    {
                        foreach (string section in SelectedAggregateSections(
                                     inspections,
                                     WriterOptions,
                                     pipeline))
                        {
                            var table = CaptureAggregateSectionTable(
                                inspections,
                                section,
                                WriterOptions,
                                options,
                                machineFormat: false,
                                aggregatePackage: null,
                                aggregateVersion: null);
                            string rendered =
                                table.RenderPlainText(
                                    section,
                                    options.Rows);
                            if (rendered.Length > 0)
                                documents.Add(rendered);
                        }
                    }
                    WriteLfLine(output, string.Join("\n\n", documents));
                }
                else if (options.VerbosityEnabled)
                {
                    var documents = new List<string>
                    {
                        RenderMarkdownHeading(
                            1,
                            LibraryViewText.Contain(documentTitle) ?? string.Empty)
                    };
                    if (packageAggregate)
                    {
                        documents.AddRange(inspections.Select(inspection =>
                        {
                            var auditView = new LibraryInspectionView(
                                inspection,
                                topFieldsOnly);
                            var title =
                                LibraryViewText.DocumentTitle(inspection);
                            var writerOptions =
                                WithoutAggregateSections(
                                    WriterOptions(inspection),
                                    pipeline);
                            var body = RemoveMarkdownDocumentTitle(
                                SerializeLibraryMarkdown(
                                    auditView,
                                    inspection,
                                    writerOptions,
                                    pipeline,
                                    options.Rows));
                            return QualifyMarkdownSectionHeadings(
                                body,
                                title);
                        }).Where(document => document.Length > 0));
                        foreach (string section in SelectedAggregateSections(
                                     inspections,
                                     WriterOptions,
                                     pipeline))
                        {
                            var table = CaptureAggregateSectionTable(
                                inspections,
                                section,
                                WriterOptions,
                                options,
                                machineFormat: false,
                                aggregatePackage: null,
                                aggregateVersion: null);
                            string rendered =
                                table.RenderMarkdown(
                                    section,
                                    options.Rows);
                            if (rendered.Length > 0)
                                documents.Add(rendered);
                        }
                    }
                    else
                    {
                        documents.AddRange(inspections.Select(inspection =>
                        {
                            var auditView = new LibraryInspectionView(inspection, topFieldsOnly);
                            var title = LibraryViewText.DocumentTitle(inspection);
                            var body = RemoveMarkdownDocumentTitle(SerializeLibraryMarkdown(
                                auditView, inspection, WriterOptions(inspection), pipeline, options.Rows));
                            return QualifyMarkdownSectionHeadings(body, title);
                        }).Where(document => document.Length > 0));
                    }
                    var markdown = string.Join("\n\n", documents);
                    WriteLfLine(output, markdown);
                }
                else if (packageAggregate)
                {
                    WritePackageAggregateTabular(
                        inspections,
                        aggregatePackage!,
                        aggregateVersion,
                        topFieldsOnly,
                        WriterOptions,
                        options,
                        pipeline,
                        output);
                }
                else
                {
                    CountingTextWriter? tsvOutput =
                        options.Tsv
                            ? new CountingTextWriter(output)
                            : null;
                    TextWriter tableOutput = tsvOutput ?? output;
                    bool tsvTableWritten = false;
                    foreach (LibraryInspection inspection in inspections)
                    {
                        var auditView = new LibraryInspectionView(inspection, topFieldsOnly);
                        var writerOpts = WriterOptions(inspection);
                        writerOpts.Projection =
                            BuildProjection(fields: options.Fields);
                        ConfigureTableWriterOptions(writerOpts, options.Tsv, options.Jsonl);
                        WriteLibraryTabular(
                            auditView,
                            inspection,
                            writerOpts,
                            options,
                            tableOutput,
                            ["library"],
                            [
                                LibraryViewText.Contain(
                                    inspection.FileName)
                                    ?? string.Empty,
                            ],
                            showHeader:
                                !options.NoHeader
                                && (!options.Tsv || !tsvTableWritten));
                        if (tsvOutput is not null
                            && tsvOutput.CharCount > 0)
                        {
                            tsvTableWritten = true;
                        }
                    }
                }
            });
        return true;
    }

    private static bool IsSingleReferenceHierarchySelection(
        LibraryOptions options) =>
        options.IncludeSections is { Count: 1 }
        && options.IncludeSections.Contains(
            SectionNames.ReferenceHierarchy);

    private static void WriteLibraryReferenceHierarchyResult(
        LibraryInspection inspection,
        LibraryOptions options)
    {
        DependsAssetProjection projection =
            inspection.ReferenceHierarchyProjection
            ?? throw new InvalidOperationException(
                "The Library reference hierarchy was not acquired.");
        IReadOnlyList<DependencyHierarchyOccurrenceRow> rows =
            WindowHierarchyRows(projection, options.Rows);
        if (options.Count)
        {
            CountOutput.WriteCountResult(
                rows.Count.ToString(CultureInfo.InvariantCulture),
                options.OutputPath,
                options.Rows);
            return;
        }

        OutputDestination.Write(
            options.OutputPath,
            options.Rows,
            output => WriteLibraryReferenceHierarchyDocument(
                inspection,
                projection,
                rows,
                options,
                output));
    }

    private static void WriteLibraryReferenceHierarchyResults(
        IReadOnlyList<LibraryInspection> inspections,
        LibraryOptions options)
    {
        if (inspections.Count == 1)
        {
            WriteLibraryReferenceHierarchyResult(
                inspections[0],
                options);
            return;
        }

        if (options.Count)
        {
            int count = inspections.Sum(
                inspection => WindowHierarchyRows(
                    inspection.ReferenceHierarchyProjection
                    ?? throw new InvalidOperationException(
                        "The Library reference hierarchy was not acquired."),
                    options.Rows).Count);
            CountOutput.WriteCountResult(
                count.ToString(CultureInfo.InvariantCulture),
                options.OutputPath,
                options.Rows);
            return;
        }

        OutputDestination.Write(
            options.OutputPath,
            options.Rows,
            output =>
            {
                if (options.JsonOutput)
                {
                    LibraryReferenceHierarchyJson[] documents =
                    [
                        .. inspections.Select(inspection =>
                        {
                            DependsAssetProjection projection =
                                inspection.ReferenceHierarchyProjection
                                ?? throw new InvalidOperationException(
                                    "The Library reference hierarchy was not acquired.");
                            IReadOnlyList<DependencyHierarchyOccurrenceRow>
                                rows = WindowHierarchyRows(
                                    projection,
                                    options.Rows);
                            return CreateReferenceHierarchyJson(
                                inspection,
                                projection,
                                rows);
                        }),
                    ];
                    output.WriteLine(JsonSerializer.Serialize(
                        documents,
                        LibraryReferenceHierarchyJsonContext.Default
                            .LibraryReferenceHierarchyJsonArray));
                    return;
                }

                for (int index = 0; index < inspections.Count; index++)
                {
                    if (index > 0)
                        output.WriteLine();
                    LibraryInspection inspection = inspections[index];
                    DependsAssetProjection projection =
                        inspection.ReferenceHierarchyProjection
                        ?? throw new InvalidOperationException(
                            "The Library reference hierarchy was not acquired.");
                    WriteLibraryReferenceHierarchyDocument(
                        inspection,
                        projection,
                        WindowHierarchyRows(projection, options.Rows),
                        options,
                        output);
                }
            });
    }

    private static void WriteLibraryReferenceHierarchyDocument(
        LibraryInspection inspection,
        DependsAssetProjection projection,
        IReadOnlyList<DependencyHierarchyOccurrenceRow> rows,
        LibraryOptions options,
        TextWriter output)
    {
        if (options.Tree)
        {
            DependencyHierarchyOutputAdapter.Write(
                projection.Hierarchy,
                rows,
                OutputFormat.PlainText,
                tree: true,
                embeddedMermaid: false,
                options.NoHeader,
                compactJson: false,
                output);
            return;
        }

        bool projected =
            options.Columns is { Length: > 0 }
            || options.Fields is { Length: > 0 };
        var tableView = new LibraryReferenceHierarchyTableView
        {
            ReferenceHierarchy =
            [
                .. rows.Select(DependsHierarchyOccurrenceView.From),
            ],
        };
        if (options.JsonOutput)
        {
            if (projected)
            {
                WriteProjectedJson(
                    output,
                    options.Columns,
                    options.Fields,
                    (writer, formatter, writerOptions) =>
                    {
                        writerOptions.IncludeSections =
                            [SectionNames.ReferenceHierarchy];
                        MarkoutSerializer.Serialize(
                            tableView,
                            writer,
                            formatter,
                            DependsAssetViewContext.Default,
                            writerOptions);
                    },
                    indented: true);
            }
            else
            {
                output.WriteLine(JsonSerializer.Serialize(
                    CreateReferenceHierarchyJson(
                        inspection,
                        projection,
                        rows),
                    LibraryReferenceHierarchyJsonContext.Default
                        .LibraryReferenceHierarchyJson));
            }
            return;
        }

        if (options.Tabular)
        {
            if (projected)
            {
                WriteProjectedTable(
                    output,
                    !options.NoHeader,
                    options.Tsv,
                    options.Jsonl,
                    options.Columns,
                    options.Fields,
                    (writer, formatter, writerOptions) =>
                    {
                        writerOptions.IncludeSections =
                            [SectionNames.ReferenceHierarchy];
                        MarkoutSerializer.Serialize(
                            tableView,
                            writer,
                            formatter,
                            DependsAssetViewContext.Default,
                            writerOptions);
                    });
            }
            else
            {
                DependencyHierarchyOutputAdapter.Write(
                    projection.Hierarchy,
                    rows,
                    options.Format,
                    tree: false,
                    embeddedMermaid: false,
                    options.NoHeader,
                    compactJson: false,
                    output);
            }
            return;
        }

        if (projected)
        {
            var writerOptions = new MarkoutWriterOptions
            {
                IncludeSections = [SectionNames.ReferenceHierarchy],
                Projection = BuildProjection(
                    options.Columns,
                    options.Fields),
            };
            MarkoutSerializer.Serialize(
                tableView,
                output,
                options.Format == OutputFormat.PlainText
                    ? new PlainTextFormatter()
                    : new MarkdownFormatter(),
                DependsAssetViewContext.Default,
                writerOptions);
            return;
        }

        if (options.Format == OutputFormat.Markdown)
        {
            output.WriteLine(RenderMarkdownHeading(
                1,
                LibraryViewText.DocumentTitle(inspection)));
            output.WriteLine();
            output.WriteLine(DependsCommand.RenderHierarchySection(
                projection,
                options.Rows,
                embeddedMermaid: false,
                SectionNames.ReferenceHierarchy));
            return;
        }

        output.WriteLine(LibraryViewText.DocumentTitle(inspection));
        output.WriteLine();
        output.WriteLine(SectionNames.ReferenceHierarchy);
        DependencyHierarchyOutputAdapter.Write(
            projection.Hierarchy,
            rows,
            options.Format,
            tree: false,
            embeddedMermaid: false,
            options.NoHeader,
            compactJson: false,
            output);
    }

    private static LibraryReferenceHierarchyJson
        CreateReferenceHierarchyJson(
        LibraryInspection inspection,
        DependsAssetProjection projection,
        IReadOnlyList<DependencyHierarchyOccurrenceRow> rows) =>
        new(
            inspection.FileName,
            inspection.Tfm,
            DependencyHierarchyOutputAdapter.CreateJsonDocument(
                projection.Hierarchy,
                rows));

    private static IReadOnlyList<DependencyHierarchyOccurrenceRow>
        WindowHierarchyRows(
        DependsAssetProjection projection,
        RowWindow? rows) =>
        rows is { IsUnlimited: false } window
            ? window.Apply(projection.HierarchyRows)
            : projection.HierarchyRows;

    private static CountProjection CaptureLibraryCountProjection(
        IEnumerable<LibraryInspection> inspections,
        bool topFieldsOnly,
        Func<LibraryInspection, MarkoutWriterOptions> writerOptions,
        LibraryOptions options,
        bool packageAggregate,
        SectionPipeline<LibraryInspection> pipeline)
    {
        LibraryInspection[] inspectionArray = [.. inspections];
        var projection = new CountProjection();
        if (packageAggregate)
        {
            foreach (string section in SelectedAggregateSections(
                         inspectionArray,
                         writerOptions,
                         pipeline))
            {
                var table = CaptureAggregateSectionTable(
                    inspectionArray,
                    section,
                    writerOptions,
                    options,
                    machineFormat: false,
                    aggregatePackage: null,
                    aggregateVersion: null);
                if (table.HasTable)
                {
                    projection.RecordRows(
                        section,
                        table.WindowedRowCount(options.Rows));
                }
            }
        }

        foreach (LibraryInspection inspection in inspectionArray)
        {
            var auditView = new LibraryInspectionView(
                inspection,
                topFieldsOnly);
            MarkoutWriterOptions participantOptions =
                writerOptions(inspection);
            if (packageAggregate)
            {
                participantOptions =
                    WithoutAggregateSections(
                        participantOptions,
                        pipeline);
                if (participantOptions.IncludeSections is { Count: 0 })
                    continue;
            }
            projection.Merge(CaptureLibraryCountProjection(
                auditView,
                inspection,
                participantOptions,
                options.Rows,
                options.Fields,
                options.Columns));
        }

        return projection;
    }

    private static void WritePackageAggregateTabular(
        IReadOnlyList<LibraryInspection> inspections,
        string aggregatePackage,
        string? aggregateVersion,
        bool topFieldsOnly,
        Func<LibraryInspection, MarkoutWriterOptions> writerOptions,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline,
        TextWriter output)
    {
        string[] aggregateSections =
        [
            .. SelectedAggregateSections(
                inspections,
                writerOptions,
                pipeline),
        ];
        if (aggregateSections.Length > 0)
        {
            if (aggregateSections.Length != 1)
            {
                throw new InvalidOperationException(
                    "A package aggregate row format requires one aggregate section shape.");
            }

            var table = CaptureAggregateSectionTable(
                inspections,
                aggregateSections[0],
                writerOptions,
                options,
                machineFormat: true,
                aggregatePackage,
                aggregateVersion);

            var formatOptions =
                CreateTableWriterOptions(
                    options.Tsv,
                    options.Jsonl);
            WriteTable(
                output,
                !options.NoHeader,
                (writer, formatter) =>
                    table.Write(
                        writer,
                        formatter,
                        formatOptions,
                        options.Rows),
                maxRows: null);
            return;
        }

        CountingTextWriter? tsvOutput =
            options.Tsv
                ? new CountingTextWriter(output)
                : null;
        TextWriter tableOutput =
            tsvOutput ?? output;
        bool tsvTableWritten = false;
        foreach (LibraryInspection inspection in inspections)
        {
            var auditView =
                new LibraryInspectionView(
                    inspection,
                    topFieldsOnly);
            MarkoutWriterOptions participantOptions =
                writerOptions(inspection);
            participantOptions.Projection =
                BuildProjection(fields: options.Fields);
            ConfigureTableWriterOptions(
                participantOptions,
                options.Tsv,
                options.Jsonl);
            WriteLibraryTabular(
                auditView,
                inspection,
                participantOptions,
                options,
                tableOutput,
                [
                    "package",
                    "package_version",
                    "library",
                    "tfm",
                ],
                [
                    LibraryViewText.Contain(aggregatePackage)
                        ?? string.Empty,
                    LibraryViewText.Contain(aggregateVersion)
                        ?? string.Empty,
                    LibraryViewText.Contain(inspection.FileName)
                        ?? string.Empty,
                    LibraryViewText.Contain(inspection.Tfm)
                        ?? string.Empty,
                ],
                showHeader:
                    !options.NoHeader
                    && (!options.Tsv || !tsvTableWritten));
            if (tsvOutput is not null
                && tsvOutput.CharCount > 0)
            {
                tsvTableWritten = true;
            }
        }
    }

    private static PackageLibraryAggregateJson
        CreatePackageLibraryAggregateJson(
            IReadOnlyList<LibraryInspection> inspections,
            string aggregatePackage,
            string? aggregateVersion,
            bool topFieldsOnly,
            Func<LibraryInspection, MarkoutWriterOptions> writerOptions,
            LibraryOptions options,
            SectionPipeline<LibraryInspection> pipeline)
    {
        var sections =
            new List<PackageLibraryAggregateSectionJson>();
        foreach (string section in pipeline.AlphabeticalSectionOrder
                     .Where(name => inspections.Any(
                         inspection => IncludesSection(
                             writerOptions(inspection),
                             name))))
        {
            var rows = new List<Dictionary<string, string>>();
            if (IsAggregateLibrarySection(section))
            {
                CapturedLibraryTableFormatter table =
                    CaptureAggregateSectionTable(
                        inspections,
                        section,
                        writerOptions,
                        options,
                        machineFormat: true,
                        aggregatePackage,
                        aggregateVersion);
                rows.AddRange(table.JsonRows(options.Rows));
            }
            else
            {
                foreach (LibraryInspection inspection in inspections)
                {
                    if (!IncludesSection(
                            writerOptions(inspection),
                            section))
                    {
                        continue;
                    }

                    CapturedLibraryTableFormatter table =
                        CapturePackageLibrarySectionTable(
                            inspection,
                            section,
                            aggregatePackage,
                            aggregateVersion,
                            topFieldsOnly,
                            writerOptions,
                            options);
                    rows.AddRange(table.JsonRows(options.Rows));
                }
            }

            if (rows.Count > 0)
            {
                sections.Add(
                    new PackageLibraryAggregateSectionJson(
                        section,
                        [.. rows]));
            }
        }

        return new PackageLibraryAggregateJson(
            aggregatePackage,
            aggregateVersion,
            [.. sections]);
    }

    private static CapturedLibraryTableFormatter
        CapturePackageLibrarySectionTable(
            LibraryInspection inspection,
            string section,
            string aggregatePackage,
            string? aggregateVersion,
            bool topFieldsOnly,
            Func<LibraryInspection, MarkoutWriterOptions> writerOptions,
            LibraryOptions options)
    {
        MarkoutWriterOptions participantOptions =
            writerOptions(inspection);
        participantOptions.IncludeSections =
            new HashSet<string>(
                [section],
                StringComparer.OrdinalIgnoreCase);
        participantOptions.Projection =
            BuildProjection(fields: options.Fields);
        ConfigureTableWriterOptions(
            participantOptions,
            tsv: false,
            jsonl: true);

        var captured = new CapturedLibraryTableFormatter();
        MarkoutSerializer.Serialize(
            new LibraryInspectionView(
                inspection,
                topFieldsOnly),
            TextWriter.Null,
            new ProducerLibraryTableFormatter(
                captured,
                [
                    "package",
                    "package_version",
                    "library",
                    "tfm",
                ],
                [
                    LibraryViewText.Contain(aggregatePackage)
                        ?? string.Empty,
                    LibraryViewText.Contain(aggregateVersion)
                        ?? string.Empty,
                    LibraryViewText.Contain(inspection.FileName)
                        ?? string.Empty,
                    LibraryViewText.Contain(inspection.Tfm)
                        ?? string.Empty,
                ],
                options.Columns),
            InspectionContext.Default,
            participantOptions);
        return captured;
    }

    private static CapturedLibraryTableFormatter
        CaptureAggregateSectionTable(
            IReadOnlyList<LibraryInspection> inspections,
            string section,
            Func<LibraryInspection, MarkoutWriterOptions> writerOptions,
            LibraryOptions options,
            bool machineFormat,
            string? aggregatePackage,
            string? aggregateVersion)
    {
        var captured = new CapturedLibraryTableFormatter();
        LibraryInspection[] selectedInspections =
        [
            .. inspections.Where(
                inspection =>
                    IncludesSection(
                        writerOptions(inspection),
                        section)),
        ];
        string[] provenanceHeaders =
            machineFormat
                ?
                [
                    "package",
                    "package_version",
                    "library",
                    "tfm",
                ]
                : ["Library", "TFM"];
        string[] DataHeaders(
            string[] display,
            string[] stable) =>
            machineFormat ? stable : display;
        string[] Provenance(
            LibraryInspection inspection) =>
            machineFormat
                ?
                [
                    LibraryViewText.Contain(aggregatePackage)
                        ?? string.Empty,
                    LibraryViewText.Contain(aggregateVersion)
                        ?? string.Empty,
                    LibraryViewText.Contain(inspection.FileName)
                        ?? string.Empty,
                    LibraryViewText.Contain(inspection.Tfm)
                        ?? string.Empty,
                ]
                :
                [
                    LibraryViewText.Contain(inspection.FileName)
                        ?? string.Empty,
                    LibraryViewText.Contain(inspection.Tfm)
                        ?? string.Empty,
                ];
        string[] AggregateRow(
            LibraryInspection inspection,
            params string[] values) =>
        [
            .. Provenance(inspection),
            .. values,
        ];

        string[] dataHeaders;
        List<string[]> rows;
        if (section.Equals(
                IntegrationSectionNames.Opportunities,
                StringComparison.OrdinalIgnoreCase))
        {
            dataHeaders = DataHeaders(
                [
                    "Integration",
                    "API",
                    "Integration Type",
                    "Look For",
                ],
                [
                    "integration",
                    "api",
                    "integration_type",
                    "look_for",
                ]);
            rows =
            [
                .. selectedInspections
                    .SelectMany(
                        inspection =>
                            (inspection.IntegrationOpportunities
                                ?? [])
                            .Select(
                                opportunity =>
                                {
                                    var row =
                                        new IntegrationOpportunityRow(
                                            opportunity.Integration,
                                            MarkoutInline.Code(
                                                opportunity.Api),
                                            opportunity.IntegrationType,
                                            opportunity.LookFor);
                                    return (
                                        Inspection: inspection,
                                        Row: row);
                                }))
                    .OrderBy(
                        item => item.Row.Integration,
                        StringComparer.Ordinal)
                    .ThenBy(
                        item => item.Row.Api,
                        StringComparer.Ordinal)
                    .Select(
                        item =>
                            AggregateRow(
                                item.Inspection,
                            item.Row.Integration,
                            item.Row.Api,
                            item.Row.IntegrationType,
                                item.Row.LookFor)),
            ];
        }
        else if (section.Equals(
                     SectionNames.Switches,
                     StringComparison.OrdinalIgnoreCase))
        {
            dataHeaders = DataHeaders(
                ["Kind", "Switch", "API"],
                ["kind", "switch", "api"]);
            rows =
            [
                .. selectedInspections
                    .SelectMany(
                        inspection =>
                            inspection.SwitchInspection
                                .PayloadsForRendering()
                                .Select(
                                    item => (
                                        Inspection: inspection,
                                        Row: new SwitchRow(
                                            item.Kind,
                                            item.Switch,
                                            item.Api))))
                    .OrderBy(
                        item => item.Row.Kind,
                        StringComparer.Ordinal)
                    .ThenBy(
                        item => item.Row.Switch,
                        StringComparer.Ordinal)
                    .ThenBy(
                        item => item.Row.Api,
                        StringComparer.Ordinal)
                    .Select(
                        item =>
                            AggregateRow(
                                item.Inspection,
                                item.Row.Kind,
                                item.Row.Switch,
                                item.Row.Api)),
            ];
        }
        else
        {
            dataHeaders = DataHeaders(
                [
                    "Integration",
                    "Kind",
                    "Shape",
                    "Symbol",
                ],
                [
                    "integration",
                    "kind",
                    "shape",
                    "symbol",
                ]);
            rows =
            [
                .. LibraryIntegrationCatalog.All
                    .SelectMany(
                        descriptor =>
                            selectedInspections
                                .SelectMany(
                                    inspection =>
                                        descriptor
                                            .RenderedSignals(
                                                descriptor.GetSignals(
                                                    inspection))
                                            .OrderBy(
                                                signal => signal.Kind,
                                                StringComparer.Ordinal)
                                            .ThenBy(
                                                signal => signal.Name,
                                                StringComparer.Ordinal)
                                            .Select(
                                                signal => (
                                                    Inspection:
                                                        inspection,
                                                    Row:
                                                        new IntegrationSignalRow(
                                                            descriptor.Name,
                                                            signal.Kind,
                                                            signal.Shape
                                                                == IntegrationSignalShape.Api
                                                                ? "API"
                                                                : "Type",
                                                            MarkoutInline.Code(
                                                                signal.Name))))))
                    .Select(
                        item =>
                            AggregateRow(
                                item.Inspection,
                                item.Row.Integration,
                                item.Row.Kind,
                                item.Row.Shape,
                                item.Row.Symbol)),
            ];
        }

        captured.FormatTable(
            TextWriter.Null,
            [.. provenanceHeaders, .. dataHeaders],
            rows,
            skippedRows: 0,
            new MarkoutWriterOptions());
        captured.ProjectColumns(
            options.Fields,
            machineFormat ? 4 : 2);
        captured.ProjectColumns(
            options.Columns,
            machineFormat ? 4 : 2);
        return captured;
    }

    private static IEnumerable<string>
        SelectedAggregateSections(
            IReadOnlyList<LibraryInspection> inspections,
            Func<LibraryInspection, MarkoutWriterOptions> writerOptions,
            SectionPipeline<LibraryInspection> pipeline) =>
        pipeline.AlphabeticalSectionOrder
            .Where(IsAggregateLibrarySection)
            .Where(section => inspections.Any(
                inspection =>
                    IncludesSection(
                        writerOptions(inspection),
                        section)));

    private static bool IncludesSection(
        MarkoutWriterOptions options,
        string section) =>
        options.IncludeSections is null
        || options.IncludeSections.Contains(section);

    private static MarkoutWriterOptions WithoutAggregateSections(
        MarkoutWriterOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        IEnumerable<string> sections =
            options.IncludeSections is { } included
                ? included
                : pipeline.AlphabeticalSectionOrder;
        return new MarkoutWriterOptions
        {
            IncludeSections =
                new HashSet<string>(
                    sections.Where(
                        section =>
                            !IsAggregateLibrarySection(
                                section)),
                    StringComparer.OrdinalIgnoreCase),
            Projection = options.Projection,
        };
    }

    internal static bool IsAggregateLibrarySection(
        string section) =>
        section.Equals(
            IntegrationSectionNames.Opportunities,
            StringComparison.OrdinalIgnoreCase)
        || section.Equals(
            IntegrationSectionNames.Integrations,
            StringComparison.OrdinalIgnoreCase)
        || section.Equals(
            SectionNames.Switches,
            StringComparison.OrdinalIgnoreCase);

    private static string RenderMarkdownHeading(int level, string title)
    {
        var output = new StringWriter { NewLine = "\n" };
        var writer = MarkoutWriter.Create(output, new MarkdownFormatter());
        writer.WriteHeading(level, title);
        writer.Flush();
        return output.ToString().TrimEnd();
    }

    private static string RemoveMarkdownDocumentTitle(string markdown)
    {
        if (!markdown.StartsWith("# ", StringComparison.Ordinal))
            return markdown;

        var lineEnd = markdown.IndexOf('\n');
        return lineEnd < 0 ? string.Empty : markdown[(lineEnd + 1)..].TrimStart('\n');
    }

    private static string RemovePlainTextDocumentTitle(string plainText, string title)
    {
        if (plainText.Equals(title, StringComparison.Ordinal))
            return string.Empty;

        var prefix = title + "\n";
        return plainText.StartsWith(prefix, StringComparison.Ordinal)
            ? plainText[prefix.Length..].TrimStart('\n')
            : plainText;
    }

    internal static string QualifyMarkdownSectionHeadings(
        string markdown,
        string producerLibrary)
    {
        if (markdown.Length == 0)
            return markdown;

        string containedProducer = RenderMarkdownHeading(
            2,
            producerLibrary)["## ".Length..];
        var newline = MarkdownScan.DetectNewline(markdown);
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var inCodeFence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (MarkdownScan.IsCodeFence(lines[i]))
            {
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence)
                continue;

            if (!lines[i].StartsWith("## ", StringComparison.Ordinal)
                || lines[i].StartsWith("### ", StringComparison.Ordinal))
            {
                continue;
            }

            lines[i] += ": " + containedProducer;
        }

        return string.Join(newline, lines);
    }

    /// <summary>
    /// Builds a MarkoutProjection for column and field filtering.
    /// Section filtering is handled by MarkoutWriterOptions.IncludeSections
    /// and must not be duplicated in the projection — doing so triggers
    /// projection-section-active mode which disables field/column filtering.
    /// Returns null when no projection is needed.
    /// </summary>
    internal static MarkoutProjection? BuildProjection(string[]? columns = null, string[]? fields = null)
    {
        if (columns == null && fields == null)
            return null;

        RejectDuplicates(columns, "--columns");
        RejectDuplicates(fields, "--fields");

        return new MarkoutProjection
        {
            IncludeColumns = columns,
            IncludeFields = fields,
        };
    }

    /// <summary>
    /// Rejects a projection that names the same column or field twice.
    /// </summary>
    /// <remarks>
    /// Naming a column twice cannot mean anything a caller wants, and what it produces depends on
    /// whether the format keys its output: TSV and the Markdown table repeat a harmless column,
    /// but JSON and JSONL emit a duplicate property, which is not an error any JSON parser reports
    /// -- consumers silently keep one. Rejecting the request here rather than in a renderer keeps
    /// every format agreeing about which requests are valid, which is the same reason an unmatched
    /// column already fails closed (dotnet-inspect#3494 review). Matching is case-insensitive
    /// because column selection is.
    /// <para>
    /// This is the second gate, not the first. <c>SharedOptions</c> attaches the same check to the
    /// <c>--columns</c>/<c>--fields</c> options themselves, so a duplicate arriving from the command
    /// line is rejected at parse time as a clean one-line error. That matters because a throw from
    /// inside the invocation pipeline is only reported cleanly by commands that happen to catch it:
    /// <c>find</c> does, <c>package</c> does not, and there it surfaced as an unhandled-exception
    /// stack trace (dotnet-inspect#3494 review).
    /// </para>
    /// <para>
    /// Every product caller of <c>BuildProjection</c> currently passes <c>Columns</c>/<c>Fields</c>
    /// sourced from those validated options, so this check is unreachable from the CLI today. It
    /// stays because <c>OutputFormatter</c> is in-process infrastructure that callers can drive
    /// directly -- <c>FindCommandTests</c> already does -- and because a future option reaching
    /// <c>BuildProjection</c> without a parse-time validator would otherwise emit a duplicate key
    /// rather than fail. It is defense in depth, not the enforcing gate; the parse-time validator
    /// is, and <c>DuplicateProjection_IsRejectedByCommandsThatDoNotCatchIt</c> pins it.
    /// </para>
    /// </remarks>
    private static void RejectDuplicates(string[]? names, string flag)
    {
        if (names is not { Length: > 1 })
            return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (!seen.Add(name))
                throw new InvalidOperationException($"Duplicate {flag} entry: {name}");
        }
    }

    internal static bool ShouldRenderLibraryContext(LibraryOptions options) =>
        options.Verbosity == Verbosity.Quiet;

    internal static CountProjection CaptureLibraryCountProjection(
        LibraryInspectionView auditView,
        LibraryInspection inspection,
        MarkoutWriterOptions writerOptions,
        RowWindow? rows,
        string[]? fields = null,
        string[]? columns = null)
    {
        writerOptions.RowWindow = RowWindow.ToMarkout(rows);
        var projection = CountProjectionFormatter.Capture(
            auditView, InspectionContext.Default, writerOptions);
        projection.Merge(MetadataLensRenderer.CaptureCounts(
            inspection, writerOptions.IncludeSections, rows));
        if (inspection.ReferenceHierarchyProjection is { } hierarchy
            && writerOptions.IncludeSections?.Contains(
                SectionNames.ReferenceHierarchy) == true)
        {
            int count = rows is { IsUnlimited: false } window
                ? window.Apply(hierarchy.HierarchyRows).Count
                : hierarchy.HierarchyRows.Length;
            projection.SetRows(
                SectionNames.ReferenceHierarchy,
                count);
        }
        ApplyILCoordinateCardinality(
            projection, inspection, writerOptions.IncludeSections, rows, fields, columns);
        return projection;
    }

    private static void ApplyILCoordinateCardinality(
        CountProjection projection,
        LibraryInspection inspection,
        IReadOnlyCollection<string>? includedSections,
        RowWindow? rows,
        string[]? fields,
        string[]? columns)
    {
        if (inspection.ILOffset is null || includedSections is null)
            return;

        var schema = InspectionContext.Default
            .GetSchemaInfo<LibraryInspectionView>()!
            .ToDocumentSchema();
        foreach (var section in includedSections)
        {
            bool? hasRow = section switch
            {
                SectionNames.ILOffset => true,
                SectionNames.MemberContext => inspection.ILOffset.MemberContext != null,
                SectionNames.InstructionContext => inspection.ILOffset.InstructionContext != null,
                SectionNames.CallsiteContext => inspection.ILOffset.CallsiteContext != null,
                SectionNames.ReturnAddressContext => inspection.ILOffset.ReturnAddressContext != null,
                _ => null
            };
            if (hasRow is null)
                continue;

            bool projected = ProjectionMatchesSection(
                schema, section, fields, columns);
            int count = hasRow.Value && projected
                ? RowWindow.Apply(rows, new[] { 0 }).Count
                : 0;
            projection.SetRows(section, count);
        }
    }

    private static bool ProjectionMatchesSection(
        DocumentSchema schema,
        string section,
        string[]? fields,
        string[]? columns)
    {
        if (fields is not { Length: > 0 }
            && columns is not { Length: > 0 })
        {
            return true;
        }

        var sectionSchema = schema.GetSection(section);
        return sectionSchema is not null
            && ((fields is { Length: > 0 }
                    && sectionSchema.ItemKind.Equals(
                        "field", StringComparison.OrdinalIgnoreCase)
                    && schema.ValidateProjection(section, fields).Resolved.Length > 0)
                || (columns is { Length: > 0 }
                    && sectionSchema.ItemKind.Equals(
                        "column", StringComparison.OrdinalIgnoreCase)
                    && schema.ValidateProjection(section, columns).Resolved.Length > 0)
                || (columns is { Length: > 0 }
                    && sectionSchema.ItemKind.Equals(
                        "field", StringComparison.OrdinalIgnoreCase)
                    && columns.Contains(
                        "*",
                        StringComparer.Ordinal)));
    }
}
