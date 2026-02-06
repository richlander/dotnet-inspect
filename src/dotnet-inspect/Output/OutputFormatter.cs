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
        var writer = new MarkoutWriter();

        // Header
        writer.WriteHeading(1, audit.FileName);

        // Assembly Info
        if (audit.AssemblyInfo != null)
        {
            var info = audit.AssemblyInfo;
            writer.WriteHeading(2, "Assembly Info");

            var infoFields = new List<string[]>();
            if (!string.IsNullOrEmpty(info.AssemblyName))
                infoFields.Add(new[] { "Name", info.AssemblyName });
            if (!string.IsNullOrEmpty(info.AssemblyVersion))
                infoFields.Add(new[] { "Version", info.AssemblyVersion });
            if (!string.IsNullOrEmpty(info.TargetFramework))
                infoFields.Add(new[] { "Target Framework", info.TargetFramework });
            if (!string.IsNullOrEmpty(info.Architecture))
                infoFields.Add(new[] { "Architecture", info.Architecture });
            if (!string.IsNullOrEmpty(info.CompilationType))
                infoFields.Add(new[] { "Compilation", info.CompilationType });
            if (!string.IsNullOrEmpty(info.InformationalVersion))
                infoFields.Add(new[] { "Informational Version", info.InformationalVersion });
            if (!string.IsNullOrEmpty(info.Product))
                infoFields.Add(new[] { "Product", info.Product });
            if (!string.IsNullOrEmpty(info.Company))
                infoFields.Add(new[] { "Company", info.Company });
            if (!string.IsNullOrEmpty(info.Copyright))
                infoFields.Add(new[] { "Copyright", info.Copyright });
            if (info.IsSigned)
                infoFields.Add(new[] { "Signed", "Yes" });
            if (!string.IsNullOrEmpty(info.PublicKeyToken))
                infoFields.Add(new[] { "Public Key Token", info.PublicKeyToken });

            writer.WriteTable(new[] { "Property", "Value" }, infoFields);

            // Assembly References section
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

        // Audit section (if --audit was specified)
        if (options.IncludeAudit)
        {
            writer.WriteHeading(2, "Build Audit");

            // Show fields before the table
            if (!string.IsNullOrEmpty(audit.RepositoryUrl))
            {
                writer.WriteField("Repository", audit.RepositoryUrl);
            }

            if (audit.NonNormalizedPaths is { Count: > 0 })
            {
                writer.WriteArray("Non-normalized paths", audit.NonNormalizedPaths);
            }

            // Build the checkmark table
            var auditRows = new List<string[]>
            {
                new[] { "Deterministic", audit.IsDeterministic ? "✓" : "✗" },
                new[] { "Reproducible Flag", audit.HasReproducibleFlag ? "✓" : "✗" },
                new[] { "SourceLink", audit.SourceLinkStatus ?? "Unknown" }
            };
            if (!string.IsNullOrEmpty(audit.Builder))
            {
                auditRows.Add(new[] { "Builder", audit.Builder });
            }
            if (!string.IsNullOrEmpty(audit.Publisher))
            {
                var publisherStatus = audit.PublisherVerified ? "(Verified)" : "";
                auditRows.Add(new[] { "Publisher", $"{audit.Publisher} {publisherStatus}" });
            }
            else if (!string.IsNullOrEmpty(audit.SignatureStatus))
            {
                auditRows.Add(new[] { "Publisher", audit.SignatureStatus });
            }
            if (audit.RepositoryVerified)
            {
                auditRows.Add(new[] { "Repository", "nuget.org (Verified)" });
            }

            writer.WriteTable(new[] { "Check", "Status" }, auditRows);

            // PDB section
            writer.WriteHeading(2, "PDB");

            var pdbRows = new List<string[]>
            {
                new[] { "Format", audit.PdbFormat ?? "Unknown" },
                new[] { "Location", audit.PdbLocation ?? "Unknown" }
            };
            if (!string.IsNullOrEmpty(audit.SymbolServer))
            {
                pdbRows.Add(new[] { "Server", audit.SymbolServer });
            }
            if (!string.IsNullOrEmpty(audit.PdbPath))
            {
                pdbRows.Add(new[] { "Path", audit.PdbPath });
            }
            writer.WriteTable(new[] { "Property", "Value" }, pdbRows);

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

            // Source Coverage section (shown when strict audit was run)
            if (audit.TotalSourceFiles > 0)
            {
                writer.WriteHeading(2, "Source Coverage");

                int accessible = audit.AccessibleSourceFiles + audit.EmbeddedSourceFiles;
                string status = audit.AllSourcesAccessible == true ? "✓" : "✗";
                writer.WriteField("Status", $"{status} {accessible}/{audit.TotalSourceFiles} files accessible");

                if (audit.EmbeddedSourceFiles > 0)
                {
                    writer.WriteField("Embedded", $"{audit.EmbeddedSourceFiles} files");
                }

                if (audit.MissingSourceFiles is { Count: > 0 })
                {
                    // Show first 10 missing files, truncate if more
                    var displayFiles = audit.MissingSourceFiles.Take(10).Select(f => $"`{f}`").ToList();
                    if (audit.MissingSourceFiles.Count > 10)
                    {
                        displayFiles.Add($"... and {audit.MissingSourceFiles.Count - 10} more");
                    }
                    writer.WriteArray("Missing sources", displayFiles);
                }
            }
        }

        return writer.ToString().TrimEnd();
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
