using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

// Slice 1 of docs/design/value-typed-emission.md: the coercion decision lives in
// one place (Coerce / TryCoerceEnumOperand / EnumConstantText), not per render
// branch. These tests pin the two behavior deltas of the consolidation:
//
// 1. Guard drift fixed: the binary/comparison enum-operand sites used
//    TypeFamilies.IsInteger, which admits bool (I4 stack family), so an enum
//    met by a bool operand rendered `(E)(x == y)` — CS0030. The shared operand
//    coercion composes the truthiness spelling instead: `(E)(x == y ? 1 : 0)`.
//    csc never emits this shape; it is verifiable IL (bool and enum share I4),
//    so the fixtures are synthetic IR.
// 2. Member naming is part of the one name-or-cast rule: an un-retyped integer
//    constant at a known-enum sink renders the member name when one matches,
//    where the sink previously open-coded the cast.
public class CoerceChokePointTests
{
    [Fact]
    public void EnumComparedToBool_ComposesTruthinessWithEnumCast()
    {
        var enumType = TypeRef.Definition("synthetic", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var inner = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadArgument(1, "x", intType),
            new LoadArgument(2, "y", intType));
        var outer = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadArgument(0, "flags", enumType),
            inner);
        string body = RenderReturn(
            outer,
            TypeRef.CoreLib("System", "Boolean"),
            [new Parameter("flags", enumType), new Parameter("x", intType), new Parameter("y", intType)],
            enumType);

