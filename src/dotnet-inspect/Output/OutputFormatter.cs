using System.Text;
using System.Text.Json;
using DotnetInspector.Options;

namespace DotnetInspector.Output;

/// <summary>
/// Handles output formatting for inspection results.
/// </summary>
public static class OutputFormatter
{
    public static void WriteResult(InspectionResult result, InspectionOptions options)
    {
        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonContext.Default.InspectionResult));
        }
        else
        {
            var formatter = new MarkoutViewFormatter(result, options);
            Console.WriteLine(formatter.Render());
        }
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
            sb.AppendLine("|----------|-------|");

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
            sb.AppendLine("|-------|--------|");
            sb.AppendLine($"| Deterministic | {(audit.IsDeterministic ? "✓" : "✗")} |");
            sb.AppendLine($"| Reproducible Flag | {(audit.HasReproducibleFlag ? "✓" : "✗")} |");
            sb.AppendLine($"| SourceLink | {(audit.HasSourceLink ? "✓" : "✗")} |");

            // PDB section
            sb.AppendLine();
            sb.AppendLine("## PDB");
            sb.AppendLine();
            sb.AppendLine("| Property | Value |");
            sb.AppendLine("|----------|-------|");
            sb.AppendLine($"| Format | {audit.PdbFormat ?? "Unknown"} |");
            sb.AppendLine($"| Location | {audit.PdbLocation ?? "Unknown"} |");
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
        }

        return sb.ToString().TrimEnd();
    }
}
