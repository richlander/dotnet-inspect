using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using System.Collections.Immutable;

namespace ILInspector.Decompiler.Tests;

// Issue #1479: printer operand positions that bypassed the precedence-wrapping
// Operand helper, reassociating compound operands into invalid/wrong C# at Full.
public class PrinterPrecedenceTests
{
    static readonly TypeRef s_bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");

    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    static string Print(string methodName) => CSharpPrinter.Print(Raised(methodName)).Output!;

    // ---- StoreElement: compound array receiver on an indexed assignment target ----

    [Fact]
    public void StoreElement_CompoundArrayReceiver_StaysParenthesized()
    {
        var output = Print(nameof(CfgSampleClass.ConditionalArrayElementStore));

        // Without the Operand wrap this rendered `flag ? a : b[i] = v;`, which
        // reparses as `flag ? a : (b[i] = v)` (CS0201, and wrong target).
        Assert.Contains("(flag ? a : b)[i] = v;", output);
        Assert.DoesNotContain("flag ? a : b[i] = v;", output);
    }

    [Fact]
    public void StoreElement_CompoundArrayReceiver_RecompilesExactly()
    {
        var result = Assert.Single(
            FidelityCheck.Evaluate(typeof(CfgSampleClass).Assembly.Location),
            r => r.Type == typeof(CfgSampleClass).FullName
                && r.Method == nameof(CfgSampleClass.ConditionalArrayElementStore));

        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
    }

    // ---- Conditional in the condition position of an enclosing ternary ----
    // Not reachable from C# source (structuring lowers the outer ternary to an
    // `if`), so the printer is exercised over a hand-built IR shape.

    [Fact]
    public void Conditional_NestedConditionalCondition_StaysParenthesized()
    {
        // (a ? b : c) ? d : e — `?:` is right-associative, so an unwrapped
        // condition reparses as `a ? b : (c ? d : e)`.
        var inner = new Conditional(
            new LoadArgument(0, "a", s_bool),
            new LoadArgument(1, "b", s_bool),
            new LoadArgument(2, "c", s_bool));
        var outer = new Conditional(inner, new LoadArgument(3, "d", s_int), new LoadArgument(4, "e", s_int));

        var output = PrintReturn(
            outer,
            s_int,
            [
                new Parameter("a", s_bool), new Parameter("b", s_bool), new Parameter("c", s_bool),
                new Parameter("d", s_int), new Parameter("e", s_int),
            ]);

        Assert.Contains("(a ? b : c) ? d : e", output);
        Assert.DoesNotContain("a ? b : c ? d", output);
    }

    static string PrintReturn(IrExpression value, TypeRef returnType, ImmutableArray<Parameter> parameters)
    {
        var block = new Block();
        block.Add(new Return(value));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(returnType, parameters, HasThis: false, GenericParameterCount: 0),
            [],
            body);
        return CSharpPrinter.Print(function).Output!;
    }
}
