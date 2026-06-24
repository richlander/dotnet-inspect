using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class RedundantBranchEliminationPassTests
{
    static IrFunction TwoBlocks(IrNode block0Terminator)
    {
        var container = new BlockContainer();
        var b0 = new Block(0);
        var b1 = new Block(8);
        container.Add(b0);
        container.Add(b1);
        b0.Add(block0Terminator);   // targets IL_0008 — the immediately following block
        b1.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("System", "Object"), signature, [], container);
    }

    static Comparison PureCondition()
    {
        var i32 = TypeRef.CoreLib("System", "Int32");
        return new Comparison(ComparisonKind.Equal, isUnsigned: false, new Constant(1, i32), new Constant(0, i32));
    }

    [Fact]
    public void RemovesConditionalBranchToFallthrough_WhenConditionIsSideEffectFree()
    {
        // `if (1 == 0) goto IL_0008; IL_0008:` — both arms reach IL_0008, so the
        // dead branch is dropped (the condition has no side effect to preserve).
        var function = TwoBlocks(new ConditionalBranch(PureCondition(), targetOffset: 8));

        new RedundantBranchEliminationPass().Run(function, PassContext.None);

        Assert.Empty(function.Body.Blocks[0].Children);
    }

    [Fact]
    public void KeepsConditionalBranchToFallthrough_WhenConditionHasSideEffect()
    {
        // The branch is still dead, but evaluating a call condition is observable,
        // so the conservative pass leaves it in place rather than elide the call.
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var call = new Call(new MethodRef(TypeRef.CoreLib("System", "Object"), "Probe", boolType, [], HasThis: false), isVirtual: false, []);
        var function = TwoBlocks(new ConditionalBranch(call, targetOffset: 8));

        new RedundantBranchEliminationPass().Run(function, PassContext.None);

        Assert.IsType<ConditionalBranch>(Assert.Single(function.Body.Blocks[0].Children));
    }

    [Fact]
    public void KeepsConditionalBranchToFallthrough_WhenConditionReadsStaticField()
    {
        // Even a redundant conditional branch must preserve an ldsfld condition:
        // reading a static field can trigger the declaring type's .cctor.
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var holder = TypeRef.Definition("SyntheticAssembly", "Samples", "Holder");
        var field = new FieldRef(holder, "Gate", boolType);
        var function = TwoBlocks(new ConditionalBranch(new LoadField(field, instance: null), targetOffset: 8));

        new RedundantBranchEliminationPass().Run(function, PassContext.None);

        Assert.IsType<ConditionalBranch>(Assert.Single(function.Body.Blocks[0].Children));
    }

    [Fact]
    public void RemovesUnconditionalBranchToFallthrough()
    {
        // The pre-existing behavior still holds.
        var function = TwoBlocks(new Branch(8));

        new RedundantBranchEliminationPass().Run(function, PassContext.None);

        Assert.Empty(function.Body.Blocks[0].Children);
    }
}
