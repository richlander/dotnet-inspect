using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;
using DotnetInspector.Services;
using SignatureVerificationResult = DotnetInspector.Services.SignatureVerificationResult;

namespace DotnetInspector.Models;

public class InspectionResult
{
    public string PackageName { get; set; } = "";

    public string? ManifestVersion { get; set; }

    public string Version { get; set; } = "";

    /// <summary>
    /// Where this package was resolved from (e.g., NuGet, Platform, File).
    /// </summary>
    public string? Source { get; set; }

    public string? Description { get; set; }

    public string? Authors { get; set; }
    public string? License { get; set; }
    public string? LicenseUrl { get; set; }
    public string? Repository { get; set; }
    public string? RepositoryType { get; set; }
    public string? RepositoryCommit { get; set; }

    /// <summary>
    /// When this package version was published to NuGet (from NuGet API).
    /// </summary>
    public DateTimeOffset? Published { get; set; }

    /// <summary>
    /// When this package was built (from nupkg file timestamps).
    /// </summary>
    public DateTimeOffset? BuiltDate { get; set; }

    /// <summary>
    /// Total downloads across all versions of the package.
    /// </summary>
    public long? TotalDownloads { get; set; }

    /// <summary>
    /// Downloads for this specific version.
    /// </summary>
    public long? VersionDownloads { get; set; }

    /// <summary>
    /// Total number of versions published for this package.
    /// </summary>
    public int? VersionCount { get; set; }

    /// <summary>
    /// Size of the .nupkg file in bytes.
    /// </summary>
    public long? PackageSize { get; set; }

    /// <summary>
    /// Whether the package owner is verified by NuGet.org.
    /// </summary>
    public bool? IsVerified { get; set; }

    /// <summary>
    /// Package owners (from NuGet.org).
    /// </summary>
    public List<string>? Owners { get; set; }

    /// <summary>
    /// Deprecation information if the package is deprecated.
    /// </summary>
    public PackageDeprecation? Deprecation { get; set; }

    /// <summary>
    /// Known security vulnerabilities for this package version.
    /// </summary>
    public List<PackageVulnerability>? Vulnerabilities { get; set; }

    /// <summary>
    /// Indicates whether the package contains a best package README candidate.
    /// </summary>
    public bool HasReadme { get; set; }

    /// <summary>
    /// Path to the readme file within the package (from nuspec readme element).
    /// </summary>
    [JsonIgnore]
    public string? ReadmeFile { get; set; }

    /// <summary>
    /// Best package README path: AGENTS.md, README.md, PACKAGE.md, then the declared readme fallback.
    /// </summary>
    [JsonIgnore]
    public string? PackageReadmeFile { get; set; }

    public bool HasAgentDocumentation { get; set; }

    public bool IsToolPackage { get; set; }

    public List<string>? PackageTypes { get; set; }

    public List<string>? ContentDirectories { get; set; }

    public List<string>? TargetFrameworks { get; set; }

    /// <summary>
    /// The highest-priority target framework in the package (computed from TargetFrameworks),
    /// or the explicit TFM override when --tfm is specified.
    /// </summary>
    [JsonIgnore]
    public string? Tfm
    {
        get => TfmOverride ?? (TargetFrameworks is { Count: > 0 }
            ? TfmSelector.SelectHighestTfm(TargetFrameworks)
            : null);
        set => TfmOverride = value;
    }

    [JsonIgnore]
    private string? TfmOverride { get; set; }

    public List<string>? SupportedRids { get; set; }

    /// <summary>
    /// Total number of library assemblies (DLLs) in the package, excluding resource assemblies.
    /// </summary>
    public int AssemblyCount { get; set; }

    /// <summary>
    /// Local package binary symbol/source provenance summary. Counts non-resource DLL files
    /// discovered in the package layout.
    /// </summary>
    public PackageBinarySignals? BinarySignals { get; set; }

    public bool IsFrameworkDependent { get; set; }

    public bool HasRidSpecificAssets { get; set; }

