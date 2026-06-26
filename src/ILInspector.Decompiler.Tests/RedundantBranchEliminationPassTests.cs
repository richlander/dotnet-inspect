using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class RedundantBranchEliminationPassTests
{
    static readonly TypeRef s_bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_object = TypeRef.CoreLib("System", "Object");

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
        return new IrFunction("M", s_object, signature, [], container);
    }

    static Comparison PureCondition()
    {
        return new Comparison(ComparisonKind.Equal, isUnsigned: false, new Constant(1, s_int), new Constant(0, s_int));
    }

    static void AssertKeepsConditional(IrExpression condition)
    {
        var function = TwoBlocks(new ConditionalBranch(condition, targetOffset: 8));

        new RedundantBranchEliminationPass().Run(function, PassContext.None);

        Assert.IsType<ConditionalBranch>(Assert.Single(function.Body.Blocks[0].Children));
        function.CheckInvariant();
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
        var call = new Call(new MethodRef(s_object, "Probe", s_bool, [], HasThis: false), isVirtual: false, []);
        var function = TwoBlocks(new ConditionalBranch(call, targetOffset: 8));

        new RedundantBranchEliminationPass().Run(function, PassContext.None);

        Assert.IsType<ConditionalBranch>(Assert.Single(function.Body.Blocks[0].Children));
    }

    [Fact]
    public void KeepsConditionalBranchToFallthrough_WhenConditionReadsStaticField()
    {
        // Even a redundant conditional branch must preserve an ldsfld condition:
        // reading a static field can trigger the declaring type's .cctor.
        var holder = TypeRef.Definition("SyntheticAssembly", "Samples", "Holder");
        var field = new FieldRef(holder, "Gate", s_bool);
        var function = TwoBlocks(new ConditionalBranch(new LoadField(field, instance: null), targetOffset: 8));

        new RedundantBranchEliminationPass().Run(function, PassContext.None);

        Assert.IsType<ConditionalBranch>(Assert.Single(function.Body.Blocks[0].Children));
    }

    [Fact]
    public void KeepsConditionalBranchToFallthrough_WhenConditionReadsStaticFieldAddress()
    {
        // ldsflda Holder::Gate; ldind.u1; brtrue.s next — taking a static field's address
        // also triggers the declaring type's .cctor, so this redundant branch must stay too.
        var holder = TypeRef.Definition("SyntheticAssembly", "Samples", "Holder");
        var field = new FieldRef(holder, "Gate", s_bool);
        var condition = new LoadIndirect(s_bool, new LoadFieldAddress(field, instance: null));
        var function = TwoBlocks(new ConditionalBranch(condition, targetOffset: 8));

        new RedundantBranchEliminationPass().Run(function, PassContext.None);

        Assert.IsType<ConditionalBranch>(Assert.Single(function.Body.Blocks[0].Children));
    }

    [Fact]
    public void RemovesConditionalBranchToFallthrough_WhenConditionUsesPureArithmetic()
    {
        var condition = new Comparison(
            ComparisonKind.GreaterThan,
            isUnsigned: false,
            new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, new LoadArgument(0, "x", s_int), new Constant(1, s_int)),
            new Constant(0, s_int));
        var function = TwoBlocks(new ConditionalBranch(condition, targetOffset: 8));

        new RedundantBranchEliminationPass().Run(function, PassContext.None);

        Assert.Empty(function.Body.Blocks[0].Children);
        function.CheckInvariant();
    }

    [Fact]
    public void KeepsConditionalBranchToFallthrough_WhenConditionLoadsArrayElement()
    {
        var array = new LoadArgument(0, "items", TypeRef.SzArray(s_int));
        var index = new LoadArgument(1, "index", s_int);
        var condition = new Comparison(
            ComparisonKind.GreaterThan,
            isUnsigned: false,
            new LoadElement(s_int, array, index),
            new Constant(0, s_int));

        AssertKeepsConditional(condition);
    }

    [Fact]
    public void KeepsConditionalBranchToFallthrough_WhenConditionDivides()
    {
        var condition = new Comparison(
            ComparisonKind.GreaterThan,
            isUnsigned: false,
            new Binary(BinaryKind.Divide, isChecked: false, isUnsigned: false, new LoadArgument(0, "x", s_int), new LoadArgument(1, "y", s_int)),
            new Constant(0, s_int));

        AssertKeepsConditional(condition);
    }

    [Fact]
    public void KeepsConditionalBranchToFallthrough_WhenConditionUsesCheckedConversion()
    {
        var condition = new Comparison(
            ComparisonKind.GreaterThan,
            isUnsigned: false,
            new ILInspector.Decompiler.Pipeline.Convert(
                s_int,
                isChecked: true,
                isUnsigned: false,
                new LoadArgument(0, "x", TypeRef.CoreLib("System", "Int64"))),
            new Constant(0, s_int));

        AssertKeepsConditional(condition);
    }

    [Fact]
    public void KeepsConditionalBranchToFallthrough_WhenConditionCastsOrUnboxes()
    {
        AssertKeepsConditional(new CastClass(s_object, new LoadArgument(0, "value", s_object)));
        AssertKeepsConditional(new UnboxAny(s_int, new LoadArgument(0, "value", s_object)));
    }

    [Fact]
    public void KeepsConditionalBranchToFallthrough_WhenConditionReadsInstanceField()
    {
        var holder = TypeRef.Definition("SyntheticAssembly", "Samples", "Holder");
        var field = new FieldRef(holder, "Gate", s_bool);

        AssertKeepsConditional(new LoadField(field, new LoadArgument(0, "holder", holder)));
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
