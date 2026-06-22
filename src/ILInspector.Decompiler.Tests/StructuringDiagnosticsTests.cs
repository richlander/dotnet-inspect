using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class StructuringDiagnosticsTests
{
    static StructuringDiagnostics RunWithDiagnostics(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        var diagnostics = new StructuringDiagnostics();
        IrPasses.Run(function, IrPasses.Default, new PassContext(new Stepper(enabled: false), diagnostics));
        return diagnostics;
    }

    static StructuringDiagnostics RunWithDiagnostics(IrFunction function)
    {
        var diagnostics = new StructuringDiagnostics();
        IrPasses.Run(function, IrPasses.Default, new PassContext(new Stepper(enabled: false), diagnostics));
        return diagnostics;
    }

    [Fact]
    public void CommonExitMerge_StructuresRegionExitDiamond()
    {
        // The inner if's two arms both branch to the enclosing diamond's tracked
        // join, which lies past the inner region's lexical stop. The structurer
        // can now name the local merge without swallowing the outer sibling block.
        var diag = RunWithDiagnostics(nameof(CfgSampleClass.GotoCommonExitGuardedMerge));

        Assert.True(diag.Structured > 0);
        Assert.Empty(diag.Stops);
    }

    [Fact]
    public void RegionExitDiamond_RequiresBothArmsToExitTrackedJoin()
    {
        // Near miss for the region-exit diamond: the fallthrough arm exits to the
        // enclosing join, but the taken arm branches to the local sibling block.
        // That is not the same structured if/else shape, so the pass keeps the
        // container flat instead of erasing a still-meaningful goto.
        var intType = TypeRef.CoreLib("System", "Int32");
        LoadArgument X() => new(0, "x", intType);

        var container = new BlockContainer();
        var head = new Block(0);
        head.Add(new ConditionalBranch(
            new Comparison(ComparisonKind.LessThanOrEqual, false, X(), new Constant(0, intType)), 16));
        var innerHead = new Block(4);
        innerHead.Add(new ConditionalBranch(
            new Comparison(ComparisonKind.LessThanOrEqual, false, X(), new Constant(100, intType)), 12));
        var falseArm = new Block(8);
        falseArm.Add(new StoreLocal(0, intType, new Constant(2, intType)));
        falseArm.Add(new Branch(20));
        var nearMissArm = new Block(12);
        nearMissArm.Add(new StoreLocal(0, intType, new Constant(1, intType)));
        nearMissArm.Add(new Branch(16));
        var outerSibling = new Block(16);
        outerSibling.Add(new StoreLocal(0, intType, new Constant(0, intType)));
        var join = new Block(20);
        join.Add(new Return(new LoadLocal(0, intType)));
        foreach (var block in (Block[])[head, innerHead, falseArm, nearMissArm, outerSibling, join])
            container.Add(block);

        var signature = new MethodSignature(intType,
            [new Parameter("x", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [intType], container);

        var diag = RunWithDiagnostics(function);

        Assert.Equal(0, diag.Structured);
        Assert.Equal("forward-branch-not-region-exit", Assert.Single(diag.Stops));
    }

    [Fact]
    public void ComparisonTree_PastRegionTerminatingCaseBody_StructuresCleanly()
    {
        var function = BuildPastRegionCaseBody(longCaseBody: false);

        var diag = RunWithDiagnostics(function);
        IrPasses.Run(function);

        Assert.True(diag.Structured > 0);
        Assert.Empty(diag.Stops);
        Assert.DoesNotContain(function.Descendants, node => node is Branch or ConditionalBranch);
    }

    [Fact]
    public void ComparisonTree_PastRegionCaseBody_TooLarge_StaysFlat()
    {
        var function = BuildPastRegionCaseBody(longCaseBody: true);

        var diag = RunWithDiagnostics(function);

        Assert.Equal(0, diag.Structured);
        Assert.Equal("cond-target-past-region", Assert.Single(diag.Stops));
    }

    static IrFunction BuildPastRegionCaseBody(bool longCaseBody)
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        LoadArgument X() => new(0, "x", intType);
        Constant C(int value) => new(value, intType);

        var container = new BlockContainer();
        var head = new Block(0);
        head.Add(new ConditionalBranch(new Comparison(ComparisonKind.GreaterThan, false, X(), C(100)), 20));
        var falseGuard1 = new Block(4);
        falseGuard1.Add(new ConditionalBranch(new Comparison(ComparisonKind.Equal, false, X(), C(1)), 32));
        var falseGuard2 = new Block(8);
        falseGuard2.Add(new ConditionalBranch(new Comparison(ComparisonKind.Equal, false, X(), C(2)), 32));
        var falseDefault = new Block(12);
        falseDefault.Add(new Return(C(10)));
        var trueArm = new Block(20);
        trueArm.Add(new Return(C(20)));

        var caseHead = new Block(32);
        caseHead.Add(new ConditionalBranch(new Comparison(ComparisonKind.Equal, false, X(), C(3)), longCaseBody ? 48 : 40));
        var caseFalse = new Block(36);
        caseFalse.Add(new Return(C(31)));

        var blocks = new List<Block> { head, falseGuard1, falseGuard2, falseDefault, trueArm, caseHead, caseFalse };
        if (longCaseBody)
        {
            var p1 = new Block(40);
            p1.Add(new StoreLocal(0, intType, C(40)));
            var p2 = new Block(44);
            p2.Add(new StoreLocal(0, intType, C(44)));
            var p3 = new Block(48);
            p3.Add(new StoreLocal(0, intType, C(48)));
            var p4 = new Block(52);
            p4.Add(new Return(C(32)));
            blocks.AddRange([p1, p2, p3, p4]);
        }
        else
        {
            var caseTrue = new Block(40);
            caseTrue.Add(new Return(C(32)));
            blocks.Add(caseTrue);
        }

        foreach (var block in blocks)
            container.Add(block);

        var signature = new MethodSignature(intType, [new Parameter("x", intType)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [intType], container);
    }

    [Theory]
    [InlineData(nameof(CfgSampleClass.TripleAnd))]
    [InlineData(nameof(CfgSampleClass.IfAnd))]
    [InlineData(nameof(CfgSampleClass.IfOr))]
    public void GuardChain_StructuresCleanlyWithNoStop(string methodName)
    {
        // &&/|| guard chains are in the slice today: the container structures and
        // the sink records the success, never a stop.
        var diag = RunWithDiagnostics(methodName);

        Assert.True(diag.Structured > 0);
        Assert.Empty(diag.Stops);
    }

    [Fact]
    public void NoSink_DefaultRunMatchesSinkRun()
    {
        // The sink is optional: a normal run (PassContext.None, null sink) records
        // nothing and behaves identically — the structuring decision is unchanged.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var withSink = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.GotoCommonExit))!;
        var withoutSink = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.GotoCommonExit))!;

        IrPasses.Run(withSink, IrPasses.Default, new PassContext(new Stepper(enabled: false), new StructuringDiagnostics()));
        IrPasses.Run(withoutSink);  // PassContext.None — no sink

        Assert.Equal(IrPrinter.Dump(withoutSink), IrPrinter.Dump(withSink));
    }

    [Fact]
    public void EarlyBreakFromInnerTry_StructuresTryBody_NoTerminatorSurvivorStop()
    {
        // A nested foreach whose inner loop early-`break`s lowers to a non-tail
        // `leave` that exits the inner try (the enumerator-dispose finally) to the
        // `if (!matched)` continuation — the surviving-leave shape of
        // HashSetEqualityComparer::Equals. The structuring pass treats a leave
        // that exits its container as a clean path terminator, so the inner try
        // body raises into `while (...) { if (...) { ...; goto ...; } }` instead
        // of bailing flat with `eh-terminator-survivor`. The outer body keeps the
        // leave's target label, so it stays flat (leave-target-in-container) — the
        // remaining, deliberately conservative, stop.
        var diag = RunWithDiagnostics(nameof(CfgSampleClass.AllOuterMatchInner));

        Assert.DoesNotContain("eh-terminator-survivor", diag.Stops);
        Assert.True(diag.Structured > 0);
        Assert.Equal("leave-target-in-container", Assert.Single(diag.Stops));
    }

    [Fact]
    public void EarlyBreakFromInnerTry_RaisesWhileLoopOverFlatGotoSoup()
    {
        // The readability win: the inner try body is a structured `while` whose
        // early exit is the surviving `goto ...; // leave`, not a flat chain of
        // gotos. Fidelity stays Full (the leave still renders the same goto).
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.AllOuterMatchInner));
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);

        var output = result.Output!.ReplaceLineEndings("\n");
        Assert.Contains("while (", output);
        Assert.Contains("// leave", output);
    }
}
