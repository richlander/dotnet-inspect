namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Fixture for issue #3028 PR A: a <c>switch</c> expression over a plain receiver
/// whose arms csc lowers to <em>heterogeneous</em> intros over the place read
/// directly — no switch-value temp. csc emits the first arm as an intro-chain
/// (<c>x = place as T; if (x is null) { … }</c>) and the remaining arms as
/// inline-positive siblings (<c>if (place is U y) { … }</c>) nested in the
/// leading arm's no-match branch, bottoming out in the default
/// <c>return</c>. <see cref="ILInspector.Decompiler.Pipeline.PatternSwitchExpressionPass"/>
/// raises the whole cascade back into a single switch expression.
///
/// <see cref="Area"/> is the mixed intro-chain-plus-inline shape (all-unguarded).
/// <see cref="GuardedArea"/> adds a <c>when</c>-guarded first arm — the #3028
/// follow-up that folds a refutable heterogeneous arm through the type-
/// disjointness oracle. Isolated from
/// <see cref="CfgSampleClass"/> so it does not perturb the fidelity gate's pinned
/// population.
/// </summary>
public static class HeterogeneousArmSample
{
    public abstract class Shape
    {
    }

    public sealed class Dot : Shape
    {
        public int Radius;
    }

    public sealed class Bar : Shape
    {
        public int Length;
    }

    public sealed class Box : Shape
    {
        public int Side;
    }

    // csc: intro-chain arm (`Dot d = shape as Dot; if (d is null) …`) followed by
    // inline-positive sibling arms, over `shape` read directly.
    public static int Area(Shape shape) => shape switch
    {
        Dot d => d.Radius,
        Bar b => b.Length,
        Box x => x.Side,
        _ => -1,
    };

    // Heterogeneous cascade whose FIRST arm is `when`-guarded over a type (Dot)
    // disjoint from every later arm (Bar, Box). csc lowers the guarded arm to an
    // `if (d is null) { REST } else { MATCHED }` dispatch whose default is a shared
    // trailing return; a Dot that fails the guard is routed to that default. The
    // fold is faithful ONLY because Dot is provably disjoint from Bar and Box, so
    // it exercises the disjointness oracle on the direct/inline surface.
    public static int GuardedArea(Shape shape, int min) => shape switch
    {
        Dot d when d.Radius > min => d.Radius,
        Bar b => b.Length,
        Box x => x.Side,
        _ => -1,
    };
}
