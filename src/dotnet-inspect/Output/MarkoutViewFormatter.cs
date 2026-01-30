using DotnetInspector.Options;
using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// Renders InspectionResult as section-based Markout with verbosity and filtering.
/// Section filtering is based on H2 boundaries (first H2 = section 1, etc.).
/// </summary>
public class MarkoutViewFormatter
{
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

        // H1 title (always included - before first H2)
        writer.WriteHeading(1, $"{_result.PackageName} {_result.Version}");
        if (!string.IsNullOrWhiteSpace(_result.Description))
            writer.WriteParagraph(_result.Description);

        // Render sections based on verbosity
        switch (_verbosity)
        {
            case Verbosity.Quiet:
                // No H2 sections
                break;
            case Verbosity.Minimal:
                WriteMetadataCompact(writer);
                break;
            case Verbosity.Normal:
                WriteMetadataFull(writer);
                WriteRidPackages(writer);
                WriteRuntimeDeps(writer);
                WriteAuditSummary(writer);
                WriteApiSurface(writer);
                break;
            case Verbosity.Detailed:
                WriteMetadataFull(writer);
                WriteRidPackages(writer);
                WritePackageDeps(writer);
                WriteRuntimeDeps(writer);
                WriteFiles(writer);
                WriteAuditSummary(writer);
                WriteAssemblyAudit(writer);
                WriteApiSurface(writer);
                break;
        }

        return writer.ToString().TrimEnd();
    }

    private void WriteMetadataCompact(MarkoutWriter writer)
    {
        var items = new List<string>();
        items.Add($"Type: {_result.PackageType}");
        if (_result.TargetFrameworkCount > 0) items.Add($"TFMs: {_result.TargetFrameworkCount}");
        items.Add($"RIDs: {_result.SupportedRidCount}");
        if (_result.AssemblyCount > 0) items.Add($"Libraries: {_result.AssemblyCount}");

        writer.WriteParagraph(string.Join(" | ", items));
    }

    private void WriteMetadataFull(MarkoutWriter writer)
    {
        // Top-level metadata as fields (per style guide)
        if (!string.IsNullOrWhiteSpace(_result.Authors))
            writer.WriteField("Authors", _result.Authors);
        if (!string.IsNullOrWhiteSpace(_result.License))
            writer.WriteField("License", _result.License);
        if (!string.IsNullOrWhiteSpace(_result.Repository))
            writer.WriteField("Repository", _result.Repository);
        writer.WriteField("Package Type", _result.PackageType);
        if (!string.IsNullOrWhiteSpace(_result.ContentSummary))
            writer.WriteField("Content", _result.ContentSummary);
        if (_result.TargetFrameworkCount > 0)
            writer.WriteField("Target Frameworks", _result.TargetFrameworkCount);
        writer.WriteField("Runtime Identifiers", _result.SupportedRidCount);
        if (_result.AssemblyCount > 0)
            writer.WriteField("Libraries", _result.AssemblyCount);
        if (_result.HasReadme)
            writer.WriteField("Readme", true);

        // Tool-specific properties
        if (!string.IsNullOrWhiteSpace(_result.ToolCommandsSummary))
            writer.WriteField("Tool Commands", _result.ToolCommandsSummary);

        // Additional properties
        if (_result.IsFrameworkDependent)
            writer.WriteField("Framework Dependent", true);
        if (_result.IsRidSpecificPointerPackage)
            writer.WriteField("RID-Specific Pointer", true);
        if (!string.IsNullOrWhiteSpace(_result.RuntimeTargetRid))
            writer.WriteField("Runtime Target RID", _result.RuntimeTargetRid);
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
            // BoolFormat attributes on the model provide ✓/✗, but we manually format here
            // to control which columns appear in the table
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
        if (_result.AssemblyAudits is not { Count: > 0 })
            return;

        var apis = _result.AssemblyAudits
            .Where(a => a.ApiSurface != null)
            .Select(a => (a.FileName, a.ApiSurface!))
            .ToList();

        if (apis.Count == 0)
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

    private static void WriteRowIfPresent(MarkoutWriter writer, string property, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            writer.WriteTableRow(property, value);
    }
}
