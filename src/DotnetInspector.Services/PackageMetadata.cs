namespace DotnetInspector.Services;

/// <summary>
/// NuGet package metadata fetched from NuGet APIs.
/// </summary>
public class PackageMetadata
{
    public DateTimeOffset? Published { get; set; }
    public long? TotalDownloads { get; set; }
    public long? VersionDownloads { get; set; }
    public int? VersionCount { get; set; }
    public long? PackageSize { get; set; }
    public bool? IsVerified { get; set; }
    public List<string>? Owners { get; set; }
    public PackageDeprecation? Deprecation { get; set; }
    public List<PackageVulnerability>? Vulnerabilities { get; set; }
}

/// <summary>
/// Deprecation information for a NuGet package.
/// </summary>
public class PackageDeprecation
{
    /// <summary>
    /// Reasons for deprecation (e.g., "Legacy", "CriticalBugs", "Other").
    /// </summary>
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
    public string Summary
    {
        get
        {
            List<string> parts = [];
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
public class PackageVulnerability
{
    /// <summary>
    /// Severity level (e.g., "Low", "Moderate", "High", "Critical").
    /// </summary>
    public string Severity { get; set; } = "";

    /// <summary>
    /// CVE identifier (e.g., "CVE-2024-43485").
    /// </summary>
    public string? CveId { get; set; }

    /// <summary>
    /// Brief description of the vulnerability.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// URL to the advisory.
    /// </summary>
    public string? AdvisoryUrl { get; set; }

    /// <summary>
    /// GHSA identifier (e.g., "GHSA-8g4q-xg66-9fp4").
    /// </summary>
    public string? GhsaId { get; set; }
}
