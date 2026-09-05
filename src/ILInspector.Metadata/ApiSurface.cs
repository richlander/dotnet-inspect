using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text.Json.Serialization;
using CSharpText;
using ILInspector.Findings;

namespace ILInspector.Metadata;

/// <summary>
/// Containment for XML documentation text.
/// </summary>
/// <remarks>
/// Doc comments are read from an untrusted assembly's companion XML file, and
/// their text is rendered into Markdown prose and table cells. A summary
/// carrying a line terminator, ANSI escape, or bidi override breaks out of its
/// cell and injects text that reads as genuine tool output (issue #3319). Doc
/// text is prose only -- no consumer matches, keys, or compares it -- so unlike
/// <see cref="ApiMember.Signature"/> it can be contained at the model rather
/// than at each of its ~40 render sites.
/// </remarks>
internal static class DocText
{
    [return: NotNullIfNotNull(nameof(value))]
    public static string? Contain(string? value)
        => value is null ? null : CSharpIdentifierCore.ContainComposedName(value);
}

/// <summary>
/// Represents extracted documentation comments from source code.
/// </summary>
public class DocComment
{
    /// <inheritdoc cref="DocText"/>
    public string? Summary { get => field; set => field = DocText.Contain(value); }

    /// <inheritdoc cref="DocText"/>
    public string? Remarks { get => field; set => field = DocText.Contain(value); }

    /// <summary>
    /// Parameter documentation, keyed by parameter name.
    /// </summary>
    /// <remarks>
    /// The key is deliberately left raw. It is a parameter name used to look
    /// documentation up, not display text, and it is never rendered — this
    /// dictionary is <see cref="JsonIgnoreAttribute"/>d and its only consumers
    /// merge it. Containing the key was containment applied to identity, which
    /// is the one thing #3319 must not do: containment folds line endings, so
    /// two distinct <c>&lt;param name&gt;</c> values in an attacker-supplied XML
    /// doc file collapsed to one key and <c>ToDictionary</c> threw, ending the
    /// inspection with an error instead of output. The value is contained
    /// because it is prose that may reach output.
    /// </remarks>
    [JsonIgnore]
    public Dictionary<string, string>? Parameters
    {
        get => field;
        set => field = value is null
            ? null
            : value.ToDictionary(e => e.Key, e => DocText.Contain(e.Value));
    }

    /// <inheritdoc cref="DocText"/>
    public string? Returns { get => field; set => field = DocText.Contain(value); }

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
    /// Resolved URL to the sample file (populated by a higher inspection layer).
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

    [JsonIgnore]
    public byte[]? SourceChecksum { get; set; }

    [JsonIgnore]
    public string? SourceChecksumAlgorithm { get; set; }
}

/// <summary>
/// Represents the extracted public API surface of an assembly.
/// </summary>
public class ApiSurface
{
    public const string ConstraintResolutionOperation =
        ApiSurfaceInspectionFailure
            .GenericParameterConstraintResolutionOperation;
    public const int MaxVisibleConstraintResolutionFailures = 64;

    readonly HashSet<ConstraintResolutionVisibleKey>
        _constraintResolutionVisibleKeys = [];
    int _constraintResolutionSummaryIndex = -1;
    int _suppressedConstraintResolutionFailureCount;

    [JsonIgnore]
    public ApiAssemblyIdentity? AssemblyIdentity { get; set; }

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

    [JsonIgnore]
    public List<FilteredRuntimeJsExportFact> FilteredRuntimeJsExportFacts
        { get; set; } = [];

