namespace ILInspector.Decompiler.Tests;

using System;

/// <summary>
/// Fixtures for the #3028 subsumption-oracle follow-up to #3065. Each method is a
/// hand-written <c>if (P is Tk xk) { return vk; } ... return d;</c> ladder that
/// csc lowers to the <em>flat all-inline</em> shape. #3065 anchors every fold on
/// a compiler-lowered intro-chain head (a <c>switch</c>-expression lowering), so
/// these hand-written ladders are deliberately left as if-cascades rather than
/// reshaped — which is exactly why a subsuming (<see cref="Subsumed"/>),
/// variance-reachable (<see cref="Variance"/>), or guarded-overlapping
/// (<see cref="GuardedOverlap"/>) ladder can never reach a fold and emit a
/// CS8510 <c>switch</c>. The types also feed the direct
/// <see cref="ILInspector.Decompiler.Pipeline.MetadataSource"/> subsumption-oracle
/// unit tests. Isolated from <see cref="CfgSampleClass"/> so it does not perturb
/// the fidelity gate's pinned population.
/// </summary>
public static class InlinePatternSwitchSample
{
    public class Shape
    {
        public int Area;
    }

    public sealed class Circle : Shape
    {
        public int Radius;
    }

    // A hand-written ladder whose earlier arm type (Shape) subsumes a later one
    // (Circle : Shape). As an `if` ladder this is valid C# — the second test is
    // reachable at compile time — but the equivalent `switch` expression is
    // rejected (CS8510: the `Circle` arm is unreachable, already handled by
    // `Shape`). csc emits the same flat inline `is` shape as the foldable cases,
    // so the fold must DECLINE here on the metadata subsumption proof. Both types
    // are same-assembly, so the oracle resolves the relationship precisely.
    public static int Subsumed(object value)
    {
        if (value is Shape shape)
        {
            return shape.Area;
        }
        if (value is Circle circle)
        {
            return circle.Radius;
        }
        return 0;
    }

    public interface ICovariant<out T>
    {
    }

    // A hand-written ladder whose earlier arm (ICovariant<object>) subsumes a
    // later arm (ICovariant<string>) through generic covariance: `out T` makes
    // `ICovariant<string>` assignable to `ICovariant<object>`, so the second
    // test is unreachable in a `switch` (CS8510) even though no nominal base or
    // interface edge connects the two constructed interfaces. As an `if` ladder
    // it is valid C#. The subsumption oracle cannot see the variance conversion
    // in the nominal closure, so it must report Unknown for a constructed
    // generic `earlier` and the fold must DECLINE. Guards against a false `No`.
    public static int Variance(object value)
    {
        if (value is ICovariant<object> objects)
        {
            return objects.GetHashCode();
        }
        if (value is ICovariant<string> strings)
        {
            return strings.GetHashCode();
        }
        return 0;
    }

    // A hand-written ladder whose first arm carries a guard and whose type
    // (IComparable) overlaps a later arm (ICloneable) — `string` satisfies both.
    // The inline fold must DECLINE here: a `switch` routes a failed `when` to the
    // next arm, but this ladder's guard-fail returns the shared default and exits,
    // so for a short-circuiting value the two forms disagree on which arm wins.
    // csc emits the same flat inline `is` shape as the foldable cases, so this is
    // the compiled canary that the guard restriction holds.
    public static int GuardedOverlap(object value, bool flag)
    {
        if (value is IComparable comparable)
        {
            if (flag)
            {
                return comparable.GetHashCode();
            }
            return 0;
        }
        if (value is ICloneable cloneable)
        {
            return cloneable.GetHashCode();
        }
        return 0;
    }
}
