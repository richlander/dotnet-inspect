namespace ILInspector.Decompiler.Fixtures.UnsafeChainB;

using ILInspector.Decompiler.Fixtures.UnsafeChainA;

/// <summary>
/// Caller assembly B of the cross-assembly unsafe chain. <see cref="M2"/> calls
/// <see cref="LibraryA.M1"/> — a pointerless requires-unsafe method in assembly
/// A. Under the updated memory-safety rules the call needs an explicit unsafe
/// context even though no pointer crosses the boundary. The source wraps it in
/// an <c>unsafe { }</c> block; a faithful decompilation must do the same, which
/// requires reading A.M1's <c>RequiresUnsafeAttribute</c> cross-assembly.
/// </summary>
public static class LibraryB
{
    // Cross-assembly call to a pointerless requires-unsafe method. The call —
    // not any intrinsic pointer op — is what forces the unsafe context.
    public static int M2()
    {
        unsafe
        {
            return LibraryA.M1();
        }
    }
}