    [JsonPropertyName("filtered_runtime_js_export_facts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FilteredRuntimeJsExportFact>? FilteredRuntimeJsExportEvidence
    {
        get => FilteredRuntimeJsExportFacts.Count == 0
            ? null
            : FilteredRuntimeJsExportFacts;
        set => FilteredRuntimeJsExportFacts = value ?? [];
    }

    public List<ApiSurfaceInspectionFailure> InspectionFailures { get; set; } = [];

    [JsonIgnore]
    public Dictionary<
        ApiSurfaceInspectionSubject,
        List<ApiSurfaceInspectionFailure>>
        ConstraintResolutionFailuresBySubject { get; } = [];

    public void AddConstraintResolutionFailure(
        ApiSurfaceInspectionSubject subject,
        ApiSurfaceInspectionFailure failure)
    {
        if (!ConstraintResolutionFailuresBySubject.TryGetValue(
                subject,
                out List<ApiSurfaceInspectionFailure>? subjectFailures))
        {
            subjectFailures = [];
            ConstraintResolutionFailuresBySubject.Add(
                subject,
                subjectFailures);
        }
        if (!subjectFailures.Any(existing =>
            existing.Mechanism == failure.Mechanism
            && existing.Kind == failure.Kind
            && existing.DependencyAssembly
                == failure.DependencyAssembly
            && existing.Detail == failure.Detail))
        {
            subjectFailures.Add(failure);
        }

        AddVisibleConstraintResolutionFailure(failure);
    }

    public void ReprojectConstraintResolutionFailures(
        Func<ApiSurfaceInspectionSubject, bool> includeSubject)
    {
        ArgumentNullException.ThrowIfNull(includeSubject);
        InspectionFailures.RemoveAll(
            static failure =>
                failure.Operation == ConstraintResolutionOperation);
        _constraintResolutionVisibleKeys.Clear();
        _constraintResolutionSummaryIndex = -1;
        _suppressedConstraintResolutionFailureCount = 0;

        foreach (var (subject, failures)
            in ConstraintResolutionFailuresBySubject)
        {
            if (!includeSubject(subject))
                continue;

            foreach (ApiSurfaceInspectionFailure failure in failures)
                AddVisibleConstraintResolutionFailure(failure);
        }
    }

    void AddVisibleConstraintResolutionFailure(
        ApiSurfaceInspectionFailure failure)
    {
        var key = new ConstraintResolutionVisibleKey(
            failure.SubjectAssembly,
            failure.DependencyAssembly,
            failure.Mechanism,
            failure.Kind,
            failure.Detail);
        if (!_constraintResolutionVisibleKeys.Add(key))
            return;

        if (_constraintResolutionVisibleKeys.Count
            <= MaxVisibleConstraintResolutionFailures)
        {
            InspectionFailures.Add(failure);
            return;
        }

        _suppressedConstraintResolutionFailureCount++;
        var summary = new ApiSurfaceInspectionFailure(
            ConstraintResolutionOperation,
            0,
            MetadataTypeNameFailureMechanism.Metadata,
            "ResourceLimit",
            $"{_suppressedConstraintResolutionFailureCount} additional "
                + "distinct generic-constraint resolution failure(s) "
                + "were suppressed.");
        if (_constraintResolutionSummaryIndex < 0)
        {
            _constraintResolutionSummaryIndex =
                InspectionFailures.Count;
            InspectionFailures.Add(summary);
        }
        else
        {
            InspectionFailures[
                _constraintResolutionSummaryIndex] = summary;
        }
    }

    public void MergeInspectionFailuresFrom(
        ApiSurface source,
        Func<ApiSurfaceInspectionSubject, bool>? includeConstraintSubject =
            null,
        bool includeNonConstraintFailures = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (includeNonConstraintFailures)
        {
            foreach (ApiSurfaceInspectionFailure failure
                in source.InspectionFailures)
            {
                if (failure.Operation != ConstraintResolutionOperation)
                    InspectionFailures.Add(failure);
            }
        }

        foreach (var (subject, failures)
            in source.ConstraintResolutionFailuresBySubject)
        {
            if (includeConstraintSubject is not null
                && !includeConstraintSubject(subject))
            {
                continue;
            }

            foreach (ApiSurfaceInspectionFailure failure in failures)
                AddConstraintResolutionFailure(subject, failure);
        }
    }

    public void SetInspectionSourceAssemblyPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        foreach (ApiType type in Types)
            type.SourceAssemblyPath = path;
        for (int i = 0; i < InspectionFailures.Count; i++)
        {
            InspectionFailures[i] = InspectionFailures[i] with
            {
                SourceAssemblyPath = path,
            };
        }

        var constraintFailures =
            ConstraintResolutionFailuresBySubject.ToArray();
        ConstraintResolutionFailuresBySubject.Clear();
        foreach (var (subject, failures) in constraintFailures)
        {
            for (int i = 0; i < failures.Count; i++)
            {
                failures[i] = failures[i] with
                {
                    SourceAssemblyPath = path,
                };
            }
            ConstraintResolutionFailuresBySubject[
                new ApiSurfaceInspectionSubject(
                    path,
                    subject.SubjectToken)] = failures;
        }
    }

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
    /// Repository URL populated by a higher inspection layer, if available.
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

    /// <summary>
    /// Typed classification evidence retained for consumers that must
    /// distinguish a non-facade surface from a failed classification.
    /// </summary>
    [JsonIgnore]
    public AssemblySurfaceClassificationOutcome? SurfaceClassification { get; set; }

    [JsonIgnore]
    public FindingInspection<AssemblySurfaceClassification>?
        SurfaceClassificationInspection { get; set; }

    readonly record struct ConstraintResolutionVisibleKey(
        AssemblyReferenceIdentity? SubjectAssembly,
        AssemblyReferenceIdentity? DependencyAssembly,
        MetadataTypeNameFailureMechanism Mechanism,
        string Kind,
        string Detail);
}

public sealed record ApiSurfaceInspectionFailure(
    string Operation,
    int SubjectToken,
    MetadataTypeNameFailureMechanism Mechanism,
    string Kind,
    string Detail,
    AssemblyReferenceIdentity? SubjectAssembly = null,
    AssemblyReferenceIdentity? DependencyAssembly = null)
{
    public const string
        GenericParameterConstraintResolutionOperation =
            "resolve generic parameter constraints";
    public const string EnumAttributeTypeIndexOperation =
        "enum attribute type index";
    public const string TypeForwarderIdentityOperation =
        "type forwarder identity";
    public const string TypeForwarderRowOperation =
        "type forwarder row";
    internal const string UnmarkedAssemblyForwarderDetail =
        "The selected image has an AssemblyRef-terminated ExportedType "
            + "chain that is not a forwarder.";

    [JsonIgnore]
    public string? SourceAssemblyPath { get; init; }

    [JsonIgnore]
    public int? OwningTypeToken { get; init; }

    [JsonIgnore]
    public MetadataTypeDefinitionName? OwningTypeDefinition { get; init; }

    [JsonIgnore]
    public ImmutableArray<MetadataTypeDefinitionName>
        AffectedTypeDefinitions { get; init; } = [];
}

public readonly record struct ApiSurfaceInspectionSubject(
    string? SourceAssemblyPath,
    int SubjectToken);

/// <summary>
/// Represents a type forwarded to another assembly.
/// </summary>
public class TypeForwarder
{
    /// <summary>
    /// Exact metadata lookup name retained for structured definition resolution.
    /// It is omitted from serialized API surfaces, which predate this currency.
    /// </summary>
    [JsonIgnore]
    public MetadataTypeDefinitionName? DefinitionName { get; set; }

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
    /// Whether the constraint set proves this type parameter is a reference type, a
    /// value type, or neither — the metadata fact behind C#'s "known to be a reference
    /// type" rule, which is not the same question as which constraint keywords are
    /// present. A named <em>class</em> constraint proves reference-ness without any
    /// keyword, while <c>System.Enum</c> is a class that proves nothing, because a type
    /// parameter constrained to it may still be a value type.
    /// </summary>
    /// <remarks>
    /// Consumers need this to decide the one constraint an <c>override</c> may restate,
    /// which is what disambiguates <c>T?</c> between a nullable reference type and
    /// <see cref="System.Nullable{T}"/>. Populated by metadata producers and left at
    /// <see cref="TypeParameterTypeKind.Undetermined"/> when a constraint type could not
    /// be classified — an external <see cref="System.Reflection.Metadata.TypeReference"/>
    /// whose interface flag this assembly cannot read, or a signature the blob guards
    /// refused to decode. Undetermined is the fail-closed default, so a producer that
    /// does not populate it reads as "do not know" rather than as "neither". Not
    /// serialized.
    /// </remarks>
    [JsonIgnore]
    public TypeParameterTypeKind TypeKind { get; set; } = TypeParameterTypeKind.Undetermined;

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

/// <summary>
/// Whether a type parameter's constraints prove it is a reference type, a value type,
/// or neither. This mirrors the rule C# uses for "known to be a reference type" — the
/// reference-type constraint flag, or an effective base class other than
/// <c>System.Object</c>, <c>System.ValueType</c> and <c>System.Enum</c> — rather than
/// the surface spelling of the constraint list.
/// </summary>
public enum TypeParameterTypeKind
{
    /// <summary>
    /// A constraint type could not be classified, so nothing is proven either way. The
    /// fail-closed default: an unpopulated or degraded read must never be mistaken for
    /// <see cref="NeitherReferenceNorValue"/>, which is a positive finding.
    /// </summary>
    Undetermined = 0,

