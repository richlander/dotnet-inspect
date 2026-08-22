using System.Text.Json.Serialization;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface;

/// <summary>
/// The C#-side wasm/JS interop surface of an assembly: every <c>[JSExport]</c>-attributed static
/// member, plus the transitive closure of the record types its parameters and return type
/// reference. Stays entirely C#-faithful — return types are reported as-is (a
/// <c>Task&lt;T&gt;</c> is reported as <c>Task&lt;T&gt;</c>, not unwrapped to a target-language
/// "promise" concept). Rewriting to a target language's vocabulary is a consumer concern, not
/// something this model performs.
/// </summary>
public sealed class JsExportSurface
{
    [JsonIgnore]
    public ApiAssemblyIdentity? AssemblyIdentity { get; init; }

    public IReadOnlyList<JsExportFunction> Functions { get; init; } = [];

    public IReadOnlyList<ApiType> Records { get; init; } = [];

    /// <summary>
    /// Enum roots discovered the same way as <see cref="Records"/> (via the assembly's
    /// <c>JsonSerializerContext</c>-registered shapes and their transitive property references),
    /// but kept separate: an <c>enum</c> has no properties to project as an interface. STJ's
    /// default <c>JsonStringEnumConverter</c> serializes declared values as member names, while
    /// undefined values can remain numeric, so the TypeScript consumer projects a string/number
    /// wire value rather than an object.
    /// </summary>
    public IReadOnlyList<ApiType> Enums { get; init; } = [];

    /// <summary>
    /// The wire directions each declared type was reached in, keyed by the
    /// <see cref="ApiType"/> instances published in <see cref="Records"/> and
    /// <see cref="Enums"/>.
    /// </summary>
    /// <remarks>
    /// Directions are composed here rather than stored on <see cref="ApiType"/>
    /// because they are a property of how an export uses a type, not a metadata
    /// fact the type itself carries. A type absent from this map — a
    /// hand-composed surface, or a registered shape no export references — is
    /// treated as <see cref="JsonWireDirection.Both"/> by consumers, which is
    /// the conservative reading. Gated by
    /// <c>JsExportSurfaceBuilderTests.Build_RecordsSerializeOnlyDirectionForReturnOnlyDto</c>.
    /// </remarks>
    [JsonIgnore]
    public IReadOnlyDictionary<ApiType, JsonWireDirection> WireDirections
        { get; init; } =
        new Dictionary<ApiType, JsonWireDirection>();
}

/// <summary>
/// One <c>[JSExport]</c>-attributed static member, with its declaring type, parameters, and
/// return type as reported by <see cref="ApiSurfaceExtractor"/> — unmodified C# signature facts.
/// </summary>
public sealed class JsExportFunction
{
    public required string DeclaringType { get; init; }

    public required string Name { get; init; }

    public required string ReturnType { get; init; }

    [JsonIgnore]
    public IReadOnlyList<ApiTypeReferenceIdentity> ReturnTypeReferences
        { get; init; } = [];

    public IReadOnlyList<ApiParameter> Parameters { get; init; } = [];

    /// <summary>
    /// The DTO type actually serialized by this method's own body onto its return value, resolved
    /// from a <c>JsonSerializer.Serialize</c> call site — not inferred from the assembly's whole
    /// registered shape vocabulary. Null when the body contains no such call (e.g. <see
    /// cref="ReturnType"/> is already a marshalable type, or the export has no return payload), or
    /// when more than one distinct DTO was found for the return position (an ambiguity this is
    /// left unresolved rather than guessed — see <see cref="JsonWireContractResolver"/> remarks).
    /// </summary>
    public string? ReturnWireType { get; init; }

    [JsonIgnore]
    public IReadOnlyList<ApiTypeReferenceIdentity> ReturnWireTypeReferences
        { get; init; } = [];

    /// <summary>
    /// DTO type(s) this method's own body deserializes from a JSON-string argument, resolved from
    /// <c>JsonSerializer.Deserialize</c> call sites. Not yet attributed to a specific parameter
    /// position — see <see cref="JsonWireContractResolver"/> remarks for that residual gap.
    /// </summary>
    public IReadOnlyList<string> ParameterWireTypes { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyList<ApiTypeReferenceIdentity> ParameterWireTypeReferences
        { get; init; } = [];
}
