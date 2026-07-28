using System.Text.Json.Serialization;
using ILInspector.CSharp;

namespace ILInspector.Metadata;

/// <summary>
/// Represents extracted documentation comments from source code.
/// </summary>
public class DocComment
{
    public string? Summary { get; set; }
    public string? Remarks { get; set; }

    [JsonIgnore]
    public Dictionary<string, string>? Parameters { get; set; }
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

    public List<ApiSurfaceInspectionFailure> InspectionFailures { get; set; } = [];

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
    /// True if this assembly is a facade/type-forwarding assembly whose listed
    /// types were resolved from target assemblies.
    /// </summary>
    public bool IsTypeForwardingAssembly { get; set; }
}

public sealed record ApiSurfaceInspectionFailure(
    string Operation,
    int SubjectToken,
    MetadataTypeNameFailureMechanism Mechanism,
    string Kind,
    string Detail);

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
    /// Structured view of <see cref="Constraints"/> (same entries and order) that
    /// records, per entry, whether it is an attribute-derived special-constraint
    /// keyword (<c>class</c>, <c>struct</c>, <c>new()</c>, …) or a constraint type
    /// name. This distinction is a metadata fact the C# printer needs so it can
    /// escape reserved-keyword type names (a type literally named <c>class</c>
    /// renders as <c>@class</c>) without misreading them as keyword constraints.
    /// Populated by metadata producers; <see langword="null"/> when unavailable, in
    /// which case printers fall back to a token heuristic. Not serialized.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<TypeParameterConstraint>? StructuredConstraints { get; set; }

    /// <summary>
    /// Returns the parameter name with variance prefix (e.g., "out T", "in TKey").
    /// </summary>
    /// <remarks>
    /// This is presentation, not identity — <see cref="Name"/> stays raw — so the
    /// untrusted metadata name is contained here (issue #3319).
    /// </remarks>
    public string DisplayName => CSharpIdentifierCore.ContainComposedName(
        Variance != null ? $"{Variance} {Name}" : Name);

    /// <summary>
    /// Returns constraints as a comma-separated string, or null if none.
    /// </summary>
    public string? ConstraintsSummary => Constraints.Count > 0
        ? string.Join(", ", Constraints)
        : null;
}

/// <summary>
/// One generic-parameter constraint paired with the metadata fact of whether it is
/// an attribute-derived special-constraint keyword (<see cref="IsTypeName"/> is
/// <see langword="false"/> — e.g. <c>class</c>, <c>struct</c>, <c>new()</c>) or a
/// constraint type name (<see cref="IsTypeName"/> is <see langword="true"/>). The
/// C# printer uses this to escape reserved-keyword type names while leaving keyword
/// constraints untouched.
/// </summary>
public readonly record struct TypeParameterConstraint(string Value, bool IsTypeName);

public class ApiSignature
{
    public string? ReturnType { get; set; }
    public string? CanonicalReturnType { get; set; }
    public List<string> ReturnAttributes { get; set; } = [];
    public string? MemberName { get; set; }
    public bool IsRequired { get; set; }
    public List<TypeParameter> TypeParameters { get; set; } = [];
    public List<ApiParameter> Parameters { get; set; } = [];
    public List<ApiAccessor> Accessors { get; set; } = [];

    public int ParameterCount => Parameters.Count;

    public string ParameterTypesSummary => Parameters.Count == 0
        ? ""
        : $"({string.Join(", ", Parameters.Select(parameter => parameter.TypeWithModifier))})";

    /// <summary>
    /// The presentation-independent parameter list used for member identity: identical
    /// to <see cref="ParameterTypesSummary"/> except that tuple element names and C#
    /// tuple syntax are erased (<c>System.ValueTuple&lt;int, string&gt;</c> rather than
    /// <c>(int count, string name)</c>). For members with no tuple parameters this equals
    /// <see cref="ParameterTypesSummary"/> character-for-character.
    /// </summary>
    public string CanonicalParameterTypesSummary => Parameters.Count == 0
        ? ""
        : $"({string.Join(", ", Parameters.Select(parameter => parameter.CanonicalTypeWithModifier))})";

