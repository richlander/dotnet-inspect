using System.Collections.Generic;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Compiled specimens for the byte-neutral Formatting (whitespace-only) and Synthesis
/// (local-name-only) knobs. Each method exercises the exact construct its knob rewrites
/// so the byte-neutrality gate can prove — not merely assert by tier — that turning the
/// knob on changes only layout or local identifiers, never the IL.
/// </summary>
public sealed class FormattingSynthesisSpecimen
{
    // Synthesis: a method with ordinary source-named locals. The test assembly carries
    // an embedded PDB, so ProduceMember binds the real names (sum, i) even with
    // pdbPath:null and readable-local-names stays inert here — its synthesis only fires
    // for a local the PDB does not name (a compiler temporary). The gate pins that inert
    // state; see ByteNeutralityGateTests for the structural byte-neutrality rationale.
    public static int ReadableLocal()
    {
        int sum = 0;
        for (int i = 0; i < 10; i++)
            sum += i * i;
        return sum;
    }

    // Formatting: an expression-bodied member whose => wrap-expression-body-arrow moves
    // onto the next line.
    public static int ArrowBody() => 42;

    // Formatting: a short-circuit && chain long enough (> the 120-column wrap width) that
    // wrap-splittable-expressions breaks it one operand per line. The single int operand
    // keeps every comparison non-constant (so nothing folds away) and the signature simple.
    public static bool LongLogicalChain(int n)
        => n > 0 && n > 1 && n > 2 && n > 3 && n > 4 && n > 5 && n > 6 && n > 7 && n > 8 && n > 9 && n > 10 && n > 11 && n > 12;

    // Formatting: a fluent instance-call chain long enough that the always-on wrapper
    // breaks it by default; disable-one-liner-wrapping keeps it on a single line.
    public static string LongFluentChain()
        => new System.Text.StringBuilder().Append("alpha").Append("beta").Append("gamma").Append("delta").Append("epsilon").Append("zeta").ToString();
}
