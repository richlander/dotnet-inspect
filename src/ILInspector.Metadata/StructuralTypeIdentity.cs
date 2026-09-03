using System.Reflection.Metadata;
using System.Text;
using CSharpText;

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

    public static string Generic(
        string @namespace,
        IEnumerable<string> metadataSegments,
        IEnumerable<string> typeArguments)
    {
        ArgumentNullException.ThrowIfNull(@namespace);
        ArgumentNullException.ThrowIfNull(metadataSegments);
        ArgumentNullException.ThrowIfNull(typeArguments);

        string[] segments = [.. metadataSegments];
        string[] arguments = [.. typeArguments];
        foreach (string segment in segments)
            ArgumentException.ThrowIfNullOrEmpty(segment);
        foreach (string argument in arguments)
            ArgumentException.ThrowIfNullOrEmpty(argument);

        int totalArity = segments.Sum(MetadataNameArity.OfSegment);
        if (totalArity != arguments.Length)
        {
            var malformed = new StringBuilder("#G");
            Append(malformed, Named(@namespace, segments));
            malformed.Append(arguments.Length).Append(':');
            foreach (string argument in arguments)
                Append(malformed, argument);
            return malformed.ToString();
        }

        var result = new StringBuilder();
        if (@namespace.Length > 0)
            result.Append(Escape(@namespace, escapeDot: false)).Append('.');

        int argumentIndex = 0;
        for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            if (segmentIndex > 0)
                result.Append('.');

            string segment = segments[segmentIndex];
            result.Append(EscapeSegment(
                MetadataNameArity.StripFromSegment(segment),
                escapeGenericParameterMarker: @namespace.Length == 0));
            int arity = MetadataNameArity.OfSegment(segment);
            if (arity <= 0)
                continue;

            result.Append('{');
            for (int index = 0; index < arity; index++)
            {
                if (index > 0)
                    result.Append(',');
                result.Append(arguments[argumentIndex++]);
            }
            result.Append('}');
        }

        return result.ToString();
    }

    internal static string Named(
        string @namespace,
        IEnumerable<string> metadataSegments)
    {
        string typeName = string.Join(
            '.',
            metadataSegments.Select(segment =>
                EscapeSegment(
                    segment,
                    escapeGenericParameterMarker: @namespace.Length == 0)));
        return @namespace.Length == 0
            ? typeName
            : $"{Escape(@namespace, escapeDot: false)}.{typeName}";
    }

    internal static bool RequiresArrayNamePayload(
        string @namespace,
        IEnumerable<string> metadataSegments)
    {
        if (ContainsArrayDelimiter(@namespace))
            return true;

        foreach (string segment in metadataSegments)
        {
            if (ContainsArrayDelimiter(segment))
                return true;
        }

        return false;
    }

    static bool ContainsArrayDelimiter(string value)
        => value.Contains('[') || value.Contains(']');

    static string EscapeSegment(
        string value,
        bool escapeGenericParameterMarker)
    {
        string escaped = Escape(value, escapeDot: true);
        return escapeGenericParameterMarker
            && IsGenericParameterIdentity(value)
                ? $"\\{escaped}"
                : escaped;
    }

    static string Escape(string value, bool escapeDot)
    {
        string escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("+", "\\+", StringComparison.Ordinal)
            .Replace("{", "\\{", StringComparison.Ordinal)
            .Replace("}", "\\}", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("@", "\\@", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("#", "\\#", StringComparison.Ordinal);
        return escapeDot
            ? escaped.Replace(".", "\\.", StringComparison.Ordinal)
            : escaped;
    }

    static bool IsGenericParameterIdentity(string value)
    {
        if (value.Length < 2 || value[0] is not ('T' or 'M'))
            return false;

        foreach (char character in value.AsSpan(1))
        {
            if (character is < '0' or > '9')
                return false;
        }
        return true;
    }

    static void Append(StringBuilder builder, string value)
        => builder.Append(value.Length).Append(':').Append(value);
}
