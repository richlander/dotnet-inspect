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
}
