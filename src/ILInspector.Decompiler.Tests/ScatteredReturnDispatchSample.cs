using System;

namespace ILInspector.Decompiler.Tests;

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

    static bool Win(int v, out int x) { x = v; return true; }

    static bool Fail(out int x) { x = 0; return false; }

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
