using ILInspector.Analysis;

namespace ILInspector.JsExportSurface;

/// <summary>
/// Resolves each <c>[JSExport]</c> method's actual JSON wire-contract DTO type(s) by reading the
/// <c>JsonSerializer.Serialize</c>/<c>Deserialize</c> call sites in the method's own IL body (via
/// <see cref="LibraryBodyIndex.DirectCalls"/>), instead of inferring them from every DTO
/// registered anywhere in the assembly's <c>JsonSerializerContext</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two <c>[JSExport]</c> exports commonly share the identical erased signature (both are
/// <c>Task&lt;string&gt;</c>, say) while wiring to entirely different DTOs. Scanning "every
/// registered shape" cannot distinguish them; this resolver reads the one fact that does:
/// which type argument the export's own body actually instantiated
/// <c>JsonSerializer.Serialize&lt;T&gt;</c>/<c>Deserialize&lt;T&gt;</c> with (surfaced as
/// <c>TypeArguments[0]</c> on the call's <c>JsonTypeInfo&lt;T&gt;</c> parameter). This relies on
/// <c>DirectCall.Caller</c> already being attributed to the declared method rather than a
/// compiler-generated async state machine or lifted body (see repository issue #4459 / PR #4461).
/// </para>
/// <para>
/// Only the DTO <em>type</em> is resolved this way. Which of the export's own parameters supplied
/// a <c>Deserialize</c> call's JSON-string argument is not resolved — that would need call-site
/// argument data-flow evidence beyond what <see cref="DirectCall"/> carries today. For a method
/// with a single <c>Deserialize</c> call this is unambiguous in practice; for multiple calls in
/// one body, every resolved DTO is reported without attribution to a specific parameter position.
/// This is a residual gap, not a silent guess.
/// </para>
/// <para>
/// <see cref="DirectCall"/> also carries no branch/reachability evidence, so a body with more than
/// one distinct <c>Serialize&lt;T&gt;</c> call site (e.g. different DTOs serialized on different
/// branches) has no principled way to pick "the" return DTO. Rather than silently guess the first
/// one found, <see cref="Attach"/> leaves <see cref="JsExportFunction.ReturnWireType"/> unset
/// whenever more than one distinct DTO is found for the return position.
/// </para>
/// </remarks>
public static class JsonWireContractResolver
{
    const string JsonSerializerTypeName = "JsonSerializer";
    const string JsonSerializerNamespace = "System.Text.Json";
    const string SerializeMethodName = "Serialize";
    const string DeserializeMethodName = "Deserialize";
    const string JsonTypeInfoName = "JsonTypeInfo`1";
    const string JsonTypeInfoNamespace = "System.Text.Json.Serialization.Metadata";

    /// <summary>
    /// Returns <paramref name="function"/> with <see cref="JsExportFunction.ReturnWireType"/> and
    /// <see cref="JsExportFunction.ParameterWireTypes"/> populated from the direct calls found in
    /// <paramref name="bodyIndex"/> for the method identified by <paramref name="metadataToken"/>.
    /// </summary>
    public static JsExportFunction Attach(
        LibraryBodyIndex bodyIndex,
        JsExportFunction function,
        int metadataToken)
    {
        // Every distinct Serialize<T> DTO found for the return position, in call-site order.
        // Kept as a list (not folded into a single "first wins" value) so ambiguity between
        // multiple distinct DTOs can be detected and left unresolved rather than guessed — see
        // remarks above.
        var returnTypes = new List<string>();
        var parameterTypes = new List<string>();

        foreach (DirectCall call in bodyIndex.DirectCalls)
        {
            if (call.Caller.MetadataToken != metadataToken
                || call.Callee.DeclaringType.Name != JsonSerializerTypeName
                || call.Callee.DeclaringType.Namespace != JsonSerializerNamespace)
            {
                continue;
            }

            string? dto = ResolveJsonTypeInfoArgument(call.Callee);
            if (dto is null)
            {
                continue;
            }

            if (call.Callee.Name == SerializeMethodName)
            {
                if (!returnTypes.Contains(dto, StringComparer.Ordinal))
                {
                    returnTypes.Add(dto);
                }
            }
            else if (call.Callee.Name == DeserializeMethodName)
            {
                parameterTypes.Add(dto);
            }
        }

        return new JsExportFunction
        {
            DeclaringType = function.DeclaringType,
            Name = function.Name,
            ReturnType = function.ReturnType,
            Parameters = function.Parameters,
            ReturnWireType = returnTypes.Count == 1 ? returnTypes[0] : null,
            ParameterWireTypes = parameterTypes,
        };
    }

    static string? ResolveJsonTypeInfoArgument(MemberRef callee)
    {
        foreach (TypeRef parameter in callee.ParameterTypes)
        {
            if (parameter.Kind == TypeRefKind.GenericInstance
                && parameter.ElementType is { } elementType
                && elementType.Name == JsonTypeInfoName
                && elementType.Namespace == JsonTypeInfoNamespace
                && parameter.TypeArguments.Length == 1)
            {
                // ToDisplayString (not .Name) so a container DTO — WidgetDto[], List<WidgetDto> —
                // renders as C#-syntax text rather than the empty string TypeRef.Name carries for
                // non-Definition kinds (GenericInstance/SzArray/Array). TsTypeMapper's Map already
                // parses this exact "[]"/generic-argument syntax for every other type string in
                // this pipeline (signature-derived parameter/return/property types), so container
                // DTOs resolve to a correct TS type instead of silently collapsing to "unknown".
                return parameter.TypeArguments[0].ToDisplayString();
            }
        }

        return null;
    }
}
