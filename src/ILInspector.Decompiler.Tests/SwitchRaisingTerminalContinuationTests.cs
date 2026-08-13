using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class SwitchRaisingTerminalContinuationTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef s_void = TypeRef.CoreLib("System", "Void");

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
    public void CompilerProducedLoopContinue_RaisesWithoutResidualGoto()
    {
        var function = Import(nameof(CfgSampleClass.SwitchCaseContinueInLoop));
        Assert.Single(function.Descendants.OfType<SwitchBranch>());

        IrPasses.Run(function);
        function.CheckInvariant();

        var loop = Assert.Single(function.Descendants.OfType<DoWhileLoop>());
        var node = Assert.Single(loop.Descendants.OfType<Switch>());
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        Assert.Empty(node.Descendants.OfType<Continue>());
        Assert.Equal(6, node.Descendants.OfType<Break>().Count());
        Assert.Empty(function.Descendants.OfType<SwitchBranch>());

        Assert.Contains("switch (value)", output);
        Assert.Contains("case 4:", output);
        int defaultStart = output.IndexOf("default:", StringComparison.Ordinal);
        Assert.True(defaultStart >= 0, output);
        int defaultEnd = output.IndexOf("break;", defaultStart, StringComparison.Ordinal);
        Assert.True(defaultEnd > defaultStart, output);
        string defaultSection = output[defaultStart..defaultEnd];
        Assert.Contains("result += 10;", defaultSection);
        Assert.Contains("result += 100;", defaultSection);
        Assert.DoesNotContain("continue;", output);
        Assert.DoesNotContain("goto IL_", output);
    }

    [Fact]
    public void LoopOwnedContinue_RemainsTerminatingWhenSectionIsWrappedInSwitch()
    {
        var loopBody = new BlockContainer();

        var dispatch = new Block(0);
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x10, 0x20, 0x30, 0x40, 0x50]));
        loopBody.Add(dispatch);

        var defaultBody = new Block(0x08);
        defaultBody.Add(new Continue());
        loopBody.Add(defaultBody);

        int[] caseOffsets = [0x10, 0x20, 0x30, 0x40, 0x50];
        foreach (int offset in caseOffsets)
        {
            var caseBody = new Block(offset);
            caseBody.Add(new Continue());
            loopBody.Add(caseBody);
        }

        var body = new BlockContainer();
        var entry = new Block(0);
        entry.Add(new DoWhileLoop(loopBody, new Constant(false, s_bool)));
        entry.Add(new Return(null));
        body.Add(entry);

        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(
                s_void,
                [new Parameter("value", s_int)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        function.CheckInvariant();
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Equal(6, node.Descendants.OfType<Continue>().Count());
        Assert.Empty(node.Descendants.OfType<Break>());
        Assert.Empty(function.Descendants.OfType<SwitchBranch>());

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        Assert.Contains("switch (value)", output);
        Assert.Contains("case 4:", output);
        Assert.Contains("continue;", output);
        Assert.DoesNotContain("goto IL_", output);
    }

    [Fact]
    public void LoopOwnedBreakInsideContinueSection_DeclinesSwitchWrapping()
    {
        var loopBody = new BlockContainer();

        var dispatch = new Block(0);
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x10, 0x20, 0x30, 0x40, 0x50]));
        loopBody.Add(dispatch);

        var defaultBody = new Block(0x08);
        defaultBody.Add(new Continue());
        loopBody.Add(defaultBody);

        var breakArm = new Block();
        breakArm.Add(new Break());
        var guardedBreak = new Block(0x10);
        guardedBreak.Add(new IfStatement(
            new LoadArgument(1, "stop", s_bool),
            breakArm,
            elseArm: null));
        guardedBreak.Add(new Continue());
        loopBody.Add(guardedBreak);

        int[] caseOffsets = [0x20, 0x30, 0x40, 0x50];
        foreach (int offset in caseOffsets)
        {
            var caseBody = new Block(offset);
            caseBody.Add(new Continue());
            loopBody.Add(caseBody);
        }

        var function = CreateLoopFunction(
            loopBody,
            [new Parameter("value", s_int), new Parameter("stop", s_bool)]);

        function.CheckInvariant();
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.Empty(function.Descendants.OfType<Switch>());
        Assert.Single(function.Descendants.OfType<Break>());
        Assert.Equal(6, function.Descendants.OfType<Continue>().Count());
    }

    [Fact]
    public void ContinueSection_RemainsTerminalBesideJoiningSections()
    {
        var loopBody = new BlockContainer();

        var dispatch = new Block(0);
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x10, 0x20]));
        loopBody.Add(dispatch);

        var defaultBody = new Block(0x08);
        defaultBody.Add(new Branch(0x30));
        loopBody.Add(defaultBody);

        var continueCase = new Block(0x10);
        continueCase.Add(new Continue());
        loopBody.Add(continueCase);

        var joiningCase = new Block(0x20);
        joiningCase.Add(new Branch(0x30));
        loopBody.Add(joiningCase);

        var join = new Block(0x30);
        join.Add(new StoreLocal(0, s_int, new Constant(1, s_int)));
        join.Add(new Continue());
        loopBody.Add(join);

        var function = CreateLoopFunction(
            loopBody,
            [new Parameter("value", s_int)],
            [s_int]);

        function.CheckInvariant();
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        var sectionContinue = Assert.Single(node.Descendants.OfType<Continue>());
        var owner = Assert.IsType<Block>(sectionContinue.Parent);
        Assert.Same(sectionContinue, owner.Children[^1]);
        Assert.NotEmpty(node.Descendants.OfType<Break>());
        Assert.Empty(function.Descendants.OfType<SwitchBranch>());
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

    static IrFunction CreateLoopFunction(
        BlockContainer loopBody,
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<TypeRef>? locals = null)
    {
        var body = new BlockContainer();
        var entry = new Block(0);
        entry.Add(new DoWhileLoop(loopBody, new Constant(false, s_bool)));
        entry.Add(new Return(null));
        body.Add(entry);

        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(
                s_void,
                [.. parameters],
                HasThis: false,
                GenericParameterCount: 0),
            locals is null ? [] : [.. locals],
            body);
    }
}
