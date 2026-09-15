namespace ILInspector.TypeScriptGeneration;

internal sealed record TypeScriptFunctionSignature(
    string Name,
    IReadOnlyList<TypeScriptParameterSignature> Parameters,
    string RawReturnType,
    string PublicReturnType,
    bool IsAsync,
    bool ParsesJson,
    bool JsonEnvelopeMayBeNull);

internal readonly record struct TypeScriptParameterSignature(
    string Name,
    string Type);
