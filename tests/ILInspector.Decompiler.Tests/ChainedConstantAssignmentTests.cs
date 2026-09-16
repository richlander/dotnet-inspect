using System;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// #2982 / #2994: a value assigned through a chain (`a = b = c = v`) is dup'd to
// each sink in IL (`ldc.i4.0; dup; call set_C; dup; call set_B; call set_A`).
// ChainedAssignmentPass recomposes that run into a single `A = B = C = v;`,
// keyed on the shared dup slot (real evidence — genuinely separate statements
// carry no dup and never collapse). A dup'd constant that is not part of a chain
// is re-materialized at its sink so a bool literal is recovered (`= false`), not
// spilled into an int stack slot (`int S = 0; a = S;`), which is CS0029.
public class ChainedConstantAssignmentTests
{
    static string Render(string methodName)
    {
        using var source = MetadataSource.Open(typeof(ChainedConstantAssignmentSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(ChainedConstantAssignmentSamples).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }

    [Fact]
    public void ChainedBoolConstant_RecomposesIntoOneChain_WithLiteralAtInnermostSink()
    {
        var output = Render(nameof(ChainedConstantAssignmentSamples.ChainedBoolFalse));

        // The three sinks collapse into one chained assignment, source order
        // preserved, and the bool literal is recovered at the innermost sink.
        Assert.Contains(
            "ChainedConstantAssignmentSamples.A = ChainedConstantAssignmentSamples.B = ChainedConstantAssignmentSamples.C = false;",
            output);

        // The dup'd constant is not spilled into an int stack slot (the #2982
        // defect rendered `int S_256 = 0; ...A = S_256;` — CS0029), and there is
        // exactly one assignment statement.
        Assert.DoesNotContain("S_", output);
        Assert.Equal(1, CountOccurrences(output, "= false;"));
    }

    [Fact]
    public void ChainedWiden_RecomposesWithImplicitWidening()
    {
        var output = Render(nameof(ChainedConstantAssignmentSamples.ChainedWiden));

        // The shared int constant lands at `I` (int) and widens implicitly into
        // `L` (long); the widening convert is dropped in the chained spelling.
        Assert.Contains(
            "ChainedConstantAssignmentSamples.L = ChainedConstantAssignmentSamples.I = -1;",
            output);
        Assert.DoesNotContain("S_", output);
    }

    [Fact]
    public void ChainedNonConstant_RecomposesSharedCallResult()
    {
        var output = Render(nameof(ChainedConstantAssignmentSamples.ChainedNonConstant));

        Assert.Contains(
            "ChainedConstantAssignmentSamples.P = ChainedConstantAssignmentSamples.Q = ChainedConstantAssignmentSamples.R = Compute();",
            output);
        Assert.DoesNotContain("S_", output);
    }

    [Fact]
    public void ChainedStaticFields_Recompose()
    {
        var output = Render(nameof(ChainedConstantAssignmentSamples.ChainedStaticFields));

        Assert.Contains(
            "ChainedConstantAssignmentSamples.F = ChainedConstantAssignmentSamples.G = ChainedConstantAssignmentSamples.H = true;",
            output);
        Assert.DoesNotContain("S_", output);
    }

    [Fact]
    public void SeparateStatements_DoNotCollapse()
    {
        var output = Render(nameof(ChainedConstantAssignmentSamples.SeparateStatements));

        // No dup slot backs these, so they stay two independent statements and
        // never form a chain.
        Assert.Contains("ChainedConstantAssignmentSamples.A = false;", output);
        Assert.Contains("ChainedConstantAssignmentSamples.B = false;", output);
        Assert.DoesNotContain(" = ChainedConstantAssignmentSamples.B = ", output);
        Assert.Equal(2, CountOccurrences(output, "= false;"));
    }

    [Fact]
    public void SideEffectValue_IsNotAChain_ConstantRematerialized()
    {
        var output = Render(nameof(ChainedConstantAssignmentSamples.SideEffectValue));

        // One sink whose value escapes into an argument: not a chain. The dup'd
        // constant is re-materialized, so both the assignment and the argument
        // carry the literal, with no int spill slot.
        Assert.Contains("ChainedConstantAssignmentSamples.P = 5;", output);
        Assert.Contains("Console.WriteLine(5);", output);
        Assert.DoesNotContain("S_", output);
    }

    static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
