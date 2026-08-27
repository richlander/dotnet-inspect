using ILInspector.JsExportSurface;

namespace tsbindgen;

sealed record TypeScriptParameterSignature(
    string Name,
    string Type);

sealed record TypeScriptFunctionSignature(
    JsExportFunction Function,
    string Name,
    string InteropReturnType,
    string ReturnType,
    string? WireReturnType,
    IReadOnlyList<TypeScriptParameterSignature> Parameters);
