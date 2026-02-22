using DotnetInspector.Models;
using DotnetInspector.Views;
using System.Text.Json;
using DotnetInspector.Options;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspector.Output;

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

        return RenderMarkout(result, options, pipeline);
    }

    public static PackageOneLineView BuildPackageOneLineView(InspectionResult result, InspectionOptions options,
        SectionPipeline<InspectionResult>? pipeline = null)
    {
        var view = new InspectionResultView(result);
        var fields = view.Metadata;

        // When -S is specified, filter rows by property name (field tables become
        // 2-column Property/Value tables in oneline mode, so column projection
        // doesn't apply — we need row-level filtering instead).
        if (options.Select is { Length: > 0 })
        {
            var selected = new HashSet<string>(options.Select, StringComparer.OrdinalIgnoreCase);
            fields = fields.Where(f => selected.Contains(f.Key)).ToList();
        }

        var rows = fields.Select(f => new PackageOneLineRow(f.Key, f.Value ?? "")).ToList();
        return new PackageOneLineView { Rows = rows };
    }

    private static string RenderMarkout(InspectionResult result, InspectionOptions options,
        SectionPipeline<InspectionResult>? pipeline)
    {
        var view = new InspectionResultView(result);

        var writerOptions = pipeline != null
            ? new MarkoutWriterOptions
            {
                IncludeSections = pipeline.ComputeIncludeSections(
                    result, options.Verbosity, options.IncludeSections, options.ExcludeSections),
                IncludeDescription = options.Verbosity != Verbosity.Quiet,
                Projection = BuildProjection(options.Select)
            }
            : new MarkoutWriterOptions
            {
                IncludeSections = options.IncludeSections,
                ExcludeSections = GetExcludeSections(options),
                IncludeDescription = options.Verbosity != Verbosity.Quiet,
                Projection = BuildProjection(options.Select)
            };

        var context = new MarkoutContext(writerOptions);
        return context.Serialize(view).TrimEnd();
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
            var view = AssemblyDependenciesView.FromInspection(inspection);
            MarkoutSerializer.Serialize(view, Console.Out, AssemblyDependenciesContext.Default);
            return;
        }

        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(inspection, JsonContext.Default.LibraryInspection));
        }
        else
        {
            var auditView = new LibraryInspectionView(inspection);
            var includeSections = pipeline?.ComputeIncludeSections(
                inspection, options.Verbosity, options.IncludeSections, options.ExcludeSections);

            var writerOptions = pipeline != null
                ? new MarkoutWriterOptions
                {
                    IncludeSections = includeSections,
                    Projection = BuildProjection(options.Select)
                }
                : new MarkoutWriterOptions
                {
                    IncludeSections = options.IncludeSections,
                    ExcludeSections = GetLibraryExcludeSections(options),
                    Projection = BuildProjection(options.Select)
                };
            var context = new MarkoutContext(writerOptions);
            Console.WriteLine(context.Serialize(auditView).TrimEnd());
        }
    }

    public static void WriteLibraryResults(List<LibraryInspection> inspections, AssemblyOptions options,
        SectionPipeline<LibraryInspection>? pipeline = null)
    {
        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(inspections.ToArray(), JsonContext.Default.LibraryInspectionArray));
        }
        else
        {
            var report = new LibraryInspectionReport
            {
                Title = Path.GetFileNameWithoutExtension(inspections[0].FileName),
                Assemblies = inspections.Select(a => new LibraryInspectionView(a)).ToList()
            };
            var writerOptions = pipeline != null
                ? new MarkoutWriterOptions
                {
                    IncludeSections = pipeline.ComputeIncludeSections(
                        inspections[0], options.Verbosity, options.IncludeSections, options.ExcludeSections),
                    Projection = BuildProjection(options.Select)
                }
                : new MarkoutWriterOptions
                {
                    IncludeSections = options.IncludeSections,
                    ExcludeSections = GetLibraryExcludeSections(options),
                    Projection = BuildProjection(options.Select)
                };
            var context = new MarkoutContext(writerOptions);
            Console.WriteLine(context.Serialize(report).TrimEnd());
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
    /// Builds a MarkoutProjection from column and field filter arrays.
    /// Returns null when no projection is needed (both null).
    /// </summary>
    internal static MarkoutProjection? BuildProjection(string[]? select)
    {
        if (select == null)
            return null;

        return new MarkoutProjection
        {
            IncludeSections = select,
            IncludeColumns = select,
            IncludeFields = select
        };
    }
}
