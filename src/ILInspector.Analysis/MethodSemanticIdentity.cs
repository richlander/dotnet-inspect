using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// The detached semantic identity of one MethodDef: every structural field of
/// <see cref="MethodIdentity"/> except its caller-unsafe contract, which the
/// memory-safety contract owner supplies separately.
/// </summary>
/// <remarks>
/// Owned by <c>docs/design/method-query-source.md#semantic-identity</c>.
/// It is decoded metadata, not display text, and is never a join key by
/// spelling.
/// </remarks>
public sealed class MethodSemanticIdentity
{
    internal MethodSemanticIdentity(
        string assemblyName,
        Guid moduleVersionId,
        TypeRef declaringType,
        string name,
        ImmutableArray<TypeRef> parameterTypes,
        TypeRef returnType,
        int metadataToken,
        bool isStatic,
        bool isExtension,
        int genericArity,
        ImmutableArray<string> genericParameterNames,
        byte signatureHeader,
        int requiredParameterCount,
        bool hasInvalidGenericParameterDeclaration,
        bool isVirtualDispatchOpen)
    {
        AssemblyName = assemblyName;
        ModuleVersionId = moduleVersionId;
        DeclaringType = declaringType;
        Name = name;
        ParameterTypes = parameterTypes;
        ReturnType = returnType;
        MetadataToken = metadataToken;
        IsStatic = isStatic;
        IsExtension = isExtension;
        GenericArity = genericArity;
        GenericParameterNames = genericParameterNames;
        SignatureHeader = signatureHeader;
        RequiredParameterCount = requiredParameterCount;
        HasInvalidGenericParameterDeclaration =
            hasInvalidGenericParameterDeclaration;
        IsVirtualDispatchOpen = isVirtualDispatchOpen;
    }

    public string AssemblyName { get; }

    public Guid ModuleVersionId { get; }

    public TypeRef DeclaringType { get; }

    public string Name { get; }

    public ImmutableArray<TypeRef> ParameterTypes { get; }

    public TypeRef ReturnType { get; }

    public int MetadataToken { get; }

    public bool IsStatic { get; }

    public bool IsExtension { get; }

    public int GenericArity { get; }

    public ImmutableArray<string> GenericParameterNames { get; }

    /// <summary>The raw signature header byte, or 0 when the signature was not decoded.</summary>
    public byte SignatureHeader { get; }

    /// <summary>The vararg required-parameter count, or -1 when the signature was not decoded.</summary>
    public int RequiredParameterCount { get; }

    public bool HasInvalidGenericParameterDeclaration { get; }

    public bool IsVirtualDispatchOpen { get; }

    /// <summary>
    /// Composes this identity with the separately owned caller-unsafe
    /// contract into the current <see cref="MethodIdentity"/>.
    /// </summary>
    public MethodIdentity ToMethodIdentity(
        CallerUnsafeMode callerUnsafeMode) =>
        new(
            AssemblyName,
            ModuleVersionId,
            DeclaringType,
            Name,
            ParameterTypes,
            ReturnType,
            MetadataToken,
            IsStatic,
            IsExtension,
            callerUnsafeMode,
            GenericArity,
            GenericParameterNames)
        {
            SignatureHeader = SignatureHeader,
            RequiredParameterCount = RequiredParameterCount,
            HasInvalidGenericParameterDeclaration =
                HasInvalidGenericParameterDeclaration,
            IsVirtualDispatchOpen = IsVirtualDispatchOpen,
        };
}

/// <summary>The outcome of decoding one MethodDef's semantic identity.</summary>
public abstract record MethodSemanticIdentityResult
{
    MethodSemanticIdentityResult()
    {
    }

    /// <summary>The identity, decoded completely.</summary>
    public sealed record Decoded(MethodSemanticIdentity Identity)
        : MethodSemanticIdentityResult;

    /// <summary>
    /// The method signature exceeds the safe decoding limits. The identity
    /// carries no parameter types, an unsupported return type, and a zero
    /// signature header, as the current <see cref="MethodIdentity"/> does.
    /// </summary>
    public sealed record SignatureBoundExceeded(
        MethodSemanticIdentity Identity)
        : MethodSemanticIdentityResult;

    /// <summary>The metadata could not be read; no identity is published.</summary>
    public sealed record Malformed(string Reason)
        : MethodSemanticIdentityResult;
}

