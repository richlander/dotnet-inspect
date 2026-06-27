using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Decodes type signatures from metadata into <see cref="TypeNode"/> trees.
/// The tree can then have nullability annotations applied before rendering.
/// </summary>
internal sealed class TypeNodeProvider : ISignatureTypeProvider<TypeNode, GenericContext?>
{
    public static TypeNodeProvider Instance { get; } = new();

    // Delegate to existing SignatureDecoder for name resolution to avoid duplication.
    private static readonly SignatureDecoder NameDecoder = SignatureDecoder.Instance;

    public TypeNode GetPrimitiveType(PrimitiveTypeCode typeCode)
    {
        string name = NameDecoder.GetPrimitiveType(typeCode);
        bool isRef = typeCode is PrimitiveTypeCode.String or PrimitiveTypeCode.Object;
        return new PrimitiveTypeNode(name, isRef);
    }

    public TypeNode GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        string name = NameDecoder.GetTypeFromDefinition(reader, handle, rawTypeKind);
        bool isRef = rawTypeKind != 0x11; // 0x11 = ELEMENT_TYPE_VALUETYPE
        return new NamedTypeNode(name, isRef);
    }

    public TypeNode GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        string name = NameDecoder.GetTypeFromReference(reader, handle, rawTypeKind);
        bool isRef = rawTypeKind != 0x11; // 0x11 = ELEMENT_TYPE_VALUETYPE
        return new NamedTypeNode(name, isRef);
    }

    public TypeNode GetTypeFromSpecification(MetadataReader reader, GenericContext? context, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        var typeSpec = reader.GetTypeSpecification(handle);
        return typeSpec.DecodeSignature(this, context);
    }

    public TypeNode GetSZArrayType(TypeNode elementType) => new SZArrayTypeNode(elementType);

    public TypeNode GetArrayType(TypeNode elementType, ArrayShape shape) => new MDArrayTypeNode(elementType, shape.Rank);

    public TypeNode GetByReferenceType(TypeNode elementType) => new ByRefTypeNode(elementType);

    public TypeNode GetPointerType(TypeNode elementType) => new PointerTypeNode(elementType);

    public TypeNode GetGenericInstantiation(TypeNode genericType, ImmutableArray<TypeNode> typeArguments)
    {
        string rawName = genericType is NamedTypeNode n ? n.Name : genericType.Render();
        var backtickIndex = rawName.IndexOf('`');
        if (backtickIndex < 0)
            return new GenericTypeNode(rawName, genericType.IsReferenceType, typeArguments);

        // Strip only the arity digits at the first backtick, keeping any trailing
        // nested-type segment (Dictionary`2.Enumerator -> base "Dictionary",
        // suffix ".Enumerator") so the instantiation renders Dictionary<…>.Enumerator
        // rather than collapsing to Dictionary<…>.
        var baseName = rawName[..backtickIndex];
        var suffixStart = backtickIndex + 1;
        while (suffixStart < rawName.Length && char.IsDigit(rawName[suffixStart]))
            suffixStart++;
        var nestedSuffix = TypeResolver.FormatDisplayName(rawName[suffixStart..]);
        return new GenericTypeNode(baseName, genericType.IsReferenceType, typeArguments, nestedSuffix);
    }

    public TypeNode GetGenericMethodParameter(GenericContext? context, int index)
    {
        string name = NameDecoder.GetGenericMethodParameter(context, index);
        return new GenericParameterNode(name);
    }

    public TypeNode GetGenericTypeParameter(GenericContext? context, int index)
    {
        string name = NameDecoder.GetGenericTypeParameter(context, index);
        return new GenericParameterNode(name);
    }

    public TypeNode GetFunctionPointerType(MethodSignature<TypeNode> signature) => new FunctionPointerTypeNode(signature);

    public TypeNode GetModifiedType(TypeNode modifier, TypeNode unmodifiedType, bool isRequired) => new ModifiedTypeNode(modifier, unmodifiedType, isRequired);

    public TypeNode GetPinnedType(TypeNode elementType) => new PassthroughTypeNode(elementType);
}
