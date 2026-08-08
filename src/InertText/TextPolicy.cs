using System.Globalization;
using System.Text;

namespace InertText;

/// <summary>
/// Names the kind of text a value is, which is what decides the scalars it may show as they are.
/// </summary>
/// <remarks>
/// A closed set, and closed is the feature rather than a limitation. The obvious alternative —
/// letting each caller pass a predicate — was tried first and fails twice over. It drifts,
/// because a rule written at one sink is invisible to the next: five predicates in this
/// repository answer "is this scalar dangerous" and disagree by up to 48 BMP characters, and one
/// of them escapes ANSI but not bidi, so the layer that renders hostile assembly metadata misses
/// the attack class Trojan Source is named after. And it leaks, because repairing a value that
/// does not satisfy a caller-supplied predicate means handing that predicate the decoded
/// original — the audit boundary the capability namespace exists to draw, crossed by a callback
/// in a file whose using block says nothing.
///
/// A closed set removes both. The rules are written once and shared, so a fix reaches every sink
/// at the same time; and no caller code runs during a repair, so no decoded scalar leaves the
/// library. What the set costs is that a sink cannot express a rule of its own. That is the
/// right price: a rule which really is unique to one sink — a package id's grammar, say — wants
/// rejection rather than encoding, because there is no spelling of a homoglyph that satisfies an
/// allow list, and rejection is a different operation that belongs in a different API.
///
/// Adding a kind is an enum member and an arm in the rule table, and no new public member
/// anywhere. That is the whole extension mechanism.
///
/// Deny-shaped throughout, and defined by Unicode general category rather than by a list,
/// because a list drifts invisibly: one written against terminal escapes will not contain the
/// characters that attack a different sink. <c>Cf</c> is the category a hand-written list always
/// misses, and it holds every code point rustc made a hard error after Trojan Source
/// (CVE-2021-42574) — none of which is anywhere near the C0 range.
/// </remarks>
public enum TextPolicy
{
    /// <summary>
    /// Refuses every non-graphic scalar, with no exemptions.
    /// </summary>
    /// <remarks>
    /// For a name or a URL, which has no business containing <c>CR</c>, <c>LF</c> or <c>TAB</c>.
    ///
    /// First, so it is what <c>default(TextPolicy)</c> resolves to. A zero value is reachable for
    /// any enum whatever the declared members say — an unchecked cast or a deserializer will
    /// produce one — so the only question is which rule it lands on, and the strictest is the one
    /// where being wrong costs nothing.
    /// </remarks>
    Field,

    /// <summary>
    /// Refuses every non-graphic scalar except <c>CR</c>, <c>LF</c> and <c>TAB</c>.
    /// </summary>
    /// <remarks>
    /// For genuinely multi-line text such as a package description. The exempt set only ever
    /// shrinks the C0 part: no kind of text may exempt a bidi control, because there is no
    /// rendering context in which artifact text needs to reorder the reader's screen.
    /// </remarks>
    Prose,
}

/// <summary>
/// Identifies a scalar a policy refused, by position and classification rather than by content.
/// </summary>
/// <remarks>
/// <paramref name="Scalar"/> is an <see cref="int"/> rather than a <see cref="Rune"/> because an
/// unpaired surrogate is one of the things this has to name, and <see cref="Rune"/> cannot hold
/// one — its invariant excludes <c>D800</c>–<c>DFFF</c>, so constructing it throws. Reporting
/// through <see cref="Rune"/> forced the replacement character in that arm, which named the
/// wrong code point for the 2048 values whose identity matters most.
/// </remarks>
/// <param name="Index">The index in the source string where the scalar begins.</param>
/// <param name="Scalar">The refused scalar, or the raw code unit for an unpaired surrogate.</param>
/// <param name="Category">The scalar's Unicode general category.</param>
public readonly record struct ScalarViolation(int Index, int Scalar, UnicodeCategory Category)
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
            $"U+{Scalar:X4} ({Category}) at {Index}");
}

/// <summary>
/// Decides whether a scalar may be shown as it is.
/// </summary>
/// <remarks>
/// Internal, and that is the point of <see cref="TextPolicy"/>. The encoder still wants a
/// predicate — it asks the question once per scalar in its hot loop — but no caller supplies one,
/// so a repair never runs code the library did not write, and a decoded scalar never reaches a
/// callback.
/// </remarks>
/// <param name="scalar">The scalar to judge.</param>
/// <returns>True when <paramref name="scalar"/> may be passed through unencoded.</returns>
internal delegate bool ScalarPolicy(Rune scalar);

/// <summary>
/// The rule table: the single place each <see cref="TextPolicy"/> is defined.
/// </summary>
internal static class ScalarPolicies
{
    private static readonly ScalarPolicy FieldPolicy = static scalar => !IsNonGraphic(scalar);

    private static readonly ScalarPolicy ProsePolicy = static scalar =>
        scalar.Value is '\r' or '\n' or '\t' || !IsNonGraphic(scalar);

    /// <summary>Resolves the rule for <paramref name="policy"/>.</summary>
    /// <remarks>
    /// An unrecognised value falls to <see cref="TextPolicy.Field"/> rather than throwing. An
    /// enum parameter is not a closed set at runtime — <c>(TextPolicy)42</c> is a legal value —
    /// and a text-hardening library that throws on one has turned a rendering decision into an
    /// availability bug. Falling to the strictest rule is wrong only in the direction that
    /// over-encodes.
    /// </remarks>
    internal static ScalarPolicy For(TextPolicy policy)
        => policy switch
        {
            TextPolicy.Prose => ProsePolicy,
            _ => FieldPolicy,
        };

    /// <summary>
    /// Reports whether <paramref name="scalar"/> falls in a category that can act on a sink.
    /// </summary>
    /// <remarks>
    /// <c>Cc</c> (C0, DEL, C1) attacks terminal control sequences; <c>Cf</c> attacks visual order
    /// and includes every character Trojan Source used; <c>Zl</c> and <c>Zp</c> attack
    /// line-oriented and JS-adjacent consumers.
    ///
    /// The <c>Cs</c> arm is unreachable and kept only for totality of the switch. No
    /// <see cref="Rune"/> can carry that category, because the type's invariant excludes the
    /// surrogate range outright. Unpaired surrogates are caught ahead of any policy, in the
    /// decode step.
    /// </remarks>
    internal static bool IsNonGraphic(Rune scalar)
        => Rune.GetUnicodeCategory(scalar) switch
        {
            UnicodeCategory.Control => true,
            UnicodeCategory.Format => true,
            UnicodeCategory.Surrogate => true,
            UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator => true,
            _ => false,
        };
}