/// <summary>
/// The metadata work one or more semantic-identity decodes performed.
/// </summary>
/// <param name="SignatureBytes">Method signature blob bytes, each counted
/// once per decode, which the depth guard reads before any decode.</param>
/// <param name="TypeDecodeWork">The type decoder's structural work for the
/// declaring type and every signature type: one unit per type node, plus the
/// bytes of every name, assembly-reference, and type-specification blob it
/// reads.</param>
/// <param name="GenericParameterRows">GenericParam rows read for the generic
/// scope and the generic-declaration check.</param>
/// <param name="CustomAttributeRows">CustomAttribute rows read by the
/// extension-method check.</param>
/// <param name="NameBytes">Bytes of the method's name and of its own and its
/// declaring type's generic parameter names.</param>
public readonly record struct MethodSemanticIdentityWork(
    long SignatureBytes,
    long TypeDecodeWork,
    int GenericParameterRows,
    int CustomAttributeRows,
    long NameBytes)
{
    public static MethodSemanticIdentityWork Empty { get; } = default;

    public MethodSemanticIdentityWork Plus(
        MethodSemanticIdentityWork other) =>
        new(
            checked(SignatureBytes + other.SignatureBytes),
            checked(TypeDecodeWork + other.TypeDecodeWork),
            checked(GenericParameterRows + other.GenericParameterRows),
            checked(CustomAttributeRows + other.CustomAttributeRows),
            checked(NameBytes + other.NameBytes));
}

/// <summary>
/// The single owner of MethodDef semantic-identity decoding, shared by Method
/// Query Source and Library Body Analysis so the two cannot diverge.
/// </summary>
internal static class MethodSemanticIdentityReader
{
    /// <summary>Counts the work of one decode as it is performed.</summary>
    internal sealed class WorkCounter
    {
        long _signatureBytes;
        long _typeDecodeWork;
        int _genericParameterRows;
        int _customAttributeRows;
        long _nameBytes;

        public MethodSemanticIdentityWork Work =>
            new(
                _signatureBytes,
                _typeDecodeWork,
                _genericParameterRows,
                _customAttributeRows,
                _nameBytes);

        internal void Signature(int bytes) =>
            _signatureBytes = checked(_signatureBytes + bytes);

        internal void TypeDecode(int work) =>
            _typeDecodeWork = checked(_typeDecodeWork + work);

        internal void GenericParameterRow() =>
            _genericParameterRows = checked(_genericParameterRows + 1);

        internal void CustomAttributeRow() =>
            _customAttributeRows = checked(_customAttributeRows + 1);

        internal void Name(int bytes) =>
            _nameBytes = checked(_nameBytes + bytes);
    }

    /// <summary>
    /// The generic scope of a method: its declaring type's and its own
    /// generic parameter names.
    /// </summary>
    internal static GenericScope CreateScope(
        MetadataReader reader,
        TypeDefinition type,
        MethodDefinition method,
        WorkCounter? work = null) =>
        new(
            GenericParameterNames(reader, type.GetGenericParameters(), work),
            GenericParameterNames(reader, method.GetGenericParameters(), work));

    /// <summary>
    /// Decodes one MethodDef's semantic identity in <paramref name="scope"/>,
    /// which must be <see cref="CreateScope"/> of the same method. Unreadable
    /// metadata throws.
    /// </summary>
    internal static MethodSemanticIdentityResult Read(
        MetadataReader reader,
        string assemblyName,
        Guid moduleVersionId,
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle methodHandle,
        MethodDefinition method,
        GenericScope scope,
        WorkCounter? work = null)
    {
        MethodSemanticIdentity identity = ReadIdentity(
            reader,
            assemblyName,
            moduleVersionId,
            typeHandle,
            methodHandle,
            method,
            scope,
            work,
            out bool signatureDecoded);
        return signatureDecoded
            ? new MethodSemanticIdentityResult.Decoded(identity)
            : new MethodSemanticIdentityResult.SignatureBoundExceeded(
                identity);
    }

