using ILInspector.Decompiler.Pipeline;

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
}
