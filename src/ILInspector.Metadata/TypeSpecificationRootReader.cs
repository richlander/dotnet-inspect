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

internal abstract record TypeSpecificationRootReadResult
{
    private protected TypeSpecificationRootReadResult()
    {
    }

    internal sealed record Read(TypeSpecificationRoot Root)
        : TypeSpecificationRootReadResult;

    internal sealed record BudgetExceeded(
        int Budget,
        EntityHandle Subject,
        string Detail)
        : TypeSpecificationRootReadResult;

    internal sealed record Malformed(
        EntityHandle Subject,
        string Detail)
        : TypeSpecificationRootReadResult;

    internal sealed record Cycle(
        EntityHandle Subject,
        string Detail)
        : TypeSpecificationRootReadResult;

    internal sealed record Unsupported(
        EntityHandle Subject,
        string Detail)
        : TypeSpecificationRootReadResult;
}

internal readonly record struct TypeSpecificationRoot(
    TypeSpecificationRootKind Kind,
    EntityHandle Type,
    byte RawTypeKind,
    bool IsGenericInstantiation,
    int GenericArgumentCount,
    int GenericParameterIndex)
{
    internal const int MaxAuthenticationSignatureDepth = 64;

    internal static bool TryRead(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        out TypeSpecificationRoot root)
    {
        TypeSpecificationRootReadResult result = Read(reader, handle);
        if (result is TypeSpecificationRootReadResult.Read read)
        {
            root = read.Root;
            return true;
        }

        root = default;
        return false;
    }

    internal static TypeSpecificationRootReadResult Read(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        Action<TypeSpecificationHandle, int>? beforeDecodeBytes = null,
        Action<TypeSpecificationHandle>? beforeDependencyEdge = null,
        Action<TypeSpecificationHandle>? graphValidated = null)
    {
        TypeSpecificationRootReadResult? validationFailure =
            ValidateGraph(
                reader,
                handle,
                beforeDecodeBytes,
                beforeDependencyEdge,
                graphValidated);
        if (validationFailure is not null)
            return validationFailure;

        try
        {
            BlobHandle signature =
                reader.GetTypeSpecification(handle).Signature;
            BlobReader blob = reader.GetBlobReader(signature);
            beforeDecodeBytes?.Invoke(handle, blob.Length);
            byte code = blob.ReadByte();
            bool isGenericInstantiation = code == 0x15; // GENERICINST
            if (isGenericInstantiation)
                code = blob.ReadByte();

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
                    return new TypeSpecificationRootReadResult.Malformed(
                        handle,
                        "The TypeSpec root has an invalid named-type encoding.");
                }

                return new TypeSpecificationRootReadResult.Read(
                    new TypeSpecificationRoot(
                        TypeSpecificationRootKind.NamedType,
                        type,
                        code,
                        isGenericInstantiation,
                        genericArgumentCount,
                        GenericParameterIndex: -1));
            }

            if (code is 0x13 or 0x1e) // VAR or MVAR
            {
                int index = blob.ReadCompressedInteger();
                if (index < 0
                    || blob.RemainingBytes != 0)
                {
                    return new TypeSpecificationRootReadResult.Malformed(
                        handle,
                        "The TypeSpec root has an invalid generic-parameter encoding.");
                }

                return new TypeSpecificationRootReadResult.Read(
                    new TypeSpecificationRoot(
                        code == 0x13
                            ? TypeSpecificationRootKind.GenericTypeParameter
                            : TypeSpecificationRootKind.GenericMethodParameter,
                        default,
                        RawTypeKind: 0,
                        IsGenericInstantiation: false,
                        GenericArgumentCount: 0,
                        index));
            }

            return new TypeSpecificationRootReadResult.Unsupported(
                handle,
                "The TypeSpec root is not a named class or generic parameter.");
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or IndexOutOfRangeException)
        {
            return new TypeSpecificationRootReadResult.Malformed(
                handle,
                ex.Message);
        }
    }

    internal static TypeSpecificationRootReadResult? ValidateGraph(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        Action<TypeSpecificationHandle, int>? beforeDecodeBytes = null,
        Action<TypeSpecificationHandle>? beforeDependencyEdge = null,
        Action<TypeSpecificationHandle>? graphValidated = null) =>
        TypeSpecificationShapeValidator.Validate(
            reader,
            handle,
            beforeDecodeBytes,
            beforeDependencyEdge,
            graphValidated);

    sealed class TypeSpecificationShapeValidator :
        ISignatureTypeProvider<byte, Stack<TypeSpecificationHandle>>
    {
        // SRM decodes one blob recursively. This leaves ample margin on the
        // 128-KiB managed stack used by the bounded-stack regression gate.
        internal static TypeSpecificationShapeValidator Instance { get; } =
            new();

        internal static TypeSpecificationRootReadResult? Validate(
            MetadataReader reader,
            TypeSpecificationHandle root,
            Action<TypeSpecificationHandle, int>? beforeDecodeBytes,
            Action<TypeSpecificationHandle>? beforeDependencyEdge,
            Action<TypeSpecificationHandle>? graphValidated)
        {
            var pending = new Stack<ValidationFrame>();
            var states =
                new Dictionary<
                    TypeSpecificationHandle,
                    ValidationState>();
            int cumulativeBytes = 0;
            EntityHandle activeSubject = root;
            pending.Push(new ValidationFrame(root, IsExit: false));
            try
            {
                while (pending.TryPop(out ValidationFrame frame))
                {
                    activeSubject = frame.Handle;
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
                        {
                            return new TypeSpecificationRootReadResult.Cycle(
                                frame.Handle,
                                "The TypeSpec dependency graph contains a cycle.");
                        }
                        continue;
                    }

                    BlobHandle signature =
                        reader.GetTypeSpecification(frame.Handle)
                            .Signature;
                    int blobLength =
                        reader.GetBlobReader(signature).Length;
                    if (states.Count >= TypeSpecGuard.MaxDepth
                        )
                    {
                        return new TypeSpecificationRootReadResult.BudgetExceeded(
                            TypeSpecGuard.MaxDepth,
                            frame.Handle,
                            "The TypeSpec dependency graph exceeded its "
                                + "handle-count budget.");
                    }
                    if (blobLength
                        > TypeSpecGuard.MaxCumulativeBytes
                            - cumulativeBytes)
                    {
                        return new TypeSpecificationRootReadResult.BudgetExceeded(
                            TypeSpecGuard.MaxCumulativeBytes,
                            frame.Handle,
                            "The TypeSpec dependency graph exceeded its "
                                + "cumulative-byte budget.");
                    }

                    states.Add(
                        frame.Handle,
                        ValidationState.Active);
                    beforeDecodeBytes?.Invoke(
                        frame.Handle,
                        blobLength);
                    cumulativeBytes += blobLength;
                    SignatureBlobGuard.CompleteValidationKind validation =
                        SignatureBlobGuard.ValidateComplete(
                            reader,
                            signature,
                            SignatureBlobGuard.Kind.TypeSpecification,
                            MaxAuthenticationSignatureDepth);
                    if (validation
                        == SignatureBlobGuard.CompleteValidationKind
                            .DepthBudgetExceeded)
                    {
                        return new TypeSpecificationRootReadResult.BudgetExceeded(
                            MaxAuthenticationSignatureDepth,
                            frame.Handle,
                            "The TypeSpec signature exceeded its structural "
                                + "depth budget.");
                    }
                    if (validation
                        == SignatureBlobGuard.CompleteValidationKind.Malformed)
                    {
                        return new TypeSpecificationRootReadResult.Malformed(
                            frame.Handle,
                            "The TypeSpec signature is incomplete or has "
                                + "trailing data.");
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
                        beforeDependencyEdge?.Invoke(dependency);
                        pending.Push(
                            new ValidationFrame(
                                dependency,
                                IsExit: false));
                    }
                }

                if (graphValidated is not null)
                {
                    foreach (TypeSpecificationHandle handle in states.Keys)
                        graphValidated(handle);
                }
                return null;
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException
                    or IndexOutOfRangeException)
            {
                return new TypeSpecificationRootReadResult.Malformed(
                    activeSubject,
                    ex.Message);
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
