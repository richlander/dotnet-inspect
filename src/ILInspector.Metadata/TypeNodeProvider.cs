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
        // Strip .NET arity suffix (e.g., List`1 -> List)
        string rawName = genericType is NamedTypeNode n ? n.Name : genericType.Render();
        var backtickIndex = rawName.IndexOf('`');
        var baseName = backtickIndex >= 0 ? rawName[..backtickIndex] : rawName;
        return new GenericTypeNode(baseName, genericType.IsReferenceType, typeArguments);
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

    public TypeNode GetFunctionPointerType(MethodSignature<TypeNode> signature) => new FunctionPointerTypeNode();

    public TypeNode GetModifiedType(TypeNode modifier, TypeNode unmodifiedType, bool isRequired) => new PassthroughTypeNode(unmodifiedType);

    public TypeNode GetPinnedType(TypeNode elementType) => new PassthroughTypeNode(elementType);
}
