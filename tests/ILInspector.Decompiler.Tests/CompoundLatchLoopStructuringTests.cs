using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class CompoundLatchLoopStructuringTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function;
    }

    [Fact]
    public void CompoundLatchLoop_RaisesToWhileLoop()
    {
        var function = Raised(nameof(CfgSampleClass.CompoundLatchLoop));

        var loop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.IsType<LogicalBinary>(loop.Condition);
        Assert.Empty(function.Descendants.OfType<Branch>());
        Assert.Empty(function.Descendants.OfType<ConditionalBranch>());
    }

    [Fact]
    public void CompoundLatchLoop_PrintsConditionWithoutGotos()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.CompoundLatchLoop)))
            .Output!
            .ReplaceLineEndings("\n");

        Assert.Contains("while (i < count && result == 0)", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void CompoundLatchLoop_DeclinesImpureLatchBlock()
    {
        var function = ImpureSecondLatchBlock();
        var diagnostics = new StructuringDiagnostics();

        IrPasses.Run(function, IrPasses.Default, new PassContext(new Stepper(enabled: false), diagnostics));

        Assert.Equal(0, diagnostics.Structured);
        Assert.Contains("forward-branch-not-region-exit", diagnostics.Stops);
    }

    static IrFunction ImpureSecondLatchBlock()
    {
        LoadArgument Count() => new(0, "count", Int32);
        LoadLocal I() => new(0, Int32);
        LoadLocal Result() => new(1, Int32);

        var container = new BlockContainer();
        var init = new Block(0);
        init.Add(new StoreLocal(0, Int32, new Constant(0, Int32)));
        init.Add(new StoreLocal(1, Int32, new Constant(0, Int32)));
        init.Add(new Branch(16));

        var body = new Block(4);
        body.Add(new StoreLocal(1, Int32, new Binary(BinaryKind.Subtract, isChecked: false, isUnsigned: false, I(), new Constant(3, Int32))));
        body.Add(new StoreLocal(0, Int32, new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, I(), new Constant(1, Int32))));

        var firstLatch = new Block(16);
        firstLatch.Add(new ConditionalBranch(
            new Comparison(ComparisonKind.GreaterThanOrEqual, false, I(), Count()), 32));

        var impureSecondLatch = new Block(24);
        impureSecondLatch.Add(new StoreLocal(2, Int32, new Constant(1, Int32)));
        impureSecondLatch.Add(new ConditionalBranch(new LogicalNot(Result()), 4));

        var exit = new Block(32);
        exit.Add(new Return(Result()));

        foreach (var block in (Block[])[init, body, firstLatch, impureSecondLatch, exit])
            container.Add(block);

        var signature = new MethodSignature(
            Int32,
            [new Parameter("count", Int32)],
            HasThis: false,
            GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [Int32, Int32, Int32], container);
    }
}
