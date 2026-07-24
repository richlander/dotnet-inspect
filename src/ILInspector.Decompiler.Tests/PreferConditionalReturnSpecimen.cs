namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Guarded-boolean-return shapes for the opt-in
/// <see cref="ILInspector.Decompiler.Pipeline.PrinterOptions.PreferConditionalExpressionReturn"/>
/// style lens (#3138). Each method compiles to an <c>if (c) return A; return B;</c>
/// pair with <see cref="bool"/> arms — the shape the default view leaves flat
/// because no short-circuit fold of it is opcode-faithful (#3114). The lens
/// re-offers those as <c>return c ? A : B;</c>. <see cref="And"/> is the no-op
/// negative — no guarded return at all, so the lens leaves it byte-identical.
/// </summary>
public static class PreferConditionalReturnSpecimen
{
    // Declined single-constant-arm case: `if (a && b) return false; return c;`.
    // The default declines the `(a && b) && c` fold (short-circuit is not
    // opcode-faithful here), so it renders flat. Lens: `return (a && b) ? false : c;`.
    public static bool NeitherOr(bool a, bool b, bool c)
    {
        if (a && b)
        {
            return false;
        }

        return c;
    }

    // Both arms variable: the default treats a general (non-constant-arm) bool
    // ternary return as a separate decision and leaves it flat. Lens: `return a ? b : c;`.
    public static bool GuardBothVariable(bool a, bool b, bool c)
    {
        if (a)
        {
            return b;
        }

        return c;
    }

    // Counter-shape for the IDE0075 boundary: `if (a) return true; return b;`.
    // The default leaves this flat too (no short-circuit fold is opcode-faithful),
    // so the lens rewrites it — but to the honest IDE0046 form `a ? true : b`, NOT
    // the further `a || b` simplification. That `? true :` -> `||` collapse is
    // IDE0075, a separate (deferred) knob, so this lens keeps the literal arm.
    public static bool OrShapedGuard(bool a, bool b)
    {
        if (a)
        {
            return true;
        }

        return b;
    }

    // Pure no-op negative: no guarded return at all, so the lens finds nothing and
    // its output is byte-identical to the default.
    public static bool And(bool a, bool b) => a & b;
}
