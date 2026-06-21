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
}