    /// <summary>
    /// Every constraint was classified and none proves the parameter is a reference or
    /// a value type — an unconstrained parameter, or one constrained only by interfaces,
    /// <c>notnull</c>, <c>new()</c> or <c>System.Enum</c>.
    /// </summary>
    NeitherReferenceNorValue,

    /// <summary>Proven a reference type, by the constraint flag or a class constraint.</summary>
    ReferenceType,

    /// <summary>Proven a value type, by the constraint flag (<c>struct</c> or <c>unmanaged</c>).</summary>
    ValueType,
}

public class ApiSignature
{
    internal string? ExtensionReceiverType { get; set; }
    public string? ReturnType { get; set; }
    public string? CanonicalReturnType { get; set; }

    /// <summary>
    /// Opaque structural return-type identity for call-graph selectors. Null on
    /// older serialized surfaces and members whose normalized display spelling
    /// already supplies the complete selector identity.
    /// </summary>
    public string? StructuralReturnType { get; set; }

    [JsonIgnore]
    public List<ApiTypeReferenceIdentity> ReturnTypeReferences { get; set; } = [];

    [JsonIgnore]
    public ApiTypeReferenceIdentity? ReturnTypeDefinitionReference
        { get; set; }

    /// <summary>
    /// Exact signature shape retained for consumers that must compare a type
    /// argument rather than its display spelling or named references.
    /// </summary>
    [JsonIgnore]
    public ApiTypeShape? ReturnTypeShape { get; set; }

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

    [JsonIgnore]
    public List<ApiTypeReferenceIdentity> TypeReferences
        { get; set; } = [];

    /// <summary>
    /// Opaque structural parameter-type identity for call-graph selectors. Null on
    /// older serialized surfaces and parameters whose normalized display spelling
    /// already supplies the complete selector identity.
    /// </summary>
    public string? StructuralType { get; set; }

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

    /// <summary>
    /// The accessor MethodDef name. Ordinary properties use <c>get_Value</c>;
    /// explicit-interface properties use <c>I.get_Value</c>, which is not
    /// <c>get_</c> prefixed onto the property display name. Null on older
    /// serialized surfaces.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Opaque structural return-type identity for the accessor method itself.
    /// Null when the display spelling is already injective, including ordinary
    /// <c>void</c> setters. <c>init</c> setters carry
    /// <c>modreq(IsExternalInit)</c> here so call-graph selectors match MemberRef.
    /// </summary>
    public string? StructuralReturnType { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<SignatureDecodeStatus>))]
public enum SignatureDecodeStatus
{
    Degraded
}

/// <summary>
/// Why a field carrying <c>[JsonPropertyName]</c> is absent from the declarable
/// <see cref="ApiMember"/> list.
/// </summary>
public enum FilteredJsonPropertyNameKind
{
    AutoPropertyBackingField,
    EventBackingField,
    CompilerNamedField,
}

/// <summary>
/// A JSON-name attribute retained from a metadata field that API-surface
/// reconstruction deliberately folds or filters.
/// </summary>
public sealed record FilteredJsonPropertyNameFact(
    FilteredJsonPropertyNameKind Kind,
    string? AssociatedMemberName,
    int MetadataToken,
    List<string?> PropertyNames);

/// <summary>
/// Authentic <c>[JSExport]</c> evidence retained from a MethodDef that API
/// surface extraction deliberately omits, such as an accessor or local
/// function. Keeping this outside <see cref="ApiType.Members"/> preserves the
/// API model while preventing a runtime export claim from disappearing.
/// </summary>
/// <remarks>
/// <c>JsExportSurfaceBuilderTests.Extract_RetainsFilteredJsExportMethodDefsAsFailureEvidence</c>
/// and
/// <c>ApiOutputFormatterTests.ApiTypeJson_RoundTripsRuntimeJsExportFailureEvidence</c>
/// gate extraction and persistence.
/// </remarks>
public sealed record FilteredRuntimeJsExportFact(
    string MethodName,
    int MetadataToken,
    int AttributeCount,
    bool HasValidRow,
    bool HasMalformedRow);

/// <summary>
/// Exact MethodDef evidence associating a generated runtime wrapper with its
/// unique generated registration method and decoded registration count.
/// </summary>
public sealed record RuntimeJsExportWrapperCandidate(
    int WrapperMethodToken,
    int RegistrationMethodToken,
    int RegistrationCount)
{
    /// <summary>
    /// Module identity that owns both MethodDef tokens. Null preserves older
    /// serialized surfaces but cannot authenticate runtime publication.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ModuleVersionId { get; init; }
}

public class ApiType
{
    public string? Namespace { get; set; }
    public string Name { get; set; } = "";

