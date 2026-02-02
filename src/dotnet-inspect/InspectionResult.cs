using System.Text.Json.Serialization;
using Markout;

namespace DotnetInspector;

[MarkoutSerializable(
    TitleProperty = nameof(PackageName), 
    TitleContextProperty = nameof(Version), 
    DescriptionProperty = nameof(Description),
    RenderScalars = false)]
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
    [MarkoutSection(Name = "Metadata")]
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
    [JsonIgnore]
    public DateTimeOffset? Published { get; set; }

    /// <summary>
    /// Formatted publish date for display.
    /// </summary>
    [MarkoutPropertyName("Updated")]
    [JsonPropertyName("published")]
    public string? PublishedDisplay => Published?.ToString("yyyy-MM-dd");

    /// <summary>
    /// Total downloads across all versions of the package.
    /// </summary>
    public long? TotalDownloads { get; set; }

    /// <summary>
    /// Formatted download count for display (e.g., "5.1B", "1.2M").
    /// </summary>
    [MarkoutPropertyName("Downloads")]
    [JsonIgnore]
    public string? DownloadsDisplay => TotalDownloads.HasValue ? FormatDownloads(TotalDownloads.Value) : null;

    /// <summary>
    /// Downloads for this specific version.
    /// </summary>
    public long? VersionDownloads { get; set; }

    /// <summary>
    /// Formatted version download count for display.
    /// </summary>
    [JsonIgnore]
    public string? VersionDownloadsDisplay => VersionDownloads.HasValue ? FormatDownloads(VersionDownloads.Value) : null;

    /// <summary>
    /// Total number of versions published for this package.
    /// </summary>
    public int? VersionCount { get; set; }

    /// <summary>
    /// Size of the .nupkg file in bytes.
    /// </summary>
    public long? PackageSize { get; set; }

    /// <summary>
    /// Formatted package size for display (e.g., "2.3 MB", "150 KB").
    /// </summary>
    [JsonIgnore]
    public string? PackageSizeDisplay => PackageSize.HasValue ? FormatBytes(PackageSize.Value) : null;

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:0.#} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:0.#} MB",
            >= 1_024 => $"{bytes / 1_024.0:0.#} KB",
            _ => $"{bytes} B"
        };
    }

    private static string FormatDownloads(long downloads)
    {
        return downloads switch
        {
            >= 1_000_000_000 => $"{downloads / 1_000_000_000.0:0.#}B",
            >= 1_000_000 => $"{downloads / 1_000_000.0:0.#}M",
            >= 1_000 => $"{downloads / 1_000.0:0.#}K",
            _ => downloads.ToString()
        };
    }

    /// <summary>
    /// Whether the package owner is verified by NuGet.org.
    /// </summary>
    public bool? IsVerified { get; set; }

    /// <summary>
    /// Package owners (from NuGet.org).
    /// </summary>
    [MarkoutIgnoreInTable]
    public List<string>? Owners { get; set; }

    /// <summary>
    /// Owners as comma-separated string for display.
    /// </summary>
    [MarkoutPropertyName("Owners")]
    [JsonIgnore]
    public string? OwnersDisplay => Owners is { Count: > 0 } ? string.Join(", ", Owners) : null;

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
    public bool HasReadme { get; set; }

    /// <summary>
    /// Path to the readme file within the package (from nuspec readme element).
    /// </summary>
    [JsonIgnore]
    public string? ReadmeFile { get; set; }

    public bool IsToolPackage { get; set; }

    [MarkoutIgnoreInTable]
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

    [MarkoutIgnoreInTable]
    public List<string>? ContentDirectories { get; set; }

    [MarkoutPropertyName("Content")]
    [JsonIgnore]
    public string? ContentSummary => ContentDirectories is { Count: > 0 }
        ? string.Join(", ", ContentDirectories)
        : null;

    [MarkoutIgnoreInTable]
    public List<string>? TargetFrameworks { get; set; }

    [MarkoutPropertyName("Target Frameworks")]
    [JsonIgnore]
    public string? TargetFrameworksSummary => TargetFrameworks is { Count: > 0 }
        ? string.Join(", ", TargetFrameworks)
        : null;

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
        // Priority order: modern .NET (net5+) > netcoreapp > netstandard > netframework
        return tfms
            .OrderByDescending(GetTfmPriority)
            .ThenByDescending(ExtractTfmVersion)
            .First();
    }

    private static int GetTfmPriority(string tfm)
    {
        var lower = tfm.ToLowerInvariant();
        // Modern .NET (net5.0+): starts with "net", has digit, and contains a dot (e.g., net8.0, net10.0)
        if (lower.StartsWith("net") && !lower.StartsWith("netstandard") && 
            !lower.StartsWith("netcoreapp") && !lower.StartsWith("netframework") &&
            lower.Length > 3 && char.IsDigit(lower[3]) && lower.Contains('.'))
            return 4; // Modern .NET (net5.0+)
        if (lower.StartsWith("netcoreapp"))
            return 3;
        if (lower.StartsWith("netstandard"))
            return 2;
        // .NET Framework: net4xx without dot, or explicit netframework prefix
        if (lower.StartsWith("net4") || lower.StartsWith("netframework"))
            return 1;
        return 0;
    }

    private static double ExtractTfmVersion(string tfm)
    {
        var lower = tfm.ToLowerInvariant();
        // Extract version number from TFM strings like "net8.0", "netstandard2.1", "net462"
        var versionPart = lower
            .Replace("netcoreapp", "")
            .Replace("netstandard", "")
            .Replace("netframework", "")
            .Replace("net", "")
            .TrimStart('v');
        
        // Handle formats like "8.0", "462", "2.1"
        if (double.TryParse(versionPart, out var version))
            return version;
        
        // Handle "462" format -> 4.62
        if (versionPart.Length == 3 && int.TryParse(versionPart, out var intVersion))
            return intVersion / 100.0;
        
        return 0;
    }

    [MarkoutIgnoreInTable]
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

    [MarkoutIgnoreInTable]
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

    [MarkoutIgnoreInTable]
    public List<string>? NativeFiles { get; set; }

    [MarkoutPropertyName("Native Files")]
    [JsonIgnore]
    public string? NativeFilesSummary => NativeFiles is { Count: > 0 }
        ? string.Join(", ", NativeFiles)
        : null;

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

    [MarkoutSection(Name = "Audit Summary")]
    public AuditSummary? AuditSummary { get; set; }

    [MarkoutSection(Name = "Assembly Audit")]
    public List<AssemblyAudit>? AssemblyAudits { get; set; }

    private List<MarkoutField> GetCompactFields()
    {
        var fields = new List<MarkoutField>();
        
        fields.Add(new("Type", PackageType));
        
        if (!string.IsNullOrEmpty(NewestTfm))
            fields.Add(new("TFM", NewestTfm));
        if (!string.IsNullOrEmpty(PublishedDisplay))
            fields.Add(new("Updated", PublishedDisplay));
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
        fields.Add(new("Type", PackageType));
        if (!string.IsNullOrEmpty(NewestTfm))
            fields.Add(new("Newest TFM", NewestTfm));
        if (!string.IsNullOrEmpty(PublishedDisplay))
            fields.Add(new("Updated", PublishedDisplay));

        if (Deprecation?.Summary != null)
            fields.Add(new("Deprecated Note", Deprecation.Summary));

        if (!string.IsNullOrWhiteSpace(Authors))
            fields.Add(new("Authors", Authors));
        if (!string.IsNullOrWhiteSpace(OwnersDisplay) && OwnersDisplay != Authors)
            fields.Add(new("Owners", OwnersDisplay));
        if (!string.IsNullOrWhiteSpace(License))
            fields.Add(new("License", License));
        if (!string.IsNullOrWhiteSpace(Repository))
            fields.Add(new("Repository", Repository));
        
        // NuGet metadata
        if (IsVerified == true)
            fields.Add(MarkoutField.Create("Verified", true));
        
        if (!string.IsNullOrWhiteSpace(ContentSummary))
            fields.Add(new("Content", ContentSummary));
        if (TargetFrameworkCount > 0)
            fields.Add(MarkoutField.Create("Target Frameworks", TargetFrameworkCount));
        if (SupportedRidCount > 0)
            fields.Add(MarkoutField.Create("Runtime Identifiers", SupportedRidCount));
        if (AssemblyCount > 0)
            fields.Add(MarkoutField.Create("Libraries", AssemblyCount));
        if (HasReadme)
            fields.Add(MarkoutField.Create("Readme", true));
        if (Vulnerabilities is { Count: > 0 })
            fields.Add(MarkoutField.Create("Vulnerabilities", Vulnerabilities.Count));

        // Tool-specific properties
        if (!string.IsNullOrWhiteSpace(ToolCommandsSummary))
            fields.Add(new("Tool Commands", ToolCommandsSummary));

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
        
        if (!string.IsNullOrEmpty(DownloadsDisplay))
            fields.Add(new("Total Downloads", DownloadsDisplay));
        if (!string.IsNullOrEmpty(VersionDownloadsDisplay))
            fields.Add(new("Version Downloads", VersionDownloadsDisplay));
        if (VersionCount.HasValue)
            fields.Add(MarkoutField.Create("Version Count", VersionCount.Value));
        if (!string.IsNullOrEmpty(PackageSizeDisplay))
            fields.Add(new("Package Size", PackageSizeDisplay));
        
        return fields;
    }
}

