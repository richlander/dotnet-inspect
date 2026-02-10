using DotnetInspector.Models;
using DotnetInspector.Views;
using System.Text.Json;
using DotnetInspector.Options;
using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// Handles output formatting for inspection results.
/// </summary>
public static class OutputFormatter
{
    public static string FormatResult(InspectionResult result, InspectionOptions options)
    {
        if (options.JsonOutput)
        {
            return JsonSerializer.Serialize(result, JsonContext.Default.InspectionResult);
        }

        return RenderMarkout(result, options);
    }

    private static string RenderMarkout(InspectionResult result, InspectionOptions options)
    {
        var view = new InspectionResultView(result);
        var context = new MarkoutContext(new MarkoutWriterOptions
        {
            IncludeSections = options.IncludeSections,
            ExcludeSections = GetExcludeSections(options),
            IncludeDescription = options.Verbosity != Verbosity.Quiet
        });

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

    public static void WriteLibraryResult(LibraryInspection inspection, AssemblyOptions options)
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
            var context = new MarkoutContext(new MarkoutWriterOptions
            {
                IncludeSections = options.IncludeSections,
                ExcludeSections = GetLibraryExcludeSections(options)
            });
            Console.WriteLine(context.Serialize(auditView).TrimEnd());
        }
    }

    public static void WriteLibraryResults(List<LibraryInspection> inspections, AssemblyOptions options)
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
            var context = new MarkoutContext(new MarkoutWriterOptions
            {
                IncludeSections = options.IncludeSections,
                ExcludeSections = GetLibraryExcludeSections(options)
            });
            Console.WriteLine(context.Serialize(report).TrimEnd());
        }
    }

    private static HashSet<string>? GetLibraryExcludeSections(AssemblyOptions options)
    {
        // When explicit include sections are set, skip exclude logic
        if (options.IncludeSections != null)
            return null;

        HashSet<string> excluded = ["Source Coverage", "Missing Sources"];

        if (options.Verbosity != Verbosity.Detailed)
            excluded.Add("Symbols");

        if (options.IncludeSourcelinkAudit)
        {
            excluded.Remove("Source Coverage");
            excluded.Remove("Missing Sources");
        }

        return excluded.Count > 0 ? excluded : null;
    }

}
