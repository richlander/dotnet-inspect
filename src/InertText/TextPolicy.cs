using System.Globalization;
using System.Text;

namespace InertText;

/// <summary>
/// Decides whether a scalar may be shown to a sink as it is.
/// </summary>
/// <remarks>
/// The policy half of the split: it answers "is this scalar permitted <em>here</em>", which is
/// a per-sink question, while <see cref="VisualEncoder"/> answers "how is a scalar that is not
/// permitted written down" and never learns why. Deny-shaped policies suit free-form text;
/// allow-shaped policies suit fields whose grammar is externally defined, and only an
/// allow-shaped one can catch a homoglyph, because Cyrillic <c>а</c> and Latin <c>a</c> are
/// both <c>Ll</c> and neither is a hazard.
/// </remarks>
/// <param name="scalar">The scalar to judge.</param>
/// <returns>True when <paramref name="scalar"/> may be passed through unencoded.</returns>
public delegate bool ScalarPolicy(Rune scalar);

/// <summary>
/// Identifies a scalar a policy refused, by position and classification rather than by content.
/// </summary>
/// <param name="Index">The index in the source string where the scalar begins.</param>
/// <param name="Scalar">The refused scalar.</param>
/// <param name="Category">The scalar's Unicode general category.</param>
public readonly record struct ScalarViolation(int Index, Rune Scalar, UnicodeCategory Category)
{
    /// <summary>
    /// Describes the violation without reproducing the character.
    /// </summary>
    /// <remarks>
    /// <c>U+XXXX</c> is ASCII, so it is inert by construction, and for a homoglyph it says
    /// strictly more than the character would: printing <c>е</c> conveys nothing, because it is
    /// indistinguishable from <c>e</c>.
    /// </remarks>
    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"U+{Scalar.Value:X4} ({Category}) at {Index}");
}

/// <summary>
/// The deny-shaped policies, for text whose grammar is not externally defined.
/// </summary>
/// <remarks>
/// What these refuse is defined by Unicode general category rather than by a list, because a
/// list drifts invisibly: one written against terminal escapes will not contain the characters
/// that attack a different sink. <c>Cf</c> is the category a hand-written list always misses,
/// and it holds every code point rustc made a hard error after Trojan Source (CVE-2021-42574) —
/// none of which is anywhere near the C0 range.
///
/// A field whose grammar <em>is</em> externally defined — a package id, a version — should not
/// use these at all. It should supply an allow-shaped <see cref="ScalarPolicy"/> of its own,
/// which is the only thing that catches a homoglyph typosquat.
/// </remarks>
public static class TextPolicy
{
    /// <summary>
    /// Refuses every non-graphic scalar, with no exemptions.
    /// </summary>
    /// <remarks>
    /// For a name or a URL, which has no business containing <c>CR</c>, <c>LF</c> or <c>TAB</c>.
    /// </remarks>
    public static ScalarPolicy Field { get; } = static scalar => !IsNonGraphic(scalar);

    /// <summary>
    /// Refuses every non-graphic scalar except <c>CR</c>, <c>LF</c> and <c>TAB</c>.
    /// </summary>
    /// <remarks>
    /// For genuinely multi-line text such as a package description. The exempt set only ever
    /// shrinks the C0 part: no sink may exempt a bidi control, because there is no rendering
    /// context in which artifact text needs to reorder the reader's screen.
    /// </remarks>
    public static ScalarPolicy Prose { get; } = static scalar =>
        scalar.Value is '\r' or '\n' or '\t' || !IsNonGraphic(scalar);

    /// <summary>
    /// Reports whether <paramref name="scalar"/> falls in a category that can act on a sink.
    /// </summary>
    /// <remarks>
    /// <c>Cc</c> (C0, DEL, C1) attacks terminal control sequences; <c>Cf</c> attacks visual
    /// order and includes every character Trojan Source used; <c>Cs</c> covers unpaired
    /// surrogates, which break UTF-8 conversion; <c>Zl</c> and <c>Zp</c> attack line-oriented
    /// and JS-adjacent consumers.
    /// </remarks>
    public static bool IsNonGraphic(Rune scalar)
        => Rune.GetUnicodeCategory(scalar) switch
        {
            UnicodeCategory.Control => true,
            UnicodeCategory.Format => true,
            UnicodeCategory.Surrogate => true,
            UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator => true,
            _ => false,
        };
}
