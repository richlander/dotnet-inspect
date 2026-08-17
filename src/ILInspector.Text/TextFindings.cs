using System.Collections.Immutable;

using ILInspector.Findings;

namespace ILInspector.Text;

/// <summary>An exact text-line census exceeded its caller-selected limit.</summary>
public sealed class TextFindingComplexityException(int limit)
    : InvalidOperationException(
        $"Text exceeds the finding complexity limit of {limit:N0} lines.")
{
    public int Limit { get; } = limit;
}

/// <summary>Projects arbitrary text onto the ordered finding spine.</summary>
public static class TextFindings
{
    /// <summary>The finding descriptor for one logical text line.</summary>
    public static readonly FindingDescriptor LineDescriptor = new("text.line", "Text line");

    /// <summary>
    /// Lazily yields an exact line census. Each string payload is the line content and
    /// <see cref="Finding{T}.Ordinal"/> is its zero-based position in the logical line stream.
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

    /// <summary>
    /// Lazily yields an exact line census after refusing text that exceeds
    /// <paramref name="maxLineCount"/>.
    /// </summary>
    public static IEnumerable<Finding<string>> Inspect(
        string text,
        FindingSubject subject,
        int maxLineCount)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLineCount);

        if (CountLines(text) > maxLineCount)
            throw new TextFindingComplexityException(maxLineCount);

        return ProjectAtoms(SplitLines(text), subject);
    }

    /// <summary>Compares two non-null text documents with exact, ordered line identity.</summary>
    public static FindingComparison<string> Compare(
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
        FindingInspection<string> oldInspection =
            new FindingInspection<string>.Complete(oldAtoms);
        FindingInspection<string> newInspection =
            new FindingInspection<string>.Complete(newAtoms);
        return FindingComparison.Compare(
            oldInspection,
            newInspection,
            acceptanceThreshold: acceptanceThreshold);
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

    static int CountLines(string text)
    {
        if (text.Length == 0)
            return 0;

        int count = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n'))
                continue;

            count++;
            if (text[i] == '\r'
                && i + 1 < text.Length
                && text[i + 1] == '\n')
            {
                i++;
            }
        }

        return count;
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
                content,
                Ordinal: position++);
        }
    }
}
