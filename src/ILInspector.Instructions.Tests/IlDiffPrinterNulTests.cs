namespace ILInspector.Instructions.Tests;

/// <summary>
/// Controls for the display projection's handling of NUL.
/// </summary>
public class IlDiffPrinterNulTests
{
    /// <summary>
    /// A folded operand carries the ordinal placeholder, which ends in NUL so that no
    /// metadata name can spell it. That NUL exists to be unspellable, not to be read, and a
    /// report containing one stops being text — <c>file</c> reports it as binary data and
    /// terminal and Markdown consumers truncate or mangle it. The display projection must
    /// therefore drop it.
    /// </summary>
    /// <remarks>
    /// The NUL is dropped rather than escaped because the visible <c>#</c> is already the
    /// whole signal, and an escape would read as part of the name. This runs after
    /// comparison, so it cannot change whether two bodies match — that decision is made on
    /// <see cref="IlBodyDiffResult"/>, which these rows are projected from.
    /// </remarks>
    [Fact]
    public void DisplayRows_DoNotCarryNul()
    {
        string folded = $"void C::<M>g__L|{CompilerGeneratedOrdinalCorrespondence.OrdinalPlaceholder}_0()";
        var row = new IlDiffRow(
            HunkId: 1,
            Kind: IlDiffKind.Remove,
            Operation: new CanonicalIlOperation(
                Offset: 0,
                OpcodeFamily: "call",
                Operand: new IlOperandIdentity(IlOperandIdentityKind.Token, folded)),
            Message: $"changed to {folded}");

        // The fixture is only meaningful while the placeholder really does carry a NUL.
        Assert.Contains('\0', folded);

        var display = IlDiffPrinter.ToDisplayRow(row);

        Assert.DoesNotContain('\0', display.OperandValue!);
        Assert.DoesNotContain('\0', display.Operation);
        Assert.DoesNotContain('\0', display.Message);
        Assert.DoesNotContain('\0', display.UnifiedLine);
        Assert.Contains("#_0", display.Operation, StringComparison.Ordinal);
    }

    /// <summary>
    /// An <c>ldstr</c> operand comes from the <c>#US</c> heap, which is length-prefixed
    /// rather than NUL-terminated, so a literal really can contain NUL independently of the
    /// placeholder. Rendering must not put that byte in a report either.
    /// </summary>
    [Fact]
    public void DisplayRows_DropNulFromUserStrings()
    {
        var row = new IlDiffRow(
            HunkId: 1,
            Kind: IlDiffKind.Remove,
            Operation: new CanonicalIlOperation(
                Offset: 0,
                OpcodeFamily: "ldstr",
                Operand: new IlOperandIdentity(IlOperandIdentityKind.Token, "string \"a\0b\"")),
            Message: "");

        var display = IlDiffPrinter.ToDisplayRow(row);

        Assert.DoesNotContain('\0', display.UnifiedLine);
        Assert.Contains("ab", display.Operation, StringComparison.Ordinal);
    }
}
