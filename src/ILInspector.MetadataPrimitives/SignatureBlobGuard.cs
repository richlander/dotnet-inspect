using System.Reflection.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>
/// Bounds the structural nesting depth of a metadata signature blob before it is handed to
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
/// computing the maximum type-nesting depth, and reports whether it exceeds a safe limit or leaves
/// unconsumed bytes so the caller can fail closed with an explicit rejection instead of crashing
/// or accepting an unrecognized trailing suffix as part of the same shape.
/// A raw blob-length cap is deliberately avoided: a legitimately <i>wide</i> method signature can
/// be long but structurally shallow. A separate type-node ceiling bounds heap work without making
/// byte length itself the discriminator.
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

        /// <summary>A StandAloneMethodSig used by <c>calli</c>. Unlike other method signatures,
        /// this permits a sentinel for both managed vararg and unmanaged cdecl conventions.</summary>
        StandaloneMethod,

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
            SignatureBlobGuardMeasurements measurements = default;
            return !ExceedsDepth(ref blob, kind, maxDepth, ref measurements);
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

    /// <summary>
    /// Returns whether the entire blob is structurally shallow and consumed by
    /// the declared signature grammar. Unlike <see cref="IsSafeToDecode(BlobReader, Kind, int)"/>,
    /// truncation and trailing bytes return <see langword="false"/>.
    /// </summary>
    public static bool IsSafeAndCompleteToDecode(
        BlobReader blob,
        Kind kind,
        int maxDepth = DefaultMaxDepth)
        => IsSafeAndCompleteToDecode(blob, kind, out _, maxDepth);

    internal static bool IsSafeAndCompleteToDecode(
        BlobReader blob,
        Kind kind,
        out SignatureBlobGuardMeasurements measurements,
        int maxDepth = DefaultMaxDepth)
    {
        measurements = default;
        try
        {
            return !ExceedsDepth(ref blob, kind, maxDepth, ref measurements)
                && blob.RemainingBytes == 0;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Convenience overload that requires the entire metadata blob to match
    /// the declared signature grammar.
    /// </summary>
    public static bool IsSafeAndCompleteToDecode(
        MetadataReader reader,
        BlobHandle signature,
        Kind kind,
        int maxDepth = DefaultMaxDepth)
        => !signature.IsNil
            && IsSafeAndCompleteToDecode(
                reader.GetBlobReader(signature),
                kind,
                maxDepth);

    internal static bool IsSafeAndCompleteToDecode(
        MetadataReader reader,
        BlobHandle signature,
        Kind kind,
        out SignatureBlobGuardMeasurements measurements,
        int maxDepth = DefaultMaxDepth)
    {
        measurements = default;
        return !signature.IsNil
            && IsSafeAndCompleteToDecode(
                reader.GetBlobReader(signature), kind, out measurements, maxDepth);
    }

    static bool ExceedsDepth(
        ref BlobReader blob,
        Kind kind,
        int maxDepth,
        ref SignatureBlobGuardMeasurements measurements)
    {
        // Work items are read strictly left-to-right; the stack only tracks *what* to read next and
        // at what depth, so recursion lives on the heap and can never overflow the native stack.
        // Every Type work item consumes at least one blob byte, and count-driven pushes are bounded
        // by both the remaining blob length and the operation type-node budget.
        var work = new Stack<WorkItem>();
        int remainingTypeNodes =
            MetadataSafetyPolicy.MaxSignatureTypeNodes;
        if (SeedRoots(
                ref blob,
                kind,
                work,
                ref remainingTypeNodes))
            return true;

        while (work.Count > 0)
        {
            var item = work.Pop();
            switch (item.Op)
            {
                case Op.Type:
                    if (item.Depth > maxDepth)
                        return true;
                    if (ReadType(
                            ref blob,
                            item.Depth,
                            work,
                            ref remainingTypeNodes))
                        return true;
                    break;

                case Op.MethodParameter:
                    if (item.Depth > maxDepth)
                        return true;
                    if (ReadMethodParameter(
                            ref blob,
                            item,
                            work,
                            ref remainingTypeNodes))
                        return true;
                    break;

                case Op.ArrayShape:
                    if (SkipArrayShape(ref blob, ref remainingTypeNodes, ref measurements))
                        return true;
                    break;
            }
        }

        return blob.RemainingBytes != 0;
    }

    /// <summary>Pushes <paramref name="count"/> Type slots at <paramref name="depth"/>. Returns true
    /// (unsafe) if the count exceeds the bytes left in the blob: every Type consumes at least one
    /// byte, so a larger count is malformed and must not be materialized (a compressed integer can
    /// encode ~536M, which would otherwise OOM the work-stack before SRM ever sees the blob).</summary>
    static bool PushTypes(
        Stack<WorkItem> work,
        int count,
        int depth,
        ref BlobReader blob,
        ref int remainingTypeNodes)
    {
        if (count < 0
            || count > blob.RemainingBytes
            || count > remainingTypeNodes)
            return true;
        remainingTypeNodes -= count;
        for (int i = 0; i < count; i++)
            work.Push(WorkItem.Type(depth));
        return false;
    }

    static bool PushType(
        Stack<WorkItem> work,
        int depth,
        ref int remainingTypeNodes)
    {
        if (remainingTypeNodes == 0)
            return true;
        remainingTypeNodes--;
        work.Push(WorkItem.Type(depth));
        return false;
    }

    static bool SeedRoots(
        ref BlobReader blob,
        Kind kind,
        Stack<WorkItem> work,
        ref int remainingTypeNodes)
    {
        switch (kind)
        {
            case Kind.TypeSpecification:
                return PushType(
                    work,
                    1,
                    ref remainingTypeNodes);

            case Kind.Field:
                blob.ReadSignatureHeader();
                return PushType(
                    work,
                    1,
                    ref remainingTypeNodes);

            case Kind.MethodSpecification:
            case Kind.LocalVariables:
            {
                blob.ReadSignatureHeader();
                int count = blob.ReadCompressedInteger();
                return PushTypes(
                    work,
                    count,
                    1,
                    ref blob,
                    ref remainingTypeNodes);
            }

            case Kind.Method:
            case Kind.StandaloneMethod:
                return SeedMethodRoots(
                    ref blob,
                    work,
                    depth: 1,
                    allowCdeclSentinel: kind == Kind.StandaloneMethod,
                    requireMethodKind: false,
                    ref remainingTypeNodes);

            default:
                return false;
        }
    }

    static bool SeedMethodRoots(
        ref BlobReader blob,
        Stack<WorkItem> work,
        int depth,
        bool allowCdeclSentinel,
        bool requireMethodKind,
        ref int remainingTypeNodes)
    {
        var header = blob.ReadSignatureHeader();
        if (requireMethodKind && header.Kind != SignatureKind.Method)
            return true;
        if (header.IsGeneric)
            blob.ReadCompressedInteger(); // generic parameter count
        int paramCount = blob.ReadCompressedInteger();
        if (paramCount < 0
            || (long)paramCount + 1 > blob.RemainingBytes
            || (long)paramCount + 1 > remainingTypeNodes)
            return true;
        remainingTypeNodes -= paramCount + 1;
        var state = new MethodState(
            header.CallingConvention
                == SignatureCallingConvention.VarArgs
            || (allowCdeclSentinel
                && header.CallingConvention
                    == SignatureCallingConvention.CDecl));
        for (int i = paramCount - 1; i >= 0; i--)
            work.Push(WorkItem.MethodParameter(depth, state));
        work.Push(WorkItem.Type(depth));
        return false;
    }

    static bool ReadMethodParameter(
        ref BlobReader blob,
        WorkItem item,
        Stack<WorkItem> work,
        ref int remainingTypeNodes)
    {
        MethodState state = item.Method
            ?? throw new InvalidOperationException(
                "A method-parameter work item requires method state.");
        if (blob.RemainingBytes > 0)
        {
            int offset = blob.Offset;
            if (blob.ReadByte() == ElementTypeSentinel)
            {
                if (!state.AllowsSentinel || state.SentinelSeen)
                    return true;
                state.SentinelSeen = true;
            }
            else
            {
                blob.Offset = offset;
            }
        }
        return ReadType(
            ref blob,
            item.Depth,
            work,
            ref remainingTypeNodes);
    }

    /// <summary>Reads one Type at <paramref name="depth"/>, consuming its own bytes and pushing its
    /// child Type slots for the main loop to process. Returns true (unsafe) if a malformed count is
    /// encountered. Every prefix that SRM recurses through (custom modifier, by-ref, pinned) pushes
    /// the modified type at <paramref name="depth"/> + 1 so a long prefix chain is bounded exactly
    /// like a chain of pointers.</summary>
    static bool ReadType(
        ref BlobReader blob,
        int depth,
        Stack<WorkItem> work,
        ref int remainingTypeNodes)
    {
        byte code = blob.ReadByte();
        // SRM reads type codes as compressed integers. Canonical codes are a
        // single byte below 0x80; a multi-byte encoding such as 0x80 0x0F is
        // PTR to SRM but a leaf here, which desynchronizes the walk and can
        // hide a deep pointer chain inside a wide GENERICINST argument list.
        if (code >= 0x80)
            return true;
        switch (code)
        {
            case ElementTypeCmodReqd:
            case ElementTypeCmodOpt:
                blob.ReadTypeHandle();               // modifier's TypeDefOrRefOrSpec token
                return PushType(
                    work,
                    depth + 1,
                    ref remainingTypeNodes);

            case ElementTypeByRef:
            case ElementTypePinned:
            case ElementTypePtr:
            case ElementTypeSzArray:
                return PushType(
                    work,
                    depth + 1,
                    ref remainingTypeNodes);

            case ElementTypeSentinel:
                return true;

            case ElementTypeArray:
                // ELEMENT_TYPE_ARRAY: Type ArrayShape. The shape trails the element in the blob, so
                // schedule it to be read *after* the element subtree (pushed first here, popped last).
                work.Push(WorkItem.ArrayShape());
                return PushType(
                    work,
                    depth + 1,
                    ref remainingTypeNodes);

            case ElementTypeGenericInst:
            {
                // GENERICINST (CLASS|VALUETYPE) TypeToken GenArgCount Type*.
                // SRM decodes that first slot as a full Type. ECMA-335 II.23.2.12
                // permits only CLASS or VALUETYPE there; anything else desynchronizes
                // this walk from SRM and can smuggle a later FNPTR header or
                // ARRAY-shape count past the dedicated bounds.
                byte genericTypeCode = blob.ReadByte();
                if (genericTypeCode is not (ElementTypeClass or ElementTypeValueType))
                    return true;
                blob.ReadTypeHandle();
                int args = blob.ReadCompressedInteger();
                return PushTypes(
                    work,
                    args,
                    depth + 1,
                    ref blob,
                    ref remainingTypeNodes);
            }

            case ElementTypeFnPtr:
                // FNPTR MethodSig: its return type and parameters are the children.
                return SeedMethodRoots(
                    ref blob,
                    work,
                    depth + 1,
                    allowCdeclSentinel: false,
                    requireMethodKind: true,
                    ref remainingTypeNodes);

            case ElementTypeClass:
            case ElementTypeValueType:
                blob.ReadTypeHandle();
                return false;

            case ElementTypeVar:
            case ElementTypeMVar:
                blob.ReadCompressedInteger(); // generic parameter index
                return false;

            default:
                // Primitive / VOID / OBJECT / STRING / TYPEDBYREF / I / U and anything else:
                // a leaf that consumes no further bytes here.
                return false;
        }

    }

    /// <summary>
    /// Consumes an ArrayShape, charging its size and lower-bound counts to the shared type-node
    /// budget before either is materialized.
    /// </summary>
    /// <remarks>
    /// The remaining-bytes check alone is not a bound on work: SRM allocates and fills an
    /// <c>ImmutableArray</c> for each count while decoding the shape, before
    /// <c>TypeNodeProvider.GetArrayType</c> gets a chance to charge anything. A blob that is
    /// merely long can therefore encode many shapes whose counts each pass the per-shape byte
    /// check while their aggregate is arbitrarily large. Charging the same currency the type
    /// nodes use bounds the aggregate too, and leaves ordinary wide-but-shallow signatures —
    /// whose real ranks are single digits — untouched.
    /// <c>SignatureBlobGuardTests.Rejects_array_shape_counts_beyond_the_type_node_budget</c> and
    /// <c>SignatureBlobGuardTests.Rejects_aggregate_array_shape_counts_beyond_the_type_node_budget</c>
    /// gate it.
    /// </remarks>
    static bool SkipArrayShape(
        ref BlobReader blob,
        ref int remainingTypeNodes,
        ref SignatureBlobGuardMeasurements measurements)
    {
        blob.ReadCompressedInteger();           // rank
        int numSizes = blob.ReadCompressedInteger();
        if (numSizes >= 0)
            measurements.Sizes = measurements.Sizes.Observe(numSizes);
        if (numSizes < 0
            || numSizes > blob.RemainingBytes
            || numSizes > remainingTypeNodes)
        {
            return true;
        }
        remainingTypeNodes -= numSizes;
        for (int i = 0; i < numSizes; i++)
            blob.ReadCompressedInteger();        // size
        int numLoBounds = blob.ReadCompressedInteger();
        if (numLoBounds >= 0)
            measurements.LowerBounds = measurements.LowerBounds.Observe(numLoBounds);
        if (numLoBounds < 0
            || numLoBounds > blob.RemainingBytes
            || numLoBounds > remainingTypeNodes)
        {
            return true;
        }
        remainingTypeNodes -= numLoBounds;
        for (int i = 0; i < numLoBounds; i++)
            blob.ReadCompressedSignedInteger();  // lower bound
        return false;
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

    enum Op : byte { Type, MethodParameter, ArrayShape }

    sealed class MethodState(bool allowsSentinel)
    {
        public bool AllowsSentinel { get; } = allowsSentinel;
        public bool SentinelSeen { get; set; }
    }

    readonly struct WorkItem
    {
        public Op Op { get; }
        public int Depth { get; }
        public MethodState? Method { get; }
        WorkItem(
            Op op,
            int depth,
            MethodState? method = null)
        {
            Op = op;
            Depth = depth;
            Method = method;
        }
        public static WorkItem Type(int depth) => new(Op.Type, depth);
        public static WorkItem MethodParameter(
            int depth,
            MethodState method)
            => new(Op.MethodParameter, depth, method);
        public static WorkItem ArrayShape() => new(Op.ArrayShape, 0);
    }
}
