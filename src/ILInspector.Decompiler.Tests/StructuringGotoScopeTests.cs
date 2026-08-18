using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

// Adversarial guard for issue #1517. StructuringPass must not wrap a forward
// diamond arm around a block that an outside control transfer still targets —
// most commonly a surviving (unraised) SwitchBranch dispatch, or the sibling
// arm. Nesting such a block puts its label inside the arm's braces while the
// outside goto stays at the container scope, which the C# compiler rejects with
// CS0159 ("no such label within the scope of the goto"). The pass must keep the
// container flat instead. csc never emits this shape directly (the switch is
// usually raised), so these are synthetic-IR near-misses, paired with a clean
// positive diamond canary that must still raise.
public class StructuringGotoScopeTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Owner = TypeRef.CoreLib("Synthetic", "T");

    static IrExpression Cond() =>
        new Comparison(ComparisonKind.Equal, isUnsigned: false, new LoadArgument(0, "a", Int32), new Constant(0, Int32));

    static IrFunction Structured(Block[] blocks)
    {
        var container = new BlockContainer();
        foreach (var block in blocks)
            container.Add(block);
        var signature = new MethodSignature(Int32, [new Parameter("a", Int32)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", Owner, signature, [Int32], container);
        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();
        return function;
    }

    // A clean forward diamond:
    //   if (a == 0) goto trueArm;   // b0, false arm falls through
    //   falseArm: goto join;        // b1
    //   trueArm:  V_0 = 1;          // b2, falls into join
    //   join:     return V_0;       // b3
    static Block[] CleanDiamond()
    {
        var b0 = new Block(0);
        b0.Add(new ConditionalBranch(Cond(), 24));     // -> trueArm (b2 @ 24)
        var b1 = new Block(8);                          // false arm
        b1.Add(new Branch(32));                         // -> join
        var b2 = new Block(24);                         // true arm head
        b2.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        var b3 = new Block(32);                         // join
        b3.Add(new Return(new LoadLocal(0, Int32)));
        return [b0, b1, b2, b3];
    }

    [Fact]
    public void CleanDiamond_RaisesToIfElse()
    {
        var function = Structured(CleanDiamond());
        Assert.NotEmpty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void RegionExitDiamondWithCrossArmTransferStaysFlat()
    {
        var crossArm = new Block(21);
        crossArm.Add(new Branch(30));
        var blocks = new[]
        {
            Block(0, new ConditionalBranch(Cond(), 40)),
            Block(10, new ConditionalBranch(Cond(), 30)),
            Block(
                20,
                new IfStatement(
                    new Constant(true, TypeRef.CoreLib("System", "Boolean")),
                    crossArm,
                    null),
                new Branch(40)),
            Block(
                30,
                new StoreLocal(0, Int32, new Constant(7, Int32)),
                new Branch(40)),
            Block(40, new Return(new LoadLocal(0, Int32))),
        };

        var function = Structured(blocks);

        Assert.Equal(blocks.Length, function.Body.Blocks.Count);
        Assert.Single(function.Descendants.OfType<IfStatement>());
        Assert.Contains(
            function.Descendants.OfType<Branch>(),
            branch => branch.TargetOffset == 30);
    }

    [Fact]
    public void CompilerTwoCaseSwitchReturnKeepsDissolvingCrossArmStructured()
    {
        const string source = """
            using System;

            namespace CompilerFixtures;

            public static class TwoCaseSwitchFixture
            {
                public static object M(int index, object first, object second)
                {
                    switch (index)
                    {
                        case 0:
                            return first;
                        case 1:
                            return second;
                        default:
                            throw new IndexOutOfRangeException();
                    }
                }
            }
            """;
        string path = Path.Combine(Path.GetTempPath(), $"two-case-switch-{Guid.NewGuid():N}.dll");

        try
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var compilation = CSharpCompilation.Create(
                "TwoCaseSwitchFixture",
                [CSharpSyntaxTree.ParseText(
                    source,
                    new CSharpParseOptions(LanguageVersion.Preview),
                    cancellationToken: cancellationToken)],
                RoslynTestReferences.TrustedPlatform,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release));
            var emit = compilation.Emit(path, cancellationToken: cancellationToken);
            Assert.True(
                emit.Success,
                "fixture compilation failed:\n"
                    + string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

            using var metadata = MetadataSource.OpenWithoutSymbols(path);
            var function = IrImporter.Import(metadata, "CompilerFixtures.TwoCaseSwitchFixture", "M");
            Assert.NotNull(function);

            IrPasses.Run(function!, IrPasses.Default, PassContext.None);
            function!.CheckInvariant();

            Assert.Empty(function.Descendants.OfType<Branch>());
            Assert.NotEmpty(function.Descendants.OfType<IfStatement>());
            Assert.Contains("throw new IndexOutOfRangeException();", CSharpPrinter.Print(function).Output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SwitchTargetInsideArm_StaysFlat()
    {
        // Same diamond, but a leading (unraised) switch also dispatches into the
        // true-arm head (b2 @ 24). Wrapping b2 in the else arm would strand the
        // switch's `goto IL_0018` inside the braces (CS0159); the pass must keep
        // the whole container flat rather than raise the diamond.
        var sw = new Block(0);
        sw.Add(new SwitchBranch(new LoadArgument(0, "a", Int32), [24]));   // case 0 -> trueArm head
        var b1 = new Block(8);
        b1.Add(new ConditionalBranch(Cond(), 24));      // diamond conditional -> trueArm
        var b2 = new Block(16);                          // false arm
        b2.Add(new Branch(32));                          // -> join
        var b3 = new Block(24);                          // true arm head, ALSO a switch target
        b3.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        var b4 = new Block(32);                          // join
        b4.Add(new Return(new LoadLocal(0, Int32)));

        var function = Structured([sw, b1, b2, b3, b4]);

        // Declined: no diamond raised, so the switch target keeps a container-level
        // label and the SwitchBranch survives at top level (legal gotos).
        Assert.Empty(function.Descendants.OfType<IfStatement>());
        Assert.NotEmpty(function.Descendants.OfType<SwitchBranch>());
    }

    [Fact]
    public void LeaveTargetInsideArm_StaysFlat()
    {
        // #1551: the same scope violation as SwitchTargetInsideArm, but the outside
        // transfer is a surviving Leave instead of a SwitchBranch. A leading leave
        // also targets the true-arm head (b3 @ 24); wrapping b3 in the else arm
        // would strand the leave's `goto IL_0018; // leave` inside the braces
        // (CS0159), so the pass must keep the whole container flat.
        var lv = new Block(0);
        lv.Add(new Leave(24));                          // surviving leave -> trueArm head
        var b1 = new Block(8);
        b1.Add(new ConditionalBranch(Cond(), 24));      // diamond conditional -> trueArm
        var b2 = new Block(16);                          // false arm
        b2.Add(new Branch(32));                          // -> join
        var b3 = new Block(24);                          // true arm head, ALSO a leave target
        b3.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        var b4 = new Block(32);                          // join
        b4.Add(new Return(new LoadLocal(0, Int32)));

        var function = Structured([lv, b1, b2, b3, b4]);

        // Declined: no diamond raised, so the leave target keeps a container-level
        // label and the Leave survives at top level (legal goto).
        Assert.Empty(function.Descendants.OfType<IfStatement>());
        Assert.NotEmpty(function.Descendants.OfType<Leave>());
    }

    static Block Block(int offset, params IrNode[] statements)
    {
        var block = new Block(offset);
        foreach (var statement in statements)
            block.Add(statement);
        return block;
    }

    [Fact]
    public void LeaveTargetInsideArmFromNestedEh_StaysFlat()
    {
        // #1551 (adversarial review): the surviving Leave into the true-arm head is
        // nested inside a TryCatch in an out-of-range block. EhStructuringPass runs
        // before StructuringPass, so a leave can already sit inside an EH shell; the
        // scope guard must scan descendants, not just direct children, or it strands
        // the nested leave's `goto IL_0018; // leave` inside the arm braces (CS0159).
        var tryInner = new Block(0);
        tryInner.Add(new Leave(24));                    // leave into trueArm head, nested in try
        var tryBody = new BlockContainer();
        tryBody.Add(tryInner);
        var catchInner = new Block(4);
        catchInner.Add(new Leave(8));                   // catch exits the region to b1
        var catchBody = new BlockContainer();
        catchBody.Add(catchInner);
        var eh = new Block(0);
        eh.Add(new TryCatch(tryBody, [new CatchClause(TypeRef.CoreLib("System", "Exception"), catchBody)]));
        var b1 = new Block(8);
        b1.Add(new ConditionalBranch(Cond(), 24));      // diamond conditional -> trueArm
        var b2 = new Block(16);                          // false arm
        b2.Add(new Branch(32));                          // -> join
        var b3 = new Block(24);                          // true arm head, ALSO a leave target
        b3.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        var b4 = new Block(32);                          // join
        b4.Add(new Return(new LoadLocal(0, Int32)));

        var function = Structured([eh, b1, b2, b3, b4]);

        Assert.Empty(function.Descendants.OfType<IfStatement>());
        Assert.NotEmpty(function.Descendants.OfType<Leave>());
    }

    [Fact]
    public void RegionExitLeaveInsideNonTailLoop_DoesNotBecomeBreak()
    {
        // A leave to the enclosing EH continuation can be raised to a loop break
        // only when the loop's normal exit reaches that continuation. If statements
        // follow the loop in the try body, `break` would run them while the leave
        // skips them, so the retry leave must stay explicit.
        var tryBody = new BlockContainer();
        var enter = new Block(0);
        enter.Add(new Branch(24));                       // guarded while preheader -> condition
        var catchBlock = new Block(8);
        catchBlock.Add(new Leave(64));                   // exits enclosing finally, not the loop
        var catchBody = new BlockContainer();
        catchBody.Add(catchBlock);
        var body = new Block(8);
        body.Add(new TryCatch(new BlockContainer(), [new CatchClause(TypeRef.CoreLib("System", "Exception"), catchBody)]));
        var condition = new Block(24);
        condition.Add(new ConditionalBranch(Cond(), 8)); // while condition -> body
        var afterLoop = new Block(32);
        afterLoop.Add(new StoreLocal(0, Int32, new Constant(42, Int32)));
        foreach (var block in (Block[])[enter, body, condition, afterLoop])
            tryBody.Add(block);

        var finallyBody = new BlockContainer();
        var finallyBlock = new Block(48);
        finallyBlock.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        finallyBody.Add(finallyBlock);

        var root = new BlockContainer();
        var holder = new Block(0);
        holder.Add(new TryFinally(tryBody, finallyBody));
        var tail = new Block(64);
        tail.Add(new Return(new LoadLocal(0, Int32)));
        root.Add(holder);
        root.Add(tail);
        var function = new IrFunction("M", Owner, new MethodSignature(Int32, [new Parameter("a", Int32)], HasThis: false, GenericParameterCount: 0), [Int32], root);

        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Descendants.OfType<Leave>(), leave => leave.TargetOffset == 64);
        Assert.Empty(function.Descendants.OfType<Break>());
        string output = CSharpPrinter.Print(function).Output ?? "";
        Assert.DoesNotContain("goto IL_0018;", output);
        Assert.DoesNotContain("IL_0018:", output);
    }

    [Theory]
    [InlineData(0x0077, 0x0062, false)]
    [InlineData(0x005E, 0x0062, false)]
    [InlineData(0x005E, 0x0015, false)]
    [InlineData(0x005E, 0x0015, true)]
    [InlineData(0x005E, 0x0013, false)]
    public void RegionExitLeaveWithDescendantBodyEntry_DoesNotBecomeBreak(
        int targetOffset,
        int sourceOffset,
        bool useLeave)
    {
        var function = CollectValidDoublesBeforeStructuring();

        var tryFinally = Assert.Single(function.Descendants.OfType<TryFinally>());
        var sourceBlock = Assert.Single(
            tryFinally.TryBody.Blocks,
            block => block.StartOffset == sourceOffset);
        var alternateExitArm = new Block();
        alternateExitArm.Add(useLeave
            ? new Leave(targetOffset)
            : new Branch(targetOffset));
        var alternateExit = new IfStatement(
            new Comparison(
                ComparisonKind.Equal,
                isUnsigned: false,
                new LoadLocal(2, Int32),
                new Constant(0, Int32)),
            alternateExitArm,
            elseArm: null);
        if (sourceBlock.Children[^1] is ConditionalBranch terminator)
        {
            terminator.Detach();
            sourceBlock.Add(alternateExit);
            sourceBlock.Add(terminator);
        }
        else
        {
            sourceBlock.Add(alternateExit);
        }

        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        string output = CSharpPrinter.Print(function).Output ?? "";
        Assert.True(function.Descendants.OfType<Leave>().Any(leave => leave.TargetOffset == 0x0087), output);
        Assert.Empty(function.Descendants.OfType<Break>());
    }

    [Fact]
    public void RegionExitLeaveWithSideEffectingTail_DoesNotBecomeBreak()
    {
        var function = CollectValidDoublesBeforeStructuring();
        var tryFinally = Assert.Single(function.Descendants.OfType<TryFinally>());
        var tail = Assert.Single(
            tryFinally.TryBody.Blocks,
            block => block.StartOffset == 0x0077);
        Assert.Empty(tail.Children);
        tail.Add(new StoreLocal(2, Int32, new Constant(0, Int32)));
        tail.Add(new Leave(0x0087));

        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        string output = CSharpPrinter.Print(function).Output ?? "";
        Assert.True(function.Descendants.OfType<Leave>().Any(leave => leave.TargetOffset == 0x0087), output);
        Assert.Empty(function.Descendants.OfType<Break>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegionExitLeaveWithPreheaderEntryToDirectTarget_DoesNotBecomeBreak(
        bool useSwitch)
    {
        var function = CollectValidDoublesBeforeStructuring();
        var tryFinally = Assert.Single(function.Descendants.OfType<TryFinally>());
        var lastGuard = Assert.Single(
            tryFinally.TryBody.Blocks,
            block => block.StartOffset == 0x003C);
        var conditional = Assert.IsType<ConditionalBranch>(lastGuard.Children[^1]);
        var replacement = new ConditionalBranch(
            (IrExpression)conditional.Condition.Clone(),
            0x006E);
        replacement.InheritSourceOffset(conditional);
        conditional.ReplaceWith(replacement);

        if (useSwitch)
        {
            var blocks = tryFinally.TryBody.Blocks.ToList();
            foreach (var block in blocks)
                block.Detach();
            var dispatch = new Block(0x0010);
            dispatch.Add(new SwitchBranch(new LoadLocal(2, Int32), [0x005E]));
            tryFinally.TryBody.Add(dispatch);
            foreach (var block in blocks)
                tryFinally.TryBody.Add(block);
        }
        else
        {
            var preheader = Assert.Single(
                tryFinally.TryBody.Blocks,
                block => block.StartOffset == 0x0013);
            var enterLoop = Assert.IsType<Branch>(preheader.Children[^1]);
            enterLoop.Detach();
            var alternateEntryArm = new Block();
            alternateEntryArm.Add(new Branch(0x005E));
            preheader.Add(new IfStatement(
                new Comparison(
                    ComparisonKind.Equal,
                    isUnsigned: false,
                    new LoadLocal(2, Int32),
                    new Constant(0, Int32)),
                alternateEntryArm,
                elseArm: null));
            preheader.Add(enterLoop);
        }

        new StructuringPass().Run(function, PassContext.None);
        function.CheckInvariant();

        string output = CSharpPrinter.Print(function).Output ?? "";
        Assert.True(function.Descendants.OfType<Leave>().Any(leave => leave.TargetOffset == 0x0087), output);
        Assert.Empty(function.Descendants.OfType<Break>());
    }

    static IrFunction CollectValidDoublesBeforeStructuring()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(CfgSampleClass).FullName!,
            nameof(CfgSampleClass.CollectValidDoubles))!;
        foreach (var pass in IrPasses.Default)
        {
            if (pass is StructuringPass)
                break;
            pass.Run(function, PassContext.None);
        }
        return function;
    }

    [Fact]
    public void ClonedSharedTail_DoesNotStealCanonicalGotoLabel()
    {
        var b0 = new Block(0x00);
        b0.Add(new ConditionalBranch(Cond(), 0x18));
        var b1 = new Block(0x08);
        b1.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        b1.Add(new Branch(0x40));
        var b2 = new Block(0x18);
        b2.Add(new ConditionalBranch(Cond(), 0x30));
        var b3 = new Block(0x20);
        b3.Add(new StoreLocal(0, Int32, new Constant(2, Int32)));
        b3.Add(new Branch(0x40));
        var b4 = new Block(0x30);
        var survivingGoto = new Block();
        survivingGoto.Add(new Branch(0x40));
        b4.Add(new IfStatement(Cond(), survivingGoto, elseArm: null));
        b4.Add(new StoreLocal(0, Int32, new Constant(3, Int32)));
        var b5 = new Block(0x40);
        b5.Add(new Return(new LoadLocal(0, Int32)));

        var function = Structured([b0, b1, b2, b3, b4, b5]);
        string output = CSharpPrinter.Print(function).Output ?? "";

        int label = output.IndexOf("IL_0040:", StringComparison.Ordinal);
        int finalGoto = output.LastIndexOf("goto IL_0040;", StringComparison.Ordinal);
        Assert.True(finalGoto >= 0, output);
        Assert.True(label > finalGoto, output);
    }

    [Fact]
    public void RetainedRegion_DoesNotAppendAlternateArmAfterTerminalBranch()
    {
        var b0 = new Block(0x00);
        b0.Add(new ConditionalBranch(Cond(), 0x54));
        var b1 = new Block(0x27);
        b1.Add(new ConditionalBranch(Cond(), 0x51));
        var b2 = new Block(0x42);
        b2.Add(new StoreLocal(0, Int32, new Constant(2, Int32)));
        b2.Add(new Branch(0x55));
        var b3 = new Block(0x51);
        b3.Add(new StoreLocal(0, Int32, new Constant(0, Int32)));
        b3.Add(new Branch(0x55));
        var b4 = new Block(0x54);
        b4.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        var b5 = new Block(0x55);
        b5.Add(new Return(new LoadLocal(0, Int32)));

        var function = Structured([b0, b1, b2, b3, b4, b5]);

        Assert.All(
            function.Descendants.OfType<Block>(),
            block => Assert.DoesNotContain(
                block.Children.Take(Math.Max(0, block.Children.Count - 1)),
                child => child is Branch or Leave or Return or Throw
                    or Break or Continue or EndFinally or EndFilter));
    }

    [Fact]
    public void CompilerSharedTailWithNestedLeave_RemainsFullyStructured()
    {
        using var source = MetadataSource.Open(typeof(StructuringGotoScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(StructuringGotoScopeFixtures).FullName!,
            nameof(StructuringGotoScopeFixtures.SharedTailWithNestedLeave))!;

        IrPasses.Run(function);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<Branch>());
        Assert.Empty(function.Descendants.OfType<Leave>());

        string output = CSharpPrinter.Print(function).Output ?? "";
        Assert.DoesNotContain("goto IL_", output);
        Assert.DoesNotContain("IL_00", output);
    }
}

static class StructuringGotoScopeFixtures
{
    static int _lastValue;

    public static int SharedTailWithNestedLeave(
        bool first,
        bool second,
        bool leaveTry)
    {
        int value = 0;
        if (first)
        {
            value = 1;
            goto Join;
        }
        if (second)
        {
            value = 2;
            goto Join;
        }
        try
        {
            if (leaveTry)
                goto Join;
            value = 3;
        }
        finally
        {
            _lastValue = value;
        }

    Join:
        _lastValue = value;
        value += 4;
        _lastValue = value;
        value -= 4;
        return value;
    }
}
