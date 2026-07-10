using System.Collections.Immutable;

using ILInspector.Findings;

namespace ILInspector.Text;

/// <summary>A logical text line whose content excludes its line terminator.</summary>
public sealed record TextLine
{
    public TextLine(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
    }

    /// <summary>The exact line content, including any whitespace.</summary>
    public string Content { get; }

    public override string ToString() => Content;
}

/// <summary>Projects arbitrary text onto the ordered finding spine.</summary>
public static class TextFindings
{
    /// <summary>The finding descriptor for one logical text line.</summary>
    public static readonly FindingDescriptor LineDescriptor = new("text.line", "Text line");

    /// <summary>
    /// Lazily yields an exact line census. CRLF, CR, and LF are equivalent boundaries.
    /// Empty text has zero lines. A terminating boundary produces a final empty line, preserving
    /// the distinction between a document with and without a final newline.
    /// </summary>
    public static IEnumerable<Finding<TextLine>> Inspect(string text, FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(subject);

        return ProjectAtoms(SplitLines(text), subject);
    }

    /// <summary>Compares two non-null text documents with exact, ordered line identity.</summary>
    public static TextFindingsResult Compare(
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

        return new TextFindingsResult(pairs, match, oldAtoms, newAtoms);
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

    static IEnumerable<Finding<TextLine>> ProjectAtoms(
        IEnumerable<string> lines,
        FindingSubject subject)
    {
        int position = 0;
        foreach (string content in lines)
        {
            var line = new TextLine(content);
            yield return new Finding<TextLine>(
                subject,
                LineDescriptor,
                new FindingKey(content),
                position++,
                line);
        }
    }
}

/// <summary>The successful outcome of an in-memory text comparison.</summary>
public sealed record TextFindingsResult(
    ImmutableArray<PairFinding<TextLine>> Pairs,
    FindingMatch Match,
    ImmutableArray<Finding<TextLine>> OldAtoms,
    ImmutableArray<Finding<TextLine>> NewAtoms)
{
    /// <summary>True when the documents have the same logical lines in the same order.</summary>
    public bool IsExact => FindingEquivalence.Exact.IsEquivalent(Pairs);
}
