namespace ILInspector.Decompiler.Fixtures.UnsafeChainA;

/// <summary>
/// Leaf assembly A of the cross-assembly unsafe chain. <see cref="M1"/> is a
/// pointerless method declared <c>unsafe</c>: under the updated memory-safety
/// rules the compiler stamps it with <c>RequiresUnsafeAttribute</c> even though
/// nothing about its signature is unsafe. The attribute is the ONLY signal that
/// the method is requires-unsafe, and it lives on this MethodDef in A — a caller
/// in another assembly must resolve A to read it.
/// </summary>
public static class LibraryA
{
    // Pointerless, declared `unsafe` -> stamped [RequiresUnsafe]. No pointer in
    // the signature, so a cross-assembly caller cannot infer requires-unsafe
    // from the MemberRef signature alone; it must read this attribute.
    public static unsafe int M1() => 42;
}
