using System.Text.Json.Serialization;
using Markout;

namespace DotnetInspector;

/// <summary>
/// Represents extracted documentation comments from source code.
/// </summary>
public class DocComment
{
    public string? Summary { get; set; }
    public string? Remarks { get; set; }

    internal Dictionary<string, string>? Parameters { get; set; }
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
public class PartialSourceFileInfo
{
    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }
    
    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; set; }
    
    [JsonPropertyName("github_browse_url")]
    public string? GitHubBrowseUrl { get; set; }
}

[MarkoutSerializable(TitleProperty = nameof(Name), AutoFields = false)]
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
    [MarkoutIgnore]
    public int PublicTypeCount { get; set; }

    [MarkoutPropertyName("Public Methods")]
    [MarkoutIgnore]
    public int PublicMethodCount { get; set; }

    [MarkoutPropertyName("Public Properties")]
    [MarkoutIgnore]
    public int PublicPropertyCount { get; set; }

    [MarkoutPropertyName("Public Events")]
    [MarkoutIgnore]
    public int PublicEventCount { get; set; }

    [MarkoutPropertyName("Public Fields")]
    [MarkoutIgnore]
    public int PublicFieldCount { get; set; }

    /// <summary>
    /// Target framework moniker for the API surface.
    /// </summary>
    [MarkoutPropertyName("TFM")]
    [MarkoutIgnore]
    public string? Tfm { get; set; }

    /// <summary>
    /// Repository URL extracted from SourceLink (if available).
    /// </summary>
    [MarkoutPropertyName("Repository")]
    [MarkoutIgnore]
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

    // ===== Field Collections for Serializer =====

    /// <summary>
    /// Summary fields (rendered as key-value fields under the title).
    /// </summary>
    [JsonIgnore]
    [MarkoutIgnoreInTable]
    public List<MarkoutField> Summary => GetSummaryFields();

    private List<MarkoutField> GetSummaryFields()
    {
        var fields = new List<MarkoutField>();
        fields.Add(new("Types", PublicTypeCount.ToString()));
        fields.Add(new("Methods", PublicMethodCount.ToString()));
        fields.Add(new("Properties", PublicPropertyCount.ToString()));
        if (Tfm != null)
            fields.Add(new("TFM", Tfm));
        if (!string.IsNullOrEmpty(RepositoryUrl))
            fields.Add(new("Repository", RepositoryUrl));
        return fields;
    }
}

