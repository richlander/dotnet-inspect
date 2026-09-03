using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Bounds custom-attribute value blobs before they are handed to
/// <c>System.Reflection.Metadata</c>'s <c>CustomAttributeDecoder</c>.
///
/// SRM reads a declared <c>Int32</c> SZArray count or <c>UInt16</c> named-argument
/// count and allocates <c>ImmutableArray.CreateBuilder</c> from that count
/// <b>before</b> reading any element and before any
/// <see cref="ICustomAttributeTypeProvider{TType}"/> callback. Four attacker
/// bytes can therefore request a gigabyte-scale builder. The blob-length charge
/// on the value heap does not see that amplification.
///
/// This guard walks the constructor MethodSig and the whole value blob —
/// fixed arguments <b>and</b> each named argument's <c>FieldOrPropType</c>,
/// name, and value — refusing decode when a declared count exceeds the
/// remaining bytes or when boxed / SZArray nesting exceeds
/// <see cref="MaxSerializedDepth"/>. Declared slots and materialized
/// <c>SerString</c> payload bytes are charged through
/// <c>beforeMaterialize</c> so hostile metadata becomes typed truncation
/// rather than a swallowed <c>OutOfMemoryException</c>.
/// <c>CustomAttributeValueGuardTests</c>'s
/// <c>AssemblyQualifiedNamedEnum_SeesFollowingArrayCount</c> gate covers both
/// charges. The walk uses an explicit heap work-stack, never the native stack,
/// matching
/// <see cref="SignatureBlobGuard"/>: the depth cap is a policy limit, not a
/// stack-safety limit. Enum argument widths come from
/// <see cref="EnumUnderlyingPrimitive"/> so the skip stays aligned with SRM's
/// provider.
/// </summary>
public static class CustomAttributeValueGuard
{
    /// <summary>
    /// Conservative decode-work charge for one SRM
    /// <c>CustomAttributeTypedArgument</c> / named-argument builder slot.
    /// </summary>
    public const int DeclaredSlotCharge = 16;

    /// <summary>
    /// Maximum boxed / SZArray nesting allowed while skipping a value blob.
    /// Matches <see cref="SignatureBlobGuard.DefaultMaxDepth"/>: far above legal
    /// attributes, far below a default managed thread stack.
    /// </summary>
    public const int MaxSerializedDepth = SignatureBlobGuard.DefaultMaxDepth;

    /// <summary>
    /// Returns <see langword="true"/> when the value blob is safe to hand to
    /// <c>DecodeValue</c>. Truncated or unrecognized blobs return
    /// <see langword="true"/> so SRM's catchable failure remains the decoder
    /// result. Returns <see langword="false"/> when a declared count would
    /// allocate more slots than the remaining bytes can describe, or when
    /// serialized nesting exceeds <see cref="MaxSerializedDepth"/>. A caller
    /// <paramref name="enumUnderlyingType"/> is bound to the same
    /// local-TypeDef-first, <see cref="EnumUnderlyingPrimitive.Normalize"/>
    /// oracle <c>DecodeValue</c> uses, so a direct skip cannot diverge. A
    /// TypeDef-index failure during that bind is <see langword="false"/>, not
    /// a swallowed blob-format success: the walk never finished, so a later
    /// <c>DecodeValue</c> with a different provider must not run.
    /// </summary>
    public static bool IsSafeToDecode(
        MetadataReader reader,
        CustomAttribute attribute,
        Action<int>? beforeMaterialize = null,
        Func<string, PrimitiveTypeCode>? enumUnderlyingType = null)
    {
        try
        {
            if (enumUnderlyingType is not null)
            {
                enumUnderlyingType = AttributeDecoder.BindEnumWidthResolver(
                    reader,
                    beforeMaterialize,
                    enumUnderlyingType);
            }

            return Check(
                    reader,
                    attribute,
                    beforeMaterialize,
                    enumUnderlyingType)
                != Result.Unsafe;
        }
        catch (AttributeDecoder.TypeDefinitionIndexException)
        {
            return false;
        }
        catch (BadImageFormatException)
        {
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }
    }

    static Result Check(
        MetadataReader reader,
        CustomAttribute attribute,
        Action<int>? beforeMaterialize,
        Func<string, PrimitiveTypeCode>? enumUnderlyingType)
    {
        if (!TryGetConstructorSignature(reader, attribute.Constructor, out var signatureHandle))
            return Result.Safe;
        if (!SignatureBlobGuard.IsSafeToDecode(
                reader,
                signatureHandle,
                SignatureBlobGuard.Kind.Method))
            return Result.Unsafe;

        var signature = reader.GetBlobReader(signatureHandle);
        var header = signature.ReadSignatureHeader();
        if (header.IsGeneric)
            signature.ReadCompressedInteger();
        int parameterCount = signature.ReadCompressedInteger();
        if (parameterCount < 0)
            return Result.Unsafe;

        var signatureSkip = new Stack<SignatureSkipItem>();
        if (!TrySkipSignatureType(ref signature, signatureSkip))
            return Result.Safe;

        var value = reader.GetBlobReader(attribute.Value);
        if (value.RemainingBytes < 2)
            return Result.Safe;
        if (value.ReadUInt16() != 1)
            return Result.Safe;

        var walk = new WalkState(
            reader,
            attribute.Constructor,
            beforeMaterialize,
            enumUnderlyingType,
            signature,
            value,
            signatureSkip);
        walk.Push(WorkItem.NamedHeader());
        if (parameterCount > 0)
            walk.Push(WorkItem.FixedArgs(parameterCount, depth: 1));
        return walk.Run();
    }