    /// <summary>
    /// The canonical (tuple-erased) return-type spelling used for identity; falls back to
    /// <see cref="ReturnType"/> when a canonical spelling was not recorded.
    /// </summary>
    public string? EffectiveCanonicalReturnType =>
        string.IsNullOrEmpty(CanonicalReturnType) ? ReturnType : CanonicalReturnType;

    public List<(string name, string type, bool hasDefault)> ParameterInfoSummary => Parameters
        .Select(parameter => (parameter.Name, parameter.TypeWithModifier, parameter.HasDefault))
        .ToList();

    public string PublicAccessorsSummary => string.Join(", ",
        Accessors
            .Where(accessor => string.IsNullOrEmpty(accessor.Accessibility))
            .Select(accessor => accessor.Kind));
}

public class ApiParameter
{
    public List<string> Attributes { get; set; } = [];
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? CanonicalType { get; set; }
    public string? Modifier { get; set; }
    public bool HasDefault { get; set; }
    public string? DefaultValueText { get; set; }

    public string TypeWithModifier => string.IsNullOrEmpty(Modifier)
        ? Type
        : $"{Modifier} {Type}";

    /// <summary>
    /// The canonical (tuple-erased) parameter type; falls back to <see cref="Type"/> when a
    /// canonical spelling was not recorded, so a non-tuple parameter's canonical spelling
    /// equals its display spelling.
    /// </summary>
    public string EffectiveCanonicalType =>
        string.IsNullOrEmpty(CanonicalType) ? Type : CanonicalType!;

    /// <summary>Canonical type composed with its by-ref/params modifier, mirroring
    /// <see cref="TypeWithModifier"/> but tuple-erased for identity.</summary>
    public string CanonicalTypeWithModifier => string.IsNullOrEmpty(Modifier)
        ? EffectiveCanonicalType
        : $"{Modifier} {EffectiveCanonicalType}";
}

public class ApiAccessor
{
    public string Kind { get; set; } = "";
    public string? Accessibility { get; set; }
    public List<string> ReturnAttributes { get; set; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter<SignatureDecodeStatus>))]
public enum SignatureDecodeStatus
{
    Degraded
}

public class ApiType
{
    public string? Namespace { get; set; }
    public string Name { get; set; } = "";
    
    /// <summary>
    /// The exact metadata name, preserving literal '+' characters and using '+'
    /// to delimit nested types, matching how TypeRef constructs its names.
    /// Null in older serialized surfaces.
    /// </summary>
    public string? MetadataName { get; set; }
    
    /// <summary>
    /// Access level for non-public types. Null means public, including for older
    /// serialized surfaces; C# rendering preserves that compatibility fallback.
    /// </summary>
    public string? Accessibility { get; set; }
    public string Kind { get; set; } = "";  // class, struct, interface, enum, delegate
    public List<string> Attributes { get; set; } = [];

    /// <summary>The C# enum underlying type, captured from the special <c>value__</c> field.</summary>
    public string? EnumUnderlyingType { get; set; }

