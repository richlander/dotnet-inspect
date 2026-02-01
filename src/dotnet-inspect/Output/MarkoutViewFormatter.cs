using DotnetInspector.Options;
using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// Renders InspectionResult as section-based Markout with verbosity and filtering.
/// Section filtering is based on H2 boundaries (first H2 = section 1, etc.).
/// </summary>
public class MarkoutViewFormatter
{
    /// <summary>
    /// Section names in order for package command.
    /// Always-present first, then detailed-only, then conditional.
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

    private readonly InspectionResult _result;
    private readonly Verbosity _verbosity;
    private readonly HashSet<int>? _includeSections;
    private readonly HashSet<int>? _excludeSections;

    public MarkoutViewFormatter(InspectionResult result, InspectionOptions options)
    {
        _result = result;
        _verbosity = options.Verbosity;
        _includeSections = options.IncludeSections;
        _excludeSections = options.ExcludeSections;
    }

    public string Render()
    {
        var writer = new MarkoutWriter
        {
            IncludeSections = _includeSections,
            ExcludeSections = _excludeSections,
            BoldFieldNames = true
        };

        // H1 title (always included)
        writer.WriteHeading(1, $"{_result.PackageName} {_result.Version}");

        // Render based on verbosity
        switch (_verbosity)
        {
            case Verbosity.Quiet:
                // H1 + compact line only
                WriteCompactLine(writer);
                break;
            case Verbosity.Minimal:
                // H1 + description + compact line
                if (!string.IsNullOrWhiteSpace(_result.Description))
                    writer.WriteParagraph(_result.Description);
                WriteCompactLine(writer);
                break;
            case Verbosity.Normal:
            case Verbosity.Detailed:
                // All sections in fixed order
                if (!string.IsNullOrWhiteSpace(_result.Description))
                    writer.WriteParagraph(_result.Description);
                WriteCompactLine(writer);
                
                // Section 1: Metadata (always)
                WriteMetadataSection(writer);
                
                // Sections 2-4: Detailed only
                if (_verbosity == Verbosity.Detailed)
                {
                    WriteStatistics(writer);
                    WritePackageDeps(writer);
                    WriteFiles(writer);
                }
                
                // Sections 5-7: Conditional (only when data exists)
                WriteVulnerabilities(writer);
                WriteRidPackages(writer);
                WriteRuntimeDeps(writer);
                break;
        }

        return writer.ToString().TrimEnd();
    }

    /// <summary>
    /// Writes the compact triage line with essential fields.
    /// Format: Type: Library | TFM: net10.0 | Updated: 2026-01-13 | Vulnerabilities: 1
    /// </summary>
    private void WriteCompactLine(MarkoutWriter writer)
    {
        var items = new List<string>();
        items.Add($"Type: {_result.PackageType}");
        if (!string.IsNullOrEmpty(_result.NewestTfm))
            items.Add($"TFM: {_result.NewestTfm}");
        if (!string.IsNullOrEmpty(_result.PublishedDisplay))
            items.Add($"Updated: {_result.PublishedDisplay}");
        if (_result.Deprecation != null)
            items.Add("Deprecated: Yes");
        if (_result.Vulnerabilities is { Count: > 0 })
            items.Add($"Vulnerabilities: {_result.Vulnerabilities.Count}");

        writer.WriteParagraph(string.Join(" | ", items));
    }