    /// <summary>
    /// Value-blob work lives on this heap stack. Wide arrays keep a remaining
    /// count rather than one item per element, so a legal 4k-int argument
    /// cannot amplify into thousands of frames.
    /// <c>CustomAttributeValueGuardTests.BoxedNestingAtLimit_OnSmallNativeStack_IsSafe</c>
    /// is the gate that this walk does not recurse on the native stack.
    /// </summary>
    sealed class WalkState(
        MetadataReader reader,
        EntityHandle constructor,
        Action<int>? beforeMaterialize,
        Func<string, PrimitiveTypeCode>? enumUnderlyingType,
        BlobReader signature,
        BlobReader value,
        Stack<SignatureSkipItem> signatureSkip)
    {
        readonly MetadataReader _reader = reader;
        readonly EntityHandle _constructor = constructor;
        readonly Action<int>? _beforeMaterialize = beforeMaterialize;
        readonly Func<string, PrimitiveTypeCode>? _enumUnderlyingType = enumUnderlyingType;
        readonly Stack<WorkItem> _work = new();
        readonly Stack<SignatureSkipItem> _signatureSkip = signatureSkip;
        readonly Stack<SrmSkipItem> _srmSkip = new();
        Stack<SignatureFrame>? _frames;
        BlobReader _signature = signature;
        BlobReader _value = value;
        bool _substituteGenerics = true;
        string? _memoEnumName;
        PrimitiveTypeCode _memoEnumWidth;
        EntityHandle _memoEnumHandle;
        PrimitiveTypeCode _memoEnumHandleWidth;
        EntityHandle _memoSystemTypeHandle;
        bool _memoIsSystemType;
        bool _memoSpecResolved;
        Result _memoSpecResult;
        bool _memoSpecFound;
        BlobReader _memoSpecArguments;
        int _memoSpecArgumentCount;
        int _memoArgumentIndex = -1;
        int _memoArgumentOffset;

        public void Push(WorkItem item) => _work.Push(item);

        /// <summary>
        /// Resolves an enum SerString to its width, reusing the previous
        /// answer when the name repeats. Every element of a typed enum array
        /// carries the same name, so without this the walk re-parses and
        /// re-projects that name once per declared element — allocation
        /// proportional to an attacker-chosen count, inside the guard whose
        /// purpose is to bound exactly that. The resolver is a frozen table
        /// and the projection is pure, so a repeated name has a repeated
        /// answer and guard/SRM alignment is unaffected.
        /// </summary>
        PrimitiveTypeCode ResolveEnumNameMemoized(string? enumName)
        {
            if (enumName is not null
                && _memoEnumName is not null
                && string.Equals(_memoEnumName, enumName, StringComparison.Ordinal))
            {
                return _memoEnumWidth;
            }

            PrimitiveTypeCode width = ResolveEnumName(
                _reader,
                enumName,
                _enumUnderlyingType);
            if (enumName is not null)
            {
                _memoEnumName = enumName;
                _memoEnumWidth = width;
            }

            return width;
        }

        /// <summary>
        /// Resolves a signature-named enum handle to its width, reusing the
        /// previous answer when the handle repeats. This is the handle-typed
        /// twin of <see cref="ResolveEnumNameMemoized"/> and exists for the
        /// same reason: every element of a typed enum array re-parses the same
        /// element type, so without this the walk resolves that handle once per
        /// attacker-chosen element. Resolving a reference scans the definition
        /// table, which would make the scan itself the amplification the guard
        /// is meant to bound. The width is a pure function of the reader and
        /// the handle, so a repeated handle has a repeated answer and
        /// guard/SRM alignment is unaffected.
        /// </summary>
        PrimitiveTypeCode ResolveEnumHandleMemoized(EntityHandle handle)
        {
            if (!handle.IsNil && handle == _memoEnumHandle)
                return _memoEnumHandleWidth;

            PrimitiveTypeCode width = ResolveEnum(
                _reader,
                handle,
                _beforeMaterialize,
                _enumUnderlyingType);
            if (!handle.IsNil)
            {
                _memoEnumHandle = handle;
                _memoEnumHandleWidth = width;
            }

            return width;
        }

        /// <summary>
        /// Skips a signature-typed named argument, matching SRM. SRM
        /// special-cases only a rendered name of "System.Type"
        /// (ArgTypeProvider.IsSystemType); structural ns+name checks miss
        /// TypeRef {ns="", name="System.Type"} and nested System+Type, both
        /// of which SRM consumes as a SerString.
        /// </summary>
        Result SkipNamedType(EntityHandle handle)
        {
            if (IsSrmSystemTypeMemoized(handle))
                return SkipSerString(ref _value, _beforeMaterialize);
            return SkipBytes(
                ref _value,
                EnumUnderlyingPrimitive.ByteSize(
                    ResolveEnumHandleMemoized(handle)));
        }

        /// <summary>
        /// Reports whether the handle renders as "System.Type", reusing the
        /// previous answer when the handle repeats. Every element of a
        /// signature-typed array of a named type carries the same handle, so
        /// without this the walk renders that name once per declared element
        /// — allocation proportional to an attacker-chosen count, inside the
        /// guard whose purpose is to bound exactly that. The rendered name is
        /// a pure function of the reader and the handle, so a repeated handle
        /// has a repeated answer and guard/SRM alignment is unaffected.
        /// </summary>
        bool IsSrmSystemTypeMemoized(EntityHandle handle)
        {
            if (!handle.IsNil && handle == _memoSystemTypeHandle)
                return _memoIsSystemType;

            bool isSystemType = IsSrmSystemType(_reader, handle);
            if (!handle.IsNil)
            {
                _memoSystemTypeHandle = handle;
                _memoIsSystemType = isSystemType;
            }

            return isSystemType;
        }

        public Result Run()
        {
            while (_work.Count > 0)
            {
                Result result = Dispatch(_work.Pop());
                if (result != Result.Safe)
                    return result;
            }

            return Result.Safe;
        }

        Result Dispatch(WorkItem item)
        {
            switch (item.Op)
            {
                case Op.FixedArgs:
                    return TakeNext(item, ProcessFixedArg);
                case Op.NamedHeader:
                    return ProcessNamedHeader();
                case Op.NamedArgs:
                    return TakeNext(item, ProcessNamedArg);
                case Op.SzArrayElements:
                    return ProcessSzArrayElements(item);
                case Op.TypedArrayElements:
                    return ProcessTypedArrayElements(item);
                case Op.Boxed:
                    return ProcessBoxed(item.Depth);
                case Op.PopFrame:
                    return PopFrame();
                case Op.RestoreSignature:
                    _signature.Offset = item.SignatureEnd;
                    return Result.Safe;
                default:
                    return Result.Unsafe;
            }
        }

        Result TakeNext(WorkItem item, Func<int, Result> process)
        {
            if (item.Remaining <= 0)
                return Result.Safe;
            if (item.Remaining > 1)
            {
                _work.Push(
                    item.Op == Op.FixedArgs
                        ? WorkItem.FixedArgs(item.Remaining - 1, item.Depth)
                        : WorkItem.NamedArgs(item.Remaining - 1, item.Depth));
            }

            return process(item.Depth);
        }

        Result ProcessNamedHeader()
        {
            if (_value.RemainingBytes < 2)
                return Result.Truncated;
            int namedCount = _value.ReadUInt16();
            Charge(_beforeMaterialize, namedCount);
            if (namedCount > _value.RemainingBytes)
                return Result.Unsafe;
            if (namedCount > 0)
                _work.Push(WorkItem.NamedArgs(namedCount, depth: 1));
            return Result.Safe;
        }

        Result ProcessFixedArg(int depth)
        {
            if (depth > MaxSerializedDepth)
                return Result.Unsafe;
            if (!TryReadElementType(ref _signature, out byte code))
                return Result.Safe;
            while (code is ElementTypeCmodReqd or ElementTypeCmodOpt)
            {
                _signature.ReadTypeHandle();
                if (!TryReadElementType(ref _signature, out code))
                    return Result.Safe;
            }

            return code switch
            {
                ElementTypeBoolean or ElementTypeI1 or ElementTypeU1
                    => SkipBytes(ref _value, 1),
                ElementTypeChar or ElementTypeI2 or ElementTypeU2
                    => SkipBytes(ref _value, 2),
                ElementTypeI4 or ElementTypeU4 or ElementTypeR4
                    => SkipBytes(ref _value, 4),
                ElementTypeI8 or ElementTypeU8 or ElementTypeR8
                    => SkipBytes(ref _value, 8),
                ElementTypeString => SkipSerString(
                    ref _value,
                    _beforeMaterialize),
                ElementTypeObject => ProcessBoxed(depth),
                ElementTypeSzArray => ProcessSzArray(depth),
                ElementTypeClass or ElementTypeValueType => SkipNamedType(
                    _signature.ReadTypeHandle()),
                ElementTypeVar or ElementTypeMVar => _substituteGenerics
                    ? ProcessGenericParameter(
                        depth,
                        methodParameter: code == ElementTypeMVar)
                    : Result.Unsafe,
                _ => Result.Unsafe,
            };
        }

        Result ProcessSzArray(int depth)
        {
            if (depth > MaxSerializedDepth)
                return Result.Unsafe;
            if (_value.RemainingBytes < 4)
                return Result.Truncated;
            // Rewind to the element TYPE, not to its custom modifiers.
            // Modifiers are a prefix of the type, not part of the value
            // grammar, so re-reading them per element buys nothing while
            // multiplying work by an attacker-chosen count on input the guard
            // accepts. SRM never spends that cost: its decoder has no case for
            // CMOD_REQD/CMOD_OPT in an argument type and rejects the blob
            // outright, so the guard would burn the entire multiplied scan
            // before the decode it is screening for ever fails.
            SkipCustomModifiers(ref _signature);
            int elementStart = _signature.Offset;
            if (!TrySkipSignatureType(ref _signature, _signatureSkip))
                return Result.Safe;
            int elementEnd = _signature.Offset;
            int count = _value.ReadInt32();
            if (count == -1)
                return Result.Safe;
            if (count < 0)
                return Result.Unsafe;
            Charge(_beforeMaterialize, count);
            if ((uint)count > (uint)_value.RemainingBytes)
                return Result.Unsafe;
            if (count > 0)
            {
                _work.Push(
                    WorkItem.SzArrayElements(
                        count,
                        elementStart,
                        elementEnd,
                        depth + 1));
            }

            return Result.Safe;
        }

        /// <summary>
        /// Advances past any leading custom modifiers, leaving the reader on
        /// the modified type. Returns with the reader unmoved when the blob
        /// ends; the caller's own read reports that truncation.
        /// </summary>
        static void SkipCustomModifiers(ref BlobReader signature)
        {
            while (true)
            {
                int start = signature.Offset;
                if (!TryReadElementType(ref signature, out byte code))
                    return;
                if (code is not (ElementTypeCmodReqd or ElementTypeCmodOpt))
                {
                    signature.Offset = start;
                    return;
                }

                signature.ReadTypeHandle();
            }
        }

        Result ProcessSzArrayElements(WorkItem item)
        {
            if (item.Remaining <= 0)
            {
                _signature.Offset = item.SignatureEnd;
                return Result.Safe;
            }

            if (_value.RemainingBytes == 0)
            {
                _signature.Offset = item.SignatureEnd;
                return Result.Truncated;
            }

            _signature.Offset = item.SignatureStart;
            if (item.Remaining > 1)
            {
                _work.Push(
                    WorkItem.SzArrayElements(
                        item.Remaining - 1,
                        item.SignatureStart,
                        item.SignatureEnd,
                        item.Depth));
            }
            else
            {
                _work.Push(WorkItem.RestoreSignature(item.SignatureEnd));
            }

            return ProcessFixedArg(item.Depth);
        }

        Result ProcessNamedArg(int depth)
        {
            if (depth > MaxSerializedDepth)
                return Result.Unsafe;
            if (!TryReadElementType(ref _value, out byte kind))
                return Result.Truncated;
            if (kind is not (SerializedField or SerializedProperty))
                return Result.Unsafe;

            Result type = ReadFieldOrPropType(
                ref _value,
                depth,
                _beforeMaterialize,
                out byte leaf,
                out int arrayDepth,
                out string? enumName);
            if (type != Result.Safe)
                return type;

            Result name = SkipSerString(
                ref _value,
                _beforeMaterialize);
            if (name != Result.Safe)
                return name;

            return ProcessTypedValue(leaf, arrayDepth, enumName, depth);
        }

        Result ProcessTypedValue(
            byte code,
            int arrayDepth,
            string? enumName,
            int depth)
        {
            if (depth > MaxSerializedDepth)
                return Result.Unsafe;
            if (arrayDepth > 0)
            {
                if (_value.RemainingBytes < 4)
                    return Result.Truncated;
                int count = _value.ReadInt32();
                if (count == -1)
                    return Result.Safe;
                if (count < 0)
                    return Result.Unsafe;
                Charge(_beforeMaterialize, count);
                if ((uint)count > (uint)_value.RemainingBytes)
                    return Result.Unsafe;
                if (count > 0)
                {
                    _work.Push(
                        WorkItem.TypedArrayElements(
                            code,
                            arrayDepth - 1,
                            enumName,
                            count,
                            depth + 1));
                }

                return Result.Safe;
            }

            return code switch
            {
                ElementTypeBoolean or ElementTypeI1 or ElementTypeU1
                    => SkipBytes(ref _value, 1),
                ElementTypeChar or ElementTypeI2 or ElementTypeU2
                    => SkipBytes(ref _value, 2),
                ElementTypeI4 or ElementTypeU4 or ElementTypeR4
                    => SkipBytes(ref _value, 4),
                ElementTypeI8 or ElementTypeU8 or ElementTypeR8
                    => SkipBytes(ref _value, 8),
                ElementTypeString or SerializedType => SkipSerString(
                    ref _value,
                    _beforeMaterialize),
                ElementTypeObject or SerializedBoxed => ProcessBoxed(depth),
                SerializedEnum => SkipBytes(
                    ref _value,
                    EnumUnderlyingPrimitive.ByteSize(
                        ResolveEnumNameMemoized(enumName))),
                _ => Result.Unsafe,
            };
        }

        Result ProcessTypedArrayElements(WorkItem item)
        {
            if (item.Remaining <= 0)
                return Result.Safe;
            if (_value.RemainingBytes == 0)
                return Result.Truncated;
            if (item.Remaining > 1)
            {
                _work.Push(
                    WorkItem.TypedArrayElements(
                        item.Code,
                        item.ArrayDepth,
                        item.EnumName,
                        item.Remaining - 1,
                        item.Depth));
            }

            return ProcessTypedValue(
                item.Code,
                item.ArrayDepth,
                item.EnumName,
                item.Depth);
        }

        Result ProcessBoxed(int depth)
        {
            if (depth > MaxSerializedDepth)
                return Result.Unsafe;
            if (!TryReadElementType(ref _value, out byte code))
                return Result.Truncated;
            return ProcessSerialized(code, depth);
        }

        Result ProcessSerialized(byte code, int depth)
        {
            if (depth > MaxSerializedDepth)
                return Result.Unsafe;
            switch (code)
            {
                case ElementTypeBoolean:
                case ElementTypeI1:
                case ElementTypeU1:
                    return SkipBytes(ref _value, 1);
                case ElementTypeChar:
                case ElementTypeI2:
                case ElementTypeU2:
                    return SkipBytes(ref _value, 2);
                case ElementTypeI4:
                case ElementTypeU4:
                case ElementTypeR4:
                    return SkipBytes(ref _value, 4);
                case ElementTypeI8:
                case ElementTypeU8:
                case ElementTypeR8:
                    return SkipBytes(ref _value, 8);
                case ElementTypeString:
                case SerializedType:
                    return SkipSerString(
                        ref _value,
                        _beforeMaterialize);
                case SerializedBoxed:
                    _work.Push(WorkItem.Boxed(depth + 1));
                    return Result.Safe;
                case SerializedEnum:
                {
                    Result name = TryReadSerString(
                        ref _value,
                        _beforeMaterialize,
                        out string? enumName);
                    return name != Result.Safe
                        ? name
                        : SkipBytes(
                            ref _value,
                            EnumUnderlyingPrimitive.ByteSize(
                                ResolveEnumNameMemoized(enumName)));
                }
                case ElementTypeSzArray:
                {
                    // SRM's DecodeNamedArgumentType(isElementType: true)
                    // consumes the element type — including an ENUM
                    // SerString and further SZARRAY wrappers — before
                    // DecodeArrayArgument reads the Int32 count. Reuse
                    // the named-argument type walk so the count is read
                    // from the same offset and per-element skips do not
                    // re-read the enum name.
                    Result type = ReadFieldOrPropType(
                        ref _value,
                        depth + 1,
                        _beforeMaterialize,
                        out byte leaf,
                        out int arrayDepth,
                        out string? enumName);
                    return type != Result.Safe
                        ? type
                        : ProcessTypedValue(
                            leaf,
                            arrayDepth + 1,
                            enumName,
                            depth + 1);
                }
                default:
                    return Result.Unsafe;
            }
        }

        Result ProcessGenericParameter(int depth, bool methodParameter)
        {
            if (_signature.RemainingBytes < 1)
                return Result.Safe;
            int parameterIndex = _signature.ReadCompressedInteger();
            if (methodParameter || parameterIndex < 0)
                return Result.Unsafe;

            Result located = LocateGenericArgument(
                parameterIndex,
                out bool found,
                out BlobReader instantiation);
            if (located != Result.Safe || !found)
                return located;

            // SRM substitutes once, then recurses with an empty generic
            // context. Re-entering this method on the same TypeSpec is a
            // stack overflow; a substituted VAR/MVAR is therefore Unsafe.
            _frames ??= new Stack<SignatureFrame>();
            _frames.Push(new SignatureFrame(_signature, _substituteGenerics));
            _work.Push(WorkItem.PopFrame());
            _signature = instantiation;
            _substituteGenerics = false;
            return ProcessFixedArg(depth + 1);
        }

        /// <summary>
        /// Locates the generic argument a VAR substitutes to, reusing the work
        /// when the index repeats. The constructor -- and so its TypeSpec -- is
        /// fixed for the whole walk, so validating that blob and stepping to an
        /// argument is the same work every time. A typed array rewinds and
        /// re-reads its element type once per element, so without this the walk
        /// re-validates the entire TypeSpec once per declared element:
        /// allocation proportional to an attacker-chosen count, inside the
        /// guard whose purpose is to bound exactly that. SRM resolves the
        /// element type once before it loops over values, so resolving once
        /// here is also what matches the decoder.
        /// </summary>
        Result LocateGenericArgument(
            int parameterIndex,
            out bool found,
            out BlobReader instantiation)
        {
            instantiation = default;
            found = false;
            if (!_memoSpecResolved)
            {
                _memoSpecResolved = true;
                _memoSpecResult = ResolveConstructorInstantiation(
                    out _memoSpecFound,
                    out _memoSpecArguments,
                    out _memoSpecArgumentCount);
            }

            if (_memoSpecResult != Result.Safe || !_memoSpecFound)
                return _memoSpecResult;
            if (parameterIndex >= _memoSpecArgumentCount)
                return Result.Unsafe;

            instantiation = _memoSpecArguments;
            if (_memoArgumentIndex == parameterIndex)
            {
                instantiation.Offset = _memoArgumentOffset;
                found = true;
                return Result.Safe;
            }

            for (int index = 0; index < parameterIndex; index++)
            {
                // Match SRM CustomAttributeDecoder.SkipType, including its
                // CLASS/VALUETYPE recurse-as-type-code, so the remaining
                // argument is the one DecodeValue will decode.
                if (!TrySkipSrmAttributeType(ref instantiation, depth: 1, _srmSkip))
                    return Result.Unsafe;
            }

            _memoArgumentIndex = parameterIndex;
            _memoArgumentOffset = instantiation.Offset;
            found = true;
            return Result.Safe;
        }

        /// <summary>
        /// Validates the constructor's TypeSpec once and positions a reader on
        /// its first generic argument. <paramref name="found"/> is false when
        /// the blob ends early, which the walk treats as safe rather than
        /// hostile.
        /// </summary>
        Result ResolveConstructorInstantiation(
            out bool found,
            out BlobReader arguments,
            out int argumentCount)
        {
            found = false;
            arguments = default;
            argumentCount = 0;
            if (!TryGetConstructorTypeSpec(_reader, _constructor, out var spec))
                return Result.Unsafe;
            if (!SignatureBlobGuard.IsSafeToDecode(
                    _reader,
                    spec.Signature,
                    SignatureBlobGuard.Kind.TypeSpecification))
            {
                return Result.Unsafe;
            }

            var instantiation = _reader.GetBlobReader(spec.Signature);
            if (!TryReadElementType(ref instantiation, out byte code))
                return Result.Safe;
            while (code is ElementTypeCmodReqd or ElementTypeCmodOpt)
            {
                instantiation.ReadTypeHandle();
                if (!TryReadElementType(ref instantiation, out code))
                    return Result.Safe;
            }

            if (code != ElementTypeGenericInst)
                return Result.Unsafe;
            if (!TryReadElementType(ref instantiation, out byte genericKind))
                return Result.Safe;
            if (genericKind is not (ElementTypeClass or ElementTypeValueType))
                return Result.Unsafe;
            instantiation.ReadTypeHandle();
            if (instantiation.RemainingBytes < 1)
                return Result.Safe;
            argumentCount = instantiation.ReadCompressedInteger();
            arguments = instantiation;
            found = true;
            return Result.Safe;
        }

        Result PopFrame()
        {
            if (_frames is null || _frames.Count == 0)
                return Result.Unsafe;
            var frame = _frames.Pop();
            _signature = frame.Signature;
            _substituteGenerics = frame.SubstituteGenerics;
            return Result.Safe;
        }
    }

