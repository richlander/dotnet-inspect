using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// TypedConstantsPass must be total over any IR shape: it retypes the constants it
// understands and leaves the rest, never throwing (issue #1489). A callee that
// reaches the pass with an uninitialized parameter list — possible through an
// unusual reconstruction seam — must not crash `.Length`; a constant argument is
// simply left untyped.
public class TypedConstantsPassRobustnessTests
{
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    static IrFunction FunctionWith(IrNode statement)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(statement);
        var signature = new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Void, signature, [], container);
    }

    [Fact]
    public void CallWithDefaultParameterTypes_DoesNotThrow_AndLeavesConstant()
    {
        // A MethodRef whose ParameterTypes is a default (uninitialized)
        // ImmutableArray — `.Length` on it would throw NullReferenceException.
        var callee = new MethodRef(
            DeclaringType: TypeRef.CoreLib("System", "Object"),
            Name: "Consume",
            ReturnType: Void,
            ParameterTypes: default,
            HasThis: false);
        var argument = new Constant(5, Int32);
        var call = new Call(callee, isVirtual: false, [argument]);

        var function = FunctionWith(new ExpressionStatement(call));

        var ex = Record.Exception(() => new TypedConstantsPass().Run(function, PassContext.None));

        Assert.Null(ex);
        // Nothing to retype against: the constant is left exactly as it was.
        var survivor = Assert.Single(function.Descendants.OfType<Constant>());
        Assert.Equal(5, survivor.Value);
    }
}
