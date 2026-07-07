using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Bounds the structural nesting depth of a metadata signature blob *before* it is handed to
/// <c>System.Reflection.Metadata</c>'s <c>SignatureDecoder</c>.
///
/// SRM decodes a signature by recursing on the native stack once per nested structural element
/// (pointer / array / by-ref / pinned / generic-instantiation argument / function-pointer
/// parameter / custom modifier), *before* any <see cref="ISignatureTypeProvider{TType,TContext}"/>
/// callback runs. Attacker-controlled metadata can therefore encode a signature whose nesting is
/// deep enough to overflow the stack with an <b>uncatchable</b> <c>StackOverflowException</c> that
/// terminates the process — no provider-side guard can stop it, because the overflow happens on
/// the descent before the first callback fires.
///
/// This guard walks the blob *iteratively* (an explicit heap work-stack, never the native stack),
/// computing the maximum type-nesting depth, and reports whether it exceeds a safe limit so the
/// caller can fail closed (skip the decode and degrade to an unresolved shape) instead of crashing.
/// A raw blob-length cap is deliberately avoided: a legitimately <i>wide</i> method signature (many
/// parameters or generic arguments) is long but structurally shallow, so length would false-reject
/// real code. Depth does not.
/// </summary>
public static class SignatureBlobGuard
{
    /// <summary>
    /// Maximum structural type-nesting depth allowed before a signature is treated as unsafe to
    /// decode. Real signatures nest only a handful of levels deep (CoreLib's deepest is in the
    /// single digits); the limit is far above that yet far below the native depth that overflows a
    /// default managed thread stack, so it only trips on malformed / adversarial metadata.
    /// </summary>
    public const int DefaultMaxDepth = 512;

    /// <summary>The kind of signature the blob encodes, which determines its header layout.</summary>
    public enum Kind
    {
        /// <summary>A MethodDefSig / MethodRefSig (also property signatures): header, [generic
        /// param count], param count, return type, parameters.</summary>
        Method,

        /// <summary>A FieldSig: header, custom-mods, type.</summary>
        Field,

        /// <summary>A LocalVarSig: header, local count, locals.</summary>
        LocalVariables,

        /// <summary>A MethodSpec instantiation signature: header, arg count, type args.</summary>
        MethodSpecification,

        /// <summary>A TypeSpec signature: a single Type with no header.</summary>
        TypeSpecification,
    }

    /// <summary>
    /// Returns <see langword="true"/> if the signature is shallow enough to hand to SRM safely.
    /// Returns <see langword="false"/> if its structural nesting exceeds <paramref name="maxDepth"/>
    /// (or the blob is malformed in a way that makes the depth unknowable), in which case the caller
    /// must not decode it. Truncated-but-shallow blobs return <see langword="true"/>: SRM raises a
    /// catchable <c>BadImageFormatException</c> for those, which is not the crash this guards.
    /// </summary>
    public static bool IsSafeToDecode(BlobReader blob, Kind kind, int maxDepth = DefaultMaxDepth)
    {
        try
        {
            return !ExceedsDepth(blob, kind, maxDepth);
        }
        catch (BadImageFormatException)
        {
            // The blob underran while we were reading a length/token. It is structurally shallow up
            // to the truncation (a deep blob would have tripped the depth check first), so SRM's own
            // catchable BadImageFormatException is the appropriate failure — allow the decode.
            return true;
        }
    }

    /// <summary>Convenience overload that reads the blob for <paramref name="signature"/>.</summary>
    public static bool IsSafeToDecode(MetadataReader reader, BlobHandle signature, Kind kind, int maxDepth = DefaultMaxDepth)
    {
        if (signature.IsNil)
            return true;
        return IsSafeToDecode(reader.GetBlobReader(signature), kind, maxDepth);
    }

    static bool ExceedsDepth(BlobReader blob, Kind kind, int maxDepth)
    {
        // Work items are read strictly left-to-right; the stack only tracks *what* to read next and
        // at what depth, so recursion lives on the heap and can never overflow the native stack.
        var work = new Stack<WorkItem>();
        SeedRoots(ref blob, kind, work);

        while (work.Count > 0)
        {
            var item = work.Pop();
            switch (item.Op)
            {
                case Op.Type:
                    if (item.Depth > maxDepth)
                        return true;
                    ReadType(ref blob, item.Depth, work);
                    break;

                case Op.ArrayShape:
                    SkipArrayShape(ref blob);
                    break;
            }
        }

        return false;
    }

    static void SeedRoots(ref BlobReader blob, Kind kind, Stack<WorkItem> work)
    {
        switch (kind)
        {
            case Kind.TypeSpecification:
                work.Push(WorkItem.Type(1));
                break;

            case Kind.Field:
                blob.ReadSignatureHeader();
                work.Push(WorkItem.Type(1));
                break;

            case Kind.MethodSpecification:
            {
                blob.ReadSignatureHeader();
                int count = blob.ReadCompressedInteger();
                for (int i = 0; i < count; i++)
                    work.Push(WorkItem.Type(1));
                break;
            }

            case Kind.LocalVariables:
            {
                blob.ReadSignatureHeader();
                int count = blob.ReadCompressedInteger();
                for (int i = 0; i < count; i++)
                    work.Push(WorkItem.Type(1));
                break;
            }

            case Kind.Method:
            {
                SeedMethodRoots(ref blob, work, depth: 1);
                break;
            }
        }
    }

