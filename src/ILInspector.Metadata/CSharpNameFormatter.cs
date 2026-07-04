using System.Text;

namespace ILInspector.Metadata;

/// <summary>
/// C# identifier and metadata-name formatting shared by declaration emitters.
/// </summary>
public static class CSharpNameFormatter
{
    static readonly HashSet<string> s_reservedKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while", "record", "required", "init", "file", "scoped",
    };

    public static bool IsReservedKeyword(string name)
        => s_reservedKeywords.Contains(name) || name == "await";

    public static string EscapeIdentifier(string name)
        => IsReservedKeyword(name) ? "@" + name : name;

    public static string EscapeNamespace(string ns)
        => string.Join(".", ns.Split('.').Select(EscapeIdentifier));

    public static string StripArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick >= 0 ? name[..tick] : name;
    }

    public static string CleanTypeName(string type)
    {
        if (type.Contains('!'))
            return "object";

        type = type.Replace("modreq(", "", StringComparison.Ordinal)
            .Replace("modopt(", "", StringComparison.Ordinal)
            .Replace(")", "", StringComparison.Ordinal)
            .Trim();

        type = type switch
        {
            "System.String" => "string",
            "System.Int32" => "int",
            "System.Void" => "void",
            _ => type,
        };

        return EscapeTypeKeywords(type);
    }

    static string EscapeTypeKeywords(string type)
    {
        var sb = new StringBuilder(type.Length);
        int i = 0;
        while (i < type.Length)
        {
            char c = type[i];
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < type.Length && (char.IsLetterOrDigit(type[i]) || type[i] == '_'))
                    i++;

                string word = type[start..i];
                bool alreadyEscaped = start > 0 && type[start - 1] == '@';
                bool qualifiedSegment = start > 0 && type[start - 1] == '.';
                bool bareSpelling = (word is "void" or "ref" || IsPrimitiveTypeName(word)) && !qualifiedSegment;
                if (!alreadyEscaped && !bareSpelling && IsReservedKeyword(word))
                    sb.Append('@');
                sb.Append(word);
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    static bool IsPrimitiveTypeName(string name)
        => name is "bool" or "byte" or "sbyte" or "char" or "decimal" or "double"
            or "float" or "int" or "uint" or "nint" or "nuint" or "long" or "ulong"
            or "object" or "short" or "ushort" or "string";
}
