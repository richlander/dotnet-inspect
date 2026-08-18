using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Text;

namespace ILInspector.Metadata;

/// <summary>
/// Resolves type names from metadata handles.
/// </summary>
public static class TypeResolver
{
    /// <summary>
    /// Strictly resolves a type name while preserving absence and typed
    /// relationship or signature rejection as distinct outcomes.
    /// </summary>
    public static MetadataTypeNameResult ResolveTypeName(
        MetadataReader reader,
        EntityHandle handle,
        GenericContext? context = null)
    {
        if (handle.IsNil)
            return new MetadataTypeNameResult.Absent();

        return handle.Kind switch
        {
            HandleKind.TypeReference => FromRelationship(
                ResolveTypeNameFromReference(reader, (TypeReferenceHandle)handle)),
            HandleKind.TypeDefinition => FromRelationship(
                ResolveTypeNameFromDefinition(reader, (TypeDefinitionHandle)handle)),
            HandleKind.TypeSpecification => FromSignature(
                DecodeTypeNameFromSpecification(
                    reader,
                    (TypeSpecificationHandle)handle,
                    context),
                (TypeSpecificationHandle)handle),
            _ => new MetadataTypeNameResult.Absent(),
        };
    }

    /// <summary>
    /// Gets the fully qualified type name from an entity handle.
    /// Handles TypeReference, TypeDefinition, and TypeSpecification.
    /// </summary>
    public static string? GetTypeName(MetadataReader reader, EntityHandle handle, GenericContext? context = null)
        => GetTypeName(reader, handle, context, beforeMaterialize: null);

    internal static string? GetTypeName(
        MetadataReader reader,
        EntityHandle handle,
        GenericContext? context,
        Action<int>? beforeMaterialize)
    {
        if (handle.IsNil)
            return null;

        return handle.Kind switch
        {
            HandleKind.TypeReference => GetTypeNameFromReference(
                reader, (TypeReferenceHandle)handle, beforeMaterialize),
            HandleKind.TypeDefinition => GetTypeNameFromDefinition(
                reader, (TypeDefinitionHandle)handle, beforeMaterialize),
            HandleKind.TypeSpecification => DecodeTypeNameFromSpecification(
                reader,
                (TypeSpecificationHandle)handle,
                context,
                beforeMaterialize,
                enforceCharacterBudget: false).TryGetValue(out var name)
                    ? name
                    : null,
            _ => null
        };
    }

    /// <summary>
    /// Gets the type name from a TypeReference handle, qualifying a nested type
    /// through its declaring type (<c>Outer.Inner</c>) - a nested
    /// <see cref="TypeReference"/> carries an empty namespace and a leaf name with
    /// its enclosing type as the resolution scope, so a raw namespace+name would
    /// drop the qualifier (rendering <c>ImmutableArray`1+Builder</c> as a bare
    /// <c>Builder</c>). Mirrors <see cref="GetFullName(MetadataReader, TypeDefinition)"/>.
    /// </summary>
    public static string GetTypeNameFromReference(MetadataReader reader, TypeReferenceHandle handle)
        => GetTypeNameFromReference(reader, handle, beforeMaterialize: null);

