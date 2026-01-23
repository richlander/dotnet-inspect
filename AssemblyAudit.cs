using System.Text.Json.Serialization;
using MarkdownData;

namespace DotnetInspector;

// Summary helper for AssemblyInfo in table display

[MdfSerializable]
public class AssemblyAudit
{
    [MdfPropertyName("File")]
    public string FileName { get; set; } = "";

    [MdfPropertyName("Type")]
    public string FileType { get; set; } = "";

    [MdfPropertyName("PDB Format")]
    public string? PdbFormat { get; set; }

    [MdfPropertyName("PDB Path")]
    public string? PdbPath { get; set; }

    [MdfPropertyName("Embedded PDB")]
    public bool HasEmbeddedPdb { get; set; }

    [MdfPropertyName("Reproducible Flag")]
    public bool HasReproducibleFlag { get; set; }

    [MdfIgnore]
    public bool? HasNormalizedPaths { get; set; }

    [MdfPropertyName("SourceLink")]
    public bool HasSourceLink { get; set; }

    public bool IsDeterministic { get; set; }

    [MdfPropertyName("Repository URL")]
    public string? RepositoryUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [MdfIgnore]
    public string? SourceLinkJson { get; set; }

    [MdfIgnore]
    public List<string>? NonNormalizedPaths { get; set; }

    // Assembly metadata
    [MdfIgnore]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AssemblyInfo? AssemblyInfo { get; set; }

    // Public API surface
    [MdfIgnore]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiSurface? ApiSurface { get; set; }

    // Computed summary properties for MDF table display
    [MdfPropertyName("Assembly")]
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

    [MdfPropertyName("API")]
    [JsonIgnore]
    public string? ApiSummary => ApiSurface switch
    {
        null => null,
        var api => $"{api.PublicTypeCount} types, {api.PublicMethodCount} methods"
    };
}
