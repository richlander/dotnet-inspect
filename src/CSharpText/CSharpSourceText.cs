namespace CSharpText;

/// <summary>Splits and slices C# source using the line endings recognized by the language.</summary>
public static class CSharpSourceText
{
    /// <summary>
    /// Splits <paramref name="sourceText"/> on CR, LF, or CRLF without retaining line terminators.
    /// </summary>
    public static string[] SplitLines(string sourceText, int maxLineCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLineCount);

        int lineCount = CountLines(sourceText);
        if (lineCount > maxLineCount)
            throw new CSharpTextComplexityException(maxLineCount, "lines");

        return SliceLines(sourceText, 0, lineCount);
    }

    /// <summary>
    /// Returns the zero-based half-open line range
    /// [<paramref name="fromLine"/>, <paramref name="toLine"/>).
    /// </summary>
    public static string[] SliceLines(string sourceText, int fromLine, int toLine)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromLine);
        ArgumentOutOfRangeException.ThrowIfLessThan(toLine, fromLine);

        var selected = new List<string>(toLine - fromLine);
        int line = 0;
        int lineStart = 0;
        for (int i = 0; i <= sourceText.Length; i++)
        {
            int terminatorLength = i < sourceText.Length
                ? LineTerminatorLength(sourceText, i)
                : 0;
            if (i < sourceText.Length && terminatorLength == 0)
                continue;

            if (line >= fromLine && line < toLine)
                selected.Add(sourceText[lineStart..i]);

            line++;
            if (line >= toLine || i == sourceText.Length)
                break;

            i += terminatorLength - 1;
            lineStart = i + 1;
        }

        return [.. selected];
    }

    private static int CountLines(string sourceText)
    {
        int count = 1;
        for (int i = 0; i < sourceText.Length; i++)
        {
            int terminatorLength = LineTerminatorLength(sourceText, i);
            if (terminatorLength == 0)
                continue;

            count++;
            i += terminatorLength - 1;
        }

        return count;
    }

    private static int LineTerminatorLength(string sourceText, int index) =>
        sourceText[index] switch
        {
            '\r' when index + 1 < sourceText.Length && sourceText[index + 1] == '\n' => 2,
            '\r' or '\n' => 1,
            _ => 0,
        };
}
