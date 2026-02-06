using System.Text.Json.Serialization;
using Markout;

namespace DotnetInspector;

// Summary helper for AssemblyInfo in table display

public class AssemblyAudit
{
    [MarkoutPropertyName("File")]
    public string FileName { get; set; } = "";

    [MarkoutPropertyName("Type")]
    public string FileType { get; set; } = "";

    /// <summary>
    /// PDB format: "Portable PDB", "Windows PDB", or null if none.
    /// </summary>
    [MarkoutPropertyName("PDB Format")]
    public string? PdbFormat { get; set; }

    /// <summary>
    /// Where the PDB is located: "Embedded", "Standalone", or null if unknown.
    /// </summary>
    [MarkoutPropertyName("PDB Location")]
    public string? PdbLocation { get; set; }

    [MarkoutPropertyName("PDB Path")]
    public string? PdbPath { get; set; }

    [MarkoutPropertyName("Embedded PDB")]
    [MarkoutBoolFormat("✓", "✗")]
    public bool HasEmbeddedPdb { get; set; }

    [MarkoutPropertyName("Reproducible Flag")]
    [MarkoutBoolFormat("✓", "✗")]
    public bool HasReproducibleFlag { get; set; }

    [MarkoutIgnore]
    public bool? HasNormalizedPaths { get; set; }

    [MarkoutPropertyName("SourceLink")]
    [MarkoutBoolFormat("✓", "✗")]
    public bool HasSourceLink { get; set; }

    /// <summary>
    /// Explanation for why SourceLink is unavailable (e.g., "Distro build (ReadyToRun)").
    /// Only set when HasSourceLink is false and we can determine the reason.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("source_link_unavailable_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceLinkUnavailableReason { get; set; }

    /// <summary>
    /// Returns SourceLink status with explanation if unavailable.
    /// </summary>
    [MarkoutPropertyName("SourceLink Status")]
    [JsonIgnore]
    public string SourceLinkStatus => HasSourceLink
        ? "✓"
        : SourceLinkUnavailableReason != null
            ? $"✗ ({SourceLinkUnavailableReason})"
            : "✗";

    [MarkoutBoolFormat("✓", "✗")]
    public bool IsDeterministic { get; set; }

    [MarkoutPropertyName("Repository URL")]
    public string? RepositoryUrl { get; set; }

    /// <summary>
    /// Indicates that a Windows PDB was detected (not supported by this tool).
    /// </summary>
    [MarkoutIgnore]
    public bool WindowsPdbDetected { get; set; }

    /// <summary>
    /// The server the PDB was retrieved from (e.g., "nuget.org", "msdl.microsoft.com"), or null if local/embedded.
    /// </summary>
    [MarkoutPropertyName("Symbol Server")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SymbolServer { get; set; }

    /// <summary>
    /// Inferred builder of the assembly based on symbol availability and SourceLink.
    /// </summary>
    [MarkoutPropertyName("Builder")]
    [JsonPropertyName("builder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Builder { get; set; }

    /// <summary>
    /// Publisher identity from NuGet package author signature (CN).
    /// </summary>
    [MarkoutPropertyName("Publisher")]
    [JsonPropertyName("publisher")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Publisher { get; set; }

    /// <summary>
    /// Whether the package publisher signature was cryptographically verified.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("publisher_verified")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PublisherVerified { get; set; }

    /// <summary>
    /// Whether the package repository signature was cryptographically verified.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("repository_verified")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RepositoryVerified { get; set; }

    /// <summary>
    /// Status message when signature verification was skipped or failed.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("signature_status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SignatureStatus { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [MarkoutIgnore]
    public string? SourceLinkJson { get; set; }

    [MarkoutIgnore]
    public List<string>? NonNormalizedPaths { get; set; }

    // Strict audit: source verification results
    /// <summary>
    /// Total number of source documents in the PDB.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("total_source_files")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TotalSourceFiles { get; set; }

    /// <summary>
    /// Number of source files accessible via SourceLink (HTTP 200).
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("accessible_source_files")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int AccessibleSourceFiles { get; set; }

    /// <summary>
    /// Number of source files embedded in the PDB.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("embedded_source_files")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int EmbeddedSourceFiles { get; set; }

    /// <summary>
    /// Source files that are neither accessible via SourceLink nor embedded.
    /// Only populated in strict audit mode.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("missing_source_files")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? MissingSourceFiles { get; set; }

    /// <summary>
    /// Whether all source files are accessible (via SourceLink or embedded).
    /// Only set in strict audit mode.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("all_sources_accessible")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllSourcesAccessible { get; set; }

    /// <summary>
    /// Human-readable source coverage summary for display.
    /// </summary>
    [MarkoutPropertyName("Source Coverage")]
    [JsonIgnore]
    public string? SourceCoverageSummary => TotalSourceFiles > 0
        ? $"{AccessibleSourceFiles + EmbeddedSourceFiles}/{TotalSourceFiles} files"
        : null;

    // Assembly metadata
    [MarkoutIgnore]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AssemblyInfo? AssemblyInfo { get; set; }

    // Public API surface
    [MarkoutIgnore]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiSurface? ApiSurface { get; set; }

    // Computed summary properties for MDF table display
    [MarkoutPropertyName("Assembly")]
    [JsonIgnore]
    public string? AssemblySummary => AssemblyInfo switch
    {
        null => null,
        var info => string.Join(", ", new[]
        {
            info.Architecture,
            info.TargetFramework,
            info.CompilationType,
            info.IsSigned ? "Signed" : null
        }.Where(s => !string.IsNullOrEmpty(s)))
    };

    [MarkoutPropertyName("API")]
    [JsonIgnore]
    public string? ApiSummary => ApiSurface switch
    {
        null => null,
        var api => $"{api.PublicTypeCount} types, {api.PublicMethodCount} methods"
    };
}
