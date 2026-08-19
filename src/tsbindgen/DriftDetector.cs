namespace tsbindgen;

/// <summary>
/// A minimal drift check for the pilot: every non-blank generated line must appear verbatim
/// (trimmed) somewhere in the hand-written file. This is intentionally crude — a real
/// implementation would parse both sides structurally — but is enough to prove the
/// generate-and-diff CI gate shape end to end.
/// </summary>
static class DriftDetector
{
    public static bool IsCovered(string generated, string handWritten)
    {
        var handWrittenLines = new HashSet<string>(
            handWritten.Split('\n').Select(l => l.Trim()),
            StringComparer.Ordinal);

        foreach (string line in generated.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (!handWrittenLines.Contains(trimmed))
            {
                return false;
            }
        }

        return true;
    }
}
