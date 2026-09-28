namespace ILInspector.TypeScriptGeneration;

internal sealed record TypeScriptFunctionSignature(
    string Name,
    IReadOnlyList<TypeScriptParameterSignature> Parameters,
    string RawReturnType,
    string PublicReturnType,
    bool IsAsync,
    bool ParsesJson,
    bool ReturnsJsonText,
    bool JsonEnvelopeMayBeNull);

internal readonly record struct TypeScriptParameterSignature(
    string Name,
    string RawType,
    string PublicType,
    bool SerializesJson);
