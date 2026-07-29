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
    public static void Write(string message)
        => Console.Error.WriteLine($"Error: {CSharpIdentifier.ContainRenderedText(message)}");
}
