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
/// Some diagnostics are deliberately multi-line -- an unknown field lists the
/// sortable ones underneath -- so structure has to survive. It arrives as
/// <c>details</c>, never as a line break inside <c>message</c>: the message is
/// always folded to a single line, so a terminator injected from untrusted text
/// produces no line at all, and every detail line is indented. An unindented,
/// non-empty line is therefore something only this writer can emit, and the
/// writer never derives more than one from caller-composed text.
///
/// Honoring line breaks inside <c>message</c> was tried first and is not
/// enough. It kept the injected line indented, but the injected text still
/// became a real line -- <c>depends "BAD\nError: FORGED"</c> printed a second
/// line reading <c>Error: FORGED' not found in the specified scope.</c> -- and
/// the writer cannot tell the composer's newline from the attacker's.
/// </remarks>
internal static class CommandError
{
    /// <summary>
    /// Writes <c>Error: &lt;message&gt;</c> to stderr with the message
    /// contained.
    /// </summary>
    public static void Write(Exception ex) => Write(ex.Message);

    /// <inheritdoc cref="Write(Exception)"/>
    public static void Write(string message, params string[] details)
        => WriteDiagnostic("Error", message, details);

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
    public static void WriteWarning(string message, params string[] details)
        => WriteDiagnostic("Warning", message, details);

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
    public static void WriteNote(string message, params string[] details)
        => WriteDiagnostic("Note", message, details);

    /// <summary>
    /// Writes <c>&lt;severity&gt;: &lt;message&gt;</c> with the message
    /// contained. The severity is chosen from a closed set by the callers
    /// above, never composed from caller text.
    /// </summary>
    private static void WriteDiagnostic(string severity, string message, string[] details)
    {
        Console.Error.WriteLine($"{severity}: {CSharpIdentifier.ContainRenderedText(message)}");

        foreach (var detail in details)
        {
            var contained = CSharpIdentifier.ContainRenderedText(detail);
            Console.Error.WriteLine(contained.Length == 0 ? string.Empty : $"  {contained}");
        }
    }
}
