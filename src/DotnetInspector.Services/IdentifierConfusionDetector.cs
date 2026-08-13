using System.Text;
using ILInspector.Text;

namespace DotnetInspector.Services;

[Flags]
public enum IdentifierConcern
{
    None = 0,
    NonAscii = 1 << 0,
    ReservedPrefixHomoglyph = 1 << 1,
}

public readonly record struct IdentifierHomoglyph(
    int CodePoint,
    char LooksLike);

public sealed record ReservedPrefixHomoglyphMatch(
    string ReservedPrefix,
    double Similarity,
    IReadOnlyList<IdentifierHomoglyph> Homoglyphs);

public sealed record IdentifierConfusion(
    IdentifierConcern Concerns,
    IReadOnlyList<int> NonAsciiCodePoints,
    ReservedPrefixHomoglyphMatch? ReservedPrefixMatch);

/// <summary>
/// Finds non-ASCII identifier characters and a bounded set of cross-script homoglyphs in
/// package or assembly names that closely resemble reserved ecosystem prefixes.
/// </summary>
public static class IdentifierConfusionDetector
{
    public const double MinimumReservedPrefixSimilarity = 0.8;

    private static readonly string[] ReservedPrefixes =
        ["System", "Microsoft", "Azure"];

    public static IdentifierConfusion? Inspect(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return null;

        List<int> nonAscii = [];
        HashSet<int> seenNonAscii = [];
        foreach (Rune rune in identifier.EnumerateRunes())
        {
            if (!rune.IsAscii && seenNonAscii.Add(rune.Value))
                nonAscii.Add(rune.Value);
        }

        if (nonAscii.Count == 0)
            return null;

        ReservedPrefixHomoglyphMatch? bestMatch = null;
        foreach (string reservedPrefix in ReservedPrefixes)
        {
            Rune[] candidate = identifier
                .EnumerateRunes()
                .Take(reservedPrefix.Length)
                .ToArray();
            if (candidate.Length != reservedPrefix.Length)
                continue;

            string candidateText = string.Concat(candidate.Select(static rune => rune.ToString()));
            double similarity = StringDistance.Similarity(
                candidateText.ToLowerInvariant(),
                reservedPrefix.ToLowerInvariant());
            if (similarity < MinimumReservedPrefixSimilarity)
                continue;

            List<IdentifierHomoglyph> homoglyphs = [];
            bool matches = true;
            for (int index = 0; index < candidate.Length; index++)
            {
                char target = char.ToLowerInvariant(reservedPrefix[index]);
                Rune rune = candidate[index];
                if (rune.IsAscii
                    && char.ToLowerInvariant((char)rune.Value) == target)
                {
                    continue;
                }

                if (!TryGetAsciiHomoglyph(rune, out char looksLike)
                    || looksLike != target)
                {
                    matches = false;
                    break;
                }

                homoglyphs.Add(new IdentifierHomoglyph(rune.Value, looksLike));
            }

            if (!matches || homoglyphs.Count == 0)
                continue;

            if (bestMatch is null || similarity > bestMatch.Similarity)
            {
                bestMatch = new ReservedPrefixHomoglyphMatch(
                    reservedPrefix,
                    similarity,
                    homoglyphs);
            }
        }

        IdentifierConcern concerns = IdentifierConcern.NonAscii;
        if (bestMatch is not null)
            concerns |= IdentifierConcern.ReservedPrefixHomoglyph;

        return new IdentifierConfusion(concerns, nonAscii, bestMatch);
    }

    private static bool TryGetAsciiHomoglyph(Rune rune, out char ascii)
    {
        ascii = rune.Value switch
        {
            // Greek and Cyrillic characters whose ordinary glyph is confusable with a
            // Latin character used by System, Microsoft, or Azure. This deliberately
            // bounded catalog is a high-confidence discriminator, not a claim to implement
            // the complete Unicode confusables table.
            0x0391 or 0x0410 or 0x0430 => 'a',
            0x03F9 or 0x0421 or 0x0441 => 'c',
            0x0395 or 0x0415 or 0x0435 => 'e',
            0x0406 or 0x0456 or 0x0399 => 'i',
            0x039C or 0x041C => 'm',
            0x039F or 0x03BF or 0x041E or 0x043E => 'o',
            0x0405 or 0x0455 => 's',
            0x03A4 or 0x0422 or 0x0442 => 't',
            0x03A5 or 0x0443 => 'y',
            0x0396 => 'z',
            _ => '\0',
        };
        return ascii != '\0';
    }
}
