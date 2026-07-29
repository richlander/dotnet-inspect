using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace ILInspector.Decompiler.Tests;

// Issue #1479: printer operand positions that bypassed the precedence-wrapping
// Operand helper, reassociating compound operands into invalid/wrong C# at Full.
public class PrinterPrecedenceTests
{
    static readonly TypeRef s_bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_uint = TypeRef.CoreLib("System", "UInt32");
    static readonly TypeRef s_nuint = TypeRef.CoreLib("System", "UIntPtr");
    static readonly TypeRef s_object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef s_string = TypeRef.CoreLib("System", "String");

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
    [Trait("Area", "Fidelity")]
    public void StoreElement_CompoundArrayReceiver_RecompilesExactly()
    {
        var result = Assert.Single(
            FidelityCheck.Evaluate(
                typeof(CfgSampleClass).Assembly.Location,
                type => type == typeof(CfgSampleClass).FullName),
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

    // ---- #3126: right-associative same-kind && / || chains keep their parens ----
    // C# `&&`/`||` are left-associative, so a right-nested same-kind chain
    // `a && (b && c)` must keep its parens: dropping them reparses as the
    // left-nested `(a && b) && c`, which csc lays out with a different branch
    // structure — the reprint would recompile to a divergent opcode stream. (Not
    // reachable as a top-level ternary from C# source — csc folds a constant-arm
    // ternary to `&&`/`||` at compile time — so the printer is exercised over the
    // hand-built right-nested IR the boolean folds reconstruct.)

    [Fact]
    public void LogicalAnd_RightNestedSameKind_StaysParenthesized()
    {
        // a && (b && c) — right-nested `&&`.
        var chain = new LogicalBinary(
            LogicalKind.And,
            new LoadArgument(0, "a", s_bool),
            new LogicalBinary(
                LogicalKind.And,
                new LoadArgument(1, "b", s_bool),
                new LoadArgument(2, "c", s_bool)));

        var output = PrintReturn(
            chain, s_bool,
            [new Parameter("a", s_bool), new Parameter("b", s_bool), new Parameter("c", s_bool)]);

        Assert.Contains("return a && (b && c);", output);
    }

    [Fact]
    public void LogicalOr_RightNestedSameKind_StaysParenthesized()
    {
        // a || (b || c) — right-nested `||`.
        var chain = new LogicalBinary(
            LogicalKind.Or,
            new LoadArgument(0, "a", s_bool),
            new LogicalBinary(
                LogicalKind.Or,
                new LoadArgument(1, "b", s_bool),
                new LoadArgument(2, "c", s_bool)));

        var output = PrintReturn(
            chain, s_bool,
            [new Parameter("a", s_bool), new Parameter("b", s_bool), new Parameter("c", s_bool)]);

        Assert.Contains("return a || (b || c);", output);
    }

    [Fact]
    public void LogicalAnd_LeftNestedSameKind_StaysFlat()
    {
        // (a && b) && c — left-nested `&&`. C# `&&` is left-associative, so this
        // is opcode-identical to the flat `a && b && c` and must NOT gain parens
        // (regression guard against over-parenthesizing the common left-nested
        // chain, which is what most compiled `&&` lowerings produce).
        var chain = new LogicalBinary(
            LogicalKind.And,
            new LogicalBinary(
                LogicalKind.And,
                new LoadArgument(0, "a", s_bool),
                new LoadArgument(1, "b", s_bool)),
            new LoadArgument(2, "c", s_bool));

        var output = PrintReturn(
            chain, s_bool,
            [new Parameter("a", s_bool), new Parameter("b", s_bool), new Parameter("c", s_bool)]);

        Assert.Contains("return a && b && c;", output);
        Assert.DoesNotContain("(a && b)", output);
    }

    [Fact]
    public void LogicalAnd_RightNestedDifferentKind_StaysParenthesized()
    {
        // a && (b || c) — a different-kind chain already parenthesizes on either
        // side (unchanged by #3126); this pins that the right-operand rule did not
        // regress the mixed-kind case.
        var chain = new LogicalBinary(
            LogicalKind.And,
            new LoadArgument(0, "a", s_bool),
            new LogicalBinary(
                LogicalKind.Or,
                new LoadArgument(1, "b", s_bool),
                new LoadArgument(2, "c", s_bool)));

        var output = PrintReturn(
            chain, s_bool,
            [new Parameter("a", s_bool), new Parameter("b", s_bool), new Parameter("c", s_bool)]);

        Assert.Contains("return a && (b || c);", output);
    }

    // Issue #2916: a ref-typed conditional whose arms are both genuine
    // ref-producers renders as a ref ternary (`ref a : ref b`, see Deref's
    // Conditional case). One arm is an `Unbox` — a managed pointer into a box.
    // Deref previously had no case for it, so it fell through to the generic
    // ByRef arm and emitted the node's own ref-producer spelling `ref (int)o`,
    // which the enclosing ref ternary re-prefixed into `ref ref (int)o`
    // (CS1525); the naive value-copy `(int)o` fixes the double keyword but is
    // an unbox.any copy, not an assignable place, so `ref (int)o` is CS0445.
    // Deref now spells the box place as `Unsafe.Unbox<T>(o)` (a `ref T`
    // intrinsic), which is valid and faithful in every Deref position. Not
    // reachable from C# source (BooleanFoldingPass.FoldSlotDiamond is the only
    // producer of a ref-typed conditional with a non-place arm), so exercised
    // on hand-built IR.
    [Fact]
    public void Deref_RefConditionalWithUnboxArm_SpellsUnsafeUnbox()
    {
        var conditional = new Conditional(
            new LoadArgument(0, "flag", s_bool),
            new Unbox(s_int, new LoadArgument(1, "o", s_object)),
            new LoadArgumentAddress(2, "n", s_int));
        var load = new LoadIndirect(s_int, conditional);

        var output = PrintReturn(
            load,
            s_int,
            [new Parameter("flag", s_bool), new Parameter("o", s_object), new Parameter("n", s_int)]);

        Assert.Contains("Unsafe.Unbox<int>(o)", output);
        Assert.DoesNotContain("ref ref", output);
        Assert.DoesNotContain("ref (int)o", output);
        AssertCompiles("public static int M(bool flag, object o, int n)", output);
    }

    // Issue #2916: a by-ref return of an `Unbox` is a ref-place return
    // (`ReturnText` routes the value through `ArgumentLvalue`). An unbox is the
    // managed pointer into the box, so the only valid spelling is the
    // `Unsafe.Unbox<T>(o)` intrinsic; the bare cast `return ref (int)o;` is
    // CS0445/CS1525. Hand-built IR (a ref-returning method whose body returns an
    // unbox place).
    [Fact]
    public void ReturnRefUnbox_SpellsUnsafeUnbox()
    {
        var output = PrintReturn(
            new Unbox(s_int, new LoadArgument(0, "o", s_object)),
            TypeRef.ByRef(s_int),
            [new Parameter("o", s_object)]);

        Assert.Contains("return ref ", output);
        Assert.Contains("Unsafe.Unbox<int>(o)", output);
        Assert.DoesNotContain("(int)o", output);
        AssertCompiles("public static ref int M(object o)", output);
    }

    // Review (#2925): a ref-place (here a by-ref return) of a *generic-parameter*
    // unbox must keep the faithful `Unsafe.Unbox<T>(o)` intrinsic — the place
    // substrates (`ArgumentLvalue`/`Deref`) stay ungated. `Unsafe.Unbox<T>`
    // compiles for a `where T : struct` parameter, whereas the value-copy cast
    // `ref (T)o` is CS0445; a ref-place has no valid cast form, so the intrinsic
    // is the only faithful spelling. (The value-position member receiver falls
    // back to the cast for a generic parameter instead — see
    // CSharpPrinterReceiverTests.UnboxReceiver_GenericParameter_KeepsCastNotUnsafeUnbox.)
    [Fact]
    public void ReturnRefUnbox_GenericParameter_SpellsUnsafeUnbox()
    {
        var t = TypeRef.MethodGenericParameter(0, "T");
        var output = PrintReturn(
            new Unbox(t, new LoadArgument(0, "o", s_object)),
            TypeRef.ByRef(t),
            [new Parameter("o", s_object)]);

        Assert.Contains("return ref ", output);
        Assert.Contains("Unsafe.Unbox<T>(o)", output);
        Assert.DoesNotContain("ref (T)o", output);
        AssertCompiles("public static ref T M<T>(object o) where T : struct", output);
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

    [Fact]
    public void InterpolationHole_BinaryExpressionWithFormat_RendersBare()
    {
        var value = new Binary(BinaryKind.Add, false, false,
            new LoadArgument(0, "a", s_int),
            new LoadArgument(1, "b", s_int));
        var interpolation = new InterpolatedStringExpression(
            [InterpolatedStringPart.FormattedValue(0, new InterpolationFormat(0, HasAlignment: false, "X"))],
            [value]);

        var output = PrintReturn(
            interpolation,
            s_string,
            [new Parameter("a", s_int), new Parameter("b", s_int)]);

        Assert.Contains("return $\"{a + b:X}\";", output);
        Assert.DoesNotContain("{(a + b):X}", output);
    }

    [Fact]
    public void InterpolationHole_ConditionalWithFormat_StaysParenthesized()
    {
        var conditional = new Conditional(
            new LoadArgument(0, "flag", s_bool),
            new LoadArgument(1, "a", s_int),
            new LoadArgument(2, "b", s_int));
        var interpolation = new InterpolatedStringExpression(
            [InterpolatedStringPart.FormattedValue(0, new InterpolationFormat(0, HasAlignment: false, "X"))],
            [conditional]);

        var output = PrintReturn(
            interpolation,
            s_string,
            [new Parameter("flag", s_bool), new Parameter("a", s_int), new Parameter("b", s_int)]);

        Assert.Contains("return $\"{(flag ? a : b):X}\";", output);
        Assert.DoesNotContain("{flag ? a : b:X}", output);
    }

    [Fact]
    public void CastOperand_NamedTargetNegativeLiteral_StaysParenthesized()
    {
        var enumType = TypeRef.Definition("Synthetic", "", "E");
        var convert = new Pipeline.Convert(enumType, isChecked: false, isUnsigned: false, new Constant(-1, s_int));

        var output = PrintReturn(convert, enumType, []);

        Assert.Contains("return (E)(-1);", output);
        Assert.DoesNotContain("return (E)-1;", output);
    }

    [Fact]
    public void BinaryOperand_TighterLeftChild_RendersBare()
    {
        var multiply = new Binary(BinaryKind.Multiply, false, false,
            new LoadArgument(0, "a", s_int),
            new LoadArgument(1, "b", s_int));
        var add = new Binary(BinaryKind.Add, false, false,
            multiply,
            new LoadArgument(2, "c", s_int));

        var output = PrintReturn(
            add,
            s_int,
            [new Parameter("a", s_int), new Parameter("b", s_int), new Parameter("c", s_int)]);

        Assert.Contains("return a * b + c;", output);
        Assert.DoesNotContain("(a * b) + c", output);
    }

    [Fact]
    public void BinaryOperand_RightEqualPrecedenceChild_StaysParenthesized()
    {
        var nested = new Binary(BinaryKind.Subtract, false, false,
            new LoadArgument(1, "b", s_int),
            new LoadArgument(2, "c", s_int));
        var subtract = new Binary(BinaryKind.Subtract, false, false,
            new LoadArgument(0, "a", s_int),
            nested);

        var output = PrintReturn(
            subtract,
            s_int,
            [new Parameter("a", s_int), new Parameter("b", s_int), new Parameter("c", s_int)]);

        Assert.Contains("return a - (b - c);", output);
        Assert.DoesNotContain("return a - b - c;", output);
    }

    [Fact]
    public void CoalesceLeft_NestedCoalesce_StaysParenthesized()
    {
        var nested = new Coalesce(
            new LoadArgument(0, "a", s_string),
            new LoadArgument(1, "b", s_string));
        var outer = new Coalesce(
            nested,
            new LoadArgument(2, "c", s_string));

        var output = PrintReturn(
            outer,
            s_string,
            [new Parameter("a", s_string), new Parameter("b", s_string), new Parameter("c", s_string)]);

        Assert.Contains("return (a ?? b) ?? c;", output);
        Assert.DoesNotContain("return a ?? b ?? c;", output);
    }

    [Fact]
    public void CoalesceLeft_Conditional_StaysParenthesized()
    {
        var conditional = new Conditional(
            new LoadArgument(0, "flag", s_bool),
            new LoadArgument(1, "a", s_string),
            new LoadArgument(2, "b", s_string));
        var coalesce = new Coalesce(
            conditional,
            new LoadArgument(3, "c", s_string));

        var output = PrintReturn(
            coalesce,
            s_string,
            [
                new Parameter("flag", s_bool),
                new Parameter("a", s_string),
                new Parameter("b", s_string),
                new Parameter("c", s_string),
            ]);

        Assert.Contains("return (flag ? a : b) ?? c;", output);
        Assert.DoesNotContain("return flag ? a : b ?? c;", output);
    }

    // Issue #2867 follow-up (Gemini review): CSharpPrecedence.Of did not
    // classify TupleSwitchExpression alongside its SwitchExpression/
    // UnionSwitchExpression siblings, so it fell through to the Primary
    // default. RenderedExpression's three consumers (BinaryOperand,
    // CoalesceLeftText, InterpolatedExpression) all trust that reported
    // precedence to decide whether to wrap — with the bug, a tuple switch
    // nested as a Binary/Coalesce operand rendered bare, inconsistent with how
    // a plain Conditional or SwitchExpression renders in the exact same
    // position (see CoalesceLeft_Conditional_StaysParenthesized above). This
    // is an IR-contract defect (any Precedence.Of consumer, present or
    // future, gets the wrong answer for this node type) rather than a hard
    // C# syntax requirement — a tuple/relational switch expression is
    // self-terminating at its closing `}`, so the *unwrapped* spelling below
    // also compiles and evaluates identically; the assertions here pin the
    // wrapped, sibling-consistent spelling the fixed Of() now produces.
    static TupleSwitchExpression MakeTupleSwitch(TypeRef componentType, TypeRef resultType, object firstValue, object defaultValue)
    {
        var x = new LoadArgument(0, "x", componentType);
        var y = new LoadArgument(1, "y", componentType);
        var firstArm = new TupleSwitchExpressionArm(
            subpatterns: [new PositionalPatternSubpattern(ComparisonKind.GreaterThan), new PositionalPatternSubpattern(ComparisonKind.GreaterThan)],
            constants: [new Constant(0, componentType), new Constant(0, componentType)],
            value: new Constant(firstValue, resultType));
        var defaultArm = new TupleSwitchExpressionArm(subpatterns: [], constants: [], value: new Constant(defaultValue, resultType));
        return new TupleSwitchExpression([x, y], [firstArm, defaultArm]);
    }

    [Fact]
    public void BinaryOperand_TupleSwitchExpressionChild_StaysParenthesized()
    {
        var tupleSwitch = MakeTupleSwitch(s_int, s_int, firstValue: 1, defaultValue: 2);
        var add = new Binary(BinaryKind.Add, false, false, new LoadArgument(2, "z", s_int), tupleSwitch);

        var output = PrintReturn(
            add,
            s_int,
            [new Parameter("x", s_int), new Parameter("y", s_int), new Parameter("z", s_int)]);

        Assert.Contains("return z + ((x, y) switch { (> 0, > 0) => 1, _ => 2 });", output);
        Assert.DoesNotContain("return z + (x, y) switch", output);
        AssertCompiles("public static int M(int x, int y, int z)", output);
    }

    [Fact]
    public void CoalesceLeft_TupleSwitchExpressionChild_StaysParenthesized()
    {
        var tupleSwitch = MakeTupleSwitch(s_int, s_string, firstValue: "one", defaultValue: "other");
        var coalesce = new Coalesce(tupleSwitch, new LoadArgument(2, "fallback", s_string));

        var output = PrintReturn(
            coalesce,
            s_string,
            [new Parameter("x", s_int), new Parameter("y", s_int), new Parameter("fallback", s_string)]);

        Assert.Contains("return ((x, y) switch { (> 0, > 0) => \"one\", _ => \"other\" }) ?? fallback;", output);
        Assert.DoesNotContain("return (x, y) switch { (> 0, > 0) => \"one\", _ => \"other\" } ?? fallback;", output);
        AssertCompiles("public static string M(int x, int y, string fallback)", output);
    }

    // Confirms the fix has no effect on the pass's actual current output
    // shape: TupleSwitchExpressionPass only ever raises a tuple switch as a
    // Return's direct value, which CoerceText special-cases (the switch is
    // the whole right-hand side, never nested under another operator), so it
    // never reaches RenderedExpression/CSharpPrecedence.Of at all.
    [Fact]
    public void DirectReturn_TupleSwitchExpression_RendersWithoutOuterParens()
    {
        var tupleSwitch = MakeTupleSwitch(s_int, s_int, firstValue: 1, defaultValue: 2);

        var output = PrintReturn(tupleSwitch, s_int, [new Parameter("x", s_int), new Parameter("y", s_int)])
            .ReplaceLineEndings("\n");

        Assert.Contains("return (x, y) switch\n{\n    (> 0, > 0) => 1,\n    _ => 2,\n};", output);
        Assert.DoesNotContain("return ((x, y) switch", output);
        AssertCompiles("public static int M(int x, int y)", output);
    }

    // Issue #2929: unbox yields a managed reference into the box. Converting
    // that reference to nuint must preserve the address, not read and convert
    // the boxed value.
    [Fact]
    public void ConvertNativeUInt_Unbox_SpellsPointerIntoBox()
    {
        var result = PrintReturnResult(
            new ILInspector.Decompiler.Pipeline.Convert(
                s_nuint,
                isChecked: false,
                isUnsigned: false,
                new Unbox(s_int, new LoadArgument(0, "o", s_object))),
            s_nuint,
            [new Parameter("o", s_object)]);
        string output = result.Output!;

        Assert.Contains(
            "return (nuint)System.Runtime.CompilerServices.Unsafe.AsPointer(ref System.Runtime.CompilerServices.Unsafe.Unbox<int>(o));",
            output);
        Assert.DoesNotContain("(nuint)(ref (int)o)", output);
        Assert.True(result.RequiresUnsafeBodyModifier);
        AssertCompiles("public static unsafe nuint M(object o)", output);
    }

    [Fact]
    public void ConvertNativeUInt_Value_RemainsOrdinarySafeCast()
    {
        var result = PrintReturnResult(
            new ILInspector.Decompiler.Pipeline.Convert(
                s_nuint,
                isChecked: false,
                isUnsigned: false,
                new LoadArgument(0, "value", s_uint)),
            s_nuint,
            [new Parameter("value", s_uint)]);
        string output = result.Output!;

        Assert.Contains("return (nuint)value;", output);
        Assert.DoesNotContain("Unsafe.", output);
        Assert.False(result.RequiresUnsafeBodyModifier);
        AssertCompiles("public static nuint M(uint value)", output);
    }

    // AssertCompiles/Recompile shape already used by DataflowFactsTests,
    // EnumCastPrinterTests, and MixedSignComparisonTests; reused here rather
    // than reimplemented.
    static void AssertCompiles(string methodHeader, string body)
    {
        var errors = Recompile(methodHeader, body)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToArray();
        Assert.True(errors.Length == 0, "Rendered method must compile, got:\n  " + string.Join("\n  ", errors) + "\n--- body ---\n" + body);
    }

    static ImmutableArray<Diagnostic> Recompile(string methodHeader, string body)
    {
        string source = $$"""
            static class __Gate
            {
                {{methodHeader}}
                {
            {{body}}
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "__gate",
            [tree],
            RoslynTestReferences.TrustedPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        return compilation.GetDiagnostics();
    }

    static string PrintReturn(IrExpression value, TypeRef returnType, ImmutableArray<Parameter> parameters)
        => PrintReturnResult(value, returnType, parameters).Output!;

    static DecompilerResult PrintReturnResult(
        IrExpression value,
        TypeRef returnType,
        ImmutableArray<Parameter> parameters)
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
        return CSharpPrinter.Print(function);
    }
}