/// <summary>
/// View model for single-type rendering. Pre-computes all display values from ApiType + options.
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Title), DescriptionProperty = nameof(Description), AutoFields = false)]
public class ApiTypeView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] public string? Description { get; set; }

    // Backing data (all [MarkoutIgnore] for JSON/table serialization)
    [MarkoutIgnore] public string Kind { get; set; } = "";
    [MarkoutIgnore] public string? Modifiers { get; set; }
    [MarkoutIgnore] public string? BaseType { get; set; }
    [MarkoutIgnore] public string? TypeParametersInline { get; set; }
    [MarkoutIgnore] public string? Implements { get; set; }
    [MarkoutIgnore] public string? Assembly { get; set; }
    [MarkoutIgnore] public string? Package { get; set; }
    [MarkoutIgnore] public string? Version { get; set; }
    [MarkoutIgnore] public string? SamplesInfo { get; set; }

    /// <summary>
    /// Identity fields (rendered as key-value fields under the title).
    /// </summary>
    [JsonIgnore]
    [MarkoutIgnoreInTable]
    public List<MarkoutField> Identity => GetIdentityFields();

    /// <summary>
    /// Enum values without Description column (default).
    /// </summary>
    [MarkoutSection(Name = "Values", IgnoreProperty = nameof(EnumValueRow.Description))]
    [JsonIgnore]
    public List<EnumValueRow>? EnumValues { get; set; }

    /// <summary>
    /// Enum values with Description column (--docs).
    /// </summary>
    [MarkoutSection(Name = "Values")]
    [JsonIgnore]
    public List<EnumValueRow>? EnumValuesWithDocs { get; set; }

    /// <summary>
    /// Type parameters table (Normal+ verbosity). Null → section skipped.
    /// </summary>
    [MarkoutSection(Name = "Type Parameters")]
    [JsonIgnore]
    public List<TypeParameterRow>? TypeParameterRows { get; set; }

    /// <summary>
    /// Implemented interfaces (Detailed+ verbosity). Null → section skipped.
    /// </summary>
    [MarkoutSection(Name = "Interfaces")]
    [JsonIgnore]
    public List<InterfaceRow>? InterfaceRows { get; set; }

    /// <summary>
    /// Base class hierarchy (Detailed+ verbosity). Null → section skipped.
    /// </summary>
    [MarkoutSection(Name = "Baseclass")]
    [JsonIgnore]
    public List<BaseclassRow>? BaseclassRows { get; set; }

    private List<MarkoutField> GetIdentityFields()
    {
        var fields = new List<MarkoutField>();
        fields.Add(new("Kind", Kind));
        if (!string.IsNullOrEmpty(Modifiers))
            fields.Add(new("Modifiers", Modifiers));
        if (!string.IsNullOrEmpty(BaseType))
            fields.Add(new("Base", BaseType));
        if (!string.IsNullOrEmpty(TypeParametersInline))
            fields.Add(new("Type Parameters", TypeParametersInline));
        if (!string.IsNullOrEmpty(Assembly))
            fields.Add(new("Assembly", Assembly));
        if (!string.IsNullOrEmpty(Package))
            fields.Add(new("Package", Package));
        if (!string.IsNullOrEmpty(Version))
            fields.Add(new("Version", Version));
        if (!string.IsNullOrEmpty(SamplesInfo))
            fields.Add(new("Samples", SamplesInfo));
        return fields;
    }
}

/// <summary>
/// Represents a type forwarded to another assembly.
/// </summary>
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

public class ApiType
{
    public string? Namespace { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";  // class, struct, interface, enum, delegate

    [MarkoutPropertyName("Sealed")]
    [MarkoutSkipDefault]
    public bool IsSealed { get; set; }

    [MarkoutPropertyName("Abstract")]
    [MarkoutSkipDefault]
    public bool IsAbstract { get; set; }

    [MarkoutPropertyName("Static")]
    [MarkoutSkipDefault]
    public bool IsStatic { get; set; }

    [MarkoutPropertyName("Base Type")]
    public string? BaseType { get; set; }

    [MarkoutPropertyName("Interfaces")]
    [MarkoutJoin(", ")]
    public List<string>? Interfaces { get; set; }

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
public class EnumValueRow
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Description { get; set; }
}

[MarkoutSerializable]
public class TypeParameterRow
{
    public string Parameter { get; set; } = "";
    public string Constraints { get; set; } = "";
}

[MarkoutSerializable]
public class InterfaceRow
{
    public string Interface { get; set; } = "";
}

[MarkoutSerializable]
public class BaseclassRow
{
    public string Type { get; set; } = "";
}

public class ApiMember
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";  // method, property, field, event, constructor

    [MarkoutPropertyName("Return Type")]
    public string? ReturnType { get; set; }

    public string? Signature { get; set; }

    [MarkoutPropertyName("Static")]
    [MarkoutSkipDefault]
    public bool IsStatic { get; set; }

    [MarkoutPropertyName("Virtual")]
    [MarkoutSkipDefault]
    public bool IsVirtual { get; set; }

    [MarkoutPropertyName("Abstract")]
    [MarkoutSkipDefault]
    public bool IsAbstract { get; set; }

    [MarkoutPropertyName("Unsafe")]
    [MarkoutSkipDefault]
    public bool IsUnsafe { get; set; }

    /// <summary>
    /// True if this is an extension method.
    /// </summary>
    [MarkoutPropertyName("Extension")]
    [MarkoutSkipDefault]
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
