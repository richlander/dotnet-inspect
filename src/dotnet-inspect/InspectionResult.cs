using System.Text.Json.Serialization;
using Markout;

namespace DotnetInspector;

[MarkoutSerializable(TitleProperty = nameof(PackageName), TitleContextProperty = nameof(Version), DescriptionProperty = nameof(Description))]
public class InspectionResult
{
    [MarkoutPropertyName("Package")]
    [MarkoutIgnore] // Rendered as title
    public string PackageName { get; set; } = "";

    [MarkoutIgnore] // Rendered as title context
    public string Version { get; set; } = "";

    [MarkoutIgnore] // Rendered as paragraph via DescriptionProperty
    public string? Description { get; set; }

    public string? Authors { get; set; }
    public string? License { get; set; }
    public string? Repository { get; set; }

    /// <summary>
    /// Indicates whether the package contains a README.md file.
    /// </summary>
    public bool HasReadme { get; set; }

    [MarkoutIgnore] // Used internally; PackageType computed property is displayed
    public bool IsToolPackage { get; set; }

    [MarkoutIgnore]
    public List<string>? PackageTypes { get; set; }

    [MarkoutPropertyName("Package Types")]
    [JsonIgnore]
    public string? PackageTypesSummary => PackageTypes is { Count: > 0 }
        ? string.Join(", ", PackageTypes)
        : null;

    /// <summary>
    /// Computed package type: Library, Tool, or Tool v2.
    /// </summary>
    [MarkoutPropertyName("Package Type")]
    [JsonIgnore]
    public string PackageType => ToolFormat?.Contains("Version=\"2\"") == true
        ? "Tool v2"
        : IsToolPackage ? "Tool" : "Library";

    [MarkoutIgnore]
    public List<string>? ContentDirectories { get; set; }

    [MarkoutPropertyName("Content")]
    [JsonIgnore]
    public string? ContentSummary => ContentDirectories is { Count: > 0 }
        ? string.Join(", ", ContentDirectories)
        : null;

    [MarkoutIgnore]
    public List<string>? TargetFrameworks { get; set; }

    [MarkoutPropertyName("Target Frameworks")]
    [JsonIgnore]
    public string? TargetFrameworksSummary => TargetFrameworks is { Count: > 0 }
        ? string.Join(", ", TargetFrameworks)
        : null;

    [MarkoutPropertyName("Target Frameworks")]
    [JsonIgnore]
    public int TargetFrameworkCount => TargetFrameworks?.Count ?? 0;

    [MarkoutIgnore]
    public List<string>? SupportedRids { get; set; }

    [MarkoutPropertyName("Supported RIDs")]
    [JsonIgnore]
    public string? SupportedRidsSummary => SupportedRids is { Count: > 0 }
        ? string.Join(", ", SupportedRids)
        : null;

    [MarkoutPropertyName("Runtime Identifiers")]
    [JsonIgnore]
    public int SupportedRidCount => SupportedRids?.Count ?? 0;

    /// <summary>
    /// Total number of library assemblies (DLLs) in the package, excluding resource assemblies.
    /// </summary>
    [MarkoutPropertyName("Libraries")]
    public int AssemblyCount { get; set; }

    [MarkoutPropertyName("Framework Dependent")]
    public bool IsFrameworkDependent { get; set; }

    [MarkoutPropertyName("RID-Specific Assets")]
    public bool HasRidSpecificAssets { get; set; }

    [MarkoutPropertyName("Native Dependencies")]
    public bool HasNativeDependencies { get; set; }

    // RID-specific tool (DotNetCliTool Version="2") properties
    [MarkoutPropertyName("Tool Format")]
    public string? ToolFormat { get; set; }

    [MarkoutPropertyName("RID-Specific Pointer Package")]
    public bool IsRidSpecificPointerPackage { get; set; }

    [MarkoutIgnore]
    public List<string>? ToolCommands { get; set; }

    [MarkoutPropertyName("Tool Commands")]
    [JsonIgnore]
    public string? ToolCommandsSummary => ToolCommands is { Count: > 0 }
        ? string.Join(", ", ToolCommands)
        : null;

    [MarkoutSection(Name = "RID Packages")]
    public List<RidPackageReference>? RuntimeIdentifierPackages { get; set; }

    [MarkoutPropertyName("Runtime Target RID")]
    public string? RuntimeTargetRid { get; set; }

    [MarkoutIgnore]
    public List<string>? NativeFiles { get; set; }

    [MarkoutPropertyName("Native Files")]
    [JsonIgnore]
    public string? NativeFilesSummary => NativeFiles is { Count: > 0 }
        ? string.Join(", ", NativeFiles)
        : null;

    [MarkoutIgnore]
    public List<DependencyGroup>? DependencyGroups { get; set; }

    [MarkoutSection(Name = "Package Dependencies")]
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

    [MarkoutSection(Name = "Runtime Dependencies")]
    public List<PackageDependency>? RuntimeDependencies { get; set; }

    /// <summary>
    /// List of files in the package (DLLs from lib/tools, or all files with --all).
    /// </summary>
    [MarkoutIgnore]
    public List<string>? Files { get; set; }

    [MarkoutSection(Name = "Audit Summary")]
    public AuditSummary? AuditSummary { get; set; }

    [MarkoutSection(Name = "Assembly Audit")]
    public List<AssemblyAudit>? AssemblyAudits { get; set; }
}
