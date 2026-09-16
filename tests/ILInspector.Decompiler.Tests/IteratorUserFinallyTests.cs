using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// Regression coverage for the #1429 class of over-raise: a user try/finally inside an
// iterator lowers to the same <>m__Finally + EndFinally + EH-region scaffolding that the
// iterator reconstruction passes strip. ForeachIteratorReconstruction was found to drop
// it (#1429); these prove the non-foreach reconstruction paths (linear, multi-yield,
// counting) do NOT silently drop the user finally — they either preserve the cleanup or
// decline to honest acknowledgment (a non-Full fidelity), never reporting Full while the
// observable finally is gone.
public class IteratorUserFinallyTests
{
    static (string Output, DecompilationFidelity Fidelity) Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        var context = new PassContext(new Stepper(enabled: false),
            importMethodBody: method => IrImporter.Import(source, method));
        IrPasses.Run(function!, IrPasses.Default, context);
        function!.CheckInvariant();
        return (CSharpPrinter.Print(function).Output!, function.Fidelity);
    }

    [Theory]
    [InlineData(nameof(CfgSampleClass.LinearYieldUserFinally))]
    [InlineData(nameof(CfgSampleClass.MultiYieldUserFinally))]
    [InlineData(nameof(CfgSampleClass.CountingLoopUserFinally))]
    public void UserFinallyNeverSilentlyDropped(string method)
    {
        var (output, fidelity) = Raised(method);

        // The user finally either survives in the rendered C#, or the method honestly
        // degrades below Full. What must never happen: Full fidelity with the finally
        // (the Console.Write("f") cleanup) erased.
        bool finallyPreserved = output.Contains("finally") || output.Contains("\"f\"");
        Assert.True(
            finallyPreserved || fidelity != DecompilationFidelity.Full,
            $"User finally silently dropped at Full fidelity:\n==== {method} ====\n{output}\n====");
    }
}
