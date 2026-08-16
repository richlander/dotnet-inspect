using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal sealed record GenericScope(ImmutableArray<string> TypeParameters, ImmutableArray<string> MethodParameters)
{
    public static readonly GenericScope Empty = new([], []);
}

internal sealed class TypeRefDecoder : ISignatureTypeProvider<TypeRef, GenericScope>
{
    public static readonly TypeRefDecoder Instance = new();
    static readonly ConditionalWeakTable<
        MetadataReader,
        CurrentAssemblyInfo> s_currentAssemblies = new();

    sealed record CurrentAssemblyInfo(
        string Name,
        AssemblyReferenceIdentity? Identity,
        bool TrustedFramework,
        bool AuthenticProtobuf);

    // TypeSpecification decoding can re-enter through custom modifiers. Relationship
    // chains use MetadataRelationshipTraversal instead and never consume native stack.
    [ThreadStatic]
    static int s_recursionDepth;
    const int MaxRecursionDepth = 256;

    // SRM's own SignatureDecoder.DecodeType recurses on the native stack for each nested
    // structural element (pointer/array/byref/pinned/generic-inst) within a *single* signature
    // blob, before any provider callback runs, so the depth counter above cannot catch it — a
    // long enough blob StackOverflows inside SRM. Blob length bounds that native depth (each
    // level costs >= 1 byte), so refuse over-long TypeSpecification blobs. Real single-type
    // blobs are tiny (CoreLib's largest TypeSpec is 57 bytes), so this only trips on malformed
    // input.
    const int MaxSignatureBlobLength = 1024;

    // A self-referential TypeSpecification (reached via a modreq custom modifier) re-enters
    // GetTypeFromSpecification, and each re-entry stacks another blob's worth of SRM native
    // DecodeType frames on top of the live ones. The per-blob cap alone would let a cycle
    // multiply into MaxSignatureBlobLength * MaxRecursionDepth native frames — still an
    // uncatchable StackOverflow — so also bound the CUMULATIVE decoded blob bytes across the
    // live re-entry chain, which is what actually bounds the native stack depth.
    [ThreadStatic]
    static int s_cumulativeSignatureBytes;
    const int MaxCumulativeSignatureBytes = 4096;

    public TypeRef GetPrimitiveType(PrimitiveTypeCode typeCode)
    {
        string name = typeCode switch
        {
            PrimitiveTypeCode.Boolean => "Boolean",
            PrimitiveTypeCode.Byte => "Byte",
            PrimitiveTypeCode.SByte => "SByte",
            PrimitiveTypeCode.Char => "Char",
            PrimitiveTypeCode.Int16 => "Int16",
            PrimitiveTypeCode.UInt16 => "UInt16",
            PrimitiveTypeCode.Int32 => "Int32",
            PrimitiveTypeCode.UInt32 => "UInt32",
            PrimitiveTypeCode.Int64 => "Int64",
            PrimitiveTypeCode.UInt64 => "UInt64",
            PrimitiveTypeCode.Single => "Single",
            PrimitiveTypeCode.Double => "Double",
            PrimitiveTypeCode.IntPtr => "IntPtr",
            PrimitiveTypeCode.UIntPtr => "UIntPtr",
            PrimitiveTypeCode.String => "String",
            PrimitiveTypeCode.Object => "Object",
            PrimitiveTypeCode.Void => "Void",
            PrimitiveTypeCode.TypedReference => "TypedReference",
            _ => typeCode.ToString(),
        };
        return Definition(
            TypeRef.CoreLibrary,
            "System",
            [name],
            new TypeReferenceOrigin.IntrinsicCoreLibrary());
    }

