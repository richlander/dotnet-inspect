using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// The owned custom-attribute value decoder.
///
/// A custom-attribute value blob is not self-describing: the value bytes carry
/// only data, and the separate constructor signature says how wide each value
/// is. Decoding one therefore reads two attacker-supplied structures together.
/// This decoder owns the walk and materializes
/// <see cref="CustomAttributeValue{TType}"/> directly. See
/// <c>docs/design/custom-attribute-value-decoding.md</c>.
///
/// The decode is all-or-nothing (D2): a blob whose <em>structure</em> cannot
/// be followed yields a refusal, never a partial value and never a guess about
/// where the next element begins. No allocation is ever sized from a declared
/// count that exceeds the remaining bytes (D1). Enum widths that no structural,
/// local, trusted, or caller path can resolve default to
/// <see cref="PrimitiveTypeCode.Int32"/>, and that default is reported
/// out-of-band through the detailed decode result rather than hidden inside the
/// value (D2's "visibly" clause).
///
/// The value walk uses an explicit heap work-stack rather than native
/// recursion, so a deeply nested blob cannot overflow the native stack before
/// <see cref="MaxSerializedDepth"/> is consulted;
/// <c>CustomAttributeValueDecoderTests.DeeplyNestedObjectArray_OnSmallNativeStack_Decodes</c>
/// is that gate. Enum argument widths come from
/// <see cref="EnumUnderlyingPrimitive"/> and the shared type-definition index.
/// </summary>
internal static class CustomAttributeValueDecoder
{
    /// <summary>
    /// Legacy conservative decode-work charge for one materialized
    /// <c>CustomAttributeTypedArgument</c> / <c>CustomAttributeNamedArgument</c>
    /// builder slot, reported through the caller's observer before the slot is
    /// allocated. It is a <em>proxy for decode work per declared slot</em>, not
    /// exact retained-byte accounting: once boxing is included the retained
    /// output is <c>O(B + S*(C+N))</c> (D1), which #5755 owns the representation
    /// evidence for. The value <c>16</c> is preserved from the paired-walker era
    /// because existing observer consumers budget against it; #5733 owns the
    /// D1 cost gate that would justify retuning it.
    /// </summary>
    public const int DeclaredSlotCharge = 16;

    /// <summary>
    /// Maximum boxed / SZArray nesting depth admitted while decoding a value
    /// blob. Matches <see cref="SignatureBlobGuard.DefaultMaxDepth"/>: far above
    /// legal attributes, far below a default managed thread stack. Nesting past
    /// this depth is refused.
    /// </summary>
    public const int MaxSerializedDepth = SignatureBlobGuard.DefaultMaxDepth;

    /// <summary>
    /// Returns <see langword="true"/> and a materialized <paramref name="value"/>
    /// when decoding succeeds, or <see langword="false"/> when the blob is refused.
    /// Caller-callback failures are raised as
    /// <see cref="AttributeDecoder.CallerCallbackException"/>
    /// so the public edge can rethrow the original unchanged; only
    /// malformed-input exceptions become a refusal.
    /// </summary>
    internal static bool TryDecode(
        MetadataReader reader,
        CustomAttribute attribute,
        bool preserveSerializedTypeNames,
        bool captureDefaultedWidths,
        Action<int>? beforeMaterialize,
        AttributeDecoder.EnumWidthResolver? enumUnderlyingType,
        out CustomAttributeValue<string> value,
        out ImmutableArray<bool> fixedArgumentWidthDefaulted,
        out ImmutableArray<bool> namedArgumentWidthDefaulted,
        GenericContextWork? genericContextWork = null)
    {
        value = default;
        fixedArgumentWidthDefaulted = default;
        namedArgumentWidthDefaulted = default;

        try
        {
            // Constructor access reads and validates metadata too.
            var decoder = new Decoder(
                reader,
                attribute.Constructor,
                preserveSerializedTypeNames,
                captureDefaultedWidths,
                beforeMaterialize,
                enumUnderlyingType,
                genericContextWork);
            return decoder.Run(
                attribute,
                out value,
                out fixedArgumentWidthDefaulted,
                out namedArgumentWidthDefaulted);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            // Malformed structure — including truncation, a bad signature, and
            // a definition-index failure — is a decode outcome, not a laundered
            // exception. A caller callback failure is wrapped in
            // CallerCallbackException, which is not one of these types, so it
            // escapes here and is rethrown at the public edge. Resource
            // exhaustion (OutOfMemoryException) and every other internal failure
            // also propagate: this filter is exact, never a bare catch.
            value = default;
            fixedArgumentWidthDefaulted = default;
            namedArgumentWidthDefaulted = default;
            return false;
        }
    }

    internal sealed class GenericContextWork
    {
        public long BytesSkipped { get; internal set; }
    }

