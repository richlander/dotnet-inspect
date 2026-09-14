namespace ILInspector.Decompiler.Fixtures.UnsafeChainC;

using ILInspector.Decompiler.Fixtures.UnsafeChainA;
using ILInspector.Decompiler.Fixtures.UnsafeChainB;

/// <summary>
/// App C of the cross-assembly unsafe chain. This assembly is NOT opted into
/// the updated memory-safety rules, so it stands in for legacy input. It calls
/// <see cref="LibraryA.M1"/> across the assembly boundary — a pointerless
/// requires-unsafe method — alongside the chained <see cref="LibraryB.M2"/>.
///
/// Under legacy rules neither the compiler nor conservative decompilation
/// demands an unsafe context for the A.M1 call (the requirement leaves no trace
/// in C). Optimistic ("simulate") decompilation recovers it: A.M1's
/// <c>RequiresUnsafeAttribute</c> lives in opted-in assembly A and is readable
/// cross-assembly, so the simulate mode wraps only that call.
/// </summary>
public static class Program
{
    /// <summary>
    /// Relays the chain (B.M2 -> A.M1) and adds a direct cross-assembly call to
    /// the requires-unsafe A.M1. The B.M2 call is not requires-unsafe; only the
    /// direct A.M1 call is, so simulate mode wraps just the latter.
    /// </summary>
    public static int CallChain() => LibraryB.M2() + LibraryA.M1();

    public static int Main()
    {
        int result = CallChain();
        Console.WriteLine(result);
        return result;
    }
}
