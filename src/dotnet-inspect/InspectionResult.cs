using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using Markout;

namespace DotnetInspector;

[MarkoutSerializable(
    TitleProperty = nameof(PackageName), 
    TitleContextProperty = nameof(Version), 
    DescriptionProperty = nameof(Description),
    AutoFields = false)]
public class InspectionResult
{
    [MarkoutPropertyName("Package")]
    public string PackageName { get; set; } = "";

    public string Version { get; set; } = "";

    public string? Description { get; set; }

    // ===== Field Collections for Serializer =====
    // Note: Property order matters for serialization output

    /// <summary>
    /// Compact summary line (rendered as pipe-separated fields).
    /// </summary>
    [JsonIgnore]
    [MarkoutIgnoreInTable]
    public List<MarkoutField> Summary => GetCompactFields();

    /// <summary>
    /// Metadata section (rendered as Property/Value table).
    /// </summary>
    [MarkoutSection(Name = "Package")]
    [JsonIgnore]
    public List<MarkoutField> Metadata => GetMetadataFields();

    /// <summary>
    /// Statistics section (rendered as Property/Value table).
    /// </summary>
    [MarkoutSection(Name = "Statistics")]
    [JsonIgnore]
    public List<MarkoutField> Statistics => GetStatisticsFields();

    public string? Authors { get; set; }
    public string? License { get; set; }
    public string? Repository { get; set; }

    /// <summary>
    /// When this package version was published to NuGet.
    /// </summary>
    [MarkoutFormat("yyyy-MM-dd")]
    [MarkoutPropertyName("Updated")]
    public DateTimeOffset? Published { get; set; }

    /// <summary>
    /// Total downloads across all versions of the package.
    /// </summary>
    [MarkoutValueFormatter(typeof(Output.CompactNumberFormatter))]
    [MarkoutPropertyName("Downloads")]
    public long? TotalDownloads { get; set; }

    /// <summary>
    /// Downloads for this specific version.
    /// </summary>
    [MarkoutValueFormatter(typeof(Output.CompactNumberFormatter))]
    [MarkoutPropertyName("Version Downloads")]
    public long? VersionDownloads { get; set; }

    /// <summary>
    /// Total number of versions published for this package.
    /// </summary>
    public int? VersionCount { get; set; }

    /// <summary>
    /// Size of the .nupkg file in bytes.
    /// </summary>
    [MarkoutValueFormatter(typeof(Output.ByteSizeFormatter))]
    [MarkoutPropertyName("Package Size")]
    public long? PackageSize { get; set; }

    /// <summary>
    /// Whether the package owner is verified by NuGet.org.
    /// </summary>
    public bool? IsVerified { get; set; }

    /// <summary>
    /// Package owners (from NuGet.org).
    /// </summary>
    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Owners")]
    public List<string>? Owners { get; set; }

    /// <summary>
    /// Deprecation information if the package is deprecated.
    /// </summary>
    [MarkoutIgnoreInTable]
    public PackageDeprecation? Deprecation { get; set; }

    /// <summary>
    /// Known security vulnerabilities for this package version.
    /// </summary>
    [MarkoutSection(Name = "Vulnerabilities")]
    [MarkoutIgnoreInTable]
    public List<PackageVulnerability>? Vulnerabilities { get; set; }

    /// <summary>
    /// Summary of vulnerabilities for display.
    /// </summary>
    [MarkoutPropertyName("Vulnerabilities")]
    [JsonIgnore]
    public string? VulnerabilitiesDisplay => Vulnerabilities is { Count: > 0 }
        ? $"{Vulnerabilities.Count} known ({string.Join(", ", Vulnerabilities.Select(v => v.Severity).Distinct())})"
        : null;

    /// <summary>
    /// Indicates whether the package contains a README.md file.
    /// </summary>
    [MarkoutSkipDefault]
    public bool HasReadme { get; set; }

    /// <summary>
    /// Path to the readme file within the package (from nuspec readme element).
    /// </summary>
    [JsonIgnore]
    public string? ReadmeFile { get; set; }

    [MarkoutSkipDefault]
    public bool IsToolPackage { get; set; }

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Package Types")]
    public List<string>? PackageTypes { get; set; }

    /// <summary>
    /// Computed package type: Library, Tool, or Tool v2.
    /// </summary>
    [MarkoutPropertyName("Package Type")]
    [JsonIgnore]
    public string PackageType => ToolFormat?.Contains("Version=\"2\"") == true
        ? "Tool v2"
        : IsToolPackage ? "Tool" : "Library";

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Content")]
    public List<string>? ContentDirectories { get; set; }

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Target Frameworks")]
    public List<string>? TargetFrameworks { get; set; }

    [MarkoutPropertyName("Target Frameworks")]
    [JsonIgnore]
    public int TargetFrameworkCount => TargetFrameworks?.Count ?? 0;

    /// <summary>
    /// The newest/highest target framework in the package (computed from TargetFrameworks).
    /// </summary>
    [MarkoutPropertyName("Newest TFM")]
    [JsonIgnore]
    public string? NewestTfm => TargetFrameworks is { Count: > 0 }
        ? GetNewestTfm(TargetFrameworks)
        : null;

    private static string GetNewestTfm(List<string> tfms)
    {
        return tfms
            .OrderByDescending(Packages.TfmResolver.GetTfmPriority)
            .First();
    }

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Supported RIDs")]
    public List<string>? SupportedRids { get; set; }

    [MarkoutPropertyName("Runtime Identifiers")]
    [JsonIgnore]
    public int SupportedRidCount => SupportedRids?.Count ?? 0;

