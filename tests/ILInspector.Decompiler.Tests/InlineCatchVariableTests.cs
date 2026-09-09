using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A once-used catch variable is left on the stack by Release codegen, so the
// caught exception imports as a bare CaughtException in value position with no
// `E e = …` store to fold. EhStructuringPass would previously leave the whole
// method flat (unconsumed-regions); it now synthesizes the catch variable back
// so the try/catch structures and recompiles opcode-exact.
public class InlineCatchVariableTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    [Fact]
    public void InlineCaughtException_SynthesizesCatchVariableAndStructures()
    {
        var function = Raised(nameof(CfgSampleClass.CatchLogs));

        // The region is consumed: a real try/catch, no surviving goto.
        var tryCatch = Assert.Single(function.Descendants.OfType<TryCatch>());
        var clause = Assert.Single(tryCatch.Clauses);
        Assert.NotNull(clause.VariableIndex);                 // a variable was bound
        Assert.Empty(function.Descendants.OfType<Leave>());
        Assert.Empty(function.Descendants.OfType<Branch>());
        // No bare CaughtException leaks — every use reads the bound variable.
        Assert.Empty(clause.Body.Descendants.OfType<CaughtException>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("catch (FormatException", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("__exception", output);
    }

    [Fact]
    public void DiscardedCatch_BindsNoVariable()
    {
        var function = Raised(nameof(CfgSampleClass.CatchDiscards));

        var tryCatch = Assert.Single(function.Descendants.OfType<TryCatch>());
        var clause = Assert.Single(tryCatch.Clauses);
        Assert.Null(clause.VariableIndex);                    // exception discarded — no synthetic var
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("catch (FormatException)", output);
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void BareRethrow_BindsNoVariable()
    {
        var function = Raised(nameof(CfgSampleClass.LogAndRethrow));

        var tryCatch = Assert.Single(function.Descendants.OfType<TryCatch>());
        var clause = Assert.Single(tryCatch.Clauses);
        Assert.Null(clause.VariableIndex);                    // a `throw;` rethrow names nothing
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("throw;", output);
        Assert.DoesNotContain("goto", output);
    }
}
