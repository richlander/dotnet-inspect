using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Regression for issue #4394: a loop-exit branch to a terminator block that
/// <see cref="StructuringPass"/> also treats as an inlinable, droppable
/// terminator (a scattered-dispatch <c>return</c> reached by two or more
/// conditional guards) must clone that terminator rather than convert to a
/// bare <c>break;</c> — otherwise the terminal block is dropped entirely and
/// the printed non-void method falls off the end (<c>CS0161</c>).
/// </summary>
public class LoopExitTerminalTargetTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    /// <summary>
    /// The five-block reproducer from #4394:
    /// <code>
    /// 0: V0 = 100; if (in1) goto 4
    /// 1: V0 = 101; return V0
    /// 2: V0 = 102; if (in1) goto 4
    /// 3: V0 = 103; goto 0
    /// 4: V0 = 104; return V0
    /// </code>
    /// Block 4 is reached only by the two conditional branches (never fallen
    /// into), so it qualifies as a scattered-dispatch terminator the structurer
    /// may clone into each guard. It is also the enclosing loop's exit target.
    /// Both paths must still reach <c>return 104</c>.
    /// </summary>
    static IrFunction LoopExitTerminalTargetFunction()
    {
        LoadArgument In1() => new(0, "in1", Int32);

        var container = new BlockContainer();

        var b0 = new Block(0);
        b0.Add(new StoreLocal(0, Int32, new Constant(100, Int32)));
        b0.Add(new ConditionalBranch(new Comparison(ComparisonKind.Equal, false, In1(), new Constant(1, Int32)), 32));

        var b1 = new Block(8);
        b1.Add(new StoreLocal(0, Int32, new Constant(101, Int32)));
        b1.Add(new Return(new LoadLocal(0, Int32)));

        var b2 = new Block(16);
        b2.Add(new StoreLocal(0, Int32, new Constant(102, Int32)));
        b2.Add(new ConditionalBranch(new Comparison(ComparisonKind.Equal, false, In1(), new Constant(1, Int32)), 32));

        var b3 = new Block(24);
        b3.Add(new StoreLocal(0, Int32, new Constant(103, Int32)));
        b3.Add(new Branch(0));

        var b4 = new Block(32);
        b4.Add(new StoreLocal(0, Int32, new Constant(104, Int32)));
        b4.Add(new Return(new LoadLocal(0, Int32)));

        foreach (var block in (Block[])[b0, b1, b2, b3, b4])
            container.Add(block);

        var signature = new MethodSignature(
            Int32,
            [new Parameter("in1", Int32)],
            HasThis: false,
            GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [Int32], container);
    }

    [Fact]
    public void LoopExitTerminalTarget_PreservesBothReturnPaths()
    {
        var function = LoopExitTerminalTargetFunction();
        IrPasses.Run(function);
        function.CheckInvariant();

        var output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");

        // Block 4 is targeted by two conditional guards (block 0's, which is
        // reachable from entry, and block 2's, which is only reached by
        // looping back through block 3 -- never on the `in1 == 1` path).
        // Asserting "104" appears anywhere is not enough: an implementation
        // could clone the terminator only into block 2's unreachable guard,
        // convert block 0's branch to a bare `break;`, and still satisfy that
        // assertion while reproducing the original bug on the reachable path.
        // Require both guards to have been cloned (two occurrences of "104")
        // and require no `break;` to remain (a `break;` here means a guard's
        // clone was skipped and its content silently dropped).
        Assert.Equal(2, CountOccurrences(output, "104"));
        Assert.Contains("101", output);
        Assert.DoesNotContain("break;", output);
    }

    static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
