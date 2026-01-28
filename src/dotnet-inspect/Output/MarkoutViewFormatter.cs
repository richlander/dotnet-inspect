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
            ExcludeSections = _excludeSections
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
                WritePackageDeps(writer);
                WriteRuntimeDeps(writer);
                WriteAuditSummary(writer);
                WriteApiSurface(writer);
                break;
            case Verbosity.Detailed:
                WriteMetadataFull(writer);
                WriteRidPackages(writer);
                WritePackageDeps(writer);
                WriteRuntimeDeps(writer);
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
        if (_result.IsToolPackage) items.Add("Tool Package");
        if (!string.IsNullOrWhiteSpace(_result.TargetFrameworksSummary)) items.Add($"TFMs: {_result.TargetFrameworksSummary}");
        if (!string.IsNullOrWhiteSpace(_result.SupportedRidsSummary)) items.Add($"RIDs: {_result.SupportedRidsSummary}");

        if (items.Count == 0) return;

        writer.WriteHeading(2, "Metadata");
        writer.WriteParagraph(string.Join(" | ", items));
    }

    private void WriteMetadataFull(MarkoutWriter writer)
    {
        writer.WriteHeading(2, "Metadata");
        writer.WriteTableStart("Property", "Value");

        WriteRowIfPresent(writer, "Authors", _result.Authors);
        WriteRowIfPresent(writer, "Repository", _result.Repository);
        writer.WriteTableRow("Tool Package", _result.IsToolPackage ? "Yes" : "No");
        WriteRowIfPresent(writer, "Package Types", _result.PackageTypesSummary);
        WriteRowIfPresent(writer, "Target Frameworks", _result.TargetFrameworksSummary);
        WriteRowIfPresent(writer, "Supported RIDs", _result.SupportedRidsSummary);
        writer.WriteTableRow("Framework Dependent", _result.IsFrameworkDependent ? "Yes" : "No");
        writer.WriteTableRow("RID-Specific Assets", _result.HasRidSpecificAssets ? "Yes" : "No");
        writer.WriteTableRow("Native Dependencies", _result.HasNativeDependencies ? "Yes" : "No");

        if (!string.IsNullOrWhiteSpace(_result.ToolFormat))
            writer.WriteTableRow("Tool Format", _result.ToolFormat);
        if (_result.IsRidSpecificPointerPackage)
            writer.WriteTableRow("RID-Specific Pointer", "Yes");
        if (!string.IsNullOrWhiteSpace(_result.ToolCommandsSummary))
            writer.WriteTableRow("Tool Commands", _result.ToolCommandsSummary);
        if (!string.IsNullOrWhiteSpace(_result.RuntimeTargetRid))
            writer.WriteTableRow("Runtime Target RID", _result.RuntimeTargetRid);
        if (!string.IsNullOrWhiteSpace(_result.NativeFilesSummary))
            writer.WriteTableRow("Native Files", _result.NativeFilesSummary);

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