    static Result ReadFieldOrPropType(
        ref BlobReader value,
        int depth,
        Action<int>? beforeMaterialize,
        out byte leaf,
        out int arrayDepth,
        out string? enumName)
    {
        leaf = 0;
        arrayDepth = 0;
        enumName = null;
        int typeDepth = depth;
        while (true)
        {
            if (typeDepth > MaxSerializedDepth)
                return Result.Unsafe;
            if (!TryReadElementType(ref value, out byte code))
                return Result.Truncated;
            if (code == ElementTypeSzArray)
            {
                arrayDepth++;
                typeDepth++;
                continue;
            }

            leaf = code;
            if (code == SerializedEnum)
                return TryReadSerString(
                    ref value,
                    beforeMaterialize,
                    out enumName);
            return code is ElementTypeBoolean or ElementTypeChar
                or ElementTypeI1 or ElementTypeU1
                or ElementTypeI2 or ElementTypeU2
                or ElementTypeI4 or ElementTypeU4
                or ElementTypeI8 or ElementTypeU8
                or ElementTypeR4 or ElementTypeR8
                or ElementTypeString or ElementTypeObject
                or SerializedType or SerializedBoxed
                ? Result.Safe
                : Result.Unsafe;
        }
    }

    static PrimitiveTypeCode ResolveEnum(
        MetadataReader reader,
        EntityHandle handle,
        Action<int>? beforeMaterialize,
        Func<string, PrimitiveTypeCode>? enumUnderlyingType)
    {
        // A handle-typed enum is resolved from the definition the signature
        // named, never from its rendered name, and the decoder resolves the
        // same handle through the same function. Distinct definitions can
        // render to one string -- a nested type joins its declaring type with
        // '.', exactly as a namespace joins a type name -- so any name-keyed
        // index must drop one of them, and a reference carries a resolution
        // scope that a flattened spelling discards. Routing this side through a
        // name, or through a caller's resolver, would let the two sides select
        // different definitions and skip different widths.
        if (EnumUnderlyingPrimitive.TryResolveDefinition(
                reader,
                handle,
                out TypeDefinitionHandle definition))
        {
            return EnumUnderlyingPrimitive.FromDefinition(reader, definition);
        }

        if (enumUnderlyingType is not null)
        {
            string? name = TypeResolver.GetTypeName(
                reader,
                handle,
                context: null,
                beforeMaterialize);
            return name is null
                ? PrimitiveTypeCode.Int32
                : EnumUnderlyingPrimitive.Normalize(
                    enumUnderlyingType(name));
        }

        return EnumUnderlyingPrimitive.FromHandle(reader, handle);
    }

