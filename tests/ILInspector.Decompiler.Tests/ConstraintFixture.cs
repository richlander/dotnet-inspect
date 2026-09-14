namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Generic methods whose bodies rely on their generic constraints, used by
/// <see cref="SkeletonEmitTests"/> to pin the fidelity skeleton's <c>where</c>
/// clause reconstruction (#1347). Each body uses a referenced constrained
/// generic — <see cref="System.Nullable{T}"/> requires <c>struct</c>, and
/// <see cref="System.Exception"/>'s members require the type constraint — so the
/// skeleton must restate the constraint or the recompile fails (CS0453 / CS1061).
/// </summary>
public static class ConstraintFixture
{
    public static T? WrapNullable<T>(T value) where T : struct => value;

    public static string DescribeException<T>(T error) where T : System.Exception => error.Message;
}
