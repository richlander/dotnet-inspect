namespace ILInspector.Decompiler.Tests;

public static class NestedBinderOwnershipSamples
{
    public static Func<int, Func<int, int>> NonCapturingLambdaWithNestedLambda() =>
        _ => item => item * 2;

    public static Func<int, int> CapturingLambdaWithNestedLambda(
        int captured) =>
        value => ((Func<int, int>)(item => item * 2))(captured + value);

    public static int CapturingLocalWithNestedLambda(int captured)
    {
        int Outer(int value) =>
            ((Func<int, int, int>)((first, second) => first + second))(
                1,
                captured + value);

        return Outer(0);
    }
}
