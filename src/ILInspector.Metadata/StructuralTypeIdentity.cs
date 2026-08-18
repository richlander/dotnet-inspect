using System.Reflection.Metadata;
using System.Text;

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

    /// <summary>
    /// Distributes type arguments across metadata arity markers so
    /// <c>Outer`1.Inner`1</c> becomes <c>Outer{a}.Inner{b}</c>, matching
    /// call-graph <c>NamedGenericTypeIdentity</c>.
    /// </summary>
    public static string Generic(string metadataName, IEnumerable<string> typeArguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(metadataName);
        ArgumentNullException.ThrowIfNull(typeArguments);

        string[] arguments = typeArguments as string[] ?? [.. typeArguments];
        foreach (string argument in arguments)
            ArgumentException.ThrowIfNullOrEmpty(argument);

        string name = metadataName.Replace('+', '.');
        if (!name.Contains('`', StringComparison.Ordinal))
        {
            return arguments.Length == 0
                ? name
                : $"{name}{{{string.Join(",", arguments)}}}";
        }

        var result = new StringBuilder(name.Length + 16);
        int argumentIndex = 0;
        for (int index = 0; index < name.Length; index++)
        {
            if (name[index] != '`')
            {
                result.Append(name[index]);
                continue;
            }

            int digitStart = index + 1;
            int digitEnd = digitStart;
            while (digitEnd < name.Length && char.IsDigit(name[digitEnd]))
                digitEnd++;
            if (digitEnd == digitStart
                || !int.TryParse(name.AsSpan(digitStart, digitEnd - digitStart), out int arity)
                || arity <= 0)
            {
                result.Append('`');
                continue;
            }

            int take = Math.Min(arity, arguments.Length - argumentIndex);
            result.Append('{');
            for (int offset = 0; offset < take; offset++)
            {
                if (offset > 0)
                    result.Append(',');
                result.Append(arguments[argumentIndex + offset]);
            }

            result.Append('}');
            argumentIndex += take;
            index = digitEnd - 1;
        }

        return result.ToString();
    }
}
