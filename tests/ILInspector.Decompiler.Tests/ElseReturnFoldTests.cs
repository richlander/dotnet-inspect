using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A boolean materialized through an if/else over a temp and then returned —
// `if (c) { v = true; } else { v = false; } return v;` — must stay in that
// accumulator form. Collapsing it to `return c;` is semantically equivalent but
// does not round-trip opcode-exact for pattern-lowered bool returns (#1047).
public class ElseReturnFoldTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");

    [Fact]
    public void IfElseBoolStoreReturn_StaysAccumulatorForm()
    {
        var output = Fold(thenValue: true, elseValue: false);

        Assert.Contains("if (c)", output);
        Assert.Contains("V_0 = true;", output);
        Assert.Contains("V_0 = false;", output);
        Assert.Contains("return V_0;", output);
        Assert.DoesNotContain("return c;", output);
    }

    [Fact]
    public void IfElseBoolStoreReturn_InvertedArms_StaysAccumulatorForm()
    {
        var output = Fold(thenValue: false, elseValue: true);

        Assert.Contains("if (c)", output);
        Assert.Contains("V_0 = false;", output);
        Assert.Contains("V_0 = true;", output);
        Assert.Contains("return V_0;", output);
        Assert.DoesNotContain("return !c;", output);
    }

    [Fact]
    public void IfElseBoolStoreReturn_TempReadElsewhere_StaysStatementForm()
    {
        // A second read of the temp means it is a real local, not the boolean's
        // materialization, so the fold must decline.
        var block = new Block(0);
        block.Add(Diamond(thenValue: true, elseValue: false));
        block.Add(new ExpressionStatement(new Call(
            new MethodRef(Holder, "Use", TypeRef.CoreLib("System", "Void"), [Bool], HasThis: false),
            isVirtual: false,
            [new LoadLocal(0, Bool)])));
        block.Add(new Return(new LoadLocal(0, Bool)));

        var output = Print(block);

        Assert.Contains("if (c)", output);
        Assert.DoesNotContain("return c;", output);
    }

    static string Fold(bool thenValue, bool elseValue)
    {
        var block = new Block(0);
        block.Add(Diamond(thenValue, elseValue));
        block.Add(new Return(new LoadLocal(0, Bool)));
        return Print(block);
    }

    static IfStatement Diamond(bool thenValue, bool elseValue)
    {
        var then = new Block(0);
        then.Add(new StoreLocal(0, Bool, new Constant(thenValue, Bool)));
        var @else = new Block(0);
        @else.Add(new StoreLocal(0, Bool, new Constant(elseValue, Bool)));
        return new IfStatement(new LoadArgument(0, "c", Bool), then, @else);
    }

    static string Print(Block block)
    {
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M", Holder,
            new MethodSignature(Bool, [new Parameter("c", Bool)], HasThis: false, GenericParameterCount: 0),
            [Bool],
            body);
        new BooleanFoldingPass().Run(function, PassContext.None);
        return CSharpPrinter.Print(function).Output!.Trim();
    }
}
