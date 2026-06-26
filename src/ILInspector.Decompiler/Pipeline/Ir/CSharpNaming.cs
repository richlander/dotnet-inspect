namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Pure C# identifier and name spelling utilities used by <see cref="CSharpPrinter"/>:
/// reserved-keyword recognition, usable-identifier validation, and the decoding of
/// compiler-generated metadata names (auto-property backing fields, local-function
/// mangled names) back to their source spelling. These are stateless functions of
/// their string inputs — they hold no per-function printer state.
/// </summary>
internal static class CSharpNaming
{
    /// <summary>A name safe to emit bare as a C# identifier: letters/digits/underscore, no leading digit, and not a reserved keyword (which would need an <c>@</c> escape).</summary>
    public static bool IsUsableIdentifier(string name)
        => IsEscapableIdentifier(name) && !ReservedKeywords.Contains(name);

    /// <summary>
    /// A name C# can emit as an identifier, possibly via an <c>@</c> escape: valid
    /// identifier characters and start, regardless of whether it is a reserved
    /// keyword. This distinguishes an escapable keyword like <c>return</c> (which
    /// <see cref="EscapeIdentifier"/> renders as the legal <c>@return</c>) from a
    /// fundamentally unspellable metadata name like <c>bad-name</c>.
    /// </summary>
    public static bool IsEscapableIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name) || !(char.IsLetter(name[0]) || name[0] == '_'))
            return false;
        foreach (char c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_'))
                return false;
        }
        return true;
    }

    /// <summary>An identifier safe to emit in C# source: a reserved keyword is
    /// <c>@</c>-escaped (a parameter named <c>delegate</c> becomes <c>@delegate</c>,
    /// which would otherwise be CS1001); any other name is returned unchanged.</summary>
    public static string EscapeIdentifier(string name)
        => ReservedKeywords.Contains(name) ? "@" + name : name;

    static readonly HashSet<string> ReservedKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while",
    };

    /// <summary>The property name an auto-property backing field <c>&lt;Prop&gt;k__BackingField</c> backs, or null for an ordinary field.</summary>
    public static string? BackingFieldProperty(string fieldName)
    {
        const string suffix = ">k__BackingField";
        return fieldName.Length > suffix.Length + 1 && fieldName[0] == '<' && fieldName.EndsWith(suffix, StringComparison.Ordinal)
            ? fieldName[1..^suffix.Length]
            : null;
    }

    /// <summary>
    /// The source name of a call target. A compiler-generated local function
    /// carries the metadata name <c>&lt;Enclosing&gt;g__Local|N_M</c>, which is
    /// not a valid C# identifier; the source name is the segment between
    /// <c>g__</c> and the <c>|</c> ordinal suffix.
    /// </summary>
    public static string MethodName(string name)
    {
        if (!name.StartsWith('<'))
            return name;
        int marker = name.IndexOf("g__", StringComparison.Ordinal);
        if (marker < 0)
            return name;
        int start = marker + 3;
        int bar = name.IndexOf('|', start);
        return bar > start ? name[start..bar] : name[start..];
    }
}
