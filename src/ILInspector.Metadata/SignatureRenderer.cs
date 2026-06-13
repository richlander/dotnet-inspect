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
        string name,
        MethodSignature<string> signature,
        bool extensionThis = false)
    {
        var paramHandles = method.GetParameters();
        var paramTypes = signature.ParameterTypes;

        List<string> parameters = [];
        for (int i = 0; i < paramTypes.Length; i++)
        {
            string? paramName = null;
            foreach (var handle in paramHandles)
            {
                var param = reader.GetParameter(handle);
                if (param.SequenceNumber == i + 1)
                {
                    paramName = reader.GetString(param.Name);
                    break;
                }
            }

            var prefix = extensionThis && i == 0 ? "this " : "";
            parameters.Add($"{prefix}{paramTypes[i]} {paramName ?? $"arg{i}"}");
        }

        return $"{signature.ReturnType} {name}({string.Join(", ", parameters)})";
    }
}
