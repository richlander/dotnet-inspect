using System.Text.Json.Serialization;
using Markout;

namespace DotnetInspector;

// Summary helper for AssemblyInfo in table display

[MarkoutSerializable]
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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [MarkoutIgnore]
    public string? SourceLinkJson { get; set; }

    [MarkoutIgnore]
    public List<string>? NonNormalizedPaths { get; set; }

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