    public static string GetTypeNameFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        Action<int>? beforeMaterialize)
        => TryGetTypeNameFromReference(
            reader,
            handle,
            beforeMaterialize,
            out string? name,
            out var rejection,
            enforceCharacterBudget: false)
            ? name
            : throw RejectedName(rejection!);

    internal static bool TryGetTypeNameFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        Action<int>? beforeMaterialize,
        [NotNullWhen(true)] out string? name,
        out RelationshipTraversalRejection? rejection,
        bool enforceCharacterBudget = true)
    {
        ObserveTypeReferenceName(reader, handle, beforeMaterialize);
        try
        {
            var typeRef = reader.GetTypeReference(handle);
            if (typeRef.ResolutionScope.Kind != HandleKind.TypeReference)
            {
                return CompleteLeafName(
                    reader,
                    typeRef.Namespace,
                    typeRef.Name,
                    handle,
                    consumedNodes: 1,
                    enforceCharacterBudget,
                    out _).TryComplete(out name, out rejection);
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            name = null;
            rejection = MalformedRejection(ex, handle, consumedNodes: 1);
            return false;
        }

        return ResolveTypeNameFromReference(reader, handle, enforceCharacterBudget)
            .TryComplete(out name, out rejection);
    }

    /// <summary>
    /// Resolves a TypeReference name through a bounded, cycle-aware
    /// resolution-scope walk.
    /// </summary>
    public static RelationshipTraversalResult<string> ResolveTypeNameFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle)
        => ResolveTypeNameFromReference(
            reader,
            handle,
            enforceCharacterBudget: true);

    public static RelationshipTraversalResult<string> ResolveTypeNameFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        bool enforceCharacterBudget)
        => ResolveTypeNameFromReference(
            reader,
            handle,
            enforceCharacterBudget,
            out _);

    static RelationshipTraversalResult<string> ResolveTypeNameFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        bool enforceCharacterBudget,
        out MetadataTypeNameBudget budget)
    {
        try
        {
            var typeRef = reader.GetTypeReference(handle);
            if (typeRef.ResolutionScope.Kind != HandleKind.TypeReference)
            {
                return CompleteLeafName(
                    reader,
                    typeRef.Namespace,
                    typeRef.Name,
                    handle,
                    consumedNodes: 1,
                    enforceCharacterBudget,
                    out budget);
            }
        }
        catch (BadImageFormatException ex)
        {
            budget = default;
            return Malformed<string>(ex, handle, consumedNodes: 1);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            budget = default;
            return Malformed<string>(ex, handle, consumedNodes: 1);
        }

        return FormatChain(
            reader,
            MetadataRelationshipTraversal.WalkTypeReferenceResolutionScope(reader, handle),
            current =>
            {
                var typeRef = reader.GetTypeReference(current);
                return (typeRef.Namespace, typeRef.Name);
            },
            static current => current,
            enforceCharacterBudget,
            out budget);
    }

    internal static MetadataTypeNameParts GetTypeNamePartsFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle)
        => ResolveTypeNamePartsFromReference(reader, handle).GetValueOrThrow();

    internal static RelationshipTraversalResult<MetadataTypeNameParts>
        ResolveTypeNamePartsFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            bool enforceCharacterBudget = true)
        => FormatNameParts(
            reader,
            MetadataRelationshipTraversal.WalkTypeReferenceResolutionScope(reader, handle),
            current =>
            {
                var typeRef = reader.GetTypeReference(current);
                return (typeRef.Namespace, typeRef.Name);
            },
            static current => current,
            enforceCharacterBudget);

    /// <summary>
    /// Gets the type name from a TypeDefinition handle.
    /// </summary>
    public static string GetTypeNameFromDefinition(MetadataReader reader, TypeDefinitionHandle handle)
        => GetTypeNameFromDefinition(reader, handle, beforeMaterialize: null);

    public static string GetTypeNameFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        Action<int>? beforeMaterialize)
        => TryGetTypeNameFromDefinition(
            reader,
            handle,
            beforeMaterialize,
            out string? name,
            out var rejection,
            enforceCharacterBudget: false)
            ? name
            : throw RejectedName(rejection!);

    internal static bool TryGetTypeNameFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        Action<int>? beforeMaterialize,
        [NotNullWhen(true)] out string? name,
        out RelationshipTraversalRejection? rejection,
        bool enforceCharacterBudget = true)
    {
        ObserveTypeDefinitionName(reader, handle, beforeMaterialize);
        try
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if (typeDef.GetDeclaringType().IsNil)
            {
                return CompleteLeafName(
                    reader,
                    typeDef.Namespace,
                    typeDef.Name,
                    handle,
                    consumedNodes: 1,
                    enforceCharacterBudget,
                    out _).TryComplete(out name, out rejection);
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            name = null;
            rejection = MalformedRejection(ex, handle, consumedNodes: 1);
            return false;
        }

        return ResolveTypeNameFromDefinition(reader, handle, enforceCharacterBudget)
            .TryComplete(out name, out rejection);
    }

    static void ObserveTypeReferenceName(
        MetadataReader reader,
        TypeReferenceHandle handle,
        Action<int>? beforeMaterialize)
    {
        if (beforeMaterialize is null)
            return;

        Span<TypeReferenceHandle> rootToLeaf =
            stackalloc TypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                reader,
                handle,
                rootToLeaf,
                out int consumedNodes,
                out _,
                out _))
            return;

        foreach (TypeReferenceHandle current in rootToLeaf[..consumedNodes])
        {
            TypeReference type = reader.GetTypeReference(current);
            beforeMaterialize(reader.GetBlobReader(type.Namespace).Length);
            beforeMaterialize(reader.GetBlobReader(type.Name).Length);
        }
    }

    static void ObserveTypeDefinitionName(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        Action<int>? beforeMaterialize)
    {
        if (beforeMaterialize is null)
            return;

        Span<TypeDefinitionHandle> rootToLeaf =
            stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                reader,
                handle,
                rootToLeaf,
                out int consumedNodes,
                out _,
                out _))
            return;

        foreach (TypeDefinitionHandle current in rootToLeaf[..consumedNodes])
        {
            TypeDefinition type = reader.GetTypeDefinition(current);
            beforeMaterialize(reader.GetBlobReader(type.Namespace).Length);
            beforeMaterialize(reader.GetBlobReader(type.Name).Length);
        }
    }

    /// <summary>
    /// Resolves a TypeDefinition name through a bounded, cycle-aware
    /// declaring-type walk.
    /// </summary>
    public static RelationshipTraversalResult<string> ResolveTypeNameFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle)
        => ResolveTypeNameFromDefinition(
            reader,
            handle,
            enforceCharacterBudget: true);

    public static RelationshipTraversalResult<string> ResolveTypeNameFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        bool enforceCharacterBudget)
        => ResolveTypeNameFromDefinition(
            reader,
            handle,
            enforceCharacterBudget,
            out _);

    static RelationshipTraversalResult<string> ResolveTypeNameFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        bool enforceCharacterBudget,
        out MetadataTypeNameBudget budget)
    {
        try
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if (typeDef.GetDeclaringType().IsNil)
            {
                return CompleteLeafName(
                    reader,
                    typeDef.Namespace,
                    typeDef.Name,
                    handle,
                    consumedNodes: 1,
                    enforceCharacterBudget,
                    out budget);
            }
        }
        catch (BadImageFormatException ex)
        {
            budget = default;
            return Malformed<string>(ex, handle, consumedNodes: 1);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            budget = default;
            return Malformed<string>(ex, handle, consumedNodes: 1);
        }

        return FormatChain(
            reader,
            MetadataRelationshipTraversal.WalkTypeDefinitionDeclaringChain(reader, handle),
            current =>
            {
                var typeDef = reader.GetTypeDefinition(current);
                return (typeDef.Namespace, typeDef.Name);
            },
            static current => current,
            enforceCharacterBudget,
            out budget);
    }

    internal static MetadataTypeNameParts GetTypeNamePartsFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle)
        => ResolveTypeNamePartsFromDefinition(reader, handle).GetValueOrThrow();

    internal static RelationshipTraversalResult<MetadataTypeNameParts>
        ResolveTypeNamePartsFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            bool enforceCharacterBudget = true)
        => FormatNameParts(
            reader,
            MetadataRelationshipTraversal.WalkTypeDefinitionDeclaringChain(reader, handle),
            current =>
            {
                var typeDef = reader.GetTypeDefinition(current);
                return (typeDef.Namespace, typeDef.Name);
            },
            static current => current,
            enforceCharacterBudget);

    internal static MetadataTypeNameParts GetTypeNameParts(
        MetadataReader reader,
        TypeDefinition type)
    {
        TypeDefinitionHandle declaringType;
        try
        {
            declaringType = type.GetDeclaringType();
            if (declaringType.IsNil)
            {
                return new MetadataTypeNameParts(
                    MetadataSafetyPolicy.ReadStructuralString(
                        reader,
                        type.Namespace),
                    [
                        MetadataSafetyPolicy.ReadStructuralString(
                            reader,
                            type.Name),
                    ]);
            }
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            throw new BadImageFormatException(
                "The type has an invalid declaring-type relationship.",
                ex);
        }

        MetadataTypeNameParts declaring =
            GetTypeNamePartsFromDefinition(reader, declaringType);
        if (declaring.Segments.Count
            >= MetadataSafetyPolicy.MaxRelationshipNodes)
        {
            throw new BadImageFormatException(
                $"The metadata relationship exceeds "
                + $"{MetadataSafetyPolicy.MaxRelationshipNodes} nodes.");
        }

        string leaf = MetadataSafetyPolicy.ReadStructuralString(
            reader,
            type.Name);
        long totalLength = declaring.Namespace.Length + leaf.Length;
        foreach (string segment in declaring.Segments)
            totalLength += segment.Length;
        if (totalLength
            > MetadataSafetyPolicy.MaxStructuralSignatureChars)
        {
            throw new BadImageFormatException(
                "The metadata type name exceeds the structural-name budget.");
        }

        return new MetadataTypeNameParts(
            declaring.Namespace,
            [.. declaring.Segments, leaf]);
    }

    /// <summary>
    /// Resolves an ExportedType name through a bounded, cycle-aware
    /// implementation walk.
    /// </summary>
    public static RelationshipTraversalResult<string> ResolveTypeNameFromExportedType(
        MetadataReader reader,
        ExportedTypeHandle handle)
        => ResolveTypeNameFromExportedType(
            reader,
            handle,
            enforceCharacterBudget: true);

    public static RelationshipTraversalResult<string> ResolveTypeNameFromExportedType(
        MetadataReader reader,
        ExportedTypeHandle handle,
        bool enforceCharacterBudget)
        => ResolveTypeNameFromExportedType(
            reader,
            handle,
            enforceCharacterBudget,
            out _);

    static RelationshipTraversalResult<string> ResolveTypeNameFromExportedType(
        MetadataReader reader,
        ExportedTypeHandle handle,
        bool enforceCharacterBudget,
        out MetadataTypeNameBudget budget)
    {
        try
        {
            var exportedType = reader.GetExportedType(handle);
            if (exportedType.Implementation.Kind != HandleKind.ExportedType)
            {
                return CompleteLeafName(
                    reader,
                    exportedType.Namespace,
                    exportedType.Name,
                    handle,
                    consumedNodes: 1,
                    enforceCharacterBudget,
                    out budget);
            }
        }
        catch (BadImageFormatException ex)
        {
            budget = default;
            return Malformed<string>(ex, handle, consumedNodes: 1);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            budget = default;
            return Malformed<string>(ex, handle, consumedNodes: 1);
        }

        return FormatChain(
            reader,
            MetadataRelationshipTraversal.WalkExportedTypeImplementationChain(reader, handle),
            current =>
            {
                var exportedType = reader.GetExportedType(current);
                return (exportedType.Namespace, exportedType.Name);
            },
            static current => current,
            enforceCharacterBudget,
            out budget);
    }

    /// <summary>
    /// Gets an ExportedType name or throws at a caller-owned failure boundary.
    /// </summary>
    public static string GetTypeNameFromExportedType(
        MetadataReader reader,
        ExportedTypeHandle handle)
    {
        try
        {
            var exportedType = reader.GetExportedType(handle);
            if (exportedType.Implementation.Kind != HandleKind.ExportedType)
            {
                return CompleteLeafName(
                    reader,
                    exportedType.Namespace,
                    exportedType.Name,
                    handle,
                    consumedNodes: 1,
                    enforceCharacterBudget: false,
                    out _).GetValueOrThrow();
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return ThrowMalformed(ex, handle);
        }

        return ResolveTypeNameFromExportedType(
            reader,
            handle,
            enforceCharacterBudget: false).GetValueOrThrow();
    }

    /// <summary>
    /// Gets the type name from a TypeSpecification handle (generic instantiations).
    /// </summary>
    public static string GetTypeNameFromSpecification(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        GenericContext? context = null)
        => DecodeTypeNameFromSpecification(
            reader,
            handle,
            context,
            beforeMaterialize: null,
            enforceCharacterBudget: false).GetValueOrThrow();

    /// <summary>
    /// Gets the guarded decode outcome for a TypeSpecification handle (generic instantiations).
    /// </summary>
    public static SignatureDecodeResult<string> DecodeTypeNameFromSpecification(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        GenericContext? context = null,
        Action<int>? beforeMaterialize = null)
        => DecodeTypeNameFromSpecification(
            reader,
            handle,
            context,
            beforeMaterialize,
            enforceCharacterBudget: true);

    public static SignatureDecodeResult<string> DecodeTypeNameFromSpecification(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        GenericContext? context,
        Action<int>? beforeMaterialize,
        bool enforceCharacterBudget)
        => GuardedSignatureDecoder.DecodeTypeSpecification(
            reader,
            handle,
            context,
            beforeMaterialize,
            enforceCharacterBudget);

    /// <summary>
    /// Gets the full name of a type definition (Namespace.Name), qualifying a
    /// nested type through its declaring type (Outer.Inner).
    /// </summary>
    public static string GetFullName(MetadataReader reader, TypeDefinition typeDef)
    {
        TypeDefinitionHandle declaringType;
        try
        {
            declaringType = typeDef.GetDeclaringType();
            if (declaringType.IsNil)
            {
                return CompleteLeafName(
                    reader,
                    typeDef.Namespace,
                    typeDef.Name,
                    default,
                    consumedNodes: 1,
                    enforceCharacterBudget: false,
                    out _).GetValueOrThrow();
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return ThrowMalformed(ex, default, consumedNodes: 0);
        }

        var declaringName = ResolveTypeNameFromDefinition(
            reader,
            declaringType,
            enforceCharacterBudget: false,
            out var declaringBudget);
        return AppendLeaf(
            reader,
            declaringName,
            typeDef.Name,
            declaringType,
            declaringBudget,
            enforceCharacterBudget: false).GetValueOrThrow();
    }

    /// <summary>
    /// Resolves the full name of a TypeDefinition value whose handle is not
    /// available to the caller.
    /// </summary>
    public static RelationshipTraversalResult<string> ResolveFullName(
        MetadataReader reader,
        TypeDefinition typeDef)
    {
        try
        {
            var declaringType = typeDef.GetDeclaringType();
            if (declaringType.IsNil)
            {
                return CompleteLeafName(
                    reader,
                    typeDef.Namespace,
                    typeDef.Name,
                    default,
                    consumedNodes: 1,
                    enforceCharacterBudget: true,
                    out _);
            }

            var declaringName = ResolveTypeNameFromDefinition(
                reader,
                declaringType,
                enforceCharacterBudget: true,
                out var declaringBudget);
            return AppendLeaf(
                reader,
                declaringName,
                typeDef.Name,
                declaringType,
                declaringBudget);
        }
        catch (BadImageFormatException ex)
        {
            return Malformed<string>(ex, default, consumedNodes: 0);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Malformed<string>(ex, default, consumedNodes: 0);
        }
    }

    /// <summary>
    /// Gets the full name of a type (Namespace.Name) from an ApiType-like structure.
    /// </summary>
    public static string GetFullName(string? ns, string name)
    {
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    /// <summary>
    /// Gets the full name of a type reference, qualifying a nested type through
    /// its resolution-scope chain.
    /// </summary>
    public static string GetFullName(MetadataReader reader, TypeReference typeRef)
    {
        EntityHandle resolutionScope;
        try
        {
            resolutionScope = typeRef.ResolutionScope;
            if (resolutionScope.Kind != HandleKind.TypeReference)
            {
                return CompleteLeafName(
                    reader,
                    typeRef.Namespace,
                    typeRef.Name,
                    default,
                    consumedNodes: 1,
                    enforceCharacterBudget: false,
                    out _).GetValueOrThrow();
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return ThrowMalformed(ex, default, consumedNodes: 0);
        }

        var declaringName = ResolveTypeNameFromReference(
            reader,
            (TypeReferenceHandle)resolutionScope,
            enforceCharacterBudget: false,
            out var declaringBudget);
        return AppendLeaf(
            reader,
            declaringName,
            typeRef.Name,
            resolutionScope,
            declaringBudget,
            enforceCharacterBudget: false).GetValueOrThrow();
    }

    /// <summary>
    /// Resolves the full name of a TypeReference value whose handle is not
    /// available to the caller.
    /// </summary>
    public static RelationshipTraversalResult<string> ResolveFullName(
        MetadataReader reader,
        TypeReference typeRef)
    {
        try
        {
            var resolutionScope = typeRef.ResolutionScope;
            if (resolutionScope.Kind != HandleKind.TypeReference)
            {
                return CompleteLeafName(
                    reader,
                    typeRef.Namespace,
                    typeRef.Name,
                    resolutionScope,
                    consumedNodes: 1,
                    enforceCharacterBudget: true,
                    out _);
            }

            var declaringName = ResolveTypeNameFromReference(
                reader,
                (TypeReferenceHandle)resolutionScope,
                enforceCharacterBudget: true,
                out var declaringBudget);
            return AppendLeaf(
                reader,
                declaringName,
                typeRef.Name,
                resolutionScope,
                declaringBudget);
        }
        catch (BadImageFormatException ex)
        {
            return Malformed<string>(ex, default, consumedNodes: 0);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Malformed<string>(ex, default, consumedNodes: 0);
        }
    }

    /// <summary>
    /// Resolves the full name of an ExportedType value whose handle is not
    /// available to the caller.
    /// </summary>
    public static RelationshipTraversalResult<string> ResolveFullName(
        MetadataReader reader,
        ExportedType exportedType)
    {
        try
        {
            var implementation = exportedType.Implementation;
            if (implementation.Kind != HandleKind.ExportedType)
            {
                return CompleteLeafName(
                    reader,
                    exportedType.Namespace,
                    exportedType.Name,
                    implementation,
                    consumedNodes: 1,
                    enforceCharacterBudget: true,
                    out _);
            }

            var declaringName = ResolveTypeNameFromExportedType(
                reader,
                (ExportedTypeHandle)implementation,
                enforceCharacterBudget: true,
                out var declaringBudget);
            return AppendLeaf(
                reader,
                declaringName,
                exportedType.Name,
                implementation,
                declaringBudget);
        }
        catch (BadImageFormatException ex)
        {
            return Malformed<string>(ex, default, consumedNodes: 0);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Malformed<string>(ex, default, consumedNodes: 0);
        }
    }

    /// <summary>
    /// Gets the full name of an exported type, qualifying a nested type through
    /// its implementation chain.
    /// </summary>
    public static string GetFullName(MetadataReader reader, ExportedType exportedType)
    {
        EntityHandle implementation;
        try
        {
            implementation = exportedType.Implementation;
            if (implementation.Kind != HandleKind.ExportedType)
            {
                return CompleteLeafName(
                    reader,
                    exportedType.Namespace,
                    exportedType.Name,
                    default,
                    consumedNodes: 1,
                    enforceCharacterBudget: false,
                    out _).GetValueOrThrow();
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return ThrowMalformed(ex, default, consumedNodes: 0);
        }

        var declaringName = ResolveTypeNameFromExportedType(
            reader,
            (ExportedTypeHandle)implementation,
            enforceCharacterBudget: false,
            out var declaringBudget);
        return AppendLeaf(
            reader,
            declaringName,
            exportedType.Name,
            implementation,
            declaringBudget,
            enforceCharacterBudget: false).GetValueOrThrow();
    }

    /// <summary>
    /// Renders a generic instantiation from legacy flat text. Only an
    /// unambiguous terminal <c>`N</c> is rewritten. Its declared arity must equal
    /// the supplied argument count except for compiler-generated names, where a
    /// partial argument list is completed with explicit placeholders so the
    /// declared arity remains visible. A possible namespace or nesting boundary
    /// preserves the raw spelling; callers with exact segments use the structured
    /// overload. When the name carries no arity marker the arguments are appended
    /// once, matching the legacy <c>Name&lt;args&gt;</c> form.
    /// </summary>
    public static string ApplyGenericArguments(string genericTypeName, IReadOnlyList<string> typeArguments)
    {
        ArgumentNullException.ThrowIfNull(genericTypeName);
        ArgumentNullException.ThrowIfNull(typeArguments);
        if (!genericTypeName.Contains('`'))
        {
            return typeArguments.Count == 0
                ? genericTypeName
                : $"{genericTypeName}<{string.Join(", ", typeArguments)}>";
        }

        if (!TryReadUnambiguousFlattenedArity(genericTypeName, out int arity))
        {
            return genericTypeName;
        }

        if (arity != typeArguments.Count)
        {
            if (typeArguments.Count == 0
                || typeArguments.Count > arity
                || arity > MaxDisplayedPlaceholders
                || !LooksCompilerGenerated(genericTypeName))
            {
                return genericTypeName;
            }

            return RewriteAritySegments(
                genericTypeName,
                (int declaredArity, StringBuilder builder) =>
                {
                    builder.Append('<');
                    for (int k = 0; k < declaredArity; k++)
                    {
                        if (k > 0)
                            builder.Append(", ");
                        builder.Append(k < typeArguments.Count
                            ? typeArguments[k]
                            : $"T{k + 1}");
                    }
                    builder.Append('>');
                    return true;
                },
                dotIsBoundary: false,
                plusIsBoundary: false);
        }

        return RewriteAritySegments(
            genericTypeName,
            (int _, StringBuilder builder) =>
            {
                builder.Append('<');
                for (int k = 0; k < typeArguments.Count; k++)
                {
                    if (k > 0)
                        builder.Append(", ");
                    builder.Append(typeArguments[k]);
                }
                builder.Append('>');
                return true;
            },
            dotIsBoundary: false,
            plusIsBoundary: false);
    }

    static bool LooksCompilerGenerated(
        string metadataName,
        bool hasTypeDecoration = true)
    {
        int boundary = metadataName.LastIndexOfAny(['.', '+']);
        ReadOnlySpan<char> leaf = metadataName.AsSpan(boundary + 1);
        if (hasTypeDecoration)
            leaf = leaf[..TypeDecorationStart(leaf)];
        return leaf.Length > 1
            && leaf[0] == '<'
            && leaf.IndexOf('>') > 0;
    }

    /// <summary>
    /// Renders a generic instantiation from exact root-to-leaf metadata-name
    /// segments. Arguments are consumed by each segment's declared arity, and a
    /// count mismatch preserves the raw segmented spelling except for a
    /// compiler-generated terminal segment whose partial argument list is
    /// completed with bounded placeholders.
    /// </summary>
    public static string ApplyGenericArguments(
        IReadOnlyList<string> metadataNameSegments,
        IReadOnlyList<string> typeArguments)
    {
        ArgumentNullException.ThrowIfNull(metadataNameSegments);
        ArgumentNullException.ThrowIfNull(typeArguments);

        long declaredArity = 0;
        foreach (string segment in metadataNameSegments)
        {
            ArgumentNullException.ThrowIfNull(segment);
            declaredArity += MetadataNameArity.OfSegment(segment);
        }

        string rawName = string.Join('.', metadataNameSegments);
        if (declaredArity == 0
            && typeArguments.Count > 0)
        {
            return $"{rawName}<{string.Join(", ", typeArguments)}>";
        }
        bool completeCompilerGeneratedName =
            typeArguments.Count > 0
            && typeArguments.Count < declaredArity
            && declaredArity <= MaxDisplayedPlaceholders
            && LooksCompilerGenerated(
                metadataNameSegments[^1],
                hasTypeDecoration: false);
        if (declaredArity != typeArguments.Count
            && !completeCompilerGeneratedName)
        {
            return rawName;
        }

        int argIndex = 0;
        var result = new StringBuilder(rawName.Length + 16);
        for (int segmentIndex = 0; segmentIndex < metadataNameSegments.Count; segmentIndex++)
        {
            if (segmentIndex > 0)
                result.Append('.');

            result.Append(RewriteAritySegments(
                metadataNameSegments[segmentIndex],
                (int arity, StringBuilder builder) =>
                {
                    builder.Append('<');
                    for (int k = 0; k < arity; k++)
                    {
                        if (k > 0)
                            builder.Append(", ");
                        builder.Append(argIndex < typeArguments.Count
                            ? typeArguments[argIndex]
                            : $"T{argIndex + 1}");
                        argIndex++;
                    }
                    builder.Append('>');
                    return true;
                },
                dotIsBoundary: false,
                plusIsBoundary: false,
                hasTypeDecoration: false));
        }

        return result.ToString();
    }

    /// <summary>
    /// Formats legacy flat metadata text by replacing one unambiguous terminal
    /// CLR generic arity suffix with readable type parameter placeholders.
    /// </summary>
    public static string FormatDisplayName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName) || !typeName.Contains('`'))
            return typeName;

        if (!TryReadUnambiguousFlattenedArity(typeName, out _))
            return typeName;

        return RewriteAritySegments(
            typeName,
            static (int arity, StringBuilder builder) =>
            {
                if (arity > MaxDisplayedPlaceholders)
                    return false;

                builder.Append('<');
                for (int parameterIndex = 1; parameterIndex <= arity; parameterIndex++)
                {
                    if (parameterIndex > 1)
                        builder.Append(", ");
                    builder.Append(arity == 1 ? "T" : $"T{parameterIndex}");
                }
                builder.Append('>');
                return true;
            },
            dotIsBoundary: false,
            plusIsBoundary: false);
    }

    /// <summary>
    /// Formats exact root-to-leaf metadata-name segments with stable generic
    /// placeholders. Literal dots and pluses remain inside their owning segment.
    /// </summary>
    public static string FormatDisplayName(IReadOnlyList<string> metadataNameSegments)
    {
        ArgumentNullException.ThrowIfNull(metadataNameSegments);
        var result = new StringBuilder();
        for (int segmentIndex = 0; segmentIndex < metadataNameSegments.Count; segmentIndex++)
        {
            string segment = metadataNameSegments[segmentIndex]
                ?? throw new ArgumentException(
                    "Metadata-name segments cannot contain null entries.",
                    nameof(metadataNameSegments));
            if (segmentIndex > 0)
                result.Append('.');
            result.Append(RewriteAritySegments(
                segment,
                static (int arity, StringBuilder builder) =>
                {
                    if (arity > MaxDisplayedPlaceholders)
                        return false;

                    builder.Append('<');
                    for (int parameterIndex = 1; parameterIndex <= arity; parameterIndex++)
                    {
                        if (parameterIndex > 1)
                            builder.Append(", ");
                        builder.Append(arity == 1 ? "T" : $"T{parameterIndex}");
                    }
                    builder.Append('>');
                    return true;
                },
                dotIsBoundary: false,
                plusIsBoundary: false,
                hasTypeDecoration: false));
        }

        return result.ToString();
    }

    /// <summary>
    /// The most type-parameter placeholders <see cref="FormatDisplayName"/> will
    /// synthesize for one arity marker. A canonical arity reaches
    /// <see cref="MetadataNameArity.MaxArity"/>, and expanding that many
    /// placeholders would turn a short hostile name into a megabyte of display
    /// text, so a larger arity keeps its raw <c>`N</c> spelling: the name stays
    /// visible and bounded instead of being rendered or silently dropped.
    /// </summary>
    public const int MaxDisplayedPlaceholders = 64;

    /// <summary>
    /// Rewrites canonical generic-arity suffixes using the caller-supplied
    /// boundary contract. An arbitrary digit run (<c>Bomb`2147483647</c>), a
    /// non-ASCII digit, a signed or padded count, or digits followed by more text
    /// is text, not arity. <paramref name="render"/> returns false to decline a
    /// marker, which restores its raw spelling.
    /// </summary>
    static string RewriteAritySegments(
        string name,
        Func<int, StringBuilder, bool> render,
        bool dotIsBoundary = true,
        bool plusIsBoundary = true,
        bool hasTypeDecoration = true)
    {
        var result = new StringBuilder(name.Length + 16);
        foreach (MetadataNameComponent component in MetadataNameArity.EnumerateComponents(
            name,
            dotIsBoundary,
            plusIsBoundary))
        {
            ReadOnlySpan<char> text = name.AsSpan(component.Start, component.Length);
            ReadOnlySpan<char> decoration = hasTypeDecoration
                ? text[TypeDecorationStart(text)..]
                : [];
            ReadOnlySpan<char> metadataName = text[..^decoration.Length];

            if (MetadataNameArity.TryReadSuffix(metadataName, out int arity, out int simpleNameLength))
            {
                result.Append(metadataName[..simpleNameLength]);
                int beforeRender = result.Length;
                if (render(arity, result))
                {
                    result.Append(decoration);
                }
                else
                {
                    result.Length = beforeRender;
                    result.Append(text[simpleNameLength..]);
                }
            }
            else
            {
                result.Append(text);
            }

            if (component.Delimiter is { } delimiter)
                result.Append(delimiter);
        }

        return result.ToString();
    }

    static bool TryReadUnambiguousFlattenedArity(string name, out int arity)
    {
        arity = 0;
        foreach (MetadataNameComponent component in MetadataNameArity.EnumerateComponents(name))
        {
            ReadOnlySpan<char> text = name.AsSpan(component.Start, component.Length);
            ReadOnlySpan<char> metadataName = text[..TypeDecorationStart(text)];
            if (!MetadataNameArity.TryReadSuffix(metadataName, out int componentArity, out _))
                continue;

            if (component.Delimiter is not null || arity != 0)
            {
                arity = 0;
                return false;
            }

            arity = componentArity;
        }

        return arity != 0;
    }

    /// <summary>
    /// Where a component's metadata name ends and signature decoration —
    /// array, pointer, by-ref, or nullable syntax — begins. Decoration is
    /// display syntax rather than name text, so it is set aside before the arity
    /// grammar is applied and restored after the arguments
    /// (<c>List`1[]</c> renders <c>List&lt;T&gt;[]</c>).
    /// </summary>
    /// <remarks>
    /// Decoration is a suffix, so it is measured from the end. Scanning from the
    /// start would cut a compiler-generated name at its leading
    /// <c>&lt;&gt;</c> — <c>&lt;&gt;c__DisplayClass0_0`1</c> is a name, not a
    /// decorated one — and leave its arity marker unexpanded in emitted C#.
    /// </remarks>
    static int TypeDecorationStart(ReadOnlySpan<char> component)
    {
        int start = component.Length;
        while (start > 0 && component[start - 1] is '[' or ']' or '*' or '&' or '?' or ',')
            start--;

        return start;
    }

    static RelationshipTraversalResult<string> FormatChain<THandle>(
        MetadataReader reader,
        RelationshipTraversalResult<RelationshipChain<THandle>> traversal,
        Func<THandle, (StringHandle Namespace, StringHandle Name)> getName,
        Func<THandle, EntityHandle> getSubject,
        bool enforceCharacterBudget,
        out MetadataTypeNameBudget budget)
        where THandle : struct
    {
        budget = default;
        if (traversal is RelationshipTraversalResult<RelationshipChain<THandle>>.Rejected rejected)
            return Reject<string>(rejected.Rejection);

        var chain = ((RelationshipTraversalResult<RelationshipChain<THandle>>.Completed)traversal).Value;
        var accumulated = new MetadataTypeNameBudget();
        var builder = new StringBuilder();
        for (int i = 0; i < chain.Handles.Length; i++)
        {
            var handle = chain.Handles[i];
            try
            {
                var (namespaceHandle, nameHandle) = getName(handle);
                if (i == 0
                    && !TryAppendNamePart(
                        reader,
                        ref accumulated,
                        namespaceHandle,
                        chargeDelimiterChars: 0,
                        renderDelimiterChars: 0,
                        builder,
                        getSubject(handle),
                        i + 1,
                        enforceCharacterBudget,
                        out var namespaceRejection))
                {
                    return namespaceRejection;
                }

                if (!TryAppendNamePart(
                        reader,
                        ref accumulated,
                        nameHandle,
                        chargeDelimiterChars: 1,
                        renderDelimiterChars: i > 0 || builder.Length > 0 ? 1 : 0,
                        builder,
                        getSubject(handle),
                        i + 1,
                        enforceCharacterBudget,
                        out var nameRejection))
                {
                    return nameRejection;
                }
            }
            catch (BadImageFormatException ex)
            {
                return Malformed<string>(ex, getSubject(handle), consumedNodes: i + 1);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Malformed<string>(ex, getSubject(handle), consumedNodes: i + 1);
            }
        }

        budget = accumulated;
        return new RelationshipTraversalResult<string>.Completed(
            builder.ToString(),
            chain.Handles.Length);
    }

    static RelationshipTraversalResult<MetadataTypeNameParts> FormatNameParts<THandle>(
        MetadataReader reader,
        RelationshipTraversalResult<RelationshipChain<THandle>> traversal,
        Func<THandle, (StringHandle Namespace, StringHandle Name)> getName,
        Func<THandle, EntityHandle> getSubject,
        bool enforceCharacterBudget)
        where THandle : struct
    {
        if (traversal is RelationshipTraversalResult<RelationshipChain<THandle>>.Rejected rejected)
            return Reject<MetadataTypeNameParts>(rejected.Rejection);

        var chain = ((RelationshipTraversalResult<RelationshipChain<THandle>>.Completed)traversal).Value;
        string rootNamespace = "";
        var segments = new string[chain.Handles.Length];
        int remainingCharacters = MetadataSafetyPolicy.MaxTypeNameCharacters;
        for (int i = 0; i < chain.Handles.Length; i++)
        {
            var handle = chain.Handles[i];
            try
            {
                var (namespaceHandle, nameHandle) = getName(handle);
                if (!enforceCharacterBudget)
                {
                    if (i == 0)
                        rootNamespace = reader.GetString(namespaceHandle);
                    segments[i] = reader.GetString(nameHandle);
                    continue;
                }
                if (i == 0)
                {
                    if (!MetadataSafetyPolicy.TryReadTypeNameComponent(
                            reader,
                            namespaceHandle,
                            ref remainingCharacters,
                            out rootNamespace))
                    {
                        return NameBudget<MetadataTypeNameParts>(
                            getSubject(handle),
                            consumedNodes: i + 1);
                    }
                }

                if ((i > 0 || rootNamespace.Length > 0)
                    && remainingCharacters == 0)
                {
                    return NameBudget<MetadataTypeNameParts>(
                        getSubject(handle),
                        consumedNodes: i + 1);
                }
                if (i > 0 || rootNamespace.Length > 0)
                    remainingCharacters--;
                if (!MetadataSafetyPolicy.TryReadTypeNameComponent(
                        reader,
                        nameHandle,
                        ref remainingCharacters,
                        out segments[i]))
                {
                    return NameBudget<MetadataTypeNameParts>(
                        getSubject(handle),
                        consumedNodes: i + 1);
                }
            }
            catch (BadImageFormatException ex)
            {
                return Malformed<MetadataTypeNameParts>(
                    ex,
                    getSubject(handle),
                    consumedNodes: i + 1);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Malformed<MetadataTypeNameParts>(
                    ex,
                    getSubject(handle),
                    consumedNodes: i + 1);
            }
        }

        return new RelationshipTraversalResult<MetadataTypeNameParts>.Completed(
            new MetadataTypeNameParts(rootNamespace, segments),
            chain.Handles.Length);
    }

    static RelationshipTraversalResult<string> AppendLeaf(
        MetadataReader reader,
        RelationshipTraversalResult<string> declaringName,
        StringHandle leafName,
        EntityHandle subject,
        in MetadataTypeNameBudget declaringBudget,
        bool enforceCharacterBudget = true)
    {
        if (declaringName is RelationshipTraversalResult<string>.Rejected rejected)
            return Reject<string>(rejected.Rejection);

        var completed = (RelationshipTraversalResult<string>.Completed)declaringName;
        if (completed.ConsumedNodes >= MetadataSafetyPolicy.MaxRelationshipNodes)
        {
            return new RelationshipTraversalResult<string>.Rejected(
                new RelationshipTraversalRejection(
                    RelationshipTraversalRejectionKind.NodeBudget,
                    $"The metadata relationship exceeds "
                    + $"{MetadataSafetyPolicy.MaxRelationshipNodes} nodes.",
                    subject,
                    completed.ConsumedNodes));
        }

        try
        {
            var budget = new MetadataTypeNameBudget();
            budget.CopyFrom(declaringBudget);
            var builder = new StringBuilder(completed.Value);
            if (!TryAppendNamePart(
                    reader,
                    ref budget,
                    leafName,
                    chargeDelimiterChars: 1,
                    renderDelimiterChars: 1,
                    builder,
                    subject,
                    completed.ConsumedNodes + 1,
                    enforceCharacterBudget,
                    out var rejection))
            {
                return rejection;
            }

            return new RelationshipTraversalResult<string>.Completed(
                builder.ToString(),
                completed.ConsumedNodes + 1);
        }
        catch (BadImageFormatException ex)
        {
            return Malformed<string>(ex, subject, completed.ConsumedNodes + 1);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Malformed<string>(ex, subject, completed.ConsumedNodes + 1);
        }
    }

    static RelationshipTraversalResult<string> CompleteLeafName(
        MetadataReader reader,
        StringHandle namespaceHandle,
        StringHandle nameHandle,
        EntityHandle subject,
        int consumedNodes,
        bool enforceCharacterBudget,
        out MetadataTypeNameBudget budget)
    {
        budget = default;
        try
        {
            var accumulated = new MetadataTypeNameBudget();
            var builder = new StringBuilder();
            if (!TryAppendNamePart(
                    reader,
                    ref accumulated,
                    namespaceHandle,
                    chargeDelimiterChars: 0,
                    renderDelimiterChars: 0,
                    builder,
                    subject,
                    consumedNodes,
                    enforceCharacterBudget,
                    out var namespaceRejection))
            {
                return namespaceRejection;
            }

            if (!TryAppendNamePart(
                    reader,
                    ref accumulated,
                    nameHandle,
                    chargeDelimiterChars: 1,
                    renderDelimiterChars: builder.Length > 0 ? 1 : 0,
                    builder,
                    subject,
                    consumedNodes,
                    enforceCharacterBudget,
                    out var nameRejection))
            {
                return nameRejection;
            }

            budget = accumulated;
            return new RelationshipTraversalResult<string>.Completed(
                builder.ToString(),
                consumedNodes);
        }
        catch (BadImageFormatException ex)
        {
            return Malformed<string>(ex, subject, consumedNodes);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Malformed<string>(ex, subject, consumedNodes);
        }
    }

    static bool TryAppendNamePart(
        MetadataReader reader,
        ref MetadataTypeNameBudget budget,
        StringHandle handle,
        int chargeDelimiterChars,
        int renderDelimiterChars,
        StringBuilder builder,
        EntityHandle subject,
        int consumedNodes,
        bool enforceCharacterBudget,
        out RelationshipTraversalResult<string> rejection)
    {
        if (!budget.TryRead(
                reader,
                handle,
                chargeDelimiterChars,
                beforeMaterialize: null,
                out string value,
                enforceCharacterBudget))
        {
            rejection = NameBudget<string>(subject, consumedNodes);
            return false;
        }

        if (renderDelimiterChars > 0)
            builder.Append('.');
        builder.Append(value);
        rejection = null!;
        return true;
    }

    static RelationshipTraversalResult<T> NameBudget<T>(
        EntityHandle subject,
        int consumedNodes)
        where T : notnull
        => new RelationshipTraversalResult<T>.Rejected(
            new RelationshipTraversalRejection(
                RelationshipTraversalRejectionKind.NameBudget,
                $"The structured type name exceeds "
                + $"{MetadataSafetyPolicy.MaxTypeNameCharacters} characters.",
                subject,
                consumedNodes));

    static RelationshipTraversalResult<T> Malformed<T>(
        Exception exception,
        EntityHandle subject,
        int consumedNodes)
        where T : notnull
        => new RelationshipTraversalResult<T>.Rejected(
            new RelationshipTraversalRejection(
                RelationshipTraversalRejectionKind.MalformedMetadata,
                exception.Message,
                subject,
                consumedNodes));

    static RelationshipTraversalResult<T> Reject<T>(
        RelationshipTraversalRejection rejection)
        where T : notnull
        => new RelationshipTraversalResult<T>.Rejected(rejection);

    static MetadataTypeNameResult FromRelationship(
        RelationshipTraversalResult<string> result)
        => result switch
        {
            RelationshipTraversalResult<string>.Completed completed =>
                new MetadataTypeNameResult.Resolved(completed.Value),
            RelationshipTraversalResult<string>.Rejected rejected =>
                new MetadataTypeNameResult.Rejected(
                    MetadataTypeNameFailure.From(rejected.Rejection)),
            _ => throw new InvalidOperationException(
                "Unknown metadata relationship traversal result."),
        };

    static MetadataTypeNameResult FromSignature(
        SignatureDecodeResult<string> result,
        TypeSpecificationHandle subject)
        => result switch
        {
            SignatureDecodeResult<string>.Decoded decoded =>
                new MetadataTypeNameResult.Resolved(decoded.Value),
            SignatureDecodeResult<string>.Rejected rejected =>
                new MetadataTypeNameResult.Rejected(
                    MetadataTypeNameFailure.From(rejected.Rejection, subject)),
            _ => throw new InvalidOperationException(
                "Unknown metadata signature decode result."),
        };

    static bool TryComplete(
        this RelationshipTraversalResult<string> result,
        [NotNullWhen(true)] out string? name,
        out RelationshipTraversalRejection? rejection)
    {
        if (result is RelationshipTraversalResult<string>.Completed completed)
        {
            name = completed.Value;
            rejection = null;
            return true;
        }

        name = null;
        rejection = ((RelationshipTraversalResult<string>.Rejected)result).Rejection;
        return false;
    }

    static BadImageFormatException RejectedName(RelationshipTraversalRejection rejection)
        => new(
            $"Metadata relationship traversal rejected ({rejection.Kind}): "
            + rejection.Detail);

    static RelationshipTraversalRejection MalformedRejection(
        Exception exception,
        EntityHandle subject,
        int consumedNodes)
        => new(
            RelationshipTraversalRejectionKind.MalformedMetadata,
            exception.Message,
            subject,
            consumedNodes);

    static string ThrowMalformed(
        Exception exception,
        EntityHandle subject,
        int consumedNodes = 1)
        => Malformed<string>(exception, subject, consumedNodes).GetValueOrThrow();
}

internal sealed class MetadataTypeNameParts(string @namespace, string[] segments)
{
    public string Namespace { get; } = @namespace;
    public IReadOnlyList<string> Segments { get; } = segments;

    public string ToDottedName()
    {
        string typeName = string.Join('.', Segments);
        return Namespace.Length == 0
            ? typeName
            : $"{Namespace}.{typeName}";
    }
}
