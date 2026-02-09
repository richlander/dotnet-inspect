using System.Text.Json.Serialization;
using DotnetInspector.Metadata;

namespace DotnetInspector.Models;

public class AssemblyAudit
{
    [JsonIgnore]
    public string? Tfm { get; set; }

    public string FileName { get; set; } = "";

    public string FileType { get; set; } = "";

    /// <summary>
    /// PDB format: "Portable PDB", "Windows PDB", or null if none.
    /// </summary>
    public string? PdbFormat { get; set; }

    /// <summary>
    /// Where the PDB is located: "Embedded", "Standalone", or null if unknown.
    /// </summary>
    public string? PdbLocation { get; set; }

    public string? PdbPath { get; set; }

    public bool HasEmbeddedPdb { get; set; }

    public bool HasReproducibleFlag { get; set; }

    public bool? HasNormalizedPaths { get; set; }

    public bool HasSourceLink { get; set; }

    /// <summary>
    /// Explanation for why SourceLink is unavailable (e.g., "Distro build (ReadyToRun)").
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceLinkUnavailableReason { get; set; }

    public bool IsDeterministic { get; set; }

    public string? RepositoryUrl { get; set; }

    /// <summary>
    /// Indicates that a Windows PDB was detected (not supported by this tool).
    /// </summary>
    public bool WindowsPdbDetected { get; set; }

    /// <summary>
    /// The server the PDB was retrieved from (e.g., "nuget.org", "msdl.microsoft.com"), or null if local/embedded.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SymbolServer { get; set; }

    /// <summary>
    /// Inferred builder of the assembly based on symbol availability and SourceLink.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Builder { get; set; }

    /// <summary>
    /// Publisher identity from NuGet package author signature (CN).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Publisher { get; set; }

    /// <summary>
    /// Whether the package publisher signature was cryptographically verified.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PublisherVerified { get; set; }

    /// <summary>
    /// Whether the package repository signature was cryptographically verified.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RepositoryVerified { get; set; }

    /// <summary>
    /// Status message when signature verification was skipped or failed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SignatureStatus { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceLinkJson { get; set; }

    public List<string>? NonNormalizedPaths { get; set; }

    /// <summary>
    /// Total number of source documents in the PDB.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TotalSourceFiles { get; set; }

    /// <summary>
    /// Number of source files accessible via SourceLink (HTTP 200).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int AccessibleSourceFiles { get; set; }

    /// <summary>
    /// Number of source files embedded in the PDB.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int EmbeddedSourceFiles { get; set; }

    /// <summary>
    /// Source files that are neither accessible via SourceLink nor embedded.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? MissingSourceFiles { get; set; }

    /// <summary>
    /// Whether all source files are accessible (via SourceLink or embedded).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllSourcesAccessible { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AssemblyInfo? AssemblyInfo { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiSurface? ApiSurface { get; set; }

    /// <summary>
    /// View routing flag: when true, show nested dependency tree instead of flat references.
    /// </summary>
    [JsonIgnore]
    public bool UseDependenciesView { get; set; }
}
