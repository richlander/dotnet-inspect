using DotnetInspector.Models;
using DotnetInspector.Views;
using System.Text.Json;
using DotnetInspector.Options;
using DotnetInspector.Sections;
using Markout;

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
    public static string FormatResult(InspectionResult result, InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline)
    {
        if (options.JsonOutput && !options.Count)
        {
            return JsonSerializer.Serialize(result, JsonContext.Default.InspectionResult);
        }

        bool selectAll = SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections);
        bool includeContext = ShouldRenderPackageContext(options);
        var view = new InspectionResultView(result, includeTitleVersion: !includeContext);
        var writerOptions = BuildWriterOptions(result, options, pipeline, includeContext);
        var markdown = MarkoutSerializer.Serialize(view, InspectionContext.Default, writerOptions).TrimEnd();
        if (selectAll)
            markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.GetAllSelectorSections(result));
        markdown = MarkdownTableRowLimiter.Apply(markdown, options.Rows);
        return options.Count ? CountOutput.CountMarkdownTableRows(markdown).ToString() : markdown;
    }

    public static void WritePackageOneLine(InspectionResult result, InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline, bool showHeader)
    {
        var writerOpts = BuildWriterOptions(result, options, pipeline);
        var view = new InspectionResultView(result);
        MarkoutSerializer.Serialize(view, Console.Out, new OneLineFormatter(showHeader: showHeader), InspectionContext.Default, writerOpts);
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
            return new RenderDiagnostic("oneline", "multiple_sections",
                writerOpts.IncludeSections.ToArray());
        return null;
    }

    internal static MarkoutWriterOptions BuildWriterOptions(InspectionResult result, InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline, bool includeContext = false)
    {
        var selectAll = SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections);
        var includeSections = pipeline.ComputeIncludeSections(
            result, options.Verbosity, options.IncludeSections, selectAll);
        if (includeContext && includeSections is { Count: > 0 })
            includeSections = [PackageSections.Summary, .. includeSections];

        return new MarkoutWriterOptions
        {
            IncludeSections = includeSections,
            IncludeDescription = options.Verbosity != Verbosity.Quiet && !includeContext,
            Projection = BuildProjection(options.Columns, options.Fields)
        };
    }

    public static void WriteLibraryResult(LibraryInspection inspection, AssemblyOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        bool selectAll = SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections);
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
            Console.WriteLine(MarkdownTableRowLimiter.Apply(markdown, options.Rows));
        }
        else if (writerOpts.IncludeSections is { Count: > 1 } && !options.OneLineExplicitlySet)
        {
            // Auto-promote to markdown when multiple sections and oneline wasn't explicitly requested
            var markdown = MarkoutSerializer.Serialize(auditView, InspectionContext.Default, writerOpts).TrimEnd();
            if (selectAll)
                markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.GetAllSelectorSections(inspection));
            Console.WriteLine(MarkdownTableRowLimiter.Apply(markdown, options.Rows));
        }
        else
        {
            MarkoutSerializer.Serialize(auditView, Console.Out, new OneLineFormatter(showHeader: !options.NoHeader), InspectionContext.Default, writerOpts);
        }
    }

    public static void WriteLibraryResults(List<LibraryInspection> inspections, AssemblyOptions options,
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
                inspections[0], options.Verbosity, options.IncludeSections, selectAll),
            Projection = BuildProjection(options.Columns, options.Fields)
        };

        if (options.Count)
        {
            var markdown = MarkoutSerializer.Serialize(report, InspectionContext.Default, writerOptions);
            if (selectAll)
                markdown = MarkdownSectionOrderer.Apply(markdown, pipeline.GetAllSelectorSections(inspections[0]));
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
                MarkoutSerializer.Serialize(auditView, Console.Out, new OneLineFormatter(showHeader: !options.NoHeader), InspectionContext.Default, writerOpts);
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
            && !options.Count
            && !options.JsonOutput);

    internal static bool ShouldRenderPackageContext(InspectionOptions options) =>
        options.IncludeSections is { Count: > 0 }
        && !SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections)
        && !options.Count
        && !options.JsonOutput
        && !options.OneLine;
}
