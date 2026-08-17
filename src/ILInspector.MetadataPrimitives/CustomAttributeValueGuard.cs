using System.Reflection;
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
/// This guard walks the constructor MethodSig and value blob, refusing decode
/// when a declared count exceeds the remaining bytes or when boxed / SZArray
/// nesting exceeds <see cref="MaxSerializedDepth"/>. Declared slots are charged
/// through <c>beforeMaterialize</c> so a hostile count becomes typed truncation
/// rather than a swallowed <c>OutOfMemoryException</c>. Nesting is bounded so a
/// chain of boxed tags cannot overflow the native stack the way SRM's recursive
/// decoder would.
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
    /// serialized nesting exceeds <see cref="MaxSerializedDepth"/>.
    /// </summary>
    public static bool IsSafeToDecode(
        MetadataReader reader,
        CustomAttribute attribute,
        Action<int>? beforeMaterialize = null)
    {
        try
        {
            return Check(reader, attribute, beforeMaterialize) != Result.Unsafe;
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
        Action<int>? beforeMaterialize)
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
        if (!TrySkipSignatureType(ref signature))
            return Result.Safe;

        var value = reader.GetBlobReader(attribute.Value);
        if (value.RemainingBytes < 2)
            return Result.Safe;
        if (value.ReadUInt16() != 1)
            return Result.Safe;

        for (int index = 0; index < parameterCount; index++)
        {
            Result result = SkipFixedArg(
                reader,
                ref signature,
                ref value,
                beforeMaterialize,
                depth: 1);
            if (result != Result.Safe)
                return result;
        }

        if (value.RemainingBytes < 2)
            return Result.Safe;
        int namedCount = value.ReadUInt16();
        Charge(beforeMaterialize, namedCount);
        return namedCount > value.RemainingBytes
            ? Result.Unsafe
            : Result.Safe;
    }

    static Result SkipFixedArg(
        MetadataReader reader,
        ref BlobReader signature,
        ref BlobReader value,
        Action<int>? beforeMaterialize,
        int depth)
    {
        if (depth > MaxSerializedDepth)
            return Result.Unsafe;
        if (!TryReadElementType(ref signature, out byte code))
            return Result.Safe;
        while (code is ElementTypeCmodReqd or ElementTypeCmodOpt)
        {
            signature.ReadTypeHandle();
            if (!TryReadElementType(ref signature, out code))
                return Result.Safe;
        }

        return code switch
        {
            ElementTypeBoolean or ElementTypeI1 or ElementTypeU1
                => SkipBytes(ref value, 1),
            ElementTypeChar or ElementTypeI2 or ElementTypeU2
                => SkipBytes(ref value, 2),
            ElementTypeI4 or ElementTypeU4 or ElementTypeR4
                => SkipBytes(ref value, 4),
            ElementTypeI8 or ElementTypeU8 or ElementTypeR8
                => SkipBytes(ref value, 8),
            ElementTypeString => SkipSerString(ref value),
            ElementTypeObject => SkipBoxed(ref value, beforeMaterialize, depth),
            ElementTypeSzArray => SkipSzArray(
                reader,
                ref signature,
                ref value,
                beforeMaterialize,
                depth),
            ElementTypeClass or ElementTypeValueType => SkipNamedType(
                reader,
                signature.ReadTypeHandle(),
                ref value,
                beforeMaterialize,
                depth),
            _ => Result.Unsafe,
        };
    }

    static Result SkipSzArray(
        MetadataReader reader,
        ref BlobReader signature,
        ref BlobReader value,
        Action<int>? beforeMaterialize,
        int depth)
    {
        if (depth > MaxSerializedDepth)
            return Result.Unsafe;
        int elementStart = signature.Offset;
        if (!TrySkipSignatureType(ref signature))
            return Result.Safe;
        int elementEnd = signature.Offset;

        if (value.RemainingBytes < 4)
            return Result.Safe;
        int count = value.ReadInt32();
        if (count == -1)
            return Result.Safe;
        if (count < 0)
            return Result.Unsafe;
        Charge(beforeMaterialize, count);
        if ((uint)count > (uint)value.RemainingBytes)
            return Result.Unsafe;

        for (int index = 0; index < count; index++)
        {
            signature.Offset = elementStart;
            Result result = SkipFixedArg(
                reader,
                ref signature,
                ref value,
                beforeMaterialize,
                depth + 1);
            if (result != Result.Safe)
            {
                signature.Offset = elementEnd;
                return result;
            }
        }

        signature.Offset = elementEnd;
        return Result.Safe;
    }

    static Result SkipNamedType(
        MetadataReader reader,
        EntityHandle handle,
        ref BlobReader value,
        Action<int>? beforeMaterialize,
        int depth)
    {
        if (IsSystemNamedType(reader, handle, "String")
            || IsSystemNamedType(reader, handle, "Type"))
            return SkipSerString(ref value);
        if (IsSystemNamedType(reader, handle, "Object"))
            return SkipBoxed(ref value, beforeMaterialize, depth);
        return SkipBytes(ref value, EnumUnderlyingSize(reader, handle));
    }

    static Result SkipBoxed(
        ref BlobReader value,
        Action<int>? beforeMaterialize,
        int depth)
    {
        if (depth > MaxSerializedDepth)
            return Result.Unsafe;
        if (!TryReadElementType(ref value, out byte code))
            return Result.Safe;
        return SkipSerialized(code, ref value, beforeMaterialize, depth);
    }

    static Result SkipSerialized(
        byte code,
        ref BlobReader value,
        Action<int>? beforeMaterialize,
        int depth)
    {
        if (depth > MaxSerializedDepth)
            return Result.Unsafe;
        switch (code)
        {
            case ElementTypeBoolean:
            case ElementTypeI1:
            case ElementTypeU1:
                return SkipBytes(ref value, 1);
            case ElementTypeChar:
            case ElementTypeI2:
            case ElementTypeU2:
                return SkipBytes(ref value, 2);
            case ElementTypeI4:
            case ElementTypeU4:
            case ElementTypeR4:
                return SkipBytes(ref value, 4);
            case ElementTypeI8:
            case ElementTypeU8:
            case ElementTypeR8:
                return SkipBytes(ref value, 8);
            case ElementTypeString:
            case SerializedType:
                return SkipSerString(ref value);
            case SerializedBoxed:
                return SkipBoxed(ref value, beforeMaterialize, depth + 1);
            case SerializedEnum:
            {
                Result name = SkipSerString(ref value);
                return name != Result.Safe
                    ? name
                    : SkipBytes(ref value, 4);
            }
            case ElementTypeSzArray:
            {
                if (!TryReadElementType(ref value, out byte element))
                    return Result.Safe;
                if (value.RemainingBytes < 4)
                    return Result.Safe;
                int count = value.ReadInt32();
                if (count == -1)
                    return Result.Safe;
                if (count < 0)
                    return Result.Unsafe;
                Charge(beforeMaterialize, count);
                if ((uint)count > (uint)value.RemainingBytes)
                    return Result.Unsafe;
                for (int index = 0; index < count; index++)
                {
                    Result result = SkipSerialized(
                        element,
                        ref value,
                        beforeMaterialize,
                        depth + 1);
                    if (result != Result.Safe)
                        return result;
                }

                return Result.Safe;
            }
            default:
                return Result.Unsafe;
        }
    }

    static Result SkipSerString(ref BlobReader blob)
    {
        if (blob.RemainingBytes < 1)
            return Result.Safe;
        int offset = blob.Offset;
        if (blob.ReadByte() == 0xFF)
            return Result.Safe;
        blob.Offset = offset;
        int length = blob.ReadCompressedInteger();
        return SkipBytes(ref blob, length);
    }

    static Result SkipBytes(ref BlobReader blob, int count)
    {
        if (count < 0)
            return Result.Unsafe;
        if (blob.RemainingBytes < count)
            return Result.Safe;
        blob.Offset += count;
        return Result.Safe;
    }

    static bool TrySkipSignatureType(ref BlobReader signature)
    {
        if (!TryReadElementType(ref signature, out byte code))
            return false;
        switch (code)
        {
            case ElementTypeCmodReqd:
            case ElementTypeCmodOpt:
                signature.ReadTypeHandle();
                return TrySkipSignatureType(ref signature);
            case ElementTypeByRef:
            case ElementTypePtr:
            case ElementTypeSzArray:
            case ElementTypePinned:
                return TrySkipSignatureType(ref signature);
            case ElementTypeClass:
            case ElementTypeValueType:
                signature.ReadTypeHandle();
                return true;
            case ElementTypeGenericInst:
                if (!TryReadElementType(ref signature, out _))
                    return false;
                signature.ReadTypeHandle();
                int arguments = signature.ReadCompressedInteger();
                for (int index = 0; index < arguments; index++)
                {
                    if (!TrySkipSignatureType(ref signature))
                        return false;
                }

                return true;
            case ElementTypeArray:
                if (!TrySkipSignatureType(ref signature))
                    return false;
                signature.ReadCompressedInteger();
                int sizes = signature.ReadCompressedInteger();
                for (int index = 0; index < sizes; index++)
                    signature.ReadCompressedInteger();
                int bounds = signature.ReadCompressedInteger();
                for (int index = 0; index < bounds; index++)
                    signature.ReadCompressedSignedInteger();
                return true;
            case ElementTypeVar:
            case ElementTypeMVar:
                signature.ReadCompressedInteger();
                return true;
            case ElementTypeFnPtr:
                return false;
            default:
                return true;
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

    static bool IsSystemNamedType(
        MetadataReader reader,
        EntityHandle handle,
        string name)
    {
        StringHandle namespaceHandle;
        StringHandle nameHandle;
        switch (handle.Kind)
        {
            case HandleKind.TypeReference:
            {
                var typeRef = reader.GetTypeReference((TypeReferenceHandle)handle);
                if (typeRef.ResolutionScope.Kind == HandleKind.TypeReference)
                    return false;
                namespaceHandle = typeRef.Namespace;
                nameHandle = typeRef.Name;
                break;
            }
            case HandleKind.TypeDefinition:
            {
                var definition = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                if (!definition.GetDeclaringType().IsNil)
                    return false;
                namespaceHandle = definition.Namespace;
                nameHandle = definition.Name;
                break;
            }
            default:
                return false;
        }

        var comparer = reader.StringComparer;
        return comparer.Equals(namespaceHandle, "System")
            && comparer.Equals(nameHandle, name);
    }

    static int EnumUnderlyingSize(MetadataReader reader, EntityHandle handle)
    {
        if (handle.Kind != HandleKind.TypeDefinition)
            return 4;

        var definition = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
        if (!IsSystemNamedType(reader, definition.BaseType, "Enum"))
            return 4;

        foreach (var fieldHandle in definition.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if ((field.Attributes & FieldAttributes.Static) != 0)
                continue;
            var signature = reader.GetBlobReader(field.Signature);
            if (signature.RemainingBytes < 2)
                return 4;
            signature.ReadByte();
            while (signature.RemainingBytes > 0)
            {
                byte code = signature.ReadByte();
                if (code is ElementTypeCmodReqd or ElementTypeCmodOpt)
                {
                    signature.ReadTypeHandle();
                    continue;
                }

                int size = PrimitiveSize(code);
                return size > 0 ? size : 4;
            }
        }

        return 4;
    }

    static int PrimitiveSize(byte code) => code switch
    {
        ElementTypeBoolean or ElementTypeI1 or ElementTypeU1 => 1,
        ElementTypeChar or ElementTypeI2 or ElementTypeU2 => 2,
        ElementTypeI4 or ElementTypeU4 or ElementTypeR4 => 4,
        ElementTypeI8 or ElementTypeU8 or ElementTypeR8 => 8,
        _ => 0,
    };

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
    }

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
    const byte ElementTypeObject = 0x1c;
    const byte ElementTypeFnPtr = 0x1b;
    const byte ElementTypeSzArray = 0x1d;
    const byte ElementTypeMVar = 0x1e;
    const byte ElementTypeCmodReqd = 0x1f;
    const byte ElementTypeCmodOpt = 0x20;
    const byte ElementTypePinned = 0x45;
    const byte SerializedType = 0x50;
    const byte SerializedBoxed = 0x51;
    const byte SerializedEnum = 0x55;
}
