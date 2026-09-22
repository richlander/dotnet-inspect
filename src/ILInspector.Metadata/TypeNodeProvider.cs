using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>
/// Decodes type signatures from metadata into <see cref="TypeNode"/> trees.
/// The tree can then have nullability annotations applied before rendering.
/// </summary>
internal sealed class TypeNodeProvider : ISignatureTypeProvider<TypeNode, GenericContext?>
{
    public static TypeNodeProvider Instance { get; } = new();
    // Signature helpers create separate providers, so local-definition work is
    // shared at the reader boundary rather than repeated per decoded member.
    static readonly ConditionalWeakTable<
        MetadataReader,
        ReaderTypeDefinitionIndexCache> LocalTypeDefinitions = new();
    static readonly ConditionalWeakTable<
        TypeNodeProvider,
        PreMaterializationCallbacks> PreMaterialization = new();
    readonly Action<string>? _beforeRetain;
    readonly Action<int>? _beforeMaterialize;
    readonly Action? _beforeCreateNode;
    readonly Action<MetadataReader, TypeSpecificationHandle>?
        _beforeTypeSpecificationDecode;
    readonly bool _retainExactScope;
    readonly Func<
        MetadataReader,
        TypeReferenceHandle,
        MetadataTypeNameParts,
        MetadataTypeNameParts?>? _enrichTypeReferenceName;
    readonly Func<
        MetadataReader,
        TypeReferenceHandle,
        ApiAssemblyIdentity?,
        MetadataTypeScopeDescriptor?>? _resolveTypeReferenceScope;
    readonly Action<MetadataReader, TypeDefinitionHandle>?
        _beforeTypeDefinitionResolve;
    readonly Action<MetadataReader, TypeReferenceHandle>?
        _beforeTypeReferenceResolve;
    readonly Func<
        MetadataReader,
        TypeSpecificationHandle,
        IDisposable?>? _typeSpecificationScope;
    readonly Action<RelationshipTraversalRejection>?
        _nameBudgetRejected;
    readonly ConditionalWeakTable<MetadataReader, ReaderNameCache> _readerNames = new();

    public TypeNodeProvider(
        Action<string>? beforeRetain = null,
        Action<int>? beforeMaterialize = null,
        Action<int>? beforeRetainMaterialize = null,
        Action? beforeNameMaterialize = null,
        Action? beforePublicKeyMaterialize = null,
        Action? beforeCreateNode = null,
        Action<MetadataReader, TypeSpecificationHandle>?
            beforeTypeSpecificationDecode = null,
        bool retainExactScope = false,
        Func<
            MetadataReader,
            TypeReferenceHandle,
            MetadataTypeNameParts,
            MetadataTypeNameParts?>? enrichTypeReferenceName = null,
        Func<
            MetadataReader,
            TypeReferenceHandle,
            ApiAssemblyIdentity?,
            MetadataTypeScopeDescriptor?>? resolveTypeReferenceScope = null,
        Action<MetadataReader, TypeDefinitionHandle>?
            beforeTypeDefinitionResolve = null,
        Action<MetadataReader, TypeReferenceHandle>?
            beforeTypeReferenceResolve = null,
        Func<
            MetadataReader,
            TypeSpecificationHandle,
            IDisposable?>? typeSpecificationScope = null,
        Action<RelationshipTraversalRejection>?
            nameBudgetRejected = null)
    {
        _beforeRetain = beforeRetain;
        _beforeMaterialize = beforeMaterialize;
        if (beforeRetainMaterialize is not null
            || beforeNameMaterialize is not null
            || beforePublicKeyMaterialize is not null)
        {
            PreMaterialization.Add(
                this,
                new(
                    beforeRetainMaterialize,
                    beforeNameMaterialize,
                    beforePublicKeyMaterialize));
        }
        _beforeCreateNode = beforeCreateNode;
        _beforeTypeSpecificationDecode =
            beforeTypeSpecificationDecode;
        _retainExactScope = retainExactScope;
        _enrichTypeReferenceName = enrichTypeReferenceName;
        _resolveTypeReferenceScope = resolveTypeReferenceScope;
        _beforeTypeDefinitionResolve = beforeTypeDefinitionResolve;
        _beforeTypeReferenceResolve = beforeTypeReferenceResolve;
        _typeSpecificationScope = typeSpecificationScope;
        _nameBudgetRejected = nameBudgetRejected;
    }

