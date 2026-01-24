using System.Text.Json.Serialization;
using MarkOut;

namespace DotnetInspector;

// Summary helper for AssemblyInfo in table display

[MarkOutSerializable]
public class AssemblyAudit
{
    [MarkOutPropertyName("File")]
    public string FileName { get; set; } = "";

    [MarkOutPropertyName("Type")]
    public string FileType { get; set; } = "";

    [MarkOutPropertyName("PDB Format")]
    public string? PdbFormat { get; set; }

    [MarkOutPropertyName("PDB Path")]
    public string? PdbPath { get; set; }

    [MarkOutPropertyName("Embedded PDB")]
    public bool HasEmbeddedPdb { get; set; }

    [MarkOutPropertyName("Reproducible Flag")]
    public bool HasReproducibleFlag { get; set; }

    [MarkOutIgnore]
    public bool? HasNormalizedPaths { get; set; }

    [MarkOutPropertyName("SourceLink")]
    public bool HasSourceLink { get; set; }

    public bool IsDeterministic { get; set; }

    [MarkOutPropertyName("Repository URL")]
    public string? RepositoryUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [MarkOutIgnore]
    public string? SourceLinkJson { get; set; }

    [MarkOutIgnore]
    public List<string>? NonNormalizedPaths { get; set; }

    // Assembly metadata
    [MarkOutIgnore]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AssemblyInfo? AssemblyInfo { get; set; }

    // Public API surface
    [MarkOutIgnore]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiSurface? ApiSurface { get; set; }

    // Computed summary properties for MDF table display
    [MarkOutPropertyName("Assembly")]
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

    [MarkOutPropertyName("API")]
    [JsonIgnore]
    public string? ApiSummary => ApiSurface switch
    {
        null => null,
        var api => $"{api.PublicTypeCount} types, {api.PublicMethodCount} methods"
    };
}