    static PrimitiveTypeCode ResolveEnumName(
        MetadataReader reader,
        string? enumName,
        Func<string, PrimitiveTypeCode>? enumUnderlyingType)
    {
        if (enumName is null)
            return PrimitiveTypeCode.Int32;
        string projected = AttributeDecoder.ProjectSerializedEnumName(
            enumUnderlyingType,
            enumName);
        return enumUnderlyingType is not null
            ? EnumUnderlyingPrimitive.Normalize(enumUnderlyingType(projected))
            : EnumUnderlyingPrimitive.FromSerializedName(reader, projected);
    }

    static Result SkipSerString(
        ref BlobReader blob,
        Action<int>? beforeMaterialize)
    {
        Result result = TryReadSerStringLength(
            ref blob,
            out int? length);
        if (result != Result.Safe || length is not { } value)
            return result;

        beforeMaterialize?.Invoke(value);
        blob.Offset += value;
        return Result.Safe;
    }

    static Result TryReadSerString(
        ref BlobReader blob,
        Action<int>? beforeMaterialize,
        out string? text)
    {
        text = null;
        Result result = TryReadSerStringLength(
            ref blob,
            out int? length);
        if (result != Result.Safe || length is not { } value)
            return result;

        beforeMaterialize?.Invoke(value);
        text = blob.ReadUTF8(value);
        return Result.Safe;
    }

