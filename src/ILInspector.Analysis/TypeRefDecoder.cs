using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Analysis;

internal sealed record GenericScope(ImmutableArray<string> TypeParameters, ImmutableArray<string> MethodParameters)
{
    public static readonly GenericScope Empty = new([], []);
}

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
                return TypeRef.Definition(declaring.Assembly, declaring.Namespace, $"{declaring.Name}+{name}", declaring.TrustedFrameworkAssembly, declaring.TrustedProtobufAssembly);
            }
            finally
            {
                s_recursionDepth--;
            }
        }
        string assembly = reader.IsAssembly
            ? reader.GetString(reader.GetAssemblyDefinition().Name)
            : "";
        return TypeRef.Definition(assembly, ns, name, FrameworkAssemblyKeys.IsFrameworkDefinition(reader), FrameworkAssemblyKeys.IsAuthenticProtobufDefinition(reader));
    }

    public TypeRef GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var typeRef = reader.GetTypeReference(handle);
        string name = reader.GetString(typeRef.Name);
        string ns = reader.GetString(typeRef.Namespace);
        return typeRef.ResolutionScope.Kind switch
        {
            HandleKind.AssemblyReference => TypeRef.Definition(
                reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)typeRef.ResolutionScope).Name),
                ns,
                name,
                FrameworkAssemblyKeys.IsFrameworkReference(reader, (AssemblyReferenceHandle)typeRef.ResolutionScope),
                FrameworkAssemblyKeys.IsAuthenticProtobufReference(reader, (AssemblyReferenceHandle)typeRef.ResolutionScope)),
            HandleKind.TypeReference => NestedReference(reader, (TypeReferenceHandle)typeRef.ResolutionScope, name),
            // ModuleReference / nil scope: the type is in the current assembly (another
            // module of it, or an exported/forwarded type resolved in this manifest), so
            // stamp the current assembly name — matching GetTypeFromDefinition — rather than
            // an empty string, so assembly-qualified identity keys are symmetric between a
            // definition and a same-assembly reference to it.
            _ => TypeRef.Definition(
                reader.IsAssembly ? reader.GetString(reader.GetAssemblyDefinition().Name) : "",
                ns,
                name,
                FrameworkAssemblyKeys.IsFrameworkDefinition(reader),
                FrameworkAssemblyKeys.IsAuthenticProtobufDefinition(reader)),
        };
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
    public TypeRef GetFunctionPointerType(MethodSignature<TypeRef> signature) => TypeRef.Unsupported("function pointer");
    public TypeRef GetModifiedType(TypeRef modifier, TypeRef unmodifiedType, bool isRequired)
        => TypeRef.Unsupported($"custom modifier ({(isRequired ? "modreq" : "modopt")} {modifier.ToDisplayString()})");

    static TypeRef NestedReference(MetadataReader reader, TypeReferenceHandle declaringHandle, string nestedName)
    {
        if (s_recursionDepth >= MaxRecursionDepth)
            return TypeRef.Unsupported("type-reference resolution-scope recursion depth exceeded");
        s_recursionDepth++;
        try
        {
            var declaring = Instance.GetTypeFromReference(reader, declaringHandle, 0);
            if (declaring.Kind == TypeRefKind.Unsupported)
                return declaring;
            return TypeRef.Definition(declaring.Assembly, declaring.Namespace, $"{declaring.Name}+{nestedName}", declaring.TrustedFrameworkAssembly, declaring.TrustedProtobufAssembly);
        }
        finally
        {
            s_recursionDepth--;
        }
    }

    static string NameAt(ImmutableArray<string> names, int index)
        => index >= 0 && index < names.Length ? names[index] : "";
}
