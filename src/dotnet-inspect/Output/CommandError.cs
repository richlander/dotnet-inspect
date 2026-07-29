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
/// Every message is single-line, so folding line endings costs nothing and a
/// terminator injected from untrusted text can no longer forge a second
/// diagnostic.
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
    /// Writes <c>&lt;severity&gt;: &lt;message&gt;</c> with the message
    /// contained. The severity is chosen from a closed set by the callers
    /// above, never composed from caller text.
    /// </summary>
    private static void WriteDiagnostic(string severity, string message)
        => Console.Error.WriteLine($"{severity}: {CSharpIdentifier.ContainRenderedText(message)}");
}
