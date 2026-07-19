namespace ILInspector.Metadata;

/// <summary>
/// Authoritative C# keyword classification for metadata-backed identifiers.
/// Declaration spelling conservatively escapes contextual keywords that can
/// acquire meaning in declaration positions; body spelling additionally treats
/// <c>await</c> as reserved without adding declaration-only noise.
/// </summary>
public static class CSharpKeywords
{
    /// <summary>Whether an identifier should be escaped in a declaration or type-name position.</summary>
    public static bool RequiresDeclarationEscape(string identifier)
        => s_reserved.Contains(identifier) || s_declarationContextual.Contains(identifier);

    /// <summary>Whether an identifier should be escaped in a method-body or expression position.</summary>
    public static bool RequiresBodyEscape(string identifier)
        => s_reserved.Contains(identifier) || identifier == "await";

    private static readonly HashSet<string> s_reserved = new(StringComparer.Ordinal)
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

    private static readonly HashSet<string> s_declarationContextual = new(StringComparer.Ordinal)
    {
        "await", "file", "init", "record", "required", "scoped",
    };
}