        Assert.Contains("(Tiny)(x == y ? 1 : 0)", body);
        Assert.DoesNotContain("(Tiny)(x == y)", body);
        AssertCompiles("public static bool M(Tiny flags, int x, int y)", body, "public enum Tiny { }");
    }

    [Fact]
    public void EnumBitwiseWithBool_ComposesTruthinessWithEnumCast()
    {
        var enumType = TypeRef.Definition("synthetic", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var inner = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadArgument(1, "x", intType),
            new LoadArgument(2, "y", intType));
        var and = new Binary(
            BinaryKind.And,
            isChecked: false,
            isUnsigned: false,
            new LoadArgument(0, "flags", enumType),
            inner);
        string body = RenderReturn(
            and,
            enumType,
            [new Parameter("flags", enumType), new Parameter("x", intType), new Parameter("y", intType)],
            enumType);

        Assert.Contains("flags & (Tiny)(x == y ? 1 : 0)", body);
        Assert.DoesNotContain("& (Tiny)(x == y)", body);
        AssertCompiles("public static Tiny M(Tiny flags, int x, int y)", body, "public enum Tiny { }");
    }

    [Fact]
    public void BoolConditionalArm_AtEnumMergedType_ComposesTruthinessWithEnumCast()
    {
        // A conditional whose join is enum-typed but whose true arm is a raw
        // comparison result: the bool arm cannot render bare (CS0029) or as a
        // direct enum cast (CS0030); it composes.
        var enumType = TypeRef.Definition("synthetic", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new Comparison(
                ComparisonKind.Equal,
                isUnsigned: false,
                new LoadArgument(1, "x", intType),
                new LoadArgument(2, "y", intType)),
            new LoadArgument(3, "e", enumType))
        {
            MergedType = enumType,
        };
        string body = RenderReturn(
            conditional,
            enumType,
            [
                new Parameter("c", boolType),
                new Parameter("x", intType),
                new Parameter("y", intType),
                new Parameter("e", enumType),
            ],
            enumType);

        Assert.Contains("(Tiny)(x == y ? 1 : 0)", body);
        AssertCompiles("public static Tiny M(bool c, int x, int y, Tiny e)", body, "public enum Tiny { }");
    }

    [Fact]
    public void Conditional_AtPrimitiveStoreTarget_DistributesCoercionToArms()
    {
        // Issue #2306: the conditional's own join is int, but the store target
        // is uint. The arms agree with each other, so join-arm typing does not
        // intervene; the target-aware merge renderer must still route through
        // the shared slot-coercion contract and cast the non-constant arm.
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var conditional = new Conditional(
            new LoadArgument(0, "flag", boolType),
            new Constant(0, intType),
            new LoadArgument(1, "tick", intType))
        {
            MergedType = intType,
        };

        string body = RenderBody(
            [
                new StoreLocal(0, uintType, conditional),
                new Return(new LoadLocal(0, uintType)),
            ],
            uintType,
            [new Parameter("flag", boolType), new Parameter("tick", intType)],
            [uintType]);

        Assert.Contains("flag ? 0 : (uint)tick", body);
        Assert.DoesNotContain("? 0 : tick", body);
        AssertCompiles("public static uint M(bool flag, int tick)", body);
    }

    [Fact]
    public void Conditional_AtNativePrimitiveStoreTarget_DistributesCoercionToArms()
    {
        // Gemini review: nint/nuint have platform-sized width, so the native
        // family must use the same slot-width helper as CoerceText instead of
        // the fixed-width SameWidth table.
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intPtrType = TypeRef.CoreLib("System", "IntPtr");
        var uintPtrType = TypeRef.CoreLib("System", "UIntPtr");
        var conditional = new Conditional(
            new LoadArgument(0, "flag", boolType),
            new LoadArgument(1, "a", intPtrType),
            new LoadArgument(2, "b", intPtrType))
        {
            MergedType = intPtrType,
        };

        string body = RenderBody(
            [
                new StoreLocal(0, uintPtrType, conditional),
                new Return(new LoadLocal(0, uintPtrType)),
            ],
            uintPtrType,
            [new Parameter("flag", boolType), new Parameter("a", intPtrType), new Parameter("b", intPtrType)],
            [uintPtrType]);

        Assert.Contains("flag ? (nuint)a : (nuint)b", body);
        Assert.DoesNotContain("? a : b", body);
        AssertCompiles("public static nuint M(bool flag, nint a, nint b)", body);
    }

    [Fact]
    public void Conditional_WithBoolArmAtPrimitiveStoreTarget_CastsComposedBoolArm()
    {
        // Gemini review: bool remains outside the primitive conditional's
        // merged type, but an integer-merged conditional can still contain a
        // bool-typed arm. The arm composes bool->int and then casts to the
        // unsigned target instead of vetoing target-aware rendering.
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var conditional = new Conditional(
            new LoadArgument(0, "flag", boolType),
            new LoadArgument(1, "b", boolType),
            new LoadArgument(2, "tick", intType))
        {
            MergedType = intType,
        };

        string body = RenderBody(
            [
                new StoreLocal(0, uintType, conditional),
                new Return(new LoadLocal(0, uintType)),
            ],
            uintType,
            [new Parameter("flag", boolType), new Parameter("b", boolType), new Parameter("tick", intType)],
            [uintType]);

        Assert.Contains("flag ? (uint)(b ? 1 : 0) : (uint)tick", body);
        Assert.DoesNotContain("? (b ? 1 : 0) : tick", body);
        AssertCompiles("public static uint M(bool flag, bool b, int tick)", body);
    }

    // #2376 phase-1 canary (corpus witness CommonConversion::.ctor): a bool
    // diamond with a false arm folds to `&&` (SpellsAsLogicalAnd), so as the
    // condition of a composed `? 1 : 0` it renders BARE — classifying the
    // Conditional node instead of its folded spelling wrapped it,
    // `(exists && true) ? 1 : 0`, the one churn class in the scanner
    // retirement A/B (6 of 135k corpus methods).
    [Fact]
    public void FoldedLogicalCondition_OfBoolToInteger_RendersBare()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var diamond = new Conditional(
            new LoadArgument(0, "exists", boolType),
            new Constant(true, boolType),
            new Constant(false, boolType))
        {
            MergedType = boolType,
        };

        string body = RenderBody(
            [
                new StoreLocal(0, intType, diamond),
                new Return(new LoadLocal(0, intType)),
            ],
            intType,
            [new Parameter("exists", boolType)],
            [intType]);

        Assert.Contains("exists && true ? 1 : 0", body);
        Assert.DoesNotContain("(exists && true)", body);
        AssertCompiles("public static int M(bool exists)", body);
    }

    // #2345 review canary (GPT-5.5, finding 1): a stale Coerce{int} over a
    // BOOL arm at an ENUM join must keep the enum/bool composition path —
    // the integer-only guard on Coerce re-targeting; preempting
    // TryCoerceEnumOperand rendered the bool bare (CS0029).
    [Fact]
    public void StaleCoerceBoolArm_AtEnumJoin_KeepsComposedEnumCast()
    {
        var enumType = TypeRef.Definition("synthetic", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new Coerce(intType, new LoadArgument(1, "b", boolType)),
            new LoadArgument(2, "e", enumType))
        {
            MergedType = enumType,
        };
        string body = RenderReturn(
            conditional,
            enumType,
            [new Parameter("c", boolType), new Parameter("b", boolType), new Parameter("e", enumType)],
            enumType);

        Assert.Contains("(Tiny)(b ? 1 : 0)", body);
        Assert.DoesNotContain("? b :", body);
        AssertCompiles("public static Tiny M(bool c, bool b, Tiny e)", body, "public enum Tiny { }");
    }

    // #2345 round-2 canary (Gemini, Critical — corpus witness
    // PEModule::GetMarshallingType): a stale Coerce{int} over a NON-constant
    // integer arm at an ENUM join must keep TryCoerceEnumOperand's cast —
    // the re-target branch firing for enum targets rendered `firstByte`
    // bare (CS0029) where base spelled `(UnmanagedType)firstByte`.
    [Fact]
    public void StaleCoerceIntegerArm_AtEnumJoin_KeepsEnumCast()
    {
        var enumType = TypeRef.Definition("synthetic", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var byteType = TypeRef.CoreLib("System", "Byte");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new Coerce(intType, new LoadArgument(1, "firstByte", byteType)),
            new Constant(0, intType))
        {
            MergedType = enumType,
        };
        string body = RenderReturn(
            conditional,
            enumType,
            [new Parameter("c", boolType), new Parameter("firstByte", byteType)],
            enumType);

        Assert.Contains("(Tiny)firstByte", body);
        Assert.DoesNotContain("? firstByte", body);
        AssertCompiles("public static Tiny M(bool c, byte firstByte)", body, "public enum Tiny { }");
    }

    // #2345 round-2 finding E (Gemini): a WIDENING arm does not anchor the
    // natural type at the target — a byte arm at a uint join widens to both
    // uint and int, csc's natural type lands on int, and a bare in-range
    // constant sibling fails (CS0266). The constant must spell `(uint)1`;
    // the byte arm stays bare (it widens to uint legitimately).
    [Fact]
    public void ConstantBesideWideningArm_AtUnsignedSink_CastsTheConstant()
    {
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var byteType = TypeRef.CoreLib("System", "Byte");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new Constant(1, intType),
            new LoadArgument(1, "x", byteType))
        {
            MergedType = intType,
        };
        string body = RenderReturn(
            conditional,
            uintType,
            [new Parameter("c", boolType), new Parameter("x", byteType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("c ? (uint)1 : x", body);
        AssertCompiles("public static uint M(bool c, byte x)", body);
    }


    // #2345 review canary (GPT-5.5, finding 2): the switch-expression gate
    // admits bool arms via CanSpellBoolToInteger, so SwitchArmValueText must
    // compose them like ConditionalArm does — bare bool at a uint join is
    // CS0029.
    [Fact]
    public void SwitchExpression_WithBoolArmAtUnsignedStoreTarget_ComposesBoolArm()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var switchExpression = new SwitchExpression(
            new LoadArgument(0, "x", intType),
            [
                new SwitchExpressionArm([0], isDefault: false, new LoadArgument(1, "i", intType)),
                new SwitchExpressionArm(default, isDefault: true, new LoadArgument(2, "b", boolType)),
            ]);

        string body = RenderBody(
            [
                new StoreLocal(0, uintType, switchExpression),
                new Return(new LoadLocal(0, uintType)),
            ],
            uintType,
            [new Parameter("x", intType), new Parameter("i", intType), new Parameter("b", boolType)],
            [uintType]);

        Assert.Contains("(uint)(b ? 1 : 0)", body);
        Assert.DoesNotContain("=> b", body);
        AssertCompiles("public static uint M(int x, int i, bool b)", body);
    }

    [Fact]
    public void CoalesceExpression_WithBoolRightAtIntStoreTarget_ParenthesizesComposedBoolArm()
    {
        // #2345 round-3 (GPT-5.5): the coalesce right side binds looser than
        // `?:`, so the Int32/Int64 bool composition must parenthesize —
        // `n ?? (b ? 1 : 0)`, not `n ?? b ? 1 : 0` which C# parses as
        // `(n ?? b) ? 1 : 0` (CS0019). Sibling to the switch/conditional bool
        // canaries; the coalesce is the one consumer whose join operator lacks a
        // delimiter to bracket the ternary.
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var nullableInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Nullable`1"),
            [intType]);
        var coalesce = new Coalesce(
            new LoadArgument(0, "n", nullableInt),
            new LoadArgument(1, "b", boolType));

        string body = RenderBody(
            [
                new StoreLocal(0, intType, coalesce),
                new Return(new LoadLocal(0, intType)),
            ],
            intType,
            [new Parameter("n", nullableInt), new Parameter("b", boolType)],
            [intType]);

        Assert.Contains("?? (b ? 1 : 0)", body);
        AssertCompiles("public static int M(int? n, bool b)", body);
    }

    [Fact]
    public void BoolCompositionOfNestedConditional_ParenthesizesCondition()
    {
        // #2345 round-4 (GPT-5.5): BoolToIntegerText composes `{cond} ? 1 : 0`;
        // when the bool value is itself a conditional it renders bare
        // `c ? b1 : b2`, and `c ? b1 : b2 ? 1 : 0` reassociates as
        // `c ? b1 : (b2 ? 1 : 0)` (bool/int arm mismatch, CS0029). The condition
        // must parenthesize: `(c ? b1 : b2) ? 1 : 0`. Root fix in
        // BoolToIntegerText, so conditional/switch/coalesce consumers all inherit
        // it — asserted here through a conditional arm.
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var nestedBool = new Conditional(
            new LoadArgument(1, "c", boolType),
            new LoadArgument(2, "b1", boolType),
            new LoadArgument(3, "b2", boolType))
        {
            MergedType = boolType,
        };
        var outer = new Conditional(
            new LoadArgument(0, "outer", boolType),
            nestedBool,
            new Constant(2, intType))
        {
            MergedType = intType,
        };

        string body = RenderBody(
            [
                new StoreLocal(0, uintType, outer),
                new Return(new LoadLocal(0, uintType)),
            ],
            uintType,
            [new Parameter("outer", boolType), new Parameter("c", boolType), new Parameter("b1", boolType), new Parameter("b2", boolType)],
            [uintType]);

        Assert.Contains("(c ? b1 : b2)", body);
        AssertCompiles("public static uint M(bool outer, bool c, bool b1, bool b2)", body);
    }

    [Fact]
    public void BoolCompositionOfCoerceWrappedConditional_ParenthesizesCondition()
    {
        // #2345 round-5 (GPT-5.5): a conditional can hide behind a stale
        // `Coerce`, so parenthesizing on `value is Conditional` misses
        // `Coerce(bool, Conditional)` — `Condition()` still renders it bare
        // `c ? b1 : b2`, producing `c ? b1 : b2 ? 1 : 0` (CS0029). The guard
        // keys off the rendered condition text, not the node type.
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var inner = new Conditional(
            new LoadArgument(0, "c", boolType),
            new LoadArgument(1, "b1", boolType),
            new LoadArgument(2, "b2", boolType))
        {
            MergedType = boolType,
        };
        var value = new Coerce(boolType, inner);

        string body = RenderBody(
            [
                new StoreLocal(0, intType, value),
                new Return(new LoadLocal(0, intType)),
            ],
            intType,
            [new Parameter("c", boolType), new Parameter("b1", boolType), new Parameter("b2", boolType)],
            [intType]);

        Assert.Contains("(c ? b1 : b2)", body);
        AssertCompiles("public static int M(bool c, bool b1, bool b2)", body);
    }

    [Fact]
    public void CoalesceRightConditional_WithBracketInStringLiteral_StillParenthesizes()
    {
        // #2345 round-5 (Gemini): the top-level-conditional scan must skip
        // string/char literals, or an unbalanced bracket inside a literal
        // (`s == "("`) corrupts its depth count and the `??` right ternary is
        // left un-parenthesized (`n ?? s == "(" ? 1 : 2`, CS0019).
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var stringType = TypeRef.CoreLib("System", "String");
        var nullableInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Nullable`1"),
            [intType]);
        var conditional = new Conditional(
            new Comparison(ComparisonKind.Equal, isUnsigned: false, new LoadArgument(1, "s", stringType), new Constant("(", stringType)),
            new Constant(1, intType),
            new Constant(2, intType))
        {
            MergedType = intType,
        };
        var coalesce = new Coalesce(new LoadArgument(0, "n", nullableInt), conditional);

        string body = RenderBody(
            [
                new StoreLocal(0, intType, coalesce),
                new Return(new LoadLocal(0, intType)),
            ],
            intType,
            [new Parameter("n", nullableInt), new Parameter("s", stringType)],
            [intType]);

        Assert.DoesNotContain("?? s ==", body);
        AssertCompiles("public static int M(int? n, string s)", body);
    }

    [Fact]
    public void CoalesceRightStringLiteral_WithQuestionMark_StaysBare()
    {
        // #2345 round-5 (Gemini): a coalesce right that is a plain string
        // literal containing a space-flanked `?` must NOT be mistaken for a
        // conditional and wrapped — `s ?? "is it ? "`, not `s ?? ("is it ? ")`.
        var stringType = TypeRef.CoreLib("System", "String");
        var coalesce = new Coalesce(
            new LoadArgument(0, "s", stringType),
            new Constant("is it ? ", stringType));

        string body = RenderBody(
            [
                new StoreLocal(0, stringType, coalesce),
                new Return(new LoadLocal(0, stringType)),
            ],
            stringType,
            [new Parameter("s", stringType)],
            [stringType]);

        Assert.DoesNotContain("(\"is it ? \")", body);
        AssertCompiles("public static string M(string s)", body);
    }

    [Fact]
    public void CoalesceRightConditional_WithInterpolatedStringHole_StillParenthesizes()
    {
        // #2345 round-6 (GPT-5.5, Gemini): the literal skipper must parse
        // interpolated-string holes, or a nested string with an unbalanced
        // brace inside a hole (`$"{"("}"`) corrupts the depth count and the
        // real top-level `??`-right ternary escapes un-parenthesized (CS0029).
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var stringType = TypeRef.CoreLib("System", "String");
        var interpolated = new InterpolatedStringExpression(
            [InterpolatedStringPart.FormattedValue(0)],
            [new Constant("(", stringType)]);
        var comparison = new Comparison(ComparisonKind.Equal, isUnsigned: false, interpolated, new LoadArgument(0, "s", stringType));
        var conditional = new Conditional(
            comparison,
            new LoadArgument(1, "b1", boolType),
            new LoadArgument(2, "b2", boolType))
        {
            MergedType = boolType,
        };
        var value = new Coerce(boolType, conditional);

        string body = RenderBody(
            [
                new StoreLocal(0, intType, value),
                new Return(new LoadLocal(0, intType)),
            ],
            intType,
            [new Parameter("s", stringType), new Parameter("b1", boolType), new Parameter("b2", boolType)],
            [intType]);

        AssertCompiles("public static int M(string s, bool b1, bool b2)", body);
    }

    [Fact]
    public void CoalesceRightConditional_WithInterpolationFormatQuote_StillParenthesizes()
    {
        // #2345 round-7 (Gemini): an interpolation format specifier is literal
        // text — a bare `'` (e.g. a DateTime custom format `hh 'o'`) or an
        // escaped `\"` there is a format character, not a string delimiter. The
        // hole skipper must not treat it as a nested literal, or it swallows the
        // hole's `}` and the string's closing `"`, hiding the real top-level
        // `??`-right ternary (left un-parenthesized → CS0019).
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var stringType = TypeRef.CoreLib("System", "String");
        var objectType = TypeRef.CoreLib("System", "Object");
        var interpolated = new InterpolatedStringExpression(
            [new InterpolatedStringPart(null, 0, new InterpolationFormat(0, HasAlignment: false, "hh 'o''clock'"))],
            [new LoadArgument(1, "x", objectType)]);
        var comparison = new Comparison(ComparisonKind.NotEqual, isUnsigned: false, interpolated, new LoadArgument(0, "s", stringType));
        var conditional = new Conditional(
            comparison,
            new LoadArgument(2, "b1", boolType),
            new LoadArgument(3, "b2", boolType))
        {
            MergedType = boolType,
        };
        var value = new Coerce(boolType, conditional);

        string body = RenderBody(
            [
                new StoreLocal(0, intType, value),
                new Return(new LoadLocal(0, intType)),
            ],
            intType,
            [new Parameter("s", stringType), new Parameter("x", objectType), new Parameter("b1", boolType), new Parameter("b2", boolType)],
            [intType]);

        AssertCompiles("public static int M(string s, object x, bool b1, bool b2)", body);
    }

    [Fact]
    public void CoalesceExpression_WithConditionalRight_ParenthesizesTernary()
    {
        // #2345 round-4 (Gemini): a coalesce right that renders as a bare
        // top-level ternary — here a stale `Coerce` over a conditional, which
        // TryCoerceJoinArm re-targets to `CoerceText(conditional, target)` and
        // returns unbracketed — must parenthesize as a `??` right operand:
        // `n ?? (c ? 1 : 2)`, not `n ?? c ? 1 : 2` (parses `(n ?? c) ? 1 : 2`,
        // CS0019). Covers the render paths the bool-composition canary does not.
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var sbyteType = TypeRef.CoreLib("System", "SByte");
        var nullableInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Nullable`1"),
            [intType]);
        var conditional = new Conditional(
            new LoadArgument(1, "c", boolType),
            new Constant(1, intType),
            new Constant(2, intType))
        {
            MergedType = intType,
        };
        var coalesce = new Coalesce(
            new LoadArgument(0, "n", nullableInt),
            new Coerce(sbyteType, conditional));

        string body = RenderBody(
            [
                new StoreLocal(0, intType, coalesce),
                new Return(new LoadLocal(0, intType)),
            ],
            intType,
            [new Parameter("n", nullableInt), new Parameter("c", boolType)],
            [intType]);

        Assert.DoesNotContain("?? c ? 1 : 2", body);
        AssertCompiles("public static int M(int? n, bool c)", body);
    }

    [Fact]
    public void Conditional_WithNarrowSignedArmsAtPrimitiveStoreTarget_CastsThroughMergedWidth()
    {
        // Clean Gemini review: the target cast is licensed by the conditional's
        // merged stack width, not the source spelling width of each arm. Two
        // sbyte arms can be int-merged by the IR and still need uint casts at a
        // uint store target.
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var sbyteType = TypeRef.CoreLib("System", "SByte");
        var intType = TypeRef.CoreLib("System", "Int32");
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var conditional = new Conditional(
            new LoadArgument(0, "flag", boolType),
            new LoadArgument(1, "sb1", sbyteType),
            new LoadArgument(2, "sb2", sbyteType))
        {
            MergedType = intType,
        };

        string body = RenderBody(
            [
                new StoreLocal(0, uintType, conditional),
                new Return(new LoadLocal(0, uintType)),
            ],
            uintType,
            [new Parameter("flag", boolType), new Parameter("sb1", sbyteType), new Parameter("sb2", sbyteType)],
            [uintType]);

        Assert.Contains("flag ? (uint)sb1 : (uint)sb2", body);
        Assert.DoesNotContain("? sb1 : sb2", body);
        AssertCompiles("public static uint M(bool flag, sbyte sb1, sbyte sb2)", body);
    }

    [Fact]
    public void UnnamedHighBitConstantArm_KeepsUncheckedCast()
    {
        // The cast half of the name-or-cast rule at the #2076 conditional-arm
        // shape: a negative payload on a uint-backed enum with NO matching member
        // must still take the overflow-aware unchecked cast (CS0221 otherwise) —
        // naming only fires on an exact member match (the named twin lives in
        // EnumCastPrinterTests.EnumConditional_SameAssemblyUnsignedEnum_NamesHighBitMember).
        var enumType = TypeRef.Definition("synthetic", "", "CfgFlags");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new Constant(-2147483647, intType),
            new LoadArgument(1, "e", enumType))
        {
            MergedType = enumType,
        };
        string body = RenderReturn(
            conditional,
            enumType,
            [new Parameter("c", boolType), new Parameter("e", enumType)],
            enumType,
            underlying: TypeRef.CoreLib("System", "UInt32"),
            // Top keys by the signed int payload (-2147483648), mirroring how the
            // real member map resolved the #2076 fixture's `ldc.i4` constant.
            members: new Dictionary<long, string> { [0] = "None", [-2147483648L] = "Top" });

        Assert.Contains("unchecked((CfgFlags)(-2147483647))", body);
        Assert.DoesNotContain("CfgFlags.Top", body);
        AssertCompiles(
            "public static CfgFlags M(bool c, CfgFlags e)",
            body,
            "public enum CfgFlags : uint { None = 0, Top = 0x80000000u }");
    }

    [Fact]
    public void UnretypedNamedConstant_AtKnownEnumSink_RendersMemberName()
    {
        // A long-payload constant TypedConstantsPass (int-only) never retyped:
        // the sink coercion still spells the member name, not `(LEnum)2`.
        var enumType = TypeRef.Definition("synthetic", "", "LEnum");
        var longType = TypeRef.CoreLib("System", "Int64");
        string body = RenderReturn(
            new Constant(2L, longType),
            enumType,
            [],
            enumType,
            underlying: longType,
            members: new Dictionary<long, string> { [2] = "High" });

        Assert.Contains("return LEnum.High;", body);
        Assert.DoesNotContain("(LEnum)2", body);
        AssertCompiles("public static LEnum M()", body, "public enum LEnum : long { Low = 0, High = 2 }");
    }

    [Fact]
    public void UnretypedUnnamedConstant_AtKnownEnumSink_KeepsOverflowAwareCast()
    {
        // The cast half of the name-or-cast rule is unchanged: no matching
        // member falls back to the overflow-aware enum cast.
        var enumType = TypeRef.Definition("synthetic", "", "LEnum");
        var longType = TypeRef.CoreLib("System", "Int64");
        string body = RenderReturn(
            new Constant(7L, longType),
            enumType,
            [],
            enumType,
            underlying: longType,
            members: new Dictionary<long, string> { [2] = "High" });

        Assert.Contains("return (LEnum)7;", body);
        AssertCompiles("public static LEnum M()", body, "public enum LEnum : long { Low = 0, High = 2 }");
    }

    [Fact]
    public void PointerTarget_NativeZero_RendersNull()
    {
        // #2338 A1: CoreLib UnmanagedMemoryAccessor initializes byte* locals
        // from `ldc.i4.0; conv.u`. Rendering the native zero as `(nuint)0` at a
        // pointer target is CS0266; the pointer null spelling is valid and
        // matches the IL null pointer.
        var bytePointer = TypeRef.Pointer(TypeRef.CoreLib("System", "Byte"));
        var nativeUInt = TypeRef.CoreLib("System", "UIntPtr");
        string body = RenderBody(
            [new StoreLocal(0, bytePointer, new ILInspector.Decompiler.Pipeline.Convert(nativeUInt, isChecked: false, isUnsigned: true, new Constant(0, TypeRef.CoreLib("System", "Int32"))))],
            TypeRef.CoreLib("System", "Void"),
            [],
            [bytePointer]);

        Assert.Contains("byte* V_0 = (byte*)null;", body);
        Assert.DoesNotContain("(nuint)0", body);
        AssertCompiles("public static unsafe void M()", body);
    }

    [Fact]
    public void PointerArgument_NativeZero_RendersTypedNull()
    {
        // Gemini review: bare null is target-typed in a local initializer, but
        // ambiguous at overloaded pointer call sites. Keep the pointer type.
        var holder = TypeRef.Definition("synthetic", "", "Holder");
        var bytePointer = TypeRef.Pointer(TypeRef.CoreLib("System", "Byte"));
        var nativeUInt = TypeRef.CoreLib("System", "UIntPtr");
        var consume = new MethodRef(holder, "Consume", TypeRef.CoreLib("System", "Void"), [bytePointer], HasThis: false);
        string body = RenderBody(
            [new ExpressionStatement(new Call(
                consume,
                isVirtual: false,
                [new ILInspector.Decompiler.Pipeline.Convert(nativeUInt, isChecked: false, isUnsigned: true, new Constant(0, TypeRef.CoreLib("System", "Int32")))]))],
            TypeRef.CoreLib("System", "Void"),
            [],
            []);

        Assert.Contains("Consume((byte*)null);", body);
        Assert.DoesNotContain("Consume(null)", body);
        Assert.DoesNotContain("(nuint)0", body);
        AssertCompiles(
            "public static unsafe void M()",
            body,
            "public static unsafe class Holder { public static void Consume(byte* p) { } public static void Consume(int* p) { } }");
    }

    [Fact]
    public void PointerTarget_NonZeroNativeInteger_RendersExplicitPointerCast()
    {
        // Gemini review round 2: the same pointer-target rule must cover
        // non-zero native integers, not only null. IL carries conv.u into a
        // pointer local; C# needs the explicit pointer cast.
        var bytePointer = TypeRef.Pointer(TypeRef.CoreLib("System", "Byte"));
        var nativeUInt = TypeRef.CoreLib("System", "UIntPtr");
        var intType = TypeRef.CoreLib("System", "Int32");
        string body = RenderBody(
            [new StoreLocal(0, bytePointer, new ILInspector.Decompiler.Pipeline.Convert(nativeUInt, isChecked: false, isUnsigned: true, new LoadArgument(0, "arg", intType)))],
            TypeRef.CoreLib("System", "Void"),
            [new Parameter("arg", intType)],
            [bytePointer]);

        Assert.Contains("byte* V_0 = (byte*)((nuint)(uint)arg);", body);
        Assert.DoesNotContain("byte* V_0 = (nuint)arg;", body);
        AssertCompiles("public static unsafe void M(int arg)", body);
    }

    [Fact]
    public void NonThrowingNumericCoerce_InCheckedRegion_DoesNotWrapUnchecked()
    {
        // #2338 B3: byte->char is explicit but cannot throw in checked C#.
        // The cast is required, the unchecked wrapper is pure noise.
        var intType = TypeRef.CoreLib("System", "Int32");
        var byteType = TypeRef.CoreLib("System", "Byte");
        var charType = TypeRef.CoreLib("System", "Char");
        var checkedAdd = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new LoadArgument(0, "x", intType),
            new Coerce(charType, new LoadArgument(1, "b", byteType)));

        string body = RenderReturn(
            checkedAdd,
            intType,
            [new Parameter("x", intType), new Parameter("b", byteType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("(char)b", body);
        Assert.DoesNotContain("unchecked((char)b)", body);
        AssertCompiles("public static int M(int x, byte b)", body);
    }

    [Fact]
    public void NonThrowingSubsumedConvert_InCheckedRegion_DoesNotWrapUnchecked()
    {
        // Same no-throw rule through the Convert-subsumed exit:
        // conv.u2 feeding a char target can spell as one `(char)b` cast.
        var intType = TypeRef.CoreLib("System", "Int32");
        var byteType = TypeRef.CoreLib("System", "Byte");
        var ushortType = TypeRef.CoreLib("System", "UInt16");
        var charType = TypeRef.CoreLib("System", "Char");
        var checkedAdd = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new LoadArgument(0, "x", intType),
            new Coerce(charType, new ILInspector.Decompiler.Pipeline.Convert(ushortType, isChecked: false, isUnsigned: false, new LoadArgument(1, "b", byteType))));

        string body = RenderReturn(
            checkedAdd,
            intType,
            [new Parameter("x", intType), new Parameter("b", byteType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("(char)b", body);
        Assert.DoesNotContain("unchecked((char)b)", body);
        Assert.DoesNotContain("(char)((ushort)b)", body);
        AssertCompiles("public static int M(int x, byte b)", body);
    }

    // #2302 canaries: the join-arm rule's third direction — a primitive arm at
    // a same-family primitive MergedType it cannot reach implicitly. The
    // pre-F1 fold shipped these bare (CS0029/CS0266); the latent class needs
    // synthetic fixtures because F1 cleared the live corpus population.
    [Fact]
    public void NegativeConstantArm_AtUnsignedMergedType_ReintepretsUnchecked()
    {
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new LoadArgument(1, "u", uintType),
            new Constant(-1, intType))
        {
            MergedType = uintType,
        };
        string body = RenderReturn(
            conditional,
            uintType,
            [new Parameter("c", boolType), new Parameter("u", uintType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("c ? u : unchecked((uint)(-1))", body);
        AssertCompiles("public static uint M(bool c, uint u)", body);
    }

    [Fact]
    public void NonConstantIntArm_AtUnsignedMergedType_TakesReinterpretCast()
    {
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new LoadArgument(1, "u", uintType),
            new LoadArgument(2, "x", intType))
        {
            MergedType = uintType,
        };
        string body = RenderReturn(
            conditional,
            uintType,
            [new Parameter("c", boolType), new Parameter("u", uintType), new Parameter("x", intType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("c ? u : (uint)x", body);
        AssertCompiles("public static uint M(bool c, uint u, int x)", body);
    }

    [Fact]
    public void InRangeConstantArm_AtUnsignedMergedType_StaysBare()
    {
        // C#'s implicit constant conversion covers in-range constants — the
        // masked case the F1 review exposed. It must stay bare (no cast churn).
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new LoadArgument(1, "u", uintType),
            new Constant(0, intType))
        {
            MergedType = uintType,
        };
        string body = RenderReturn(
            conditional,
            uintType,
            [new Parameter("c", boolType), new Parameter("u", uintType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("c ? u : 0", body);
        Assert.DoesNotContain("(uint)0", body);
        AssertCompiles("public static uint M(bool c, uint u)", body);
    }

    [Fact]
    public void ImplicitlyWideningArm_AtLongMergedType_StaysBare()
    {
        // int -> long is an implicit conversion; NeedsNumericCast gates the
        // third direction so implicitly-reachable arms never gain cast churn.
        var longType = TypeRef.CoreLib("System", "Int64");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new LoadArgument(1, "l", longType),
            new LoadArgument(2, "x", intType))
        {
            MergedType = longType,
        };
        string body = RenderReturn(
            conditional,
            longType,
            [new Parameter("c", boolType), new Parameter("l", longType), new Parameter("x", intType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("c ? l : x", body);
        Assert.DoesNotContain("(long)x", body);
        AssertCompiles("public static long M(bool c, long l, int x)", body);
    }

    // work-2302 review (both reviewers, blocking): the unchecked(...) wrapper
    // must not absorb NESTED checked operations — a checked add renders bare
    // under an ambient checked region, so wrapping its pre-rendered text
    // silenced its overflow check (`unchecked((uint)(a + b))` where the IL
    // demands `unchecked((uint)checked(a + b))`). CheckedSafeCast now renders
    // its operand with the context cleared so nested checked nodes self-wrap.
    [Fact]
    public void CheckedAddUnderCoerce_InCheckedRegion_KeepsItsOwnCheckedWrapper()
    {
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var checkedInner = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new LoadArgument(1, "a", intType),
            new LoadArgument(2, "b", intType));
        var outer = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new LoadArgument(0, "u", uintType),
            new Coerce(uintType, checkedInner));
        string body = RenderReturn(
            outer,
            uintType,
            [new Parameter("u", uintType), new Parameter("a", intType), new Parameter("b", intType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("unchecked((uint)checked(a + b))", body);
        AssertCompiles("public static uint M(uint u, int a, int b)", body);
    }

    [Fact]
    public void PlainAddUnderCoerce_InCheckedRegion_StaysInsideTheUncheckedWrapper()
    {
        // The dual: a PLAIN add under the reinterpret needs no checked(...) —
        // the cleared context renders it bare and the unchecked wrapper is its
        // faithful home (no double-wrap noise).
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var plainInner = new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new LoadArgument(1, "a", intType),
            new LoadArgument(2, "b", intType));
        var outer = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new LoadArgument(0, "u", uintType),
            new Coerce(uintType, plainInner));
        string body = RenderReturn(
            outer,
            uintType,
            [new Parameter("u", uintType), new Parameter("a", intType), new Parameter("b", intType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("unchecked((uint)(a + b))", body);
        Assert.DoesNotContain("checked(a + b)", body.Replace("unchecked((uint)(a + b))", ""));
        AssertCompiles("public static uint M(uint u, int a, int b)", body);
    }

    [Fact]
    public void CheckedAddArm_AtUnsignedMergedType_InCheckedRegion_KeepsItsCheckedWrapper()
    {
        // The join-arm form of the same finding: the third-direction arm cast
        // must protect checked arithmetic inside the arm.
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var checkedArm = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new LoadArgument(2, "a", intType),
            new LoadArgument(3, "b", intType));
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new LoadArgument(1, "u", uintType),
            checkedArm)
        {
            MergedType = uintType,
        };
        var outer = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new Constant(0, uintType),
            conditional);
        string body = RenderReturn(
            outer,
            uintType,
            [
                new Parameter("c", boolType),
                new Parameter("u", uintType),
                new Parameter("a", intType),
                new Parameter("b", intType),
            ],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("unchecked((uint)checked(a + b))", body);
        AssertCompiles("public static uint M(bool c, uint u, int a, int b)", body);
    }

    // #2333 (round-3 review, GPT-5.5 with a runtime OverflowException repro):
    // the enum->integer JOIN-ARM exit had the same checked-region hazard as
    // the CoerceText exits — a uint-backed enum arm at an int join spells
    // `(int)f`, which recompiles to conv.ovf.i4.un inside checked. It wraps
    // exactly when the underlying->target conversion can throw; the
    // int-backed identity stays bare.
    [Fact]
    public void UnsignedBackedEnumArm_AtIntJoin_InCheckedRegion_WrapsUnchecked()
    {
        var enumType = TypeRef.Definition("synthetic", "", "UFlags");
        var intType = TypeRef.CoreLib("System", "Int32");
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new Constant(1, intType),
            new LoadArgument(1, "f", enumType))
        {
            MergedType = intType,
        };
        var checkedAdd = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new LoadArgument(2, "x", intType),
            conditional);
        string body = RenderReturn(
            checkedAdd,
            intType,
            [new Parameter("c", boolType), new Parameter("f", enumType), new Parameter("x", intType)],
            enumType,
            underlying: uintType);

        Assert.Contains("unchecked((int)f)", body);
        AssertCompiles("public static int M(bool c, UFlags f, int x)", body, "public enum UFlags : uint { }");
    }

    [Fact]
    public void IntBackedEnumArm_AtIntJoin_InCheckedRegion_StaysBare()
    {
        var enumType = TypeRef.Definition("synthetic", "", "IFlags");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new Constant(1, intType),
            new LoadArgument(1, "f", enumType))
        {
            MergedType = intType,
        };
        var checkedAdd = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new LoadArgument(2, "x", intType),
            conditional);
        string body = RenderReturn(
            checkedAdd,
            intType,
            [new Parameter("c", boolType), new Parameter("f", enumType), new Parameter("x", intType)],
            enumType,
            underlying: intType);

        Assert.Contains("(int)f", body);
        Assert.DoesNotContain("unchecked((int)f)", body);
        AssertCompiles("public static int M(bool c, IFlags f, int x)", body, "public enum IFlags { }");
    }

    [Fact]
    public void IntBackedEnumOperand_InCheckedRegion_StaysBare()
    {
        // #2338 B5: the operand-shaped enum cast shares the same checked-region
        // rule. int -> int-backed enum cannot throw in checked C#.
        var enumType = TypeRef.Definition("synthetic", "", "IFlags");
        var intType = TypeRef.CoreLib("System", "Int32");
        var comparison = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadArgument(0, "f", enumType),
            new LoadArgument(1, "raw", intType));
        var checkedAdd = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new LoadArgument(2, "x", intType),
            new Coerce(intType, comparison));
        string body = RenderReturn(
            checkedAdd,
            intType,
            [new Parameter("f", enumType), new Parameter("raw", intType), new Parameter("x", intType)],
            enumType,
            underlying: intType);

        Assert.Contains("(IFlags)raw", body);
        Assert.DoesNotContain("unchecked((IFlags)raw)", body);
        AssertCompiles("public static int M(IFlags f, int raw, int x)", body, "public enum IFlags { }");
    }

    [Fact]
    public void ByteBackedEnumOperand_InCheckedRegion_WrapsUnchecked()
    {
        var enumType = TypeRef.Definition("synthetic", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var byteType = TypeRef.CoreLib("System", "Byte");
        var comparison = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadArgument(0, "f", enumType),
            new LoadArgument(1, "raw", intType));
        var checkedAdd = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new LoadArgument(2, "x", intType),
            new Coerce(intType, comparison));
        string body = RenderReturn(
            checkedAdd,
            intType,
            [new Parameter("f", enumType), new Parameter("raw", intType), new Parameter("x", intType)],
            enumType,
            underlying: byteType);

        Assert.Contains("unchecked((Tiny)raw)", body);
        AssertCompiles("public static int M(Tiny f, int raw, int x)", body, "public enum Tiny : byte { }");
    }

    [Fact]
    public void BoolEnumOperand_InCheckedRegion_StaysBare()
    {
        var enumType = TypeRef.Definition("synthetic", "", "Tiny");
        var intType = TypeRef.CoreLib("System", "Int32");
        var byteType = TypeRef.CoreLib("System", "Byte");
        var boolComparison = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadArgument(1, "a", intType),
            new LoadArgument(2, "b", intType));
        var enumComparison = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadArgument(0, "f", enumType),
            boolComparison);
        var checkedAdd = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new LoadArgument(3, "x", intType),
            new Coerce(intType, enumComparison));
        string body = RenderReturn(
            checkedAdd,
            intType,
            [
                new Parameter("f", enumType),
                new Parameter("a", intType),
                new Parameter("b", intType),
                new Parameter("x", intType),
            ],
            enumType,
            underlying: byteType);

        Assert.Contains("(Tiny)(a == b ? 1 : 0)", body);
        Assert.DoesNotContain("unchecked((Tiny)(a == b ? 1 : 0))", body);
        AssertCompiles("public static int M(Tiny f, int a, int b, int x)", body, "public enum Tiny : byte { }");
    }

    // The CI-caught dual pair: an enum->underlying cast in a checked region
    // wraps only when the checked conversion can actually throw. Identity
    // (int-backed -> int) stays bare — EnumUnderlyingCastTests pins that side;
    // cross-signedness (uint-backed -> int) must wrap.
    [Fact]
    public void UnsignedBackedEnumCast_ToInt_InCheckedRegion_WrapsUnchecked()
    {
        var enumType = TypeRef.Definition("synthetic", "", "UFlags");
        var intType = TypeRef.CoreLib("System", "Int32");
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var checkedAdd = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new Coerce(intType, new LoadArgument(0, "f", enumType)),
            new LoadArgument(1, "x", intType));
        string body = RenderReturn(
            checkedAdd,
            intType,
            [new Parameter("f", enumType), new Parameter("x", intType)],
            enumType,
            underlying: uintType);

        Assert.Contains("unchecked((int)f)", body);
        AssertCompiles("public static int M(UFlags f, int x)", body, "public enum UFlags : uint { }");
    }

    // #2301: a cross-signedness reinterpret cast rendered inside a lexical
    // checked region must wrap in unchecked(...) — bare `(uint)x` there
    // recompiles to a conv.ovf.u4 the IL never had (and throws on negative x).
    [Fact]
    public void CrossSignednessCoerce_InsideCheckedBinary_WrapsUnchecked()
    {
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var checkedAdd = new Binary(
            BinaryKind.Add,
            isChecked: true,
            isUnsigned: false,
            new LoadArgument(0, "u", uintType),
            new Coerce(uintType, new LoadArgument(1, "x", intType)));
        string body = RenderReturn(
            checkedAdd,
            uintType,
            [new Parameter("u", uintType), new Parameter("x", intType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("unchecked((uint)x)", body);
        AssertCompiles("public static uint M(uint u, int x)", body);
    }

    // #2306: an int-MergedType Conditional at a uint sink — the live
    // SpinThenBlockingWait shape (`uint V_1 = c ? 0 : Environment.TickCount;`
    // shipped CS0266). The sink target distributes into the arms: the in-range
    // constant stays bare, the int expression takes the reinterpret cast.
    [Fact]
    public void IntConditional_AtUnsignedSink_DistributesTargetIntoArms()
    {
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new Constant(0, intType),
            new LoadArgument(1, "x", intType))
        {
            MergedType = intType,
        };
        string body = RenderReturn(
            conditional,
            uintType,
            [new Parameter("c", boolType), new Parameter("x", intType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("c ? 0 : (uint)x", body);
        AssertCompiles("public static uint M(bool c, int x)", body);
    }

    // C#'s natural type is computed TYPE-level: `sbyte s = c ? 127 : (sbyte)x;`
    // is CS0266 (127 contributes int; sbyte -> int gives the join a natural
    // type of int, and target-typing never rescues a conditional that HAS a
    // natural type — AssertCompiles refuted the value-aware theory). So this
    // join distributes: the in-range constant takes `(sbyte)127`, and the
    // stale pipeline Coerce{int} arm re-targets its OPERAND — a single cast,
    // never the `(sbyte)((sbyte)value)` double of the corpus audit.
    [Fact]
    public void NaturalTypePoisonedConditional_DistributesSingleCastPerArm()
    {
        var sbyteType = TypeRef.CoreLib("System", "SByte");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new Constant(127, intType),
            new Coerce(intType, new Pipeline.Convert(sbyteType, isChecked: false, isUnsigned: false, new LoadArgument(1, "x", intType))))
        {
            MergedType = intType,
        };
        string body = RenderReturn(
            conditional,
            sbyteType,
            [new Parameter("c", boolType), new Parameter("x", intType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.Contains("c ? (sbyte)127 : (sbyte)x", body);
        Assert.DoesNotContain("(sbyte)((sbyte)", body);
        AssertCompiles("public static sbyte M(bool c, int x)", body);
    }

    // The cross-family refusal: a long-armed conditional at a uint sink must
    // NOT distribute (the cast would be the place that discovers a wrong
    // join); it stays on the merge-node bail.
    [Fact]
    public void LongConditional_AtUnsignedSink_DeclinesDistribution()
    {
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var longType = TypeRef.CoreLib("System", "Int64");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var conditional = new Conditional(
            new LoadArgument(0, "c", boolType),
            new LoadArgument(1, "a", longType),
            new LoadArgument(2, "b", longType))
        {
            MergedType = longType,
        };
        string body = RenderReturn(
            conditional,
            uintType,
            [new Parameter("c", boolType), new Parameter("a", longType), new Parameter("b", longType)],
            TypeRef.Definition("synthetic", "", "UnusedEnum"));

        Assert.DoesNotContain("(uint)a", body);
        Assert.DoesNotContain("(uint)(c", body);
    }

    static string RenderReturn(
        IrExpression value,
        TypeRef returnType,
        IReadOnlyList<Parameter> parameters,
        TypeRef enumType,
        TypeRef? underlying = null,
        IReadOnlyDictionary<long, string>? members = null)
    {
        var block = new Block(0);
        block.Add(new Return(value));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(returnType, [.. parameters], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum },
            EnumUnderlyingTypes = underlying is null
                ? new Dictionary<TypeRef, TypeRef>()
                : new Dictionary<TypeRef, TypeRef> { [enumType] = underlying },
            EnumMembers = members is null
                ? new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>()
                : new Dictionary<TypeRef, IReadOnlyDictionary<long, string>> { [enumType] = members },
        };

        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static string RenderBody(
        IReadOnlyList<IrNode> statements,
        TypeRef returnType,
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<TypeRef> locals)
    {
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(returnType, [.. parameters], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.Definition("synthetic", "", "Holder"), signature, [.. locals], container);
        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static void AssertCompiles(string header, string body, string extraDeclarations = "")
    {
        var errors = Recompile(header, body, extraDeclarations)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToArray();
        Assert.True(errors.Length == 0, "Rendered body must compile, got:\n  " + string.Join("\n  ", errors) + "\n--- body ---\n" + body);
    }

    static ImmutableArray<Diagnostic> Recompile(string methodHeader, string body, string extraDeclarations)
    {
        string source = $$"""
            using System;
            {{extraDeclarations}}
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
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        return compilation.GetDiagnostics();
    }

    static ImmutableArray<MetadataReference> RuntimeReferences()
    {
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (string path in (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;
            try { references.Add(MetadataReference.CreateFromFile(path)); }
            catch { }
        }
        return references.ToImmutable();
    }
}