    /// <summary>
    /// Writes the Metadata section as a property/value table.
    /// </summary>
    private void WriteMetadataSection(MarkoutWriter writer)
    {
        // Collect metadata rows
        var rows = new List<(string Property, string Value)>();

        // Include compact line fields first (for consistency with compact line display)
        rows.Add(("Type", _result.PackageType));
        if (!string.IsNullOrEmpty(_result.NewestTfm))
            rows.Add(("Newest TFM", _result.NewestTfm));
        if (!string.IsNullOrEmpty(_result.PublishedDisplay))
            rows.Add(("Updated", _result.PublishedDisplay));
        if (_result.Deprecation != null)
        {
            rows.Add(("Deprecated", "Yes"));
            if (!string.IsNullOrEmpty(_result.Deprecation.Summary))
                rows.Add(("Deprecated Note", _result.Deprecation.Summary));
        }
        if (_result.Vulnerabilities is { Count: > 0 })
            rows.Add(("Vulnerabilities", _result.Vulnerabilities.Count.ToString()));

        if (!string.IsNullOrWhiteSpace(_result.Authors))
            rows.Add(("Authors", _result.Authors));
        if (!string.IsNullOrWhiteSpace(_result.OwnersDisplay) && _result.OwnersDisplay != _result.Authors)
            rows.Add(("Owners", _result.OwnersDisplay));
        if (!string.IsNullOrWhiteSpace(_result.License))
            rows.Add(("License", _result.License));
        if (!string.IsNullOrWhiteSpace(_result.Repository))
            rows.Add(("Repository", _result.Repository));
        
        // NuGet metadata
        if (_result.IsVerified == true)
            rows.Add(("Verified", "yes"));
        
        if (!string.IsNullOrWhiteSpace(_result.ContentSummary))
            rows.Add(("Content", _result.ContentSummary));
        if (_result.TargetFrameworkCount > 0)
            rows.Add(("Target Frameworks", _result.TargetFrameworkCount.ToString()));
        if (_result.SupportedRidCount > 0)
            rows.Add(("Runtime Identifiers", _result.SupportedRidCount.ToString()));
        if (_result.AssemblyCount > 0)
            rows.Add(("Libraries", _result.AssemblyCount.ToString()));
        if (_result.HasReadme)
            rows.Add(("Readme", "yes"));

        // Tool-specific properties
        if (!string.IsNullOrWhiteSpace(_result.ToolCommandsSummary))
            rows.Add(("Tool Commands", _result.ToolCommandsSummary));

        // Additional properties
        if (_result.IsFrameworkDependent)
            rows.Add(("Framework Dependent", "yes"));
        if (_result.IsRidSpecificPointerPackage)
            rows.Add(("RID-Specific Pointer", "yes"));
        if (!string.IsNullOrWhiteSpace(_result.RuntimeTargetRid))
            rows.Add(("Runtime Target RID", _result.RuntimeTargetRid));

        // Only write section if we have rows
        if (rows.Count == 0)
            return;

        writer.WriteHeading(2, "Metadata");
        writer.WriteTableStart("Property", "Value");

        foreach (var (property, value) in rows)
            writer.WriteTableRow(property, value);

        writer.WriteTableEnd();
    }

    /// <summary>
    /// Writes the Statistics section with download and size data.
    /// </summary>
    private void WriteStatistics(MarkoutWriter writer)
    {
        var rows = new List<(string Property, string Value)>();

        if (!string.IsNullOrEmpty(_result.DownloadsDisplay))
            rows.Add(("Total Downloads", _result.DownloadsDisplay));
        if (!string.IsNullOrEmpty(_result.VersionDownloadsDisplay))
            rows.Add(("Version Downloads", _result.VersionDownloadsDisplay));
        if (_result.VersionCount.HasValue)
            rows.Add(("Version Count", _result.VersionCount.Value.ToString()));
        if (!string.IsNullOrEmpty(_result.PackageSizeDisplay))
            rows.Add(("Package Size", _result.PackageSizeDisplay));

        if (rows.Count == 0)
            return;

        writer.WriteHeading(2, "Statistics");
        writer.WriteTableStart("Property", "Value");

        foreach (var (property, value) in rows)
            writer.WriteTableRow(property, value);

        writer.WriteTableEnd();
    }

    private void WriteVulnerabilities(MarkoutWriter writer)
    {
        if (_result.Vulnerabilities is not { Count: > 0 })
            return;

        writer.WriteHeading(2, "Vulnerabilities");
        writer.WriteTableStart("Severity", "CVE", "Summary");

        foreach (var vuln in _result.Vulnerabilities)
        {
            var cve = vuln.CveId ?? vuln.GhsaId ?? "";
            // Escape pipes in summary for markdown table
            var summary = (vuln.Summary ?? "").Replace("|", "-");
            writer.WriteTableRow(vuln.Severity, cve, summary);
        }

        writer.WriteTableEnd();
    }

