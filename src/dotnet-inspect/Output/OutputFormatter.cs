using DotnetInspector.Models;
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
        var sw = new StringWriter();
        serialize(sw, new TableFormatter(showHeader));
        return sw.ToString();
    }

    public static void WriteTable(TextWriter output, bool showHeader, Action<TextWriter, IMarkoutFormatter> serialize, int? maxRows = null)
    {
        // Row-limiting operates on the rendered text, so the capped path must materialize
        // the table first. Without a cap, serialize straight to the destination writer and
        // skip the StringWriter + whole-table string allocation.
        if (maxRows is null or < 0)
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
    public static string LimitRenderedTableRows(string rendered, int? maxRows, bool hasHeader)
    {
        if (maxRows is null or < 0 || string.IsNullOrEmpty(rendered))
            return rendered;

        var trailingNewline = rendered.EndsWith('\n');
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

        var kept = string.Join('\n', lines.Take(headerLines + maxRows.Value));
        return trailingNewline ? kept + "\n" : kept;
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
        int? maxRows = null) =>
        output.Write(LimitRenderedTableRows(RenderProjectedTable(showHeader, tsv, jsonl, columns, fields, serialize), maxRows, showHeader));

    public static string ApplyRowLimit(string markdown, int? rows) =>
        MarkdownTableRowLimiter.Apply(markdown.TrimEnd(), rows);

    public static void WriteLimitedMarkdown(TextWriter output, string markdown, int? rows) =>
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
        return options.Count ? CountOutput.CountMarkdownTableRows(markdown).ToString() : markdown;
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
            result, options.Verbosity, options.IncludeSections, selectAll);
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
        bool selectInfo = SelectResolver.IsActiveInfoSelector(options.Select, options.IncludeSections);
        bool topFieldsOnly = ShouldRenderLibraryContext(options);
        var auditView = new LibraryInspectionView(inspection, topFieldsOnly);
        var includeSections = pipeline.ComputeIncludeSections(
            inspection, options.Verbosity, options.IncludeSections, selectAll);
        var writerOpts = new MarkoutWriterOptions
        {
            IncludeSections = includeSections,
            Projection = BuildProjection(options.Columns, options.Fields)
        };

        if (options.Count)
        {
            var markdown = MarkoutSerializer.Serialize(auditView, InspectionContext.Default, writerOpts);
            if (selectAll)
                markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.GetAllSelectorSections(inspection));
            else if (selectInfo)
                markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.InfoSectionNames);
            markdown = MarkdownTableRowLimiter.Apply(markdown, options.Rows);
            CountOutput.WriteCountFromMarkdown(markdown);
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
        }
        else if (options.VerbosityEnabled)
        {
            var markdown = MarkoutSerializer.Serialize(auditView, InspectionContext.Default, writerOpts).TrimEnd();
            if (selectAll)
                markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.GetAllSelectorSections(inspection));
            else if (selectInfo)
                markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.InfoSectionNames);
            Console.WriteLine(MarkdownTableRowLimiter.Apply(markdown, options.Rows));
        }
        else if (writerOpts.IncludeSections is { Count: > 1 } && !options.OneLineExplicitlySet)
        {
            // Auto-promote to markdown when multiple sections and tabular output wasn't explicitly requested
            var markdown = MarkoutSerializer.Serialize(auditView, InspectionContext.Default, writerOpts).TrimEnd();
            if (selectAll)
                markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.GetAllSelectorSections(inspection));
            else if (selectInfo)
                markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.InfoSectionNames);
            Console.WriteLine(MarkdownTableRowLimiter.Apply(markdown, options.Rows));
        }
        else
        {
            ConfigureTableWriterOptions(writerOpts, options.Tsv, options.Jsonl);
            WriteTable(Console.Out, !options.NoHeader,
                (writer, formatter) => MarkoutSerializer.Serialize(auditView, writer, formatter, InspectionContext.Default, writerOpts),
                options.Rows);
        }
    }

    public static void WriteLibraryResults(List<LibraryInspection> inspections, LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        bool selectAll = SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections);
        bool selectInfo = SelectResolver.IsActiveInfoSelector(options.Select, options.IncludeSections);
        bool topFieldsOnly = ShouldRenderLibraryContext(options);
        var report = new LibraryInspectionReport
        {
            Title = Path.GetFileNameWithoutExtension(inspections[0].FileName),
            Assemblies = inspections.Select(a => new LibraryInspectionView(a, topFieldsOnly)).ToList()
        };
        var writerOptions = new MarkoutWriterOptions
        {
            IncludeSections = pipeline.ComputeIncludeSections(
                inspections[0], options.Verbosity, options.IncludeSections, selectAll),
            Projection = BuildProjection(options.Columns, options.Fields)
        };

        if (options.Count)
        {
            var markdown = MarkoutSerializer.Serialize(report, InspectionContext.Default, writerOptions);
            if (selectAll)
                markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.GetAllSelectorSections(inspections[0]));
            else if (selectInfo)
                markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.InfoSectionNames);
            markdown = MarkdownTableRowLimiter.Apply(markdown, options.Rows);
            CountOutput.WriteCountFromMarkdown(markdown);
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
            if (selectAll)
                markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.GetAllSelectorSections(inspections[0]));
            else if (selectInfo)
                markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.InfoSectionNames);
            Console.WriteLine(MarkdownTableRowLimiter.Apply(markdown, options.Rows));
        }
        else
        {
            foreach (var inspection in inspections)
            {
                var auditView = new LibraryInspectionView(inspection, topFieldsOnly);
                var includeSections = pipeline.ComputeIncludeSections(
                    inspection, options.Verbosity, options.IncludeSections, selectAll);
                var writerOpts = new MarkoutWriterOptions
                {
                    IncludeSections = includeSections,
                    Projection = BuildProjection(options.Columns, options.Fields),
                };
                ConfigureTableWriterOptions(writerOpts, options.Tsv, options.Jsonl);
                WriteTable(Console.Out, !options.NoHeader,
                    (writer, formatter) => MarkoutSerializer.Serialize(auditView, writer, formatter, InspectionContext.Default, writerOpts),
                    options.Rows);
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

        return new MarkoutProjection
        {
            IncludeColumns = columns,
            IncludeFields = fields,
        };
    }

    internal static bool ShouldRenderLibraryContext(LibraryOptions options) =>
        options.Verbosity == Verbosity.Quiet
        || (options.IncludeSections is { Count: > 0 }
            && !SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections)
            && !SelectResolver.IsActiveInfoSelector(options.Select, options.IncludeSections)
            && !options.Count
            && !options.JsonOutput
            && !options.OneLine);

    internal static bool ShouldRenderPackageContext(InspectionOptions options) =>
        options.IncludeSections is { Count: > 0 }
        && !SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections)
        && !SelectResolver.IsActiveInfoSelector(options.Select, options.IncludeSections)
        && !options.Count
        && !options.JsonOutput
        && !options.OneLine;
}
