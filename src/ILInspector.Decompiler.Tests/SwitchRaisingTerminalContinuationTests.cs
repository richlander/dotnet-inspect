using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class SwitchRaisingTerminalContinuationTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");

    [Fact]
    public void CompilerProducedBreakToTerminalReturn_PreservesReturnAsJoin()
    {
        var function = Import(nameof(CfgSampleClass.TerminalSwitchBreakToReturn));
        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.Same(
            function.Body.Blocks[function.Body.Blocks.Count - 1],
            Assert.Single(function.Body.Blocks, block => block.Children is [Return { Value: null }]));

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Empty(node.Descendants.OfType<Return>());
        Assert.NotEmpty(node.Descendants.OfType<Break>());
        Assert.Single(function.Descendants.OfType<Return>());

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        Assert.Contains("case CfgTerminalSwitchKind.Value25:", output);
        Assert.Contains("case CfgTerminalSwitchKind.Value40:", output);
        Assert.Contains("        break;", output);
        Assert.DoesNotContain("        return;", output);
    }

    [Fact]
    public void CompilerProducedReturnInsideCase_RemainsTerminatingSection()
    {
        var function = Import(nameof(CfgSampleClass.TerminalSwitchReturnInCase));
        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.NotSame(
            function.Body.Blocks[function.Body.Blocks.Count - 1],
            Assert.Single(function.Body.Blocks, block => block.Children is [Return { Value: null }]));

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Single(node.Descendants.OfType<Return>());
        Assert.Empty(node.Descendants.OfType<Break>());

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        Assert.Contains("case CfgTerminalSwitchKind.Value25:", output);
        Assert.Contains("case CfgTerminalSwitchKind.Value40:", output);
        Assert.Contains("        return;", output);
    }

    [Fact]
    public void RunningJoinCandidateThatIsAlsoACaseTarget_IsNotSkipped()
    {
        var body = new BlockContainer();

        var entry = new Block(0);
        entry.Add(new SwitchBranch(
            new LoadArgument(0, "n", s_int),
            [0x10, 0x40, 0x20, 0x30]));
        body.Add(entry);

        var caseA = new Block(0x10);
        caseA.Add(new Branch(0x40));
        body.Add(caseA);

        var caseB = new Block(0x20);
        caseB.Add(new Branch(0x38));
        body.Add(caseB);

        var caseC = new Block(0x30);
        caseC.Add(new Branch(0x38));
        body.Add(caseC);

        var upstreamJoin = new Block(0x38);
        upstreamJoin.Add(new StoreLocal(1, s_int, new Constant(0, s_int)));
        body.Add(upstreamJoin);

        var caseTarget = new Block(0x40);
        caseTarget.Add(new StoreLocal(0, s_int, new Constant(5, s_int)));
        body.Add(caseTarget);

        var returnBlock = new Block(0x50);
        returnBlock.Add(new Return(new LoadLocal(0, s_int)));
        body.Add(returnBlock);

        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(
                s_int,
                [new Parameter("n", s_int)],
                HasThis: false,
                GenericParameterCount: 0),
            [s_int, s_int],
            body);

        function.CheckInvariant();
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<Switch>());
        Assert.Single(function.Descendants.OfType<SwitchBranch>());
    }

    static IrFunction Import(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        return function;
    }
}
