using ILInspector.CSharp;

namespace DotnetInspector.Output;

/// <summary>
/// The single writer for the CLI's <c>Error:</c> line on stderr.
/// </summary>
/// <remarks>
/// An error message routinely quotes the thing that failed -- a package id, a
/// type name, a file path -- so it carries untrusted text even though trusted
/// code composed it. Message construction stays free of presentation concerns,
/// which leaves the write site as the owner.
///
/// A hundred and eight call sites had spelled this inline, which is the shape
/// that keeps reopening: a rule restated at a hundred call sites disagrees with
/// itself the moment the next one is added. Routing them through here makes
/// containment a property of the write rather than of remembering (issue
/// #3319).
///
/// Some messages are deliberately multi-line -- an unknown field lists the
/// sortable ones underneath -- so folding them onto one line would be a
/// readability regression. The writer instead keeps the composer's line breaks
/// and indents every continuation line, which preserves the block while making
/// an unindented, non-empty line something only this writer can produce. A
/// terminator injected from untrusted text therefore lands inside the
/// indented block and cannot forge a second diagnostic.
/// </remarks>
internal static class CommandError
{
    /// <summary>
    /// Writes <c>Error: &lt;message&gt;</c> to stderr with the message
    /// contained.
    /// </summary>
    public static void Write(Exception ex) => Write(ex.Message);

    /// <inheritdoc cref="Write(Exception)"/>
    public static void Write(string message) => WriteDiagnostic("Error", message);

    /// <summary>
    /// Writes <c>Warning: &lt;message&gt;</c> to stderr with the message
    /// contained.
    /// </summary>
    /// <remarks>
    /// Severity belongs here rather than at the call site because a caller that
    /// picks a severity tends to compose the whole prefix to do it. One such
    /// site wrote <c>$"{prefix}: Select value '{value}' not found."</c>, which
    /// never spells the literal <c>Error:</c> and so escaped both this writer
    /// and the source gate that pins it, while still emitting a line the reader
    /// cannot distinguish from a real diagnostic.
    /// </remarks>
    public static void WriteWarning(string message) => WriteDiagnostic("Warning", message);

    /// <summary>
    /// Writes <c>Note: &lt;message&gt;</c> to stderr with the message
    /// contained.
    /// </summary>
    /// <remarks>
    /// A note is the easiest severity to overlook, and the most useful one to
    /// forge: it quotes the token the user asked about, so it carries
    /// untrusted text by construction, and a reader skims it. Every note in
    /// the product goes through here for that reason.
    /// </remarks>
    public static void WriteNote(string message) => WriteDiagnostic("Note", message);

    /// <summary>
    /// Writes <c>&lt;severity&gt;: &lt;message&gt;</c> with the message
    /// contained. The severity is chosen from a closed set by the callers
    /// above, never composed from caller text.
    /// </summary>
    private static void WriteDiagnostic(string severity, string message)
    {
        var lines = message.Split(LineBreaks, StringSplitOptions.None);
        Console.Error.WriteLine($"{severity}: {CSharpIdentifier.ContainRenderedText(lines[0])}");

        for (var i = 1; i < lines.Length; i++)
        {
            var contained = CSharpIdentifier.ContainRenderedText(lines[i]);
            Console.Error.WriteLine(contained.Length == 0 ? string.Empty : $"  {contained}");
        }
    }

    private static readonly string[] LineBreaks = ["\r\n", "\n", "\r"];
}
