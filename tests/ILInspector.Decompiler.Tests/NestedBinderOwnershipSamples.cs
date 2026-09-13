namespace ILInspector.Decompiler.Tests;

public static class NestedBinderOwnershipSamples
{
    public static int CapturingLocalWithNestedLambda(int captured)
    {
        int Outer(int value) =>
            ((Func<int, int, int>)((first, second) => first + second))(
                1,
                captured + value);

        return Outer(0);
    }
}