    public bool HasNativeDependencies { get; set; }

    /// <summary>
    /// RID-specific tool (DotNetCliTool Version="2") format description.
    /// </summary>
    public string? ToolFormat { get; set; }

    public bool IsRidSpecificPointerPackage { get; set; }

    public List<string>? ToolCommands { get; set; }

    public List<RidPackageReference>? RuntimeIdentifierPackages { get; set; }

    public string? RuntimeTargetRid { get; set; }

    public List<string>? NativeFiles { get; set; }

    /// <summary>
    /// Files under the package lib/ directory, across all target frameworks.
    /// </summary>
    public List<string>? LibraryFiles { get; set; }

    public List<DependencyGroup>? DependencyGroups { get; set; }

    public List<PackageDependency>? RuntimeDependencies { get; set; }

    /// <summary>
    /// All files in the package (excluding zip plumbing), each with its
    /// uncompressed size in bytes. Populated on demand for the Files section.
    /// </summary>
    public List<PackageFile>? Files { get; set; }

    /// <summary>
    /// SourceLink URL rows aggregated from package libraries. Populated only
    /// when the Source Files section is selected.
    /// </summary>
    public List<PackageSourceFileInfo>? SourceFiles { get; set; }

    /// <summary>
    /// Complete package file listing used to derive focused file sections.
    /// </summary>
    [JsonIgnore]
    public List<PackageFile>? PackageFiles { get; set; }

    /// <summary>
    /// Result of NuGet package signature verification.
    /// </summary>
    public SignatureVerificationResult? SignatureResult { get; set; }

    /// <summary>
    /// Metadata and registry audit signals. These are observations, not a trust verdict.
    /// </summary>
    public List<AuditSignal>? AuditSignals { get; set; }

    /// <summary>
    /// Whether the package has a valid signature (author or repository).
    /// </summary>
    [JsonIgnore]
    public bool? Signed => SignatureResult switch
    {
        null => null,
        { IsUnsigned: true } => false,
        { AuthorVerified: true } or { RepositoryVerified: true } => true,
        _ => false
    };
}

public record class PackageBinarySignals
{
    public int TotalBinaries { get; init; }
    public int SymbolsAvailable { get; init; }
    public int SourceLinkAvailable { get; init; }
    public int EmbeddedPdbs { get; init; }
    public int InPackagePdbs { get; init; }
    public int SnupkgPdbs { get; init; }
    public int MsdlPdbs { get; init; }
    public int OtherPdbs { get; init; }
    public int EmbeddedSourceLinkPdbs { get; init; }
    public int InPackageSourceLinkPdbs { get; init; }
    public int SnupkgSourceLinkPdbs { get; init; }
    public int MsdlSourceLinkPdbs { get; init; }
    public int OtherSourceLinkPdbs { get; init; }
}

/// <summary>
/// A single file in a NuGet package: its package-relative path (forward slashes)
/// and uncompressed size in bytes (read from the already-extracted package, so
/// no extra download is required). <paramref name="IsReadme"/> marks the file the
/// package's <c>.nuspec</c> declares as its readme (not always <c>README.md</c>);
/// <paramref name="IsAgents"/> marks a root <c>AGENTS.md</c> file.
/// </summary>
public sealed record PackageFile(
    string Path,
    long Size,
    [property: JsonIgnore] bool IsReadme = false,
    [property: JsonIgnore] bool IsAgents = false);

public sealed record PackageFileJsonRow(
    string Path,
    long Size);

public sealed record PackageFileMultiJsonRow(
    string Package,
    string Version,
    string Path,
    long? Size);

/// <summary>
/// Content for one selected package file. Empty rows preserve package input
/// cardinality for surveys when a selector has no match.
/// </summary>
public sealed record PackageFileContent(
    string Package,
    string Version,
    string Path,
    long Size,
    bool Found,
    string Content);

public sealed record PackageSourceFileInfo(
    string Library,
    string Type,
    string? Url);
