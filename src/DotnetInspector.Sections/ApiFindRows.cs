using System.Text.Json.Serialization;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Sections;

[JsonConverter(typeof(JsonStringEnumConverter<TypeFindMatchKind>))]
public enum TypeFindMatchKind
{
    Exact,
    Direct,
    Glob,
    Partial,
    NotFound,
    Namespace,
    Prefix,
    Substring
}

[JsonConverter(typeof(JsonStringEnumConverter<MemberFindMatchKind>))]
public enum MemberFindMatchKind
{
    Direct,
    Glob
}

public record TypeFindResult : ApiFindRow
{
    [JsonPropertyName("pattern")]
    public string Pattern { get; init; } = "";

    [JsonPropertyName("match")]
    public TypeFindMatchKind Match { get; init; }

    [JsonPropertyName("similarity")]
    public double? Similarity { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [JsonPropertyName("namespace")]
    public string Namespace { get; init; } = "";

    [JsonPropertyName("full_name")]
    public string FullName { get; init; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "";

    [JsonPropertyName("library")]
    public string Library { get; init; } = "";

    [JsonPropertyName("source")]
    public string Source { get; init; } = "";

    [JsonPropertyName("ecosystem")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ecosystem { get; init; }

    [JsonPropertyName("source_version")]
    public string? SourceVersion { get; init; }

    [JsonIgnore]
    public TypeDeclarationLocatorSectionCandidate? Location { get; init; }

    [JsonIgnore]
    public ExactLibrarySourceCoordinate? ExactSource { get; init; }

    [JsonIgnore]
    public MetadataTypeDefinitionName? ExactType { get; init; }
}

public record MemberFindResult : ApiFindRow
{
    [JsonPropertyName("pattern")]
    public string Pattern { get; init; } = "";

    [JsonPropertyName("match")]
    public MemberFindMatchKind Match { get; init; }

    [JsonPropertyName("member")]
    public string Member { get; init; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "";

    [JsonPropertyName("declaring_type")]
    public string DeclaringType { get; init; } = "";

    [JsonPropertyName("namespace")]
    public string Namespace { get; init; } = "";

    [JsonPropertyName("signature")]
    public string? Signature { get; init; }

    [JsonPropertyName("return_type")]
    public string? ReturnType { get; init; }

    [JsonPropertyName("library")]
    public string Library { get; init; } = "";

    [JsonPropertyName("source")]
    public string Source { get; init; } = "";

    [JsonPropertyName("ecosystem")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ecosystem { get; init; }

    [JsonPropertyName("source_version")]
    public string? SourceVersion { get; init; }

    [JsonIgnore]
    public ExactLibrarySourceCoordinate? ExactSource { get; init; }

    [JsonIgnore]
    public MemberAnchor? ExactMember { get; init; }
}
