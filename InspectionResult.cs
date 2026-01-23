using System.Text.Json.Serialization;
using MarkdownData;

namespace DotnetInspector;

[MdfSerializable(TitleProperty = nameof(PackageName), DescriptionProperty = nameof(Description))]
public class InspectionResult
{
    [MdfPropertyName("Package")]
    public string PackageName { get; set; } = "";

    public string Version { get; set; } = "";

    [MdfIgnore] // Rendered as paragraph via DescriptionProperty
    public string? Description { get; set; }

    public string? Authors { get; set; }
    public string? Repository { get; set; }

    [MdfPropertyName("Tool Package")]
    public bool IsToolPackage { get; set; }

    [MdfIgnore]
    public List<string>? PackageTypes { get; set; }

    [MdfPropertyName("Package Types")]
    [JsonIgnore]
    public string? PackageTypesSummary => PackageTypes is { Count: > 0 }
        ? string.Join(", ", PackageTypes)
        : null;

    [MdfIgnore]
    public List<string>? TargetFrameworks { get; set; }

    [MdfPropertyName("Target Frameworks")]
    [JsonIgnore]
    public string? TargetFrameworksSummary => TargetFrameworks is { Count: > 0 }
        ? string.Join(", ", TargetFrameworks)
        : null;

    [MdfIgnore]
    public List<string>? SupportedRids { get; set; }

    [MdfPropertyName("Supported RIDs")]
    [JsonIgnore]
    public string? SupportedRidsSummary => SupportedRids is { Count: > 0 }
        ? string.Join(", ", SupportedRids)
        : null;

    [MdfPropertyName("Framework Dependent")]
    public bool IsFrameworkDependent { get; set; }

    [MdfPropertyName("RID-Specific Assets")]
    public bool HasRidSpecificAssets { get; set; }

    [MdfPropertyName("Native Dependencies")]
    public bool HasNativeDependencies { get; set; }

    // RID-specific tool (DotNetCliTool Version="2") properties
    [MdfPropertyName("Tool Format")]
    public string? ToolFormat { get; set; }

    [MdfPropertyName("RID-Specific Pointer Package")]
    public bool IsRidSpecificPointerPackage { get; set; }

    [MdfIgnore]
    public List<string>? ToolCommands { get; set; }

    [MdfPropertyName("Tool Commands")]
    [JsonIgnore]
    public string? ToolCommandsSummary => ToolCommands is { Count: > 0 }
        ? string.Join(", ", ToolCommands)
        : null;

    [MdfSection(Name = "RID Packages")]
    public List<RidPackageReference>? RuntimeIdentifierPackages { get; set; }

    [MdfPropertyName("Runtime Target RID")]
    public string? RuntimeTargetRid { get; set; }

    [MdfIgnore]
    public List<string>? NativeFiles { get; set; }

    [MdfPropertyName("Native Files")]
    [JsonIgnore]
    public string? NativeFilesSummary => NativeFiles is { Count: > 0 }
        ? string.Join(", ", NativeFiles)
        : null;

    [MdfIgnore]
    public List<DependencyGroup>? DependencyGroups { get; set; }

    [MdfSection(Name = "Package Dependencies")]
    [JsonIgnore]
    public List<FlatDependency>? FlatDependencies => DependencyGroups?
        .SelectMany(g => g.Dependencies.Select(d => new FlatDependency
        {
            TargetFramework = g.TargetFramework,
            Id = d.Id,
            Version = d.Version
        }))
        .ToList();

    [MdfSection(Name = "Runtime Dependencies")]
    public List<PackageDependency>? RuntimeDependencies { get; set; }

    [MdfSection(Name = "Audit Summary")]
    public AuditSummary? AuditSummary { get; set; }

    [MdfSection(Name = "Assembly Audit")]
    public List<AssemblyAudit>? AssemblyAudits { get; set; }
}
