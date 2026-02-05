using System.Text;
using System.Text.Json;
using DotnetInspector.Options;

namespace DotnetInspector.Output;

/// <summary>
/// Handles output formatting for inspection results.
/// </summary>
public static class OutputFormatter
{
    /// <summary>
    /// Section names in order for package command.
    /// </summary>
    public static readonly string[] SectionNames =
    [
        "Metadata",
        "Statistics",
        "Package Dependencies",
        "Files",
        "Vulnerabilities",
        "RID Packages",
        "Runtime Dependencies"
    ];

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
        var context = new MarkoutContext
        {
            IncludeSections = options.IncludeSections,
            ExcludeSections = GetExcludeSections(options),
            IncludeDescription = options.Verbosity != Verbosity.Quiet
        };

        return context.Serialize(result).TrimEnd();
    }

    private static HashSet<int>? GetExcludeSections(InspectionOptions options)
    {
        // If user specified explicit excludes, use those
        if (options.ExcludeSections != null)
            return options.ExcludeSections;

        // Otherwise, map verbosity to section exclusions
        return options.Verbosity switch
        {
            // Quiet: exclude all sections (just title + compact line)
            Verbosity.Quiet => [1, 2, 3, 4, 5, 6, 7, 8],
            // Minimal: exclude all sections (just title + description + compact line)
            Verbosity.Minimal => [1, 2, 3, 4, 5, 6, 7, 8],
            // Normal: show Metadata (1), exclude Statistics (2), Package Deps (3), Files (4)
            Verbosity.Normal => [2, 3, 4],
            // Detailed: show everything
            Verbosity.Detailed => null,
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
        var sb = new StringBuilder();

        // Header
        sb.AppendLine($"# {audit.FileName}");

        // Assembly Info
        if (audit.AssemblyInfo != null)
        {
            var info = audit.AssemblyInfo;
            sb.AppendLine();
            sb.AppendLine("## Assembly Info");
            sb.AppendLine();
            sb.AppendLine("| Property | Value |");
            sb.AppendLine("| --- | --- |");

            if (!string.IsNullOrEmpty(info.AssemblyName))
                sb.AppendLine($"| Name | {info.AssemblyName} |");
            if (!string.IsNullOrEmpty(info.AssemblyVersion))
                sb.AppendLine($"| Version | {info.AssemblyVersion} |");
            if (!string.IsNullOrEmpty(info.TargetFramework))
                sb.AppendLine($"| Target Framework | {info.TargetFramework} |");
            if (!string.IsNullOrEmpty(info.Architecture))
                sb.AppendLine($"| Architecture | {info.Architecture} |");
            if (!string.IsNullOrEmpty(info.CompilationType))
                sb.AppendLine($"| Compilation | {info.CompilationType} |");
            if (!string.IsNullOrEmpty(info.InformationalVersion))
                sb.AppendLine($"| Informational Version | {info.InformationalVersion} |");
            if (!string.IsNullOrEmpty(info.Product))
                sb.AppendLine($"| Product | {info.Product} |");
            if (!string.IsNullOrEmpty(info.Company))
                sb.AppendLine($"| Company | {info.Company} |");
            if (!string.IsNullOrEmpty(info.Copyright))
                sb.AppendLine($"| Copyright | {info.Copyright} |");
            if (info.IsSigned)
                sb.AppendLine($"| Signed | Yes |");
            if (!string.IsNullOrEmpty(info.PublicKeyToken))
                sb.AppendLine($"| Public Key Token | {info.PublicKeyToken} |");
        }

        // Audit section (if --audit was specified)
        if (options.IncludeAudit)
        {
            sb.AppendLine();
            sb.AppendLine("## Build Audit");

            // Show fields before the table
            if (!string.IsNullOrEmpty(audit.RepositoryUrl))
            {
                sb.AppendLine();
                sb.AppendLine($"**Repository:** {audit.RepositoryUrl}");
            }

            if (audit.NonNormalizedPaths is { Count: > 0 })
            {
                sb.AppendLine();
                sb.AppendLine("**Non-normalized paths:**");
                foreach (var path in audit.NonNormalizedPaths)
                {
                    sb.AppendLine($"- {path}");
                }
            }

            // Show the checkmark table
            sb.AppendLine();
            sb.AppendLine("| Check | Status |");
            sb.AppendLine("| --- | --- |");
            sb.AppendLine($"| Deterministic | {(audit.IsDeterministic ? "✓" : "✗")} |");
            sb.AppendLine($"| Reproducible Flag | {(audit.HasReproducibleFlag ? "✓" : "✗")} |");
            sb.AppendLine($"| SourceLink | {audit.SourceLinkStatus} |");
            if (!string.IsNullOrEmpty(audit.Builder))
            {
                sb.AppendLine($"| Builder | {audit.Builder} |");
            }
            if (!string.IsNullOrEmpty(audit.Publisher))
            {
                var publisherStatus = audit.PublisherVerified ? "(Verified)" : "";
                sb.AppendLine($"| Publisher | {audit.Publisher} {publisherStatus} |");
            }
            else if (!string.IsNullOrEmpty(audit.SignatureStatus))
            {
                sb.AppendLine($"| Publisher | {audit.SignatureStatus} |");
            }
            if (audit.RepositoryVerified)
            {
                sb.AppendLine($"| Repository | nuget.org (Verified) |");
            }

            // PDB section
            sb.AppendLine();
            sb.AppendLine("## PDB");
            sb.AppendLine();
            sb.AppendLine("| Property | Value |");
            sb.AppendLine("| --- | --- |");
            sb.AppendLine($"| Format | {audit.PdbFormat ?? "Unknown"} |");
            sb.AppendLine($"| Location | {audit.PdbLocation ?? "Unknown"} |");
            if (!string.IsNullOrEmpty(audit.SymbolServer))
            {
                sb.AppendLine($"| Server | {audit.SymbolServer} |");
            }
            if (!string.IsNullOrEmpty(audit.PdbPath))
            {
                sb.AppendLine($"| Path | {audit.PdbPath} |");
            }

            if (audit.PdbLocation == null && !string.IsNullOrEmpty(audit.PdbPath))
            {
                sb.AppendLine();
                sb.AppendLine("*Path is from the CodeView record in the assembly; actual PDB location is unknown.*");
            }

            if (audit.WindowsPdbDetected)
            {
                sb.AppendLine();
                sb.AppendLine("**Note:** Windows PDB format is not supported by this tool.");
                sb.AppendLine("Only Portable PDBs (embedded or in .snupkg) can be read.");
                sb.AppendLine("Consider asking the package maintainer to publish Portable PDBs.");
            }

            // Source Coverage section (shown when strict audit was run)
            if (audit.TotalSourceFiles > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Source Coverage");
                sb.AppendLine();

                int accessible = audit.AccessibleSourceFiles + audit.EmbeddedSourceFiles;
                string status = audit.AllSourcesAccessible == true ? "✓" : "✗";
                sb.AppendLine($"**Status:** {status} {accessible}/{audit.TotalSourceFiles} files accessible");

                if (audit.EmbeddedSourceFiles > 0)
                {
                    sb.AppendLine($"**Embedded:** {audit.EmbeddedSourceFiles} files");
                }

                if (audit.MissingSourceFiles is { Count: > 0 })
                {
                    sb.AppendLine();
                    sb.AppendLine("**Missing sources:**");
                    // Show first 10 missing files, truncate if more
                    int shown = 0;
                    foreach (var file in audit.MissingSourceFiles.Take(10))
                    {
                        sb.AppendLine($"- `{file}`");
                        shown++;
                    }
                    if (audit.MissingSourceFiles.Count > 10)
                    {
                        sb.AppendLine($"- ... and {audit.MissingSourceFiles.Count - 10} more");
                    }
                }
            }
        }

        return sb.ToString().TrimEnd();
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
