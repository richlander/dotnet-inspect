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
    public static void WriteResult(InspectionResult result, InspectionOptions options)
    {
        string output;
        if (options.JsonOutput)
        {
            output = JsonSerializer.Serialize(result, JsonContext.Default.InspectionResult);
        }
        else
        {
            output = RenderMarkout(result, options);
        }
        
        WriteOutput(output, options.OutputPath);
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
            Verbosity.Quiet => ["Package", "Statistics", "Package Dependencies", "Files", "Vulnerabilities", "RID Packages", "Runtime Dependencies"],
            // Minimal: show Metadata, exclude Statistics, Package Dependencies, Files
            Verbosity.Minimal => ["Statistics", "Package Dependencies", "Files"],
            // Normal and Detailed: show everything
            Verbosity.Normal => null,
            Verbosity.Detailed => ["Files"],
            _ => null
        };
    }

    public static void WriteAssemblyResult(AssemblyAudit audit, AssemblyOptions options)
    {
        if (audit.UseDependenciesView)
        {
            var view = AssemblyDependenciesView.FromAudit(audit);
            MarkoutSerializer.Serialize(view, Console.Out, AssemblyDependenciesContext.Default);
            return;
        }

        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(audit, JsonContext.Default.AssemblyAudit));
        }
        else
        {
            var auditView = new AssemblyAuditView(audit);
            var context = new MarkoutContext(new MarkoutWriterOptions
            {
                ExcludeSections = GetAuditExcludeSections(options)
            });
            Console.WriteLine(context.Serialize(auditView).TrimEnd());
        }
    }

    public static void WriteAssemblyResults(List<AssemblyAudit> audits, AssemblyOptions options)
    {
        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(audits.ToArray(), JsonContext.Default.AssemblyAuditArray));
        }
        else
        {
            var report = new AssemblyAuditReport
            {
                Title = Path.GetFileNameWithoutExtension(audits[0].FileName),
                Assemblies = audits.Select(a => new AssemblyAuditView(a)).ToList()
            };
            var context = new MarkoutContext(new MarkoutWriterOptions
            {
                ExcludeSections = GetAuditExcludeSections(options)
            });
            Console.WriteLine(context.Serialize(report).TrimEnd());
        }
    }

    private static HashSet<string>? GetAuditExcludeSections(AssemblyOptions options)
    {
        if (!options.HasAuditTier)
            return ["Symbols", "Source Coverage", "Non-normalized Paths", "Missing Sources"];
        if (!options.IncludeSourcelinkAudit)
            return ["Source Coverage", "Missing Sources"];
        return null;
    }

    /// <summary>
    /// Writes output to file if path is specified, otherwise to stdout.
    /// </summary>
    public static void WriteOutput(string content, string? outputPath)
    {
        if (!string.IsNullOrEmpty(outputPath))
        {
            File.WriteAllText(outputPath, content);
        }
        else
        {
            Console.WriteLine(content);
        }
    }
}