    /// <summary>
    /// Total number of library assemblies (DLLs) in the package, excluding resource assemblies.
    /// </summary>
    [MarkoutPropertyName("Libraries")]
    public int AssemblyCount { get; set; }

    [MarkoutPropertyName("Framework Dependent")]
    [MarkoutSkipDefault]
    public bool IsFrameworkDependent { get; set; }

    [MarkoutPropertyName("RID-Specific Assets")]
    [MarkoutSkipDefault]
    public bool HasRidSpecificAssets { get; set; }

    [MarkoutPropertyName("Native Dependencies")]
    [MarkoutSkipDefault]
    public bool HasNativeDependencies { get; set; }

    // RID-specific tool (DotNetCliTool Version="2") properties
    [MarkoutPropertyName("Tool Format")]
    public string? ToolFormat { get; set; }

    [MarkoutPropertyName("RID-Specific Pointer Package")]
    [MarkoutSkipDefault]
    public bool IsRidSpecificPointerPackage { get; set; }

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Tool Commands")]
    public List<string>? ToolCommands { get; set; }

    [MarkoutSection(Name = "RID Packages")]
    public List<RidPackageReference>? RuntimeIdentifierPackages { get; set; }

    [MarkoutPropertyName("Runtime Target RID")]
    public string? RuntimeTargetRid { get; set; }

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Native Files")]
    public List<string>? NativeFiles { get; set; }

    [MarkoutIgnoreInTable]
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
    [MarkoutSection(Name = "Files")]
    public List<string>? Files { get; set; }

    private List<MarkoutField> GetCompactFields()
    {
        var fields = new List<MarkoutField>();
        
        fields.Add(new("Version", Version));
        fields.Add(new("Type", PackageType));
        
        if (!string.IsNullOrEmpty(NewestTfm))
            fields.Add(new("TFM", NewestTfm));
        if (Published.HasValue)
            fields.Add(new("Updated", Published.Value.ToString("yyyy-MM-dd")));
        if (Deprecation != null)
            fields.Add(new("Deprecated", "Yes"));
        if (Vulnerabilities is { Count: > 0 })
            fields.Add(MarkoutField.Create("Vulnerabilities", Vulnerabilities.Count));
        
        return fields;
    }

    private List<MarkoutField> GetMetadataFields()
    {
        var fields = new List<MarkoutField>();
        
        // Core fields (same as compact but with different TFM label)
        fields.Add(new("Version", Version));
        fields.Add(new("Type", PackageType));
        if (!string.IsNullOrEmpty(NewestTfm))
            fields.Add(new("Newest TFM", NewestTfm));
        if (Published.HasValue)
            fields.Add(new("Updated", Published.Value.ToString("yyyy-MM-dd")));

        if (Deprecation?.Summary != null)
            fields.Add(new("Deprecated Note", Deprecation.Summary));

        if (!string.IsNullOrWhiteSpace(Authors))
            fields.Add(new("Authors", Authors));
        if (Owners is { Count: > 0 } && string.Join(", ", Owners) != Authors)
            fields.Add(new("Owners", string.Join(", ", Owners)));
        if (!string.IsNullOrWhiteSpace(License))
            fields.Add(new("License", License));
        if (!string.IsNullOrWhiteSpace(Repository))
            fields.Add(new("Repository", Repository));
        
        // NuGet metadata
        if (IsVerified == true)
            fields.Add(MarkoutField.Create("Verified", true));
        
        if (ContentDirectories is { Count: > 0 })
            fields.Add(new("Content", string.Join(", ", ContentDirectories)));
        if (TargetFrameworkCount > 0)
            fields.Add(MarkoutField.Create("Target Frameworks", TargetFrameworkCount));
        if (SupportedRidCount > 0)
            fields.Add(MarkoutField.Create("Runtime Identifiers", SupportedRidCount));
        if (AssemblyCount > 1)
            fields.Add(MarkoutField.Create("Libraries", AssemblyCount));
        if (HasReadme)
            fields.Add(MarkoutField.Create("Readme", true));
        if (Vulnerabilities is { Count: > 0 })
            fields.Add(MarkoutField.Create("Vulnerabilities", Vulnerabilities.Count));

        // Tool-specific properties
        if (ToolCommands is { Count: > 0 })
            fields.Add(new("Tool Commands", string.Join(", ", ToolCommands)));

        // Additional properties
        if (IsFrameworkDependent)
            fields.Add(MarkoutField.Create("Framework Dependent", true));
        if (IsRidSpecificPointerPackage)
            fields.Add(MarkoutField.Create("RID-Specific Pointer", true));
        if (!string.IsNullOrWhiteSpace(RuntimeTargetRid))
            fields.Add(new("Runtime Target RID", RuntimeTargetRid));
        
        return fields;
    }

    private List<MarkoutField> GetStatisticsFields()
    {
        var fields = new List<MarkoutField>();
        var formatter = new Output.CompactNumberFormatter();
        var sizeFormatter = new Output.ByteSizeFormatter();
        
        if (TotalDownloads.HasValue)
            fields.Add(new("Total Downloads", formatter.Format(TotalDownloads.Value)));
        if (VersionDownloads.HasValue)
            fields.Add(new("Version Downloads", formatter.Format(VersionDownloads.Value)));
        if (VersionCount.HasValue)
            fields.Add(MarkoutField.Create("Version Count", VersionCount.Value));
        if (PackageSize.HasValue)
            fields.Add(new("Package Size", sizeFormatter.Format(PackageSize.Value)));
        
        return fields;
    }
}


