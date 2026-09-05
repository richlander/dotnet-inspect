using DotnetInspector.Models;
using DotnetInspector.Packages;
using DotnetInspector.Views;
using System.Text.Json;
using DotnetInspector.Options;
using DotnetInspector.Sections;
using Markout;
using Markout.Formatting;

namespace DotnetInspector.Output;

/// <summary>
/// Diagnostic returned when the rendering service detects an incompatibility
/// between the requested sections and the formatter's capabilities.
/// </summary>
public record RenderDiagnostic(string Formatter, string Condition, string[] Sections);

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
        RowWindow? maxRows = null)
    {
        var writerOptions = CreateProjectedWriterOptions(columns, fields, maxRows);
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
        RowWindow? maxRows = null) =>
        output.WriteLine(RenderProjectedJson(columns, fields, serialize, indented, maxRows));

    /// <summary>
    /// Serializes a view with <c>--rows</c> applied at the writer seam and writes the result.
    /// </summary>
    /// <remarks>
    /// markout windows rows as it emits them, so the window is applied to table rows the writer
    /// knows about rather than re-derived by parsing rendered Markdown back into tables. That
    /// removes the need to tell a table row from a prose line or a fenced code line after the
    /// fact. The two remaining rendered-text windowing sites are the ones whose content the
    /// writer never sees: the <c>@Metadata</c> lens (#3619) and the package all-libraries
    /// aggregates (#3624).
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

        if (options.JsonOutput)
        {
            var objects = items.Select(v => new VersionFeedJson(v.Version, v.Feed, v.Listed)).ToList();
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
        var rows = versions.Select(v => new[] { v.Version, v.Listed ? "listed" : "unlisted" }).ToArray();
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
                PackageInspectionJson.Create(result),
                PackageInspectionJsonContext.Default.PackageInspectionJson);
        }

        var view = new InspectionResultView(result, includeTitleVersion: false);
        var writerOptions = BuildPackageDocumentWriterOptions(result, options, pipeline);
        if (options.Count)
        {
            var projection = CountProjectionFormatter.Capture(
                view, InspectionContext.Default, writerOptions);
            var ordered = ResolveCountMapSections(
                pipeline, options.IncludeSections, options.FixedOverview);
            return CountOutput.Render(
                projection, ordered, options.Format, options.NoHeader);
        }

        return MarkoutSerializer.Serialize(
            view, InspectionContext.Default, writerOptions).TrimEnd();
    }

    internal static CountProjection CapturePackageCountProjection(
        InspectionResult result,
        InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
        => CountProjectionFormatter.Capture(
            new InspectionResultView(result, includeTitleVersion: false),
            InspectionContext.Default,
            BuildPackageDocumentWriterOptions(result, options, pipeline));

    private static MarkoutWriterOptions BuildPackageDocumentWriterOptions(
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
        var view = new InspectionResultView(result);
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

        if (options.Count)
        {
            var projection = CaptureLibraryCountProjection(
                auditView, inspection, writerOpts, options.Rows, options.Fields, options.Columns);
            var ordered = ResolveCountMapSections(pipeline, options.IncludeSections, options.FixedOverview);
            CountOutput.Write(
                projection, ordered, options.Format, options.NoHeader, options.OutputPath, options.Rows);
            return;
        }

        if (options.Tree && options.Discover == null)
        {
            OutputDestination.Write(
                options.OutputPath,
                options.Rows,
                output => WriteReferenceTree(inspection, output));
            return;
        }

        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(inspection, JsonContext.Default.LibraryInspection));
            return;
        }

        if (options.Format == OutputFormat.PlainText)
        {
            WriteLfLine(Console.Out, SerializeLibraryPlainText(
                auditView, inspection, writerOpts, options.Rows));
        }
        else if (options.VerbosityEnabled)
        {
            var markdown = SerializeLibraryMarkdown(
                auditView, inspection, writerOpts, pipeline, options.Rows);
            WriteLfLine(Console.Out, markdown);
        }
        else if (writerOpts.IncludeSections is { Count: > 1 } && !options.TabularExplicitlySet)
        {
            // Auto-promote to markdown when multiple sections and tabular output wasn't explicitly requested
            var markdown = SerializeLibraryMarkdown(
                auditView, inspection, writerOpts, pipeline, options.Rows);
            WriteLfLine(Console.Out, markdown);
        }
        else
        {
            ConfigureTableWriterOptions(writerOpts, options.Tsv, options.Jsonl);
            WriteLibraryTabular(auditView, inspection, writerOpts, options);
        }
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

    private static void WriteReferenceTree(
        LibraryInspection inspection,
        TextWriter output)
    {
        var references = inspection.AssemblyInfo?.TransitiveReferences ?? [];
        var tree = LibraryInspectionView.BuildNestedReferenceTree(references);
        var writer = MarkoutWriter.Create(output, new MarkdownFormatter());
        writer.WriteHeading(1, LibraryViewText.Contain(inspection.FileName) ?? string.Empty);
        writer.WriteHeading(2, SectionNames.References);
        writer.WriteTree([.. tree]);
        writer.Flush();
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
            writerOpts.SectionOrder = pipeline.AlphabeticalSectionOrder;
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
        MarkoutWriterOptions writerOpts, LibraryOptions options)
    {
        // The metadata lens owns its own tabular rendering for the same reason it owns its
        // Markdown rendering: per-table column shapes have no static row type for Markout to bind.
        // Its rows already self-identify with a leading Table/Section column. It still goes through
        // WriteTable so `--rows` windows it exactly as it windows every other tabular section —
        // the limiter operates on rendered text and needs no knowledge of the lens.
        if (MetadataLensRenderer.IsSelected(writerOpts.IncludeSections))
        {
            var format = MetadataLensRenderer.FormatFor(options.Tsv, options.Jsonl);
            WriteTable(Console.Out, !options.NoHeader,
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
            WriteTable(Console.Out, !options.NoHeader,
                (writer, formatter) => MarkoutSerializer.Serialize(groupView, writer, formatter, InspectionContext.Default, groupOpts),
                options.Rows);
        }
        else
        {
            WriteTable(Console.Out, !options.NoHeader,
                (writer, formatter) => MarkoutSerializer.Serialize(auditView, writer, formatter, InspectionContext.Default, writerOpts),
                options.Rows);
        }
    }

    public static void WriteLibraryResults(List<LibraryInspection> inspections, LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        bool selectAll = SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections);
        bool topFieldsOnly = ShouldRenderLibraryContext(options);

        MarkoutWriterOptions WriterOptions(LibraryInspection inspection) => new()
        {
            IncludeSections = pipeline.ComputeIncludeSections(
                inspection, options.Verbosity, options.IncludeSections, selectAll, options.FixedOverview),
            Projection = BuildProjection(options.Columns, options.Fields)
        };

        if (options.Count)
        {
            var projection = new CountProjection();
            foreach (var inspection in inspections)
            {
                var auditView = new LibraryInspectionView(inspection, topFieldsOnly);
                projection.Merge(CaptureLibraryCountProjection(
                    auditView, inspection, WriterOptions(inspection), options.Rows, options.Fields, options.Columns));
            }
            var ordered = ResolveCountMapSections(pipeline, options.IncludeSections, options.FixedOverview);
            CountOutput.Write(
                projection, ordered, options.Format, options.NoHeader, options.OutputPath, options.Rows);
            return;
        }

        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(inspections.ToArray(), JsonContext.Default.LibraryInspectionArray));
            return;
        }

        if (options.Format == OutputFormat.PlainText)
        {
            var documents = new List<string>
            {
                LibraryViewText.Contain(Path.GetFileNameWithoutExtension(inspections[0].FileName))
                    ?? string.Empty,
                "Libraries"
            };
            documents.AddRange(inspections.Select(inspection =>
            {
                var auditView = new LibraryInspectionView(inspection, topFieldsOnly);
                var writerOpts = WriterOptions(inspection);
                var title = LibraryViewText.DocumentTitle(inspection);
                var body = RemovePlainTextDocumentTitle(
                    SerializeLibraryPlainText(
                        auditView, inspection, writerOpts, options.Rows),
                    LibraryViewText.DocumentTitle(auditView));
                return body.Length == 0 ? title : title + "\n\n" + body;
            }));
            WriteLfLine(Console.Out, string.Join("\n\n", documents));
        }
        else if (options.VerbosityEnabled)
        {
            var documents = new List<string>
            {
                RenderMarkdownHeading(1, LibraryViewText.Contain(
                    Path.GetFileNameWithoutExtension(inspections[0].FileName)) ?? string.Empty),
                RenderMarkdownHeading(2, "Libraries")
            };
            documents.AddRange(inspections.Select(inspection =>
            {
                var auditView = new LibraryInspectionView(inspection, topFieldsOnly);
                var title = LibraryViewText.DocumentTitle(inspection);
                var body = RemoveMarkdownDocumentTitle(SerializeLibraryMarkdown(
                    auditView, inspection, WriterOptions(inspection), pipeline, options.Rows));
                body = ShiftMarkdownHeadingLevels(body, 2);
                var heading = RenderMarkdownHeading(3, title);
                return body.Length == 0
                    ? heading
                    : heading + "\n\n" + body;
            }));
            var markdown = string.Join("\n\n", documents);
            WriteLfLine(Console.Out, markdown);
        }
        else
        {
            foreach (var inspection in inspections)
            {
                var auditView = new LibraryInspectionView(inspection, topFieldsOnly);
                var writerOpts = WriterOptions(inspection);
                ConfigureTableWriterOptions(writerOpts, options.Tsv, options.Jsonl);
                WriteLibraryTabular(auditView, inspection, writerOpts, options);
            }
        }
    }

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

    internal static string ShiftMarkdownHeadingLevels(string markdown, int offset)
    {
        if (offset == 0 || markdown.Length == 0)
            return markdown;

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

            var level = 0;
            while (level < lines[i].Length && level < 6 && lines[i][level] == '#')
                level++;

            if (level == 0 || level >= lines[i].Length || lines[i][level] != ' ')
                continue;

            var shiftedLevel = Math.Clamp(level + offset, 1, 6);
            lines[i] = new string('#', shiftedLevel) + lines[i][level..];
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