    /// <summary>
    /// One decode of one attribute value. Fixed arguments are read from the
    /// constructor signature interleaved with their values from the value blob,
    /// exactly once each (matching SRM's <c>CustomAttributeDecoder</c>); the
    /// value tree beneath each argument is walked iteratively on
    /// <see cref="_work"/> so nesting cannot overflow the native stack.
    /// </summary>
    sealed class Decoder(
        MetadataReader reader,
        EntityHandle constructor,
        bool preserveSerializedTypeNames,
        bool captureDefaultedWidths,
        Action<int>? beforeMaterialize,
        AttributeDecoder.EnumWidthResolver? enumUnderlyingType,
        GenericContextWork? genericContextWork)
    {
        readonly MetadataReader _reader = reader;
        readonly EntityHandle _constructor = constructor;
        readonly bool _captureDefaultedWidths = captureDefaultedWidths;
        readonly Action<int>? _beforeMaterialize = beforeMaterialize;
        readonly Classifier _classifier = new(
            reader,
            preserveSerializedTypeNames,
            beforeMaterialize,
            enumUnderlyingType);
        readonly Stack<ValueJob> _work = new();

        BlobReader _value;
        BlobReader _genericArgumentCursor;
        List<int>? _genericArgumentOffsets;
        int _genericParameterCount;
        bool _currentArgumentDefaulted;
        CustomAttributeTypedArgument<string> _rootResult;

        public bool Run(
            CustomAttribute attribute,
            out CustomAttributeValue<string> value,
            out ImmutableArray<bool> fixedDefaulted,
            out ImmutableArray<bool> namedDefaulted)
        {
            value = default;
            fixedDefaulted = default;
            namedDefaulted = default;

            if (!TryGetConstructorSignature(
                    _reader,
                    _constructor,
                    out BlobHandle signatureHandle))
            {
                return false;
            }

            var signature = _reader.GetBlobReader(signatureHandle);
            _value = _reader.GetBlobReader(attribute.Value);

            if (_value.ReadUInt16() != 1)
                throw new BadImageFormatException();

            var header = signature.ReadSignatureHeader();
            if (header.Kind != SignatureKind.Method || header.IsGeneric)
                throw new BadImageFormatException();

            int parameterCount = signature.ReadCompressedInteger();
            if (parameterCount < 0)
                throw new BadImageFormatException();
            if (signature.ReadSignatureTypeCode() != SignatureTypeCode.Void)
                throw new BadImageFormatException();

            BlobReader genericContext = ResolveGenericContext();

            // Never size the fixed-argument builder from a declared count that
            // exceeds the remaining value bytes: every fixed argument consumes
            // at least one value byte, so this cannot reject a legal attribute.
            if (parameterCount > _value.RemainingBytes)
                throw new BadImageFormatException();

            ChargeSlots(parameterCount);
            var fixedArguments =
                ImmutableArray.CreateBuilder<CustomAttributeTypedArgument<string>>(
                    parameterCount);
            var fixedFlags = _captureDefaultedWidths
                ? ImmutableArray.CreateBuilder<bool>(parameterCount)
                : null;

            for (int i = 0; i < parameterCount; i++)
            {
                _currentArgumentDefaulted = false;
                ArgumentType info = DecodeFixedArgumentType(
                    ref signature,
                    genericContext,
                    isElementType: false,
                    depth: 1);
                CustomAttributeTypedArgument<string> argument =
                    DecodeArgument(info, depth: 1);
                fixedArguments.Add(argument);
                fixedFlags?.Add(_currentArgumentDefaulted);
            }

            int namedCount = _value.ReadUInt16();
            if (namedCount > _value.RemainingBytes)
                throw new BadImageFormatException();

            ChargeSlots(namedCount);
            var namedArguments =
                ImmutableArray.CreateBuilder<CustomAttributeNamedArgument<string>>(
                    namedCount);
            var namedFlags = _captureDefaultedWidths
                ? ImmutableArray.CreateBuilder<bool>(namedCount)
                : null;

            for (int i = 0; i < namedCount; i++)
            {
                _currentArgumentDefaulted = false;
                var kind = (CustomAttributeNamedArgumentKind)
                    _value.ReadSerializationTypeCode();
                if (kind != CustomAttributeNamedArgumentKind.Field
                    && kind != CustomAttributeNamedArgumentKind.Property)
                {
                    throw new BadImageFormatException();
                }

                ArgumentType info = DecodeNamedArgumentType(
                    isElementType: false,
                    depth: 1);
                string? name = ReadSerializedString();
                CustomAttributeTypedArgument<string> argument =
                    DecodeArgument(info, depth: 1);
                namedArguments.Add(
                    new CustomAttributeNamedArgument<string>(
                        name,
                        kind,
                        argument.Type,
                        argument.Value));
                namedFlags?.Add(_currentArgumentDefaulted);
            }

            value = new CustomAttributeValue<string>(
                fixedArguments.MoveToImmutable(),
                namedArguments.MoveToImmutable());

            if (_captureDefaultedWidths)
            {
                fixedDefaulted = fixedFlags!.MoveToImmutable();
                namedDefaulted = namedFlags!.MoveToImmutable();
            }
            return true;
        }

        BlobReader ResolveGenericContext()
        {
            if (_constructor.Kind != HandleKind.MemberReference)
                return default;
            EntityHandle parent = _reader.GetMemberReference(
                (MemberReferenceHandle)_constructor).Parent;
            if (parent.Kind != HandleKind.TypeSpecification)
                return default;

            var spec = _reader.GetTypeSpecification(
                (TypeSpecificationHandle)parent);
            if (spec.Signature.IsNil)
                return default;

            var context = _reader.GetBlobReader(spec.Signature);
            if (context.ReadSignatureTypeCode()
                != SignatureTypeCode.GenericTypeInstance)
            {
                // Some other TypeSpec. Do not resolve generic parameters from a
                // broken blob; a VAR in the signature then refuses.
                return default;
            }

            int kind = context.ReadCompressedInteger();
            if (kind != (int)SignatureTypeKind.Class
                && kind != (int)SignatureTypeKind.ValueType)
            {
                throw new BadImageFormatException();
            }

            context.ReadTypeHandle();
            // Positioned at "GenArgCount Type Type*".
            return context;
        }

        BlobReader LocateGenericArgument(BlobReader genericContext, int parameterIndex)
        {
            if (_genericArgumentOffsets is null)
            {
                _genericArgumentCursor = genericContext;
                _genericParameterCount = _genericArgumentCursor.ReadCompressedInteger();
            }
            if (parameterIndex >= _genericParameterCount)
                throw new BadImageFormatException();

            // Retain only visited starts, never storage sized by declared arity.
            // Leave unused suffixes alone and preserve the existing skipper's
            // cursor semantics rather than decoding types into the index.
            _genericArgumentOffsets ??= [_genericArgumentCursor.Offset];
            while (_genericArgumentOffsets.Count <= parameterIndex)
            {
                int start = _genericArgumentCursor.Offset;
                bool skipped = SrmType.TrySkip(ref _genericArgumentCursor);
                if (genericContextWork is not null)
                    genericContextWork.BytesSkipped += _genericArgumentCursor.Offset - start;
                if (!skipped)
                    throw new BadImageFormatException();
                _genericArgumentOffsets.Add(_genericArgumentCursor.Offset);
            }

            BlobReader arguments = genericContext;
            arguments.Offset = _genericArgumentOffsets[parameterIndex];
            return arguments;
        }

        // Reads one fixed-argument type from the constructor signature. Renders
        // the type name and resolves any enum width exactly once; array element
        // types are read here once and the value loop never re-reads them.
        ArgumentType DecodeFixedArgumentType(
            ref BlobReader signature,
            BlobReader genericContext,
            bool isElementType,
            int depth)
        {
            if (depth > MaxSerializedDepth)
                throw new BadImageFormatException();

            SignatureTypeCode code = signature.ReadSignatureTypeCode();
            switch (code)
            {
                case SignatureTypeCode.Boolean:
                case SignatureTypeCode.Char:
                case SignatureTypeCode.SByte:
                case SignatureTypeCode.Byte:
                case SignatureTypeCode.Int16:
                case SignatureTypeCode.UInt16:
                case SignatureTypeCode.Int32:
                case SignatureTypeCode.UInt32:
                case SignatureTypeCode.Int64:
                case SignatureTypeCode.UInt64:
                case SignatureTypeCode.Single:
                case SignatureTypeCode.Double:
                case SignatureTypeCode.String:
                    return ArgumentType.Scalar(
                        _classifier.GetPrimitiveType((PrimitiveTypeCode)code),
                        (SerializationTypeCode)code);

                case SignatureTypeCode.Object:
                    return ArgumentType.Scalar(
                        _classifier.GetPrimitiveType(PrimitiveTypeCode.Object),
                        SerializationTypeCode.TaggedObject);

                case SignatureTypeCode.TypeHandle:
                {
                    EntityHandle handle = signature.ReadTypeHandle();
                    string type = _classifier.GetTypeFromHandle(handle);
                    if (_classifier.IsSystemType(type))
                        return ArgumentType.Scalar(type, SerializationTypeCode.Type);
                    var underlying = (SerializationTypeCode)
                        ResolveEnumWidth(type);
                    return ArgumentType.Scalar(type, underlying);
                }

                case SignatureTypeCode.SZArray:
                {
                    if (isElementType)
                        throw new BadImageFormatException(); // jagged, refused
                    ArgumentType element = DecodeFixedArgumentType(
                        ref signature,
                        genericContext,
                        isElementType: true,
                        depth + 1);
                    return ArgumentType.Array(
                        _classifier.GetSZArrayType(element.Type),
                        element);
                }

                case SignatureTypeCode.GenericTypeParameter:
                {
                    if (genericContext.Length == 0)
                        throw new BadImageFormatException();
                    int parameterIndex = signature.ReadCompressedInteger();
                    if (parameterIndex < 0)
                        throw new BadImageFormatException();
                    BlobReader arguments = LocateGenericArgument(
                        genericContext, parameterIndex);

                    // Substitute once, then decode with an empty generic
                    // context so a self-referential VAR cannot recurse.
                    return DecodeFixedArgumentType(
                        ref arguments,
                        default,
                        isElementType,
                        depth + 1);
                }

                default:
                    // GenericMethodParameter (MVAR), custom modifiers, and every
                    // other code are refused.
                    throw new BadImageFormatException();
            }
        }

        // Reads one named-argument (or boxed / object[]-element) type inline
        // from the value blob.
        ArgumentType DecodeNamedArgumentType(bool isElementType, int depth)
        {
            if (depth > MaxSerializedDepth)
                throw new BadImageFormatException();

            SerializationTypeCode code = _value.ReadSerializationTypeCode();
            switch (code)
            {
                case SerializationTypeCode.Boolean:
                case SerializationTypeCode.Char:
                case SerializationTypeCode.SByte:
                case SerializationTypeCode.Byte:
                case SerializationTypeCode.Int16:
                case SerializationTypeCode.UInt16:
                case SerializationTypeCode.Int32:
                case SerializationTypeCode.UInt32:
                case SerializationTypeCode.Int64:
                case SerializationTypeCode.UInt64:
                case SerializationTypeCode.Single:
                case SerializationTypeCode.Double:
                case SerializationTypeCode.String:
                    return ArgumentType.Scalar(
                        _classifier.GetPrimitiveType((PrimitiveTypeCode)code),
                        code);

                case SerializationTypeCode.Type:
                    return ArgumentType.Scalar(
                        _classifier.GetSystemType(),
                        SerializationTypeCode.Type);

                case SerializationTypeCode.TaggedObject:
                    return ArgumentType.Scalar(
                        _classifier.GetPrimitiveType(PrimitiveTypeCode.Object),
                        SerializationTypeCode.TaggedObject);

                case SerializationTypeCode.SZArray:
                {
                    if (isElementType)
                        throw new BadImageFormatException(); // jagged, refused
                    ArgumentType element = DecodeNamedArgumentType(
                        isElementType: true,
                        depth + 1);
                    return ArgumentType.Array(
                        _classifier.GetSZArrayType(element.Type),
                        element);
                }

                case SerializationTypeCode.Enum:
                {
                    string? name = ReadSerializedString();
                    if (name is null)
                        throw new BadImageFormatException();
                    string type = _classifier.GetTypeFromSerializedName(name);
                    var underlying = (SerializationTypeCode)ResolveEnumWidth(type);
                    return ArgumentType.Scalar(type, underlying);
                }

                default:
                    throw new BadImageFormatException();
            }
        }

        // Iteratively decodes the value tree rooted at one argument. Runs the
        // work-stack to completion so a top-level argument fully materializes
        // before the next one begins; native recursion is never used.
        CustomAttributeTypedArgument<string> DecodeArgument(
            ArgumentType info,
            int depth)
        {
            _work.Push(ValueJob.Decode(info, depth, container: null));
            while (_work.Count > 0)
            {
                ValueJob job = _work.Pop();
                if (job.Kind == ValueJobKind.ArrayLoop)
                    ProcessArrayLoop(job.Container!);
                else
                    ProcessDecode(job.Info, job.Depth, job.Container);
            }

            return _rootResult;
        }

        void ProcessDecode(ArgumentType info, int depth, ArrayContainer? container)
        {
            if (depth > MaxSerializedDepth)
                throw new BadImageFormatException();

            if (info.Code == SerializationTypeCode.TaggedObject)
            {
                // The boxing is itself one nesting level, so charge depth for
                // it before reading the boxed type. A boxed object whose boxed
                // type is again a boxed object (0x51 0x51) is refused here just
                // as SRM refuses it: DecodeNamedArgumentType yields TaggedObject
                // and the value switch below has no case for it.
                depth++;
                if (depth > MaxSerializedDepth)
                    throw new BadImageFormatException();
                info = DecodeNamedArgumentType(isElementType: false, depth);
            }

            switch (info.Code)
            {
                case SerializationTypeCode.SZArray:
                    ProcessArray(info, depth, container);
                    return;

                case SerializationTypeCode.String:
                {
                    string? text = ReadSerializedString();
                    Deliver(new CustomAttributeTypedArgument<string>(info.Type, text), container);
                    return;
                }

                case SerializationTypeCode.Type:
                {
                    string? name = ReadSerializedString();
                    object? value = name is not null
                        ? _classifier.GetTypeFromSerializedName(name)
                        : null;
                    Deliver(new CustomAttributeTypedArgument<string>(info.Type, value), container);
                    return;
                }

                default:
                {
                    object? value = ReadScalar(info.Code);
                    Deliver(new CustomAttributeTypedArgument<string>(info.Type, value), container);
                    return;
                }
            }
        }

        void ProcessArray(ArgumentType info, int depth, ArrayContainer? container)
        {
            int count = _value.ReadInt32();
            if (count == -1)
            {
                // A null array. SRM materializes a null value.
                Deliver(new CustomAttributeTypedArgument<string>(info.Type, null), container);
                return;
            }

            if (count == 0)
            {
                Deliver(
                    new CustomAttributeTypedArgument<string>(
                        info.Type,
                        ImmutableArray<CustomAttributeTypedArgument<string>>.Empty),
                    container);
                return;
            }

            if (count < 0)
                throw new BadImageFormatException();

            if ((uint)count > (uint)_value.RemainingBytes)
                throw new BadImageFormatException(); // refuse before allocating

            ChargeSlots(count);
            var child = new ArrayContainer(
                ImmutableArray.CreateBuilder<CustomAttributeTypedArgument<string>>(count),
                info.Type,
                info.Element,
                depth + 1,
                count,
                container);
            _work.Push(ValueJob.ArrayLoop(child));
        }

        void ProcessArrayLoop(ArrayContainer container)
        {
            if (container.Remaining == 0)
            {
                Deliver(
                    new CustomAttributeTypedArgument<string>(
                        container.Type,
                        container.Builder.MoveToImmutable()),
                    container.Parent);
                return;
            }

            if (_value.RemainingBytes == 0)
                throw new BadImageFormatException(); // truncated mid-array

            container.Remaining--;
            _work.Push(ValueJob.ArrayLoop(container));
            _work.Push(
                ValueJob.Decode(container.Element, container.ElementDepth, container));
        }

        void Deliver(
            CustomAttributeTypedArgument<string> argument,
            ArrayContainer? container)
        {
            if (container is null)
                _rootResult = argument;
            else
                container.Builder.Add(argument);
        }

        object? ReadScalar(SerializationTypeCode code)
        {
            return code switch
            {
                SerializationTypeCode.Boolean => _value.ReadBoolean(),
                SerializationTypeCode.Byte => _value.ReadByte(),
                SerializationTypeCode.SByte => _value.ReadSByte(),
                SerializationTypeCode.Char => _value.ReadChar(),
                SerializationTypeCode.Int16 => _value.ReadInt16(),
                SerializationTypeCode.UInt16 => _value.ReadUInt16(),
                SerializationTypeCode.Int32 => _value.ReadInt32(),
                SerializationTypeCode.UInt32 => _value.ReadUInt32(),
                SerializationTypeCode.Int64 => _value.ReadInt64(),
                SerializationTypeCode.UInt64 => _value.ReadUInt64(),
                SerializationTypeCode.Single => _value.ReadSingle(),
                SerializationTypeCode.Double => _value.ReadDouble(),
                _ => throw new BadImageFormatException(),
            };
        }

        // Charges the serialized byte length before decoding the string.
        string? ReadSerializedString()
        {
            if (_value.RemainingBytes < 1)
                throw new BadImageFormatException();
            int offset = _value.Offset;
            if (_value.ReadByte() == 0xFF)
                return null;
            _value.Offset = offset;
            int length = _value.ReadCompressedInteger();
            if (length < 0 || _value.RemainingBytes < length)
                throw new BadImageFormatException();
            Observe(length);
            return _value.ReadUTF8(length);
        }

        PrimitiveTypeCode ResolveEnumWidth(string type)
        {
            PrimitiveTypeCode width = _classifier.GetUnderlyingEnumType(type);
            if (_classifier.LastResolutionDefaulted)
                _currentArgumentDefaulted = true;
            return width;
        }

        // Charges the caller's observer for a declared count of materialized
        // slots, saturating so a hostile count cannot overflow the charge.
        void ChargeSlots(int count)
        {
            if (count <= 0)
                return;
            int charge = count <= int.MaxValue / DeclaredSlotCharge
                ? count * DeclaredSlotCharge
                : int.MaxValue;
            Observe(charge);
        }

        // Invokes the caller observer, wrapping any failure in the provenance
        // sentinel so it is never misclassified as a malformed blob.
        void Observe(int amount)
        {
            if (_beforeMaterialize is null || amount <= 0)
                return;
            try
            {
                _beforeMaterialize(amount);
            }
            catch (Exception ex)
            {
                throw new AttributeDecoder.CallerCallbackException(
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex));
            }
        }
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

