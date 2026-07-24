namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Guarded-boolean-return shapes for the opt-in
/// <see cref="ILInspector.Decompiler.Pipeline.PrinterOptions.PreferBranchlessBoolean"/>
/// style lens (#3138). Each fold method compiles to an
/// <c>if (c) return A; return B;</c> pair with a <see cref="bool"/> constant arm —
/// the shape the default view leaves flat because the short-circuit fold is not
/// opcode-faithful when the surviving operand is a bare load csc would collapse to
/// a branchless <c>&amp;</c>/<c>|</c> (#3114). The lens re-offers those as the
/// compact short-circuit "bool hack".
///
/// The four fold shapes (surviving operand is the bare parameter, so the default
/// declines every one):
/// <list type="bullet">
/// <item><see cref="AndTailGuard"/>: <c>if (a) return b; return false;</c> → <c>a &amp;&amp; b</c></item>
/// <item><see cref="OrTailGuard"/>: <c>if (a) return b; return true;</c> → <c>!a || b</c></item>
/// <item><see cref="OrThenGuard"/>: <c>if (a) return true; return b;</c> → <c>a || b</c></item>
/// <item><see cref="AndThenGuard"/>: <c>if (a) return false; return b;</c> → <c>!a &amp;&amp; b</c></item>
/// </list>
/// </summary>
public static class PreferBranchlessBooleanSpecimen
{
    // if (a) return b; return false; ≡ a && b. Surviving operand `b` is a bare arg,
    // so the default declines the branchless fold and leaves this flat.
    public static bool AndTailGuard(bool a, bool b)
    {
        if (a)
        {
            return b;
        }

        return false;
    }

    // if (a) return b; return true; ≡ !a || b. Negated condition + bare surviving
    // operand; default leaves it flat.
    public static bool OrTailGuard(bool a, bool b)
    {
        if (a)
        {
            return b;
        }

        return true;
    }

    // if (a) return true; return b; ≡ a || b. Bare surviving operand; default flat.
    public static bool OrThenGuard(bool a, bool b)
    {
        if (a)
        {
            return true;
        }

        return b;
    }

    // if (a) return false; return b; ≡ !a && b. Negated condition + bare surviving
    // operand; default flat.
    public static bool AndThenGuard(bool a, bool b)
    {
        if (a)
        {
            return false;
        }

        return b;
    }

    // Both arms variable (no constant): the branchless "bool hack" does not apply
    // (there is no operator to lift the condition into), so the lens is a no-op and
    // leaves this flat. (The ternary lens would fold it; this one must not.)
    public static bool BothVariable(bool a, bool b, bool c)
    {
        if (a)
        {
            return b;
        }

        return c;
    }

    // No guarded return at all: the lens finds nothing and its output is
    // byte-identical to the default.
    public static bool Plain(bool a, bool b) => a & b;

    // User-defined truthiness in the condition: the lens must DECLINE (stay flat),
    // because lifting `t` into `t && b` rebinds to a user-defined `&` that does not
    // exist and changes the runtime result (CS0019 / behavior divergence).
    public static bool UserTruthinessGuard(Truthy t, bool b)
    {
        if (t)
        {
            return b;
        }

        return false;
    }

    // Surviving operand is a managed by-ref dereference: csc's branchless lowering
    // would eagerly dereference `p` on the path the branch had guarded (null-by-ref
    // NRE divergence), so the lens must DECLINE and leave the flat guard.
    public static bool ByRefOperandGuard(bool a, ref bool p)
    {
        if (a)
        {
            return p;
        }

        return false;
    }

    // User-defined truthiness under a negation: `if (t) {} else { return b; }`
    // raises to a guarded return whose condition is `LogicalNot(op_True(t))`. With a
    // `true` tail this is a NEGATING fold shape (`!c || b`): Conditions.Negate
    // unwraps the LogicalNot to the bare `op_True` call the printer strips to `t`,
    // so the direct-call guard would miss it and the lens would emit the
    // uncompilable `t || b` (Truthy has no user `|`). The lens must DECLINE.
    public static bool NegatedUserTruthinessGuard(Truthy t, bool b)
    {
        if (t)
        {
        }
        else
        {
            return b;
        }

        return true;
    }

    // A struct usable in boolean context via operator true/false, but with NO
    // user-defined `&`/`|`, so any short-circuit lift of a `Truthy` condition is
    // uncompilable.
    public readonly struct Truthy
    {
        public static bool operator true(Truthy _) => true;

        public static bool operator false(Truthy _) => false;
    }
}