    static Result TryReadSerStringLength(
        ref BlobReader blob,
        out int? length)
    {
        length = null;
        if (blob.RemainingBytes < 1)
            return Result.Truncated;
        int offset = blob.Offset;
        if (blob.ReadByte() == 0xFF)
            return Result.Safe;
        blob.Offset = offset;
        int value = blob.ReadCompressedInteger();
        if (blob.RemainingBytes < value)
            return Result.Truncated;
        length = value;
        return Result.Safe;
    }

    static Result SkipBytes(ref BlobReader blob, int count)
    {
        if (count < 0)
            return Result.Unsafe;
        if (blob.RemainingBytes < count)
            return Result.Truncated;
        blob.Offset += count;
        return Result.Safe;
    }

    static bool TrySkipSignatureType(
        ref BlobReader signature,
        Stack<SignatureSkipItem> work)
    {
        work.Clear();
        work.Push(SignatureSkipItem.Type());
        while (work.Count > 0)
        {
            var item = work.Pop();
            switch (item.Op)
            {
                case SignatureSkipOp.Type:
                    if (!TrySkipOneSignatureType(ref signature, work))
                        return false;
                    break;
                case SignatureSkipOp.Types:
                    if (item.Remaining <= 0)
                        break;
                    if (item.Remaining > 1)
                        work.Push(SignatureSkipItem.Types(item.Remaining - 1));
                    if (!TrySkipOneSignatureType(ref signature, work))
                        return false;
                    break;
                case SignatureSkipOp.ArrayShape:
                    if (!TrySkipArrayShape(ref signature))
                        return false;
                    break;
                case SignatureSkipOp.FnPtr:
                    if (!TrySeedFnPtrSkip(ref signature, work))
                        return false;
                    break;
                case SignatureSkipOp.FnPtrParams:
                    if (!TrySkipFnPtrParams(ref signature, item, work))
                        return false;
                    break;
            }
        }

        return true;
    }