    public TypeRef GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        Span<TypeDefinitionHandle> handles =
            stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                reader,
                handle,
                handles,
                out int consumedNodes,
                out _,
                out var rejection))
        {
            return TypeRef.Unsupported(
                RelationshipFailure("type-definition declaring-type", rejection!),
                MetadataTypeNameFailure.From(rejection!));
        }

        try
        {
            var chain = handles[..consumedNodes];
            var root = reader.GetTypeDefinition(chain[0]);
            CurrentAssemblyInfo currentAssembly =
                CurrentAssembly(reader);
            string ns = reader.GetString(root.Namespace);
            return Definition(
                currentAssembly.Name,
                ns,
                TypeNameSegments(
                    reader,
                    chain,
                    static (metadata, item) =>
                        metadata.GetTypeDefinition(item).Name),
                new TypeReferenceOrigin.CurrentAssembly(
                    currentAssembly.Identity),
                currentAssembly.TrustedFramework,
                currentAssembly.AuthenticProtobuf,
                rawTypeKind);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return TypeRef.Unsupported(
                RelationshipProjectionFailure("type-definition declaring-type", handle, ex),
                RelationshipProjectionFailure(handle, ex));
        }
    }

    public TypeRef GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        Span<TypeReferenceHandle> handles =
            stackalloc TypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                reader,
                handle,
                handles,
                out int consumedNodes,
                out EntityHandle terminal,
                out var rejection))
        {
            return TypeRef.Unsupported(
                RelationshipFailure("type-reference resolution-scope", rejection!),
                MetadataTypeNameFailure.From(rejection!));
        }

        try
        {
            var chain = handles[..consumedNodes];
            var root = reader.GetTypeReference(chain[0]);
            string ns = reader.GetString(root.Namespace);
            ImmutableArray<string> segments = TypeNameSegments(
                reader,
                chain,
                static (metadata, item) =>
                    metadata.GetTypeReference(item).Name);

            if (terminal.Kind == HandleKind.AssemblyReference)
            {
                var assemblyHandle = (AssemblyReferenceHandle)terminal;
                AssemblyReferenceIdentity assembly =
                    AssemblyReferenceIdentity.From(reader, assemblyHandle);
                return Definition(
                    assembly.Name,
                    ns,
                    segments,
                    new TypeReferenceOrigin.AssemblyReference(assembly),
                    FrameworkAssemblyKeys.IsFrameworkReference(reader, assemblyHandle),
                    FrameworkAssemblyKeys.IsAuthenticProtobufReference(reader, assemblyHandle),
                    rawTypeKind);
            }

            TypeReferenceOrigin origin;
            CurrentAssemblyInfo currentAssembly =
                CurrentAssembly(reader);
            if (terminal.IsNil || terminal.Kind == HandleKind.ModuleDefinition)
            {
                origin = new TypeReferenceOrigin.CurrentAssembly(
                    currentAssembly.Identity);
            }
            else if (terminal.Kind == HandleKind.ModuleReference)
            {
                string moduleName = reader.GetString(
                    reader.GetModuleReference(
                        (ModuleReferenceHandle)terminal).Name);
                if (string.IsNullOrEmpty(moduleName))
                    return TypeRef.Unsupported("type-reference module scope has no name");
                origin = new TypeReferenceOrigin.ModuleReference(moduleName);
            }
            else
            {
                return TypeRef.Unsupported(
                    $"type-reference resolution scope kind {terminal.Kind} is unsupported");
            }

            return Definition(
                currentAssembly.Name,
                ns,
                segments,
                origin,
                currentAssembly.TrustedFramework,
                currentAssembly.AuthenticProtobuf,
                rawTypeKind);
        }

        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return TypeRef.Unsupported(
                RelationshipProjectionFailure("type-reference resolution-scope", handle, ex),
                RelationshipProjectionFailure(handle, ex));
        }
    }

    static CurrentAssemblyInfo CurrentAssembly(
        MetadataReader reader)
        => s_currentAssemblies.GetValue(
            reader,
            static current => current.IsAssembly
                ? new CurrentAssemblyInfo(
                    current.GetString(
                        current.GetAssemblyDefinition().Name),
                    AssemblyReferenceIdentity
                        .FromAssemblyDefinition(current),
                    FrameworkAssemblyKeys
                        .IsFrameworkDefinition(current),
                    FrameworkAssemblyKeys
                        .IsAuthenticProtobufDefinition(current))
                : new CurrentAssemblyInfo(
                    "",
                    null,
                    TrustedFramework: false,
                    AuthenticProtobuf: true));

    /// <summary>
    /// Validates the structured name and returns the definition it names. The flattened
    /// <c>+</c>-joined spelling is produced once, by the identity owner, from the already
    /// validated segments — never by re-concatenating a growing prefix per nesting level, and
    /// never before the name has passed its character budget.
    /// </summary>
    static TypeRef Definition(
        string assembly,
        string ns,
        ImmutableArray<string> segments,
        TypeReferenceOrigin origin,
        bool trustedFrameworkAssembly = true,
        bool trustedProtobufAssembly = true,
        byte rawTypeKind = 0)
    {
        MetadataTypeDefinitionNameResult result =
            MetadataTypeDefinitionName.Create(
                ns,
                segments);
        if (result is not MetadataTypeDefinitionNameResult.Valid valid)
        {
            MetadataTypeNameRejection rejection =
                ((MetadataTypeDefinitionNameResult.Rejected)result).Rejection;
            return TypeRef.Unsupported(
                $"type-reference metadata name is invalid ({rejection.Kind})");
        }

        return TypeRef.Definition(
            assembly,
            ns,
            valid.Name.ToNestedMetadataName(),
            new ResolvableTypeReference(origin, valid.Name),
            trustedFrameworkAssembly,
            trustedProtobufAssembly,
            rawTypeKind);
    }

    static ImmutableArray<string> TypeNameSegments<THandle>(
        MetadataReader reader,
        ReadOnlySpan<THandle> chain,
        Func<MetadataReader, THandle, StringHandle> getName)
        where THandle : struct
    {
        var segments = ImmutableArray.CreateBuilder<string>(chain.Length);
        foreach (THandle handle in chain)
            segments.Add(reader.GetString(getName(reader, handle)));
        return segments.MoveToImmutable();
    }

    public TypeRef GetTypeFromSpecification(MetadataReader reader, GenericScope genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        if (s_recursionDepth >= MaxRecursionDepth)
            return TypeRef.Unsupported("type-specification recursion depth exceeded");
        var spec = reader.GetTypeSpecification(handle);
        int blobLength = reader.GetBlobReader(spec.Signature).Length;
        if (blobLength > MaxSignatureBlobLength)
            return TypeRef.Unsupported("type-specification signature blob too large");
        if (s_cumulativeSignatureBytes + blobLength > MaxCumulativeSignatureBytes)
            return TypeRef.Unsupported("type-specification cumulative signature blob too large");
        if (!ILInspector.Metadata.SignatureBlobGuard.IsSafeToDecode(
            reader,
            spec.Signature,
            ILInspector.Metadata.SignatureBlobGuard.Kind.TypeSpecification))
        {
            return TypeRef.Unsupported("type-specification signature nesting depth exceeded");
        }
        s_cumulativeSignatureBytes += blobLength;
        s_recursionDepth++;
        try
        {
            TypeRef decoded =
                spec.DecodeSignature(this, genericContext);
            decoded.RawTypeKind = rawTypeKind;
            return decoded;
        }
        finally
        {
            s_recursionDepth--;
            s_cumulativeSignatureBytes -= blobLength;
        }
    }

    public TypeRef GetSZArrayType(TypeRef elementType) => TypeRef.SzArray(elementType);
    public TypeRef GetArrayType(TypeRef elementType, ArrayShape shape)
        => TypeRef.MdArray(elementType, shape);
    public TypeRef GetByReferenceType(TypeRef elementType) => TypeRef.ByRef(elementType);
    public TypeRef GetPointerType(TypeRef elementType) => TypeRef.Pointer(elementType);
    public TypeRef GetPinnedType(TypeRef elementType) => TypeRef.Pinned(elementType);
    public TypeRef GetGenericInstantiation(TypeRef genericType, ImmutableArray<TypeRef> typeArguments)
        => TypeRef.GenericInstance(genericType, typeArguments);
    public TypeRef GetGenericTypeParameter(GenericScope genericContext, int index)
        => TypeRef.GenericParameter(index, NameAt(genericContext.TypeParameters, index));
    public TypeRef GetGenericMethodParameter(GenericScope genericContext, int index)
        => TypeRef.MethodGenericParameter(index, NameAt(genericContext.MethodParameters, index));
    public TypeRef GetFunctionPointerType(MethodSignature<TypeRef> signature)
        => TypeRef.UnsupportedFunctionPointer(signature);
    public TypeRef GetModifiedType(TypeRef modifier, TypeRef unmodifiedType, bool isRequired)
        => TypeRef.UnsupportedModified(
            modifier,
            unmodifiedType,
            isRequired);

    static string NameAt(ImmutableArray<string> names, int index)
        => index >= 0 && index < names.Length ? names[index] : "";

    static string RelationshipFailure(
        string relationship,
        RelationshipTraversalRejection rejection)
        => $"{relationship} relationship rejected ({rejection.Kind}) at "
            + $"0x{MetadataTokens.GetToken(rejection.Subject):X8} after "
            + $"{rejection.ConsumedNodes} nodes: {rejection.Detail}";

    static string RelationshipProjectionFailure(
        string relationship,
        EntityHandle subject,
        Exception exception)
        => $"{relationship} projection rejected (MalformedMetadata) at "
            + $"0x{MetadataTokens.GetToken(subject):X8}: {exception.Message}";

    static MetadataTypeNameFailure RelationshipProjectionFailure(
        EntityHandle subject,
        Exception exception)
        => MetadataTypeNameFailure.From(
            new RelationshipTraversalRejection(
                RelationshipTraversalRejectionKind.MalformedMetadata,
                exception.Message,
                subject,
                consumedNodes: 0));
}
