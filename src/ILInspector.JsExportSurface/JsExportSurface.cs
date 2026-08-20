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
    public IReadOnlyList<JsExportFunction> Functions { get; init; } = [];

    public IReadOnlyList<ApiType> Records { get; init; } = [];
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

    /// <summary>
    /// DTO type(s) this method's own body deserializes from a JSON-string argument, resolved from
    /// <c>JsonSerializer.Deserialize</c> call sites. Not yet attributed to a specific parameter
    /// position — see <see cref="JsonWireContractResolver"/> remarks for that residual gap.
    /// </summary>
    public IReadOnlyList<string> ParameterWireTypes { get; init; } = [];
}
