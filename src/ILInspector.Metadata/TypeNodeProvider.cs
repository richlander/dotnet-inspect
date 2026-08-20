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
    readonly Action<string>? _beforeRetain;
    readonly Action<int>? _beforeMaterialize;
    readonly bool _scopeNamedTypeIdentity;
    readonly AssemblyReferenceProjectionCache?
        _assemblyReferenceProjection;

    public TypeNodeProvider(
        Action<string>? beforeRetain = null,
        Action<int>? beforeMaterialize = null,
        bool scopeNamedTypeIdentity = false,
        AssemblyReferenceProjectionCache?
            assemblyReferenceProjection = null)
    {
        _beforeRetain = beforeRetain;
        _beforeMaterialize = beforeMaterialize;
        _scopeNamedTypeIdentity = scopeNamedTypeIdentity;
        _assemblyReferenceProjection =
            assemblyReferenceProjection;
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
        => ReadNamedType(
            TypeResolver.TryGetTypeNameFromDefinition(
                reader,
                handle,
                _beforeMaterialize,
                out string? name,
                out var rejection),
            name,
            rejection,
            rawTypeKind,
            _scopeNamedTypeIdentity
                ? ScopedNamedTypeIdentity(
                    reader,
                    handle,
                    name,
                    rawTypeKind)
                : null);

    public TypeNode GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        => ReadNamedType(
            TypeResolver.TryGetTypeNameFromReference(
                reader,
                handle,
                _beforeMaterialize,
                out string? name,
                out var rejection),
            name,
            rejection,
            rawTypeKind,
            _scopeNamedTypeIdentity
                ? ScopedNamedTypeIdentity(
                    reader,
                    handle,
                    name,
                    rawTypeKind)
                : null);

    TypeNode ReadNamedType(
        bool resolved,
        string? name,
        RelationshipTraversalRejection? rejection,
        byte rawTypeKind,
        string? structuralIdentity)
    {
        if (resolved)
        {
            _beforeRetain?.Invoke(name!);
            bool isRef = rawTypeKind != 0x11; // 0x11 = ELEMENT_TYPE_VALUETYPE
            return new NamedTypeNode(
                name!,
                isRef,
                structuralIdentity);
        }

        ArgumentNullException.ThrowIfNull(rejection);
        if (rejection.Kind == RelationshipTraversalRejectionKind.NameBudget)
            return new DegradedTypeNode();

        throw new BadImageFormatException(
            $"Metadata relationship traversal rejected ({rejection.Kind}): "
            + rejection.Detail);
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
        string structuralBaseIdentity =
            genericType.StructuralIdentity();
        var backtickIndex = rawName.IndexOf('`');
        GenericTypeNode node;
        if (backtickIndex < 0)
        {
            node = new GenericTypeNode(
                rawName,
                genericType.IsReferenceType,
                typeArguments,
                metadataName: rawName,
                structuralBaseIdentity:
                    structuralBaseIdentity);
        }
        else
        {
            // Strip only the arity digits at the first backtick, keeping any trailing
            // nested-type segment (Dictionary`2.Enumerator -> base "Dictionary",
            // suffix ".Enumerator") so the instantiation renders Dictionary<…>.Enumerator
            // rather than collapsing to Dictionary<…>.
            var baseName = rawName[..backtickIndex];
            var suffixStart = backtickIndex + 1;
            while (suffixStart < rawName.Length && char.IsDigit(rawName[suffixStart]))
                suffixStart++;
            var nestedSuffix = TypeResolver.FormatDisplayName(rawName[suffixStart..]);
            node = new GenericTypeNode(
                baseName,
                genericType.IsReferenceType,
                typeArguments,
                nestedSuffix,
                genericType.IsDegraded,
                rawName,
                structuralBaseIdentity);
        }

        ObserveMaterialization(node.EstimatedRenderedLength);
        return node;
    }

    string? ScopedNamedTypeIdentity(
        MetadataReader reader,
        EntityHandle handle,
        string? name,
        byte rawTypeKind)
    {
        if (name is null)
            return null;

        string? scope = handle.Kind switch
        {
            HandleKind.TypeDefinition => "current",
            HandleKind.TypeReference =>
                TypeReferenceScopeIdentity(
                    reader,
                    (TypeReferenceHandle)handle),
            _ => null,
        };
        return scope is null
            ? null
            : StructuralSegment(
                "named",
                rawTypeKind.ToString("x2"),
                scope,
                name);
    }

    string? TypeReferenceScopeIdentity(
        MetadataReader reader,
        TypeReferenceHandle handle)
    {
        Span<TypeReferenceHandle> chain =
            stackalloc TypeReferenceHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal
                .TryWalkTypeReferenceResolutionScope(
                    reader,
                    handle,
                    chain,
                    out int consumed,
                    out EntityHandle terminal,
                    out _))
        {
            return null;
        }
        _beforeMaterialize?.Invoke(consumed);
        return terminal.Kind switch
        {
            HandleKind.AssemblyReference =>
                AssemblyScopeIdentity(
                    ReadAssemblyReferenceIdentity(
                        reader,
                        (AssemblyReferenceHandle)terminal)),
            HandleKind.ModuleDefinition => "current",
            HandleKind.ModuleReference =>
                StructuralSegment(
                    "module",
                    reader.GetString(
                        reader.GetModuleReference(
                            (ModuleReferenceHandle)terminal).Name)),
            _ when terminal.IsNil => "current",
            _ => null,
        };
    }

    AssemblyReferenceIdentity ReadAssemblyReferenceIdentity(
        MetadataReader reader,
        AssemblyReferenceHandle handle)
    {
        if (_beforeMaterialize is not null)
        {
            var reference =
                reader.GetAssemblyReference(handle);
            _beforeMaterialize(
                checked(
                    reader.GetBlobReader(reference.Name).Length
                    + reader.GetBlobReader(
                        reference.Culture).Length
                    + reader.GetBlobReader(
                        reference.PublicKeyOrToken).Length));
        }
        return _assemblyReferenceProjection is null
            ? AssemblyReferenceIdentity.From(reader, handle)
            : AssemblyReferenceIdentity.From(
                handle,
                _assemblyReferenceProjection);
    }

    static string AssemblyScopeIdentity(
        AssemblyReferenceIdentity assembly)
        => StructuralSegment(
            "assembly",
            assembly.Name.ToUpperInvariant(),
            assembly.Version?.ToString() ?? "",
            NormalizeCulture(assembly.Culture),
            (assembly.PublicKeyToken ?? "").ToUpperInvariant());

    static string NormalizeCulture(string? culture)
        => string.IsNullOrEmpty(culture)
            || culture.Equals(
                "neutral",
                StringComparison.OrdinalIgnoreCase)
                ? ""
                : culture.ToUpperInvariant();

    static string StructuralSegment(
        string kind,
        params string[] values)
        => kind
            + string.Concat(values.Select(value =>
                $"{{{value.Length}:{value}}}"));

    public TypeNode GetGenericMethodParameter(GenericContext? context, int index)
    {
        _beforeMaterialize?.Invoke(16);
        string name = NameDecoder.GetGenericMethodParameter(context, index);
        _beforeRetain?.Invoke(name);
        return new GenericParameterNode(
            name,
            hasValueTypeConstraint: context?.HasMethodParameterValueTypeConstraint(index) == true,
            isMethodParameter: true,
            index);
    }

    public TypeNode GetGenericTypeParameter(GenericContext? context, int index)
    {
        _beforeMaterialize?.Invoke(16);
        string name = NameDecoder.GetGenericTypeParameter(context, index);
        _beforeRetain?.Invoke(name);
        return new GenericParameterNode(
            name,
            hasValueTypeConstraint: context?.HasTypeParameterValueTypeConstraint(index) == true,
            isMethodParameter: false,
            index);
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
        var node = new PinnedTypeNode(elementType);
        ObserveMaterialization(node.EstimatedRenderedLength);
        return node;
    }

    void ObserveMaterialization(long units)
        => _beforeMaterialize?.Invoke((int)Math.Min(units, int.MaxValue));
}
