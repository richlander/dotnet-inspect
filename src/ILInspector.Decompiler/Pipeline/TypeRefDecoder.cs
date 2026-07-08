using System.Collections.Immutable;
using System.Reflection.Metadata;

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

    // Attacker-controlled metadata can encode a self-referential resolution scope,
    // TypeSpecification, or nested-type chain, which recurses into an *uncatchable*
    // StackOverflow (the try/catch filters around these calls cannot catch it). Guard the
    // recursive descents with a per-thread depth limit — the decoder is a shared singleton
    // used under Parallel, so the counter must be thread-local — and fail closed to
    // Unsupported. Real metadata nests shallowly, so the limit only trips on malformed input.
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
        var typeDef = reader.GetTypeDefinition(handle);
        string name = reader.GetString(typeDef.Name);
        string ns = reader.GetString(typeDef.Namespace);
        if (typeDef.IsNested)
        {
            if (s_recursionDepth >= MaxRecursionDepth)
                return TypeRef.Unsupported("type-definition nesting depth exceeded");
            s_recursionDepth++;
            try
            {
                var declaring = GetTypeFromDefinition(reader, typeDef.GetDeclaringType(), 0);
                if (declaring.Kind == TypeRefKind.Unsupported)
                    return declaring;
                return TypeRef.Definition(
                    declaring.Assembly,
                    declaring.Namespace,
                    $"{declaring.Name}+{name}",
                    HintFrom(rawTypeKind),
                    InlineArrayFact(reader, typeDef));
            }
            finally
            {
                s_recursionDepth--;
            }
        }
        string assembly = reader.IsAssembly
            ? Canonical(reader.GetString(reader.GetAssemblyDefinition().Name))
            : "";
        return TypeRef.Definition(assembly, ns, name, HintFrom(rawTypeKind), InlineArrayFact(reader, typeDef));
    }

    public TypeRef GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var typeRef = reader.GetTypeReference(handle);
        string name = reader.GetString(typeRef.Name);
        string ns = reader.GetString(typeRef.Namespace);
        switch (typeRef.ResolutionScope.Kind)
        {
            case HandleKind.AssemblyReference:
                var assembly = reader.GetAssemblyReference((AssemblyReferenceHandle)typeRef.ResolutionScope);
                return TypeRef.Definition(Canonical(reader.GetString(assembly.Name)), ns, name, HintFrom(rawTypeKind));
            case HandleKind.TypeReference:
                if (s_recursionDepth >= MaxRecursionDepth)
                    return TypeRef.Unsupported("type-reference resolution-scope recursion depth exceeded");
                s_recursionDepth++;
                try
                {
                    var declaring = GetTypeFromReference(reader, (TypeReferenceHandle)typeRef.ResolutionScope, 0);
                    if (declaring.Kind == TypeRefKind.Unsupported)
                        return declaring;
                    return TypeRef.Definition(declaring.Assembly, declaring.Namespace, $"{declaring.Name}+{name}", HintFrom(rawTypeKind));
                }
                finally
                {
                    s_recursionDepth--;
                }
            default:
                return TypeRef.Definition("", ns, name, HintFrom(rawTypeKind));
        }
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
    /// Custom modifiers are seen through to the unmodified type. The three that
    /// occur in practice — <c>modreq(InAttribute)</c> (an <c>in</c>/<c>ref readonly</c>
    /// parameter or return), <c>modreq(IsVolatile)</c> (a <c>volatile</c> field),
    /// and <c>modreq(IsExternalInit)</c> (an <c>init</c> accessor) — are
    /// declaration-site concerns the signature renderer reads from metadata; they
    /// never appear in a method body, where types surface only as local
    /// declarations, casts, and call arguments over the *unmodified* type. Seeing
    /// through keeps the underlying shape intact (an <c>in T</c> stays
    /// <c>ByRef(T)</c>, so every <c>ByRef</c>/<c>Pointer</c> unwrap site still
    /// matches) and lets a fully-representable body import at
    /// <see cref="DecompilationFidelity.Full"/> instead of being capped by a
    /// modifier that the C# never spells here.
    ///
    /// This is the "no infrastructure without a customer" choice
    /// (docs/decompiler.md): the design contract has type identity carry
    /// modifiers through the tree, but no body consumer reads them today, and
    /// wrapping the byref of an <c>in</c> parameter would break the structural
    /// <c>Kind == ByRef</c> checks. When an IR-based signature renderer needs the
    /// distinction, model it then.
    /// </summary>
    public TypeRef GetModifiedType(TypeRef modifier, TypeRef unmodifiedType, bool isRequired)
        => IsRepresentedFunctionPointerModifier(modifier, isRequired)
            ? unmodifiedType.WithCustomModifier(modifier, isRequired)
            : unmodifiedType;

    static bool IsRepresentedFunctionPointerModifier(TypeRef modifier, bool isRequired)
        => modifier is { Kind: TypeRefKind.Definition }
            && ((isRequired
                    && modifier.Namespace == "System.Runtime.InteropServices"
                    && modifier.Name is "InAttribute" or "OutAttribute")
                || (isRequired
                    && modifier.Namespace == "System.Runtime.CompilerServices"
                    && modifier.Name is "IsReadOnlyAttribute" or "RequiresLocationAttribute")
                || (!isRequired
                    && modifier.Namespace == "System.Runtime.CompilerServices"
                    && modifier.Name == "CallConvSuppressGCTransition"));

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

    /// <summary>Canonicalizes corelib spellings so facade choice never affects identity.</summary>
    internal static string Canonical(string assemblyName) => assemblyName is
        "System.Private.CoreLib" or "System.Runtime" or "mscorlib" or "netstandard" or "System.Runtime.Extensions"
        ? TypeRef.CoreLibrary
        : assemblyName;
}