    /// <summary>Rendered type and (for arrays) element type of one argument.</summary>
    readonly struct ArgumentType
    {
        public string Type { get; }
        public SerializationTypeCode Code { get; }
        public string ElementType { get; }
        public SerializationTypeCode ElementCode { get; }

        ArgumentType(
            string type,
            SerializationTypeCode code,
            string elementType,
            SerializationTypeCode elementCode)
        {
            Type = type;
            Code = code;
            ElementType = elementType;
            ElementCode = elementCode;
        }

        public ArgumentType Element =>
            new(ElementType, ElementCode, string.Empty, default);

        public static ArgumentType Scalar(string type, SerializationTypeCode code)
            => new(type, code, string.Empty, default);

        public static ArgumentType Array(string type, ArgumentType element)
            => new(type, SerializationTypeCode.SZArray, element.Type, element.Code);
    }

    sealed class ArrayContainer(
        ImmutableArray<CustomAttributeTypedArgument<string>>.Builder builder,
        string type,
        ArgumentType element,
        int elementDepth,
        int remaining,
        ArrayContainer? parent)
    {
        public ImmutableArray<CustomAttributeTypedArgument<string>>.Builder Builder { get; }
            = builder;
        public string Type { get; } = type;
        public ArgumentType Element { get; } = element;
        public int ElementDepth { get; } = elementDepth;
        public int Remaining { get; set; } = remaining;
        public ArrayContainer? Parent { get; } = parent;
    }