/// <summary>
/// Deprecation information for a NuGet package.
/// </summary>
public class PackageDeprecation
{
    /// <summary>
    /// Reasons for deprecation (e.g., "Legacy", "CriticalBugs", "Other").
    /// </summary>
    [MarkoutIgnoreInTable]
    public List<string>? Reasons { get; set; }

    /// <summary>
    /// Human-readable deprecation message.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Recommended alternative package ID.
    /// </summary>
    public string? AlternatePackageId { get; set; }

    /// <summary>
    /// Formatted deprecation summary for display.
    /// </summary>
    [JsonIgnore]
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Reasons is { Count: > 0 })
                parts.Add(string.Join(", ", Reasons));
            if (!string.IsNullOrEmpty(AlternatePackageId))
                parts.Add($"use {AlternatePackageId}");
            if (!string.IsNullOrEmpty(Message))
                parts.Add(Message);
            return parts.Count > 0 ? string.Join(" - ", parts) : "Deprecated";
        }
    }
}

/// <summary>
/// Security vulnerability information for a NuGet package.
/// </summary>
[MarkoutSerializable]
public class PackageVulnerability
{
    /// <summary>
    /// Severity level (e.g., "Low", "Moderate", "High", "Critical").
    /// </summary>
    public string Severity { get; set; } = "";

    /// <summary>
    /// CVE identifier (e.g., "CVE-2024-43485").
    /// </summary>
    [MarkoutPropertyName("CVE")]
    public string? CveId { get; set; }

    /// <summary>
    /// Brief description of the vulnerability.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// URL to the advisory.
    /// </summary>
    [MarkoutIgnore]
    public string? AdvisoryUrl { get; set; }

    /// <summary>
    /// GHSA identifier (e.g., "GHSA-8g4q-xg66-9fp4").
    /// </summary>
    [MarkoutIgnore]
    public string? GhsaId { get; set; }
}