    /// <summary>
    /// <see cref="Read"/> without the outcome wrapper, for a caller that
    /// treats both published outcomes alike.
    /// </summary>
    internal static MethodSemanticIdentity ReadIdentity(
        MetadataReader reader,
        string assemblyName,
        Guid moduleVersionId,
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle methodHandle,
        MethodDefinition method,
        GenericScope scope,
        WorkCounter? work,
        out bool signatureDecoded)
    {
        TypeRefDecoder decoder = work is null
            ? TypeRefDecoder.Instance
            : TypeRefDecoder.Observing(work.TypeDecode);
        GenericParameterHandleCollection genericParameters =
            method.GetGenericParameters();
        work?.Signature(reader.GetBlobReader(method.Signature).Length);

        ImmutableArray<TypeRef> parameterTypes;
        TypeRef returnType;
        byte signatureHeader;
        int requiredParameterCount;
        bool hasInvalidGenericParameterDeclaration;
        signatureDecoded = SignatureBlobGuard.IsSafeToDecode(
            reader,
            method.Signature,
            SignatureBlobGuard.Kind.Method);
        if (signatureDecoded)
        {
            MethodSignature<TypeRef> signature =
                method.DecodeSignature(decoder, scope);
            parameterTypes = signature.ParameterTypes;
            returnType = signature.ReturnType;
            signatureHeader = signature.Header.RawValue;
            requiredParameterCount = signature.RequiredParameterCount;
            hasInvalidGenericParameterDeclaration =
                !HasExactGenericParameters(
                    reader,
                    genericParameters,
                    signature.GenericParameterCount,
                    work);
        }
        else
        {
            parameterTypes = [];
            returnType = TypeRef.Unsupported(
                "method signature nesting depth exceeded");
            signatureHeader = 0;
            requiredParameterCount = -1;
            hasInvalidGenericParameterDeclaration =
                !HasExactGenericParameterDeclaration(reader, method, work);
        }

        TypeDefinition type = reader.GetTypeDefinition(typeHandle);
        TypeRef declaringType =
            decoder.GetTypeFromDefinition(reader, typeHandle, 0);
        work?.Name(reader.GetBlobReader(method.Name).Length);
        return new MethodSemanticIdentity(
            assemblyName,
            moduleVersionId,
            declaringType,
            reader.GetString(method.Name),
            parameterTypes,
            returnType,
            MetadataTokens.GetToken(methodHandle),
            (method.Attributes & MethodAttributes.Static) != 0,
            IsExtensionMethod(reader, type, method, work),
            genericParameters.Count,
            scope.MethodParameters,
            signatureHeader,
            requiredParameterCount,
            hasInvalidGenericParameterDeclaration,
            DispatchCanTargetOverride(type, method));
    }

    internal static bool DispatchCanTargetOverride(
        TypeDefinition declaringType,
        MethodDefinition method) =>
        (method.Attributes & MethodAttributes.Virtual) != 0
        && (method.Attributes & MethodAttributes.Final) == 0
        && (declaringType.Attributes & TypeAttributes.Sealed) == 0;

    static ImmutableArray<string> GenericParameterNames(
        MetadataReader reader,
        GenericParameterHandleCollection handles,
        WorkCounter? work)
    {
        if (handles.Count == 0)
            return [];
        var names = ImmutableArray.CreateBuilder<string>(handles.Count);
        foreach (GenericParameterHandle handle in handles)
        {
            work?.GenericParameterRow();
            StringHandle name = reader.GetGenericParameter(handle).Name;
            work?.Name(reader.GetBlobReader(name).Length);
            names.Add(reader.GetString(name));
        }
        return names.MoveToImmutable();
    }

    static bool HasExactGenericParameters(
        MetadataReader reader,
        GenericParameterHandleCollection parameters,
        int signatureCount,
        WorkCounter? work) =>
        work is null
            ? MemberResolver.HasExactGenericParameters(
                reader,
                parameters,
                signatureCount)
            : MemberResolver.HasExactGenericParameters(
                reader,
                parameters,
                signatureCount,
                work.GenericParameterRow);

    static bool HasExactGenericParameterDeclaration(
        MetadataReader reader,
        MethodDefinition method,
        WorkCounter? work)
    {
        try
        {
            BlobReader signature = reader.GetBlobReader(method.Signature);
            SignatureHeader header = signature.ReadSignatureHeader();
            if (header.Kind != SignatureKind.Method)
                return false;

            int signatureGenericParameterCount =
                header.IsGeneric
                    ? signature.ReadCompressedInteger()
                    : 0;
            return signatureGenericParameterCount >= 0
                && HasExactGenericParameters(
                    reader,
                    method.GetGenericParameters(),
                    signatureGenericParameterCount,
                    work);
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or OverflowException)
        {
            return false;
        }
    }

    static bool IsExtensionMethod(
        MetadataReader reader,
        TypeDefinition type,
        MethodDefinition method,
        WorkCounter? work)
    {
        if ((type.Attributes & TypeAttributes.Abstract) == 0
            || (type.Attributes & TypeAttributes.Sealed) == 0
            || (method.Attributes & MethodAttributes.Static) == 0)
        {
            return false;
        }

        return HasExtensionAttribute(
                reader,
                type.GetCustomAttributes(),
                work)
            && HasExtensionAttribute(
                reader,
                method.GetCustomAttributes(),
                work);
    }

    static bool HasExtensionAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        WorkCounter? work)
    {
        if (work is null)
            return AttributeReader.HasExtensionAttribute(reader, attributes);

        foreach (CustomAttributeHandle handle in attributes)
        {
            work.CustomAttributeRow();
            if (AttributeReader.IsExtensionAttribute(
                    reader,
                    reader.GetCustomAttribute(handle)))
            {
                return true;
            }
        }
        return false;
    }
}
