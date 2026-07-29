using ILInspector.CSharp;

namespace DotnetInspector.Output;

/// <summary>
/// The single writer of stderr for the CLI.
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
///
/// Owning the severity line alone was not enough either. stderr also carries
/// suggestion lists, TFM lists, and progress text, and those went out raw from
/// thirty-four other sites; one of them printed a hostile package's
/// <c>targetFramework</c> attribute unindented, which is a forged diagnostic
/// with no severity literal anywhere in the source. <see cref="WriteLine"/> and
/// <see cref="WriteDetail"/> exist so that every line on this stream comes from
/// here, which is what makes the gate a statement about the stream rather than
/// about a spelling.
///
/// The claims above are enforced, not asserted. Each is named with the gate
/// that fails when it stops holding:
///
/// <list type="bullet">
/// <item><description>
/// "the single writer" -- <c>CommandErrorOwnershipTests.CommandError_IsTheOnlyWriterOfStderr</c>
/// scans the CLI's transitive ProjectReference closure for any call on
/// <c>Console.Error</c>, for <c>OpenStandardError</c>/<c>SetError</c>, and for
/// any <c>using static</c>/alias import of <c>System.Console</c> that would
/// make <c>Error</c> nameable without that receiver.
/// </description></item>
/// <item><description>
/// "an unindented, non-empty line is something only this writer can emit" --
/// <c>UntrustedArgumentDiagnosticContainmentTests.Diagnostic_KeepsItsDetailBlockAndNeverGrowsALineFromItsMessage</c>
/// and <c>HostileArgument_IsContainedInDiagnostics</c>, which run the built CLI
/// out of process over eleven argv channels and five hazards and assert the
/// unindented-line count directly.
/// </description></item>
/// <item><description>
/// "the runtime's own printer cannot bypass this" --
/// <c>UntrustedArgumentDiagnosticContainmentTests.EscapingException_IsContainedRatherThanPrintedByTheRuntime</c>,
/// which forces a real <c>DirectoryNotFoundException</c> carrying a hazard from
/// argv through <see cref="WriteUnhandled"/>.
/// </description></item>
/// <item><description>
/// "the four sinks are the only ones" --
/// <c>CommandErrorOwnershipTests.StderrSinks_AreStillTheOnesAccountedFor</c>,
/// which asserts the set of sites rather than their number, so a fifth cannot
/// arrive by replacing one of the four.
/// </description></item>
/// </list>
///
/// Two properties here are <b>not</b> gated, and are called out rather than
/// implied.
///
/// Sibling entry points that are not in this CLI's project closure --
/// <c>mdi</c> above all -- read the same untrusted metadata, write their own
/// stderr, and cannot reach this writer. They are out of scope by construction,
/// tracked as issue #3444.
///
/// And the ownership scan reads source text, so it sees the names a program
/// spells, not the members it reaches. Comments, identifier escapes, and
/// verbatim <c>@</c> are normalized away because those are still spellings of
/// the name; reflection is not.
/// <c>((TextWriter)typeof(Console).GetProperty("Error")!.GetValue(null)!).WriteLine(untrusted)</c>
/// names neither, and no amount of pattern work will make a text scan see it --
/// which is the same argument this class makes about severity prefixes, applied
/// to itself.
///
/// What covers it is the other kind of gate. The out-of-process tests in
/// <c>UntrustedArgumentDiagnosticContainmentTests</c> read the bytes actually
/// on the stream, so they do not care how a write was spelled: introducing that
/// exact reflective write inside <see cref="Write(string, string[])"/> leaves
/// all five ownership tests green and fails those tests across every channel and
/// hazard. The residual is therefore narrower than "reflection defeats this" --
/// it is a reflective write on a path no hostile test exercises -- and it is a
/// reach limitation of the behavioral suite rather than a hole in the rule.
/// Note also that neither gate defends against an author who intends the leak,
/// since the same commit can delete the test; both exist to catch the
/// regression, not the adversary with commit rights.
/// </remarks>
internal static class CommandError
{
    /// <summary>
    /// Writes <c>Error: &lt;message&gt;</c> to stderr with the message
    /// contained.
    /// </summary>
    public static void Write(Exception ex) => Write(ex.Message);

    /// <summary>
    /// Writes a validation failure that carries its own detail lines.
    /// </summary>
    /// <remarks>
    /// The overload exists so a validator can hand over structure without
    /// encoding it as newlines in <see cref="OptionError.Message"/>, which this
    /// writer folds by design.
    /// </remarks>
    public static void Write(DotnetInspector.Options.OptionError error) =>
        Write(error.Message, error.Details);

    /// <inheritdoc cref="Write(Exception)"/>
    public static void Write(string message, params string[] details)
        => WriteDiagnostic("Error", message, details);

    /// <summary>
    /// Writes an exception that escaped every handler, as one contained
    /// <c>Error:</c> line followed by its full detail indented.
    /// </summary>
    /// <remarks>
    /// Without this, the .NET runtime prints the escaping exception itself, at
    /// column 0, with the message interpolated raw -- the one writer of this
    /// stream that cannot be routed through here, because it is not product
    /// code. An exception message routinely quotes attacker-reachable text (an
    /// <c>--out</c> path, a zip entry name, a nuspec fragment), so that printer
    /// emitted forged unindented diagnostics for free.
    ///
    /// Nothing is dropped: the whole <see cref="Exception.ToString"/>, stack
    /// frames and inner exceptions included, still reaches the reader. It
    /// arrives as indented detail because an unindented line is the thing that
    /// can be mistaken for a diagnostic, not the text itself.
    ///
    /// Gated end to end by
    /// <c>UntrustedArgumentDiagnosticContainmentTests.EscapingException_IsContainedRatherThanPrintedByTheRuntime</c>,
    /// which reaches this method through a real escaping exception -- an
    /// <c>--out</c> path under a missing directory, which is an ordinary user
    /// mistake -- rather than by calling it. That test also asserts the absence
    /// of the runtime banner, so it fails if the default handler is ever
    /// re-enabled in <c>CommandLineBuilder.InvokeAsync</c>.
    /// </remarks>
    public static void WriteUnhandled(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        Write(ex.Message, [.. ex.ToString().ReplaceLineEndings("\n").Split('\n')]);
    }

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
    /// Writes a contained line with no severity prefix, for stderr text that
    /// is not a diagnostic -- a hint, a progress note, a discovery listing.
    /// </summary>
    /// <remarks>
    /// Such a line is still attacker-reachable and still folded to one line,
    /// because the forgery that matters is a new unindented line, not the word
    /// in front of it.
    /// </remarks>
    public static void WriteLine(string text)
        => Console.Error.WriteLine(CSharpIdentifier.ContainRenderedText(text));

    /// <summary>
    /// Writes a contained, indented continuation line, for the items of a
    /// suggestion or availability list.
    /// </summary>
    public static void WriteDetail(string text)
    {
        var contained = CSharpIdentifier.ContainRenderedText(text);
        Console.Error.WriteLine(contained.Length == 0 ? string.Empty : $"  {contained}");
    }

    /// <summary>
    /// Writes an empty line to stderr, the one line that carries no text and
    /// so needs no containment.
    /// </summary>
    public static void WriteBlankLine() => Console.Error.WriteLine();

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
            WriteDetail(detail);
        }
    }
}
