using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

internal enum TypeSpecificationRootKind
{
    NamedType,
    GenericTypeParameter,
    GenericMethodParameter,
}

internal readonly record struct TypeSpecificationRoot(
    TypeSpecificationRootKind Kind,
    EntityHandle Type,
    byte RawTypeKind,
    int GenericParameterIndex)
{
    internal static bool TryRead(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        out TypeSpecificationRoot root)
    {
        root = default;
        try
        {
            BlobHandle signature =
                reader.GetTypeSpecification(handle).Signature;
            BlobReader blob = reader.GetBlobReader(signature);
            byte code = blob.ReadByte();
            bool isGenericInstantiation = code == 0x15; // GENERICINST
            if (isGenericInstantiation)
            {
                if (!TypeSpecificationShapeValidator.IsWellFormed(
                        reader,
                        handle))
                {
                    return false;
                }

                code = blob.ReadByte();
            }

            if (code is 0x11 or 0x12) // VALUETYPE or CLASS
            {
                int encoded = blob.ReadCompressedInteger();
                if (!TryDecodeTypeDefOrRef(
                        encoded,
                        out EntityHandle type)
                    || (!isGenericInstantiation
                        && blob.RemainingBytes != 0))
                {
                    return false;
                }

                root = new TypeSpecificationRoot(
                    TypeSpecificationRootKind.NamedType,
                    type,
                    code,
                    GenericParameterIndex: -1);
                return true;
            }

            if (code is 0x13 or 0x1e) // VAR or MVAR
            {
                int index = blob.ReadCompressedInteger();
                if (index < 0
                    || blob.RemainingBytes != 0)
                    return false;

                root = new TypeSpecificationRoot(
                    code == 0x13
                        ? TypeSpecificationRootKind.GenericTypeParameter
                        : TypeSpecificationRootKind.GenericMethodParameter,
                    default,
                    RawTypeKind: 0,
                    index);
                return true;
            }

            return false;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    sealed class TypeSpecificationShapeValidator :
        ISignatureTypeProvider<byte, Stack<TypeSpecificationHandle>>
    {
        internal static TypeSpecificationShapeValidator Instance { get; } =
            new();

        internal static bool IsWellFormed(
            MetadataReader reader,
            TypeSpecificationHandle root)
        {
            var pending = new Stack<TypeSpecificationHandle>();
            var visited = new HashSet<TypeSpecificationHandle>();
            pending.Push(root);
            try
            {
                while (pending.TryPop(out TypeSpecificationHandle handle))
                {
                    if (!visited.Add(handle))
                        continue;

                    BlobHandle signature =
                        reader.GetTypeSpecification(handle).Signature;
                    if (!SignatureBlobGuard.IsSafeToDecode(
                            reader,
                            signature,
                            SignatureBlobGuard.Kind.TypeSpecification))
                    {
                        return false;
                    }

                    GuardedProviderDecode.TypeSpec(
                        reader,
                        handle,
                        Instance,
                        pending,
                        fallback: (byte)0);
                }

                return true;
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        public byte GetArrayType(byte elementType, ArrayShape shape) => 0;

        public byte GetByReferenceType(byte elementType) => 0;

        public byte GetFunctionPointerType(MethodSignature<byte> signature) => 0;

        public byte GetGenericInstantiation(
            byte genericType,
            ImmutableArray<byte> typeArguments) => 0;

        public byte GetGenericMethodParameter(
            Stack<TypeSpecificationHandle> context,
            int index) => 0;

        public byte GetGenericTypeParameter(
            Stack<TypeSpecificationHandle> context,
            int index) => 0;

        public byte GetModifiedType(
            byte modifier,
            byte unmodifiedType,
            bool isRequired) => 0;

        public byte GetPinnedType(byte elementType) => 0;

        public byte GetPointerType(byte elementType) => 0;

        public byte GetPrimitiveType(PrimitiveTypeCode typeCode) => 0;

        public byte GetSZArrayType(byte elementType) => 0;

        public byte GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind) => 0;

        public byte GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind) => 0;

        public byte GetTypeFromSpecification(
            MetadataReader reader,
            Stack<TypeSpecificationHandle> context,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            context.Push(handle);
            return 0;
        }
    }

    static bool TryDecodeTypeDefOrRef(
        int encoded,
        out EntityHandle handle)
    {
        int row = encoded >> 2;
        if (row <= 0)
        {
            handle = default;
            return false;
        }

        handle = (encoded & 3) switch
        {
            0 => MetadataTokens.TypeDefinitionHandle(row),
            1 => MetadataTokens.TypeReferenceHandle(row),
            _ => default,
        };
        return !handle.IsNil;
    }
}
