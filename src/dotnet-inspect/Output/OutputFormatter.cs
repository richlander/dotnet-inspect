using DotnetInspector.Models;
using DotnetInspector.Packages;
using DotnetInspector.Views;
using System.Globalization;
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
        var sw = new StringWriter();
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

    public static MarkoutWriterOptions CreateProjectedWriterOptions(string[]? columns = null, string[]? fields = null) =>
        new()
        {
            Projection = BuildProjection(columns, fields)
        };

    public static string RenderProjectedTable(
        bool showHeader,
        bool tsv,
        bool jsonl,
        string[]? columns,
        string[]? fields,
        Action<TextWriter, IMarkoutFormatter, MarkoutWriterOptions> serialize)
    {
        var writerOptions = CreateProjectedWriterOptions(columns, fields);
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
        output.Write(LimitRenderedTableRows(RenderProjectedTable(showHeader, tsv, jsonl, columns, fields, serialize), maxRows, showHeader));

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
        var writerOptions = CreateProjectedWriterOptions(columns, fields);
        // Ask Markout for the JSONL flavor of the header names. The formatter is ours, so this
        // does not change who renders the table -- it changes the vocabulary handed to the
        // renderer, which is how --jsonl and the pre-lowered --json both get machine keys
        // ("type") rather than the display headings Markdown shows ("Type"). Without it the same
        // --json flag would change key casing depending on whether a projection was requested.
        ConfigureTableWriterOptions(writerOptions, tsv: false, jsonl: true);
        var formatter = new JsonSectionFormatter();
        formatter.BeginDocument(writerOptions, maxRows);
        serialize(TextWriter.Null, formatter, writerOptions);
        return formatter.Finish(indented);
    }

    /// <summary>
    /// Writes the lowered JSON view produced by <see cref="RenderProjectedJson"/>.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="WriteProjectedTable"/> this does not post-process the rendered text with
    /// <see cref="LimitRenderedTableRows"/>. That limiter counts output lines, which is safe only
    /// because every table format puts one row on one line; a pretty-printed JSON document has no
    /// such correspondence and would be cut mid-object. The window is applied to the data instead.
    /// </remarks>
    public static void WriteProjectedJson(
        TextWriter output,
        string[]? columns,
        string[]? fields,
        Action<TextWriter, IMarkoutFormatter, MarkoutWriterOptions> serialize,
        bool indented = true,
        RowWindow? maxRows = null) =>
        output.WriteLine(RenderProjectedJson(columns, fields, serialize, indented, maxRows));

    public static string ApplyRowLimit(string markdown, RowWindow? rows) =>
        MarkdownTableRowLimiter.Apply(markdown.TrimEnd(), rows);

    public static void WriteLimitedMarkdown(TextWriter output, string markdown, RowWindow? rows) =>
        output.WriteLine(ApplyRowLimit(markdown, rows));

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
        bool tsv, bool jsonl, TextWriter output)
    {
        var rows = versions.Select(v => new[] { v.Version, v.Listed ? "listed" : "unlisted" }).ToArray();
        WriteTable(output, showHeader: false, (writer, formatter) =>
        {
            var markoutWriter = new MarkoutWriter(writer, formatter, CreateTableWriterOptions(tsv, jsonl));
            markoutWriter.WriteTable(["Version", "Listing"], ["version", "listing"], rows);
            markoutWriter.Flush();
        });
    }

    public static string FormatResult(InspectionResult result, InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
    {
        if (options.JsonOutput && !options.Count)
        {
            return JsonSerializer.Serialize(result, JsonContext.Default.InspectionResult);
        }

        bool selectAll = SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections);
        bool selectInfo = SelectResolver.IsActiveInfoSelector(options.Select, options.IncludeSections);
        bool includeContext = ShouldRenderPackageContext(options);
        var view = new InspectionResultView(result, includeTitleVersion: false);
        var writerOptions = BuildWriterOptions(result, options, pipeline, includeContext);
        var markdown = MarkoutSerializer.Serialize(view, InspectionContext.Default, writerOptions).TrimEnd();
        if (selectAll)
            markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.GetAllSelectorSections(result));
        else if (selectInfo)
            markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.InfoSectionNames);
        markdown = MarkdownTableRowLimiter.Apply(markdown, options.Rows);
        if (!options.Count)
            return markdown;

        // A category selects many sections at once; report each member's count, including the
        // members that rendered nothing, so the map describes the whole category.
        if (options.IncludeSections is { Count: > 1 })
        {
            var ordered = pipeline.AlphabeticalSectionOrder.Where(options.IncludeSections.Contains).ToList();
            return CountOutput.RenderCountMapFromMarkdown(markdown, ordered);
        }

        return CountOutput.CountMarkdownTableRows(markdown).ToString(CultureInfo.InvariantCulture);
    }

    public static void WritePackageTable(InspectionResult result, InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline, bool showHeader)
    {
        var writerOpts = BuildWriterOptions(result, options, pipeline);
        ConfigureTableWriterOptions(writerOpts, options.Tsv, options.Jsonl);
        var view = new InspectionResultView(result);
        WriteTable(Console.Out, showHeader,
            (writer, formatter) => MarkoutSerializer.Serialize(view, writer, formatter, InspectionContext.Default, writerOpts));
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
        SectionPipeline<InspectionResult> pipeline, bool includeContext = false)
    {
        var selectAll = SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections);
        var selectInfo = SelectResolver.IsActiveInfoSelector(options.Select, options.IncludeSections);
        var includeSections = pipeline.ComputeIncludeSections(
            result, options.Verbosity, options.IncludeSections, selectAll, options.FixedOverview);
        if (includeContext && includeSections is { Count: > 0 })
            includeSections = [PackageSections.Summary, .. includeSections];

        return new MarkoutWriterOptions
        {
            IncludeSections = includeSections,
            IncludeDescription = options.Verbosity != Verbosity.Quiet && !includeContext && !selectInfo,
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
            var markdown = SerializeLibraryMarkdown(auditView, inspection, writerOpts, pipeline);
            markdown = MarkdownTableRowLimiter.Apply(markdown, options.Rows);
            if (options.IncludeSections is { Count: > 1 })
            {
                var ordered = pipeline.AlphabeticalSectionOrder.Where(options.IncludeSections.Contains).ToList();
                CountOutput.WriteCountMapFromMarkdown(markdown, ordered);
            }
            else
            {
                CountOutput.WriteCountFromMarkdown(markdown);
            }
            return;
        }

        if (inspection.UseDependenciesView)
        {
            Console.Error.WriteLine("Tip: use 'depends --library' for dependency trees.");
            var view = AssemblyDependenciesView.FromInspection(inspection);
            MarkoutSerializer.Serialize(view, Console.Out, AssemblyDependenciesContext.Default);
            return;
        }

        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(inspection, JsonContext.Default.LibraryInspection));
            return;
        }

        if (options.Format == OutputFormat.PlainText)
        {
            MarkoutSerializer.Serialize(auditView, Console.Out, new PlainTextFormatter(), InspectionContext.Default, writerOpts);
            if (MetadataLensRenderer.RenderMarkdown(inspection, writerOpts.IncludeSections, writerOpts.Projection?.IncludeColumns) is { } plainMetadata)
                Console.WriteLine(plainMetadata);
        }
        else if (options.VerbosityEnabled)
        {
            var markdown = SerializeLibraryMarkdown(auditView, inspection, writerOpts, pipeline);
            Console.WriteLine(MarkdownTableRowLimiter.Apply(markdown, options.Rows));
        }
        else if (writerOpts.IncludeSections is { Count: > 1 } && !options.TabularExplicitlySet)
        {
            // Auto-promote to markdown when multiple sections and tabular output wasn't explicitly requested
            var markdown = SerializeLibraryMarkdown(auditView, inspection, writerOpts, pipeline);
            Console.WriteLine(MarkdownTableRowLimiter.Apply(markdown, options.Rows));
        }
        else
        {
            ConfigureTableWriterOptions(writerOpts, options.Tsv, options.Jsonl);
            WriteLibraryTabular(auditView, inspection, writerOpts, options);
        }
    }

    /// <summary>
    /// Serializes the library view and, when the <c>@Metadata</c> lens is selected, composes its
    /// sections into the same Markdown document before ordering.
    ///
    /// Metadata sections cannot be attributed view properties (their columns differ per table), so
    /// they are rendered separately and appended. Ordering runs *after* the append, which is what
    /// places them among the other sections rather than in a block at the end; every downstream
    /// step — <c>--rows</c> windowing, <c>--count</c> — then treats them as ordinary sections.
    /// </summary>
    private static string SerializeLibraryMarkdown(
        LibraryInspectionView auditView,
        LibraryInspection inspection,
        MarkoutWriterOptions writerOpts,
        SectionPipeline<LibraryInspection> pipeline)
    {
        var markdown = MarkoutSerializer.Serialize(auditView, InspectionContext.Default, writerOpts);

        if (MetadataLensRenderer.RenderMarkdown(inspection, writerOpts.IncludeSections, writerOpts.Projection?.IncludeColumns) is { } metadata)
        {
            var body = markdown.TrimEnd();
            markdown = body.Length == 0 ? metadata : body + Environment.NewLine + Environment.NewLine + metadata;
        }

        return MarkdownSectionOrderer.Apply(markdown, pipeline.AlphabeticalSectionOrder);
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
                    inspection, writerOpts.IncludeSections, format, writer, Console.Error,
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
        var report = new LibraryInspectionReport
        {
            Title = Path.GetFileNameWithoutExtension(inspections[0].FileName),
            Assemblies = inspections.Select(a => new LibraryInspectionView(a, topFieldsOnly)).ToList()
        };
        var writerOptions = new MarkoutWriterOptions
        {
            IncludeSections = pipeline.ComputeIncludeSections(
                inspections[0], options.Verbosity, options.IncludeSections, selectAll, options.FixedOverview),
            Projection = BuildProjection(options.Columns, options.Fields)
        };

        if (options.Count)
        {
            var markdown = MarkoutSerializer.Serialize(report, InspectionContext.Default, writerOptions);
            markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.AlphabeticalSectionOrder);
            markdown = MarkdownTableRowLimiter.Apply(markdown, options.Rows);
            if (options.IncludeSections is { Count: > 1 })
            {
                var ordered = pipeline.AlphabeticalSectionOrder.Where(options.IncludeSections.Contains).ToList();
                CountOutput.WriteCountMapFromMarkdown(markdown, ordered);
            }
            else
            {
                CountOutput.WriteCountFromMarkdown(markdown);
            }
            return;
        }

        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(inspections.ToArray(), JsonContext.Default.LibraryInspectionArray));
            return;
        }

        if (options.VerbosityEnabled)
        {
            var markdown = MarkoutSerializer.Serialize(report, InspectionContext.Default, writerOptions).TrimEnd();
            markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.AlphabeticalSectionOrder);
            Console.WriteLine(MarkdownTableRowLimiter.Apply(markdown, options.Rows));
        }
        else
        {
            foreach (var inspection in inspections)
            {
                var auditView = new LibraryInspectionView(inspection, topFieldsOnly);
                var includeSections = pipeline.ComputeIncludeSections(
                    inspection, options.Verbosity, options.IncludeSections, selectAll, options.FixedOverview);
                var writerOpts = new MarkoutWriterOptions
                {
                    IncludeSections = includeSections,
                    Projection = BuildProjection(options.Columns, options.Fields),
                };
                ConfigureTableWriterOptions(writerOpts, options.Tsv, options.Jsonl);
                WriteLibraryTabular(auditView, inspection, writerOpts, options);
            }
        }
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
        options.Verbosity == Verbosity.Quiet
        || (options.IncludeSections is { Count: > 0 }
            && !SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections)
            && !SelectResolver.IsActiveInfoSelector(options.Select, options.IncludeSections)
            && !options.Count
            && !options.JsonOutput
            && !options.Tabular);

    internal static bool ShouldRenderPackageContext(InspectionOptions options) =>
        options.IncludeSections is { Count: > 0 }
        && !SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections)
        && !SelectResolver.IsActiveInfoSelector(options.Select, options.IncludeSections)
        && !options.Count
        && !options.JsonOutput
        && !options.Tabular;
}
