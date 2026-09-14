using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class BoolToIntNormalizationPassTests
{
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef UInt32 = TypeRef.CoreLib("System", "UInt32");

    static IrFunction StoreOf(IrExpression value)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreLocal(0, Int32, value));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("System", "Object"), signature, [], container);
    }

    static StoreLocal RunAndGetStore(IrFunction function)
    {
        new BoolToIntNormalizationPass().Run(function, PassContext.None);
        return (StoreLocal)function.Body.Blocks[0].Children[0];
    }

    [Fact]
    public void BoolComparedGreaterThanZeroUnsigned_RaisesToConditional()
    {
        // `int V = bToUpper > false` — the cgt.un(bool, 0) bool→int marshalling
        // idiom. Left flat it is CS0019; raised it is `bToUpper ? 1 : 0`.
        var comparison = new Comparison(
            ComparisonKind.GreaterThan, isUnsigned: true,
            new LoadArgument(0, "bToUpper", Bool),
            new Constant(false, Bool));

        var store = RunAndGetStore(StoreOf(comparison));

        var conditional = Assert.IsType<Conditional>(store.Value);
        Assert.IsType<LoadArgument>(conditional.Condition);
        Assert.Equal(1, Assert.IsType<Constant>(conditional.WhenTrue).Value);
        Assert.Equal(0, Assert.IsType<Constant>(conditional.WhenFalse).Value);
    }

    [Fact]
    public void NonBoolComparedGreaterThanZeroUnsigned_IsLeftAsComparison()
    {
        // cgt.un(x, 0) on a non-bool x is a genuine unsigned `x != 0` test
        // (a null/zero check), not a bool→int normalization — it must survive.
        var comparison = new Comparison(
            ComparisonKind.GreaterThan, isUnsigned: true,
            new LoadArgument(0, "count", UInt32),
            new Constant(0, Int32));

        var store = RunAndGetStore(StoreOf(comparison));

        Assert.IsType<Comparison>(store.Value);
    }

    [Fact]
    public void SignedBoolComparison_IsLeftAsComparison()
    {
        // Only the unsigned form is the normalization idiom; a signed comparison
        // is not the shape the compiler emits for bool→int and is left alone.
        var comparison = new Comparison(
            ComparisonKind.GreaterThan, isUnsigned: false,
            new LoadArgument(0, "flag", Bool),
            new Constant(false, Bool));

        var store = RunAndGetStore(StoreOf(comparison));

        Assert.IsType<Comparison>(store.Value);
    }

    [Fact]
    public void StackSlotNormalization_UpdatesLoadsToTheMaterializedIntType()
    {
        var comparison = new Comparison(
            ComparisonKind.GreaterThan,
            isUnsigned: true,
            new LoadArgument(0, "flag", Bool),
            new Constant(false, Bool));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(
            StoreStackSlot.DupSlotBase,
            comparison));
        block.Add(new Return(new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new LoadStackSlot(StoreStackSlot.DupSlotBase, Bool),
            new Constant(1, Int32))));
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("System", "Object"),
            new MethodSignature(
                Int32,
                [new Parameter("flag", Bool)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container);

        new BoolToIntNormalizationPass().Run(
            function,
            PassContext.None);
        new ArithmeticBoolOperandPass().Run(
            function,
            PassContext.None);

        var store = Assert.IsType<StoreStackSlot>(block.Children[0]);
        Assert.IsType<Conditional>(store.Value);
        var returned = Assert.IsType<Return>(block.Children[1]);
        var add = Assert.IsType<Binary>(returned.Value);
        var load = Assert.IsType<LoadStackSlot>(add.Left);
        Assert.Equal(Int32, load.ResultType);
    }

    [Fact]
    public void StackSlotNormalization_DoesNotRetypeMixedJoinStores()
    {
        var comparison = new Comparison(
            ComparisonKind.GreaterThan,
            isUnsigned: true,
            new LoadArgument(0, "flag", Bool),
            new Constant(false, Bool));
        var container = new BlockContainer();
        var first = new Block(0);
        first.Add(new StoreStackSlot(0, comparison));
        container.Add(first);
        var second = new Block(1);
        second.Add(new StoreStackSlot(
            0,
            new LoadArgument(0, "flag", Bool)));
        second.Add(new Return(new LoadStackSlot(0, Bool)));
        container.Add(second);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("System", "Object"),
            new MethodSignature(
                Bool,
                [new Parameter("flag", Bool)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container);

        new BoolToIntNormalizationPass().Run(
            function,
            PassContext.None);

        var load = Assert.Single(
            function.Descendants.OfType<LoadStackSlot>());
        Assert.Equal(Bool, load.ResultType);
    }

    [Fact]
    public void StackSlotNormalization_DoesNotRetypeNestedFunctionSlots()
    {
        var comparison = new Comparison(
            ComparisonKind.GreaterThan,
            isUnsigned: true,
            new LoadArgument(0, "flag", Bool),
            new Constant(false, Bool));
        var lambdaBody = new BlockContainer();
        var lambdaBlock = new Block(0);
        lambdaBlock.Add(new Return(new LoadStackSlot(
            StoreStackSlot.DupSlotBase,
            Bool)));
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(
            TypeRef.Definition(
                "Synthetic",
                "System",
                "Func`1"),
            [],
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody);
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(
            StoreStackSlot.DupSlotBase,
            comparison));
        block.Add(new ExpressionStatement(lambda));
        block.Add(new Return(new LoadStackSlot(
            StoreStackSlot.DupSlotBase,
            Bool)));
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("System", "Object"),
            new MethodSignature(
                Int32,
                [new Parameter("flag", Bool)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container);

        new BoolToIntNormalizationPass().Run(
            function,
            PassContext.None);

        Assert.Equal(
            Bool,
            Assert.Single(
                lambda.Body.Descendants.OfType<LoadStackSlot>())
                .ResultType);
        Assert.Equal(
            Int32,
            Assert.Single(
                function.Body.DescendantsOutsideNestedFunctions
                    .OfType<LoadStackSlot>())
                .ResultType);
    }
}
