using System.Text.Json.Serialization;

namespace DotnetInspector.Metadata;

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
    public List<SampleReference> Samples { get; set; } = [];
}

/// <summary>
/// Represents a reference to sample code in the same repository.
/// </summary>
public class SampleReference
{
    /// <summary>
    /// Relative path to the sample file from the source file.
    /// </summary>
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
    public string? ResolvedUrl { get; set; }

    /// <summary>
    /// Fetched sample content (populated when --inline is used).
    /// </summary>
    public string? Content { get; set; }
}

/// <summary>
/// Represents a source file that is part of a partial type definition.
/// </summary>
public class PartialSourceFileInfo
{
    public string? FilePath { get; set; }

    public string? SourceUrl { get; set; }

    public string? GitHubBrowseUrl { get; set; }
}

/// <summary>
/// Represents the extracted public API surface of an assembly.
/// </summary>
public class ApiSurface
{
    /// <summary>
    /// Package or assembly name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Package or assembly version.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Where the assembly was loaded from (e.g., "nuget", "platform", "local").
    /// </summary>
    [JsonIgnore]
    public string? Source { get; set; }

    public List<ApiType> Types { get; set; } = [];

    public int PublicTypeCount { get; set; }
    public int PublicMethodCount { get; set; }
    public int PublicPropertyCount { get; set; }
    public int PublicEventCount { get; set; }
    public int PublicFieldCount { get; set; }

    /// <summary>
    /// The assembly file name (e.g., "Foo.dll").
    /// </summary>
    [JsonIgnore]
    public string? Library { get; set; }

    /// <summary>
    /// Target framework moniker for the API surface.
    /// </summary>
    public string? Tfm { get; set; }

    /// <summary>
    /// Repository URL extracted from SourceLink (if available).
    /// </summary>
    public string? RepositoryUrl { get; set; }

    /// <summary>
    /// Type forwarders in this assembly (types re-exported from other assemblies).
    /// </summary>
    public List<TypeForwarder> TypeForwarders { get; set; } = [];

    /// <summary>
    /// True if this assembly is a type-forwarding assembly (types resolved from target assemblies).
    /// </summary>
    public bool IsTypeForwardingAssembly { get; set; }
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
    public List<string> Constraints { get; set; } = [];

    /// <summary>
    /// Returns the parameter name with variance prefix (e.g., "out T", "in TKey").
    /// </summary>
    public string DisplayName => Variance != null ? $"{Variance} {Name}" : Name;

    /// <summary>
    /// Returns constraints as a comma-separated string, or null if none.
    /// </summary>
    public string? ConstraintsSummary => Constraints.Count > 0
        ? string.Join(", ", Constraints)
        : null;
}

public class ApiType
{
    public string? Namespace { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";  // class, struct, interface, enum, delegate

    public bool IsSealed { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsStatic { get; set; }

    public string? BaseType { get; set; }
    public List<string> Interfaces { get; set; } = [];

    /// <summary>
    /// Known derived types within the same assembly.
    /// </summary>
    public List<string> DerivedTypes { get; set; } = [];

    /// <summary>
    /// Generic type parameters with their constraints.
    /// </summary>
    public List<TypeParameter> TypeParameters { get; set; } = [];

    public List<ApiMember> Members { get; set; } = [];

    // Source information (populated with --source-url)
    public string? SourceFilePath { get; set; }

    public string? SourceUrl { get; set; }

    public string? GitHubBrowseUrl { get; set; }

    public int? SourceLineNumber { get; set; }

    /// <summary>
    /// How the source URL was resolved: "SourceLink" (from method debug info) or "Inferred" (from document name).
    /// </summary>
    public string? SourceResolution { get; set; }

    /// <summary>
    /// Additional source files for partial types. Only populated when type spans multiple files.
    /// </summary>
    public List<PartialSourceFileInfo> AdditionalSourceFiles { get; set; } = [];

    /// <summary>
    /// Indicates whether this type is defined across multiple partial files.
    /// </summary>
    public bool IsPartialType => AdditionalSourceFiles.Count > 0;

    /// <summary>
    /// True if this type was resolved from a type forwarder in another assembly.
    /// </summary>
    public bool IsForwarded { get; set; }

    /// <summary>
    /// Full name of the type (Namespace.Name, or just Name if no namespace).
    /// </summary>
    public string FullName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";

    // Documentation (populated with --docs)
    public DocComment Documentation { get; set; } = new();
}

public class ApiMember
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";  // method, property, field, event, constructor, operator, explicit-interface-implementation, extension-method

    public string? ReturnType { get; set; }
    public string? Signature { get; set; }

    public bool IsStatic { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsUnsafe { get; set; }

    /// <summary>
    /// Access level for non-public members (e.g., "private", "protected", "internal").
    /// Null for public members.
    /// </summary>
    public string? Accessibility { get; set; }

    /// <summary>
    /// True if this is an extension method.
    /// </summary>
    public bool IsExtension { get; set; }

    /// <summary>
    /// True if the member carries an [Obsolete] attribute.
    /// </summary>
    public bool IsObsolete { get; set; }

    /// <summary>
    /// Optional deprecation message from [Obsolete("...")]. Null when the
    /// attribute is missing or has no message argument.
    /// </summary>
    public string? ObsoleteMessage { get; set; }

    /// <summary>
    /// The type that this extension method extends (first parameter type).
    /// Only populated when IsExtension is true.
    /// </summary>
    public string? ExtendedType { get; set; }

    /// <summary>
    /// The type that declares this member when it is shown on another type, such as a local
    /// extension method projected onto its extended type.
    /// </summary>
    public string? DeclaringType { get; set; }

    /// <summary>
    /// 1-based overload index in <see cref="DeclaringType"/> for projected members.
    /// </summary>
    public int? DeclaringOverloadIndex { get; set; }

    // Enum value (for enum fields only)
    public long? EnumValue { get; set; }

    // Source information (populated with --source-url)
    public int? SourceLineNumber { get; set; }

    // Documentation (populated with --docs)
    public DocComment Documentation { get; set; } = new();
}
