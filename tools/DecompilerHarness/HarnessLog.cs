namespace ILInspector.DecompilerHarness;

/// <summary>
/// Output discipline for the harness: stdout carries a sensor's data (reports,
/// cards, and <c>--json</c>/<c>--jsonl</c>/<c>--tsv</c> payloads); stderr carries
/// status, progress, and gate diagnostics. Routing status messages through
/// <see cref="Status(string)"/> keeps structured stdout parseable — for example a
/// <c>--jsonl</c> stream stays valid and a teed quality card stays free of stray
/// "Wrote …" lines.
/// </summary>
static class HarnessLog
{
    /// <summary>Writes a status, progress, or gate diagnostic to stderr.</summary>
    public static void Status(string message) => Console.Error.WriteLine(message);
}
