using System.Collections.Immutable;

using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class BooleanFoldingSourceOffsetTests
{
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");

    [Fact]
    public void GuardReturnFold_PreservesSourceOffset()
    {
        var then = new Block(0);
        then.Add(new Return(new Constant(true, Boolean)));
        var guard = new IfStatement(new LoadArgument(0, "c", Boolean), then, null);
        guard.SetSourceOffset(0x10);

        var block = new Block(0);
        block.Add(guard);
        block.Add(new Return(new Constant(false, Boolean)));
        var function = Function(block, Boolean, [new Parameter("c", Boolean)]);

        new BooleanFoldingPass().Run(function, PassContext.None);

        var folded = Assert.IsType<Return>(Assert.Single(block.Children));
        Assert.Equal(0x10, folded.SourceOffset);
    }

    [Fact]
    public void TernaryReturnFold_PreservesSourceOffset()
    {
        var then = new Block(0);
        then.Add(new Return(new Constant(1, Int32)));
        var guard = new IfStatement(
            new Comparison(
                ComparisonKind.GreaterThan,
                isUnsigned: false,
                new LoadArgument(0, "x", Int32),
                new Constant(0, Int32)),
            then,
            null);
        guard.SetSourceOffset(0x20);

        var block = new Block(0);
        block.Add(guard);
        block.Add(new Return(new Constant(2, Int32)));
        var function = Function(block, Int32, [new Parameter("x", Int32)]);

        new BooleanFoldingPass().Run(function, PassContext.None);

        var folded = Assert.IsType<Return>(Assert.Single(block.Children));
        Assert.Equal(0x20, folded.SourceOffset);
    }

    static IrFunction Function(
        Block block,
        TypeRef returnType,
        ImmutableArray<Parameter> parameters)
    {
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(returnType, parameters, HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