    [JsonIgnore]
    public int? MetadataToken { get; set; }

    /// <summary>
    /// The exact metadata name, preserving literal '+' characters and using '+'
    /// to delimit nested types, matching how TypeRef constructs its names.
    /// Null in older serialized surfaces.
    /// </summary>
    public string? MetadataName { get; set; }

    /// <summary>
    /// Exact structured metadata lookup name retained for definition
    /// resolution and durable member identity. Null in older serialized
    /// surfaces.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MetadataTypeDefinitionName? DefinitionName { get; set; }

    /// <summary>
    /// Number of generic parameters introduced by each root-to-leaf exact
    /// metadata-name segment. Null or empty in older serialized surfaces.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<int>? IntroducedTypeParameterCounts { get; set; }
    
    /// <summary>
    /// Access level for non-public types. Null means public, including for older
    /// serialized surfaces; C# rendering preserves that compatibility fallback.
    /// </summary>
    public string? Accessibility { get; set; }
    public string Kind { get; set; } = "";  // class, struct, interface, enum, delegate
    public List<string> Attributes { get; set; } = [];

    /// <summary>
    /// Whether the type declares the exact-name runtime UnionAttribute, including a
    /// downlevel polyfill. This is marker presence, not a valid union or JSON contract.
    /// Null means the marker was not inspected, including older or summary surfaces.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HasUnionAttribute { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiTypeLayout? Layout { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiModuleMemorySafetyFacts? MemorySafety { get; set; }

    /// <summary>The C# enum underlying type, captured from the special <c>value__</c> field.</summary>
    public string? EnumUnderlyingType { get; set; }

    /// <summary>
    /// For an enum (<see cref="Kind"/> == <c>"enum"</c>): whether it carries <c>[Flags]</c>. With the default
    /// string-enum converter, STJ serializes named combinations as a comma-joined string of member names
    /// (e.g. <c>"Read, Write"</c>) but can serialize unnamed combinations numerically. Null for non-enum types
    /// and for older serialized surfaces that predate this field. True only for a well-formed authentic row;
    /// <see cref="FlagsAttributeCount"/> and <see cref="HasMalformedFlagsAttribute"/> carry the evidence a
    /// wire projection needs to fail closed instead of reading unreadable metadata as absence.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsFlagsEnum { get; set; }

    /// <summary>
    /// Number of well-formed authentic <c>[Flags]</c> rows on an enum. More than one is unsupported
    /// evidence: <c>[Flags]</c> is <c>AllowMultiple = false</c>, so a duplicate row cannot have come
    /// from a compiler.
    /// </summary>
    [JsonIgnore]
    public int FlagsAttributeCount { get; set; }

    /// <summary>
    /// True when an authentic <c>[Flags]</c> row on an enum carried a constructor or value blob this
    /// reader cannot honor. The claim is real but unreadable, so consumers must treat it as unsupported
    /// evidence rather than as absence.
    /// </summary>
    [JsonIgnore]
    public bool HasMalformedFlagsAttribute { get; set; }

    /// <summary>
    /// For an enum (<see cref="Kind"/> == <c>"enum"</c>): whether it carries
    /// <c>[JsonConverter(typeof(JsonStringEnumConverter&lt;...&gt;))]</c> (or the non-generic form), which makes
    /// STJ serialize declared values by name while its default configuration can serialize undefined values
    /// numerically. This is captured here, rather than derived from <see cref="Attributes"/>, because the
    /// converter's <c>typeof()</c> argument is a generic type reference that the rendered
    /// <see cref="Attributes"/> text cannot represent (dropped whole by the C#-spelling renderer). Null for
    /// non-enum types and for older serialized surfaces.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HasJsonStringEnumConverter { get; set; }

    [JsonIgnore]
    public int JsonConverterAttributeCount { get; set; }

    [JsonIgnore]
    public bool HasUnsupportedJsonWireAttributes { get; set; }

    [JsonIgnore]
    public int JsonSerializableAttributeCount { get; set; }

