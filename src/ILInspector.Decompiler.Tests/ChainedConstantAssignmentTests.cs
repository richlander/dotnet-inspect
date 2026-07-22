using System;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// #2982: a constant assigned through a chain (`a = b = c = false`) is dup'd to each
// sink in IL (`ldc.i4.0; dup; call set_C; dup; call set_B; call set_A`). The importer
// must re-materialize the dup'd constant at each bool sink so per-sink constant
// recovery fires and it renders `= false` — not spill it into an int stack slot
// (`int S = 0; a = S;`), which is CS0029 (cannot implicitly convert int to bool).
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
    public void ChainedBoolConstant_RendersLiteralAtEachSink_NotIntSpill()
    {
        var output = Render(nameof(ChainedConstantAssignmentSamples.ChainedBoolFalse));

        // Each of the three chained sinks receives the bool literal directly.
        Assert.Equal(3, CountOccurrences(output, "= false;"));

        // The dup'd constant is not spilled into an int stack slot: the #2982 defect
        // rendered `int S_256 = 0; ...A = S_256;` — CS0029 at every bool sink.
        Assert.DoesNotContain("int S", output);
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
