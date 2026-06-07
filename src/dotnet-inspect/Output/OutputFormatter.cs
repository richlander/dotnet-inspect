using DotnetInspector.Models;
using DotnetInspector.Views;
using System.Text.Json;
using DotnetInspector.Options;
using DotnetInspector.Sections;
using Markout;
using Markout.Formatting;
using System.Text;

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
    public static string RenderTable(bool tsv, bool showHeader, Action<TextWriter, IMarkoutFormatter> serialize)
    {
        var sw = new StringWriter();
        serialize(sw, new TableFormatter(showHeader));
        var tabular = sw.ToString();
        return tsv ? tabular : PrettyTableFormatter.Format(tabular);
    }

    public static void WriteTable(bool tsv, TextWriter output, bool showHeader, Action<TextWriter, IMarkoutFormatter> serialize) =>
        output.Write(RenderTable(tsv, showHeader, serialize));

    public static MarkoutWriterOptions ConfigureTableWriterOptions(MarkoutWriterOptions options, bool tsv)
    {
        if (tsv)
            options.FormatTableHeader = header => ToMachineHeader(header.Name);
        return options;
    }

    public static MarkoutWriterOptions CreateTableWriterOptions(bool tsv) =>
        ConfigureTableWriterOptions(new MarkoutWriterOptions(), tsv);

    private static string ToMachineHeader(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var sb = new StringBuilder(name.Length + 4);
        var lastWasSeparator = true;

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c))
            {
                AppendSeparator(sb, ref lastWasSeparator);
                continue;
            }

            if (char.IsUpper(c))
            {
                var previousIsLowerOrDigit = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (sb.Length > 0 && !lastWasSeparator && (previousIsLowerOrDigit || nextIsLower))
                    AppendSeparator(sb, ref lastWasSeparator);
            }

            sb.Append(char.ToLowerInvariant(c));
            lastWasSeparator = false;
        }

        if (sb.Length > 0 && sb[^1] == '_')
            sb.Length--;
        return sb.ToString();
    }

    private static void AppendSeparator(StringBuilder sb, ref bool lastWasSeparator)
    {
        if (!lastWasSeparator && sb.Length > 0)
        {
            sb.Append('_');
            lastWasSeparator = true;
        }
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
        ConfigureTableWriterOptions(writerOpts, options.Tsv);
        var view = new InspectionResultView(result);
        WriteTable(options.Tsv, Console.Out, showHeader,
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

    public static void WriteLibraryResult(LibraryInspection inspection, AssemblyOptions options,
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
            ConfigureTableWriterOptions(writerOpts, options.Tsv);
            WriteTable(options.Tsv, Console.Out, !options.NoHeader,
                (writer, formatter) => MarkoutSerializer.Serialize(auditView, writer, formatter, InspectionContext.Default, writerOpts));
        }
    }

    public static void WriteLibraryResults(List<LibraryInspection> inspections, AssemblyOptions options,
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
                ConfigureTableWriterOptions(writerOpts, options.Tsv);
                WriteTable(options.Tsv, Console.Out, !options.NoHeader,
                    (writer, formatter) => MarkoutSerializer.Serialize(auditView, writer, formatter, InspectionContext.Default, writerOpts));
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

    internal static bool ShouldRenderLibraryContext(AssemblyOptions options) =>
        options.Verbosity == Verbosity.Quiet
        || (options.IncludeSections is { Count: > 0 }
            && !SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections)
            && !SelectResolver.IsActiveInfoSelector(options.Select, options.IncludeSections)
            && !options.Count
            && !options.JsonOutput);

    internal static bool ShouldRenderPackageContext(InspectionOptions options) =>
        options.IncludeSections is { Count: > 0 }
        && !SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections)
        && !SelectResolver.IsActiveInfoSelector(options.Select, options.IncludeSections)
        && !options.Count
        && !options.JsonOutput
        && !options.OneLine;
}