    private void WriteRidPackages(MarkoutWriter writer)
    {
        if (_result.RuntimeIdentifierPackages is not { Count: > 0 })
            return;

        writer.WriteHeading(2, "RID Packages");
        writer.WriteTableStart("RID", "Package", "Available");

        foreach (var pkg in _result.RuntimeIdentifierPackages)
            writer.WriteTableRow(pkg.RuntimeIdentifier, pkg.PackageId, pkg.AvailableDisplay);

        writer.WriteTableEnd();
    }

    private void WritePackageDeps(MarkoutWriter writer)
    {
        if (_result.FlatDependencies is not { Count: > 0 })
            return;

        writer.WriteHeading(2, "Package Dependencies");
        writer.WriteTableStart("Target Framework", "Package", "Version");

        foreach (var dep in _result.FlatDependencies)
            writer.WriteTableRow(dep.TargetFramework, dep.Id, dep.Version);

        writer.WriteTableEnd();
    }

    private void WriteRuntimeDeps(MarkoutWriter writer)
    {
        if (_result.RuntimeDependencies is not { Count: > 0 })
            return;

        writer.WriteHeading(2, "Runtime Dependencies");
        writer.WriteTableStart("Package", "Version");

        foreach (var dep in _result.RuntimeDependencies)
            writer.WriteTableRow(dep.Id, dep.Version);

        writer.WriteTableEnd();
    }

    private void WriteFiles(MarkoutWriter writer)
    {
        if (_result.Files is not { Count: > 0 })
            return;

        writer.WriteHeading(2, "Files");
        writer.WriteTableStart("Path");

        foreach (var file in _result.Files)
            writer.WriteTableRow(file);

        writer.WriteTableEnd();
    }

    private void WriteAuditSummary(MarkoutWriter writer)
    {
        if (_result.AuditSummary is null)
            return;

        writer.WriteHeading(2, "Audit Summary");

        var summary = _result.AuditSummary;
        writer.WriteParagraph($"**Assemblies:** {summary.TotalAssemblies} total, {summary.DeterministicCount} deterministic, {summary.SourceLinkCount} with SourceLink, {summary.EmbeddedPdbCount} embedded PDB");

        if (summary.AllDeterministic && summary.AllHaveSourceLink)
            writer.WriteParagraph("✓ All assemblies are deterministic with SourceLink");
    }

    private void WriteAssemblyAudit(MarkoutWriter writer)
    {
        if (_result.AssemblyAudits is not { Count: > 0 })
            return;

        writer.WriteHeading(2, "Assembly Audit");
        writer.WriteTableStart("File", "Type", "Deterministic", "SourceLink", "Embedded PDB");

        foreach (var audit in _result.AssemblyAudits)
        {
            writer.WriteTableRow(
                audit.FileName,
                audit.FileType,
                audit.IsDeterministic ? "✓" : "✗",
                audit.HasSourceLink ? "✓" : "✗",
                audit.HasEmbeddedPdb ? "✓" : "✗");
        }

        writer.WriteTableEnd();
    }

    private void WriteApiSurface(MarkoutWriter writer)
    {
        var apis = _result.AssemblyAudits?
            .Where(a => a.ApiSurface != null)
            .Select(a => (a.FileName, a.ApiSurface!))
            .ToList();

        if (apis is not { Count: > 0 })
            return;

        writer.WriteHeading(2, "API Surface");

        foreach (var (fileName, api) in apis)
        {
            writer.WriteHeading(3, fileName);
            writer.WriteParagraph($"**{api.PublicTypeCount}** types, **{api.PublicMethodCount}** methods, **{api.PublicPropertyCount}** properties");

            if (api.Types.Count > 0)
            {
                writer.WriteTableStart("Type", "Kind", "Members");

                foreach (var type in api.Types)
                {
                    var memberCount = type.Members?.Count ?? 0;
                    var fullName = string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";
                    writer.WriteTableRow(fullName, type.Kind, memberCount.ToString());
                }

                writer.WriteTableEnd();
            }
        }
    }
}
