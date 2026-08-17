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
    public static TypeNodeProvider CreateCaching() => new(cacheNames: true);

    /// <summary>
    /// Caching provider that count-firsts TypeDef/TypeRef names before
    /// <c>GetString</c>. Used by bounded API-surface extraction so a single
    /// multi-MB signature type name cannot materialize before the retained
    /// budget rejects it (Sol R10).
    /// </summary>
    public static TypeNodeProvider CreateCaching(Action<long> beforeMaterializeName)
        => new(cacheNames: true, beforeMaterializeName);

    // Delegate to existing SignatureDecoder for name resolution to avoid duplication.
    private static readonly SignatureDecoder NameDecoder = SignatureDecoder.Instance;
    readonly Dictionary<TypeDefinitionHandle, string>? _definitionNames;
    readonly Dictionary<TypeReferenceHandle, string>? _referenceNames;
    readonly Action<long>? _beforeMaterializeName;

    TypeNodeProvider(bool cacheNames = false, Action<long>? beforeMaterializeName = null)
    {
        _beforeMaterializeName = beforeMaterializeName;
        if (cacheNames)
        {
            _definitionNames = [];
            _referenceNames = [];
        }
    }

    public TypeNode GetPrimitiveType(PrimitiveTypeCode typeCode)
    {
        string name = NameDecoder.GetPrimitiveType(typeCode);
        bool isRef = typeCode is PrimitiveTypeCode.String or PrimitiveTypeCode.Object;
        return new PrimitiveTypeNode(name, isRef);
    }

    public TypeNode GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        string name;
        if (_definitionNames is null
            || !_definitionNames.TryGetValue(handle, out name!))
        {
            EnsureNameCanMaterialize(reader, handle);
            name = NameDecoder.GetTypeFromDefinition(reader, handle, rawTypeKind);
            _definitionNames?.Add(handle, name);
        }
        bool isRef = rawTypeKind != 0x11; // 0x11 = ELEMENT_TYPE_VALUETYPE
        return new NamedTypeNode(name, isRef);
    }

    public TypeNode GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        string name;
        if (_referenceNames is null
            || !_referenceNames.TryGetValue(handle, out name!))
        {
            EnsureNameCanMaterialize(reader, handle);
            name = NameDecoder.GetTypeFromReference(reader, handle, rawTypeKind);
            _referenceNames?.Add(handle, name);
        }
        bool isRef = rawTypeKind != 0x11; // 0x11 = ELEMENT_TYPE_VALUETYPE
        return new NamedTypeNode(name, isRef);
    }

    void EnsureNameCanMaterialize(MetadataReader reader, EntityHandle handle)
    {
        if (_beforeMaterializeName is null)
            return;
        if (MetadataSafetyPolicy.TryCountTypeNameCharacters(
                reader,
                handle,
                out long characters))
        {
            _beforeMaterializeName(characters);
        }
    }

    public TypeNode GetTypeFromSpecification(MetadataReader reader, GenericContext? context, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
            return new DegradedTypeNode();
        using (scope)
        {
            return reader.GetTypeSpecification(handle).DecodeSignature(this, context);
        }
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

        // Keep metadata arity markers structural. Expanding one to N placeholder
        // names here lets a few metadata digits allocate gigabytes before the
        // caller can consult RenderLength.
        return new GenericTypeNode(
            rawName,
            genericType.IsReferenceType,
            typeArguments,
            degradedGenericType: genericType.IsDegraded,
            useMetadataArity: true);
    }

    public TypeNode GetGenericMethodParameter(GenericContext? context, int index)
    {
        string name = NameDecoder.GetGenericMethodParameter(context, index);
        return new GenericParameterNode(
            name,
            hasValueTypeConstraint: context?.HasMethodParameterValueTypeConstraint(index) == true);
    }

    public TypeNode GetGenericTypeParameter(GenericContext? context, int index)
    {
        string name = NameDecoder.GetGenericTypeParameter(context, index);
        return new GenericParameterNode(
            name,
            hasValueTypeConstraint: context?.HasTypeParameterValueTypeConstraint(index) == true);
    }

    public TypeNode GetFunctionPointerType(MethodSignature<TypeNode> signature) => new FunctionPointerTypeNode(signature);

    public TypeNode GetModifiedType(TypeNode modifier, TypeNode unmodifiedType, bool isRequired) => new ModifiedTypeNode(modifier, unmodifiedType, isRequired);

    public TypeNode GetPinnedType(TypeNode elementType) => new PassthroughTypeNode(elementType);
}
