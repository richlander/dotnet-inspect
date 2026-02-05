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

/// <summary>
/// Represents a source file that is part of a partial type definition.
/// </summary>
[MarkoutSerializable]
public class PartialSourceFileInfo
{
    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }
    
    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; set; }
    
    [JsonPropertyName("github_browse_url")]
    public string? GitHubBrowseUrl { get; set; }
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

    /// <summary>
    /// Type forwarders in this assembly (types re-exported from other assemblies).
    /// </summary>
    [MarkoutIgnore]
    public List<TypeForwarder> TypeForwarders { get; set; } = [];

    /// <summary>
    /// True if this assembly is a type-forwarding assembly (types resolved from target assemblies).
    /// </summary>
    [MarkoutIgnore]
    public bool IsTypeForwardingAssembly { get; set; }
}

/// <summary>
/// Represents a type forwarded to another assembly.
/// </summary>
[MarkoutSerializable]
public class TypeForwarder
{
    /// <summary>
    /// Full name of the forwarded type.
    /// </summary>
    public string TypeName { get; set; } = "";

    /// <summary>
    /// Name of the target assembly where the type is defined.
    /// </summary>
    public string TargetAssembly { get; set; } = "";
}

/// <summary>
/// Represents a generic type parameter with its constraints.
/// </summary>
[MarkoutSerializable]
public class TypeParameter
{
    /// <summary>
    /// The name of the type parameter (e.g., "T", "TKey").
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Variance modifier: "out" (covariant), "in" (contravariant), or null.
    /// </summary>
    public string? Variance { get; set; }

    /// <summary>
    /// List of constraints on this type parameter.
    /// Includes special constraints (class, struct, notnull, unmanaged, new())
    /// and type constraints (interfaces, base class).
    /// </summary>
    [MarkoutIgnore]
    public List<string> Constraints { get; set; } = [];

    /// <summary>
    /// Returns the parameter name with variance prefix (e.g., "out T", "in TKey").
    /// </summary>
    [MarkoutIgnore]
    [JsonIgnore]
    public string DisplayName => Variance != null ? $"{Variance} {Name}" : Name;

    /// <summary>
    /// Returns constraints as a comma-separated string, or null if none.
    /// </summary>
    [MarkoutIgnore]
    [JsonIgnore]
    public string? ConstraintsSummary => Constraints.Count > 0
        ? string.Join(", ", Constraints)
        : null;
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

    /// <summary>
    /// Known derived types within the same assembly.
    /// </summary>
    [MarkoutIgnore]
    public List<string>? DerivedTypes { get; set; }

    /// <summary>
    /// Generic type parameters with their constraints.
    /// </summary>
    [MarkoutIgnore]
    public List<TypeParameter>? TypeParameters { get; set; }

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

    /// <summary>
    /// How the source URL was resolved: "SourceLink" (from method debug info) or "Inferred" (from document name).
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("source_resolution")]
    public string? SourceResolution { get; set; }
    
    /// <summary>
    /// Additional source files for partial types. Only populated when type spans multiple files.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("additional_source_files")]
    public List<PartialSourceFileInfo>? AdditionalSourceFiles { get; set; }
    
    /// <summary>
    /// Indicates whether this type is defined across multiple partial files.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("is_partial_type")]
    public bool IsPartialType => AdditionalSourceFiles?.Count > 0;

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

    /// <summary>
    /// True if this is an extension method.
    /// </summary>
    [MarkoutPropertyName("Extension")]
    public bool IsExtension { get; set; }

    /// <summary>
    /// The type that this extension method extends (first parameter type).
    /// Only populated when IsExtension is true.
    /// </summary>
    [MarkoutIgnore]
    [JsonPropertyName("extended_type")]
    public string? ExtendedType { get; set; }

    // Enum value (for enum fields only)
    [MarkoutIgnore]
    [JsonPropertyName("enum_value")]
    public long? EnumValue { get; set; }

    // Source information (populated with --source-url)
    [MarkoutIgnore]
    [JsonPropertyName("source_line_number")]
    public int? SourceLineNumber { get; set; }

    // Documentation (populated with --docs)
    [MarkoutIgnore]
    public DocComment? Documentation { get; set; }
}
