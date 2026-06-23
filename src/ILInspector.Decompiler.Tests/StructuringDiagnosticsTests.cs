using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class StructuringDiagnosticsTests
{
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");

    static StructuringDiagnostics RunWithDiagnostics(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        var diagnostics = new StructuringDiagnostics();
        IrPasses.Run(function, IrPasses.Default, new PassContext(new Stepper(enabled: false), diagnostics));
        return diagnostics;
    }

    static StructuringDiagnostics RunStructuringOnly(IrFunction function)
    {
        var diagnostics = new StructuringDiagnostics();
        new StructuringPass().Run(function, new PassContext(new Stepper(enabled: false), diagnostics));
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
        var function = BuildPastRegionCaseBody(comparisonTree: true);

        var diag = RunWithDiagnostics(function);
        IrPasses.Run(function);

        Assert.True(diag.Structured > 0);
        Assert.Empty(diag.Stops);
        Assert.DoesNotContain(function.Descendants, node => node is Branch or ConditionalBranch);
    }

    [Fact]
    public void PastRegionCaseBody_WithoutComparisonTree_StructuresCleanly()
    {
        var function = BuildPastRegionCaseBody(comparisonTree: false);

        var diag = RunWithDiagnostics(function);
        IrPasses.Run(function);

        Assert.True(diag.Structured > 0);
        Assert.Empty(diag.Stops);
        Assert.DoesNotContain(function.Descendants, node => node is Branch or ConditionalBranch);
    }

    static IrFunction BuildPastRegionCaseBody(bool comparisonTree)
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
        var caseCondition = new Comparison(ComparisonKind.Equal, false, X(), C(3));
        caseHead.Add(new ConditionalBranch(comparisonTree ? caseCondition : new LogicalNot(caseCondition), 40));
        var caseFalse = new Block(36);
        caseFalse.Add(new Return(C(31)));

        var blocks = new List<Block> { head, falseGuard1, falseGuard2, falseDefault, trueArm, caseHead, caseFalse };
        var caseTrue = new Block(40);
        caseTrue.Add(new Return(C(32)));
        blocks.Add(caseTrue);

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
    public void EarlyBreakFromInnerTry_StructuresThroughCommonExit_NoLeaveTargetStop()
    {
        // A nested foreach whose inner loop early-`break`s lowers to a non-tail
        // `leave` that exits the inner try (the enumerator-dispose finally) to the
        // `if (!matched)` continuation — the surviving-leave shape of
        // HashSetEqualityComparer::Equals. The row-5 slice consumes the leave when
        // it targets the recovered finally's normal continuation, so both the
        // inner body and the post-try continuation can structure without keeping a
        // label alive.
        var diag = RunWithDiagnostics(nameof(CfgSampleClass.AllOuterMatchInner));

        Assert.DoesNotContain("eh-terminator-survivor", diag.Stops);
        Assert.True(diag.Structured > 0);
        Assert.Empty(diag.Stops);
    }

    [Fact]
    public void EarlyBreakFromInnerTry_RaisesWithoutLeaveGoto()
    {
        // The readability win: the inner try body and its post-finally
        // continuation structure without either flat goto soup or a surviving
        // `goto ...; // leave`.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.AllOuterMatchInner));
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);

        var output = result.Output!.ReplaceLineEndings("\n");
        Assert.Contains("while (", output);
        Assert.DoesNotContain("// leave", output);
    }

    [Fact]
    public void LeaveToCommonExit_StructuresAfterRecoveredTryFinally()
    {
        var function = BuildTryFinallyWithLeaveToCommonExit();

        var diag = RunStructuringOnly(function);
        function.CheckInvariant();

        Assert.True(diag.Structured >= 2);
        Assert.Empty(diag.Stops);
        Assert.Empty(function.Descendants.OfType<Leave>());
        Assert.Single(function.Descendants.OfType<IfStatement>());

        var output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        Assert.Contains("try", output);
        Assert.Contains("finally", output);
        Assert.DoesNotContain("// leave", output);
    }

    [Fact]
    public void LeaveRetryLoop_KeepsOuterContinuationLabel()
    {
        var function = BuildTryFinallyWithLeaveRetryLoop();

        var diag = RunStructuringOnly(function);
        function.CheckInvariant();

        Assert.Contains(function.Descendants, node => node is Leave { TargetOffset: 0x0000 });
        Assert.Equal("leave-target-in-container", Assert.Single(diag.Stops));
    }

    [Fact]
    public void SelfContainedLeaveRetryLoop_RaisesCatchRetryToContinue()
    {
        var function = BuildSelfContainedTryFinallyWithCatchRetryLoop();

        var diag = RunStructuringOnly(function);
        function.CheckInvariant();

        Assert.True(diag.Structured > 0);
        Assert.Empty(diag.Stops);
        var loops = function.Descendants.OfType<WhileLoop>().ToList();
        Assert.Contains(loops, loop => loop.Condition is Constant { Value: true });
        Assert.Equal(2, function.Descendants.OfType<Continue>().Count());
        Assert.Contains(function.Descendants.OfType<Leave>(), leave => leave.TargetOffset == 0x0040);

        var output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        Assert.Contains("while (true)", output);
        Assert.Contains("continue;", output);
        Assert.Contains("catch", output);
        Assert.Contains("finally", output);
        Assert.Contains("goto IL_0040; // leave", output);
        Assert.DoesNotContain("goto IL_0010", output);
    }

    [Fact]
    public void TryCatchLeaveRetryLoop_WithFallthroughExit_RaisesRetryToContinueAndBreak()
    {
        var function = BuildTryCatchLeaveRetryLoopWithFallthroughExit();

        var diag = RunStructuringOnly(function);
        function.CheckInvariant();

        Assert.True(diag.Structured > 0);
        Assert.Empty(diag.Stops);
        Assert.Single(function.Descendants.OfType<Continue>());
        Assert.True(function.Descendants.OfType<Break>().Count() >= 2);

        var output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        Assert.Contains("while (true)", output);
        Assert.Contains("continue;", output);
        Assert.Contains("catch", output);
        Assert.Contains("break;", output);
        Assert.DoesNotContain("goto IL_0010", output);
    }

    static IrFunction BuildTryFinallyWithLeaveToCommonExit()
    {
        var tryBody = new BlockContainer();
        var guard = new Block(0x0010);
        guard.Add(new ConditionalBranch(new LoadArgument(0, "flag", Boolean), 0x0018));
        var exit = new Block(0x0014);
        exit.Add(new Leave(0x0030));
        var body = new Block(0x0018);
        body.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        body.Add(new Leave(0x0030));
        foreach (var block in (Block[])[guard, exit, body])
            tryBody.Add(block);

        var finallyBody = new BlockContainer();
        var finallyBlock = new Block(0x0020);
        finallyBlock.Add(new StoreLocal(1, Int32, new Constant(2, Int32)));
        finallyBody.Add(finallyBlock);

        var root = new BlockContainer();
        var holder = new Block(0x0010);
        holder.Add(new TryFinally(tryBody, finallyBody));
        var tail = new Block(0x0030);
        tail.Add(new Return(new LoadLocal(0, Int32)));
        root.Add(holder);
        root.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Int32, [new Parameter("flag", Boolean)], HasThis: false, GenericParameterCount: 0),
            [Int32, Int32],
            root);
    }

    static IrFunction BuildTryFinallyWithLeaveRetryLoop()
    {
        var tryBody = new BlockContainer();
        var body = new Block(0x0010);
        body.Add(new Leave(0x0000));
        tryBody.Add(body);

        var finallyBody = new BlockContainer();
        var finallyBlock = new Block(0x0020);
        finallyBlock.Add(new StoreLocal(0, Int32, new Constant(2, Int32)));
        finallyBody.Add(finallyBlock);

        var root = new BlockContainer();
        var retry = new Block(0x0000);
        retry.Add(new StoreLocal(0, Int32, new Constant(0, Int32)));
        var holder = new Block(0x0010);
        holder.Add(new TryFinally(tryBody, finallyBody));
        var tail = new Block(0x0030);
        tail.Add(new Return(null));
        foreach (var block in (Block[])[retry, holder, tail])
            root.Add(block);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Int32],
            root);
    }

    static IrFunction BuildSelfContainedTryFinallyWithCatchRetryLoop()
    {
        var exception = TypeRef.CoreLib("System", "Exception");

        var retryTryBody = new BlockContainer();
        var retryTry = new Block(0x0030);
        retryTry.Add(new Leave(0x0010));
        retryTryBody.Add(retryTry);

        var retryCatchBody = new BlockContainer();
        var retryCatch = new Block(0x0038);
        retryCatch.Add(new ExpressionStatement(new CaughtException(exception)));
        retryCatch.Add(new Leave(0x0010));
        retryCatchBody.Add(retryCatch);

        var outerTry = new BlockContainer();
        var head = new Block(0x0010);
        head.Add(new StoreLocal(0, Int32, new Constant(0, Int32)));
        var exitTryBody = new BlockContainer();
        var exitTry = new Block(0x0020);
        exitTry.Add(new Leave(0x0040));
        exitTryBody.Add(exitTry);
        var exitFinallyBody = new BlockContainer();
        var exitFinally = new Block(0x0028);
        exitFinally.Add(new StoreLocal(0, Int32, new Constant(2, Int32)));
        exitFinallyBody.Add(exitFinally);
        var exitHolder = new Block(0x0020);
        exitHolder.Add(new TryFinally(exitTryBody, exitFinallyBody));
        var handler = new Block(0x0030);
        handler.Add(new TryCatch(retryTryBody, [new CatchClause(exception, retryCatchBody)]));
        outerTry.Add(head);
        outerTry.Add(exitHolder);
        outerTry.Add(handler);

        var outerFinally = new BlockContainer();
        var finallyBlock = new Block(0x003C);
        finallyBlock.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        outerFinally.Add(finallyBlock);

        var root = new BlockContainer();
        var holder = new Block(0x0010);
        holder.Add(new TryFinally(outerTry, outerFinally));
        var tail = new Block(0x0040);
        tail.Add(new Return(null));
        root.Add(holder);
        root.Add(tail);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0),
            [Int32],
            root);
    }

    static IrFunction BuildTryCatchLeaveRetryLoopWithFallthroughExit()
    {
        var exception = TypeRef.CoreLib("System", "Exception");

        var retryTryBody = new BlockContainer();
        var retryTry = new Block(0x0020);
        retryTry.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        retryTry.Add(new Leave(0x0010));
        retryTryBody.Add(retryTry);

        var retryCatchBody = new BlockContainer();
        var retryCatch = new Block(0x0030);
        retryCatch.Add(new ExpressionStatement(new CaughtException(exception)));
        retryCatchBody.Add(retryCatch);

        var root = new BlockContainer();
        var head = new Block(0x0010);
        head.Add(new ConditionalBranch(new LoadArgument(0, "done", Boolean), 0x0040));
        var holder = new Block(0x0020);
        holder.Add(new TryCatch(retryTryBody, [new CatchClause(exception, retryCatchBody)]));
        var tail = new Block(0x0040);
        tail.Add(new Return(null));
        foreach (var block in (Block[])[head, holder, tail])
            root.Add(block);

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Void, [new Parameter("done", Boolean)], HasThis: false, GenericParameterCount: 0),
            [Int32],
            root);
    }
}
