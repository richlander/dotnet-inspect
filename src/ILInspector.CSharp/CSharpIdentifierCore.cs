namespace ILInspector.CSharp;

using System.Globalization;
using System.Linq;
using System.Text;

/// <summary>
/// The identifier-spelling primitives shared by every layer that emits C#
/// identifier text, parameterized over the keyword set so declaration-position and
/// body-position callers get the same sanitization over their own keyword rules.
/// </summary>
/// <remarks>
/// This is <c>internal</c> and source-linked (like <see cref="CSharpKeywords"/>)
/// rather than referenced, because <c>ILInspector.Metadata</c> emits C# declaration
/// spellings but cannot reference <c>ILInspector.CSharp</c> — that reference runs
/// the other way. Public callers use <see cref="CSharpIdentifier"/>.
/// </remarks>
internal static class CSharpIdentifierCore
{
    /// <summary>
    /// Whether a name spells as a C# identifier under the full Unicode identifier
    /// grammar (letters, connector/combining marks, and letter-number categories),
    /// so that an escapable keyword or a Unicode identifier is recognized rather
    /// than leaking a raw unspeakable name into the sanitizing branch.
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

    /// <summary>
    /// The safest emittable spelling of a metadata name under <paramref name="requiresEscape"/>:
    /// an identifier-like name is keyword-escaped, and any other name is folded to
    /// identifier characters by <see cref="SanitizeUnspellable"/>.
    /// </summary>
    public static string Sanitize(string name, Func<string, bool> requiresEscape)
        => IsIdentifierLike(name)
            ? (requiresEscape(name) ? "@" + name : name)
            : SanitizeUnspellable(name, requiresEscape);

    /// <summary>
    /// The spelling for a metadata name that reaches rendered output: keyword
    /// escaping, plus containment of the two things a name must never do, which are
    /// to carry a line terminator out of its code fence, Markdown table row, or
    /// <c>type</c> tree gutter, and to move or rewrite the terminal cursor once it
    /// gets there (issue #3319).
    /// </summary>
    /// <remarks>
    /// Deliberately narrower than <see cref="Sanitize"/>. An unspellable name that
    /// cannot break the output — <c>bad-name</c> — is preserved, because the
    /// decompiler's contract for those is to keep identity visible and report the
    /// problem through the fidelity marker instead of quietly rewriting the name;
    /// <c>KeywordIdentifierTests.RaisedNullConditionalUnspellableProperty_PreservesIdentity</c>
    /// and <c>UnspeakableNameFidelityTests</c> gate exactly that. Narrowing to line
    /// terminators and rendering hazards is also what makes this byte-neutral for
    /// every name a compiler can emit, since none of them contain either.
    /// <para>
    /// A line terminator is not the whole hazard. Vertical tab is not a C# line
    /// terminator, so <c>ReplaceLineEndings</c> and
    /// <see cref="ContainsLineTerminator"/> both leave it, but it still moves a
    /// terminal cursor down a row — which is the same injection by a different
    /// character. ANSI escapes recolor or erase the rendered output, and bidi
    /// overrides reorder what follows them. This is the repository's existing
    /// position for the taste side-comment channel; see
    /// <c>ApiOutputFormatter.NeutralizeForSideComment</c>, which shares
    /// <see cref="IsRenderingHazard"/> with this method so the two channels cannot
    /// drift apart.
    /// </para>
    /// </remarks>
    public static string ContainIdentifier(string name, Func<string, bool> requiresEscape)
        => ContainsLineTerminator(name) || name.Any(IsRenderingHazard)
            ? SanitizeUnspellable(name, requiresEscape)
            : (requiresEscape(name) ? "@" + name : name);

    /// <summary>
    /// The line terminators <c>ReplaceLineEndings</c> recognizes — the set that can
    /// end a line in rendered output, and so the set that must never survive inside
    /// a name.
    /// </summary>
    public static bool ContainsLineTerminator(string name)
    {
        foreach (char c in name)
        {
            if (c is '\r' or '\n' or '\f' or '\u0085' or '\u2028' or '\u2029')
                return true;
        }
        return false;
    }

    /// <summary>Rewrites an unspellable metadata name into a legal C# identifier:
    /// a non-identifier start gets a leading underscore, and every non-identifier
    /// character becomes <c>_</c>. The result is keyword-escaped for completeness.</summary>
    public static string SanitizeUnspellable(string name, Func<string, bool> requiresEscape)
    {
        var sb = new StringBuilder(name.Length + 1);
        if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_'))
            sb.Append('_');
        foreach (char c in name)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        return requiresEscape(sb.ToString()) ? "@" + sb : sb.ToString();
    }

    /// <summary>
    /// Contains a composed name that legitimately carries structural punctuation —
    /// an explicit interface implementation's dots, a generic instantiation's angle
    /// brackets, <c>.ctor</c> — and so cannot be spelled as a simple identifier.
    /// Folds the line terminators that would let it break out of a code fence, a
    /// table row, or a tree gutter, and changes nothing else (issue #3319).
    /// </summary>
    /// <remarks>
    /// This is the weakest containment in this file, and it is the right one here:
    /// the text is already not a C# identifier (<c>.ctor</c>, <c>IFoo.Bar</c>), so
    /// neither the "escapes as an identifier" property of
    /// <see cref="ContainIdentifier"/> nor the "is a legal C# identifier" property
    /// of <see cref="Sanitize"/> applies — folding a composed name into one would
    /// destroy the structure that makes it readable. Do not use this for a simple
    /// identifier; those go through <see cref="ContainIdentifier"/>. Gated by
    /// <c>CSharpIdentifierSanitizationTests.ContainComposedName_*</c>.
    /// </remarks>
    public static string ContainComposedName(string name)
    {
        var folded = name.ReplaceLineEndings(" ");
        if (!folded.Any(IsRenderingHazard))
            return folded;

        // A visible \uXXXX keeps the identity legible, which matters more here than
        // it does for an identifier: this text is already not compilable C#, so
        // there is nothing to be gained by folding it to identifier characters and
        // something to be lost by dropping the real bytes of the name.
        var builder = new StringBuilder(folded.Length);
        foreach (var ch in folded)
        {
            if (IsRenderingHazard(ch))
                builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)ch:X4}");
            else
                builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>
    /// A character that is safe as C# syntax but not safe once rendered: a C0/C1
    /// control that moves or rewrites the terminal cursor (vertical tab, ESC, NUL),
    /// or a Unicode bidi control that reorders its neighbors. Tab is deliberately
    /// allowed — it is legal and renders as space.
    /// </summary>
    public static bool IsRenderingHazard(char ch)
        => ch != '\t' && (char.IsControl(ch) || IsBidiControl(ch));

    /// <summary>
    /// Exactly Unicode's Bidi_Control set: ALM, LRM/RLM, the LRE/RLE/PDF/LRO/RLO
    /// embeddings and overrides, and the LRI/RLI/FSI/PDI isolates. Deliberately
    /// narrower than the Cf category — a zero-width joiner or a BOM does not
    /// reorder its neighbors, and legitimate identifiers may contain format
    /// characters, so escaping all of Cf would corrupt ordinary names.
    /// </summary>
    public static bool IsBidiControl(char ch)
        => ch is '\u061C' or '\u200E' or '\u200F'
            or >= '\u202A' and <= '\u202E'
            or >= '\u2066' and <= '\u2069';

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
}
