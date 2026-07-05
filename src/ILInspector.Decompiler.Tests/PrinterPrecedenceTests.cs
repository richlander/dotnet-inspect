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
    static readonly TypeRef s_uint = TypeRef.CoreLib("System", "UInt32");

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
    [Trait("Speed", "Slow")]
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

    // Issue #2302: an arm whose signedness (or width) disagrees with the numeric
    // join renders CS0266 bare (`flag ? s : u` with `u` a uint at an int join).
    // The join spells the faithful same-stack-family reinterpretation cast on the
    // disagreeing arm; the agreeing arm and implicit widenings stay bare.
    [Fact]
    public void Conditional_CrossSignednessArm_CastsToJoinType()
    {
        var conditional = new Conditional(
            new LoadArgument(0, "flag", s_bool),
            new LoadArgument(1, "s", s_int),
            new LoadArgument(2, "u", s_uint))
        {
            MergedType = s_int,
        };

        var output = PrintReturn(
            conditional,
            s_int,
            [new Parameter("flag", s_bool), new Parameter("s", s_int), new Parameter("u", s_uint)]);

        // The int arm stays bare; the uint arm takes the reinterpretation cast.
        Assert.Contains("flag ? s : (int)u", output);
        Assert.DoesNotContain(": u;", output);
    }

    // Issue #2302: a same-width cross-signedness *constant* arm keeps the
    // target-aware spelling — in-range bare, out-of-range unchecked reinterpret —
    // so a negative int constant at a uint join is `unchecked((uint)(-1))`, not
    // the bare `-1` that is CS0173.
    [Fact]
    public void Conditional_OutOfRangeConstantArm_UncheckedReinterpretsToJoin()
    {
        var conditional = new Conditional(
            new LoadArgument(0, "flag", s_bool),
            new LoadArgument(1, "u", s_uint),
            new Constant(-1, s_int))
        {
            MergedType = s_uint,
        };

        var output = PrintReturn(
            conditional,
            s_uint,
            [new Parameter("flag", s_bool), new Parameter("u", s_uint)]);

        Assert.Contains("unchecked((uint)(-1))", output);
        Assert.DoesNotContain(": -1", output);
    }

    // Issue #2302 (Gemini review): the cast is only spelled for a *same-width*
    // sibling. A differing-width join (int arm at a short join) is a narrowing the
    // printer must not silently introduce, so no truncating `(short)` cast appears.
    [Fact]
    public void Conditional_DifferingWidthArm_DoesNotEmitNarrowingCast()
    {
        var s_short = TypeRef.CoreLib("System", "Int16");
        var conditional = new Conditional(
            new LoadArgument(0, "flag", s_bool),
            new LoadArgument(1, "s", s_short),
            new LoadArgument(2, "i", s_int))
        {
            MergedType = s_short,
        };

        var output = PrintReturn(
            conditional,
            s_short,
            [new Parameter("flag", s_bool), new Parameter("s", s_short), new Parameter("i", s_int)]);

        Assert.DoesNotContain("(short)i", output);
    }

    // Issue #2151: a bare negative constant as a member-access receiver misbinds —
    // `-1.Twice()` parses as `-(1.Twice())` (CS0023). Operand treats a Constant as
    // an atom, so the receiver must be parenthesized: `(-1).Twice()`.
    [Fact]
    public void ExtensionCall_OnNegativeConstantReceiver_ParenthesizesReceiver()
    {
        var callee = new MethodRef(
            TypeRef.Definition("Asm", "Ns", "Ext"), "Twice", s_int, [s_int], HasThis: false)
        {
            IsExtension = MetadataFactState.Yes,
        };
        var call = new Call(callee, isVirtual: false, [new Constant(-1, s_int)]);

        var output = PrintReturn(call, s_int, []);

        Assert.Contains("(-1).Twice()", output);
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
