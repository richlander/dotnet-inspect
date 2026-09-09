using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// StructuringPass raises csc's infinite-loop lowering — an unconditional
/// backward branch to a loop head — into a <c>while (true)</c> loop whose exits
/// are the body's own break/return statements. Verified for both the
/// return-only form and the conditional-break form, with and without symbols.
/// </summary>
[Trait("Area", "Pass")]
public class InfiniteLoopStructuringTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function;
    }

    static IrFunction RaisedWithoutSymbols(string methodName)
    {
        using var source = MetadataSource.OpenWithoutSymbols(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function;
    }

    static bool IsWhileTrue(WhileLoop loop) =>
        loop.Condition is Constant { Value: true };

    [Fact]
    public void ReturnOnlyInfiniteLoop_RaisesToWhileTrue()
    {
        var function = Raised(nameof(CfgSampleClass.WhileTrueWithReturns));

        var loop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.True(IsWhileTrue(loop), "the back-edge loop must raise to while (true)");
        // The two early returns survive as guards inside the loop body; no goto
        // back-edge remains anywhere in the structured tree.
        Assert.Equal(2, loop.Body.Descendants.OfType<Return>().Count());
        Assert.Empty(function.Descendants.OfType<Branch>());
    }

    [Fact]
    public void BreakInfiniteLoop_RaisesToWhileTrueWithBreak()
    {
        var function = Raised(nameof(CfgSampleClass.WhileTrueWithBreak));

        var loop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.True(IsWhileTrue(loop), "the back-edge loop must raise to while (true)");
        Assert.Single(loop.Body.Descendants.OfType<Break>());
        Assert.Empty(function.Descendants.OfType<Branch>());
    }

    [Fact]
    public void WhileTrueWithReturns_PrintsWhileTrueAndGuards()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.WhileTrueWithReturns))).Output;

        Assert.Contains("while (true)", output);
        Assert.Contains("return", output);
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void WhileTrueWithBreak_PrintsBreak()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.WhileTrueWithBreak))).Output;

        Assert.Contains("while (true)", output);
        Assert.Contains("break;", output);
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void InfiniteLoop_WithoutSymbols_StillRaisesToWhileTrue()
    {
        var function = RaisedWithoutSymbols(nameof(CfgSampleClass.WhileTrueWithReturns));

        var loop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.True(IsWhileTrue(loop));
        Assert.Empty(function.Descendants.OfType<Branch>());
    }

    [Fact]
    public void ForEverLoop_RaisesToWhileTrue_LikeWhileTrue()
    {
        // `for (;;)` and `while (true)` share the unconditional-back-branch lowering
        // — no IL anchor distinguishes them — so the for(;;) source recovers as
        // while (true) just like the while(true) source does.
        var function = Raised(nameof(CfgSampleClass.ForEverLoopWithReturn));

        var loop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.True(IsWhileTrue(loop), "the for(;;) back-edge loop must raise to while (true)");
        Assert.Empty(function.Descendants.OfType<Branch>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("while (true)", output);
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void InfiniteLoopWithMidBodyContinue_DeclinesAndStaysFlat()
    {
        // A mid-body `continue` is a second back-edge to the loop head. The
        // infinite-loop shape used to decline it; cond-backward self-loop
        // recovery now lets the method fully structure without stray gotos.
        var function = Raised(nameof(CfgSampleClass.InfiniteLoopWithContinue));

        var loop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.True(IsWhileTrue(loop));
        Assert.Empty(function.Descendants.OfType<Branch>());
        Assert.DoesNotContain("goto", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void TryFinallyRetryLoop_RaisesToWhileTrueWithContinue()
    {
        var function = Raised(nameof(CfgSampleClass.TryFinallyRetryLoop));

        var loop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.True(IsWhileTrue(loop));
        Assert.Single(loop.Body.Descendants.OfType<TryFinally>());
        Assert.Single(loop.Body.Descendants.OfType<Continue>());
        Assert.Empty(function.Descendants.OfType<Leave>());

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("while (true)", output);
        Assert.Contains("continue;", output);
        Assert.Contains("finally", output);
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void LeaveRetryPastRegionExitUsesValidatedCloneAfterHeadDetaches()
    {
        var boolean = TypeRef.CoreLib("System", "Boolean");
        var int32 = TypeRef.CoreLib("System", "Int32");
        var @void = TypeRef.CoreLib("System", "Void");

        var retryTryBody = new BlockContainer();
        var retryLeave = new Block(0x0011);
        retryLeave.Add(new Leave(0x0000));
        retryTryBody.Add(retryLeave);

        var retryFinallyBody = new BlockContainer();
        var finallyBlock = new Block(0x0012);
        finallyBlock.Add(new StoreLocal(0, int32, new Constant(1, int32)));
        retryFinallyBody.Add(finallyBlock);

        var head = new Block(0x0000);
        head.Add(new ConditionalBranch(new LoadArgument(0, "done", boolean), 0x0030));
        var retry = new Block(0x0010);
        retry.Add(new TryFinally(retryTryBody, retryFinallyBody));
        var otherPredecessor = new Block(0x0020);
        otherPredecessor.Add(new StoreLocal(0, int32, new Constant(2, int32)));
        otherPredecessor.Add(new ConditionalBranch(
            new LoadArgument(1, "redundant", boolean),
            0x0030));
        var nestedTransferArm = new Block(0x0031);
        nestedTransferArm.Add(new Branch(0x0030));
        var exit = new Block(0x0030);
        exit.Add(new IfStatement(
            new LoadArgument(2, "spinTail", boolean),
            nestedTransferArm,
            null));
        exit.Add(new Return(null));

        var body = new BlockContainer();
        foreach (var block in (Block[])[head, retry, otherPredecessor, exit])
            body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "Repro"),
            new MethodSignature(
                @void,
                [
                    new Parameter("done", boolean),
                    new Parameter("redundant", boolean),
                    new Parameter("spinTail", boolean),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [int32],
            body);

        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var loop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.Contains(loop.Body.Descendants, node =>
            node is Return or Throw or Break
            || node is Branch { TargetOffset: 0x0030 }
            || node is Leave { TargetOffset: 0x0030 });
    }

    [Fact]
    public void NestedOuterRetryLoop_KeepsOuterRetryLeave()
    {
        var function = Raised(nameof(CfgSampleClass.NestedOuterRetryLoop));

        Assert.Equal(2, function.Descendants.OfType<WhileLoop>().Count());
        Assert.Contains(function.Descendants.OfType<Leave>(), leave => leave.TargetOffset == 0);

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("IL_0000:", output);
        Assert.Contains("goto IL_0000; // leave", output);
    }

    [Fact]
    public void SwitchLoopGotoDone_KeepsSwitchOwnedBreak()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(CfgSampleClass).FullName!,
            nameof(CfgSampleClass.SwitchLoopGotoDone))!;
        foreach (var pass in IrPasses.Default)
        {
            if (pass is StructuringPass)
                break;
            pass.Run(function, PassContext.None);
            function.CheckInvariant();
        }

        var @switch = Assert.Single(function.Descendants.OfType<Switch>());
        var caseZero = Assert.Single(
            @switch.Sections,
            section => section.Labels.Any(label => Equals(label.Value, 0)));
        var switchBreak = Assert.Single(caseZero.Body.Descendants.OfType<Break>());
        Assert.Same(@switch, StructuredTransferOwner(switchBreak));

        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(caseZero.Body.Descendants.OfType<WhileLoop>());
        Assert.Same(@switch, StructuredTransferOwner(switchBreak));
        Assert.Single(caseZero.Body.Descendants.OfType<Branch>());
    }

    [Fact]
    public void EnumeratorLoopCatchContinue_RaisesLeaveToContinue()
    {
        var function = Raised(nameof(CfgSampleClass.EnumeratorLoopCatchContinue));

        Assert.Contains(function.Descendants.OfType<TryCatch>(), tryCatch =>
            tryCatch.Clauses.Any(clause => clause.Body.Descendants.OfType<Continue>().Any()));
        Assert.Empty(function.Descendants.OfType<Leave>());

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("catch", output);
        Assert.Contains("continue;", output);
        Assert.DoesNotContain("goto IL_", output);
    }

    [Fact]
    public void EnumeratorLoopCatchBreak_RaisesLeaveToBreak()
    {
        var function = Raised(nameof(CfgSampleClass.EnumeratorLoopCatchBreak));

        Assert.Contains(function.Descendants.OfType<TryCatch>(), tryCatch =>
            tryCatch.Clauses.Any(clause => clause.Body.Descendants.OfType<Break>().Any()));
        Assert.Empty(function.Descendants.OfType<Leave>());

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("catch", output);
        Assert.Contains("break;", output);
        Assert.DoesNotContain("goto IL_", output);
    }

    [Fact]
    public void GuardedWhileNestedContinue_StaysFlat()
    {
        var function = Raised(nameof(CfgSampleClass.WhileNestedContinueKeepsArmExclusive));

        Assert.Empty(function.Descendants.OfType<WhileLoop>());

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("goto IL_", output);
        Assert.Contains("throw new FormatException();", output);
    }

    [Fact]
    public void InfiniteLoopContainingOuterOwnedContinue_StaysFlat()
    {
        var loopBody = new BlockContainer();

        var dispatch = new Block(0x00);
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", TypeRef.CoreLib("System", "Int32")),
            [0x10, 0x20]));
        loopBody.Add(dispatch);

        foreach (int offset in new[] { 0x08, 0x10, 0x20 })
        {
            var section = new Block(offset);
            section.Add(new Continue());
            loopBody.Add(section);
        }

        var latch = new Block(0x30);
        latch.Add(new Branch(0x00));
        loopBody.Add(latch);

        var function = InOuterLoop(loopBody);

        function.CheckInvariant();
        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<WhileLoop>());
        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.Equal(3, function.Descendants.OfType<Continue>().Count());
    }

    [Fact]
    public void GuardedWhileContainingOuterOwnedContinue_StaysFlat()
    {
        var loopBody = new BlockContainer();

        var guard = new Block(0x00);
        guard.Add(new Branch(0x20));
        loopBody.Add(guard);

        var body = new Block(0x10);
        body.Add(new Continue());
        loopBody.Add(body);

        var condition = new Block(0x20);
        condition.Add(new ConditionalBranch(
            new Constant(true, TypeRef.CoreLib("System", "Boolean")),
            0x10));
        loopBody.Add(condition);

        var exit = new Block(0x30);
        exit.Add(new Return(null));
        loopBody.Add(exit);

        var function = InOuterLoop(loopBody);

        function.CheckInvariant();
        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<WhileLoop>());
        Assert.Single(function.Descendants.OfType<Continue>());
        Assert.NotEmpty(function.Descendants.OfType<Branch>());
    }

    [Fact]
    public void PastRegionCloneContainingOuterContinue_StaysFlat()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var voidType = TypeRef.CoreLib("System", "Void");
        var candidate = new BlockContainer();

        var head = new Block(0x00);
        head.Add(new ConditionalBranch(
            new LoadArgument(0, "takeTail", boolType),
            0x40));
        candidate.Add(head);

        var latch = new Block(0x10);
        latch.Add(new Branch(0x00));
        candidate.Add(latch);

        var interveningExit = new Block(0x20);
        interveningExit.Add(new Return(null));
        candidate.Add(interveningExit);

        var continueArm = new Block();
        continueArm.Add(new Continue());
        var clonedTail = new Block(0x40);
        clonedTail.Add(new IfStatement(
            new LoadArgument(1, "again", boolType),
            continueArm,
            elseArm: null));
        clonedTail.Add(new Return(null));
        candidate.Add(clonedTail);

        var root = new BlockContainer();
        var outerLoopHolder = new Block(0x100);
        outerLoopHolder.Add(new DoWhileLoop(
            candidate,
            new Constant(false, boolType)));
        outerLoopHolder.Add(new Return(null));
        root.Add(outerLoopHolder);

        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(
                voidType,
                [
                    new Parameter("takeTail", boolType),
                    new Parameter("again", boolType),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            root);

        function.CheckInvariant();
        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<WhileLoop>());
        Assert.Single(function.Descendants.OfType<Continue>());
        Assert.Contains(
            function.Descendants.OfType<ConditionalBranch>(),
            branch => branch.TargetOffset == 0x40);
    }

    [Fact]
    public void RetryLeaveInsideExistingLoop_StaysFlat()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var voidType = TypeRef.CoreLib("System", "Void");

        var tryBody = new BlockContainer();
        var retry = new Block(0x110);
        retry.Add(new Leave(0x00));
        tryBody.Add(retry);

        var finallyBody = new BlockContainer();
        var finallyExit = new Block(0x120);
        finallyExit.Add(new EndFinally());
        finallyBody.Add(finallyExit);

        var innerLoopBody = new BlockContainer();
        var protectedBody = new Block(0x100);
        protectedBody.Add(new TryFinally(tryBody, finallyBody));
        innerLoopBody.Add(protectedBody);

        var body = new BlockContainer();
        var retryHead = new Block(0x00);
        retryHead.Add(new DoWhileLoop(
            innerLoopBody,
            new Constant(false, boolType)));
        body.Add(retryHead);

        var exit = new Block(0x10);
        exit.Add(new Return(null));
        body.Add(exit);

        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(
                voidType,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        function.CheckInvariant();
        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<WhileLoop>());
        Assert.Single(function.Descendants.OfType<Leave>());
        Assert.Empty(function.Descendants.OfType<Continue>());
    }

    [Theory]
    [InlineData(NestedFunctionKind.LocalFunction)]
    [InlineData(NestedFunctionKind.Lambda)]
    public void NestedFunctionRetryLeaveAtOuterHeadOffset_DoesNotCreateOuterLoop(
        NestedFunctionKind nestedFunction)
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var voidType = TypeRef.CoreLib("System", "Void");

        var body = new BlockContainer();
        var head = new Block(0x00);
        head.Add(new StoreLocal(0, intType, new Constant(0, intType)));
        body.Add(head);

        var localHolder = new Block(0x0A);
        IrNode nested = NestedFunctionWithLeave(nestedFunction, 0x00);
        nested.SetSourceOffset(0x0A);
        localHolder.Add(nested);
        body.Add(localHolder);

        var survivingBranch = new Block(0x14);
        var branchArm = new Block();
        branchArm.Add(new Branch(0x0A));
        survivingBranch.Add(new IfStatement(
            new Constant(true, boolType),
            branchArm,
            elseArm: null));
        body.Add(survivingBranch);

        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(
                voidType,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [intType],
            body);

        function.CheckInvariant();
        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.DescendantsOutsideNestedFunctions.OfType<WhileLoop>());
        Assert.Contains(
            function.DescendantsOutsideNestedFunctions.OfType<Branch>(),
            branch => branch.TargetOffset == 0x0A);

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        Assert.Contains("IL_000A:", output);
        Assert.Contains("goto IL_000A;", output);
        Assert.DoesNotContain("while (true)", output);
    }

    [Theory]
    [InlineData(NestedFunctionKind.LocalFunction, NestedLoopExit.Return)]
    [InlineData(NestedFunctionKind.LocalFunction, NestedLoopExit.Branch)]
    [InlineData(NestedFunctionKind.LocalFunction, NestedLoopExit.Break)]
    [InlineData(NestedFunctionKind.LocalFunction, NestedLoopExit.Leave)]
    [InlineData(NestedFunctionKind.Lambda, NestedLoopExit.Return)]
    [InlineData(NestedFunctionKind.Lambda, NestedLoopExit.Branch)]
    [InlineData(NestedFunctionKind.Lambda, NestedLoopExit.Break)]
    [InlineData(NestedFunctionKind.Lambda, NestedLoopExit.Leave)]
    public void NestedFunctionControlFlow_DoesNotQualifyOuterRetryLoop(
        NestedFunctionKind nestedFunction,
        NestedLoopExit nestedExit)
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var voidType = TypeRef.CoreLib("System", "Void");

        var retryTryBody = new BlockContainer();
        var retry = new Block(0x00);
        retry.Add(new Leave(0x00));
        retryTryBody.Add(retry);

        var retryFinallyBody = new BlockContainer();
        retryFinallyBody.Add(new Block(0x08));

        var body = new BlockContainer();
        var head = new Block(0x00);
        head.Add(NestedFunctionWithExit(nestedFunction, nestedExit));
        head.Add(new TryFinally(retryTryBody, retryFinallyBody));
        body.Add(head);

        var tail = new Block(0x10);
        tail.Add(new Return(null));
        body.Add(tail);

        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(
                voidType,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        function.CheckInvariant();
        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.DescendantsOutsideNestedFunctions.OfType<WhileLoop>());
        Assert.Contains(
            function.DescendantsOutsideNestedFunctions.OfType<Leave>(),
            leave => leave.TargetOffset == 0x00);
    }

    [Fact]
    public void InfiniteLoopContainingSwitchOwnedBreak_StaysFlat()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var sectionBody = new BlockContainer();

        var head = new Block(0x10);
        head.Add(new ConditionalBranch(
            new LoadArgument(1, "exit", boolType),
            0x20));
        sectionBody.Add(head);

        var exitArm = new Block(0x18);
        exitArm.Add(new Break());
        sectionBody.Add(exitArm);

        var latch = new Block(0x20);
        latch.Add(new Branch(0x10));
        sectionBody.Add(latch);

        var body = new BlockContainer();
        var entry = new Block(0x00);
        entry.Add(new Switch(
            new LoadArgument(0, "value", intType),
            [
                new SwitchSection(
                    [new Constant(0, intType)],
                    isDefault: false,
                    sectionBody),
            ]));
        entry.Add(new Return(null));
        body.Add(entry);

        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(
                voidType,
                [
                    new Parameter("value", intType),
                    new Parameter("exit", boolType),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        function.CheckInvariant();
        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<WhileLoop>());
        Assert.Single(function.Descendants.OfType<Break>());
        Assert.Single(function.Descendants.OfType<Branch>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedTerminatorCloneContainingSwitchOwnedBreak_StaysFlat(
        bool nestedBranch)
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var exceptionType = TypeRef.CoreLib("System", "Exception");
        var voidType = TypeRef.CoreLib("System", "Void");
        var sectionBody = new BlockContainer();

        var head = new Block(0x00);
        head.Add(new ConditionalBranch(
            new LoadArgument(1, "takeTail", boolType),
            0x40));
        sectionBody.Add(head);

        var latch = new Block(0x10);
        latch.Add(new Branch(0x00));
        sectionBody.Add(latch);

        var otherGuard = new Block(0x20);
        otherGuard.Add(new ConditionalBranch(
            new LoadArgument(2, "other", boolType),
            0x40));
        sectionBody.Add(otherGuard);

        var branchDestination = new Block(0x30);
        branchDestination.Add(new Return(null));
        sectionBody.Add(branchDestination);

        var breakArm = new Block();
        breakArm.Add(new Break());
        var sharedTerminator = new Block(0x40);
        if (nestedBranch)
        {
            var nestedBranchArm = new Block();
            nestedBranchArm.Add(new Branch(0x30));
            sharedTerminator.Add(new IfStatement(
                new LoadArgument(3, "jump", boolType),
                nestedBranchArm,
                elseArm: null));
        }
        sharedTerminator.Add(new IfStatement(
            new LoadArgument(4, "stop", boolType),
            breakArm,
            elseArm: null));
        sharedTerminator.Add(new Throw(new Constant(null, exceptionType)));
        sectionBody.Add(sharedTerminator);

        var body = new BlockContainer();
        var entry = new Block(0x100);
        entry.Add(new Switch(
            new LoadArgument(0, "value", intType),
            [
                new SwitchSection(
                    [new Constant(0, intType)],
                    isDefault: false,
                    sectionBody),
            ]));
        entry.Add(new Return(null));
        body.Add(entry);

        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(
                voidType,
                [
                    new Parameter("value", intType),
                    new Parameter("takeTail", boolType),
                    new Parameter("other", boolType),
                    new Parameter("jump", boolType),
                    new Parameter("stop", boolType),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        function.CheckInvariant();
        var @switch = Assert.Single(function.Descendants.OfType<Switch>());
        var before = Assert.Single(function.Descendants.OfType<Break>());
        Assert.Same(@switch, StructuredTransferOwner(before));

        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<WhileLoop>());
        var after = Assert.Single(function.Descendants.OfType<Break>());
        Assert.Same(@switch, StructuredTransferOwner(after));
    }

    static IrNode NestedFunctionWithLeave(
        NestedFunctionKind nestedFunction,
        int targetOffset)
    {
        var tryBody = new BlockContainer();
        var leave = new Block(0x00);
        leave.Add(new Leave(targetOffset));
        tryBody.Add(leave);

        var finallyBody = new BlockContainer();
        finallyBody.Add(new Block(0x04));

        var body = new BlockContainer();
        var holder = new Block(0x00);
        holder.Add(new TryFinally(tryBody, finallyBody));
        body.Add(holder);

        return NestedFunction(nestedFunction, body);
    }

    static IrNode NestedFunctionWithExit(
        NestedFunctionKind nestedFunction,
        NestedLoopExit nestedExit)
    {
        var body = new BlockContainer();
        var entry = new Block(0x00);
        body.Add(entry);

        switch (nestedExit)
        {
            case NestedLoopExit.Return:
                entry.Add(new Return(null));
                break;
            case NestedLoopExit.Branch:
                entry.Add(new Branch(0x10));
                body.Add(new Block(0x10));
                break;
            case NestedLoopExit.Break:
                var loopBody = new BlockContainer();
                var loopEntry = new Block(0x00);
                loopEntry.Add(new Break());
                loopBody.Add(loopEntry);
                entry.Add(new DoWhileLoop(
                    loopBody,
                    new Constant(false, TypeRef.CoreLib("System", "Boolean"))));
                break;
            case NestedLoopExit.Leave:
                var tryBody = new BlockContainer();
                var leave = new Block(0x00);
                leave.Add(new Leave(0x10));
                tryBody.Add(leave);

                var finallyBody = new BlockContainer();
                finallyBody.Add(new Block(0x08));

                entry.Add(new TryFinally(tryBody, finallyBody));
                body.Add(new Block(0x10));
                break;
        }

        return NestedFunction(nestedFunction, body);
    }

    static IrNode NestedFunction(
        NestedFunctionKind nestedFunction,
        BlockContainer body)
        => nestedFunction switch
        {
            NestedFunctionKind.LocalFunction => new LocalFunctionStatement(
                "Local",
                TypeRef.CoreLib("System", "Void"),
                [],
                isStatic: true,
                [],
                [],
                usesUpdatedMemorySafetyRules: false,
                skipLocalsInit: false,
                body),
            NestedFunctionKind.Lambda => new ExpressionStatement(
                new Lambda(
                    TypeRef.CoreLib("System", "Action"),
                    [],
                    [],
                    [],
                    usesUpdatedMemorySafetyRules: false,
                    skipLocalsInit: false,
                    body)
                {
                    ReturnsVoid = true,
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(nestedFunction)),
        };

    static IrFunction InOuterLoop(BlockContainer loopBody)
    {
        var body = new BlockContainer();
        var entry = new Block(0x00);
        entry.Add(new DoWhileLoop(
            loopBody,
            new Constant(false, TypeRef.CoreLib("System", "Boolean"))));
        entry.Add(new Return(null));
        body.Add(entry);

        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [new Parameter("value", TypeRef.CoreLib("System", "Int32"))],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);
    }

    static IrNode? StructuredTransferOwner(IrNode node)
    {
        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor is Switch or WhileLoop or DoWhileLoop or ForLoop or ForeachStatement)
                return ancestor;
        return null;
    }

    public enum NestedLoopExit
    {
        Return,
        Branch,
        Break,
        Leave,
    }

    public enum NestedFunctionKind
    {
        LocalFunction,
        Lambda,
    }
}
