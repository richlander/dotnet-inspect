using System.Globalization;
using System.Text;

namespace ILInspector.CSharp;

/// <summary>
/// The authoritative producer of spellable C# identifiers: reserved-keyword
/// <c>@</c>-escaping and the sanitization of unspellable metadata names (names with
/// non-identifier characters such as compiler-generated <c>&lt;&gt;c__DisplayClass</c>
/// spellings) back into legal identifiers. These are stateless functions of their
/// string inputs.
/// </summary>
/// <remarks>
/// This is the <em>position-agnostic</em> escaper: it escapes only names that are
/// unsafe as a bare identifier in <em>any</em> position — the always-reserved
/// keywords plus the contextual <c>await</c> keyword (illegal bare inside async
/// bodies). It deliberately does <em>not</em> escape declaration-only contextual
/// keywords (<c>record</c>, <c>required</c>, <c>init</c>, <c>file</c>, <c>scoped</c>),
/// which are legal bare identifiers in expression and body position where raised and
/// compile-back identifiers live. Declaration-position escaping, which additionally
/// covers those contextual keywords, is
/// <see cref="CSharpDeclarationWriter.EscapeIdentifier"/>.
/// </remarks>
public static class CSharpIdentifier
{
    /// <summary>A name safe to emit bare as a C# identifier: valid identifier
    /// characters and start, and not a keyword that requires an <c>@</c> escape.</summary>
    public static bool IsUsable(string name)
        => IsEscapable(name) && !RequiresEscape(name);

    /// <summary>
    /// A name C# can emit as an identifier, possibly via an <c>@</c> escape: valid
    /// ASCII identifier characters and start, regardless of whether it is a reserved
    /// keyword. This distinguishes an escapable keyword like <c>return</c> (which
    /// <see cref="Escape"/> renders as the legal <c>@return</c>) from a fundamentally
    /// unspellable metadata name like <c>bad-name</c>.
    /// </summary>
    public static bool IsEscapable(string name)
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

    /// <summary>
    /// Whether a name spells as a C# identifier under the full Unicode identifier
    /// grammar (letters, connector/combining marks, and letter-number categories),
    /// so that an escapable keyword or a Unicode identifier is recognized rather than
    /// leaking a raw unspeakable name into <see cref="Sanitize"/>'s sanitizing branch.
    /// </summary>
    public static bool IsIdentifierLike(string name)
    {
        if (name.Length == 0)
            return false;
        bool first = true;
        foreach (var rune in name.EnumerateRunes())
        {
            if (first)
            {
                if (!IsIdentifierStartRune(rune))
                    return false;
                first = false;
            }
            else if (!IsIdentifierPartRune(rune))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>An identifier safe to emit in C# source: a reserved keyword is
    /// <c>@</c>-escaped (a parameter named <c>delegate</c> becomes <c>@delegate</c>).
    /// The contextual <c>await</c> keyword is escaped too: it is illegal as a bare
    /// identifier inside async methods, which is where recovered local functions and
    /// parameter references can appear.</summary>
    public static string Escape(string name)
        => RequiresEscape(name) ? "@" + name : name;

    /// <summary>The safest emittable spelling of a metadata name: an identifier-like
    /// name is keyword-escaped, and any other name is sanitized into a legal
    /// identifier by <see cref="SanitizeUnspellable"/>.</summary>
    public static string Sanitize(string name)
        => IsIdentifierLike(name) ? Escape(name) : SanitizeUnspellable(name);

    /// <summary>Rewrites an unspellable metadata name into a legal C# identifier:
    /// a non-identifier start gets a leading underscore, and every non-identifier
    /// character becomes <c>_</c>. The result is keyword-escaped for completeness.</summary>
    public static string SanitizeUnspellable(string name)
    {
        var sb = new StringBuilder(name.Length + 1);
        if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_'))
            sb.Append('_');
        foreach (char c in name)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        return RequiresEscape(sb.ToString()) ? "@" + sb : sb.ToString();
    }

    static bool RequiresEscape(string name)
        => ReservedKeywords.Contains(name) || name == "await";

    static bool IsIdentifierStartRune(Rune rune)
        => rune.Value == '_'
            || Rune.IsLetter(rune)
            || Rune.GetUnicodeCategory(rune) == UnicodeCategory.LetterNumber;

    static bool IsIdentifierPartRune(Rune rune)
        => rune.Value == '_'
            || Rune.IsLetterOrDigit(rune)
            || Rune.GetUnicodeCategory(rune) is
                UnicodeCategory.LetterNumber
                or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.Format;

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
}
