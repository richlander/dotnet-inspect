using System.Runtime.CompilerServices;

namespace ILInspector.Analysis.MalformedOwnershipFixtures;

public static class MalformedAsyncOwnershipFixture
{
    public sealed class NotAStateMachine
    {
    }

    [AsyncStateMachine(typeof(NotAStateMachine))]
    public static bool PoisonedGenericBoxEquals<T>(
        T left,
        T right) =>
        left!.Equals(right);

    [AsyncStateMachine(typeof(NotAStateMachine))]
    public static object PoisonedBoxedInt(int value) =>
        value;

    public static bool CleanGenericBoxEquals<T>(
        T left,
        T right) =>
        left!.Equals(right);

    public static object CleanBoxedInt(int value) =>
        value;
}
