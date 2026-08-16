namespace ILInspector.Decompiler.Tests;

// Issue #1142: deconstruction into field targets. Top-level (the test importer
// helper resolves by simple FullName, not nested `+` names).
public sealed class FieldDeconstructionTargets
{
    public int InstanceX;
    public int InstanceY;
    public static int StaticX;
    public static int StaticY;

    // Two instance fields. An instance-field receiver keeps the importer from
    // promoting the tuple temp to a stack slot, so the seed is a StoreLocal.
    public void IntoTwoInstanceFields((int, int) pair) => (InstanceX, InstanceY) = pair;

    // Two static fields — no receiver, so the temp promotes to a stack slot.
    public static void IntoTwoStaticFields((int, int) pair) => (StaticX, StaticY) = pair;
}