    static bool TrySkipOneSignatureType(
        ref BlobReader signature,
        Stack<SignatureSkipItem> work)
    {
        if (!TryReadElementType(ref signature, out byte code))
            return false;
        switch (code)
        {
            case ElementTypeCmodReqd:
            case ElementTypeCmodOpt:
                signature.ReadTypeHandle();
                work.Push(SignatureSkipItem.Type());
                return true;
            case ElementTypeByRef:
            case ElementTypePtr:
            case ElementTypeSzArray:
            case ElementTypePinned:
                work.Push(SignatureSkipItem.Type());
                return true;
            case ElementTypeClass:
            case ElementTypeValueType:
                signature.ReadTypeHandle();
                return true;
            case ElementTypeGenericInst:
                if (!TryReadElementType(ref signature, out _))
                    return false;
                signature.ReadTypeHandle();
                int arguments = signature.ReadCompressedInteger();
                if (arguments < 0)
                    return false;
                if (arguments > 0)
                    work.Push(SignatureSkipItem.Types(arguments));
                return true;
            case ElementTypeArray:
                work.Push(SignatureSkipItem.ArrayShape());
                work.Push(SignatureSkipItem.Type());
                return true;
            case ElementTypeVar:
            case ElementTypeMVar:
                signature.ReadCompressedInteger();
                return true;
            case ElementTypeFnPtr:
                work.Push(SignatureSkipItem.FnPtr());
                return true;
            default:
                return true;
        }
    }

    static bool TrySkipArrayShape(ref BlobReader signature)
    {
        signature.ReadCompressedInteger();
        int sizes = signature.ReadCompressedInteger();
        for (int index = 0; index < sizes; index++)
            signature.ReadCompressedInteger();
        int bounds = signature.ReadCompressedInteger();
        for (int index = 0; index < bounds; index++)
            signature.ReadCompressedSignedInteger();
        return true;
    }

    static bool TrySeedFnPtrSkip(
        ref BlobReader signature,
        Stack<SignatureSkipItem> work)
    {
        if (signature.RemainingBytes < 1)
            return false;
        var header = signature.ReadSignatureHeader();
        if (header.IsGeneric)
        {
            if (signature.RemainingBytes < 1)
                return false;
            signature.ReadCompressedInteger();
        }

        if (signature.RemainingBytes < 1)
            return false;
        int parameterCount = signature.ReadCompressedInteger();
        if (parameterCount < 0
            || (long)parameterCount + 1 > signature.RemainingBytes)
            return false;

        if (parameterCount > 0)
        {
            work.Push(
                SignatureSkipItem.FnPtrParams(
                    parameterCount,
                    header.CallingConvention == SignatureCallingConvention.VarArgs,
                    sentinelSeen: false));
        }

        work.Push(SignatureSkipItem.Type());
        return true;
    }

    static bool TrySkipFnPtrParams(
        ref BlobReader signature,
        SignatureSkipItem item,
        Stack<SignatureSkipItem> work)
    {
        if (item.Remaining <= 0)
            return true;

        bool sentinelSeen = item.SentinelSeen;
        if (signature.RemainingBytes > 0)
        {
            int offset = signature.Offset;
            if (signature.ReadByte() == ElementTypeSentinel)
            {
                if (!item.AllowsSentinel || sentinelSeen)
                    return false;
                sentinelSeen = true;
            }
            else
            {
                signature.Offset = offset;
            }
        }

        if (item.Remaining > 1)
        {
            work.Push(
                SignatureSkipItem.FnPtrParams(
                    item.Remaining - 1,
                    item.AllowsSentinel,
                    sentinelSeen));
        }

        work.Push(SignatureSkipItem.Type());
        return true;
    }

    /// <summary>
    /// Skips one Type the way SRM <c>CustomAttributeDecoder.SkipType</c>
    /// does when walking earlier generic-attribute arguments. That helper
    /// treats a CLASS/VALUETYPE token's TypeDefOrRef coded index as another
    /// type code, so a TypeDef row 4 (coded 16 / BYREF) or TypeRef row 4
    /// (coded 17 / VALUETYPE) consumes the next official argument.
    /// </summary>
    static bool TrySkipSrmAttributeType(
        ref BlobReader signature,
        int depth,
        Stack<SrmSkipItem> work)
    {
        work.Clear();
        work.Push(SrmSkipItem.Type(depth));
        while (work.Count > 0)
        {
            var item = work.Pop();
            switch (item.Op)
            {
                case SrmSkipOp.Types:
                    if (item.Remaining <= 0)
                        continue;
                    if (item.Remaining > 1)
                        work.Push(SrmSkipItem.Types(item.Remaining - 1, item.Depth));
                    work.Push(SrmSkipItem.Type(item.Depth));
                    continue;
                case SrmSkipOp.ArrayShape:
                    if (!TrySkipSrmArrayShape(ref signature))
                        return false;
                    continue;
                case SrmSkipOp.GenericInstArgs:
                    try
                    {
                        int arguments = signature.ReadCompressedInteger();
                        if (arguments < 0 || arguments > signature.RemainingBytes)
                            return false;
                        if (arguments > 0)
                            work.Push(SrmSkipItem.Types(arguments, item.Depth));
                        continue;
                    }
                    catch (BadImageFormatException)
                    {
                        return false;
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return false;
                    }
            }

            if (!TrySkipOneSrmAttributeType(ref signature, item.Depth, work))
                return false;
        }

        return true;
    }

