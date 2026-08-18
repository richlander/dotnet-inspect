using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>Generic parameter names in scope while decoding a signature.</summary>
internal sealed record GenericScope(ImmutableArray<string> TypeParameters, ImmutableArray<string> MethodParameters)
{
    public static readonly GenericScope Empty = new([], []);
}

/// <summary>
/// Decodes metadata signatures into <see cref="TypeRef"/>s. Primitives and
/// corelib-resolved types canonicalize to <see cref="TypeRef.CoreLibrary"/>
/// so identity does not depend on which facade spelled the reference.
/// Shapes outside the supported core (function pointers, custom modifiers)
/// decode to <see cref="TypeRefKind.Unsupported"/> — honest, fidelity-lowering,
/// never a guess.
/// </summary>
internal sealed class TypeRefDecoder : ISignatureTypeProvider<TypeRef, GenericScope>
{
    public static readonly TypeRefDecoder Instance = new();

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
        => TypeRef.CoreLib("System", typeCode switch
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
        });

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
            var leaf = reader.GetTypeDefinition(handle);
            string assembly = reader.IsAssembly
                ? CanonicalSelf(reader)
                : "";
            MetadataTypeDefinitionNameReadResult nameResult =
                MetadataTypeDefinitionName.Read(reader, handle);
            if (nameResult
                is MetadataTypeDefinitionNameReadResult.Rejected rejected)
            {
                return TypeRef.Unsupported(
                    "type-definition metadata name is incomplete",
                    rejected.Failure);
            }
            var definitionName =
                ((MetadataTypeDefinitionNameReadResult.Read)nameResult).Name;
            return TypeRef.DefinitionWithResolution(
                assembly,
                definitionName.Namespace,
                definitionName.ToNestedMetadataName(),
                HintFrom(rawTypeKind),
                InlineArrayFact(reader, leaf),
                EnclosingTypeFrom(assembly, definitionName),
                definitionName,
                resolutionAssembly: null);
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
            AssemblyReferenceIdentity? resolutionAssembly =
                terminal.Kind == HandleKind.AssemblyReference
                    ? AssemblyReferenceIdentity.From(
                        reader,
                        (AssemblyReferenceHandle)terminal)
                    : null;
            string assembly = resolutionAssembly is not null
                ? CanonicalReferenced(resolutionAssembly)
                : "";
            MetadataTypeDefinitionNameReadResult nameResult =
                MetadataTypeDefinitionName.Read(reader, handle);
            if (nameResult
                is MetadataTypeDefinitionNameReadResult.Rejected rejected)
            {
                return TypeRef.Unsupported(
                    "type-reference metadata name is incomplete",
                    rejected.Failure);
            }
            var definitionName =
                ((MetadataTypeDefinitionNameReadResult.Read)nameResult).Name;
            return TypeRef.DefinitionWithResolution(
                assembly,
                definitionName.Namespace,
                definitionName.ToNestedMetadataName(),
                HintFrom(rawTypeKind),
                MetadataFactState.Unknown,
                enclosingType: null,
                definitionName,
                resolutionAssembly);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return TypeRef.Unsupported(
                RelationshipProjectionFailure("type-reference resolution-scope", handle, ex),
                RelationshipProjectionFailure(handle, ex));
        }
    }

    /// <summary>
    /// The immediately-enclosing type for a nested type-definition chain
    /// built from the exact structured name the metadata relationship already
    /// proved — never by parsing the leaf's <c>+</c>-joined
    /// <see cref="TypeRef.Name"/>.
    /// Null when the leaf is not nested (a structured name with one segment).
    /// </summary>
    /// <remarks>
    /// Only this single level is materialized. It is the sole level any consumer
    /// reads (<c>DynamicCallSitePass.IsProvenBinderContext</c>), and
    /// <see cref="TypeRef"/> equality excludes <see cref="TypeRef.EnclosingType"/>,
    /// so no deeper ancestor is ever observed. Recursively rebuilding every
    /// ancestor's joined name grew allocation cubically in nesting depth on
    /// untrusted metadata.
    /// </remarks>
    static TypeRef? EnclosingTypeFrom(
        string assembly,
        MetadataTypeDefinitionName definitionName)
    {
        if (definitionName.Segments.Length <= 1)
            return null;

        MetadataTypeDefinitionNameResult parent =
            MetadataTypeDefinitionName.Create(
                definitionName.Namespace,
                definitionName.Segments.RemoveAt(
                    definitionName.Segments.Length - 1));
        return parent is not MetadataTypeDefinitionNameResult.Valid valid
            ? null
            : TypeRef.DefinitionWithResolution(
                assembly,
                valid.Name.Namespace,
                valid.Name.ToNestedMetadataName(),
                ValueTypeHint.Unknown,
                MetadataFactState.Unknown,
                enclosingType: null,
                valid.Name,
                resolutionAssembly: null);
    }

    public TypeRef GetTypeFromSpecification(MetadataReader reader, GenericScope genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        if (s_recursionDepth >= MaxRecursionDepth)
            return TypeRef.Unsupported("type-specification recursion depth exceeded");
        var spec = reader.GetTypeSpecification(handle);
        int blobLength = reader.GetBlobReader(spec.Signature).Length;
        // Apply the cheap #2489 length / cumulative caps FIRST: they reject an over-long blob in
        // O(1), which also keeps the SignatureBlobGuard below from ever walking (and allocating a
        // work item per element of) a huge blob. The length/cumulative caps bound the cross-blob
        // modreq *cycle*; the guard then bounds this single (now <= 1024-byte) blob's structural
        // depth and count-driven allocations — a short blob with a huge count field (e.g. an
        // array-shape size count) would otherwise reach SRM and OOM. Covers both direct callers of
        // this method and SRM's recursive re-entry for a nested TypeSpec.
        if (blobLength > MaxSignatureBlobLength)
            return TypeRef.Unsupported("type-specification signature blob too large");
        if (s_cumulativeSignatureBytes + blobLength > MaxCumulativeSignatureBytes)
            return TypeRef.Unsupported("type-specification cumulative signature blob too large");
        if (!ILInspector.Metadata.SignatureBlobGuard.IsSafeToDecode(reader, spec.Signature, ILInspector.Metadata.SignatureBlobGuard.Kind.TypeSpecification))
            return TypeRef.Unsupported("type-specification signature nesting depth exceeded");
        s_cumulativeSignatureBytes += blobLength;
        s_recursionDepth++;
        try
        {
            return spec.DecodeSignature(this, genericContext);
        }
        finally
        {
            s_recursionDepth--;
            s_cumulativeSignatureBytes -= blobLength;
        }
    }

    public TypeRef GetSZArrayType(TypeRef elementType) => TypeRef.SzArray(elementType);

    public TypeRef GetArrayType(TypeRef elementType, ArrayShape shape) => TypeRef.MdArray(elementType, shape.Rank);

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
        => TypeRef.FunctionPointer(signature.ReturnType, signature.ParameterTypes, ConventionText(signature.Header.CallingConvention));

    /// <summary>The C# calling-convention spelling for a function pointer: empty for a managed pointer, the <c>unmanaged</c> keyword (with the specific convention in brackets) otherwise.</summary>
    public static string ConventionText(SignatureCallingConvention convention) => convention switch
    {
        SignatureCallingConvention.Default => "",
        SignatureCallingConvention.CDecl => "unmanaged[Cdecl]",
        SignatureCallingConvention.StdCall => "unmanaged[Stdcall]",
        SignatureCallingConvention.ThisCall => "unmanaged[Thiscall]",
        SignatureCallingConvention.FastCall => "unmanaged[Fastcall]",
        _ => "unmanaged",
    };

    public static string ConventionText(SignatureCallingConvention convention, TypeRef returnType)
    {
        var pointer = TypeRef.FunctionPointer(returnType, [], ConventionText(convention));
        return pointer.CallingConvention;
    }

    /// <summary>
    /// Custom modifiers remain attached to the unmodified type without wrapping
    /// or changing its structural <see cref="TypeRef.Kind"/>. Body equality and
    /// rendering continue to see through declaration-only modifiers, while exact
    /// cross-assembly signature matching can distinguish overloads whose metadata
    /// signatures differ only by <c>modreq</c>/<c>modopt</c>.
    /// <c>CrossAssemblyMethodFactsTests.CustomModifierSignatureCollision_UsesExactModifiers</c>
    /// gates that distinction.
    /// </summary>
    public TypeRef GetModifiedType(TypeRef modifier, TypeRef unmodifiedType, bool isRequired)
        => unmodifiedType.WithCustomModifier(modifier, isRequired);

    static string NameAt(ImmutableArray<string> names, int index)
        => index >= 0 && index < names.Length ? names[index] : "";

    // ECMA-335 II.23.1.16 signature element types.
    const byte ElementTypeValueType = 0x11;
    const byte ElementTypeClass = 0x12;

    /// <summary>Maps the signature's CLASS/VALUETYPE byte to a hint; anything else (a type reached outside a signature) is Unknown.</summary>
    static ValueTypeHint HintFrom(byte rawTypeKind) => rawTypeKind switch
    {
        ElementTypeValueType => ValueTypeHint.ValueType,
        ElementTypeClass => ValueTypeHint.ReferenceType,
        _ => ValueTypeHint.Unknown,
    };

    static MetadataFactState InlineArrayFact(MetadataReader reader, TypeDefinition typeDef)
        => MethodDefinitionFacts.HasInlineArrayAttribute(reader, typeDef)
            ? MetadataFactState.Yes
            : MetadataFactState.No;

    /// <summary>
    /// Canonicalizes corelib spellings so facade choice never affects identity.
    /// Name-only: safe ONLY as an input to <see cref="CanonicalSelf"/>-consistent
    /// comparisons where the reader's own token has already been asserted
    /// elsewhere, or for non-identity display purposes. Never call this directly
    /// on an <see cref="AssemblyReference"/>'s name (forgeable; see
    /// <see cref="CanonicalReferenced"/>) or as a substitute for
    /// <see cref="CanonicalSelf"/> on an <see cref="AssemblyDefinition"/>'s own
    /// name (also forgeable: the reader could be an untrusted assembly opened by
    /// cross-assembly resolution, not the originally-opened target).
    /// </summary>
    internal static string Canonical(string assemblyName)
        => IsCoreLibFacadeName(assemblyName) ? TypeRef.CoreLibrary : assemblyName;

    /// <summary>
    /// Canonicalizes the reader's own <see cref="AssemblyDefinition"/> simple
    /// name to <see cref="TypeRef.CoreLibrary"/> only when its public key hashes
    /// to a trusted platform token (<see cref="PlatformKeys.IsPlatform"/>). The
    /// reader is not always the originally-opened, explicitly-trusted target: a
    /// cross-assembly resolver can open an untrusted sibling file (e.g. a
    /// same-directory <c>System.Runtime.dll</c> resolved for an unsigned
    /// reference) and decode types from ITS OWN metadata through
    /// <see cref="GetTypeFromDefinition"/>. Trusting that reader's self-claimed
    /// name would let a planted file mint corelib identity for the types it
    /// defines. Every caller of self-name canonicalization (same-assembly
    /// identity comparisons included) must use this, never plain
    /// <see cref="Canonical(string)"/>, so identity stays consistent.
    /// </summary>
    internal static string CanonicalSelf(MetadataReader reader)
    {
        var definition = reader.GetAssemblyDefinition();
        string name = reader.GetString(definition.Name);
        if (!IsCoreLibFacadeName(name))
            return name;
        if (definition.PublicKey.IsNil)
            return name;
        string token = AssemblyReferenceIdentity.ComputePublicKeyToken(reader.GetBlobBytes(definition.PublicKey));
        return PlatformKeys.IsPlatform(token) ? TypeRef.CoreLibrary : name;
    }

    /// <summary>
    /// Canonicalizes a referenced assembly's simple name to
    /// <see cref="TypeRef.CoreLibrary"/> only when its public-key token is a
    /// trusted platform key (<see cref="PlatformKeys.IsPlatform"/>). An
    /// <see cref="AssemblyReference"/>'s <c>Name</c> is forgeable — a planted
    /// assembly can declare an <c>AssemblyRef</c> row named
    /// <c>"System.Runtime"</c> with no valid public-key token — so name alone
    /// must never grant corelib identity for a reference.
    /// </summary>
    static string CanonicalReferenced(AssemblyReferenceIdentity identity)
    {
        string name = identity.Name;
        if (!IsCoreLibFacadeName(name))
            return name;
        return PlatformKeys.IsPlatform(identity.PublicKeyToken)
            ? TypeRef.CoreLibrary
            : name;
    }

    static bool IsCoreLibFacadeName(string assemblyName) => assemblyName is
        "System.Private.CoreLib" or "System.Runtime" or "mscorlib" or "netstandard" or "System.Runtime.Extensions";

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
