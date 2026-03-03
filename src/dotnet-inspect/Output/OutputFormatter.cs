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
        if (options.JsonOutput)
        {
            return JsonSerializer.Serialize(result, JsonContext.Default.InspectionResult);
        }

        var view = new InspectionResultView(result);
        var writerOptions = BuildWriterOptions(result, options, pipeline);
        var context = new MarkoutContext(writerOptions);
        return context.Serialize(view).TrimEnd();
    }

    public static void WritePackageOneLine(InspectionResult result, InspectionOptions options,
        SectionPipeline<InspectionResult> pipeline, bool showHeader)
    {
        var writerOpts = BuildWriterOptions(result, options, pipeline);
        var view = new InspectionResultView(result);
        new MarkoutContext().Serialize(view, Console.Out, new OneLineFormatter(showHeader: showHeader), writerOpts);
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
        SectionPipeline<InspectionResult> pipeline)
    {
        var includeSections = pipeline.ComputeIncludeSections(
            result, options.Verbosity, options.IncludeSections);

        return new MarkoutWriterOptions
        {
            IncludeSections = includeSections,
            IncludeDescription = options.Verbosity != Verbosity.Quiet,
            Projection = BuildProjection(options.Columns, options.Fields)
        };
    }

    public static void WriteLibraryResult(LibraryInspection inspection, AssemblyOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
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

        bool topFieldsOnly = options.Verbosity == Verbosity.Quiet;
        var auditView = new LibraryInspectionView(inspection, topFieldsOnly);
        var includeSections = pipeline.ComputeIncludeSections(
            inspection, options.Verbosity, options.IncludeSections);
        var writerOpts = new MarkoutWriterOptions
        {
            IncludeSections = includeSections,
            Projection = BuildProjection(options.Columns, options.Fields)
        };

        if (options.VerbosityEnabled)
        {
            var context = new MarkoutContext(writerOpts);
            Console.WriteLine(context.Serialize(auditView).TrimEnd());
        }
        else if (writerOpts.IncludeSections is { Count: > 1 } && !options.OneLineExplicitlySet)
        {
            // Auto-promote to markdown when multiple sections and oneline wasn't explicitly requested
            var context = new MarkoutContext(writerOpts);
            Console.WriteLine(context.Serialize(auditView).TrimEnd());
        }
        else
        {
            new MarkoutContext().Serialize(auditView, Console.Out, new OneLineFormatter(), writerOpts);
        }
    }

    public static void WriteLibraryResults(List<LibraryInspection> inspections, AssemblyOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(inspections.ToArray(), JsonContext.Default.LibraryInspectionArray));
            return;
        }

        if (options.VerbosityEnabled)
        {
            bool topFieldsOnly = options.Verbosity == Verbosity.Quiet;
            var report = new LibraryInspectionReport
            {
                Title = Path.GetFileNameWithoutExtension(inspections[0].FileName),
                Assemblies = inspections.Select(a => new LibraryInspectionView(a, topFieldsOnly)).ToList()
            };
            var writerOptions = new MarkoutWriterOptions
            {
                IncludeSections = pipeline.ComputeIncludeSections(
                    inspections[0], options.Verbosity, options.IncludeSections),
                Projection = BuildProjection(options.Columns, options.Fields)
            };
            var context = new MarkoutContext(writerOptions);
            Console.WriteLine(context.Serialize(report).TrimEnd());
        }
        else
        {
            foreach (var inspection in inspections)
            {
                var auditView = new LibraryInspectionView(inspection);
                var includeSections = pipeline.ComputeIncludeSections(
                    inspection, options.Verbosity, options.IncludeSections);
                var writerOpts = new MarkoutWriterOptions
                {
                    IncludeSections = includeSections,
                    Projection = BuildProjection(options.Columns, options.Fields),
                };
                new MarkoutContext().Serialize(auditView, Console.Out, new OneLineFormatter(), writerOpts);
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
}
