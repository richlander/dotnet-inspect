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
        SectionPipeline<InspectionResult>? pipeline = null)
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
        SectionPipeline<InspectionResult>? pipeline, bool showHeader)
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
        SectionPipeline<InspectionResult>? pipeline)
    {
        var writerOpts = BuildWriterOptions(result, options, pipeline);
        if (writerOpts.IncludeSections is { Count: > 1 })
            return new RenderDiagnostic("oneline", "multiple_sections",
                writerOpts.IncludeSections.ToArray());
        return null;
    }

    internal static MarkoutWriterOptions BuildWriterOptions(InspectionResult result, InspectionOptions options,
        SectionPipeline<InspectionResult>? pipeline)
    {
        HashSet<string>? includeSections = null;

        if (pipeline != null)
        {
            includeSections = pipeline.ComputeIncludeSections(
                result, options.Verbosity, options.IncludeSections, options.ExcludeSections);
        }

        return new MarkoutWriterOptions
        {
            IncludeSections = includeSections,
            ExcludeSections = includeSections == null ? GetExcludeSections(options) : null,
            IncludeDescription = options.Verbosity != Verbosity.Quiet,
            Projection = BuildProjection(options.Columns, options.Fields)
        };
    }

    private static HashSet<string>? GetExcludeSections(InspectionOptions options)
    {
        // Don't set excludes when includes are specified
        if (options.IncludeSections != null)
            return null;

        // If user specified explicit excludes, use those
        if (options.ExcludeSections != null)
            return options.ExcludeSections;

        // Otherwise, map verbosity to section exclusions
        return options.Verbosity switch
        {
            // Quiet: exclude all sections (just title + compact line)
            Verbosity.Quiet => [PackageSections.Package, PackageSections.Statistics, PackageSections.PackageDependencies, PackageSections.Files, PackageSections.Vulnerabilities, PackageSections.RidPackages, PackageSections.RuntimeDependencies],
            // Minimal: show Metadata, exclude Statistics, Package Dependencies, Files, Vulnerabilities
            Verbosity.Minimal => [PackageSections.Statistics, PackageSections.PackageDependencies, PackageSections.Files, PackageSections.Vulnerabilities],
            // Normal: show most sections, exclude Vulnerabilities (use -v:d to see them)
            Verbosity.Normal => [PackageSections.Vulnerabilities],
            Verbosity.Detailed => [PackageSections.Files],
            _ => null
        };
    }

    public static void WriteLibraryResult(LibraryInspection inspection, AssemblyOptions options,
        SectionPipeline<LibraryInspection>? pipeline = null)
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
        }
        else if (options.VerbosityEnabled)
        {
            bool topFieldsOnly = options.Verbosity == Verbosity.Quiet;
            var auditView = new LibraryInspectionView(inspection, topFieldsOnly);
            var includeSections = pipeline?.ComputeIncludeSections(
                inspection, options.Verbosity, options.IncludeSections, options.ExcludeSections);

            var writerOptions = pipeline != null
                ? new MarkoutWriterOptions
                {
                    IncludeSections = includeSections,
                    Projection = BuildProjection(options.Columns, options.Fields)
                }
                : new MarkoutWriterOptions
                {
                    IncludeSections = options.IncludeSections,
                    ExcludeSections = GetLibraryExcludeSections(options),
                    Projection = BuildProjection(options.Columns, options.Fields)
                };
            var context = new MarkoutContext(writerOptions);
            Console.WriteLine(context.Serialize(auditView).TrimEnd());
        }
        else if (options.Verbosity == Verbosity.Quiet)
        {
            var auditView = new LibraryInspectionView(inspection, topFieldsOnly: true);
            var includeSections = pipeline?.ComputeIncludeSections(
                inspection, options.Verbosity, options.IncludeSections, options.ExcludeSections);
            var writerOptions = new MarkoutWriterOptions
            {
                IncludeSections = includeSections,
                Projection = BuildProjection(options.Columns, options.Fields)
            };
            var context = new MarkoutContext(writerOptions);
            Console.WriteLine(context.Serialize(auditView).TrimEnd());
        }
        else
        {
            var auditView = new LibraryInspectionView(inspection);
            var includeSections = pipeline?.ComputeIncludeSections(
                inspection, options.Verbosity, options.IncludeSections, options.ExcludeSections);
            var writerOpts = new MarkoutWriterOptions
            {
                IncludeSections = includeSections ?? options.IncludeSections,
                Projection = BuildProjection(options.Columns, options.Fields),
            };

            // Auto-promote to markdown when multiple sections and oneline wasn't explicitly requested
            if (writerOpts.IncludeSections is { Count: > 1 } && !options.OneLineExplicitlySet)
            {
                var context = new MarkoutContext(writerOpts);
                Console.WriteLine(context.Serialize(auditView).TrimEnd());
            }
            else
            {
                new MarkoutContext().Serialize(auditView, Console.Out, new OneLineFormatter(), writerOpts);
            }
        }
    }

    public static void WriteLibraryResults(List<LibraryInspection> inspections, AssemblyOptions options,
        SectionPipeline<LibraryInspection>? pipeline = null)
    {
        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(inspections.ToArray(), JsonContext.Default.LibraryInspectionArray));
        }
        else if (options.VerbosityEnabled)
        {
            bool topFieldsOnly = options.Verbosity == Verbosity.Quiet;
            var report = new LibraryInspectionReport
            {
                Title = Path.GetFileNameWithoutExtension(inspections[0].FileName),
                Assemblies = inspections.Select(a => new LibraryInspectionView(a, topFieldsOnly)).ToList()
            };
            var writerOptions = pipeline != null
                ? new MarkoutWriterOptions
                {
                    IncludeSections = pipeline.ComputeIncludeSections(
                        inspections[0], options.Verbosity, options.IncludeSections, options.ExcludeSections),
                    Projection = BuildProjection(options.Columns, options.Fields)
                }
                : new MarkoutWriterOptions
                {
                    IncludeSections = options.IncludeSections,
                    ExcludeSections = GetLibraryExcludeSections(options),
                    Projection = BuildProjection(options.Columns, options.Fields)
                };
            var context = new MarkoutContext(writerOptions);
            Console.WriteLine(context.Serialize(report).TrimEnd());
        }
        else if (options.Verbosity == Verbosity.Quiet)
        {
            foreach (var inspection in inspections)
            {
                var auditView = new LibraryInspectionView(inspection, topFieldsOnly: true);
                var includeSections = pipeline?.ComputeIncludeSections(
                    inspection, options.Verbosity, options.IncludeSections, options.ExcludeSections);
                var writerOpts = new MarkoutWriterOptions
                {
                    IncludeSections = includeSections,
                    Projection = BuildProjection(options.Columns, options.Fields),
                };
                var context = new MarkoutContext(writerOpts);
                Console.WriteLine(context.Serialize(auditView).TrimEnd());
            }
        }
        else
        {
            foreach (var inspection in inspections)
            {
                var auditView = new LibraryInspectionView(inspection);
                var includeSections = pipeline?.ComputeIncludeSections(
                    inspection, options.Verbosity, options.IncludeSections, options.ExcludeSections);
                var writerOpts = new MarkoutWriterOptions
                {
                    IncludeSections = includeSections ?? options.IncludeSections,
                    Projection = BuildProjection(options.Columns, options.Fields),
                };
                new MarkoutContext().Serialize(auditView, Console.Out, new OneLineFormatter(), writerOpts);
            }
        }
    }

    private static HashSet<string>? GetLibraryExcludeSections(AssemblyOptions options)
    {
        // When explicit include sections are set, skip exclude logic
        if (options.IncludeSections != null)
            return null;

        HashSet<string> excluded = ["Source Link Audit"];

        if (options.Verbosity != Verbosity.Detailed)
            excluded.Add("Symbols");

        if (options.IncludeSourcelinkAudit)
        {
            excluded.Remove("Source Link Audit");
        }

        return excluded.Count > 0 ? excluded : null;
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
