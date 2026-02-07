using System.Text;
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
        var context = new MarkoutContext(new MarkoutWriterOptions
        {
            IncludeSections = options.IncludeSections,
            ExcludeSections = GetExcludeSections(options),
            IncludeDescription = options.Verbosity != Verbosity.Quiet
        });

        return context.Serialize(result).TrimEnd();
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
            Verbosity.Quiet => ["Metadata", "Statistics", "Package Dependencies", "Files", "Vulnerabilities", "RID Packages", "Runtime Dependencies"],
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
        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(audit, JsonContext.Default.AssemblyAudit));
        }
        else
        {
            Console.WriteLine(RenderAssemblyMarkdown(audit, options));
        }
    }

    private static string RenderAssemblyMarkdown(AssemblyAudit audit, AssemblyOptions options)
    {
        // Determine which sections to exclude
        HashSet<string>? excludeSections = null;
        if (!options.IncludeAudit)
        {
            excludeSections = ["Build Audit", "PDB", "Source Coverage"];
        }

        var context = new MarkoutContext(new MarkoutWriterOptions
        {
            ExcludeSections = excludeSections
        });

        var output = context.Serialize(audit);

        // Append sections that need imperative rendering
        var writer = new MarkoutWriter();

        // Assembly References (tree vs table format requires imperative control)
        if (audit.AssemblyInfo != null)
        {
            var info = audit.AssemblyInfo;
            if (info.TransitiveReferences is { Count: > 0 })
            {
                writer.WriteHeading(2, "Assembly References (Transitive)");
                var refTree = BuildReferenceTree(info.TransitiveReferences);
                writer.WriteTree(refTree);
            }
            else if (info.References is { Count: > 0 })
            {
                writer.WriteHeading(2, "Assembly References");
                var refRows = info.References.OrderBy(r => r.Name)
                    .Select(r => new[] { r.Name, r.Version, r.PublicKeyToken ?? "-" });
                writer.WriteTable(new[] { "Name", "Version", "Public Key Token" }, refRows);
            }
        }

        if (options.IncludeAudit)
        {
            // RepositoryUrl and NonNormalizedPaths appear before the audit table
            // but after the Assembly Info section — append them here
            if (!string.IsNullOrEmpty(audit.RepositoryUrl))
            {
                writer.WriteField("Repository", audit.RepositoryUrl);
            }

            if (audit.NonNormalizedPaths is { Count: > 0 })
            {
                writer.WriteArray("Non-normalized paths", audit.NonNormalizedPaths);
            }

            // Windows PDB warning
            if (audit.PdbLocation == null && !string.IsNullOrEmpty(audit.PdbPath))
            {
                writer.WriteParagraph("*Path is from the CodeView record in the assembly; actual PDB location is unknown.*");
            }

            if (audit.WindowsPdbDetected)
            {
                writer.WriteParagraph("**Note:** Windows PDB format is not supported by this tool.");
                writer.WriteParagraph("Only Portable PDBs (embedded or in .snupkg) can be read.");
                writer.WriteParagraph("Consider asking the package maintainer to publish Portable PDBs.");
            }

            // Missing source files (truncated to 10)
            if (audit.MissingSourceFiles is { Count: > 0 })
            {
                var displayFiles = audit.MissingSourceFiles.Take(10).Select(f => $"`{f}`").ToList();
                if (audit.MissingSourceFiles.Count > 10)
                {
                    displayFiles.Add($"... and {audit.MissingSourceFiles.Count - 10} more");
                }
                writer.WriteArray("Missing sources", displayFiles);
            }
        }

        var additional = writer.ToString();
        return (output + additional).TrimEnd();
    }

    private static List<TreeNode> BuildReferenceTree(List<AssemblyReferenceNode> nodes)
    {
        var result = new List<TreeNode>();
        foreach (var node in nodes)
        {
            var icon = node.ResolvedFrom switch
            {
                "local" => "📁",
                "platform" => "🚢",
                _ => "❓"
            };
            var suffix = node.IsCyclic ? " (circular)" : "";
            result.Add(new TreeNode($"{node.Name} {node.Version}{suffix}", icon));
        }
        return result;
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