    enum ValueJobKind : byte
    {
        Decode,
        ArrayLoop,
    }

    readonly struct ValueJob
    {
        public ValueJobKind Kind { get; }
        public ArgumentType Info { get; }
        public int Depth { get; }
        public ArrayContainer? Container { get; }

        ValueJob(
            ValueJobKind kind,
            ArgumentType info,
            int depth,
            ArrayContainer? container)
        {
            Kind = kind;
            Info = info;
            Depth = depth;
            Container = container;
        }

        public static ValueJob Decode(
            ArgumentType info,
            int depth,
            ArrayContainer? container)
            => new(ValueJobKind.Decode, info, depth, container);

        public static ValueJob ArrayLoop(ArrayContainer container)
            => new(ValueJobKind.ArrayLoop, default, 0, container);
    }

    /// <summary>
    /// Renders argument type names and resolves enum widths for the owned
    /// decoder. Handle-typed enums resolve directly from the handle the
    /// signature named (a rendered name cannot carry the identity that
    /// distinguishes two definitions or an external reference from a local
    /// type); serialized-name enums resolve by projected name through the
    /// shared type-definition index, then a caller resolver, then the
    /// <see cref="PrimitiveTypeCode.Int32"/> default. Type-name rendering and
    /// index construction retain
    /// their existing observer charges, while the value walk owns declared-slot
    /// and serialized-string charges.
    /// </summary>
    internal sealed class Classifier(
        MetadataReader reader,
        bool preserveSerializedTypeNames,
        Action<int>? beforeMaterialize,
        AttributeDecoder.EnumWidthResolver? enumUnderlyingType)
    {
        readonly MetadataReader _reader = reader;
        readonly bool _preserveSerializedTypeNames = preserveSerializedTypeNames;
        readonly Action<int>? _beforeMaterialize = beforeMaterialize;
        readonly AttributeDecoder.EnumWidthResolver? _enumUnderlyingType =
            enumUnderlyingType;
        readonly AttributeDecoder.MaterializationContext? _materializationContext =
            beforeMaterialize?.Target as AttributeDecoder.MaterializationContext;

        Dictionary<string, TypeDefinitionHandle>? _typeDefinitionsByName;
        bool _lastNameFromBlob;
        TypeDefinitionHandle _pendingDefinition;
        TypeReferenceHandle _pendingReference;
        MetadataReader? _pendingReader;

        /// <summary>
        /// Whether the most recent <see cref="GetUnderlyingEnumType"/> reached
        /// the <see cref="PrimitiveTypeCode.Int32"/> default because no
        /// structural, local, or caller path resolved the width.
        /// </summary>
        public bool LastResolutionDefaulted { get; private set; }

        public string GetPrimitiveType(PrimitiveTypeCode code) => code switch
        {
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.SByte => "sbyte",
            PrimitiveTypeCode.Byte => "byte",
            PrimitiveTypeCode.Int16 => "short",
            PrimitiveTypeCode.UInt16 => "ushort",
            PrimitiveTypeCode.Int32 => "int",
            PrimitiveTypeCode.UInt32 => "uint",
            PrimitiveTypeCode.Int64 => "long",
            PrimitiveTypeCode.UInt64 => "ulong",
            PrimitiveTypeCode.Single => "float",
            PrimitiveTypeCode.Double => "double",
            PrimitiveTypeCode.String => "string",
            _ => "object",
        };

        public string GetSystemType() => "System.Type";

        public bool IsSystemType(string type) => type == "System.Type";

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetTypeFromHandle(EntityHandle handle) => handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeFromDefinition(
                _reader,
                (TypeDefinitionHandle)handle,
                0),
            HandleKind.TypeReference => GetTypeFromReference(
                _reader,
                (TypeReferenceHandle)handle,
                0),
            _ => throw new BadImageFormatException(),
        };

