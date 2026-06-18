using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class LoweredViewTests
{
    static IrFunction ImportFixture(string methodName)
    {
        var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        return function!;
    }

    [Fact]
    public void Lowered_OmitsCosmeticSugarButKeepsLoadBearingPasses()
    {
        var lowered = IrPasses.Lowered.Select(p => p.GetType().Name).ToList();

        // The cosmetic statement-sugar passes are dropped.
        Assert.DoesNotContain("ForLoopPass", lowered);
        Assert.DoesNotContain("IncrementDecrementPass", lowered);
        Assert.DoesNotContain("LockSugarPass", lowered);

        // The passes whose removal would emit unspellable C# (a direct get_X()
        // accessor call, a bare ldftn) are kept, so the output stays valid.
        Assert.Contains("PropertySugarPass", lowered);
        Assert.Contains("DelegateConstructionPass", lowered);
        Assert.Contains("StructuringPass", lowered);
    }

    [Fact]
    public void PrintLowered_DesugarsLockToExplicitMonitor()
    {
        // The shipped output raises the Monitor lowering to `lock (gate)`; the
        // lowered view stops short of that pass, leaving the explicit Monitor
        // try/finally — still valid C#.
        var sugared = CSharpPrinter.PrintRaised(ImportFixture(nameof(CfgSampleClass.LockedAssign))).Output;
        var lowered = CSharpPrinter.PrintLowered(ImportFixture(nameof(CfgSampleClass.LockedAssign))).Output;

        Assert.NotNull(sugared);
        Assert.NotNull(lowered);
        Assert.Contains("lock (", sugared);
        Assert.DoesNotContain("lock (", lowered);
        Assert.Contains("Monitor", lowered);
    }

    [Fact]
    public void PrintLowered_ProducesValidRecompilableOutput()
    {
        // Lowering can legitimately reduce fidelity below the shipped output: a
        // sugar pass that resolves a slot (e.g. lock absorbing the Monitor.Enter
        // ref-kind) no longer runs. What must hold is that the lowered view still
        // succeeds with spellable C# — SharpLab-style valid lowered code.
        var result = CSharpPrinter.PrintLowered(ImportFixture(nameof(CfgSampleClass.LockedAssign)));

        Assert.True(result.Succeeded);
        Assert.NotEqual(DecompilationFidelity.Failed, result.Fidelity);
        Assert.DoesNotContain("get_", result.Output);  // no illegal accessor calls (CS0571)
    }
}