    [JsonIgnore]
    public List<ApiJsonSerializableRoot> JsonSerializableRoots
        { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonWireNamingPolicy? JsonPropertyNamingPolicy { get; set; }

    /// <summary>
    /// The effective source-generation mode declared by this serializer
    /// context. This remains a metadata fact because consumers must not infer
    /// deserialize support from a generated <c>JsonTypeInfo&lt;T&gt;</c> property.
    /// </summary>
    [JsonIgnore]
    public JsonSourceGenerationMode JsonSourceGenerationMode { get; set; }

    /// <summary>
    /// Whether an extracted serializer registration type carries the authentic
    /// marker emitted by the System.Text.Json source generator. Registration
    /// attributes and matching getter names do not establish generated
    /// implementation provenance by themselves. Null is retained for ordinary
    /// types and older or hand-composed surfaces.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HasSystemTextJsonSourceGenerationMarker { get; set; }

    [JsonIgnore]
    public List<FilteredJsonPropertyNameFact> FilteredJsonPropertyNameFacts
        { get; set; } = [];

    [JsonIgnore]
    public List<FilteredRuntimeJsExportFact> FilteredRuntimeJsExportFacts
        { get; set; } = [];

    [JsonPropertyName("filtered_runtime_js_export_facts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FilteredRuntimeJsExportFact>? FilteredRuntimeJsExportEvidence
    {
        get => FilteredRuntimeJsExportFacts.Count == 0
            ? null
            : FilteredRuntimeJsExportFacts;
        set => FilteredRuntimeJsExportFacts = value ?? [];
    }

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
    [JsonIgnore]
    public ApiTypeReferenceIdentity? BaseTypeReference { get; set; }
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

    [JsonIgnore]
    public byte[]? SourceChecksum { get; set; }

    [JsonIgnore]
    public string? SourceChecksumAlgorithm { get; set; }

    /// <summary>
    /// How the source URL was resolved by the higher inspection layer.
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

    /// <summary>
    /// Display spelling of the member's type. Deliberately raw: after a JSON
    /// round-trip <see cref="SignatureModel"/> is absent, and
    /// <c>ApiMemberIdentity.GetCanonicalSignature</c> falls back to parsing
    /// <see cref="Signature"/> to rebuild canonical identity — so containing it
    /// here would make a round-tripped member's identity diverge from the same
    /// member read live (issue #3319, found in adversarial review). Containment
    /// for these belongs at the rendering sites, never on the transfer object.
    /// </summary>
    public string? ReturnType { get; set; }

    /// <inheritdoc cref="ReturnType"/>
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
    /// Number of index parameters on a property. Null means older or
    /// hand-composed evidence could not prove the property is non-indexed.
    /// </summary>
    /// <remarks>
    /// <c>JsonWireMemberRulesTests.ExtractedCompilerIndexerIsExcludedFromJsonContract</c>
    /// and
    /// <c>ApiOutputFormatterTests.ApiTypeJson_RoundTripsRuntimeJsExportFailureEvidence</c>
    /// gate extraction and persistence.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? IndexParameterCount { get; set; }

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
    /// Method generic arity from the MethodDef. Runtime JSExport does not
    /// publish generic methods, so consumers must not infer a wrapper from the
    /// rendered signature alone.
    /// </summary>
    /// <remarks>
    /// <c>JsExportSurfaceBuilderTests.Build_RejectsGenericJsExportWithoutRuntimeWrapper</c>
    /// and
    /// <c>ApiOutputFormatterTests.ApiTypeJson_RoundTripsRuntimeJsExportFailureEvidence</c>
    /// are the gates.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int GenericArity { get; set; }

    /// <summary>
    /// PropertyDef or FieldDef token used to identify declaration-scoped
    /// metadata diagnostics without changing the MethodDef-only body token
    /// contract of <see cref="MetadataToken"/>. Gated by
    /// <c>MatchCommandTests.ExecuteAsync_PropertyWithGetterAndSetter_RejectsRatherThanSilentlySelectingGetter</c>
    /// and <c>ExecuteAsync_GetOnlyProperty_ResolvesToGetterBody</c>.
    /// </summary>
    [JsonIgnore]
    public int? DeclarationMetadataToken { get; set; }

    /// <summary>
    /// MethodDef tokens of a property's get/set accessors when known. Lets accessor-level
    /// call-graph rows (e.g. <c>get_Foo</c>) map back to the owning property's selector.
    /// </summary>
    public int? GetterToken { get; set; }
    public int? SetterToken { get; set; }

    [JsonIgnore]
    public bool? HasGetter { get; set; }

    [JsonIgnore]
    public string? GetterAccessibility { get; set; }

    /// <summary>
    /// Whether a property has a setter, and that setter's accessibility.
    /// Null preserves older or hand-composed surface compatibility.
    /// </summary>
    [JsonIgnore]
    public bool? HasSetter { get; set; }

    /// <inheritdoc cref="HasSetter"/>
    [JsonIgnore]
    public string? SetterAccessibility { get; set; }

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
    /// Version-aware caller contract and independent signature pointer evidence.
    /// Null denotes an older or hand-composed surface, not a safe contract.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiMemberMemorySafetyFacts? MemorySafety { get; set; }

    /// <summary>
    /// Contracts for the accessor MethodDefs represented by this property or
    /// event, including accessors not exposed as separate API members.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<ApiMemberMemorySafetyFacts>? AccessorMemorySafety
        { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiBackingStorageAssociation? BackingStorage { get; set; }

    /// <summary>
    /// Whether this MethodDef has a managed body RVA. Null is retained for
    /// older or hand-composed surfaces that predate the exact metadata fact.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HasMethodBody { get; set; }

    /// <summary>
    /// Whether metadata contains an exact-name runtime-wrapper MethodDef and a
    /// target-matched <c>DynamicDependency</c> row on the SDK-generated
    /// registration container for this JSExport MethodDef. This is not body
    /// provenance: consumers that publish runtime bindings must authenticate
    /// the wrapper's call chain. Null preserves older or hand-composed
    /// surfaces.
    /// </summary>
    /// <remarks>
    /// <c>JsExportSurfaceBuilderTests.Build_RejectsJsExportWithoutGeneratedRuntimeWrapper</c>
    /// and
    /// <c>ApiOutputFormatterTests.ApiTypeJson_RoundTripsRuntimeJsExportFailureEvidence</c>
    /// gate extraction, enforcement, and persistence.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HasRuntimeJsExportWrapperCandidate { get; set; }

    /// <summary>
    /// Exact wrapper and registration MethodDef evidence behind
    /// <see cref="HasRuntimeJsExportWrapperCandidate"/>. Runtime publishers
    /// authenticate these tokens against Analysis-owned call evidence.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<RuntimeJsExportWrapperCandidate>?
        RuntimeJsExportWrapperCandidates { get; set; }

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
    /// True when the member carries <c>[CompilerGeneratedAttribute]</c> — for example a positional record's
    /// synthesized <c>EqualityContract</c> property. This is the precise signal for compiler-synthesized
    /// infrastructure; unlike <see cref="Accessibility"/>, it does not also match a legitimate non-public
    /// member deliberately opted into the wire contract (e.g. via <c>[JsonInclude]</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsCompilerGenerated { get; set; }

    /// <summary>
    /// True when the member carries <c>[JsonInclude]</c>. Source-generated STJ
    /// can honor the opt-in only when the generated context can access the
    /// member or relevant accessor.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HasJsonInclude { get; set; }

    /// <summary>
    /// True when an authentic <c>[JsonInclude]</c> row carried a constructor or
    /// value blob this reader cannot honor. The opt-in is real but unreadable,
    /// so consumers must treat it as unsupported evidence rather than absence.
    /// </summary>
    [JsonIgnore]
    public bool HasMalformedJsonInclude { get; set; }

    /// <summary>
    /// One entry per authentic <c>[JsonIgnore]</c> row, in metadata order, with
    /// <see langword="null"/> marking a row whose metadata cannot be honored.
    /// This is the authoritative directional fact; <see cref="HasJsonIgnore"/>
    /// and <see cref="HasJsonIgnoreNever"/> are derived from it so the two
    /// cannot drift apart. Its persisted projection is
    /// <see cref="JsonIgnoreConditionEvidence"/>.
    /// </summary>
    [JsonIgnore]
    public List<JsonWireIgnoreCondition?> JsonIgnoreConditions { get; set; } = [];

    /// <summary>
    /// The persisted projection of <see cref="JsonIgnoreConditions"/>, named
    /// <c>json_ignore_conditions</c> on the wire and omitted when the member
    /// carries no authentic <c>[JsonIgnore]</c> row.
    /// </summary>
    /// <remarks>
    /// The conditions themselves are persisted rather than the derived
    /// <see cref="HasJsonIgnore"/> flag, because <c>WhenWriting</c> and
    /// <c>WhenReading</c> are directional and a single boolean cannot
    /// reconstruct which direction survived. A <see langword="null"/> element
    /// persists an authentic row whose metadata could not be decoded, so
    /// unreadable evidence stays visible across a round trip instead of
    /// reappearing as a well-formed condition or as absence. The wire name is
    /// pinned with <see cref="JsonPropertyNameAttribute"/> so this projection
    /// does not read as a second, differently-named fact under a context whose
    /// naming policy differs.
    /// <c>ApiOutputFormatterTests.ApiTypeJson_RoundTripsDirectionalAndMalformedJsonIgnoreEvidence</c>
    /// is the gate.
    /// </remarks>
    [JsonPropertyName("json_ignore_conditions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<JsonWireIgnoreCondition?>? JsonIgnoreConditionEvidence
    {
        get => JsonIgnoreConditions.Count == 0 ? null : JsonIgnoreConditions;
        set => JsonIgnoreConditions = value ?? [];
    }

    /// <summary>
    /// True when the member carries any authentic <c>[JsonIgnore]</c> row,
    /// including a malformed one. Derived from
    /// <see cref="JsonIgnoreConditions"/> and emitted for compatibility with
    /// consumers of the existing <c>has_json_ignore</c> field; a reader
    /// reconstructs it from the persisted
    /// <see cref="JsonIgnoreConditionEvidence"/> rather than from this field,
    /// which carries no direction.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HasJsonIgnore => JsonIgnoreConditions.Count > 0;

    [JsonIgnore]
    public bool HasJsonIgnoreNever =>
        JsonIgnoreConditions is [JsonWireIgnoreCondition.Never];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JsonPropertyName { get; set; }

    [JsonIgnore]
    public List<string?> JsonPropertyNameAttributeValues { get; set; } = [];

    [JsonIgnore]
    public int JsonConverterAttributeCount { get; set; }

    [JsonIgnore]
    public bool HasUnsupportedJsonWireAttributes { get; set; }

    /// <summary>
    /// Compatibility projection of authentic valid <c>[JSExport]</c> evidence.
    /// The count and malformed marker retain the rows that this Boolean cannot
    /// express.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HasRuntimeJsExport { get; set; }

    /// <summary>
    /// Number of authentic framework-signed <c>[JSExport]</c> rows. A count
    /// other than one is unsupported evidence rather than an absent export.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int RuntimeJsExportAttributeCount { get; set; }

    /// <summary>
    /// True when an authentic framework-signed <c>[JSExport]</c> row could not
    /// be decoded or did not have the marker attribute shape.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HasMalformedRuntimeJsExportAttribute { get; set; }

    [JsonIgnore]
    public List<string?> JsonStringEnumMemberNameAttributeValues { get; set; } = [];

    [JsonIgnore]
    public string? JsonStringEnumMemberName =>
        JsonStringEnumMemberNameAttributeValues is [string name]
            ? name
            : null;

    /// <summary>
    /// Ordered persisted evidence for authentic
    /// <c>[JsonStringEnumMemberName]</c> rows. Null entries retain malformed
    /// rows, and the resolved wire name is derived only from one valid row.
    /// </summary>
    /// <remarks>
    /// <c>ApiOutputFormatterTests.ApiTypeJson_RoundTripsEnumWireNameEvidence</c>
    /// gates the production JSON contract.
    /// </remarks>
    [JsonPropertyName("json_string_enum_member_names")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string?>? JsonStringEnumMemberNameEvidence
    {
        get => JsonStringEnumMemberNameAttributeValues.Count == 0
            ? null
            : JsonStringEnumMemberNameAttributeValues;
        set => JsonStringEnumMemberNameAttributeValues = value ?? [];
    }

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
    /// Exact canonical declaring-type spelling for a member projected onto a
    /// different type. Null for members declared on their containing
    /// <see cref="ApiType"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeclaringTypeCanonicalName { get; set; }

    /// <summary>
    /// Exact metadata lookup name of the declaring Type when this Member is
    /// projected beneath another <see cref="ApiType"/>.
    /// </summary>
    /// <remarks>
    /// Null for Members declared on their containing Type and for older
    /// serialized surfaces. Consumers that require exact declaration identity
    /// must not reconstruct this value from declaring-Type display text.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MetadataTypeDefinitionName? DeclaringTypeDefinitionName { get; set; }

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

    [JsonIgnore]
    public byte[]? SourceChecksum { get; set; }

    [JsonIgnore]
    public string? SourceChecksumAlgorithm { get; set; }

    // Documentation (populated with --docs)
    public DocComment Documentation { get; set; } = new();
}

public sealed class ApiAssemblyIdentity : IEquatable<ApiAssemblyIdentity>
{
    public ApiAssemblyIdentity(
        string name,
        Version? version,
        string? culture,
        string? publicKeyToken)
    {
        Name = name;
        Version = version;
        Culture = culture;
        PublicKeyToken = publicKeyToken;
    }

    public string Name { get; }
    public Version? Version { get; }
    public string? Culture { get; }
    public string? PublicKeyToken { get; }

    public bool Equals(ApiAssemblyIdentity? other) =>
        other is not null
        && StringComparer.OrdinalIgnoreCase.Equals(Name, other.Name)
        && Version == other.Version
        && StringComparer.OrdinalIgnoreCase.Equals(
            NormalizeCulture(Culture),
            NormalizeCulture(other.Culture))
        && StringComparer.OrdinalIgnoreCase.Equals(
            PublicKeyToken ?? "",
            other.PublicKeyToken ?? "");

    public override bool Equals(object? obj) =>
        obj is ApiAssemblyIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name, StringComparer.OrdinalIgnoreCase);
        hash.Add(Version);
        hash.Add(
            NormalizeCulture(Culture),
            StringComparer.OrdinalIgnoreCase);
        hash.Add(
            PublicKeyToken ?? "",
            StringComparer.OrdinalIgnoreCase);
        return hash.ToHashCode();
    }

    internal int RetainedCharacterCount =>
        Name.Length
        + (Culture?.Length ?? 0)
        + (PublicKeyToken?.Length ?? 0);

    internal static ApiAssemblyIdentity FromDefinition(
        MetadataReader reader,
        Action<int>? beforeMaterialize = null)
    {
        AssemblyDefinition definition = reader.GetAssemblyDefinition();
        return new(
            ReadString(reader, definition.Name, beforeMaterialize),
            definition.Version,
            ReadStringOrNull(
                reader,
                definition.Culture,
                beforeMaterialize),
            ReadToken(
                reader,
                definition.PublicKey,
                isPublicKey: true,
                beforeMaterialize));
    }

    internal static ApiAssemblyIdentity FromReference(
        MetadataReader reader,
        AssemblyReferenceHandle handle,
        Action<int>? beforeMaterialize = null)
    {
        System.Reflection.Metadata.AssemblyReference reference =
            reader.GetAssemblyReference(handle);
        return new(
            ReadString(reader, reference.Name, beforeMaterialize),
            reference.Version,
            ReadStringOrNull(
                reader,
                reference.Culture,
                beforeMaterialize),
            ReadToken(
                reader,
                reference.PublicKeyOrToken,
                (reference.Flags & AssemblyFlags.PublicKey) != 0,
                beforeMaterialize));
    }

    static string ReadString(
        MetadataReader reader,
        StringHandle handle,
        Action<int>? beforeMaterialize)
    {
        beforeMaterialize?.Invoke(reader.GetBlobReader(handle).Length);
        return reader.GetString(handle);
    }

    static string? ReadStringOrNull(
        MetadataReader reader,
        StringHandle handle,
        Action<int>? beforeMaterialize) =>
        handle.IsNil
            ? null
            : ReadString(reader, handle, beforeMaterialize);

    static string? ReadToken(
        MetadataReader reader,
        BlobHandle handle,
        bool isPublicKey,
        Action<int>? beforeMaterialize)
    {
        if (handle.IsNil)
            return null;

        int length = reader.GetBlobReader(handle).Length;
        long work = (long)length
            + (isPublicKey ? 16L : (long)length * 2);
        beforeMaterialize?.Invoke(
            (int)Math.Min(int.MaxValue, work));
        return AssemblyReferenceIdentity.TokenOrNull(
            reader,
            handle,
            isPublicKey);
    }

    static string NormalizeCulture(string? value) =>
        string.IsNullOrEmpty(value)
            || value.Equals(
                "neutral",
                StringComparison.OrdinalIgnoreCase)
                ? ""
                : value;
}

public sealed record ApiTypeReferenceIdentity(
    ApiAssemblyIdentity Assembly,
    string FullName,
    MetadataTypeDefinitionName? DefinitionName = null);

/// <summary>
/// The structural shape of a metadata signature or serializer root. This is
/// intentionally separate from display spelling and named-reference lists:
/// primitive codes, array rank, generic arguments, and exact named-definition
/// identities all participate in equality.
/// </summary>
public sealed class ApiTypeShape : IEquatable<ApiTypeShape>
{
    ApiTypeShape(
        ApiTypeShapeKind kind,
        ApiPrimitiveType? primitive = null,
        ApiTypeReferenceIdentity? definition = null,
        ApiTypeShape? elementType = null,
        ImmutableArray<ApiTypeShape> typeArguments = default,
        int arrayRank = 0,
        ImmutableArray<int> arraySizes = default,
        ImmutableArray<int> arrayLowerBounds = default)
    {
        Kind = kind;
        Primitive = primitive;
        Definition = definition;
        ElementType = elementType;
        TypeArguments = typeArguments.IsDefault ? [] : typeArguments;
        ArrayRank = arrayRank;
        ArraySizes = arraySizes.IsDefault ? [] : arraySizes;
        ArrayLowerBounds = arrayLowerBounds.IsDefault
            ? []
            : arrayLowerBounds;
    }

    public ApiTypeShapeKind Kind { get; }

    public ApiPrimitiveType? Primitive { get; }

    public ApiTypeReferenceIdentity? Definition { get; }

    public ApiTypeShape? ElementType { get; }

    public ImmutableArray<ApiTypeShape> TypeArguments { get; }

    public int ArrayRank { get; }

    /// <summary>
    /// Optional ECMA array shape sizes retained for multi-dimensional
    /// signature identity.
    /// </summary>
    public ImmutableArray<int> ArraySizes { get; }

    /// <summary>
    /// Optional ECMA array shape lower bounds retained for
    /// multi-dimensional signature identity.
    /// </summary>
    public ImmutableArray<int> ArrayLowerBounds { get; }

    public static ApiTypeShape PrimitiveType(ApiPrimitiveType primitive) =>
        new(ApiTypeShapeKind.Primitive, primitive: primitive);

    public static ApiTypeShape Named(ApiTypeReferenceIdentity definition) =>
        new(ApiTypeShapeKind.Named, definition: definition);

    public static ApiTypeShape GenericInstance(
        ApiTypeReferenceIdentity definition,
        ImmutableArray<ApiTypeShape> typeArguments) =>
        new(
            ApiTypeShapeKind.GenericInstance,
            definition: definition,
            typeArguments: typeArguments);

    public static ApiTypeShape SzArray(ApiTypeShape elementType) =>
        new(ApiTypeShapeKind.SzArray, elementType: elementType);

    public static ApiTypeShape Array(
        ApiTypeShape elementType,
        int rank,
        ImmutableArray<int> arraySizes = default,
        ImmutableArray<int> arrayLowerBounds = default) =>
        new(
            ApiTypeShapeKind.Array,
            elementType: elementType,
            arrayRank: rank,
            arraySizes: arraySizes,
            arrayLowerBounds: arrayLowerBounds);

    public bool Equals(ApiTypeShape? other)
    {
        if (other is null)
            return false;

        var pending = new Stack<(ApiTypeShape Left, ApiTypeShape Right)>();
        pending.Push((this, other));
        while (pending.Count > 0)
        {
            (ApiTypeShape left, ApiTypeShape right) = pending.Pop();
            if (ReferenceEquals(left, right))
                continue;
            if (left.Kind != right.Kind
                || left.Primitive != right.Primitive
                || left.Definition != right.Definition
                || left.ArrayRank != right.ArrayRank
                || !left.ArraySizes.AsSpan().SequenceEqual(
                    right.ArraySizes.AsSpan())
                || !left.ArrayLowerBounds.AsSpan().SequenceEqual(
                    right.ArrayLowerBounds.AsSpan())
                || left.TypeArguments.Length != right.TypeArguments.Length
                || (left.ElementType is null) != (right.ElementType is null))
            {
                return false;
            }

            if (left.ElementType is not null)
                pending.Push((left.ElementType, right.ElementType!));
            for (int i = 0; i < left.TypeArguments.Length; i++)
                pending.Push((left.TypeArguments[i], right.TypeArguments[i]));
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as ApiTypeShape);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        var pending = new Stack<ApiTypeShape>();
        pending.Push(this);
        while (pending.Count > 0)
        {
            ApiTypeShape current = pending.Pop();
            hash.Add(current.Kind);
            hash.Add(current.Primitive);
            hash.Add(current.Definition);
            hash.Add(current.ArrayRank);
            foreach (int size in current.ArraySizes)
                hash.Add(size);
            foreach (int lowerBound in current.ArrayLowerBounds)
                hash.Add(lowerBound);
            if (current.ElementType is not null)
                pending.Push(current.ElementType);
            for (int i = current.TypeArguments.Length - 1; i >= 0; i--)
                pending.Push(current.TypeArguments[i]);
        }
        return hash.ToHashCode();
    }
}

public enum ApiTypeShapeKind
{
    Primitive,
    Named,
    GenericInstance,
    SzArray,
    Array,
}

public enum ApiPrimitiveType
{
    Void,
    Boolean,
    Char,
    SByte,
    Byte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    Decimal,
    String,
    Object,
}

public sealed record ApiJsonSerializableRoot(
    ApiTypeReferenceIdentity? ElementType,
    bool IsArray,
    string? TypeInfoPropertyName = null)
{
    /// <summary>
    /// Exact registered root shape. Null means the authentic row was unreadable
    /// or names a shape the metadata model cannot represent.
    /// </summary>
    [JsonIgnore]
    public ApiTypeShape? Type { get; init; }

    /// <summary>
    /// Visible failure evidence for an authentic root whose metadata or type
    /// shape is not supported.
    /// </summary>
    [JsonIgnore]
    public string? UnsupportedReason { get; init; }

    /// <summary>
    /// Per-root source-generation override. <see cref="JsonSourceGenerationMode.Default"/>
    /// delegates to the owning context's mode.
    /// </summary>
    [JsonIgnore]
    public JsonSourceGenerationMode GenerationMode { get; init; }
}
