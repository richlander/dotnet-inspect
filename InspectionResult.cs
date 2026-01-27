using System.Text.Json.Serialization;
using MarkOut;

namespace DotnetInspector;

[MarkOutSerializable(TitleProperty = nameof(PackageName), DescriptionProperty = nameof(Description))]
public class InspectionResult
{
    [MarkOutPropertyName("Package")]
    public string PackageName { get; set; } = "";

    public string Version { get; set; } = "";

    [MarkOutIgnore] // Rendered as paragraph via DescriptionProperty
    public string? Description { get; set; }

    public string? Authors { get; set; }
    public string? Repository { get; set; }

    [MarkOutPropertyName("Tool Package")]
    public bool IsToolPackage { get; set; }

    [MarkOutIgnore]
    public List<string>? PackageTypes { get; set; }

    [MarkOutPropertyName("Package Types")]
    [JsonIgnore]
    public string? PackageTypesSummary => PackageTypes is { Count: > 0 }
        ? string.Join(", ", PackageTypes)
        : null;

    [MarkOutIgnore]
    public List<string>? TargetFrameworks { get; set; }

    [MarkOutPropertyName("Target Frameworks")]
    [JsonIgnore]
    public string? TargetFrameworksSummary => TargetFrameworks is { Count: > 0 }
        ? string.Join(", ", TargetFrameworks)
        : null;

    [MarkOutIgnore]
    public List<string>? SupportedRids { get; set; }

    [MarkOutPropertyName("Supported RIDs")]
    [JsonIgnore]
    public string? SupportedRidsSummary => SupportedRids is { Count: > 0 }
        ? string.Join(", ", SupportedRids)
        : null;

    [MarkOutPropertyName("Framework Dependent")]
    public bool IsFrameworkDependent { get; set; }

    [MarkOutPropertyName("RID-Specific Assets")]
    public bool HasRidSpecificAssets { get; set; }

    [MarkOutPropertyName("Native Dependencies")]
    public bool HasNativeDependencies { get; set; }

    // RID-specific tool (DotNetCliTool Version="2") properties
    [MarkOutPropertyName("Tool Format")]
    public string? ToolFormat { get; set; }

    [MarkOutPropertyName("RID-Specific Pointer Package")]
    public bool IsRidSpecificPointerPackage { get; set; }

    [MarkOutIgnore]
    public List<string>? ToolCommands { get; set; }

    [MarkOutPropertyName("Tool Commands")]
    [JsonIgnore]
    public string? ToolCommandsSummary => ToolCommands is { Count: > 0 }
        ? string.Join(", ", ToolCommands)
        : null;

    [MarkOutSection(Name = "RID Packages")]
    public List<RidPackageReference>? RuntimeIdentifierPackages { get; set; }

    [MarkOutPropertyName("Runtime Target RID")]
    public string? RuntimeTargetRid { get; set; }

    [MarkOutIgnore]
    public List<string>? NativeFiles { get; set; }

    [MarkOutPropertyName("Native Files")]
    [JsonIgnore]
    public string? NativeFilesSummary => NativeFiles is { Count: > 0 }
        ? string.Join(", ", NativeFiles)
        : null;

    [MarkOutIgnore]
    public List<DependencyGroup>? DependencyGroups { get; set; }

    [MarkOutSection(Name = "Package Dependencies")]
    [JsonIgnore]
    public List<FlatDependency>? FlatDependencies => DependencyGroups?
        .OrderBy(g => GetTfmSortOrder(g.TargetFramework))
        .ThenBy(g => g.TargetFramework)
        .SelectMany(g => g.Dependencies
            .OrderBy(d => d.Id)
            .Select(d => new FlatDependency
            {
                TargetFramework = g.TargetFramework,
                Id = d.Id,
                Version = d.Version
            }))
        .ToList();

    private static int GetTfmSortOrder(string tfm)
    {
        // Sort order: netstandard → netframework → netcoreapp → net (modern)
        if (tfm.StartsWith(".NETStandard", StringComparison.OrdinalIgnoreCase) ||
            tfm.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (tfm.StartsWith(".NETFramework", StringComparison.OrdinalIgnoreCase) ||
            tfm.StartsWith("net4", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (tfm.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase))
            return 2;
        // Modern .NET (net5.0+)
        return 3;
    }

    [MarkOutSection(Name = "Runtime Dependencies")]
    public List<PackageDependency>? RuntimeDependencies { get; set; }

    [MarkOutSection(Name = "Audit Summary")]
    public AuditSummary? AuditSummary { get; set; }

    [MarkOutSection(Name = "Assembly Audit")]
    public List<AssemblyAudit>? AssemblyAudits { get; set; }
}
