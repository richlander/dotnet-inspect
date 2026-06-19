namespace ILInspector.Decompiler.Fixtures.UnsafeChainA;

/// <summary>
/// Leaf assembly A of the cross-assembly unsafe chain, and the shared
/// memory-safety specimen library. The module opts into the updated
/// memory-safety rules (see the csproj), so the compiler stamps every method
/// declared <c>unsafe</c> with <c>RequiresUnsafeAttribute</c> and treats a
/// pointer anywhere in a signature as requires-unsafe.
///
/// The methods below are deliberately diverse so the fixture serves many
/// scenarios at once — cross-assembly requires-unsafe rendering, and the
/// "is this method's unsafety realized in its body?" classification. Each is a
/// single, clean specimen of one cell in this matrix:
///
/// <code>
/// method               | requires-unsafe via   | body deref? | forwards ptr? | classification
/// ---------------------+-----------------------+-------------+---------------+-----------------------------
/// M1                   | `unsafe` modifier     | yes         | no            | real unsafe (cross-asm leaf)
/// RealUnsafePointer    | pointer signature     | yes         | no            | real unsafe (signature+body)
/// HollowUnsafe         | `unsafe` modifier     | no          | no            | hollow — removable `unsafe`
/// SignatureOnlyUnsafe  | pointer signature     | no          | no            | hollow — signature-only
/// DelegatedUnsafe      | pointer signature     | no          | yes           | delegated — correctly unsafe
/// Safe                 | (not requires-unsafe) | no          | no            | safe baseline
/// </code>
/// </summary>
public static class LibraryA
{
    // ---- Cross-assembly chain leaf -------------------------------------------

    /// <summary>
    /// Pointerless method declared <c>unsafe</c> -> stamped <c>[RequiresUnsafe]</c>.
    /// No pointer in the signature, so a cross-assembly caller cannot infer
    /// requires-unsafe from the <c>MemberRef</c> alone; it must resolve A and
    /// read this attribute. The body does real pointer work so a result
    /// genuinely "passes through" unsafe code (B.M2 -> A.M1, printed by C).
    /// </summary>
    public static unsafe int M1()
    {
        int value = 41;
        int* p = &value;
        unsafe
        {
            *p += 1;
            return *p;
        }
    }

    // ---- Real unsafe (unsafety realized in the body) -------------------------

    /// <summary>
    /// Requires-unsafe via its pointer signature AND realizes it: dereferences
    /// the pointer (an <c>ldind</c> body op). The unambiguous "real unsafe"
    /// specimen — both the signature and the body are unsafe.
    /// </summary>
    public static unsafe int RealUnsafePointer(int* p)
    {
        unsafe { return *p + 1; }
    }

    // ---- Hollow unsafe (caller-unsafe, but no unsafe op in the body) ---------

    /// <summary>
    /// Declared <c>unsafe</c> — so stamped <c>[RequiresUnsafe]</c>, forcing every
    /// caller into an unsafe context — yet the body performs no unsafe operation
    /// and touches no pointer. The <c>unsafe</c> modifier is gratuitous: this is
    /// the canonical "removable <c>unsafe</c>" specimen.
    /// </summary>
    public static unsafe int HollowUnsafe(int x) => x * 2;

    /// <summary>
    /// Requires-unsafe purely because of the <c>int*</c> in its signature, but
    /// the body never dereferences it — it only compares the pointer to null.
    /// "Signature-only" hollow: caller-unsafe, no body deref, no pointer handed
    /// onward. Distinct from <see cref="HollowUnsafe"/> (hollow via the modifier).
    /// </summary>
    public static unsafe bool SignatureOnlyUnsafe(int* p) => p == null;

    /// <summary>
    /// Requires-unsafe via its pointer signature and never dereferences the
    /// pointer itself — but forwards it to <see cref="RealUnsafePointer"/>. Its
    /// unsafety is real, just delegated: the discriminator that separates a
    /// genuine pass-through from a truly hollow method. A body-op-only scan sees
    /// no deref here, but the pointer-forwarding call marks it correctly unsafe.
    /// </summary>
    public static unsafe int DelegatedUnsafe(int* p)
    {
        unsafe { return RealUnsafePointer(p); }
    }

    // ---- Safe baseline -------------------------------------------------------

    /// <summary>
    /// No <c>unsafe</c> modifier, no pointer in the signature, no unsafe body op:
    /// not requires-unsafe at all. The negative control for every specimen above.
    /// </summary>
    public static int Safe(int x) => x + 1;
}
