using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

namespace ILInspector.Metadata;

/// <summary>
/// Decodes type signatures from metadata into <see cref="TypeNode"/> trees.
/// The tree can then have nullability annotations applied before rendering.
/// </summary>
internal sealed class TypeNodeProvider : ISignatureTypeProvider<TypeNode, GenericContext?>
{
    public static TypeNodeProvider Instance { get; } = new();
    readonly Action<string>? _beforeRetain;
    readonly Action<int>? _beforeMaterialize;
    readonly ConditionalWeakTable<MetadataReader, ReaderNameCache> _readerNames = new();

    public TypeNodeProvider(
        Action<string>? beforeRetain = null,
        Action<int>? beforeMaterialize = null)
    {
        _beforeRetain = beforeRetain;
        _beforeMaterialize = beforeMaterialize;
    }

    // Delegate to existing SignatureDecoder for name resolution to avoid duplication.
    private static readonly SignatureDecoder NameDecoder = SignatureDecoder.Instance;

    public TypeNode GetPrimitiveType(PrimitiveTypeCode typeCode)
    {
        _beforeMaterialize?.Invoke(16);
        string name = NameDecoder.GetPrimitiveType(typeCode);
        _beforeRetain?.Invoke(name);
        bool isRef = typeCode is PrimitiveTypeCode.String or PrimitiveTypeCode.Object;
        return new PrimitiveTypeNode(name, isRef);
    }

    public TypeNode GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        if (TryGetCached(reader, handle, out NamedTypeRead? cached))
        {
            ReplayMaterializationWork(cached.MaterializationWork);
            return ReadNamedType(cached, rawTypeKind);
        }

