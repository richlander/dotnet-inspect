namespace CiChangeDetection.Planning;

/// <summary>
/// The subset of Bash <c>case</c> pattern matching the classifier rules use:
/// literal bytes plus <c>*</c>, which crosses <c>/</c> exactly as in the
/// shell. Matching runs over raw path bytes so an invalidly encoded path is
/// never replacement-decoded to be routed.
/// </summary>
internal static class BytePattern
{
    /// <summary>
    /// Reports whether raw bytes match an ASCII shell-style pattern.
    /// </summary>
    /// <param name="value">The raw path bytes.</param>
    /// <param name="pattern">The ASCII pattern, using only <c>*</c>.</param>
    /// <returns>True when the pattern matches the whole value.</returns>
    internal static bool Matches(ReadOnlySpan<byte> value, string pattern)
    {
        int valueIndex = 0;
        int patternIndex = 0;
        int starPattern = -1;
        int starValue = 0;
        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length
                && pattern[patternIndex] == '*')
            {
                starPattern = patternIndex++;
                starValue = valueIndex;
            }
            else if (patternIndex < pattern.Length
                && pattern[patternIndex] == value[valueIndex])
            {
                patternIndex++;
                valueIndex++;
            }
            else if (starPattern >= 0)
            {
                patternIndex = starPattern + 1;
                valueIndex = ++starValue;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    /// <summary>
    /// Reports whether raw bytes match any pattern in an alternation, matching
    /// the shell's <c>a|b</c> arm semantics.
    /// </summary>
    /// <param name="value">The raw path bytes.</param>
    /// <param name="patterns">The alternation members.</param>
    /// <returns>True when any member matches.</returns>
    internal static bool MatchesAny(
        ReadOnlySpan<byte> value,
        params string[] patterns)
    {
        foreach (string pattern in patterns)
        {
            if (Matches(value, pattern))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Folds ASCII upper-case bytes to lower case and leaves every other byte
    /// alone, reproducing <c>tr '[:upper:]' '[:lower:]'</c> in the C locale.
    /// </summary>
    /// <param name="value">The raw path bytes.</param>
    /// <returns>The folded copy.</returns>
    internal static byte[] AsciiFold(ReadOnlySpan<byte> value)
    {
        byte[] folded = value.ToArray();
        for (int index = 0; index < folded.Length; index++)
        {
            if (folded[index] is >= (byte)'A' and <= (byte)'Z')
            {
                folded[index] += (byte)('a' - 'A');
            }
        }

        return folded;
    }
}
