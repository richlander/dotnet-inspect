namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Long-constant shapes for the opt-in
/// <see cref="ILInspector.Decompiler.Pipeline.PrinterOptions.PreferLongLiteralSuffix"/>
/// style lens (#3347). The lens folds the IR shape
/// <c>Convert(→Int64, Int32 Constant)</c> — what csc's <c>ldc.i4(.s) N; conv.i8</c>
/// imports as — into the idiomatic <c>NL</c> literal, and must leave a genuine
/// <c>ldc.i8</c> (a bare <c>Int64</c> constant, no <c>Convert</c> over it) alone.
///
/// <para>The split between the IL encodings is a property of csc's own literal
/// emission, not of the decompiler: csc uses <c>ldc.i4</c>+<c>conv.i8</c> for
/// the signed-int-range cases below, <c>ldc.i4</c>+<c>conv.u8</c> for the first
/// positive value beyond that range, and <c>ldc.i8</c> for larger values. That is
/// why every method below is authored as a plain C# constant — the compiler, not
/// the fixture, chooses the opcode, so the
/// fixture is a real compiled canary for the shape rather than an assumption
/// about it. The value at the int/long boundary is covered from both sides:
/// <see cref="IntMaxValue"/> uses <c>conv.i8</c>, while
/// <see cref="JustPastIntMaxValue"/> uses the distinct
/// <c>ldc.i4 int.MinValue; conv.u8</c> zero-extension encoding. The small
/// <c>ldc.i8</c> case C# cannot author at all is pinned synthetically in
/// <c>LongLiteralFoldTests</c>.</para>
/// </summary>
public static class LongLiteralFoldFixture
{
    // --- conv.i8 sources: the lens folds these ---

    // The reference witness's shape (CfgSampleClass.InlineArraySpanTernaryConditionValue),
    // reduced: two `ldc.i4.s N; conv.i8` arms feeding an `add`, which is what keeps the
    // join a real `?:` — a bare `return c ? 10L : 20L;` compiles to two returns and
    // never reaches the conditional-arm seam at all.
    public static long TernaryArms(bool c, long tail) => (c ? 10L : 20L) + tail;

    // Return position: the value flows through the return sink's coercion, not an
    // arm join. `ldc.i4.s 42; conv.i8; ret`.
    public static long SmallReturn() => 42L;

    // Argument position: the coercion sink is the parameter type, and the folded
    // literal has to stay a well-formed argument.
    public static long SmallArgument() => Consume(7L);

    // Binary operand: exercises precedence. A cast operand and a literal operand
    // report different precedences, so the fold must carry its own rather than
    // inherit the cast's.
    public static long BinaryOperand(long x) => x * 3L;

    // Negative literal operand — the fold's text starts with a unary `-`, so this is
    // the precedence case a bare Primary claim would get wrong.
    public static long NegativeBinaryOperand(long x) => x * -1L;

    // Boundary and sign coverage, all still `ldc.i4*; conv.i8`.
    public static long Zero() => 0L;

    public static long MinusOne() => -1L;

    public static long IntMinValue() => int.MinValue;

    // The largest long constant csc still encodes as `ldc.i4`.
    public static long IntMaxValue() => int.MaxValue;

    // --- non-conv.i8 sources: the lens must leave these exactly as they are ---

    // One past int.MaxValue: csc encodes the bits as int.MinValue and applies
    // conv.u8, so the printer must retain the zero-extended positive value.
    public static long JustPastIntMaxValue() => 2147483648L;

    // A comfortably large `ldc.i8`, the issue's own example.
    public static long LargeReturn() => 5_000_000_000L;

    // An `ldc.i8` in the same ternary-arm position the fold fires in, so the close
    // negative is pinned at the seam and not only at a return.
    public static long LargeTernaryArms(bool c, long tail) => (c ? 5_000_000_000L : 6_000_000_000L) + tail;

    public static long Consume(long value) => value;
}
