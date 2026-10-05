using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Prints a hand-built tree after issuing the decisions the emission tail
/// makes before printing. The pipeline ends with
/// <see cref="DefiniteAssignmentPass"/>, and the printer refuses to declare an
/// up-front local from a body that never ran it (value-typed-emission.md,
/// Instance 3); a test that builds IR directly and prints it runs that pass
/// here, as the residual-binding tests run <see cref="ResidualSlotBindingPass"/>.
/// </summary>
static class DecidedPrint
{
    internal static DecompilerResult Print(IrFunction function, PrinterOptions? options = null)
    {
        DefiniteAssignmentPass.Issue(function);
        return CSharpPrinter.Print(function, options);
    }

    internal static DecompilerResult Print(IrFunction function, out PrintedRangeMap printedRanges, PrinterOptions? options = null)
    {
        DefiniteAssignmentPass.Issue(function);
        return CSharpPrinter.Print(function, out printedRanges, options);
    }
}
