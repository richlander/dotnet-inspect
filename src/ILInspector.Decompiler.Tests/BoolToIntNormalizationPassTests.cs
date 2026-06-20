using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

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
}
