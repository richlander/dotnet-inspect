namespace tsbindgen;

/// <summary>
/// Structural drift check for generated declarations: generated and checked-in files match when
/// their trimmed, non-blank lines are exactly equal in order. This is the contract exercised by
/// the inspect-web promotion gate.
/// </summary>
static class DriftDetector
{
    public static bool IsCovered(string generated, string handWritten)
    {
        string[] generatedLines = Normalize(generated);
        string[] handWrittenLines = Normalize(handWritten);
        return generatedLines.SequenceEqual(handWrittenLines, StringComparer.Ordinal);
    }

    static string[] Normalize(string text) =>
        text.Split('\n')
            .Select(static l => l.Trim())
            .Where(static l => l.Length > 0)
            .ToArray();
}
