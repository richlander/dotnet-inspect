namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// One outer variable a recovered nested function captured, named by the exact
/// substituted loads that read it inside the recovered body.
/// </summary>
/// <remarks>
/// <para>
/// This is producer evidence, not a re-derivable fact. <see cref="LambdaRaisingPass"/>
/// and <see cref="LocalFunctionRaisingPass"/> are the only places where the
/// display-class field a hoisted variable was lowered into is still known: each
/// resolves <c>&lt;&gt;c__DisplayClass</c> field reads to the outer
/// <see cref="LoadArgument"/>/<see cref="LoadLocal"/> that was captured, clones
/// that load into the nested body, and then erases the environment. After that
/// erasure nothing downstream can tell a substituted capture read from an
/// ordinary outer-variable read, and no consumer — least of all one holding only
/// rendered text — can recover the association. Recording the exact clone
/// instances here keeps the evidence alive until
/// <see cref="PrintedBodyMap"/> can bind it to printed node ids.
/// </para>
/// <para>
/// Only the use instances are recorded, deliberately. The captured variable's
/// C# spelling is a print-time decision — an argument prints through
/// <c>CSharpNaming.ContainedIdentifier</c> and a local through the printer's
/// deduplicated local-name table — so a name minted here could disagree with the
/// characters the reader sees. <see cref="PrintedBodyMap"/> reads the display
/// name off the exact printed extent of these uses instead, and declines the row
/// when they do not all print one identical name.
/// </para>
/// <para>
/// A raise that does not substitute anything records nothing: a non-capturing
/// lambda or a <c>static</c> local function carries an empty capture list, which
/// is the positive statement that the producer found no captured variable, not a
/// gap in the evidence.
/// </para>
/// </remarks>
public sealed class IrCapturedVariable
{
    /// <summary>Records one captured variable's substituted uses.</summary>
    /// <param name="uses">The exact cloned outer-variable loads substituted into the recovered body. At least one.</param>
    public IrCapturedVariable(IEnumerable<IrExpression> uses)
    {
        ArgumentNullException.ThrowIfNull(uses);

        Uses = [.. uses];
        if (Uses.Count == 0)
        {
            throw new ArgumentException(
                "A captured variable is evidenced by its substituted uses, so it must have at least one.",
                nameof(uses));
        }
        if (Uses.Any(use => use is null))
            throw new ArgumentException("Capture uses cannot contain null.", nameof(uses));
    }

    /// <summary>
    /// The exact cloned outer-variable loads substituted into the recovered
    /// body, in body order. Reference identity is the whole point: these are the
    /// instances the printer will position, not a description of them.
    /// </summary>
    public IReadOnlyList<IrExpression> Uses { get; }
}
