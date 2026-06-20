using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A `ref bool`/`bool*` deref loads via `ldind.u1`, so the branch condition's IR
// ResultType is `byte`. The printer's truthiness speller must still treat it as
// a boolean (it renders as the bool place), spelling `!flag` rather than the
// CS0019 integer comparison `flag == 0`.
public class RefBoolBranchTests
{
    static string Render(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }

    [Fact]
    public void BranchOverRefBoolDeref_NegatesRatherThanComparesToZero()
    {
        var output = Render(nameof(CfgSampleClass.RefBoolGuard));

        Assert.DoesNotContain("== 0", output);
        Assert.DoesNotContain("!= 0", output);
        Assert.Contains("!", output);
    }
}
