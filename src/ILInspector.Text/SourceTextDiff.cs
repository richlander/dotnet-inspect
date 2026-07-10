using System.Collections.Immutable;

using ILInspector.Findings;

namespace ILInspector.Text;

/// <summary>Projects arbitrary text onto the ordered finding spine.</summary>
public static class SourceTextDiff
{
    /// <summary>The finding descriptor for one logical text line.</summary>
    public static readonly FindingDescriptor LineDescriptor = new("text.line", "Text line");

    /// <summary>
    /// Lazily yields an exact line census. Each string payload is the line content and
    /// <see cref="Finding{T}.Position"/> is its zero-based position in the logical line stream.
    /// CRLF, CR, and LF are equivalent boundaries.
    /// Empty text has zero lines. A terminating boundary produces a final empty line, preserving
    /// the distinction between a document with and without a final newline.
    /// </summary>
    public static IEnumerable<Finding<string>> Inspect(string text, FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(subject);

        return ProjectAtoms(SplitLines(text), subject);
    }

    /// <summary>Compares two non-null text documents with exact, ordered line identity.</summary>
    public static SourceTextDiffResult Compare(
        string oldText,
        string newText,
        FindingSubject subject,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);
        ArgumentNullException.ThrowIfNull(subject);

        var oldAtoms = Inspect(oldText, subject).ToImmutableArray();
        var newAtoms = Inspect(newText, subject).ToImmutableArray();
        var match = FindingMatcher.Match(oldAtoms.Keys(), newAtoms.Keys());
        var pairs = FindingFold.ToPairs(match, oldAtoms, newAtoms, acceptanceThreshold);

        return new SourceTextDiffResult(pairs, match, oldAtoms, newAtoms);
    }

    static IEnumerable<string> SplitLines(string text)
    {
        if (text.Length == 0)
            yield break;

        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (character is not ('\r' or '\n'))
                continue;

            yield return text[start..i];

            if (character == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;

            start = i + 1;
        }

        yield return text[start..];
    }

    static IEnumerable<Finding<string>> ProjectAtoms(
        IEnumerable<string> lines,
        FindingSubject subject)
    {
        int position = 0;
        foreach (string content in lines)
        {
            yield return new Finding<string>(
                subject,
                LineDescriptor,
                new FindingKey(content),
                position++,
                content);
        }
    }
}

/// <summary>The successful outcome of an in-memory text comparison.</summary>
public sealed record SourceTextDiffResult(
    ImmutableArray<PairFinding<string>> Pairs,
    FindingMatch Match,
    ImmutableArray<Finding<string>> OldAtoms,
    ImmutableArray<Finding<string>> NewAtoms)
{
    /// <summary>True when the documents have the same logical lines in the same order.</summary>
    public bool IsExact => FindingEquivalence.Exact.IsEquivalent(Pairs);
}
