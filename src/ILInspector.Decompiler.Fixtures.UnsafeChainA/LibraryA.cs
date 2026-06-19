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
/// M1                   | `unsafe` modifier     | yes*        | no            | real unsafe (cross-asm leaf)
/// RealUnsafePointer    | pointer signature     | yes         | no            | real unsafe (signature+body)
/// ContractUnsafe       | `unsafe` modifier     | yes         | no            | caller contract, safe-looking sig
/// EscapingStackPointer | pointer signature     | no (escape) | no            | unconditionally unsafe (stack escape)
/// HollowUnsafe         | `unsafe` modifier     | no          | no            | hollow — removable `unsafe`
/// SignatureOnlyUnsafe  | pointer signature     | no          | no            | hollow — signature-only
/// DelegatedUnsafe      | pointer signature     | no          | yes           | delegated — correctly unsafe
/// Safe                 | (not requires-unsafe) | no          | no            | safe baseline
/// </code>
///
/// * M1 derefs in source, but its plain pointer local is optimized away in
/// Release so the body scan records NO realized op (bodyOp=n) — the standing
/// caution that "no visible unsafe op" must never be read as "safe."
/// These annotations advertise obligations and surface evidence; none asserts
/// safety (raw pointers opt out of the ref-safety escape analysis that the
/// compiler enforces for `Span`/ref structs, so verification is out of scope).
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

    // ---- Caller contract behind a safe-looking signature ---------------------

    /// <summary>
    /// A SAFE-looking signature (<c>int[]</c>, no pointer) that is nonetheless
    /// requires-unsafe: the body pins the array (a <c>fixed</c> pinned local),
    /// advances the pointer by one element with pointer arithmetic, dereferences
    /// it, and prints the result. Reading element <c>[1]</c> is hardcoded, so the
    /// method is only memory-safe if the caller passes a two-element (or longer)
    /// array — an unenforced precondition, i.e. a real CALLER CONTRACT. Because
    /// of that contract the method carries the <c>unsafe</c> modifier, so the
    /// compiler stamps <c>RequiresUnsafeAttribute</c> and
    /// <c>CallerUnsafeMode</c> is <c>Explicit</c>. The <c>int[]</c> signature
    /// gives a caller no hint; the attribute on the MethodDef is the ONLY signal,
    /// and a cross-assembly caller can only learn it by resolving A
    /// (the case <c>MetadataContext</c> serves). Contrast with a truly safe
    /// encapsulation, which would discharge the obligation internally (bounds
    /// check, or derive the index from <c>pair.Length</c>) and stay
    /// <c>CallerUnsafeMode.None</c>. Unlike <see cref="M1"/> the deref also
    /// survives the body scan: the <c>fixed</c> pinned local is flagged
    /// unconditionally, opening the indirect-opcode scan.
    /// </summary>
    public static unsafe void ContractUnsafe(int[] pair)
    {
        // The `unsafe` modifier above is the caller contract (stamps
        // RequiresUnsafeAttribute); this inner block is the deref *context* that
        // v2 requires separately.
        unsafe
        {
            fixed (int* p = pair)
            {
                int* second = p + 1;
                System.Console.WriteLine(*second);
            }
        }
    }

    // ---- Unconditionally unsafe: a pointer that escapes its stack frame ------

    /// <summary>
    /// Returns a pointer into stack memory (<c>stackalloc</c> ⇒ <c>localloc</c>)
    /// that is reclaimed the instant this method returns: a dangling pointer.
    /// Unlike <see cref="ContractUnsafe"/>, there is NO caller contract that makes
    /// the result usable — even a correct caller cannot safely deref it. C#
    /// compiles this because raw pointers carry no lifetime / ref-safety tracking;
    /// the unsafe surface is exactly where C#'s static lifetime guarantees are
    /// switched off. Compare the <c>Span&lt;int&gt;</c> form, which is the SAME
    /// bug but the compiler REJECTS it via ref-struct ref-safety analysis:
    /// <code>
    ///     static Span&lt;int&gt; Escaping() {
    ///         Span&lt;int&gt; s = stackalloc int[10];
    ///         return s; // CS8352: may expose referenced variables outside their
    ///                   // declaration scope — rejected at COMPILE time.
    ///     }
    /// </code>
    /// Two cheap, honest annotations fall out of this specimen, neither of which
    /// claims "safe": (1) the pointer RETURN type alone signals the caller
    /// inherits an unverifiable lifetime obligation; (2) <c>localloc</c> in a body
    /// that returns a pointer signals a probable stack escape. <c>localloc</c> is
    /// flagged UNGATED, so this is a gate-independent body-op survivor.
    /// </summary>
    public static unsafe int* EscapingStackPointer()
    {
        unsafe
        {
            int* p = stackalloc int[10];
            return p;
        }
    }

    // ---- Safe baseline -------------------------------------------------------

    /// <summary>
    /// No <c>unsafe</c> modifier, no pointer in the signature, no unsafe body op:
    /// not requires-unsafe at all. The negative control for every specimen above.
    /// </summary>
    public static int Safe(int x) => x + 1;
}