        public string GetTypeFromDefinition(
            MetadataReader r,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
        {
            // Remember the definition itself, not just its rendered name, and
            // the reader it belongs to: a definition-typed enum resolves its
            // width straight from this handle, because distinct definitions can
            // render to the same string.
            _lastNameFromBlob = false;
            _pendingDefinition = handle;
            _pendingReference = default;
            _pendingReader = r;
            return TypeResolver.GetTypeNameFromDefinition(
                r,
                handle,
                ObserveBeforeMaterialize);
        }

        public string GetTypeFromReference(
            MetadataReader r,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            // A reference carries a resolution scope its flattened spelling
            // discards, so remember the reference and resolve it structurally.
            _lastNameFromBlob = false;
            _pendingDefinition = default;
            _pendingReference = handle;
            _pendingReader = r;
            return TypeResolver.GetTypeName(
                r,
                handle,
                context: null,
                beforeMaterialize: ObserveBeforeMaterialize) ?? "object";
        }

        public string GetTypeFromSerializedName(string name)
        {
            // Record that the most recent name came from the blob so a later
            // handle-derived occurrence of the same spelling is not resolved as
            // reflection syntax.
            _lastNameFromBlob = true;
            _pendingDefinition = default;
            _pendingReference = default;
            _pendingReader = null;
            return _preserveSerializedTypeNames
                ? name
                : EnumUnderlyingPrimitive.WithoutAssemblyQualification(name);
        }

        public PrimitiveTypeCode GetUnderlyingEnumType(string type)
        {
            bool fromBlob = _lastNameFromBlob;
            TypeDefinitionHandle pending = _pendingDefinition;
            TypeReferenceHandle pendingReference = _pendingReference;
            MetadataReader? pendingReader = _pendingReader;
            _lastNameFromBlob = false;
            _pendingDefinition = default;
            _pendingReference = default;
            _pendingReader = null;
            LastResolutionDefaulted = false;

            if (pendingReader is not null)
            {
                if (!pending.IsNil)
                    return EnumUnderlyingPrimitive.FromDefinition(pendingReader, pending);
                if (!pendingReference.IsNil
                    && EnumUnderlyingPrimitive.TryResolveDefinition(
                        pendingReader,
                        pendingReference,
                        out TypeDefinitionHandle referenced))
                {
                    return EnumUnderlyingPrimitive.FromDefinition(pendingReader, referenced);
                }
            }

            if (!fromBlob && TypeDefinitionsByName.TryGetValue(type, out var exact))
                return EnumUnderlyingPrimitive.FromDefinition(_reader, exact);

            string normalized = EnumUnderlyingPrimitive.NormalizeSerializedName(type);
            if (TypeDefinitionsByName.TryGetValue(normalized, out var handle))
                return EnumUnderlyingPrimitive.FromDefinition(_reader, handle);

            if (_enumUnderlyingType is not null
                && Invoke(_enumUnderlyingType, normalized, out PrimitiveTypeCode width))
            {
                return EnumUnderlyingPrimitive.Normalize(width);
            }

            LastResolutionDefaulted = true;
            return PrimitiveTypeCode.Int32;
        }

        static bool Invoke(
            AttributeDecoder.EnumWidthResolver resolver,
            string name,
            out PrimitiveTypeCode width)
        {
            try
            {
                return resolver(name, out width);
            }
            catch (Exception ex)
            {
                throw new AttributeDecoder.CallerCallbackException(
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex));
            }
        }

