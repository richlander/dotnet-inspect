using System;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Enum governing type for <see cref="ScatteredReturnDispatchSample.ClassifyEnum"/>:
/// its comparison-chain labels are int-backed but must render as member names.
/// </summary>
public enum DispatchKind
{
    Zero = 0,
    One = 1,
    Two = 2,
}

/// <summary>
/// Fixtures for issue #2978: a nested type-pattern <c>switch</c> expression whose
/// shared <c>_ =&gt; Fail</c> default is reached by two conditional guards at
/// different nesting levels (the outer <c>is not int</c> and the inner
/// <c>i &lt;= 0</c>). In block order a self-contained sibling region (the inner
/// <c>string</c>-length switch) is interleaved between the two guards, so
/// <see cref="ILInspector.Decompiler.Pipeline.StructuringPass"/> must recognize
/// the guards as a scattered dispatch and duplicate the default return into each
/// guard — otherwise the <c>i &lt;= 0</c> path falls off the end of a non-void
/// method (CS0177: <c>out</c> parameter unassigned).
///
/// The <see cref="PlainAnd"/> and <see cref="ThrowTernaryChain"/> canaries are the
/// close negatives: contiguous short-circuit <c>&amp;&amp;</c> guard chains whose
/// shared return must stay combined under one condition (the #640 fidelity
/// canary), not unrolled into duplicated returns.
/// </summary>
public static class ScatteredReturnDispatchSample
{
    // #2978 witness: the i <= 0 path must return the duplicated default.
    public static bool Dispatch(object o, out int x) => o switch
    {
        string s => s.Length switch
        {
            0 => Fail(out x),
            1 => Win(1, out x),
            _ => Fail(out x)
        },
        int i when i > 0 => Win(i, out x),
        _ => Fail(out x)
    };

    // #3514 witness: the default has one direct guard-failure edge and one
    // type-test-failure edge routed through a pure goto trampoline.
    public static int GuardedTypeAfterSibling(object value) => value switch
    {
        string text => text.Length,
        int number when number > 0 => number,
        _ => -1
    };

    static bool Win(int v, out int x) { x = v; return true; }

    static bool Fail(out int x) { x = 0; return false; }

    // Slice-1 isolate: the inner comparison-chain value switch expression on its
    // own. csc lowers this to an equality chain (brfalse/beq) whose arms each
    // store one dedicated result temp read once at the return join — the faithful
    // signal SwitchRaisingPass recognizes.
    public static bool ClassifyLength(string s, out int x) => s.Length switch
    {
        0 => Fail(out x),
        1 => Win(1, out x),
        _ => Fail(out x)
    };

    // Close negative: a hand-written equality chain returning directly. It reads
    // s.Length once per test and has no result temp or convergence read, so it
    // must stay if/else and never raise to a switch expression.
    public static bool ClassifyLengthManual(string s, out int x)
    {
        if (s.Length == 0)
        {
            return Fail(out x);
        }

        if (s.Length == 1)
        {
            return Win(1, out x);
        }

        return Fail(out x);
    }

    // Close negative for the unsigned-label bug: the same store-to-temp
    // switch-expression shape but on a uint governing value whose label is
    // uint.MaxValue (IL ldc.i4.m1). That label is recorded as a negative int32,
    // so raising would misprint it as -1 (CS0031: -1 cannot convert to uint).
    // The raiser declines a uint governing value with a negative label and
    // leaves it to the other raisers.
    public static int ClassifyUnsigned(uint u) => u switch
    {
        0u => 10,
        uint.MaxValue => 20,
        _ => 30
    };

    // Positive: an enum governing value. Labels are int-backed, but the printer
    // renders them as enum member names via the governing type, so the raise is
    // faithful and must be preserved (an Int32-only guard wrongly declined these).
    public static bool ClassifyEnum(DispatchKind k, out int x) => k switch
    {
        DispatchKind.Zero => Fail(out x),
        DispatchKind.One => Win(1, out x),
        _ => Fail(out x)
    };

    // Positive: a byte governing value. Its labels are small non-negative int32
    // constants that print faithfully, so it must keep raising.
    public static bool ClassifyByte(byte b, out int x) => b switch
    {
        0 => Fail(out x),
        1 => Win(1, out x),
        _ => Fail(out x)
    };

    // #640 canary: two contiguous guards on a shared return — must stay `a && b`.
    public static int PlainAnd(int a, int b)
    {
        if (a > 0 && b > 0)
        {
            return 1;
        }

        return 2;
    }

    // Round-2 witness: a throw in a ternary inside `&&` — must stay combined.
    public static bool ThrowTernaryChain(bool a, bool b, bool c)
        => a && (b ? c : throw new Exception()) && Other();

    static bool Other() => true;
}