    public bool IsSealed { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsStatic { get; set; }

    /// <summary>
    /// A byref-like struct (<c>ref struct</c>) — carries <c>[IsByRefLike]</c>, which
    /// is suppressed from the attribute list as compiler-synthesized syntax, so the
    /// modifier is reconstructed here instead.
    /// </summary>
    public bool IsByRefLike { get; set; }

    /// <summary>A <c>readonly struct</c> — carries the likewise-suppressed <c>[IsReadOnly]</c>.</summary>
    public bool IsReadOnly { get; set; }

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
    /// Assembly path that supplied this type's metadata, when extracted from a file.
    /// Used internally to route body/evidence sections for forwarded platform types.
    /// </summary>
    [JsonIgnore]
    public string? SourceAssemblyPath { get; set; }

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
    public List<string> Attributes { get; set; } = [];

    public string? ReturnType { get; set; }
    public string? Signature { get; set; }

    /// <summary>
    /// Durable 10-char digest for this overload — the same value shown in the Markdown
    /// Digest column and the <c>Name~digest</c> stable selector. Lets JSON consumers
    /// address the exact overload without parsing the display table. Populated for the
    /// type/member JSON output; null (and omitted) when identity was not projected.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Digest { get; set; }

    /// <summary>
    /// The persisted, presentation-independent canonical member identity — the Member Index
    /// digest input (e.g. <c>M:N.C.Parse(System.ValueTuple&lt;int,string&gt;)</c>). Populated
    /// at extraction when it diverges from what parsing the display
    /// <see cref="Signature"/> would yield — i.e. for members whose signature contains C#
    /// tuple syntax, whose element names and <c>(...)</c> spelling must not leak into
    /// identity and cannot be recovered from the display text after a JSON round-trip
    /// (<see cref="SignatureModel"/> is <see cref="JsonIgnoreAttribute"/>) — and also filled
    /// in for the type/member JSON output so consumers get durable identity without a side
    /// call. Omitted when null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CanonicalSignature { get; set; }

    [JsonIgnore]
    public ApiSignature? SignatureModel { get; set; }

    /// <summary>
    /// Set when guarded metadata decoding substituted part of this member's signature.
    /// Null means the signature decoded completely, including for older serialized surfaces.
    /// </summary>
    public SignatureDecodeStatus? SignatureDecodeStatus { get; set; }

    /// <summary>
    /// MethodDef metadata token for method-like members when known.
    /// Used for stable body evidence lookups such as call-site sections.
    /// </summary>
    public int? MetadataToken { get; set; }

    /// <summary>
    /// MethodDef tokens of a property's get/set accessors when known. Lets accessor-level
    /// call-graph rows (e.g. <c>get_Foo</c>) map back to the owning property's selector.
    /// </summary>
    public int? GetterToken { get; set; }
    public int? SetterToken { get; set; }

    /// <summary>
    /// MethodDef tokens of an event's add/remove accessors when known. Serialized (like
    /// <see cref="GetterToken"/>/<see cref="SetterToken"/>) so JSON consumers can address an
    /// event's accessor bodies; omitted for members that expose no such accessor.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AdderToken { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RemoverToken { get; set; }

    public bool IsStatic { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsOverride { get; set; }
    public bool IsSealed { get; set; }

    /// <summary>
    /// True when this method is a class finalizer — the <c>object.Finalize</c>
    /// override the C# <c>~Type()</c> destructor syntax compiles to. Detected
    /// from the method's explicit <c>.override</c> MethodImpl targeting
    /// <c>System.Object::Finalize</c>, so it is judged by the overridden slot
    /// rather than by name or signature shape. Lets the C# writer spell it
    /// <c>~Type()</c> instead of the bare <c>void Finalize()</c>. A metadata fact
    /// (the overridden slot), separate from the C# spelling the writer owns.
    /// Emitted only when true: the finalizer identity is already carried by the
    /// dedicated <c>Kind = "finalizer"</c>, so serializing <c>is_finalizer: false</c>
    /// on every other member would be redundant schema noise.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsFinalizer { get; set; }

    public bool IsReadOnly { get; set; }
    public bool IsConst { get; set; }
    public bool IsUnsafe { get; set; }
    public bool IsAsync { get; set; }

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

    /// <summary>
    /// Render-only selector index override for filtered member inventories whose visible order
    /// is narrower than the declaring overload set.
    /// </summary>
    [JsonIgnore]
    public int? SelectorOverloadIndex { get; set; }

    // Enum value (for enum fields only)
    public long? EnumValue { get; set; }

    /// <summary>Lossless decimal literal text for enum constants, including unsigned 64-bit values.</summary>
    public string? EnumValueLiteral { get; set; }

    // Source information (populated with --source-url)
    public string? SourceFilePath { get; set; }

    public string? SourceUrl { get; set; }

    public int? SourceLineNumber { get; set; }

    public int? SourceEndLineNumber { get; set; }

    // Documentation (populated with --docs)
    public DocComment Documentation { get; set; } = new();
}