    static bool TrySkipOneSrmAttributeType(
        ref BlobReader signature,
        int depth,
        Stack<SrmSkipItem> work)
    {
        if (depth > MaxSerializedDepth)
            return false;
        try
        {
            if (signature.RemainingBytes < 1)
                return false;
            int typeCode = signature.ReadCompressedInteger();
            switch (typeCode)
            {
                case ElementTypeVoid:
                case ElementTypeBoolean:
                case ElementTypeChar:
                case ElementTypeI1:
                case ElementTypeU1:
                case ElementTypeI2:
                case ElementTypeU2:
                case ElementTypeI4:
                case ElementTypeU4:
                case ElementTypeI8:
                case ElementTypeU8:
                case ElementTypeR4:
                case ElementTypeR8:
                case ElementTypeString:
                case ElementTypeObject:
                case ElementTypeTypedByRef:
                case ElementTypeI:
                case ElementTypeU:
                    return true;
                case ElementTypePtr:
                case ElementTypeByRef:
                case ElementTypePinned:
                case ElementTypeSzArray:
                    work.Push(SrmSkipItem.Type(depth + 1));
                    return true;
                case ElementTypeFnPtr:
                    return TrySeedSrmFnPtrSkip(ref signature, depth + 1, work);
                case ElementTypeArray:
                    work.Push(SrmSkipItem.ArrayShape());
                    work.Push(SrmSkipItem.Type(depth + 1));
                    return true;
                case ElementTypeCmodReqd:
                case ElementTypeCmodOpt:
                    signature.ReadTypeHandle();
                    work.Push(SrmSkipItem.Type(depth + 1));
                    return true;
                case ElementTypeGenericInst:
                    work.Push(SrmSkipItem.GenericInstArgs(depth + 1));
                    work.Push(SrmSkipItem.Type(depth + 1));
                    return true;
                case ElementTypeVar:
                    signature.ReadCompressedInteger();
                    return true;
                case ElementTypeClass:
                case ElementTypeValueType:
                    work.Push(SrmSkipItem.Type(depth + 1));
                    return true;
                default:
                    return false;
            }
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    static bool TrySkipSrmArrayShape(ref BlobReader signature)
    {
        try
        {
            signature.ReadCompressedInteger();
            int sizes = signature.ReadCompressedInteger();
            if (sizes < 0 || sizes > signature.RemainingBytes)
                return false;
            for (int index = 0; index < sizes; index++)
                signature.ReadCompressedInteger();
            int bounds = signature.ReadCompressedInteger();
            if (bounds < 0 || bounds > signature.RemainingBytes)
                return false;
            for (int index = 0; index < bounds; index++)
                signature.ReadCompressedSignedInteger();
            return true;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    static bool TrySeedSrmFnPtrSkip(
        ref BlobReader signature,
        int depth,
        Stack<SrmSkipItem> work)
    {
        if (signature.RemainingBytes < 1)
            return false;
        var header = signature.ReadSignatureHeader();
        if (header.IsGeneric)
        {
            if (signature.RemainingBytes < 1)
                return false;
            signature.ReadCompressedInteger();
        }

        if (signature.RemainingBytes < 1)
            return false;
        int parameterCount = signature.ReadCompressedInteger();
        if (parameterCount < 0
            || (long)parameterCount + 1 > signature.RemainingBytes)
            return false;
        work.Push(SrmSkipItem.Types(parameterCount + 1, depth));
        return true;
    }

    static bool TryGetConstructorTypeSpec(
        MetadataReader reader,
        EntityHandle constructor,
        out TypeSpecification spec)
    {
        spec = default;
        if (constructor.Kind != HandleKind.MemberReference)
            return false;
        EntityHandle parent = reader.GetMemberReference(
            (MemberReferenceHandle)constructor).Parent;
        if (parent.Kind != HandleKind.TypeSpecification)
            return false;
        spec = reader.GetTypeSpecification((TypeSpecificationHandle)parent);
        return !spec.Signature.IsNil;
    }

    static bool TryGetConstructorSignature(
        MetadataReader reader,
        EntityHandle constructor,
        out BlobHandle signature)
    {
        switch (constructor.Kind)
        {
            case HandleKind.MemberReference:
                signature = reader.GetMemberReference(
                    (MemberReferenceHandle)constructor).Signature;
                return !signature.IsNil;
            case HandleKind.MethodDefinition:
                signature = reader.GetMethodDefinition(
                    (MethodDefinitionHandle)constructor).Signature;
                return !signature.IsNil;
            default:
                signature = default;
                return false;
        }
    }

    static bool IsSrmSystemType(MetadataReader reader, EntityHandle handle)
    {
        // Classify through the one shared rule, so this side cannot drift from
        // ArgTypeProvider.IsSystemType. Both sides are pinned behaviorally:
        // GuardClassifiesExactlyAsTheSharedRule drives IsSafeToDecode over
        // built images and fails if this comparison stops agreeing with the
        // shared rule. It crosses the corpus with every layout reachable
        // below -- TypeRef and TypeDef, each carrying the name split across
        // the namespace and name columns or dotted in the name column alone --
        // because the two branches here render separately, and a pin that
        // builds only one of them leaves the other free to diverge.
        //
        // Do not charge through the observer: ResolveEnum already charges when
        // the product path supplies a name oracle, and this check must not
        // double-count or shift declared-slot charges.
        string? name = handle.Kind == HandleKind.TypeDefinition
            ? TypeResolver.GetTypeNameFromDefinition(
                reader,
                (TypeDefinitionHandle)handle)
            : TypeResolver.GetTypeName(reader, handle);
        return SystemTypeArgumentName.Matches(name);
    }

    static bool TryReadElementType(ref BlobReader blob, out byte code)
    {
        if (blob.RemainingBytes < 1)
        {
            code = 0;
            return false;
        }

        code = blob.ReadByte();
        return true;
    }

    static void Charge(Action<int>? beforeMaterialize, int count)
    {
        if (beforeMaterialize is null || count <= 0)
            return;
        int charge = count <= int.MaxValue / DeclaredSlotCharge
            ? count * DeclaredSlotCharge
            : int.MaxValue;
        beforeMaterialize(charge);
    }

    enum Result : byte
    {
        Safe,
        Unsafe,
        Truncated,
    }

    enum Op : byte
    {
        FixedArgs,
        NamedHeader,
        NamedArgs,
        SzArrayElements,
        TypedArrayElements,
        Boxed,
        PopFrame,
        RestoreSignature,
    }

    readonly struct SignatureFrame(BlobReader signature, bool substituteGenerics)
    {
        public BlobReader Signature { get; } = signature;
        public bool SubstituteGenerics { get; } = substituteGenerics;
    }

    readonly struct WorkItem
    {
        public Op Op { get; }
        public int Depth { get; }
        public int Remaining { get; }
        public int SignatureStart { get; }
        public int SignatureEnd { get; }
        public byte Code { get; }
        public int ArrayDepth { get; }
        public string? EnumName { get; }

        WorkItem(
            Op op,
            int depth = 0,
            int remaining = 0,
            int signatureStart = 0,
            int signatureEnd = 0,
            byte code = 0,
            int arrayDepth = 0,
            string? enumName = null)
        {
            Op = op;
            Depth = depth;
            Remaining = remaining;
            SignatureStart = signatureStart;
            SignatureEnd = signatureEnd;
            Code = code;
            ArrayDepth = arrayDepth;
            EnumName = enumName;
        }

        public static WorkItem FixedArgs(int remaining, int depth)
            => new(Op.FixedArgs, depth, remaining);

        public static WorkItem NamedHeader() => new(Op.NamedHeader);

        public static WorkItem NamedArgs(int remaining, int depth)
            => new(Op.NamedArgs, depth, remaining);

        public static WorkItem SzArrayElements(
            int remaining,
            int signatureStart,
            int signatureEnd,
            int depth)
            => new(
                Op.SzArrayElements,
                depth,
                remaining,
                signatureStart,
                signatureEnd);

        public static WorkItem RestoreSignature(int signatureEnd)
            => new(Op.RestoreSignature, signatureEnd: signatureEnd);

        public static WorkItem TypedArrayElements(
            byte code,
            int arrayDepth,
            string? enumName,
            int remaining,
            int depth)
            => new(
                Op.TypedArrayElements,
                depth,
                remaining,
                code: code,
                arrayDepth: arrayDepth,
                enumName: enumName);

        public static WorkItem Boxed(int depth) => new(Op.Boxed, depth);

        public static WorkItem PopFrame() => new(Op.PopFrame);
    }

    enum SignatureSkipOp : byte
    {
        Type,
        Types,
        ArrayShape,
        FnPtr,
        FnPtrParams,
    }

    readonly struct SignatureSkipItem
    {
        public SignatureSkipOp Op { get; }
        public int Remaining { get; }
        public bool AllowsSentinel { get; }
        public bool SentinelSeen { get; }

        SignatureSkipItem(
            SignatureSkipOp op,
            int remaining = 0,
            bool allowsSentinel = false,
            bool sentinelSeen = false)
        {
            Op = op;
            Remaining = remaining;
            AllowsSentinel = allowsSentinel;
            SentinelSeen = sentinelSeen;
        }

        public static SignatureSkipItem Type() => new(SignatureSkipOp.Type);

        public static SignatureSkipItem Types(int remaining)
            => new(SignatureSkipOp.Types, remaining);

        public static SignatureSkipItem ArrayShape()
            => new(SignatureSkipOp.ArrayShape);

        public static SignatureSkipItem FnPtr() => new(SignatureSkipOp.FnPtr);

        public static SignatureSkipItem FnPtrParams(
            int remaining,
            bool allowsSentinel,
            bool sentinelSeen)
            => new(
                SignatureSkipOp.FnPtrParams,
                remaining,
                allowsSentinel,
                sentinelSeen);
    }

    enum SrmSkipOp : byte
    {
        Type,
        Types,
        ArrayShape,
        GenericInstArgs,
    }

    readonly struct SrmSkipItem
    {
        public SrmSkipOp Op { get; }
        public int Depth { get; }
        public int Remaining { get; }

        SrmSkipItem(SrmSkipOp op, int depth, int remaining = 0)
        {
            Op = op;
            Depth = depth;
            Remaining = remaining;
        }

        public static SrmSkipItem Type(int depth) => new(SrmSkipOp.Type, depth);

        public static SrmSkipItem Types(int remaining, int depth)
            => new(SrmSkipOp.Types, depth, remaining);

        public static SrmSkipItem ArrayShape() => new(SrmSkipOp.ArrayShape, 0);

        public static SrmSkipItem GenericInstArgs(int depth)
            => new(SrmSkipOp.GenericInstArgs, depth);
    }

    const byte ElementTypeVoid = 0x01;
    const byte ElementTypeBoolean = 0x02;
    const byte ElementTypeChar = 0x03;
    const byte ElementTypeI1 = 0x04;
    const byte ElementTypeU1 = 0x05;
    const byte ElementTypeI2 = 0x06;
    const byte ElementTypeU2 = 0x07;
    const byte ElementTypeI4 = 0x08;
    const byte ElementTypeU4 = 0x09;
    const byte ElementTypeI8 = 0x0a;
    const byte ElementTypeU8 = 0x0b;
    const byte ElementTypeR4 = 0x0c;
    const byte ElementTypeR8 = 0x0d;
    const byte ElementTypeString = 0x0e;
    const byte ElementTypePtr = 0x0f;
    const byte ElementTypeByRef = 0x10;
    const byte ElementTypeValueType = 0x11;
    const byte ElementTypeClass = 0x12;
    const byte ElementTypeVar = 0x13;
    const byte ElementTypeArray = 0x14;
    const byte ElementTypeGenericInst = 0x15;
    const byte ElementTypeTypedByRef = 0x16;
    const byte ElementTypeI = 0x18;
    const byte ElementTypeU = 0x19;
    const byte ElementTypeObject = 0x1c;
    const byte ElementTypeFnPtr = 0x1b;
    const byte ElementTypeSzArray = 0x1d;
    const byte ElementTypeMVar = 0x1e;
    const byte ElementTypeCmodReqd = 0x1f;
    const byte ElementTypeCmodOpt = 0x20;
    const byte ElementTypeSentinel = 0x41;
    const byte ElementTypePinned = 0x45;
    const byte SerializedType = 0x50;
    const byte SerializedBoxed = 0x51;
    const byte SerializedField = 0x53;
    const byte SerializedProperty = 0x54;
    const byte SerializedEnum = 0x55;
}
