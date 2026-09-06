namespace ILInspector.Metadata.Tests;

internal static class CustomAttributeGenericContextSamples
{
    [Pair<int, int>(1, 2, 3, 4)]
    public sealed class PairFour;

    [Pair<int, int>(1, 2, 3, 4, 5, 6, 7, 8)]
    public sealed class PairEight;

    [Repeated<int, int, int, int>(1, 2, 3, 4)]
    public sealed class RepeatedFour;

    [Repeated<int, int, int, int>(1, 2, 3, 4, 5, 6, 7, 8)]
    public sealed class RepeatedEight;

    [Alternating<int, int, int, int>(1, 2, 3, 4, 5, 6, 7, 8)]
    public sealed class AlternatingEight;

    [Ascending<int, int, int, int>(1, 2, 3, 4)]
    public sealed class AscendingFour;

    [Descending<int, int, int, int>(1, 2, 3, 4)]
    public sealed class DescendingFour;

    [Unused<int, Dictionary<string, int>>(1)]
    public sealed class UnusedTail;

    [Mixed<int, long[], string>("first", new long[] { 2, 3 }, 4, "last")]
    public sealed class MixedValues;

    sealed class PairAttribute<T0, T1> : Attribute
    {
        public PairAttribute(T1 a, T0 b, T1 c, T0 d) { }
        public PairAttribute(T1 a, T0 b, T1 c, T0 d, T1 e, T0 f, T1 g, T0 h) { }
    }

    sealed class RepeatedAttribute<T0, T1, T2, T3> : Attribute
    {
        public RepeatedAttribute(T3 a, T3 b, T3 c, T3 d) { }
        public RepeatedAttribute(T3 a, T3 b, T3 c, T3 d, T3 e, T3 f, T3 g, T3 h) { }
    }

    sealed class AlternatingAttribute<T0, T1, T2, T3> : Attribute
    {
        public AlternatingAttribute(T3 a, T0 b, T3 c, T0 d, T3 e, T0 f, T3 g, T0 h) { }
    }

    sealed class AscendingAttribute<T0, T1, T2, T3> : Attribute
    {
        public AscendingAttribute(T0 a, T1 b, T2 c, T3 d) { }
    }

    sealed class DescendingAttribute<T0, T1, T2, T3> : Attribute
    {
        public DescendingAttribute(T3 a, T2 b, T1 c, T0 d) { }
    }

    sealed class UnusedAttribute<T0, T1> : Attribute
    {
        public UnusedAttribute(T0 value) { }
    }

    sealed class MixedAttribute<T0, T1, T2> : Attribute
    {
        public MixedAttribute(T2 first, T1 second, T0 third, T2 last) { }
    }
}
