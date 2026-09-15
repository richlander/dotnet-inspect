using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A LogicalNot in VALUE position (a `return`/assign, not a branch condition) over
// a NON-bool operand is the truthiness test C# spells with `is null` / `== 0`,
// not a literal `!operand`. It arises when a `brfalse x; ldc.0/ldc.1` select that
// computes `x == null` folds to `return LogicalNot(x)` (e.g.
// System.Collections.StructuralEqualityComparer.Equals returns `!y` for an
// `object y`). The condition path already spells this; the expression path did
// not, emitting `!y` — CS0023 (`!` is not defined on `object`). csc emits the
// folded shape from its own structuring, so it is constructed at the IR level.
public class LogicalNotValuePositionTests
{
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");

    // bool M(<argType> a) => return !a;   (a LogicalNot of the single argument)
    static string Render(TypeRef argType)
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(new LogicalNot(new LoadArgument(0, "a", argType))));
        body.Add(block);
        var function = new IrFunction(
            "M", owner,
            new MethodSignature(Bool, [new Parameter("a", argType)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
        return CSharpPrinter.Print(function).Output!;
    }

    [Fact]
    public void ReferenceOperand_SpellsIsNull_NotBang()
    {
        var output = Render(Object);

        // `!a` on an object is CS0023; the faithful truthiness form is `a is null`.
        Assert.Contains("return a is null;", output);
        Assert.DoesNotContain("!a", output);
    }

    [Fact]
    public void IntegerOperand_SpellsEqualsZero_NotBang()
    {
        var output = Render(Int);

        // `!a` on an int is CS0023; the truthiness form is `a == 0`.
        Assert.Contains("return a == 0;", output);
        Assert.DoesNotContain("!a", output);
    }

    [Fact]
    public void BooleanOperand_StaysBang()
    {
        var output = Render(Bool);

        // A genuine bool operand is its own truth value — the bare `!a` is correct
        // and must be preserved (Truthiness returns null for bool).
        Assert.Contains("return !a;", output);
    }
}