    static void SeedMethodRoots(ref BlobReader blob, Stack<WorkItem> work, int depth)
    {
        var header = blob.ReadSignatureHeader();
        if (header.IsGeneric)
            blob.ReadCompressedInteger(); // generic parameter count
        int paramCount = blob.ReadCompressedInteger();
        // Return type, then each parameter, are Type slots (with the usual leading modifiers /
        // by-ref / typedbyref / sentinel handled inside ReadType). Ordering among siblings does not
        // affect the maximum depth, so push them all at the same depth.
        work.Push(WorkItem.Type(depth));
        for (int i = 0; i < paramCount; i++)
            work.Push(WorkItem.Type(depth));
    }

    /// <summary>Reads one Type at <paramref name="depth"/>, consuming its own bytes and pushing its
    /// child Type slots (at <paramref name="depth"/> + 1) for the main loop to process.</summary>
    static void ReadType(ref BlobReader blob, int depth, Stack<WorkItem> work)
    {
        // Skip any leading prefixes that modify the following type without adding a structural
        // frame worth bounding separately: custom modifiers (each carries a coded token), by-ref,
        // pinned, and the vararg sentinel.
        while (true)
        {
            byte code = blob.ReadByte();
            switch (code)
            {
                case ElementTypeCmodReqd:
                case ElementTypeCmodOpt:
                    blob.ReadTypeHandle(); // modifier's TypeDefOrRefOrSpec token
                    continue;
                case ElementTypeByRef:
                case ElementTypePinned:
                case ElementTypeSentinel:
                    continue;

                case ElementTypePtr:
                case ElementTypeSzArray:
                    work.Push(WorkItem.Type(depth + 1));
                    return;

                case ElementTypeArray:
                    // ELEMENT_TYPE_ARRAY: Type ArrayShape. The shape trails the element in the blob,
                    // so schedule it to be read *after* the element subtree (pushed first here, so it
                    // pops last).
                    work.Push(WorkItem.ArrayShape());
                    work.Push(WorkItem.Type(depth + 1));
                    return;

                case ElementTypeGenericInst:
                {
                    // GENERICINST (CLASS|VALUETYPE) TypeToken GenArgCount Type*
                    blob.ReadByte();       // CLASS / VALUETYPE
                    blob.ReadTypeHandle(); // generic type token
                    int args = blob.ReadCompressedInteger();
                    for (int i = 0; i < args; i++)
                        work.Push(WorkItem.Type(depth + 1));
                    return;
                }

                case ElementTypeFnPtr:
                    // FNPTR MethodSig: its return type and parameters are the children.
                    SeedMethodRoots(ref blob, work, depth + 1);
                    return;

                case ElementTypeClass:
                case ElementTypeValueType:
                    blob.ReadTypeHandle();
                    return;

                case ElementTypeVar:
                case ElementTypeMVar:
                    blob.ReadCompressedInteger(); // generic parameter index
                    return;

                default:
                    // Primitive / VOID / OBJECT / STRING / TYPEDBYREF / I / U and anything else:
                    // a leaf that consumes no further bytes here.
                    return;
            }
        }
    }

    static void SkipArrayShape(ref BlobReader blob)
    {
        blob.ReadCompressedInteger();           // rank
        int numSizes = blob.ReadCompressedInteger();
        for (int i = 0; i < numSizes; i++)
            blob.ReadCompressedInteger();        // size
        int numLoBounds = blob.ReadCompressedInteger();
        for (int i = 0; i < numLoBounds; i++)
            blob.ReadCompressedSignedInteger();  // lower bound
    }

    // ECMA-335 II.23.1.16 element types, by raw byte value (the SignatureTypeCode enum does not
    // name the modifier / prefix codes we care about, so spell them all out to avoid ambiguity).
    const byte ElementTypePtr = 0x0f;         // ELEMENT_TYPE_PTR
    const byte ElementTypeByRef = 0x10;       // ELEMENT_TYPE_BYREF
    const byte ElementTypeValueType = 0x11;   // ELEMENT_TYPE_VALUETYPE
    const byte ElementTypeClass = 0x12;       // ELEMENT_TYPE_CLASS
    const byte ElementTypeVar = 0x13;         // ELEMENT_TYPE_VAR
    const byte ElementTypeArray = 0x14;       // ELEMENT_TYPE_ARRAY
    const byte ElementTypeGenericInst = 0x15; // ELEMENT_TYPE_GENERICINST
    const byte ElementTypeFnPtr = 0x1b;       // ELEMENT_TYPE_FNPTR
    const byte ElementTypeSzArray = 0x1d;     // ELEMENT_TYPE_SZARRAY
    const byte ElementTypeMVar = 0x1e;        // ELEMENT_TYPE_MVAR
    const byte ElementTypeCmodReqd = 0x1f;    // ELEMENT_TYPE_CMOD_REQD
    const byte ElementTypeCmodOpt = 0x20;     // ELEMENT_TYPE_CMOD_OPT
    const byte ElementTypeSentinel = 0x41;    // ELEMENT_TYPE_SENTINEL
    const byte ElementTypePinned = 0x45;      // ELEMENT_TYPE_PINNED

    enum Op : byte { Type, ArrayShape }

    readonly struct WorkItem
    {
        public Op Op { get; }
        public int Depth { get; }
        WorkItem(Op op, int depth) { Op = op; Depth = depth; }
        public static WorkItem Type(int depth) => new(Op.Type, depth);
        public static WorkItem ArrayShape() => new(Op.ArrayShape, 0);
    }
}
