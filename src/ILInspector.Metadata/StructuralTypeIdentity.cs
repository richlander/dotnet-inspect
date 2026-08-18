using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Opaque structural type identity for call-graph selectors. Display spelling stays
/// API-compatible; this encoding carries custom modifiers, pinned wrappers, and
/// function-pointer header fields that <see cref="TypeNode.Render"/> erases.
/// </summary>
public static class StructuralTypeIdentity
{
    public static string Pinned(string inner)
    {
        ArgumentException.ThrowIfNullOrEmpty(inner);
        return $"pinned{{{inner}}}";
    }

    public static string Modified(bool required, string modifier, string inner)
    {
        ArgumentException.ThrowIfNullOrEmpty(modifier);
        ArgumentException.ThrowIfNullOrEmpty(inner);
        return $"{(required ? "modreq" : "modopt")}{{{modifier}}}{{{inner}}}";
    }

    public static string FunctionPointer(
        SignatureCallingConvention convention,
        bool hasThis,
        bool explicitThis,
        int genericParameterCount,
        int requiredParameterCount,
        IEnumerable<string> parameterTypes,
        string returnType)
    {
        ArgumentNullException.ThrowIfNull(parameterTypes);
        ArgumentException.ThrowIfNullOrEmpty(returnType);

        string conventionText = convention switch
        {
            SignatureCallingConvention.Default => "",
            SignatureCallingConvention.CDecl => " unmanaged[Cdecl]",
            SignatureCallingConvention.StdCall => " unmanaged[Stdcall]",
            SignatureCallingConvention.ThisCall => " unmanaged[Thiscall]",
            SignatureCallingConvention.FastCall => " unmanaged[Fastcall]",
            _ => " unmanaged",
        };
        string payload = string.Join(",", parameterTypes.Append(returnType));
        return $"delegate*{conventionText}{{{payload}}};I{(hasThis ? 1 : 0)};E{(explicitThis ? 1 : 0)};G{genericParameterCount};R{requiredParameterCount}";
    }
}
