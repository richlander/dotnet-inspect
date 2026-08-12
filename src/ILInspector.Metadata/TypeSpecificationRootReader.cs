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
    int GenericArgumentCount,
    int GenericParameterIndex)
{
    internal const int MaxAuthenticationSignatureDepth = 64;

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
                int genericArgumentCount = isGenericInstantiation
                    ? blob.ReadCompressedInteger()
                    : 0;
                if (!TryDecodeTypeDefOrRef(
                        encoded,
                        out EntityHandle type)
                    || genericArgumentCount < 0
                    || (!isGenericInstantiation
                        && blob.RemainingBytes != 0))
                {
                    return false;
                }

                root = new TypeSpecificationRoot(
                    TypeSpecificationRootKind.NamedType,
                    type,
                    code,
                    genericArgumentCount,
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
                    GenericArgumentCount: 0,
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
        // SRM decodes one blob recursively. This leaves ample margin on the
        // 128-KiB managed stack used by the bounded-stack regression gate.
        internal static TypeSpecificationShapeValidator Instance { get; } =
            new();

        internal static bool IsWellFormed(
            MetadataReader reader,
            TypeSpecificationHandle root)
        {
            var pending = new Stack<ValidationFrame>();
            var states =
                new Dictionary<
                    TypeSpecificationHandle,
                    ValidationState>();
            int cumulativeBytes = 0;
            pending.Push(new ValidationFrame(root, IsExit: false));
            try
            {
                while (pending.TryPop(out ValidationFrame frame))
                {
                    if (frame.IsExit)
                    {
                        states[frame.Handle] =
                            ValidationState.Complete;
                        continue;
                    }

                    if (states.TryGetValue(
                            frame.Handle,
                            out ValidationState state))
                    {
                        if (state == ValidationState.Active)
                            return false;
                        continue;
                    }

                    BlobHandle signature =
                        reader.GetTypeSpecification(frame.Handle)
                            .Signature;
                    int blobLength =
                        reader.GetBlobReader(signature).Length;
                    if (states.Count >= TypeSpecGuard.MaxDepth
                        || blobLength
                            > TypeSpecGuard.MaxCumulativeBytes
                                - cumulativeBytes)
                    {
                        return false;
                    }

                    states.Add(
                        frame.Handle,
                        ValidationState.Active);
                    cumulativeBytes += blobLength;
                    if (!SignatureBlobGuard
                            .IsCompleteTypeSpecification(
                                reader.GetBlobReader(signature),
                                MaxAuthenticationSignatureDepth))
                    {
                        return false;
                    }

                    var dependencies =
                        new Stack<TypeSpecificationHandle>();
                    GuardedProviderDecode.TypeSpec(
                        reader,
                        frame.Handle,
                        Instance,
                        dependencies,
                        fallback: (byte)0);
                    pending.Push(
                        new ValidationFrame(
                            frame.Handle,
                            IsExit: true));
                    while (dependencies.TryPop(
                        out TypeSpecificationHandle dependency))
                    {
                        pending.Push(
                            new ValidationFrame(
                                dependency,
                                IsExit: false));
                    }
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

        enum ValidationState
        {
            Active,
            Complete,
        }

        readonly record struct ValidationFrame(
            TypeSpecificationHandle Handle,
            bool IsExit);

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
