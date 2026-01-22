using System.Text.Json.Serialization;

namespace DotnetInspector;

public class AssemblyAudit
{
    public string FileName { get; set; } = "";
    public string FileType { get; set; } = "";
    public string? PdbFormat { get; set; }
    public string? PdbPath { get; set; }
    public bool HasEmbeddedPdb { get; set; }
    public bool HasReproducibleFlag { get; set; }
    public bool? HasNormalizedPaths { get; set; }
    public bool HasSourceLink { get; set; }
    public bool IsDeterministic { get; set; }
    public string? RepositoryUrl { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceLinkJson { get; set; }
    public List<string>? NonNormalizedPaths { get; set; }

    // Assembly metadata
    public AssemblyInfo? AssemblyInfo { get; set; }

    // Public API surface
    public ApiSurface? ApiSurface { get; set; }
}
