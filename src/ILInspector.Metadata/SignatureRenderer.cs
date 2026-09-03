using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Renders a decoded method signature as <c>ReturnType Name(type name, ...)</c>, resolving parameter
/// names from metadata. Shared by the extension-method and method-classification scanners so the
/// simple-signature format stays identical between them.
///
/// This is intentionally the lightweight renderer (string-based <see cref="SignatureDecoder"/>
/// output). The full API surface uses <c>ApiSurfaceExtractor</c>'s richer renderer, which also
/// applies nullability annotations, ref/out/params modifiers, and default values.
/// </summary>
internal static class SignatureRenderer
{
    /// <param name="extensionThis">When true, prefixes the first parameter with <c>this </c>.</param>
    public static string RenderDecodedSignature(
        MetadataReader reader,
        MethodDefinition method,
        string methodName,
        MethodSignature<string> signature,
        GenericContext context,
        bool extensionThis = false)
    {
        var paramHandles = method.GetParameters();
        var paramTypes = signature.ParameterTypes;
        string[] parameterNames = MetadataParameterNames.Resolve(
            reader,
            paramHandles,
            paramTypes.Length,
            context.MethodParameters);

        List<string> parameters = [];
        for (int i = 0; i < paramTypes.Length; i++)
        {
            var prefix = extensionThis && i == 0 ? "this " : "";
            parameters.Add($"{prefix}{paramTypes[i]} {parameterNames[i]}");
        }

        string declarationName = context.MethodParameters.Count == 0
            ? methodName
            : $"{methodName}<{string.Join(", ", context.MethodParameters)}>";
        return $"{signature.ReturnType} {declarationName}({string.Join(", ", parameters)})";
    }
}