        int materializationWork = 0;
        Action<int>? observe = _beforeMaterialize is null
            ? null
            : amount =>
            {
                materializationWork = (int)Math.Min(
                    int.MaxValue,
                    (long)materializationWork + amount);
                _beforeMaterialize(amount);
            };
        bool resolved = TypeResolver.TryGetTypeNameFromDefinition(
            reader,
            handle,
            observe,
            out string? name,
            out RelationshipTraversalRejection? rejection);
        MetadataTypeNameParts? metadataName = resolved
            ? TypeResolver.GetTypeNamePartsFromDefinition(reader, handle)
            : null;
        var read = new NamedTypeRead(
            resolved,
            name,
            rejection,
            metadataName,
            materializationWork);
        Cache(reader, handle, read);
        return ReadNamedType(read, rawTypeKind);
    }

    public TypeNode GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        if (TryGetCached(reader, handle, out NamedTypeRead? cached))
        {
            ReplayMaterializationWork(cached.MaterializationWork);
            return ReadNamedType(cached, rawTypeKind);
        }

        int materializationWork = 0;
        Action<int>? observe = _beforeMaterialize is null
            ? null
            : amount =>
            {
                materializationWork = (int)Math.Min(
                    int.MaxValue,
                    (long)materializationWork + amount);
                _beforeMaterialize(amount);
            };
        bool resolved = TypeResolver.TryGetTypeNameFromReference(
            reader,
            handle,
            observe,
            out string? name,
            out RelationshipTraversalRejection? rejection);
        MetadataTypeNameParts? metadataName = resolved
            ? TypeResolver.GetTypeNamePartsFromReference(reader, handle)
            : null;
        var read = new NamedTypeRead(
            resolved,
            name,
            rejection,
            metadataName,
            materializationWork);
        Cache(reader, handle, read);
        return ReadNamedType(read, rawTypeKind);
    }

    TypeNode ReadNamedType(
        NamedTypeRead read,
        byte rawTypeKind)
    {
        if (read.Resolved)
        {
            _beforeRetain?.Invoke(read.Name!);
            bool isRef = rawTypeKind != 0x11; // 0x11 = ELEMENT_TYPE_VALUETYPE
            return new NamedTypeNode(
                read.Name!,
                isRef,
                read.MetadataName);
        }

        ArgumentNullException.ThrowIfNull(read.Rejection);
        if (read.Rejection.Kind == RelationshipTraversalRejectionKind.NameBudget)
            return new DegradedTypeNode();

        throw new BadImageFormatException(
            $"Metadata relationship traversal rejected ({read.Rejection.Kind}): "
            + read.Rejection.Detail);
    }

    bool TryGetCached(
        MetadataReader reader,
        EntityHandle handle,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out NamedTypeRead? read)
    {
        if (!_readerNames.TryGetValue(reader, out ReaderNameCache? cache))
        {
            read = null;
            return false;
        }
        lock (cache.Names)
            return cache.Names.TryGetValue(handle, out read);
    }

    void Cache(
        MetadataReader reader,
        EntityHandle handle,
        NamedTypeRead read)
    {
        ReaderNameCache cache = _readerNames.GetValue(
            reader,
            static _ => new ReaderNameCache());
        lock (cache.Names)
        {
            if (cache.TryReserve(read))
                cache.Names.TryAdd(handle, read);
        }
    }

    sealed record NamedTypeRead(
        bool Resolved,
        string? Name,
        RelationshipTraversalRejection? Rejection,
        MetadataTypeNameParts? MetadataName,
        int MaterializationWork);

    void ReplayMaterializationWork(int amount)
    {
        if (amount > 0)
            _beforeMaterialize?.Invoke(amount);
    }

    sealed class ReaderNameCache
    {
        internal Dictionary<EntityHandle, NamedTypeRead> Names { get; } = [];

        long _retainedCharacters;

        internal bool TryReserve(NamedTypeRead read)
        {
            if (Names.Count >= SignatureDecoder.MaxAcceptedNameCacheEntries)
                return false;

            long characters = read.Name?.Length ?? 0;
            if (read.MetadataName is { } structured)
            {
                characters += structured.Namespace.Length;
                foreach (string segment in structured.Segments)
                    characters += segment.Length;
            }
            if (characters
                > SignatureDecoder.MaxAcceptedNameCacheCharacters
                    - _retainedCharacters)
            {
                return false;
            }

            _retainedCharacters += characters;
            return true;
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

    public TypeNode GetSZArrayType(TypeNode elementType)
    {
        _beforeMaterialize?.Invoke(16);
        var node = new SZArrayTypeNode(elementType);
        ObserveMaterialization(node.EstimatedRenderedLength);
        return node;
    }

    public TypeNode GetArrayType(TypeNode elementType, ArrayShape shape)
    {
        ObserveMaterialization(16L + Math.Max(shape.Rank, 0));
        var node = new MDArrayTypeNode(elementType, shape.Rank);
        ObserveMaterialization(node.EstimatedRenderedLength);
        return node;
    }

    public TypeNode GetByReferenceType(TypeNode elementType)
    {
        _beforeMaterialize?.Invoke(16);
        var node = new ByRefTypeNode(elementType);
        ObserveMaterialization(node.EstimatedRenderedLength);
        return node;
    }

    public TypeNode GetPointerType(TypeNode elementType)
    {
        _beforeMaterialize?.Invoke(16);
        var node = new PointerTypeNode(elementType);
        ObserveMaterialization(node.EstimatedRenderedLength);
        return node;
    }

    public TypeNode GetGenericInstantiation(TypeNode genericType, ImmutableArray<TypeNode> typeArguments)
    {
        _beforeMaterialize?.Invoke(checked(16 + typeArguments.Length * 4));
        ObserveMaterialization(genericType.EstimatedRenderedLength);
        string rawName = genericType is NamedTypeNode n ? n.Name : genericType.Render();
        GenericTypeNode node;
        if (genericType is NamedTypeNode { MetadataName: { } metadataName })
        {
            string exactBaseName = string.Join(
                ".",
                metadataName.Segments.Select(MetadataNameArity.StripFromSegment));
            if (metadataName.Namespace.Length > 0)
                exactBaseName = $"{metadataName.Namespace}.{exactBaseName}";
            node = new GenericTypeNode(
                exactBaseName,
                genericType.IsReferenceType,
                typeArguments,
                degradedGenericType: genericType.IsDegraded,
                metadataName: metadataName);
        }
        else
        {
            // Split at the first canonical `N marker, keeping any trailing
            // nested-type segment (Dictionary`2.Enumerator -> base "Dictionary",
            // suffix ".Enumerator") so the instantiation renders Dictionary<…>.Enumerator
            // rather than collapsing to Dictionary<…>. MetadataNameArity owns
            // what counts as a marker, so a literal backtick is retained.
            MetadataNameComponent marker = default;
            bool found = false;
            foreach (MetadataNameComponent component in
                MetadataNameArity.EnumerateComponents(rawName))
            {
                if (component.Arity <= 0)
                    continue;
                marker = component;
                found = true;
                break;
            }

            if (!found)
            {
                node = new GenericTypeNode(
                    rawName,
                    genericType.IsReferenceType,
                    typeArguments);
            }
            else
            {
                string baseName = rawName[..marker.SimpleNameEnd];
                string nestedSuffix =
                    TypeResolver.FormatDisplayName(rawName[marker.End..]);
                node = new GenericTypeNode(
                    baseName,
                    genericType.IsReferenceType,
                    typeArguments,
                    nestedSuffix,
                    genericType.IsDegraded);
            }
        }

        ObserveMaterialization(node.EstimatedRenderedLength);
        return node;
    }

    public TypeNode GetGenericMethodParameter(GenericContext? context, int index)
    {
        _beforeMaterialize?.Invoke(16);
        string name = NameDecoder.GetGenericMethodParameter(context, index);
        _beforeRetain?.Invoke(name);
        return new GenericParameterNode(
            name,
            hasValueTypeConstraint: context?.HasMethodParameterValueTypeConstraint(index) == true);
    }

    public TypeNode GetGenericTypeParameter(GenericContext? context, int index)
    {
        _beforeMaterialize?.Invoke(16);
        string name = NameDecoder.GetGenericTypeParameter(context, index);
        _beforeRetain?.Invoke(name);
        return new GenericParameterNode(
            name,
            hasValueTypeConstraint: context?.HasTypeParameterValueTypeConstraint(index) == true);
    }

    public TypeNode GetFunctionPointerType(MethodSignature<TypeNode> signature)
    {
        _beforeMaterialize?.Invoke(checked(16 + signature.ParameterTypes.Length * 4));
        var node = new FunctionPointerTypeNode(signature);
        ObserveMaterialization(node.EstimatedRenderedLength);
        return node;
    }

    public TypeNode GetModifiedType(TypeNode modifier, TypeNode unmodifiedType, bool isRequired)
    {
        _beforeMaterialize?.Invoke(16);
        var node = new ModifiedTypeNode(modifier, unmodifiedType, isRequired);
        ObserveMaterialization(node.EstimatedRenderedLength);
        return node;
    }

    public TypeNode GetPinnedType(TypeNode elementType)
    {
        _beforeMaterialize?.Invoke(16);
        var node = new PassthroughTypeNode(elementType);
        ObserveMaterialization(node.EstimatedRenderedLength);
        return node;
    }

    void ObserveMaterialization(long units)
        => _beforeMaterialize?.Invoke((int)Math.Min(units, int.MaxValue));
}
