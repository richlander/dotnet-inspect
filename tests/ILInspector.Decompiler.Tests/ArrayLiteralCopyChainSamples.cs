namespace ILInspector.Decompiler.Tests;

// csc keeps the array in a local when it is read inside a loop, so these fills
// reach ArrayLiteralFromStoresPass with a local place rather than a dup chain.
// Conditional elements are spilled to a merge slot before their element store.
// #9427 round 1 found the spilled-element rule throwing for local places.
public static class ArrayLiteralCopyChainSamples
{
    public static void Use(object[] a) { }
    public static object Z() => 2;

    // Two conditionals share one merge slot: a multi-store carrier, so the run
    // declines and keeps its allocation and stores.
    public static void LocalSharedSpill(object x, bool c, bool d, int n)
    {
        object[] a = new object[3];
        a[0] = x;
        a[1] = c ? Z() : x;
        a[2] = d ? x : Z();
        for (int i = 0; i < n; i++) Use(a);
    }

    // One conditional element in a local array.
    public static void LocalSpilledConditional(object x, bool c, int n)
    {
        object[] a = new object[2];
        a[0] = x;
        a[1] = c ? Z() : x;
        for (int i = 0; i < n; i++) Use(a);
    }
}
