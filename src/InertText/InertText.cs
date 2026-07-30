using System.Globalization;
using System.Text;

namespace Inert;

/// <summary>
/// Makes untrusted text inert, so a sink cannot be made to act on what it contains.
/// </summary>
/// <remarks>
/// The contract comes from BSD <c>vis(3)</c> ("visually encode characters"): the output is
/// inert (no sink interprets it), lossless (nothing is dropped, so the reader still sees what
/// was actually there), and invertible (<see cref="TryDecode"/> recovers the original exactly).
/// This is not neutralization, which has none of the three — it removes the evidence and is
/// silent when its idea of "dangerous" is incomplete.
///
/// What gets encoded is defined by Unicode general category rather than by a list, because a
/// list drifts invisibly: one written against terminal escapes will not contain the characters
/// that attack a different sink. <c>Cf</c> is the category a hand-written list always misses,
/// and it holds every code point rustc made a hard error after Trojan Source (CVE-2021-42574) —
/// none of which is anywhere near the C0 range.
///
/// Inertness is the only guarantee. Text that cannot act on a sink can still lie about what it
/// is: a Cyrillic <c>а</c> in a host name, or a fullwidth solidus in <c>evil.com／nuget.org</c>,
/// is composed entirely of legitimate graphic characters and passes through untouched by
/// design. Detecting that is confusability analysis, a different problem with a different
/// answer, and callers who need it need something in addition to this.
/// </remarks>
public static class InertText
{
    /// <summary>
    /// Reports whether <paramref name="value"/> can be shown to a sink unchanged.
    /// </summary>
    /// <remarks>
    /// The early-fail check, for callers that would rather reject untrusted text than display
    /// an encoded rendering of it. This is deliberately not "would <see cref="Encode"/> change
    /// it": a backslash is inert, but <see cref="Encode"/> still rewrites it so the transform
    /// stays invertible, and reporting an ordinary Windows path as non-inert would make the
    /// check useless exactly where it is most likely to be applied.
    /// </remarks>
    public static bool IsInert(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        for (int i = 0; i < value.Length; i++)
        {
            if (IsPairAt(value, i))
            {
                i++;
                continue;
            }

            if (IsActive(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns <paramref name="value"/> with every code point that could act on a sink
    /// visually encoded.
    /// </summary>
    /// <remarks>
    /// Ordinary text, including CJK, diacritics, and ligatures, is returned unchanged.
    /// </remarks>
    public static string Encode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!NeedsEncoding(value))
        {
            return value;
        }

        StringBuilder builder = new(value.Length + 8);

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            // A well-formed surrogate pair is an ordinary code point — an emoji or any other
            // astral character — not the unpaired surrogate that Cs is in the list for.
            if (IsPairAt(value, i))
            {
                builder.Append(c).Append(value[i + 1]);
                i++;
                continue;
            }

            switch (c)
            {
                case '\\':
                    // The introducer itself, so the transform stays invertible.
                    builder.Append(@"\\");
                    break;
                case <= '\u001F':
                    // Caret notation, introduced by a backslash. Introducing with the caret
                    // instead collides: U+001E is 0x1E + 0x40 = '^', so RS and a literal caret
                    // would spell the same thing.
                    builder.Append(@"\^").Append((char)(c + 0x40));
                    break;
                case '\u007F':
                    builder.Append(@"\^?");
                    break;
                default:
                    if (IsActive(c))
                    {
                        builder.Append(@"\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Recovers the original text from <see cref="Encode"/>'s output.
    /// </summary>
    /// <remarks>
    /// Exists because invertibility is an asserted property, and an encoder without a decoder
    /// cannot demonstrate it: a caret-introduced spelling survives casual inspection and only
    /// fails a round-trip that a decoder makes possible.
    /// </remarks>
    /// <returns>True when <paramref name="encoded"/> is well-formed.</returns>
    public static bool TryDecode(string encoded, out string? value)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        value = null;
        StringBuilder builder = new(encoded.Length);

        for (int i = 0; i < encoded.Length; i++)
        {
            if (encoded[i] != '\\')
            {
                builder.Append(encoded[i]);
                continue;
            }

            if (++i == encoded.Length)
            {
                return false;
            }

            switch (encoded[i])
            {
                case '\\':
                    builder.Append('\\');
                    break;
                case '^':
                    if (++i == encoded.Length)
                    {
                        return false;
                    }

                    char caret = encoded[i];

                    if (caret == '?')
                    {
                        builder.Append('\u007F');
                    }
                    else if (caret is >= '@' and <= '_')
                    {
                        builder.Append((char)(caret - 0x40));
                    }
                    else
                    {
                        return false;
                    }

                    break;
                case 'u':
                    if (i + 4 >= encoded.Length
                        || !ushort.TryParse(
                            encoded.AsSpan(i + 1, 4),
                            NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture,
                            out ushort code))
                    {
                        return false;
                    }

                    builder.Append((char)code);
                    i += 4;
                    break;
                default:
                    return false;
            }
        }

        value = builder.ToString();
        return true;
    }

    private static bool IsPairAt(string value, int i)
        => char.IsHighSurrogate(value[i])
            && i + 1 < value.Length
            && char.IsLowSurrogate(value[i + 1]);

    // Not the same question as IsInert: a backslash is inert, but Encode still has to rewrite
    // it to stay invertible.
    private static bool NeedsEncoding(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (IsPairAt(value, i))
            {
                i++;
                continue;
            }

            if (value[i] == '\\' || IsActive(value[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsActive(char c)
        => CharUnicodeInfo.GetUnicodeCategory(c) switch
        {
            // C0, DEL, and C1: terminal control sequences.
            UnicodeCategory.Control => true,
            // Bidi overrides and isolates, U+FEFF, zero-width joiners: visual reordering.
            UnicodeCategory.Format => true,
            // Unpaired surrogates: UTF-8 conversion.
            UnicodeCategory.Surrogate => true,
            // U+2028 and U+2029: line-oriented and JS-adjacent consumers.
            UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator => true,
            _ => false,
        };
}
