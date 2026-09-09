namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Fixture for issue #3022: a <c>switch</c> expression over a plain receiver
/// (an ordinary expression, not a discriminated-union <c>.Value</c>) with a
/// bare type-pattern arm and a single-level property-subpattern arm, both
/// <c>when</c>-guarded, plus a <c>_ =&gt; false</c> default. This is the exact
/// shape <c>YieldBreakLoopIteratorReconstruction.TryNormalizeContinueCondition</c>
/// lowers to and <see cref="ILInspector.Decompiler.Pipeline.PatternSwitchExpressionPass"/>
/// raises. Isolated from <see cref="CfgSampleClass"/> so it does not perturb the
/// fidelity gate's pinned population.
/// </summary>
public static class PatternSwitchSample
{
    public abstract class Node
    {
    }

    public sealed class Leaf : Node
    {
        public Leaf(int weight) => Weight = weight;

        public int Weight { get; }
    }

    public sealed class Wrapper : Node
    {
        public Wrapper(Node inner) => Inner = inner;

        public Node Inner { get; }
    }

    public static bool Classify(Node node, int threshold, out int result)
    {
        result = 0;
        return node switch
        {
            Leaf leaf when Exceeds(leaf.Weight, threshold) => Capture(leaf.Weight, out result),
            Wrapper { Inner: Leaf inner } when Exceeds(inner.Weight, threshold) => Capture(-inner.Weight, out result),
            _ => false,
        };
    }

    static bool Exceeds(int value, int threshold) => value > threshold;

    static bool Capture(int value, out int result)
    {
        result = value;
        return true;
    }
}
