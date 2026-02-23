using System.Text.Json.Serialization;

namespace DotnetInspector.Models;

/// <summary>
/// The kind of match for a type search result.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MatchKind>))]
public enum MatchKind
{
    Exact,
    Glob,
    Partial,
    NotFound
}

/// <summary>
/// Raw result from type search. One record per match.
/// Services return this flat model; writers decide how to present it.
/// </summary>
public record TypeFindResult
{
    // ─── Search Context ─────────────────────────────────────────────────
    [JsonPropertyName("pattern")]
    public string Pattern { get; init; } = "";

    [JsonPropertyName("match")]
    public MatchKind Match { get; init; }

    /// <summary>
    /// Match quality score (0.0-1.0). Exact/glob matches = 1.0, fuzzy matches = similarity score.
    /// </summary>
    [JsonPropertyName("similarity")]
    public double? Similarity { get; init; }

    // ─── Type Identity ──────────────────────────────────────────────────
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [JsonPropertyName("namespace")]
    public string Namespace { get; init; } = "";

    [JsonPropertyName("full_name")]
    public string FullName { get; init; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "";  // class, struct, interface, enum, delegate

    // ─── Location ───────────────────────────────────────────────────────
    [JsonPropertyName("library")]
    public string Library { get; init; } = "";

    [JsonPropertyName("source")]
    public string Source { get; init; } = "";  // runtime, aspnetcore, package name

    [JsonPropertyName("source_version")]
    public string? SourceVersion { get; init; }
}

/// <summary>
/// Unified member model containing all extractable and computed member data.
/// Writers project subsets of these fields for different rendering modes.
/// </summary>
public record MemberInfo
{
    // ─── Identity ───────────────────────────────────────────────────────
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "";  // method, property, field, event, constructor

    [JsonPropertyName("overload_index")]
    public int? OverloadIndex { get; init; }

    [JsonIgnore]
    public string Selector => OverloadIndex.HasValue ? $"{Name}:{OverloadIndex}" : Name;

    [JsonPropertyName("overload_count")]
    public int? OverloadCount { get; init; }

    // ─── Signature & Types ──────────────────────────────────────────────
    [JsonPropertyName("signature")]
    public string? Signature { get; init; }

    [JsonPropertyName("return_type")]
    public string? ReturnType { get; init; }

    [JsonPropertyName("parameters")]
    public List<MemberParameterInfo> Parameters { get; init; } = [];

    [JsonIgnore]
    public string ParameterTypes => string.Join(", ", Parameters.Select(p => p.Type));

    [JsonPropertyName("accessors")]
    public string? Accessors { get; init; }  // get, set, init

    // ─── Modifiers ──────────────────────────────────────────────────────
    [JsonPropertyName("is_static")]
    public bool IsStatic { get; init; }

    [JsonPropertyName("is_virtual")]
    public bool IsVirtual { get; init; }

    [JsonPropertyName("is_abstract")]
    public bool IsAbstract { get; init; }

    [JsonPropertyName("is_unsafe")]
    public bool IsUnsafe { get; init; }

    [JsonPropertyName("is_extension")]
    public bool IsExtension { get; init; }

    [JsonPropertyName("extended_type")]
    public string? ExtendedType { get; init; }

    [JsonPropertyName("modifiers")]
    public string? Modifiers { get; init; }  // "static async"

    // ─── Documentation ──────────────────────────────────────────────────
    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("returns")]
    public string? Returns { get; init; }

    // ─── Attributes ─────────────────────────────────────────────────────
    [JsonPropertyName("attributes")]
    public List<MemberAttributeInfo> Attributes { get; init; } = [];

    [JsonIgnore]
    public bool IsObsolete => Attributes.Any(a => a.Name == "Obsolete");

    // ─── Source Location ────────────────────────────────────────────────
    [JsonPropertyName("source_line")]
    public int? SourceLine { get; init; }

    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; init; }

    // ─── Provenance ─────────────────────────────────────────────────────
    [JsonPropertyName("declaring_type")]
    public string? DeclaringType { get; init; }

    [JsonPropertyName("library")]
    public string? Library { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }
}

/// <summary>
/// Parameter information for a member.
/// </summary>
public record MemberParameterInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("has_default")] bool HasDefault,
    [property: JsonPropertyName("default_value")] string? DefaultValue);

/// <summary>
/// Attribute information for a member.
/// </summary>
public record MemberAttributeInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string? Value);