    // Delegate to existing SignatureDecoder for name resolution to avoid duplication.
    private static readonly SignatureDecoder NameDecoder = SignatureDecoder.Instance;

    public TypeNode GetPrimitiveType(PrimitiveTypeCode typeCode)
    {
        _beforeMaterialize?.Invoke(16);
        _beforeCreateNode?.Invoke();
        string name = NameDecoder.GetPrimitiveType(typeCode);
        _beforeRetain?.Invoke(name);
        bool isRef = typeCode is PrimitiveTypeCode.String or PrimitiveTypeCode.Object;
        return new PrimitiveTypeNode(name, isRef);
    }

    public TypeNode GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        _beforeTypeDefinitionResolve?.Invoke(reader, handle);
        if (TryGetCached(reader, handle, out NamedTypeRead? cached))
        {
            ReplayMaterializationWork(cached.MaterializationWork);
            return ReadNamedType(cached, rawTypeKind);
        }

        int materializationWork = 0;
        PreMaterialization.TryGetValue(
            this,
            out PreMaterializationCallbacks? callbacks);
        Action<int>? observe = _beforeMaterialize is null
            ? null
            : amount =>
            {
                materializationWork = (int)Math.Min(
                    int.MaxValue,
                    (long)materializationWork + amount);
                _beforeMaterialize(amount);
            };
        Action<int>? preflightRetained =
            CreateRetainedMaterializationPreflight(callbacks);
        Action<int>? observeName = callbacks is null
            ? observe
            : CreateNameMaterializationObserver(
                observe,
                preflightRetained);
        bool resolved = TypeResolver.TryGetTypeNameFromDefinition(
            reader,
            handle,
            observeName,
            out string? name,
            out RelationshipTraversalRejection? rejection);
        MetadataTypeNameParts? metadataName = resolved
            ? WithTrustedArity(reader, handle, TypeResolver.GetTypeNamePartsFromDefinition(reader, handle))
            : null;
        ApiAssemblyIdentity? assemblyIdentity =
            CurrentAssemblyIdentity(
                reader,
                observe,
                preflightRetained,
                callbacks?.BeforeNameMaterialize,
                callbacks?.BeforePublicKeyMaterialize);
        var read = new NamedTypeRead(
            resolved,
            name,
            rejection,
            metadataName,
            assemblyIdentity,
            _retainExactScope
                ? CurrentScopeIdentity(
                    reader,
                    assemblyIdentity,
                    observe,
                    preflightRetained,
                    callbacks?.BeforeNameMaterialize)
                : null,
            materializationWork);
        Cache(reader, handle, read);
        return ReadNamedType(read, rawTypeKind);
    }

    /// <summary>
    /// Attaches metadata-verified, per-segment introduced generic-parameter
    /// counts along <paramref name="handle"/>'s declaring chain, so nested-type
    /// rendering can recover a segment's true arity even when its raw name lacks
    /// a canonical <c>`N</c> suffix (#4507). Scoped to <see cref="TypeDefinitionHandle"/>
    /// (a local declaration). A <see cref="TypeReferenceHandle"/> scoped to this
    /// module first resolves its exact local TypeDef and then uses this same
    /// evidence; an external reference has no equivalent trusted source without
    /// loading the referenced assembly, which this product does not do.
    /// Malformed generic-parameter ownership propagates as a rejection instead
    /// of producing an ordinary raw-name rendering, gated by
    /// <c>NestedGenericSignature_WithMalformedOwnership_IsReportedAsInspectionFailure</c>.
    /// </summary>
    static MetadataTypeNameParts WithTrustedArity(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        MetadataTypeNameParts metadataName)
        => metadataName.WithIntroducedTypeParameterCounts(
            MetadataDeclarationQuery.GetIntroducedTypeParameterCounts(
                reader,
                handle));

    public TypeNode GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        _beforeTypeReferenceResolve?.Invoke(reader, handle);
        if (TryGetCached(reader, handle, out NamedTypeRead? cached))
        {
            ReplayMaterializationWork(cached.MaterializationWork);
            return ReadNamedType(cached, rawTypeKind);
        }

        int materializationWork = 0;
        PreMaterialization.TryGetValue(
            this,
            out PreMaterializationCallbacks? callbacks);
        Action<int>? observe = _beforeMaterialize is null
            ? null
            : amount =>
            {
                materializationWork = (int)Math.Min(
                    int.MaxValue,
                    (long)materializationWork + amount);
                _beforeMaterialize(amount);
            };
        Action<int>? preflightRetained =
            CreateRetainedMaterializationPreflight(callbacks);
        Action<int>? observeName = callbacks is null
            ? observe
            : CreateNameMaterializationObserver(
                observe,
                preflightRetained);
        bool resolved = TypeResolver.TryGetTypeNameFromReference(
            reader,
            handle,
            observeName,
            out string? name,
            out RelationshipTraversalRejection? rejection);
        MetadataTypeNameParts? metadataName = resolved
            ? WithTrustedLocalReferenceArity(
                reader,
                handle,
                TypeResolver.GetTypeNamePartsFromReference(reader, handle))
            : null;
        ApiAssemblyIdentity? assemblyIdentity =
            ReferencedAssemblyIdentity(
                reader,
                handle,
                observe,
                preflightRetained,
                callbacks?.BeforeNameMaterialize,
                callbacks?.BeforePublicKeyMaterialize);
        var read = new NamedTypeRead(
            resolved,
            name,
            rejection,
            metadataName,
            assemblyIdentity,
            _retainExactScope
                ? _resolveTypeReferenceScope is null
                    ? ReferencedScopeIdentity(
                        reader,
                        handle,
                        assemblyIdentity,
                        observe,
                        preflightRetained,
                        callbacks?.BeforeNameMaterialize)
                    : _resolveTypeReferenceScope(
                        reader,
                        handle,
                        assemblyIdentity)
                : null,
            materializationWork);
        Cache(reader, handle, read);
        return ReadNamedType(read, rawTypeKind);
    }

    MetadataTypeNameParts? WithTrustedLocalReferenceArity(
        MetadataReader reader,
        TypeReferenceHandle handle,
        MetadataTypeNameParts metadataName)
    {
        if (_enrichTypeReferenceName is not null)
        {
            return _enrichTypeReferenceName(
                reader,
                handle,
                metadataName);
        }

        Span<TypeReferenceHandle> chain =
            stackalloc TypeReferenceHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal
                .TryWalkTypeReferenceResolutionScope(
                    reader,
                    handle,
                    chain,
                    out _,
                    out EntityHandle terminal,
                    out _))
        {
            return null;
        }
        if (terminal.Kind != HandleKind.ModuleDefinition)
            return terminal.IsNil ? null : metadataName;

        if (MetadataTypeDefinitionName.Create(
                metadataName.Namespace,
                [.. metadataName.Segments])
            is not MetadataTypeDefinitionNameResult.Valid valid)
        {
            return null;
        }
        MetadataTypeDefinitionIndex definitions =
            LocalTypeDefinitions.GetValue(
                reader,
                static _ => new ReaderTypeDefinitionIndexCache())
            .GetOrCreate(reader, _beforeMaterialize);
        if (!definitions.TryGetUniqueDefinition(
                valid.Name,
                out TypeDefinitionHandle definition))
        {
            return null;
        }

        return WithTrustedArity(
            reader,
            definition,
            metadataName);
    }

    TypeNode ReadNamedType(
        NamedTypeRead read,
        byte rawTypeKind)
    {
        if (read.Resolved)
        {
            _beforeCreateNode?.Invoke();
            _beforeRetain?.Invoke(read.Name!);
            RetainAssemblyIdentity(read.AssemblyIdentity);
            bool isRef = rawTypeKind != 0x11; // 0x11 = ELEMENT_TYPE_VALUETYPE
            return new NamedTypeNode(
                read.Name!,
                isRef,
                read.MetadataName,
                read.AssemblyIdentity,
                read.ExactScope);
        }

        ArgumentNullException.ThrowIfNull(read.Rejection);
        if (read.Rejection.Kind == RelationshipTraversalRejectionKind.NameBudget)
        {
            _nameBudgetRejected?.Invoke(read.Rejection);
            return new DegradedTypeNode();
        }

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
        ApiAssemblyIdentity? AssemblyIdentity,
        MetadataTypeScopeDescriptor? ExactScope,
        int MaterializationWork);

    static ApiAssemblyIdentity? CurrentAssemblyIdentity(
        MetadataReader reader,
        Action<int>? beforeMaterialize,
        Action<int>? beforeRetainMaterialize,
        Action? beforeNameMaterialize,
        Action? beforePublicKeyMaterialize) =>
        reader.IsAssembly
            ? ApiAssemblyIdentity.FromDefinition(
                reader,
                beforeMaterialize,
                beforeRetainMaterialize,
                beforeNameMaterialize,
                beforePublicKeyMaterialize)
            : null;

    static ApiAssemblyIdentity? ReferencedAssemblyIdentity(
        MetadataReader reader,
        TypeReferenceHandle handle,
        Action<int>? beforeMaterialize,
        Action<int>? beforeRetainMaterialize,
        Action? beforeNameMaterialize,
        Action? beforePublicKeyMaterialize)
    {
        Span<TypeReferenceHandle> chain =
            stackalloc TypeReferenceHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal
                .TryWalkTypeReferenceResolutionScope(
                    reader,
                    handle,
                    chain,
                    out _,
                    out EntityHandle terminal,
                    out _))
        {
            return null;
        }

        return terminal.Kind switch
        {
            HandleKind.AssemblyReference =>
                ApiAssemblyIdentity.FromReference(
                    reader,
                    (AssemblyReferenceHandle)terminal,
                    beforeMaterialize,
                    beforeRetainMaterialize,
                    beforeNameMaterialize,
                    beforePublicKeyMaterialize),
            HandleKind.ModuleDefinition or HandleKind.ModuleReference =>
                CurrentAssemblyIdentity(
                    reader,
                    beforeMaterialize,
                    beforeRetainMaterialize,
                    beforeNameMaterialize,
                    beforePublicKeyMaterialize),
            _ when terminal.IsNil =>
                CurrentAssemblyIdentity(
                    reader,
                    beforeMaterialize,
                    beforeRetainMaterialize,
                    beforeNameMaterialize,
                    beforePublicKeyMaterialize),
            _ => null,
        };
    }

    static MetadataTypeScopeDescriptor CurrentScopeIdentity(
        MetadataReader reader,
        ApiAssemblyIdentity? assemblyIdentity,
        Action<int>? beforeMaterialize,
        Action<int>? beforeRetainMaterialize,
        Action? beforeNameMaterialize)
    {
        ModuleDefinition module = reader.GetModuleDefinition();
        return new(
            MetadataTypeScopeKind.CurrentModule,
            MetadataModuleIdentity.ReadVersionId(reader),
            ReadProjectedString(
                reader,
                module.Name,
                beforeMaterialize,
                beforeRetainMaterialize,
                beforeNameMaterialize),
            reader.IsAssembly
                ? ProjectAssemblyIdentity(assemblyIdentity)
                : null);
    }

    static MetadataTypeScopeDescriptor? ReferencedScopeIdentity(
        MetadataReader reader,
        TypeReferenceHandle handle,
        ApiAssemblyIdentity? assemblyIdentity,
        Action<int>? beforeMaterialize,
        Action<int>? beforeRetainMaterialize,
        Action? beforeNameMaterialize)
    {
        Span<TypeReferenceHandle> chain =
            stackalloc TypeReferenceHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal
                .TryWalkTypeReferenceResolutionScope(
                    reader,
                    handle,
                    chain,
                    out _,
                    out EntityHandle terminal,
                    out _))
        {
            return null;
        }

        return terminal.Kind switch
        {
            HandleKind.AssemblyReference =>
                new MetadataTypeScopeDescriptor(
                    MetadataTypeScopeKind.AssemblyReference,
                    Guid.Empty,
                    ModuleName: null,
                    ProjectAssemblyIdentity(assemblyIdentity)),
            HandleKind.ModuleReference =>
                new MetadataTypeScopeDescriptor(
                    MetadataTypeScopeKind.ModuleReference,
                    Guid.Empty,
                    ReadProjectedString(
                        reader,
                        reader.GetModuleReference(
                            (ModuleReferenceHandle)terminal).Name,
                        beforeMaterialize,
                        beforeRetainMaterialize,
                        beforeNameMaterialize),
                    reader.IsAssembly
                        ? ProjectAssemblyIdentity(assemblyIdentity)
                        : null),
            HandleKind.ModuleDefinition =>
                CurrentScopeIdentity(
                    reader,
                    assemblyIdentity,
                    beforeMaterialize,
                    beforeRetainMaterialize,
                    beforeNameMaterialize),
            _ when terminal.IsNil =>
                CurrentScopeIdentity(
                    reader,
                    assemblyIdentity,
                    beforeMaterialize,
                    beforeRetainMaterialize,
                    beforeNameMaterialize),
            _ => null,
        };
    }

    Action<int>? CreateNameMaterializationObserver(
        Action<int>? beforeMaterialize,
        Action<int>? beforeRetainMaterialize)
    {
        PreMaterialization.TryGetValue(
            this,
            out PreMaterializationCallbacks? callbacks);
        if (beforeMaterialize is null
            && beforeRetainMaterialize is null
            && callbacks?.BeforeNameMaterialize is null)
        {
            return null;
        }

        return amount =>
        {
            if (amount == 0)
                return;

            beforeRetainMaterialize?.Invoke(amount);
            beforeMaterialize?.Invoke(amount);
            callbacks?.BeforeNameMaterialize?.Invoke();
        };
    }

    static Action<int>? CreateRetainedMaterializationPreflight(
        PreMaterializationCallbacks? callbacks)
    {
        if (callbacks?.BeforeRetainMaterialize is not { } before)
            return null;

        return new RetainedMaterializationPreflight(before).Observe;
    }

    static string ReadProjectedString(
        MetadataReader reader,
        StringHandle handle,
        Action<int>? beforeMaterialize,
        Action<int>? beforeRetainMaterialize,
        Action? beforeNameMaterialize)
    {
        int length = reader.GetBlobReader(handle).Length;
        beforeRetainMaterialize?.Invoke(length);
        beforeMaterialize?.Invoke(length);
        beforeNameMaterialize?.Invoke();
        return reader.GetString(handle);
    }

    static AssemblyReferenceIdentity ProjectAssemblyIdentity(
        ApiAssemblyIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new(
            identity.Name,
            identity.Version,
            identity.Culture,
            identity.PublicKeyToken);
    }

    sealed record PreMaterializationCallbacks(
        Action<int>? BeforeRetainMaterialize,
        Action? BeforeNameMaterialize,
        Action? BeforePublicKeyMaterialize);

    sealed class RetainedMaterializationPreflight(
        Action<int> beforeRetainMaterialize)
    {
        int _cumulativeWork;

        internal void Observe(int amount)
        {
            _cumulativeWork = (int)Math.Min(
                int.MaxValue,
                (long)_cumulativeWork + amount);
            beforeRetainMaterialize(_cumulativeWork);
        }
    }

    void RetainAssemblyIdentity(ApiAssemblyIdentity? identity)
    {
        if (identity is null)
            return;

        _beforeRetain?.Invoke(identity.Name);
        if (identity.Culture is not null)
            _beforeRetain?.Invoke(identity.Culture);
        if (identity.PublicKeyToken is not null)
            _beforeRetain?.Invoke(identity.PublicKeyToken);
    }

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
            characters +=
                read.AssemblyIdentity?.RetainedCharacterCount ?? 0;
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

    sealed class ReaderTypeDefinitionIndexCache
    {
        readonly object _gate = new();
        MetadataTypeDefinitionIndex? _index;

        internal MetadataTypeDefinitionIndex GetOrCreate(
            MetadataReader reader,
            Action<int>? beforeMaterialize)
        {
            lock (_gate)
            {
                return _index ??=
                    MetadataTypeDefinitionIndex.Create(
                        reader,
                        definitionVisited: null,
                        beforeMaterialize: beforeMaterialize);
            }
        }
    }

    public TypeNode GetTypeFromSpecification(MetadataReader reader, GenericContext? context, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        using IDisposable? accountingScope =
            _typeSpecificationScope?.Invoke(reader, handle);
        _beforeTypeSpecificationDecode?.Invoke(reader, handle);
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
        _beforeCreateNode?.Invoke();
        var node = new SZArrayTypeNode(elementType);
        ObserveMaterialization(node.EstimatedRenderedLength);
        return node;
    }

    public TypeNode GetArrayType(TypeNode elementType, ArrayShape shape)
    {
        ObserveMaterialization(16L + Math.Max(shape.Rank, 0));
        _beforeCreateNode?.Invoke();
        var node = new MDArrayTypeNode(
            elementType,
            shape.Rank,
            shape.Sizes,
            shape.LowerBounds);
        ObserveMaterialization(node.EstimatedRenderedLength);
        return node;
    }

    public TypeNode GetByReferenceType(TypeNode elementType)
    {
        _beforeMaterialize?.Invoke(16);
        _beforeCreateNode?.Invoke();
        var node = new ByRefTypeNode(elementType);
        ObserveMaterialization(node.EstimatedRenderedLength);
        return node;
    }

    public TypeNode GetPointerType(TypeNode elementType)
    {
        _beforeMaterialize?.Invoke(16);
        _beforeCreateNode?.Invoke();
        var node = new PointerTypeNode(elementType);
        ObserveMaterialization(node.EstimatedRenderedLength);
        return node;
    }

    public TypeNode GetGenericInstantiation(TypeNode genericType, ImmutableArray<TypeNode> typeArguments)
    {
        _beforeMaterialize?.Invoke(checked(16 + typeArguments.Length * 4));
        _beforeCreateNode?.Invoke();
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
                metadataName: metadataName,
                definitionAssemblyIdentity:
                ((NamedTypeNode)genericType).AssemblyIdentity,
                exactScope: genericType.ExactScope);
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
                    typeArguments,
                    structuralMetadataName: rawName,
                    definitionAssemblyIdentity:
                        (genericType as NamedTypeNode)?.AssemblyIdentity,
                    exactScope: genericType.ExactScope);
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
                    genericType.IsDegraded,
                    structuralMetadataName: rawName,
                    definitionAssemblyIdentity:
                        (genericType as NamedTypeNode)?.AssemblyIdentity,
                    exactScope: genericType.ExactScope);
            }
        }

        ObserveMaterialization(node.EstimatedRenderedLength);
        return node;
    }

    public TypeNode GetGenericMethodParameter(GenericContext? context, int index)
    {
        _beforeMaterialize?.Invoke(16);
        _beforeCreateNode?.Invoke();
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
        _beforeCreateNode?.Invoke();
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
        _beforeCreateNode?.Invoke();
        var node = new FunctionPointerTypeNode(signature);
        ObserveMaterialization(node.EstimatedRenderedLength);
        return node;
    }

    public TypeNode GetModifiedType(TypeNode modifier, TypeNode unmodifiedType, bool isRequired)
    {
        _beforeMaterialize?.Invoke(16);
        _beforeCreateNode?.Invoke();
        var node = new ModifiedTypeNode(modifier, unmodifiedType, isRequired);
        ObserveMaterialization(node.EstimatedRenderedLength);
        return node;
    }

    public TypeNode GetPinnedType(TypeNode elementType)
    {
        _beforeMaterialize?.Invoke(16);
        _beforeCreateNode?.Invoke();
        var node = new PinnedTypeNode(elementType);
        ObserveMaterialization(node.EstimatedRenderedLength);
        return node;
    }

    void ObserveMaterialization(long units)
        => _beforeMaterialize?.Invoke((int)Math.Min(units, int.MaxValue));
}
