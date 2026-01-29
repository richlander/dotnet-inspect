using System.Text.Json.Serialization;
using Markout;

namespace DotnetInspector;

/// <summary>
/// Represents extracted documentation comments from source code.
/// </summary>
[MarkoutSerializable]
public class DocComment
{
    public string? Summary { get; set; }
    public string? Remarks { get; set; }

    [MarkoutIgnore]
    public Dictionary<string, string>? Parameters { get; set; }
    public string? Returns { get; set; }

    /// <summary>
    /// Sample code references extracted from doc comments.
    /// </summary>
    [MarkoutIgnore]
    public List<SampleReference>? Samples { get; set; }
}

/// <summary>
/// Represents a reference to sample code in the same repository.
/// </summary>
[MarkoutSerializable]
public class SampleReference
{
    /// <summary>
    /// Relative path to the sample file from the source file.
    /// </summary>
    [JsonPropertyName("relative_path")]
    public string RelativePath { get; set; } = "";

    /// <summary>
    /// Human-readable description or title of the sample.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional region name to extract from the sample file.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Resolved URL to the sample file (computed from SourceLink).
    /// </summary>
    [JsonPropertyName("resolved_url")]
    public string? ResolvedUrl { get; set; }

    /// <summary>
    /// Fetched sample content (populated when --inline is used).
    /// </summary>
    [MarkoutIgnore]
    public string? Content { get; set; }
}

[MarkoutSerializable]
public class ApiSurface
{
    /// <summary>
    /// Package or assembly name.
    /// </summary>
    [MarkoutPropertyName("Name")]
    public string? Name { get; set; }

    [MarkoutIgnore]
    public List<ApiType> Types { get; set; } = [];

    [MarkoutPropertyName("Public Types")]
    public int PublicTypeCount { get; set; }

    [MarkoutPropertyName("Public Methods")]
    public int PublicMethodCount { get; set; }

    [MarkoutPropertyName("Public Properties")]
    public int PublicPropertyCount { get; set; }

    [MarkoutPropertyName("Public Events")]
    public int PublicEventCount { get; set; }

    [MarkoutPropertyName("Public Fields")]
    public int PublicFieldCount { get; set; }

    /// <summary>
    /// Target framework moniker for the API surface.
    /// </summary>
    [MarkoutPropertyName("TFM")]
    public string? Tfm { get; set; }

    /// <summary>
    /// Repository URL extracted from SourceLink (if available).
    /// </summary>
    [MarkoutPropertyName("Repository")]
    public string? RepositoryUrl { get; set; }
}

[MarkoutSerializable]
public class ApiType
{
    public string? Namespace { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";  // class, struct, interface, enum, delegate

    [MarkoutPropertyName("Sealed")]
    public bool IsSealed { get; set; }

    [MarkoutPropertyName("Abstract")]
    public bool IsAbstract { get; set; }

    [MarkoutPropertyName("Static")]
    public bool IsStatic { get; set; }

    [MarkoutPropertyName("Base Type")]
    public string? BaseType { get; set; }

    [MarkoutIgnore]
    public List<string>? Interfaces { get; set; }

    [MarkoutPropertyName("Interfaces")]
    [JsonIgnore]
    public string? InterfacesSummary => Interfaces is { Count: > 0 }
        ? string.Join(", ", Interfaces)
        : null;

    [MarkoutIgnore]
    public List<ApiMember>? Members { get; set; }

    // Source information (populated with --source-url)
    [MarkoutIgnore]
    [JsonPropertyName("source_file_path")]
    public string? SourceFilePath { get; set; }

    [MarkoutIgnore]
    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; set; }

    [MarkoutIgnore]
    [JsonPropertyName("github_browse_url")]
    public string? GitHubBrowseUrl { get; set; }

    [MarkoutIgnore]
    [JsonPropertyName("source_line_number")]
    public int? SourceLineNumber { get; set; }

    // Documentation (populated with --docs)
    [MarkoutIgnore]
    public DocComment? Documentation { get; set; }
}

[MarkoutSerializable]
public class ApiMember
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";  // method, property, field, event, constructor

    [MarkoutPropertyName("Return Type")]
    public string? ReturnType { get; set; }

    public string? Signature { get; set; }

    [MarkoutPropertyName("Static")]
    public bool IsStatic { get; set; }

    [MarkoutPropertyName("Virtual")]
    public bool IsVirtual { get; set; }

    [MarkoutPropertyName("Abstract")]
    public bool IsAbstract { get; set; }

    [MarkoutPropertyName("Unsafe")]
    public bool IsUnsafe { get; set; }

    // Source information (populated with --source-url)
    [MarkoutIgnore]
    [JsonPropertyName("source_line_number")]
    public int? SourceLineNumber { get; set; }

    // Documentation (populated with --docs)
    [MarkoutIgnore]
    public DocComment? Documentation { get; set; }
}