        Dictionary<string, TypeDefinitionHandle> TypeDefinitionsByName =>
            _materializationContext?.GetOrCreateTypeDefinitionsByName(
                BuildTypeDefinitionIndex)
            ?? (_typeDefinitionsByName ??= BuildTypeDefinitionIndex());

        Dictionary<string, TypeDefinitionHandle> BuildTypeDefinitionIndex()
        {
            ObserveBeforeMaterialize(_reader.TypeDefinitions.Count);
            var result = new Dictionary<string, TypeDefinitionHandle>(
                _reader.TypeDefinitions.Count,
                StringComparer.Ordinal);
            foreach (var handle in _reader.TypeDefinitions)
            {
                string name;
                try
                {
                    name = TypeResolver.GetTypeNameFromDefinition(
                        _reader,
                        handle,
                        ObserveBeforeMaterialize);
                }
                catch (Exception ex) when (
                    ex is BadImageFormatException or ArgumentOutOfRangeException)
                {
                    throw new AttributeDecoder.TypeDefinitionIndexException(
                        MetadataTypeNameFailure.Malformed(handle, ex.Message));
                }

                result.TryAdd(name, handle);
            }

            return result;
        }

        void ObserveBeforeMaterialize(int amount)
        {
            if (_beforeMaterialize is null || amount <= 0)
                return;
            try
            {
                _beforeMaterialize(amount);
            }
            catch (Exception ex)
            {
                throw new AttributeDecoder.CallerCallbackException(
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex));
            }
        }
    }

    /// <summary>
    /// Skips one type the way SRM's <c>CustomAttributeDecoder.SkipType</c> does
    /// while walking earlier generic-attribute arguments. Iterative on an
    /// explicit stack so a hostile TypeSpec cannot overflow the native stack.
    /// A CLASS/VALUETYPE token's TypeDefOrRef coded index is treated as another
    /// type code, matching SRM.
    /// </summary>
    static class SrmType
    {
        public static bool TrySkip(ref BlobReader signature)
        {
            var work = new Stack<Item>();
            work.Push(Item.Type(1));
            while (work.Count > 0)
            {
                Item item = work.Pop();
                switch (item.Op)
                {
                    case Op.Types:
                        if (item.Remaining <= 0)
                            continue;
                        if (item.Remaining > 1)
                            work.Push(Item.Types(item.Remaining - 1, item.Depth));
                        work.Push(Item.Type(item.Depth));
                        continue;
                    case Op.ArrayShape:
                        if (!TrySkipArrayShape(ref signature))
                            return false;
                        continue;
                    case Op.GenericInstArgs:
                        try
                        {
                            int arguments = signature.ReadCompressedInteger();
                            if (arguments < 0 || arguments > signature.RemainingBytes)
                                return false;
                            if (arguments > 0)
                                work.Push(Item.Types(arguments, item.Depth));
                            continue;
                        }
                        catch (Exception ex) when (
                            ex is BadImageFormatException or ArgumentOutOfRangeException)
                        {
                            return false;
                        }
                }

                if (!TrySkipOne(ref signature, item.Depth, work))
                    return false;
            }

            return true;
        }

        static bool TrySkipOne(ref BlobReader signature, int depth, Stack<Item> work)
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
                        work.Push(Item.Type(depth + 1));
                        return true;
                    case ElementTypeFnPtr:
                        return TrySeedFnPtr(ref signature, depth + 1, work);
                    case ElementTypeArray:
                        work.Push(Item.ArrayShape());
                        work.Push(Item.Type(depth + 1));
                        return true;
                    case ElementTypeCmodReqd:
                    case ElementTypeCmodOpt:
                        signature.ReadTypeHandle();
                        work.Push(Item.Type(depth + 1));
                        return true;
                    case ElementTypeGenericInst:
                        work.Push(Item.GenericInstArgs(depth + 1));
                        work.Push(Item.Type(depth + 1));
                        return true;
                    case ElementTypeVar:
                        signature.ReadCompressedInteger();
                        return true;
                    case ElementTypeClass:
                    case ElementTypeValueType:
                        work.Push(Item.Type(depth + 1));
                        return true;
                    default:
                        return false;
                }
            }
            catch (Exception ex) when (
                ex is BadImageFormatException or ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        static bool TrySkipArrayShape(ref BlobReader signature)
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
            catch (Exception ex) when (
                ex is BadImageFormatException or ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        static bool TrySeedFnPtr(ref BlobReader signature, int depth, Stack<Item> work)
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
            work.Push(Item.Types(parameterCount + 1, depth));
            return true;
        }

        enum Op : byte
        {
            Type,
            Types,
            ArrayShape,
            GenericInstArgs,
        }

        readonly struct Item
        {
            public Op Op { get; }
            public int Depth { get; }
            public int Remaining { get; }

            Item(Op op, int depth, int remaining)
            {
                Op = op;
                Depth = depth;
                Remaining = remaining;
            }

            public static Item Type(int depth) => new(Op.Type, depth, 0);
            public static Item Types(int remaining, int depth)
                => new(Op.Types, depth, remaining);
            public static Item ArrayShape() => new(Op.ArrayShape, 0, 0);
            public static Item GenericInstArgs(int depth)
                => new(Op.GenericInstArgs, depth, 0);
        }
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
    const byte ElementTypeFnPtr = 0x1b;
    const byte ElementTypeObject = 0x1c;
    const byte ElementTypeSzArray = 0x1d;
    const byte ElementTypeCmodReqd = 0x1f;
    const byte ElementTypeCmodOpt = 0x20;
    const byte ElementTypePinned = 0x45;
}
