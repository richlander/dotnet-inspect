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
    public void InteriorizedCaseLoopWithOuterContinue_DeclinesSwitchRaise()
    {
        var loopBody = new BlockContainer();

        var dispatch = new Block(0);
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x10, 0x40]));
        loopBody.Add(dispatch);

        var defaultBody = new Block(0x08);
        defaultBody.Add(new Return(null));
        loopBody.Add(defaultBody);

        var loopHead = new Block(0x10);
        loopHead.Add(new ConditionalBranch(
            new LoadArgument(1, "repeat", s_bool),
            0x30));
        loopBody.Add(loopHead);

        var continueOuterLoop = new Block(0x20);
        continueOuterLoop.Add(new Continue());
        loopBody.Add(continueOuterLoop);

        var loopLatch = new Block(0x30);
        loopLatch.Add(new Branch(0x10));
        loopBody.Add(loopLatch);

        var otherCase = new Block(0x40);
        otherCase.Add(new Return(null));
        loopBody.Add(otherCase);

        var function = CreateLoopFunction(
            loopBody,
            [
                new Parameter("value", s_int),
                new Parameter("repeat", s_bool),
            ]);

        new SwitchRaisingPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.Empty(function.Descendants.OfType<Switch>());
        Assert.Single(function.Descendants.OfType<Continue>());
    }

    [Fact]
    public void CaseTargetJoinWithInteriorizedLoopAndOuterContinue_DeclinesSwitchRaise()
    {
        var loopBody = new BlockContainer();

        var dispatch = new Block(0x00);
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x50, 0x20]));
        loopBody.Add(dispatch);

        var defaultBody = new Block(0x10);
        defaultBody.Add(new Branch(0x50));
        loopBody.Add(defaultBody);

        var loopHead = new Block(0x20);
        loopHead.Add(new ConditionalBranch(
            new LoadArgument(1, "repeat", s_bool),
            0x40));
        loopBody.Add(loopHead);

        var continueOuterLoop = new Block(0x30);
        continueOuterLoop.Add(new Continue());
        loopBody.Add(continueOuterLoop);

        var loopLatch = new Block(0x40);
        loopLatch.Add(new Branch(0x20));
        loopBody.Add(loopLatch);

        var join = new Block(0x50);
        join.Add(new Return(null));
        loopBody.Add(join);

        var function = CreateLoopFunction(
            loopBody,
            [
                new Parameter("value", s_int),
                new Parameter("repeat", s_bool),
            ]);

        new SwitchRaisingPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.Empty(function.Descendants.OfType<Switch>());
        Assert.Single(function.Descendants.OfType<Continue>());
    }

    [Fact]
    public void NestedBackEdgeWithOuterContinue_DeclinesSwitchRaise()
    {
        var loopBody = new BlockContainer();

        var dispatch = new Block(0x00);
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x10, 0x40]));
        loopBody.Add(dispatch);

        var defaultBody = new Block(0x08);
        defaultBody.Add(new Return(null));
        loopBody.Add(defaultBody);

        var caseHead = new Block(0x10);
        loopBody.Add(caseHead);

        var backEdgeArm = new Block();
        backEdgeArm.Add(new Branch(0x10));
        var caseExit = new Block(0x20);
        caseExit.Add(new IfStatement(
            new LoadArgument(1, "repeat", s_bool),
            backEdgeArm,
            elseArm: null));
        caseExit.Add(new Continue());
        loopBody.Add(caseExit);

        var otherCase = new Block(0x40);
        otherCase.Add(new Return(null));
        loopBody.Add(otherCase);

        var function = CreateLoopFunction(
            loopBody,
            [
                new Parameter("value", s_int),
                new Parameter("repeat", s_bool),
            ]);

        function.CheckInvariant();
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.Empty(function.Descendants.OfType<Switch>());
        Assert.Single(function.Descendants.OfType<Continue>());
    }

    [Fact]
    public void DuplicateOwnedBlockOffsets_DeclineWithoutThrowing()
    {
        var body = new BlockContainer();

        var dispatch = new Block(0x00);
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x10]));
        body.Add(dispatch);

        var inlineDefault = new Block(0x08);
        inlineDefault.Add(new StoreLocal(0, s_int, new Constant(1, s_int)));
        body.Add(inlineDefault);

        var defaultExit = new Block(0x10);
        defaultExit.Add(new Branch(0x20));
        body.Add(defaultExit);

        var caseBody = new Block(0x10);
        caseBody.Add(new Branch(0x20));
        body.Add(caseBody);

        var join = new Block(0x20);
        join.Add(new Return(null));
        body.Add(join);

        var function = CreateFunction(
            body,
            [new Parameter("value", s_int)],
            [s_int]);

        function.CheckInvariant();
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.Empty(function.Descendants.OfType<Switch>());
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
    public void PrecedingNestedBranchEnteringContinueCase_RemainsFlat()
    {
        var loopBody = new BlockContainer();

        var enteringArm = new Block();
        enteringArm.Add(new Branch(0x20));
        var preceding = new Block(0);
        preceding.Add(new IfStatement(
            new LoadArgument(1, "enterCase", s_bool),
            enteringArm,
            elseArm: null));
        loopBody.Add(preceding);

        var dispatch = new Block(0x10);
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x20, 0x30]));
        loopBody.Add(dispatch);

        var defaultBody = new Block(0x18);
        defaultBody.Add(new Continue());
        loopBody.Add(defaultBody);

        var firstCase = new Block(0x20);
        firstCase.Add(new Continue());
        loopBody.Add(firstCase);

        var secondCase = new Block(0x30);
        secondCase.Add(new Continue());
        loopBody.Add(secondCase);

        var function = CreateLoopFunction(
            loopBody,
            [new Parameter("value", s_int), new Parameter("enterCase", s_bool)]);

        function.CheckInvariant();
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.Empty(function.Descendants.OfType<Switch>());
    }

    [Fact]
    public void DispatchHeadNestedBranchEnteringContinueCase_RemainsFlat()
    {
        var loopBody = new BlockContainer();

        var enteringArm = new Block();
        enteringArm.Add(new Branch(0x20));
        var dispatch = new Block(0);
        dispatch.Add(new IfStatement(
            new LoadArgument(1, "enterCase", s_bool),
            enteringArm,
            elseArm: null));
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x20, 0x30]));
        loopBody.Add(dispatch);

        var defaultBody = new Block(0x18);
        defaultBody.Add(new Continue());
        loopBody.Add(defaultBody);

        var firstCase = new Block(0x20);
        firstCase.Add(new Continue());
        loopBody.Add(firstCase);

        var secondCase = new Block(0x30);
        secondCase.Add(new Continue());
        loopBody.Add(secondCase);

        var function = CreateLoopFunction(
            loopBody,
            [new Parameter("value", s_int), new Parameter("enterCase", s_bool)]);

        function.CheckInvariant();
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.Empty(function.Descendants.OfType<Switch>());
    }

    [Fact]
    public void DeeplyNestedBranchEnteringCase_DeclinesWithoutRecursiveTraversal()
    {
        IrNode enteringCase = new Branch(0x20);
        for (int i = 0; i < 20_000; i++)
        {
            var then = new Block();
            then.Add(enteringCase);
            enteringCase = new IfStatement(new Constant(true, s_bool), then, elseArm: null);
        }

        var body = new BlockContainer();
        var dispatch = new Block(0);
        dispatch.Add(enteringCase);
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x20, 0x30]));
        body.Add(dispatch);

        foreach (int offset in new[] { 0x10, 0x20, 0x30 })
        {
            var section = new Block(offset);
            section.Add(new Return(null));
            body.Add(section);
        }

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

        new SwitchRaisingPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.Empty(function.Descendants.OfType<Switch>());
    }

    [Fact]
    public void OwnedCaseBranchToConsumedDefaultDispatcher_DeclinesSwitchRaise()
    {
        var loopBody = new BlockContainer();

        var dispatch = new Block(0x00);
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x10]));
        loopBody.Add(dispatch);

        var defaultDispatcher = new Block(0x08);
        defaultDispatcher.Add(new Branch(0x20));
        loopBody.Add(defaultDispatcher);

        var enterDefaultArm = new Block();
        enterDefaultArm.Add(new Branch(0x08));

        var caseBody = new Block(0x10);
        caseBody.Add(new IfStatement(
            new LoadArgument(1, "enterDefault", s_bool),
            enterDefaultArm,
            elseArm: null));
        caseBody.Add(new Continue());
        loopBody.Add(caseBody);

        var defaultBody = new Block(0x20);
        defaultBody.Add(new StoreLocal(
            0,
            s_int,
            new Constant(42, s_int)));
        defaultBody.Add(new Continue());
        loopBody.Add(defaultBody);

        var function = CreateLoopFunction(
            loopBody,
            [
                new Parameter("value", s_int),
                new Parameter("enterDefault", s_bool),
            ],
            [s_int]);

        function.CheckInvariant();
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.Empty(function.Descendants.OfType<Switch>());
        Assert.Contains(
            function.Descendants.OfType<Block>(),
            block => block.StartOffset == 0x08);
    }

    [Fact]
    public void OwnedCaseBranchEnteringSiblingCaseRegion_DeclinesSwitchRaise()
    {
        var body = new BlockContainer();

        var dispatch = new Block(0x00);
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x10, 0x20]));
        body.Add(dispatch);

        var defaultDispatcher = new Block(0x08);
        defaultDispatcher.Add(new Branch(0x40));
        body.Add(defaultDispatcher);

        var enterSiblingArm = new Block();
        enterSiblingArm.Add(new Branch(0x30));
        var firstCase = new Block(0x10);
        firstCase.Add(new IfStatement(
            new LoadArgument(1, "enterSibling", s_bool),
            enterSiblingArm,
            elseArm: null));
        firstCase.Add(new Branch(0x40));
        body.Add(firstCase);

        var secondCase = new Block(0x20);
        secondCase.Add(new ConditionalBranch(
            new LoadArgument(2, "condition", s_bool),
            0x30));
        body.Add(secondCase);

        var falseBody = new Block(0x28);
        falseBody.Add(new StoreLocal(0, s_int, new Constant(1, s_int)));
        falseBody.Add(new Branch(0x38));
        body.Add(falseBody);

        var trueBody = new Block(0x30);
        trueBody.Add(new StoreLocal(0, s_int, new Constant(2, s_int)));
        body.Add(trueBody);

        var secondCaseExit = new Block(0x38);
        secondCaseExit.Add(new Branch(0x40));
        body.Add(secondCaseExit);

        var join = new Block(0x40);
        join.Add(new Return(null));
        body.Add(join);

        var function = CreateFunction(
            body,
            [
                new Parameter("value", s_int),
                new Parameter("enterSibling", s_bool),
                new Parameter("condition", s_bool),
            ],
            [s_int]);

        function.CheckInvariant();
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.Empty(function.Descendants.OfType<Switch>());
    }

    [Fact]
    public void OwnedCaseBranchWithinSameRegion_RaisesSwitch()
    {
        var body = new BlockContainer();

        var dispatch = new Block(0x00);
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x10, 0x20]));
        body.Add(dispatch);

        var defaultDispatcher = new Block(0x08);
        defaultDispatcher.Add(new Branch(0x30));
        body.Add(defaultDispatcher);

        var enterLaterArm = new Block();
        enterLaterArm.Add(new Branch(0x18));
        var firstCase = new Block(0x10);
        firstCase.Add(new IfStatement(
            new LoadArgument(1, "enterLater", s_bool),
            enterLaterArm,
            elseArm: null));
        body.Add(firstCase);

        var firstCaseExit = new Block(0x18);
        firstCaseExit.Add(new Branch(0x30));
        body.Add(firstCaseExit);

        var secondCase = new Block(0x20);
        secondCase.Add(new Branch(0x30));
        body.Add(secondCase);

        var join = new Block(0x30);
        join.Add(new Return(null));
        body.Add(join);

        var function = CreateFunction(
            body,
            [
                new Parameter("value", s_int),
                new Parameter("enterLater", s_bool),
            ]);

        function.CheckInvariant();
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<SwitchBranch>());
        Assert.Single(function.Descendants.OfType<Switch>());
    }

    [Fact]
    public void DispatchHeadDirectBranchEnteringContinueCase_RemainsFlat()
    {
        var loopBody = new BlockContainer();

        var dispatch = new Block(0);
        dispatch.Add(new ConditionalBranch(
            new LoadArgument(1, "enterCase", s_bool),
            0x20));
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x20, 0x30]));
        loopBody.Add(dispatch);

        var defaultBody = new Block(0x18);
        defaultBody.Add(new Continue());
        loopBody.Add(defaultBody);

        var firstCase = new Block(0x20);
        firstCase.Add(new Continue());
        loopBody.Add(firstCase);

        var secondCase = new Block(0x30);
        secondCase.Add(new Continue());
        loopBody.Add(secondCase);

        var function = CreateLoopFunction(
            loopBody,
            [new Parameter("value", s_int), new Parameter("enterCase", s_bool)]);

        function.CheckInvariant();
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.Empty(function.Descendants.OfType<Switch>());
    }

    [Fact]
    public void NestedFunctionBranchOffset_DoesNotEnterOuterContinueCase()
    {
        var loopBody = new BlockContainer();

        var localBody = new BlockContainer();
        var localBlock = new Block();
        localBlock.Add(new Branch(0x20));
        localBody.Add(localBlock);

        var preceding = new Block(0);
        preceding.Add(new LocalFunctionStatement(
            "Local",
            s_void,
            [],
            isStatic: true,
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            localBody));
        loopBody.Add(preceding);

        var dispatch = new Block(0x10);
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x20, 0x30]));
        loopBody.Add(dispatch);

        var defaultBody = new Block(0x18);
        defaultBody.Add(new Continue());
        loopBody.Add(defaultBody);

        var firstCase = new Block(0x20);
        firstCase.Add(new Continue());
        loopBody.Add(firstCase);

        var secondCase = new Block(0x30);
        secondCase.Add(new Continue());
        loopBody.Add(secondCase);

        var function = CreateLoopFunction(
            loopBody,
            [new Parameter("value", s_int)]);

        function.CheckInvariant();
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Empty(function.Descendants.OfType<SwitchBranch>());
        Assert.Single(localBody.Descendants.OfType<Branch>());
    }

    [Fact]
    public void NestedFunctionLeaveOffset_DoesNotEnterOuterContinueCase()
    {
        var loopBody = new BlockContainer();

        var localBody = new BlockContainer();
        var localBlock = new Block();
        localBlock.Add(new Leave(0x20));
        localBody.Add(localBlock);

        var preceding = new Block(0);
        preceding.Add(new LocalFunctionStatement(
            "Local",
            s_void,
            [],
            isStatic: true,
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            localBody));
        loopBody.Add(preceding);

        var dispatch = new Block(0x10);
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x20, 0x30]));
        loopBody.Add(dispatch);

        var defaultBody = new Block(0x18);
        defaultBody.Add(new Continue());
        loopBody.Add(defaultBody);

        var firstCase = new Block(0x20);
        firstCase.Add(new Continue());
        loopBody.Add(firstCase);

        var secondCase = new Block(0x30);
        secondCase.Add(new Continue());
        loopBody.Add(secondCase);

        var function = CreateLoopFunction(
            loopBody,
            [new Parameter("value", s_int)]);

        function.CheckInvariant();
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Empty(function.Descendants.OfType<SwitchBranch>());
        Assert.Single(localBody.Descendants.OfType<Leave>());
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

    static IrFunction CreateFunction(
        BlockContainer body,
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<TypeRef>? locals = null) =>
        new(
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
