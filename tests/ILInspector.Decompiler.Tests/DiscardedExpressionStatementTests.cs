using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// C# requires an expression statement to be an invocation, object creation,
// await, or inc/decrement. A bare value left as a statement — a stack slot
// discarded by an IL `pop`, the caught exception, a comparison — is CS0201.
// The printer must spell such a discard with `_ =` (always valid), never as the
// bare `value;`. A genuine statement-expression (a call) stays bare so a
// void-returning call is not turned into the illegal `_ = VoidCall();` (CS0815).
public class DiscardedExpressionStatementTests
{
    static readonly TypeRef Owner = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    static string Print(IrNode statement)
    {
        var container = new BlockContainer();
        var entry = new Block(0x00);
        entry.Add(statement);
        entry.Add(new Return(null));
        container.Add(entry);

        var signature = new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", Owner, signature, [], container);
        return CSharpPrinter.Print(function).Output!;
    }

    [Fact]
    public void DiscardedStackSlot_RendersAsDiscardAssignment()
    {
        // An IL `pop` of a stored slot surfaces as `ExpressionStatement(LoadStackSlot)`.
        var output = Print(new ExpressionStatement(new LoadStackSlot(0, Int32)));

        Assert.Contains("_ = S_0;", output);
    }

    [Fact]
    public void DiscardedComparison_RendersAsDiscardAssignment()
    {
        var comparison = new Comparison(
            ComparisonKind.Equal, isUnsigned: false,
            new LoadStackSlot(0, Int32), new Constant(0, Int32));
        var output = Print(new ExpressionStatement(comparison));

        Assert.Contains("_ = ", output);
        Assert.DoesNotContain("\nS_0 == 0;", output);
    }

    [Fact]
    public void DiscardedVoidCall_StaysBareStatement()
    {
        // A call is a legal statement-expression; wrapping a void call with `_ =`
        // would be CS0815, so it must stay the bare invocation statement.
        var callee = new MethodRef(Owner, "DoWork", Void, [], HasThis: false);
        var output = Print(new ExpressionStatement(new Call(callee, isVirtual: false, [])));

        Assert.Contains("DoWork();", output);
        Assert.DoesNotContain("_ =", output);
    }
}
