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
/// <see cref="CSharpDeclarationWriter.EscapeIdentifier"/>, and its containing
/// counterpart is <see cref="ContainIdentifierForDeclaration"/>.
/// </remarks>
public static class CSharpIdentifier
{
    /// <summary>A name safe to emit bare as a C# identifier: valid identifier
    /// characters and start, and not a keyword that requires an <c>@</c> escape.</summary>
    public static bool IsUsable(string name)
        => IsEscapable(name) && !CSharpKeywords.RequiresBodyEscape(name);

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
        => CSharpIdentifierCore.IsIdentifierLike(name);

    /// <summary>An identifier safe to emit in C# source: a reserved keyword is
    /// <c>@</c>-escaped (a parameter named <c>delegate</c> becomes <c>@delegate</c>).
    /// The contextual <c>await</c> keyword is escaped too: it is illegal as a bare
    /// identifier inside async methods, which is where recovered local functions and
    /// parameter references can appear.</summary>
    public static string Escape(string name)
        => CSharpKeywords.RequiresBodyEscape(name) ? "@" + name : name;

    /// <summary>The safest emittable spelling of a metadata name: an identifier-like
    /// name is keyword-escaped, and any other name is sanitized into a legal
    /// identifier by <see cref="SanitizeUnspellable"/>.</summary>
    public static string Sanitize(string name)
        => CSharpIdentifierCore.Sanitize(name, CSharpKeywords.RequiresBodyEscape);

    /// <summary>
    /// <see cref="Escape"/> plus containment of a name carrying a line terminator,
    /// which would otherwise break out of its code fence, table row, or tree gutter
    /// (issue #3319). Byte-neutral for every name a compiler can emit.
    /// </summary>
    /// <remarks>
    /// Narrower than <see cref="Sanitize"/> on purpose: an unspellable name that
    /// cannot break the output is preserved so identity stays visible and the
    /// fidelity marker keeps reporting it. Gated by
    /// <c>CSharpIdentifierSanitizationTests</c> and, end to end, by
    /// <c>UntrustedIdentifierPresentationTests</c>.
    /// </remarks>
    public static string ContainIdentifier(string name)
        => CSharpIdentifierCore.ContainIdentifier(name, CSharpKeywords.RequiresBodyEscape);

    /// <summary>
    /// The declaration-position counterpart of <see cref="ContainIdentifier"/>, over
    /// the broader declaration keyword set that additionally covers <c>record</c>,
    /// <c>required</c>, <c>init</c>, <c>file</c>, and <c>scoped</c>. Declaration
    /// sites must use this rather than <see cref="ContainIdentifier"/>, which would
    /// narrow their keyword escaping.
    /// </summary>
    public static string ContainIdentifierForDeclaration(string name)
        => CSharpIdentifierCore.ContainIdentifier(name, CSharpKeywords.RequiresDeclarationEscape);

    /// <summary>Rewrites an unspellable metadata name into a legal C# identifier:
    /// a non-identifier start gets a leading underscore, and every non-identifier
    /// character becomes <c>_</c>. The result is keyword-escaped for completeness.</summary>
    public static string SanitizeUnspellable(string name)
        => CSharpIdentifierCore.SanitizeUnspellable(name, CSharpKeywords.RequiresBodyEscape);
}
